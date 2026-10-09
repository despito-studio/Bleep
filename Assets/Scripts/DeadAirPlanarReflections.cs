using UnityEngine;
using System.Collections.Generic;

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
        readonly List<Renderer> receivers = new List<Renderer>();
        readonly Plane[] receiverPlanes = new Plane[6];
        static readonly List<Light> localShadowLights = new List<Light>();
        struct SuspendedLight { public Light light; public LightShadows shadows; }
        readonly List<SuspendedLight> suspendedLights = new List<SuspendedLight>(32);
        int receiverMask = int.MinValue;
        public RenderTexture ReflectionFrame => frame;
        public bool IsRenderingReflection { get; private set; }
        public int VisibleReceiverCount { get; private set; }
        public int SuspendedLocalShadowCount => suspendedLights.Count;

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
            if(!HasVisibleReceiver(floor))
            {
                Shader.SetGlobalFloat("_DeadAirReflectionEnabled",0);
                IsRenderingReflection=false;
                return;
            }
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
            int previousPixelLights = QualitySettings.pixelLightCount;
            try
            {
                rendering = true;
                Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
                GL.invertCulling = !previousCulling;
                // Avoid a second set of local shadow maps. Keep directional moon
                // occlusion so reflected indoor walls do not glow through roofs.
                SuspendLocalShadows(reflectedCamera.cullingMask);
                QualitySettings.pixelLightCount = Mathf.Min(previousPixelLights, 4);
                reflectedCamera.Render();
            }
            finally
            {
                RestoreLocalShadows();
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

        public bool HasVisibleReceiver(float floor)
        {
            if(source==null)source=GetComponent<Camera>();
            if(source==null)return false;
            if(receiverMask!=source.cullingMask)
            {
                receivers.Clear();receiverMask=source.cullingMask;
                // Cache once per world/menu layer transition. Moving characters
                // and equipment do not add polished floor receivers.
                foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                {
                    if((receiverMask&(1<<renderer.gameObject.layer))==0)continue;
                    foreach(var material in renderer.sharedMaterials)
                        if(material!=null&&material.HasProperty("_PlanarReflection")&&material.GetFloat("_PlanarReflection")>.01f)
                        {receivers.Add(renderer);break;}
                }
                foreach(var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                    RegisterLocalShadowLight(light);
            }
            VisibleReceiverCount=0;
            GeometryUtility.CalculateFrustumPlanes(source,receiverPlanes);
            foreach(var receiver in receivers)
            {
                if(receiver==null||!receiver.enabled||!receiver.gameObject.activeInHierarchy)continue;
                var bounds=receiver.bounds;
                if(floor<bounds.min.y-.12f||floor>bounds.max.y+.12f)continue;
                if(GeometryUtility.TestPlanesAABB(receiverPlanes,bounds))VisibleReceiverCount++;
            }
            return VisibleReceiverCount>0;
        }

        public static void RegisterLocalShadowLight(Light light)
        {
            if(light==null||light.type==LightType.Directional)return;
            for(int i=localShadowLights.Count-1;i>=0;i--)
            {
                if(localShadowLights[i]==null)localShadowLights.RemoveAt(i);
                else if(localShadowLights[i]==light)return;
            }
            localShadowLights.Add(light);
        }

        void SuspendLocalShadows(int cullingMask)
        {
            suspendedLights.Clear();
            // Keep directional maps at their fixed world resolution. Resizing
            // the same moon light between cameras stalls the built-in renderer.
            foreach(var light in localShadowLights)
            {
                if(light==null||!light.isActiveAndEnabled||light.intensity<=.0001f||light.shadows==LightShadows.None||
                    (light.cullingMask&cullingMask)==0)continue;
                suspendedLights.Add(new SuspendedLight{light=light,shadows=light.shadows});
                light.shadows=LightShadows.None;
            }
        }

        void RestoreLocalShadows()
        {
            foreach(var entry in suspendedLights)if(entry.light!=null)entry.light.shadows=entry.shadows;
            suspendedLights.Clear();
        }

        void ReleaseFrame() { if (frame != null) { frame.Release(); Destroy(frame); frame = null; } }
        void OnDestroy()
        {
            RestoreLocalShadows();
            ReleaseFrame();
            if (reflectedCamera != null) Destroy(reflectedCamera.gameObject);
            Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
        }
    }
}
