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
            GameObject testRoot=null;
            var temporaryMaterials=new HashSet<Material>();
            if(savedRoot!=null)savedRoot.name="Saved scene / lighting validation";
            try
            {
                PlayerPrefs.SetInt("DeadAir.Flicker",0);
                var world=new DeadAirWorld(WorldData.Load());
                testRoot=GameObject.Find(RootName);
                Require(testRoot!=null,"Runtime world was not created.");
                var station=testRoot.transform.Find("station");
                var lamps=station.GetComponentsInChildren<Light>(true).Where(l=>l.name.StartsWith("lamp:zone-")).ToArray();
                Require(lamps.Length>=30,"Furnished rooms and circulation need their own ceiling lamps.");
                var fullOutput=lamps.Select(l=>l.intensity).ToArray();
                foreach(var light in lamps)
                {
                    Require(light.type==LightType.Spot && Vector3.Dot(light.transform.forward,Vector3.down)>.99f,
                        light.name+" must illuminate the room below the diffuser.");
                    Require(light.renderMode==LightRenderMode.ForcePixel,
                        light.name+" must shade the middle of large walls and floors.");
                    Require(light.shadows!=LightShadows.None && light.range>=6 && light.spotAngle>=110 && light.intensity>=2,
                        light.name+" needs a useful pool of light and physical shadow occlusion.");
                    Require(light.shadowCustomResolution==256,"Practical shadows need their local 256px render budget.");
                }
                var ambient=RenderSettings.ambientProbe;
                Require(RenderSettings.ambientMode==UnityEngine.Rendering.AmbientMode.Custom,
                    "The explicit diffuse probe must use Custom mode instead of an automatically overwritten sky probe.");
                var directions=new[]{Vector3.up,Vector3.down,Vector3.right};
                var radiance=new Color[directions.Length];ambient.Evaluate(directions,radiance);
                foreach(var color in radiance)
                    Require(Mathf.Abs(color.r-.018f)<.001f && Mathf.Abs(color.g-.021f)<.001f && Mathf.Abs(color.b-.027f)<.001f,
                        "Ceilings, floors and walls must receive the authored low diffuse ambient radiance.");
                var state=new RoomState {phase="playing"};
                world.Apply(state);
                for(int i=0;i<lamps.Length;i++)
                    Require(Mathf.Abs(lamps[i].intensity/fullOutput[i]-.6f)<.001f && lamps[i].intensity>1,
                        "Battery lighting must remain usable before the generator: "+lamps[i].name);
                state.power=true;
                world.Apply(state);
                for(int i=0;i<lamps.Length;i++)
                    Require(Mathf.Abs(lamps[i].intensity-fullOutput[i])<.001f,
                        "Repairing the generator must restore the authored fixture output: "+lamps[i].name);
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
                Require(lamps.Count(l=>l.enabled&&l.shadows!=LightShadows.None)<=8,
                    "Nearby practicals must share a bounded shadow-map budget.");
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
                Debug.Log("DEAD AIR LIGHTING PASSED: downward shadow-casting per-pixel practicals, usable battery lighting, restored generator output, readable alarm lighting and "+diffuseCount+" nonemissive physical material slots.");
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
            }
        }

        static bool LegitimateEmitter(string name)
        {
            if(name=="light-panel")return true;
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
