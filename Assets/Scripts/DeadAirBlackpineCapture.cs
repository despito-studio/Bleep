using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeadAir
{
    /// <summary>Opt-in real socket smoke test and rendered Blackpine review frames.</summary>
    [DefaultExecutionOrder(2000)]
    public sealed class DeadAirBlackpineCapture : MonoBehaviour
    {
        const string Argument = "-deadAirBlackpineCapture";
        DeadAirSession session, guest;
        DeadAirPlayer player;
        DeadAirUI ui;
        WorldData world;
        string output;
        GameObject guestObject;
        Camera captureUI;
        Canvas captureCanvas;
        RenderTexture captureFrame, originalOutputTarget;
        Texture2D readback;
        DeadAirFrameRenderer frameRenderer;
        DeadAirWorld captureWorld;
        DeadAirFarmyard captureFarmyard;
        DeadAirScarePresentation captureScare;
        RenderMode originalCanvasMode;
        Camera originalCanvasCamera;
        float originalCanvasPlane;
        bool originalWorldEnabled, originalOutputEnabled;
        int originalWorldMask;
        CameraClearFlags originalClearFlags;
        readonly Dictionary<GameObject, int> originalUILayers = new Dictionary<GameObject, int>();
        readonly List<byte[]> frameSignatures = new List<byte[]>();
        readonly List<string> checks = new List<string>();
        readonly List<string> frames = new List<string>();
        readonly List<string> errors = new List<string>();
        float startedAt;
        bool finished;

        public static bool IsRequested => Array.IndexOf(Environment.GetCommandLineArgs(), Argument) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int argument = Array.IndexOf(args, Argument);
            if (argument < 0) return;
            if (argument + 1 >= args.Length || !Path.IsPathRooted(args[argument + 1]))
            {
                Debug.LogError(Argument + " requires an absolute output folder.");
                Application.Quit(2);
                return;
            }
            var capture = new GameObject("Opt-in Blackpine socket smoke and review capture").AddComponent<DeadAirBlackpineCapture>();
            capture.output = args[argument + 1];
            Directory.CreateDirectory(capture.output);
            capture.startedAt = Time.realtimeSinceStartup;
            Application.logMessageReceived += capture.ObserveLog;
            capture.StartCoroutine(capture.Guard(capture.Run()));
        }

        IEnumerator Guard(IEnumerator routine)
        {
            var routines = new Stack<IEnumerator>();
            routines.Push(routine);
            while (routines.Count > 0 && !finished)
            {
                bool more;
                object current = null;
                try
                {
                    more = routines.Peek().MoveNext();
                    if (more) current = routines.Peek().Current;
                }
                catch (Exception error)
                {
                    Finish(false, error.ToString());
                    yield break;
                }
                if (!more) { routines.Pop(); continue; }
                if (current is IEnumerator nested) routines.Push(nested);
                else yield return current;
            }
        }

        void Update()
        {
            if (!finished && output != null && Time.realtimeSinceStartup - startedAt > 180)
                Finish(false, "Capture watchdog expired after 180 seconds.");
        }

        void InitializeOffscreenCapture()
        {
            frameRenderer = player.ViewCamera.GetComponent<DeadAirFrameRenderer>();
            Require(frameRenderer != null && frameRenderer.OutputCamera != null,
                "The real HDR world and VHS presentation cameras are available.");
            captureWorld = FindFirstObjectByType<DeadAirBootstrap>().World;
            captureFarmyard = FindFirstObjectByType<DeadAirFarmyard>();
            var interfaceObject = GameObject.Find("BUREAU-9 interface");
            Require(interfaceObject != null && interfaceObject.GetComponent<Canvas>() != null, "The real game canvas is available for GPU capture.");
            captureCanvas = interfaceObject.GetComponent<Canvas>();
            originalCanvasMode = captureCanvas.renderMode;
            originalCanvasCamera = captureCanvas.worldCamera;
            originalCanvasPlane = captureCanvas.planeDistance;
            originalWorldEnabled = player.ViewCamera.enabled;
            originalWorldMask = player.ViewCamera.cullingMask;
            originalClearFlags = player.ViewCamera.clearFlags;
            originalOutputEnabled = frameRenderer.OutputCamera.enabled;
            originalOutputTarget = frameRenderer.OutputCamera.targetTexture;
            captureFrame = new RenderTexture(Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height), 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "Blackpine review / actual world, VHS and UI", hideFlags = HideFlags.DontSave };
            Require(captureFrame.Create(), "A real GPU review target is allocated.");
            readback = new Texture2D(captureFrame.width, captureFrame.height, TextureFormat.RGB24, false)
            { name = "Blackpine review / validated readback", hideFlags = HideFlags.DontSave };
            foreach (var item in captureCanvas.GetComponentsInChildren<Transform>(true))
            { originalUILayers[item.gameObject] = item.gameObject.layer; item.gameObject.layer = 5; }
            captureUI = new GameObject("Blackpine review UI camera").AddComponent<Camera>();
            captureUI.enabled = false; captureUI.orthographic = true; captureUI.cullingMask = 1 << 5;
            captureUI.clearFlags = CameraClearFlags.Depth; captureUI.nearClipPlane = .1f; captureUI.farClipPlane = 10;
            captureUI.targetTexture = captureFrame;
            captureCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            captureCanvas.worldCamera = captureUI; captureCanvas.planeDistance = 1;
            player.ViewCamera.enabled = false;
            player.ViewCamera.cullingMask &= ~(1 << 5);
            player.ViewCamera.clearFlags = CameraClearFlags.SolidColor;
            frameRenderer.OutputCamera.enabled = false;
            frameRenderer.OutputCamera.targetTexture = captureFrame;
            captureScare = FindFirstObjectByType<DeadAirScarePresentation>();
        }

        void LateUpdate()
        {
            if (finished || captureFrame == null) return;
            try { RenderOffscreen(); }
            catch (Exception error) { Finish(false, "GPU review rendering failed: " + error); }
        }

        void RenderOffscreen()
        {
            // Batch players have no reliable presented backbuffer. These are the actual
            // game cameras and shader chain, composed into a persistent GPU target.
            Canvas.ForceUpdateCanvases();
            frameRenderer.EnsureTargets();
            captureWorld.PrepareRender(player.ViewCamera);
            captureFarmyard?.PrepareOwnerCarry(player.ViewCamera);
            player.ViewCamera.Render();
            frameRenderer.RenderEquipment();
            frameRenderer.OutputCamera.Render();
            captureUI.Render();
            captureScare?.RenderFullscreen(captureFrame);
        }

        IEnumerator Run()
        {
            yield return Until(() => FindFirstObjectByType<DeadAirSession>() != null &&
                FindFirstObjectByType<DeadAirPlayer>() != null && FindFirstObjectByType<DeadAirUI>() != null,
                "Gameplay bootstrap becomes available", 20);
            session = FindFirstObjectByType<DeadAirSession>();
            player = FindFirstObjectByType<DeadAirPlayer>();
            ui = FindFirstObjectByType<DeadAirUI>();
            world = WorldData.Load();
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "Rendered review frames require an actual graphics device; omit -nographics.");
            yield return new WaitForSecondsRealtime(.5f);
            yield return SocketSmoke();
            yield return RenderReview();
            Finish(errors.Count == 0, errors.Count == 0 ? null : "Unity logged runtime errors; see blackpine-runtime-errors.txt.");
        }

        IEnumerator SocketSmoke()
        {
            session.Host("Blackpine smoke host", "engineer", false, true, DeadAirChapters.DefaultId);
            Require(string.IsNullOrEmpty(session.LastError), "Host opens the actual LAN port: " + session.LastError);
            guestObject = new GameObject("Blackpine smoke / real loopback guest");
            guest = guestObject.AddComponent<DeadAirSession>();
            guest.Initialize(world);
            guest.Join("127.0.0.1", session.State.code, "Blackpine smoke guest", "operator");
            yield return Until(() => guest.State != null && !string.IsNullOrEmpty(guest.LocalId) && session.State.players.Count == 2,
                "Actual TCP guest joins and receives a room snapshot");
            string guestId = guest.LocalId;
            Require(guest.State.code == session.State.code && guest.State.chapterId == DeadAirChapters.DefaultId,
                "Socket guest receives the same room code and Blackpine chapter.");
            session.Command(new NetPacket { type = "start" });
            yield return Until(() => guest.State?.phase == "playing", "Socket guest receives the start snapshot");
            // Fixtures use real authoritative player positions; they do not simulate a physical walkthrough.
            session.State.elapsed = DeadAirVanPresentation.ArrivalDuration + 2;
            var chicken = DeadAirFarmyard.Position(world, session.State, DeadAirFarmyard.ChickenId);
            Inspect(chicken.x + .65f, chicken.z + 1, 0, chicken.x, .5f, chicken.z);
            var remote = session.State.Player(guestId);
            Place(remote, chicken.x - .65f, 0, chicken.z + 1);
            guest.Command(new NetPacket { type = "interact", id = DeadAirFarmyard.ChickenId });
            yield return Until(() => session.State.blackpine.chickenCarrierId == guestId &&
                guest.State?.blackpine?.chickenCarrierId == guestId, "Guest picks up hen through the actual TCP command path");
            session.Command(new NetPacket { type = "interact", id = DeadAirFarmyard.ChickenId });
            yield return Until(() => guest.State?.blackpine?.chickenCarrierId == session.LocalId,
                "Host receives hen from nearby guest and the handoff reaches the guest snapshot");
            session.Command(new NetPacket { type = "interact", id = "farm-chicken-drop" });
            yield return Until(() => guest.State?.blackpine != null && guest.State.blackpine.chickenDropped &&
                string.IsNullOrEmpty(guest.State.blackpine.chickenCarrierId), "Dropping the hen replicates across the socket");
            Require(Mathf.Abs(guest.State.blackpine.chickenX - session.State.Player(session.LocalId).x) < .01f,
                "Dropped hen retains its authoritative position.");
            guest.Command(new NetPacket { type = "interact", id = DeadAirFarmyard.ChickenId });
            yield return Until(() => session.State.blackpine.chickenCarrierId == guestId,
                "Guest takes the dropped hen for disconnect recovery");

            PrepareReceiver(session.State);
            var receiver = world.Objective("broadcast");
            Inspect(receiver.x, receiver.z + 1, 4.2f, receiver.x, receiver.y, receiver.z);
            session.Command(new NetPacket { type = "calibrate", frequency = DeadAirBlackpine.TargetFrequency(session.State) });
            yield return Until(() => guest.State?.blackpine != null && guest.State.blackpine.interceptProgress > 1 &&
                guest.State.blackpine.operationId == "broadcast", "In-progress receiver capture is replicated to the real guest");
            File.WriteAllText(Path.Combine(output, "socket-midpoint-snapshot.json"), JsonUtility.ToJson(guest.State, true));
            float held = session.State.blackpine.interceptProgress;
            Inspect(0, -9, 4.2f, 0, 5.4f, -18);
            yield return Until(() => string.IsNullOrEmpty(session.State.blackpine.operationId),
                "Leaving the receiver pauses its operation");
            yield return new WaitForSecondsRealtime(.25f);
            Require(session.State.blackpine.interceptProgress >= held && session.State.blackpine.interceptProgress < DeadAirBlackpine.CaptureSeconds,
                "Receiver progress survives interruption.");
            Inspect(receiver.x, receiver.z + 1, 4.2f, receiver.x, receiver.y, receiver.z);
            session.Command(new NetPacket { type = "interact", id = "broadcast" });
            yield return Until(() => session.State.blackpine.intercepted && guest.State?.blackpine?.intercepted == true,
                "Resumed receiver capture completes and reaches the actual guest", 14);
            Require(!session.State.broadcast && !guest.State.broadcast && guest.State.blackpine.echoSerial > 0,
                "The midpoint interception produces an echo and leaves extraction unfinished.");
            File.WriteAllText(Path.Combine(output, "socket-intercepted-snapshot.json"), JsonUtility.ToJson(guest.State, true));

            remote = session.State.Player(guestId);
            remote.inventory.Add("key-main");
            remote.inventory.Add("ladder");
            guest.Leave();
            yield return Until(() => session.State.players.Count == 1 && session.State.blackpine.chickenDropped &&
                string.IsNullOrEmpty(session.State.blackpine.chickenCarrierId), "Actual socket disconnect releases the guest's hen");
            Require(HasDropped("key-main") && HasDropped("ladder"), "Disconnect recovery preserves the mission key and ladder.");
            Destroy(guestObject); guestObject = null; guest = null;
            session.Leave();
            player.EndInspection();
            yield return null;
            checks.Add("Socket smoke uses real TcpListener/TcpClient sessions with protocol " + DeadAirSession.NetworkProtocol +
                "; player locations and receiver prerequisites are explicitly staged fixtures.");
        }

        bool HasDropped(string item)
        {
            foreach (var dropped in session.State.droppedItems)
                if (dropped.itemId == item) return true;
            return false;
        }

        IEnumerator RenderReview()
        {
            session.Host("Blackpine visual review", "engineer", true, true, DeadAirChapters.DefaultId);
            session.Command(new NetPacket { type = "start" });
            yield return Until(() => session.State?.phase == "playing", "Fresh solo creative run starts for review frames");
            InitializeOffscreenCapture();
            session.State.elapsed = DeadAirVanPresentation.ArrivalDuration + 2;
            var me = session.State.Player(session.LocalId);
            me.inventory.Add("tape-1"); me.selectedSlot = -1;
            me.uniqueItemEquipped = false;
            ui.SetBlackpineCaptureHud(false);
            Inspect(0, 35, .15f, 0, 3.9f, -7);
            yield return Save("01-snowy-front-overview");
            var winter = FindFirstObjectByType<DeadAirWinterWorld>();
            Require(winter != null && winter.WinterActive && winter.CaretakerCanopyCount == 2 && winter.ParticleBudget <= 640,
                "Live winter is active, capped and aware of both collider-free animal shelters.");

            Inspect(-15.7f, -61.1f, .05f, -20.1f, .8f, -68.5f);
            yield return Save("02-farmyard-goat-and-hen");
            var goat = DeadAirFarmyard.Position(world, session.State, DeadAirFarmyard.GoatId);
            Inspect(goat.x + 2.3f, goat.z + .8f, .02f, goat.x, .65f, goat.z);
            yield return Save("03-goat-close-view");

            var chicken = DeadAirFarmyard.Position(world, session.State, DeadAirFarmyard.ChickenId);
            Inspect(chicken.x + .7f, chicken.z + 1, 0, chicken.x, .48f, chicken.z);
            session.Command(new NetPacket { type = "interact", id = DeadAirFarmyard.ChickenId });
            yield return Until(() => DeadAirFarmyard.IsCarrying(session.State, me), "Solo host picks up the rendered hen");
            Require(me.SelectedItem == null, "Carrying the hen leaves the actual equipment slot empty.");
            Inspect(-19.4f, -67, 0, -20.5f, 1.15f, -72.3f);
            ui.SetBlackpineCaptureHud(true);
            yield return Save("04-hen-carried-first-person");
            session.Command(new NetPacket { type = "interact", id = "farm-chicken-drop" });

            PrepareReceiver(session.State);
            foreach (var id in new[] { "tape-2", "tape-3", "fuse-1", "fuse-2", "key-main", "key-basement" })
                if (!me.inventory.Contains(id)) me.inventory.Add(id);
            me.selectedSlot = me.inventory.IndexOf("tape-3");
            Inspect(0, -9, 4.2f, 0, 5.5f, -20);
            ui.ToggleInventory();
            yield return Until(() => ui.InventoryOpen, "The real field journal opens");
            yield return Save("05-evidence-field-journal");
            ui.TryCloseTopModal();

            var receiver = world.Objective("broadcast");
            Inspect(receiver.x, receiver.z + 1, 4.2f, receiver.x, receiver.y, receiver.z);
            ui.ShowCalibration();
            yield return Until(() => ui.HasModal, "The actual receiver tuning panel opens");
            yield return Save("06-receiver-tuning-panel");
            ui.TryCloseTopModal();
            session.State.blackpine.intercepted = true;
            session.State.calibrated = session.State.quest.calibrated = true;
            session.State.blackpine.interceptProgress = DeadAirBlackpine.CaptureSeconds;
            session.State.blackpine.operationId = session.State.blackpine.operationPlayerId = null;
            me.selectedSlot = me.inventory.IndexOf("tape-1");
            ui.SetBlackpineCaptureHud(false);
            Inspect(18.4f, -64, .1f, 21, 3.05f, -59);
            yield return Save("07-rear-yard-antenna");

            session.State.blackpine.antennaDisconnected = true;
            me.selectedSlot = me.inventory.IndexOf("flashlight");
            Inspect(2.5f, -35.25f, -4.2f, 2.5f, -2.85f, -38);
            ui.SetBlackpineCaptureHud(true);
            yield return Save("08-basement-resonator");

            me.selectedSlot = me.inventory.IndexOf("tape-1");
            Inspect(-13, -9, 4.2f, 12, 5.65f, -9);
            yield return Save("09-disturbed-office-after-interception");
            ui.ShowBlackpineEvidence("tape-2");
            yield return Save("10-evidence-playback-subtitles");
            checks.Add("Ten review images render the real HDR world, equipment, VHS presentation and UI cameras into a GPU target; every readback passes luminance, color and distinct-frame checks.");
            checks.Add("Review images stage chapter flags and camera locations after the socket smoke. They are visual review fixtures, not a full physical walkthrough.");
            checks.Add("Capture writes no PlayerPrefs and does not alter graphics, audio, display or accessibility preferences.");
            ui.SetBlackpineCaptureHud(true);
        }

        static void PrepareReceiver(RoomState state)
        {
            if(!state.canisters.Contains("fuel-canister"))state.canisters.Add("fuel-canister");
            state.canister=state.quest.fuelAdded=true;
            state.power = state.quest.power = true;
            state.breakerOn = state.quest.breakerOn = true;
            state.ladderPlaced = state.quest.ladderPlaced = true;
            foreach (var id in new[] { "tape-1", "tape-2", "tape-3" }) if (!state.tapes.Contains(id)) state.tapes.Add(id);
            foreach (var id in new[] { "fuse-1", "fuse-2" }) if (!state.fuses.Contains(id)) state.fuses.Add(id);
            if (!state.collected.Contains("key-main")) state.collected.Add("key-main");
        }

        static void Place(PlayerState actor, float x, float y, float z)
        {
            actor.x = x; actor.y = y; actor.z = z; actor.yaw = actor.pitch = 0;
            actor.down = actor.ladderClimbing = actor.sprinting = actor.camera = actor.mapOpen = false;
            actor.hiding = null;
        }

        void Inspect(float x, float z, float floor, float targetX, float targetY, float targetZ)
        {
            float dx = targetX - x, dz = targetZ - z;
            float yaw = Mathf.Atan2(-dx, -dz);
            float pitch = Mathf.Atan2(targetY - floor - 1.65f, Mathf.Sqrt(dx * dx + dz * dz));
            player.SetInspectionPose(x, z, yaw, pitch, false, floor);
        }

        IEnumerator Save(string name)
        {
            yield return new WaitForSecondsRealtime(.75f);
            for (int i = 0; i < 3; i++) yield return null;
            RenderOffscreen();
            string path = Path.Combine(output, name + ".png");
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = captureFrame;
                readback.ReadPixels(new Rect(0, 0, captureFrame.width, captureFrame.height), 0, 0, false);
                readback.Apply(false, false);
            }
            finally { RenderTexture.active = previous; }
            File.WriteAllBytes(path, readback.EncodeToPNG());
            ValidatePixels(name, readback);
            frames.Add(name + ".png");
            Debug.Log("BLACKPINE REVIEW FRAME: " + path);
        }

        void ValidatePixels(string name, Texture2D image)
        {
            const int columns = 64, rows = 36;
            var pixels = image.GetPixels32();
            var signature = new byte[columns * rows];
            var colors = new HashSet<int>();
            int minimum = 255, maximum = 0, visible = 0;
            float total = 0;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int px = Mathf.Min(image.width - 1, (x * 2 + 1) * image.width / (columns * 2));
                    int py = Mathf.Min(image.height - 1, (y * 2 + 1) * image.height / (rows * 2));
                    var color = pixels[py * image.width + px];
                    byte light = (byte)((color.r * 54 + color.g * 183 + color.b * 19) >> 8);
                    signature[y * columns + x] = light;
                    minimum = Mathf.Min(minimum, light); maximum = Mathf.Max(maximum, light); total += light;
                    if (light > 6) visible++;
                    colors.Add(((color.r >> 2) << 12) | ((color.g >> 2) << 6) | (color.b >> 2));
                }
            float mean = total / signature.Length, visibleFraction = visible / (float)signature.Length;
            float leastDifference = 255;
            foreach (var earlier in frameSignatures)
            {
                float difference = 0;
                for (int i = 0; i < signature.Length; i++) difference += Mathf.Abs(signature[i] - earlier[i]);
                leastDifference = Mathf.Min(leastDifference, difference / signature.Length);
            }
            string statistics = name + ": meanLuma=" + mean.ToString("F2") + ", range=" + (maximum - minimum) +
                ", colors=" + colors.Count + ", visible=" + visibleFraction.ToString("F3") +
                ", minimumFrameDifference=" + leastDifference.ToString("F2");
            checks.Add("PIXELS: " + statistics);
            Require(mean > 1.2f && maximum - minimum > 20 && colors.Count > 32 && visibleFraction > .008f,
                "Rendered frame has nontrivial luminance and color: " + statistics);
            Require(frameSignatures.Count == 0 || leastDifference > .25f,
                "Rendered frame differs from every earlier review image: " + statistics);
            frameSignatures.Add(signature);
        }

        IEnumerator Until(Func<bool> predicate, string name, float timeout = 9, bool record = true)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate())
            {
                if (Time.realtimeSinceStartup > deadline)
                    throw new InvalidOperationException(name + " timed out. Host: " + session?.LastError + "; guest: " + guest?.LastError);
                yield return null;
            }
            if (record) checks.Add("PASS: " + name + ".");
        }

        void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }

        void ObserveLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message + "\n" + stack);
        }

        void Finish(bool success, string failure)
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= ObserveLog;
            if (guest != null) guest.Leave();
            if (guestObject != null) Destroy(guestObject);
            if (ui != null) ui.SetBlackpineCaptureHud(true);
            var report = new StringBuilder();
            report.AppendLine(success ? "PASSED: BLACKPINE RUNTIME SMOKE AND RENDER REVIEW" : "FAILED: BLACKPINE RUNTIME SMOKE AND RENDER REVIEW");
            report.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; LAN protocol " + DeadAirSession.NetworkProtocol);
            report.AppendLine("Elapsed seconds: " + (Time.realtimeSinceStartup - startedAt).ToString("F1"));
            report.AppendLine("Fixture scope: real TCP commands/snapshots with staged positions/prerequisites, then staged visual review. No full physical walkthrough claimed.");
            foreach (var check in checks) report.AppendLine(check);
            report.AppendLine("Rendered images: " + frames.Count);
            foreach (var frame in frames) report.AppendLine(frame);
            report.AppendLine("Unity runtime errors: " + errors.Count);
            if (!string.IsNullOrEmpty(failure)) report.AppendLine("FAILURE: " + failure);
            File.WriteAllText(Path.Combine(output, "blackpine-runtime-report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(output, "blackpine-runtime-errors.txt"), string.Join("\n\n", errors));
            Debug.Log(success ? "BLACKPINE RUNTIME CAPTURE PASSED: " + output : "BLACKPINE RUNTIME CAPTURE FAILED: " + failure);
            Application.Quit(success ? 0 : 1);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= ObserveLog;
            if (guest != null) guest.Leave();
            if (captureCanvas != null)
            {
                captureCanvas.renderMode = originalCanvasMode; captureCanvas.worldCamera = originalCanvasCamera;
                captureCanvas.planeDistance = originalCanvasPlane;
            }
            foreach (var entry in originalUILayers) if (entry.Key != null) entry.Key.layer = entry.Value;
            if (player != null && player.ViewCamera != null && captureFrame != null)
            {
                player.ViewCamera.enabled = originalWorldEnabled; player.ViewCamera.cullingMask = originalWorldMask;
                player.ViewCamera.clearFlags = originalClearFlags;
            }
            if (frameRenderer != null && frameRenderer.OutputCamera != null && captureFrame != null)
            { frameRenderer.OutputCamera.enabled = originalOutputEnabled; frameRenderer.OutputCamera.targetTexture = originalOutputTarget; }
            if (captureUI != null) Destroy(captureUI.gameObject);
            if (readback != null) Destroy(readback);
            if (captureFrame != null) { captureFrame.Release(); Destroy(captureFrame); }
        }
    }
}
