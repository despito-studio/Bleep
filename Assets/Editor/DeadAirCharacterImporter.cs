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
        const string ModelFolder = "Assets/Resources/Characters";
        const string MaterialFolder = "Assets/Characters/Materials";
        const string PrefabFolder = "Assets/Resources/World";

        [MenuItem("Dead Air/Import Blender Characters")]
        public static void ImportAll()
        {
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(PrefabFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string role in Roles) ImportCharacter(role);
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
            foreach (string expected in ClipNames)
            {
                var clip = defaults.FirstOrDefault(c => string.Equals(c.name, expected, StringComparison.OrdinalIgnoreCase)) ??
                    defaults.FirstOrDefault(c => c.name.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        c.takeName.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0);
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
                visual.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var animation = visual.GetComponentInChildren<Animation>(true);
                if (animation == null) animation = visual.AddComponent<Animation>();
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    if (ClipNames.Contains(clip.name)) animation.AddClip(clip, clip.name);
                animation.clip = animation.GetClip("Idle");
                animation.playAutomatically = false;
                animation.cullingType = AnimationCullingType.BasedOnRenderers;
                var motion = root.AddComponent<DeadAirCharacterMotion>();
                motion.ClassId = role;
                string prefabPath = PrefabFolder + "/character_" + role + ".prefab";
                // Saving through AssetDatabase retains the existing prefab's GUID and every scene reference.
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("Imported Blender " + role + ": " + renderers.Length + " skinned meshes; " + bounds.size.ToString("F3") + " m.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
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
                var animation = prefab.GetComponentInChildren<Animation>(true);
                foreach (string name in ClipNames)
                {
                    var clip = animation == null ? null : animation.GetClip(name);
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
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var bounds = MeasureGeometry(instance);
                    if (bounds.size.y < 1.2f || bounds.size.y > 2.8f || bounds.size.x < .2f || bounds.size.x > 2.5f || Mathf.Abs(bounds.min.y) > .05f)
                        throw new InvalidOperationException(role + " has incorrect metre scale or foot origin: " + bounds);
                    var instanceAnimation = instance.GetComponentInChildren<Animation>(true);
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
                    Debug.Log("BLENDER CHARACTER VALIDATED: " + role + " triangles=" + triangles + " bones=" + renderers.Max(r => r.bones.Length) +
                        " clips=4 dimensions=" + bounds.size.ToString("F3"));
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            Debug.Log("DEAD AIR BLENDER CHARACTERS PASSED: four rigged characters, four baked actions each, materials and metre-scale geometry.");
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
