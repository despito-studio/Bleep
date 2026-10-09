using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>A small owner-only skin surface underneath the source garment's open neckline.</summary>
    public sealed class DeadAirBodyNeckLining : MonoBehaviour
    {
        Mesh ownedMesh;
        Material ownedMaterial;

        // Create while the source rig is in its rest pose, before owner pose/root fitting.
        public static SkinnedMeshRenderer Create(GameObject avatar, string role)
        {
            if (avatar == null || role != "operator" && role != "dispatcher") return null;
            Transform chest = null, secondaryChest = null;
            foreach (var bone in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "chest") chest = bone;
                if (bone.name == "Secondary clothed chest") secondaryChest = bone;
            }
            var support = secondaryChest != null ? secondaryChest : chest;
            var shader = Shader.Find("Dead Air/Source Surface");
            if (support == null || shader == null) return null;

            bool operatorRole = role == "operator";
            Vector3 centre = operatorRole ? new Vector3(0, 1.42f, .112f) : new Vector3(.005f, 1.55f, .211f);
            float radiusX = operatorRole ? .130f : .135f, radiusZ = operatorRole ? .065f : .073f;
            const int segments = 32;
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            var weights = new BoneWeight[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = centre + Vector3.up * .03f;
            uv[0] = new Vector2(.5f, .5f);
            weights[0] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment * Mathf.PI * 2 / segments;
                float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
                vertices[segment + 1] = centre + new Vector3(x * radiusX, 0, z * radiusZ);
                uv[segment + 1] = new Vector2(x * .5f + .5f, z * .5f + .5f);
                weights[segment + 1] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                triangles[segment * 3] = 0;
                // X/Z rings wind clockwise when viewed from above: reverse the fan for +Y normals.
                triangles[segment * 3 + 1] = (segment + 1) % segments + 1;
                triangles[segment * 3 + 2] = segment + 1;
            }
            var mesh = new Mesh { name = role + " / owner neckline skin lining", hideFlags = HideFlags.DontSave,
                vertices = vertices, uv = uv, triangles = triangles, boneWeights = weights,
                bindposes = new[] { support.worldToLocalMatrix * avatar.transform.localToWorldMatrix } };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            Color fallback = operatorRole ? new Color32(172, 139, 74, 255) : new Color32(170, 132, 74, 255);
            var material = new Material(shader) { name = role + " / source hand-colour neckline lining",
                color = HandColour(avatar, role, fallback), hideFlags = HideFlags.DontSave };
            material.SetFloat("_Glossiness", .10f); material.SetFloat("_Metallic", 0);
            material.SetFloat("_Cull", 2); material.SetFloat("_ZWrite", 1); material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            material.SetFloat("_SrcBlend", (float)BlendMode.One); material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetColor("_EmissionColor", Color.black);

            var child = new GameObject("First person neckline lining") { layer = 27 };
            child.transform.SetParent(avatar.transform, false);
            var lifetime = child.AddComponent<DeadAirBodyNeckLining>();
            lifetime.ownedMesh = mesh; lifetime.ownedMaterial = material;
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; renderer.sharedMaterial = material;
            renderer.bones = new[] { support }; renderer.rootBone = avatar.transform;
            renderer.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * .12f);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.updateWhenOffscreen = true;
            return renderer;
        }

        static Color HandColour(GameObject avatar, string role, Color fallback)
        {
            var resource = Resources.Load<TextAsset>("Characters/Appearance/" + role + "-palette");
            byte[] palette = resource != null ? resource.bytes : null;
            if (palette == null || palette.Length != 128 * 128 * 3) return fallback;
            Color total = Color.black; int count = 0;
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // One original body supplies its unused, still-readable hand vertices after clipping.
                // Ignore its full-shadow copy and generated wardrobe surfaces.
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || !renderer.gameObject.activeInHierarchy) continue;
                var mesh = renderer.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var sourceUV = mesh.uv; var weights = mesh.boneWeights; var bones = renderer.bones;
                for (int vertex = 0; vertex < weights.Length && vertex < sourceUV.Length; vertex++)
                {
                    var weight = weights[vertex];
                    int bone = weight.boneIndex0; float largest = weight.weight0;
                    if (weight.weight1 > largest) { bone = weight.boneIndex1; largest = weight.weight1; }
                    if (weight.weight2 > largest) { bone = weight.boneIndex2; largest = weight.weight2; }
                    if (weight.weight3 > largest) bone = weight.boneIndex3;
                    if (bone < 0 || bone >= bones.Length || bones[bone] == null ||
                        !bones[bone].name.StartsWith("hand.", StringComparison.Ordinal)) continue;
                    int x = Mathf.Clamp(Mathf.FloorToInt(sourceUV[vertex].x * 128), 0, 127);
                    int y = Mathf.Clamp(Mathf.FloorToInt(sourceUV[vertex].y * 128), 0, 127);
                    int pixel = (y * 128 + x) * 3;
                    var colour = new Color(palette[pixel] / 255f, palette[pixel + 1] / 255f, palette[pixel + 2] / 255f);
                    if (colour.grayscale < .13f || colour.r > colour.g * 1.6f && colour.g < .23f) continue;
                    total += colour; count++;
                }
            }
            if (count < 3) return fallback;
            Color average = total / count; average.a = 1; return average;
        }

        void OnDestroy()
        {
            if (ownedMesh != null) Release(ownedMesh);
            if (ownedMaterial != null) Release(ownedMaterial);
            ownedMesh = null; ownedMaterial = null;
        }
        static void Release(UnityEngine.Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
