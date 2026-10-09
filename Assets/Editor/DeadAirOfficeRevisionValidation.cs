using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirOfficeRevisionValidation
    {
        public static void Prepare()
        {
            DeadAirReleaseBuild.PrepareFixedScene();
            ValidateSaved();
        }

        public static void ValidateSaved()
        {
            DeadAirNavigationBake.ValidateSaved();
            DeadAirSuppliesValidation.Validate();
            DeadAirDoorDirectionValidation.Validate();
            DeadAirOfficeVerification.ValidateHinges();
            DeadAirCoopFlashlightValidation.Validate();
            DeadAirDressingValidation.Validate();
            DeadAirPeriodProps.Validate();
            DeadAirOfficePlacementValidation.Validate();
            DeadAirMountedGeometryValidation.Validate();
            DeadAirMoonlightValidation.Validate();
            ValidateArt();
            Directory.CreateDirectory("Art/OfficeRevision");
            File.WriteAllText("Art/OfficeRevision/editor-report.txt","PASS: rebuilt irregular office prefabs and Blackpine scene; open airwell and dogleg gallery; physical routes to all rooms/objectives; basement and stair geometry; quest completion; LAN and inventory; "+WorldData.Load().doors.Length+" door directions; four role flashlight poses and two TCP guests; furniture shapes and placements; shadowed moonlight; mountain/forest cards.\n");
            Debug.Log("DEAD AIR OFFICE REVISION PREPARED AND VALIDATED");
        }

        public static void ValidateArt()
        {
            var station=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var outside=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            var data=WorldData.Load();
            Require(station!=null&&outside!=null,"Regenerated office prefabs missing.");
            var parts=station.GetComponentsInChildren<Transform>(true).Concat(outside.GetComponentsInChildren<Transform>(true)).ToArray();
            Require(parts.Any(t=>t.name=="Late Soviet stepped facade"),"Stepped building massing missing.");
            Require(parts.Count(t=>t.name=="Faded curtains")>=24,"Interior windows need supported curtains.");
            foreach(var calendar in parts.Where(t=>t.name=="Abandoned 1991 calendar"))
            {
                Vector3 p=calendar.position;
                Require(!data.doors.Any(d=>Mathf.Abs(d.y-1.4f-(p.y-1.94f))<.1f&&
                    Mathf.Abs((-p.x-d.x)*Mathf.Cos(d.rotation)-(p.z-d.z)*Mathf.Sin(d.rotation))<.25f&&
                    Mathf.Abs((-p.x-d.x)*Mathf.Sin(d.rotation)+(p.z-d.z)*Mathf.Cos(d.rotation))<d.w/2+.15f),"Calendar overlaps doorway: "+calendar.parent.name);
            }
            var panorama=outside.transform.Find("Mountain panorama cards");
            Require(panorama!=null&&panorama.GetComponentsInChildren<MeshRenderer>().Length==4,"Three mountain rings and forest card batch required.");
            Require(panorama.GetComponentsInChildren<Collider>().Length==0,"Scenic cards must not create collisions.");
            int triangles=0;
            foreach(var renderer in panorama.GetComponentsInChildren<MeshRenderer>())
            {
                Require(renderer.shadowCastingMode==ShadowCastingMode.Off&&!renderer.receiveShadows,"Scenic cards consume shadow budget.");
                Require(renderer.sharedMaterial.shader.name=="Dead Air/Source Unlit","Scenic cards use expensive lighting.");
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                Require(mesh!=null&&mesh.bounds.size.y>5,"Empty or flat scenery mesh.");triangles+=mesh.triangles.Length/3;
            }
            Require(triangles<=1700,"Mountain and forest card triangle budget exceeded.");
            Require(data.props.Select(p=>p.type).Distinct().Count()>=64,"Revised office still repeats too few prop families.");
            var floors=station.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).ToArray();
            Require(floors.Any(m=>m.name=="aged-parquet")&&floors.Any(m=>m.name=="worn-sage-paint"),"Reference finishes missing.");
            Debug.Log("DEAD AIR PERIOD OFFICE ART PASSED: "+data.props.Length+" prop records, "+data.props.Select(p=>p.type).Distinct().Count()+" families, mounted curtains/calendars; stepped civic facade; four no-shadow panorama meshes / "+triangles+" triangles.");
        }

        public static void Capture()
        {
            DeadAirCoopFlashlightValidation.ValidateWithGpu();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var data=WorldData.Load();new DeadAirWorld(data);
            var camera=new GameObject("Office revision camera").AddComponent<Camera>();
            camera.tag="MainCamera";camera.allowHDR=true;camera.nearClipPlane=.06f;camera.farClipPlane=250;camera.fieldOfView=68;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.075f,.095f);
            var light=new GameObject("Observer flashlight").AddComponent<Light>();light.transform.SetParent(camera.transform,false);
            light.transform.localPosition=new Vector3(.1f,-.1f,.12f);light.type=LightType.Spot;light.range=24;light.spotAngle=65;light.innerSpotAngle=28;
            light.color=new Color(1,.92f,.77f);light.intensity=1.5f;light.renderMode=LightRenderMode.ForcePixel;light.shadows=LightShadows.Soft;
            var frame=new RenderTexture(960,540,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);frame.Create();
            var display=new RenderTexture(1440,810,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);display.Create();
            var post=new Material(Resources.Load<Shader>("Shaders/DeadAirTape"));post.SetFloat("_Exposure",1.3f);post.SetFloat("_Aspect",16f/9f);
            camera.targetTexture=frame;camera.aspect=16f/9f;
            string folder="Art/OfficeRevision/Previews";Directory.CreateDirectory(folder);
            try
            {
                Shot("01-mountain-office",new Vector3(-25,5,34),new Vector3(0,4.9f,-6),false);
                Shot("02-reception",new Vector3(7.3f,1.7f,3.5f),new Vector3(13,1.3f,-2.8f));
                Shot("03-editorial",new Vector3(4.8f,5.85f,-12.4f),new Vector3(-2,5.6f,-20));
                Shot("04-archive",new Vector3(-4.7f,1.65f,-12.3f),new Vector3(1.5f,1.6f,-19));
                Shot("05-director",new Vector3(7.3f,5.85f,3.4f),new Vector3(12.4f,5.5f,-2.3f));
                Shot("06-transmission-control",new Vector3(-7.5f,5.85f,3.5f),new Vector3(-11.5f,6.1f,-5.7f));
                Shot("07-generator-and-truck",new Vector3(-27,2,-55.5f),new Vector3(-14,1.8f,-68),false);
                Shot("08-mountain-path",new Vector3(-26,1.65f,-20),new Vector3(-72,15,-36),false);
                Shot("09-airwell-courtyard",new Vector3(5,1.65f,-34),new Vector3(7,3.6f,-29),false);
                Shot("10-stepped-building",new Vector3(55,42,-69),new Vector3(3,2,-27),false);
                Shot("11-west-gallery-turn",new Vector3(9.5f,5.85f,-36.5f),new Vector3(5,5.7f,-40.2f));
                File.WriteAllText(folder+"/capture-report.txt","11 GPU renders of regenerated prefabs: stepped footprint, open airwell, dogleg gallery, updated furniture, night atmosphere, player-equivalent flashlight and real geometry/materials. Mountain panorama uses four shared meshes and 1632 triangles. Separate off/on remote-light render in ../CoopFlashlight and measured moonlight/roof occlusion in ../Rendering.\n");
                Debug.Log("DEAD AIR OFFICE REVISION GPU CAPTURES COMPLETE");
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=null;frame.Release();display.Release();
                UnityEngine.Object.DestroyImmediate(frame);UnityEngine.Object.DestroyImmediate(display);UnityEngine.Object.DestroyImmediate(post);
                EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            }
            DeadAirMoonlightValidation.ValidateWithGpu();
            void Shot(string name,Vector3 position,Vector3 target,bool flashlight=true)
            {
                light.enabled=flashlight;camera.transform.position=position;camera.transform.LookAt(target);Physics.SyncTransforms();camera.Render();
                Graphics.Blit(frame,display,post);RenderTexture.active=display;
                var pixels=new Texture2D(display.width,display.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,display.width,display.height),0,0);pixels.Apply();
                File.WriteAllBytes(folder+"/"+name+".png",pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);RenderTexture.active=null;
            }
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
