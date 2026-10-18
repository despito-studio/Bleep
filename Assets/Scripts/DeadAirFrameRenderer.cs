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
            DeadAirSettings.Changed += ApplyReflectionSetting;
            ApplyReflectionSetting();
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

        static void ApplyReflectionSetting()
        {
            Shader.SetGlobalFloat("_DeadAirReflectionsDisabled", DeadAirSettings.Reflections ? 0 : 1);
            if (!DeadAirSettings.Reflections) Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
        }


		public void EnsureTargets()
		{
			if (worldCamera == null || OutputCamera == null)
			{
				return;
			}
			RenderingPath renderingPath = (((worldCamera.cullingMask & 0x10000000) != 0 || DeadAirSettings.GraphicsQuality <= 0 || SystemInfo.supportedRenderTargetCount < 4) ? RenderingPath.Forward : RenderingPath.DeferredShading);
			if (worldCamera.renderingPath != renderingPath)
			{
				worldCamera.renderingPath = renderingPath;
			}
			int num = Mathf.Max(1, Screen.width);
			int num2 = Mathf.Max(1, Screen.height);
			float num3 = (float)num / (float)num2;
			int num4 = Mathf.Max(240, Mathf.RoundToInt((float)num2 * DeadAirSettings.RenderScale));
			int num5 = Mathf.Max(1, Mathf.RoundToInt((float)num4 * num3));
			if (SceneFrame == null || SceneFrame.width != num5 || SceneFrame.height != num4)
			{
				RenderTexture renderTexture = new RenderTexture(num5, num4, 24, HdrColorFormat, RenderTextureReadWrite.Linear)
				{
					name = "DEAD AIR scene frame",
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					antiAliasing = 1,
					useMipMap = false,
					autoGenerateMips = false,
					hideFlags = HideFlags.DontSave
				};
				if (!renderTexture.Create())
				{
					DestroyTexture(renderTexture);
					throw new InvalidOperationException("Unable to allocate the DEAD AIR scene frame.");
				}
				RenderTexture sceneFrame = SceneFrame;
				SceneFrame = renderTexture;
				worldCamera.targetTexture = SceneFrame;
				tape.SourceFrame = SceneFrame;
				DestroyTexture(sceneFrame);
			}
			else if (!SceneFrame.IsCreated())
			{
				SceneFrame.Create();
			}
			worldCamera.targetTexture = SceneFrame;
			worldCamera.aspect = num3;
			if (equipment != null)
			{
				equipment.ConfigureRenderer(SceneFrame);
			}
			OutputCamera.aspect = num3;
			OutputCamera.depth = worldCamera.depth + 1f;
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
            DeadAirSettings.Changed -= ApplyReflectionSetting;
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
    

		internal static RenderTextureFormat HdrColorFormat
		{
			get
			{
				if (!hdrFormatSelected)
				{
					hdrFormat = (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32));
					hdrFormatSelected = true;
				}
				return hdrFormat;
			}
		}



		private static RenderTextureFormat hdrFormat;



		private static bool hdrFormatSelected;

}
}
