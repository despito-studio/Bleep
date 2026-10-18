using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DeadAir.Editor
{
    public static class DeadAirReleaseBuild
    {
        public static void BuildFixedWindows()
        {
            PrepareFixedScene();
            Progress("Building Windows " + DeadAirVersion.Current);
            DeadAirEditorSetup.BuildWindows();
            Progress("Windows build complete; GPU runtime verification pending");
        }

        // A separate preparation run releases the generated/editor validation
        // objects before BuildPlayer on machines with limited available memory.
        public static void PrepareFixedScene()
        {
            Directory.CreateDirectory("Logs");
            Progress("Rebuilding reference graphics, soft lighting and stair/basement geometry");
            PlayerSettings.bundleVersion = DeadAirVersion.Current;
            DeadAirOfficeBuilder.Build();
            DeadAirEditorSetup.CreateScene();
            Progress("Validating canisters, equipment, supplies and objectives");
            DeadAirCharacterRulesValidation.Validate();
            DeadAirSuppliesValidation.Validate();
            DeadAirMushroomValidation.Validate();
            DeadAirBreakerValidation.Validate();
            DeadAirBasementValidation.Validate();
            DeadAirNavigationValidation.Validate();
            DeadAirV175Validation.Validate();
            DeadAirV176LayoutValidation.Validate();
            DeadAirLightingValidation.Validate();
            DeadAirV176PresentationValidation.Validate();
            DeadAirOfficeBuilder.ValidateDoorsAndGlass();
            DeadAirEditorSetup.ValidateMigration();
            DeadAirEditorSetup.ValidateInventory();
            DeadAirEditorSetup.ValidateLan();
            Progress("Prepared scene and saved assets passed all release checks");
        }

        static void Progress(string message)
        {
            File.WriteAllText("Logs/fixed-release-progress.txt", message + "\n");
            Debug.Log("DEAD AIR FIXED RELEASE: " + message);
        }

        public static string OutputPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-buildOutput");
            return index >= 0 && index + 1 < args.Length
                ? Path.GetFullPath(args[index + 1])
                : Path.GetFullPath("Builds/Windows/DeadAir.exe");
        }

        public static void WriteManifest(string executable, BuildReport report)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(executable));
            var manifest = new BuildManifest
            {
                schemaVersion = 2,
                game = PlayerSettings.productName,
                version = PlayerSettings.bundleVersion,
                platform = "Windows x64",
                unity = Application.unityVersion,
                builtAtUtc = DateTime.UtcNow.ToString("o"),
                sourceCommit = Git("rev-parse HEAD").Trim(),
                sourceDirty = !string.IsNullOrWhiteSpace(Git("status --porcelain --untracked-files=normal -- Assets Packages ProjectSettings Tools scripts")),
                sourceRepository = "https://github.com/despito-studio/Dear-air",
                runtimeVerification = "pending",
                buildResult = report.summary.result.ToString(),
                files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(PackagedFile).OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => new BuildFile
                    {
                        path = path.Substring(directory.Length + 1).Replace('\\', '/'),
                        bytes = new FileInfo(path).Length,
                        sha256 = HashFile(path)
                    }).ToArray()
            };
            if (manifest.version != DeadAirVersion.Current || manifest.sourceCommit.Length != 40)
                throw new InvalidOperationException("Build version or source revision is missing.");
            File.WriteAllText(Path.Combine(directory, "build-info.json"), JsonUtility.ToJson(manifest, true) + "\n");
            Debug.Log("Wrote fresh build-info.json for " + manifest.version + " (" + manifest.files.Length + " files).");
        }

        static bool PackagedFile(string path)
        {
            string name = Path.GetFileName(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return name != "build-info.json" && name != "DeadAir.provenance.json"
                && !new[] { ".log", ".pdb", ".mdb", ".dmp", ".bak", ".tmp" }.Contains(extension)
                && !path.Contains("_BackUpThisFolder_ButDontShipItWithYourGame")
                && !path.Contains("BurstDebugInformation_DoNotShip");
        }

        static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static string Git(string arguments)
        {
            var info = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = Path.GetFullPath("."), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Git provenance failed: " + error);
                return output;
            }
        }

        [Serializable]
        sealed class BuildManifest
        {
            public int schemaVersion;
            public string game, version, platform, unity, builtAtUtc, sourceCommit, sourceRepository;
            public bool sourceDirty;
            public string runtimeVerification, buildResult;
            public BuildFile[] files;
        }

        [Serializable]
        sealed class BuildFile
        {
            public string path, sha256;
            public long bytes;
        }
    }
}
