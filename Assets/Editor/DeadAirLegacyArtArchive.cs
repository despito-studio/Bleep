using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirLegacyArtArchive
    {
        [Serializable] sealed class Report { public int examined, archived; public string[] paths; }

        [MenuItem("Dead Air/Art Production/Archive Unreferenced Web Export Resources")]
        public static void Run()
        {
            var all = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
            var candidates = all.Where(p =>
                (p.StartsWith("Assets/Resources/World/Meshes/geometry_", StringComparison.Ordinal) && p.EndsWith(".asset", StringComparison.Ordinal)) ||
                (p.StartsWith("Assets/Resources/World/Materials/surface_", StringComparison.Ordinal) && p.EndsWith(".mat", StringComparison.Ordinal))).ToArray();
            var candidateSet = candidates.ToHashSet(StringComparer.Ordinal);
            var roots = all.Where(p => !candidateSet.Contains(p) && !p.StartsWith("Assets/ArtProduction/LegacyArchive/", StringComparison.Ordinal)
                && new[] { ".prefab", ".unity", ".asset", ".mat", ".controller", ".overrideController", ".playable" }.Contains(Path.GetExtension(p))).ToArray();
            var referenced = AssetDatabase.GetDependencies(roots, true).ToHashSet(StringComparer.Ordinal);
            var unused = candidates.Where(p => !referenced.Contains(p)).ToArray();
            Directory.CreateDirectory("Assets/ArtProduction/LegacyArchive/Meshes");
            Directory.CreateDirectory("Assets/ArtProduction/LegacyArchive/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var source in unused)
                {
                    string target = source.Replace("Assets/Resources/World/", "Assets/ArtProduction/LegacyArchive/");
                    string error = AssetDatabase.MoveAsset(source, target);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("Cannot archive legacy resource " + source + ": " + error);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("outputs/art-overhaul-20261019");
            File.WriteAllText("outputs/art-overhaul-20261019/legacy-resource-archive.json", JsonUtility.ToJson(new Report
                { examined = candidates.Length, archived = unused.Length, paths = unused }, true));
            Debug.Log("ART PRODUCTION: " + unused.Length + " unreferenced legacy resources moved outside Resources; source files and GUIDs retained.");
        }
    }
}
