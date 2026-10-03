using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirThirdPersonGripPreview
    {
        [Serializable] class Result { public string role, mode; public float contactErrorMetres, torsoDeltaMetres; public string image; }
        [Serializable] class Report { public string capturedAtUtc; public List<Result> results = new List<Result>(); }

        [MenuItem("Dead Air/Render Third Person Equipment Grips")]
        public static void RenderAll()
        {
            const int layer = 29;
            const string output = "Art/Characters/Previews/third-person";
            Directory.CreateDirectory(output);
            var report = new Report { capturedAtUtc = DateTime.UtcNow.ToString("O") };
            var stage = new GameObject("Third person grip validation") { hideFlags = HideFlags.HideAndDontSave };
            var oldTarget = RenderTexture.active;
            RenderTexture frame = null;
            try
            {
                var camera = new GameObject("Equipment contact studio camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.enabled = false;
                camera.cullingMask = 1 << layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.055f, .075f, .085f);
                camera.orthographic = true;
                camera.orthographicSize = 1.10f;
                camera.transform.position = new Vector3(1.1f, 2.05f, 4.5f);
                camera.transform.LookAt(new Vector3(0, 1.25f, .1f));
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 15;
                Light(stage.transform, layer, new Vector3(-3, 5, 5), .60f, new Color(1, .87f, .75f));
                Light(stage.transform, layer, new Vector3(4, 3, 4), .40f, new Color(.73f, .84f, 1));
                Light(stage.transform, layer, new Vector3(0, 4, -3), .35f, new Color(.76f, 1, .90f));
                frame = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32);
                frame.antiAliasing = 2;
                frame.Create();
                camera.targetTexture = frame;
                foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
                    foreach (string mode in new[] { "flashlight", "map", "camera", "personal" })
                        RenderPose(stage, camera, frame, layer, output, report, role, mode, "Idle", .2f);
                RenderPose(stage, camera, frame, layer, output, report, "operator", "lighting", "UseItem", 2.4f / 7f);
                RenderPose(stage, camera, frame, layer, output, report, "operator", "smoke", "UseItem", 4.15f / 7f);
                RenderPose(stage, camera, frame, layer, output, report, "engineer", "empty", "Idle", .2f);
                camera.transform.position = new Vector3(4.5f, 1.75f, .45f);
                camera.transform.LookAt(new Vector3(0, 1.25f, .1f));
                foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
                    foreach (string mode in new[] { "map", "camera" })
                        RenderPose(stage, camera, frame, layer, output, report, role, mode, "Idle", .2f, "-side");
                File.WriteAllText(Path.Combine(output, "capture-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("DEAD AIR THIRD PERSON GRIPS PASSED: " + report.results.Count + " rendered equipment poses, measured imported finger contacts, map/camera errors within one centimetre, torso vertices unchanged by hand IK.");
            }
            finally
            {
                RenderTexture.active = oldTarget;
                if (frame != null) { frame.Release(); UnityEngine.Object.DestroyImmediate(frame); }
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        static void RenderPose(GameObject stage, Camera camera, RenderTexture frame, int layer, string output, Report report,
            string role, string mode, string clip, float normalizedTime, string suffix = "")
        {
            var prefab = Resources.Load<GameObject>("World/character_" + role);
            if (prefab == null) throw new InvalidOperationException("Missing Blender character " + role);
            var character = UnityEngine.Object.Instantiate(prefab, stage.transform);
            try
            {
                var motion = character.GetComponent<DeadAirCharacterMotion>();
                if (motion == null) throw new InvalidOperationException("Missing character motion " + role);
                motion.SetPreview(clip, normalizedTime);
                var before = Bake(character);
                var equipment = new PlayerState { characterClass = role, flashlight = true, inventory = new List<string> { mode == "empty" ? "empty" : "flashlight" },
                    selectedSlot = 0, cigarettesRemaining = 11, mapOpen = mode == "map", camera = mode == "camera",
                    uniqueItemEquipped = mode == "personal" || mode == "lighting" || mode == "smoke" };
                motion.SetEquipmentPreview(equipment, clip, normalizedTime);
                float error = motion.GripContactError;
                float torsoDelta = TorsoDelta(character, before);
                if (torsoDelta > .00001f) throw new InvalidOperationException(role + " " + mode + " hand IK moved torso vertices: " + torsoDelta);
                if (float.IsNaN(error) || float.IsInfinity(error) || error > .01f)
                    throw new InvalidOperationException(role + " " + mode + " hand contact error exceeds one centimetre: " + error);
                foreach (var t in character.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    renderer.updateWhenOffscreen = true;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }
                camera.Render();
                RenderTexture.active = frame;
                var pixels = new Texture2D(frame.width, frame.height, TextureFormat.RGB24, false);
                try
                {
                    pixels.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0);
                    pixels.Apply();
                    string imagePath = Path.Combine(output, role + "-" + mode + suffix + ".png");
                    File.WriteAllBytes(imagePath, pixels.EncodeToPNG());
                    report.results.Add(new Result { role = role, mode = mode + suffix, contactErrorMetres = error, torsoDeltaMetres = torsoDelta, image = imagePath.Replace('\\', '/') });
                    Debug.Log("THIRD PERSON GRIP VALIDATED: " + role + " " + mode + " errorMetres=" + error);
                }
                finally { UnityEngine.Object.DestroyImmediate(pixels); }
            }
            finally { UnityEngine.Object.DestroyImmediate(character); }
        }

        static Dictionary<SkinnedMeshRenderer, Vector3[]> Bake(GameObject character)
        {
            var result = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
            foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try { renderer.BakeMesh(mesh); result.Add(renderer, mesh.vertices); }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            return result;
        }

        static float TorsoDelta(GameObject character, Dictionary<SkinnedMeshRenderer, Vector3[]> before)
        {
            float maximum = 0;
            foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try
                {
                    renderer.BakeMesh(mesh);
                    var vertices = mesh.vertices;
                    var weights = renderer.sharedMesh.boneWeights;
                    var original = before[renderer];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var w = weights[i];
                        if ((w.weight0 > .001f && !TorsoBone(renderer.bones[w.boneIndex0].name)) ||
                            (w.weight1 > .001f && !TorsoBone(renderer.bones[w.boneIndex1].name)) ||
                            (w.weight2 > .001f && !TorsoBone(renderer.bones[w.boneIndex2].name)) ||
                            (w.weight3 > .001f && !TorsoBone(renderer.bones[w.boneIndex3].name))) continue;
                        maximum = Mathf.Max(maximum, Vector3.Distance(vertices[i], original[i]));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            return maximum;
        }

        static bool TorsoBone(string name) => name == "root" || name == "pelvis" || name == "spine" || name == "chest" || name == "neck" || name == "head";

        static void Light(Transform parent, int layer, Vector3 position, float intensity, Color color)
        {
            var light = new GameObject("Contact studio light").AddComponent<UnityEngine.Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = position;
            light.transform.LookAt(new Vector3(0, 1.2f, 0));
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << layer;
            light.shadows = LightShadows.None;
        }
    }
}
