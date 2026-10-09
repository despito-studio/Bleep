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
            if (DeadAirSettings.AmbientOcclusion) view.depthTextureMode |= DepthTextureMode.DepthNormals;
            else view.depthTextureMode &= ~DepthTextureMode.DepthNormals;
        }
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!DeadAirSettings.AmbientOcclusion) { Graphics.Blit(source, destination); return; }
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Shaders/DeadAirAmbientOcclusion");
                if (shader == null || !shader.isSupported) { Graphics.Blit(source, destination); return; }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            material.SetMatrix("_InverseProjection", view.projectionMatrix.inverse);
            material.SetVector("_CameraProjectionScale", new Vector4(Mathf.Abs(view.projectionMatrix.m00), Mathf.Abs(view.projectionMatrix.m11), 0, 0));
            material.SetFloat("_Radius", .55f);
            var ao = RenderTexture.GetTemporary(Mathf.Max(1, source.width / 2), Mathf.Max(1, source.height / 2), 0, RenderTextureFormat.RHalf);
            ao.filterMode = FilterMode.Bilinear;
            try
            {
                Graphics.Blit(source, ao, material, 0);
                material.SetTexture("_OcclusionTex", ao);
                Graphics.Blit(source, destination, material, 1);
            }
            finally { material.SetTexture("_OcclusionTex", null); RenderTexture.ReleaseTemporary(ao); }
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
