using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // An authored office set isolated from all match objects, lights and interactions.
    public sealed class DeadAirMenuBackdrop : MonoBehaviour
    {
        public const int PresentationLayer=28;
        public static Vector3 PresentationOrigin=>new Vector3(1500,0,1000);
        public static Vector3 OperativePosition=>PresentationOrigin+new Vector3(-1.05f,0,-.9f);
        public static Quaternion OperativeRotation=>Quaternion.Euler(0,23,0);
        public bool IsVisible=>presentation!=null&&presentation.activeSelf;
        GameObject presentation;
        Camera framedCamera;
        int previousMask;
        CameraClearFlags previousClear;
        Color previousBackground;
        float previousFar;
        bool previousOcclusion;
        readonly List<Material> materials=new List<Material>();
        readonly List<Mesh> meshes=new List<Mesh>();
        Material wall,floor,trim,wood,steel,paper,window,lamp;
        Shader surfaceShader;
        SphericalHarmonicsL2 menuProbe;
        bool environmentOverridden;
        AmbientMode previousAmbientMode;
        Color previousAmbient,previousSky,previousEquator,previousGround;
        float previousAmbientIntensity,previousReflectionIntensity;
        SphericalHarmonicsL2 previousProbe;
        bool previousFog;

        void OnEnable(){Camera.onPreCull+=BeforeCamera;Camera.onPostRender+=AfterCamera;}
        public void SetVisible(bool visible)
        {
            if(visible&&presentation==null)CreatePresentation();
            if(presentation!=null&&presentation.activeSelf!=visible)presentation.SetActive(visible);
            if(!visible)RestoreCamera();
        }
        public void Frame(Camera camera,Transform actor)
        {
            SetVisible(true);
            if(framedCamera!=camera)
            {
                RestoreCamera();framedCamera=camera;
                previousMask=camera.cullingMask;previousClear=camera.clearFlags;
                previousBackground=camera.backgroundColor;previousFar=camera.farClipPlane;previousOcclusion=camera.useOcclusionCulling;
                camera.fieldOfView=58;camera.farClipPlane=80;
                camera.cullingMask=(1<<PresentationLayer)|(1<<29);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.033f,.041f);camera.useOcclusionCulling=false;
            }
            // Canonical composition and constant lamps make every menu entry identical.
            var actorPosition=PresentationOrigin+new Vector3(.6f,0,3);
            if(actor.position!=actorPosition||actor.rotation!=Quaternion.identity)actor.SetPositionAndRotation(actorPosition,Quaternion.identity);
            var view=camera.transform;
            if(view.localPosition!=new Vector3(0,1.6f,0))view.localPosition=new Vector3(0,1.6f,0);
            var rotation=Quaternion.LookRotation(PresentationOrigin+new Vector3(.45f,1.48f,-23)-view.position);
            if(view.rotation!=rotation)view.rotation=rotation;
        }
        void RestoreCamera()
        {
            RestoreEnvironment();if(framedCamera==null)return;
            framedCamera.cullingMask=previousMask;framedCamera.clearFlags=previousClear;framedCamera.backgroundColor=previousBackground;
            framedCamera.farClipPlane=previousFar;framedCamera.useOcclusionCulling=previousOcclusion;framedCamera=null;
        }
        void BeforeCamera(Camera camera)
        {
            bool reflection=camera.name=="Dead Air mirrored floor camera"&&(camera.cullingMask&(1<<PresentationLayer))!=0;
            if((camera!=framedCamera&&!reflection)||!IsVisible)return;
            // The mirrored camera can render before the main camera callback. Snapshot the
            // live environment only once and keep the canonical set through both renders.
            if(!environmentOverridden)
            {
                previousAmbientMode=RenderSettings.ambientMode;previousAmbient=RenderSettings.ambientLight;
                previousSky=RenderSettings.ambientSkyColor;previousEquator=RenderSettings.ambientEquatorColor;previousGround=RenderSettings.ambientGroundColor;
                previousAmbientIntensity=RenderSettings.ambientIntensity;previousReflectionIntensity=RenderSettings.reflectionIntensity;previousFog=RenderSettings.fog;
                previousProbe=RenderSettings.ambientProbe;environmentOverridden=true;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.19f,.20f,.22f);
            RenderSettings.ambientIntensity=.5f;RenderSettings.reflectionIntensity=.25f;RenderSettings.fog=false;
            RenderSettings.ambientProbe=menuProbe;
        }
        void AfterCamera(Camera camera){if(camera==framedCamera)RestoreEnvironment();}
        void RestoreEnvironment()
        {
            if(!environmentOverridden)return;
            RenderSettings.ambientMode=previousAmbientMode;RenderSettings.ambientLight=previousAmbient;
            RenderSettings.ambientSkyColor=previousSky;RenderSettings.ambientEquatorColor=previousEquator;RenderSettings.ambientGroundColor=previousGround;
            RenderSettings.ambientIntensity=previousAmbientIntensity;RenderSettings.reflectionIntensity=previousReflectionIntensity;RenderSettings.fog=previousFog;
            RenderSettings.ambientProbe=previousProbe;
            environmentOverridden=false;
        }
        void CreatePresentation()
        {
            surfaceShader=Shader.Find("Dead Air/Source Surface")??Shader.Find("Standard");
            menuProbe=new SphericalHarmonicsL2();menuProbe.AddAmbientLight(new Color(.095f,.10f,.11f));
            presentation=new GameObject("Menu only / quiet Blackpine archive");presentation.transform.position=PresentationOrigin;presentation.layer=PresentationLayer;
            wall=Surface("Warm plaster",new Color(.43f,.44f,.37f),.12f);floor=Surface("Office tiles",new Color(.23f,.25f,.22f),.58f);
            if(floor.HasProperty("_PlanarReflection"))floor.SetFloat("_PlanarReflection",1);
            trim=Surface("Painted trim",new Color(.12f,.16f,.14f),.2f);wood=Surface("Dark oak",new Color(.24f,.13f,.065f),.16f);
            steel=Surface("Archive steel",new Color(.11f,.14f,.13f),.22f);paper=Surface("Old files",new Color(.52f,.46f,.32f),.04f);
            window=Surface("Night window",new Color(.025f,.055f,.095f),.18f,new Color(.04f,.08f,.14f));
            lamp=Surface("Warm lens",new Color(.94f,.79f,.49f),.1f,new Color(2.2f,1.65f,.8f));
            Block("Floor",new Vector3(0,-.1f,-10),new Vector3(8,.2f,30),floor);
            Block("Ceiling",new Vector3(0,3.45f,-10),new Vector3(8,.12f,30),wall);
            foreach(float x in new[]{-4f,4f})
            {
                Block("Plaster wall",new Vector3(x,1.7f,-10),new Vector3(.16f,3.4f,30),wall);
                Block("Skirting",new Vector3(x*.985f,.13f,-10),new Vector3(.08f,.26f,30),trim);
                Block("Chair rail",new Vector3(x*.983f,1.06f,-10),new Vector3(.09f,.05f,30),wood);
            }
            Block("End wall",new Vector3(0,1.7f,-24.5f),new Vector3(8,3.4f,.16f),wall);
            for(int z=-23;z<5;z+=2)
            {
                Block("Ceiling joint",new Vector3(0,3.36f,z),new Vector3(8,.025f,.025f),trim);
                Block("Floor joint",new Vector3(0,.002f,z),new Vector3(8,.004f,.018f),trim);
            }
            foreach(float x in new[]{-2f,0f,2f})
            {
                Block("Ceiling runner",new Vector3(x,3.36f,-10),new Vector3(.025f,.025f,30),trim);
                Block("Floor runner",new Vector3(x,.002f,-10),new Vector3(.018f,.004f,30),trim);
            }
            foreach(float z in new[]{-4f,-10f,-16f,-22f})
            {
                Block("Lamp housing",new Vector3(0,3.29f,z),new Vector3(.72f,.13f,1.3f),steel);
                Block("Lamp diffuser",new Vector3(0,3.215f,z),new Vector3(.56f,.025f,1.13f),lamp);
                AddLight("Warm practical",new Vector3(0,3.04f,z),new Color(1,.78f,.46f),1.2f,8);
            }
            foreach(float z in new[]{-3.6f,-8.7f,-13.8f})
            {
                Block("Night window",new Vector3(-3.895f,2.03f,z),new Vector3(.026f,1.62f,3.45f),window);
                foreach(float dz in new[]{-1.75f,0f,1.75f})Block("Window upright",new Vector3(-3.85f,2.03f,z+dz),new Vector3(.12f,1.77f,.07f),trim);
                foreach(float y in new[]{1.18f,2.03f,2.9f})Block("Window crossbar",new Vector3(-3.85f,y,z),new Vector3(.12f,.07f,3.58f),trim);
                Block("Window sill",new Vector3(-3.73f,1.12f,z),new Vector3(.4f,.1f,3.68f),paper);
            }
            Shelf(new Vector3(-2.9f,0,-8.2f));Shelf(new Vector3(-2.9f,0,-12.1f));Shelf(new Vector3(2.9f,0,-15.2f));Desk(new Vector3(2.25f,0,-6.7f));
            Block("Archive door",new Vector3(.65f,1.36f,-24.37f),new Vector3(1.6f,2.72f,.12f),wood);
            foreach(float x in new[]{-.20f,1.50f})Block("Door casing",new Vector3(x,1.4f,-24.25f),new Vector3(.12f,2.8f,.15f),trim);
            Block("Door head",new Vector3(.65f,2.83f,-24.25f),new Vector3(1.83f,.12f,.15f),trim);
            AddLight("Night fill",new Vector3(-3.15f,2.4f,-5.8f),new Color(.43f,.57f,.74f),.5f,7);
            AddLight("Entrance fill",new Vector3(1.8f,2.4f,2),new Color(1,.82f,.62f),.7f,7);
            BatchPresentation();
        }
        void Shelf(Vector3 position)
        {
            foreach(float dx in new[]{-.6f,.6f})Block("Shelf upright",position+new Vector3(dx,1.16f,0),new Vector3(.055f,2.32f,.48f),steel);
            for(int row=0;row<5;row++)
            {
                float y=.12f+row*.46f;Block("Shelf board",position+new Vector3(0,y,0),new Vector3(1.25f,.04f,.48f),steel);
                for(int file=0;file<7;file++)
                {
                    var p=position+new Vector3(-.49f+file*.16f,y+.19f,.035f);
                    Block("File box",p,new Vector3(.145f,.34f,.36f),file%3==0?trim:paper);
                    Block("File label",p+new Vector3(0,.01f,.188f),new Vector3(.055f,.09f,.006f),paper);
                }
            }
        }
        void Desk(Vector3 position)
        {
            Block("Desk top",position+new Vector3(0,.8f,0),new Vector3(2.15f,.09f,1.05f),wood);
            Block("Desk pedestal",position+new Vector3(.68f,.39f,0),new Vector3(.55f,.74f,.9f),trim);
            foreach(float z in new[]{-.42f,.42f})Block("Desk leg",position+new Vector3(-.82f,.39f,z),new Vector3(.07f,.74f,.07f),steel);
            Block("CRT casing",position+new Vector3(-.15f,1.12f,-.15f),new Vector3(.58f,.53f,.49f),paper);
            Block("CRT screen",position+new Vector3(-.15f,1.13f,.105f),new Vector3(.45f,.34f,.015f),trim);
            Block("Keyboard",position+new Vector3(-.14f,.87f,.33f),new Vector3(.56f,.04f,.17f),paper);
            Block("Desk paperwork",position+new Vector3(.67f,.86f,.32f),new Vector3(.27f,.022f,.3f),paper);
        }
        Material Surface(string name,Color color,float gloss,Color? emission=null)
        {
            var material=new Material(surfaceShader){name="Menu only / "+name,color=color,hideFlags=HideFlags.DontSave};
            material.SetFloat("_Glossiness",gloss);material.SetFloat("_Metallic",0);
            if(emission.HasValue){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",emission.Value);}
            materials.Add(material);return material;
        }
        void BatchPresentation()
        {
            var sources=presentation.GetComponentsInChildren<MeshRenderer>();
            var groups=new Dictionary<Material,List<CombineInstance>>();
            foreach(var renderer in sources)
            {
                var filter=renderer.GetComponent<MeshFilter>();
                if(filter==null||filter.sharedMesh==null)continue;
                if(!groups.TryGetValue(renderer.sharedMaterial,out var instances))
                {instances=new List<CombineInstance>();groups.Add(renderer.sharedMaterial,instances);}
                instances.Add(new CombineInstance{mesh=filter.sharedMesh,transform=presentation.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix});
            }
            foreach(var group in groups)
            {
                var mesh=new Mesh{name="Menu set / "+group.Key.name,hideFlags=HideFlags.DontSave};
                mesh.CombineMeshes(group.Value.ToArray(),true,true);meshes.Add(mesh);
                var surface=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer));surface.layer=PresentationLayer;
                surface.transform.SetParent(presentation.transform,false);surface.GetComponent<MeshFilter>().sharedMesh=mesh;
                surface.GetComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
            foreach(var renderer in sources){renderer.enabled=false;Destroy(renderer.gameObject);}
        }
        void Block(string name,Vector3 position,Vector3 size,Material material)
        {
            var block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.name="Menu only / "+name;
            block.transform.SetParent(presentation.transform,false);block.transform.localPosition=position;block.transform.localScale=size;block.layer=PresentationLayer;
            block.GetComponent<MeshRenderer>().sharedMaterial=material;Destroy(block.GetComponent<Collider>());
        }
        void AddLight(string name,Vector3 position,Color color,float intensity,float range)
        {
            var light=new GameObject("Menu only / "+name).AddComponent<Light>();light.transform.SetParent(presentation.transform,false);light.transform.localPosition=position;
            light.gameObject.layer=PresentationLayer;light.type=LightType.Point;light.color=color;light.intensity=intensity;light.range=range;
            light.cullingMask=1<<PresentationLayer;light.shadows=LightShadows.Soft;light.shadowStrength=.65f;light.shadowBias=.025f;light.shadowNormalBias=.08f;light.renderMode=LightRenderMode.ForcePixel;
        }
        void OnDisable(){Camera.onPreCull-=BeforeCamera;Camera.onPostRender-=AfterCamera;SetVisible(false);}
        void OnDestroy(){RestoreCamera();if(presentation!=null)Destroy(presentation);foreach(var material in materials)if(material!=null)Destroy(material);foreach(var mesh in meshes)if(mesh!=null)Destroy(mesh);}
    }
}
