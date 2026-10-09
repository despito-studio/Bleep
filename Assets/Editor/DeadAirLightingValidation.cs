using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirLightingValidation
    {
        const string RootName="BLACKPINE / original web scene";

        [MenuItem("Dead Air/Validate Practical Lighting")]
        public static void Validate()
        {
            var savedRoot=GameObject.Find(RootName);
            bool hadFlicker=PlayerPrefs.HasKey("DeadAir.Flicker");
            int savedFlicker=PlayerPrefs.GetInt("DeadAir.Flicker",1);
            string[] preferenceKeys={"DeadAir.GraphicsQuality","DeadAir.ShadowQuality"};
            var preferences=preferenceKeys.ToDictionary(k=>k,k=>PlayerPrefs.HasKey(k)?(int?)PlayerPrefs.GetInt(k):null);
            GameObject testRoot=null;
            var temporaryMaterials=new HashSet<Material>();
            if(savedRoot!=null)savedRoot.name="Saved scene / lighting validation";
            try
            {
                PlayerPrefs.SetInt("DeadAir.Flicker",0);
                PlayerPrefs.SetInt("DeadAir.GraphicsQuality",2);
                PlayerPrefs.SetInt("DeadAir.ShadowQuality",2);
                var world=new DeadAirWorld(WorldData.Load());
                testRoot=GameObject.Find(RootName);
                Require(testRoot!=null,"Runtime world was not created.");
                var station=testRoot.transform.Find("station");
                var lamps=station.GetComponentsInChildren<Light>(true).Where(l=>l.name.StartsWith("lamp:zone-")).ToArray();
                Require(lamps.Length>=30,"Furnished rooms and circulation need their own ceiling lamps.");
                var fullOutput=lamps.Select(l=>l.intensity).ToArray();
                foreach(var light in lamps)
                {
                    Require(light.type==LightType.Point,
                        light.name+" must spread soft illumination throughout the room below the diffuser.");
                    Require(light.renderMode==LightRenderMode.ForcePixel,
                        light.name+" must shade the middle of large walls and floors.");
                    Require(light.shadows==LightShadows.Soft && light.range>=8 && light.intensity>=.3f,
                        light.name+" needs a useful pool of light and physical shadow occlusion.");
                    Require(light.shadowCustomResolution>=512,"Soft practical shadows need sufficient local map detail.");
                }
                var fills=station.GetComponentsInChildren<Light>(true).Where(l=>l.name.StartsWith("lamp:bounce-")).ToArray();
                Require(fills.Length>=lamps.Length/2 && fills.All(l=>l.type==LightType.Point&&l.shadows==LightShadows.None&&l.range>=4),
                    "Practical pools need local shadow-free diffuse fill to preserve readable room materials.");
                Require(lamps.Count(l=>l.color.r>l.color.b*1.15f)>lamps.Length/2,
                    "Occupied offices and halls need warm practical light against the cool night environment.");
                var ambient=RenderSettings.ambientProbe;
                Require(RenderSettings.ambientMode==UnityEngine.Rendering.AmbientMode.Custom,
                    "The explicit diffuse probe must use Custom mode instead of an automatically overwritten sky probe.");
                var directions=new[]{Vector3.up,Vector3.down,Vector3.right};
                var radiance=new Color[directions.Length];ambient.Evaluate(directions,radiance);
                foreach(var color in radiance)
                    Require(color.maxColorComponent>.005f && color.maxColorComponent<.1f,
                        "Ceilings, floors and walls must receive restrained readable diffuse ambient radiance.");
                var state=new RoomState {phase="playing"};
                world.Apply(state);
                for(int i=0;i<lamps.Length;i++)
                    Require(lamps[i].intensity < .01f,
                        "Lighting must be completely off before generator and breaker: "+lamps[i].name);
                state.power=true;
                state.breakerOn=true;
                world.Apply(state);
                for(int i=0;i<lamps.Length;i++)
                    Require(Mathf.Abs(lamps[i].intensity-fullOutput[i])<.001f,
                        "Repairing generator and toggling breaker must restore fixture output: "+lamps[i].name);
                state.broadcast=true;
                world.Apply(state);
                for(int i=0;i<lamps.Length;i++)
                    Require(lamps[i].intensity>fullOutput[i]*.8f && lamps[i].color.g>.3f,
                        "Broadcast alarm lighting must retain visibility: "+lamps[i].name);

                var nearLamp=lamps.First(l=>l.transform.position.y<4 && l.transform.position.z>-10);
                var cameraObject=new GameObject("Lighting validation camera");
                cameraObject.transform.SetParent(testRoot.transform,false);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
                camera.transform.position=new Vector3(nearLamp.transform.position.x,1.65f,nearLamp.transform.position.z+2);
                camera.transform.rotation=Quaternion.LookRotation(Vector3.forward);
                world.PrepareRender(camera);
                Require(nearLamp.enabled,"A nearby fixture behind the camera still needs to light the view.");
                foreach(int quality in new[]{0,1,2})
                {
                    PlayerPrefs.SetInt("DeadAir.ShadowQuality",quality);
                    world.PrepareRender(camera);
                    int count=lamps.Count(l=>l.enabled&&l.shadows!=LightShadows.None);
                    int budget=quality==2?3:quality==1?1:0;
                    Require(count<=budget && (quality==0||count>0),
                        "The selected practical shadow quality must honor its 0/1/3 map budget: setting="+quality+", actual="+count);
                    if(quality==2)Require(lamps.Where(l=>l.enabled&&l.shadows!=LightShadows.None).All(l=>l.shadows==LightShadows.Soft&&l.shadowCustomResolution>=512),
                        "The high practical quality must keep soft, detailed local shadow maps.");
                }
                Require(lamps.Where(l=>l.transform.position.y>4).All(l=>!l.enabled),
                    "A solid office ceiling should cull practicals on the other floor.");
                Require(lamps.Where(l=>l.transform.position.y<4 && l.transform.position.z<-40).All(l=>!l.enabled),
                    "Lights far behind the view must not redraw the whole office shell.");
                camera.transform.position=new Vector3(0,3.6f,-45);
                camera.transform.rotation=Quaternion.LookRotation(Vector3.back);
                world.PrepareRender(camera);
                Require(lamps.Any(l=>l.transform.position.y<4&&l.enabled) && lamps.Any(l=>l.transform.position.y>4&&l.enabled),
                    "The open stairwell must preserve relevant lamps from both floors.");
                camera.transform.position=new Vector3(0,1.65f,9);
                camera.transform.rotation=Quaternion.LookRotation(Vector3.back);
                var occluder=new GameObject("Lighting occlusion regression wall");
                occluder.transform.SetParent(testRoot.transform,false);
                occluder.transform.position=new Vector3(0,4,7);
                occluder.AddComponent<BoxCollider>().size=new Vector3(40,10,.5f);
                Physics.SyncTransforms();
                world.PrepareRender(camera);
                Require(lamps.All(l=>!l.enabled),"A full opaque facade must cull the interior practical pools it hides.");
                UnityEngine.Object.DestroyImmediate(occluder);
                Physics.SyncTransforms();

                int diffuseCount=0;
                foreach(var renderer in testRoot.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null)continue;
                    if((material.hideFlags&HideFlags.DontSave)!=0)temporaryMaterials.Add(material);
                    if(material.shader.name!="Dead Air/Source Surface" && material.shader.name!="Dead Air/Source Unlit")continue;
                    if(LegitimateEmitter(material.name))continue;
                    Require(material.shader.name!="Dead Air/Source Unlit",
                        "Physical props and labels must receive light: "+renderer.name+" / "+material.name);
                    var emission=material.GetColor("_EmissionColor");
                    Require(emission.maxColorComponent<.001f,
                        "Nonluminous boxes, paper and surfaces must not emit: "+renderer.name+" / "+material.name);
                    diffuseCount++;
                }
                Require(diffuseCount>100,"The validation must inspect the furnished office's real diffuse surfaces.");
                Require(QualitySettings.pixelLightCount>=8,"The runtime light budget is too small for nearby practicals and a flashlight.");
                Debug.Log("DEAD AIR LIGHTING PASSED: warm, soft point practicals with local diffuse fill, selected 0/1/3 shadow budgets, usable battery lighting, restored generator output, readable alarm lighting, wall/floor occlusion and "+diffuseCount+" nonemissive physical material slots.");
            }
            finally
            {
                if(testRoot!=null)
                {
                    foreach(var renderer in testRoot.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                        if(material!=null && (material.hideFlags&HideFlags.DontSave)!=0)temporaryMaterials.Add(material);
                    UnityEngine.Object.DestroyImmediate(testRoot);
                }
                foreach(var material in temporaryMaterials)if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(savedRoot!=null)savedRoot.name=RootName;
                if(hadFlicker)PlayerPrefs.SetInt("DeadAir.Flicker",savedFlicker);else PlayerPrefs.DeleteKey("DeadAir.Flicker");
                foreach(var setting in preferences)if(setting.Value.HasValue)PlayerPrefs.SetInt(setting.Key,setting.Value.Value);else PlayerPrefs.DeleteKey(setting.Key);
            }
        }

        static bool LegitimateEmitter(string name)
        {
            if(name=="light-panel" || name.StartsWith("light-panel"))return true;
            if(!name.StartsWith("surface_"))return false;
            string number=name.Substring(8).Split(' ')[0];
            if(!int.TryParse(number,out int id))return false;
            return id>=298&&id<=308 || id>=328&&id<=463 || id>=496&&id<=513 ||
                id==677 || id==678 || id==679 || id==684 || id==685 || id==696 ||
                id>=797&&id<=819;
        }

        static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Practical lighting validation: "+message);}
    }
}
