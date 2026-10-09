using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    /// <summary>Checks the saved physical geometry, including the cases visible in user screenshots.</summary>
    public static class DeadAirMountedGeometryValidation
    {
        [MenuItem("Dead Air/Validate Mounted Office Geometry")]
        public static void Validate()
        {
            var data=WorldData.Load();
            var station=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var exterior=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            Require(station!=null&&exterior!=null,"Saved office prefabs are missing.");
            var transforms=station.GetComponentsInChildren<Transform>(true).Concat(exterior.GetComponentsInChildren<Transform>(true)).ToArray();
            var walls=station.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name.Contains("partition")||c.name=="Entrance header"||c.name.Contains("facade")||c.name.Contains("Facade")||c.name.Contains("window pier")||c.name.Contains("Under-stairs")).ToArray();
            int windowCount=0,mountedCount=0;
            foreach(var window in transforms.Where(t=>t.name=="Window"))
            {
                var pane=(window.Find("Recessed glass") ?? window.Find("state:directorWindowPane/Recessed glass"))?.GetComponent<BoxCollider>();
                Require(pane!=null,"Window has no glazing collision envelope.");
                float width=pane.size.x+.08f;
                bool side=Mathf.Abs(window.forward.x)>.9f;
                float coordinate=side?-window.position.x:window.position.z;
                float center=side?window.position.z:-window.position.x;
                float floorY=Mathf.Floor(window.position.y/DeadAirOfficeLayout.FloorHeight)*DeadAirOfficeLayout.FloorHeight;
                var spans=data.rooms.Where(r=>DeadAirOfficeLayout.IsInterior(r)&&Mathf.Abs(r.floorY-floorY)<.01f&&
                    (side?Mathf.Abs(r.minX-coordinate)<.01f||Mathf.Abs(r.maxX-coordinate)<.01f:
                    Mathf.Abs(r.minZ-coordinate)<.01f||Mathf.Abs(r.maxZ-coordinate)<.01f));
                Require(spans.Any(r=>center-width/2>=(side?r.minZ:r.minX)+.42f&&center+width/2<=(side?r.maxZ:r.maxX)-.42f),
                    "Window overlaps a room junction or leaves no solid pier: "+window.position);
                Require(ExposedBoundaryCovers(data,floorY,side,coordinate,center-width/2,center+width/2),"Window is outside an exposed facade plane: "+window.position);
                foreach(float along in new[]{center-width/2,center,center+width/2})
                {
                    bool before=DeadAirOfficeLayout.InteriorAt(data,side?coordinate-.02f:along,side?along:coordinate-.02f,floorY);
                    bool after=DeadAirOfficeLayout.InteriorAt(data,side?coordinate+.02f:along,side?along:coordinate+.02f,floorY);
                    Require(before!=after,"Window is placed on an internal partition or unsupported exterior edge: "+window.position);
                }
                Require(window.Cast<Transform>().Count(t=>t.name=="Window reveal side")==2&&window.Cast<Transform>().Count(t=>t.name=="Window frame side")==4&&window.Find("Interior sill")!=null,
                    "Window lacks full-depth reveal, inside/outside frame or sill.");
                windowCount++;
            }
            Require(windowCount>=40,"Office facade lost most of its windows.");

            foreach(var prop in data.props)
            {
                float depth=MountedDepth(prop.type);
                if(depth==0)continue;
                var root=transforms.Single(t=>t.name=="prop:"+prop.id);
                Require(Vector3.Distance(root.position,DeadAirOfficeLayout.Position(prop.x,prop.y,prop.z))<.001f,"Mounted prop does not match saved position: "+prop.id);
                // Sample the backing, not the printed front face. A small penetration probes
                // actual solid partition colliders and also catches boards above door openings.
                foreach(float x in new[]{-prop.w*.43f,0,prop.w*.43f})
                {
                    var point=root.TransformPoint(new Vector3(x,prop.type=="fusePanel"?1.65f:1.8f,-depth/2-.006f));
                    Require(walls.Any(wall=>Contains(wall,point,.003f)),"Mounted panel has no solid wall backing: "+prop.id+" at "+point);
                }
                mountedCount++;
            }
            foreach(var sign in transforms.Where(t=>t.name.StartsWith("Fitted sign:")&&t.parent.name.StartsWith("Door frame ")))
                Require(walls.Any(wall=>Contains(wall,sign.position-sign.forward*.019f,.004f)),"Door label is suspended away from its wall: "+sign.parent.name);

            var generator=transforms.Single(t=>t.name=="prop:"+data.props.Single(p=>p.type=="dieselGenerator").id);
            var casing=generator.Find("Generator enclosure").GetComponent<BoxCollider>();
            var power=transforms.Single(t=>t.name=="interactive:power");
            var cabinet=power.Find("Power control cabinet").GetComponent<BoxCollider>();
            var bodyBounds=WorldBounds(casing);var cabinetBounds=WorldBounds(cabinet);
            Require(cabinetBounds.min.x>=bodyBounds.min.x&&cabinetBounds.max.x<=bodyBounds.max.x&&cabinetBounds.min.y>=bodyBounds.min.y&&cabinetBounds.max.y<=bodyBounds.max.y,
                "Generator controls extend beyond their supporting enclosure.");
            Require(cabinetBounds.min.z-bodyBounds.max.z>=-.01f&&cabinetBounds.min.z-bodyBounds.max.z<.02f,"Generator cabinet is floating in front of the enclosure.");
            Require(generator.Find("Radiator recess")!=null&&generator.Find("Generator skid")!=null,"Generator lacks a complete body/base.");
            Require(!generator.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m!=null&&m.name=="surface_289")),"Old luminous floating generator label remains.");

            var valve=transforms.Single(t=>t.name=="interactive:pressure-valve");
            Require(valve.Find("Wall-mounted pressure pipe")!=null,"Pressure wheel has no supporting pipe.");
            var valvePoint=valve.TransformPoint(new Vector3(0,0,-.20f));
            Require(Mathf.Abs(Mathf.Abs(valvePoint.x)-18)<.19f,"Pressure valve is detached from the exterior wall.");
            Require(transforms.Count(t=>t.name=="Wayfinding sign hanger")>=10&&transforms.Count(t=>t.name=="Parking sign post")==2,"Wayfinding or parking signs have no physical supports.");
            Debug.Log("Mounted geometry validated: "+windowCount+" aligned window openings; "+mountedCount+" wall-backed panels; door/sign supports; complete generator with attached controls; wall-mounted valve.");
        }

        static float MountedDepth(string type)
        {
            switch(type)
            {
                case "corkboard":case "toolboard":case "directory":return .09f;
                case "mirror":return .08f;
                case "whiteboard":return .06f;
                case "fusePanel":return .22f;
                case "fan":return .10f;
                default:return 0;
            }
        }
        static bool ExposedBoundaryCovers(WorldData data,float floorY,bool vertical,float coordinate,float from,float to)
        {
            // The union helper cuts at every room coordinate, including unrelated
            // stair/room edges. A glazing bay can cross those contiguous segments.
            float cursor=from;
            foreach(var edge in DeadAirOfficeFootprint.Boundary(data,floorY).Where(edge=>edge.vertical==vertical&&Mathf.Abs(edge.coordinate-coordinate)<.001f).OrderBy(edge=>edge.from))
            {
                if(edge.to<cursor-.001f)continue;
                if(edge.from>cursor+.001f)return false;
                cursor=Mathf.Max(cursor,edge.to);
                if(cursor>=to-.001f)return true;
            }
            return false;
        }
        static bool Contains(BoxCollider box,Vector3 point,float allowance)
        {
            var p=box.transform.InverseTransformPoint(point)-box.center;var half=box.size/2+Vector3.one*allowance;
            return Mathf.Abs(p.x)<=half.x&&Mathf.Abs(p.y)<=half.y&&Mathf.Abs(p.z)<=half.z;
        }
        static Bounds WorldBounds(BoxCollider box)
        {
            var bounds=new Bounds(box.transform.TransformPoint(box.center),Vector3.zero);
            var half=box.size/2;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                bounds.Encapsulate(box.transform.TransformPoint(box.center+Vector3.Scale(half,new Vector3(x,y,z))));
            return bounds;
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
