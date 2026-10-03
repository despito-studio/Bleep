using UnityEngine;

namespace DeadAir
{
    // Dynamic font atlases can be rebuilt when Cyrillic and Latin glyphs are requested.
    // Keep their current texture on a world-space depth-tested material without mutating an asset.
    [ExecuteAlways, RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public sealed class DeadAirOfficeSign : MonoBehaviour
    {
        [SerializeField] Font font;
        [SerializeField] Material worldMaterial;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock properties;
        static readonly int MainTexture = Shader.PropertyToID("_MainTex");

        public void Initialize(Font sourceFont, Material material)
        {
            font=sourceFont;
            worldMaterial=material;
            RefreshAtlas();
        }

        void OnEnable()
        {
            Font.textureRebuilt+=OnAtlasRebuilt;
            RefreshAtlas();
        }

        void OnDisable() { Font.textureRebuilt-=OnAtlasRebuilt; }
        void OnAtlasRebuilt(Font changed) { if(changed==font) RefreshAtlas(); }
        void OnValidate() { RefreshAtlas(); }

        void RefreshAtlas()
        {
            if(font==null||worldMaterial==null)return;
            if(meshRenderer==null)meshRenderer=GetComponent<MeshRenderer>();
            if(meshRenderer==null)return;
            meshRenderer.sharedMaterial=worldMaterial;
            if(properties==null)properties=new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetTexture(MainTexture,font.material.mainTexture);
            meshRenderer.SetPropertyBlock(properties);
        }
    }
}
