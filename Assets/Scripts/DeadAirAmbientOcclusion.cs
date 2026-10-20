using UnityEngine;

namespace DeadAir
{
    [RequireComponent(typeof(Camera))]
    public sealed class DeadAirAmbientOcclusion : MonoBehaviour
    {
        Material material;
        Camera view;
        public bool IsActive => DeadAirSettings.AmbientOcclusion && material != null;
        void Awake() { view = GetComponent<Camera>(); }
        void OnPreCull()
        {
            // Depth alone is enough: the occlusion shader rebuilds normals from
            // it. Depth is free in the deferred path, while DepthNormals would add
            // a second full geometry pass for every opaque object.
            if (DeadAirSettings.AmbientOcclusion) view.depthTextureMode |= DepthTextureMode.Depth;
            else view.depthTextureMode &= ~DepthTextureMode.Depth;
        }


		private void OnRenderImage(RenderTexture source, RenderTexture destination)
		{
			if (!DeadAirSettings.AmbientOcclusion)
			{
				Graphics.Blit(source, destination);
				return;
			}
			if (material == null)
			{
				Shader shader = Resources.Load<Shader>("Shaders/DeadAirAmbientOcclusion");
				if (shader == null || !shader.isSupported)
				{
					Graphics.Blit(source, destination);
					return;
				}
				material = new Material(shader)
				{
					hideFlags = HideFlags.HideAndDontSave
				};
			}
			material.SetMatrix("_InverseProjection", view.projectionMatrix.inverse);
			material.SetVector("_CameraProjectionScale", new Vector4(Mathf.Abs(view.projectionMatrix.m00), Mathf.Abs(view.projectionMatrix.m11), 0f, 0f));
			material.SetFloat("_Radius", 0.55f);
			int num = DeadAirSettings.GraphicsQuality switch
			{
				1 => 3, 
				2 => 2, 
				_ => 4, 
			};
			RenderTexture temporary = RenderTexture.GetTemporary(Mathf.Max(1, source.width / num), Mathf.Max(1, source.height / num), 0, RenderTextureFormat.RHalf);
			temporary.filterMode = FilterMode.Bilinear;
			try
			{
				Graphics.Blit(source, temporary, material, 0);
				material.SetTexture("_OcclusionTex", temporary);
				Graphics.Blit(source, destination, material, 1);
			}
			finally
			{
				material.SetTexture("_OcclusionTex", null);
				RenderTexture.ReleaseTemporary(temporary);
			}
		}

        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
