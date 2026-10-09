using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    public sealed class DeadAirWorld
    {
        readonly WorldData data;
        readonly Transform root;
        readonly Dictionary<string, Transform> interactables = new Dictionary<string, Transform>();
        readonly Dictionary<string, Transform> doorViews = new Dictionary<string, Transform>();
        readonly Dictionary<string, DeadAirHingedDoor> doorAnimations = new Dictionary<string, DeadAirHingedDoor>();
        readonly Dictionary<string, DeadAirHingedDoor> lockerAnimations = new Dictionary<string, DeadAirHingedDoor>();
        readonly HashSet<string> occupiedLockers = new HashSet<string>();
        readonly HashSet<string> currentOccupiedLockers = new HashSet<string>();
        readonly Dictionary<string, Transform> stateViews = new Dictionary<string, Transform>();
        readonly Dictionary<string, Quaternion> interactionRotations = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Renderer> doorLeds = new Dictionary<string, Renderer>();
        readonly Dictionary<string, Light> doorLights = new Dictionary<string, Light>();
        readonly Dictionary<string, byte> doorTintStates = new Dictionary<string, byte>();
        readonly List<Transform> fans = new List<Transform>();
        readonly List<Renderer> monitors = new List<Renderer>();
        readonly List<Practical> lamps = new List<Practical>();
        readonly GameObject anchor, bloodyAnchor;
        readonly DeadAirMonsterMotion anchorMotion, bloodyAnchorMotion;
        readonly Dictionary<string, GameObject> droppedViews = new Dictionary<string, GameObject>();
        readonly HashSet<string> activeDropped = new HashSet<string>();
        readonly List<string> droppedToRemove = new List<string>();
        bool lastPower, lastBroadcast, lastBreaker;
        GameObject windowDirectorGlass;
        readonly DeadAirWindowBreak directorWindow;
        readonly DeadAirDrawerPresentation drawerPresentation;
        readonly DeadAirVanPresentation vanPresentation;
        readonly Material interiorLampMaterial;
        readonly DeadAirSky sky;
        readonly DeadAirCulling culling;
        Camera renderCamera;
        sealed class Practical
        {
            public Light light; public float intensity; public Color color; public bool interior;
            public float appliedIntensity=float.NaN;
            public Color appliedColor;
        }

        public DeadAirWorld(WorldData data)
        {
            this.data = data;
            var sceneRoot=GameObject.Find("BLACKPINE / original web scene");
            root = sceneRoot!=null?sceneRoot.transform:new GameObject("BLACKPINE / original web scene").transform;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientSkyColor = new Color(.095f,.12f,.165f);
            RenderSettings.ambientEquatorColor = new Color(.045f,.052f,.067f);
            RenderSettings.ambientGroundColor = new Color(.025f,.026f,.033f);
            // No skybox is assigned, so there is no baked sky probe to refresh.
            // Supply a small real diffuse ambient term for unlit ceiling faces.
            var ambientProbe=new SphericalHarmonicsL2();
            ambientProbe.AddAmbientLight(new Color(.024f,.034f,.056f));
            RenderSettings.ambientIntensity=1;
            RenderSettings.ambientProbe=ambientProbe;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            ColorUtility.TryParseHtmlString("#101621",out var night);
            RenderSettings.fogColor = night;
            RenderSettings.fogDensity = .008f;
            // Practical lamps must shade the middle of the room, rather than only
            // the corners of the large floor meshes used by the office.
            ApplyGraphicsQuality();
            DeadAirSettings.Changed -= ApplyGraphicsQuality;
            DeadAirSettings.Changed += ApplyGraphicsQuality;
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadowCascades = 2;
            if(root.Find("station")==null) Instantiate("station").name="station";
            if(root.Find("exterior")==null) Instantiate("exterior").name="exterior";
            DiscardOriginalVanBeams(root.Find("exterior"));
            RestoreParkedCars(root.Find("exterior"),data);
            CalibratePhysicalMaterials(root.Find("station"));
            CalibratePhysicalMaterials(root.Find("exterior"));
            CalibrateExteriorLighting(root.Find("exterior"));
            StylePracticalLighting(root.Find("station"),true);
            StylePracticalLighting(root.Find("exterior"),false);
            var litShader = Shader.Find("Dead Air/Source Surface");
            if(litShader!=null)
            {
                interiorLampMaterial = new Material(litShader){name="light-panel / dynamic interior",hideFlags=HideFlags.DontSave};
                interiorLampMaterial.SetColor("_EmissionColor",Color.black);
                interiorLampMaterial.SetColor("_Color",new Color(.20f,.22f,.18f,1f));
                var stationRoot = root.Find("station");
                if(stationRoot!=null)
                {
                    foreach(var renderer in stationRoot.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = renderer.sharedMaterials;
                        bool modified = false;
                        for(int i=0;i<slots.Length;i++)
                        {
                            if(slots[i]!=null && slots[i].name.StartsWith("light-panel"))
                            {
                                slots[i] = interiorLampMaterial;
                                modified = true;
                            }
                        }
                        if(modified) renderer.sharedMaterials = slots;
                    }
                }
            }
            var dynamicRoots = new HashSet<Transform>();
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(t.name.StartsWith("interactive:"))
                {
                    string id=t.name.Substring(12);interactables[id]=t;interactionRotations[id]=t.localRotation;dynamicRoots.Add(t);
                }
                else if(t.name.StartsWith("door:"))
                {
                    string id=t.name.Substring(5);
                    doorViews[id] = t; dynamicRoots.Add(t);
                    var hinge=t.GetComponent<DeadAirHingedDoor>()??t.gameObject.AddComponent<DeadAirHingedDoor>();
                    var specification=data.Door(id);
                    hinge.Initialize(specification!=null&&Mathf.Abs(specification.openAngle)>.1f?specification.openAngle:90);
                    doorAnimations[id]=hinge;
                }
                else if(t.name.StartsWith("locker-door:"))
                {
                    string id=t.name.Substring(12);dynamicRoots.Add(t);
                    var hinge=t.GetComponent<DeadAirHingedDoor>()??t.gameObject.AddComponent<DeadAirHingedDoor>();
                    hinge.Initialize(-105);lockerAnimations[id]=hinge;
                }
                else if(t.name.StartsWith("door-led:")) doorLeds[t.name.Substring(9)] = t.GetComponent<Renderer>();
                else if(t.name.StartsWith("door-light:")) doorLights[t.name.Substring(11)] = t.GetComponent<Light>();
                else if(t.name.StartsWith("state:")) { stateViews[t.name.Substring(6)] = t; dynamicRoots.Add(t); }
                else if(t.name == "window-director-glass") { windowDirectorGlass = t.gameObject; dynamicRoots.Add(t); }
                else if(t.name.StartsWith("fan:")) { fans.Add(t); dynamicRoots.Add(t); }
                else if(t.name.StartsWith("monitor:")) monitors.Add(t.GetComponent<Renderer>());
                else if(t.name.StartsWith("lamp:"))
                {
                    var l=t.GetComponent<Light>();
                    if(l!=null)lamps.Add(new Practical {light=l,intensity=l.intensity,color=l.color,
                        interior=t.IsChildOf(root.Find("station"))});
                }
            }
            drawerPresentation = new DeadAirDrawerPresentation(root, data);
            vanPresentation=DeadAirVanPresentation.Attach(root,data);
            if (interactables.TryGetValue("key-main", out var directorKey))
                foreach (var part in root.GetComponentsInChildren<Transform>(true))
                    if (part.name == "drawer:drawer-director")
                    {
                        directorKey.SetParent(part, true);
                        break;
                    }
            var pane = stateViews.TryGetValue("directorWindowPane", out var breakablePane) ? breakablePane.gameObject : windowDirectorGlass;
            var brokenFrame = stateViews.TryGetValue("directorWindowBroken", out var bFrame) ? bFrame.gameObject : null;
            directorWindow = new DeadAirWindowBreak(pane, brokenFrame);
            sky = new DeadAirSky(root);
            foreach(var worldLight in root.GetComponentsInChildren<Light>(true))
            {
                worldLight.cullingMask &= ~((1 << 28) | (1 << 29) | (1 << 30));
                DeadAirPlanarReflections.RegisterLocalShadowLight(worldLight);
            }
            var moon=root.Find("Dead Air Night Sky & Moon/Directional moonlight")?.GetComponent<Light>();
            if(moon!=null)
            {
                moon.shadows=LightShadows.Soft;
                moon.shadowCustomResolution=1024;
            }
            if(vanPresentation.VehicleRoot!=null)dynamicRoots.Add(vanPresentation.VehicleRoot);
            culling = new DeadAirCulling(data,root.Find("station"),root.Find("exterior"),dynamicRoots,doorAnimations);
            anchor=Instantiate("monster");bloodyAnchor=Instantiate("monster_bloody");
            anchorMotion=anchor.AddComponent<DeadAirMonsterMotion>();
            bloodyAnchorMotion=bloodyAnchor.AddComponent<DeadAirMonsterMotion>();
            anchorMotion.Initialize();bloodyAnchorMotion.Initialize();
            anchor.SetActive(false);bloodyAnchor.SetActive(false);
        }

        static bool IsMonitorMaterial(Material material)
        {
            // The export contains CRT canvases as flat, unlit screen surfaces.
            // They are the only source canvases that should emit their image.
            if(!material.name.StartsWith("surface_"))return false;
            if(!int.TryParse(material.name.Substring(8),out int id))return false;
            return id>=298&&id<=308 || id>=328&&id<=463 || id>=496&&id<=513;
        }

        static bool IsLampMaterial(Material material)
        {
            if(material.name=="light-panel")return true;
            if(material.name.StartsWith("van-headlamp-lens",StringComparison.Ordinal))return true;
            // Headlamps, indicators and tail lamps on the saved service van.
            return material.name=="surface_677" || material.name=="surface_678" ||
                material.name=="surface_679" || material.name=="surface_684" ||
                material.name=="surface_685" || material.name=="surface_696";
        }

        static void CalibratePhysicalMaterials(Transform content)
        {
            if(content==null)return;
            var litShader=Shader.Find("Dead Air/Source Surface");
            if(litShader==null)throw new InvalidOperationException("The lit world surface shader is missing.");
            var copies=new Dictionary<Material,Material>();
            foreach(var renderer in content.GetComponentsInChildren<Renderer>(true))
            {
                var slots=renderer.sharedMaterials;
                bool changed=false;
                for(int i=0;i<slots.Length;i++)
                {
                    var source=slots[i];if(source==null)continue;
                    string shaderName=source.shader.name;
                    if(shaderName!="Dead Air/Source Surface" && shaderName!="Dead Air/Source Unlit")continue;
                    // Procedural car materials already belong to this world and
                    // are calibrated when created; copying loses their ownership.
                    if(source.name.StartsWith("Parked car /")&&(source.hideFlags&HideFlags.DontSave)!=0)continue;
                    if(IsLampMaterial(source))continue;
                    if(!copies.TryGetValue(source,out var material))
                    {
                        material=new Material(source) {name=source.name+" / lit",hideFlags=HideFlags.DontSave};
                        material.shader=litShader;
                        bool monitor=IsMonitorMaterial(source);
                        material.SetColor("_EmissionColor",monitor?new Color(.12f,.16f,.13f):Color.black);
                        // Labels converted from the unlit export need a matte
                        // response. Preserve the authored finish on physical props.
                        if(shaderName=="Dead Air/Source Unlit")
                        {
                            material.SetFloat("_Glossiness",monitor?.32f:.16f);
                            material.SetFloat("_Metallic",0);
                        }
                        copies.Add(source,material);
                    }
                    slots[i]=material;changed=true;
                }
                if(changed)renderer.sharedMaterials=slots;
            }
        }

        static void CalibrateExteriorLighting(Transform exterior)
        {
            if(exterior==null)return;
            foreach(var light in exterior.GetComponentsInChildren<Light>(true))
            {
                if(light.type==LightType.Spot && light.range>25)
                {
                    // An older export used one broad spotlight between both lenses.
                    // The pair below replaces it with emitters on the actual glass.
                    light.enabled=false;
                }
                else if(light.type==LightType.Point && light.range>25)
                {
                    light.intensity=73f*.045f*.45f;
                    light.renderMode = LightRenderMode.Auto;
                }
            }
            foreach(var transform in exterior.GetComponentsInChildren<Transform>(true))
            {
                if(!transform.name.StartsWith("prop:van-") && transform.name!="van" &&
                    !transform.name.StartsWith("Broadcast service van"))continue;
                InstallVanHeadlamps(transform.Find("vehicle:crew-van")??transform);
            }
        }

        static void StylePracticalLighting(Transform content,bool interior)
        {
            if(content==null)return;
            foreach(var light in content.GetComponentsInChildren<Light>(true))
            {
                if(!light.name.StartsWith("lamp:"))continue;
                if(light.name.StartsWith("lamp:van-headlamp-"))continue;
                if(interior && light.name.StartsWith("lamp:zone-")) light.intensity *= .7f;
                else if(interior && light.name.StartsWith("lamp:bounce-zone-")) light.intensity *= .8f;
                if(interior && light.name.StartsWith("lamp:zone-"))
                {
                    // Equipment rooms retain a cold tube hue; the occupied
                    // offices get warm practical pools against blue night shadows.
                    bool equipment=light.name.Contains("СЕРВЕРНА") || light.name.Contains("АПАРАТНА") ||
                        light.name.Contains("МОНТАЖНА") || light.name.Contains("САНВУЗОЛ");
                    bool passage=light.name.Contains("ХОЛ") || light.name.Contains("ГАЛЕРЕЯ") ||
                        light.name.Contains("ПРОХІД");
                    light.color=equipment?new Color(.88f,.94f,1f):
                        passage?new Color(1f,.85f,.64f):new Color(1f,.82f,.58f);
                }
                else if(!interior && light.type!=LightType.Directional)
                    light.color=new Color(1f,.87f,.67f);
                if(light.shadows==LightShadows.None)continue;
                light.shadows=LightShadows.Soft;
                light.shadowBias=.018f;
                light.shadowNormalBias=.035f;
            }
        }

        static void ApplyGraphicsQuality()
        {
            QualitySettings.pixelLightCount = DeadAirSettings.GraphicsQuality == 0 ? 6 : 10;
            QualitySettings.shadows = DeadAirSettings.ShadowQuality == 0 ? ShadowQuality.Disable : ShadowQuality.All;
            QualitySettings.shadowResolution = DeadAirSettings.ShadowQuality == 2 ? ShadowResolution.High : ShadowResolution.Medium;
            QualitySettings.shadowDistance = DeadAirSettings.ShadowQuality == 2 ? 30 : 20;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        }

        static void DiscardOriginalVanBeams(Transform exterior)
        {
            if(exterior==null)return;
            foreach(var renderer in exterior.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                    if(material!=null && material.name.Split(' ')[0]=="surface_697")
                    {
                        // Compatibility for a saved scene built before the old
                        // translucent browser cones were removed from the prefab.
                        renderer.gameObject.SetActive(false);break;
                    }
        }

        // The old furniture cache extracted only a hubcap from the browser's
        // batched car. Its three orphaned disks survived in the saved exterior,
        // while the bodies never reached the car prefab. Build complete cars at
        // the authored prop transforms, including a matching physical footprint.
        public static void RestoreParkedCars(Transform exterior,WorldData world)
        {
            if(exterior==null || world?.props==null)return;
            foreach(var renderer in exterior.GetComponentsInChildren<Renderer>(true))
                if(renderer.name=="Batched surfaces / surface_524")renderer.enabled=false;
            var transforms=exterior.GetComponentsInChildren<Transform>(true);
            bool needsRepair=false;
            foreach(var prop in world.props)
            {
                if(prop.type!="abandonedCar")continue;
                Transform parked=null;
                foreach(var candidate in transforms)if(candidate.name=="prop:"+prop.id){parked=candidate;break;}
                if(parked==null||parked.Find("Parked sedan")==null){needsRepair=true;break;}
            }
            if(!needsRepair)return;
            var shader=Shader.Find("Dead Air/Source Surface");
            if(shader==null)throw new InvalidOperationException("The parked-car surface shader is missing.");
            var rubber=VehicleMaterial(shader,"Parked car / rubber",new Color(.045f,.049f,.046f));
            var glass=VehicleMaterial(shader,"Parked car / dark glass",new Color(.085f,.14f,.17f));
            var trim=VehicleMaterial(shader,"Parked car / aged metal",new Color(.32f,.34f,.31f));
            var lens=VehicleMaterial(shader,"Parked car / unpowered headlamp",new Color(.67f,.64f,.48f));
            var tail=VehicleMaterial(shader,"Parked car / unpowered tail lamp",new Color(.36f,.07f,.055f));
            var cube=VehicleCubeMesh();
            var wheel=VehicleWheelMesh();
            int index=0;
            foreach(var prop in world.props)
            {
                if(prop.type!="abandonedCar")continue;
                Transform parked=null;
                foreach(var candidate in transforms)
                    if(candidate.name=="prop:"+prop.id){parked=candidate;break;}
                if(parked==null)
                {
                    parked=new GameObject("prop:"+prop.id).transform;parked.SetParent(exterior,false);
                    parked.localPosition=DeadAirOfficeLayout.Position(prop.x,prop.y,prop.z);
                    parked.localRotation=DeadAirOfficeLayout.Rotation(prop.rotation);
                }
                if(parked.Find("Parked sedan")!=null)continue;
                foreach(var renderer in parked.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
                foreach(var collider in parked.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                var sedan=new GameObject("Parked sedan").transform;sedan.SetParent(parked,false);
                float w=prop.w>0?prop.w:2.1f,d=prop.d>0?prop.d:4.2f;
                var color=index%3==0?new Color(.36f,.43f,.39f):index%3==1?new Color(.38f,.42f,.49f):new Color(.46f,.33f,.28f);
                if(!string.IsNullOrEmpty(prop.color)&&ColorUtility.TryParseHtmlString(prop.color,out var authored))color=authored;
                var paint=VehicleMaterial(shader,"Parked car / weathered paint "+index,color);index++;
                VehiclePart(sedan,"Sedan body",VehicleHullMesh(w,d),paint,Vector3.zero,Vector3.one);
                VehiclePart(sedan,"Passenger cabin",VehicleCabinMesh(w,d),paint,Vector3.zero,Vector3.one);
                var windowVertices=new List<Vector3>();
                for(int side=-1;side<=1;side+=2)
                {
                    float lower=side*w*.443f,upper=side*w*.370f;
                    var a=new Vector3(lower,.99f,-d*.217f);var b=new Vector3(upper,1.43f,-d*.103f);
                    var c=new Vector3(upper,1.43f,d*.031f);var e=new Vector3(lower,.99f,d*.031f);
                    if(side>0)VehicleFace(windowVertices,a,b,c,e);else VehicleFace(windowVertices,e,c,b,a);
                    a=new Vector3(lower,.99f,d*.049f);b=new Vector3(upper,1.43f,d*.049f);
                    c=new Vector3(upper,1.43f,d*.161f);e=new Vector3(lower,.99f,d*.258f);
                    if(side>0)VehicleFace(windowVertices,a,b,c,e);else VehicleFace(windowVertices,e,c,b,a);
                }
                VehicleFace(windowVertices,new Vector3(-w*.412f,.995f,-d*.233f),new Vector3(-w*.348f,1.43f,-d*.133f),
                    new Vector3(w*.348f,1.43f,-d*.133f),new Vector3(w*.412f,.995f,-d*.233f));
                VehicleFace(windowVertices,new Vector3(w*.412f,.995f,d*.283f),new Vector3(w*.348f,1.43f,d*.193f),
                    new Vector3(-w*.348f,1.43f,d*.193f),new Vector3(-w*.412f,.995f,d*.283f));
                // Glass is opaque with physical outward normals, so it never becomes floating
                // additive dots or reveals the empty cabin through the body.
                VehiclePart(sedan,"Six dark windows",VehicleMesh("Sedan windows",windowVertices),glass,Vector3.zero,Vector3.one);
                for(int side=-1;side<=1;side+=2)
                {
                    for(int axle=-1;axle<=1;axle+=2)
                    {
                        var position=new Vector3(side*w*.445f,.34f,axle*d*.31f);
                        VehiclePart(sedan,"Wheel "+side+" / "+axle,wheel,rubber,position,new Vector3(.27f,.34f,.34f));
                        VehiclePart(sedan,"Wheel hub "+side+" / "+axle,wheel,trim,position+new Vector3(side*.145f,0,0),new Vector3(.022f,.18f,.18f));
                    }
                    VehiclePart(sedan,"Side sill "+side,cube,trim,new Vector3(side*w*.475f,.47f,0),new Vector3(.05f,.08f,d*.77f));
                    VehiclePart(sedan,"Door handle "+side,cube,trim,new Vector3(side*w*.455f,.96f,d*.04f),new Vector3(.028f,.04f,.17f));
                    VehiclePart(sedan,"Wing mirror "+side,cube,paint,new Vector3(side*w*.485f,1.01f,-d*.18f),new Vector3(.16f,.13f,.22f));
                    VehiclePart(sedan,"Rectangular headlamp "+side,cube,lens,new Vector3(side*w*.305f,.76f,-d*.479f),new Vector3(w*.18f,.15f,.04f));
                    VehiclePart(sedan,"Rectangular tail lamp "+side,cube,tail,new Vector3(side*w*.315f,.78f,d*.478f),new Vector3(w*.17f,.14f,.04f));
                }
                foreach(int end in new[]{-1,1})
                    VehiclePart(sedan,"Bumper "+end,cube,trim,new Vector3(0,.54f,end*d*.48f),new Vector3(w*.90f,.12f,.15f));
                VehiclePart(sedan,"Front grille",cube,rubber,new Vector3(0,.74f,-d*.482f),new Vector3(w*.34f,.16f,.044f));
                VehiclePart(sedan,"Number plate",cube,lens,new Vector3(0,.575f,-d*.501f),new Vector3(.43f,.105f,.024f));
                var bodyCollider=sedan.gameObject.AddComponent<BoxCollider>();
                bodyCollider.center=new Vector3(0,.77f,0);bodyCollider.size=new Vector3(w,1.48f,d);
            }
        }

        static Material VehicleMaterial(Shader shader,string name,Color color)
        {
            var material=new Material(shader){name=name,hideFlags=HideFlags.DontSave,enableInstancing=true};
            material.SetColor("_Color",color);material.SetColor("_EmissionColor",Color.black);
            bool glass=name.Contains("glass"),metal=name.Contains("metal");
            material.SetFloat("_Glossiness",glass?.48f:metal?.32f:.12f);
            material.SetFloat("_Metallic",metal?.65f:0);
            return material;
        }

        static void VehiclePart(Transform parent,string name,Mesh mesh,Material material,Vector3 position,Vector3 scale)
        {
            var part=new GameObject(name);part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;
            part.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
        }

        static void VehicleFace(List<Vector3> vertices,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);}

        static Mesh VehicleMesh(string name,List<Vector3> vertices)
        {
            var indices=new int[vertices.Count/4*6];
            for(int face=0;face<vertices.Count/4;face++)
            {
                int v=face*4,t=face*6;indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+2;
                indices[t+3]=v;indices[t+4]=v+2;indices[t+5]=v+3;
            }
            var mesh=new Mesh(){name=name,hideFlags=HideFlags.DontSave};
            mesh.SetVertices(vertices);mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        static Mesh VehicleCubeMesh()
        {
            var vertices=new List<Vector3>();
            VehicleFace(vertices,new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,-.5f,-.5f));
            VehicleFace(vertices,new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,-.5f,.5f));
            VehicleFace(vertices,new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,-.5f));
            VehicleFace(vertices,new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,-.5f,.5f));
            VehicleFace(vertices,new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,.5f,-.5f));
            VehicleFace(vertices,new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f));
            return VehicleMesh("Sedan box detail",vertices);
        }

        static Mesh VehicleHullMesh(float w,float d)
        {
            var vertices=new List<Vector3>();
            var z=new[]{-d*.475f,-d*.35f,d*.35f,d*.475f};
            var widths=new[]{w*.43f,w*.48f,w*.48f,w*.43f};
            var heights=new[]{.87f,.96f,.96f,.88f};
            for(int i=0;i<3;i++)
            {
                VehicleFace(vertices,new Vector3(-widths[i],heights[i],z[i]),new Vector3(-widths[i+1],heights[i+1],z[i+1]),
                    new Vector3(widths[i+1],heights[i+1],z[i+1]),new Vector3(widths[i],heights[i],z[i]));
                foreach(int side in new[]{-1,1})
                {
                    var a=new Vector3(side*widths[i],.48f,z[i]);var b=new Vector3(side*widths[i],heights[i],z[i]);
                    var c=new Vector3(side*widths[i+1],heights[i+1],z[i+1]);var e=new Vector3(side*widths[i+1],.48f,z[i+1]);
                    if(side==1)VehicleFace(vertices,a,b,c,e);else VehicleFace(vertices,e,c,b,a);
                }
            }
            VehicleFace(vertices,new Vector3(-widths[0],.48f,z[0]),new Vector3(-widths[0],heights[0],z[0]),new Vector3(widths[0],heights[0],z[0]),new Vector3(widths[0],.48f,z[0]));
            VehicleFace(vertices,new Vector3(widths[3],.48f,z[3]),new Vector3(widths[3],heights[3],z[3]),new Vector3(-widths[3],heights[3],z[3]),new Vector3(-widths[3],.48f,z[3]));
            VehicleFace(vertices,new Vector3(-widths[0],.48f,z[0]),new Vector3(widths[0],.48f,z[0]),new Vector3(widths[3],.48f,z[3]),new Vector3(-widths[3],.48f,z[3]));
            return VehicleMesh("Low polygon sedan body",vertices);
        }

        static Mesh VehicleCabinMesh(float w,float d)
        {
            var vertices=new List<Vector3>();
            var a=new Vector3(-w*.44f,.95f,-d*.24f);var b=new Vector3(w*.44f,.95f,-d*.24f);
            var c=new Vector3(w*.44f,.95f,d*.29f);var e=new Vector3(-w*.44f,.95f,d*.29f);
            var f=new Vector3(-w*.36f,1.48f,-d*.12f);var g=new Vector3(w*.36f,1.48f,-d*.12f);
            var h=new Vector3(w*.36f,1.48f,d*.18f);var j=new Vector3(-w*.36f,1.48f,d*.18f);
            VehicleFace(vertices,a,f,g,b);VehicleFace(vertices,b,g,h,c);VehicleFace(vertices,c,h,j,e);
            VehicleFace(vertices,e,j,f,a);VehicleFace(vertices,f,j,h,g);VehicleFace(vertices,e,a,b,c);
            return VehicleMesh("Sloped sedan cabin",vertices);
        }

        static Mesh VehicleWheelMesh()
        {
            var vertices=new List<Vector3>();const int sides=10;
            for(int i=0;i<sides;i++)
            {
                // A half-segment offset places vertices exactly at the top and
                // bottom of a ten-sided tire, preserving its authored radius.
                float a=(i+.5f)*Mathf.PI*2/sides,b=(i+1.5f)*Mathf.PI*2/sides;
                var p=new Vector3(0,Mathf.Sin(a),Mathf.Cos(a));var q=new Vector3(0,Mathf.Sin(b),Mathf.Cos(b));
                VehicleFace(vertices,p-Vector3.right*.5f,p+Vector3.right*.5f,q+Vector3.right*.5f,q-Vector3.right*.5f);
                VehicleFace(vertices,-Vector3.right*.5f,p-Vector3.right*.5f,q-Vector3.right*.5f,-Vector3.right*.5f);
                VehicleFace(vertices,Vector3.right*.5f,q+Vector3.right*.5f,p+Vector3.right*.5f,Vector3.right*.5f);
            }
            return VehicleMesh("Ten sided sedan wheel",vertices);
        }

        public static void InstallVanHeadlamps(Transform van)
        {
            int count=0;
            foreach(var lens in van.GetComponentsInChildren<Renderer>(true))
            {
                bool headlamp=false;
                foreach(var material in lens.sharedMaterials)
                    if(material!=null && (material.name=="surface_677"||material.name.StartsWith("van-headlamp-lens",StringComparison.Ordinal))){headlamp=true;break;}
                if(!headlamp)continue;
                var local=van.InverseTransformPoint(lens.bounds.center);
                float side=Mathf.Sign(local.x);
                string name=side<0?"lamp:van-headlamp-left":"lamp:van-headlamp-right";
                var emitter=van.Find(name);
                if(emitter==null){emitter=new GameObject(name).transform;emitter.SetParent(van,false);}
                // Start ahead of the outer glass, so the housing cannot shadow its
                // own beam. The upper edge stays below the horizon.
                bool modern=van.name=="vehicle:crew-van"||lens.name.StartsWith("headlamp-lens:");
                float forward=modern?1f:-1f;
                emitter.localPosition=new Vector3(local.x,local.y,local.z+forward*.065f);
                emitter.localRotation=Quaternion.LookRotation(new Vector3(side*.035f,-.30f,forward));
                var light=emitter.GetComponent<Light>();
                if(light==null)light=emitter.gameObject.AddComponent<Light>();
                light.type=LightType.Spot;light.range=18;light.intensity=3.6f;
                light.spotAngle=36;light.innerSpotAngle=23;
                light.color=new Color(1,.87f,.66f);
            light.renderMode=LightRenderMode.ForcePixel;
                light.shadows=LightShadows.Soft;light.shadowCustomResolution=512;
                light.shadowBias=.012f;light.shadowNormalBias=.025f;light.shadowNearPlane=.06f;
                var block=new MaterialPropertyBlock();lens.GetPropertyBlock(block);
                block.SetColor("_EmissionColor",new Color(.62f,.48f,.28f));
                block.SetColor("_Color",new Color(.76f,.68f,.5f));
                lens.SetPropertyBlock(block);
                count++;
            }
            if(count!=2)Debug.LogWarning("Expected two physical service-van headlamp lenses; found "+count+".");
        }

        GameObject Instantiate(string id)
        {
            var prefab=Resources.Load<GameObject>("World/"+id);
            if(prefab==null)throw new InvalidOperationException("Missing textured scene asset: "+id+". Run Dead Air / Import Complete Web Scene.");
            return UnityEngine.Object.Instantiate(prefab,root,false);
        }

        public void Apply(RoomState state)
        {
            if(state==null) return;
            vanPresentation.Apply(state);
            foreach(var door in state.doors)
            {
                if(doorAnimations.TryGetValue(door.id,out var animation))animation.SetOpen(door.open, door.openSign);
                byte tintState=door.locked?(byte)0:door.open?(byte)1:(byte)2;
                if(!doorTintStates.TryGetValue(door.id,out var previousTint)||previousTint!=tintState)
                {
                    var color=door.locked?new Color(.88f,.27f,.20f):door.open?new Color(.31f,.78f,.47f):new Color(.84f,.74f,.47f);
                    if(doorLeds.TryGetValue(door.id,out var led)&&led!=null) SetTint(led,color);
                    if(doorLights.TryGetValue(door.id,out var light)&&light!=null)light.color=color;
                    doorTintStates[door.id]=tintState;
                }
            }
            float animationDelta=state.paused?0:Time.deltaTime;
            foreach(var animation in doorAnimations.Values)animation.Advance(animationDelta);
            currentOccupiedLockers.Clear();
            foreach(var actor in state.players)
                if(!string.IsNullOrEmpty(actor.hiding))currentOccupiedLockers.Add(actor.hiding);
            foreach(var entry in lockerAnimations)
            {
                bool occupied=currentOccupiedLockers.Contains(entry.Key);
                if(occupied!=occupiedLockers.Contains(entry.Key))
                {
                    entry.Value.PlayAccessCycle();
                    if(occupied)occupiedLockers.Add(entry.Key);else occupiedLockers.Remove(entry.Key);
                }
                entry.Value.Advance(animationDelta);
            }
            foreach(var obj in data.objects)
            {
                if(!interactables.TryGetValue(obj.id,out var view))continue;
                if(obj.type=="tape"||obj.type=="fuse"||obj.type=="keycard"||obj.type=="key"||obj.type=="ladder"||obj.type=="canister")view.gameObject.SetActive(!state.Collected(obj.id));
                if(obj.id=="pressure-valve")view.localRotation=interactionRotations[obj.id]*Quaternion.Euler(0,0,state.valveOpened?180:0);
            }
            bool flicker=DeadAirSettings.Flicker;
            bool interiorPowered=state.power && state.breakerOn;
            for(int i=0;i<lamps.Count;i++)
            {
                var lamp=lamps[i];
                Color color=state.broadcast&&lamp.interior?new Color(1,.36f,.19f):lamp.color;
                if(float.IsNaN(lamp.appliedIntensity)||lamp.appliedColor!=color)
                { lamp.light.color=color;lamp.appliedColor=color; }
                if(lamp.interior&&!interiorPowered)
                {
                    if(lamp.appliedIntensity!=0){lamp.light.intensity=0;lamp.appliedIntensity=0;}
                    continue;
                }
                // An invisible fixture need not evaluate two flicker waves. Its
                // full output stays ready when culling exposes it again.
                float time=Time.time+i*3.77f;
                float wave=flicker&&lamp.light.enabled?Mathf.Sin(time*1.7f)*Mathf.Sin(time*13.6f):0;
                float flutter=wave>.94f?.72f:1f;
                if(state.broadcast&&lamp.interior)flutter*=flicker?.86f+Mathf.Sin(Time.time*3)*.08f:.9f;
                float intensity=lamp.intensity*flutter;
                if(lamp.appliedIntensity!=intensity)
                { lamp.light.intensity=intensity;lamp.appliedIntensity=intensity; }
            }
            if(state.power && state.breakerOn)
                foreach(var fan in fans)fan.Rotate(0,0,-Time.deltaTime*14*Mathf.Rad2Deg,Space.Self);
            if(stateViews.TryGetValue("powerLever",out var lever))lever.localRotation=Quaternion.Euler(0,0,-(state.power?-.7f:.55f)*Mathf.Rad2Deg);
            if(stateViews.TryGetValue("breakerHandle",out var breakerH))breakerH.localRotation=Quaternion.Euler(-(state.breakerOn?45f:-45f),0,0);
            drawerPresentation.Apply(state, animationDelta);
            if(stateViews.TryGetValue("placedLadder",out var ladderView))ladderView.gameObject.SetActive(state.ladderPlaced);
            directorWindow.Apply(state.windowBroken, animationDelta);
            if(lastPower!=state.power||lastBroadcast!=state.broadcast||lastBreaker!=state.breakerOn)
            {
                TintState("powerLamp",state.power?new Color(.53f,.78f,.53f):new Color(.88f,.32f,.17f));
                TintState("breakerLamp",(state.power&&state.breakerOn)?new Color(.53f,.78f,.53f):state.power?new Color(.88f,.62f,.22f):new Color(.15f,.15f,.15f));
                TintState("broadcastBulb",state.broadcast?new Color(.87f,.13f,.067f):new Color(.58f,.27f,.16f));
                TintState("exitLamp",state.broadcast?new Color(.2f,1,.33f):new Color(.87f,.2f,.13f));
                foreach(var monitor in monitors)SetTint(monitor,(state.power&&state.breakerOn)?Color.white:new Color(.39f,.47f,.41f));
                bool lit = state.power && state.breakerOn;
                if(interiorLampMaterial!=null)
                {
                    Color emission = lit ? (state.broadcast ? new Color(1.5f, .42f, .18f) : new Color(1.8f, 1.27f, .64f)) : Color.black;
                    Color albedo = lit ? (state.broadcast ? new Color(1f, .55f, .45f, 1f) : new Color(.92f, .83f, .65f, 1f)) : new Color(.20f, .22f, .18f, 1f);
                    interiorLampMaterial.SetColor("_EmissionColor", emission);
                    interiorLampMaterial.SetColor("_Color", albedo);
                }
                lastPower=state.power;lastBroadcast=state.broadcast;lastBreaker=state.breakerOn;
            }
            bool enemyVisible=state.enemy.active&&state.enemy.behaviour!="vanished";
            anchor.SetActive(enemyVisible&&!state.broadcast);
            bloodyAnchor.SetActive(enemyVisible&&state.broadcast);
            if(enemyVisible)
            {
                var motion=state.broadcast?bloodyAnchorMotion:anchorMotion;
                motion.Present(state.enemy,state.elapsed,state.paused);
            }
            ApplyDropped(state);
            var cam=renderCamera!=null?renderCamera:Camera.main;
            if(cam==null)
            {
                var player=UnityEngine.Object.FindAnyObjectByType<DeadAirPlayer>();
                if(player!=null)cam=player.ViewCamera;
            }
            if(cam!=null)
            {
                renderCamera=cam;
                sky?.UpdatePosition(cam.transform.position);
                culling?.Update(state,cam);
            }
        }

        // Manual GPU captures disable automatic camera rendering. Their lighting
        // and sky must still use the camera that will actually render the frame.
        public void PrepareRender(Camera camera)
        {
            if(camera==null)return;
            renderCamera=camera;
            sky?.UpdatePosition(camera.transform.position);
            culling?.Update(null,camera,true);
        }

        void ApplyDropped(RoomState state)
        {
            activeDropped.Clear();
            if (state.droppedItems != null)
            {
                for (int i = 0; i < state.droppedItems.Count; i++)
                {
                    var item = state.droppedItems[i];
                    activeDropped.Add(item.id);
                    if (!droppedViews.TryGetValue(item.id, out var view) || view == null)
                    {
                        view = CreateDroppedView(item);
                        droppedViews[item.id] = view;
                    }
                    if (view != null)
                    {
                        view.transform.position = new Vector3(-item.x, item.y + 0.15f, item.z);
                    }
                }
            }
            droppedToRemove.Clear();
            foreach (var id in droppedViews.Keys)
            {
                if (!activeDropped.Contains(id)) droppedToRemove.Add(id);
            }
            for (int i = 0; i < droppedToRemove.Count; i++)
            {
                var id = droppedToRemove[i];
                if (droppedViews.TryGetValue(id, out var view) && view != null)
                {
                    UnityEngine.Object.Destroy(view);
                }
                droppedViews.Remove(id);
            }
        }

        GameObject CreateDroppedView(DroppedItem item)
        {
            string type = string.IsNullOrEmpty(item.itemId) ? item.id : item.itemId;
            GameObject view = null;
            if (type == "flashlight")
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                view.name = "dropped_flashlight_" + item.id;
                view.transform.SetParent(root, false);
                view.transform.localScale = new Vector3(0.08f, 0.16f, 0.08f);
                view.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 90);
                var ren = view.GetComponent<Renderer>();
                if (ren != null) ren.material.color = new Color(0.18f, 0.18f, 0.18f);
                var lightObj = new GameObject("dropped_light");
                lightObj.transform.SetParent(view.transform, false);
                var l = lightObj.AddComponent<Light>();
                l.enabled = false;
                lightObj.transform.localPosition=new Vector3(0,1.05f,0);
                lightObj.transform.localRotation=Quaternion.Euler(-90,0,0);
                l.type = LightType.Spot;
                l.range = 15;
                l.cullingMask = ~((1 << 28) | (1 << 29) | (1 << 30));
                l.spotAngle=48;l.innerSpotAngle=23;
                l.intensity = 1.55f;
                l.shadows=LightShadows.Soft;l.shadowBias=.015f;l.shadowNormalBias=.025f;
                l.color = new Color(1f, .96f, .88f);
                DeadAirPlanarReflections.RegisterLocalShadowLight(l);
            }
            else if (type == DeadAirSupplies.BatteryItem)
            {
                view = DeadAirDrawerPresentation.CreateBattery(root);
                view.name = "dropped_battery_" + item.id;
                view.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            else if (interactables.TryGetValue(type, out var orig) && orig != null)
            {
                view = UnityEngine.Object.Instantiate(orig.gameObject, root, false);
                view.name = "dropped_" + type + "_" + item.id;
                view.SetActive(true);
            }
            else
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                view.name = "dropped_prop_" + item.id;
                view.transform.SetParent(root, false);
                view.transform.localScale = new Vector3(0.25f, 0.12f, 0.25f);
                var ren = view.GetComponent<Renderer>();
                if (ren != null) ren.material.color = type.StartsWith("keycard") ? new Color(0.3f, 0.7f, 1f) : new Color(0.8f, 0.8f, 0.3f);
            }
            foreach (var col in view.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(col);
            return view;
        }
        static readonly MaterialPropertyBlock sharedBlock = new MaterialPropertyBlock();
        static void SetTint(Renderer renderer,Color color)
        {
            if(renderer==null)return;
            renderer.GetPropertyBlock(sharedBlock);
            sharedBlock.SetColor("_Color",color);
            renderer.SetPropertyBlock(sharedBlock);
        }
        void TintState(string id,Color color)
        {
            if(!stateViews.TryGetValue(id,out var view))return;
            var renderer=view.GetComponent<Renderer>();if(renderer==null)return;
            renderer.GetPropertyBlock(sharedBlock);
            sharedBlock.SetColor("_Color",color);
            sharedBlock.SetColor("_EmissionColor",color);
            renderer.SetPropertyBlock(sharedBlock);
        }
    }
}
