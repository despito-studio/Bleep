using System;
using System.IO;
using UnityEditor;

public static class PresentationBuildRun
{
    public static string Main()
    {
        return Schedule(DeadAir.Editor.DeadAirPresentationValidation.ValidateAndBuild,
            "clean GPU frame, parked cars, volumetric webs, fullscreen scares/audio, horror rules, hinges, inventory, procedural WAV export and Windows build");
    }

    public static string RebuildScareFraming()
    {
        return Schedule(() =>
        {
            DeadAir.Editor.DeadAirScareValidation.Validate();
            DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        }, "refined fullscreen scare framing/audio and Windows build");
    }

    static string Schedule(Action work, string summary)
    {
        File.WriteAllText("Logs/presentation-build-result.txt", "RUNNING");
        // The live Pipeline request returns before Unity's synchronous build.
        // The completion file records the real result instead of a transport timeout.
        EditorApplication.delayCall += () =>
        {
            try
            {
                work();
                File.WriteAllText("Logs/presentation-build-result.txt", "PASSED: " + summary + ".");
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                File.WriteAllText("Logs/presentation-build-result.txt", "FAILED: " + error);
            }
        };
        return "Presentation validation and build queued; completion: Logs/presentation-build-result.txt";
    }
}
