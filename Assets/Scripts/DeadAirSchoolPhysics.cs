using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public static class DeadAirSchoolPhysics
    {
        private static readonly Vector3 CounterReflection = new Vector3(-1f, 1f, 1f);
        private static readonly Dictionary<Mesh, Mesh> ReflectedMeshes = new Dictionary<Mesh, Mesh>();

        private static Transform Host(Transform source)
        {
            if (source.localToWorldMatrix.determinant >= 0f) return source;
            string name = source.name.StartsWith("Scanned ", System.StringComparison.Ordinal) ? "Scanned collision proxy" : source.name;
            var host = new GameObject(name).transform;
            host.SetParent(source, false);
            host.localScale = CounterReflection;
            host.gameObject.layer = source.gameObject.layer;
            return host;
        }

        private static Vector3 Center(Transform source, Transform host, Vector3 center)
        {
            return host == source ? center : Vector3.Scale(center, CounterReflection);
        }

        public static BoxCollider AddBox(Transform source, Vector3 size, Vector3 center = default)
        {
            Transform host = Host(source);
            var collider = host.gameObject.AddComponent<BoxCollider>();
            collider.center = Center(source, host, center);
            collider.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            return collider;
        }

        public static CapsuleCollider AddCapsule(Transform source, float radius, float height, Vector3 center = default, int direction = 1)
        {
            Transform host = Host(source);
            var collider = host.gameObject.AddComponent<CapsuleCollider>();
            collider.center = Center(source, host, center);
            collider.radius = radius;
            collider.height = height;
            collider.direction = direction;
            return collider;
        }

        internal static GameObject InstallScanned(Transform parent, string key, Vector3 position, Quaternion rotation, Vector3 size)
        {
            if (parent == null || parent.localToWorldMatrix.determinant >= 0f)
                return DeadAirScannedModels.TryInstall(parent, key, position, rotation, size);

            var staging = new GameObject("School scanned collision staging").transform;
            staging.SetParent(parent, false);
            staging.localScale = CounterReflection;
            staging.gameObject.SetActive(false);
            try
            {
                var result = DeadAirScannedModels.TryInstall(staging, key, position, rotation, size);
                if (result == null) return null;
                bool active = result.activeSelf;
                result.SetActive(false);
                result.transform.SetParent(parent, false);
                foreach (var original in result.GetComponentsInChildren<Collider>(true))
                {
                    if (original.transform.localToWorldMatrix.determinant >= 0f) continue;
                    bool enabled = original.enabled;
                    original.enabled = false;
                    Collider replacement;
                    if (original is BoxCollider box)
                        replacement = AddBox(box.transform, box.size, box.center);
                    else if (original is CapsuleCollider capsule)
                        replacement = AddCapsule(capsule.transform, capsule.radius, capsule.height, capsule.center, capsule.direction);
                    else if (original is SphereCollider sphere)
                    {
                        Transform host = Host(sphere.transform);
                        var target = host.gameObject.AddComponent<SphereCollider>();
                        target.center = Center(sphere.transform, host, sphere.center);
                        target.radius = sphere.radius;
                        replacement = target;
                    }
                    else if (original is MeshCollider mesh)
                    {
                        var target = Host(mesh.transform).gameObject.AddComponent<MeshCollider>();
                        target.convex = mesh.convex;
                        target.cookingOptions = mesh.cookingOptions;
                        target.sharedMesh = Reflect(mesh.sharedMesh);
                        replacement = target;
                    }
                    else
                        throw new System.InvalidOperationException("Unsupported school scanned collider: " + original.GetType().Name);
                    replacement.sharedMaterial = original.sharedMaterial;
                    replacement.isTrigger = original.isTrigger;
                    replacement.contactOffset = original.contactOffset;
                    replacement.enabled = enabled;
                    Remove(original);
                }
                result.SetActive(active);
                return result;
            }
            finally
            {
                Remove(staging.gameObject);
            }
        }

        private static Mesh Reflect(Mesh original)
        {
            if (original == null) return null;
            if (ReflectedMeshes.TryGetValue(original, out Mesh reflected) && reflected != null) return reflected;
            reflected = Object.Instantiate(original);
            reflected.name = "School positive-basis collision: " + original.name;
            reflected.hideFlags = HideFlags.DontSave;
            Vector3[] vertices = reflected.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].x = -vertices[i].x;
            reflected.vertices = vertices;
            Vector3[] normals = reflected.normals;
            for (int i = 0; i < normals.Length; i++) normals[i].x = -normals[i].x;
            reflected.normals = normals;
            for (int submesh = 0; submesh < reflected.subMeshCount; submesh++)
            {
                int[] triangles = reflected.GetTriangles(submesh);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int first = triangles[i]; triangles[i] = triangles[i + 2]; triangles[i + 2] = first;
                }
                reflected.SetTriangles(triangles, submesh);
            }
            reflected.RecalculateBounds();
            ReflectedMeshes[original] = reflected;
            return reflected;
        }

        private static void Remove(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
