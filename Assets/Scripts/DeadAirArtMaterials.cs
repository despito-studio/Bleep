using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeadAir
{
    public static class DeadAirArtMaterials
    {
        static readonly Dictionary<string, Material> sources = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material> variants = new Dictionary<string, Material>();
        static bool streamingConfigured;
        static readonly string[] Maps = { "_MainTex", "_BumpMap", "_MaskMap", "_SurfaceMap" };
        static readonly string[] Scalars = { "_MaskMapEnabled", "_BumpScale", "_OcclusionStrength", "_MetallicMapStrength", "_RoughnessFactor", "_Metallic" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ConfigureStreaming()
        {
            if (!streamingConfigured)
            {
                DeadAirSettings.Changed += ApplyStreamingBudget;
                Application.quitting += ReleaseStreamingSubscriptions;
            }
            streamingConfigured = true;
            ApplyStreamingBudget();
        }

        static void ApplyStreamingBudget()
        {
            int quality = DeadAirSettings.GraphicsQuality;
            float budget = quality == 0 ? 128 : quality == 1 ? 192 : 256;
            if (!QualitySettings.streamingMipmapsActive) QualitySettings.streamingMipmapsActive = true;
            if (QualitySettings.streamingMipmapsMemoryBudget != budget) QualitySettings.streamingMipmapsMemoryBudget = budget;
            if (QualitySettings.streamingMipmapsMaxLevelReduction != 3) QualitySettings.streamingMipmapsMaxLevelReduction = 3;
        }

        static void ReleaseStreamingSubscriptions()
        {
            DeadAirSettings.Changed -= ApplyStreamingBudget;
            Application.quitting -= ReleaseStreamingSubscriptions;
            streamingConfigured = false;
            ClearCaches();
        }

        public static void ClearCaches()
        {
            sources.Clear();
            variants.Clear();
        }

        public static void Apply(Material material, string surfaceKey)
        {
            if (Application.isPlaying && !streamingConfigured)
            {
                ConfigureStreaming();
            }
            if (material == null || string.IsNullOrEmpty(surfaceKey)) return;
            Material source;
            if (!sources.TryGetValue(surfaceKey, out source) || source == null)
            {
                source = Resources.Load<Material>("ArtProduction/Materials/" + surfaceKey);
                if (source == null) return;
                sources[surfaceKey] = source;
            }
            foreach (var property in Maps)
                if (material.HasProperty(property) && source.HasProperty(property))
                    material.SetTexture(property, source.GetTexture(property));
            foreach (var property in Scalars)
                if (material.HasProperty(property) && source.HasProperty(property))
                    material.SetFloat(property, source.GetFloat(property));
            if (material.HasProperty("_MaskMapEnabled") && source.GetFloat("_MaskMapEnabled") > .5f)
                material.SetFloat("_Glossiness", 1f);
            material.EnableKeyword("_NORMALMAP");
            if (surfaceKey == "glass" && material.HasProperty("_DirtMap"))
                material.SetTexture("_DirtMap", Resources.Load<Texture2D>("ArtProduction/Textures/window-grime"));
            material.enableInstancing = true;
        }

        public static Material Create(string name, string surfaceKey, Color tint, float smoothness)
        {
            string id = surfaceKey + "-" + ColorUtility.ToHtmlStringRGBA(tint) + "-" + Mathf.RoundToInt(smoothness * 1000);
            Material material;
            if (variants.TryGetValue(id, out material) && material != null) return material;
#if UNITY_EDITOR
            string folder = "Assets/Resources/ArtProduction/Materials/Variants";
            string path = folder + "/" + id + ".mat";
            if (!Application.isPlaying) material = AssetDatabase.LoadAssetAtPath<Material>(path);
#endif
            if (material == null)
                material = new Material(Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard"));
            material.name = name;
            material.color = tint;
            material.SetFloat("_Glossiness", smoothness);
            Apply(material, surfaceKey);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                System.IO.Directory.CreateDirectory(folder);
                if (!AssetDatabase.Contains(material)) AssetDatabase.CreateAsset(material, path);
                else EditorUtility.SetDirty(material);
            }
#endif
            variants[id] = material;
            return material;
        }
    }
}
