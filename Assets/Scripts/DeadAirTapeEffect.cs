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

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Shaders/DeadAirTape");
                if (shader == null || !shader.isSupported) { Graphics.Blit(source, destination); return; }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var frame = SourceFrame;
            if(frame==null)
            {
                int height = Mathf.Max(240, Mathf.RoundToInt(source.height * DeadAirSettings.RenderScale));
                int width = Mathf.Max(1, Mathf.RoundToInt(height * source.width / (float)source.height));
                if (tape == null || tape.width != width || tape.height != height)
                {
                    ReleaseTexture();
                    tape = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    tape.name = "Scaled HDR source frame";
                    tape.filterMode = FilterMode.Bilinear;
                    tape.wrapMode = TextureWrapMode.Clamp;
                    tape.Create();
                }
                Graphics.Blit(source, tape);
                frame=tape;
            }
            material.SetFloat("_Clock", Time.unscaledTime);
            material.SetFloat("_Aspect", frame.width / (float)frame.height);
            material.SetFloat("_Grain", DeadAirSettings.Grain ? .035f : 0);
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
            // A small HDR glow gives practical lamps soft halos without blurring the world.
            if (bloomMaterial == null)
            {
                var shader = Resources.Load<Shader>("Shaders/DeadAirBloom");
                if (shader != null && shader.isSupported)
                    bloomMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (bloomMaterial == null || !DeadAirSettings.Bloom)
            {
                material.SetTexture("_BloomTex", Texture2D.blackTexture);
                material.SetFloat("_BloomStrength", 0);
                Graphics.Blit(frame, destination, material); return;
            }
            int bloomWidth = Mathf.Max(1, frame.width / 4), bloomHeight = Mathf.Max(1, frame.height / 4);
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            var glow = RenderTexture.GetTemporary(bloomWidth, bloomHeight, 0, format, RenderTextureReadWrite.Linear);
            var blur = RenderTexture.GetTemporary(bloomWidth, bloomHeight, 0, format, RenderTextureReadWrite.Linear);
            try
            {
                glow.filterMode = blur.filterMode = FilterMode.Bilinear;
                glow.wrapMode = blur.wrapMode = TextureWrapMode.Clamp;
                bloomMaterial.SetFloat("_Threshold", 1.1f);
                Graphics.Blit(frame, glow, bloomMaterial, 0);
                bloomMaterial.SetVector("_Direction", new Vector4(1f / bloomWidth, 0, 0, 0));
                Graphics.Blit(glow, blur, bloomMaterial, 1);
                bloomMaterial.SetVector("_Direction", new Vector4(0, 1f / bloomHeight, 0, 0));
                Graphics.Blit(blur, glow, bloomMaterial, 1);
                material.SetTexture("_BloomTex", glow);
                material.SetFloat("_BloomStrength", .16f);
                Graphics.Blit(frame, destination, material);
            }
            finally
            {
                material.SetTexture("_BloomTex", Texture2D.blackTexture);
                RenderTexture.ReleaseTemporary(glow);
                RenderTexture.ReleaseTemporary(blur);
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
