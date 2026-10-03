using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadAir
{
    // Opt-in build verification: -deadAirCapture <absolute output folder>.
    // Uses real rendered frames (including the game UI and post processing).
    public sealed class DeadAirVisualCapture : MonoBehaviour
    {
        DeadAirSession session;
        DeadAirPlayer player;
        DeadAirUI ui;
        string output;
        Camera captureUI;
        Canvas captureCanvas;
        RenderTexture captureFrame;
        DeadAirFrameRenderer frameRenderer;
        DeadAirWorld captureWorld;
        bool officeCapture, menuCapture, repairCapture, lightingCapture, captureWithoutEquipment;
        bool preferencesCaptured, hadPlayerName, hadChapter;
        string originalPlayerName, originalChapter;
        readonly Dictionary<string,float> originalSliders = new Dictionary<string,float>();
        readonly Dictionary<string,int?> originalToggles = new Dictionary<string,int?>();
        readonly List<string> repairChecks = new List<string>();

        public void Initialize(DeadAirSession game, DeadAirPlayer actor)
        {
            session = game; player = actor;
            ui = FindFirstObjectByType<DeadAirUI>();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-deadAirCapture" || args[i] == "-deadAirOfficeCapture" || args[i] == "-deadAirMenuCapture" || args[i] == "-deadAirRepairCapture" || args[i] == "-deadAirLightingCapture")
                { output = args[i + 1];officeCapture=args[i]=="-deadAirOfficeCapture";menuCapture=args[i]=="-deadAirMenuCapture";repairCapture=args[i]=="-deadAirRepairCapture";lightingCapture=args[i]=="-deadAirLightingCapture"; }
            if (string.IsNullOrEmpty(output)) { enabled = false; return; }
            if (!Path.IsPathRooted(output)) throw new ArgumentException("-deadAirCapture requires an absolute output folder.");
            Directory.CreateDirectory(output);
            if(menuCapture||repairCapture||lightingCapture)
            {
                hadPlayerName=PlayerPrefs.HasKey("DeadAir.PlayerName");originalPlayerName=PlayerPrefs.GetString("DeadAir.PlayerName");
                hadChapter=PlayerPrefs.HasKey("DeadAir.Chapter");originalChapter=PlayerPrefs.GetString("DeadAir.Chapter");
                foreach(string key in new[]{"DeadAir.Volume","DeadAir.AmbienceVolume","DeadAir.EffectsVolume","DeadAir.MouseSensitivity"})
                    originalSliders[key]=PlayerPrefs.HasKey(key)?PlayerPrefs.GetFloat(key):float.NaN;
                foreach(string key in new[]{"DeadAir.Fullscreen","DeadAir.Muted","DeadAir.Grain","DeadAir.Flicker","DeadAir.Quality"})
                    originalToggles[key]=PlayerPrefs.HasKey(key)?(int?)PlayerPrefs.GetInt(key):null;
                preferencesCaptured=true;
            }
            if(Application.isBatchMode)InitializeOffscreenCapture();
            StartCoroutine(lightingCapture?CaptureLighting():repairCapture?CaptureRepairs():menuCapture?CaptureMenu():officeCapture?CaptureOffice():Capture());
        }

        IEnumerator CaptureLighting()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=30;
            yield return new WaitForSecondsRealtime(1.5f);
            ui.ReturnToMenu();yield return null;
            ClickUiButton("Chapters","Main menu");yield return null;
            Require(!UiButton("Select coming-soon","Chapter selection card").IsInteractable(),"An unfinished chapter is playable.");
            yield return VerifyKeyboardFocus("Chapter selection");
            yield return Save("lighting-01-chapter-cards");
            ClickUiButton("Select blackpine","Chapter selection card");
            ClickUiButton("Begin chapter","Chapter selection card");yield return null;
            ClickUiButton("Choose engineer","Prepare operation card");ClickUiButton("Explore","Prepare operation card");
            yield return Until(()=>session.State?.phase=="playing","Lighting capture starts actual chapter");
            bool grain=DeadAirSettings.Grain,flicker=DeadAirSettings.Flicker;
            DeadAirSettings.Grain=false;DeadAirSettings.Flicker=false;
            var checks=new List<string>();
            // Look below the fluorescent diffuser and away from the HUD/hand.
            // This comparison measures surfaces lit by the fixtures, rather than
            // accepting the brightness of an emissive panel as room lighting.
            InspectToward(-11.3f,-16.1f,4.2f,-15,5.3f,-13);player.SetVerificationFlashlight(false);
            yield return Save("lighting-02-meeting-emergency-torch-off");
            var sample=new Rect(.23f,.24f,.38f,.37f);
            float emergency=DisplayLuminance(sample);
            Require(!player.FlashlightOn&&!player.ViewCamera.transform.Find("Flashlight").GetComponent<Light>().enabled,"The practical-light capture secretly enabled the flashlight.");
            var practicals=new List<GameObject>();
            foreach(var light in GameObject.Find("BLACKPINE / original web scene").GetComponentsInChildren<Light>(true))
                if(light.name.StartsWith("lamp:")&&light.gameObject.activeSelf){practicals.Add(light.gameObject);light.gameObject.SetActive(false);}
            Require(practicals.Count>20,"The office practical lamps are absent.");
            yield return Save("lighting-03-same-room-practicals-disabled");
            float ambient=DisplayLuminance(sample);
            foreach(var light in practicals)light.SetActive(true);
            Require(emergency>.035f&&emergency>ambient+.018f&&emergency>ambient*1.25f,
                "Ceiling lights do not visibly illuminate room surfaces without a flashlight: emergency="+emergency+" ambient="+ambient);
            var data=WorldData.Load();var power=data.Objective("power");
            InspectToward(power.x,power.z+2,0,power.x,power.y,power.z);player.SetVerificationFlashlight(false);
            session.State.fuses.Add("fuse-1");session.State.fuses.Add("fuse-2");
            session.Command(new NetPacket{type="interact",id="power"});
            Require(session.State.power&&session.State.quest.power,"The mounted generator panel cannot be used from its physical front approach.");
            InspectToward(-11.3f,-16.1f,4.2f,-15,5.3f,-13);player.SetVerificationFlashlight(false);
            yield return Save("lighting-04-meeting-powered-torch-off");
            float powered=DisplayLuminance(sample);
            Require(powered>emergency+.01f,"Restoring generator power did not increase practical illumination.");
            checks.Add("PASSED: GPU diffuse-room comparison, flashlight OFF; emergency="+emergency.ToString("F4")+", lamps disabled="+ambient.ToString("F4")+", powered="+powered.ToString("F4")+" (display luminance).");
            InspectToward(0,-9,0,-15,1.3f,-9);player.SetVerificationFlashlight(false);yield return Save("lighting-05-cross-hall-torch-off");
            InspectToward(-8,-13,0,-8,1.45f,-24);player.SetVerificationFlashlight(false);yield return Save("lighting-06-side-gallery-torch-off");
            InspectToward(0,-12.1f,4.2f,0,5.5f,-22);player.SetVerificationFlashlight(false);yield return Save("lighting-07-newsroom-torch-off");
            InspectToward(-7.1f,3.8f,0,-13,1.2f,-1);player.SetVerificationFlashlight(false);yield return Save("lighting-08-reception-torch-off");
            InspectToward(14.5f,-15.8f,0,18,2.18f,-14);player.SetVerificationFlashlight(false);yield return Save("lighting-09-window-room-junction");
            InspectToward(14,-30,4.2f,18,6,-34);player.SetVerificationFlashlight(false);yield return Save("lighting-10-upper-window");
            InspectToward(power.x+1.5f,power.z+4.8f,0,power.x,power.y,power.z);player.SetVerificationFlashlight(false);yield return Save("lighting-11-generator-attached-panel");
            var sky=GameObject.Find("Distant night sky");
            Require(sky!=null&&Vector3.Distance(sky.transform.position,player.ViewCamera.transform.position)<.05f,"The captured rear-yard sky did not follow the real camera.");
            checks.Add("PASSED: rear-yard manual render uses real camera for sky and room culling.");
            checks.Add("PASSED: real interact command restores generator power from the mounted panel's front approach.");
            InspectToward(7,20,0,0,4,-11);player.SetVerificationFlashlight(false);yield return Save("lighting-12-front-facade");
            InspectToward(-11.3f,-16.1f,4.2f,-15,5.3f,-13);player.SetVerificationFlashlight(false);
            captureWithoutEquipment=true;captureCanvas.enabled=false;
            yield return Save("lighting-13-chapter-office-photo");
            captureCanvas.enabled=true;captureWithoutEquipment=false;
            checks.Add("PASSED: actual chapter card clicks, locked chapter, keyboard focus, character preparation and chapter start.");
            DeadAirSettings.Grain=grain;DeadAirSettings.Flicker=flicker;
            player.EndInspection();ui.ReturnToMenu();yield return null;RequireMenu("Lighting capture returns to menu");
            File.WriteAllLines(Path.Combine(output,"lighting-runtime-report.txt"),checks);
            Debug.Log("DEAD AIR LIGHTING RUNTIME PASSED: real GPU practical lights, power states, torch OFF, chapter cards, rear camera.");
            Application.Quit(0);
        }

        float DisplayLuminance(Rect area)
        {
            Require(captureFrame!=null,"Lighting verification requires an actual rendered GPU target.");
            var previous=RenderTexture.active;RenderTexture.active=captureFrame;
            var pixels=new Texture2D(captureFrame.width,captureFrame.height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,captureFrame.width,captureFrame.height),0,0);pixels.Apply();RenderTexture.active=previous;
            var colors=pixels.GetPixels32();float sum=0;int count=0;
            for(int y=(int)(area.yMin*pixels.height);y<(int)(area.yMax*pixels.height);y+=2)
                for(int x=(int)(area.xMin*pixels.width);x<(int)(area.xMax*pixels.width);x+=2)
                {var color=colors[y*pixels.width+x];sum+=(color.r*.2126f+color.g*.7152f+color.b*.0722f)/255;count++;}
            Destroy(pixels);return sum/Mathf.Max(1,count);
        }

        IEnumerator CaptureRepairs()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=30;
            string report=Path.Combine(output,"repair-runtime-report.txt");
            if(File.Exists(report))File.Delete(report);
            yield return new WaitForSecondsRealtime(1.5f);
            player.ClearVerificationMovement();player.EndInspection();ui.ReturnToMenu();
            yield return null;
            yield return Save("repair-01-main-menu");
            ClickUiButton("Settings","Main menu");
            Require(!UiButton("Solo","Main menu").IsInteractable(),"The main menu accepts input beneath settings.");
            EventSystem.current.SetSelectedGameObject(null);yield return null;yield return null;
            RequireUiFocus("Settings");yield return VerifyKeyboardFocus("Settings");
            yield return Save("repair-02-settings");
            ClickUiButton("Save settings","Settings card");yield return null;
            ClickUiButton("Chapters","Main menu");yield return null;
            Require(!UiButton("Select coming-soon","Chapter selection card").IsInteractable(),"An unfinished chapter can be started.");
            yield return Save("repair-03-chapters");
            ClickUiButton("Begin chapter","Chapter selection card");yield return null;
            UiElement<UnityEngine.UI.InputField>("Operator name","Prepare operation card").text="Vivian";
            ClickUiButton("Choose operator","Prepare operation card");
            ClickUiButton("Explore","Prepare operation card");
            yield return Until(()=>session.State?.phase=="playing","Repair capture solo start");
            var me=session.State.Player(session.LocalId);
            Require(me.characterClass=="operator"&&me.inventory.Count>=2&&me.inventory[0]=="flashlight"&&me.inventory[1]=="cigarette-pack",
                "Vivian must start with a flashlight and cigarette pack in actual inventory slots.");
            repairChecks.Add("PASSED: simplified main/settings/chapter flow; modal keyboard focus; Vivian inventory initialization.");

            // The front-yard center lane has 22 metres of unobstructed pavement.
            // Eight seconds at ten frames/sec exercises real Player.Update input,
            // CharacterController stepping, packet sends and reconciliation.
            player.SetInspectionPose(0,30,0,0);yield return null;
            player.EndInspection();yield return null;
            yield return VerifySlowMovement("solo");

            player.SetInspectionPose(0,22,0,0);
            ui.ToggleInventory();yield return null;
            Require(ui.InventoryOpen&&session.State.paused,"Vivian's solo inventory must pause the world.");
            var packSlot=UiButton("Inventory item 1","Inventory card");
            Require(packSlot.GetComponentInChildren<UnityEngine.UI.Text>().text.IndexOf("сигарет",StringComparison.OrdinalIgnoreCase)>=0,"The cigarette pack has no visible inventory label.");
            ClickUiButton("Inventory item 1","Inventory card");
            yield return Save("repair-04-vivian-inventory");
            ClickUiButton("Equip item","Inventory card");yield return null;
            Require(!ui.InventoryOpen&&!session.State.paused&&me.selectedSlot==1&&me.HasSelectedRoleItem&&me.uniqueItemEquipped,
                "Choosing Vivian's pack through inventory must equip the selected role item.");
            yield return Save("repair-05-vivian-pack");
            int actionSequence=me.uniqueActionSequence;
            session.Command(new NetPacket{type="unique-use"});
            Require(me.uniqueActionSequence==actionSequence+1&&me.cigarettesRemaining==11,
                "The R action on the selected cigarette-pack slot did not consume one cigarette.");
            var rig=player.GetComponent<DeadAirFirstPersonRig>();
            yield return Until(()=>HasActiveFlame(rig),"Selected cigarette pack produces a visible lighter flame");
            yield return Save("repair-06-lighter-light",false);
            Require(HasActiveFlame(rig),"The visible lighter flame must emit local light during the smoking animation.");
            repairChecks.Add("PASSED: real inventory raycast selection of cigarette pack; selected-slot R action; consumption and active flame light.");

            ui.ToggleInventory();yield return null;
            ClickUiButton("Inventory item 0","Inventory card");
            ClickUiButton("Equip item","Inventory card");yield return null;
            Require(me.selectedSlot==0&&!me.uniqueItemEquipped,"Selecting the flashlight failed to stow the cigarette pack.");
            InspectToward(0,-3,0,-3,1.45f,-9);
            yield return Save("repair-07-held-flashlight");
            Require(rig.TryGetFlashlightPose(out var lampPosition,out var lampRotation),"The selected flashlight has no visible emitter pose.");
            var lamp=player.ViewCamera.transform.Find("Flashlight").GetComponent<Light>();
            Require(lamp.enabled&&Vector3.Distance(lamp.transform.position,lampPosition)<.003f&&Quaternion.Angle(lamp.transform.rotation,lampRotation)<.1f,
                "The flashlight beam does not originate from the animated item lens.");
            Require((lamp.cullingMask&(1<<30))==0,"The world flashlight incorrectly illuminates the separate first-person equipment layer.");
            repairChecks.Add("PASSED: flashlight returns through inventory and its real emitter follows the visible lens.");

            bool grain=DeadAirSettings.Grain,flicker=DeadAirSettings.Flicker;
            DeadAirSettings.Grain=false;DeadAirSettings.Flicker=false;
            player.SetInspectionPose(0,31,.408f,.31f);
            var sky=GameObject.Find("Distant night sky");
            Require(sky!=null&&sky.GetComponent<Renderer>().sharedMaterial.shader.isSupported,"The new night-sky shader is unavailable in the player build.");
            yield return Save("repair-08-night-courtyard");
            DeadAirSettings.Grain=grain;DeadAirSettings.Flicker=flicker;
            InspectToward(0,-9,0,-15,1.5f,-9);yield return Save("repair-09-ground-cross-hall");
            InspectToward(-8,-13,0,-8,1.5f,-24);yield return Save("repair-10-side-gallery");
            InspectToward(0,-26,0,0,2.3f,-40);yield return Save("repair-11-service-junction");
            InspectToward(0,-9,4.2f,15,5.6f,-9);yield return Save("repair-12-upper-cross-hall");
            InspectToward(-11.3f,-16.1f,4.2f,-15,5.3f,-13);yield return Save("repair-13-meeting-room");
            InspectToward(0,-12.1f,4.2f,0,5.5f,-22);yield return Save("repair-14-editorial-office");
            ui.ToggleMap();yield return Save("repair-14b-held-map");
            Require(ui.MapOpen&&session.State.Player(session.LocalId).mapOpen,"The paper-map lighting capture must show the actual equipped map.");
            ui.TryCloseTopModal();
            InspectToward(-7.1f,3.8f,0,-13,1.2f,-1);yield return Save("repair-15-reception");

            player.EndInspection();ui.ReturnToMenu();yield return null;yield return null;
            var peerObject=new GameObject("Repair capture remote host");
            var peer=peerObject.AddComponent<DeadAirSession>();
            var guestWorld=WorldData.Load();guestWorld.spawn.z=30;
            peer.Initialize(guestWorld);
            try
            {
                peer.Host("Remote host","engineer",false,true);
                session.Join("127.0.0.1",peer.State.code,"Vivian guest","operator");
                yield return Until(()=>session.State!=null&&peer.State.players.Count==2,"Repair capture guest joins loopback host");
                peer.Command(new NetPacket{type="start"});
                yield return Until(()=>session.State?.phase=="playing","Repair capture guest start");
                yield return new WaitForSecondsRealtime(.35f);
                Require(!session.IsHost&&session.State.Player(session.LocalId).z>29,"Movement capture must drive the real local LAN guest at its authoritative spawn.");
                yield return VerifySlowMovement("LAN guest");
                yield return Until(()=>Vector3.Distance(player.transform.position,new Vector3(-peer.State.Player(session.LocalId).x,peer.State.Player(session.LocalId).y,peer.State.Player(session.LocalId).z))<.35f,
                    "Guest position reaches host after stopping");
                ui.ToggleInventory();yield return null;
                float elapsed=peer.State.elapsed;
                Require(!session.State.paused&&!peer.State.paused,"A guest inventory froze the cooperative world.");
                yield return new WaitForSecondsRealtime(.3f);
                Require(peer.State.elapsed>elapsed,"The cooperative world must keep ticking behind inventory.");
                ClickUiButton("Inventory item 1","Inventory card");ClickUiButton("Equip item","Inventory card");
                yield return Until(()=>session.State.Player(session.LocalId).uniqueItemEquipped&&peer.State.Player(session.LocalId).uniqueItemEquipped,
                    "Guest role inventory selection replicates");
                session.Command(new NetPacket{type="unique-use"});
                yield return Until(()=>session.State.Player(session.LocalId).cigarettesRemaining==11&&peer.State.Player(session.LocalId).cigarettesRemaining==11,
                    "Guest selected-slot cigarette action replicates");
                yield return Until(()=>HasActiveFlame(rig),"Guest selected pack produces a lighter flame");
                yield return Save("repair-16-guest-lighter",false);
                repairChecks.Add("PASSED: live LAN guest inventory leaves co-op time running; selected role-item action and cigarette count replicate.");
            }
            finally {player.ClearVerificationMovement();session.Leave();peer.Leave();Destroy(peerObject);}
            yield return null;yield return null;
            RequireMenu("Repair capture returns to menu");
            File.WriteAllLines(report,repairChecks);
            Debug.Log("DEAD AIR REPAIR RUNTIME PASSED: low-frame-rate solo/guest movement, inventory role items, item lights and rendered office views.");
            Application.Quit(0);
        }

        void InspectToward(float sourceX,float sourceZ,float floorY,float targetX,float targetY,float targetZ)
        {
            float dx=targetX-sourceX,dz=targetZ-sourceZ;
            player.SetInspectionPose(sourceX,sourceZ,Mathf.Atan2(-dx,-dz),Mathf.Atan2(targetY-floorY-1.65f,Mathf.Sqrt(dx*dx+dz*dz)),false,floorY);
        }

        static bool HasActiveFlame(DeadAirFirstPersonRig rig)
        {
            foreach(var light in rig.GetComponentsInChildren<Light>(true))
                if(light.name=="Small flame"&&light.gameObject.activeInHierarchy&&light.enabled&&light.intensity>0&&light.range>0)return true;
            return false;
        }

        IEnumerator VerifySlowMovement(string label)
        {
            Application.targetFrameRate=10;yield return null;yield return null;
            var origin=player.transform.position;
            int corrections=player.MovementCorrectionCount,totalFrames=0;
            float travelled=0,slowest=0;
            try
            {
                for(int leg=0;leg<2;leg++)
                {
                    Vector3 forward=leg==0?Vector3.back:Vector3.forward;
                    var previous=player.transform.position;
                    float deadline=Time.realtimeSinceStartup+4,legTravel=0;
                    player.SetVerificationMovement(leg==0?Vector2.up:Vector2.down,true);
                    while(Time.realtimeSinceStartup<deadline)
                    {
                        yield return null;
                        var position=player.transform.position;
                        float advance=Vector3.Dot(position-previous,forward);
                        Require(advance>=-.035f,label+" rewound during unobstructed movement: "+advance.ToString("F3"));
                        Require(Mathf.Abs(position.x-origin.x)<.2f&&position.z>7&&position.z<34,label+" left the unobstructed yard lane.");
                        Require(player.MovementCorrectionCount==corrections,label+" required a server correction during ordinary movement; distance="+player.LastMovementCorrectionDistance);
                        legTravel+=Mathf.Max(0,advance);slowest=Mathf.Max(slowest,Time.unscaledDeltaTime);totalFrames++;previous=position;
                    }
                    Require(legTravel>11,label+" made insufficient progress at low frame rate: "+legTravel.ToString("F2")+" m");
                    travelled+=legTravel;
                }
                player.SetVerificationMovement(Vector2.zero,false);yield return new WaitForSecondsRealtime(.45f);
                Require(player.MovementCorrectionCount==corrections,label+" was corrected after the final movement acknowledgement.");
                Require(Vector3.Distance(origin,player.transform.position)<2,label+" did not finish its forward/back yard path near the start.");
                string result="PASSED: "+label+" real Player.Update movement at target10fps; "+totalFrames+" frames, "+travelled.ToString("F2")+" m, max frame "+(slowest*1000).ToString("F1")+" ms, zero authoritative corrections.";
                repairChecks.Add(result);Debug.Log("DEAD AIR MOVEMENT REPAIR "+result);
            }
            finally {player.ClearVerificationMovement();Application.targetFrameRate=30;}
        }

        IEnumerator CaptureMenu()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            string report = Path.Combine(output, "menu-runtime-report.txt");
            if (File.Exists(report)) File.Delete(report);
            yield return new WaitForSecondsRealtime(2);
            player.EndInspection(); ui.ReturnToMenu();
            yield return Save("01-main-menu");

            ClickUiButton("Chapters", "Main menu");
            yield return null;
            var unavailable = UiButton("Select coming-soon", "Chapter selection card");
            Require(unavailable.gameObject.activeInHierarchy && !unavailable.interactable,
                "The next chapter must be visible and unavailable.");
            ClickUiButton("Select blackpine", "Chapter selection card");
            Require(ui.SelectedChapter == "blackpine", "Selecting chapter one did not retain Blackpine.");
            yield return Save("02-chapter-selection");
            ClickUiButton("Begin chapter", "Chapter selection card");
            yield return null;
            Require(UiButton("Begin operation", "Prepare operation card").isActiveAndEnabled,
                "The selected chapter must open operation preparation.");
            UiElement<UnityEngine.UI.InputField>("Operator name", "Prepare operation card").text = "Victor";
            ClickUiButton("Choose engineer", "Prepare operation card");
            yield return Save("03-operation-preparation");
            ClickUiButton("Close", "Prepare operation card");
            yield return null;

            ClickUiButton("Settings", "Main menu");
            yield return Save("04-settings");
            ClickUiButton("Save settings", "Settings card");
            yield return null;
            ClickUiButton("Dossier", "Main menu");
            yield return Save("05-dossier-and-controls");
            ClickUiButton("Close dossier", "Case file card");
            yield return null;
            ClickUiButton("Network", "Main menu");
            yield return Save("06-network-game");
            ClickUiButton("Join", "Network game card");
            yield return Save("07-join-session");
            ClickUiButton("Close", "Join session card");
            yield return null;

            ClickUiButton("Network", "Main menu");
            yield return null;
            ClickUiButton("Host", "Network game card");
            yield return null;
            ClickUiButton("Begin operation", "Prepare operation card");
            yield return Until(() => session.State?.phase == "lobby", "UI cooperative host creation");
            Require(session.State.chapterId == "blackpine" && !session.State.solo,
                "Creating a cooperative room must preserve its selected chapter.");
            var world = WorldData.Load();
            var guestObject = new GameObject("Menu capture chapter guest");
            var guest = guestObject.AddComponent<DeadAirSession>(); guest.Initialize(world);
            try
            {
                guest.Join("127.0.0.1", session.State.code, "Vivian", "operator");
                yield return Until(() => guest.State != null && session.State.players.Count == 2, "UI chapter lobby guest join");
                Require(guest.State.chapterId == session.State.chapterId && guest.State.chapterId == ui.SelectedChapter,
                    "The cooperative lobby must publish its selected chapter to guests.");
                guest.Command(new NetPacket { type = "chapter", chapterId = "coming-soon" });
                yield return Until(() => guest.LastError != null, "Guest chapter selection rejection");
                Require(session.State.chapterId == "blackpine", "A guest changed the host's selected chapter.");
                yield return Save("08-cooperative-lobby");
                ClickUiButton("Leave lobby", "Cooperative lobby");
                yield return Until(() => guest.State == null, "UI cooperative leave disconnects guest");
            }
            finally { guest.Leave(); Destroy(guestObject); }
            yield return null;
            RequireMenu("Chapter cooperative lobby leave");

            // Start the playable chapter through the full visible menu path.
            ClickUiButton("Chapters", "Main menu");
            yield return null;
            ClickUiButton("Select blackpine", "Chapter selection card");
            yield return null;
            ClickUiButton("Begin chapter", "Chapter selection card");
            yield return null;
            ClickUiButton("Begin operation", "Prepare operation card");
            yield return Until(() => session.State?.phase == "playing", "UI playable chapter start");
            Require(session.State.solo && session.State.chapterId == "blackpine", "Begin operation must start the chosen solo chapter.");
            player.SetInspectionPose(0, -12, 0, 0);
            yield return Save("09-gameplay-hud");

            // Populate the inventory through the authoritative pickup rules.
            foreach (string id in new[] { "tape-1", "fuse-1", "keycard-guard" })
            {
                var item = world.Objective(id);
                Require(item != null, "Missing inventory capture objective: " + id);
                float floorY = item.y >= DeadAirOfficeLayout.FloorHeight ? DeadAirOfficeLayout.FloorHeight : 0;
                player.SetInspectionPose(item.x, item.z, 0, 0, false, floorY);
                session.Command(new NetPacket { type = "interact", id = id });
                Require(session.State.Player(session.LocalId).HasItem(id), "The UI capture could not collect " + id);
            }
            player.SetInspectionPose(0, -12, 0, 0);
            ui.ToggleInventory();
            Require(ui.InventoryOpen && session.State.paused, "The solo inventory must pause the operation.");
            yield return Save("10-inventory");
            ui.ShowSettings();
            yield return Save("11-inventory-settings");
            Require(!UiButton("Inventory item 1", "Inventory card").IsInteractable(),
                "Inventory controls must reject keyboard navigation beneath settings.");
            yield return VerifyKeyboardFocus("Settings");
            ClickUiButton("Save settings", "Settings card");
            yield return null;
            Require(ui.InventoryOpen && session.State.paused,
                "Closing settings above inventory must preserve the inventory and solo pause.");
            RequireUiFocus("Inventory");
            ClickUiButton("Inventory item 2", "Inventory card");
            yield return null;
            ClickUiButton("Equip item", "Inventory card");
            var me = session.State.Player(session.LocalId);
            Require(!ui.InventoryOpen && !session.State.paused && me.selectedSlot == 2 && me.inventory[2] == "tape-1" &&
                !me.uniqueItemEquipped && !me.camera, "Equip item must select the chosen inventory slot and resume the operation.");

            player.SetPause(true);
            yield return Save("12-pause");
            ClickUiButton("Options", "Pause card");
            yield return null;
            Require(session.State.paused && player.LocalPaused, "Opening settings above pause must preserve the pause.");
            ClickUiButton("Save settings", "Settings card");
            yield return null;
            Require(session.State.paused && player.LocalPaused, "Closing nested pause settings must keep the pause menu.");
            ClickUiButton("Resume", "Pause card");
            Require(!session.State.paused && !player.LocalPaused, "Resume must close the last solo pause panel.");

            var console = world.Objective("broadcast");
            player.SetInspectionPose(console.x, console.z, 0, 0, false, DeadAirOfficeLayout.FloorHeight);
            ui.ShowCalibration();
            yield return Save("13-frequency-calibration");
            var dial = UiElement<UnityEngine.UI.Slider>("Frequency dial", "Frequency calibration card");
            for (int attempt = 0; attempt < 220 && Mathf.Abs(dial.value - 96.4f) > .05f; attempt++)
            {
                ClickUiButton(dial.value < 96.4f ? "Tune up" : "Tune down", "Frequency calibration card");
                yield return null;
            }
            Require(Mathf.Abs(dial.value - 96.4f) <= .05f, "The calibration buttons did not tune to 96.4 MHz.");
            ClickUiButton("Confirm frequency", "Frequency calibration card");
            Require(session.State.calibrated && !ui.HasModal && !session.State.paused,
                "Confirm frequency must calibrate the chosen chapter and resume solo gameplay.");

            player.EndInspection(); DefeatEveryone(session);
            yield return Until(() => session.State?.phase == "lost", "UI final defeat");
            yield return Save("14-final-defeat");
            ClickReportButton("Return to menu");
            yield return null; RequireMenu("Redesigned final report leave");
            yield return Save("15-returned-main-menu");
            yield return VerifyEndScreenNavigation();
            File.WriteAllText(report, "PASSED: real UI raycast clicks; playable/locked chapter selection; chapter preparation and solo start; settings/dossier/network/join; loopback chapter lobby and guest authority; HUD and inventory; nested solo modal pause; upper-modal keyboard navigation and returned inventory focus; selected item equip; pause resume; calibration button tuning/confirmation; solo and cooperative final-report exit/navigation.\n");
            Debug.Log("DEAD AIR HORROR MENU RUNTIME PASSED: chapter/menu/inventory/settings/calibration/solo and cooperative navigation.");
            Application.Quit(0);
        }

        void OnApplicationQuit() => RestoreCapturePreferences();

        void OnDestroy() => RestoreCapturePreferences();

        void RestoreCapturePreferences()
        {
            if(!preferencesCaptured)return;
            if(hadPlayerName)PlayerPrefs.SetString("DeadAir.PlayerName",originalPlayerName);else PlayerPrefs.DeleteKey("DeadAir.PlayerName");
            if(hadChapter)PlayerPrefs.SetString("DeadAir.Chapter",originalChapter);else PlayerPrefs.DeleteKey("DeadAir.Chapter");
            foreach(var slider in originalSliders)
                if(float.IsNaN(slider.Value))PlayerPrefs.DeleteKey(slider.Key);else PlayerPrefs.SetFloat(slider.Key,slider.Value);
            foreach(var toggle in originalToggles)
                if(toggle.Value.HasValue)PlayerPrefs.SetInt(toggle.Key,toggle.Value.Value);else PlayerPrefs.DeleteKey(toggle.Key);
            PlayerPrefs.Save();preferencesCaptured=false;
        }

        IEnumerator CaptureOffice()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=30;
            yield return new WaitForSecondsRealtime(1);
            session.Host("Office verification","engineer",true,true);
            session.Command(new NetPacket{type="start"});yield return new WaitForSecondsRealtime(.5f);
            WorldData data=WorldData.Load();
            player.SetInspectionPose(7,19.5f,.478f,-.136f);
            yield return Save("office-01-front-yard");
            var securityDoor=data.Door("door-security");
            Require(securityDoor!=null,"The office verification requires its security door.");
            InspectToward(securityDoor.x-Mathf.Cos(securityDoor.rotation)*1.2f,
                securityDoor.z+Mathf.Sin(securityDoor.rotation)*1.2f,securityDoor.y-1.4f,
                securityDoor.x,securityDoor.y,securityDoor.z);
            session.Command(new NetPacket{type="interact",id="door-security"});
            yield return new WaitForSecondsRealtime(.22f);
            var doorLeaf=GameObject.Find("door:door-security").GetComponent<DeadAirHingedDoor>();
            Require(Mathf.Abs(doorLeaf.Angle)>10&&Mathf.Abs(doorLeaf.Angle)<85,"A live door must swing through intermediate angles.");
            yield return new WaitForSecondsRealtime(.4f);
            Require(Mathf.Abs(doorLeaf.Angle)>89,"The live door failed to finish opening.");
            session.Command(new NetPacket{type="interact",id="door-security"});
            yield return new WaitForSecondsRealtime(.6f);
            Require(Mathf.Abs(doorLeaf.Angle)<.1f,"The live door failed to finish closing.");
            player.SetInspectionPose(0,-37,0,0);
            var controller=player.GetComponent<CharacterController>();
            float deadline=Time.realtimeSinceStartup+12;
            while(player.transform.position.z>-52.3f&&Time.realtimeSinceStartup<deadline)
            {
                controller.Move(new Vector3(0,-2,-2.6f)*Time.deltaTime);
                var position=player.transform.position;
                session.Command(new NetPacket{type="move",x=-position.x,y=position.y,z=position.z,yaw=0,flashlight=true});
                yield return null;
            }
            Require(player.transform.position.y>4.05f,"The real CharacterController could not climb the office stairs.");
            Require(session.State.Player(session.LocalId).y>4.05f,"The host rejected the upstairs movement height.");
            player.SetInspectionPose(0,-52.3f,Mathf.PI,0,false,4.2f);
            yield return Save("office-02-upper-landing");
            player.SetInspectionPose(0,-20,Mathf.PI,0,false,4.2f);
            yield return Save("office-03-upper-corridor");
            ui.ToggleMap();yield return Save("office-04-two-floor-map");ui.TryCloseTopModal();
            var upperTape=data.Objective("tape-2");
            var me=session.State.Player(session.LocalId);
            me.x=upperTape.x;me.z=upperTape.z;me.y=0;
            session.Command(new NetPacket{type="interact",id=upperTape.id});
            Require(!session.State.Collected(upperTape.id),"An upstairs item was collected through the downstairs ceiling.");
            me.y=4.2f;session.Command(new NetPacket{type="interact",id=upperTape.id});
            Require(session.State.Collected(upperTape.id),"An upstairs tape could not be collected on its own floor.");
            session.Command(new NetPacket{type="drop",id=upperTape.id});
            Require(session.State.droppedItems.Count==1&&session.State.droppedItems[0].y>4,"Dropped items lost their floor height.");
            var rear=data.Objective("power");
            var generator=Array.Find(data.props,p=>p.type=="dieselGenerator");
            player.SetInspectionPose((generator!=null?generator.x:rear.x)+1.5f,rear.z+5.8f,.253f,-.105f);
            yield return Save("office-05-rear-generator");
            foreach(var locker in data.lockers)
            {
            player.SetInspectionPose(locker.exitX,locker.exitZ,locker.FacingYaw+Mathf.PI,0,false,locker.y);
            player.EndInspection();yield return null;
            session.Command(new NetPacket{type="interact",id=locker.id});
            yield return new WaitForSecondsRealtime(1.3f);
            Require(session.State.Player(session.LocalId).hiding==locker.id,"Locker entry failed.");
            var outward=new Vector3(locker.x-locker.exitX,0,locker.exitZ-locker.z).normalized;
            Require(Vector3.Dot(player.ViewCamera.transform.forward,outward)>.97f,"The hidden camera is facing the locker wall.");
            Require(!Physics.Raycast(player.ViewCamera.transform.position,player.ViewCamera.transform.forward,.65f,~0,QueryTriggerInteraction.Ignore),"The locker grille blocks the eye-level sight line.");
            if(locker==data.lockers[0])yield return Save("office-06-hidden-grille");
            if(locker==data.lockers[2])yield return Save("office-07-upper-locker-grille");
            session.Command(new NetPacket{type="interact",id="exit-locker"});
            yield return new WaitForSecondsRealtime(1.3f);
            Require(string.IsNullOrEmpty(session.State.Player(session.LocalId).hiding),"Locker exit failed.");
            var exitPosition=new Vector3(-locker.exitX,locker.ExitFloorY,locker.exitZ);
            Require(Vector3.Distance(player.transform.position,exitPosition)<.2f,"Locker "+locker.id+" exit placement: actual "+player.transform.position+", expected "+exitPosition);
            Debug.Log("DEAD AIR LOCKER RUNTIME PASSED: "+locker.id+" exit="+player.transform.position);
            }
            // Descend the same physical flight from its upper landing.
            player.SetInspectionPose(0,-52.3f,Mathf.PI,0,false,4.2f);
            deadline=Time.realtimeSinceStartup+12;
            while(player.transform.position.z<-37.5f&&Time.realtimeSinceStartup<deadline)
            {
                controller.Move(new Vector3(0,-4,2.6f)*Time.deltaTime);
                var position=player.transform.position;
                session.Command(new NetPacket{type="move",x=-position.x,y=position.y,z=position.z,yaw=Mathf.PI,flashlight=true});
                yield return null;
            }
            Require(player.transform.position.y<.25f,"The real CharacterController could not descend the office stairs.");
            Require(session.State.Player(session.LocalId).y<.25f,"The host rejected the downstairs movement height.");
            File.WriteAllText(Path.Combine(output,"office-runtime-report.txt"),"PASSED: live door intermediate/open/closed positions; physical stairs up/down; host floor movement; two-panel held map; no cross-floor pickup; upstairs item drop; all eight lockers entry/outward camera/open grille/exit on both floors.\n");
            Debug.Log("DEAD AIR OFFICE RUNTIME PASSED: stairs, floor rules, map, drops and locker sightline.");
            Application.Quit(0);
        }

        void InitializeOffscreenCapture()
        {
            // A hidden Windows batch player has no presented backbuffer. Render
            // the same game camera and UI explicitly into a real GPU target.
            captureFrame=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            captureFrame.Create();
            captureCanvas=GameObject.Find("BUREAU-9 interface").GetComponent<Canvas>();
            foreach(var item in captureCanvas.GetComponentsInChildren<Transform>(true))item.gameObject.layer=5;
            captureUI=new GameObject("Capture UI camera").AddComponent<Camera>();
            captureUI.enabled=false;captureUI.orthographic=true;captureUI.cullingMask=1<<5;
            captureUI.clearFlags=CameraClearFlags.Depth;captureUI.nearClipPlane=.1f;captureUI.farClipPlane=10;
            captureUI.targetTexture=captureFrame;
            captureCanvas.renderMode=RenderMode.ScreenSpaceCamera;captureCanvas.worldCamera=captureUI;captureCanvas.planeDistance=1;
            player.ViewCamera.enabled=false;player.ViewCamera.cullingMask&=~(1<<5);
            player.ViewCamera.clearFlags=CameraClearFlags.SolidColor;
            captureWorld=FindFirstObjectByType<DeadAirBootstrap>().World;
            frameRenderer=player.ViewCamera.GetComponent<DeadAirFrameRenderer>();
            frameRenderer.OutputCamera.enabled=false;frameRenderer.OutputCamera.targetTexture=captureFrame;
        }

        void LateUpdate()
        {
            if(captureFrame==null)return;
            Canvas.ForceUpdateCanvases();
            frameRenderer.EnsureTargets();
            captureWorld.PrepareRender(player.ViewCamera);
            player.ViewCamera.Render();if(!captureWithoutEquipment)frameRenderer.RenderEquipment();frameRenderer.OutputCamera.Render();captureUI.Render();
        }

        IEnumerator Capture()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            yield return new WaitForSecondsRealtime(2);
            var audio = FindFirstObjectByType<DeadAirAudio>();
            if (audio != null)
            {
                try { audio.ValidateInstruments(); }
                catch (Exception error) { Debug.LogException(error); Application.Quit(3); yield break; }
            }
            yield return Save("01-menu");
            OpenRequiredPanel(ui, "ShowSettings");
            yield return Save("01b-settings");
            ui.TryCloseTopModal();
            OpenRequiredPanel(ui, "ShowDossier");
            yield return Save("01c-dossier");
            ui.TryCloseTopModal();
            session.Host("Victor", "engineer", true, true);
            session.Command(new NetPacket { type = "start" });
            yield return new WaitForSecondsRealtime(.5f);
            player.SetInspectionPose(-5, 19, -.3671738f, .0035897f);
            yield return Save("02-courtyard");
            player.SetInspectionPose(0, 1, 0, 0);
            yield return Save("03-corridor");
            player.SetInspectionPose(-5, -1, .9272952f, -.1292768f);
            yield return Save("04-archive");
            player.SetInspectionPose(4, -2, -Mathf.PI * .5f, -.03f, true);
            yield return Save("05-control-room");
            ui.ToggleRadio();
            yield return Save("05b-radio");
            ui.HideRadio();
            player.SetPause(true);
            yield return Save("06-pause");
            ui.ToggleMap();
            yield return Save("07-map");
            ui.TryCloseTopModal();
            player.SetPause(false);
            player.SetInspectionPose(8.5f, -2, -Mathf.PI * .5f, -.03f);
            ui.ShowCalibration();
            yield return Save("08-calibration");
            ui.TryCloseTopModal();
            player.EndInspection();
            yield return CaptureCharacterEquipment();
            yield return VerifyEndScreenNavigation();
            Debug.Log("DEAD AIR VISUAL CAPTURE COMPLETE: " + output);
            Application.Quit(0);
        }

        IEnumerator CaptureCharacterEquipment()
        {
            foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
            {
                ui.ReturnToMenu();
                session.Host(role, role, true, true);
                session.Command(new NetPacket { type = "start" });
                yield return null;
                player.SetInspectionPose(0, 4, 0, 0);
                yield return Save("character-" + role + "-flashlight");
                var selectedActor=session.State.Player(session.LocalId);
                Require(selectedActor.inventory.Count>1&&selectedActor.inventory[1]==DeadAirRoleItems.ForClass(role),"Missing role item inventory slot: "+role);
                session.Command(new NetPacket { type = "slot", phrase = 1 });
                Require(selectedActor.selectedSlot==1&&selectedActor.uniqueItemEquipped,"Selecting the role inventory slot did not equip it: "+role);
                yield return Save("character-" + role + "-personal-item");
                int sequence = session.State.Player(session.LocalId).uniqueActionSequence;
                session.Command(new NetPacket { type = "unique-use" });
                Require(session.State.Player(session.LocalId).uniqueActionSequence == sequence + 1, "Personal action not accepted: " + role);
                yield return new WaitForSecondsRealtime(role == "operator" ? 2.2f : .4f);
                yield return Save("character-" + role + "-use");
                if (role == "operator")
                {
                    Require(session.State.Player(session.LocalId).cigarettesRemaining == 11, "Smoking must consume one cigarette");
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Save("character-operator-smoke");
                    ui.ToggleMap();
                    Require(session.State.Player(session.LocalId).mapOpen && !session.State.Player(session.LocalId).uniqueItemEquipped,
                        "The held map must replace the personal item in authoritative state");
                    yield return Save("character-held-map");
                    ui.TryCloseTopModal();
                    player.SetInspectionPose(0, 4, 0, 0, true);
                    yield return Save("character-held-camera");
                }
                player.EndInspection();
            }
            ui.ReturnToMenu();
        }

        IEnumerator VerifyEndScreenNavigation()
        {
            ui.ReturnToMenu();
            foreach(string exit in new[]{"Return to menu","Close","Escape"})
            {
                session.Host("Solo check", "engineer", true, false);
                session.Command(new NetPacket{type="start"});
                yield return null;
                if(exit=="Return to menu")
                {
                    ui.ToggleMap();Require(session.State.paused,"Solo map must pause the game");
                    ui.ShowSettings();ui.TryCloseTopModal();
                    Require(session.State.paused,"Closing settings above the map must preserve the pause");
                    ui.TryCloseTopModal();Require(!session.State.paused,"Closing the last solo modal must resume");
                }
                DefeatEveryone(session);
                yield return Until(()=>session.State?.phase=="lost","Solo defeat");
                if(exit=="Return to menu")yield return Save("09-solo-defeat");
                if(exit=="Escape")Require(ui.TryCloseTopModal(),"Escape must handle the defeat screen");
                else ClickReportButton(exit);
                yield return null;
                RequireMenu("Solo "+exit);
            }

            var peerObject=new GameObject("Capture LAN guest");
            var peer=peerObject.AddComponent<DeadAirSession>();peer.Initialize(WorldData.Load());
            session.Host("Host check","engineer",false,false);
            peer.Join("127.0.0.1",session.State.code,"Guest check","operator");
            yield return Until(()=>peer.State!=null&&session.State.players.Count==2,"Guest join");
            session.Command(new NetPacket{type="start"});
            yield return Until(()=>peer.State?.phase=="playing","Coop start");
            peer.Command(new NetPacket { type = "slot", phrase = 1 });
            peer.Command(new NetPacket { type = "unique-use" });
            yield return Until(()=>session.State.Player(peer.LocalId)?.uniqueActionSequence==1&&peer.State.Player(peer.LocalId)?.uniqueActionSequence==1,"Coop personal action replication");
            Require(session.State.Player(peer.LocalId).cigarettesRemaining==11&&peer.State.Player(peer.LocalId).cigarettesRemaining==11,"Coop cigarette count differs between host and client");
            var guestPose = session.State.Player(peer.LocalId);
            player.SetInspectionPose(guestPose.x, guestPose.z - 3.5f, Mathf.PI, -.12f);
            yield return new WaitForSecondsRealtime(3.4f);
            yield return Save("character-coop-smoking");
            peer.Command(new NetPacket { type = "map-equip", mapOpen = true });
            yield return Until(() => session.State.Player(peer.LocalId).mapOpen, "Coop held map replication");
            yield return Save("character-coop-map");
            ValidateRemoteGrip(peer.LocalId, "map");
            peer.Command(new NetPacket { type = "map-equip", mapOpen = false });
            peer.Command(new NetPacket { type = "move", x = guestPose.x, z = guestPose.z, yaw = guestPose.yaw,
                pitch = guestPose.pitch, camera = true, flashlight = true });
            yield return Until(() => session.State.Player(peer.LocalId).camera, "Coop held camera replication");
            yield return Save("character-coop-camera");
            ValidateRemoteGrip(peer.LocalId, "camera");
            player.EndInspection();
            player.SetPause(true);Require(!session.State.paused,"Host menu must not freeze a coop session");player.SetPause(false);
            DefeatEveryone(session);
            yield return Until(()=>session.State?.phase=="lost"&&peer.State?.phase=="lost","Coop host defeat");
            yield return Save("10-coop-host-defeat");
            ClickReportButton("Return to menu");yield return null;RequireMenu("Coop host");
            yield return Until(()=>peer.State==null,"Host exit must disconnect guests");
            peer.Leave();Destroy(peerObject);

            peerObject=new GameObject("Capture LAN host");
            peer=peerObject.AddComponent<DeadAirSession>();peer.Initialize(WorldData.Load());
            peer.Host("Remote host","engineer",false,false);
            session.Join("127.0.0.1",peer.State.code,"Local guest","operator");
            yield return Until(()=>session.State!=null&&peer.State.players.Count==2,"Local guest join");
            peer.Command(new NetPacket{type="start"});
            yield return Until(()=>session.State?.phase=="playing","Guest start");
            DefeatEveryone(peer);
            yield return Until(()=>session.State?.phase=="lost","Coop guest defeat");
            yield return Save("11-coop-guest-defeat");
            Require(!ReportButton("Restart").gameObject.activeInHierarchy,"Guest must not restart the host's game");
            var fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var closedPeer=typeof(DeadAirSession).GetField("server",fields).GetValue(session);
            var lateSnapshot=new NetPacket{type="state",stateJson=JsonUtility.ToJson(session.State)};
            ClickReportButton("Return to menu");yield return null;RequireMenu("Coop guest");
            typeof(DeadAirSession).GetMethod("ClientPacket",fields).Invoke(session,new object[]{closedPeer,lateSnapshot});
            yield return null;RequireMenu("Late packet after guest exit");
            peer.Leave();Destroy(peerObject);
            yield return Save("12-returned-to-menu");
            Debug.Log("DEAD AIR MENU REGRESSION PASSED: solo button/close/Escape, host defeat, guest defeat, nested solo pause and coop pause.");
        }

        void ValidateRemoteGrip(string id, string mode)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var views = (Dictionary<string, GameObject>)typeof(DeadAirPlayer).GetField("remoteViews", flags).GetValue(player);
            Require(views.TryGetValue(id, out var model), "Missing remote model for " + mode + " grip");
            var motion = model.GetComponent<DeadAirCharacterMotion>();
            Require(motion != null && !float.IsNaN(motion.GripContactError) && motion.GripContactError <= .01f,
                "Remote hands must contact the " + mode + " within one centimetre");
            Debug.Log("DEAD AIR REMOTE GRIP PASSED: " + mode + " errorMetres=" + motion.GripContactError);
        }

        static void DefeatEveryone(DeadAirSession game)
        {
            game.State.enemy.active=true;
            foreach(var actor in game.State.players){actor.down=true;actor.sanity=0;}
        }

        IEnumerator Until(Func<bool> ready,string label)
        {
            float deadline=Time.realtimeSinceStartup+15;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(ready(),label+" timed out");
            yield return null;
        }

        UnityEngine.UI.Button ReportButton(string name)
        {
            var canvas=GameObject.Find("BUREAU-9 interface");
            foreach(var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if(button.name==name&&button.transform.parent.name=="Final report card")return button;
            throw new InvalidOperationException("Missing result-screen button: "+name);
        }

        T UiElement<T>(string name, string parentName) where T : Component
        {
            var canvas = GameObject.Find("BUREAU-9 interface");
            foreach (var element in canvas.GetComponentsInChildren<T>(true))
                if (element.name == name && element.transform.parent.name == parentName) return element;
            throw new InvalidOperationException("Missing UI element: " + parentName + "/" + name);
        }

        UnityEngine.UI.Button UiButton(string name, string parentName) => UiElement<UnityEngine.UI.Button>(name, parentName);

        void ClickUiButton(string name, string parentName) => ClickVerifiedButton(UiButton(name, parentName), parentName + "/" + name);

        void ClickReportButton(string name)
        {
            ClickVerifiedButton(ReportButton(name), "Final report card/" + name);
        }

        void ClickVerifiedButton(UnityEngine.UI.Button button, string name)
        {
            Require(button.isActiveAndEnabled&&button.IsInteractable(),"UI button unavailable: "+name);
            Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;
            var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(captureUI,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            string topHit=hits.Count>0?hits[0].gameObject.name:"<none>";
            Require(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),"UI button blocked by another panel: "+name+"; top hit="+topHit);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }

        IEnumerator VerifyKeyboardFocus(string panelName)
        {
            foreach(var direction in new[]{MoveDirection.Down,MoveDirection.Right,MoveDirection.Up,MoveDirection.Left})
            {
                RequireUiFocus(panelName);
                var move=new AxisEventData(EventSystem.current){moveDir=direction,
                    moveVector=direction==MoveDirection.Down?Vector2.down:direction==MoveDirection.Up?Vector2.up:direction==MoveDirection.Left?Vector2.left:Vector2.right};
                ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject,move,ExecuteEvents.moveHandler);
                RequireUiFocus(panelName);
                yield return null;
            }
        }

        void RequireUiFocus(string panelName)
        {
            // Main-menu buttons share names with some modal roots (Settings,
            // Chapters). Resolve the modal card first rather than that button.
            var panel=GameObject.Find(panelName+" card");
            if(panel==null)panel=GameObject.Find(panelName);
            var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            var control=selected!=null?selected.GetComponent<UnityEngine.UI.Selectable>():null;
            Require(panel!=null&&selected!=null&&selected.transform.IsChildOf(panel.transform)&&control!=null&&control.IsActive()&&control.IsInteractable(),
                "Keyboard focus must stay in "+panelName+"; actual="+(selected!=null?selected.name:"<none>"));
        }

        void RequireMenu(string label)
        {
            Require(session.State==null&&GameObject.Find("Main menu")!=null&&Cursor.visible&&Cursor.lockState==CursorLockMode.None,label+" did not return to an interactive main menu");
        }

        static void Require(bool condition,string message)
        {
            if(condition)return;
            Debug.LogError("DEAD AIR MENU REGRESSION FAILED: "+message);Application.Quit(4);
            throw new InvalidOperationException(message);
        }

        static void OpenRequiredPanel(DeadAirUI ui, string method)
        {
            var action=typeof(DeadAirUI).GetMethod(method);
            if(action==null)throw new MissingMethodException("The capture requires a working UI panel: "+method);
            action.Invoke(ui,null);
        }

        IEnumerator Save(string name,bool settle=true)
        {
            // A few rendered frames let UI/state listeners and realtime lights settle.
            for (int frame = 0; frame < (settle?5:1); frame++) yield return null;
            float seconds = 0, slowest = 0;
            int measuredFrames = settle?8:1;
            for (int frame = 0; frame < measuredFrames; frame++)
            {
                yield return null;
                seconds += Time.unscaledDeltaTime;
                slowest = Mathf.Max(slowest, Time.unscaledDeltaTime);
            }
            Debug.Log("DEAD AIR FRAME TIMING: " + name + " averageMs=" + (seconds * 1000 / measuredFrames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " slowestMs=" + (slowest * 1000).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " fps=" + (measuredFrames / Mathf.Max(.0001f, seconds)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " captureCap=30");
            string path = Path.Combine(output, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            if(captureFrame!=null)
            {
                var previous=RenderTexture.active;RenderTexture.active=captureFrame;
                var pixels=new Texture2D(captureFrame.width,captureFrame.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,captureFrame.width,captureFrame.height),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());Destroy(pixels);RenderTexture.active=previous;
            }
            else ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 15;
            do { yield return null; } while (!File.Exists(path) && Time.realtimeSinceStartup < deadline);
            if (!File.Exists(path)) { Debug.LogError("Screenshot failed: " + path); Application.Quit(2); yield break; }
            yield return null;
            Debug.Log("DEAD AIR SCREENSHOT: " + path);
        }
    }
}
