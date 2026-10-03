using System;
using System.Linq;
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
                Require(apparition.HeadPivot != null && Vector3.Distance(prop.transform.InverseTransformPoint(apparition.HeadPivot.position), new Vector3(.1f, -.03f, .55f)) < .002f,
                    "The apparition head must fit a pitched/rotated camera in local coordinates.");
                var spider = prop.transform.Find("Locker spider");
                Require(spider != null && spider.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Leg ")) == 8,
                    "The locker scare needs eight articulated legs.");
                Require(prop.GetComponentsInChildren<Collider>(true).Length == 0, "A scare prop must not block the locker or movement.");
                Require(prop.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).All(m =>
                    m.GetFloat("_ZTest") == (float)UnityEngine.Rendering.CompareFunction.Always && m.GetFloat("_ZWrite") == 0),
                    "A nearby wall or grille must not hide the transient camera scare.");
                Require(!prop.SpiderActive && !prop.SignalActive, "Scare props must start hidden.");
                prop.Spider();
                Require(prop.SpiderActive && (camera.cullingMask & (1 << spider.gameObject.layer)) != 0,
                    "A hiding player must see the spider without the disabled equipment camera.");
                Require(prop.GetComponentsInChildren<Light>().All(l => l.enabled && l.cullingMask == 1 << 31),
                    "Scare illumination must stay on the transient props.");
                prop.Clear();
                Require(!prop.SpiderActive && !prop.SignalActive && prop.GetComponentsInChildren<Light>(true).All(l => !l.enabled),
                    "Finishing or leaving a run must hide scare props and their lights.");
                var audio = audioObject.AddComponent<DeadAirAudio>();
                audio.Initialize(); audio.ValidateInstruments();
                Debug.Log("DEAD AIR SCARE PRESENTATION PASSED: eight-leg nonblocking spider visible in hiding, transient light isolation/reset and finite nonclipped procedural audio.");
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(audioObject); }
        }
        static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    }
}
