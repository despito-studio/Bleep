using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirOfficeVerification
    {
        [MenuItem("Dead Air/Office/Validate Animated Hinges")]
        public static void ValidateHinges()
        {
            var test = new GameObject("Hinge regression");
            try
            {
                test.transform.localRotation=Quaternion.Euler(0,90,0);
                var hinge=test.AddComponent<DeadAirHingedDoor>();hinge.Initialize(-95);
                hinge.SetOpen(true);hinge.Advance(.24f);
                Require(hinge.Angle < -10 && hinge.Angle > -90,"Door must pass through an intermediate angle.");
                hinge.Advance(.24f);
                Require(Quaternion.Angle(test.transform.localRotation,Quaternion.Euler(0,-5,0))<.01f,"Swing must preserve the authored closed rotation.");
                hinge.SetOpen(false);hinge.Advance(.48f);
                Require(Quaternion.Angle(test.transform.localRotation,Quaternion.Euler(0,90,0))<.01f,"Door must return to its authored orientation.");
                hinge.PlayAccessCycle();hinge.Advance(.2f);
                Require(hinge.IsAjar,"Locker access must open its visible leaf.");
                hinge.Advance(1.1f);Require(Mathf.Abs(hinge.Angle)<.01f&&!hinge.IsAjar,"Locker access must finish shut.");
                Debug.Log("DEAD AIR HINGES PASSED: intermediate swing, rotated door, closing, locker access cycle.");
            }
            finally { UnityEngine.Object.DestroyImmediate(test); }
        }

        [MenuItem("Dead Air/Office/Capture Office References")]
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            WorldData data=WorldData.Load();
            new DeadAirWorld(data);
            var camera=new GameObject("Office reference camera").AddComponent<Camera>();
            camera.tag="MainCamera";camera.allowHDR=true;camera.allowMSAA=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color32(10,16,10,255);
            camera.fieldOfView=68;camera.nearClipPlane=.06f;camera.farClipPlane=180;
            var flashlight=new GameObject("Reference flashlight").AddComponent<Light>();
            flashlight.transform.SetParent(camera.transform,false);flashlight.transform.localPosition=new Vector3(.1f,-.1f,.12f);
            flashlight.type=LightType.Spot;flashlight.range=24;flashlight.spotAngle=65;flashlight.innerSpotAngle=28;
            flashlight.color=new Color32(255,233,183,255);flashlight.intensity=1.5f;flashlight.renderMode=LightRenderMode.ForcePixel;
            var frame=new RenderTexture(640,360,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            frame.filterMode=FilterMode.Point;frame.Create();
            var display=new RenderTexture(1280,720,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);display.Create();
            var post=new Material(Resources.Load<Shader>("Shaders/DeadAirTape"));
            post.SetFloat("_Exposure",1.3f);post.SetFloat("_Aspect",16f/9f);
            camera.targetTexture=frame;camera.aspect=16f/9f;
            string folder=Path.GetFullPath("Art/Office/Previews");Directory.CreateDirectory(folder);
            try
            {
                Shot("01-front-yard",new Vector3(-7,1.65f,33),new Vector3(0,3.5f,6));
                Shot("02-reception",new Vector3(7.1f,1.65f,3.8f),new Vector3(13,1.25f,-1));
                Shot("03-ground-office",new Vector3(-4.8f,1.65f,-12.2f),new Vector3(0,1.4f,-19));
                Shot("04-staircase",new Vector3(0,1.65f,-36),new Vector3(0,4.5f,-48));
                Shot("05-upper-corridor",new Vector3(0,5.85f,-26),new Vector3(14,5.75f,-26));
                Shot("06-upper-office",new Vector3(0,5.85f,-12.1f),new Vector3(0,5.6f,-22));
                var generator=data.Objective("power");
                var engine=Array.Find(data.props,p=>p.type=="dieselGenerator");
                float engineX=engine!=null?-engine.x:-generator.x;
                Shot("07-rear-generator",new Vector3(engineX-1.5f,1.65f,generator.z+5.8f),new Vector3(engineX,1,generator.z));
                if(data.lockers.Length>0)
                {
                    var locker=data.lockers[0];
                    var inside=new Vector3(-locker.x,locker.y+1.65f,locker.z);
                    var outward=new Vector3(locker.x-locker.exitX,0,locker.exitZ-locker.z).normalized;
                    Shot("08-locker-grille",inside,inside+outward*4);
                }
                foreach(var leaf in UnityEngine.Object.FindObjectsByType<DeadAirHingedDoor>(FindObjectsSortMode.None))
                    if(leaf.name.StartsWith("door:")) { leaf.SetOpen(true);leaf.Advance(.24f); }
                Shot("09-half-open-doors",new Vector3(0,1.65f,-.5f),new Vector3(-8.6f,1.65f,-.5f));
                foreach(var leaf in UnityEngine.Object.FindObjectsByType<DeadAirHingedDoor>(FindObjectsSortMode.None))
                    if(leaf.name.StartsWith("door:"))leaf.Advance(.24f);
                Shot("10-open-doors",new Vector3(0,1.65f,-.5f),new Vector3(-8.6f,1.65f,-.5f));
                Shot("11-west-gallery",new Vector3(8,1.65f,-11.8f),new Vector3(8,1.65f,-23));
                Shot("12-meeting-room",new Vector3(11.3f,5.85f,-16.1f),new Vector3(15,5.3f,-13));
                Shot("13-staff-lounge",new Vector3(-9.3f,1.65f,-29.5f),new Vector3(-14.2f,1.1f,-36));
                Shot("14-kitchen",new Vector3(-11.4f,1.65f,-15.8f),new Vector3(-15,1.25f,-11.8f));
                File.WriteAllText(Path.Combine(folder,"capture-report.txt"),"14 real GPU captures at 640x360, point-upscaled to 1280x720. Branched circulation, furnished reception, archive, editorial, meeting, kitchen and staff rooms; both floor elevations, stairs, yards, physical locker grille and door animation.\n");
                Debug.Log("DEAD AIR OFFICE CAPTURE COMPLETE: "+folder);
            }
            finally
            {
                camera.targetTexture=null;RenderTexture.active=null;frame.Release();display.Release();
                UnityEngine.Object.DestroyImmediate(frame);UnityEngine.Object.DestroyImmediate(display);UnityEngine.Object.DestroyImmediate(post);
                EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            }

            void Shot(string name,Vector3 position,Vector3 target)
            {
                camera.transform.position=position;camera.transform.LookAt(target);Physics.SyncTransforms();camera.Render();
                Graphics.Blit(frame,display,post);RenderTexture.active=display;
                var pixels=new Texture2D(display.width,display.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,display.width,display.height),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);RenderTexture.active=null;
            }
        }

        static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    }
}
