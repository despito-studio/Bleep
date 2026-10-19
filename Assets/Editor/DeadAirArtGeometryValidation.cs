using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirArtGeometryValidation
    {
        [Serializable] public struct Summary { public int uniqueMeshes, rendererBounds; public long vertices; }

        public static Summary RequireFiniteAssembly(Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var issues = new List<string>();
            var meshes = new HashSet<Mesh>();
            var summary = new Summary();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!Finite(renderer.bounds)) issues.Add("Nonfinite renderer bounds: " + Hierarchy(renderer.transform));
                summary.rendererBounds++;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                // TextMesh has a renderer without an authored MeshFilter.
                if (mesh == null || !meshes.Add(mesh)) continue;
                summary.uniqueMeshes++; summary.vertices += mesh.vertexCount;
                InspectMesh(mesh, issues);
            }
            if (issues.Count > 0) throw new InvalidOperationException("NONFINITE GEOMETRY: " + root.name + ": " +
                issues.Count + " issue(s). " + string.Join(" | ", issues.Take(20)));
            return summary;
        }

        public static void InspectMesh(Mesh mesh, List<string> issues)
        {
            string identity = mesh.name + " [" + AssetDatabase.GetAssetPath(mesh) + "]";
            if (!Finite(mesh.bounds)) issues.Add("Nonfinite mesh bounds: " + identity);
            if (mesh.vertexCount == 0) { issues.Add("Empty render mesh: " + identity); return; }
            try
            {
                using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = snapshot[0];
                    using (var vectors = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(vectors);
                        for (int i = 0; i < vectors.Length; i++)
                            if (!Finite(vectors[i])) { issues.Add(identity + " nonfinite vertex[" + i + "]"); break; }
                        if (data.HasVertexAttribute(VertexAttribute.Normal))
                        {
                            data.GetNormals(vectors);
                            for (int i = 0; i < vectors.Length; i++)
                                if (!Finite(vectors[i])) { issues.Add(identity + " nonfinite normal[" + i + "]"); break; }
                        }
                    }
                    if (data.HasVertexAttribute(VertexAttribute.Tangent))
                        using (var tangents = new NativeArray<Vector4>(data.vertexCount, Allocator.Temp))
                        {
                            data.GetTangents(tangents);
                            for (int i = 0; i < tangents.Length; i++)
                                if (!Finite(tangents[i])) { issues.Add(identity + " nonfinite tangent[" + i + "]"); break; }
                        }
                    if (data.HasVertexAttribute(VertexAttribute.TexCoord0))
                        using (var uv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp))
                        {
                            data.GetUVs(0, uv);
                            for (int i = 0; i < uv.Length; i++)
                                if (!Finite(uv[i].x) || !Finite(uv[i].y)) { issues.Add(identity + " nonfinite UV0[" + i + "]"); break; }
                        }
                }
            }
            catch (Exception exception) { issues.Add("Cannot inspect actual mesh buffers: " + identity + ": " + exception.Message); }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Vector4 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        static bool Finite(Bounds bounds) => Finite(bounds.center) && Finite(bounds.extents) &&
            bounds.extents.x >= 0 && bounds.extents.y >= 0 && bounds.extents.z >= 0 && Finite(bounds.size.sqrMagnitude);
        static string Hierarchy(Transform part)
        {
            string name = part.name;
            for (var parent = part.parent; parent != null; parent = parent.parent) name = parent.name + "/" + name;
            return name;
        }

        [MenuItem("Dead Air/Production Art/Validate Geometry Gate Fixtures")]
        public static void ValidateGate()
        {
            var mesh = new Mesh { name = "Geometry gate / stale finite bounds fixture" };
            var finite = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var normal = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            try
            {
                mesh.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1));
                mesh.SetVertexBufferData(finite, 0, 0, 3, 0, flags); mesh.SetVertexBufferData(normal, 0, 0, 3, 1, flags);
                mesh.bounds = new Bounds(Vector3.one * .5f, Vector3.one);
                var issues = new List<string>(); InspectMesh(mesh, issues);
                if (issues.Count != 0) throw new InvalidOperationException("A finite two-stream mesh must pass: " + string.Join("; ", issues));
                finite[1] = new Vector3(float.NaN, 0, 0); mesh.SetVertexBufferData(finite, 0, 0, 3, 0, flags);
                InspectMesh(mesh, issues);
                if (!issues.Any(issue => issue.Contains("nonfinite vertex[1]"))) throw new InvalidOperationException("The gate missed a NaN vertex hidden by stale finite bounds.");
                issues.Clear(); finite[1] = Vector3.right; normal[2] = new Vector3(0, float.PositiveInfinity, 0);
                mesh.SetVertexBufferData(finite, 0, 0, 3, 0, flags); mesh.SetVertexBufferData(normal, 0, 0, 3, 1, flags);
                InspectMesh(mesh, issues);
                if (!issues.Any(issue => issue.Contains("nonfinite normal[2]"))) throw new InvalidOperationException("The gate missed an infinite normal with finite vertices/bounds.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            Debug.Log("DEAD AIR GEOMETRY GATE FIXTURES PASSED: finite multistream mesh, NaN vertex with stale bounds, infinite normal.");
        }
    }
}
