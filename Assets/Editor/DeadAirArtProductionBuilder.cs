using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirArtProductionBuilder
    {
        const string Root = "Assets/Resources/ArtProduction";
        const string Sources = "Assets/ArtProduction";
        [Serializable] sealed class ModelManifest { public ModelRecord[] models; }
        [Serializable] sealed class SurfaceManifest { public SurfaceRecord[] assets; }
        [Serializable] sealed class SurfaceRecord { public string key; public SurfaceMaps maps; }
        [Serializable] sealed class SurfaceMaps { public string albedo, normal, mask; }
        [Serializable] sealed class ModelRecord
        {
            public string key, asset, detail, distance;
            public MapRecord[] materials;
            public int triangles, distanceTriangles, sourceParts;
        }
        [Serializable] sealed class MapRecord
        {
            public string name, albedo, normal, mask, emission, alphaMode;
            public float alphaCutoff;
            public float metallicFactor = 1, roughnessFactor = 1;
            public float transmission;
            public float[] baseColorFactor;
            public bool doubleSided;
        }
        static readonly string[] SurfaceKeys = { "plaster", "painted-wall", "wood", "parquet", "steel", "painted-metal", "concrete", "asphalt", "fabric", "brick", "foliage", "soil", "leather", "wood-panel", "snow-asphalt", "snow", "bark", "roof-tile", "floor-tile" };
        static readonly string[] LargeSurfaces = { "plaster", "painted-wall", "parquet", "concrete", "asphalt", "brick", "wood-panel", "snow-asphalt", "snow", "roof-tile", "floor-tile" };
        static readonly string[] SmallModels = { "wall-clock", "kettle", "fire-alarm", "desk-lamp", "security-camera", "wall-lamp" };
        static readonly string[] LargeModels = { "sofa", "armchair", "bookshelf", "steel-shelves", "wooden-cabinet", "barrel", "military-crate", "wooden-crate", "generator", "pine-tree", "fir-tree", "shrub" };
        static readonly string[] AuthoringOnlyModels = { "armchair", "barrel", "bookshelf", "cardboard-box", "coffee-table", "fire-alarm", "kettle", "military-crate", "searchlight", "security-camera", "steel-shelves", "video-camera", "wall-lamp", "wooden-cabinet", "wooden-chair" };

        [MenuItem("Dead Air/Art Production/Apply Texture Budgets and Build Windows")]
        public static void ApplyTextureBudgetsAndBuildWindows()
        {
            Directory.CreateDirectory(Sources + "/UnusedModels");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string key in AuthoringOnlyModels)
            {
                string source = Root + "/Models/" + key + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null) continue;
                string error = AssetDatabase.MoveAsset(source, Sources + "/UnusedModels/" + key + ".prefab");
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            ConfigureTextureImports();
            AssetDatabase.SaveAssets();
            Debug.Log("ART TEXTURE BUDGETS APPLIED: small fixtures 256px; props 512px with 256px data maps; large tiled albedo at most 1024px, data maps at most 512px. Fifteen unused model libraries moved outside Resources. No review or validation entry point invoked.");
            DeadAirEditorSetup.BuildWindows();
        }

        [MenuItem("Dead Air/Art Production/Import Licensed Materials and Models")]
        public static void Import()
        {
            DeadAirScannedModels.ClearCaches();
            DeadAirArtMaterials.ClearCaches();
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Models");
            Directory.CreateDirectory(Sources + "/Meshes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImports();
            var surfaceManifest = JsonUtility.FromJson<SurfaceManifest>(File.ReadAllText(Sources + "/surface-manifest.json"));
            foreach (string key in SurfaceKeys)
            {
                var maps = surfaceManifest.assets.Single(record => record.key == key).maps;
                var material = MaterialAsset(Root + "/Materials/" + key + ".mat");
                material.name = "Production " + key;
                material.color = Color.white;
                Assign(material, "_MainTex", maps.albedo);
                Assign(material, "_BumpMap", maps.normal);
                Assign(material, "_MaskMap", maps.mask);
                ConfigurePbr(material);
                EditorUtility.SetDirty(material);
            }
            Uniform("paper", .08f, 0);
            Uniform("ceramic", .64f, 0);
            Uniform("rubber", .12f, 0);
            Uniform("glass", .94f, 0);
            Uniform("brass", .55f, 1);
            Uniform("duct-zinc", .58f, 1);
            var manifest = JsonUtility.FromJson<ModelManifest>(File.ReadAllText(Sources + "/model-manifest.json"));
            foreach (var record in manifest.models) ImportModel(record);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("ART PRODUCTION: imported " + SurfaceKeys.Length + " scanned PBR surface sets, 6 uniform physical materials, " + manifest.models.Length + " scanned models with near/distance LODs.");
        }

        [MenuItem("Dead Air/Art Production/Rebuild and Validate Worlds")]
        public static void Rebuild()
        {
            Import();
            DeadAirOfficeBuilder.Build();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Office/Materials" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && material.shader.name == "Dead Air/Window Glass")
                {
                    DeadAirArtMaterials.Apply(material, "glass");
                    EditorUtility.SetDirty(material);
                }
            }
            AssetDatabase.SaveAssets();
            DeadAirEditorSetup.CreateScene();
            ValidateWorlds();
        }

        [MenuItem("Dead Air/Art Production/Validate Saved Worlds")]
        public static void ValidateWorlds()
        {
            Validate();
            DeadAirSchoolArtValidation.Run();
            DeadAirSchoolCollisionValidation.Run();
            DeadAirMovementValidation.Validate();
            DeadAirProductionReview.Validate();
            DeadAirOfficeBuilder.ValidateDoorsAndGlass();
            DeadAirLightingValidation.Validate();
            ValidatePhysicalWorld();
            Debug.Log("ART PRODUCTION: saved worlds rebuilt and core validations passed.");
        }

        public static void ValidatePhysicalWorld()
        {
            // Route checks consume real saved colliders after the isolated review scenes close.
            if (Application.isBatchMode)
                EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity", OpenSceneMode.Single);
            Physics.SyncTransforms();
            DeadAirNavigationValidation.Validate();
            DeadAirBasementValidation.Validate();
            DeadAirBreakerValidation.Validate();
            DeadAirEditorSetup.ValidateInventory();
            DeadAirEditorSetup.ValidateLan();
            Debug.Log("ART PRODUCTION: saved-world traversal, basement, breaker, inventory and LAN validations passed.");
        }

        public static void Validate()
        {
            var failures = new List<string>();
            foreach (var key in SurfaceKeys)
            {
                var material = Resources.Load<Material>("ArtProduction/Materials/" + key);
                if (material == null || material.GetTexture("_BumpMap") == null || material.GetTexture("_MaskMap") == null)
                    failures.Add("PBR channels missing: " + key);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var lod = prefab.GetComponent<LODGroup>();
                if (lod == null || lod.lodCount != 2) failures.Add("Two LODs missing: " + path);
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || !filter.sharedMesh.HasVertexAttribute(VertexAttribute.Tangent)) failures.Add("Mesh/tangents missing: " + path);
                    if ((filter.transform.localScale - Vector3.one).sqrMagnitude > .00001f) failures.Add("Scaled source part: " + path);
                }
            }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log("ART PRODUCTION: material channels, model LODs, tangent bases and unit-scale parts validated.");
        }

        static void ConfigureTextureImports()
        {
            foreach (string path in Directory.GetFiles(Sources, "*", SearchOption.AllDirectories).Concat(
                Directory.Exists(Root + "/Textures") ? Directory.GetFiles(Root + "/Textures", "*", SearchOption.AllDirectories) : Array.Empty<string>())
                .Where(p => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant())))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                string filename = Path.GetFileName(path);
                bool normal = filename.Contains("_nor_gl_");
                bool mask = filename.Contains("grime") || filename.Contains("_arm_") || filename.Contains("_rough_") ||
                    filename.Contains("_roughness_") || filename.Contains("_metallic_") || filename.Contains("_spec_");
                int maximum = TextureBudget(path, normal || mask || filename.Contains("_emit_") || filename.Contains("_emission_"));
                bool unchanged = importer.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default)
                    && importer.sRGBTexture == (!normal && !mask) && importer.mipmapEnabled && importer.streamingMipmaps
                    && importer.maxTextureSize == maximum && importer.textureCompression == TextureImporterCompression.CompressedHQ
                    && importer.npotScale == TextureImporterNPOTScale.None
                    && importer.filterMode == FilterMode.Trilinear && importer.anisoLevel == (path.Contains("Textures") ? 8 : 4)
                    && !importer.isReadable;
                if (unchanged) continue;
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !mask;
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                importer.streamingMipmapsPriority = 0;
                importer.maxTextureSize = maximum;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = path.Contains("Textures") ? 8 : 4;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.isReadable = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.SaveAndReimport();
            }
        }

        static int TextureBudget(string path, bool dataMap)
        {
            path = path.Replace('\\', '/');
            if (path.Contains("window-grime")) return 512;
            const string surfacePrefix = Sources + "/Textures/";
            if (path.StartsWith(surfacePrefix, StringComparison.Ordinal))
            {
                string key = path.Substring(surfacePrefix.Length).Split('/')[0];
                return LargeSurfaces.Contains(key) && !dataMap ? 1024 : 512;
            }
            const string modelPrefix = Sources + "/SourceModels/";
            if (path.StartsWith(modelPrefix, StringComparison.Ordinal))
            {
                string key = path.Substring(modelPrefix.Length).Split('/')[0];
                if (SmallModels.Contains(key)) return 256;
                return LargeModels.Contains(key) || !dataMap ? 512 : 256;
            }
            return path.StartsWith(Sources + "/Graphics/", StringComparison.Ordinal) ? 1024 : 512;
        }

        static void ImportModel(ModelRecord record)
        {
            var materialMap = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in record.materials)
            {
                string path = Sources + "/SourceModels/" + record.key + "/" + Safe(source.name) + ".mat";
                var material = MaterialAsset(path);
                material.name = source.name;
                material.color = source.baseColorFactor != null && source.baseColorFactor.Length == 4
                    ? new Color(source.baseColorFactor[0], source.baseColorFactor[1], source.baseColorFactor[2], source.baseColorFactor[3]) : Color.white;
                if (source.transmission > 0)
                {
                    var tint = material.color;
                    tint.a *= Mathf.Lerp(1, .18f, source.transmission);
                    material.color = tint;
                }
                Assign(material, "_MainTex", source.albedo);
                Assign(material, "_BumpMap", source.normal);
                Assign(material, "_MaskMap", source.mask);
                ConfigurePbr(material);
                material.SetFloat("_MetallicMapStrength", source.metallicFactor);
                material.SetFloat("_RoughnessFactor", source.roughnessFactor);
                if (source.mask != null && !source.mask.Contains("_arm_")) material.SetFloat("_OcclusionStrength", 0);
                material.SetFloat("_Cull", source.doubleSided ? 0 : 2);
                material.SetFloat("_Cutoff", source.alphaMode == "MASK" ? source.alphaCutoff : 0);
                if (source.alphaMode == "BLEND")
                {
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0);
                    material.renderQueue = 3000;
                }
                if (!string.IsNullOrEmpty(source.emission))
                {
                    Assign(material, "_EmissionMap", source.emission);
                    // Practical fixtures are powered by game state; source emission is not permanently lit.
                    material.SetColor("_EmissionColor", Color.black);
                }
                EditorUtility.SetDirty(material);
                materialMap[source.name] = material;
            }
            var root = new GameObject(record.key);
            try
            {
                Renderer[][] renderers = new Renderer[2][];
                for (int level = 0; level < 2; level++)
                {
                    string path = level == 0 ? record.detail : record.distance;
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null) throw new InvalidOperationException("Model import missing: " + path);
                    importer.globalScale = 1;
                    importer.useFileScale = true;
                    importer.importAnimation = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.isReadable = true;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.meshCompression = ModelImporterMeshCompression.Off;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importer.SaveAndReimport();
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var group = new GameObject("LOD" + level);
                    group.transform.SetParent(root.transform, false);
                    foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var part = new GameObject(filter.name);
                        part.transform.SetParent(group.transform, false);
                        var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                        // The FBX root can carry file-unit and axis conversion. Bake it too.
                        Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                        var vertices = mesh.vertices;
                        for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                        mesh.vertices = vertices;
                        var normals = mesh.normals;
                        var normalMatrix = matrix.inverse.transpose;
                        for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                        mesh.normals = normals;
                        mesh.RecalculateTangents();
                        mesh.RecalculateBounds();
                        mesh.name = record.key + "-LOD" + level + "-" + Safe(filter.name);
                        string meshPath = Sources + "/Meshes/" + mesh.name + ".asset";
                        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
                        else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
                        part.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = part.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = filter.GetComponent<Renderer>().sharedMaterials.Select(m =>
                        {
                            Material replacement;
                            if (m != null && materialMap.TryGetValue(m.name, out replacement)) return replacement;
                            if (m != null && materialMap.TryGetValue(m.name.Replace(" (Instance)", ""), out replacement)) return replacement;
                            return materialMap.Values.First();
                        }).ToArray();
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                        if (level == 0 && !new[] { "potted-plant", "pine-tree", "fir-tree", "shrub" }.Contains(record.key))
                        {
                            var collider = part.AddComponent<MeshCollider>();
                            collider.sharedMesh = mesh;
                        }
                    }
                    renderers[level] = group.GetComponentsInChildren<Renderer>();
                }
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.12f, renderers[0]), new LOD(.015f, renderers[1]) });
                lod.fadeMode = LODFadeMode.None;
                lod.RecalculateBounds();
                string destination = AuthoringOnlyModels.Contains(record.key) ? Sources + "/UnusedModels" : Root + "/Models";
                Directory.CreateDirectory(destination);
                PrefabUtility.SaveAsPrefabAsset(root, destination + "/" + record.key + ".prefab");
                Debug.Log("ART MODEL IMPORT: " + record.key + ", source parts=" + record.sourceParts +
                    ", triangles=" + record.triangles + "/" + record.distanceTriangles + ", diameter=" + lod.size.ToString("F3"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Uniform(string key, float smoothness, float metallic)
        {
            var material = MaterialAsset(Root + "/Materials/" + key + ".mat");
            material.color = Color.white;
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_MaskMapEnabled", 0);
            EditorUtility.SetDirty(material);
        }
        static void ConfigurePbr(Material material)
        {
            material.SetFloat("_Glossiness", 1);
            material.SetFloat("_MaskMapEnabled", material.GetTexture("_MaskMap") != null ? 1 : 0);
            material.SetFloat("_BumpScale", .8f);
            material.SetFloat("_OcclusionStrength", .85f);
            material.SetFloat("_MetallicMapStrength", 1);
            material.enableInstancing = true;
        }
        static Material MaterialAsset(string path)
        {
            var shader = Shader.Find("Dead Air/Source Surface");
            if (shader == null) throw new InvalidOperationException("Production surface shader missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            return material;
        }
        static void Assign(Material material, string property, string path)
        {
            material.SetTexture(property, string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace('\\', '/')));
        }
        static string Safe(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
    }
}
