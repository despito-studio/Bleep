using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirProductionArtOverrides
    {
        const string Folder="Assets/Office/ArtOverrides";
        public static string SourcePath(PropData prop)
        {
            string path=Folder+"/Instances/"+Safe(prop.id)+".prefab";
            if(File.Exists(path))return path;
            path=Folder+"/Types/"+Safe(prop.type)+".prefab";
            return File.Exists(path)?path:null;
        }
        public static bool TryBuild(Transform destination,PropData prop)
        {
            string path=SourcePath(prop);if(path==null)return false;
            bool instance=path.StartsWith(Folder+"/Instances/",StringComparison.Ordinal);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)throw Error(path,"The file could not be loaded as a Unity prefab.");
            var copy=UnityEngine.Object.Instantiate(prefab,destination,false);
            copy.name=prop.type=="abandonedCar"?"Parked sedan":"Art override / "+prop.type;
            copy.transform.localPosition=Vector3.zero;copy.transform.localRotation=Quaternion.identity;copy.transform.localScale=Vector3.one;
            try
            {
                var drawers=DeadAirDrawers.ForProp(prop);
                foreach(var t in copy.GetComponentsInChildren<Transform>(true))
                {
                    t.name=t.name.Replace("$PROP",prop.id);
                    for(int i=0;i<drawers.Length;i++)t.name=t.name.Replace("$DRAWER_"+i,drawers[i].id);
                }
                Validate(copy.transform,destination,prop,path,instance);
                // Locker lookup and sightline validation require its hinge directly below interactive:id.
                if(prop.type=="locker")
                {
                    var hinge=copy.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="locker-door:"+prop.id);
                    hinge.SetParent(destination,true);
                }
                if(prop.type=="van")
                {
                    var vehicle=copy.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="vehicle:crew-van");
                    vehicle.SetParent(destination,true);
                }
                return true;
            }
            catch{UnityEngine.Object.DestroyImmediate(copy);throw;}
        }
        static void Validate(Transform copy,Transform destination,PropData prop,string path,bool instance)
        {
            var parts=copy.GetComponentsInChildren<Transform>(true);
            var drawers=DeadAirDrawers.ForProp(prop);
            var expected=drawers.Select(d=>"drawer:"+d.id).ToArray();
            foreach(var t in parts)
            {
                bool valid=true;
                if(t.name.StartsWith("drawer:",StringComparison.Ordinal))valid=expected.Contains(t.name);
                else if(t.name.StartsWith("locker-door:",StringComparison.Ordinal))valid=prop.type=="locker"&&t.name=="locker-door:"+prop.id;
                else if(t.name.StartsWith("fan:",StringComparison.Ordinal))valid=prop.type=="fan"&&t.name=="fan:"+prop.id;
                else if(t.name.StartsWith("interactive:",StringComparison.Ordinal)||t.name.StartsWith("door:",StringComparison.Ordinal)||
                    t.name.StartsWith("door-led:",StringComparison.Ordinal)||t.name.StartsWith("door-light:",StringComparison.Ordinal)||t.name.StartsWith("prop:",StringComparison.Ordinal))valid=false;
                else if(t.name.StartsWith("state:",StringComparison.Ordinal))valid=prop.type=="van"&&t.name=="state:vanSlidingDoor";
                if(!valid)throw Error(path,"ID-bearing node '"+t.name+"' cannot be applied safely to "+prop.id+". "+
                    (instance?"Keep the exact target IDs and pivots from this instance's source prefab.":"For a type prefab use drawer:$DRAWER_0, drawer:$DRAWER_1, etc. and locker-door:$PROP or fan:$PROP; otherwise provide an Instances/"+Safe(prop.id)+".prefab override."));
                if(t.GetComponent<MeshFilter>()!=null&&t.childCount>0&&Vector3.Distance(t.localScale,Vector3.one)>.0001f)
                    throw Error(path,"Scaled mesh '"+t.name+"' parents other parts; use a separate unit-scale assembly transform.");
                if((t.name.StartsWith("drawer:")||t.name.StartsWith("locker-door:")||t.name.StartsWith("motion:")||t.name.StartsWith("fan:"))&&Vector3.Distance(t.localScale,Vector3.one)>.0001f)
                    throw Error(path,"Articulation pivot '"+t.name+"' must have unit scale.");
            }
            if(copy.GetComponentsInChildren<MeshFilter>(true).All(f=>f.sharedMesh==null))throw Error(path,"The override contains no mesh geometry.");
            foreach(var drawer in drawers)
            {
                var matches=parts.Where(t=>t.name=="drawer:"+drawer.id).ToArray();
                if(matches.Length!=1)throw Error(path,"Expected exactly one drawer:"+drawer.id+" pivot. Preserve the interaction parts and use $DRAWER_i tokens for type overrides.");
                var local=destination.InverseTransformPoint(matches[0].position);
                if(Vector3.Distance(local,drawer.localPosition)>.002f)throw Error(path,"drawer:"+drawer.id+" must keep local pivot "+drawer.localPosition+" because reach, collision and key placement use world.json drawer data.");
            }
            if(prop.type=="locker")
            {
                var hinges=parts.Where(t=>t.name=="locker-door:"+prop.id).ToArray();
                if(hinges.Length!=1)throw Error(path,"Expected one locker-door:"+prop.id+" hinge.");
                if(hinges[0].Cast<Transform>().Count(t=>t.name.StartsWith("Open grille slat",StringComparison.Ordinal))!=6)
                    throw Error(path,"Keep six separate Open grille slat nodes and the open sight gap at eye height.");
                if(Vector3.Distance(destination.InverseTransformPoint(hinges[0].position),new Vector3(-.53f,0,.49f))>.002f)
                    throw Error(path,"The locker hinge pivot must remain (-0.53, 0, 0.49) metres.");
            }
            if(prop.type=="fan"&&parts.Count(t=>t.name=="fan:"+prop.id)!=1)throw Error(path,"Expected a fan:"+prop.id+" blade pivot.");
            if(prop.type=="van"&&(!parts.Any(t=>t.name=="vehicle:crew-van")||!parts.Any(t=>t.name=="state:vanSlidingDoor")))
                throw Error(path,"Crew van overrides must retain vehicle:crew-van and state:vanSlidingDoor plus the existing pickup/aisle clearances.");
        }
        static string Safe(string value)=>string.Concat((value??"").Select(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'?c:'_'));
        static InvalidOperationException Error(string path,string message)=>new InvalidOperationException("Art override "+path+": "+message);
    }
}
