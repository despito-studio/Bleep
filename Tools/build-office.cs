using System.IO;
using UnityEngine;
public static class OfficeBuildRun
{
    public static string Main()
    {
        DeadAir.Editor.DeadAirOfficeBuilder.Build();
        DeadAir.Editor.DeadAirEditorSetup.CreateScene();
        DeadAir.Editor.DeadAirFloorRulesValidation.Validate();
        DeadAir.Editor.DeadAirOfficeVerification.ValidateHinges();
        DeadAir.Editor.DeadAirCharacterRulesValidation.Validate();
        DeadAir.Editor.DeadAirEditorSetup.ValidateMigration();
        DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        DeadAir.Editor.DeadAirEditorSetup.ValidateLan();
        DeadAir.Editor.DeadAirOfficeVerification.Capture();
        File.WriteAllText("Art/Office/Previews/editor-verification.txt", "PASSED: persisted office geometry, vertical gameplay/LOS, animated hinges, character rules, full objective path, floor collisions, personal keycards/item drops, LAN join/start/movement.\n" + DeadAir.Editor.DeadAirTextureAudit.Validate());
        return "DEAD AIR EXPANDED OFFICE EDITOR VERIFICATION COMPLETE";
    }
}
