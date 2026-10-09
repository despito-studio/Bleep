using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    // Reference-driven additions stay separate from mission coordinates and moving doors.
    public static class DeadAirSovietOfficeArt
    {
        public delegate GameObject Solid(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collide);
        public delegate void Label(Transform parent,string text,Vector3 position,Quaternion rotation,float width,float height);
        static Transform Group(Transform parent,string name)
        {
            var t=new GameObject(name).transform;t.SetParent(parent,false);return t;
        }

        public static void Architecture(Transform shell,WorldData world,Solid block,Label sign,Material brick,Material plaster,Material metal,Material wood,Material trim)
        {
            var root=Group(shell,"Late Soviet stepped facade");
            // A projecting civic entry, lower wings and taller stair head break up the old box.
            foreach(float x in new[]{-4.1f,4.1f})
            {
                block(root,"Entry portal pier",new Vector3(x,4.55f,7.15f),new Vector3(.65f,9.1f,2.1f),plaster,true);
                block(root,"Portal stone foot",new Vector3(x,.4f,7.15f),new Vector3(.84f,.8f,2.3f),brick,true);
            }
            block(root,"Raised central entry crown",new Vector3(0,8.8f,7.15f),new Vector3(8.85f,.65f,2.1f),plaster,false);
            block(root,"Entry crown coping",new Vector3(0,9.16f,7.15f),new Vector3(9.15f,.12f,2.4f),metal,false);
            // Wing parapets stop at the central risalit. Ribs recall Soviet civic buildings.
            foreach(float x in new[]{-13.35f,13.35f})
            {
                block(root,"Low front wing parapet",new Vector3(x,8.62f,6.15f),new Vector3(17.6f,.65f,.45f),plaster,false);
                block(root,"Front parapet cap",new Vector3(x,8.98f,6.15f),new Vector3(17.75f,.08f,.6f),metal,false);
            }
            for(int i=0;i<34;i++)
            {
                float x=-21.4f+i*1.29f;
                if(Mathf.Abs(x)<4.6f)continue;
                block(root,"Front crown vertical fin",new Vector3(x,8.56f,6.48f),new Vector3(.10f,.74f,.22f),trim,false);
            }
            foreach(var edge in DeadAirOfficeFootprint.Boundary(world,0))
            {
                float coordinate=edge.coordinate+edge.outward*.24f,mid=(edge.from+edge.to)/2,length=edge.to-edge.from;
                Vector3 At(float y)=>DeadAirOfficeLayout.Position(edge.vertical?coordinate:mid,y,edge.vertical?mid:coordinate);
                Vector3 Size(float thickness,float h)=>edge.vertical?new Vector3(thickness,h,length):new Vector3(length,h,thickness);
                block(root,"Contour cornice",At(8.37f),Size(.42f,.3f),plaster,false);
                block(root,"Facade storey belt",At(4.14f),Size(.34f,.18f),plaster,false);
                block(root,"Weathered foundation plinth",At(.31f),Size(.34f,.62f),trim,false);
            }
            block(root,"Rear stair head body",new Vector3(0,9.06f,-46),new Vector3(7.6f,1.4f,12.6f),brick,false);
            block(root,"Rear stair head roof",new Vector3(0,9.85f,-46),new Vector3(8.1f,.18f,13.1f),metal,false);
            block(root,"Stair head clerestory",new Vector3(0,9.23f,-39.68f),new Vector3(5.8f,.48f,.035f),trim,false);
            for(int i=0;i<8;i++)block(root,"Clerestory mullion",new Vector3(-2.75f+i*.78f,9.23f,-39.63f),new Vector3(.06f,.52f,.06f),metal,false);
            foreach(float x in new[]{-18.5f,18.5f})
            {
                block(root,"Rusty rainwater pipe",new Vector3(x,4.15f,-53.8f),new Vector3(.085f,8.3f,.085f),metal,false);
                for(int i=0;i<4;i++)block(root,"Drainpipe wall collar",new Vector3(x,1+i*2,-53.8f),new Vector3(.17f,.04f,.14f),metal,false);
            }
            sign(root,"РАЙОННИЙ ТЕЛЕЦЕНТР\n1988",new Vector3(0,8.78f,8.225f),Quaternion.identity,6.8f,.45f);
        }

        public static void Interiors(Transform station,WorldData world,Solid block,Label sign,Material wood,Material metal,Material paper,Material trim,Material black)
        {
            var sectors=station.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("sector:",StringComparison.Ordinal)).ToArray();
            foreach(var room in world.rooms.Where(r=>DeadAirOfficeLayout.IsInterior(r)&&!DeadAirOfficeLayout.IsCirculation(r)&&r.floorY>=0))
            {
                var parent=sectors.FirstOrDefault(t=>t.name=="sector:"+room.name);
                if(parent==null)continue;
                var root=Group(parent,"Period details:"+room.mapLabel);
                // Curtain slats attach to each real outside window rather than floating in a room.
                foreach(var window in station.GetComponentsInChildren<Transform>().Where(t=>t.name=="Window").ToArray())
                {
                    var source=new Vector3(-window.position.x,window.position.y,window.position.z);
                    if(Mathf.Abs(source.y-(room.floorY+2.18f))>.05f)continue;
                    bool atSide=(Mathf.Abs(source.x-room.minX)<.01f||Mathf.Abs(source.x-room.maxX)<.01f)&&source.z>room.minZ&&source.z<room.maxZ;
                    bool atEnd=(Mathf.Abs(source.z-room.minZ)<.01f||Mathf.Abs(source.z-room.maxZ)<.01f)&&source.x>room.minX&&source.x<room.maxX;
                    if(!atSide&&!atEnd)continue;
                    var curtain=Group(root,"Faded curtains");curtain.position=window.position;curtain.rotation=window.rotation;
                    var roomCentre=DeadAirOfficeLayout.Position((room.minX+room.maxX)/2,source.y,(room.minZ+room.maxZ)/2);
                    float inward=Mathf.Sign(Vector3.Dot(roomCentre-curtain.position,curtain.forward));
                    float width=window.GetComponentsInChildren<Collider>().Select(c=>c.bounds.size).DefaultIfEmpty(new Vector3(2.45f,2,0)).Max(s=>Mathf.Max(s.x,s.z));
                    width=Mathf.Clamp(width,1.2f,2.45f);
                    for(int side=-1;side<=1;side+=2)for(int pleat=0;pleat<4;pleat++)
                        block(curtain,"Dusty curtain pleat",new Vector3(side*(width*.44f-pleat*.045f),-.12f,inward*.22f),new Vector3(.07f,1.94f,.045f),trim,false);
                    block(curtain,"Curtain rod",new Vector3(0,1.01f,inward*.24f),new Vector3(width+.24f,.035f,.035f),metal,false);
                }
                // Calendars and notices are supported by the back wall, clear of doors.
                if(room.mapLabel=="DIRECTOR"||room.mapLabel=="RECEPTION"||room.mapLabel=="EDITORIAL"||room.mapLabel=="RADIO")
                {
                    float sx=room.minX+1.3f;
                    var mount=Group(root,"Abandoned 1991 calendar");mount.localPosition=DeadAirOfficeLayout.Position(sx,room.floorY+1.94f,room.minZ+.1275f);
                    if(room.mapLabel=="DIRECTOR"||room.mapLabel=="RECEPTION")
                    {
                        mount.localPosition=DeadAirOfficeLayout.Position(room.maxX-.1275f,room.floorY+1.94f,room.mapLabel=="DIRECTOR"?2.3f:-4.5f);
                        mount.localRotation=Quaternion.Euler(0,90,0);
                    }
                    block(mount,"Calendar paper",Vector3.zero,new Vector3(.37f,.51f,.035f),paper,false);
                    sign(mount,"1991\nСЕРПЕНЬ\n19  20  21",new Vector3(0,0,.022f),Quaternion.identity,.32f,.38f);
                    block(mount,"Calendar suspension",new Vector3(0,.275f,0),new Vector3(.026f,.055f,.012f),metal,false);
                }
            }
        }

        public static void Mountains(Transform exterior)
        {
            var root=Group(exterior,"Mountain panorama cards");
            // Closed rings eliminate exposed card edges from both floors and all fenced paths.
            for(int layer=0;layer<3;layer++)
            {
                float rx=95+layer*31,rz=130+layer*34;
                var vertices=new List<Vector3>();var triangles=new List<int>();
                const int segments=96;
                for(int i=0;i<=segments;i++)
                {
                    float a=i*Mathf.PI*2/segments;
                    float peak=17+layer*8+Mathf.Sin(i*.79f+layer)*9+Mathf.Sin(i*.23f+layer*1.8f)*7;
                    var p=new Vector3(Mathf.Cos(a)*rx,0,-20+Mathf.Sin(a)*rz);
                    vertices.Add(p+Vector3.down*2);vertices.Add(p+Vector3.up*peak);
                    if(i==segments)continue;
                    int at=i*2;triangles.AddRange(new[]{at,at+1,at+2,at+1,at+3,at+2});
                }
                // Match last peak to first so a full panorama cannot reveal a seam.
                vertices[vertices.Count-1]=new Vector3(vertices[vertices.Count-1].x,vertices[1].y,vertices[vertices.Count-1].z);
                AddMesh(root,"Mountain ridge "+layer,vertices,triangles,new Color(.054f+layer*.013f,.075f+layer*.014f,.082f+layer*.019f));
            }
            var forest=new List<Vector3>();var indices=new List<int>();
            var random=new System.Random(198891);
            for(int i=0;i<176;i++)
            {
                float a=i*Mathf.PI*2/176,rx=47+(float)random.NextDouble()*28,rz=80+(float)random.NextDouble()*22;
                var p=new Vector3(Mathf.Cos(a)*rx,0,-20+Mathf.Sin(a)*rz);
                float height=6+(float)random.NextDouble()*7,width=height*.29f;
                var right=new Vector3(-Mathf.Sin(a),0,Mathf.Cos(a));
                // Two crossed cut-out silhouettes give parallax at a few triangles per tree.
                foreach(var axis in new[]{right,new Vector3(right.z,0,-right.x)})
                {
                    for(int tier=0;tier<3;tier++)
                    {
                        float top=height-tier*height*.20f,bottom=top-height*.48f,half=width*(1-tier*.17f);
                        int first=forest.Count;
                        forest.Add(p+Vector3.up*top);forest.Add(p+Vector3.up*bottom-axis*half);forest.Add(p+Vector3.up*bottom+axis*half);
                        indices.AddRange(new[]{first,first+1,first+2});
                    }
                }
            }
            AddMesh(root,"Forest silhouette cards",forest,indices,new Color(.032f,.056f,.039f));
        }

        static void AddMesh(Transform parent,string name,List<Vector3> vertices,List<int> triangles,Color color)
        {
            string key=name.Replace(' ','-');string folder="Assets/Office";
            string meshPath=folder+"/Meshes/"+key+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,meshPath);}
            mesh.Clear();mesh.name=name;mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            mesh.SetUVs(0,vertices.Select(v=>new Vector2(v.x*.02f,v.y*.02f)).ToList());mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            string materialPath=folder+"/Materials/"+key+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader=Shader.Find("Dead Air/Source Unlit");
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}
            material.shader=shader;material.color=color;material.SetFloat("_Cull",0);material.SetFloat("_FogEnabled",1);material.enableInstancing=true;EditorUtility.SetDirty(material);
            var obj=Group(parent,name);obj.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
    }
}
