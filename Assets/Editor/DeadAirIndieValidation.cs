using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirIndieValidation
    {
        [MenuItem("Dead Air/Validate Indie Post Processing")]
        public static void Validate()
        {
            const int width = 256, height = 128;
            var tapeShader = Resources.Load<Shader>("Shaders/DeadAirTape");
            var glowShader = Resources.Load<Shader>("Shaders/DeadAirBloom");
            Require(tapeShader != null && tapeShader.isSupported && glowShader != null && glowShader.isSupported,
                "Both post-processing shaders must be GPU supported.");
            var cameraObject = new GameObject("Indie shader validation camera");
            var tape = cameraObject.AddComponent<Camera>().gameObject.AddComponent<DeadAirTapeEffect>();
            var source = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pattern = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            bool hadGrain = PlayerPrefs.HasKey("DeadAir.FilmGrain");
            int grain = PlayerPrefs.GetInt("DeadAir.FilmGrain");
            try
            {
                PlayerPrefs.SetInt("DeadAir.FilmGrain", 0);
                var pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = x > 100 && x < 140 && y > 40 && y < 90
                        ? new Color(2.2f, 1.6f, .8f) : new Color(.04f + x / 900f, .05f + y / 700f, .09f);
                pattern.SetPixels(pixels); pattern.Apply();
                Graphics.Blit(pattern, source);
                tape.SourceFrame = source;
                var render = typeof(DeadAirTapeEffect).GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.NonPublic);
                render.Invoke(tape, new object[] { source, target });
                var clean = Read(target, readback);
                tape.Trip = 1; tape.TripClock = 8;
                render.Invoke(tape, new object[] { source, target });
                var trip = Read(target, readback);
                float change = 0;
                for (int i = 0; i < clean.Length; i++)
                    change += Mathf.Abs(clean[i].r - trip[i].r) + Mathf.Abs(clean[i].g - trip[i].g) + Mathf.Abs(clean[i].b - trip[i].b);
                Require(change / clean.Length > .025f, "An eaten mushroom must visibly alter the rendered frame.");
                tape.Trip = 0;
                render.Invoke(tape, new object[] { source, target });
                var restored = Read(target, readback);
                for (int i = 0; i < clean.Length; i++)
                    Require(Mathf.Abs(restored[i].r - clean[i].r) + Mathf.Abs(restored[i].g - clean[i].g) +
                        Mathf.Abs(restored[i].b - clean[i].b) < .015f, "Ending the trip must restore the exact ordinary view.");
                Require(clean[height / 2 * width + 120].maxColorComponent > .5f,
                    "Tone mapping must preserve readable warm highlights.");
                Debug.Log("DEAD AIR INDIE POST PASSED: HDR bloom and supported shaders, visibly distinct mushroom GPU frame and clean reset without persistent distortion.");
            }
            finally
            {
                if (hadGrain) PlayerPrefs.SetInt("DeadAir.FilmGrain", grain); else PlayerPrefs.DeleteKey("DeadAir.FilmGrain");
                RenderTexture.active = previous;
                tape.SourceFrame = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                source.Release(); target.Release();
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pattern); UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        static Color[] Read(RenderTexture frame, Texture2D readback)
        {
            RenderTexture.active = frame;
            readback.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0); readback.Apply();
            return readback.GetPixels();
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Indie post validation: " + message);
        }
    }
}
