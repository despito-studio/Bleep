using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirReferenceCapture
    {
        [MenuItem("Dead Air/Capture Scene References")]
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new DeadAirWorld(WorldData.Load());
            var camera=new GameObject("Reference camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;
            ColorUtility.TryParseHtmlString("#0a100a",out var night);camera.backgroundColor=night;
            camera.fieldOfView=68;camera.nearClipPlane=.08f;camera.farClipPlane=85;
            var light=new GameObject("Reference flashlight").AddComponent<Light>();
            light.type=LightType.Spot;ColorUtility.TryParseHtmlString("#ffe5b2",out var warm);light.color=warm;
            light.intensity=1.17f;light.range=14;light.spotAngle=.84f*Mathf.Rad2Deg;light.innerSpotAngle=light.spotAngle*.3f;
            light.transform.SetParent(camera.transform,false);light.renderMode=LightRenderMode.ForcePixel;
            camera.allowHDR=true;
            var target=new RenderTexture(1366,768,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var display=new RenderTexture(1366,768,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var tape=new Material(Resources.Load<Shader>("Shaders/DeadAirTape"));
            tape.SetFloat("_Exposure",1.3f);tape.SetFloat("_Aspect",1366f/768);
            camera.targetTexture=target;camera.aspect=1366f/768;
            Directory.CreateDirectory("../output/unity-fidelity");
            string[] names={"courtyard","corridor","archive"};
            Vector3[] positions={new Vector3(5,1.65f,19),new Vector3(0,1.65f,1),new Vector3(5,1.65f,-1)};
            Vector3[] targets={new Vector3(0,1.7f,6),new Vector3(0,1.65f,-18),new Vector3(9,1,-4)};
            for(int i=0;i<names.Length;i++)
            {
                camera.transform.position=positions[i];camera.transform.LookAt(targets[i]);camera.Render();
                Graphics.Blit(target,display,tape);
                RenderTexture.active=display;var image=new Texture2D(1366,768,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1366,768),0,0);image.Apply();
                File.WriteAllBytes("../output/unity-fidelity/unity-post-"+names[i]+".png",image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            RenderTexture.active=null;camera.targetTexture=null;target.Release();display.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(display);UnityEngine.Object.DestroyImmediate(tape);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Debug.Log("Scene reference captures saved to output/unity-fidelity.");
        }
    }
}
