using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MenuBuildRun
{
    public static string Main()
    {
        const string thumbnail="Assets/Resources/UI/ChapterBlackpine.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(thumbnail);
        if(importer!=null)
        {
            importer.textureType=TextureImporterType.Default;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.filterMode=FilterMode.Point;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        DeadAir.Editor.DeadAirChapterValidation.Validate();
        DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
        Directory.CreateDirectory("Art/Menu");
        File.WriteAllText("Art/Menu/editor-verification.txt","PASSED: chapter availability/host authority/default compatibility/snapshot serialization/actual loopback LAN; inventory rules.\n");
        DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
        return "DEAD AIR HORROR MENU BUILD COMPLETE";
    }
}
