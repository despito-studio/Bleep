using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirArtMirrorValidation
    {
        [MenuItem("Dead Air/Production Art/Validate Mirror Projection Math")]
        public static void ValidateMath()
        {
            var point = new Vector3(3.4f, 1.9f, -6.2f);
            var normal = new Vector3(.4f, .1f, .8f).normalized;
            var matrix = DeadAirArtMirror.ReflectionMatrix(normal, point);
            Require(Vector3.Distance(matrix.MultiplyPoint(point), point) < .0001f, "A point on the silvering plane must remain fixed.");
            var sample = point + normal * 2.7f + Vector3.Cross(normal, Vector3.up) * .8f;
            var reflected = matrix.MultiplyPoint(sample);
            Require(Mathf.Abs(Vector3.Dot(sample - point, normal) + Vector3.Dot(reflected - point, normal)) < .0001f,
                "Reflection must invert signed distance from an arbitrarily oriented plane.");
            Require(Vector3.Distance(matrix.MultiplyPoint(reflected), sample) < .0001f, "Reflecting twice must restore the source position.");
            Require(Mathf.Abs(matrix.determinant + 1) < .0001f, "Reflection must reverse handedness exactly once.");
            var fixture = new GameObject("Mirror normal and camera-space plane fixture");
            try
            {
                fixture.transform.SetPositionAndRotation(point, Quaternion.Euler(8, 37, 0));
                fixture.transform.localScale = new Vector3(1.8f, .7f, 1.2f);
                fixture.AddComponent<MeshRenderer>();
                var mirror = DeadAirArtMirror.Attach(fixture.GetComponent<Renderer>(), Vector3.forward);
                Require(Vector3.Dot(mirror.PlaneNormal, fixture.transform.forward) > .9999f,
                    "A nonuniformly scaled mirror must preserve its physical plane normal.");
                fixture.name = "mirror:explicit-local-axis-fixture";
                DeadAirArtMirror.Attach(fixture.GetComponent<Renderer>(), Vector3.up);
                DeadAirArtMotion.Attach(fixture.transform);
                Require(Vector3.Dot(mirror.PlaneNormal, fixture.transform.up) > .9999f,
                    "Automatic mirror discovery must preserve an explicitly authored local-Y silvering normal.");
                var cameraToWorld = Matrix4x4.TRS(new Vector3(0, 2, -3), Quaternion.Euler(5, 21, 0), Vector3.one);
                var view = cameraToWorld.inverse;
                var clip = DeadAirArtMirror.CameraSpacePlane(view, point, normal, .012f);
                var onPlane = view.MultiplyPoint(point + normal * .012f);
                Require(Mathf.Abs(Vector4.Dot(clip, new Vector4(onPlane.x, onPlane.y, onPlane.z, 1))) < .0001f,
                    "The oblique clipping plane is not expressed in reflection-camera space.");
                mirror.ReleaseOwnedResources();
                Require(!mirror.HasOwnedResources, "An unused mirror must release idempotently.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
            Debug.Log("DEAD AIR MIRROR MATH PASSED: arbitrary-plane reflection, inverse, handedness, scaled normal and camera-space oblique clipping.");
        }

        [MenuItem("Dead Air/Production Art/Validate Mirror GPU and Cleanup")]
        public static void ValidateGpu()
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Mirror GPU validation requires a graphics device.");
            var fixture = new GameObject("Vertical mirror GPU fixture"); fixture.transform.position = new Vector3(1000, 1000, 1000);
            var material = new Material(Shader.Find("Unlit/Color")) { color = Color.gray };
            var red = new Material(Shader.Find("Unlit/Color")) { color = new Color(1, .01f, .01f) };
            var cyan = new Material(Shader.Find("Unlit/Color")) { color = new Color(.01f, 1, 1) };
            bool hadSetting = PlayerPrefs.HasKey("DeadAir.Reflections"); bool oldSetting = DeadAirSettings.Reflections;
            var previousTarget = RenderTexture.active; bool previousCulling = GL.invertCulling;
            try
            {
                DeadAirSettings.Reflections = true;
                var pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pane.name = "mirror:gpu-fixture"; pane.transform.SetParent(fixture.transform, false);
                pane.transform.localScale = new Vector3(2.4f, 1.4f, .018f);
                var renderer = pane.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock(); block.SetFloat("_ReviewMarker", 17); renderer.SetPropertyBlock(block);
                var mirror = DeadAirArtMirror.Attach(renderer, Vector3.forward);
                var cameraObject = new GameObject("Mirror validation source"); cameraObject.transform.SetParent(fixture.transform, false);
                var source = cameraObject.AddComponent<Camera>(); source.enabled = false;
                source.transform.localPosition = new Vector3(0, 0, 3); source.transform.LookAt(fixture.transform.position);
                source.nearClipPlane = .05f; source.farClipPlane = 8; source.fieldOfView = 60; source.aspect = 16 / 9f;
                source.clearFlags = CameraClearFlags.SolidColor; source.backgroundColor = Color.black;
                Marker("Front-side red marker", new Vector3(.55f, .1f, 1.2f), red);
                Marker("Clipped rear-side cyan marker", new Vector3(-.55f, .1f, -.4f), cyan);
                var position = source.transform.position; var rotation = source.transform.rotation;
                var projection = source.projectionMatrix; var view = source.worldToCameraMatrix;
                int mask = source.cullingMask; var target = source.targetTexture;
                Physics.SyncTransforms();
                Require(mirror.RenderReflection(source), "The real mirror did not render its front-side reflected camera.");
                Require(mirror.ReflectionFrame != null && mirror.ReflectionFrame.IsCreated() &&
                    (mirror.ReflectionFrame.width == 512 || mirror.ReflectionFrame.width == 768), "Mirror target allocation is outside its graphics-quality budget.");
                Require(source.transform.position == position && source.transform.rotation == rotation && source.projectionMatrix == projection &&
                    source.worldToCameraMatrix == view && source.cullingMask == mask && source.targetTexture == target,
                    "Mirror rendering mutated the primary camera.");
                Require(GL.invertCulling == previousCulling && RenderTexture.active == previousTarget && renderer.enabled,
                    "Mirror rendering did not restore culling, active target or silvering visibility.");
                renderer.GetPropertyBlock(block);
                Require(block.GetTexture("_MirrorTex") == mirror.ReflectionFrame && block.GetFloat("_ReflectionReady") == 1,
                    "The reflected scene must belong to the silvering renderer's property block.");
                var pixels = new Texture2D(mirror.ReflectionFrame.width, mirror.ReflectionFrame.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = mirror.ReflectionFrame;
                    pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
                    int redPixels = 0, cyanPixels = 0;
                    foreach (var pixel in pixels.GetPixels32())
                    {
                        if (pixel.r > 150 && pixel.g < 80 && pixel.b < 80) redPixels++;
                        if (pixel.r < 80 && pixel.g > 150 && pixel.b > 150) cyanPixels++;
                    }
                    Require(redPixels > 20, "Reflected geometry is absent from the mirror texture.");
                    Require(cyanPixels == 0, "Geometry behind the silvering plane leaked through its oblique clip.");
                }
                finally { UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.active = previousTarget; }
                source.transform.localPosition += Vector3.right * .2f;
                Require(mirror.RenderReflection(source) &&
                    Vector3.Distance(mirror.ReflectionCamera.transform.position,
                        DeadAirArtMirror.ReflectionMatrix(mirror.PlaneNormal, mirror.PlanePoint).MultiplyPoint(source.transform.position)) < .001f,
                    "An edit-mode camera move in the same frame retained a stale reflected view.");
                mirror.ReleaseOwnedResources(); mirror.ReleaseOwnedResources();
                renderer.GetPropertyBlock(block);
                Require(!mirror.HasOwnedResources && renderer.sharedMaterial == material && block.GetFloat("_ReviewMarker") == 17,
                    "Mirror cleanup did not release its texture/camera/material and restore the original renderer.");

                void Marker(string name, Vector3 localPosition, Material surface)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.name = name;
                    marker.transform.SetParent(fixture.transform, false); marker.transform.localPosition = localPosition;
                    marker.transform.localScale = Vector3.one * .35f; marker.GetComponent<Renderer>().sharedMaterial = surface;
                }
            }
            finally
            {
                RenderTexture.active = previousTarget; GL.invertCulling = previousCulling;
                DeadAirSettings.Reflections = oldSetting; if (!hadSetting) PlayerPrefs.DeleteKey("DeadAir.Reflections");
                UnityEngine.Object.DestroyImmediate(fixture);
                UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(red); UnityEngine.Object.DestroyImmediate(cyan);
            }
            Debug.Log("DEAD AIR MIRROR GPU PASSED: front-side scene pixels, oblique rear clipping, source-camera preservation, per-renderer binding, same-frame camera moves and owned-resource cleanup.");
        }

        static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Mirror validation: " + message);
        }
    }
}
