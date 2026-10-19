using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DeadAir.Editor
{
    public static class DeadAirProductionReview
    {
        const string Output = "outputs/art-overhaul-20261019";
        const int Width = 1920, Height = 1080;

        [Serializable] sealed class ShotRecord
        {
            public string file, chapter;
            public Vector3 eye, target;
            public bool referenceFlashlight;
            public float meanLuminance, luminanceRange;
        }
        [Serializable] sealed class ReviewReport
        {
            public string generatedUtc, graphicsDevice, graphicsApi, scope;
            public int officePickups, schoolPickups;
            public DeadAirArtGeometryValidation.Summary officeGeometry, schoolGeometry;
            public List<ShotRecord> shots = new List<ShotRecord>();
        }

        [MenuItem("Dead Air/Production Art/Validate Motion and Pickup Contracts")]
        public static void Validate()
        {
            DeadAirArtMotionValidation.Validate();
            DeadAirArtReflectionProbeValidation.Validate();
            DeadAirArtGeometryValidation.ValidateGate();
            DeadAirSuppliesValidation.Validate();
            int count = ValidateOfficePickupContracts(WorldData.Load());
            using (var context = new ReviewScene())
            {
                var school = new DeadAirSchoolWorld(context.Root, WorldData.Load());
                Complete(school.InitializeSteps());
                ValidateSchoolPickupContracts(school);
            }
            Directory.CreateDirectory(Path.GetFullPath(Output));
            File.WriteAllText(Path.GetFullPath(Output + "/motion-pickup-validation.txt"),
                "PASSED: mechanism motion, authoritative drawer/pickup lifecycle, " + count +
                " office pickup anchors and 14 school pickups; independent render parts, metric bounds, nonblocking small pickups and collection visibility.\n");
            Debug.Log("DEAD AIR PRODUCTION REVIEW VALIDATION PASSED: motion, collision, office pickups and school pickup contracts.");
        }

        [MenuItem("Dead Air/Production Art/Capture Both Chapters")]
        public static void CaptureBothChapters()
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "GPU review requires a graphics device. Run this entry point without -nographics.");
            DeadAirArtMotionValidation.Validate();
            DeadAirArtMirrorValidation.ValidateGpu();
            DeadAirArtReflectionProbeValidation.ValidateGpu();
            DeadAirArtGeometryValidation.ValidateGate();
            var data = WorldData.Load();
            var report = new ReviewReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), officePickups = ValidateOfficePickupContracts(data),
                scope = "Actual 1920x1080 GPU reference renders of both generated environments, using the game surface/post shaders. No UI or character animation certification. Reference flashlight is recorded per frame."
            };
            string folder = Path.GetFullPath(Output + "/captures"); Directory.CreateDirectory(folder);
            using (var context = new ReviewScene())
            using (var capture = new CaptureCamera(context.Root, folder, report))
            {
                var rules = new DeadAirRules(data, "host", "Production art review", "engineer", true, true);
                Require(rules.Start("host") == null, "The reference host fixture did not start.");
                var state = rules.State; state.elapsed = 30; state.power = state.breakerOn = true;
                var world = new DeadAirWorld(data);
                world.Apply(state);
                var office = GameObject.Find("BLACKPINE / original web scene");
                Require(office != null, "The actual runtime office root is missing.");
                report.officeGeometry = DeadAirArtGeometryValidation.RequireFiniteAssembly(office.transform);

                void Office(string file, Vector3 eye, Vector3 target, bool flashlight = false)
                {
                    SetActor(state, eye); world.Apply(state); world.PrepareRender(capture.Camera);
                    capture.Shot(file, "blackpine", eye, target, flashlight, () => world.PrepareRender(capture.Camera));
                }

                Office("office-01-courtyard", new Vector3(-7, 1.65f, 33), new Vector3(0, 3.5f, 6));
                Office("office-02-reception", new Vector3(7.1f, 1.65f, 3.8f), new Vector3(13, 1.25f, -1));
                Office("office-03-ground-office", new Vector3(-4.8f, 1.65f, -12.2f), new Vector3(0, 1.4f, -19));
                Office("office-04-staircase", new Vector3(0, 1.65f, -36), new Vector3(0, 4.5f, -48));
                Office("office-05-newsroom", new Vector3(0, 5.85f, -12.1f), new Vector3(0, 5.6f, -22));
                Office("office-06-meeting-room", new Vector3(11.3f, 5.85f, -16.1f), new Vector3(15, 5.3f, -13));
                Office("office-07-kitchen", new Vector3(-11.4f, 1.65f, -15.8f), new Vector3(-15, 1.25f, -11.8f));
                int mirrorIndex = 0;
                foreach (var mirror in office.GetComponentsInChildren<DeadAirArtMirror>(true))
                {
                    var target = mirror.PlanePoint;
                    Office("office-mirror-" + (++mirrorIndex), target + mirror.PlaneNormal * 2.7f + mirror.transform.right * .2f,
                        target, true);
                }
                foreach (var hinge in office.GetComponentsInChildren<DeadAirHingedDoor>(true))
                    if (hinge.name.StartsWith("door:", StringComparison.Ordinal)) { hinge.SetOpen(true); hinge.Advance(.24f); }
                // Render this mechanical pose directly; a host snapshot would begin closing it again.
                capture.Shot("office-08-door-articulation", "blackpine", new Vector3(0, 1.65f, -.5f), new Vector3(-8.6f, 1.65f, -.5f), true,
                    () => world.PrepareRender(capture.Camera));
                foreach (var drawer in state.drawers) drawer.open = drawer.searched = true;
                var presentation = new DeadAirDrawerPresentation(office.transform, data);
                presentation.Apply(state, 1);
                var director = data.Drawer("drawer-director") ?? data.Drawers[0];
                var drawerTarget = DeadAirOfficeLayout.Position(director.x, director.y, director.z);
                capture.Shot("office-09-drawer-articulation", "blackpine", drawerTarget + new Vector3(1.1f, .9f, 1.25f), drawerTarget, true,
                    () => world.PrepareRender(capture.Camera));
                state.power = state.breakerOn = false;
                Office("office-10-blackout-materials", new Vector3(7.1f, 1.65f, 3.8f), new Vector3(13, 1.25f, -1), true);
                DestroyReviewAssembly(office);
                DeadAirScannedModels.ClearCaches();
                EditorUtility.UnloadUnusedAssetsImmediate();
                GC.Collect();

                var school = new DeadAirSchoolWorld(context.Root, data);
                Complete(school.InitializeSteps()); school.SetActive(true);
                report.schoolGeometry = DeadAirArtGeometryValidation.RequireFiniteAssembly(school.Root);
                report.schoolPickups = ValidateSchoolPickupContracts(school);
                state.chapterId = "school"; state.power = state.breakerOn = true;
                state.collected.Clear(); state.tapes.Clear(); state.elapsed = 60;

                void School(string file, Vector3 sourceEye, Vector3 sourceTarget, bool flashlight = false)
                {
                    var eye = Source(sourceEye); var target = Source(sourceTarget);
                    SetActor(state, eye); capture.Camera.transform.position = eye;
                    school.Update(state, .1f);
                    // Edit-mode capture has no player loop to refresh the school's distance-culling timer.
                    foreach (var light in school.Root.GetComponentsInChildren<Light>(true))
                        if (light.type != LightType.Directional) light.enabled = Vector3.Distance(light.transform.position, eye) < light.range + 18;
                    capture.Shot(file, "school", eye, target, flashlight);
                }

                School("school-01-facade", new Vector3(0, 1.75f, 25), new Vector3(0, 5.5f, -5));
                School("school-02-shoe-lockers", new Vector3(1.6f, 2.25f, 4.5f), new Vector3(-3.6f, 1.2f, -.5f), true);
                School("school-03-classroom", new Vector3(-23.5f, 2.25f, -6.8f), new Vector3(-29, 1.2f, -3.5f));
                School("school-04-ground-corridor", new Vector3(-5, 2.25f, -11), new Vector3(-28, 1.8f, -11));
                School("school-05-science-lab", new Vector3(-25, 5.4f, -14.8f), new Vector3(-29.3f, 4.8f, -17.75f), true);
                School("school-06-library", new Vector3(10, 5.45f, -15), new Vector3(15.5f, 5.2f, -18.5f));
                School("school-07-music-room", new Vector3(28, 5.45f, -6.6f), new Vector3(31.8f, 4.9f, -4.25f), true);
                School("school-08-upper-wing", new Vector3(-7, 9.05f, -26), new Vector3(-21, 8.5f, -27));
                School("school-09-courtyard-props", new Vector3(-5, 1.8f, 15), new Vector3(-12, 1.1f, 10));
                School("school-10-neighborhood", new Vector3(-40, 1.75f, 36), new Vector3(-48, 4.2f, 31.4f), true);
                var slider = school.Root.GetComponentsInChildren<DeadAirArtMotionSlidingLeaf>(true).FirstOrDefault();
                if (slider != null)
                {
                    var target = BoundsOf(slider.transform).center;
                    var eye = slider.transform.parent.TransformPoint(new Vector3(-.8f, 1.5f, 2.7f));
                    slider.Advance(true, .35f);
                    capture.Shot("school-11-separate-glazing-and-sliding-leaf", "school", eye, target, true);
                }
                var sink = school.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(p => p.name == "Recessed sink bowl");
                if (sink != null)
                {
                    var target = BoundsOf(sink).center;
                    capture.Shot("school-12-open-sink-and-counter", "school", target + new Vector3(1.25f, 1.6f, 1.5f), target, true);
                }
                DestroyReviewAssembly(school.Root.gameObject);
            }
            File.WriteAllText(Path.Combine(folder, "capture-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("DEAD AIR BOTH-CHAPTER GPU REVIEW COMPLETE: " + report.shots.Count + " full-resolution frames in " + folder);
        }

        [MenuItem("Dead Air/Production Art/Capture Both Chapters From Fresh Batch Scene")]
        public static void CaptureFresh()
        {
            DeadAirProductionCatalogCapture.PrepareFreshBatchScene();
            CaptureBothChapters();
        }

        public static void ValidateSchoolAndCaptureFresh()
        {
            DeadAirSchoolArtValidation.Run();
            DeadAirScannedModels.ClearCaches();
            EditorUtility.UnloadUnusedAssetsImmediate();
            CaptureFresh();
        }

        static void DestroyReviewAssembly(GameObject root)
        {
            foreach (var mirror in root.GetComponentsInChildren<DeadAirArtMirror>(true)) mirror.ReleaseOwnedResources();
            var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && !EditorUtility.IsPersistent(m)).Distinct().ToArray();
            var meshes = root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
                .Concat(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh))
                .Where(m => m != null && !EditorUtility.IsPersistent(m)).Distinct().ToArray();
            UnityEngine.Object.DestroyImmediate(root);
            // Batch review owns these transient copies. Interactive review can share cached meshes with an existing school.
            if (!Application.isBatchMode) return;
            foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            foreach (var mesh in meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }

        static int ValidateOfficePickupContracts(WorldData data)
        {
            var prefabs = new[] { Resources.Load<GameObject>("World/station"), Resources.Load<GameObject>("World/exterior") };
            Require(prefabs.All(p => p != null), "Saved office and exterior prefabs are required.");
            var parts = prefabs.SelectMany(p => p.GetComponentsInChildren<Transform>(true)).ToArray();
            int checkedCount = 0;
            foreach (var objective in data.objects)
            {
                if (objective.type != "key" && objective.type != "keycard" && objective.type != "tape" && objective.type != "fuse") continue;
                var matches = parts.Where(p => p.name == "interactive:" + objective.id).ToArray();
                Require(matches.Length == 1, "Pickup ID missing or duplicated in the saved world: " + objective.id);
                var root = matches[0];
                Require((root.localScale - Vector3.one).sqrMagnitude < .000001f, "Pickup scale is inherited from a mesh: " + objective.id);
                var bounds = BoundsOf(root);
                Require(bounds.size.maxComponent() < .65f && bounds.size.maxComponent() > .015f,
                    "Small pickup dimensions are not in metres: " + objective.id + " " + bounds.size);
                Require(root.GetComponentsInChildren<Collider>(true).All(c => !c.enabled || c.isTrigger),
                    "Small pickups must use host interaction range without blocking character movement: " + objective.id);
                checkedCount++;
            }
            Require(checkedCount > 0, "No office small-pickup contracts were inspected.");
            return checkedCount;
        }

        static int ValidateSchoolPickupContracts(DeadAirSchoolWorld school)
        {
            Require(school.Pills.Count == 9 && school.EvidenceTapes.Count == 5, "School pickup IDs/count changed.");
            foreach (var pill in school.Pills) ValidateSchoolPickup(pill.id, pill.go, pill.position);
            foreach (var tape in school.EvidenceTapes) ValidateSchoolPickup(tape.id, tape.go, tape.position);
            var state = new RoomState { chapterId = "school", power = true, breakerOn = true };
            var targetPill = school.Pills[0]; var targetTape = school.EvidenceTapes[0];
            state.collected.Add(targetPill.id); state.tapes.Add(targetTape.id);
            school.Update(state, 0);
            Require(!targetPill.go.activeSelf && !targetTape.go.activeSelf, "Collected school pickups remain visible.");
            state.collected.Clear(); state.tapes.Clear(); school.Update(state, 0);
            Require(targetPill.go.activeSelf && targetTape.go.activeSelf, "School pickup reset lost its render assembly.");
            return school.Pills.Count + school.EvidenceTapes.Count;
        }

        static void ValidateSchoolPickup(string id, GameObject pickup, Vector3 anchor)
        {
            Require(pickup != null && Vector3.Distance(pickup.transform.localPosition, anchor) < .001f, "School pickup anchor moved: " + id);
            Require((pickup.transform.localScale - Vector3.one).sqrMagnitude < .000001f, "School pickup assembly must have metric root scale: " + id);
            Require(pickup.GetComponentsInChildren<Renderer>(true).Length >= 2, "School pickup has lost its separate visible parts: " + id);
            Require(pickup.GetComponentsInChildren<Collider>(true).All(c => !c.enabled || c.isTrigger), "School pickup unexpectedly blocks traversal: " + id);
            var bounds = BoundsOf(pickup.transform);
            Require(bounds.size.maxComponent() > .015f && bounds.size.maxComponent() < .65f, "School pickup size is implausible: " + id);
        }

        static float maxComponent(this Vector3 size) => Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        static Vector3 Source(Vector3 p) => new Vector3(-p.x, p.y, p.z);
        static void SetActor(RoomState state, Vector3 eye)
        {
            var actor = state.Player("host"); if (actor == null) return;
            actor.x = -eye.x; actor.y = eye.y - 1.65f; actor.z = eye.z;
        }
        static Bounds BoundsOf(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length > 0, "Render assembly is empty: " + root.name);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
        static void Complete(IEnumerator steps)
        {
            while (steps.MoveNext()) if (steps.Current is IEnumerator child) Complete(child);
        }
        static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Production review: " + message);
        }

        sealed class ReviewScene : IDisposable
        {
            readonly Scene previous, scene;
            readonly bool batch;
            readonly List<GameObject> activeRoots = new List<GameObject>();
            readonly UnityEngine.Random.State randomState;
            public Transform Root { get; }
            public ReviewScene()
            {
                previous = SceneManager.GetActiveScene(); randomState = UnityEngine.Random.state;
                batch = Application.isBatchMode;
                if (!batch)
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                            throw new InvalidOperationException("Save untitled scenes before starting an interactive production review.");
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                            if (root.activeSelf) { activeRoots.Add(root); root.SetActive(false); }
                }
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                Root = new GameObject("Production review fixture").transform;
                UnityEngine.Random.InitState(19102026);
            }
            public void Dispose()
            {
                try
                {
                    if (batch) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else
                    {
                        if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                    }
                }
                finally
                {
                    foreach (var root in activeRoots) if (root != null) root.SetActive(true);
                    UnityEngine.Random.state = randomState;
                }
            }
        }

        sealed class CaptureCamera : IDisposable
        {
            readonly string folder;
            readonly ReviewReport report;
            readonly RenderTexture frame, display;
            readonly Material post;
            readonly Light flashlight;
            readonly RenderTexture previousActive;
            public Camera Camera { get; }
            public CaptureCamera(Transform parent, string folder, ReviewReport report)
            {
                this.folder = folder; this.report = report; previousActive = RenderTexture.active;
                try
                {
                var cameraObject = new GameObject("Production GPU review camera"); cameraObject.transform.SetParent(parent, false);
                Camera = cameraObject.AddComponent<Camera>(); Camera.tag = "MainCamera"; Camera.enabled = false;
                Camera.allowHDR = true; Camera.allowMSAA = false; Camera.useOcclusionCulling = false;
                Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = new Color(.012f, .018f, .028f);
                Camera.fieldOfView = 68; Camera.nearClipPlane = .045f; Camera.farClipPlane = 240; Camera.aspect = (float)Width / Height;
                Camera.renderingPath = RenderingPath.DeferredShading; Camera.depthTextureMode = DepthTextureMode.DepthNormals;
                frame = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { name = "Production review HDR", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                display = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Production review output", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                Require(frame.Create() && display.Create(), "GPU reference target allocation failed.");
                Camera.targetTexture = frame;
                var shader = Resources.Load<Shader>("Shaders/DeadAirTape");
                Require(shader != null && shader.isSupported, "The actual game presentation shader is unavailable.");
                post = new Material(shader) { hideFlags = HideFlags.DontSave };
                post.SetFloat("_Aspect", (float)Width / Height); post.SetFloat("_Exposure", .85f * DeadAirSettings.Brightness);
                post.SetFloat("_Antialiasing", 1); post.SetFloat("_Clock", 30); post.SetFloat("_Grain", 0); post.SetFloat("_Flicker", 0);
                var lightObject = new GameObject("Recorded reference flashlight"); lightObject.transform.SetParent(cameraObject.transform, false);
                flashlight = lightObject.AddComponent<Light>(); flashlight.transform.localPosition = new Vector3(.1f, -.1f, .12f);
                flashlight.type = LightType.Spot; flashlight.range = 20; flashlight.spotAngle = 62; flashlight.innerSpotAngle = 26;
                flashlight.color = new Color(1, .94f, .83f); flashlight.intensity = 1.55f; flashlight.renderMode = LightRenderMode.ForcePixel;
                flashlight.shadows = LightShadows.Soft; flashlight.shadowBias = .012f; flashlight.shadowNormalBias = .025f;
                }
                catch { Dispose(); throw; }
            }
            public void Shot(string file, string chapter, Vector3 eye, Vector3 target, bool referenceFlashlight, Action prepare = null)
            {
                Require(frame != null && display != null && frame.IsCreated() && display.IsCreated(),
                    "GPU review targets were lost; refusing a backbuffer readback: " + file);
                Camera.transform.position = eye; Camera.transform.LookAt(target); flashlight.enabled = referenceFlashlight;
                Physics.SyncTransforms(); prepare?.Invoke();
                foreach (var probes in UnityEngine.Object.FindObjectsByType<DeadAirArtReflectionProbes>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    probes.CaptureReference(Camera);
                Camera.Render(); Graphics.Blit(frame, display, post);
                RenderTexture.active = display;
                var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                try
                {
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                    var samples = pixels.GetPixels32(); float sum = 0, min = 1, max = 0;
                    for (int i = 0; i < samples.Length; i += 37)
                    {
                        var p = samples[i]; float l = (p.r * .2126f + p.g * .7152f + p.b * .0722f) / 255;
                        sum += l; min = Mathf.Min(min, l); max = Mathf.Max(max, l);
                    }
                    float mean = sum / Mathf.CeilToInt(samples.Length / 37f);
                    File.WriteAllBytes(Path.Combine(folder, file + ".png"), pixels.EncodeToPNG());
                    report.shots.Add(new ShotRecord { file = file + ".png", chapter = chapter, eye = eye, target = target,
                        referenceFlashlight = referenceFlashlight, meanLuminance = mean, luminanceRange = max - min });
                    Require(max - min > .015f && mean > .002f, "GPU frame is blank or unreadable: " + file);
                }
                finally { UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.active = previousActive; }
            }
            public void Dispose()
            {
                if (Camera != null) Camera.targetTexture = null;
                RenderTexture.active = previousActive;
                if (frame != null) { frame.Release(); UnityEngine.Object.DestroyImmediate(frame); }
                if (display != null) { display.Release(); UnityEngine.Object.DestroyImmediate(display); }
                if (post != null) UnityEngine.Object.DestroyImmediate(post);
                if (Camera != null) UnityEngine.Object.DestroyImmediate(Camera.gameObject);
            }
        }
    }
}
