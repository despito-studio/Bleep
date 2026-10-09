using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirMonsterPresentationValidation
    {
        [MenuItem("Dead Air/Validate Monster Presentation and Headlamps")]
        public static void Validate()
        {
            const string rootName="BLACKPINE / original web scene";
            var saved=GameObject.Find(rootName);
            if(saved!=null)saved.name="Saved scene / monster presentation validation";
            GameObject temporary=null;
            var materials=new HashSet<Material>();
            try
            {
                var exteriorPrefab=Resources.Load<GameObject>("World/exterior");
                Require(exteriorPrefab!=null,"The saved exterior prefab was not loaded.");
                Require(exteriorPrefab.GetComponentsInChildren<Light>(true).Count(light=>light.name.StartsWith("lamp:van-headlamp-"))==2,
                    "The two physical van headlamps must survive static batching in the saved prefab.");
                Require(exteriorPrefab.GetComponentsInChildren<Renderer>(true).Count(renderer=>renderer.name.StartsWith("headlamp-lens:"))==2,
                    "Static batching must preserve the two individual glass lenses for emitter placement and highlight control.");
                Require(exteriorPrefab.GetComponentsInChildren<Renderer>(true).All(renderer=>
                    renderer.sharedMaterials.All(material=>material==null||material.name!="surface_697")),
                    "Old translucent browser beam shells must not occlude the real headlamps or hang over the road.");
                var world=new DeadAirWorld(WorldData.Load());
                temporary=GameObject.Find(rootName);
                Require(temporary!=null,"The physical world was not loaded.");
                var beams=temporary.GetComponentsInChildren<Light>(true)
                    .Where(light=>light.name.StartsWith("lamp:van-headlamp-")).ToArray();
                Require(beams.Length==2,"The two visible van lenses need their own headlamp emitters.");
                Require(Vector3.Distance(beams[0].transform.position,beams[1].transform.position)>1.5f,
                    "Headlamp beams must originate at the separate physical lenses.");
                foreach(var beam in beams)
                {
                    Require(beam.type==LightType.Spot && beam.renderMode==LightRenderMode.ForcePixel && beam.shadows==LightShadows.Hard,
                        "Headlamps must cast useful pixel-lit pools with wall occlusion.");
                    Require(beam.range>=12 && beam.range<=20 && beam.intensity>2 && beam.intensity<5,
                        "The van headlamp must stay local to the road.");
                    float depression=Mathf.Asin(-beam.transform.forward.y)*Mathf.Rad2Deg;
                    Require(depression-beam.spotAngle*.5f>3,
                        "The full headlamp cone must point below the horizon, rather than into the office windows.");
                    Require(beam.transform.localPosition.z<-2.50f && beam.transform.localPosition.y>.9f && beam.transform.localPosition.y<1.1f,
                        "The beam must begin just ahead of the real glass, outside the van housing.");
                    var roadPoint=beam.transform.position+beam.transform.forward*(beam.transform.position.y/-beam.transform.forward.y);
                    Require(roadPoint.y<.01f && roadPoint.z<beam.transform.position.z-1.5f,
                        "The centre of each beam must intersect the road ahead of the vehicle.");
                }

                var rigs=temporary.GetComponentsInChildren<DeadAirMonsterMotion>(true);
                Require(rigs.Length==2,"Both regular and broadcast monsters need articulated presentation.");
                foreach(var rig in rigs)
                {
                    Require(rig.AnimatedJointCount>=14 && rig.HasBlindfold,
                        "The existing source model needs articulated limbs, a listening head and an opaque eye wrap.");
                    foreach(var renderer in rig.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                        Require(!material.HasProperty("_EmissionColor") || material.GetColor("_EmissionColor").maxColorComponent<.001f,
                            "A blind monster's face and cloth must respond to scene light instead of emitting it.");
                    var head=rig.HeadPivot;
                    Require(head!=null && head.localPosition.y>1.3f,"The new adult listening head needs its own pivot above the shoulders.");
                    var legs=rig.transform.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Anchor knee").ToArray();
                    Require(legs.Length==2,"The authored legs need distinct knee pivots.");
                    rig.SetPreviewPose("patrol",0,.25f);
                    var first=legs.Select(t=>t.localRotation).ToArray();
                    rig.SetPreviewPose("patrol",.4f,.75f);
                    Require(legs.Where((t,i)=>Quaternion.Angle(first[i],t.localRotation)>4).Any(),
                        "Patrol gait must articulate the legs, instead of sliding a rigid body.");
                    rig.SetPreviewPose("search",1);
                    float restingHead=rig.transform.InverseTransformPoint(head.position).x;
                    rig.SetPreviewPose("peek",1,.25f,1);
                    float leaningHead=rig.transform.InverseTransformPoint(head.position).x;
                    Require(leaningHead-restingHead>.32f && leaningHead-restingHead<.55f,
                        "The head must lean around the corner while the hips remain behind cover.");
                    var state=new RoomState {elapsed=1,enemy=new EnemyState {active=true,behaviour="vanished"}};
                    world.Apply(state);
                    Require(rigs.All(value=>!value.gameObject.activeSelf),
                        "A vanished encounter must disable both monster models on every peer.");
                }
                Debug.Log("DEAD AIR MONSTER PRESENTATION PASSED: articulated listening/gait/peek poses, opaque nonemissive blindfold, hidden encounter models and two downward shadow-casting lens-mounted van beams.");
            }
            finally
            {
                if(temporary!=null)
                {
                    foreach(var renderer in temporary.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                        if(material!=null && (material.hideFlags&HideFlags.DontSave)!=0)materials.Add(material);
                    UnityEngine.Object.DestroyImmediate(temporary);
                }
                foreach(var material in materials)if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(saved!=null)saved.name=rootName;
            }
        }

        static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Monster presentation validation: "+message);}
    }
}
