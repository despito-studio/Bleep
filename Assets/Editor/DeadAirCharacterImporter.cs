using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirCharacterImporter
    {
        static readonly string[] Roles = { "engineer", "operator", "journalist", "dispatcher" };
        static readonly string[] ClipNames = { "Idle", "Walk", "Run", "UseItem" };
        static readonly string[] IndieClipNames = { "Idle", "Walk", "Run", "UseItem", "WalkBack", "StrafeLeft", "StrafeRight", "CrouchIdle", "CrouchWalk" };
        const string ModelFolder = "Assets/Resources/Characters";
        const string MaterialFolder = "Assets/Characters/Materials";
        const string PrefabFolder = "Assets/Resources/World";
        const string HandFolder = "Assets/Resources/Characters/Hands";
        const string HandMeshFolder = "Assets/Characters/Meshes";

        [MenuItem("Dead Air/Import Blender Characters")]
        public static void ImportAll()
        {
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(HandFolder);
            Directory.CreateDirectory(HandMeshFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string role in Roles)
            {
                ConfigureTexture(role);
                ImportCharacter(role);
                ImportHands(role);
            }
            AssetDatabase.SaveAssets();
            Validate();
        }

        static void ImportCharacter(string role)
        {
            string path = ModelFolder + "/character_" + role + ".fbx";
            if (!File.Exists(path)) throw new FileNotFoundException("Export the Blender character before importing.", path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing FBX importer: " + path);
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.isReadable = true;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();

            var defaults = importer.defaultClipAnimations;
            var clips = new List<ModelImporterClipAnimation>();
            bool indie = defaults.Any(c => c.name.IndexOf("CrouchWalk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.takeName.IndexOf("CrouchWalk", StringComparison.OrdinalIgnoreCase) >= 0);
            var expectedClips = indie ? IndieClipNames : ClipNames;
            foreach (string expected in expectedClips)
            {
                var clip = defaults.FirstOrDefault(c => string.Equals(c.name, expected, StringComparison.OrdinalIgnoreCase)) ??
                    defaults.FirstOrDefault(c => ActionName(c.name).Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                        ActionName(c.takeName).Equals(expected, StringComparison.OrdinalIgnoreCase));
                if (clip == null)
                    throw new InvalidOperationException(path + " has no baked " + expected + " action. Takes: " + string.Join(", ", defaults.Select(c => c.name)));
                clip.name = expected;
                clip.loopTime = expected != "UseItem";
                clip.loopPose = expected != "UseItem";
                clip.wrapMode = expected == "UseItem" ? WrapMode.ClampForever : WrapMode.Loop;
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.lockRootRotation = true;
                clips.Add(clip);
            }
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = new GameObject("character_" + role);
            try
            {
                var visual = UnityEngine.Object.Instantiate(model, root.transform);
                visual.name = "Blender character";
                var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException(path + " has no skinned meshes.");
                foreach (var renderer in renderers)
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => ImportMaterial(role, m)).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.updateWhenOffscreen = false;
                    var safeBounds = renderer.localBounds;
                    safeBounds.Expand(new Vector3(.8f, .3f, .8f));
                    renderer.localBounds = safeBounds;
                }
                // Blender uses metres. Keep the authored proportions, and only correct obvious unit conversions.
                var bounds = MeasureGeometry(root);
                if (bounds.size.y < .5f || bounds.size.y > 4)
                {
                    visual.transform.localScale *= 1.8f / Mathf.Max(.0001f, bounds.size.y);
                    bounds = MeasureGeometry(root);
                }
                // The new cast authors its rig origin and contact sockets in metres.
                // Bounds-centering asymmetric clothing would shift those measured eye/grip contacts.
                visual.transform.localPosition -= indie ? new Vector3(0, bounds.min.y, 0) :
                    new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var animation = visual.GetComponentInChildren<Animation>(true);
                if (animation == null) animation = visual.AddComponent<Animation>();
                var importedClips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(clip => expectedClips.Contains(clip.name)).ToArray();
                foreach (var clip in importedClips) animation.AddClip(clip, clip.name);
                animation.clip = animation.GetClip("Idle");
                animation.playAutomatically = false;
                animation.cullingType = AnimationCullingType.BasedOnRenderers;
                // AddClip updates legacy runtime states. Persist the actual
                // imported clip references explicitly after an FBX reimport.
                var serializedAnimation = new SerializedObject(animation);
                var savedClips = serializedAnimation.FindProperty("m_Animations");
                savedClips.arraySize = expectedClips.Length;
                for (int i = 0; i < expectedClips.Length; i++)
                    savedClips.GetArrayElementAtIndex(i).objectReferenceValue = importedClips.Single(clip => clip.name == expectedClips[i]);
                serializedAnimation.FindProperty("m_Animation").objectReferenceValue = importedClips.Single(clip => clip.name == "Idle");
                serializedAnimation.ApplyModifiedPropertiesWithoutUndo();
                var motion = root.AddComponent<DeadAirCharacterMotion>();
                motion.ClassId = role;
                string prefabPath = PrefabFolder + "/character_" + role + ".prefab";
                // Saving through AssetDatabase retains the existing prefab's GUID and every scene reference.
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("Imported Blender " + role + ": " + renderers.Length + " skinned meshes; " + bounds.size.ToString("F3") + " m.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static string ActionName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int separator = name.LastIndexOf('|');
            return separator >= 0 ? name.Substring(separator + 1) : name;
        }

        static Material ImportMaterial(string role, Material source)
        {
            if (source == null) throw new InvalidOperationException("Blender character contains an unassigned material.");
            string safeName = string.Concat(source.name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
            string path = MaterialFolder + "/" + role + "_" + safeName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Dead Air/Source Surface");
            if (shader == null) throw new InvalidOperationException("The character surface shader is missing.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.color = source.HasProperty("_Color") ? source.GetColor("_Color") :
                source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            material.mainTexture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") :
                source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            bool indie = source.name.StartsWith(role + "_indie_palette", StringComparison.Ordinal);
            bool indieHand = source.name.StartsWith(role + "_indie_hand_", StringComparison.Ordinal);
            string atlasPath = "Assets/Characters/Textures/" + role + (indie ? "_indie_albedo.png" : "_albedo.png");
            var atlas = indieHand ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (indie && atlas == null) throw new InvalidOperationException("Missing indie cast palette: " + atlasPath);
            if (atlas != null)
            {
                material.mainTexture = atlas;
                // Original sources used an authored .8 factor; the new atlas is final colour.
                // Keep these contracts separate when old assets remain in the project.
                material.color = indie ? Color.white : new Color(.8f, .8f, .8f, 1);
            }
            if (indieHand) material.mainTexture = null; // Newly authored FPS parts carry solid palette colours.
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.SetFloat("_Glossiness", .06f);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Cull", 2);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1);
            material.SetFloat("_Cutoff", 0);
            material.SetColor("_EmissionColor", Color.black);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void ConfigureTexture(string role)
        {
            ConfigureTexture("Assets/Characters/Textures/" + role + "_albedo.png", false);
            ConfigureTexture("Assets/Characters/Textures/" + role + "_indie_albedo.png", true);
        }

        static void ConfigureTexture(string path, bool indie)
        {
            if (!File.Exists(path)) return; // The original reference cast has a flat material palette.
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing character atlas importer: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.filterMode = indie ? FilterMode.Point : FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = indie ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        // The authored chunks retain their original atlas UVs. Bake the FBX axis
        // conversion into each mesh, then place it in the grip solver's unit frame.
        static void ImportHands(string role)
        {
            string sourcePath = "Assets/Characters/Imported/hand_" + role + ".fbx";
            if (!File.Exists(sourcePath)) return;
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing hand FBX importer: " + role);
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.isReadable = true;
            importer.SaveAndReimport();
            var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
            var root = new GameObject("hand_" + role);
            string meshPath = HandMeshFolder + "/hand_" + role + ".asset";
            var saved = AssetDatabase.LoadAllAssetsAtPath(meshPath).OfType<Mesh>().ToDictionary(m => m.name);
            try
            {
                var sources = source.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
                foreach (string part in HandPartNames())
                {
                    if (!sources.TryGetValue(part, out var filter) || filter.sharedMesh == null)
                        throw new InvalidOperationException(role + " is missing the textured hand part " + part);
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || renderer.sharedMaterials.Length == 0)
                        throw new InvalidOperationException(role + " hand part has no material: " + part);
                    var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                    mesh.name = part;
                    var vertices = mesh.vertices;
                    var normals = mesh.normals;
                    var matrix = filter.transform.localToWorldMatrix;
                    var normalMatrix = matrix.inverse.transpose;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                        if (i < normals.Length) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                    }
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    var bounds = mesh.bounds;
                    if (bounds.size.x < .00001f || bounds.size.y < .00001f || bounds.size.z < .00001f)
                        throw new InvalidOperationException(role + " has a flat/empty hand part: " + part);
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i] = Vector3.Scale(vertices[i] - bounds.center,
                            new Vector3(1 / bounds.size.x, 1 / bounds.size.y, 1 / bounds.size.z));
                    mesh.vertices = vertices;
                    // Correct normals for the final nonuniform normalization.
                    for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.Scale(normals[i], bounds.size).normalized;
                    mesh.normals = normals;
                    mesh.RecalculateBounds();
                    Mesh asset;
                    if (saved.TryGetValue(part, out asset))
                    {
                        EditorUtility.CopySerialized(mesh, asset);
                        UnityEngine.Object.DestroyImmediate(mesh);
                        EditorUtility.SetDirty(asset);
                    }
                    else
                    {
                        asset = mesh;
                        if (!File.Exists(meshPath)) AssetDatabase.CreateAsset(asset, meshPath);
                        else AssetDatabase.AddObjectToAsset(asset, meshPath);
                        saved.Add(part, asset);
                    }
                    var child = new GameObject(part);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = asset;
                    child.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials.Select(m => ImportMaterial(role, m)).ToArray();
                }
                PrefabUtility.SaveAsPrefabAsset(root, HandFolder + "/hand_" + role + ".prefab");
                Debug.Log("IMPORTED CHARACTER HANDS: " + role + " parts=44; source palette and shared meshes/materials retained for every equipment grip.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static IEnumerable<string> HandPartNames()
        {
            foreach (string side in new[] { "Right", "Left" })
            {
                foreach (string part in new[] { "forearm", "cuff", "palm", "thumb", "thumb_middle", "thumb_tip" }) yield return side + "_" + part;
                for (int finger = 0; finger < 4; finger++)
                    foreach (string part in new[] { "proximal", "middle", "distal", "tip" }) yield return side + "_finger" + finger + "_" + part;
            }
        }

        static Bounds MeasureGeometry(GameObject root)
        {
            var bounds = new Bounds();
            bool initialized = false;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    foreach (var vertex in baked.vertices)
                    {
                        var point = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                        if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            if (!initialized) throw new InvalidOperationException(root.name + " has no skinned geometry to measure.");
            return bounds;
        }

        [MenuItem("Dead Air/Validate Blender Characters")]
        public static void Validate()
        {
            foreach (string role in Roles)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/character_" + role + ".prefab");
                if (prefab == null) throw new InvalidOperationException("Missing character prefab: " + role);
                var motion = prefab.GetComponent<DeadAirCharacterMotion>();
                if (motion == null || motion.ClassId != role) throw new InvalidOperationException("Missing character presentation for " + role);
                var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0 || renderers.Any(r => r.sharedMesh == null || r.bones.Length < 10 || r.bones.Any(b => b == null)))
                    throw new InvalidOperationException(role + " has missing skinning or bone references.");
                int triangles = renderers.Sum(r => (int)r.sharedMesh.GetIndexCount(0) / 3);
                foreach (var renderer in renderers)
                    for (int submesh = 1; submesh < renderer.sharedMesh.subMeshCount; submesh++)
                        triangles += (int)renderer.sharedMesh.GetIndexCount(submesh) / 3;
                if (triangles < 100 || triangles > 20000) throw new InvalidOperationException(role + " triangle budget is invalid: " + triangles);
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    // FBX reimports invalidate the native legacy state cache on
                    // loaded prefab assets. A fresh instance resolves persisted
                    // clip references exactly as gameplay does.
                    var instanceAnimation = instance.GetComponentInChildren<Animation>(true);
                    foreach (string name in ClipNames)
                    {
                        var clip = instanceAnimation == null ? null : instanceAnimation.GetClip(name);
                        if (clip == null || !clip.legacy || clip.length <= .1f || AnimationUtility.GetCurveBindings(clip).Length < 4)
                            throw new InvalidOperationException(role + " has no usable baked " + name + " animation.");
                        bool movingCurve = AnimationUtility.GetCurveBindings(clip).Any(binding =>
                        {
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            return binding.type == typeof(Transform) && curve != null && curve.keys.Length > 1 &&
                                curve.keys.Max(k => k.value) - curve.keys.Min(k => k.value) > .001f;
                        });
                        if (!movingCurve) throw new InvalidOperationException(role + " " + name + " has only a static pose.");
                    }
                    var bounds = MeasureGeometry(instance);
                    if (bounds.size.y < 1.2f || bounds.size.y > 2.8f || bounds.size.x < .2f || bounds.size.x > 2.5f || Mathf.Abs(bounds.min.y) > .05f)
                        throw new InvalidOperationException(role + " has incorrect metre scale or foot origin: " + bounds);
                    var walk = instanceAnimation.GetClip("Walk");
                    walk.SampleAnimation(instanceAnimation.gameObject, 0);
                    var firstPose = SkinVertices(instance);
                    walk.SampleAnimation(instanceAnimation.gameObject, walk.length * .25f);
                    var secondPose = SkinVertices(instance);
                    if (firstPose.Length != secondPose.Length || firstPose.Length == 0 ||
                        !firstPose.Where((point, index) => Vector3.Distance(point, secondPose[index]) > .025f).Any())
                        throw new InvalidOperationException(role + " Walk curves do not deform the imported skin.");
                    var animatedBounds = MeasureGeometry(instance);
                    if (animatedBounds.size.y < .8f || animatedBounds.size.y > 3.2f || animatedBounds.size.x > 3)
                        throw new InvalidOperationException(role + " Walk produces invalid skin bounds: " + animatedBounds);
                    int clipCount = 0; foreach (AnimationState state in instanceAnimation) clipCount++;
                    Debug.Log("BLENDER CHARACTER VALIDATED: " + role + " triangles=" + triangles + " bones=" + renderers.Max(r => r.bones.Length) +
                        " clips=" + clipCount + " dimensions=" + bounds.size.ToString("F3"));
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            Debug.Log("DEAD AIR BLENDER CHARACTERS PASSED: four rigged characters, required Idle/Walk/Run/UseItem actions, materials and metre-scale geometry.");
        }

        static Vector3[] SkinVertices(GameObject root)
        {
            var points = new List<Vector3>();
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    points.AddRange(baked.vertices.Select(point => root.transform.InverseTransformPoint(renderer.transform.TransformPoint(point))));
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            return points.ToArray();
        }
    }
}
