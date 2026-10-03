using System.IO;
using UnityEditor;
using UnityEngine;

public static class HorrorBuildRun
{
    public static string Main()
    {
        DeadAir.Editor.DeadAirOfficeBuilder.Build();
        DeadAir.Editor.DeadAirEditorSetup.CreateScene();
        DeadAir.Editor.DeadAirDressingValidation.Validate();
        DeadAir.Editor.DeadAirMountedGeometryValidation.Validate();
        DeadAir.Editor.DeadAirMonsterPresentationValidation.Validate();
        DeadAir.Editor.DeadAirHorrorRulesValidation.Validate();
        DeadAir.Editor.DeadAirScareValidation.Validate();
        DeadAir.Editor.DeadAirLightingValidation.Validate();
        DeadAir.Editor.DeadAirNavigationValidation.Validate();
        DeadAir.Editor.DeadAirFloorRulesValidation.Validate();
        DeadAir.Editor.DeadAirOfficeVerification.ValidateHinges();
        DeadAir.Editor.DeadAirCharacterRulesValidation.Validate();
        DeadAir.Editor.DeadAirChapterValidation.Validate();
        DeadAir.Editor.DeadAirEditorSetup.ValidateMigration();
        DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        Directory.CreateDirectory("Art/Horror");
        File.WriteAllText("Art/Horror/editor-verification.txt", "PASSED: blind hearing/no live player tracking/sound memory/patrol; real cover peek/approach vanish; door wait and locked refusal; host-only spider RNG/current-sanity penalty; articulated blind monster; two physical road beams; mounted webs, floor litter, continuous scenery; collision/navigation/floors/hinges/character actions/chapters/objectives/inventory.\n" + DeadAir.Editor.DeadAirTextureAudit.Validate());
        PlayerSettings.bundleVersion = "1.4.0";
        DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        return "DEAD AIR HORROR BUILD COMPLETE";
    }
}
