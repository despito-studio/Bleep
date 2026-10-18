using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirBlackpineIntegrationValidation
    {
        public static void ValidateAndBuild()
        {
            ValidateAll();
            DeadAirEditorSetup.BuildWindows();
        }

        public static void ValidateAll()
        {
            try
            {
                DeadAirBlackpineValidation.Validate();
                WinterValidation.Validate();
                foreach (var id in new[] { "tape-1", "tape-2", "tape-3-948", "tape-3-964", "tape-3-1012",
                    "radio-m-0", "radio-m-1", "radio-m-2", "radio-m-3", "radio-f-0", "radio-f-1", "radio-f-2", "radio-f-3" })
                {
                    var clip = Resources.Load<AudioClip>("Audio/Blackpine/"+id);
                    if (clip == null || clip.length < .25f) throw new InvalidOperationException("Missing Ukrainian dialogue: "+id);
                }
                if (DeadAirSession.NetworkProtocol != 5) throw new InvalidOperationException("Network protocol must reject pre-investigation clients.");
                Debug.Log("BLACKPINE INTEGRATION PASSED: gameplay, winter, Ukrainian dialogue assets, protocol.");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/blackpine-integration-result.txt","PASS: gameplay, winter, Ukrainian dialogue, protocol\n");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/blackpine-integration-result.txt",exception.ToString());
                EditorApplication.Exit(1);
                throw;
            }
        }
    }
}
