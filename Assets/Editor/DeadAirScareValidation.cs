using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirScareValidation
    {
        [MenuItem("Dead Air/Validate Scare Props and Audio")]
        public static void Validate()
        {
            var cameraObject = new GameObject("Scare verification camera");
            var audioObject = new GameObject("Scare verification audio");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.cullingMask &= ~(1 << 30);
                camera.transform.SetPositionAndRotation(new Vector3(3, 7, -12), Quaternion.Euler(27, 123, 0));
                var prop = new GameObject("Scare verification props").AddComponent<DeadAirScarePresentation>();
                prop.Initialize(camera);
                var apparition = prop.transform.Find("Brief signal apparition").GetComponent<DeadAirMonsterMotion>();
                Require(apparition.HeadPivot != null, "The full-screen apparition needs its articulated head.");
                var spider = prop.transform.Find("Locker spider");
                Require(spider != null && spider.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Leg ")) == 8,
                    "The locker scare needs eight articulated legs.");
                Require(prop.GetComponentsInChildren<Collider>(true).Length == 0, "A scare prop must not block the locker or movement.");
                Require(prop.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).All(m =>
                    m.GetFloat("_ZTest") == (float)UnityEngine.Rendering.CompareFunction.LessEqual && m.GetFloat("_ZWrite") == 1),
                    "Full-screen creatures must preserve their own face/leg depth instead of drawing through themselves.");
                Require(!prop.SpiderActive && !prop.SignalActive, "Scare props must start hidden.");
                prop.Spider();
                Require(prop.SpiderActive && prop.IsFullscreenActive && DeadAirScarePresentation.FullscreenActive &&
                    prop.FullscreenCamera.enabled && prop.FullscreenCamera.cullingMask == 1 << spider.gameObject.layer,
                    "A hiding player needs a dedicated full-screen spider camera without the equipment camera.");
                Require((camera.cullingMask & (1 << 31)) == 0 && prop.FullscreenCamera.clearFlags == CameraClearFlags.SolidColor &&
                    prop.FullscreenCamera.rect == new Rect(0,0,1,1) && prop.FullscreenCamera.depth > camera.depth + 1,
                    "The full-screen scare must clear the entire world/equipment presentation and retain no HUD-sized inset.");
                prop.FullscreenCamera.aspect=1366f/768;
                PoseAt(prop,"spider",.15f);
                var spiderParts=spider.GetComponentsInChildren<Renderer>(true);
                var legs=ProjectedRect(spiderParts.Where(r=>r.name=="Upper leg"||r.name=="Lower leg").ToArray(),prop.FullscreenCamera);
                Require(legs.width>=.65f&&legs.width<=1.15f,"The spider needs a near-full leg span without an oversized cropped body.");
                var face=ProjectedRect(spiderParts.Where(r=>r.name=="Eye"||r.name=="Fang").ToArray(),prop.FullscreenCamera);
                Require(face.xMin>.02f&&face.xMax<.98f&&face.yMin>.02f&&face.yMax<.98f,
                    "The spider's eyes and fangs must remain recognisable and fully inside the screen.");
                Require(prop.GetComponentsInChildren<Light>().All(l => l.enabled && l.cullingMask == 1 << 31),
                    "Scare illumination must stay on the transient props.");
                prop.Clear();
                Require(!prop.SpiderActive && !prop.SignalActive && !DeadAirScarePresentation.FullscreenActive &&
                    !prop.FullscreenCamera.enabled && prop.GetComponentsInChildren<Light>(true).All(l => !l.enabled),
                    "Finishing or leaving a run must hide scare props and their lights.");
                prop.Signal();
                prop.FullscreenCamera.aspect=1366f/768;
                PoseAt(prop,"signal",.15f);
                var headBounds=BoundsOf(apparition.HeadPivot.GetComponentsInChildren<Renderer>(true));
                var headCenter=prop.FullscreenCamera.WorldToViewportPoint(headBounds.center);
                Require(prop.SignalActive && Mathf.Abs(headCenter.x-.5f)<.08f && Mathf.Abs(headCenter.y-.5f)<.12f,
                    "The actual apparition face must be centered independently of the player's pitched/rotated camera.");
                var framedHead=ProjectedRect(apparition.HeadPivot.GetComponentsInChildren<Renderer>(true),prop.FullscreenCamera);
                Require(framedHead.height>=.72f&&framedHead.height<=1&&framedHead.yMin>-.1f&&framedHead.yMax<1.05f,
                    "The entity face must fill a bounded close-up instead of clipping most of its forehead and jaw.");
                var eyeWrap=ProjectedRect(apparition.HeadPivot.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Opaque eye wrap").ToArray(),prop.FullscreenCamera);
                Require(eyeWrap.xMin>.05f&&eyeWrap.xMax<.95f&&eyeWrap.yMin>.08f&&eyeWrap.yMax<.92f,
                    "The entity's defining blindfold must stay completely visible in its close-up.");
                prop.SetPaused(true);
                Require(!prop.IsFullscreenActive&&!prop.FullscreenCamera.enabled,"Pausing must release the full-screen scare and UI suppression.");
                var audio = audioObject.AddComponent<DeadAirAudio>();
                audio.Initialize(); audio.ValidateInstruments();
                Debug.Log("DEAD AIR SCARE PRESENTATION PASSED: full-screen nonblocking spider and centered entity face, isolated camera/light composition, pause/reset and finite nonclipped procedural audio.");
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(audioObject); }
        }
        static void PoseAt(DeadAirScarePresentation prop,string kind,float time)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(DeadAirScarePresentation).GetField(kind+"Time",flags).SetValue(prop,time);
            typeof(DeadAirScarePresentation).GetMethod(kind=="spider"?"PoseSpider":"PoseSignal",flags).Invoke(prop,null);
        }
        static Rect ProjectedRect(Renderer[] renderers,Camera camera)
        {
            Require(renderers.Length>0,"Scare face or limbs are missing.");
            Vector2 min=Vector2.one*100,max=Vector2.one*-100;
            foreach(var renderer in renderers)
            {
                if(!renderer.gameObject.activeInHierarchy)continue;
                var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;
                foreach(var vertex in filter.sharedMesh.vertices)
                {
                    var point=camera.WorldToViewportPoint(filter.transform.TransformPoint(vertex));
                    min=Vector2.Min(min,point);max=Vector2.Max(max,point);
                }
            }
            return new Rect(min,max-min);
        }
        static Bounds BoundsOf(Renderer[] renderers)
        {
            Require(renderers.Length>0,"Scare geometry is missing.");
            var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;
        }
        static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    }
}
