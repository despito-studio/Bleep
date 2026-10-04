using System;
using System.IO;
using UnityEditor;

public static class UserCharacterImportRun
{
    const string Result = "Logs/imported-characters-result.txt";

    public static string Main()
    {
        return Schedule(() =>
        {
            DeadAir.Editor.DeadAirCharacterImporter.ImportAll();
            DeadAir.Editor.DeadAirImportedCharacterValidation.Validate();
            DeadAir.Editor.DeadAirCharacterRulesValidation.Validate();
            RenderPreviews();
        }, "Four supplied character variants imported; source atlases, skeletons, four actions, all equipment hands and original character rules verified; GPU previews rendered.");
    }

    public static string Previews()
    {
        return Schedule(RenderPreviews, "Imported cast and equipment hands: GPU previews and measured mesh contacts passed.");
    }

    static void RenderPreviews()
    {
        DeadAir.Editor.DeadAirCharacterPreview.RenderLineup("Verification-ImportedCharacters/lineup.png");
        DeadAir.Editor.DeadAirFirstPersonPreview.RenderAll("Verification-ImportedCharacters/FirstPerson");
        DeadAir.Editor.DeadAirThirdPersonGripPreview.RenderAll("Verification-ImportedCharacters/ThirdPerson");
    }

    public static string Build()
    {
        return Schedule(() =>
        {
            DeadAir.Editor.DeadAirImportedCharacterValidation.Validate();
            DeadAir.Editor.DeadAirCharacterPreview.RenderLineup("Verification-ImportedCharacters/final-lineup.png");
            DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        }, "Imported cast and equipment hands verified; Windows build succeeded.");
    }

    static string Schedule(Action action, string summary)
    {
        File.WriteAllText(Result, "RUNNING");
        EditorApplication.delayCall += () =>
        {
            try
            {
                action();
                Directory.CreateDirectory("Verification-ImportedCharacters");
                string message = "PASSED: " + summary;
                File.WriteAllText(Result, message);
                File.AppendAllText("Verification-ImportedCharacters/editor-report.txt", message + Environment.NewLine);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                File.WriteAllText(Result, "FAILED: " + error);
            }
        };
        return "Character work queued; completion recorded in " + Result;
    }
}
