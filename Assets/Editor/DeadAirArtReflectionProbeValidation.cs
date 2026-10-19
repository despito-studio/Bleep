using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirArtReflectionProbeValidation
    {
        [MenuItem("Dead Air/Production Art/Validate Local Reflection Probe Contracts")]
        public static void Validate()
        {
            Require(!DeadAirSettings.ResolveArtReviewReflections(null) &&
                !DeadAirSettings.ResolveArtReviewReflections(new[] { "-deadAirArtReviewReflections" }) &&
                !DeadAirSettings.ResolveArtReviewReflections(new[] { "-deadAirBlackpineCapture", "D:/review" }) &&
                DeadAirSettings.ResolveArtReviewReflections(new[] { "-deadAirBlackpineCapture", "D:/review", "-deadAirArtReviewReflections" }),
                "The process-only reflection override must require both explicit capture and reflection flags.");
            Require(DeadAirArtReflectionProbes.ResolutionForQuality(0) == 64 &&
                DeadAirArtReflectionProbes.ResolutionForQuality(1) == 64 &&
                DeadAirArtReflectionProbes.ResolutionForQuality(2) == 128, "Environment capture must remain bounded at 64/128 pixels.");
            var root = new GameObject("Mirrored school root / probe contract");
            root.transform.localScale = new Vector3(-1, 1, 1);
            try
            {
                var manager = DeadAirArtReflectionProbes.Attach(root.transform);
                var eye = new Vector3(18.4f, 5.4f, -14.8f);
                var volume = manager.VolumeForEye(eye, out var key);
                Require(volume.Contains(eye) && key.StartsWith("cell:"), "School probe influence must use actual world-space eye coordinates.");
                manager.ReleaseOwnedResources(); manager.ReleaseOwnedResources();
                Require(manager.AllocatedProbeCount == 0, "Probe cleanup must be idempotent.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("DEAD AIR LOCAL REFLECTION PROBE CONTRACTS PASSED: world-space bounds, resolution budget, cleanup.");
        }

        [MenuItem("Dead Air/Production Art/Validate Real Cubemap GPU Capture")]
        public static void ValidateGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Real cubemap validation requires a GPU.");
            var root = new GameObject("sector: actual cubemap GPU fixture");
            var material = new Material(Shader.Find("Unlit/Color")) { color = new Color(.05f, .9f, .15f), hideFlags = HideFlags.HideAndDontSave };
            RenderTexture cube = null;
            var target = RenderTexture.active;
            float planar = Shader.GetGlobalFloat("_DeadAirReflectionEnabled");
            var eye = new Vector3(1000, 1000, 1000);
            try
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube); panel.transform.SetParent(root.transform, false);
                panel.layer = 24; panel.transform.position = eye + Vector3.forward * 3;
                panel.transform.localScale = new Vector3(8, 8, .05f); panel.GetComponent<Renderer>().sharedMaterial = material;
                var renderer = panel.GetComponent<Renderer>(); renderer.enabled = false;
                cube = DeadAirArtReflectionProbes.CaptureCube(eye, 64, 1 << 24, .05f, 8, root.scene, new Bounds(eye, Vector3.one * 16));
                Require(cube != null && cube.IsCreated() && cube.dimension == TextureDimension.Cube && cube.mipmapCount > 1,
                    "Actual HDR cubemap must have six faces and a roughness mip chain.");
                Graphics.SetRenderTarget(cube, 0, CubemapFace.PositiveZ);
                var sample = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                try
                {
                    sample.ReadPixels(new Rect(32, 32, 1, 1), 0, 0); sample.Apply(); var color = sample.GetPixel(0, 0);
                    Require(color.g > .5f && color.g > color.r * 4, "The cubemap must contain the actual green scene panel, rather than a flat fallback.");
                }
                finally { UnityEngine.Object.DestroyImmediate(sample); }
                Require(!renderer.enabled, "The hidden sector renderer must be captured and restored before the main camera renders.");
                renderer.enabled = true; renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                cube.Release(); UnityEngine.Object.DestroyImmediate(cube);
                cube = DeadAirArtReflectionProbes.CaptureCube(eye, 64, 1 << 24, .05f, 8, root.scene, new Bounds(eye, Vector3.one * 16));
                Graphics.SetRenderTarget(cube, 0, CubemapFace.PositiveZ);
                var shadowSample = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                try
                {
                    shadowSample.ReadPixels(new Rect(32, 32, 1, 1), 0, 0); shadowSample.Apply();
                    Require(shadowSample.GetPixel(0, 0).g > .5f, "A ShadowsOnly sector surface must also appear in the actual cubemap.");
                }
                finally { UnityEngine.Object.DestroyImmediate(shadowSample); }
                Require(renderer.enabled && renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly,
                    "Reflection capture must restore both sector visibility and original shadow mode.");
                Require(!DeadAirArtReflectionProbes.IsCapturing && Mathf.Approximately(planar, Shader.GetGlobalFloat("_DeadAirReflectionEnabled")),
                    "Cubemap capture must restore the floor-planar shader state and recursion guard.");
            }
            finally
            {
                RenderTexture.active = target;
                if (cube != null) { cube.Release(); UnityEngine.Object.DestroyImmediate(cube); }
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(material);
            }
            Debug.Log("DEAD AIR REAL CUBEMAP GPU VALIDATION PASSED: actual scene pixels, HDR mips, state restoration, resource cleanup.");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
