using System;
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
    public static class DeadAirProductionCatalogCapture
    {
        const string Types = "Assets/Office/Authoring/Types";
        const string Output = "outputs/art-overhaul-20261019/catalog-captures";
        const int Resolution = 640, StudioLayer = 25;
        [Serializable] sealed class Entry
        {
            public string type, prefab, image;
            public Vector3 boundsCenter, boundsSize;
            public int meshParts, uniqueMeshes, renderers, uniqueMaterials, colliders, enabledColliders, triggerColliders, lodGroups;
            public long vertices, triangles, lod0Triangles;
            public int geometryCheckedMeshes;
            public long geometryCheckedVertices;
            public int normalMappedMaterials, surfaceMappedMaterials, magentaPixels, foregroundPixels;
            public float magentaFraction, meanLuminance, luminanceRange, luminanceStdDev, foregroundFraction;
            public string[] materialNames, shaderNames, colliderKinds, issues;
        }
        [Serializable] sealed class Report
        {
            public string generatedUtc, graphicsDevice, graphicsApi;
            public string framing = "640px square; automatic bounds, LOD0, neutral three-quarter studio, actual geometry/materials. Real HDR environment captured from studio ground and reflection-only softboxes with the tested model excluded. No UI.";
            public int expectedTypes, capturedTypes, failedTypes;
            public List<Entry> entries = new List<Entry>();
        }

        [MenuItem("Dead Air/Production Art/Capture Saved Type Catalog")]
        public static void CaptureCatalog()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Catalog capture requires a GPU; omit -nographics.");
            if (!Directory.Exists(Types)) throw new DirectoryNotFoundException(Types);
            var paths = Directory.GetFiles(Types, "*.prefab").OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (paths.Length == 0) throw new InvalidOperationException("The saved authoring type catalog is empty.");
            PrepareFreshBatchScene();
            string folder = Path.GetFullPath(Output); Directory.CreateDirectory(folder);
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), expectedTypes = paths.Length };
            using (var studio = new Studio())
            {
                foreach (var path in paths)
                {
                    Entry entry;
                    try { entry = CaptureType(path.Replace('\\', '/'), studio, folder); }
                    catch (Exception error)
                    {
                        entry = new Entry { type = Path.GetFileNameWithoutExtension(path), prefab = path, issues = new[] { error.ToString() } };
                        Debug.LogError("Catalog capture failed for " + path + ": " + error.Message);
                    }
                    report.entries.Add(entry);
                    if (!string.IsNullOrEmpty(entry.image)) report.capturedTypes++;
                    if (entry.issues != null && entry.issues.Length > 0) report.failedTypes++;
                    File.WriteAllText(Path.Combine(folder, "catalog-capture-report.json"), JsonUtility.ToJson(report, true));
                    // Keep only one asset family resident; the full catalog includes 2K scanned atlases.
                    EditorUtility.UnloadUnusedAssetsImmediate();
                    GC.Collect(); GC.WaitForPendingFinalizers();
                }
            }
            Debug.Log("DEAD AIR CATALOG CAPTURE: " + report.capturedTypes + "/" + report.expectedTypes + " images, " + report.failedTypes + " flagged families. " + folder);
            if (report.failedTypes != 0)
                throw new InvalidOperationException("Catalog review flagged " + report.failedTypes + " families; inspect catalog-capture-report.json and their saved PNGs.");
        }

        public static void PrepareFreshBatchScene()
        {
            if (!Application.isBatchMode) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeadAirScannedModels.ClearCaches();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect(); GC.WaitForPendingFinalizers();
        }

        static Entry CaptureType(string path, Studio studio, string folder)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Saved prefab did not load.");
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, studio.Scene);
                instance.transform.SetParent(studio.Root, false); instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.SetActive(true);
                foreach (var part in instance.GetComponentsInChildren<Transform>(true))
                {
                    part.gameObject.layer = StudioLayer;
                    if (part.name.StartsWith("mirror:", StringComparison.Ordinal) && part.GetComponent<Renderer>() != null)
                        DeadAirArtMirror.Attach(part.GetComponent<Renderer>(), Vector3.forward);
                }
                var lods = instance.GetComponentsInChildren<LODGroup>(true);
                foreach (var lod in lods) lod.ForceLOD(0);
                foreach (var light in instance.GetComponentsInChildren<Light>(true)) light.enabled = false;
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                var geometry = DeadAirArtGeometryValidation.RequireFiniteAssembly(instance.transform);
                var bounds = BoundsOf(renderers);
                instance.transform.position = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(renderers);
                var issues = new List<string>();
                var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
                foreach (var renderer in renderers)
                    if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null)) issues.Add("Missing material slot: " + renderer.name);
                foreach (var material in materials)
                    if (material.shader == null || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                        issues.Add("Missing, unsupported or failed shader: " + material.name + " / " + (material.shader != null ? material.shader.name : "null"));
                var meshes = instance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null).ToArray();
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh).Where(m => m != null).ToArray();
                meshes = meshes.Concat(skins).ToArray();
                if (meshes.Length == 0) issues.Add("The authoring prefab has no mesh parts.");
                var colliders = instance.GetComponentsInChildren<Collider>(true);
                var entry = new Entry
                {
                    type = Path.GetFileNameWithoutExtension(path), prefab = path, boundsCenter = bounds.center, boundsSize = bounds.size,
                    meshParts = meshes.Length, uniqueMeshes = meshes.Distinct().Count(), renderers = renderers.Length, uniqueMaterials = materials.Length,
                    colliders = colliders.Length, enabledColliders = colliders.Count(c => c.enabled), triggerColliders = colliders.Count(c => c.isTrigger), lodGroups = lods.Length,
                    vertices = meshes.Sum(m => (long)m.vertexCount), triangles = meshes.Sum(Triangles),
                    geometryCheckedMeshes = geometry.uniqueMeshes, geometryCheckedVertices = geometry.vertices,
                    materialNames = materials.Select(m => m.name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                    shaderNames = materials.Where(m => m.shader != null).Select(m => m.shader.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                    colliderKinds = colliders.Select(c => c.GetType().Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                    normalMappedMaterials = materials.Count(m => m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null),
                    surfaceMappedMaterials = materials.Count(m => m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null || m.HasProperty("_SurfaceMap") && m.GetTexture("_SurfaceMap") != null)
                };
                var lowerRenderers = new HashSet<Renderer>();
                foreach (var lod in lods)
                {
                    var levels = lod.GetLODs();
                    for (int i = 1; i < levels.Length; i++) foreach (var renderer in levels[i].renderers) if (renderer != null) lowerRenderers.Add(renderer);
                }
                entry.lod0Triangles = renderers.Where(r => !lowerRenderers.Contains(r)).Sum(r =>
                {
                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
                    else
                    {
                        var filter = r.GetComponent<MeshFilter>();
                        if (filter != null) mesh = filter.sharedMesh;
                    }
                    return mesh != null ? Triangles(mesh) : 0;
                });
                studio.Frame(bounds);
                instance.SetActive(false);
                try { studio.CaptureEnvironment(bounds); }
                finally { instance.SetActive(true); }
                string filename = entry.type + ".png";
                var pixels = studio.Render();
                try
                {
                    File.WriteAllBytes(Path.Combine(folder, filename), pixels.EncodeToPNG()); entry.image = filename;
                    var colors = pixels.GetPixels32(); int longestRun = 0, run = 0; double luminance = 0, squaredLuminance = 0;
                    double minimumLuminance = 1, maximumLuminance = 0;
                    foreach (var color in colors)
                    {
                        // Error pink requires both red and blue; red equipment indicators do not match it.
                        bool magenta = color.r > 225 && color.b > 225 && color.g < 45;
                        if (magenta) { entry.magentaPixels++; run++; longestRun = Mathf.Max(longestRun, run); }
                        else run = 0;
                        double value = (color.r * .2126 + color.g * .7152 + color.b * .0722) / 255;
                        luminance += value; squaredLuminance += value * value;
                        minimumLuminance = Math.Min(minimumLuminance, value); maximumLuminance = Math.Max(maximumLuminance, value);
                    }
                    entry.magentaFraction = (float)entry.magentaPixels / colors.Length; entry.meanLuminance = (float)(luminance / colors.Length);
                    entry.luminanceRange = (float)(maximumLuminance - minimumLuminance);
                    entry.luminanceStdDev = (float)Math.Sqrt(Math.Max(0, squaredLuminance / colors.Length - Math.Pow(luminance / colors.Length, 2)));
                    if (entry.luminanceRange < .025f || entry.luminanceStdDev < .003f)
                        issues.Add("GPU image is flat: insufficient actual luminance range/variance.");
                    if (entry.magentaFraction > .005f && longestRun >= 8)
                        issues.Add("Contiguous error-pink pixels cover " + entry.magentaFraction.ToString("P2") + " of the GPU image.");
                    Texture2D emptyStudio = null;
                    try
                    {
                        instance.SetActive(false);
                        emptyStudio = studio.Render();
                        var background = emptyStudio.GetPixels32();
                        for (int i = 0; i < colors.Length; i++)
                            if (Math.Abs(colors[i].r - background[i].r) > 6 || Math.Abs(colors[i].g - background[i].g) > 6 || Math.Abs(colors[i].b - background[i].b) > 6)
                                entry.foregroundPixels++;
                        entry.foregroundFraction = (float)entry.foregroundPixels / colors.Length;
                        if (entry.foregroundFraction < .001f)
                            issues.Add("GPU image contains no measurable model foreground compared with the same empty studio.");
                    }
                    finally
                    {
                        if (emptyStudio != null) UnityEngine.Object.DestroyImmediate(emptyStudio);
                        instance.SetActive(true);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(pixels); }
                entry.issues = issues.Distinct().ToArray();
                return entry;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                prefab = null;
            }
        }

        static long Triangles(Mesh mesh)
        {
            long count = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetTopology(submesh) == MeshTopology.Triangles) count += (long)mesh.GetIndexCount(submesh) / 3;
            return count;
        }
        static Bounds BoundsOf(Renderer[] renderers)
        {
            bool found = false; var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                if (!renderer.gameObject.activeInHierarchy || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            if (!found || bounds.size.sqrMagnitude < .000001f) throw new InvalidOperationException("The type has empty render bounds.");
            return bounds;
        }

        sealed class Studio : IDisposable
        {
            readonly Scene previousScene;
            readonly bool batch;
            readonly List<GameObject> priorRoots = new List<GameObject>();
            readonly RenderTexture hdr, display, previousTarget;
            readonly Material groundMaterial, post, softboxMaterial;
            readonly GameObject ground;
            readonly List<Transform> softboxes = new List<Transform>();
            readonly ReflectionProbe environment;
            RenderTexture environmentCube;
            readonly Camera camera;
            readonly float previousShadowDistance;
            readonly int previousPixelLights;
            readonly ShadowQuality previousShadows;
            public Scene Scene { get; }
            public Transform Root { get; }
            public Studio()
            {
                previousScene = SceneManager.GetActiveScene(); previousTarget = RenderTexture.active;
                batch = Application.isBatchMode;
                previousShadowDistance = QualitySettings.shadowDistance; previousPixelLights = QualitySettings.pixelLightCount; previousShadows = QualitySettings.shadows;
                try
                {
                if (!batch)
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                            throw new InvalidOperationException("Save untitled scenes before starting an interactive catalog capture.");
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                            if (root.activeSelf) { priorRoots.Add(root); root.SetActive(false); }
                }
                Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive); SceneManager.SetActiveScene(Scene);
                Root = new GameObject("One-at-a-time prop studio").transform;
                RenderSettings.fog = false; RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.42f, .44f, .47f);
                RenderSettings.ambientEquatorColor = new Color(.25f, .26f, .28f); RenderSettings.ambientGroundColor = new Color(.12f, .12f, .13f);
                RenderSettings.reflectionIntensity = 1;
                QualitySettings.shadows = ShadowQuality.All; QualitySettings.pixelLightCount = 6;
                groundMaterial = new Material(Shader.Find("Standard")) { name = "Neutral studio ground", color = new Color(.18f, .19f, .21f), hideFlags = HideFlags.DontSave };
                groundMaterial.SetFloat("_Glossiness", .16f);
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Studio ground and contact shadows";
                ground.transform.SetParent(Root, false); ground.GetComponent<Renderer>().sharedMaterial = groundMaterial; ground.layer = StudioLayer;
                softboxMaterial = new Material(Shader.Find("Unlit/Color")) { name = "Real studio reflection softboxes", color = Color.white, hideFlags = HideFlags.HideAndDontSave };
                for (int i = 0; i < 4; i++)
                {
                    var card = GameObject.CreatePrimitive(PrimitiveType.Cube); card.name = "Reflection-only studio softbox " + i;
                    card.layer = 24; card.transform.SetParent(Root, false);
                    UnityEngine.Object.DestroyImmediate(card.GetComponent<Collider>());
                    card.GetComponent<Renderer>().sharedMaterial = softboxMaterial;
                    card.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    softboxes.Add(card.transform);
                }
                var probeObject = new GameObject("Actual captured studio environment"); probeObject.transform.SetParent(Root, false);
                environment = probeObject.AddComponent<ReflectionProbe>(); environment.mode = ReflectionProbeMode.Custom;
                environment.boxProjection = false; environment.intensity = 1; environment.importance = 10;
                environment.blendDistance = 0; environment.size = Vector3.one * 1000;
                Light("Large soft key", Quaternion.Euler(48, -32, 0), 1.7f, new Color(1, .97f, .93f), true);
                Light("Neutral fill", Quaternion.Euler(22, 135, 0), .7f, new Color(.88f, .93f, 1), false);
                Light("Upper rim", Quaternion.Euler(65, 180, 0), .45f, Color.white, false);
                var view = new GameObject("Neutral catalog camera"); view.transform.SetParent(Root, false); camera = view.AddComponent<Camera>();
                camera.enabled = false; camera.allowHDR = true; camera.allowMSAA = false; camera.useOcclusionCulling = false;
                camera.cullingMask = 1 << StudioLayer; camera.fieldOfView = 42; camera.aspect = 1;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.19f, .21f, .24f);
                camera.renderingPath = RenderingPath.Forward;
                hdr = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { name = "Catalog studio HDR", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                display = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "Catalog studio readback", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                if (!hdr.Create() || !display.Create()) throw new InvalidOperationException("Studio GPU targets failed to allocate.");
                camera.targetTexture = hdr;
                var shader = Resources.Load<Shader>("Shaders/DeadAirTape");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("The game tone-mapping shader is unavailable.");
                post = new Material(shader) { hideFlags = HideFlags.DontSave };
                post.SetFloat("_Exposure", .85f); post.SetFloat("_Aspect", 1); post.SetFloat("_Antialiasing", 1);
                post.SetFloat("_Clock", 0); post.SetFloat("_Grain", 0); post.SetFloat("_Flicker", 0);
                }
                catch { Dispose(); throw; }
            }
            void Light(string name, Quaternion rotation, float intensity, Color color, bool shadows)
            {
                var node = new GameObject(name); node.transform.SetParent(Root, false); node.transform.rotation = rotation;
                var light = node.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity; light.color = color;
                light.cullingMask = 1 << StudioLayer; light.renderMode = LightRenderMode.ForcePixel;
                light.shadows = shadows ? LightShadows.Soft : LightShadows.None; light.shadowBias = .012f; light.shadowNormalBias = .025f;
                if (shadows) light.shadowCustomResolution = 2048;
            }
            public void Frame(Bounds bounds)
            {
                var direction = new Vector3(1.2f, .8f, 1.6f).normalized;
                var rotation = Quaternion.LookRotation(-direction, Vector3.up);
                var right = rotation * Vector3.right; var up = rotation * Vector3.up;
                float tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad), distance = .05f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    float near = Vector3.Dot(offset, direction);
                    distance = Mathf.Max(distance, near + Mathf.Abs(Vector3.Dot(offset, right)) / tangent,
                        near + Mathf.Abs(Vector3.Dot(offset, up)) / tangent);
                }
                distance = distance * 1.16f + bounds.extents.magnitude * .04f;
                camera.transform.SetPositionAndRotation(bounds.center + direction * distance, rotation);
                camera.nearClipPlane = Mathf.Max(.003f, distance / 200); camera.farClipPlane = Mathf.Max(12, distance + bounds.extents.magnitude * 5);
                float span = Mathf.Max(bounds.size.x, bounds.size.z) * 5 + 1;
                ground.transform.localScale = new Vector3(span, .02f, span); ground.transform.localPosition = new Vector3(0, -.014f, 0);
                QualitySettings.shadowDistance = Mathf.Max(5, distance + bounds.extents.magnitude * 3);
            }
            public Texture2D Render()
            {
                if (hdr == null || display == null || !hdr.IsCreated() || !display.IsCreated())
                    throw new InvalidOperationException("Studio GPU targets were lost before rendering; refusing a backbuffer readback.");
                Physics.SyncTransforms(); camera.Render(); Graphics.Blit(hdr, display, post);
                RenderTexture.active = display;
                var pixels = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
                try { pixels.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0); pixels.Apply(); return pixels; }
                catch { UnityEngine.Object.DestroyImmediate(pixels); throw; }
                finally { RenderTexture.active = previousTarget; }
            }
            public void CaptureEnvironment(Bounds bounds)
            {
                float radius = Mathf.Max(.35f, bounds.extents.magnitude) * 2.8f;
                var directions = new[] { new Vector3(-1, .7f, .7f), new Vector3(1, .4f, -.6f), new Vector3(0, 1, 0), new Vector3(.3f, .35f, 1) };
                for (int i = 0; i < softboxes.Count; i++)
                {
                    softboxes[i].position = bounds.center + directions[i].normalized * radius;
                    softboxes[i].rotation = Quaternion.LookRotation(bounds.center - softboxes[i].position);
                    softboxes[i].localScale = new Vector3(radius * (i == 2 ? 1.4f : .95f), radius * 1.3f, .02f);
                }
                if (environmentCube != null) { environment.customBakedTexture = null; environmentCube.Release(); UnityEngine.Object.DestroyImmediate(environmentCube); }
                environment.transform.position = bounds.center;
                environmentCube = DeadAirArtReflectionProbes.CaptureCube(bounds.center, 128, (1 << StudioLayer) | (1 << 24),
                    .005f, radius * 8, Scene, new Bounds(bounds.center, Vector3.one * radius * 8));
                environment.customBakedTexture = environmentCube;
            }
            public void Dispose()
            {
                try
                {
                    if (camera != null) camera.targetTexture = null; RenderTexture.active = previousTarget;
                    if (hdr != null) { hdr.Release(); UnityEngine.Object.DestroyImmediate(hdr); }
                    if (display != null) { display.Release(); UnityEngine.Object.DestroyImmediate(display); }
                    if (post != null) UnityEngine.Object.DestroyImmediate(post);
                    if (environmentCube != null) { if (environment != null) environment.customBakedTexture = null; environmentCube.Release(); UnityEngine.Object.DestroyImmediate(environmentCube); }
                    if (softboxMaterial != null) UnityEngine.Object.DestroyImmediate(softboxMaterial);
                    if (groundMaterial != null) UnityEngine.Object.DestroyImmediate(groundMaterial);
                    if (batch) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else
                    {
                        if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
                        if (Scene.IsValid() && Scene.isLoaded) EditorSceneManager.CloseScene(Scene, true);
                    }
                }
                finally
                {
                    foreach (var root in priorRoots) if (root != null) root.SetActive(true);
                    QualitySettings.shadowDistance = previousShadowDistance; QualitySettings.pixelLightCount = previousPixelLights; QualitySettings.shadows = previousShadows;
                }
            }
        }
    }
}
