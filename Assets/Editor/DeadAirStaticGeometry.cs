using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadAir.Editor
{
    // Marks the office and the yard as static so Umbra can bake occlusion data and
    // the build can batch static geometry. Moving or re-materialised parts keep
    // their non-static state: doors, lockers, drawers, fans, mirrors, lamps and
    // screens are controlled at runtime by DeadAirWorld.
    public static class DeadAirStaticGeometry
    {
        const string ScenePath = "Assets/Scenes/Blackpine.unity";
        static readonly string[] Prefabs =
        {
            "Assets/Resources/World/station.prefab",
            "Assets/Resources/World/exterior.prefab",
        };
        static readonly string[] DynamicPrefixes =
        {
            "door:", "locker-door:", "interactive:", "state:", "fan:", "motion:", "mirror:",
            "monitor:", "drawer:", "vehicle:", "van", "prop:van-", "dropped",
        };

        [MenuItem("Dead Air/Performance/1. Mark static geometry")]
        public static void Apply()
        {
            foreach (string path in Prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) throw new InvalidOperationException("Missing prefab: " + path);
                int occludees = 0, occluders = 0, dynamic = 0;
                try
                {
                    foreach (var go in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (go == root.transform) continue;
                        var flags = (StaticEditorFlags)0;
                        if (IsDynamic(go))
                        {
                            dynamic++;
                        }
                        else
                        {
                            flags |= StaticEditorFlags.OccludeeStatic;
                            occludees++;
                            if (CanOcclude(go))
                            {
                                flags |= StaticEditorFlags.OccluderStatic;
                                occluders++;
                            }
                        }
                        GameObjectUtility.SetStaticEditorFlags(go.gameObject, flags);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                Debug.Log("DEAD AIR STATIC: " + path + " occludees=" + occludees + " occluders=" + occluders + " dynamic=" + dynamic);
            }
            Debug.Log("DEAD AIR STATIC: prefab flags written.");
        }

        [MenuItem("Dead Air/Performance/2. Bake occlusion culling")]
        public static void Bake()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int occludees = 0, occluders = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.OccludeeStatic) != 0) occludees++;
                if ((flags & StaticEditorFlags.OccluderStatic) != 0) occluders++;
            }
            Debug.Log("DEAD AIR STATIC: scene occludees=" + occludees + " occluders=" + occluders);
            if (occluders == 0) throw new InvalidOperationException("No occluders in the scene; mark static geometry first.");

            StaticOcclusionCulling.Compute();
            float deadline = Time.realtimeSinceStartup + 3600;
            while (StaticOcclusionCulling.isRunning && Time.realtimeSinceStartup < deadline)
                System.Threading.Thread.Sleep(500);
            if (StaticOcclusionCulling.isRunning)
            {
                StaticOcclusionCulling.Cancel();
                throw new TimeoutException("Occlusion bake did not finish within an hour.");
            }
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DEAD AIR STATIC: occlusion bake saved for " + ScenePath);
        }

        [MenuItem("Dead Air/Performance/4. Add static batching")]
        public static void ApplyBatching()
        {
            foreach (string path in Prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) throw new InvalidOperationException("Missing prefab: " + path);
                int batched = 0;
                try
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform == root.transform) continue;
                        var flags = GameObjectUtility.GetStaticEditorFlags(transform.gameObject);
                        // Batching only objects that are not moved at runtime; the
                        // dynamic prefixes keep their flags cleared by Apply().
                        if (flags == 0) continue;
                        GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags | StaticEditorFlags.BatchingStatic);
                        batched++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                Debug.Log("DEAD AIR STATIC: batching added to " + batched + " objects in " + path);
            }
        }

        [MenuItem("Dead Air/Performance/5. Remove static batching")]
        public static void ClearBatching()
        {
            foreach (string path in Prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) throw new InvalidOperationException("Missing prefab: " + path);
                int cleared = 0;
                try
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform == root.transform) continue;
                        var flags = GameObjectUtility.GetStaticEditorFlags(transform.gameObject);
                        if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                        GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                        cleared++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                Debug.Log("DEAD AIR STATIC: batching removed from " + cleared + " objects in " + path);
            }
        }

        [MenuItem("Dead Air/Performance/3. Report static flags")]
        public static void Report()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int batching = 0, occludee = 0, occluder = 0, dynamic = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if (flags == 0) { dynamic++; continue; }
                if ((flags & StaticEditorFlags.BatchingStatic) != 0) batching++;
                if ((flags & StaticEditorFlags.OccludeeStatic) != 0) occludee++;
                if ((flags & StaticEditorFlags.OccluderStatic) != 0) occluder++;
            }
            Debug.Log("DEAD AIR STATIC REPORT: batching=" + batching + " occludee=" + occludee + " occluder=" + occluder + " dynamic=" + dynamic);
        }

        static bool IsDynamic(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                string name = current.name;
                foreach (string prefix in DynamicPrefixes)
                    if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static bool CanOcclude(Transform transform)
        {
            var renderer = transform.GetComponent<Renderer>();
            if (renderer == null) renderer = transform.GetComponentInChildren<Renderer>(false);
            if (renderer == null) return false;
            var bounds = transform.InverseTransformBounds(renderer.bounds);
            var size = bounds.size;
            if (size.x * size.y * size.z < 8f) return false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (material.renderQueue >= 2450) return false;
                if (material.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (material.name.IndexOf("web", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
        }

        static Bounds InverseTransformBounds(this Transform transform, Bounds worldBounds)
        {
            var center = transform.InverseTransformPoint(worldBounds.center);
            var size = transform.InverseTransformVector(worldBounds.size);
            return new Bounds(center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }
    }
}
