using System;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirPresentationValidation
    {
        [MenuItem("Dead Air/Validate Clean Screen")]
        public static void ValidateCleanScreen()
        {
            const int width = 256, height = 128;
            var shader = Resources.Load<Shader>("Shaders/DeadAirTape");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Presentation shader unavailable.");
            var material = new Material(shader);
            var source = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                var pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = x / 8 % 2 == 0 ? new Color(.12f, .2f, .3f) : new Color(.45f, .32f, .1f);
                source.SetPixels(pixels); source.Apply();
                material.SetFloat("_Exposure", 1.3f);
                material.SetFloat("_Aspect", 1366f / 768);
                material.SetFloat("_Clock", 13.7f);
                material.SetFloat("_Flicker", 1);
                material.SetFloat("_Grain", 0);
                // Check normal menu/game output and maximum horror state. Neither can
                // tear floor lines, shift the lower band or darken the frame edges.
                for (int horror = 0; horror <= 1; horror++)
                {
                    material.SetFloat("_Threat", horror);
                    material.SetFloat("_Insanity", horror);
                    material.SetFloat("_Nightmare", horror);
                    Graphics.Blit(source, target, material);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                    var actual = readback.GetPixels32();
                    for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        var center = actual[(height / 2) * width + x];
                        var sample = actual[y * width + x];
                        if (Math.Abs(sample.r - center.r) > 1 || Math.Abs(sample.g - center.g) > 1 || Math.Abs(sample.b - center.b) > 1)
                            throw new InvalidOperationException("Screen has nonuniform shading or tracking at " + x + "," + y + ".");
                        var matchingColumn = actual[(height / 2) * width + x % 16];
                        if (Math.Abs(sample.r - matchingColumn.r) > 1 || Math.Abs(sample.g - matchingColumn.g) > 1 || Math.Abs(sample.b - matchingColumn.b) > 1)
                            throw new InvalidOperationException("Screen has barrel/channel distortion or corner shading at " + x + "," + y + ".");
                    }
                }
                Debug.Log("DEAD AIR CLEAN SCREEN PASSED: actual GPU output is uniform at all edges, with no tracking band, scanlines, vignette or channel/UV warp.");
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static void ValidateAndBuild()
        {
            DeadAirOfficeBuilder.RefreshCobwebGeometry();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            var failures = new System.Collections.Generic.List<Exception>();
            foreach (Action check in new Action[] { ValidateCleanScreen, DeadAirParkingValidation.Validate,
                DeadAirDressingValidation.Validate, DeadAirScareValidation.Validate,
                DeadAirMountedGeometryValidation.Validate, DeadAirHorrorRulesValidation.Validate,
                DeadAirOfficeVerification.ValidateHinges, DeadAirEditorSetup.ValidateInventory })
            {
                try { check(); }
                catch (Exception error) { Debug.LogException(error); failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException("Presentation validation failed.", failures);
            var audioObject = new GameObject("Procedural library export");
            try
            {
                var audio = audioObject.AddComponent<DeadAirAudio>(); audio.Initialize();
                audio.ExportLibrary("Verification-Presentation/Audio");
            }
            finally { UnityEngine.Object.DestroyImmediate(audioObject); }
            DeadAirEditorSetup.BuildWindows();
        }
    }
}
