using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    // Canvas labels frequently have sizes such as 128 x 96. Unity's default
    // NPOT resizing changes their aspect and averages the original pixel rows.
    public static class DeadAirTextureAudit
    {
        [Serializable] sealed class TextureExport { public TextureInfo[] textures; }
        [Serializable] sealed class TextureInfo { public int id; public bool wrap, nearest; }
        const string ExportPath = "Assets/SourceExport/scene.json";
        const string TextureFolder = "Assets/SourceExport/Textures/";

        public static bool Configure(TextureImporter importer, bool repeat, bool nearest)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            bool changed = !HasExpectedSettings(importer, repeat, nearest);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = nearest ? FilterMode.Point : FilterMode.Bilinear;
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            // Keep 3D tiled surfaces stable in the distance; preserve original
            // pixels for clamped text, decals and control-panel canvas textures.
            importer.mipmapEnabled = repeat;
            importer.anisoLevel = 0;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            return changed;
        }

        static bool HasExpectedSettings(TextureImporter importer, bool repeat, bool nearest)
        {
            return importer.textureType == TextureImporterType.Default
                && importer.sRGBTexture
                && importer.npotScale == TextureImporterNPOTScale.None
                && importer.filterMode == (nearest ? FilterMode.Point : FilterMode.Bilinear)
                && importer.wrapMode == (repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp)
                && importer.mipmapEnabled == repeat
                && importer.anisoLevel == 0
                && importer.textureCompression == TextureImporterCompression.Uncompressed
                && !importer.crunchedCompression
                && importer.alphaSource == TextureImporterAlphaSource.FromInput
                && importer.alphaIsTransparency;
        }

        [MenuItem("Dead Air/Textures/Apply Canvas Texture Policy")]
        public static void Apply()
        {
            var export = ReadExport();
            int changed = 0;
            foreach (var texture in export.textures)
            {
                var importer = AssetImporter.GetAtPath(TexturePath(texture.id)) as TextureImporter;
                if (Configure(importer, texture.wrap, texture.nearest))
                {
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            Debug.Log($"Canvas texture policy applied: {changed} importers updated. {Validate()}");
        }

        [MenuItem("Dead Air/Textures/Audit Canvas Textures")]
        public static void Audit() { Debug.Log(Validate()); }

        // Returns a deterministic report for editor/CLI validation without
        // rebuilding the source prefabs or changing the new office materials.
        public static string Validate()
        {
            var export = ReadExport();
            int nonPowerOfTwo = 0, badSettings = 0, resized = 0, missing = 0;
            var details = new StringBuilder();
            foreach (var texture in export.textures)
            {
                string path = TexturePath(texture.id);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (importer == null || asset == null)
                {
                    missing++;
                    details.AppendLine("Missing texture: " + path);
                    continue;
                }
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (!Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height)) nonPowerOfTwo++;
                if (!HasExpectedSettings(importer, texture.wrap, texture.nearest))
                {
                    badSettings++;
                    details.AppendLine("Import settings differ: " + path);
                }
                if (asset.width != width || asset.height != height)
                {
                    resized++;
                    details.AppendLine($"Resized canvas: {path}: {width} x {height} -> {asset.width} x {asset.height}");
                }
            }
            return $"Canvas texture audit: {export.textures.Length} textures, {nonPowerOfTwo} NPOT, "
                + $"{badSettings} settings mismatches, {resized} resized, {missing} missing.\n" + details;
        }

        static TextureExport ReadExport()
        {
            var export = JsonUtility.FromJson<TextureExport>(File.ReadAllText(ExportPath));
            if (export == null || export.textures == null || export.textures.Length == 0)
                throw new InvalidOperationException("The source texture export is missing or incomplete.");
            return export;
        }

        static string TexturePath(int id) { return TextureFolder + "texture_" + id + ".png"; }
    }
}
