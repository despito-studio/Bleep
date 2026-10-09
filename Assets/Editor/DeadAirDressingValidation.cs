using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Validates saved scenery against physical supports and the playable office plan.</summary>
    public static class DeadAirDressingValidation
    {
        [MenuItem("Dead Air/Validate Office Dressing")]
        public static void Validate()
        {
            var data=WorldData.Load();
            var station=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var exterior=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            Require(station!=null&&exterior!=null,"Office prefabs are missing.");
            var transforms=station.GetComponentsInChildren<Transform>(true).Concat(exterior.GetComponentsInChildren<Transform>(true)).ToArray();
            var solids=station.GetComponentsInChildren<BoxCollider>(true);
            var webs=transforms.Where(t=>t.name.StartsWith("Cobweb:",StringComparison.Ordinal)).ToArray();
            Require(webs.Length>=30,"Both office floors and the locker backs need their mounted cobwebs.");
            foreach(var web in webs)
            {
                Require(web.GetComponentsInChildren<Collider>(true).Length==0,"Cobweb blocks a player, door or locker: "+web.name);
                var strands=web.Find("Web strands");
                Require(strands!=null&&strands.TryGetComponent<MeshFilter>(out var filter)&&filter.sharedMesh!=null&&
                    filter.sharedMesh.bounds.size.z>.035f,"Cobweb needs curved three-dimensional silk threads: "+web.name);
                Require(strands.GetComponent<MeshRenderer>()!=null&&strands.GetComponent<MeshRenderer>().sharedMaterial.mainTexture==null,
                    "Cobweb silk must be thread geometry without a flat texture: "+web.name);
                foreach(string name in new[]{"Web anchor corner","Web anchor ceiling","Web anchor wall"})
                {
                    var anchor=web.Find(name);
                    Require(anchor!=null,"Cobweb has no support marker: "+web.name+" / "+name);
                    Require(solids.Any(s=>Distance(s,anchor.position)<.04f),"Cobweb anchor is suspended away from a real wall or locker: "+web.name+" / "+name+" at "+anchor.position);
                }
                if(web.name.StartsWith("Cobweb:locker:",StringComparison.Ordinal))
                    Require(web.parent.name.StartsWith("interactive:locker-",StringComparison.Ordinal)&&web.localPosition.z<-.4f,
                        "Locker cobweb must stay behind the player's view and grille.");
            }
            var strandMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Office/Meshes/cobweb-strands.asset");
            Require(strandMesh!=null,"Persisted cobweb strand mesh is missing.");
            float webArea=0;var vertices=strandMesh.vertices;var triangles=strandMesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
                webArea+=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).magnitude*.5f;
            // Closed tubes have more surface area than strips but keep the
            // triangular corner open and have a visible third dimension.
            Require(strandMesh.bounds.size.z>.1f&&webArea>.12f&&webArea<.8f,
                "Cobweb has lost its curved tubular threads or become a filled sheet.");
            CheckDressingMaterial("cobweb-silk");CheckDressingMaterial("waste-paper");

            var litter=transforms.Where(t=>t.name.StartsWith("Floor litter:",StringComparison.Ordinal)).ToArray();
            Require(litter.Length>=120,"Office dressing lost most of its scattered floor litter.");
            int paper=0,cardboard=0,cups=0,folders=0;
            var cupMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Office/Meshes/paper-cup.asset");
            foreach(var root in litter)
            {
                Require(root.GetComponentsInChildren<Collider>(true).Length==0,"Floor litter introduces collision: "+root.name);
                float x=-root.position.x,z=root.position.z;
                var room=data.rooms.FirstOrDefault(r=>root.parent.name=="sector:"+r.name);
                Require(room!=null,"Floor litter has no authored room/yard sector: "+root.name);
                bool interior=DeadAirOfficeLayout.IsInterior(room),circulation=DeadAirOfficeLayout.IsCirculation(room);
                float finish=room.floorY+(interior&&!circulation&&room.floorY>=0?.016f:0);
                Require(Mathf.Abs(root.position.y-finish-.001f)<.0005f,"Floor litter is floating above its floor finish: "+root.name);
                Require(x>room.minX+.4f&&x<room.maxX-.4f&&z>room.minZ+.4f&&z<room.maxZ-.4f,"Floor litter intersects a perimeter wall: "+root.name);
                Require(!data.doors.Any(d=>Mathf.Abs(d.y-1.4f-room.floorY)<.1f&&Mathf.Abs(x-d.x)<1.6f&&Mathf.Abs(z-d.z)<1.6f),"Floor litter is in an animated doorway: "+root.name);
                var page=root.Find("Discarded page");if(page!=null){paper++;Require(Mathf.Abs(page.localPosition.y-.002f)<.0001f,"Loose page lost floor contact.");}
                var card=root.Find("Flattened cardboard");if(card!=null){cardboard++;Require(Mathf.Abs(card.localPosition.y-.003f)<.0001f,"Cardboard lost floor contact.");}
                var folder=root.Find("Abandoned file cover");if(folder!=null){folders++;Require(Mathf.Abs(folder.localPosition.y-.003f)<.0001f,"Folder lost floor contact.");}
                var cup=root.Find("Tipped paper cup");
                if(cup!=null)
                {
                    cups++;Require(cupMesh!=null,"Persisted paper cup mesh is missing.");
                    float lowest=cupMesh.vertices.Min(v=>cup.TransformPoint(v).y);
                    Require(Mathf.Abs(lowest-root.position.y)<.0005f,"Tipped paper cup does not rest on the floor.");
                }
            }
            Require(paper>10&&cardboard>10&&cups>10&&folders>10,"Floor litter must retain its varied, sparse office shapes.");

            var background=exterior.transform.Find("Beyond the fence");
            Require(background!=null,"Scenery beyond the fence is missing.");
            Require(background.GetComponentsInChildren<Collider>(true).Length==0,"Background scenery changes the playable collision bounds.");
            var ground=new[]{
                GroundBounds(background,"Distant ground west",new Vector3(190,.36f,500)),
                GroundBounds(background,"Distant ground east",new Vector3(190,.36f,500)),
                GroundBounds(background,"Distant ground rear",new Vector3(60,.36f,194)),
                GroundBounds(background,"Distant ground front",new Vector3(60,.36f,192))
            };
            // The first centimetre beyond every fence must be covered, as well as
            // the horizon below the 250m far clip from both playable floors.
            foreach(float z in new[]{-230f,-100,-75,0,37,100,200})foreach(float x in new[]{-200f,-30.01f,30.01f,200})
                Require(ground.Any(b=>ContainsXZ(b,new Vector3(x,0,z))),"Visible terrain ends beyond the fence at "+x+", "+z);
            foreach(float x in new[]{-29f,0,29})foreach(float z in new[]{-230f,-76.01f,38.01f,200})
                Require(ground.Any(b=>ContainsXZ(b,new Vector3(x,0,z))),"Front/rear yard opens onto a void at "+x+", "+z);
            Require(background.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="Distant service building")==5,
                "Distant service district is incomplete.");
            Require(background.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="Courtyard birch")>=50&&background.Find("Distant transmission mast")!=null,
                "Scenery lacks its layered tree line and transmission mast.");
            Require(background.GetComponentsInChildren<Renderer>(true).All(r=>r.shadowCastingMode==ShadowCastingMode.Off),
                "Far scenery must not consume the office's local shadow-map budget.");
            Debug.Log("Office dressing validated: "+webs.Length+" physically anchored strand webs; "+litter.Length+" floor-mounted nonblocking litter pieces; continuous scenery terrain, road, tree layers and five distant buildings beyond unchanged playable bounds.");
        }

        static void CheckDressingMaterial(string name)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Office/Materials/"+name+".mat");
            Require(material!=null&&material.shader.name=="Dead Air/Source Surface","Dressing must use the physically lit office shader: "+name);
            Require(material.GetColor("_EmissionColor").maxColorComponent<.0001f,"Dressing must not glow without a practical light: "+name);
        }
        static float Distance(BoxCollider box,Vector3 point)
        {
            var local=box.transform.InverseTransformPoint(point)-box.center;var half=box.size/2;
            var nearest=new Vector3(Mathf.Clamp(local.x,-half.x,half.x),Mathf.Clamp(local.y,-half.y,half.y),Mathf.Clamp(local.z,-half.z,half.z));
            return Vector3.Distance(point,box.transform.TransformPoint(box.center+nearest));
        }
        static Bounds GroundBounds(Transform parent,string name,Vector3 size)
        {
            var root=parent.Find(name);Require(root!=null,"A terrain skirt segment is missing: "+name);
            string path="Assets/Office/Meshes/box_"+string.Join("_",new[]{size.x,size.y,size.z}.Select(v=>v.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)))+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);Require(mesh!=null,"Persisted terrain slab is missing: "+name);
            var bounds=new Bounds(root.TransformPoint(mesh.bounds.center),Vector3.Scale(mesh.bounds.size,root.lossyScale));
            Require(Mathf.Abs(bounds.max.y+.03f)<.0001f,"Terrain must meet the grounded background tree bases: "+name);
            return bounds;
        }
        static bool ContainsXZ(Bounds bounds,Vector3 point)=>point.x>=bounds.min.x&&point.x<=bounds.max.x&&point.z>=bounds.min.z&&point.z<=bounds.max.z;
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
