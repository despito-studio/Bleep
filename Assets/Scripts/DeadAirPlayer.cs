using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirPlayer : MonoBehaviour
    {
        WorldData world;
        DeadAirSession session;
        DeadAirUI ui;
        CharacterController controller;
        Camera view;
        Light flashlight;
        DeadAirFirstPersonRig firstPerson;
        DeadAirFirstPersonBody firstPersonBody;
        DeadAirMenuBackdrop menuBackdrop;
        GameObject menuCharacter;
        string menuClass;
        readonly Dictionary<string, GameObject> remoteViews = new Dictionary<string, GameObject>();
        readonly Dictionary<string, string> remoteClasses = new Dictionary<string, string>();
        readonly Dictionary<string, DeadAirCharacterMotion> remoteMotions = new Dictionary<string, DeadAirCharacterMotion>();
        readonly HashSet<string> activeRemoteIds = new HashSet<string>();
        string[] lockerInteractionLabels, mushroomInteractionLabels;
        DoorInteractionLabels[] doorInteractionLabels;
        struct DoorInteractionLabels { public string open, closed, lockedOpen, lockedClosed; }
        readonly Dictionary<string, Vector3> drawerSlideOffsets = new Dictionary<string, Vector3>();
        readonly Dictionary<string, Transform> drawerAimFronts = new Dictionary<string, Transform>();
        public string InteractionId { get; private set; }
        public string InteractionLabel { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool FlashlightOn => lightOn;
        public bool CameraUp => cameraUp;
        public bool LocalPaused => pausedLocally;
        public bool ModalOpen => ui != null && ui.HasModal;
        public bool Sprinting { get; private set; }
        public float Stamina => stamina;
        public Camera ViewCamera => view;
        public DeadAirFirstPersonBody FirstPersonBody => firstPersonBody;
        public bool Noclip { get; private set; }
        public void ToggleNoclip() => SetNoclip(!Noclip);
        public void SetNoclip(bool enabled)
        {
            Noclip = enabled;
            if (controller != null) controller.enabled = !Noclip && !climbingLadder;
            verticalVelocity = 0;
            var me = session?.State?.Player(session?.LocalId);
            if (me != null) me.noclip = Noclip;
        }
        bool lightOn = true, cameraUp, pausedLocally;
        bool inspectionPose;
        float yaw, pitch, stamina = 100, nextSend, nextRadio, footBob, currentBob, verticalVelocity;
        string previousPhase;
        string previousHiding;
        readonly Dictionary<int, Vector3> sentMovement = new Dictionary<int, Vector3>();
        readonly List<int> acknowledgedMovement = new List<int>();
        int nextMoveSequence, reconciledMoveSequence;
        int observedFlashlightReload;
        bool verificationAllowed, verificationMovement, verificationSprint;
        Vector2 verificationAxes;
        public int MovementCorrectionCount { get; private set; }
        public float LastMovementCorrectionDistance { get; private set; }
        bool windowCutscenePlayed, windowCutsceneActive;
        float windowCutsceneTimer, cutsceneStartYaw, cutsceneStartPitch;
        GameObject cutsceneMonster;

        public void Initialize(WorldData data, DeadAirSession game, DeadAirUI gameUI)
        {
            world = data; session = game; ui = gameUI;
            lockerInteractionLabels = new string[data.lockers.Length];
            for (int i = 0; i < data.lockers.Length; i++) lockerInteractionLabels[i] = "Сховатися: " + data.lockers[i].label;
            doorInteractionLabels = new DoorInteractionLabels[data.doors.Length];
            for (int i = 0; i < data.doors.Length; i++)
            {
                string open = data.doors[i].label + " — зачинити", closed = data.doors[i].label + " — відчинити";
                doorInteractionLabels[i] = new DoorInteractionLabels { open = open, closed = closed,
                    lockedOpen = open + " (потрібна картка або ключ)", lockedClosed = closed + " (потрібна картка або ключ)" };
            }
            mushroomInteractionLabels = new string[data.Mushrooms.Length];
            for (int i = 0; i < data.Mushrooms.Length; i++) mushroomInteractionLabels[i] = "З’їсти " + data.Mushrooms[i].label + " · дивний ефект";
            if (data.props != null)
                foreach (var prop in data.props)
                    drawerSlideOffsets[prop.id] = DeadAirOfficeLayout.Rotation(prop.rotation) * Vector3.forward * .38f;
            var office = GameObject.Find("BLACKPINE / original web scene");
            if (office != null)
                foreach (var part in office.GetComponentsInChildren<Transform>(true))
                {
                    if (!part.name.StartsWith("drawer:", StringComparison.Ordinal)) continue;
                    var front = part.Find("Drawer front");
                    if (front != null) drawerAimFronts[part.name.Substring("drawer:".Length)] = front;
                }
            foreach(var argument in Environment.GetCommandLineArgs())
                if(argument=="-deadAirCapture"||argument=="-deadAirOfficeCapture"||argument=="-deadAirMenuCapture"||argument=="-deadAirRepairCapture"||argument=="-deadAirLightingCapture"||argument=="-deadAirHorrorCapture"||argument=="-deadAirMovementVerification"||argument=="-deadAirIndieCapture"||argument=="-deadAirUpdateCapture"||argument=="-deadAirGraphicsCapture")verificationAllowed=true;
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-deadAirFieldCapture") >= 0) verificationAllowed = true;
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "-deadAirBodyCapture") >= 0) verificationAllowed = true;
            controller = gameObject.AddComponent<CharacterController>();
            controller.height = 1.7f; controller.radius = .22f; controller.center = new Vector3(0, .85f, 0);
            controller.stepOffset = .28f; controller.slopeLimit = 48; controller.skinWidth = .035f;
            transform.position = new Vector3(-data.spawn.x, data.spawn.y, data.spawn.z);
            view = new GameObject("First person camera").AddComponent<Camera>();
            view.tag = "MainCamera";
            view.transform.SetParent(transform, false);
            view.transform.localPosition = new Vector3(0, 1.65f, 0);
            view.fieldOfView = 68; view.nearClipPlane = .08f; view.farClipPlane = 250;
            view.clearFlags = CameraClearFlags.Depth;
            view.backgroundColor = new Color32(10, 16, 10, 255);
            view.allowHDR = true;
            view.gameObject.AddComponent<AudioListener>();
            view.gameObject.AddComponent<DeadAirAudioLimiter>();
            flashlight = new GameObject("Flashlight").AddComponent<Light>();
            flashlight.transform.SetParent(view.transform, false);
            flashlight.type = LightType.Spot;
            flashlight.range = 24; flashlight.spotAngle = 62; flashlight.innerSpotAngle = 17;
            flashlight.color = new Color32(255, 239, 211, 255);
            flashlight.intensity = 1.45f;
            flashlight.cullingMask = ~((1 << 28) | (1 << 29) | (1 << 30));
            flashlight.shadows = LightShadows.Soft;
            flashlight.shadowStrength = .78f; flashlight.shadowBias = .012f;
            flashlight.shadowNormalBias = .04f; flashlight.renderMode = LightRenderMode.ForcePixel;
            flashlight.transform.localPosition = new Vector3(.16f, -.12f, .15f);
            firstPerson = gameObject.AddComponent<DeadAirFirstPersonRig>();
            firstPerson.Initialize(view, world);
            firstPersonBody = gameObject.AddComponent<DeadAirFirstPersonBody>();
            menuBackdrop = gameObject.AddComponent<DeadAirMenuBackdrop>();
        }

        void LateUpdate()
        {
            var state = session?.State;
            var me = state?.Player(session.LocalId);
            if (firstPersonBody == null) return;
            if (me == null || (!IsPlaying && !inspectionPose)) { firstPersonBody.SetVisible(false); return; }
            if (!climbingLadder && !windowCutsceneActive)
            {
                var eye = view.transform.localPosition; eye.z = .08f; view.transform.localPosition = eye;
            }
            firstPersonBody.Present(me, state, transform, yaw, pitch, inspectionPose ? me.crouching : Crouching, Sprinting, view);
        }

        void Update()
        {
            // Result screens and menu dialogs must accept Escape even when the
            // character is dead and gameplay input is no longer being processed.
            bool escapeHandled = Input.GetKeyDown(KeyCode.Escape) && ui.TryCloseTopModal();
            var state = session.State;
            IsPlaying = state != null && (state.phase == "playing" || state.phase == "departing");
            if (menuCharacter != null) menuCharacter.SetActive(!IsPlaying && !inspectionPose);
            if (menuBackdrop != null) menuBackdrop.SetVisible(!IsPlaying && !inspectionPose);
            if (inspectionPose)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
                if (state != null)
                {
                    var inspected = state.Player(session.LocalId);
                    UpdateEquipment(inspected, state, 0); SyncRemote(state);
                    if (state.phase == "playing" && inspected != null) FindInteraction(inspected, state);
                }
                return;
            }
            if (state == null)
            {
                EndLadderPresentation(null);
                previousPhase = previousHiding = null; pausedLocally = false;
                sentMovement.Clear();reconciledMoveSequence=0;verificationMovement=false;
                foreach (var remote in remoteViews.Values) Destroy(remote);
                remoteViews.Clear();remoteClasses.Clear();remoteMotions.Clear();activeRemoteIds.Clear();ShowMenuCamera();return;
            }
            var me = state.Player(session.LocalId);
            if (me == null) { EndLadderPresentation(null); ShowMenuCamera(); return; }
            if (previousPhase != state.phase && state.phase == "playing")
            {
                pausedLocally = false;
                windowCutscenePlayed = false;
                windowCutsceneActive = false;
                if (cutsceneMonster != null) { Destroy(cutsceneMonster); cutsceneMonster = null; }
                yaw = me.yaw; pitch = 0; stamina = 100; cameraUp = false; lightOn = true;
                observedFlashlightReload = me.flashlightReloadSerial;
                firstPerson.ResetPresentation();
                ResetLocomotion();
                Teleport(me.x, me.y, me.z);
            }
            previousPhase = state.phase;
            if (previousHiding != me.hiding)
            {
                Teleport(me.x, me.y, me.z);
                yaw = me.yaw; pitch = me.pitch;
                transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
                cameraUp = false;
                previousHiding = me.hiding;
            }
            if (!IsPlaying) { EndLadderPresentation(me); ShowMenuCamera(); SyncRemote(state); return; }
            if (PresentVanTransit(me, state)) { SyncRemote(state); return; }
            UpdateLadderPresentation(me, state);
            if (observedFlashlightReload != me.flashlightReloadSerial)
            {
                observedFlashlightReload = me.flashlightReloadSerial;
                lightOn = true;
                DeadAirAudio.Play("flashlight");
            }
            if (!DeadAirSupplies.HasCharge(me)) lightOn = false;
            if(string.IsNullOrEmpty(me.hiding) && !climbingLadder && !Noclip)ReconcileMovement(me);
            if (Input.GetKeyDown(KeyCode.Escape) && !escapeHandled)
                SetPause(!pausedLocally);
            var controlled = !pausedLocally && !state.paused && !ui.HasModal && !DeadAirConsole.IsOpen && !me.down && string.IsNullOrEmpty(me.hiding) && !climbingLadder;
            bool lookingFromLocker = !pausedLocally && !state.paused && !ui.HasModal && !DeadAirConsole.IsOpen && !me.down && !string.IsNullOrEmpty(me.hiding) && !climbingLadder;
            Sprinting = false;
            bool hasFlashlight = me.HasItem("flashlight");
            if (controlled)
            {
                if (Input.GetKeyDown(KeyCode.F))
                {
                    if (hasFlashlight)
                    {
                        lightOn = DeadAirSupplies.HasCharge(me) && (!HoldingFlashlight(me) || !lightOn);
                        SelectSlot(me.inventory.IndexOf("flashlight"));
                        DeadAirAudio.Play("flashlight");
                        if (!DeadAirSupplies.HasCharge(me)) ui.Toast("Батарейка розряджена. Натисніть R, щоб замінити її.");
                    }
                    else
                    {
                        ui.Toast("У вас немає ліхтаря!");
                        DeadAirAudio.Play("error");
                    }
                }
                if (Input.GetKeyDown(KeyCode.V)) { cameraUp = !cameraUp; session.Command(new NetPacket { type = "unique-equip", uniqueEquipped = false }); DeadAirAudio.Play("camera-equip"); }
                if (Input.GetKeyDown(KeyCode.R))
                {
                    if (HoldingFlashlight(me) || me.SelectedItem == DeadAirSupplies.BatteryItem) session.Command(new NetPacket { type = "reload" });
                    else if (me.inventory != null && me.selectedSlot >= 0 && me.selectedSlot < me.inventory.Count && me.inventory[me.selectedSlot] == "prison-book") ui.ShowBook();
                    else if (me.uniqueItemEquipped) session.Command(new NetPacket { type = "unique-use" });
                }
                if (Input.GetKeyDown(KeyCode.Q)) ui.ToggleRadio();
                if (ui.RadioOpen)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1)) SendRadioPhrase(0);
                    else if (Input.GetKeyDown(KeyCode.Alpha2)) SendRadioPhrase(1);
                    else if (Input.GetKeyDown(KeyCode.Alpha3)) SendRadioPhrase(2);
                    else if (Input.GetKeyDown(KeyCode.Alpha4)) SendRadioPhrase(3);
                }
                else
                {
                    int page = me.selectedSlot / 5 * 5;
                    if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(page);
                    else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(page + 1);
                    else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(page + 2);
                    else if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(page + 3);
                    else if (Input.GetKeyDown(KeyCode.Alpha5)) SelectSlot(page + 4);
                    float scroll = Input.GetAxis("Mouse ScrollWheel");
                    int itemCount = Mathf.Max(1, me.inventory.Count);
                    if (scroll > 0.05f) SelectSlot((me.selectedSlot + itemCount - 1) % itemCount);
                    else if (scroll < -0.05f) SelectSlot((me.selectedSlot + 1) % itemCount);
                    if (Input.GetKeyDown(KeyCode.G)) DropCurrentItem(me);
                }
            }
            else ui.HideRadio();
            bool watchingClimb = climbingLadder && !pausedLocally && !state.paused && !ui.HasModal;
            Cursor.lockState = (controlled || lookingFromLocker || watchingClimb) && !DeadAirConsole.IsOpen ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = (!controlled && !lookingFromLocker && !watchingClimb) || DeadAirConsole.IsOpen;
            if (controlled) Move(me, state);
            else if (lookingFromLocker) LookFromLocker(me);
            else if (!state.paused && !pausedLocally && string.IsNullOrEmpty(me.hiding) && !climbingLadder && !Noclip) ApplyGravity(Vector3.zero);
            var authoritativePosition = new Vector3(-me.x, me.y, me.z);
            if (!string.IsNullOrEmpty(me.hiding) && Vector3.Distance(transform.position, authoritativePosition) > .1f && !climbingLadder)
                Teleport(me.x, me.y, me.z);
            SendMovement(me, state);
            bool moving = controlled && MovementAxes().sqrMagnitude > .01f;
            currentBob = Mathf.Lerp(currentBob, moving ? (Sprinting ? .028f : .018f) : 0, Time.deltaTime * 10);
            footBob += Time.deltaTime * (moving ? (Sprinting ? 13 : 8) : 2);
            if (!climbingLadder)
            {
                view.transform.localPosition = new Vector3(0, me.down ? .55f : EyeHeight + Mathf.Sin(footBob) * currentBob, 0);
                view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, me.down ? -12.6f : Mathf.Sin(footBob * .5f) * currentBob * 6.87f);
            }
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, cameraUp ? 57 : Sprinting ? 74 : 68, Time.deltaTime * 5);
            var classData = world.GetClass(me.characterClass);
            flashlight.enabled = lightOn && DeadAirSupplies.HasCharge(me) && HoldingFlashlight(me) && !me.down && string.IsNullOrEmpty(me.hiding) && !climbingLadder;
            flashlight.range = classData.bonuses.flashlightDist;
            flashlight.spotAngle = classData.bonuses.flashlightAngle * Mathf.Rad2Deg * 2;
            flashlight.innerSpotAngle = flashlight.spotAngle * .28f;
            UpdateEquipment(me, state, moving ? (Sprinting ? 1.7f : 1f) : 0);
            FindInteraction(me, state);
            if (Input.GetKeyDown(KeyCode.E) && !state.paused && !me.down && !ui.HasModal && !DeadAirConsole.IsOpen && !pausedLocally && !string.IsNullOrEmpty(InteractionId) && !climbingLadder)
            {
                if (InteractionId == "van-map")
                    ui.OpenVanMap();
                else if (InteractionId == "broadcast" && !state.calibrated)
                    ui.ShowCalibration();
                else if (InteractionId == "ladder-climb")
                {
                    SendMovement(me, state, true);
                    session.Command(new NetPacket { type = "interact", id = "ladder-climb" });
                }
                else
                {
                    if (InteractionId == "drawer-director" || InteractionId.StartsWith("drawer:", StringComparison.Ordinal)) DeadAirAudio.Play("drawer-slide");
                    else if (InteractionId == "breaker") DeadAirAudio.Play("breaker-toggle");
                    else if (InteractionId == "ladder" || InteractionId == "ladder-spot") DeadAirAudio.Play("ladder");
                    else if (InteractionId.StartsWith("key-")) DeadAirAudio.Play("key");
                    SendMovement(me, state, true);
                    session.Command(new NetPacket { type = "interact", id = InteractionId });
                }
            }
            SyncRemote(state);
        }

        bool climbingLadder;
        float ladderPresentationAge, presentedLadderStart;
        int lastLadderRung;
        Quaternion ladderStartRotation;
        float ladderStartPitch;

        void UpdateLadderPresentation(PlayerState me, RoomState state)
        {
            if (!me.ladderClimbing || me.down || !string.IsNullOrEmpty(me.hiding))
            {
                EndLadderPresentation(me); return;
            }
            float acceptedAge = Mathf.Clamp(state.elapsed - me.ladderStartedAt, 0, DeadAirLadder.Duration);
            if (!climbingLadder || presentedLadderStart != me.ladderStartedAt)
            {
                climbingLadder = true; presentedLadderStart = me.ladderStartedAt;
                ladderPresentationAge = acceptedAge; lastLadderRung = -1;
                ladderStartRotation = transform.rotation; ladderStartPitch = pitch;
                controller.enabled = false; verticalVelocity = 0; cameraUp = false;
                sentMovement.Clear(); acknowledgedMovement.Clear();
            }
            if (!state.paused)
                ladderPresentationAge = Mathf.Min(Mathf.Max(acceptedAge, ladderPresentationAge + Time.deltaTime), acceptedAge + .12f, DeadAirLadder.Duration);
            float age = ladderPresentationAge;
            transform.position = DeadAirLadder.Position(me, age);
            float alignment = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / .35f));
            transform.rotation = Quaternion.Slerp(ladderStartRotation, LadderFacingRotation(age, me.ladderUp), alignment);
            yaw = (180 - transform.rotation.eulerAngles.y) * Mathf.Deg2Rad;
            float routeAge = DeadAirLadder.PathAge(age, me.ladderUp);
            bool onRungs = routeAge > .35f && routeAge < 3.45f;
            float handCycle = (routeAge - .35f) / (3.10f / 9) * Mathf.PI;
            float bob = onRungs ? Mathf.Sin(handCycle * 2) * .012f : 0;
            pitch = Mathf.Lerp(ladderStartPitch, onRungs ? -.13f : 0, alignment);
            view.transform.localPosition = new Vector3(0, DeadAirLadder.EyeHeight(age, me.ladderUp) + bob, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, onRungs ? Mathf.Sin(handCycle) * .65f : 0);
            int rung = onRungs ? Mathf.FloorToInt((routeAge - .35f) / .47f) : -1;
            if (rung >= 0 && rung != lastLadderRung)
            {
                lastLadderRung = rung; DeadAirAudio.Play("ladder");
            }
        }

        void EndLadderPresentation(PlayerState me)
        {
            if (!climbingLadder) return;
            climbingLadder = false;
            if (me != null)
            {
                transform.position = new Vector3(-me.x, me.y, me.z);
                yaw = me.yaw; pitch = me.pitch;
                transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
            }
            controller.enabled = !Noclip; verticalVelocity = 0;
            sentMovement.Clear(); acknowledgedMovement.Clear(); nextSend = 0;
            if (view != null)
            {
                view.transform.localPosition = new Vector3(0, me != null && me.down ? .55f : 1.65f, 0);
                view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);
            }
            firstPerson?.ResetPresentation();
        }

        static Quaternion LadderFacingRotation(float age, bool up)
        {
            if (up) return Quaternion.Euler(0, 180, 0);
            // Look out through the window first. Turn toward the rungs only
            // after clearing the frame, then face the yard before stepping off.
            float faceRungs = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - 1.28f) / .42f));
            float faceYard = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - 4.80f) / .35f));
            return Quaternion.Euler(0, 180 * faceRungs * (1 - faceYard), 0);
        }

        void OnDisable() => EndLadderPresentation(null);

        void DropCurrentItem(PlayerState me)
        {
            if (me.inventory == null || me.inventory.Count == 0)
            {
                ui.Toast("Інвентар порожній");
                return;
            }
            if (me.selectedSlot < 0 || me.selectedSlot >= me.inventory.Count)
            {
                ui.Toast("У вибраному слоті порожньо");
                return;
            }
            string itemId = me.inventory[me.selectedSlot];
            if(DeadAirRoleItems.IsRoleItem(itemId)){ui.Toast("Особистий предмет залишається з персонажем.");return;}
            session.Command(new NetPacket { type = "drop", id = itemId });
            if (itemId == "flashlight") lightOn = false;
        }

        void SelectSlot(int slot)
        {
            var me = session.State?.Player(session.LocalId);
            if (me != null && me.inventory != null && slot >= 0 && slot < me.inventory.Count && slot != me.selectedSlot)
                DeadAirAudio.Play("equip");
            cameraUp = false;
            session.Command(new NetPacket { type = "slot", phrase = slot });
        }

        public void SelectInventorySlot(int slot)
        {
            var state = session?.State;
            var me = state?.Player(session.LocalId);
            if (state == null || state.phase != "playing" || me == null || me.inventory == null ||
                slot < 0 || slot >= me.inventory.Count || me.down || !string.IsNullOrEmpty(me.hiding)) return;
            SelectSlot(slot);
        }

        bool HoldingFlashlight(PlayerState me) => me != null && !ui.MapOpen && !me.uniqueItemEquipped &&
            (cameraUp ? me.HasItem("flashlight") : me.inventory != null && me.selectedSlot >= 0 &&
                me.selectedSlot < me.inventory.Count && me.inventory[me.selectedSlot] == "flashlight");

        void UpdateEquipment(PlayerState me, RoomState state, float movement)
        {
            if (me == null) { firstPerson.SetVisible(false); return; }
            firstPerson.SynchronizeUniqueAction(me.uniqueActionSequence > 0 && me.uniqueItemEquipped && !me.uniqueActionCancelled ? state.elapsed - me.uniqueActionAt : -1);
            firstPerson.Present(me, cameraUp, ui.MapOpen, lightOn && DeadAirSupplies.HasCharge(me), movement, state.paused || pausedLocally, state.elapsed);
            if (climbingLadder) firstPerson.PresentLadder(ladderPresentationAge, me.ladderUp, !state.windowBroken);
            firstPerson.SetVisible(state.phase == "playing" && !me.down && string.IsNullOrEmpty(me.hiding));
            flashlight.enabled = lightOn && DeadAirSupplies.HasCharge(me) && HoldingFlashlight(me) && !me.down && string.IsNullOrEmpty(me.hiding) && !climbingLadder;
            if(flashlight.enabled && firstPerson.TryGetFlashlightPose(out var position,out var rotation))
                flashlight.transform.SetPositionAndRotation(position,rotation);
        }

        void CheckWindowCutsceneTrigger(PlayerState me)
        {
            if (windowCutscenePlayed || windowCutsceneActive || me == null || me.down || climbingLadder || !string.IsNullOrEmpty(me.hiding)) return;
            float dx = transform.position.x - 9f;
            float dz = transform.position.z - 7.1f;
            if (dx * dx + dz * dz < 1.7f * 1.7f && transform.position.y < 1.0f)
            {
                StartWindowCutscene();
            }
        }

        void StartWindowCutscene()
        {
            windowCutscenePlayed = true;
            windowCutsceneActive = true;
            windowCutsceneTimer = 0f;
            cutsceneStartYaw = yaw;
            cutsceneStartPitch = pitch;
            DeadAirAudio.Play("window-shatter");
            DeadAirAudio.Play("scare");

            var prefab = Resources.Load<GameObject>("World/monster");
            if (prefab != null)
            {
                cutsceneMonster = Instantiate(prefab);
                cutsceneMonster.name = "Director window cutscene monster";
                cutsceneMonster.transform.position = new Vector3(9f, 4.2f, 5.4f);
                cutsceneMonster.transform.rotation = Quaternion.Euler(0, 0, 0);
                foreach (var col in cutsceneMonster.GetComponentsInChildren<Collider>()) Destroy(col);
                var glow = new GameObject("Monster gaze glow").AddComponent<Light>();
                glow.transform.SetParent(cutsceneMonster.transform, false);
                glow.transform.localPosition = new Vector3(0, 1.8f, 0.3f);
                glow.type = LightType.Point;
                glow.range = 3.5f;
                glow.intensity = 1.2f;
                glow.color = new Color(0.85f, 0.95f, 1f);
            }
        }

        void UpdateWindowCutscene()
        {
            windowCutsceneTimer += Time.deltaTime;
            Vector3 eyePos = transform.position + Vector3.up * 1.65f;
            Vector3 toWindow = (new Vector3(9f, 6.0f, 5.6f) - eyePos).normalized;
            float targetYaw = Mathf.Atan2(-toWindow.x, -toWindow.z);
            float targetPitch = Mathf.Asin(Mathf.Clamp(toWindow.y, -1f, 1f));

            if (windowCutsceneTimer <= 0.65f)
            {
                float t = Mathf.SmoothStep(0, 1, windowCutsceneTimer / 0.65f);
                yaw = Mathf.LerpAngle(cutsceneStartYaw * Mathf.Rad2Deg, targetYaw * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
                pitch = Mathf.Lerp(cutsceneStartPitch, targetPitch, t);
            }
            else if (windowCutsceneTimer <= 2.2f)
            {
                yaw = targetYaw;
                pitch = targetPitch;
            }
            else if (windowCutsceneTimer <= 2.8f)
            {
                if (cutsceneMonster != null)
                {
                    DeadAirAudio.Play("vanish");
                    Destroy(cutsceneMonster);
                    cutsceneMonster = null;
                }
                float t = Mathf.SmoothStep(0, 1, (windowCutsceneTimer - 2.2f) / 0.6f);
                pitch = Mathf.Lerp(targetPitch, 0f, t);
            }
            else
            {
                if (cutsceneMonster != null)
                {
                    Destroy(cutsceneMonster);
                    cutsceneMonster = null;
                }
                windowCutsceneActive = false;
            }

            transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);
            ApplyGravity(Vector3.zero);
        }

        void Move(PlayerState me, RoomState state)
        {
            CheckWindowCutsceneTrigger(me);
            if (windowCutsceneActive)
            {
                UpdateWindowCutscene();
                return;
            }
            if(!verificationMovement)ReadLook();
            transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);
            var axes=MovementAxes();
            if (Noclip)
            {
                Vector3 moveDir = view.transform.forward * axes.y + view.transform.right * axes.x;
                float upDown = 0f;
                if (Input.GetKey(KeyCode.Space)) upDown += 1f;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.C)) upDown -= 1f;
                moveDir += Vector3.up * upDown;

                bool fastFly = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                float flySpeed = fastFly ? 16f : 6.5f;
                transform.position += moveDir * (flySpeed * Time.deltaTime);
                verticalVelocity = 0;
                return;
            }
            var forward = axes.y;
            var sideways = axes.x;
            UpdateCrouch();
            var direction = (transform.forward * forward + transform.right * sideways).normalized;
            bool sprint = !Crouching && direction.sqrMagnitude > 0 && (verificationMovement?verificationSprint:Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && (state.creative || stamina > 2);
            Sprinting = sprint;
            var bonuses = world.GetClass(me.characterClass).bonuses;
            stamina = state.creative ? 100 : Mathf.Clamp(stamina + (sprint ? -23 : 14 * bonuses.staminaRecovery) * Time.deltaTime, 0, 100);
            float speed = Crouching ? DeadAirLocomotion.CrouchSpeed : sprint ? DeadAirLocomotion.RunSpeed * bonuses.speedMultiplier : DeadAirLocomotion.WalkSpeed;
            ApplyGravity(direction * speed);
        }

        void ReadLook()
        {
            // Legacy mouse axes already scale pixel motion by 0.1; restore the web game's 0.002 rad/pixel.
            yaw -= Mathf.Clamp(Input.GetAxisRaw("Mouse X"), -8, 8) * .02f * DeadAirSettings.Sensitivity;
            pitch = Mathf.Clamp(pitch + Mathf.Clamp(Input.GetAxisRaw("Mouse Y"), -8, 8) * .02f * DeadAirSettings.Sensitivity, DeadAirLocomotion.MaximumDownwardPitch, 1.35f);
            if (Input.GetKey(KeyCode.LeftArrow)) yaw += Time.deltaTime * 1.6f;
            if (Input.GetKey(KeyCode.RightArrow)) yaw -= Time.deltaTime * 1.6f;
            if (Input.GetKey(KeyCode.UpArrow)) pitch = Mathf.Min(1.3f, pitch + Time.deltaTime);
            if (Input.GetKey(KeyCode.DownArrow)) pitch = Mathf.Max(-1.3f, pitch - Time.deltaTime);
        }

        void LookFromLocker(PlayerState me)
        {
            var locker = world.Locker(me.hiding);
            if (locker == null) return;
            ReadLook();
            float centerYaw = locker.FacingYaw;
            yaw = centerYaw + Mathf.Clamp(Mathf.DeltaAngle(centerYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg), -18, 18) * Mathf.Deg2Rad;
            pitch = Mathf.Clamp(pitch, -14 * Mathf.Deg2Rad, 14 * Mathf.Deg2Rad);
            transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
        }

        void ApplyGravity(Vector3 horizontalVelocity)
        {
            if (Noclip || !controller.enabled) return;
            // Keep physical stair/floor collision steps small even during a slow render frame.
            int steps=Mathf.Max(1,Mathf.CeilToInt(Time.deltaTime/.033f));
            float dt=Time.deltaTime/steps;
            for(int step=0;step<steps;step++)
            {
                if (controller.isGrounded && verticalVelocity < 0) verticalVelocity = -2;
                else verticalVelocity = Mathf.Max(-30, verticalVelocity - 18 * dt);
                controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            }
        }

        Vector2 MovementAxes() => verificationMovement?verificationAxes:new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical"));

        public void SetVerificationMovement(Vector2 axes,bool sprint)
        {
            if(!verificationAllowed)throw new InvalidOperationException("Verification movement requires an opt-in Dead Air capture argument.");
            verificationMovement=true;verificationAxes=Vector2.ClampMagnitude(axes,1);verificationSprint=sprint;
        }

        public void ClearVerificationMovement(){verificationMovement=false;verificationAxes=Vector2.zero;verificationSprint=false;}

        public void SetVerificationFlashlight(bool on)
        {
            if(!verificationAllowed)throw new InvalidOperationException("Verification flashlight control requires an opt-in Dead Air capture argument.");
            lightOn=on;
            var me=session.State?.Player(session.LocalId);
            if(me!=null){me.flashlight=on&&DeadAirSupplies.HasCharge(me);if(firstPerson!=null)UpdateEquipment(me,session.State,0);}
            flashlight.enabled=on&&DeadAirSupplies.HasCharge(me)&&HoldingFlashlight(me)&&!climbingLadder;
        }

        void ReconcileMovement(PlayerState actor)
        {
            int sequence=actor.moveSequence;
            if(sequence<=reconciledMoveSequence)return;
            reconciledMoveSequence=sequence;
            if(sentMovement.TryGetValue(sequence,out var sentPosition))
            {
                var authority=new Vector3(-actor.x,actor.y,actor.z);
                Vector3 error=authority-sentPosition;
                if(error.sqrMagnitude>.0001f)
                {
                    LastMovementCorrectionDistance=error.magnitude;
                    if(error.magnitude>.05f)MovementCorrectionCount++;
                    Vector3 before=transform.position;
                    // Reapply movement predicted after the acknowledged packet from the
                    // server position. Comparing against the current pose would rewind
                    // every guest merely because snapshots arrive behind its render frame.
                    Vector3 pending=before-sentPosition;
                    controller.enabled=false;transform.position=authority;controller.enabled=true;
                    controller.Move(pending);
                    Vector3 correction=transform.position-before;
                    foreach(var entry in sentMovement)if(entry.Key>sequence)acknowledgedMovement.Add(entry.Key);
                    foreach(int key in acknowledgedMovement)sentMovement[key]+=correction;
                    acknowledgedMovement.Clear();
                }
            }
            foreach(var entry in sentMovement)if(entry.Key<=sequence)acknowledgedMovement.Add(entry.Key);
            foreach(int key in acknowledgedMovement)sentMovement.Remove(key);
            acknowledgedMovement.Clear();
        }

        void SendMovement(PlayerState me, RoomState state, bool force = false)
        {
            // Send the stopped state too: opening a map must not leave a network player
            // sprinting indefinitely or keep footsteps/noise active on other clients.
            if (state.paused || climbingLadder || me.ladderClimbing) return;
            if (force || Time.time >= nextSend)
            {
                nextSend = Time.time + .066f;
                int sequence=++nextMoveSequence;
                sentMovement[sequence]=transform.position;
                if(sentMovement.Count>256)sentMovement.Remove(sequence-256);
                session.Command(new NetPacket
                {
                    type = "move", x = -transform.position.x, y = transform.position.y, z = transform.position.z,
                    moveSequence=sequence,
                    yaw = yaw, pitch = pitch, flashlight = lightOn && DeadAirSupplies.HasCharge(me) && HoldingFlashlight(me) && !me.down && string.IsNullOrEmpty(me.hiding),
                    camera = cameraUp && !me.down && string.IsNullOrEmpty(me.hiding), mapOpen = ui.MapOpen && !me.down && string.IsNullOrEmpty(me.hiding), sprinting = Sprinting, crouching = Crouching,
                    noclip = Noclip
                });
            }
        }

        bool InteractionInReach(float x, float y, float z, float range, Vector2 origin, Vector2 forward, float nearest, float eyeOffset = 0)
        {
            var offset = new Vector2(x, z) - origin;
            float vertical = y + eyeOffset - transform.position.y - 1;
            float squared = offset.sqrMagnitude + vertical * vertical;
            if (squared >= nearest * nearest || squared > range * range) return false;
            return squared <= .9f * .9f || Vector2.Dot(forward, offset.normalized) >= .35f;
        }

        void ConsiderInteraction(string id, string label, float x, float y, float z, float range, Vector2 origin, Vector2 forward, ref float nearest, float eyeOffset = 0)
        {
            if (!InteractionInReach(x, y, z, range, origin, forward, nearest, eyeOffset)) return;
            var offset = new Vector2(x, z) - origin;
            float vertical = y + eyeOffset - transform.position.y - 1;
            float distance = Mathf.Sqrt(offset.sqrMagnitude + vertical * vertical);
            if (!DeadAirRules.HasInteractionSightline(transform.position + Vector3.up * 1.4f,
                new Vector3(-x, y + eyeOffset, z), id)) return;
            nearest = distance; InteractionId = id; InteractionLabel = label;
        }

        void FindInteraction(PlayerState me, RoomState state)
        {
            InteractionId = InteractionLabel = null;
            if (me.down || climbingLadder) return;
            if (!string.IsNullOrEmpty(me.hiding)) { InteractionId = "exit-locker"; InteractionLabel = "Вийти з шафи"; return; }
            float nearest = 2.5f;
            var origin = new Vector2(-transform.position.x, transform.position.z);
            var forward = new Vector2(-transform.forward.x, transform.forward.z);
            for (int i = 0; i < world.lockers.Length; i++)
            {
                var locker = world.lockers[i];
                ConsiderInteraction(locker.id, lockerInteractionLabels[i], locker.x, locker.y, locker.z, 2.2f, origin, forward, ref nearest, 1);
            }
            for (int i = 0; i < world.doors.Length; i++)
            {
                var door = world.doors[i];
                if (!InteractionInReach(door.x, door.y, door.z, 2.5f, origin, forward, nearest)) continue;
                var current = state.Door(door.id);
                var labels = doorInteractionLabels[i];
                bool open = current != null && current.open;
                bool locked = current != null && current.locked && !me.HasItem(current.requiredKey);
                string label = open ? (locked ? labels.lockedOpen : labels.open) : (locked ? labels.lockedClosed : labels.closed);
                ConsiderInteraction(door.id, label, door.x, door.y, door.z, 2.5f, origin, forward, ref nearest);
            }
            if (state.droppedItems != null)
            {
                for (int i = 0; i < state.droppedItems.Count; i++)
                {
                    var dropped = state.droppedItems[i];
                    if (!InteractionInReach(dropped.x, dropped.y, dropped.z, 2.2f, origin, forward, nearest, 1)) continue;
                    ConsiderInteraction("dropped:" + dropped.id, "Підняти: " + dropped.label, dropped.x, dropped.y, dropped.z, 2.2f, origin, forward, ref nearest, 1);
                }
            }
            foreach (var obj in world.objects)
            {
                float objRange = obj.id == "ladder" ? 3.5f : 2.5f;
                float objEyeOffset = obj.id == "ladder" ? 0.9f : 0f;
                if (!InteractionInReach(obj.x, obj.y, obj.z, objRange, origin, forward, nearest, objEyeOffset)) continue;
                if (obj.id == "exit" && !state.broadcast) continue;
                if (state.Collected(obj.id) || obj.id == "power" && state.power || obj.id == "broadcast" && state.broadcast ||
                    obj.id == "fuel-canister" && state.canister || obj.id == "pressure-valve" && state.valveOpened) continue;
                if (obj.id == "key-main" && !state.directorDrawerOpen) continue;
                string label = obj.label;
                if (obj.id == "breaker")
                    label = "Рубильник освітлення (" + (state.breakerOn ? "увімкнено — вимкнути" : "вимкнено — увімкнути") + ")";
                ConsiderInteraction(obj.id, label, obj.x, obj.y, obj.z, objRange, origin, forward, ref nearest, objEyeOffset);
            }
            if (!state.ladderPlaced)
            {
                if (me.HasItem("ladder"))
                    ConsiderInteraction("ladder-spot", "Встановити драбину під вікно", -9f, 0.5f, 7.26f, 3.5f, origin, forward, ref nearest);
                else
                    ConsiderInteraction("ladder-spot", "Вікно кабінету директора (потрібна драбина з фургона)", -9f, 0.5f, 7.26f, 3.5f, origin, forward, ref nearest);
            }
            else
            {
                if (me.y < 2)
                    ConsiderInteraction("ladder-climb", state.windowBroken ? "Залізти у вікно директора" : "Залізти по драбині і розбити вікно", -9f, 1.5f, 6.8f, 3.5f, origin, forward, ref nearest);
                else
                    ConsiderInteraction("ladder-climb", "Спуститися по драбині назовні", -9f, 4.2f, 5.8f, 3.5f, origin, forward, ref nearest);
            }
            foreach (var battery in world.Batteries)
                if (InteractionInReach(battery.x, battery.y, battery.z, 2.5f, origin, forward, nearest) && !state.Collected(battery.id))
                    ConsiderInteraction(battery.id, "Взяти батарейку для ліхтаря", battery.x, battery.y, battery.z, 2.5f, origin, forward, ref nearest);
            for (int i = 0; i < world.Mushrooms.Length; i++)
            {
                var mushroom = world.Mushrooms[i];
                if (!InteractionInReach(mushroom.x, mushroom.y, mushroom.z, DeadAirMushrooms.EatRange, origin, forward, nearest, DeadAirMushrooms.EyeOffset)) continue;
                if (state.Collected(mushroom.id) || Mathf.Abs(me.y - mushroom.y) >= 1) continue;
                // A nearby floor patch must not steal E from the book or an item
                // the player is actually looking at above it.
                var mushroomTarget = new Vector3(-mushroom.x, mushroom.y + DeadAirMushrooms.EyeOffset, mushroom.z);
                var mushroomRay = mushroomTarget - view.transform.position;
                if (mushroomRay.sqrMagnitude < .0001f || Vector3.Dot(view.transform.forward, mushroomRay.normalized) < .96f) continue;
                ConsiderInteraction(mushroom.id, mushroomInteractionLabels[i],
                    mushroom.x, mushroom.y, mushroom.z, DeadAirMushrooms.EatRange, origin, forward, ref nearest,
                    DeadAirMushrooms.EyeOffset);
            }
            foreach (var other in state.players)
                if (other.id != session.LocalId && other.down && InteractionInReach(other.x, other.y, other.z, 2.2f, origin, forward, nearest, 1))
                    ConsiderInteraction("revive:" + other.id, "Підняти " + other.name, other.x, other.y, other.z, 2.2f, origin, forward, ref nearest, 1);
            FindAimedDrawer(state);
        }

        void FindAimedDrawer(RoomState state)
        {
            float bestAim = .975f;
            DrawerData selected = null;
            foreach (var drawer in world.Drawers)
            {
                Vector3 authorityTarget = new Vector3(-drawer.x, drawer.y, drawer.z);
                if (Vector3.SqrMagnitude(authorityTarget - transform.position - Vector3.up) > 2.5f * 2.5f) continue;
                if (!TryGetDrawerAimTarget(drawer.id, out var target)) continue;
                Vector3 ray = target - view.transform.position;
                if (ray.sqrMagnitude > 2.5f * 2.5f || ray.sqrMagnitude < .0001f) continue;
                float aim = Vector3.Dot(view.transform.forward, ray.normalized);
                if (aim <= bestAim || !DeadAirRules.HasInteractionSightline(view.transform.position, target, drawer.id)) continue;
                bestAim = aim; selected = drawer;
            }
            if (selected == null) return;
            var contents = state.Drawer(selected.id);
            if (contents != null && contents.open && contents.searched && !contents.lootTaken && contents.loot != "empty")
            {
                InteractionId = "drawer-loot:" + selected.id;
                InteractionLabel = contents.loot == "coins" ? "Забрати монетки" : "Взяти батарейку для ліхтаря";
            }
            else if (selected.directorKey && contents?.open == true && !state.Collected("key-main"))
            {
                InteractionId = "key-main"; InteractionLabel = "Взяти головну картку доступу";
            }
            else
            {
                InteractionId = selected.id;
                InteractionLabel = contents?.open == true ? "Зачинити шухляду" : "Відкрити шухляду";
            }
        }

        // Captures and targeting use the actual moving front. Authored interaction
        // coordinates remain the host's reach check and support older prefabs.
        public bool TryGetDrawerAimTarget(string id, out Vector3 target)
        {
            if (id != null && drawerAimFronts.TryGetValue(id, out var front) && front != null)
            {
                target = front.position;
                return true;
            }
            var drawer = world?.Drawer(id);
            if (drawer == null) { target = Vector3.zero; return false; }
            target = new Vector3(-drawer.x, drawer.y, drawer.z);
            if (session?.State?.Drawer(id)?.open == true && drawerSlideOffsets.TryGetValue(drawer.propId, out var slide)) target += slide;
            return true;
        }

        readonly List<string> remoteIdsToRemove = new List<string>();
        void SyncRemote(RoomState state)
        {
            activeRemoteIds.Clear();
            foreach (var p in state.players)
            {
                if (p.id == session.LocalId) continue;
                activeRemoteIds.Add(p.id);
                if (remoteViews.TryGetValue(p.id, out var oldView) && remoteClasses[p.id] != p.characterClass)
                {
                    Destroy(oldView); remoteViews.Remove(p.id); remoteClasses.Remove(p.id); remoteMotions.Remove(p.id);
                }
                if (!remoteViews.TryGetValue(p.id, out var viewObject))
                {
                    string model = p.characterClass == "operator" ? "character_operator_vivian" :
                        p.characterClass == "journalist" ? "character_journalist_edgar" :
                        p.characterClass == "dispatcher" ? "character_dispatcher_hazel" : "character_engineer_victor";
                    var prefab = Resources.Load<GameObject>("World/character_" + p.characterClass) ?? Resources.Load<GameObject>("Models/" + model);
                    viewObject = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    viewObject.name = p.name + " / " + p.characterClass;
                    foreach (var collider in viewObject.GetComponentsInChildren<Collider>()) Destroy(collider);
                    remoteViews.Add(p.id, viewObject);
                    remoteClasses[p.id] = p.characterClass;
                    remoteMotions[p.id] = viewObject.GetComponent<DeadAirCharacterMotion>();
                    if (remoteMotions[p.id] != null) remoteMotions[p.id].ConfigureFlashlight(world.GetClass(p.characterClass).bonuses);
                    viewObject.transform.position = new Vector3(-p.x, p.y, p.z);
                }
                viewObject.transform.position = Vector3.Lerp(viewObject.transform.position, new Vector3(-p.x, p.y + (p.down ? .2f : 0), p.z) + DeadAirVanPresentation.TransitOffset(state), Time.deltaTime * 12);
                remoteMotions.TryGetValue(p.id, out var motion);
                if (motion == null || p.down || p.ladderClimbing || DeadAirVanPresentation.SeatedWeight(state) > .001f)
                    viewObject.transform.rotation = p.ladderClimbing && !p.down ?
                        LadderFacingRotation(Mathf.Clamp(state.elapsed - p.ladderStartedAt, 0, DeadAirLadder.Duration), p.ladderUp) :
                        Quaternion.Euler(0, 180 - p.yaw * Mathf.Rad2Deg, p.down ? -90 : 0);
                if (motion != null) { motion.SetTransitPose(DeadAirVanPresentation.SeatedWeight(state)); motion.Present(p, state.elapsed, state.paused, state.phase == "playing"); }
                viewObject.SetActive(string.IsNullOrEmpty(p.hiding));
            }
            remoteIdsToRemove.Clear();
            foreach (var id in remoteViews.Keys)
                if (!activeRemoteIds.Contains(id)) remoteIdsToRemove.Add(id);
            for (int i = 0; i < remoteIdsToRemove.Count; i++)
            {
                var id = remoteIdsToRemove[i];
                Destroy(remoteViews[id]);
                remoteViews.Remove(id);
                remoteClasses.Remove(id);
                remoteMotions.Remove(id);
            }
            remoteIdsToRemove.Clear();
        }

        void ShowMenuCamera()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            flashlight.enabled = false;
            firstPerson.SetVisible(false);
            firstPersonBody.SetVisible(false);
            InteractionId = InteractionLabel = null;
            menuBackdrop.Frame(view, transform);
            UpdateMenuCharacter();
        }

        public void Teleport(float x, float y, float z)
        {
            if (controller != null) controller.enabled = false;
            transform.position = new Vector3(-x, y, z);
            if (firstPersonBody != null) firstPersonBody.ResetPose(yaw, pitch);
            verticalVelocity = 0;
            if (controller != null && !Noclip) controller.enabled = true;
            sentMovement.Clear();
            reconciledMoveSequence = session.State?.Player(session.LocalId)?.moveSequence ?? 0;
            nextMoveSequence = Mathf.Max(nextMoveSequence, reconciledMoveSequence);
        }

        public void SendRadioPhrase(int phrase)
        {
            var state = session.State;
            var me = state?.Player(session.LocalId);
            if (phrase < 0 || phrase > 3 || me == null || state.phase != "playing" || state.paused || pausedLocally || ui.HasModal || me.down || !string.IsNullOrEmpty(me.hiding)) return;
            if (Time.time < nextRadio) return;
            nextRadio = Time.time + world.GetClass(me.characterClass).bonuses.radioCooldown / 1000f;
            ui.HideRadio();
            DeadAirAudio.Play("radio");
            session.Command(new NetPacket { type = "radio", phrase = phrase });
        }

        void UpdateMenuCharacter()
        {
            string selected = ui.SelectedClass;
            const int mask = -1;
            if (menuCharacter != null && menuClass == selected) { DeadAirClothing.Apply(menuCharacter, mask, selected); return; }
            if (menuCharacter != null) Destroy(menuCharacter);
            var prefab = Resources.Load<GameObject>("World/character_" + selected);
            if (prefab == null) return;
            menuClass = selected;
            menuCharacter = Instantiate(prefab);
            menuCharacter.name = "Selected operative — " + selected;
            menuCharacter.transform.position = DeadAirMenuBackdrop.OperativePosition;
            menuCharacter.transform.rotation = DeadAirMenuBackdrop.OperativeRotation;
            foreach (var collider in menuCharacter.GetComponentsInChildren<Collider>()) Destroy(collider);
            foreach (var part in menuCharacter.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 29;
            DeadAirClothing.Apply(menuCharacter, mask, selected);
            AddMenuCharacterLight("Operative preview key", new Vector3(1.3f, 2.5f, 2.2f), new Color32(235, 213, 180, 255), .9f);
            AddMenuCharacterLight("Operative preview rim", new Vector3(-1.7f, 2.25f, -1.3f), new Color32(152, 177, 197, 255), .8f);
        }

        void AddMenuCharacterLight(string name, Vector3 position, Color color, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(menuCharacter.transform, false);
            light.transform.localPosition = position;
            light.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 1.1f, 0) - position);
            light.type = LightType.Spot;
            light.range = 7;
            light.spotAngle = 75;
            light.intensity = intensity;
            light.color = color;
            light.cullingMask = 1 << 29;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // Coordinates and angles here use the original web map convention, like network state.
        public void SetInspectionPose(float sourceX, float z, float sourceYaw, float sourcePitch, bool equipment = false, float floorY = 0)
        {
            if(menuBackdrop!=null)menuBackdrop.SetVisible(false);
            inspectionPose = true;
            yaw = sourceYaw; pitch = sourcePitch;
            Teleport(sourceX, floorY, z);
            transform.rotation = Quaternion.Euler(0, 180 - sourceYaw * Mathf.Rad2Deg, 0);
            view.transform.localPosition = new Vector3(0, 1.65f, 0);
            view.transform.localRotation = Quaternion.Euler(-sourcePitch * Mathf.Rad2Deg, 0, 0);
            view.fieldOfView = equipment ? 57 : 68;
            lightOn = true; cameraUp = equipment;
            flashlight.enabled = true;
            flashlight.range = 30;
            flashlight.spotAngle = world.GetClass("engineer").bonuses.flashlightAngle * Mathf.Rad2Deg * 2;
            flashlight.innerSpotAngle = flashlight.spotAngle * .4f;
            var me = session.State?.Player(session.LocalId);
            if (me != null) { me.x = sourceX; me.y = floorY; me.z = z; me.yaw = sourceYaw; me.pitch = sourcePitch; me.camera = equipment; }
            if (me != null) UpdateEquipment(me, session.State, 0);
        }

        public void EndInspection() { inspectionPose = false; }

        public void SetMapPresentation(bool open)
        {
            if (open)
            {
                cameraUp = false;
                var board = world.Objective("van-map");
                if (board != null)
                {
                    var direction = new Vector3(-board.x, board.y, board.z) - view.transform.position;
                    yaw = Mathf.Atan2(direction.x, -direction.z);
                    pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude);
                    transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
                    view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);
                }
            }
            DeadAirAudio.Play("paper");
            session.Command(new NetPacket { type = "map-equip", mapOpen = open });
        }

        public void SetPause(bool paused)
        {
            pausedLocally = paused;
            ui.ShowPause(paused);
        }
    }
}
