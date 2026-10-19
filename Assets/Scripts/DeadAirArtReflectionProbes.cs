using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DeadAir
{
    public sealed class DeadAirArtReflectionProbes : MonoBehaviour
    {
        sealed class Slot
        {
            public ReflectionProbe probe;
            public string key;
            public int lighting = int.MinValue, renderId = -1;
            public bool completedRealtime;
            public double used;
            public RenderTexture reference;
        }
        struct CulledSurface { public Renderer renderer; public bool enabled; public ShadowCastingMode shadows; }

        sealed class FaceVisibility : IDisposable
        {
            readonly List<CulledSurface> changed = new List<CulledSurface>();
            readonly float previousPlanar;
            readonly bool previousCapturing;
            public int RestoredSurfaces => changed.Count;
            public FaceVisibility(Renderer[] surfaces, Bounds volume, int mask)
            {
                previousPlanar = Shader.GetGlobalFloat("_DeadAirReflectionEnabled");
                previousCapturing = capturing; capturing = true;
                Shader.SetGlobalFloat("_DeadAirReflectionEnabled", 0);
                foreach (var renderer in surfaces)
                {
                    if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.bounds.Intersects(volume) ||
                        (mask & (1 << renderer.gameObject.layer)) == 0) continue;
                    bool mirror = renderer.name.StartsWith("mirror:", StringComparison.Ordinal);
                    if (!mirror && renderer.enabled && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
                    changed.Add(new CulledSurface { renderer = renderer, enabled = renderer.enabled, shadows = renderer.shadowCastingMode });
                    renderer.enabled = !mirror;
                    if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) renderer.shadowCastingMode = ShadowCastingMode.On;
                }
            }
            public void Dispose()
            {
                foreach (var item in changed) if (item.renderer != null)
                { item.renderer.enabled = item.enabled; item.renderer.shadowCastingMode = item.shadows; }
                changed.Clear(); Shader.SetGlobalFloat("_DeadAirReflectionEnabled", previousPlanar); capturing = previousCapturing;
            }
        }

        [Serializable] public struct RuntimeDiagnostic
        {
            public int allocatedProbes, activeRealtimeProbes, completedRealtimeProbes, maximumResolution, renderedFaceCallbacks, restoredSectorSurfaces;
            public bool faceScopeOpen;
        }

        const int SlotCount = 2;
        const float RefreshInterval = 3f;
        static DeadAirArtReflectionProbes inFlight;
        static double nextCapture;
        static bool capturing;
        readonly Slot[] slots = new Slot[SlotCount];
        WorldData world;
        GameObject rig;
        Renderer[] environmentSurfaces = Array.Empty<Renderer>();
        Slot activeCapture;
        Bounds captureVolume;
        FaceVisibility faceVisibility;
        Camera faceCamera;
        int renderedFaces, restoredSurfaces;
        public static bool IsCapturing => capturing;
        public int AllocatedProbeCount => rig != null ? rig.GetComponentsInChildren<ReflectionProbe>(true).Length : 0;
        public static int ResolutionForQuality(int quality) => quality >= 2 ? 128 : 64;

        public static DeadAirArtReflectionProbes Attach(Transform owner, WorldData office = null)
        {
            var component = owner.GetComponent<DeadAirArtReflectionProbes>();
            if (component == null) component = owner.gameObject.AddComponent<DeadAirArtReflectionProbes>();
            component.world = office;
            component.environmentSurfaces = CollectProbeSurfaces(owner);
            return component;
        }

        public void Present(RoomState state, Camera viewer, bool active = true)
        {
            EndProbeFace();
            bool enabledForView = active && viewer != null && DeadAirSettings.Reflections && isActiveAndEnabled &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!enabledForView)
            {
                if (rig != null) ReleaseOwnedResources();
                return;
            }
            EnsureRig(); rig.SetActive(true);
            RenderSettings.reflectionIntensity = 1;
            if (!Application.isPlaying) return;
            QualitySettings.realtimeReflectionProbes = true;
            FinishScheduledCapture();
            var bounds = VolumeForEye(viewer.transform.position, out var key);
            int lighting = LightingRevision(state);
            var slot = Select(key);
            slot.used = Time.realtimeSinceStartupAsDouble;
            if (slot.key == key && slot.lighting == lighting && slot.probe.resolution == ResolutionForQuality(DeadAirSettings.GraphicsQuality)) return;
            if (inFlight != null || Time.realtimeSinceStartupAsDouble < nextCapture) return;
            Configure(slot, viewer.transform.position, bounds, viewer.cullingMask);
            slot.probe.mode = ReflectionProbeMode.Realtime;
            slot.probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            slot.key = key; slot.lighting = lighting;
            slot.completedRealtime = false; activeCapture = slot; captureVolume = bounds;
            inFlight = this;
            try { slot.renderId = slot.probe.RenderProbe(); }
            catch { inFlight = null; activeCapture = null; throw; }
            nextCapture = Time.realtimeSinceStartupAsDouble + RefreshInterval;
        }

        void FinishScheduledCapture()
        {
            if (inFlight == null) return;
            foreach (var slot in inFlight.slots)
                if (slot != null && slot.renderId >= 0 && !slot.probe.IsFinishedRendering(slot.renderId)) return;
            foreach (var slot in inFlight.slots) if (slot != null && slot.renderId >= 0)
            { slot.completedRealtime = true; slot.renderId = -1; }
            inFlight.activeCapture = null;
            inFlight = null;
        }

        public RuntimeDiagnostic GetRuntimeDiagnostic()
        {
            var result = new RuntimeDiagnostic { allocatedProbes = AllocatedProbeCount, renderedFaceCallbacks = renderedFaces,
                restoredSectorSurfaces = restoredSurfaces, faceScopeOpen = faceVisibility != null };
            foreach (var slot in slots)
            {
                if (slot == null || slot.probe == null || !slot.probe.enabled || slot.probe.mode != ReflectionProbeMode.Realtime) continue;
                result.activeRealtimeProbes++;
                result.maximumResolution = Mathf.Max(result.maximumResolution, slot.probe.resolution);
                if (slot.completedRealtime || slot.renderId >= 0 && slot.probe.IsFinishedRendering(slot.renderId)) result.completedRealtimeProbes++;
            }
            return result;
        }

        void BeginProbeFace(Camera camera)
        {
            EndProbeFace();
            if (inFlight != this || activeCapture == null || camera == null || camera.cameraType != CameraType.Reflection ||
                (camera.transform.position - activeCapture.probe.transform.position).sqrMagnitude > .01f) return;
            faceCamera = camera;
            faceVisibility = new FaceVisibility(environmentSurfaces, captureVolume, activeCapture.probe.cullingMask);
            restoredSurfaces += faceVisibility.RestoredSurfaces; renderedFaces++;
        }
        void FinishProbeFace(Camera camera) { if (camera == faceCamera) EndProbeFace(); }
        void EndProbeFace()
        {
            faceVisibility?.Dispose(); faceVisibility = null; faceCamera = null;
        }
        static Renderer[] CollectProbeSurfaces(Transform owner)
        {
            var surfaces = new List<Renderer>();
            foreach (var renderer in owner.GetComponentsInChildren<Renderer>(true))
            {
                bool relevant = renderer.name.StartsWith("mirror:", StringComparison.Ordinal);
                for (var ancestor = renderer.transform.parent; !relevant && ancestor != null; ancestor = ancestor.parent)
                    relevant = ancestor.name.StartsWith("sector:", StringComparison.Ordinal);
                if (relevant) surfaces.Add(renderer);
            }
            return surfaces.ToArray();
        }

        // ExecuteMethod has no player loop to advance Unity's time-sliced probe scheduler.
        public bool CaptureReference(Camera viewer)
        {
            if (viewer == null || !isActiveAndEnabled || !DeadAirSettings.Reflections) return false;
            EnsureRig(); rig.SetActive(true);
            var bounds = VolumeForEye(viewer.transform.position, out var key);
            var slot = Select(key);
            Configure(slot, viewer.transform.position, bounds, viewer.cullingMask);
            slot.probe.mode = ReflectionProbeMode.Custom;
            if (slot.reference != null) { slot.probe.customBakedTexture = null; Release(slot.reference); }
            slot.reference = CaptureCube(viewer.transform.position, slot.probe.resolution, slot.probe.cullingMask,
                .06f, 200, gameObject.scene, bounds);
            slot.probe.customBakedTexture = slot.reference;
            slot.key = key; slot.used = Time.realtimeSinceStartupAsDouble;
            RenderSettings.reflectionIntensity = 1;
            return slot.reference != null;
        }

        public Bounds VolumeForEye(Vector3 eye, out string key)
        {
            if (world != null && world.rooms != null)
                for (int i = 0; i < world.rooms.Length; i++)
                {
                    var room = world.rooms[i];
                    if (-eye.x < room.minX || -eye.x > room.maxX || eye.z < room.minZ || eye.z > room.maxZ ||
                        eye.y < room.floorY + .05f || eye.y > room.floorY + 3.9f) continue;
                    key = "room:" + i;
                    return new Bounds(new Vector3(-(room.minX + room.maxX) * .5f, room.floorY + 1.9f, (room.minZ + room.maxZ) * .5f),
                        new Vector3(Mathf.Max(1, room.maxX - room.minX), 3.8f, Mathf.Max(1, room.maxZ - room.minZ)));
                }
            var cell = new Vector3Int(Mathf.FloorToInt(eye.x / 12), Mathf.FloorToInt((eye.y - .5f) / 3.6f), Mathf.FloorToInt(eye.z / 12));
            key = "cell:" + cell;
            // The key is quantized, but the optical origin remains the unobstructed viewer eye.
            return new Bounds(eye, new Vector3(24, 3.6f, 24));
        }

        static int LightingRevision(RoomState state)
        {
            if (state == null) return 0;
            return (state.power ? 1 : 0) | (state.breakerOn ? 2 : 0) | (state.broadcast ? 4 : 0) |
                (state.blackpine != null && state.blackpine.intercepted ? 8 : 0);
        }

        Slot Select(string key)
        {
            foreach (var slot in slots) if (slot.key == key) return slot;
            foreach (var slot in slots) if (slot.key == null) return slot;
            return slots[0].used <= slots[1].used ? slots[0] : slots[1];
        }

        void EnsureRig()
        {
            if (rig != null) return;
            rig = new GameObject("Local real-scene specular probes") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(rig, gameObject.scene);
            for (int i = 0; i < SlotCount; i++)
            {
                var node = new GameObject("Local environment probe " + i) { hideFlags = HideFlags.HideAndDontSave };
                node.transform.SetParent(rig.transform, false);
                var probe = node.AddComponent<ReflectionProbe>();
                probe.enabled = false;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.boxProjection = true; probe.hdr = true; probe.intensity = 1;
                probe.blendDistance = 1.25f; probe.importance = 2;
                probe.clearFlags = ReflectionProbeClearFlags.SolidColor; probe.backgroundColor = Color.black;
                slots[i] = new Slot { probe = probe };
            }
        }

        void Configure(Slot slot, Vector3 eye, Bounds volume, int mask)
        {
            var probe = slot.probe;
            probe.enabled = true;
            probe.resolution = ResolutionForQuality(DeadAirSettings.GraphicsQuality);
            probe.transform.SetPositionAndRotation(eye, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            probe.center = volume.center - eye; probe.size = volume.size;
            probe.nearClipPlane = .06f; probe.farClipPlane = 200;
            probe.cullingMask = mask & ~((1 << 28) | (1 << 29) | (1 << 30));
            probe.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, 35);
        }

        public static RenderTexture CaptureCube(Vector3 eye, int resolution, int mask, float near, float far, Scene scene, Bounds volume)
        {
            if (capturing) throw new InvalidOperationException("Recursive environment capture is not supported.");
            var cube = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "Actual local environment / HDR cube", dimension = TextureDimension.Cube,
                useMipMap = true, autoGenerateMips = false, filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            var cameraObject = new GameObject("Synchronous environment probe camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.allowHDR = true; camera.allowMSAA = false;
            camera.useOcclusionCulling = false; camera.cullingMask = mask;
            camera.nearClipPlane = near; camera.farClipPlane = far;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.renderingPath = RenderingPath.Forward;
            camera.transform.SetPositionAndRotation(eye, Quaternion.identity);
            FaceVisibility visibility = null;
            var priorTarget = RenderTexture.active;
            bool priorCulling = GL.invertCulling;
            float priorPlanar = Shader.GetGlobalFloat("_DeadAirReflectionEnabled");
            try
            {
                var surfaces = new List<Renderer>();
                foreach (var root in scene.GetRootGameObjects()) surfaces.AddRange(CollectProbeSurfaces(root.transform));
                visibility = new FaceVisibility(surfaces.ToArray(), volume, mask);
                if (!cube.Create() || !camera.RenderToCubemap(cube, 63)) throw new InvalidOperationException("Real-scene environment cubemap capture failed.");
                cube.GenerateMips();
                return cube;
            }
            catch { Release(cube); throw; }
            finally
            {
                visibility?.Dispose();
                Shader.SetGlobalFloat("_DeadAirReflectionEnabled", priorPlanar);
                RenderTexture.active = priorTarget; GL.invertCulling = priorCulling; capturing = false;
                DisposeObject(cameraObject);
            }
        }

        public void ReleaseOwnedResources()
        {
            EndProbeFace(); activeCapture = null;
            if (inFlight == this) inFlight = null;
            foreach (var slot in slots) if (slot != null && slot.reference != null) Release(slot.reference);
            if (rig != null) DisposeObject(rig);
            rig = null;
        }
        static void Release(RenderTexture texture) { texture.Release(); DisposeObject(texture); }
        static void DisposeObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void OnEnable() { Camera.onPreCull += BeginProbeFace; Camera.onPostRender += FinishProbeFace; }
        void LateUpdate() => EndProbeFace();
        void OnDisable()
        {
            Camera.onPreCull -= BeginProbeFace; Camera.onPostRender -= FinishProbeFace;
            ReleaseOwnedResources();
        }
        void OnDestroy() => ReleaseOwnedResources();
    }
}
