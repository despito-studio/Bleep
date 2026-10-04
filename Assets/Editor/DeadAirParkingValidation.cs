using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirParkingValidation
    {
        [MenuItem("Dead Air/Validate Complete Parked Cars")]
        public static void Validate()
        {
            const string rootName="BLACKPINE / original web scene";
            var saved=GameObject.Find(rootName);
            if(saved!=null)saved.name="Saved scene / parking validation";
            GameObject temporary=null;
            var materials=new HashSet<Material>();var meshes=new HashSet<Mesh>();
            try
            {
                var data=WorldData.Load();new DeadAirWorld(data);temporary=GameObject.Find(rootName);
                Require(temporary!=null,"The actual runtime world was not loaded.");
                var exterior=temporary.transform.Find("exterior");
                var cars=data.props.Where(prop=>prop.type=="abandonedCar").ToArray();
                Require(cars.Length==3,"The courtyard must contain all three authored parked cars.");
                foreach(var prop in cars)
                {
                    var parked=exterior.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="prop:"+prop.id);
                    var sedan=parked.Find("Parked sedan");Require(sedan!=null,"Missing complete sedan: "+prop.id);
                    Require(Vector3.Distance(parked.position,DeadAirOfficeLayout.Position(prop.x,prop.y,prop.z))<.01f,
                        "The repaired car moved away from its authored parking bay: "+prop.id);
                    var renderers=sedan.GetComponentsInChildren<Renderer>();
                    var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                    Require(bounds.size.x>=prop.w*.96f&&bounds.size.z>=prop.d*.96f&&bounds.size.y>=1.45f,
                        "A parked car must have a full body silhouette rather than an orphaned hubcap: "+prop.id);
                    var tires=renderers.Where(renderer=>renderer.name.StartsWith("Wheel ")&&!renderer.name.StartsWith("Wheel hub ")).ToArray();
                    Require(tires.Length==4&&tires.All(tire=>Mathf.Abs(tire.bounds.min.y-prop.y)<.015f),
                        "The sedan needs four actual grounded tires: "+prop.id);
                    Require(sedan.Find("Passenger cabin").GetComponent<Renderer>().bounds.size.y>.5f,
                        "The parked sedan needs a raised passenger cabin: "+prop.id);
                    var windows=sedan.Find("Six dark windows").GetComponent<MeshFilter>().sharedMesh;
                    Require(windows.vertexCount==24&&windows.bounds.size.y>.4f,"All six dark windows must be three-dimensional cabin faces.");
                    var collider=sedan.GetComponent<BoxCollider>();
                    Require(collider!=null&&Mathf.Abs(collider.size.x-prop.w)<.01f&&Mathf.Abs(collider.size.z-prop.d)<.01f,
                        "The repaired physical body must match the authoritative obstacle footprint: "+prop.id);
                    foreach(var renderer in renderers)
                    {
                        Require(renderer.shadowCastingMode==ShadowCastingMode.On&&renderer.receiveShadows,
                            "Car surfaces must receive the flashlight and cast physical shadows.");
                        foreach(var material in renderer.sharedMaterials)
                            Require(material.shader.name=="Dead Air/Source Surface"&&material.GetColor("_EmissionColor").maxColorComponent<.001f,
                                "Abandoned cars and their switched-off lamps must remain lit, opaque, nonemissive geometry.");
                    }
                    Require(sedan.GetComponentsInChildren<Light>().Length==0,"Switched-off parked headlights must not create glowing dots.");
                }
                Require(exterior.GetComponentsInChildren<Renderer>(true).Where(renderer=>renderer.name=="Batched surfaces / surface_524")
                    .All(renderer=>!renderer.enabled),"The three legacy floating hubcaps must no longer render.");
                int before=exterior.GetComponentsInChildren<Renderer>(true).Length;
                DeadAirWorld.RestoreParkedCars(exterior,data);
                Require(exterior.GetComponentsInChildren<Renderer>(true).Length==before,"Loading the world again duplicated repaired cars.");
                Debug.Log("DEAD AIR PARKING PASSED: three complete lit sedans, four tires and six windows each, authored bay placement/collision, no orphaned glowing hubcaps, idempotent restoration.");
            }
            finally
            {
                if(temporary!=null)
                {
                    foreach(var renderer in temporary.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                        if(material!=null&&(material.hideFlags&HideFlags.DontSave)!=0)materials.Add(material);
                    foreach(var filter in temporary.GetComponentsInChildren<MeshFilter>(true))
                        if(filter.sharedMesh!=null&&(filter.sharedMesh.hideFlags&HideFlags.DontSave)!=0)meshes.Add(filter.sharedMesh);
                    UnityEngine.Object.DestroyImmediate(temporary);
                }
                foreach(var material in materials)if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                foreach(var mesh in meshes)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
                if(saved!=null)saved.name=rootName;
            }
        }

        static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Parking validation: "+message);}
    }
}
