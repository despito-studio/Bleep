using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    // Exports the actual game instruments for the standalone listening website.
    public static class DeadAirAudioLibraryExport
    {
        [MenuItem("Dead Air/Export Audio Listening Library")]
        public static void Export()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../AudioLibrarySite/dist/audio"));
            var go = new GameObject("Audio library export");
            try
            {
                var audio = go.AddComponent<DeadAirAudio>();
                audio.Initialize();
                audio.ValidateInstruments();
                int count = audio.ExportLibrary(folder);
                Debug.Log("DEAD AIR LISTENING LIBRARY: exported " + count + " original clips to " + folder);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
