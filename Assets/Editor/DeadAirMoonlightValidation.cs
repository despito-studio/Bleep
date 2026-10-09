using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirMoonlightValidation
    {
        const string Output="Art/OfficeRevision/Rendering";

        [MenuItem("Dead Air/Validate Moonlight and Rendering Budgets")]
        public static void Validate()
        {
            var evidence=new List<string>();
            var stage=new GameObject("Moonlight structural validation");
            Material skyMaterial=null;
            try
            {
                var sky=new DeadAirSky(stage.transform);
                var moon=sky.MoonLight;
                Require(moon!=null && moon.type==LightType.Directional && moon.intensity>=.3f,
                    "The visible moon needs a useful directional light for the real yard.");
                Require(moon.shadows==LightShadows.Soft && moon.shadowStrength>.99f && moon.shadowCustomResolution==1024,
                    "Moonlit surfaces need soft shadows and complete roof/wall occlusion.");
                Require(moon.renderMode==LightRenderMode.ForcePixel && (moon.cullingMask&((1<<28)|(1<<29)|(1<<30)))==0,
                    "Moonlight must illuminate world surfaces without changing the menu or equipment presentation.");
                var material=stage.GetComponentInChildren<DeadAirSkyResources>().material;
                skyMaterial=material;
                Require(material!=null && Vector3.Angle(-moon.transform.forward,material.GetVector("_MoonDirection"))<.01f,
                    "Physical lunar illumination must originate from the moon drawn in the sky.");
                sky.UpdatePosition(new Vector3(13,2,-9));
                Require(Vector3.Distance(stage.transform.Find("Dead Air Night Sky & Moon/Distant night sky").position,new Vector3(13,2,-9))<.001f,
                    "The distant moon must remain fixed in direction while the sky follows the camera.");
                sky.UpdatePosition(new Vector3(0,-2,0));Require(!moon.enabled,"The windowless opaque basement still submits direct lunar light/shadows.");
                sky.UpdatePosition(new Vector3(0,-.9f,0));Require(moon.enabled,"The upper stair landing lost its direct lunar light.");
                evidence.Add("PASSED: physical moon direction matches the sky disc, .34 directional intensity, full soft roof/wall occlusion, world-only layers, position-independent sky.");
                evidence.Add("PASSED: direct moon is culled only below -1m in the fully covered windowless basement; the upper stair landing restores it.");
                ValidateReflectionReceivers(stage.transform,evidence);
                ValidateLightCulling(stage.transform,evidence);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stage);
                if(skyMaterial!=null)UnityEngine.Object.DestroyImmediate(skyMaterial);
            }
            Directory.CreateDirectory(Output);File.WriteAllLines(Path.Combine(Output,"structural-report.txt"),evidence);
            Debug.Log("DEAD AIR MOONLIGHT / RENDERING BUDGETS PASSED: lunar direction/intensity/occlusion configuration, visible polished receivers, zero queries for unpowered lamps and interior/courtyard floor relevance.");
        }

        static void ValidateReflectionReceivers(Transform parent,List<string> evidence)
        {
            const int layer=26;
            var camera=new GameObject("Reflection receiver validation").AddComponent<Camera>();camera.transform.SetParent(parent,false);
            camera.enabled=false;camera.cullingMask=1<<layer;camera.fieldOfView=60;camera.farClipPlane=20;
            camera.transform.position=new Vector3(2500,1.65f,2500);camera.transform.LookAt(new Vector3(2500,0,2505));
            var receiver=GameObject.CreatePrimitive(PrimitiveType.Cube);receiver.name="Polished receiver";receiver.layer=layer;
            receiver.transform.SetParent(parent,false);receiver.transform.position=new Vector3(2500,-.05f,2505);receiver.transform.localScale=new Vector3(8,.1f,8);
            var material=new Material(Shader.Find("Dead Air/Source Surface")){hideFlags=HideFlags.DontSave};
            material.SetFloat("_PlanarReflection",1);receiver.GetComponent<Renderer>().sharedMaterial=material;
            try
            {
                var reflections=camera.gameObject.AddComponent<DeadAirPlanarReflections>();
                Require(reflections.HasVisibleReceiver(0),"Visible polished floor incorrectly loses its reflection.");
                Require(!reflections.HasVisibleReceiver(4.2f),"Carpeted upper floor unnecessarily renders the downstairs mirror.");
                camera.transform.rotation=Quaternion.LookRotation(Vector3.up,Vector3.forward);
                Require(!reflections.HasVisibleReceiver(0),"Looking only at the sky still renders a floor mirror.");
                camera.transform.LookAt(new Vector3(2500,0,2505));receiver.SetActive(false);
                Require(!reflections.HasVisibleReceiver(0),"An inactive polished floor still schedules a mirrored camera.");
                var point=new GameObject("Local point shadow suspension test").AddComponent<Light>();point.transform.SetParent(parent,false);
                point.type=LightType.Point;point.intensity=1;point.shadows=LightShadows.Soft;point.cullingMask=1<<layer;
                var spot=new GameObject("Local spot shadow suspension test").AddComponent<Light>();spot.transform.SetParent(parent,false);
                spot.type=LightType.Spot;spot.intensity=1;spot.shadows=LightShadows.Hard;spot.cullingMask=1<<layer;
                var moon=parent.GetComponentInChildren<DeadAirSkyResources>().transform.Find("Directional moonlight").GetComponent<Light>();
                var lunarShadows=moon.shadows;int lunarResolution=moon.shadowCustomResolution;var globalShadows=QualitySettings.shadows;
                DeadAirPlanarReflections.RegisterLocalShadowLight(point);DeadAirPlanarReflections.RegisterLocalShadowLight(spot);
                var suspend=typeof(DeadAirPlanarReflections).GetMethod("SuspendLocalShadows",BindingFlags.Instance|BindingFlags.NonPublic);
                var restore=typeof(DeadAirPlanarReflections).GetMethod("RestoreLocalShadows",BindingFlags.Instance|BindingFlags.NonPublic);
                try
                {
                    suspend.Invoke(reflections,new object[]{1<<layer});
                    Require(point.shadows==LightShadows.None && spot.shadows==LightShadows.None && reflections.SuspendedLocalShadowCount>=2,
                        "Mirror render must suspend both local cube and spotlight maps.");
                    Require(moon.shadows==lunarShadows && QualitySettings.shadows==globalShadows,
                        "Mirror optimization disabled moon/roof occlusion.");
                    Require(moon.shadowCustomResolution==1024 && moon.shadowCustomResolution==lunarResolution && moon.shadows==LightShadows.Soft,
                        "Mirror rendering must preserve the fixed 1024 moon map and soft roof/wall occlusion without resizing per camera.");
                }
                finally{restore.Invoke(reflections,null);}
                Require(point.shadows==LightShadows.Soft && spot.shadows==LightShadows.Hard && reflections.SuspendedLocalShadowCount==0 && moon.shadowCustomResolution==lunarResolution,
                    "Mirror render must restore the exact local shadow modes without leaking state into the main camera.");
                evidence.Add("PASSED: polished floor retains reflections; other storey, sky-only view and inactive receiver suppress unnecessary mirrored-camera work.");
                evidence.Add("PASSED: mirror shadow suspension preserves the fixed 1024 soft moon map and full roof occlusion, suspends only local point/spot maps and restores their exact modes.");
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
        }

        static void ValidateLightCulling(Transform parent,List<string> evidence)
        {
            var savedRoot=GameObject.Find("BLACKPINE / original web scene");bool savedActive=savedRoot!=null&&savedRoot.activeSelf;
            bool hadQuality=PlayerPrefs.HasKey("DeadAir.ShadowQuality");int quality=PlayerPrefs.GetInt("DeadAir.ShadowQuality",2);
            if(savedRoot!=null)savedRoot.SetActive(false);
            try
            {
                var station=new GameObject("Isolated light culling fixture");station.transform.SetParent(parent,false);
                var group=new GameObject("sector:fixture");group.transform.SetParent(station.transform,false);
                var surface=GameObject.CreatePrimitive(PrimitiveType.Cube);surface.transform.SetParent(group.transform,false);
                surface.transform.position=new Vector3(0,1,0);surface.transform.localScale=Vector3.one*.1f;
                UnityEngine.Object.DestroyImmediate(surface.GetComponent<Collider>());
                var light=new GameObject("lamp:zone-fixture").AddComponent<Light>();light.transform.SetParent(group.transform,false);
                light.transform.position=new Vector3(0,2.8f,0);light.type=LightType.Point;light.range=12;light.intensity=0;light.shadows=LightShadows.Soft;
                var upper=new GameObject("lamp:zone-upper-fixture").AddComponent<Light>();upper.transform.SetParent(group.transform,false);
                upper.transform.position=new Vector3(0,7,0);upper.type=LightType.Point;upper.range=12;upper.intensity=0;upper.shadows=LightShadows.Soft;
                var camera=new GameObject("Light budget camera").AddComponent<Camera>();camera.transform.SetParent(parent,false);camera.enabled=false;
                camera.transform.position=new Vector3(0,1.65f,0);camera.transform.rotation=Quaternion.LookRotation(Vector3.forward);
                var data=new WorldData{rooms=new[]{new RoomData{name="Fixture",minX=-2,maxX=2,minZ=-2,maxZ=2,floorY=0},
                    new RoomData{name="Upper fixture",minX=-2,maxX=2,minZ=-2,maxZ=2,floorY=4.2f}}};
                var culling=new DeadAirCulling(data,station.transform,null,new HashSet<Transform>(),new Dictionary<string,DeadAirHingedDoor>());
                culling.Update(null,camera,true);
                Require(culling.VisibilityRaycasts==0 && culling.EnabledPracticalCount==0 && culling.ShadowPracticalCount==0,
                    "Unpowered fixtures consume visibility queries or light/shadow budget.");
                light.intensity=upper.intensity=1;PlayerPrefs.SetInt("DeadAir.ShadowQuality",2);culling.Update(null,camera,true);
                Require(light.enabled && !upper.enabled && culling.EnabledPracticalCount==1 && culling.ShadowPracticalCount==1,
                    "An enclosed ground room needs its own light and must reject the upper-storey pool.");
                // The gap has no authored room/ceiling, though it lies inside the
                // overall building rectangle. Both floors remain relevant here.
                camera.transform.position=new Vector3(4,1.65f,0);culling.Update(null,camera,true);
                Require(light.enabled && upper.enabled,"An open courtyard incorrectly inherits the rectangular envelope's ceiling.");
                PlayerPrefs.SetInt("DeadAir.ShadowQuality",0);culling.Update(null,camera,true);
                Require(light.enabled && upper.enabled && culling.ShadowPracticalCount==0,
                    "Disabling shadow maps must preserve the actual illumination.");
                light.intensity=upper.intensity=0;PlayerPrefs.SetInt("DeadAir.ShadowQuality",2);
                var hiddenGroup=new GameObject("sector:offscreen lunar caster");hiddenGroup.transform.SetParent(station.transform,false);
                var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.transform.SetParent(hiddenGroup.transform,false);
                caster.transform.position=new Vector3(5.9f,4,-8);caster.transform.localScale=new Vector3(1,8,1);
                var casterRenderer=caster.GetComponent<Renderer>();casterRenderer.shadowCastingMode=ShadowCastingMode.On;
                var unrelated=GameObject.CreatePrimitive(PrimitiveType.Cube);unrelated.transform.SetParent(hiddenGroup.transform,false);
                unrelated.transform.position=new Vector3(-15,4,-8);unrelated.transform.localScale=new Vector3(1,8,1);
                var unrelatedRenderer=unrelated.GetComponent<Renderer>();unrelatedRenderer.shadowCastingMode=ShadowCastingMode.On;
                var shell=new GameObject("shell");shell.transform.SetParent(station.transform,false);
                var roof=GameObject.CreatePrimitive(PrimitiveType.Cube);roof.transform.SetParent(shell.transform,false);
                roof.transform.position=new Vector3(0,10,-20);roof.transform.localScale=new Vector3(20,.2f,20);
                var roofRenderer=roof.GetComponent<Renderer>();roofRenderer.shadowCastingMode=ShadowCastingMode.On;
                camera.transform.position=new Vector3(0,1.65f,0);camera.transform.LookAt(new Vector3(0,0,3));
                var shadowCulling=new DeadAirCulling(data,station.transform,null,new HashSet<Transform>(),new Dictionary<string,DeadAirHingedDoor>());
                shadowCulling.Update(null,camera,true);
                Require(casterRenderer.enabled && casterRenderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly && shadowCulling.ShadowOnlyRendererCount>0,
                    "A hidden caster whose moon shadow reaches the view disappears from the shadow pass.");
                Require(!unrelatedRenderer.enabled,"One relevant caster needlessly resubmits unrelated hidden furniture in the same sector.");
                camera.transform.position=new Vector3(5.9f,1.65f,-8);shadowCulling.Update(null,camera,true);
                Require(casterRenderer.enabled && unrelatedRenderer.enabled && casterRenderer.shadowCastingMode==ShadowCastingMode.On && unrelatedRenderer.shadowCastingMode==ShadowCastingMode.On,
                    "Re-entering a retained sector fails to restore ordinary visible rendering and original shadow modes.");
                camera.transform.position=new Vector3(0,1.65f,0);camera.transform.LookAt(new Vector3(0,0,3));
                shadowCulling.Update(null,camera,true);shadowCulling.Update(null,camera,true);
                Require(casterRenderer.enabled && casterRenderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly && !unrelatedRenderer.enabled && shadowCulling.ShadowOnlyRendererCount==1,
                    "Visible-to-hidden and repeated updates leave the cached cast-only mask inconsistent.");
                Require(roofRenderer.enabled && roofRenderer.shadowCastingMode==ShadowCastingMode.On && roof.GetComponent<Collider>().enabled && caster.GetComponent<Collider>().enabled,
                    "Room visibility culling must preserve opaque structural occlusion and physical colliders.");
                camera.gameObject.AddComponent<DeadAirPlanarReflections>();
                camera.transform.position=new Vector3(0,-2,0);camera.transform.LookAt(new Vector3(0,-4.2f,3));
                shadowCulling.Update(null,camera,true);
                Require(!casterRenderer.enabled && shadowCulling.ShadowOnlyRendererCount==0,
                    "A windowless underground camera unnecessarily retains aboveground lunar casters.");
                camera.transform.position=new Vector3(0,1.65f,0);camera.transform.LookAt(new Vector3(0,0,3));
                PlayerPrefs.SetInt("DeadAir.ShadowQuality",0);shadowCulling.Update(null,camera,true);
                Require(!casterRenderer.enabled && roofRenderer.enabled,"Shadow-free preset still submits hidden furniture or hides the building shell.");
                evidence.Add("PASSED: unpowered fixtures use 0 occlusion rays/0 shadow maps; a roofed room culls the other floor; an open courtyard retains both; shadow-free preset preserves light pools.");
                evidence.Add("PASSED: offscreen geometry whose lunar shadow reaches visible floor stays ShadowsOnly; opaque shell and all colliders remain enabled; shadow-free preset skips hidden furniture.");
                evidence.Add("PASSED: per-renderer shadow relevance skips unrelated furniture within the same sector; a windowless basement view skips aboveground lunar casters.");
            }
            finally
            {
                if(savedRoot!=null)savedRoot.SetActive(savedActive);
                if(hadQuality)PlayerPrefs.SetInt("DeadAir.ShadowQuality",quality);else PlayerPrefs.DeleteKey("DeadAir.ShadowQuality");
            }
        }

        [MenuItem("Dead Air/Validate Physical Moonlight With GPU")]
        public static void ValidateWithGpu()
        {
            Validate();
            Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"Moonlight GPU validation needs a graphics device.");
            const int layer=27;
            var stage=new GameObject("Physical moonlight GPU fixture");stage.transform.position=new Vector3(3000,0,3000);
            var previousTarget=RenderTexture.active;var previousAmbientMode=RenderSettings.ambientMode;var previousAmbient=RenderSettings.ambientLight;
            var previousProbe=RenderSettings.ambientProbe;bool previousFog=RenderSettings.fog;
            var previousShadows=QualitySettings.shadows;float previousDistance=QualitySettings.shadowDistance;
            int previousCascades=QualitySettings.shadowCascades;var previousResolution=QualitySettings.shadowResolution;
            var lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);var enabled=new bool[lights.Length];
            Material material=null,skyMaterial=null;RenderTexture frame=null;
            try
            {
                for(int i=0;i<lights.Length;i++){enabled[i]=lights[i].enabled;lights[i].enabled=false;}
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.ambientProbe=new SphericalHarmonicsL2();RenderSettings.fog=false;
                QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=25;QualitySettings.shadowCascades=2;QualitySettings.shadowResolution=ShadowResolution.High;
                var sky=new DeadAirSky(stage.transform);var moon=sky.MoonLight;moon.cullingMask=1<<layer;
                skyMaterial=stage.GetComponentInChildren<DeadAirSkyResources>().material;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.layer=layer;floor.transform.SetParent(stage.transform,false);
                floor.transform.localPosition=new Vector3(0,-.1f,1.5f);floor.transform.localScale=new Vector3(10,.2f,10);
                var roof=GameObject.CreatePrimitive(PrimitiveType.Cube);roof.layer=layer;roof.transform.SetParent(stage.transform,false);
                roof.transform.localPosition=new Vector3(0,2.5f,0);roof.transform.localScale=new Vector3(22,.3f,22);
                material=new Material(Shader.Find("Dead Air/Source Surface")){color=new Color(.65f,.65f,.65f),hideFlags=HideFlags.DontSave};
                floor.GetComponent<Renderer>().sharedMaterial=material;roof.GetComponent<Renderer>().sharedMaterial=material;
                var camera=new GameObject("Moon illumination camera").AddComponent<Camera>();camera.transform.SetParent(stage.transform,false);camera.enabled=false;
                camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.nearClipPlane=.05f;camera.farClipPlane=30;camera.fieldOfView=55;camera.renderingPath=RenderingPath.Forward;
                camera.transform.localPosition=new Vector3(0,1.65f,-3);camera.transform.LookAt(stage.transform.TransformPoint(new Vector3(0,0,1.5f)));
                frame=new RenderTexture(480,320,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);frame.Create();camera.targetTexture=frame;
                roof.SetActive(false);moon.enabled=false;float dark=Capture(camera,frame,"moon-01-light-off.png");
                moon.enabled=true;moon.intensity=.19f;float previous=Capture(camera,frame,"moon-02-prior-intensity.png");
                moon.intensity=.34f;float exposed=Capture(camera,frame,"moon-03-open-yard.png");
                roof.SetActive(true);float covered=Capture(camera,frame,"moon-04-opaque-roof.png");
                float priorGain=previous-dark,gain=exposed-dark;
                // Built-in light color/intensity conversion in Linear rendering
                // is not a guaranteed linear radiance multiplier. Verify the real
                // floor gain rather than assuming the inspector-value ratio.
                Require(gain>=.003f && priorGain>0 && gain>priorGain*1.5f,
                    "The strengthened moon must usefully increase real floor illumination from .19 -> .34 intensity: off="+dark+", prior="+previous+", open="+exposed);
                Require(covered-dark<=gain*.15f+.0005f,
                    "The strengthened moon leaks through opaque roofs: off="+dark+", open="+exposed+", roof="+covered);
                Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"moon-gpu-report.txt"),
                    "PASS: physical lunar surface illumination, gain from prior .19 to .34 intensity and opaque roof occlusion; linear GPU floor ROI mean RGB: OFF="+dark.ToString("F4")+", PRIOR="+previous.ToString("F4")+", OPEN="+exposed.ToString("F4")+", ROOF="+covered.ToString("F4")+"; net NEW/PRIOR="+(gain/priorGain).ToString("F3")+".\n");
                Debug.Log("DEAD AIR MOONLIGHT GPU PASSED: floor ROI OFF="+dark+", PRIOR="+previous+", OPEN="+exposed+", ROOF="+covered+", net NEW/PRIOR="+(gain/priorGain));
            }
            finally
            {
                RenderTexture.active=previousTarget;
                if(frame!=null){frame.Release();UnityEngine.Object.DestroyImmediate(frame);}
                UnityEngine.Object.DestroyImmediate(stage);if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(skyMaterial!=null)UnityEngine.Object.DestroyImmediate(skyMaterial);
                RenderSettings.ambientMode=previousAmbientMode;RenderSettings.ambientLight=previousAmbient;RenderSettings.ambientProbe=previousProbe;RenderSettings.fog=previousFog;
                QualitySettings.shadows=previousShadows;QualitySettings.shadowDistance=previousDistance;QualitySettings.shadowCascades=previousCascades;QualitySettings.shadowResolution=previousResolution;
                for(int i=0;i<lights.Length;i++)if(lights[i]!=null)lights[i].enabled=enabled[i];
            }
        }

        static float Capture(Camera camera,RenderTexture frame,string name)
        {
            camera.Render();camera.Render();RenderTexture.active=frame;
            var pixels=new Texture2D(frame.width,frame.height,TextureFormat.RGB24,false);
            try
            {
                pixels.ReadPixels(new Rect(0,0,frame.width,frame.height),0,0);pixels.Apply();float mean=0;
                // Compare the same central floor patch in every frame. Averaging
                // the full image mixes black sky with the newly visible roof.
                var patch=pixels.GetPixels(frame.width/4,frame.height/8,frame.width/2,frame.height/3);
                foreach(var pixel in patch)mean+=pixel.grayscale;
                Directory.CreateDirectory(Output);File.WriteAllBytes(Path.Combine(Output,name),pixels.EncodeToPNG());return mean/patch.Length;
            }
            finally{UnityEngine.Object.DestroyImmediate(pixels);}
        }

        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Moonlight / rendering validation: "+message);}
    }
}
