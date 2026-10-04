using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    // A deterministic render from the imported game prefabs, independent of the open scene.
    public static class DeadAirCharacterPreview
    {
        [MenuItem("Dead Air/Render Character Lineup")]
        public static void RenderLineup() => RenderLineup(null);

        public static void RenderLineup(string outputPath)
        {
            const int layer = 29;
            var stage = new GameObject("Character validation studio") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture target = null;
            Material floorMaterial = null;
            var previous = RenderTexture.active;
            try
            {
                var roles = new[] { "operator", "engineer", "journalist", "dispatcher" };
                for (int i = 0; i < roles.Length; i++)
                {
                    var prefab = Resources.Load<GameObject>("World/character_" + roles[i]);
                    if (prefab == null) throw new Exception("Missing character: " + roles[i]);
                    var model = UnityEngine.Object.Instantiate(prefab, stage.transform);
                    model.transform.position = new Vector3(-2.7f + i * 1.8f, 0, 0);
                    var motion = model.GetComponent<DeadAirCharacterMotion>();
                    if (motion != null) motion.SetPreview("Idle", .2f);
                    foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                    foreach (var mesh in model.GetComponentsInChildren<SkinnedMeshRenderer>()) mesh.updateWhenOffscreen = true;
                }
                floorMaterial = new Material(Shader.Find("Dead Air/Source Surface")) { color = new Color(.14f, .18f, .18f) };
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.SetParent(stage.transform); floor.layer = layer;
                floor.transform.position = new Vector3(0, -.07f, 0); floor.transform.localScale = new Vector3(10, .1f, 5);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var camera = new GameObject("Character studio camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform);
                camera.enabled = false; camera.cullingMask = 1 << layer;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .08f, .085f);
                camera.orthographic = true; camera.orthographicSize = 1.7f;
                camera.transform.position = new Vector3(0, 2.7f, 10);
                camera.transform.LookAt(new Vector3(0, 1.2f, 0));
                camera.nearClipPlane = .05f; camera.farClipPlane = 25;
                CreateLight(stage.transform, layer, new Vector3(-4, 5, 6), .7f, new Color(1, .87f, .72f));
                CreateLight(stage.transform, layer, new Vector3(4, 3, 2), .5f, new Color(.64f, .8f, 1));
                CreateLight(stage.transform, layer, new Vector3(0, 4, -3), .6f, new Color(.75f, 1, .87f));
                target = new RenderTexture(1800, 1000, 24, RenderTextureFormat.ARGB32);
                target.antiAliasing = 2; target.Create(); camera.targetTexture = target;
                camera.Render(); RenderTexture.active = target;
                var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                string path = Path.GetFullPath(outputPath ?? "Art/Characters/Previews/unity-lineup.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels);
                Debug.Log("CHARACTER LINEUP SAVED: " + path);
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        static void CreateLight(Transform parent, int layer, Vector3 position, float intensity, Color color)
        {
            var light = new GameObject("Studio softbox").AddComponent<Light>();
            light.transform.SetParent(parent); light.transform.position = position;
            light.transform.LookAt(new Vector3(0, 1, 0)); light.type = LightType.Directional;
            light.color = color; light.intensity = intensity; light.cullingMask = 1 << layer;
            light.shadows = LightShadows.None;
        }
    }
}
