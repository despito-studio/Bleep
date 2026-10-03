using System.IO;
using UnityEditor;
using UnityEngine;

public static class LightingBuildRun
{
    public static string Main()
    {
        DeadAir.Editor.DeadAirOfficeBuilder.Build();
        DeadAir.Editor.DeadAirEditorSetup.CreateScene();
        DeadAir.Editor.DeadAirMountedGeometryValidation.Validate();
        DeadAir.Editor.DeadAirLightingValidation.Validate();
        DeadAir.Editor.DeadAirNavigationValidation.Validate();
        DeadAir.Editor.DeadAirFloorRulesValidation.Validate();
        DeadAir.Editor.DeadAirOfficeVerification.ValidateHinges();
        DeadAir.Editor.DeadAirChapterValidation.Validate();
        DeadAir.Editor.DeadAirEditorSetup.ValidateMigration();
        DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        if(PlayerSettings.fullScreenMode!=FullScreenMode.FullScreenWindow||!PlayerSettings.defaultIsNativeResolution)
            throw new System.InvalidOperationException("Windows must start at native-resolution fullscreen.");
        Directory.CreateDirectory("Art/Lighting");
        File.WriteAllText("Art/Lighting/editor-verification.txt","PASSED: 63 aligned window openings, wall-backed panels and mounted generator controls; physical practical lights, reflective materials, emergency/powered/alarm states; explicit ambient radiance, wall occlusion and bounded local shadows; physical navigation, floor rules, hinges, chapter authority, objective route, inventory, native fullscreen.\n"+DeadAir.Editor.DeadAirTextureAudit.Validate());
        PlayerSettings.bundleVersion="1.3.1";
        DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        return "DEAD AIR LIGHTING BUILD COMPLETE";
    }
}
