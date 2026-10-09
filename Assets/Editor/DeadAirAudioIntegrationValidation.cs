using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirAudioIntegrationValidation
    {
        const string Output = "AudioProduction/integration/Verification";

        [MenuItem("Dead Air/Audio/Validate Imported Library")]
        public static void Validate()
        {
            var report = Audit();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/import-audit.json", JsonUtility.ToJson(report, true) + "\n");
            if (!report.passed) throw new InvalidOperationException("Audio import validation: " + string.Join("; ", report.errors));
            Debug.Log("DEAD AIR AUDIO IMPORT VALIDATED: " + report.assets.Length + " WAV assets, 8 synchronized 160 BPM stems; no injury assets imported.");
        }

        public static ImportAudit Audit()
        {
            var errors = new List<string>();
            var rows = new List<ImportRow>();
            if (!Directory.Exists(DeadAirAudioImport.Root))
                errors.Add("Imported audio library is missing.");
            else
                foreach (string source in Directory.GetFiles(DeadAirAudioImport.Root, "*.wav", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    string path = source.Replace('\\', '/');
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (clip == null || importer == null) { errors.Add("Import missing: " + path); continue; }
                    var settings = importer.defaultSampleSettings;
                    var policy = DeadAirAudioImport.Policy(path, DeadAirAudioImport.WavSeconds(path));
                    var row = new ImportRow
                    {
                        path = path, key = clip.name, seconds = clip.length, samples = clip.samples,
                        channels = clip.channels, frequency = clip.frequency,
                        loadType = clip.loadType.ToString(), compression = settings.compressionFormat.ToString(), quality = settings.quality,
                        forceToMono = importer.forceToMono, background = importer.loadInBackground, preload = settings.preloadAudioData,
                        sourceChannels = DeadAirAudioImport.WavChannels(path),
                        preserveSampleRate = settings.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate,
                        rawPcmBytes = (long)clip.samples * clip.channels * sizeof(float), sourceBytes = new FileInfo(source).Length,
                        editorAssetResidentBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip)
                    };
                    rows.Add(row);
                    Check(settings.loadType == policy.loadType && clip.loadType == policy.loadType, "Load type mismatch: " + path, errors);
                    Check(settings.compressionFormat == policy.compression, "Compression mismatch: " + path, errors);
                    Check(importer.forceToMono == policy.mono && clip.channels == (policy.mono ? 1 : row.sourceChannels), "Channels/spatial policy mismatch: " + path, errors);
                    Check(importer.loadInBackground == policy.background && settings.preloadAudioData == policy.preload, "First-play/background policy mismatch: " + path, errors);
                    Check(row.preserveSampleRate && row.frequency == 48000, "48 kHz source grid changed: " + path, errors);
                    var platform = importer.GetOverrideSampleSettings("Standalone");
                    Check(platform.loadType == policy.loadType && platform.compressionFormat == policy.compression && platform.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate && platform.preloadAudioData == policy.preload,
                        "Windows override mismatch: " + path, errors);
                    if (policy.compression == AudioCompressionFormat.Vorbis)
                        Check(Mathf.Abs(settings.quality - policy.quality) < .001f && Mathf.Abs(platform.quality - policy.quality) < .001f, "Vorbis quality mismatch: " + path, errors);
                }

            var stems = rows.Where(r => r.path.StartsWith(DeadAirAudioImport.Root + "Music/", StringComparison.Ordinal) && r.key != "music-menu").ToArray();
            Check(stems.Length == 8, "Expected exactly 8 score stems, found " + stems.Length, errors);
            foreach (string key in new[] { "exploration-bed", "exploration-pulse", "tension-bed", "tension-pulse", "pursuit-atmosphere", "pursuit-kick", "pursuit-bass-drums", "pursuit-lead" })
                Check(stems.Any(s => s.key == key), "Missing score stem: " + key, errors);
            foreach (var stem in stems)
                Check(stem.samples == 2304000 && stem.frequency == 48000 && stem.channels == 2,
                    "Stem must span 32 bars/48 seconds at 160 BPM: " + stem.key, errors);
            Check(rows.Count(r => r.key == "music-menu") == 1, "Expected one imported menu score.", errors);
            var falls = rows.Where(r => r.path.Contains("/Falls/")).ToArray();
            Check(falls.Length == 4, "Expected exactly four character falls, found " + falls.Length, errors);
            foreach (string character in new[] { "operator", "engineer", "journalist", "dispatcher" })
                Check(falls.Any(r => r.key == "fall-" + character), "Missing fall for " + character, errors);
            Check(rows.Count == 83, "Expected 83 imported WAV assets, found " + rows.Count, errors);
            Check(rows.Count(r => r.path.Contains("/Scares/")) == 3, "Expected three dedicated scare clips.", errors);
            foreach (string key in new[] { "spider", "screamer", "scare" })
                Check(rows.Any(r => r.path.Contains("/Scares/") && r.key == key), "Missing fullscreen scare: " + key, errors);
            Check(rows.Count(r => r.path.Contains("/Beds/")) == 8 && rows.Count(r => r.path.Contains("/Rooms/")) == 6, "Bed/room ambience count mismatch.", errors);
            Check(!rows.Any(r => new[] { "injury", "pain-", "wounded", "bandage", "blood-drops" }.Any(word => r.path.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)),
                "Injury/pain system assets must stay in the standalone pack.", errors);
            AudioSettings.GetDSPBufferSize(out int length, out int buffers);
            return new ImportAudit
            {
                passed = errors.Count == 0, checkedAtUtc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(), outputSampleRate = AudioSettings.outputSampleRate,
                dspBufferFrames = length, dspBuffers = buffers,
                memoryNote = "editorAssetResidentBytes is indicative Editor asset residency only. Verify device playback with the opt-in standalone audio capture.",
                assets = rows.ToArray(), errors = errors.ToArray(),
                sourcePcmBytes = rows.Sum(r => r.rawPcmBytes), editorAssetResidentBytes = rows.Sum(r => r.editorAssetResidentBytes)
            };
        }

        // Build only the existing scene. Never regenerate the scene or world as a side effect.
        [MenuItem("Dead Air/Audio/Build Windows Audio Smoke Test")]
        public static void BuildWindowsSmoke()
        {
            Validate();
            const string scene = "Assets/Scenes/Blackpine.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null) throw new FileNotFoundException("Existing verification scene missing", scene);
            string executable = Path.GetFullPath(Output + "/Windows/DeadAir.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene }, locationPathName = executable,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            File.WriteAllText(Output + "/build-audit.json", JsonUtility.ToJson(new SmokeBuild
            {
                result = result.summary.result.ToString(), errors = result.summary.totalErrors,
                warnings = result.summary.totalWarnings, output = executable,
                bytes = result.summary.totalSize, durationSeconds = result.summary.totalTime.TotalSeconds,
                scene = scene, sceneRegeneration = false
            }, true) + "\n");
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Audio smoke build failed: " + result.summary.result);
            Debug.Log("DEAD AIR AUDIO SMOKE BUILD: " + executable);
        }

        // Shipping build of the already prepared scene; audio integration never
        // invokes office/world regeneration or rewrites the player's scene.
        [MenuItem("Dead Air/Audio/Build Windows Audio Release")]
        public static void BuildWindowsRelease()
        {
            Validate();
            if (PlayerSettings.bundleVersion != DeadAirVersion.Current)
                throw new InvalidOperationException("Player version does not match the source version.");
            const string scene = "Assets/Scenes/Blackpine.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                throw new FileNotFoundException("Existing release scene missing", scene);
            string executable = Path.GetFullPath("Builds/Windows-v" + DeadAirVersion.Current + "-audio/DeadAir.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            // Prevent a failed rebuild from leaving a previous success manifest usable.
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(executable), "build-info.json"),
                "{\"schemaVersion\":2,\"version\":\"" + DeadAirVersion.Current + "\",\"buildResult\":\"Building\",\"runtimeVerification\":\"pending\"}\n");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene }, locationPathName = executable,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            File.WriteAllText(Output + "/release-build-audit.json", JsonUtility.ToJson(new SmokeBuild
            {
                result = result.summary.result.ToString(), errors = result.summary.totalErrors,
                warnings = result.summary.totalWarnings, output = executable,
                bytes = result.summary.totalSize, durationSeconds = result.summary.totalTime.TotalSeconds,
                scene = scene, sceneRegeneration = false
            }, true) + "\n");
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Audio release build failed: " + result.summary.result);
            DeadAirReleaseBuild.WriteManifest(executable, result);
            Debug.Log("DEAD AIR AUDIO RELEASE BUILD: " + executable);
        }

        static void Check(bool condition, string error, List<string> errors) { if (!condition) errors.Add(error); }

        [Serializable] public sealed class ImportAudit
        {
            public bool passed;
            public string checkedAtUtc, unity, activeBuildTarget, memoryNote;
            public int outputSampleRate, dspBufferFrames, dspBuffers;
            public long sourcePcmBytes, editorAssetResidentBytes;
            public ImportRow[] assets;
            public string[] errors;
        }
        [Serializable] public sealed class ImportRow
        {
            public string path, key, loadType, compression;
            public int samples, frequency, channels, sourceChannels;
            public float seconds, quality;
            public bool forceToMono, background, preload, preserveSampleRate;
            public long rawPcmBytes, sourceBytes, editorAssetResidentBytes;
        }
        [Serializable] sealed class SmokeBuild
        {
            public string result, output, scene;
            public int errors, warnings;
            public ulong bytes;
            public double durationSeconds;
            public bool sceneRegeneration;
        }
    }
}
