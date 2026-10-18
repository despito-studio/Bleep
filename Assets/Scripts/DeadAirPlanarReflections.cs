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


		private void OnPreCull()
		{
			if (rendering || source == null)
			{
				return;
			}
			if (!DeadAirSettings.Reflections)
			{
				Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0f);
				IsRenderingReflection = false;
				return;
			}
			float num = ((source.transform.position.y > 4.6f) ? 4.2f : ((source.transform.position.y < -1f) ? (-4.2f) : 0f));
			if (!HasVisibleReceiver(num))
			{
				Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0f);
				IsRenderingReflection = false;
				return;
			}
			int graphicsQuality = DeadAirSettings.GraphicsQuality;
			int num2 = graphicsQuality switch
			{
				1 => 2, 
				2 => 1, 
				_ => 3, 
			};
			float num3 = graphicsQuality switch
			{
				1 => 0.4f, 
				2 => 0.5f, 
				_ => 0.33f, 
			};
			int num4 = Mathf.Max(192, Mathf.RoundToInt((float)((source.targetTexture != null) ? source.targetTexture.width : Screen.width) * num3));
			int num5 = Mathf.Max(108, Mathf.RoundToInt((float)((source.targetTexture != null) ? source.targetTexture.height : Screen.height) * num3));
			if (!IsRenderingReflection || !(frame != null) || frame.width != num4 || frame.height != num5 || lastFloor != num || lastMask != source.cullingMask || Time.frameCount - lastFrame >= num2)
			{
				if (frame == null || frame.width != num4 || frame.height != num5)
				{
					ReleaseFrame();
					frame = new RenderTexture(num4, num5, 16, DeadAirFrameRenderer.HdrColorFormat, RenderTextureReadWrite.Linear)
					{
						name = "Dead Air polished floor reflection",
						filterMode = FilterMode.Bilinear,
						wrapMode = TextureWrapMode.Clamp,
						hideFlags = HideFlags.DontSave
					};
					frame.Create();
				}
				if (reflectedCamera == null)
				{
					GameObject gameObject = new GameObject("Dead Air mirrored floor camera")
					{
						hideFlags = HideFlags.DontSave
					};
					reflectedCamera = gameObject.AddComponent<Camera>();
				}
				reflectedCamera.CopyFrom(source);
				reflectedCamera.enabled = false;
				reflectedCamera.depthTextureMode = DepthTextureMode.None;
				reflectedCamera.useOcclusionCulling = false;
				reflectedCamera.allowMSAA = false;
				reflectedCamera.targetTexture = frame;
				reflectedCamera.cullingMask = source.cullingMask & -1073741825;
				reflectedCamera.clearFlags = CameraClearFlags.Color;
				reflectedCamera.backgroundColor = new Color(0.012f, 0.019f, 0.034f);
				Matrix4x4 identity = Matrix4x4.identity;
				identity.m11 = -1f;
				identity.m13 = 2f * num;
				reflectedCamera.transform.position = identity.MultiplyPoint(source.transform.position);
				reflectedCamera.transform.rotation = Quaternion.Euler(0f - source.transform.eulerAngles.x, source.transform.eulerAngles.y, source.transform.eulerAngles.z);
				reflectedCamera.worldToCameraMatrix = source.worldToCameraMatrix * identity;
				Vector3 lhs = reflectedCamera.worldToCameraMatrix.MultiplyPoint(new Vector3(0f, num + 0.025f, 0f));
				Vector3 normalized = reflectedCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
				Vector4 clipPlane = new Vector4(normalized.x, normalized.y, normalized.z, 0f - Vector3.Dot(lhs, normalized));
				reflectedCamera.projectionMatrix = source.CalculateObliqueMatrix(clipPlane);
				bool invertCulling = GL.invertCulling;
				int pixelLightCount = QualitySettings.pixelLightCount;
				try
				{
					rendering = true;
					Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0f);
					GL.invertCulling = !invertCulling;
					SuspendLocalShadows(reflectedCamera.cullingMask);
					QualitySettings.pixelLightCount = Mathf.Min(pixelLightCount, 4);
					reflectedCamera.Render();
				}
				finally
				{
					RestoreLocalShadows();
					QualitySettings.pixelLightCount = pixelLightCount;
					GL.invertCulling = invertCulling;
					rendering = false;
				}
				Shader.SetGlobalTexture("_DeadAirReflectionTex", frame);
				Shader.SetGlobalVector("_DeadAirReflectionTex_TexelSize", new Vector4(1f / (float)num4, 1f / (float)num5, num4, num5));
				Shader.SetGlobalMatrix("_DeadAirReflectionVP", GL.GetGPUProjectionMatrix(reflectedCamera.projectionMatrix, renderIntoTexture: true) * reflectedCamera.worldToCameraMatrix);
				Shader.SetGlobalFloat("_DeadAirReflectionFloor", num);
				Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 1f);
				lastFrame = Time.frameCount;
				lastFloor = num;
				lastMask = source.cullingMask;
				IsRenderingReflection = true;
			}
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
