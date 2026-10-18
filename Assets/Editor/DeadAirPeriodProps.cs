using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Restores the part transforms omitted by the standalone OBJ exporter and supplies period office equipment.</summary>
    public static class DeadAirPeriodProps
    {
        const string Models = "Assets/Resources/Models/";
        const string MeshFolder = "Assets/Office/Meshes";
        const string MaterialFolder = "Assets/Office/Materials";
        static readonly Dictionary<string, SourcePart[]> sources = new Dictionary<string, SourcePart[]>();
        static readonly Dictionary<string, Material> sourceMaterials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material> furnitureMaterials = new Dictionary<string, Material>();
        sealed class SourcePart { public Mesh mesh; public string material; }
        sealed class ObjPart
        {
            public string material;
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }
        sealed class Context
        {
            public Transform root;
            public Func<Transform,string,Vector3,Vector3,Material,bool,GameObject> block;
            public Action<Transform,string,Vector3,Quaternion,float,float> sign;
            public Material wood,trim,metal,paper,black,screen;
            public GameObject Box(string name, Vector3 position, Vector3 size, Material material, bool collide=false)
                => block(root,name,position,size,material,collide);
            public void Sign(string text,Vector3 position,float width,float height)
                => sign(root,text,position,Quaternion.identity,width,height);
            public GameObject Rounded(string name,Vector3 position,Vector3 size,Material material,float radius=.035f,bool collide=false,float bevel=.012f)
                =>DeadAirFurnitureMeshes.Install(root,name,DeadAirFurnitureMeshes.RoundedBox(size,radius,bevel),position,material,collide);
            public GameObject Turned(string name,Vector3 position,float[] heights,float[] radii,Material material,int sides=12)
                =>DeadAirFurnitureMeshes.Install(root,name,DeadAirFurnitureMeshes.Turned(heights,radii,sides),position,material);
        }

        public static bool TryBuild(Transform root, PropData p,
            Func<Transform,string,Vector3,Vector3,Material,bool,GameObject> block,
            Action<Transform,string,Vector3,Quaternion,float,float> sign,
            Material wood,Material trim,Material metal,Material paper,Material black,Material darkScreen)
        {
            var c=new Context {root=root,block=block,sign=sign,wood=wood,trim=trim,metal=metal,paper=paper,black=black,screen=darkScreen};
            switch(p.type)
            {
                case "cableSpool": CableSpool(c); return true;
                case "airDuct": AirDuct(c); return true;
                case "fireSafetyBoard": FireSafetyBoard(c); return true;
                case "securityTurnstile": Turnstile(c); return true;
                case "guardBooth": GuardBooth(c); return true;
                case "floodlightTower": Floodlight(c); return true;
                case "gateBarrier": Barrier(c,p); return true;
                case "camera": Camera(c); return true;
                case "abandonedTruck": Truck(c); return true;
                case "trashBin": TrashBin(c); return true;
                case "workstation": case "sovietWorkstation": Workstation(c,p); return true;
                case "typewriter": Typewriter(c,Vector3.zero); return true;
                case "rotaryTelephone": Telephone(c,Vector3.zero); return true;
                case "deskLamp": DeskLamp(c,Vector3.zero); return true;
                case "wallClock": Clock(c); return true;
                case "tapeRecorder": TapeRecorder(c); return true;
                case "wallMap": WallMap(c,p); return true;
                case "radiator": Radiator(c,p); return true;
                case "kettle": Kettle(c); return true;
                case "micStand": Microphone(c); return true;
                case "table": case "archivistDesk": case "anchorDesk": case "meetingTable": Desk(c,p); return true;
                case "coffeeTable": CoffeeTable(c,p); return true;
                case "chair": Chair(c,p); return true;
                case "shelf": Shelf(c,p); return true;
                case "sofa": Sofa(c,p); return true;
                case "coatRack": CoatRack(c,p); return true;
                case "officePlant": Plant(c); return true;
                case "paperCart": case "cleaningCart": Cart(c,p); return true;
                case "copier": Copier(c,p); return true;
                default: return false;
            }
        }

        static Transform Group(Transform parent,string name)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}

        static Transform Source(Context c,string file)
        {Load(file);return Group(c.root,"Repaired source model: "+file);}

        static GameObject Part(Transform parent,string file,int index,string name,Vector3 position,Vector3 euler=default(Vector3),Material overrideMaterial=null)
        {
            var parts=Load(file);
            if(index<0||index>=parts.Length)throw new InvalidOperationException("Missing source part: "+file+" #"+index);
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh=parts[index].mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=overrideMaterial??SourceMaterial(file,parts[index].material);
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            return go;
        }

        static void Solid(Transform parent,Vector3 position,Vector3 size)
        {var collider=parent.gameObject.AddComponent<BoxCollider>();collider.center=position;collider.size=size;}

        static void CableSpool(Context c)
        {
            const string f="prop_high_voltage_cable_spool";var t=Source(c,f);
            // Raw flange meshes were both centred at zero; the cable cylinder supplies the true height.
            Part(t,f,0,"Lower wooden flange",new Vector3(0,.04f,0));
            Part(t,f,1,"Upper wooden flange",new Vector3(0,.72f,0));
            Part(t,f,2,"Coiled cable",new Vector3(0,.38f,0),overrideMaterial:c.black);
            for(int i=0;i<8;i++)Part(t,f,3+i,"Flange timber "+i,new Vector3(0,i<4?.085f:.765f,0),new Vector3(0,(i%4)*45,0));
            Solid(t,new Vector3(0,.38f,0),new Vector3(1.16f,.76f,1.16f));
        }

        static void AirDuct(Context c)
        {
            const string f="prop_ventilation_duct";var t=Source(c,f);
            Part(t,f,0,"Vertical ventilation trunk",new Vector3(0,1.75f,0),overrideMaterial:c.metal);
            for(int i=0;i<3;i++)Part(t,f,i+1,"Duct joining collar "+i,new Vector3(0,.12f+i*1.62f,0),overrideMaterial:c.trim);
            Part(t,f,4,"Duct inspection door",new Vector3(0,1.25f,.543f),overrideMaterial:c.black);
            Solid(t,new Vector3(0,1.75f,0),new Vector3(1.12f,3.5f,1.12f));
        }

        static void FireSafetyBoard(Context c)
        {
            const string f="prop_fire_safety_board";var t=Source(c,f);
            Part(t,f,0,"Fire board backing",new Vector3(0,1.75f,0),overrideMaterial:c.trim);
            Part(t,f,1,"Painted fire board",new Vector3(0,1.75f,.12f));
            Part(t,f,2,"Fire bucket",new Vector3(-.30f,1.46f,.29f),new Vector3(90,0,0));
            Part(t,f,3,"Fire tool grip",new Vector3(.28f,1.98f,.17f),overrideMaterial:c.wood);
            Part(t,f,4,"Fire shovel shaft",new Vector3(.28f,1.65f,.17f),overrideMaterial:c.wood);
            Part(t,f,5,"Fire shovel blade",new Vector3(.28f,1.27f,.17f),overrideMaterial:c.metal);
            // The exported seventh part was a flat sign with coincident text. Replace it with fitted lettering.
            c.Sign("ПОЖЕЖНИЙ ЩИТ\nПІСОК / ВОДА",new Vector3(0,2.12f,.155f),1.02f,.22f);
        }

        static void Turnstile(Context c)
        {
            const string f="prop_security_turnstile";var t=Source(c,f);
            // Keep one complete unit; the source OBJ contained two overlapping copies.
            Part(t,f,0,"Turnstile pedestal",new Vector3(0,.50f,0),overrideMaterial:c.trim);
            Part(t,f,1,"Turnstile cap",new Vector3(0,1.04f,0),overrideMaterial:c.metal);
            Part(t,f,2,"Turnstile hub",new Vector3(.29f,.87f,0),new Vector3(0,0,90),c.metal);
            for(int i=0;i<3;i++)
            {
                float a=i*120*Mathf.Deg2Rad;
                Part(t,f,3+i,"Turnstile arm "+i,new Vector3(.30f,.87f+Mathf.Cos(a)*.24f,Mathf.Sin(a)*.24f),new Vector3(i*120,0,0),c.metal);
            }
            Part(t,f,6,"Turnstile pass slot",new Vector3(0,.97f,.30f),overrideMaterial:c.black);
            Solid(t,new Vector3(0,.55f,0),new Vector3(.48f,1.1f,.8f));
        }

        static void GuardBooth(Context c)
        {
            const string f="prop_checkpoint_guard_booth";var t=Source(c,f);
            Part(t,f,0,"Guard booth platform",new Vector3(0,.1f,0),overrideMaterial:c.metal);
            for(int i=0;i<4;i++)Part(t,f,1+i,"Booth corner post "+i,new Vector3(i%2==0?-1.12f:1.12f,1.3f,i<2?-.99f:.99f),overrideMaterial:c.trim);
            Part(t,f,5,"Booth back panel",new Vector3(0,.70f,-1.06f),overrideMaterial:c.trim);
            Part(t,f,6,"Booth left panel",new Vector3(-1.16f,.7f,0),overrideMaterial:c.trim);
            Part(t,f,7,"Booth right panel",new Vector3(1.16f,.7f,0),overrideMaterial:c.trim);
            Part(t,f,8,"Booth front panel",new Vector3(-.63f,.7f,1.06f),overrideMaterial:c.trim);
            Part(t,f,9,"Booth back window",new Vector3(0,1.67f,-1.06f),overrideMaterial:c.screen);
            Part(t,f,10,"Booth left window",new Vector3(-1.16f,1.67f,0),overrideMaterial:c.screen);
            Part(t,f,11,"Booth right window",new Vector3(1.16f,1.67f,0),overrideMaterial:c.screen);
            Part(t,f,12,"Booth front window",new Vector3(-.63f,1.67f,1.06f),overrideMaterial:c.screen);
            Part(t,f,13,"Booth weather roof",new Vector3(0,2.47f,0),overrideMaterial:c.metal);
            Part(t,f,14,"Guard desk",new Vector3(0,.91f,-.68f),overrideMaterial:c.wood);
            for(int i=0;i<2;i++)Part(t,f,15+i,"Guard desk support "+i,new Vector3(i==0?-.52f:.52f,.49f,-.68f),overrideMaterial:c.metal);
            Part(t,f,17,"Guard stool seat",new Vector3(0,.50f,.2f),overrideMaterial:c.wood);
            Part(t,f,18,"Guard stool support",new Vector3(0,.27f,.2f),overrideMaterial:c.metal);
            c.Sign("ПРОПУСКНИЙ ПУНКТ\nСТОРОННІМ ВХІД ЗАБОРОНЕНО",new Vector3(0,2.27f,1.11f),1.7f,.28f);
            // Collision follows the open doorway instead of closing the entire booth with one box.
            Solid(t,new Vector3(0,.7f,-1.06f),new Vector3(2.4f,1.4f,.16f));
            Solid(t,new Vector3(-1.16f,1.25f,0),new Vector3(.16f,2.5f,2.2f));
            Solid(t,new Vector3(1.16f,1.25f,0),new Vector3(.16f,2.5f,2.2f));
            Solid(t,new Vector3(-.63f,1.25f,1.06f),new Vector3(1.0f,2.5f,.16f));
        }

        static void Floodlight(Context c)
        {
            const string f="prop_floodlight_tower";var t=Source(c,f);
            for(int i=0;i<4;i++)Part(t,f,i,"Tower upright "+i,new Vector3(i%2==0?-.55f:.55f,3.6f,i<2?-.55f:.55f),overrideMaterial:c.metal);
            for(int j=0;j<3;j++)for(int i=0;i<4;i++)
                Part(t,f,4+j*4+i,"Tower cross rail "+j+"-"+i,new Vector3(i<2?0:(i==2?-.55f:.55f),1.4f+j*2.35f,i<2?(i==0?-.55f:.55f):0),overrideMaterial:c.metal);
            Part(t,f,16,"Tower base",new Vector3(0,.04f,0),overrideMaterial:c.trim);
            for(int i=0;i<4;i++)Part(t,f,17+i,"Tower head frame "+i,new Vector3(i<2?0:(i==2?-.7f:.7f),7.5f,i<2?(i==0?-.7f:.7f):0),overrideMaterial:c.metal);
            Part(t,f,21,"Unpowered floodlight housing",new Vector3(0,7.8f,.67f),overrideMaterial:c.metal);
            Part(t,f,22,"Unpowered floodlight lens",new Vector3(0,7.79f,.905f),new Vector3(90,0,0),c.paper);
            Solid(t,new Vector3(0,3.6f,0),new Vector3(1.2f,7.2f,1.2f));
        }

        static void Barrier(Context c,PropData p)
        {
            const string f="prop_gate_barrier";var t=Source(c,f);float x=-Mathf.Max(1.5f,p.w*.5f);
            Part(t,f,0,"Barrier control pedestal",new Vector3(x,.55f,0),overrideMaterial:c.trim);
            Part(t,f,1,"Barrier weather cap",new Vector3(x,1.15f,0),overrideMaterial:c.metal);
            Part(t,f,2,"Barrier motor",new Vector3(x,.92f,0),overrideMaterial:c.metal);
            Part(t,f,3,"Barrier arm pivot",new Vector3(x,1.04f,.10f),overrideMaterial:c.black);
            // Raise the restored arm so the front-yard route remains passable.
            Part(t,f,4,"Raised barrier arm",new Vector3(x,2.47f,.10f),new Vector3(0,0,90),c.paper);
            for(int i=0;i<5;i++)Part(t,f,5+i,"Barrier warning stripe "+i,new Vector3(x,1.40f+i*.46f,.10f),new Vector3(0,0,90),c.trim);
            Solid(t,new Vector3(x,.6f,0),new Vector3(.55f,1.2f,.55f));
        }

        static void Camera(Context c)
        {
            const string f="prop_tk04_studio_camera";var t=Source(c,f);
            Part(t,f,0,"TK04 central pedestal",new Vector3(0,.75f,0),overrideMaterial:c.metal);
            // These three meshes already contain the strut rotations; restore their shared ground datum.
            for(int i=0;i<3;i++)Part(t,f,i+1,"TK04 tripod leg "+i,new Vector3(0,-.04f,0),overrideMaterial:c.metal);
            Part(t,f,4,"TK04 camera body",new Vector3(0,1.66f,0),overrideMaterial:c.trim);
            Part(t,f,5,"TK04 lens base",new Vector3(0,1.66f,.54f),new Vector3(90,0,0),c.black);
            Part(t,f,6,"TK04 lens turret",new Vector3(0,1.66f,.73f),new Vector3(90,0,0),c.metal);
            Part(t,f,7,"TK04 camera lens",new Vector3(0,1.66f,.90f),new Vector3(90,0,0),c.black);
            Part(t,f,9,"TK04 viewfinder",new Vector3(-.24f,1.92f,-.24f),overrideMaterial:c.black);
            Solid(t,new Vector3(0,.82f,0),new Vector3(1f,1.64f,1f));
        }

        static void Truck(Context c)
        {
            const string f="prop_abandoned_gaz66_truck";var t=Source(c,f);
            Part(t,f,0,"GAZ66 chassis",new Vector3(0,.69f,0),overrideMaterial:c.metal);
            for(int i=0;i<4;i++)Part(t,f,1+i,"GAZ66 wheel "+i,new Vector3(i%2==0?-.92f:.92f,.34f,i<2?-1.30f:1.30f),new Vector3(0,0,90),c.black);
            Part(t,f,5,"GAZ66 cabin",new Vector3(0,1.35f,1.18f),overrideMaterial:c.trim);
            Part(t,f,6,"GAZ66 cabin roof",new Vector3(0,2.03f,1.23f),overrideMaterial:c.trim);
            Part(t,f,7,"GAZ66 windscreen",new Vector3(0,1.65f,2.09f),overrideMaterial:c.screen);
            Part(t,f,8,"GAZ66 radiator grille",new Vector3(0,1.07f,2.11f),overrideMaterial:c.black);
            for(int i=0;i<2;i++)Part(t,f,9+i,"GAZ66 headlight "+i,new Vector3(i==0?-.63f:.63f,1.17f,2.13f),new Vector3(90,0,0),c.paper);
            Part(t,f,11,"GAZ66 cargo bed",new Vector3(0,1.1f,-1.02f),overrideMaterial:c.wood);
            Part(t,f,12,"GAZ66 cargo bed floor",new Vector3(0,.79f,-1.02f),overrideMaterial:c.wood);
            Solid(t,new Vector3(0,1.16f,.04f),new Vector3(2.22f,2.32f,4.28f));
        }

        static void TrashBin(Context c)
        {
            c.Box("Bin base",new Vector3(0,.035f,0),new Vector3(.44f,.07f,.44f),c.metal,true);
            foreach(float x in new[]{-.215f,.215f})c.Box("Bin side",new Vector3(x,.32f,0),new Vector3(.04f,.58f,.44f),c.metal,true);
            foreach(float z in new[]{-.215f,.215f})c.Box("Bin side",new Vector3(0,.32f,z),new Vector3(.40f,.58f,.04f),c.metal,true);
            c.Box("Discarded paper inside bin",new Vector3(0,.19f,0),new Vector3(.34f,.11f,.32f),c.paper);
        }

        static void Workstation(Context c,PropData p)
        {
            float w=p.w>0?p.w:2.3f,d=p.d>0?p.d:1.25f;
            Desktop(c,w,d,true);
            // Use the common drawer presentation so existing drawer IDs, contents and animations survive.
            var drawerProp=p;
            if(p.type=="sovietWorkstation")drawerProp=new PropData {id=p.id,type="workstation",label=p.label,w=p.w,d=p.d,x=p.x,y=p.y,z=p.z,rotation=p.rotation};
            DeadAirDrawerPresentation.BuildFurnitureDrawers(c.root,drawerProp,c.wood,c.trim,c.metal,c.paper);
            Typewriter(c,new Vector3(.12f,.82f,-.14f));Telephone(c,new Vector3(-w*.34f,.82f,-d*.20f));
            DeskLamp(c,new Vector3(w*.36f,.82f,-d*.27f));
            c.Box("Unfinished typed report",new Vector3(-w*.25f,.826f,d*.21f),new Vector3(.42f,.012f,.30f),c.paper);
        }

        static Material FurnitureMaterial(string name,Color color,float smoothness=.13f)
        {
            if(furnitureMaterials.TryGetValue(name,out var saved))return saved;
            string path=MaterialFolder+"/"+name+".mat";saved=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(saved==null){saved=new Material(Shader.Find("Dead Air/Source Surface"));AssetDatabase.CreateAsset(saved,path);}
            saved.SetColor("_Color",color);saved.SetColor("_EmissionColor",Color.black);saved.SetFloat("_Glossiness",smoothness);saved.SetFloat("_Cull",2);
            saved.enableInstancing=true;EditorUtility.SetDirty(saved);furnitureMaterials[name]=saved;return saved;
        }

        static void Desktop(Context c,float w,float d,bool drawers)
        {
            c.Rounded("Desk top",new Vector3(0,.78f,0),new Vector3(w,.08f,d),c.wood,.055f,true);
            c.Rounded("Desk underside apron",new Vector3(0,.71f,0),new Vector3(w-.10f,.06f,d-.10f),c.wood,.028f);
            c.Rounded("Desk rear privacy panel",new Vector3(0,.43f,-d*.34f),new Vector3(w*.77f,.46f,.035f),c.wood,.012f);
            if(drawers)
            {
                foreach(float z in new[]{-d*.31f,d*.31f})
                    DeadAirFurnitureMeshes.Support(c.root,"Tapered desk support",new Vector3(w*.39f,0,z),new Vector3(w*.37f,.75f,z),.036f,.027f,c.wood);
            }
            else foreach(float x in new[]{-w*.38f,w*.38f})
            {
                c.Rounded("Wooden desk pedestal",new Vector3(x,.38f,0),new Vector3(w*.19f,.70f,d*.77f),c.wood,.025f,true);
                c.Rounded("Desk pedestal plinth",new Vector3(x,.025f,0),new Vector3(w*.19f+.018f,.05f,d*.79f),c.trim,.012f);
                for(int i=0;i<3;i++)
                {
                    c.Rounded("Pedestal drawer front",new Vector3(x,.19f+i*.19f,d*.394f),new Vector3(w*.17f,.17f,.028f),c.wood,.015f);
                    DeadAirFurnitureMeshes.Support(c.root,"Pedestal brass pull",new Vector3(x-.075f,.19f+i*.19f,d*.42f),new Vector3(x+.075f,.19f+i*.19f,d*.42f),.010f,.010f,c.metal);
                }
            }
        }

        static void Desk(Context c,PropData p)
        {
            float w=p.w>0?p.w:2.3f,d=p.d>0?p.d:1.25f;
            if(p.type=="meetingTable")
            {
                c.Rounded("Desk top",new Vector3(0,.78f,0),new Vector3(w,.08f,d),c.wood,Mathf.Min(d*.30f,.45f),true);
                foreach(float x in new[]{-w*.34f,w*.34f})foreach(float z in new[]{-d*.29f,d*.29f})
                    DeadAirFurnitureMeshes.Support(c.root,"Meeting table turned leg",new Vector3(x,0,z),new Vector3(x*.96f,.75f,z*.96f),.045f,.03f,c.wood);
                Telephone(c,new Vector3(0,.82f,0));
            }
            else Desktop(c,w,d,false);
            c.Rounded("Desk document stack",new Vector3(0,.837f,d*.30f),new Vector3(.37f,.034f,.26f),c.paper,.008f);
            if(p.type=="archivistDesk")Telephone(c,new Vector3(w*.32f,.82f,-d*.24f));
        }

        static void CoffeeTable(Context c,PropData p)
        {
            float w=p.w>0?p.w:1.5f,d=p.d>0?p.d:.75f;
            c.Rounded("Low tabletop",new Vector3(0,.43f,0),new Vector3(w,.08f,d),c.wood,.09f,true);
            c.Rounded("Low table magazine shelf",new Vector3(0,.15f,0),new Vector3(w*.75f,.045f,d*.75f),c.wood,.04f);
            foreach(float x in new[]{-w*.38f,w*.38f})foreach(float z in new[]{-d*.34f,d*.34f})
                DeadAirFurnitureMeshes.Support(c.root,"Tapered low table leg",new Vector3(x,0,z),new Vector3(x*.94f,.40f,z*.94f),.026f,.034f,c.wood);
            c.Rounded("Abandoned journal",new Vector3(.15f,.49f,0),new Vector3(.34f,.035f,.25f),c.paper,.008f);
        }

        static void Chair(Context c,PropData p)
        {
            float w=Mathf.Min(.59f,p.w>0?p.w-.035f:.59f),d=Mathf.Min(.59f,p.d>0?p.d-.035f:.59f);
            var fabric=FurnitureMaterial("period-ochre-upholstery",new Color(.34f,.21f,.10f));
            c.Rounded("Curved plywood seat",new Vector3(0,.475f,0),new Vector3(w,.055f,d*.91f),c.wood,.075f,true);
            c.Rounded("Worn chair seat cushion",new Vector3(0,.514f,.012f),new Vector3(w-.065f,.035f,d*.80f),fabric,.08f);
            foreach(float x in new[]{-w*.40f,w*.40f})foreach(float z in new[]{-d*.40f,d*.40f})
                DeadAirFurnitureMeshes.Support(c.root,"Splayed chair leg",new Vector3(x,0,z),new Vector3(x*.87f,.455f,z*.84f),.024f,.018f,c.wood);
            foreach(float x in new[]{-w*.36f,w*.36f})
                DeadAirFurnitureMeshes.Support(c.root,"Chair back support",new Vector3(x,.45f,-d*.32f),new Vector3(x,.95f,-d*.40f),.018f,.018f,c.wood);
            DeadAirFurnitureMeshes.Install(c.root,"Bent plywood chair back",DeadAirFurnitureMeshes.CurvedPanel(w,.32f,.035f,.055f),new Vector3(0,.86f,-d*.40f),c.wood,true);
            foreach(float z in new[]{-d*.32f,d*.32f})DeadAirFurnitureMeshes.Support(c.root,"Chair side stretcher",new Vector3(-w*.37f,.21f,z),new Vector3(w*.37f,.21f,z),.012f,.012f,c.wood);
        }

        static void Shelf(Context c,PropData p)
        {
            float w=p.w>0?p.w:2.1f,d=p.d>0?p.d:.6f;
            for(int side=0;side<2;side++)
            {
                float x=(side==0?-1:1)*(w*.5f-.035f);
                foreach(float z in new[]{-d*.43f,d*.43f})c.Rounded("Shelf angle upright",new Vector3(x,.975f,z),new Vector3(.06f,1.95f,.045f),c.metal,.012f,true,0);
            }
            c.Rounded("Shelf rear cross brace",new Vector3(0,1.86f,-d*.44f),new Vector3(w-.04f,.035f,.035f),c.metal,.01f,bevel:0);
            Color[] colors={new Color(.26f,.15f,.11f),new Color(.23f,.27f,.17f),new Color(.30f,.13f,.11f),new Color(.16f,.20f,.22f)};
            int seed=0;foreach(char ch in p.id??"shelf")seed=(seed*31+ch)&0x7fffffff;
            for(int row=0;row<5;row++)
            {
                float y=.16f+row*.40f;c.Rounded("Shelf timber board",new Vector3(0,y,0),new Vector3(w-.025f,.035f,d),c.wood,.014f,true,0);
                int count=Mathf.Max(6,Mathf.FloorToInt(w/.18f));float step=(w-.17f)/count;
                for(int col=0;col<count;col++)
                {
                    int variant=(seed+row*7+col*11)&3;float bookW=step*(.65f+variant*.08f),h=.235f+variant*.035f,depth=d*(.58f+variant*.065f);
                    float x=-(w-.17f)/2+step*(col+.5f),bottom=y+.018f;
                    var cover=FurnitureMaterial("period-book-cover-"+variant,colors[variant]);
                    var book=DeadAirFurnitureMeshes.Install(c.root,"Bound archive book",DeadAirFurnitureMeshes.Book(bookW,h,depth,col%3==0),new Vector3(x,bottom+h/2,0),cover);
                    book.GetComponent<MeshRenderer>().sharedMaterials=new[]{cover,c.paper};
                }
            }
        }

        static void Sofa(Context c,PropData p)
        {
            float w=p.w>0?p.w:2.6f,d=p.d>0?p.d:1;
            var upholstery=FurnitureMaterial("period-brown-sofa-fabric",new Color(.28f,.16f,.105f));
            foreach(float x in new[]{-w*.40f,w*.40f})foreach(float z in new[]{-d*.35f,d*.35f})
                DeadAirFurnitureMeshes.Support(c.root,"Sofa tapered foot",new Vector3(x,0,z),new Vector3(x,.17f,z),.027f,.035f,c.wood);
            c.Rounded("Sofa upholstered carcass",new Vector3(0,.30f,0),new Vector3(w,.30f,d),upholstery,.09f,true);
            c.Rounded("Sofa rounded back",new Vector3(0,.73f,-d*.36f),new Vector3(w,.68f,d*.23f),upholstery,.07f,true);
            for(int i=0;i<3;i++)
            {
                float x=-w*.30f+i*w*.30f;
                c.Rounded("Separate sofa seat cushion",new Vector3(x,.495f,d*.07f),new Vector3(w*.283f,.13f,d*.69f),upholstery,.07f);
                c.Rounded("Sofa back cushion",new Vector3(x,.79f,-d*.23f),new Vector3(w*.282f,.43f,.13f),upholstery,.055f);
            }
            foreach(float x in new[]{-w/2+.08f,w/2-.08f})c.Rounded("Rounded upholstered arm",new Vector3(x,.58f,0),new Vector3(.16f,.32f,d*.95f),upholstery,.065f,true);
        }

        static void CoatRack(Context c,PropData p)
        {
            float w=p.w>0?p.w:1.6f,d=p.d>0?p.d:.4f;
            foreach(float x in new[]{-w*.42f,w*.42f})
            {
                c.Rounded("Coat rack floor foot",new Vector3(x,.025f,0),new Vector3(.12f,.05f,d),c.metal,.018f,true);
                DeadAirFurnitureMeshes.Support(c.root,"Coat rack round post",new Vector3(x,.05f,0),new Vector3(x,1.95f,0),.018f,.018f,c.metal);
            }
            DeadAirFurnitureMeshes.Support(c.root,"Coat rack rail",new Vector3(-w*.42f,1.95f,0),new Vector3(w*.42f,1.95f,0),.018f,.018f,c.metal);
            var coat=FurnitureMaterial("period-grey-overcoat",new Color(.17f,.185f,.17f));
            for(int i=0;i<3;i++)
            {
                float x=-w*.27f+i*w*.27f;
                DeadAirFurnitureMeshes.Support(c.root,"Coat hook",new Vector3(x,1.94f,0),new Vector3(x,1.85f,.07f),.010f,.010f,c.metal);
                var g=DeadAirFurnitureMeshes.Install(c.root,"Hanging wool coat",DeadAirFurnitureMeshes.HangingCoat(),new Vector3(x,1.28f,.045f),coat);
                g.transform.localScale=new Vector3(Mathf.Min(1,w*.30f/.54f),1,1);
                c.Rounded("Coat folded collar",new Vector3(x,1.76f,.083f),new Vector3(.15f,.10f,.055f),coat,.025f);
            }
        }

        static void Plant(Context c)
        {
            var pot=FurnitureMaterial("period-terracotta-pot",new Color(.38f,.20f,.12f));
            var leaves=FurnitureMaterial("period-dusty-leaf",new Color(.16f,.24f,.115f));
            c.Turned("Terracotta turned flowerpot",Vector3.zero,new[]{0f,.035f,.36f,.42f,.455f},new[]{.16f,.18f,.235f,.25f,.23f},pot,14);
            c.Turned("Pot dark soil",new Vector3(0,.455f,0),new[]{0f,.007f},new[]{.215f,.215f},c.wood,14);
            for(int i=0;i<7;i++)
            {
                float a=i*2.4f;Vector3 start=new Vector3(0,.44f,0),end=new Vector3(Mathf.Sin(a)*.08f,.63f+(i%3)*.13f,Mathf.Cos(a)*.08f);
                DeadAirFurnitureMeshes.Support(c.root,"Plant stem",start,end,.007f,.004f,leaves);
                var leaf=DeadAirFurnitureMeshes.Install(c.root,"Curved dusty foliage",DeadAirFurnitureMeshes.Leaf(),end,leaves);
                leaf.transform.localRotation=Quaternion.Euler(18+(i%3)*9,a*Mathf.Rad2Deg,0);
                leaf.transform.localScale=Vector3.one*.78f;
            }
        }

        static void Cart(Context c,PropData p)
        {
            float w=p.w>0?p.w:1.1f,d=p.d>0?p.d:.65f;
            foreach(float x in new[]{-w*.41f,w*.41f})foreach(float z in new[]{-d*.37f,d*.37f})
            {
                var wheel=c.Turned("Rubber trolley wheel",new Vector3(x,.07f,z),new[]{-.025f,.025f},new[]{.07f,.07f},c.black,10);wheel.transform.localRotation=Quaternion.Euler(0,0,90);
                DeadAirFurnitureMeshes.Support(c.root,"Trolley tubular upright",new Vector3(x,.13f,z),new Vector3(x,.94f,z),.013f,.013f,c.metal);
            }
            foreach(float y in new[]{.21f,.69f})c.Rounded("Trolley pressed shelf",new Vector3(0,y,0),new Vector3(w,.035f,d),c.metal,.04f,true);
            DeadAirFurnitureMeshes.Support(c.root,"Trolley push handle",new Vector3(-w*.41f,.95f,-d*.37f),new Vector3(w*.41f,.95f,-d*.37f),.014f,.014f,c.metal);
            if(p.type=="paperCart")for(int i=0;i<3;i++)
                c.Rounded("Bundled paper stack",new Vector3(-w*.27f+i*w*.27f,.752f,0),new Vector3(w*.23f,.09f,d*.67f),c.paper,.009f);
            else
            {
                c.Turned("Cleaning bucket",new Vector3(0,.71f,0),new[]{0f,.05f,.34f,.36f},new[]{.15f,.16f,.21f,.22f},c.trim,12);
                DeadAirFurnitureMeshes.Support(c.root,"Mop wooden shaft",new Vector3(w*.25f,.24f,0),new Vector3(w*.32f,1.3f,-.08f),.010f,.010f,c.wood);
            }
        }

        static void Copier(Context c,PropData p)
        {
            float w=p.w>0?p.w:1.1f,d=p.d>0?p.d:.8f;
            c.Rounded("Analog copier pedestal",new Vector3(0,.37f,0),new Vector3(w*.88f,.74f,d*.86f),c.trim,.045f,true);
            c.Rounded("Copier rounded enamel body",new Vector3(0,.84f,0),new Vector3(w,.30f,d),c.paper,.055f,true);
            c.Rounded("Copier glass bed",new Vector3(0,1.003f,-.035f),new Vector3(w*.84f,.018f,d*.72f),c.screen,.035f);
            c.Rounded("Copier platen lid",new Vector3(0,1.044f,-.04f),new Vector3(w*.94f,.065f,d*.79f),c.trim,.045f);
            c.Rounded("Copier control strip",new Vector3(w*.23f,1.08f,d*.35f),new Vector3(w*.35f,.035f,.15f),c.metal,.02f);
            for(int i=0;i<4;i++)c.Turned("Copier mechanical control button",new Vector3(w*.12f+i*.055f,1.101f,d*.35f),new[]{0f,.008f},new[]{.016f,.016f},c.black,8);
            for(int i=0;i<2;i++)
            {
                c.Rounded("Copier paper cassette seam",new Vector3(0,.24f+i*.23f,d*.441f),new Vector3(w*.75f,.018f,.008f),c.black,.003f);
                c.Rounded("Copier paper cassette pull",new Vector3(0,.20f+i*.23f,d*.456f),new Vector3(.19f,.025f,.035f),c.metal,.01f);
            }
            c.Rounded("Copier paper output tray",new Vector3(-w*.31f,.79f,d*.36f),new Vector3(w*.33f,.025f,d*.20f),c.metal,.018f);
        }

        static void Typewriter(Context c,Vector3 p)
        {
            c.Box("Mechanical typewriter body",p+new Vector3(0,.07f,0),new Vector3(.56f,.14f,.40f),c.trim);
            c.Box("Typewriter key bed",p+new Vector3(0,.095f,.18f),new Vector3(.53f,.055f,.20f),c.black);
            for(int row=0;row<3;row++)for(int col=0;col<10;col++)
                c.Box("Typewriter key",p+new Vector3(-.225f+col*.05f,.127f+row*.004f,.225f-row*.055f),new Vector3(.032f,.018f,.028f),c.paper);
            c.Box("Typewriter space bar",p+new Vector3(0,.126f,.283f),new Vector3(.32f,.025f,.027f),c.black);
            c.Box("Typewriter platen",p+new Vector3(0,.21f,-.135f),new Vector3(.52f,.08f,.075f),c.black);
            c.Box("Typewriter carriage rail",p+new Vector3(0,.205f,-.20f),new Vector3(.67f,.035f,.04f),c.metal);
            c.Box("Paper in typewriter",p+new Vector3(0,.322f,-.155f),new Vector3(.30f,.24f,.012f),c.paper);
            for(int i=0;i<4;i++)c.Box("Typed report line",p+new Vector3(0,.38f-i*.025f,-.145f),new Vector3(.20f,.006f,.003f),c.black);
        }

        static void Telephone(Context c,Vector3 p)
        {
            c.Box("Rotary telephone base",p+new Vector3(0,.05f,0),new Vector3(.28f,.10f,.23f),c.black);
            c.Box("Telephone handset bridge",p+new Vector3(0,.145f,-.06f),new Vector3(.34f,.055f,.055f),c.black);
            foreach(float x in new[]{-.135f,.135f})c.Box("Telephone handset cup",p+new Vector3(x,.12f,-.06f),new Vector3(.09f,.08f,.11f),c.black);
            for(int i=0;i<10;i++)
            {float a=i*Mathf.PI*.2f;c.Box("Rotary dial finger hole",p+new Vector3(Mathf.Sin(a)*.068f,.105f,.035f+Mathf.Cos(a)*.063f),new Vector3(.022f,.012f,.022f),c.paper);}
            c.Box("Telephone dial hub",p+new Vector3(0,.106f,.035f),new Vector3(.053f,.018f,.053f),c.metal);
            for(int i=0;i<8;i++)c.Box("Telephone coiled cord",p+new Vector3(-.19f,.025f,.03f+i*.019f),new Vector3(.055f,.025f,.014f),c.black);
        }

        static void DeskLamp(Context c,Vector3 p)
        {
            c.Box("Desk lamp weighted base",p+new Vector3(0,.025f,0),new Vector3(.20f,.05f,.16f),c.trim);
            var arm=c.Box("Desk lamp bent neck",p+new Vector3(0,.22f,-.025f),new Vector3(.03f,.36f,.03f),c.metal);arm.transform.localRotation=Quaternion.Euler(-18,0,0);
            c.Box("Desk lamp upper arm",p+new Vector3(0,.39f,.055f),new Vector3(.03f,.04f,.20f),c.metal);
            c.Box("Desk lamp enamel shade",p+new Vector3(0,.39f,.14f),new Vector3(.25f,.09f,.17f),c.trim);
            c.Box("Desk lamp unlit diffuser",p+new Vector3(0,.341f,.14f),new Vector3(.20f,.008f,.12f),c.paper);
        }

        static void Clock(Context c)
        {
            c.Box("Wall clock wooden casing",new Vector3(0,2.55f,0),new Vector3(.58f,.58f,.10f),c.wood);
            c.Box("Wall clock ivory face",new Vector3(0,2.55f,.058f),new Vector3(.50f,.50f,.018f),c.paper);
            for(int i=0;i<12;i++)
            {float a=i*Mathf.PI/6;var tick=c.Box("Wall clock hour mark",new Vector3(Mathf.Sin(a)*.205f,2.55f+Mathf.Cos(a)*.205f,.072f),new Vector3(.012f,.037f,.006f),c.black);tick.transform.localRotation=Quaternion.Euler(0,0,-i*30);}
            c.Box("Stopped clock minute hand",new Vector3(0,2.64f,.083f),new Vector3(.012f,.18f,.008f),c.black);
            var hour=c.Box("Stopped clock hour hand",new Vector3(.052f,2.52f,.089f),new Vector3(.13f,.018f,.008f),c.black);hour.transform.localRotation=Quaternion.Euler(0,0,-32);
        }

        static void TapeRecorder(Context c)
        {
            c.Box("Reel recorder wooden case",new Vector3(0,.26f,0),new Vector3(.70f,.52f,.25f),c.wood);
            c.Box("Recorder brushed panel",new Vector3(0,.26f,.135f),new Vector3(.64f,.46f,.018f),c.metal);
            foreach(float x in new[]{-.175f,.175f})
            {
                for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var rim=c.Box("Reel rim",new Vector3(x+Mathf.Sin(a)*.12f,.34f+Mathf.Cos(a)*.12f,.165f),new Vector3(.07f,.018f,.03f),c.black);rim.transform.localRotation=Quaternion.Euler(0,0,-i*30);}
                c.Box("Reel spindle",new Vector3(x,.34f,.177f),new Vector3(.038f,.038f,.025f),c.paper);
                for(int i=0;i<3;i++){var spoke=c.Box("Reel spoke",new Vector3(x,.34f,.18f),new Vector3(.19f,.013f,.014f),c.metal);spoke.transform.localRotation=Quaternion.Euler(0,0,i*60);}
            }
            for(int i=0;i<5;i++)c.Box("Recorder transport key",new Vector3(-.17f+i*.085f,.09f,.159f),new Vector3(.052f,.035f,.025f),c.paper);
            c.Box("Recorder tape bridge",new Vector3(0,.245f,.164f),new Vector3(.24f,.035f,.02f),c.black);
        }

        static void WallMap(Context c,PropData p)
        {
            float w=p.w>1?p.w:3.4f;
            c.Box("Transmission diagram frame",new Vector3(0,1.94f,0),new Vector3(w,1.70f,.09f),c.wood);
            c.Box("Green transmission diagram",new Vector3(0,1.94f,.057f),new Vector3(w-.12f,1.58f,.023f),c.trim);
            for(int row=0;row<4;row++)c.Box("Diagram horizontal trunk",new Vector3(0,1.45f+row*.29f,.076f),new Vector3(w-.35f,.012f,.007f),c.paper);
            for(int col=0;col<7;col++)
            {
                float x=-w*.40f+col*w*.133f;
                c.Box("Diagram vertical trunk",new Vector3(x,1.94f,.077f),new Vector3(.012f,1.2f,.007f),c.paper);
                for(int row=0;row<4;row++)c.Box("Diagram relay node",new Vector3(x,1.45f+row*.29f,.087f),new Vector3(.07f,.045f,.012f),c.paper);
            }
            c.Sign("СХЕМА ПРИЙОМУ / АВАРІЙНА НЕСУЧА",new Vector3(0,2.60f,.083f),w-.3f,.16f);
        }

        static void Radiator(Context c,PropData p)
        {
            float w=p.w>0?p.w:1.5f;int count=Mathf.Max(5,Mathf.RoundToInt(w/.13f));
            for(int i=0;i<count;i++)c.Box("Cast iron radiator rib",new Vector3(-w*.5f+.065f+i*w/count,.48f,0),new Vector3(.08f,.68f,.18f),c.metal);
            foreach(float y in new[]{.23f,.71f})c.Box("Radiator connecting pipe",new Vector3(0,y,0),new Vector3(w,.06f,.065f),c.metal);
            c.Box("Radiator service valve",new Vector3(w*.5f+.10f,.71f,0),new Vector3(.17f,.09f,.08f),c.trim);
        }

        static void Kettle(Context c)
        {
            c.Box("Enamel kettle body",new Vector3(0,.12f,0),new Vector3(.22f,.24f,.20f),c.paper);
            c.Box("Kettle lid",new Vector3(0,.251f,0),new Vector3(.20f,.025f,.18f),c.metal);
            c.Box("Kettle lid knob",new Vector3(0,.284f,0),new Vector3(.045f,.04f,.045f),c.black);
            var spout=c.Box("Kettle spout",new Vector3(.15f,.16f,0),new Vector3(.15f,.065f,.065f),c.metal);spout.transform.localRotation=Quaternion.Euler(0,0,25);
            foreach(float z in new[]{-.065f,.065f})c.Box("Kettle carry handle support",new Vector3(0,.31f,z),new Vector3(.035f,.12f,.035f),c.black);
            c.Box("Kettle carry handle",new Vector3(0,.376f,0),new Vector3(.035f,.035f,.16f),c.black);
        }

        static void Microphone(Context c)
        {
            c.Box("Microphone weighted base",new Vector3(0,.035f,0),new Vector3(.40f,.07f,.40f),c.black,true);
            c.Box("Microphone stand",new Vector3(0,.73f,0),new Vector3(.028f,1.39f,.028f),c.metal);
            c.Box("Microphone boom",new Vector3(0,1.38f,.19f),new Vector3(.027f,.027f,.45f),c.metal);
            c.Box("Broadcast microphone casing",new Vector3(0,1.40f,.44f),new Vector3(.09f,.13f,.08f),c.trim);
            for(int i=0;i<5;i++)c.Box("Microphone grille slot",new Vector3(0,1.36f+i*.02f,.484f),new Vector3(.074f,.007f,.005f),c.black);
            c.Box("Microphone cable",new Vector3(.042f,.69f,.015f),new Vector3(.009f,1.32f,.009f),c.black);
        }

        [MenuItem("Dead Air/Validate Period Props")]
        public static void Validate()
        {
            var names=new[]{"cableSpool","airDuct","fireSafetyBoard","securityTurnstile","guardBooth","floodlightTower","gateBarrier","camera","abandonedTruck",
                "trashBin","workstation","typewriter","rotaryTelephone","deskLamp","wallClock","tapeRecorder","wallMap","radiator","kettle","micStand",
                "table","archivistDesk","anchorDesk","meetingTable","coffeeTable","chair","shelf","sofa","coatRack","officePlant","paperCart","cleaningCart","copier"};
            var palette=new Material(Shader.Find("Dead Air/Source Surface"));var parent=new GameObject("Period props validation");
            try
            {
                foreach(string type in names)
                {
                    var root=Group(parent.transform,type);var p=new PropData {id="validate-"+type,type=type,w=type=="workstation"?2.3f:1.5f,d=1.25f,label="validation"};
                    if(!TryBuild(root,p,ValidationBlock,ValidationSign,palette,palette,palette,palette,palette,palette))throw new InvalidOperationException("Unhandled period prop: "+type);
                    var renderers=root.GetComponentsInChildren<MeshRenderer>();
                    if(renderers.Length<3)throw new InvalidOperationException("Incomplete period prop: "+type);
                    var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    if(bounds.size.x<.1f||bounds.size.y<.1f||bounds.size.z<.03f)throw new InvalidOperationException("Flat or missing model body: "+type);
                    foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh=filter.sharedMesh;
                        if(mesh==null||mesh.vertexCount==0||!mesh.HasVertexAttribute(VertexAttribute.Normal)||!mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                            throw new InvalidOperationException("Missing source geometry/normals/UV: "+type);
                    }
                    if(type=="workstation"&&Mathf.Abs(root.Find("Desk top").GetComponent<MeshRenderer>().bounds.max.y-.82f)>.001f)throw new InvalidOperationException("Objective tabletop moved.");
                    if(type=="cableSpool")
                    {
                        var source=root.GetChild(0);
                        if(Vector3.Distance(source.Find("Lower wooden flange").position,source.Find("Upper wooden flange").position)<.65f)
                            throw new InvalidOperationException("Cable flanges are still overlapping.");
                    }
                    if(type=="guardBooth")
                    {
                        var source=root.GetChild(0);
                        if(Vector3.Distance(source.Find("Booth corner post 0").position,source.Find("Booth corner post 3").position)<2.5f)
                            throw new InvalidOperationException("Booth corner columns are still overlapping.");
                    }
                    if(type=="camera")
                    {
                        var source=root.GetChild(0);
                        if(source.Find("TK04 camera body").position.y<1.5f||source.Find("TK04 camera lens").position.z<.8f)
                            throw new InvalidOperationException("Camera assembly transforms were not restored.");
                    }
                    UnityEngine.Object.DestroyImmediate(root.gameObject);
                }
                AssetDatabase.SaveAssets();
                ValidateFurnitureShapes();
                Debug.Log("DEAD AIR PERIOD PROPS PASSED: "+names.Length+" complete model families; 9 original OBJ assemblies restored, normals/UV present, objective desktop retained, shaped furniture bounds and supports validated.");
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(palette);}
        }

        [MenuItem("Dead Air/Validate Furniture Model Shapes")]
        public static void ValidateFurnitureShapes()
        {
            var palette=new Material(Shader.Find("Dead Air/Source Surface"));var parent=new GameObject("Furniture shape validation");
            var samples=new[]{
                new PropData{type="workstation",w=2.3f,d=1.25f},new PropData{type="table",w=2.4f,d=1.3f},
                new PropData{type="archivistDesk",w=2.6f,d=1.35f},new PropData{type="anchorDesk",w=3.4f,d=1.4f},
                new PropData{type="meetingTable",w=3.6f,d=1.7f},new PropData{type="coffeeTable",w=2.1f,d=.9f},
                new PropData{type="chair",w=.65f,d=.65f},new PropData{type="shelf",w=2.1f,d=.6f},
                new PropData{type="sofa",w=2.7f,d=.95f},new PropData{type="coatRack",w=1.3f,d=.55f},
                new PropData{type="officePlant",w=.7f,d=.7f},new PropData{type="paperCart",w=.8f,d=.7f},
                new PropData{type="cleaningCart",w=.8f,d=.7f},new PropData{type="copier",w=1.1f,d=.8f}};
            try
            {
                foreach(var p in samples)
                {
                    p.id="shape-"+p.type;p.label="validation";var root=Group(parent.transform,p.type);
                    TryBuild(root,p,ValidationBlock,ValidationSign,palette,palette,palette,palette,palette,palette);
                    var renderers=root.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    if(bounds.min.x<-p.w/2-.025f||bounds.max.x>p.w/2+.025f||bounds.min.z<-p.d/2-.025f||bounds.max.z>p.d/2+.025f)
                        throw new InvalidOperationException("Furniture exceeds its authored clearance footprint: "+p.type+" / "+bounds);
                    if(bounds.min.y<-.005f||bounds.min.y>.015f)throw new InvalidOperationException("Furniture support is not grounded: "+p.type);
                    int triangles=0,shaped=0;
                    foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        triangles+=filter.sharedMesh.triangles.Length/3;
                        if(!filter.sharedMesh.name.StartsWith("furniture-",StringComparison.Ordinal))continue;
                        DeadAirFurnitureMeshes.ValidateMesh(filter.sharedMesh);shaped++;
                    }
                    int budget=p.type=="shelf"?3300:13000;
                    if(shaped<2||triangles>budget)throw new InvalidOperationException("Furniture silhouette/triangle budget failed: "+p.type+" / "+triangles);
                    if(p.type=="shelf")
                    {
                        var books=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Bound archive book").ToArray();
                        if(books.Length!=55||books.Any(f=>f.sharedMesh.subMeshCount!=2||f.sharedMesh.triangles.Length/3>46))throw new InvalidOperationException("Shared book geometry/coverage failed.");
                        Debug.Log("ARCHIVE SHELF GEOMETRY: "+triangles+" triangles, "+books.Length+" closed books with thin covers, recessed page surfaces, cloth spines and catalogue labels.");
                    }
                    var top=root.Find("Desk top");
                    if(top!=null&&Mathf.Abs(top.GetComponent<MeshRenderer>().bounds.max.y-.82f)>.0005f)throw new InvalidOperationException("Changed objective worktop: "+p.type);
                    if(p.type=="workstation")
                    {
                        foreach(var data in DeadAirDrawers.ForProp(p))
                        {var drawer=root.Find("drawer:"+data.id);if(drawer==null||Vector3.Distance(drawer.localPosition,data.localPosition)>.0001f)throw new InvalidOperationException("Drawer datum changed.");}
                    }
                    UnityEngine.Object.DestroyImmediate(root.gameObject);
                }
                AssetDatabase.SaveAssets();Debug.Log("FURNITURE SHAPES PASSED: 14 families, grounded supports, authored footprints, beveled/curved mesh silhouettes, shading attributes, shelf <=3300 triangles, other families <=13k, .82m worktops and original drawers.");
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(palette);}
        }

        static GameObject ValidationBlock(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collide)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;
            go.GetComponent<MeshRenderer>().sharedMaterial=material;if(!collide)UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());return go;
        }
        static void ValidationSign(Transform parent,string text,Vector3 position,Quaternion rotation,float width,float height)
        {var go=new GameObject("Validation plaque");go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=rotation;}

        static SourcePart[] Load(string file)
        {
            if(sources.TryGetValue(file,out var cached))return cached;
            Directory.CreateDirectory(MeshFolder);Directory.CreateDirectory(MaterialFolder);AssetDatabase.Refresh();
            var positions=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var parts=new List<ObjPart>();ObjPart current=null;
            foreach(string raw in File.ReadLines(Models+file+".obj"))
            {
                string line=raw.Trim();if(line.Length==0||line[0]=='#')continue;
                string[] f=line.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
                switch(f[0])
                {
                    case "o": current=new ObjPart();parts.Add(current);break;
                    case "usemtl": if(current==null){current=new ObjPart();parts.Add(current);}current.material=f[1];break;
                    case "v": positions.Add(new Vector3(-Number(f[1]),Number(f[2]),Number(f[3])));break;
                    case "vn": normals.Add(new Vector3(-Number(f[1]),Number(f[2]),Number(f[3])));break;
                    case "vt": uv.Add(new Vector2(Number(f[1]),Number(f[2])));break;
                    case "f":
                        if(current==null)throw new InvalidOperationException("OBJ face precedes object: "+file);
                        int start=current.vertices.Count;
                        for(int i=1;i<f.Length;i++)
                        {
                            string[] corner=f[i].Split('/');current.vertices.Add(positions[Index(corner[0],positions.Count)]);
                            current.uv.Add(corner.Length>1&&corner[1].Length>0?uv[Index(corner[1],uv.Count)]:Vector2.zero);
                            current.normals.Add(corner.Length>2&&corner[2].Length>0?normals[Index(corner[2],normals.Count)]:Vector3.zero);
                        }
                        // Reflect X once, including winding and normals, as at the main import boundary.
                        for(int i=1;i<f.Length-2;i++){current.triangles.Add(start);current.triangles.Add(start+i+1);current.triangles.Add(start+i);}
                        break;
                }
            }
            var result=new List<SourcePart>();
            foreach(var part in parts.Where(part=>part.vertices.Count>0))
            {
                string path=MeshFolder+"/repaired_"+file+"_"+result.Count.ToString("D2")+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.name=file+" / original part "+result.Count;
                mesh.indexFormat=part.vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
                mesh.SetVertices(part.vertices);mesh.SetUVs(0,part.uv);mesh.SetTriangles(part.triangles,0);
                if(part.normals.All(n=>n.sqrMagnitude>.5f))mesh.SetNormals(part.normals);else mesh.RecalculateNormals();
                mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
                result.Add(new SourcePart {mesh=mesh,material=part.material});
            }
            if(result.Count==0)throw new InvalidOperationException("Empty office source model: "+file);
            cached=result.ToArray();sources[file]=cached;return cached;
        }

        static Material SourceMaterial(string file,string key)
        {
            string cacheKey=file+"/"+key;if(sourceMaterials.TryGetValue(cacheKey,out var existing))return existing;
            string active=null;Color color=new Color(.35f,.39f,.33f,1);
            foreach(string raw in File.ReadLines(Models+file+".mtl"))
            {
                string[] f=raw.Trim().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);if(f.Length==0)continue;
                if(f[0]=="newmtl")active=f[1];
                if(f[0]=="Kd"&&active==key){color=new Color(Number(f[1]),Number(f[2]),Number(f[3]),1).gamma;break;}
            }
            // MTL diffuse values came from Three's linear colors. Convert once for Unity material serialization.
            string palette=ColorUtility.ToHtmlStringRGB(color);string path=MaterialFolder+"/source-prop-"+palette+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Dead Air/Source Surface"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_Color",color);material.SetColor("_EmissionColor",Color.black);material.SetFloat("_Glossiness",.16f);material.SetFloat("_Cull",2);
            material.enableInstancing=true;EditorUtility.SetDirty(material);sourceMaterials[cacheKey]=material;return material;
        }

        static float Number(string text)=>float.Parse(text,CultureInfo.InvariantCulture);
        static int Index(string text,int count){int i=int.Parse(text,CultureInfo.InvariantCulture);return i>0?i-1:count+i;}
    }
}
