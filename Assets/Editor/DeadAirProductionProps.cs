using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirProductionProps
    {
        sealed class Kit
        {
            public Transform root;
            public Action<Transform,string,Vector3,Quaternion,float,float> sign;
            public Material wood,paint,metal,paper,rubber,glass,ceramic,fabric,soil,leaf;
            public Kit Child(Transform child) => new Kit {root=child,sign=sign,wood=wood,paint=paint,metal=metal,paper=paper,rubber=rubber,glass=glass,ceramic=ceramic,fabric=fabric,soil=soil,leaf=leaf};
            public GameObject Box(string name,Vector3 at,Vector3 size,Material material,bool solid=false)
            {
                float radius=Mathf.Min(.028f,Mathf.Min(size.x,size.z)*.18f);
                return DeadAirFurnitureMeshes.Install(root,name,DeadAirFurnitureMeshes.RoundedBox(size,radius,Mathf.Min(.006f,size.y*.15f)),at,material,solid);
            }
            public GameObject Cylinder(string name,Vector3 at,float radius,float height,Material mat,bool solid=false)
            {
                float bevel=Mathf.Min(.008f,height*.2f);
                return DeadAirFurnitureMeshes.Install(root,name,DeadAirFurnitureMeshes.Turned(new[]{0f,bevel,height-bevel,height},new[]{radius*.94f,radius,radius,radius*.94f},24),at,mat,solid);
            }
            public GameObject Tube(string name,Vector3 a,Vector3 b,float radius,Material material)
                =>DeadAirFurnitureMeshes.Support(root,name,a,b,radius,radius,material);
            public GameObject Ring(string name,Vector3 at,float radius,float tube,Material mat,Quaternion rotation)
            {var go=DeadAirFurnitureMeshes.Install(root,name,DeadAirFurnitureMeshes.Ring(radius,tube),at,mat);go.transform.localRotation=rotation;return go;}
            public Transform Pivot(string name,Vector3 at)
            {var t=new GameObject(name).transform;t.SetParent(root,false);t.localPosition=at;return t;}
            public void Label(string text,Vector3 at,float w,float h)=>sign(root,text,at,Quaternion.identity,w,h);
            public void Collision(string name,Vector3 at,Vector3 size)
            {var t=Pivot("Collision / "+name,at);t.gameObject.AddComponent<BoxCollider>().size=size;}
        }
        static readonly Dictionary<string,Material> palette=new Dictionary<string,Material>();
        public static void ClearCaches()=>palette.Clear();
        static Material Mat(string name,string surface,Color tint,float gloss=.2f)
        {
            if(!palette.TryGetValue(name,out var material))palette[name]=material=DeadAirArtMaterials.Create("production-"+name,surface,tint,gloss);
            return material;
        }
        static Kit Materials(Transform root,Action<Transform,string,Vector3,Quaternion,float,float> sign)
        {
            return new Kit {root=root,sign=sign,
                wood=Mat("oak-veneer","wood",new Color(.88f,.82f,.72f),.3f),
                paint=Mat("industrial-sage-enamel","painted-metal",new Color(.75f,.80f,.76f),.32f),
                metal=Mat("galvanized-steel","steel",new Color(.64f,.68f,.67f),.45f),
                paper=Mat("archive-paper","paper",new Color(.85f,.80f,.67f),.08f),
                rubber=Mat("bakelite-and-rubber","rubber",new Color(.085f,.075f,.065f),.26f),
                glass=Mat("equipment-glass","glass",new Color(.19f,.29f,.27f),.85f),
                ceramic=Mat("ivory-enamel","ceramic",new Color(.82f,.82f,.75f),.68f),
                fabric=Mat("institutional-wool","fabric",new Color(.30f,.32f,.26f),.06f),
                soil=Mat("potting-earth","soil",new Color(.20f,.16f,.11f),.02f),
                leaf=Mat("dusty-foliage","foliage",new Color(.24f,.36f,.16f),.12f)};
        }

        public static bool TryBuild(Transform root,PropData p,Action<Transform,string,Vector3,Quaternion,float,float> sign,
            Action<Transform,string,Vector3,float,float,Color> practical,Action<Transform> generatorLamp)
        {
            var c=Materials(root,sign);float w=p.w>0?p.w:1,d=p.d>0?p.d:1;
            switch(p.type)
            {
                case "cardCatalog": DeadAirDrawerPresentation.BuildFurnitureDrawers(root,p,c.wood,c.paint,c.metal,c.paper);return true;
                case "bench": Bench(c,w,d);return true;
                case "planter": Planter(c,w,d);return true;
                case "yardLamp": YardLamp(c,p,practical);return true;
                case "generatorShelter": Shelter(c,w,d);generatorLamp(root);return true;
                case "fuelStore": FuelStore(c,w,d);return true;
                case "prisonBed": Bed(c,w,d);return true;
                case "prisonSanitary": Sanitary(c,true);return true;
                case "sink": Sink(c,w,d);return true;
                case "toilet": ToiletStall(c,w,d);return true;
                case "kitchen": Kitchen(c,w,d);return true;
                case "fridge": Cabinet(c,w,d,"fridge");return true;
                case "wardrobe": Cabinet(c,w,d,"wardrobe");return true;
                case "radioCabinet": Cabinet(c,w,d,"radio");return true;
                case "vendingMachine": Vending(c,w,d);return true;
                case "corkboard": case "toolboard": case "directory": case "whiteboard": Noticeboard(c,p,w);return true;
                case "mirror": Mirror(c,w);return true;
                case "chromakeyBackdrop": Backdrop(c,w);return true;
                case "fan": Fan(c,p);return true;
                case "dumpster": Dumpster(c,w,d);return true;
                case "crate": Crate(c,w,d);return true;
                case "mixingConsole": Console(c,w,d);return true;
                case "fusePanel": FusePanel(c,w);return true;
                case "monitorWall": Rack(c,w,d,true);return true;
                case "serverRack": Rack(c,w,d,false);return true;
                case "crt": CRT(c,Vector3.zero,.7f);return true;
                case "dieselGenerator": Generator(c,w,d);return true;
                case "abandonedCar": Sedan(c.Child(c.Pivot("Parked sedan",Vector3.zero)),w,d);return true;
                case "abandonedTruck": Truck(c,w,d);return true;
                case "cableSpool": CableSpool(c);return true;
                case "airDuct": Duct(c);return true;
                case "fireSafetyBoard": FireBoard(c);return true;
                case "securityTurnstile": Turnstile(c);return true;
                case "guardBooth": Booth(c,w,d);return true;
                case "floodlightTower": Floodlight(c);return true;
                case "gateBarrier": Barrier(c,w);return true;
                case "camera": Camera(c);return true;
                case "trashBin": Trash(c);return true;
                default:return false;
            }
        }

        static void Feet(Kit c,float w,float d,float h,Material material)
        {
            foreach(float x in new[]{-w*.42f,w*.42f})foreach(float z in new[]{-d*.36f,d*.36f})
            {c.Tube("Tubular support",new Vector3(x,0,z),new Vector3(x,h,z),.026f,material);c.Collision("support",new Vector3(x,h/2,z),new Vector3(.065f,h,.065f));}
        }
        static void Pull(Kit c,Vector3 at,float length=.16f)
        {
            foreach(float x in new[]{-length/2,length/2})c.Tube("Handle mounting post",at+new Vector3(x,0,-.035f),at+new Vector3(x,0,0),.009f,c.metal);
            c.Tube("Handle grip",at+Vector3.left*length/2,at+Vector3.right*length/2,.011f,c.metal);
        }
        static void Bench(Kit c,float w,float d)
        {
            Feet(c,w,d,.46f,c.metal);
            for(int i=0;i<5;i++)c.Box("Separate hardwood seat slat",new Vector3(0,.48f,-d*.4f+i*d*.2f),new Vector3(w,.055f,d*.17f),c.wood,true);
            for(int i=0;i<3;i++)c.Box("Separate curved-back timber",new Vector3(0,.72f+i*.14f,-d*.43f-i*.025f),new Vector3(w,.115f,.06f),c.wood,true);
            foreach(float x in new[]{-w*.42f,w*.42f})c.Tube("Bench back cast support",new Vector3(x,.16f,-d*.36f),new Vector3(x,1.06f,-d*.50f),.027f,c.metal);
            for(int i=0;i<5;i++)foreach(float x in new[]{-w*.42f,w*.42f})c.Cylinder("Recessed seat bolt",new Vector3(x,.508f,-d*.4f+i*d*.2f),.009f,.003f,c.metal);
        }
        static void Planter(Kit c,float w,float d)
        {
            var stone=Mat("aggregate-planter","concrete",new Color(.61f,.60f,.52f),.07f);
            c.Box("Planter base",new Vector3(0,.06f,0),new Vector3(w-.08f,.12f,d-.08f),stone,true);
            foreach(float x in new[]{-w/2+.06f,w/2-.06f})c.Box("Concrete planter side",new Vector3(x,.30f,0),new Vector3(.12f,.48f,d),stone,true);
            foreach(float z in new[]{-d/2+.06f,d/2-.06f})c.Box("Concrete planter end",new Vector3(0,.30f,z),new Vector3(w-.24f,.48f,.12f),stone,true);
            c.Box("Recessed visible earth",new Vector3(0,.42f,0),new Vector3(w-.25f,.06f,d-.25f),c.soil);
            for(int i=0;i<13;i++)
            {
                float a=i*2.4f;var at=new Vector3(Mathf.Sin(a)*w*.30f,.55f+(i%3)*.08f,Mathf.Cos(a)*d*.23f);
                c.Tube("Shrub twig",new Vector3(at.x,.43f,at.z),at+Vector3.up*.22f,.008f,c.wood);
                var leaf=DeadAirFurnitureMeshes.Install(c.root,"Planter curved foliage",DeadAirFurnitureMeshes.Leaf(),at,c.leaf);leaf.transform.localRotation=Quaternion.Euler(30,a*57.2958f,0);
            }
        }
        static void YardLamp(Kit c,PropData p,Action<Transform,string,Vector3,float,float,Color> practical)
        {
            c.Cylinder("Cast pole base",Vector3.zero,.15f,.25f,c.metal,true);c.Cylinder("Tapered steel pole",new Vector3(0,.20f,0),.058f,4.1f,c.metal,true);
            c.Tube("Curved lamp support",new Vector3(0,4.2f,0),new Vector3(0,4.4f,.42f),.04f,c.metal);
            c.Box("Pressed aluminium luminaire",new Vector3(0,4.36f,.48f),new Vector3(.35f,.14f,.66f),c.metal);
            c.Box("Opal glass diffuser",new Vector3(0,4.282f,.48f),new Vector3(.29f,.025f,.57f),c.ceramic);
            practical(c.root,"lamp:yard-"+p.id,new Vector3(0,4.23f,.48f),10,1.2f,new Color(.90f,.91f,.76f));
        }
        static void Shelter(Kit c,float w,float d)
        {
            foreach(float x in new[]{-w/2+.2f,w/2-.2f})foreach(float z in new[]{-d/2+.2f,d/2-.2f})
            {c.Box("Anchored shelter steel post",new Vector3(x,1.7f,z),new Vector3(.12f,3.4f,.12f),c.metal,true);c.Box("Bolted foot plate",new Vector3(x,.035f,z),new Vector3(.31f,.07f,.31f),c.metal);}
            c.Box("Masonry weather wall",new Vector3(0,1.5f,-d/2),new Vector3(w,3,.14f),Mat("weathered-service-brick","brick",new Color(.63f,.55f,.43f)),true);
            for(int i=0;i<Mathf.CeilToInt(w/.2f);i++)c.Box("Corrugated weather roof rib",new Vector3(-w/2+i*.2f,3.5f,0),new Vector3(.21f,.065f,d+.18f),c.metal);
            foreach(float z in new[]{-d*.37f,0,d*.37f})c.Box("Roof purlin",new Vector3(0,3.39f,z),new Vector3(w,.14f,.09f),c.metal);
        }
        static void Drum(Kit c,Vector3 at)
        {
            c.Cylinder("Steel fuel drum",at,.31f,.88f,c.paint,true);
            foreach(float y in new[]{.025f,.25f,.64f,.86f})c.Ring("Rolled drum bead",at+Vector3.up*y,.312f,.013f,c.metal,Quaternion.identity);
            c.Cylinder("Drum screw plug",at+new Vector3(.15f,.88f,0),.035f,.014f,c.metal);
        }
        static void FuelStore(Kit c,float w,float d)
        {
            c.Box("Fuel bund bottom",new Vector3(0,.075f,0),new Vector3(w,.15f,d),c.metal,true);
            foreach(float z in new[]{-d/2,d/2})c.Box("Spill bund lip",new Vector3(0,.17f,z),new Vector3(w,.19f,.055f),c.metal,true);
            foreach(float x in new[]{-w/2,w/2})c.Box("Spill bund end",new Vector3(x,.17f,0),new Vector3(.055f,.19f,d),c.metal,true);
            for(int i=0;i<3;i++)Drum(c,new Vector3(-w*.31f+i*w*.31f,.15f,0));
        }
        static void Bed(Kit c,float w,float d)
        {
            Feet(c,w,d,.42f,c.metal);
            foreach(float z in new[]{-d*.43f,d*.43f})c.Tube("Steel mattress rail",new Vector3(-w/2,.40f,z),new Vector3(w/2,.40f,z),.028f,c.metal);
            for(int i=0;i<12;i++)c.Tube("Individual bed spring support",new Vector3(-w*.44f+i*w*.08f,.4f,-d*.43f),new Vector3(-w*.44f+i*w*.08f,.4f,d*.43f),.012f,c.metal);
            c.Box("Tufted institutional mattress",new Vector3(0,.49f,0),new Vector3(w-.14f,.17f,d-.12f),c.fabric,true);
            c.Box("Separate cotton pillow",new Vector3(-w*.33f,.62f,0),new Vector3(.48f,.13f,d*.74f),c.paper);
            c.Box("Folded wool blanket",new Vector3(w*.27f,.61f,0),new Vector3(.60f,.075f,d*.84f),c.fabric);
            for(int end=-1;end<=1;end+=2)for(int i=0;i<3;i++)c.Tube("Bed end hoop rail",new Vector3(end*w*.46f,.42f+i*.13f,-d*.42f),new Vector3(end*w*.46f,.42f+i*.13f,d*.42f),.019f,c.metal);
        }
        static void Basin(Kit c,Vector3 at,float width,float depth)
        {
            var bowl=DeadAirFurnitureMeshes.Install(c.root,"Open enamel basin",DeadAirFurnitureMeshes.OpenVessel(.18f,.17f,.28f,.018f),at,c.ceramic);
            bowl.transform.localScale=new Vector3(width/.56f,1,depth/.56f);
            c.Collision("basin base",at+Vector3.up*.025f,new Vector3(width*.65f,.05f,depth*.65f));
            foreach(float x in new[]{-width*.44f,width*.44f})c.Collision("basin side rim",at+new Vector3(x,.15f,0),new Vector3(width*.10f,.06f,depth*.62f));
            foreach(float z in new[]{-depth*.44f,depth*.44f})c.Collision("basin end rim",at+new Vector3(0,.15f,z),new Vector3(width*.62f,.06f,depth*.10f));
            c.Cylinder("Basin drain",at+Vector3.up*.037f,.026f,.004f,c.metal);
            var tap=at+new Vector3(0,.19f,-depth*.46f);
            c.Tube("Faucet upright",tap,tap+Vector3.up*.15f,.018f,c.metal);c.Tube("Faucet spout",tap+Vector3.up*.15f,tap+new Vector3(0,.15f,.13f),.018f,c.metal);
            foreach(float x in new[]{-.10f,.10f})c.Cylinder("Separate faucet valve",tap+new Vector3(x,0,0),.027f,.035f,c.metal);
        }
        static void Sink(Kit c,float w,float d)
        {
            c.Box("Basin wall mounting bracket",new Vector3(0,.76f,-d*.36f),new Vector3(w*.75f,.08f,.08f),c.metal,true);
            Basin(c,new Vector3(0,.75f,0),w*.88f,d*.9f);
            c.Tube("Visible chrome waste pipe",new Vector3(0,.76f,0),new Vector3(0,.52f,0),.025f,c.metal);
            c.Tube("Waste pipe wall return",new Vector3(0,.52f,0),new Vector3(0,.52f,-d*.44f),.025f,c.metal);
        }
        static void Sanitary(Kit c,bool cell)
        {
            c.Cylinder("Toilet pedestal",new Vector3(0,.025f,.15f),.15f,.33f,c.ceramic,true);
            var bowl=DeadAirFurnitureMeshes.Install(c.root,"Hollow ceramic toilet bowl",DeadAirFurnitureMeshes.OpenVessel(.21f,.17f,.25f,.035f),new Vector3(0,.28f,.15f),c.ceramic);bowl.transform.localScale=new Vector3(1,1,1.23f);
            c.Ring("Separate bakelite toilet seat",new Vector3(0,.5f,.15f),.218f,.025f,c.rubber,Quaternion.identity).transform.localScale=new Vector3(1,1,1.23f);
            c.Box("Wall cistern",new Vector3(0,.69f,-.25f),new Vector3(.47f,.43f,.19f),c.ceramic,true);
            c.Box("Cistern removable lid",new Vector3(0,.917f,-.25f),new Vector3(.50f,.035f,.22f),c.ceramic);
            c.Tube("Separate flush lever",new Vector3(.19f,.79f,-.14f),new Vector3(.26f,.79f,-.14f),.014f,c.metal);
            if(cell)Basin(c,new Vector3(0,.95f,-.25f),.55f,.35f);
        }
        static void ToiletStall(Kit c,float w,float d)
        {
            // World-data dimensions describe a cubicle rather than a scaled sanitary fixture.
            c.Box("Cubicle rear partition",new Vector3(0,1.02f,-d*.43f),new Vector3(w*.85f,2.04f,.045f),c.paint,true);
            foreach(float x in new[]{-w*.43f,w*.43f})c.Box("Cubicle side partition",new Vector3(x,1.02f,0),new Vector3(.045f,2.04f,d*.86f),c.paint,true);
            var fixture=c.Pivot("Full-size sanitary fixture",new Vector3(0,0,-d*.23f));Sanitary(c.Child(fixture),false);
            var pivot=c.Pivot("motion:cubicleDoor",new Vector3(-w*.39f,.15f,d*.42f));var door=c.Child(pivot);
            door.Box("Cubicle door leaf",new Vector3(w*.25f,.85f,0),new Vector3(w*.50f,1.7f,.045f),c.paint,true);Pull(door,new Vector3(w*.42f,.9f,.06f));
            pivot.localRotation=Quaternion.Euler(0,-55,0);
        }
        static void Carcass(Kit c,float w,float d,float height,Material finish)
        {
            foreach(float x in new[]{-w/2+.025f,w/2-.025f})c.Box("Cabinet side panel",new Vector3(x,height/2,0),new Vector3(.05f,height,d),finish,true);
            foreach(float y in new[]{.05f,height-.025f})c.Box("Cabinet horizontal panel",new Vector3(0,y,0),new Vector3(w-.10f,.05f,d),finish,true);
            c.Box("Cabinet recessed backing",new Vector3(0,height/2,-d/2+.02f),new Vector3(w-.10f,height-.10f,.04f),finish,true);
        }
        static void Cabinet(Kit c,float w,float d,string kind)
        {
            if(kind=="radio"){Radio(c,w,d);return;}
            float h=kind=="radio"?1.35f:2.0f;var finish=kind=="fridge"?c.ceramic:c.wood;Carcass(c,w,d,h,finish);
            foreach(float y in new[]{.48f,.97f,1.46f})if(y<h-.1f)c.Box("Visible internal cabinet shelf",new Vector3(0,y,0),new Vector3(w-.12f,.028f,d-.08f),finish);
            int leaves=kind=="wardrobe"?2:1;
            for(int i=0;i<leaves;i++)
            {
                float leafW=(w-.025f)/leaves;var pivot=c.Pivot("motion:"+kind+"Door-"+i,new Vector3(-w/2+i*leafW,0,d/2));var k=c.Child(pivot);
                k.Box("Separate cabinet door",new Vector3(leafW/2,h/2,0),new Vector3(leafW-.012f,h-.035f,.055f),finish,true);
                Pull(k,new Vector3(leafW-.10f,h*.55f,.075f),.10f);
                foreach(float y in new[]{h*.18f,h*.82f})k.Cylinder("Door hinge barrel",new Vector3(.01f,y,-.01f),.012f,.08f,c.metal);
            }
        }
        static void Radio(Kit c,float w,float d)
        {
            const float h=1.35f;Carcass(c,w,d,h,c.wood);
            foreach(float y in new[]{.48f,.94f})c.Box("Visible radio chassis support shelf",new Vector3(0,y,0),new Vector3(w-.12f,.028f,d-.08f),c.wood);
            c.Box("Fixed radio control fascia",new Vector3(0,1.12f,d/2),new Vector3(w-.055f,.40f,.055f),c.wood,true);
            float front=d/2+.036f;
            var scale=Mat("radio-printed-tuning-scale","ceramic",new Color(.045f,.074f,.060f),.50f);
            var ink=Mat("radio-scale-ivory-ink","ceramic",new Color(.83f,.78f,.61f),.15f);
            var needle=Mat("radio-tuning-pointer","ceramic",new Color(.58f,.12f,.07f),.24f);
            var nickel=Mat("radio-nickel-fittings","duct-zinc",new Color(.88f,.90f,.91f),.65f);
            c.Box("Glazed tuning scale recessed backing",new Vector3(0,1.122f,front),new Vector3(w*.76f,.145f,.010f),scale);
            foreach(float x in new[]{-w*.395f,w*.395f})c.Box("Tuning aperture nickel side bezel",new Vector3(x,1.122f,front+.004f),new Vector3(.011f,.163f,.012f),nickel);
            foreach(float y in new[]{1.043f,1.201f})c.Box("Tuning aperture nickel horizontal bezel",new Vector3(0,y,front+.004f),new Vector3(w*.80f,.010f,.012f),nickel);
            for(int i=0;i<=40;i++)
            {
                float length=i%10==0?.040f:i%5==0?.029f:.014f;
                c.Box("Printed physical frequency graduation "+i,new Vector3(-w*.35f+i*w*.0175f,1.153f-length/2,front+.006f),new Vector3(.0017f,length,.001f),ink);
            }
            DeadAirPrecisionEquipment.Print(c.root,"65     68     70     72     74",new Vector3(0,1.073f,front+.008f),Quaternion.identity,w*.69f,.032f,ink.color);
            DeadAirPrecisionEquipment.Print(c.root,"ДВ       СВ       КХ       УКХ",new Vector3(0,1.180f,front+.008f),Quaternion.identity,w*.70f,.024f,ink.color);
            c.Box("Separate red tuning carriage pointer",new Vector3(w*.105f,1.12f,front+.009f),new Vector3(.003f,.105f,.002f),needle);
            foreach(float x in new[]{-w*.30f,0,w*.30f})
            {
                var bezel=c.Cylinder("Rotary control nickel escutcheon",new Vector3(x,.989f,front),.038f,.008f,nickel);bezel.transform.localRotation=Quaternion.Euler(90,0,0);
                var knob=c.Cylinder("Fluted bakelite radio control knob",new Vector3(x,.989f,front+.008f),.030f,.025f,c.rubber);knob.transform.localRotation=Quaternion.Euler(90,0,0);
                for(int i=0;i<12;i++)
                {
                    float a=i*Mathf.PI/6;
                    c.Tube("Control knob moulded grip flute",new Vector3(x+Mathf.Cos(a)*.029f,.989f+Mathf.Sin(a)*.029f,front+.013f),new Vector3(x+Mathf.Cos(a)*.029f,.989f+Mathf.Sin(a)*.029f,front+.030f),.0017f,c.rubber);
                }
                c.Box("Ivory knob position index",new Vector3(x,1.008f,front+.034f),new Vector3(.002f,.010f,.001f),ink);
            }
            DeadAirPrecisionEquipment.Print(c.root,"ГУЧНІСТЬ     ДІАПАЗОН     НАСТРОЙКА",new Vector3(0,.940f,front+.007f),Quaternion.identity,w*.86f,.026f,ink.color);
            DeadAirPrecisionEquipment.Print(c.root,"РАДІОЛА  •  УКХ",new Vector3(0,1.273f,front),Quaternion.identity,w*.60f,.045f,ink.color);
            var pivot=c.Pivot("motion:radioDoor-0",new Vector3(-w/2,0,d/2));var door=c.Child(pivot);
            door.Box("Separate lower speaker service door",new Vector3(w/2,.467f,0),new Vector3(w-.035f,.875f,.046f),c.wood,true);
            door.Box("Recessed woven speaker cloth",new Vector3(w/2,.470f,.027f),new Vector3(w*.80f,.68f,.009f),c.fabric);
            for(int i=0;i<17;i++)door.Tube("Walnut speaker grille vertical lath "+i,new Vector3(w*.12f+i*w*.0475f,.15f,.040f),new Vector3(w*.12f+i*w*.0475f,.79f,.040f),.004f,c.wood);
            foreach(float y in new[]{.12f,.82f})door.Box("Speaker cloth horizontal retaining rail",new Vector3(w/2,y,.042f),new Vector3(w*.82f,.017f,.018f),c.wood);
            foreach(float y in new[]{.18f,.76f})door.Cylinder("Service-door hinge barrel",new Vector3(.011f,y,-.01f),.012f,.065f,nickel);
            door.Box("Small recessed speaker service pull",new Vector3(w*.89f,.12f,.046f),new Vector3(.040f,.013f,.010f),c.rubber);
        }
        static void Kitchen(Kit c,float w,float d)
        {
            int count=Mathf.Max(2,Mathf.RoundToInt(w/.8f));float module=w/count;
            for(int i=0;i<count;i++)
            {
                var unit=c.Child(c.Pivot("Kitchen base unit "+i,new Vector3(-w/2+module*(i+.5f),0,0)));Carcass(unit,module-.018f,d,.82f,c.paint);
                var pivot=unit.Pivot("motion:kitchenDoor-"+i,new Vector3(-module/2+.04f,0,d/2));var door=unit.Child(pivot);
                door.Box("Separate enamel cabinet leaf",new Vector3(module/2-.04f,.44f,0),new Vector3(module-.085f,.70f,.04f),c.paint,true);Pull(door,new Vector3(module-.15f,.66f,.045f),.09f);
                if(i==1)Basin(unit,new Vector3(0,.77f,0),module*.82f,d*.74f);
                else unit.Box("Individual laminate worktop",new Vector3(0,.85f,0),new Vector3(module,.06f,d+.035f),c.ceramic,true);
            }
            c.Box("Ceramic splashback",new Vector3(0,1.10f,-d/2),new Vector3(w,.43f,.035f),c.ceramic);
        }
        static void Vending(Kit c,float w,float d)
        {
            Carcass(c,w,d,2.1f,c.paint);
            c.Box("Soda machine upper front panel",new Vector3(0,1.49f,d/2),new Vector3(w-.04f,1.16f,.07f),c.paint,true);
            c.Box("Soda machine lower front panel",new Vector3(0,.18f,d/2),new Vector3(w-.04f,.30f,.07f),c.paint,true);
            c.Box("Dispensing bay narrow jamb",new Vector3(-w*.46f,.62f,d/2),new Vector3(w*.08f,.58f,.07f),c.paint,true);
            c.Box("Dispensing bay coin-side jamb",new Vector3(w*.34f,.62f,d/2),new Vector3(w*.32f,.58f,.07f),c.paint,true);
            c.Box("Coin mechanism plate",new Vector3(w*.29f,1.38f,d/2+.052f),new Vector3(.19f,.42f,.035f),c.metal);
            c.Box("Coin slot",new Vector3(w*.29f,1.46f,d/2+.075f),new Vector3(.07f,.012f,.012f),c.rubber);
            // The dispensing bay remains physically open between two jambs and a recessed back.
            c.Box("Dispensing bay back",new Vector3(-w*.12f,.60f,d*.27f),new Vector3(w*.60f,.43f,.035f),c.rubber);
            c.Box("Drip tray",new Vector3(-w*.12f,.36f,d*.40f),new Vector3(w*.61f,.035f,d*.21f),c.metal);
            c.Tube("Soda dispensing nozzle",new Vector3(-w*.10f,.80f,d*.36f),new Vector3(-w*.10f,.73f,d*.36f),.018f,c.metal);
            for(int i=0;i<5;i++)c.Box("Drip tray grille slot",new Vector3(-w*.35f+i*w*.12f,.38f,d*.40f),new Vector3(.014f,.005f,d*.17f),c.rubber);
            c.Label("ГАЗОВАНА ВОДА\n1 КОП. / 3 КОП.",new Vector3(0,1.86f,d/2+.05f),w*.81f,.27f);
        }
        static void Noticeboard(Kit c,PropData p,float w)
        {
            float h=p.type=="whiteboard"?1.45f:1.35f,y=1.9f;
            c.Box("Noticeboard recessed backing",new Vector3(0,y,0),new Vector3(w,h,.045f),p.type=="whiteboard"?c.ceramic:c.fabric);
            foreach(float x in new[]{-w/2,w/2})c.Box("Individual board frame rail",new Vector3(x,y,.02f),new Vector3(.065f,h+.10f,.065f),c.wood);
            foreach(float dy in new[]{-h/2,h/2})c.Box("Individual board frame rail",new Vector3(0,y+dy,.02f),new Vector3(w,.065f,.065f),c.wood);
            if(p.type=="toolboard")for(int i=0;i<7;i++)
            {
                float x=-w*.37f+i*w*.12f;c.Tube("Hanging tool shaft",new Vector3(x,1.48f,.08f),new Vector3(x,2.1f,.08f),.012f,c.metal);c.Box("Tool wooden handle",new Vector3(x,1.59f,.08f),new Vector3(.05f,.22f,.05f),c.wood);
                c.Box("Tool head",new Vector3(x,2.1f,.08f),new Vector3(.15f,.05f,.045f),c.metal);
            }
            else if(p.type=="directory")c.Label("РАЙОННИЙ ТЕЛЕЦЕНТР\n1 — РЕДАКЦІЯ / АРХІВ\n2 — СТУДІЯ / АПАРАТНА",new Vector3(0,y,.052f),w*.88f,.8f);
            else for(int i=0;i<6;i++)
            {
                float x=-w*.32f+(i%3)*w*.32f,dy=-.30f+(i/3)*.60f;
                c.Box("Pinned typed notice",new Vector3(x,y+dy,.035f),new Vector3(w*.23f,.48f,.004f),c.paper);
                c.Label(i%2==0?"НАКАЗ\nЧЕРГУВАННЯ\n19.08.1991":"УВАГА\nНЕ ВМИКАТИ",new Vector3(x,y+dy,.04f),w*.20f,.30f);
                c.Cylinder("Notice drawing pin",new Vector3(x,y+dy+.21f,.041f),.008f,.006f,c.metal).transform.localRotation=Quaternion.Euler(90,0,0);
            }
        }
        static void Mirror(Kit c,float w)
        {
            c.Box("mirror:vertical",new Vector3(0,1.9f,.018f),new Vector3(w-.09f,1.14f,.018f),Mat("silvered-mirror","steel",new Color(.78f,.81f,.79f),.96f));
            foreach(float x in new[]{-w/2,w/2})c.Box("Mirror oak frame",new Vector3(x,1.9f,0),new Vector3(.055f,1.26f,.06f),c.wood);
            foreach(float y in new[]{1.30f,2.50f})c.Box("Mirror oak frame",new Vector3(0,y,0),new Vector3(w,.055f,.06f),c.wood);
        }
        static void Backdrop(Kit c,float w)
        {
            var green=Mat("broadcast-chromakey-cloth","fabric",new Color(.12f,.29f,.16f),.03f);
            for(int i=0;i<Mathf.CeilToInt(w/.11f);i++)c.Box("Studio curtain hanging fold",new Vector3(-w/2+i*.11f,1.48f,Mathf.Sin(i*.9f)*.026f),new Vector3(.12f,2.96f,.035f),green);
            foreach(float x in new[]{-w/2,w/2}){c.Tube("Backdrop support stand",new Vector3(x,0,0),new Vector3(x,3.2f,0),.026f,c.metal);c.Box("Backdrop stand foot",new Vector3(x,.025f,0),new Vector3(.35f,.05f,.24f),c.metal,true);}
            c.Tube("Backdrop top crossbar",new Vector3(-w/2,3.07f,0),new Vector3(w/2,3.07f,0),.027f,c.metal);
        }
        static void Fan(Kit c,PropData p)
        {
            c.Box("Fan wall backing",new Vector3(0,1.8f,-.075f),new Vector3(1.2f,1.2f,.04f),c.rubber);
            foreach(float x in new[]{-.60f,.60f})c.Box("Fan mounting frame",new Vector3(x,1.8f,0),new Vector3(.06f,1.25f,.14f),c.metal);
            foreach(float y in new[]{1.20f,2.4f})c.Box("Fan mounting frame",new Vector3(0,y,0),new Vector3(1.25f,.06f,.14f),c.metal);
            var pivot=c.Pivot("fan:"+p.id,new Vector3(0,1.8f,0));var blades=c.Child(pivot);
            blades.Cylinder("Fan motor hub",Vector3.zero,.09f,.11f,c.metal).transform.localRotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<4;i++)
            {float a=i*Mathf.PI*.5f;var blade=blades.Box("Separate pitched fan blade",new Vector3(Mathf.Sin(a)*.26f,Mathf.Cos(a)*.26f,.025f),new Vector3(.18f,.47f,.035f),c.metal);blade.transform.localRotation=Quaternion.Euler(0,14,-i*90);}
            for(int i=0;i<5;i++)c.Ring("Circular fan guard",new Vector3(0,1.8f,.13f),.13f+i*.095f,.006f,c.metal,Quaternion.Euler(90,0,0));
            foreach(float a in new[]{0f,45f,90f,135f}){var guard=c.Box("Radial safety guard",new Vector3(0,1.8f,.13f),new Vector3(.009f,1.05f,.009f),c.metal);guard.transform.localRotation=Quaternion.Euler(0,0,a);}
        }
        static void Dumpster(Kit c,float w,float d)
        {
            Carcass(c,w,d,1.25f,c.paint);
            foreach(float z in new[]{-d/2,d/2})for(int i=0;i<5;i++)c.Box("Pressed container stiffener",new Vector3(-w*.40f+i*w*.2f,.65f,z),new Vector3(.025f,1.1f,.035f),c.metal);
            var lid=c.Child(c.Pivot("motion:dumpsterLid",new Vector3(0,1.28f,-d/2)));lid.Box("Separate hinged container lid",new Vector3(0,0,d/2),new Vector3(w+.08f,.065f,d+.08f),c.paint,true);Pull(lid,new Vector3(0,.06f,d*.90f),.30f);
            foreach(float x in new[]{-w*.38f,w*.38f})foreach(float z in new[]{-d*.36f,d*.36f})c.Cylinder("Container castor",new Vector3(x,-.02f,z),.10f,.09f,c.rubber).transform.localRotation=Quaternion.Euler(0,0,90);
        }
        static void Crate(Kit c,float w,float d)
        {
            float h=Mathf.Min(w,1.3f);
            foreach(float x in new[]{-w*.44f,w*.44f})foreach(float z in new[]{-d*.44f,d*.44f})c.Box("Crate internal corner post",new Vector3(x,h/2,z),new Vector3(.075f,h,.075f),c.wood);
            for(int i=0;i<5;i++)
            {
                float y=h*(i+.5f)/5;
                foreach(float z in new[]{-d/2,d/2})c.Box("Individual crate side plank",new Vector3(0,y,z),new Vector3(w,h/5-.009f,.055f),c.wood);
                foreach(float x in new[]{-w/2,w/2})c.Box("Individual crate end plank",new Vector3(x,y,0),new Vector3(.055f,h/5-.009f,d),c.wood);
            }
            for(int i=0;i<5;i++)c.Box("Separate crate lid plank",new Vector3(-w*.4f+i*w*.2f,h,0),new Vector3(w*.19f,.045f,d),c.wood);
            foreach(float z in new[]{-d*.32f,d*.32f})c.Box("Crate lid cross batten",new Vector3(0,h+.035f,z),new Vector3(w,.035f,.085f),c.wood);
            c.Collision("crate volume",new Vector3(0,h/2,0),new Vector3(w,h,d));c.Label("ТЕЛЕЦЕНТР\nАПАРАТУРА / КРИХКЕ",new Vector3(0,h*.58f,d/2+.032f),w*.65f,h*.30f);
        }
        static void Console(Kit c,float w,float d)
        {
            Feet(c,w,d,.8f,c.metal);c.Box("Mixing console underside",new Vector3(0,.79f,0),new Vector3(w,.08f,d),c.wood,true);
            var wedge=DeadAirFurnitureMeshes.Install(c.root,"Sloping analogue mixing desk",DeadAirFurnitureMeshes.SlopingConsole(w-.10f,d*.8f,.38f,.10f),new Vector3(0,.83f,0),c.paint,true);
            int channels=12;float depth=d*.8f;
            for(int i=0;i<channels;i++)
            {
                float x=-w*.42f+i*w*.84f/(channels-1);
                c.Box("Channel fader slot",new Vector3(x,.975f,depth*.25f),new Vector3(.014f,.012f,.23f),c.rubber);
                c.Box("Separate channel fader cap",new Vector3(x,1.002f+(i%3)*.009f,depth*.24f-(i%3)*.035f),new Vector3(.055f,.027f,.045f),c.paper);
                for(int j=0;j<3;j++)c.Cylinder("Equalizer rotary knob",new Vector3(x,1.10f+j*.042f,-j*.13f),.018f,.024f,c.rubber);
                c.Box("Channel number strip",new Vector3(x,.967f,depth*.38f),new Vector3(.055f,.008f,.035f),c.paper);
            }
            foreach(float x in new[]{-w*.28f,w*.28f}){c.Box("Analogue VU meter glass",new Vector3(x,1.221f,-depth*.37f),new Vector3(.24f,.025f,.10f),c.glass);c.Box("VU meter ivory scale",new Vector3(x,1.21f,-depth*.37f),new Vector3(.22f,.01f,.08f),c.paper);}
        }
        static void FusePanel(Kit c,float w)
        {
            var k=c.Child(c.Pivot("Electrical cabinet assembly",new Vector3(0,1.08f,0)));Carcass(k,w,.26f,1.16f,c.metal);
            for(int i=0;i<8;i++)k.Box("Ceramic fuse carrier",new Vector3((i%4-1.5f)*w*.19f,.27f+(i/4)*.37f,.10f),new Vector3(w*.13f,.21f,.08f),c.ceramic);
            var door=k.Child(k.Pivot("motion:fusePanelDoor",new Vector3(-w/2,0,.16f)));door.Box("Separate electrical service door",new Vector3(w/2,.58f,0),new Vector3(w,1.15f,.035f),c.paint);Pull(door,new Vector3(w-.09f,.58f,.055f),.07f);
            door.Label("380 В\nНЕБЕЗПЕЧНО",new Vector3(w/2,.89f,.024f),w*.72f,.20f);
        }
        static void CRT(Kit c,Vector3 at,float width)
        {
            var t=c.Child(c.Pivot("Complete CRT monitor",at));float h=width*.74f,d=width*.71f;
            t.Box("CRT tapered rear cabinet",new Vector3(0,h/2,-d*.07f),new Vector3(width*.84f,h*.92f,d*.85f),c.paint,true);
            t.Box("CRT front bezel",new Vector3(0,h/2,d*.39f),new Vector3(width,h,.09f),c.rubber);
            var screen=t.Box("monitor:screen",new Vector3(-width*.04f,h*.54f,d*.46f),new Vector3(width*.76f,h*.76f,.028f),c.glass);screen.transform.localScale=Vector3.one;
            for(int i=0;i<2;i++)t.Cylinder("CRT mechanical tuning dial",new Vector3(width*.41f,h*(.25f+i*.23f),d*.48f),.028f,.018f,c.metal).transform.localRotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<9;i++)t.Box("CRT rear ventilation slot",new Vector3(-width*.31f+i*width*.078f,h*.95f,-d*.06f),new Vector3(.012f,.006f,d*.48f),c.rubber);
        }
        static void Rack(Kit c,float w,float d,bool monitors)
        {
            Carcass(c,w,d,2.35f,c.metal);
            foreach(float x in new[]{-w*.45f,w*.45f})c.Box("Rack vertical mounting rail",new Vector3(x,1.20f,d*.43f),new Vector3(.035f,2.25f,.045f),c.rubber);
            if(monitors)for(int i=0;i<3;i++)for(int col=0;col<2;col++)CRT(c,new Vector3((col-.5f)*w*.48f,.13f+i*.72f,d*.09f),w*.44f);
            else for(int i=0;i<9;i++)
            {
                float y=.17f+i*.24f;c.Box("Broadcast equipment rack module",new Vector3(0,y,d*.32f),new Vector3(w*.88f,.21f,d*.38f),c.paint);
                foreach(float x in new[]{-w*.40f,w*.40f})Pull(c,new Vector3(x,y,d*.53f),.07f);
                for(int j=0;j<8;j++)c.Box("Rack ventilation perforation",new Vector3(-w*.26f+j*w*.07f,y+.02f,d*.516f),new Vector3(.012f,.05f,.005f),c.rubber);
                c.Box("Calibrated analogue meter",new Vector3(0,y-.025f,d*.52f),new Vector3(w*.25f,.07f,.007f),c.paper);
            }
        }
        static void Generator(Kit c,float w,float d)
        {
            foreach(float z in new[]{-d*.43f,d*.43f})c.Box("Generator structural skid",new Vector3(0,.10f,z),new Vector3(w+.12f,.20f,.14f),c.metal,true);
            c.Box("Diesel engine acoustic casing",new Vector3(0,1,0),new Vector3(w,1.65f,d),c.paint,true);
            c.Box("Separate top weather cover",new Vector3(0,1.88f,0),new Vector3(w+.08f,.09f,d+.08f),c.metal);
            var panel=c.Child(c.Pivot("motion:generatorServiceDoor",new Vector3(-w*.44f,.3f,-d/2-.025f)));
            panel.Box("Separate engine service panel",new Vector3(w*.25f,.70f,0),new Vector3(w*.50f,1.34f,.04f),c.paint);Pull(panel,new Vector3(w*.45f,.7f,-.045f));
            c.Box("Radiator recess",new Vector3(w*.27f,1,d/2+.015f),new Vector3(w*.38f,1.15f,.025f),c.rubber);
            for(int i=0;i<10;i++)c.Box("Separate radiator ventilation louvre",new Vector3(w*.27f,.49f+i*.115f,d/2+.045f),new Vector3(w*.36f,.035f,.065f),c.metal);
            c.Tube("Diesel exhaust pipe",new Vector3(-w*.32f,1.78f,-d*.22f),new Vector3(-w*.32f,2.27f,-d*.22f),.065f,c.metal);
            c.Cylinder("Exhaust rain cap",new Vector3(-w*.32f,2.29f,-d*.22f),.095f,.02f,c.metal);
            c.Label("ДИЗЕЛЬ / 380 В\nАВАРІЙНЕ ЖИВЛЕННЯ",new Vector3(-w*.25f,1.46f,d/2+.025f),w*.38f,.3f);
            for(int i=0;i<3;i++)c.Cylinder("Generator analogue gauge",new Vector3(-w*.36f+i*w*.12f,1.05f,d/2+.035f),.06f,.018f,c.paper).transform.localRotation=Quaternion.Euler(90,0,0);
        }
        static void Sedan(Kit c,float w,float d)
        {
            var paint=Mat("saloon-dull-sage-body","painted-metal",new Color(.85f,.89f,.84f),.30f);
            var chrome=Mat("saloon-aged-chrome","steel",new Color(.83f,.85f,.82f),.64f);
            var cabin=Mat("saloon-dark-seat-cloth","fabric",new Color(.16f,.17f,.15f),.04f);
            var glass=VehicleGlazing("saloon-smoked-glass");
            var lamp=Mat("saloon-amber-indicator","ceramic",new Color(.67f,.32f,.08f),.62f);
            var tail=Mat("saloon-red-tail-lens","ceramic",new Color(.43f,.065f,.035f),.65f);
            void Skin(string name,Vector3[][] grid,Vector3 normal,Material material)
                =>DeadAirFurnitureMeshes.Install(c.root,name,DeadAirFurnitureMeshes.Surface("sedan-v2-"+name,grid,normal),Vector3.zero,material);
            float Half(float z)=>w*(.455f-.035f*Mathf.Pow(Mathf.Abs(z)/(d*.475f),6));
            float Belt(float z)=>.91f-.11f*Mathf.Pow(Mathf.Abs(z)/(d*.475f),5);
            float Lower(float z)
            {
                float bottom=.43f;
                foreach(float axle in new[]{-d*.31f,d*.31f}){float delta=Mathf.Abs(z-axle);if(delta<.385f)bottom=Mathf.Max(bottom,.32f+Mathf.Sqrt(.385f*.385f-delta*delta));}
                return bottom;
            }
            foreach(int side in new[]{-1,1})
            {
                var panel=new Vector3[101][];
                for(int i=0;i<panel.Length;i++)
                {
                    float z=Mathf.Lerp(-d*.475f,d*.475f,i/100f),half=Half(z),top=Belt(z);
                    panel[i]=new[]{new Vector3(side*(half-.028f),Lower(z),z),new Vector3(side*half,top-.07f,z),new Vector3(side*(half-.025f),top,z)};
                }
                Skin((side<0?"Left":"Right")+" pressed quarter and door skin with open wheel arches",panel,Vector3.right*side,paint);
                var waist=new Vector3[25][];
                for(int i=0;i<waist.Length;i++){float z=Mathf.Lerp(-d*.255f,d*.235f,i/24f);waist[i]=new[]{new Vector3(side*(Half(z)-.025f),Belt(z),z),new Vector3(side*w*.425f,.965f,z)};}
                Skin("Painted window sill shoulder return "+side,waist,Vector3.up,paint);
                foreach(float axle in new[]{-d*.31f,d*.31f})
                {
                    var arch=new Vector3[33][];
                    for(int i=0;i<arch.Length;i++)
                    {
                        float angle=Mathf.Lerp(.14f,Mathf.PI-.14f,i/32f),z=axle+Mathf.Cos(angle)*.385f,y=.32f+Mathf.Sin(angle)*.385f;
                        float x=side*(Half(z)-.023f);
                        arch[i]=new[]{new Vector3(x,y,z),new Vector3(x-side*.038f,y+.016f,z),new Vector3(x-side*.09f,y+.014f,z)};
                    }
                    Skin("Rolled wheel arch return "+side+" "+axle,arch,Vector3.right*side,paint);
                }
                c.Tube("Separate sill bright trim "+side,new Vector3(side*w*.448f,.455f,-d*.20f),new Vector3(side*w*.448f,.455f,d*.19f),.013f,chrome);
                c.Tube("Door shut line "+side,new Vector3(side*w*.456f,.48f,-d*.018f),new Vector3(side*w*.456f,.90f,-d*.018f),.0045f,c.rubber);
                foreach(float z in new[]{-d*.15f,d*.10f})
                {
                    c.Box("Recessed handle pocket",new Vector3(side*w*.457f,.84f,z),new Vector3(.014f,.044f,.19f),c.rubber);
                    c.Tube("Separate chrome pull handle",new Vector3(side*w*.466f,.85f,z-.065f),new Vector3(side*w*.466f,.85f,z+.065f),.010f,chrome);
                }
            }
            void Deck(string name,float back,float front,bool roof)
            {
                var grid=new Vector3[13][];
                for(int row=0;row<grid.Length;row++)
                {
                    float z=Mathf.Lerp(back,front,row/12f);grid[row]=new Vector3[17];
                    for(int col=0;col<17;col++)
                    {
                        float u=(col-8)/8f,half=roof?w*.352f:Half(z)-.025f;
                        float y=roof?1.425f+.067f*(1-u*u)+.025f*Mathf.Sin(row/12f*Mathf.PI):Belt(z)+.026f*(1-u*u);
                        grid[row][col]=new Vector3(u*half,y,z);
                    }
                }
                Skin(name,grid,Vector3.up,paint);
            }
            Deck("Tapered crowned engine bonnet",d*.235f,d*.475f,false);
            Deck("Tapered rear boot lid",-d*.475f,-d*.255f,false);
            Deck("Continuously crowned steel roof",-d*.13f,d*.075f,true);
            foreach(int end in new[]{-1,1})
            {
                float z=end*d*.475f,half=Half(z);
                Skin("Curved end valance "+end,new[]{new[]{new Vector3(-half,.43f,z),new Vector3(0,.42f,z+end*.018f),new Vector3(half,.43f,z)},new[]{new Vector3(-half,.80f,z),new Vector3(0,.82f,z+end*.018f),new Vector3(half,.80f,z)}},Vector3.forward*end,paint);
                c.Tube("Separate bumper blade "+end,new Vector3(-w*.43f,.49f,z+end*.035f),new Vector3(w*.43f,.49f,z+end*.035f),.035f,chrome);
                c.Box("Registration plate "+end,new Vector3(0,.49f,z+end*.077f),new Vector3(.43f,.105f,.008f),c.paper);
            }
            void Glaze(string name,Vector3 a,Vector3 b,Vector3 topA,Vector3 topB,Vector3 normal)
            {
                Skin(name,new[]{new[]{a,b},new[]{topA,topB}},normal,glass);
                foreach(var edge in new[]{new[]{a,b},new[]{b,topB},new[]{topB,topA},new[]{topA,a}})c.Tube("Independent glazing rubber seal",edge[0],edge[1],.012f,c.rubber);
            }
            Glaze("Sloped separate front windscreen",new Vector3(-w*.417f,.957f,d*.235f),new Vector3(w*.417f,.957f,d*.235f),new Vector3(-w*.342f,1.426f,d*.075f),new Vector3(w*.342f,1.426f,d*.075f),Vector3.forward);
            Glaze("Sloped separate rear windscreen",new Vector3(w*.417f,.957f,-d*.255f),new Vector3(-w*.417f,.957f,-d*.255f),new Vector3(w*.342f,1.426f,-d*.13f),new Vector3(-w*.342f,1.426f,-d*.13f),Vector3.back);
            foreach(int side in new[]{-1,1})
            {
                Vector3 LowerEdge(float z)=>new Vector3(side*w*.425f,.965f,z);
                Vector3 UpperEdge(float z)=>new Vector3(side*w*.351f,1.423f,z);
                Glaze("Front door trapezoid glass "+side,LowerEdge(-d*.012f),LowerEdge(d*.218f),UpperEdge(-d*.012f),UpperEdge(d*.071f),Vector3.right*side);
                Glaze("Rear door trapezoid glass "+side,LowerEdge(-d*.238f),LowerEdge(-d*.035f),UpperEdge(-d*.125f),UpperEdge(-d*.035f),Vector3.right*side);
                foreach(var ends in new[]{new[]{LowerEdge(d*.231f),UpperEdge(d*.079f)},new[]{LowerEdge(-d*.024f),UpperEdge(-d*.024f)},new[]{LowerEdge(-d*.25f),UpperEdge(-d*.136f)}})
                    c.Tube("Independent A B C roof pillar",ends[0],ends[1],.028f,paint);
                c.Tube("Chrome window belt moulding",LowerEdge(-d*.255f),LowerEdge(d*.235f),.012f,chrome);
                c.Tube("Curved roof rain gutter",UpperEdge(-d*.135f),UpperEdge(d*.081f),.012f,chrome);
                var mirror=c.Box("Separate wing mirror housing",new Vector3(side*w*.466f,.99f,d*.17f),new Vector3(.12f,.09f,.17f),paint);
                c.Tube("Wing mirror mounting stalk",new Vector3(side*w*.43f,.95f,d*.17f),mirror.transform.localPosition,.012f,chrome);
            }
            c.Box("Cabin floor pan",new Vector3(0,.39f,0),new Vector3(w*.73f,.10f,d*.55f),c.rubber);
            foreach(float x in new[]{-w*.21f,w*.21f})foreach(float z in new[]{-d*.135f,d*.07f})
            {
                c.Box("Upholstered seat cushion",new Vector3(x,.55f,z),new Vector3(w*.35f,.16f,.48f),cabin);
                var back=c.Box("Separate upholstered seat back",new Vector3(x,.79f,z-.23f),new Vector3(w*.35f,.53f,.12f),cabin);back.transform.localRotation=Quaternion.Euler(-9,0,0);
            }
            c.Box("Inset dashboard fascia",new Vector3(0,.89f,d*.18f),new Vector3(w*.77f,.12f,.22f),c.rubber);
            c.Ring("Three spoke steering wheel",new Vector3(-w*.23f,1.00f,d*.14f),.135f,.014f,c.rubber,Quaternion.Euler(64,0,0));
            foreach(int side in new[]{-1,1})foreach(float axle in new[]{-d*.31f,d*.31f})
            {
                float x=side*w*.428f;var tyre=new Vector3[7][];
                float[] offsets={-.105f,-.099f,-.076f,0,.076f,.099f,.105f},radii={.255f,.295f,.322f,.329f,.322f,.295f,.255f};
                for(int row=0;row<7;row++){tyre[row]=new Vector3[49];for(int i=0;i<=48;i++){float a=i*Mathf.PI*2/48;tyre[row][i]=new Vector3(x+offsets[row],.329f+Mathf.Cos(a)*radii[row],axle+Mathf.Sin(a)*radii[row]);}}
                Skin("Separate rounded tyre "+side+" "+axle,tyre,Vector3.up,c.rubber);
                var wheel=c.Cylinder("Stamped steel rim dish",new Vector3(x+side*.088f,.329f,axle),.224f,.018f,chrome);wheel.transform.localRotation=Quaternion.Euler(0,0,-side*90);
                var hub=c.Cylinder("Dished chrome hub cap",new Vector3(x+side*.115f,.329f,axle),.125f,.025f,chrome);hub.transform.localRotation=Quaternion.Euler(0,0,-side*90);
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4;var vent=c.Cylinder("Recessed wheel ventilation hole",new Vector3(x+side*.113f,.329f+Mathf.Cos(a)*.18f,axle+Mathf.Sin(a)*.18f),.018f,.004f,c.rubber);vent.transform.localRotation=Quaternion.Euler(0,0,-side*90);}
            }
            c.Box("Inset horizontal radiator opening",new Vector3(0,.704f,d*.485f),new Vector3(w*.42f,.18f,.026f),c.rubber);
            for(int i=0;i<7;i++)c.Tube("Fine horizontal grille bar",new Vector3(-w*.20f,.63f+i*.024f,d*.495f),new Vector3(w*.20f,.63f+i*.024f,d*.495f),.005f,chrome);
            foreach(int side in new[]{-1,1})
            {
                var rim=c.Cylinder("Round chrome headlamp bezel",new Vector3(side*w*.322f,.707f,d*.475f+.008f),.125f,.028f,chrome);rim.transform.localRotation=Quaternion.Euler(90,0,0);
                var lens=c.Cylinder("Separate ribbed headlamp lens",new Vector3(side*w*.322f,.707f,d*.475f+.043f),.106f,.012f,c.ceramic);lens.transform.localRotation=Quaternion.Euler(90,0,0);
                c.Box("Amber side indicator",new Vector3(side*w*.391f,.547f,d*.487f),new Vector3(.115f,.062f,.024f),lamp);
                c.Box("Rear tail lamp lens",new Vector3(side*w*.332f,.707f,-d*.488f),new Vector3(.29f,.118f,.024f),tail);
                c.Tube("Separate windscreen wiper",new Vector3(side*w*.12f,.972f,d*.235f),new Vector3(side*w*.26f,1.09f,d*.193f),.006f,c.rubber);
            }
            // Keep the existing navigation and player-contact volumes while changing the visible shell.
            c.Collision("saloon chassis proxy",new Vector3(0,.255f,0),new Vector3(w*.86f,.24f,d*.88f));
            c.Collision("saloon lower body proxy",new Vector3(0,.71f,0),new Vector3(w,.53f,d*.92f));
            c.Collision("saloon cabin proxy",new Vector3(0,1.29f,-d*.025f),new Vector3(w*.80f,.66f,d*.47f));
        }

        static Material VehicleGlazing(string name)
        {
            var material=Mat(name,"glass",new Color(.12f,.18f,.19f,.18f),.89f);
            var shader=Shader.Find("Dead Air/Window Glass");
            if(shader==null)throw new InvalidOperationException("Vehicle glazing shader is missing.");
            material.shader=shader;material.color=new Color(.12f,.18f,.19f,.18f);
            material.SetFloat("_Glossiness",.89f);material.SetFloat("_ReflectionStrength",.17f);
            UnityEditor.EditorUtility.SetDirty(material);return material;
        }

        static void Truck(Kit c,float w,float d)
        {
            var paint=Mat("gaz66-weathered-olive-body","painted-metal",new Color(.85f,.88f,.71f),.24f);
            var trim=Mat("gaz66-darkened-steel-fittings","steel",new Color(.39f,.42f,.38f),.34f);
            var glass=VehicleGlazing("gaz66-separate-glazing");
            void Skin(string name,Vector3[][] grid,Vector3 normal,Material mat)
                =>DeadAirFurnitureMeshes.Install(c.root,name,DeadAirFurnitureMeshes.Surface("gaz66-v2-"+name,grid,normal),Vector3.zero,mat);
            float Half(float z)=>w*(.431f-.023f*Mathf.Pow(Mathf.Abs(z-d*.235f)/(d*.235f),4));
            foreach(int side in new[]{-1,1})
            {
                var flank=new Vector3[81][];
                for(int i=0;i<flank.Length;i++)
                {
                    float z=Mathf.Lerp(d*.015f,d*.455f,i/80f),delta=Mathf.Abs(z-d*.30f),low=.77f;
                    if(delta<.51f)low=Mathf.Max(low,.43f+Mathf.Sqrt(.51f*.51f-delta*delta));
                    float x=side*Half(z);flank[i]=new[]{new Vector3(x-side*.025f,low,z),new Vector3(x,1.25f,z),new Vector3(x-side*.028f,1.37f,z)};
                }
                Skin("Cab pressed side panel with real front wheel opening "+side,flank,Vector3.right*side,paint);
                var arch=new Vector3[33][];
                for(int i=0;i<arch.Length;i++){float a=Mathf.Lerp(.12f,Mathf.PI-.12f,i/32f),z=d*.30f+Mathf.Cos(a)*.51f,y=.43f+Mathf.Sin(a)*.51f,x=side*(Half(z)-.02f);arch[i]=new[]{new Vector3(x,y,z),new Vector3(x+side*.04f,y+.02f,z),new Vector3(x-side*.09f,y+.035f,z)};}
                Skin("Rounded cab wheel arch lip "+side,arch,Vector3.right*side,paint);
                Vector3 loRear=new Vector3(side*w*.416f,1.38f,d*.055f),loFront=new Vector3(side*w*.416f,1.38f,d*.418f);
                Vector3 hiRear=new Vector3(side*w*.380f,2.045f,d*.091f),hiFront=new Vector3(side*w*.380f,2.045f,d*.365f);
                Skin("Separate sloping cab door glass "+side,new[]{new[]{loRear,loFront},new[]{hiRear,hiFront}},Vector3.right*side,glass);
                foreach(var edge in new[]{new[]{loRear,loFront},new[]{loRear,hiRear},new[]{hiRear,hiFront},new[]{loFront,hiFront}})c.Tube("Truck glazing rubber and pillar",edge[0],edge[1],.024f,c.rubber);
                c.Tube("Rear cab pillar",loRear,hiRear,.046f,paint);c.Tube("Front sloped cab pillar",loFront,hiFront,.042f,paint);
                c.Tube("Cab door shut line",new Vector3(side*w*.433f,.83f,d*.065f),loRear,.005f,c.rubber);
                c.Tube("Separate truck door pull",new Vector3(side*w*.44f,1.24f,d*.09f),new Vector3(side*w*.44f,1.24f,d*.125f),.013f,trim);
                c.Tube("External mirror support",new Vector3(side*w*.427f,1.55f,d*.34f),new Vector3(side*w*.483f,1.70f,d*.34f),.013f,trim);
                c.Box("Separate tall truck mirror",new Vector3(side*w*.484f,1.75f,d*.34f),new Vector3(.08f,.27f,.13f),trim);
                c.Box("Separate cab entry step",new Vector3(side*w*.432f,.60f,d*.105f),new Vector3(.27f,.055f,.49f),trim);
            }
            var roof=new Vector3[13][];
            for(int i=0;i<roof.Length;i++){float z=Mathf.Lerp(d*.085f,d*.375f,i/12f);roof[i]=new Vector3[17];for(int j=0;j<17;j++){float u=(j-8)/8f;roof[i][j]=new Vector3(u*w*.385f,2.075f+.062f*(1-u*u)+.006f*Mathf.Sin(i/12f*Mathf.PI),z);}}
            Skin("Crowned rolled steel cab roof",roof,Vector3.up,paint);
            var deck=new Vector3[13][];
            for(int i=0;i<deck.Length;i++){float z=Mathf.Lerp(d*.418f,d*.455f,i/12f);deck[i]=new Vector3[13];for(int j=0;j<13;j++){float u=(j-6)/6f;deck[i][j]=new Vector3(u*(Half(z)-.025f),1.37f-.08f*i/12f+.018f*(1-u*u),z);}}
            Skin("Sloping pressed cab nose top",deck,Vector3.up,paint);
            Skin("Front valance with curved shoulder",new[]{new[]{new Vector3(-w*.40f,.80f,d*.455f),new Vector3(0,.78f,d*.472f),new Vector3(w*.40f,.80f,d*.455f)},new[]{new Vector3(-w*.40f,1.29f,d*.455f),new Vector3(0,1.30f,d*.472f),new Vector3(w*.40f,1.29f,d*.455f)}},Vector3.forward,paint);
            Skin("Cab rear sheet",new[]{new[]{new Vector3(-w*.416f,1.34f,d*.055f),new Vector3(w*.416f,1.34f,d*.055f)},new[]{new Vector3(-w*.38f,2.045f,d*.091f),new Vector3(w*.38f,2.045f,d*.091f)}},Vector3.back,paint);
            for(int side=-1;side<=1;side+=2)
            {
                float x0=side<0?-w*.371f:w*.012f,x1=side<0?-w*.012f:w*.371f;
                Skin("Separate split sloping windscreen "+side,new[]{new[]{new Vector3(x0,1.40f,d*.422f),new Vector3(x1,1.40f,d*.422f)},new[]{new Vector3(x0*.94f,2.045f,d*.365f),new Vector3(x1*.94f,2.045f,d*.365f)}},Vector3.forward,glass);
                c.Tube("Separate truck windscreen wiper",new Vector3(side*w*.16f,1.414f,d*.428f),new Vector3(side*w*.27f,1.61f,d*.411f),.009f,c.rubber);
            }
            c.Tube("Windscreen centre mullion",new Vector3(0,1.39f,d*.424f),new Vector3(0,2.05f,d*.365f),.019f,paint);
            c.Tube("Windscreen upper weather seal",new Vector3(-w*.35f,2.045f,d*.365f),new Vector3(w*.35f,2.045f,d*.365f),.022f,c.rubber);
            c.Tube("Windscreen lower weather seal",new Vector3(-w*.372f,1.40f,d*.422f),new Vector3(w*.372f,1.40f,d*.422f),.022f,c.rubber);
            c.Box("Inset truck dashboard",new Vector3(0,1.35f,d*.36f),new Vector3(w*.75f,.13f,.25f),c.rubber);
            foreach(float x in new[]{-w*.20f,w*.20f}){c.Box("Truck seat cushion",new Vector3(x,1.02f,d*.20f),new Vector3(.68f,.16f,.52f),c.fabric);c.Box("Truck seat back",new Vector3(x,1.28f,d*.12f),new Vector3(.68f,.55f,.12f),c.fabric);}
            c.Box("Flatbed separate timber deck",new Vector3(0,.91f,-d*.24f),new Vector3(w,.12f,d*.49f),c.wood,true);
            foreach(float x in new[]{-w*.485f,w*.485f})for(int i=0;i<4;i++)c.Box("Independent cargo bed plank",new Vector3(x,1.07f+i*.13f,-d*.24f),new Vector3(.055f,.115f,d*.49f),c.wood,true);
            foreach(float z in new[]{-.48f*d,-.015f*d})for(int i=0;i<4;i++)c.Box("Cargo end gate plank",new Vector3(0,1.07f+i*.13f,z),new Vector3(w,.115f,.055f),c.wood);
            foreach(float x in new[]{-w*.485f,w*.485f})foreach(float z in new[]{-d*.44f,-d*.24f,-d*.045f})c.Box("Separate cargo stanchion",new Vector3(x,1.25f,z),new Vector3(.055f,.64f,.065f),trim);
            foreach(int side in new[]{-1,1})foreach(float axle in new[]{-d*.30f,d*.30f})
            {
                float x=side*w*.448f;var tyre=new Vector3[7][];float[] offsets={-.115f,-.105f,-.08f,0,.08f,.105f,.115f},radii={.32f,.39f,.425f,.43f,.425f,.39f,.32f};
                for(int row=0;row<7;row++){tyre[row]=new Vector3[49];for(int j=0;j<=48;j++){float a=j*Mathf.PI*2/48;tyre[row][j]=new Vector3(x+offsets[row],.43f+Mathf.Cos(a)*radii[row],axle+Mathf.Sin(a)*radii[row]);}}
                Skin("Rounded offroad tyre "+side+" "+axle,tyre,Vector3.up,c.rubber);
                for(int i=0;i<24;i++){float angle=i*15f,a=angle*Mathf.Deg2Rad;var tread=c.Box("Individual offroad tread block",new Vector3(x,.43f+Mathf.Cos(a)*.432f,axle+Mathf.Sin(a)*.432f),new Vector3(.17f,.019f,.085f),c.rubber);tread.transform.localRotation=Quaternion.Euler(angle,0,0);}
                var rim=c.Cylinder("Dished heavy steel wheel",new Vector3(x+side*.09f,.43f,axle),.295f,.024f,trim);rim.transform.localRotation=Quaternion.Euler(0,0,-side*90);
                var hub=c.Cylinder("Separate axle hub",new Vector3(x+side*.11f,.43f,axle),.125f,.020f,trim);hub.transform.localRotation=Quaternion.Euler(0,0,-side*90);
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4;var bolt=c.Cylinder("Truck rim lug nut",new Vector3(x+side*.115f,.43f+Mathf.Cos(a)*.19f,axle+Mathf.Sin(a)*.19f),.018f,.012f,trim);bolt.transform.localRotation=Quaternion.Euler(0,0,-side*90);}
            }
            c.Box("Recessed truck radiator grille",new Vector3(0,1.07f,d*.474f),new Vector3(w*.46f,.35f,.04f),c.rubber);
            for(int i=0;i<11;i++)c.Tube("Narrow stamped grille louvre",new Vector3(-w*.21f+i*w*.042f,.91f,d*.478f),new Vector3(-w*.21f+i*w*.042f,1.23f,d*.478f),.011f,trim);
            foreach(int side in new[]{-1,1}){var rim=c.Cylinder("Truck headlamp rim",new Vector3(side*w*.32f,1.07f,d*.46f),.15f,.035f,trim);rim.transform.localRotation=Quaternion.Euler(90,0,0);var lens=c.Cylinder("Truck independent glass headlamp",new Vector3(side*w*.32f,1.07f,d*.47f),.128f,.014f,c.ceramic);lens.transform.localRotation=Quaternion.Euler(90,0,0);}
            c.Box("Separate heavy bumper",new Vector3(0,.67f,d*.475f),new Vector3(w*.94f,.14f,.17f),trim);
            c.Collision("truck chassis proxy",new Vector3(0,.43f*.85f,0),new Vector3(w*.86f,.24f,d*.88f));
            c.Collision("truck bonnet proxy",new Vector3(0,1.13f,d*.32f),new Vector3(w*.85f,.58f,d*.27f));
            c.Collision("truck cabin proxy",new Vector3(0,1.45f,d*.10f),new Vector3(w*.89f,1.39f,d*.29f));
        }

        static void CableSpool(Kit c)
        {
            foreach(float y in new[]{.035f,.73f})
            {c.Cylinder("Independent wooden cable flange",new Vector3(0,y,0),.55f,.07f,c.wood);for(int i=0;i<6;i++){float a=i*Mathf.PI/3;c.Cylinder("Cable flange bolt",new Vector3(Mathf.Sin(a)*.37f,y+.071f,Mathf.Cos(a)*.37f),.018f,.006f,c.metal);}}
            c.Cylinder("Wooden cable reel hub",new Vector3(0,.10f,0),.22f,.63f,c.wood);
            for(int i=0;i<21;i++)c.Ring("Individual high-voltage cable winding",new Vector3(0,.13f+i*.027f,0),.39f,.014f,c.rubber,Quaternion.identity);
            c.Collision("spool",new Vector3(0,.42f,0),new Vector3(1.1f,.8f,1.1f));
        }
        static void Duct(Kit c)
        {
            var zinc=Mat("ventilation-zinc","duct-zinc",new Color(.78f,.80f,.82f),.58f);
            c.Box("Galvanized ventilation trunk",new Vector3(0,1.75f,0),new Vector3(1.05f,3.5f,1.05f),zinc,true);
            foreach(float y in new[]{.12f,1.74f,3.36f})
            {foreach(float x in new[]{-.55f,.55f})c.Box("Duct flange side",new Vector3(x,y,0),new Vector3(.055f,.07f,1.15f),zinc);foreach(float z in new[]{-.55f,.55f})c.Box("Duct flange end",new Vector3(0,y,z),new Vector3(1.15f,.07f,.055f),zinc);}
            var door=c.Child(c.Pivot("motion:ductInspectionDoor",new Vector3(-.3f,.92f,.54f)));door.metal=zinc;
            door.Box("Separate duct inspection plate",new Vector3(.3f,.3f,0),new Vector3(.60f,.60f,.028f),zinc);Pull(door,new Vector3(.5f,.3f,.04f),.08f);
        }
        static void FireBoard(Kit c)
        {
            var red=Mat("fire-safety-red-enamel","ceramic",new Color(.50f,.09f,.045f),.28f);
            c.Box("Steel fire safety shield",new Vector3(0,1.75f,0),new Vector3(1.30f,1.30f,.06f),red);
            c.Label("ПОЖЕЖНИЙ ЩИТ\nПІСОК / ВОДА",new Vector3(0,2.18f,.042f),1.10f,.22f);
            var bucket=DeadAirFurnitureMeshes.Install(c.root,"Open fire bucket",DeadAirFurnitureMeshes.OpenVessel(.30f,.08f,.16f,.009f),new Vector3(-.30f,1.27f,.15f),red);c.Ring("Fire bucket handle",new Vector3(-.30f,1.61f,.15f),.145f,.008f,c.metal,Quaternion.Euler(90,0,0));
            c.Tube("Fire shovel timber shaft",new Vector3(.28f,1.27f,.10f),new Vector3(.28f,2.02f,.10f),.015f,c.wood);
            c.Box("Curved steel shovel blade",new Vector3(.28f,1.20f,.10f),new Vector3(.18f,.25f,.025f),c.metal);
            c.Tube("Fire hook",new Vector3(-.05f,1.25f,.10f),new Vector3(-.05f,2.05f,.10f),.013f,c.metal);
        }
        static void Turnstile(Kit c)
        {
            c.Box("Turnstile enamel pedestal",new Vector3(0,.55f,0),new Vector3(.44f,1.10f,.64f),c.paint,true);c.Box("Turnstile polished top",new Vector3(0,1.12f,0),new Vector3(.49f,.055f,.69f),c.metal);
            var hub=c.Child(c.Pivot("motion:turnstileHub",new Vector3(.26f,.87f,0)));
            for(int i=0;i<3;i++){float a=i*Mathf.PI*2/3;hub.Tube("Separate turnstile radial arm",Vector3.zero,new Vector3(.18f,Mathf.Cos(a)*.47f,Mathf.Sin(a)*.47f),.018f,c.metal);}
            c.Box("Pass card slot",new Vector3(0,1.15f,.16f),new Vector3(.13f,.008f,.022f),c.rubber);
        }
        static void Booth(Kit c,float w,float d)
        {
            c.Box("Guard booth floor platform",new Vector3(0,.08f,0),new Vector3(w,.16f,d),c.metal,true);
            foreach(float x in new[]{-w*.47f,w*.47f})foreach(float z in new[]{-d*.47f,d*.47f})c.Box("Separate guard booth corner post",new Vector3(x,1.3f,z),new Vector3(.075f,2.5f,.075f),c.metal,true);
            foreach(float x in new[]{-w*.48f,w*.48f})
            {c.Box("Guard booth low side panel",new Vector3(x,.7f,0),new Vector3(.055f,1.15f,d),c.paint,true);c.Box("Guard booth side glass",new Vector3(x,1.7f,0),new Vector3(.025f,.84f,d*.88f),c.glass,true);}
            c.Box("Guard booth back low panel",new Vector3(0,.7f,-d*.48f),new Vector3(w,1.15f,.055f),c.paint,true);
            c.Box("Guard booth back glass",new Vector3(0,1.70f,-d*.48f),new Vector3(w*.88f,.84f,.025f),c.glass,true);
            c.Box("Guard booth front panel",new Vector3(-w*.27f,.70f,d*.48f),new Vector3(w*.44f,1.15f,.055f),c.paint,true);
            c.Box("Guard booth front window",new Vector3(-w*.27f,1.70f,d*.48f),new Vector3(w*.40f,.84f,.025f),c.glass,true);
            c.Box("Guard booth weather roof",new Vector3(0,2.55f,0),new Vector3(w+.18f,.12f,d+.18f),c.metal);
            c.Box("Guard booth desk",new Vector3(0,.91f,-d*.30f),new Vector3(w*.80f,.06f,.46f),c.wood,true);
            c.Label("ПРОПУСКНИЙ ПУНКТ",new Vector3(0,2.32f,d*.49f+.015f),w*.86f,.18f);
        }
        static void Floodlight(Kit c)
        {
            foreach(float x in new[]{-.35f,.35f})foreach(float z in new[]{-.35f,.35f})c.Tube("Tower steel upright",new Vector3(x,0,z),new Vector3(x,7.35f,z),.035f,c.metal);
            for(int i=0;i<7;i++)foreach(float z in new[]{-.35f,.35f}){c.Tube("Tower diagonal cross brace",new Vector3(-.35f,i,z),new Vector3(.35f,i+1,z),.016f,c.metal);c.Tube("Tower cross brace",new Vector3(.35f,i,z),new Vector3(-.35f,i+1,z),.016f,c.metal);}
            c.Collision("tower support",new Vector3(0,3.6f,0),new Vector3(.8f,7.2f,.8f));
            for(int i=0;i<3;i++){float x=(i-1)*.45f;c.Box("Floodlight weather housing",new Vector3(x,7.45f,.2f),new Vector3(.39f,.34f,.25f),c.metal);c.Box("Floodlight recessed glass",new Vector3(x,7.45f,.335f),new Vector3(.33f,.27f,.018f),c.ceramic);}
        }
        static void Barrier(Kit c,float w)
        {
            c.Box("Gate barrier control pedestal",new Vector3(-w/2+.30f,.65f,0),new Vector3(.48f,1.30f,.43f),c.paint,true);
            var pivot=c.Child(c.Pivot("motion:barrierBoom",new Vector3(-w/2+.30f,1.20f,0)));pivot.root.localRotation=Quaternion.Euler(0,0,78);
            pivot.Box("Separate raised gate boom",new Vector3(w*.43f,0,0),new Vector3(w*.86f,.105f,.10f),c.paper);
            var red=Mat("barrier-red","ceramic",new Color(.58f,.08f,.04f),.3f);
            for(int i=0;i<8;i++)pivot.Box("Gate reflective red band",new Vector3(w*.10f+i*w*.095f,0,.052f),new Vector3(w*.045f,.109f,.004f),red);
        }
        static void Camera(Kit c)
        {
            foreach(float a in new[]{0f,120f,240f}){float angle=a*Mathf.Deg2Rad;c.Tube("Broadcast camera tripod leg",new Vector3(Mathf.Sin(angle)*.46f,0,Mathf.Cos(angle)*.46f),new Vector3(0,1.25f,0),.023f,c.metal);}
            c.Cylinder("Camera pan head",new Vector3(0,1.22f,0),.12f,.22f,c.metal);
            var head=c.Child(c.Pivot("motion:cameraHead",new Vector3(0,1.45f,0)));
            head.Box("Broadcast camera body",new Vector3(0,.20f,.09f),new Vector3(.48f,.40f,.77f),c.paint);
            head.Cylinder("Broadcast camera lens barrel",new Vector3(0,.2f,.49f),.11f,.25f,c.rubber).transform.localRotation=Quaternion.Euler(90,0,0);
            head.Cylinder("Broadcast camera objective glass",new Vector3(0,.2f,.755f),.093f,.014f,c.glass).transform.localRotation=Quaternion.Euler(90,0,0);
            head.Box("Camera viewfinder",new Vector3(.31f,.35f,.08f),new Vector3(.15f,.13f,.27f),c.rubber);
            head.Tube("Camera pan handle",new Vector3(.22f,0,-.22f),new Vector3(.35f,-.06f,-.66f),.014f,c.metal);
            head.Label("ТК-04",new Vector3(0,.29f,.49f),.20f,.08f);c.Collision("tripod",new Vector3(0,.7f,0),new Vector3(.68f,1.4f,.68f));
        }
        static void Trash(Kit c)
        {
            DeadAirFurnitureMeshes.Install(c.root,"Open pressed steel waste bin",DeadAirFurnitureMeshes.OpenVessel(.61f,.18f,.24f,.009f),Vector3.zero,c.metal);
            c.Cylinder("Crumpled paper in bin",new Vector3(0,.20f,0),.17f,.10f,c.paper);c.Collision("bin walls",new Vector3(0,.30f,0),new Vector3(.46f,.60f,.46f));
        }
    }
}
