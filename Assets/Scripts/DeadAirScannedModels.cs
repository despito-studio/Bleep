using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadAir
{
    public static class DeadAirScannedModels
    {
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Mesh> fittedMeshes = new Dictionary<string, Mesh>();

        public static void ClearCaches()
        {
            prefabs.Clear();
            fittedMeshes.Clear();
        }

        public static GameObject TryInstall(Transform parent, string resourceKey, Vector3 position,
            Quaternion rotation, Vector3 targetBounds)
        {
            GameObject prefab;
            if (!prefabs.TryGetValue(resourceKey, out prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>("ArtProduction/Models/" + resourceKey);
                if (prefab == null) return null;
                prefabs[resourceKey] = prefab;
            }
            var result = Object.Instantiate(prefab, parent, false);
            result.name = "Scanned " + resourceKey;
            result.transform.localPosition = Vector3.zero;
            result.transform.localRotation = Quaternion.identity;
            result.transform.localScale = Vector3.one;
            var filters = result.GetComponentsInChildren<MeshFilter>(true);
            var bounds = new Bounds();
            bool initialized = false;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = result.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!initialized) { bounds = new Bounds(p, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(p);
                }
            }
            if (!initialized) { Object.DestroyImmediate(result); return null; }
            var scale = new Vector3(targetBounds.x / Mathf.Max(.001f, bounds.size.x),
                targetBounds.y / Mathf.Max(.001f, bounds.size.y), targetBounds.z / Mathf.Max(.001f, bounds.size.z));
            var offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (var filter in filters)
            {
                Mesh original = filter.sharedMesh;
                if (original == null) continue;
                Matrix4x4 matrix = Matrix4x4.Scale(scale) * Matrix4x4.Translate(-offset) *
                    result.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                string id = resourceKey + "-" + original.name + "-" + original.vertexCount + "-" +
                    Hash128.Compute(matrix.ToString()).ToString();
                Mesh mesh;
                if (!fittedMeshes.TryGetValue(id, out mesh) || mesh == null)
                {
                    mesh = Object.Instantiate(original);
                    mesh.name = id;
                    var vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                    mesh.vertices = vertices;
                    var normals = mesh.normals;
                    var normalMatrix = matrix.inverse.transpose;
                    for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                    mesh.normals = normals;
                    mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        string folder = "Assets/ArtProduction/FittedMeshes";
                        System.IO.Directory.CreateDirectory(folder);
                        string path = folder + "/" + Hash128.Compute(id + matrix.ToString()).ToString() + ".asset";
                        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (saved == null) AssetDatabase.CreateAsset(mesh, path);
                        else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); mesh = saved; EditorUtility.SetDirty(saved); }
                    }
#endif
                    fittedMeshes[id] = mesh;
                }
                // Parts retain separate objects and materials; only their transform is baked.
                filter.transform.SetParent(result.transform, false);
                filter.name = original.name;
                filter.transform.localPosition = Vector3.zero;
                filter.transform.localRotation = Quaternion.identity;
                filter.transform.localScale = Vector3.one;
                filter.sharedMesh = mesh;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider != null) collider.sharedMesh = mesh;
            }
            var lod = result.GetComponent<LODGroup>();
            if (lod != null) lod.RecalculateBounds();
            if (resourceKey == "potted-plant")
            {
                var pot = result.AddComponent<BoxCollider>();
                pot.center = new Vector3(0, targetBounds.y * .15f, 0);
                pot.size = new Vector3(targetBounds.x * .65f, targetBounds.y * .3f, targetBounds.z * .65f);
            }
            result.transform.localPosition = position;
            result.transform.localRotation = rotation;
            return result;
        }
    }
}
