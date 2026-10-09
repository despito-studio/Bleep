using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Modern high-roof expedition van: separate movable visuals and a permanently supported walkable deck.</summary>
    public static class DeadAirModernVanBuilder
    {
        static Material Material(string name,Color color,float gloss=.25f,bool transparent=false)
        {
            string path="Assets/Office/Materials/van-"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Dead Air/Source Surface"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_Color",color);m.SetColor("_EmissionColor",Color.black);m.SetFloat("_Glossiness",gloss);m.enableInstancing=true;
            m.SetFloat("_SrcBlend",transparent?5:1);m.SetFloat("_DstBlend",transparent?10:0);m.SetFloat("_ZWrite",transparent?0:1);m.renderQueue=transparent?3000:2000;
            EditorUtility.SetDirty(m);return m;
        }
        static GameObject Part(Transform root,string name,Vector3 position,Vector3 size,Material material,float radius=.025f)
        {
            var g=DeadAirFurnitureMeshes.Install(root,name,DeadAirFurnitureMeshes.RoundedBox(size,radius),position,material);
            if(material.renderQueue>=2500)g.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            return g;
        }
        static void Solid(Transform root,string name,Vector3 position,Vector3 size)
        {var g=new GameObject(name);g.transform.SetParent(root,false);g.transform.localPosition=position;g.AddComponent<BoxCollider>().size=size;}

        public static void Build(Transform parked,PropData prop,Action<Transform,string,Vector3,Quaternion,float,float> sign,Action<Transform,string> batch)
        {
            var visual=new GameObject("vehicle:crew-van").transform;visual.SetParent(parked,false);
            var fixedShell=new GameObject("Van stationary collision shell").transform;fixedShell.SetParent(parked,false);
            var paint=Material("pearl-body",new Color(.72f,.76f,.76f),.42f);
            var graphite=Material("graphite-trim",new Color(.065f,.085f,.10f),.28f);
            var cabinFloor=Material("cabin-floor",new Color(.25f,.28f,.29f),.14f);
            var rubber=Material("rubber",new Color(.025f,.032f,.035f),.10f);
            var alloy=Material("alloy",new Color(.35f,.39f,.40f),.42f);
            var interior=Material("interior-lining",new Color(.39f,.42f,.42f),.12f);
            var fabric=Material("blue-seat-fabric",new Color(.24f,.32f,.36f),.10f);
            var window=Material("tinted-glass",new Color(.19f,.30f,.36f,.23f),.58f,true);
            var lens=Material("headlamp-lens",new Color(.81f,.87f,.90f),.65f);
            var tail=Material("red-tail-lens",new Color(.40f,.035f,.025f),.42f);
            var lamp=Material("cabin-diffuser",new Color(.84f,.84f,.73f),.18f);
            // The floor top and roof underside define a real 2.40 m interior clearance.
            Part(visual,"Crew van deck",new Vector3(0,.53f,-1.125f),new Vector3(2.92f,.14f,4.65f),cabinFloor,.03f);
            Solid(fixedShell,"Van supported floor",new Vector3(0,.53f,-1.125f),new Vector3(2.92f,.14f,4.65f));
            Part(visual,"High roof cap",new Vector3(0,3.18f,-.78f),new Vector3(3.24f,.36f,5.34f),paint,.12f);
            Part(visual,"Interior ceiling liner",new Vector3(0,3.025f,-1.125f),new Vector3(2.92f,.05f,4.65f),interior);
            Solid(fixedShell,"Van ceiling",new Vector3(0,3.18f,-1.125f),new Vector3(3.24f,.36f,4.65f));
            // The left sliding entry stays open. Its frame is outside the full-height opening.
            foreach(var panel in new[]{new Vector3(-1.55f,1.8f,-2.225f),new Vector3(-1.55f,1.8f,.975f),new Vector3(1.55f,1.8f,-1.125f)})
            {
                float length=panel.x>0?4.65f:panel.z<0?2.45f:.45f;
                Part(visual,"Painted van side panel",panel,new Vector3(.18f,2.4f,length),paint);
                Solid(fixedShell,"Van side collision",panel,new Vector3(.18f,2.4f,length));
            }
            Part(visual,"Sliding entry lintel",new Vector3(-1.55f,2.865f,-.125f),new Vector3(.18f,.27f,1.75f),paint);
            Solid(fixedShell,"Van entry lintel",new Vector3(-1.55f,2.865f,-.125f),new Vector3(.18f,.27f,1.75f));
            var slidingDoor=new GameObject("state:vanSlidingDoor").transform;slidingDoor.SetParent(visual,false);slidingDoor.localPosition=new Vector3(-1.69f,1.78f,-2.35f);
            Part(slidingDoor,"Modern sliding door",Vector3.zero,new Vector3(.08f,2.16f,1.68f),paint);
            Part(slidingDoor,"Sliding door handle",new Vector3(-.055f,-.28f,0),new Vector3(.045f,.06f,.27f),graphite);
            Part(visual,"Rear lower double doors",new Vector3(0,1.24f,-3.54f),new Vector3(3.08f,1.28f,.18f),paint);
            Part(visual,"Rear window head",new Vector3(0,2.875f,-3.54f),new Vector3(3.08f,.33f,.18f),paint);
            foreach(float x in new[]{-1.445f,0f,1.445f})Part(visual,"Rear window door upright",new Vector3(x,2.285f,-3.54f),new Vector3(x==0?.13f:.19f,.81f,.18f),paint);
            Solid(fixedShell,"Van rear doors",new Vector3(0,1.82f,-3.54f),new Vector3(3.08f,2.44f,.18f));
            Part(visual,"Rear door seam",new Vector3(0,1.85f,-3.64f),new Vector3(.015f,2.36f,.012f),graphite,.003f);
            foreach(float x in new[]{-.78f,.78f})
            {
                Part(visual,"Rear tinted window",new Vector3(x,2.26f,-3.644f),new Vector3(1.17f,.73f,.018f),window);
                Part(visual,"Vertical LED tail lamp",new Vector3(x<0?-1.38f:1.38f,1.37f,-3.65f),new Vector3(.13f,.65f,.045f),tail);
                Part(visual,"Rear door pull",new Vector3(x*.31f,1.3f,-3.655f),new Vector3(.18f,.065f,.045f),graphite);
            }
            Part(visual,"Rear bumper",new Vector3(0,.42f,-3.61f),new Vector3(3.2f,.23f,.18f),graphite,.06f);
            // Sloped nose/cab outline, black grille and narrow modern lamps.
            DeadAirFurnitureMeshes.Install(visual,"Sloped modern cab body",CabMesh(),Vector3.zero,paint);
            Solid(fixedShell,"Van cab bulkhead",new Vector3(0,1.6f,2.40f),new Vector3(3.10f,2.30f,2.40f));
            var windshieldFrame=Part(visual,"Dark windshield surround",new Vector3(0,2.225f,2.685f),new Vector3(2.77f,1.53f,.025f),graphite,.06f);
            windshieldFrame.transform.localRotation=Quaternion.Euler(-42,0,0);
            var windshield=Part(visual,"Sloped windshield",new Vector3(0,2.225f,2.713f),new Vector3(2.65f,1.43f,.035f),window,.05f);
            windshield.transform.localRotation=Quaternion.Euler(-42,0,0);
            Part(visual,"Black radiator grille",new Vector3(0,1.01f,3.596f),new Vector3(1.45f,.33f,.04f),graphite);
            for(int i=0;i<4;i++)Part(visual,"Modern grille rib",new Vector3(0,.90f+i*.07f,3.623f),new Vector3(1.37f,.018f,.016f),alloy,.003f);
            Part(visual,"Front bumper",new Vector3(0,.47f,3.61f),new Vector3(3.18f,.26f,.14f),graphite,.05f);
            foreach(float x in new[]{-1.18f,1.18f})
            {
                var headlamp=Part(visual,"headlamp-lens:"+(x<0?"left":"right"),new Vector3(x,1.18f,3.61f),new Vector3(.56f,.16f,.045f),lens,.03f);
                headlamp.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                Part(visual,"Cab side tinted window",new Vector3(Mathf.Sign(x)*1.555f,2.18f,1.98f),new Vector3(.02f,.74f,1.11f),window);
                Part(visual,"Modern wing mirror",new Vector3(Mathf.Sign(x)*1.71f,1.81f,2.27f),new Vector3(.16f,.28f,.20f),graphite);
                Part(visual,"Cab door seam",new Vector3(Mathf.Sign(x)*1.57f,1.23f,1.68f),new Vector3(.013f,.82f,.016f),graphite,.003f);
            }
            foreach(float x in new[]{-1.48f,1.48f})foreach(float z in new[]{-2.57f,2.45f})
            {
                var tyre=DeadAirFurnitureMeshes.Install(visual,"Modern van tyre",DeadAirFurnitureMeshes.Turned(new[]{-.15f,.15f},new[]{.45f,.45f},14),new Vector3(x,.45f,z),rubber);
                tyre.transform.localRotation=Quaternion.Euler(0,0,90);
                var hub=DeadAirFurnitureMeshes.Install(visual,"Five spoke alloy hub",DeadAirFurnitureMeshes.Turned(new[]{-.015f,.015f},new[]{.29f,.29f},10),new Vector3(x+Mathf.Sign(x)*.16f,.45f,z),alloy);
                hub.transform.localRotation=Quaternion.Euler(0,0,90);
                Part(visual,"Wheel arch trim",new Vector3(x, .90f,z),new Vector3(.20f,.095f,1.07f),graphite,.03f);
            }
            // Two opposing padded crew benches leave a 1.80 m aisle. The entry-side bench stops before the opening.
            foreach(var bench in new[]{new Vector3(1.15f,0,-1.30f),new Vector3(-1.15f,0,-2.25f)})
            {
                float length=bench.x>0?3.55f:1.85f;
                Part(visual,"Crew bench storage base",bench+new Vector3(0,.79f,0),new Vector3(.56f,.38f,length),cabinFloor);
                Part(visual,"Padded crew bench",bench+new Vector3(0,1.025f,0),new Vector3(.54f,.13f,length),fabric,.07f);
                Part(visual,"Crew bench back cushion",bench+new Vector3(Mathf.Sign(bench.x)*.23f,1.36f,0),new Vector3(.10f,.62f,length),fabric,.035f);
                Solid(fixedShell,"Van bench collision",bench+new Vector3(0,.845f,0),new Vector3(.56f,.49f,length));
                for(int i=0;i<3;i++)Part(visual,"Crew seat belt",bench+new Vector3(-Mathf.Sign(bench.x)*.27f,1.11f,-length*.32f+i*length*.32f),new Vector3(.035f,.23f,.025f),rubber,.003f);
            }
            foreach(var step in new[]{new Vector3(-2.07f,.10f,-.125f),new Vector3(-1.79f,.20f,-.125f),new Vector3(-1.515f,.30f,-.125f)})
            {
                float width=step.y<.15f?.42f:step.y<.25f?.40f:.15f;
                var size=new Vector3(width,step.y*2,1.75f);
                Part(visual,"Van boarding step",step,size,graphite,.015f);Solid(fixedShell,"Van walkable step",step,size);
            }
            Part(visual,"Cabin ceiling light housing",new Vector3(0,2.965f,-1.20f),new Vector3(.24f,.05f,1.1f),graphite);
            Part(visual,"Cabin ceiling diffuser",new Vector3(0,2.933f,-1.20f),new Vector3(.20f,.025f,.98f),lamp);
            var light=new GameObject("lamp:van-cabin").AddComponent<Light>();light.transform.SetParent(visual,false);light.transform.localPosition=new Vector3(0,2.68f,-1.40f);
            light.type=LightType.Point;light.range=5.3f;light.intensity=.8f;light.color=new Color(1,.94f,.84f);light.shadows=LightShadows.None;
            // Ladder is an existing pickup on these rear-wall clips, with no blocking collision in the aisle.
            foreach(float y in new[]{1.35f,2.65f})Part(visual,"Ladder storage clip",new Vector3(1.425f,y,-2.90f),new Vector3(.075f,.055f,.50f),alloy);
            Part(visual,"Rear battery supply tray",new Vector3(0,.84f,-3.86f),new Vector3(.84f,.08f,.35f),graphite);
            sign(visual,"BLACKPINE / CREW",new Vector3(0,1.15f,-3.658f),Quaternion.Euler(0,180,0),1.05f,.20f);
            sign(visual,"CREW EXPEDITION",new Vector3(1.65f,1.45f,-.45f),Quaternion.Euler(0,90,0),2.4f,.21f);
            batch(visual,"crew-van");
            DeadAirWorld.InstallVanHeadlamps(visual);
        }

        static Mesh CabMesh()
        {
            const string path="Assets/Office/Meshes/modern-crew-van-cab.asset";
            Vector2[] outline={new Vector2(.45f,1.20f),new Vector2(.45f,3.58f),new Vector2(1.28f,3.58f),new Vector2(1.62f,3.18f),new Vector2(2.83f,2.10f),new Vector2(3.34f,1.35f),new Vector2(3.34f,1.20f)};
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
            {
                int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)>0)tris.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});else tris.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
            }
            for(int i=0;i<outline.Length;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Length];var outward=new Vector3(0,-(b.y-a.y),b.x-a.x);
                Face(new Vector3(-1.52f,a.x,a.y),new Vector3(1.52f,a.x,a.y),new Vector3(1.52f,b.x,b.y),new Vector3(-1.52f,b.x,b.y),outward);
            }
            foreach(float side in new[]{-1f,1f})
            {
                int n=vertices.Count;foreach(var p in outline){vertices.Add(new Vector3(side*1.52f,p.x,p.y));uv.Add(new Vector2(p.y/2,p.x/2));}
                for(int i=1;i<outline.Length-1;i++)if(side<0)tris.AddRange(new[]{n,n+i,n+i+1});else tris.AddRange(new[]{n,n+i+1,n+i});
            }
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.name="Modern high roof crew van cab";
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }
    }
}
