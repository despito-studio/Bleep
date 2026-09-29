using System;
using UnityEngine;

namespace DeadAir
{
    // Draw the scene at the selected VHS resolution, then present it beneath the full-size UI.
    [DefaultExecutionOrder(1000)]
    public sealed class DeadAirFrameRenderer : MonoBehaviour
    {
        Camera worldCamera;
        DeadAirTapeEffect tape;
        RenderTexture previousTarget;
        bool previousMsaa;
        float previousAspect;

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
            previousTarget = camera.targetTexture;
            previousMsaa = camera.allowMSAA;
            previousAspect = camera.aspect;
            camera.allowMSAA = false;
            camera.allowHDR = true;

            var output = new GameObject("VHS output camera");
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
            int screenWidth = Mathf.Max(1, Screen.width);
            int screenHeight = Mathf.Max(1, Screen.height);
            float aspect = screenWidth / (float)screenHeight;
            int height = Mathf.Min(screenHeight, Mathf.Max(240, DeadAirSettings.Quality));
            int width = Mathf.Max(1, Mathf.RoundToInt(height * aspect));

            if (SceneFrame == null || SceneFrame.width != width || SceneFrame.height != height)
            {
                var next = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "DEAD AIR scene frame",
                    filterMode = FilterMode.Point,
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
            OutputCamera.aspect = aspect;
            OutputCamera.depth = worldCamera.depth + 1;
            tape.SourceFrame = SceneFrame;
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
