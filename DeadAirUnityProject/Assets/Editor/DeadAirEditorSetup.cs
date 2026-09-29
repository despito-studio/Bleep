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
            RenderSettings.ambientSkyColor=new Color(.15f,.17f,.135f);
            RenderSettings.ambientEquatorColor=new Color(.075f,.085f,.055f);
            RenderSettings.ambientGroundColor=new Color(.042f,.033f,.018f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
            ColorUtility.TryParseHtmlString("#0a100a",out var night);
            RenderSettings.fogColor=night;RenderSettings.fogDensity=.032f;
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Blackpine.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Blackpine.unity", true) };
            PlayerSettings.companyName = "BUREAU-9";
            PlayerSettings.productName = "Dead Air: Blackpine Broadcast";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
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
            if (world.props.Length < 300 || world.objects.Length != 12 || world.doors.Length != 7)
                throw new Exception("Imported world counts do not match the source game.");
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
                player.x = obj.x; player.z = obj.z;
                var error = rules.Interact("host", id);
                if (error != null) throw new Exception(id + ": " + error);
            }
            var actor = rules.State.Player("host"); actor.x = 10; actor.z = -2;
            if (rules.Calibrate("host", 96.4f) != null || rules.Interact("host", "broadcast") != null)
                throw new Exception("Broadcast objective failed.");
            actor.x = world.extraction.x; actor.z = world.extraction.z;
            if (rules.Interact("host", "exit") != null || rules.State.phase != "won")
                throw new Exception("Extraction objective failed.");
            var bootstrap = scene.GetRootGameObjects()[0].GetComponent<DeadAirBootstrap>();
            typeof(DeadAirBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bootstrap, null);
            Physics.SyncTransforms();
            foreach(var position in new[] { new Vector3(5,2,19),new Vector3(0,2,-5),new Vector3(6,2,-4),new Vector3(-6,2,-20) })
                if(!Physics.Raycast(position,Vector3.down,out var hit,3) || hit.normal.y < .5f)
                    throw new Exception("Source mesh floor collision failed at " + position);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("Dead Air migration validated: textured scene, floor collisions and complete quest path.");
        }

        [MenuItem("Dead Air/Build Windows")]
        public static void BuildWindows()
        {
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
            ValidateLan();
            BuildWindows();
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
                var before = host.State.Player(id);
                float startZ = before.z;
                guest.Command(new NetPacket { type = "move", x = before.x, z = startZ + .5f,
                    yaw = 0, flashlight = true });
                for (int i = 0; i < 100 && host.State.Player(id).z < startZ + .4f; i++)
                {
                    update.Invoke(host, null); update.Invoke(guest, null);
                    System.Threading.Thread.Sleep(15);
                }
                if (host.State.Player(id).z < startZ + .4f)
                    throw new Exception("LAN movement was not received by the host.");
                Debug.Log("Dead Air LAN validated: host, join, synchronized start and guest movement.");
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
