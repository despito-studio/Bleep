namespace DeadAir.Editor
{
    public static class DeadAirBodyRelease
    {
        public static void BuildWindows()
        {
            DeadAirFirstPersonBodyValidation.Validate();
            UnityEditor.EditorUtility.UnloadUnusedAssetsImmediate();
            System.GC.Collect();
            DeadAirEditorSetup.BuildWindows();
        }
    }
}
