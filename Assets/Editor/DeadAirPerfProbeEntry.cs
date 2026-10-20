using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadAir.Editor
{
    // Batch entry point for the performance probe:
    // Unity.exe -batchmode -projectPath . -executeMethod DeadAir.Editor.DeadAirPerfProbeEntry.Run
    //   -deadAirPerf <absolute output folder> [-deadAirPerfLabel <name>] -logFile <file>
    public static class DeadAirPerfProbeEntry
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            EditorApplication.isPlaying = true;
            Debug.Log("DEAD AIR PERF: entering play mode for the measurement pass.");
        }
    }
}
