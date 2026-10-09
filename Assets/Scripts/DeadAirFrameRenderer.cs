using System;
using UnityEngine;

namespace DeadAir
{
    // Render the world at the selected scale beneath the native-resolution UI.
    [DefaultExecutionOrder(1000)]
    public sealed class DeadAirFrameRenderer : MonoBehaviour
    {
        Camera worldCamera;
        DeadAirFirstPersonRig equipment;
        DeadAirTapeEffect tape;
        RenderTexture previousTarget;
        bool previousMsaa;
        float previousAspect;
        RenderingPath previousRenderingPath;

        public Camera OutputCamera { get; private set; }
        public RenderTexture SceneFrame { get; private set; }

        public DeadAirTapeEffect Initialize(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (worldCamera != null)
            {
                if (worldCamera != camera) throw new InvalidOperationException("The frame renderer already has a world camera.");
                EnsureTargets();
                return tape;
            }

            worldCamera = camera;
            equipment = camera.GetComponentInParent<DeadAirFirstPersonRig>();
            previousTarget = camera.targetTexture;
            previousMsaa = camera.allowMSAA;
            previousAspect = camera.aspect;
            previousRenderingPath = camera.renderingPath;
            camera.allowMSAA = false;
            camera.allowHDR = true;
            camera.depthTextureMode |= DepthTextureMode.DepthNormals;
            camera.gameObject.AddComponent<DeadAirAmbientOcclusion>();
            camera.gameObject.AddComponent<DeadAirPlanarReflections>();
            foreach(var light in camera.GetComponentsInChildren<Light>(true))DeadAirPlanarReflections.RegisterLocalShadowLight(light);

            var output = new GameObject("Dead Air presentation camera");
            output.transform.SetParent(camera.transform, false);
            OutputCamera = output.AddComponent<Camera>();
            OutputCamera.cullingMask = 0;
            OutputCamera.clearFlags = CameraClearFlags.SolidColor;
            OutputCamera.backgroundColor = Color.black;
            OutputCamera.allowHDR = true;
            OutputCamera.allowMSAA = false;
            OutputCamera.useOcclusionCulling = false;
            OutputCamera.targetDisplay = camera.targetDisplay;
            OutputCamera.depth = camera.depth + 1;
            OutputCamera.enabled = true;
            tape = output.AddComponent<DeadAirTapeEffect>();
            EnsureTargets();
            return tape;
        }

        void LateUpdate() { EnsureTargets(); }

        public void EnsureTargets()
        {
            if (worldCamera == null || OutputCamera == null) return;
            // Many overlapping practical lights repeatedly redraw room geometry in
            // Forward. Deferred shades those pixels without one mesh pass per light.
            // The small menu and Low preset keep the cheaper Forward setup.
            bool menu=(worldCamera.cullingMask & (1 << 28))!=0;
            var path=!menu && DeadAirSettings.GraphicsQuality>0 && SystemInfo.supportedRenderTargetCount>=4
                ? RenderingPath.DeferredShading : RenderingPath.Forward;
            if(worldCamera.renderingPath!=path)worldCamera.renderingPath=path;
            int screenWidth = Mathf.Max(1, Screen.width);
            int screenHeight = Mathf.Max(1, Screen.height);
            float aspect = screenWidth / (float)screenHeight;
            int height = Mathf.Max(240, Mathf.RoundToInt(screenHeight * DeadAirSettings.RenderScale));
            int width = Mathf.Max(1, Mathf.RoundToInt(height * aspect));

            if (SceneFrame == null || SceneFrame.width != width || SceneFrame.height != height)
            {
                // Keep lamp and flashlight highlights until the final tone map.
                // An 8-bit scene target clipped them before exposure was applied.
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                    ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                var next = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Linear)
                {
                    name = "DEAD AIR scene frame",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.DontSave
                };
                if (!next.Create())
                {
                    DestroyTexture(next);
                    throw new InvalidOperationException("Unable to allocate the DEAD AIR scene frame.");
                }
                var old = SceneFrame;
                SceneFrame = next;
                worldCamera.targetTexture = SceneFrame;
                tape.SourceFrame = SceneFrame;
                DestroyTexture(old);
            }
            else if (!SceneFrame.IsCreated()) SceneFrame.Create();

            worldCamera.targetTexture = SceneFrame;
            worldCamera.aspect = aspect;
            if (equipment != null) equipment.ConfigureRenderer(SceneFrame);
            OutputCamera.aspect = aspect;
            OutputCamera.depth = worldCamera.depth + 1;
            tape.SourceFrame = SceneFrame;
        }

        public void RenderEquipment()
        {
            if (equipment != null) equipment.RenderEquipment(SceneFrame);
        }

        static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        }

        void OnDestroy()
        {
            if (worldCamera != null)
            {
                if (worldCamera.targetTexture == SceneFrame) worldCamera.targetTexture = previousTarget;
                worldCamera.allowMSAA = previousMsaa;
                worldCamera.aspect = previousAspect;
                worldCamera.renderingPath = previousRenderingPath;
            }
            if (tape != null) tape.SourceFrame = null;
            DestroyTexture(SceneFrame);
            SceneFrame = null;
            if (OutputCamera != null)
            {
                OutputCamera.enabled = false;
                if (Application.isPlaying) Destroy(OutputCamera.gameObject); else DestroyImmediate(OutputCamera.gameObject);
            }
        }
    }
}
