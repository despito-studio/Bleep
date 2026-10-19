using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirProductionCatalog
    {
        const string Folder="Assets/Office/Authoring";
        [Serializable] sealed class Catalog
        {
            public int schemaVersion=2;
            public string artDirection="Ukrainian district broadcast centre, 1988 equipment, abandoned August 1991; contemporary crew van";
            public string interiorSource=Folder+"/World/station-source.prefab",exteriorSource=Folder+"/World/exterior-source.prefab";
            public string artistInstanceOverrides="Assets/Office/ArtOverrides/Instances/{id}.prefab",artistTypeOverrides="Assets/Office/ArtOverrides/Types/{type}.prefab";
            public string overrideContract="Instance overrides take precedence; override folders are never generated. Preserve exact instance articulation IDs, or use $PROP and drawer:$DRAWER_i tokens in type overrides. Drawer pivots must retain their gameplay positions.";
            public TypeEntry[] types;
            public InstanceEntry[] instances;
        }
        [Serializable] sealed class TypeEntry
        {
            public string type,prefab;
            public string[] variants;
            public int placements;
        }
        [Serializable] sealed class InstanceEntry
        {
            public string id,type,label,prefab,sourceOverride;
            public float x,y,z,rotation,width,depth;
            public int meshParts,collisionShapes;
            public string[] articulatedParts,modelSources,assemblyParts,materialNames;
        }
        static string Safe(string text)=>string.Concat((text??"unnamed").Select(ch=>char.IsLetterOrDigit(ch)||ch=='-'||ch=='_'?ch:'_'));
        static bool Articulated(Transform t)=>t.name.StartsWith("door:",StringComparison.Ordinal)||t.name.StartsWith("locker-door:",StringComparison.Ordinal)||
            t.name.StartsWith("drawer:",StringComparison.Ordinal)||t.name.StartsWith("motion:",StringComparison.Ordinal)||t.name.StartsWith("fan:",StringComparison.Ordinal)||t.name.StartsWith("state:",StringComparison.Ordinal);

        public static void Save(WorldData world,GameObject station,GameObject exterior)
        {
            foreach(string sub in new[]{"World","Instances","Types"})Directory.CreateDirectory(Folder+"/"+sub);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var all=station.GetComponentsInChildren<Transform>(true).Concat(exterior.GetComponentsInChildren<Transform>(true)).ToArray();
            ValidateWorldContracts(world,all);
            var entries=new List<InstanceEntry>();var typePrefabs=new Dictionary<string,string>();
            foreach(var p in world.props.OrderBy(p=>p.type,StringComparer.Ordinal).ThenBy(p=>p.id,StringComparer.Ordinal))
            {
                string node=(p.type=="locker"?"interactive:":"prop:")+p.id;
                var root=all.SingleOrDefault(t=>t.name==node);
                if(root==null)throw new InvalidOperationException("Authoring assembly missing: "+node);
                ValidateAssembly(root,p.id);
                string path=Folder+"/Instances/"+Safe(p.id)+".prefab";
                SaveAtOrigin(root.gameObject,path);
                if(!typePrefabs.ContainsKey(p.type))
                {
                    string typePath=Folder+"/Types/"+Safe(p.type)+".prefab";
                    SaveAtOrigin(root.gameObject,typePath);typePrefabs.Add(p.type,typePath);
                }
                entries.Add(new InstanceEntry {id=p.id,type=p.type,label=p.label,prefab=path,sourceOverride=DeadAirProductionArtOverrides.SourcePath(p),x=p.x,y=p.y,z=p.z,rotation=p.rotation,width=p.w,depth=p.d,
                    meshParts=root.GetComponentsInChildren<MeshFilter>(true).Length,collisionShapes=root.GetComponentsInChildren<Collider>(true).Length,
                    articulatedParts=root.GetComponentsInChildren<Transform>(true).Where(Articulated).Select(t=>AnimationUtility.CalculateTransformPath(t,root)).ToArray(),
                    modelSources=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Scanned ",StringComparison.Ordinal)).Select(t=>t.name.Substring(8)).Distinct().ToArray(),
                    assemblyParts=root.GetComponentsInChildren<MeshFilter>(true).Select(f=>AnimationUtility.CalculateTransformPath(f.transform,root)).ToArray(),
                    materialNames=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.name).Distinct().OrderBy(n=>n,StringComparer.Ordinal).ToArray()});
            }
            // These are the complete editable assemblies. Runtime prefabs are a separate derived bake.
            PrefabUtility.SaveAsPrefabAsset(station,Folder+"/World/station-source.prefab");
            PrefabUtility.SaveAsPrefabAsset(exterior,Folder+"/World/exterior-source.prefab");
            var catalog=new Catalog {instances=entries.ToArray(),types=entries.GroupBy(e=>e.type).Select(g=>new TypeEntry {
                type=g.Key,prefab=typePrefabs[g.Key],placements=g.Count(),variants=g.Select(e=>e.prefab).ToArray()}).ToArray()};
            string catalogPath=Folder+"/prop-catalog.json";File.WriteAllText(catalogPath,JsonUtility.ToJson(catalog,true));
            AssetDatabase.ImportAsset(catalogPath,ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            Debug.Log("Production source saved before static bake: "+entries.Count+" editable placements, "+catalog.types.Length+" registered families and two complete authored worlds. Catalog: "+catalogPath);
        }
        static void SaveAtOrigin(GameObject original,string path)
        {
            var copy=UnityEngine.Object.Instantiate(original);copy.transform.SetParent(null,false);
            copy.name=original.name;copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            try{PrefabUtility.SaveAsPrefabAsset(copy,path);}finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        static void ValidateWorldContracts(WorldData world,Transform[] parts)
        {
            foreach(var door in world.doors)
                if(parts.Count(t=>t.name=="door:"+door.id)!=1)throw new InvalidOperationException("Authored door hinge missing/duplicated: "+door.id);
            foreach(var locker in world.lockers)
                if(parts.Count(t=>t.name=="locker-door:"+locker.id)!=1)throw new InvalidOperationException("Authored locker hinge missing/duplicated: "+locker.id);
            foreach(var prop in world.props)foreach(var drawer in DeadAirDrawers.ForProp(prop))
                if(parts.Count(t=>t.name=="drawer:"+drawer.id)!=1)throw new InvalidOperationException("Authored drawer pivot missing/duplicated: "+drawer.id);
            if(world.props.Select(p=>p.id).Distinct().Count()!=world.props.Length)throw new InvalidOperationException("Duplicate prop IDs cannot be catalogued.");
        }
        static void ValidateAssembly(Transform root,string id)
        {
            var filters=root.GetComponentsInChildren<MeshFilter>(true);
            if(filters.Length==0)throw new InvalidOperationException("Authoring assembly has no geometry: "+id);
            foreach(var f in filters)
            {
                var mesh=f.sharedMesh;if(mesh==null||mesh.vertexCount==0)throw new InvalidOperationException("Missing mesh in "+id+" / "+f.name);
                if(f.transform.childCount>0&&Vector3.Distance(f.transform.localScale,Vector3.one)>.0001f)
                    throw new InvalidOperationException("Scaled mesh is parenting assembly details: "+id+" / "+f.name);
            }
            foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(Articulated))
                if(Vector3.Distance(t.localScale,Vector3.one)>.0001f)throw new InvalidOperationException("Articulation must use an unscaled pivot: "+id+" / "+t.name);
            foreach(var box in root.GetComponentsInChildren<BoxCollider>(true))
                if(box.size.x<=0||box.size.y<=0||box.size.z<=0)throw new InvalidOperationException("Invalid collision proxy: "+id+" / "+box.name);
        }
    }
}
