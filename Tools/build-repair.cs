using System.IO;
using UnityEditor;
using UnityEngine;

public static class RepairBuildRun
{
    public static string Main()
    {
        DeadAir.Editor.DeadAirOfficeBuilder.Build();
        DeadAir.Editor.DeadAirEditorSetup.CreateScene();
        DeadAir.Editor.DeadAirMovementValidation.Validate();
        DeadAir.Editor.DeadAirNavigationValidation.Validate();
        DeadAir.Editor.DeadAirFloorRulesValidation.Validate();
        DeadAir.Editor.DeadAirOfficeVerification.ValidateHinges();
        DeadAir.Editor.DeadAirCharacterRulesValidation.Validate();
        DeadAir.Editor.DeadAirChapterValidation.Validate();
        DeadAir.Editor.DeadAirEditorSetup.ValidateMigration();
        DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        DeadAir.Editor.DeadAirEditorSetup.ValidateLan();
        if (PlayerSettings.fullScreenMode != FullScreenMode.FullScreenWindow || !PlayerSettings.defaultIsNativeResolution)
            throw new System.InvalidOperationException("Player must start in native-resolution borderless fullscreen.");
        Directory.CreateDirectory("Art/Repair");
        File.WriteAllText("Art/Repair/editor-verification.txt", "PASSED: generated branched office, movement/acknowledgment, physical navigation, floor rules, hinges, character actions, chapters, objective route, inventory, LAN, native fullscreen.\n" + DeadAir.Editor.DeadAirTextureAudit.Validate());
        DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        return "DEAD AIR REPAIR BUILD COMPLETE";
    }
}
