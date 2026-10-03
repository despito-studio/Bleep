using System.IO;
public static class OfficeFinalizeRun
{
    public static string Main()
    {
        DeadAir.Editor.DeadAirOfficeBuilder.RefreshGeneratorAppearance();
        DeadAir.Editor.DeadAirEditorSetup.CreateScene();
        DeadAir.Editor.DeadAirFloorRulesValidation.Validate();
        DeadAir.Editor.DeadAirOfficeVerification.ValidateHinges();
        DeadAir.Editor.DeadAirEditorSetup.ValidateMigration();
        DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        DeadAir.Editor.DeadAirEditorSetup.ValidateLan();
        File.WriteAllText("Art/Office/Previews/editor-verification.txt", "PASSED: persisted office geometry, vertical gameplay/LOS, animated hinges, full objective path, floor collisions, personal keycards/item drops, LAN join/start/movement.\n" + DeadAir.Editor.DeadAirTextureAudit.Validate());
        DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        return "DEAD AIR FINAL OFFICE WINDOWS BUILD AND RULE CHECKS COMPLETE";
    }
}
