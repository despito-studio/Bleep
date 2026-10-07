        const string result = "Logs/update-build-result.txt";
        const string progress = "Logs/update-build-progress.txt";
        const bool rebuildGeometry = true;
        try
        {
            System.IO.File.WriteAllText(result, "RUNNING\n");
            System.IO.File.WriteAllText(progress, "Importing generated world\n");
            System.IO.File.WriteAllText("Assets/Resources/world.json", System.IO.File.ReadAllText("Logs/update-world.json"));
            UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/world.json", UnityEditor.ImportAssetOptions.ForceSynchronousImport | UnityEditor.ImportAssetOptions.ForceUpdate);
            if (rebuildGeometry)
            {
                System.IO.File.WriteAllText(progress, "Rebuilding saved office, drawers, glass and prison geometry\n");
                DeadAir.Editor.DeadAirOfficeBuilder.Build();
            }
            System.IO.File.WriteAllText(progress, "Validating mushrooms\n");
            DeadAir.Editor.DeadAirMushroomValidation.Validate();
            System.IO.File.WriteAllText(progress, "Validating drawers and batteries\n");
            DeadAir.Editor.DeadAirSuppliesValidation.Validate();
            System.IO.File.WriteAllText(progress, "Validating breaker protection\n");
            DeadAir.Editor.DeadAirBreakerValidation.Validate();
            System.IO.File.WriteAllText(progress, "Validating physical prison, book access and navigation\n");
            DeadAir.Editor.DeadAirBasementValidation.Validate();
            DeadAir.Editor.DeadAirNavigationValidation.Validate();
            System.IO.File.WriteAllText(progress, "Validating complete objective path, inventory transfers and LAN\n");
            DeadAir.Editor.DeadAirEditorSetup.ValidateMigration();
            DeadAir.Editor.DeadAirEditorSetup.ValidateInventory();
            DeadAir.Editor.DeadAirEditorSetup.ValidateLan();
            System.IO.File.WriteAllText(progress, "Saving playable scene and building Windows\n");
            DeadAir.Editor.DeadAirEditorSetup.CreateScene();
            UnityEditor.PlayerSettings.bundleVersion = DeadAir.DeadAirVersion.Current;
            UnityEditor.AssetDatabase.SaveAssets();
            DeadAir.Editor.DeadAirEditorSetup.BuildWindows();
            System.IO.File.WriteAllText(result, "PASSED: imported world; saved rebuilt station/exterior; mushrooms; drawers/battery rules; breaker protection and cap; physical basement cells and diary; navigation; Windows build. Runtime capture still required.\n");
            System.IO.File.WriteAllText(progress, "Windows build complete\n");
        }
        catch (System.Exception error)
        {
            UnityEngine.Debug.LogException(error);
            System.IO.File.WriteAllText(result, "FAILED: " + error + "\n");
        }
return System.IO.File.ReadAllText("Logs/update-build-result.txt");
