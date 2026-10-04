public static class ImportedCharacterValidationRun
{
    public static string Main()
    {
        DeadAir.Editor.DeadAirImportedCharacterValidation.Validate();
        DeadAir.Editor.DeadAirCharacterRulesValidation.Validate();
        return "Imported models/hands and original character equipment/inventory rules passed.";
    }

    public static string RenderPreviews()
    {
        DeadAir.Editor.DeadAirCharacterPreview.RenderLineup();
        DeadAir.Editor.DeadAirFirstPersonPreview.RenderAll();
        DeadAir.Editor.DeadAirThirdPersonGripPreview.RenderAll();
        return "Imported cast lineup, first person equipment and third person grips rendered in Art/Characters/Previews.";
    }
}
