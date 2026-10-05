using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class IndieUpgradeRun
{
    public static string Main()
    {
        return Schedule(() =>
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeadAir.Editor.DeadAirIndieValidation.Validate();
            DeadAir.Editor.DeadAirPresentationValidation.ValidateCleanScreen();
            DeadAir.Editor.DeadAirMushroomValidation.Validate();
            DeadAir.Editor.DeadAirLightingValidation.Validate();
            DeadAir.Editor.DeadAirScareValidation.Validate();
            // Cover and navigation checks need the authored office colliders.
            EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            UnityEngine.Physics.SyncTransforms();
            DeadAir.Editor.DeadAirHorrorRulesValidation.Validate();
            DeadAir.Editor.DeadAirCharacterRulesValidation.Validate();
            DeadAir.Editor.DeadAirFloorRulesValidation.Validate();
            DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        }, "validation");
    }

    public static string Build()
    {
        return Schedule(DeadAir.Editor.DeadAirEditorSetup.BuildWindows, "build");
    }

    static string Schedule(Action work, string stage)
    {
        string path = "Verification-IndieUpgrade/" + stage + "-result.txt";
        Directory.CreateDirectory("Verification-IndieUpgrade");
        File.WriteAllText(path, "RUNNING");
        EditorApplication.delayCall += () =>
        {
            try { work(); File.WriteAllText(path, "PASSED"); }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                File.WriteAllText(path, "FAILED: " + error);
            }
        };
        return stage + " queued; actual result: " + path;
    }
}
