using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    /// <summary>Imports and checks the source release without building a player.</summary>
    public static class DeadAirCharacterSourceRelease
    {
        public static void Prepare()
        {
            var results = new List<string> { "SOURCE RELEASE " + DeadAirVersion.Current, "UTC " + DateTime.UtcNow.ToString("O") };
            var failures = new List<Exception>();
            // Import must succeed before dependent checks can describe the new resources.
            DeadAirCharacterImporter.ImportAll();
            results.Add("PASS: Unity compilation and import of four replacement bodies and four hand sets.");
            PlayerSettings.bundleVersion = DeadAirVersion.Current;
            AssetDatabase.SaveAssets();
            Check("Imported cast and first person regressions", DeadAirIndieCharacterValidation.ValidateAll, results, failures);
            Check("Runtime feet, terrain and transitions", DeadAirIndieCharacterValidation.RunRuntimeContracts, results, failures);
            results.Add("No player build was requested or produced by this preparation.");
            Directory.CreateDirectory("Art/Characters/IndieCast/Unity");
            File.WriteAllLines("Art/Characters/IndieCast/Unity/source-release-checks.txt", results);
            if (failures.Count > 0) throw new AggregateException("Source release validation has unresolved checks", failures);
            Debug.Log("DEAD AIR CHARACTER SOURCE RELEASE CHECKS PASSED");
        }

        static void Check(string name, Action action, List<string> results, List<Exception> failures)
        {
            try { action(); results.Add("PASS: " + name); }
            catch (Exception error)
            {
                results.Add("FAIL: " + name + ": " + error.Message);
                failures.Add(error);
                Debug.LogException(error);
            }
        }
    }
}
