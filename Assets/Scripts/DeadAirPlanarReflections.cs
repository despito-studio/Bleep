using UnityEngine;

namespace DeadAir
{
    [RequireComponent(typeof(Camera))]
    public sealed class DeadAirPlanarReflections : MonoBehaviour
    {
        Camera source, reflectedCamera;
        RenderTexture frame;
        bool rendering;
        int lastFrame = -1;
        int lastMask;
        float lastFloor = float.NaN;
        public RenderTexture ReflectionFrame => frame;
        public bool IsRenderingReflection { get; private set; }

        void Awake() { source = GetComponent<Camera>(); }

        void OnPreCull()
        {
            if (rendering || source == null) return;
            if (!DeadAirSettings.Reflections)
            {
                Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
                IsRenderingReflection = false;
                return;
            }
            float floor = source.transform.position.y > 4.6f ? 4.2f : source.transform.position.y < -1 ? -4.2f : 0;
            int interval = DeadAirSettings.GraphicsQuality == 2 ? 1 : 2;
            int width = Mathf.Max(256, Mathf.RoundToInt((source.targetTexture != null ? source.targetTexture.width : Screen.width) * .5f));
            int height = Mathf.Max(144, Mathf.RoundToInt((source.targetTexture != null ? source.targetTexture.height : Screen.height) * .5f));
            if (IsRenderingReflection && frame != null && frame.width == width && frame.height == height &&
                lastFloor == floor && lastMask == source.cullingMask &&
                Time.frameCount - lastFrame < interval) return;
            if (frame == null || frame.width != width || frame.height != height)
            {
                ReleaseFrame();
                frame = new RenderTexture(width, height, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Dead Air polished floor reflection", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                frame.Create();
            }
            if (reflectedCamera == null)
            {
                var reflection = new GameObject("Dead Air mirrored floor camera") { hideFlags = HideFlags.DontSave };
                reflectedCamera = reflection.AddComponent<Camera>();
            }
            reflectedCamera.CopyFrom(source);
            reflectedCamera.enabled = false;
            reflectedCamera.depthTextureMode = DepthTextureMode.None;
            reflectedCamera.useOcclusionCulling = false;
            reflectedCamera.allowMSAA = false;
            reflectedCamera.targetTexture = frame;
            reflectedCamera.cullingMask = source.cullingMask & ~(1 << 30);
            reflectedCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectedCamera.backgroundColor = new Color(.012f, .019f, .034f);
            var reflectionMatrix = Matrix4x4.identity;
            reflectionMatrix.m11 = -1;
            reflectionMatrix.m13 = 2 * floor;
            reflectedCamera.transform.position = reflectionMatrix.MultiplyPoint(source.transform.position);
            reflectedCamera.transform.rotation = Quaternion.Euler(-source.transform.eulerAngles.x, source.transform.eulerAngles.y, source.transform.eulerAngles.z);
            reflectedCamera.worldToCameraMatrix = source.worldToCameraMatrix * reflectionMatrix;
            Vector3 position = reflectedCamera.worldToCameraMatrix.MultiplyPoint(new Vector3(0, floor + .025f, 0));
            Vector3 normal = reflectedCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
            var clip = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(position, normal));
            reflectedCamera.projectionMatrix = source.CalculateObliqueMatrix(clip);
            bool previousCulling = GL.invertCulling;
            var previousShadows = QualitySettings.shadows;
            int previousPixelLights = QualitySettings.pixelLightCount;
            try
            {
                rendering = true;
                Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
                GL.invertCulling = !previousCulling;
                // The floor's rough reflection needs light and silhouettes, but
                // a second set of point-light shadow cubemaps adds six passes per light.
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.pixelLightCount = Mathf.Min(previousPixelLights, 4);
                reflectedCamera.Render();
            }
            finally
            {
                QualitySettings.shadows = previousShadows;
                QualitySettings.pixelLightCount = previousPixelLights;
                GL.invertCulling = previousCulling;
                rendering = false;
            }
            Shader.SetGlobalTexture("_DeadAirReflectionTex", frame);
            Shader.SetGlobalVector("_DeadAirReflectionTex_TexelSize", new Vector4(1f / width, 1f / height, width, height));
            Shader.SetGlobalMatrix("_DeadAirReflectionVP", GL.GetGPUProjectionMatrix(reflectedCamera.projectionMatrix, true) * reflectedCamera.worldToCameraMatrix);
            Shader.SetGlobalFloat("_DeadAirReflectionFloor", floor);
            Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 1);
            lastFrame = Time.frameCount;
            lastFloor = floor;
            lastMask = source.cullingMask;
            IsRenderingReflection = true;
        }

        void ReleaseFrame() { if (frame != null) { frame.Release(); Destroy(frame); frame = null; } }
        void OnDestroy()
        {
            ReleaseFrame();
            if (reflectedCamera != null) Destroy(reflectedCamera.gameObject);
            Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
        }
    }
}
