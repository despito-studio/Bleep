using UnityEngine;

namespace DeadAir
{
    // Dynamic font atlases can be rebuilt when Cyrillic and Latin glyphs are requested.
    // Keep their current texture on a world-space depth-tested material without mutating an asset.
    // Fit the generated glyph geometry, including its bearings, to the physical plaque.
    [ExecuteAlways, RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public sealed class DeadAirOfficeSign : MonoBehaviour
    {
        [SerializeField] Font font;
        [SerializeField] Material worldMaterial;
        [SerializeField] Vector2 plaqueSize;
        [SerializeField] float letteringDepth = .018f;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock properties;
        Texture atlasTexture;
        int pendingFitFrames;
        static readonly int MainTexture = Shader.PropertyToID("_MainTex");

        public void Initialize(Font sourceFont, Material material, float width, float height)
        {
            font=sourceFont;
            worldMaterial=material;
            plaqueSize=new Vector2(width,height);
            letteringDepth=transform.localPosition.z;
            RefreshAtlas();
            FitToPlaque();
        }

        void OnEnable()
        {
            Font.textureRebuilt+=OnAtlasRebuilt;
            RefreshAtlas();
            FitToPlaque();
        }

        void OnDisable() { Font.textureRebuilt-=OnAtlasRebuilt; }
        void OnAtlasRebuilt(Font changed) { if(changed==font) { RefreshAtlas(); FitToPlaque(); } }
        void OnValidate() { RefreshAtlas(); FitToPlaque(); }

        // Native glyph generation can finish after the atlas callback.
        void LateUpdate()
        {
            if(!Application.isPlaying||pendingFitFrames>0)
                if(FitGeometry()&&pendingFitFrames>0)pendingFitFrames--;
        }

        public bool FitToPlaque()
        {
            pendingFitFrames=2;
            return FitGeometry();
        }

        bool FitGeometry()
        {
            if(plaqueSize.x<=0||plaqueSize.y<=0)return false;
            if(meshRenderer==null)meshRenderer=GetComponent<MeshRenderer>();
            if(meshRenderer==null)return false;
            var bounds=meshRenderer.localBounds;
            if(bounds.size.x<=.00001f||bounds.size.y<=.00001f)return false;
            float scale=Mathf.Min(plaqueSize.x*.90f/bounds.size.x,plaqueSize.y*.80f/bounds.size.y);
            if(float.IsNaN(scale)||float.IsInfinity(scale)||scale<=0)return false;
            var fittedScale=Vector3.one*scale;
            // MiddleCenter anchors the font's line metrics, not necessarily the visible ink.
            // Center the actual glyphs so ascenders/descenders and bearings cannot escape one edge.
            var centerOffset=transform.localRotation*(bounds.center*scale);
            var fittedPosition=new Vector3(-centerOffset.x,-centerOffset.y,letteringDepth);
            if((transform.localScale-fittedScale).sqrMagnitude>1e-12f)transform.localScale=fittedScale;
            if((transform.localPosition-fittedPosition).sqrMagnitude>1e-12f)transform.localPosition=fittedPosition;
            return true;
        }

        public bool IsContained(float tolerance=.001f)
        {
            if(plaqueSize.x<=0||plaqueSize.y<=0||meshRenderer==null)return false;
            var bounds=meshRenderer.localBounds;
            if(bounds.size.x<=.00001f||bounds.size.y<=.00001f)return false;
            var half=plaqueSize*new Vector2(.45f,.40f);
            for(int ix=0;ix<2;ix++)
                for(int iy=0;iy<2;iy++)
                {
                    float x=ix==0?bounds.min.x:bounds.max.x,y=iy==0?bounds.min.y:bounds.max.y;
                    var point=transform.localPosition+transform.localRotation*Vector3.Scale(new Vector3(x,y,bounds.center.z),transform.localScale);
                    if(Mathf.Abs(point.x)>half.x+tolerance||Mathf.Abs(point.y)>half.y+tolerance)return false;
                }
            return true;
        }

        void RefreshAtlas()
        {
            if(font==null||worldMaterial==null)return;
            if(meshRenderer==null)meshRenderer=GetComponent<MeshRenderer>();
            if(meshRenderer==null)return;
            if(meshRenderer.sharedMaterial!=worldMaterial)meshRenderer.sharedMaterial=worldMaterial;
            var texture=font.material.mainTexture;
            if(properties!=null&&atlasTexture==texture)return;
            if(properties==null)properties=new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(properties);
            properties.SetTexture(MainTexture,texture);atlasTexture=texture;
            meshRenderer.SetPropertyBlock(properties);
        }
    }
}
