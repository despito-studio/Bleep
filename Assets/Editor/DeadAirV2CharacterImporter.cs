using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    /// <summary>Explicit V2 import only. Never invokes the legacy cast importer or changes Resources wrappers.</summary>
    public static class DeadAirV2CharacterImporter
    {
        public const string AssetRoot = "Assets/Characters/ProductionV2/";

        [Serializable] public sealed class RigPath
        {
            public string semantic;
            public string path;
        }
        [Serializable] public sealed class Garment
        {
            public string id;
            public int mask;
            public string[] rendererPaths;
        }
        [Serializable] public sealed class Clip
        {
            public string action;
            public string name;
            public bool loop;
        }
        [Serializable] public sealed class Surface
        {
            public string rendererPath;
            public string[] materialPaths;
        }
        [Serializable] public sealed class Prop
        {
            public string id;
            public string mode;
            public string[] rendererPaths;
            public bool visibleWhileHeld = true;
            public bool visibleDuringAction = true;
            public float actionStart;
            public float actionEnd = 1;
            public bool aimWithView;
        }
        [Serializable] public sealed class ViewPitch
        {
            public Vector3 chestLocalAxis, headLocalAxis, aimPivotLocalAxis;
            public float chestShare, chestDownLimit, chestUpLimit, headDownLimit, headUpLimit;
            public string aimPivotPath;
            public Vector3 leftElbowPole, rightElbowPole;
            public float handContactTolerance;
        }
        [Serializable] public sealed class Manifest
        {
            public string characterId;
            public string characterClass;
            public string rigVersion;
            public string modelPath;
            public string prefabPath;
            public RigPath[] rigBindings;
            public string[] bodyRendererPaths;
            public Garment[] garments;
            public Clip[] clips;
            public Surface[] surfaces;
            public DeadAirCharacterV2Presentation.Gait[] gaits;
            public Vector3 ownerHeadCenter;
            public Vector3 ownerHeadRadii;
            public Prop[] props;
            public string flashlightSocketPath;
            public ViewPitch viewPitch;
        }

        [MenuItem("Dead Air/Production V2/Import Selected Manifest")]
        public static void ImportSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Select a V2 .import.json manifest.");
            ImportManifest(path);
        }

        // This entry point deliberately requires a real model and explicit manifest. It cannot manufacture a fallback body.
        public static void ImportVivian()
            => ImportManifest(AssetRoot + "vivian/Data/CHR_vivian.import.json");

        public static string ImportManifest(string manifestPath)
        {
            if (!IsV2Path(manifestPath) || !manifestPath.EndsWith(".import.json", StringComparison.Ordinal))
                throw new InvalidOperationException("Import manifests must be inside ProductionV2 and end in .import.json.");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("No authored V2 manifest exists.", manifestPath);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            ValidateManifest(manifest);
            if (!File.Exists(manifest.modelPath)) throw new FileNotFoundException("Export the new character first.", manifest.modelPath);
            AssetDatabase.ImportAsset(manifest.modelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(manifest.modelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("The specified model has no ModelImporter.");
            ConfigureModelImporter(importer);
            importer.SaveAndReimport();
            ConfigureClips(importer, manifest.clips);
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(manifest.modelPath);
            if (model == null) throw new InvalidOperationException("Unity did not import the authored V2 model.");
            var instance = UnityEngine.Object.Instantiate(model);
            instance.name = "CHR_" + manifest.characterId;
            try
            {
                ValidateImportedMeshes(instance);
                var animator = instance.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || animator.avatar.isHuman)
                    throw new InvalidOperationException("The imported character needs its own valid Generic Avatar.");
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var rig = instance.AddComponent<DeadAirCharacterRigBindings>();
                rig.Configure(manifest.characterId, manifest.characterClass, manifest.rigVersion,
                    manifest.rigBindings.Select(binding => new DeadAirCharacterRigBindings.Binding {
                        semantic = (DeadAirCharacterRigBindings.Joint)Enum.Parse(typeof(DeadAirCharacterRigBindings.Joint), binding.semantic),
                        target = ExactTransform(instance.transform, binding.path)
                    }).ToArray());
                var wardrobe = instance.AddComponent<DeadAirCharacterWardrobe>();
                wardrobe.ConfigureBindings(manifest.characterClass,
                    manifest.bodyRendererPaths.Select(path => ExactRenderer(instance.transform, path)).ToArray(),
                    manifest.garments.Select(garment => new DeadAirCharacterWardrobe.GarmentBinding {
                        id = garment.id, requiredMask = garment.mask, match = DeadAirCharacterWardrobe.MaskMatch.All,
                        renderers = garment.rendererPaths.Select(path => ExactRenderer(instance.transform, path)).ToArray()
                    }).ToArray());
                AssignMaterials(instance.transform, manifest.surfaces);
                var importedClips = AssetDatabase.LoadAllAssetsAtPath(manifest.modelPath).OfType<AnimationClip>().ToArray();
                var playback = instance.AddComponent<DeadAirCharacterAnimator>();
                playback.Configure(manifest.clips.Select(binding => new DeadAirCharacterAnimator.ClipBinding {
                    action = binding.action, rigVersion = manifest.rigVersion, loop = binding.loop,
                    clip = importedClips.Single(clip => clip.name == binding.name)
                }).ToArray());
                if (!playback.ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
                ValidateClipCurves(instance.transform, manifest, importedClips);
                var presentation = instance.AddComponent<DeadAirCharacterV2Presentation>();
                presentation.Configure(manifest.gaits, (manifest.props ?? Array.Empty<Prop>()).Select(prop => new DeadAirCharacterV2Presentation.Prop {
                    id = prop.id, mode = prop.mode, visibleWhileHeld = prop.visibleWhileHeld,
                    visibleDuringAction = prop.visibleDuringAction, actionStart = prop.actionStart, actionEnd = prop.actionEnd, aimWithView = prop.aimWithView,
                    renderers = prop.rendererPaths.Select(path => ExactRenderer(instance.transform, path)).ToArray()
                }).ToArray(), manifest.ownerHeadCenter, manifest.ownerHeadRadii,
                    CreateFlashlight(instance.transform, manifest.flashlightSocketPath), new DeadAirCharacterV2Presentation.ViewPitchProfile {
                        chestLocalAxis = manifest.viewPitch.chestLocalAxis, headLocalAxis = manifest.viewPitch.headLocalAxis,
                        chestShare = manifest.viewPitch.chestShare, chestDownLimit = manifest.viewPitch.chestDownLimit,
                        chestUpLimit = manifest.viewPitch.chestUpLimit, headDownLimit = manifest.viewPitch.headDownLimit,
                        headUpLimit = manifest.viewPitch.headUpLimit, aimPivotLocalAxis = manifest.viewPitch.aimPivotLocalAxis,
                        aimPivot = ExactTransform(instance.transform, manifest.viewPitch.aimPivotPath),
                        leftElbowPole = manifest.viewPitch.leftElbowPole, rightElbowPole = manifest.viewPitch.rightElbowPole,
                        handContactTolerance = manifest.viewPitch.handContactTolerance
                    });
                if (!presentation.ValidateProfile(out error)) throw new InvalidOperationException(error);
                instance.AddComponent<DeadAirCharacterMotion>().ClassId = manifest.characterClass;
                wardrobe.Apply(-1, manifest.characterClass);
                EnsureAssetFolder(Path.GetDirectoryName(manifest.prefabPath).Replace('\\', '/'));
                if (PrefabUtility.SaveAsPrefabAsset(instance, manifest.prefabPath) == null)
                    throw new InvalidOperationException("The V2 prefab could not be saved.");
                AssetDatabase.SaveAssets();
                Debug.Log("V2 imported " + manifest.characterId + " at " + manifest.prefabPath +
                    ". Generic rig, blendshapes and complete source meshes retained; Resources/gameplay wrappers unchanged.");
                return manifest.prefabPath;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        public static bool IsV2Path(string path)
            => !string.IsNullOrEmpty(path) && path.StartsWith(AssetRoot, StringComparison.Ordinal) &&
                !path.Contains("\\") && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

        public static void ValidateManifest(Manifest manifest)
        {
            if (manifest == null || DeadAirCharacterRigBindings.RoleFor(manifest.characterId) != manifest.characterClass ||
                DeadAirCharacterRigBindings.RoleFor(manifest.characterId) == null || string.IsNullOrWhiteSpace(manifest.rigVersion))
                throw new InvalidOperationException("The manifest needs a matching character, role and rig version.");
            string characterRoot = AssetRoot + manifest.characterId + "/";
            if (!IsV2Path(manifest.modelPath) || !manifest.modelPath.StartsWith(characterRoot + "Models/", StringComparison.Ordinal) ||
                !manifest.modelPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                manifest.prefabPath != characterRoot + "Prefabs/CHR_" + manifest.characterId + ".prefab")
                throw new InvalidOperationException("V2 models and prefab output must stay inside this character's isolated V2 folders.");
            if (manifest.rigBindings == null || manifest.rigBindings.Length == 0 || manifest.bodyRendererPaths == null ||
                manifest.bodyRendererPaths.Length == 0 || manifest.garments == null || manifest.garments.Length == 0 ||
                manifest.clips == null || manifest.clips.Length == 0 || manifest.surfaces == null || manifest.surfaces.Length == 0)
                throw new InvalidOperationException("Explicit rig, complete body, garments, materials and authored clips are required.");
            if (manifest.viewPitch == null || !IsTransformPath(manifest.viewPitch.aimPivotPath))
                throw new InvalidOperationException("Supply this character's explicit calibrated view-pitch profile and aim-pivot path.");
            var semanticIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in manifest.rigBindings)
                if (binding == null || !Enum.TryParse(binding.semantic, out DeadAirCharacterRigBindings.Joint joint) ||
                    !Enum.IsDefined(typeof(DeadAirCharacterRigBindings.Joint), joint) || !semanticIds.Add(binding.semantic) || !IsTransformPath(binding.path))
                    throw new InvalidOperationException("Rig bindings need unique named semantics and exact hierarchy paths.");
            foreach (string path in manifest.bodyRendererPaths)
                if (!IsTransformPath(path)) throw new InvalidOperationException("Invalid complete body renderer path.");
            foreach (var garment in manifest.garments)
                if (garment == null || string.IsNullOrWhiteSpace(garment.id) || garment.mask <= 0 ||
                    (garment.mask & ~DeadAirClothing.All) != 0 || garment.rendererPaths == null || garment.rendererPaths.Length == 0 ||
                    garment.rendererPaths.Any(path => !IsTransformPath(path)))
                    throw new InvalidOperationException("Every garment needs its own explicit renderers and valid mask.");
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in manifest.clips)
                if (clip == null || string.IsNullOrWhiteSpace(clip.action) || !actions.Add(clip.action) || string.IsNullOrEmpty(clip.name) ||
                    !clip.name.StartsWith("AN_" + manifest.characterId + "_" + clip.action + "_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Each action needs its own exact character-specific clip name.");
            foreach (string action in DeadAirCharacterAnimator.CoreActions)
                if (!actions.Contains(action)) throw new InvalidOperationException("Missing required authored action " + action + ".");
            var surfacePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var surface in manifest.surfaces)
                if (surface == null || !IsTransformPath(surface.rendererPath) || surface.materialPaths == null || surface.materialPaths.Length == 0 ||
                    !surfacePaths.Add(surface.rendererPath) ||
                    surface.materialPaths.Any(path => !IsV2Path(path) || !path.StartsWith(characterRoot + "Materials/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Material bindings must use explicit assets belonging to this V2 character.");
        }

        public static void ConfigureModelImporter(ModelImporter importer)
        {
            if (importer == null || !IsV2Path(importer.assetPath))
                throw new InvalidOperationException("V2 settings cannot be applied to a legacy/outside model.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.optimizeGameObjects = false;
            importer.importBlendShapes = true;
            importer.importAnimation = true;
            importer.importCameras = false; importer.importLights = false;
            importer.globalScale = 1; importer.useFileScale = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            // Preserve export vertex/index ordering for the source/Unity geometry audit.
            importer.optimizeMeshPolygons = false; importer.optimizeMeshVertices = false;
            importer.weldVertices = false; importer.isReadable = true;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            // Import every authored weight first. Validation rejects >4; silently truncating could conceal a bad export.
            importer.maxBonesPerVertex = 255; importer.minBoneWeight = 0;
            importer.resampleCurves = false;
        }

        static void ConfigureClips(ModelImporter importer, Clip[] clips)
        {
            var takes = importer.defaultClipAnimations;
            importer.clipAnimations = clips.Select(binding => {
                var matches = takes.Where(take => take.name == binding.name || take.takeName == binding.name).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException("Expected one exact exported take '" + binding.name + "'; found " + matches.Length + ".");
                var take = matches[0]; take.name = binding.name; take.loopTime = binding.loop; take.loopPose = false;
                take.lockRootHeightY = true; take.lockRootPositionXZ = true; take.lockRootRotation = true;
                return take;
            }).ToArray();
        }

        public static void ValidateImportedMeshes(GameObject model)
        {
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("No authored skinned meshes were imported.");
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 || skin.bones.Length == 0 || mesh.bindposes.Length != skin.bones.Length ||
                    skin.rootBone == null || !skin.rootBone.IsChildOf(model.transform))
                    throw new InvalidOperationException("Invalid skinned mesh/bind matrices: " + skin.name);
                if (skin.bones.Any(bone => bone == null || !bone.IsChildOf(model.transform)))
                    throw new InvalidOperationException("Foreign/missing bone in " + skin.name);
                using (var counts = mesh.GetBonesPerVertex())
                using (var weights = mesh.GetAllBoneWeights())
                {
                    if (counts.Length != mesh.vertexCount) throw new InvalidOperationException("Incomplete skin weights in " + skin.name);
                    int index = 0;
                    for (int v = 0; v < counts.Length; v++)
                    {
                        if (counts[v] < 1 || counts[v] > 4)
                            throw new InvalidOperationException("Every vertex requires 1–4 authored weights: " + skin.name + " vertex " + v);
                        float sum = 0;
                        for (int w = 0; w < counts[v]; w++)
                        {
                            var weight = weights[index++];
                            if (weight.boneIndex < 0 || weight.boneIndex >= skin.bones.Length || float.IsNaN(weight.weight) ||
                                float.IsInfinity(weight.weight) || weight.weight <= 0)
                                throw new InvalidOperationException("Invalid bone weight in " + skin.name);
                            sum += weight.weight;
                        }
                        if (Mathf.Abs(sum - 1) > .0001f) throw new InvalidOperationException("Unnormalized weights in " + skin.name);
                    }
                }
            }
            foreach (var node in model.GetComponentsInChildren<Transform>(true))
            {
                if (node.name.StartsWith("CTRL_", StringComparison.Ordinal) || node.name.StartsWith("MCH_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Authoring controllers must stay in Blender: " + node.name);
                if ((node.localScale - Vector3.one).sqrMagnitude > 1e-8f)
                    throw new InvalidOperationException("Apply authored unit scale before export: " + node.name);
            }
        }

        static void ValidateClipCurves(Transform root, Manifest manifest, AnimationClip[] clips)
        {
            string rootBonePath = manifest.rigBindings.Single(binding => binding.semantic == "Root").path;
            foreach (var record in manifest.clips)
            {
                var clip = clips.Single(item => item.name == record.name);
                if (AnimationUtility.GetAnimationEvents(clip).Length != 0)
                    throw new InvalidOperationException("V2 sampled clips must not carry gameplay animation events: " + clip.name);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform)) continue;
                    if (binding.path.Length != 0) ExactTransform(root, binding.path);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (binding.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal) &&
                        curve.keys.Any(key => Mathf.Abs(key.value - 1) > .0001f))
                        throw new InvalidOperationException("Animated bone scale is not allowed: " + clip.name + "/" + binding.path);
                    if ((binding.path == "" || binding.path == rootBonePath) && curve.keys.Length > 1 &&
                        curve.keys.Any(key => Mathf.Abs(key.value - curve.keys[0].value) > .0001f ||
                            Mathf.Abs(key.inTangent) > .0001f || Mathf.Abs(key.outTangent) > .0001f))
                        throw new InvalidOperationException("The clip's root must stay in place: " + clip.name);
                }
            }
        }

        static void AssignMaterials(Transform root, Surface[] surfaces)
        {
            var assigned = new HashSet<Renderer>();
            foreach (var surface in surfaces)
            {
                var renderer = ExactRenderer(root, surface.rendererPath);
                assigned.Add(renderer);
                if (renderer.sharedMaterials.Length != surface.materialPaths.Length)
                    throw new InvalidOperationException("Material slot count mismatch: " + surface.rendererPath);
                renderer.sharedMaterials = surface.materialPaths.Select(path => {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) throw new InvalidOperationException("Missing authored material: " + path);
                    if (!material.HasProperty("_DeadAirOwnerCamera"))
                        throw new InvalidOperationException("V2 materials must support owner camera visibility without editing meshes: " + path);
                    return material;
                }).ToArray();
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (!assigned.Contains(renderer))
                    throw new InvalidOperationException("Every V2 renderer needs an explicit authored material assignment: " + renderer.name);
        }

        static Light CreateFlashlight(Transform root, string socketPath)
        {
            if (string.IsNullOrEmpty(socketPath)) return null;
            var socket = ExactTransform(root, socketPath);
            var light = new GameObject("V2 flashlight emitter").AddComponent<Light>();
            light.transform.SetParent(socket, false);
            light.type = LightType.Spot; light.range = 24; light.spotAngle = 55.385f; light.innerSpotAngle = 17;
            light.color = new Color32(255, 239, 211, 255); light.intensity = 1.45f;
            light.shadows = LightShadows.Soft; light.shadowStrength = .78f;
            light.shadowBias = .012f; light.shadowNormalBias = .04f; light.enabled = false;
            return light;
        }

        static bool IsTransformPath(string path) => !string.IsNullOrEmpty(path) && !path.Contains("\\") &&
            path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

        public static Transform ExactTransform(Transform root, string path)
        {
            if (!IsTransformPath(path)) throw new InvalidOperationException("Invalid explicit transform path: " + path);
            Transform current = root;
            foreach (string segment in path.Split('/'))
            {
                Transform found = null;
                foreach (Transform child in current)
                    if (child.name == segment)
                    {
                        if (found != null) throw new InvalidOperationException("Ambiguous sibling name in explicit path: " + path);
                        found = child;
                    }
                if (found == null) throw new InvalidOperationException("Missing exact transform path: " + path);
                current = found;
            }
            return current;
        }

        static Renderer ExactRenderer(Transform root, string path)
        {
            var matches = ExactTransform(root, path).GetComponents<Renderer>();
            if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one renderer at " + path);
            return matches[0];
        }

        static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
