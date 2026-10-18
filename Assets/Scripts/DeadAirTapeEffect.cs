using UnityEngine;

namespace DeadAir
{
    [RequireComponent(typeof(Camera))]
    public sealed class DeadAirTapeEffect : MonoBehaviour
    {
        public float Threat, Insanity, Nightmare, Flash;
        public float Trip, TripClock;
        public bool Recording;
        public float Exposure = .85f;
        public RenderTexture SourceFrame;
        Material material;
        Material bloomMaterial;
        RenderTexture tape;


		private void OnRenderImage(RenderTexture source, RenderTexture destination)
		{
			if (material == null)
			{
				Shader shader = Resources.Load<Shader>("Shaders/DeadAirTape");
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
			RenderTexture sourceFrame = SourceFrame;
			if (sourceFrame == null)
			{
				int num = Mathf.Max(240, Mathf.RoundToInt((float)source.height * DeadAirSettings.RenderScale));
				int num2 = Mathf.Max(1, Mathf.RoundToInt((float)(num * source.width) / (float)source.height));
				if (tape == null || tape.width != num2 || tape.height != num)
				{
					ReleaseTexture();
					tape = new RenderTexture(num2, num, 0, DeadAirFrameRenderer.HdrColorFormat, RenderTextureReadWrite.Linear);
					tape.name = "Scaled HDR source frame";
					tape.filterMode = FilterMode.Bilinear;
					tape.wrapMode = TextureWrapMode.Clamp;
					tape.Create();
				}
				Graphics.Blit(source, tape);
				sourceFrame = tape;
			}
			material.SetFloat("_Clock", Time.unscaledTime);
			material.SetFloat("_Aspect", (float)sourceFrame.width / (float)sourceFrame.height);
			material.SetFloat("_Grain", DeadAirSettings.Grain ? 0.035f : 0f);
			material.SetFloat("_Threat", Mathf.Clamp01(Threat));
			material.SetFloat("_Insanity", Mathf.Clamp01(Insanity));
			material.SetFloat("_Nightmare", Mathf.Clamp01(Nightmare));
			material.SetFloat("_Recording", Recording ? 1 : 0);
			material.SetFloat("_Flicker", DeadAirSettings.Flicker ? 1 : 0);
			material.SetFloat("_Flash", Flash);
			material.SetFloat("_Exposure", Exposure * DeadAirSettings.Brightness);
			material.SetFloat("_Antialiasing", DeadAirSettings.Antialiasing ? 1 : 0);
			material.SetFloat("_Trip", Mathf.Clamp01(Trip));
			material.SetFloat("_TripClock", TripClock);
			if (bloomMaterial == null)
			{
				Shader shader2 = Resources.Load<Shader>("Shaders/DeadAirBloom");
				if (shader2 != null && shader2.isSupported)
				{
					bloomMaterial = new Material(shader2)
					{
						hideFlags = HideFlags.HideAndDontSave
					};
				}
			}
			if (bloomMaterial == null || !DeadAirSettings.Bloom)
			{
				material.SetTexture("_BloomTex", Texture2D.blackTexture);
				material.SetFloat("_BloomStrength", 0f);
				Graphics.Blit(sourceFrame, destination, material);
				return;
			}
			int num3 = Mathf.Max(1, sourceFrame.width / 4);
			int num4 = Mathf.Max(1, sourceFrame.height / 4);
			RenderTextureFormat hdrColorFormat = DeadAirFrameRenderer.HdrColorFormat;
			RenderTexture temporary = RenderTexture.GetTemporary(num3, num4, 0, hdrColorFormat, RenderTextureReadWrite.Linear);
			RenderTexture temporary2 = RenderTexture.GetTemporary(num3, num4, 0, hdrColorFormat, RenderTextureReadWrite.Linear);
			try
			{
				FilterMode filterMode = (temporary2.filterMode = FilterMode.Bilinear);
				temporary.filterMode = filterMode;
				TextureWrapMode wrapMode = (temporary2.wrapMode = TextureWrapMode.Clamp);
				temporary.wrapMode = wrapMode;
				bloomMaterial.SetFloat("_Threshold", 1.1f);
				Graphics.Blit(sourceFrame, temporary, bloomMaterial, 0);
				bloomMaterial.SetVector("_Direction", new Vector4(1f / (float)num3, 0f, 0f, 0f));
				Graphics.Blit(temporary, temporary2, bloomMaterial, 1);
				bloomMaterial.SetVector("_Direction", new Vector4(0f, 1f / (float)num4, 0f, 0f));
				Graphics.Blit(temporary2, temporary, bloomMaterial, 1);
				material.SetTexture("_BloomTex", temporary);
				material.SetFloat("_BloomStrength", 0.16f);
				Graphics.Blit(sourceFrame, destination, material);
			}
			finally
			{
				material.SetTexture("_BloomTex", Texture2D.blackTexture);
				RenderTexture.ReleaseTemporary(temporary);
				RenderTexture.ReleaseTemporary(temporary2);
			}
		}


        void ReleaseTexture()
        {
            if (tape == null) return;
            tape.Release();
            if (Application.isPlaying) Destroy(tape); else DestroyImmediate(tape);
            tape = null;
        }

        void OnDisable()
        {
            ReleaseTexture();
            if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); material = null; }
            if (bloomMaterial != null) { if (Application.isPlaying) Destroy(bloomMaterial); else DestroyImmediate(bloomMaterial); bloomMaterial = null; }
        }
    }
}
