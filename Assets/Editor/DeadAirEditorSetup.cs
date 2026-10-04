using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.Reflection;

namespace DeadAir.Editor
{
    public static class DeadAirEditorSetup
    {
        [MenuItem("Dead Air/Create Playable Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Dead Air / Runtime bootstrap").AddComponent<DeadAirBootstrap>();
            var environment = new GameObject("BLACKPINE / original web scene");
            foreach(string id in new[] { "station", "exterior" })
            {
                var prefab=Resources.Load<GameObject>("World/"+id);
                if(prefab==null) throw new Exception("Import the textured web scene before creating Blackpine.");
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,environment.transform);
                instance.name=id;
            }
            RenderSettings.skybox=null;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.095f,.12f,.165f);
            RenderSettings.ambientEquatorColor=new Color(.045f,.052f,.067f);
            RenderSettings.ambientGroundColor=new Color(.025f,.026f,.033f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
            ColorUtility.TryParseHtmlString("#101621",out var night);
            RenderSettings.fogColor=night;RenderSettings.fogDensity=.014f;
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Blackpine.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Blackpine.unity", true) };
            PlayerSettings.companyName = "BUREAU-9";
            PlayerSettings.productName = "Dead Air: Blackpine Broadcast";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("Blackpine scene created and added to Build Settings.");
        }

        [MenuItem("Dead Air/Reimport World Data")]
        public static void ReimportWorldData()
        {
            AssetDatabase.ImportAsset("Assets/Resources/world.json", ImportAssetOptions.ForceUpdate);
            Debug.Log("Blackpine world data reimported.");
        }

        [MenuItem("Dead Air/Validate Migration")]
        public static void ValidateMigration()
        {
            var world = WorldData.Load();
            if (world.rooms.Length < 20 || world.objects.Length != 12 || world.doors.Length < 16 || world.stairs == null || world.stairs.Length == 0)
                throw new Exception("The two-storey office is missing rooms, doors, stairs or objectives.");
            string[] models = { "station", "exterior", "character_engineer", "character_operator",
                "character_journalist", "character_dispatcher", "monster", "monster_bloody", "camera" };
            foreach (var model in models)
                if (Resources.Load<GameObject>("World/" + model) == null)
                    throw new Exception("Missing imported model: " + model);
            int textured = 0;
            var station = Resources.Load<GameObject>("World/station");
            foreach(var renderer in station.GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)
                    if(material != null && material.mainTexture != null) textured++;
            if(textured < 50) throw new Exception("Station textures have not been imported.");
            Debug.Log("Textured station validation: " + textured + " textured renderer slots.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            if (scene.GetRootGameObjects().Length == 0 ||
                scene.GetRootGameObjects()[0].GetComponent<DeadAirBootstrap>() == null)
                throw new Exception("Playable Blackpine scene is missing its bootstrap.");
            var rules = new DeadAirRules(world, "host", "Victor", "engineer", true, false);
            rules.Start("host");
            foreach (string id in new[] { "keycard-guard", "keycard-engineer", "tape-1", "tape-2", "tape-3",
                "fuse-1", "fuse-2", "fuel-canister", "pressure-valve", "power" })
            {
                var obj = world.Objective(id);
                var player = rules.State.Player("host");
                player.x = obj.x; player.z = obj.z; player.y = obj.y > 4 ? 4.2f : 0;
                var error = rules.Interact("host", id);
                if (error != null) throw new Exception(id + ": " + error);
            }
            var actor = rules.State.Player("host");var broadcast=world.Objective("broadcast");
            actor.x = broadcast.x; actor.z = broadcast.z;actor.y=broadcast.y>4?4.2f:0;
            if (rules.Calibrate("host", 96.4f) != null || rules.Interact("host", "broadcast") != null)
                throw new Exception("Broadcast objective failed.");
            actor.x = world.extraction.x; actor.z = world.extraction.z;actor.y=world.extraction.y;
            if (rules.Interact("host", "exit") != null || rules.State.phase != "won")
                throw new Exception("Extraction objective failed.");
            var bootstrap = scene.GetRootGameObjects()[0].GetComponent<DeadAirBootstrap>();
            typeof(DeadAirBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bootstrap, null);
            Physics.SyncTransforms();
            foreach(var position in new[] { new Vector3(5,2,19),new Vector3(0,2,-5),new Vector3(6,2,-4),new Vector3(-6,2,-20),new Vector3(0,6.2f,-5),new Vector3(5,2,-62) })
                if(!Physics.Raycast(position,Vector3.down,out var hit,3) || hit.normal.y < .5f)
                    throw new Exception("Source mesh floor collision failed at " + position);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("Dead Air migration validated: textured scene, floor collisions and complete quest path.");
        }

        [MenuItem("Dead Air/Build Windows")]
        public static void BuildWindows()
        {
            // The Built-in renderer needs no DirectX 12 features. DirectX 11
            // also supports older GPUs whose DX12 driver can fail creating HDR targets.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Blackpine.unity" },
                locationPathName = "Builds/Windows/DeadAir.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("Dead Air Windows build succeeded: Builds/Windows/DeadAir.exe");
        }

        public static void PrepareAndBuild()
        {
            CreateScene();
            ValidateMigration();
            ValidateInventory();
            ValidateLan();
            BuildWindows();
        }

        [MenuItem("Dead Air/Validate Inventory")]
        public static void ValidateInventory()
        {
            var world = WorldData.Load();
            var rules = new DeadAirRules(world, "host", "Victor", "engineer", false, false);
            rules.AddPlayer("guest", "Vivian", "operator");
            rules.Start("host");

            var host = rules.State.Player("host");
            var guest = rules.State.Player("guest");

            if (!host.HasItem("flashlight") || !guest.HasItem("flashlight"))
                throw new Exception("Players must start with flashlight in inventory.");
            if(!host.HasItem("wrench")||!guest.HasItem("cigarette-pack")||host.inventory.Count!=2||guest.inventory.Count!=2)
                throw new Exception("Every role item must start in the regular inventory.");
            if (!host.flashlight || !guest.flashlight)
                throw new Exception("Flashlight should be enabled on start.");

            var dropErr = rules.Drop("host", "flashlight");
            if (dropErr != null) throw new Exception("Drop flashlight failed: " + dropErr);
            if (host.HasItem("flashlight")) throw new Exception("Host should no longer have flashlight.");
            if (host.flashlight) throw new Exception("Host flashlight should be turned off when dropped.");
            if (rules.State.droppedItems.Count != 1) throw new Exception("Dropped items count should be 1.");

            var droppedFlashlight = rules.State.droppedItems[0];
            guest.x = droppedFlashlight.x; guest.z = droppedFlashlight.z;guest.y=droppedFlashlight.y;
            var pickErr = rules.Interact("guest", "dropped:" + droppedFlashlight.id);
            if (pickErr != null) throw new Exception("Guest pickup failed: " + pickErr);
            if (rules.State.droppedItems.Count != 0) throw new Exception("Dropped items count should be 0.");

            var keyObj = world.Objective("keycard-engineer");
            host.x = keyObj.x; host.z = keyObj.z;host.y=keyObj.y>4?4.2f:0;
            var keyPickErr = rules.Interact("host", "keycard-engineer");
            if (keyPickErr != null) throw new Exception("Keycard pickup failed: " + keyPickErr);
            if (!host.HasItem("keycard-engineer")) throw new Exception("Host should have keycard-engineer.");
            if (guest.HasItem("keycard-engineer")) throw new Exception("Guest should NOT have keycard-engineer.");

            DoorData engDoor = null;
            foreach (var d in world.doors)
            {
                if (d.requires == "keycard-engineer") { engDoor = d; break; }
            }
            if (engDoor == null) throw new Exception("No door requires keycard-engineer.");

            guest.x = engDoor.x; guest.z = engDoor.z;guest.y=engDoor.y>4?4.2f:0;
            var guestOpenErr = rules.Interact("guest", engDoor.id);
            if (guestOpenErr == null || !guestOpenErr.Contains("картка"))
                throw new Exception("Guest without keycard should not be able to open door: " + guestOpenErr);
            if (rules.State.Door(engDoor.id).open)
                throw new Exception("Door should remain closed/locked for guest.");

            host.x = engDoor.x; host.z = engDoor.z;host.y=engDoor.y>4?4.2f:0;
            var hostOpenErr = rules.Interact("host", engDoor.id);
            if (hostOpenErr != null) throw new Exception("Host with keycard failed to open door: " + hostOpenErr);
            if (!rules.State.Door(engDoor.id).open) throw new Exception("Door should now be open.");

            var dropKeyErr = rules.Drop("host", "keycard-engineer");
            if (dropKeyErr != null) throw new Exception("Host failed to drop keycard: " + dropKeyErr);
            if (host.HasItem("keycard-engineer")) throw new Exception("Host should not have keycard after drop.");

            var droppedKey = rules.State.droppedItems[0];
            guest.x = droppedKey.x; guest.z = droppedKey.z;guest.y=droppedKey.y;
            var guestKeyPickErr = rules.Interact("guest", "dropped:" + droppedKey.id);
            if (guestKeyPickErr != null) throw new Exception("Guest pickup keycard failed: " + guestKeyPickErr);
            if (!guest.HasItem("keycard-engineer")) throw new Exception("Guest should now have keycard-engineer.");

            rules.SelectSlot("guest", 1);
            if (guest.selectedSlot != 1 || !guest.HasSelectedRoleItem || !guest.uniqueItemEquipped)
                throw new Exception("Selecting the cigarette-pack inventory slot must equip it.");
            if(rules.Drop("guest","cigarette-pack")==null||!guest.HasItem("cigarette-pack"))
                throw new Exception("The permanent role item must reject dropping.");

            Debug.Log("Dead Air inventory validated: personal keycards, dropping items, flashlight mechanics, and slot selection.");
        }

        [MenuItem("Dead Air/Validate LAN")]
        public static void ValidateLan()
        {
            var world = WorldData.Load();
            var hostObject = new GameObject("LAN test host");
            var clientObject = new GameObject("LAN test guest");
            var host = hostObject.AddComponent<DeadAirSession>();
            var guest = clientObject.AddComponent<DeadAirSession>();
            var update = typeof(DeadAirSession).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                host.Initialize(world); guest.Initialize(world);
                host.Host("Victor", "engineer", false, false);
                guest.Join("127.0.0.1", host.State.code, "Vivian", "operator");
                for (int i = 0; i < 200 && (guest.State == null || host.State.players.Count < 2); i++)
                {
                    update.Invoke(host, null); update.Invoke(guest, null);
                    System.Threading.Thread.Sleep(15);
                }
                if (guest.State == null || host.State.players.Count != 2 || guest.State.players.Count != 2)
                    throw new Exception("LAN guest did not join the lobby.");
                host.Command(new NetPacket { type = "start" });
                for (int i = 0; i < 100 && guest.State.phase != "playing"; i++)
                {
                    update.Invoke(host, null); update.Invoke(guest, null);
                    System.Threading.Thread.Sleep(15);
                }
                if (guest.State.phase != "playing") throw new Exception("LAN start was not synchronized.");
                var id = guest.LocalId;
                if(!host.State.Player(id).HasItem("cigarette-pack")||!guest.State.Player(id).HasItem("cigarette-pack"))
                    throw new Exception("The LAN guest must receive its role item in inventory.");
                guest.Command(new NetPacket{type="slot",phrase=guest.State.Player(id).inventory.IndexOf("cigarette-pack")});
                for(int i=0;i<100&&!guest.State.Player(id).uniqueItemEquipped;i++)
                {update.Invoke(host,null);update.Invoke(guest,null);System.Threading.Thread.Sleep(15);}
                if(!host.State.Player(id).uniqueItemEquipped||!guest.State.Player(id).HasSelectedRoleItem)
                    throw new Exception("Regular inventory role-item equip was not synchronized.");
                guest.Command(new NetPacket{type="unique-use"});
                for(int i=0;i<100&&guest.State.Player(id).cigarettesRemaining!=11;i++)
                {update.Invoke(host,null);update.Invoke(guest,null);System.Threading.Thread.Sleep(15);}
                if(host.State.Player(id).cigarettesRemaining!=11||guest.State.Player(id).cigarettesRemaining!=11)
                    throw new Exception("LAN role action must consume one cigarette on both peers.");
                var before = host.State.Player(id);
                float startZ = before.z;
                guest.Command(new NetPacket { type = "move", x = before.x, z = startZ + .5f,
                    yaw = 0, flashlight = true,moveSequence=1 });
                for (int i = 0; i < 100 && host.State.Player(id).z < startZ + .4f; i++)
                {
                    update.Invoke(host, null); update.Invoke(guest, null);
                    System.Threading.Thread.Sleep(15);
                }
                if (host.State.Player(id).z < startZ + .4f)
                    throw new Exception("LAN movement was not received by the host.");
                for(int i=0;i<100&&guest.State.Player(id).moveSequence!=1;i++)
                {update.Invoke(host,null);update.Invoke(guest,null);System.Threading.Thread.Sleep(15);}
                if(guest.State.Player(id).moveSequence!=1)throw new Exception("LAN movement acknowledgement was not replicated.");
                Debug.Log("Dead Air LAN validated: host, join, synchronized start, inventory role gear/action and acknowledged guest movement.");
            }
            finally
            {
                guest.Leave(); host.Leave();
                UnityEngine.Object.DestroyImmediate(clientObject);
                UnityEngine.Object.DestroyImmediate(hostObject);
            }
        }
    }
}
