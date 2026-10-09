namespace DeadAir.Editor
{
    public static class DeadAirBodyRelease
    {
        public static void BuildWindows()
        {
            DeadAirFirstPersonBodyValidation.Validate();
            DeadAirEditorSetup.BuildWindows();
        }
    }
}
