using UnityEngine;

namespace DeadAir
{
    [RequireComponent(typeof(Camera))]
    public sealed class DeadAirTapeEffect : MonoBehaviour
    {
        public float Threat, Insanity, Nightmare, Flash;
        public bool Recording;
        public float Exposure = 1.3f;
        public RenderTexture SourceFrame;
        Material material;
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
                int height = Mathf.Clamp(DeadAirSettings.Quality, 240, source.height);
                int width = Mathf.Max(1, Mathf.RoundToInt(height * source.width / (float)source.height));
                if (tape == null || tape.width != width || tape.height != height)
                {
                    ReleaseTexture();
                    tape = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    tape.name = "VHS source frame";
                    tape.filterMode = FilterMode.Point;
                    tape.wrapMode = TextureWrapMode.Clamp;
                    tape.Create();
                }
                Graphics.Blit(source, tape);
                frame=tape;
            }
            material.SetFloat("_Clock", Time.unscaledTime);
            material.SetFloat("_Aspect", frame.width / (float)frame.height);
            material.SetFloat("_Grain", DeadAirSettings.Grain ? .035f : 0);
            material.SetFloat("_Scanlines", DeadAirSettings.Grain ? .075f : 0);
            material.SetFloat("_Threat", Mathf.Clamp01(Threat));
            material.SetFloat("_Insanity", Mathf.Clamp01(Insanity));
            material.SetFloat("_Nightmare", Mathf.Clamp01(Nightmare));
            material.SetFloat("_Recording", Recording ? 1 : 0);
            material.SetFloat("_Flicker", DeadAirSettings.Flicker ? 1 : 0);
            material.SetFloat("_Flash", Flash);
            material.SetFloat("_Exposure", Exposure);
            Graphics.Blit(frame, destination, material);
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
            if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        }
    }
}
