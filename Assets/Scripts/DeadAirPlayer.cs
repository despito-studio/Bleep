using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirPlayer : MonoBehaviour
    {
        WorldData world;
        DeadAirSession session;
        DeadAirUI ui;
        CharacterController controller;
        Camera view;
        Light flashlight;
        DeadAirFirstPersonRig firstPerson;
        DeadAirMenuBackdrop menuBackdrop;
        GameObject menuCharacter;
        string menuClass;
        readonly Dictionary<string, GameObject> remoteViews = new Dictionary<string, GameObject>();
        readonly Dictionary<string, string> remoteClasses = new Dictionary<string, string>();
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
        bool lightOn = true, cameraUp, pausedLocally;
        bool inspectionPose;
        float yaw, pitch, stamina = 100, nextSend, nextRadio, footBob, currentBob, verticalVelocity;
        string previousPhase;
        string previousHiding;
        readonly Dictionary<int, Vector3> sentMovement = new Dictionary<int, Vector3>();
        readonly List<int> acknowledgedMovement = new List<int>();
        int nextMoveSequence, reconciledMoveSequence;
        bool verificationAllowed, verificationMovement, verificationSprint;
        Vector2 verificationAxes;
        public int MovementCorrectionCount { get; private set; }
        public float LastMovementCorrectionDistance { get; private set; }

        public void Initialize(WorldData data, DeadAirSession game, DeadAirUI gameUI)
        {
            world = data; session = game; ui = gameUI;
            foreach(var argument in Environment.GetCommandLineArgs())
                if(argument=="-deadAirCapture"||argument=="-deadAirOfficeCapture"||argument=="-deadAirMenuCapture"||argument=="-deadAirRepairCapture"||argument=="-deadAirLightingCapture"||argument=="-deadAirHorrorCapture"||argument=="-deadAirMovementVerification")verificationAllowed=true;
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
            flashlight.range = 24; flashlight.spotAngle = 55.4f; flashlight.innerSpotAngle = 22;
            flashlight.color = new Color32(255, 239, 211, 255);
            flashlight.intensity = 1.25f;
            flashlight.cullingMask = ~(1 << 30);
            flashlight.shadows = LightShadows.Hard;
            flashlight.shadowStrength = .8f; flashlight.shadowBias = .018f;
            flashlight.shadowNormalBias = .04f; flashlight.renderMode = LightRenderMode.ForcePixel;
            flashlight.transform.localPosition = new Vector3(.16f, -.12f, .15f);
            firstPerson = gameObject.AddComponent<DeadAirFirstPersonRig>();
            firstPerson.Initialize(view, world);
            menuBackdrop = gameObject.AddComponent<DeadAirMenuBackdrop>();
        }

        void Update()
        {
            // Result screens and menu dialogs must accept Escape even when the
            // character is dead and gameplay input is no longer being processed.
            bool escapeHandled = Input.GetKeyDown(KeyCode.Escape) && ui.TryCloseTopModal();
            var state = session.State;
            IsPlaying = state != null && state.phase == "playing";
            if (menuCharacter != null) menuCharacter.SetActive(!IsPlaying && !inspectionPose);
            if (menuBackdrop != null) menuBackdrop.SetVisible(!IsPlaying && !inspectionPose);
            if (inspectionPose)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
                if (state != null) { UpdateEquipment(state.Player(session.LocalId), state, 0); SyncRemote(state); }
                return;
            }
            if (state == null)
            {
                previousPhase = previousHiding = null; pausedLocally = false;
                sentMovement.Clear();reconciledMoveSequence=0;verificationMovement=false;
                foreach (var remote in remoteViews.Values) Destroy(remote);
                remoteViews.Clear();remoteClasses.Clear();ShowMenuCamera();return;
            }
            var me = state.Player(session.LocalId);
            if (me == null) { ShowMenuCamera(); return; }
            if (previousPhase != state.phase && state.phase == "playing")
            {
                pausedLocally = false;
                yaw = pitch = 0; stamina = 100; cameraUp = false; lightOn = true;
                firstPerson.ResetPresentation();
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
            if (!IsPlaying) { ShowMenuCamera(); SyncRemote(state); return; }
            if(string.IsNullOrEmpty(me.hiding))ReconcileMovement(me);
            if (Input.GetKeyDown(KeyCode.Escape) && !escapeHandled)
                SetPause(!pausedLocally);
            if (Input.GetKeyDown(KeyCode.M) && !me.down && string.IsNullOrEmpty(me.hiding)) { cameraUp = false; ui.ToggleMap(); }
            var controlled = !pausedLocally && !state.paused && !ui.HasModal && !me.down && string.IsNullOrEmpty(me.hiding);
            bool lookingFromLocker = !pausedLocally && !state.paused && !ui.HasModal && !me.down && !string.IsNullOrEmpty(me.hiding);
            Sprinting = false;
            bool hasFlashlight = me.HasItem("flashlight");
            if (controlled)
            {
                if (Input.GetKeyDown(KeyCode.F))
                {
                    if (hasFlashlight)
                    {
                        lightOn = !HoldingFlashlight(me) || !lightOn;
                        SelectSlot(me.inventory.IndexOf("flashlight"));
                        DeadAirAudio.Play("flashlight");
                    }
                    else
                    {
                        ui.Toast("У вас немає ліхтаря!");
                        DeadAirAudio.Play("error");
                    }
                }
                if (Input.GetKeyDown(KeyCode.V)) { cameraUp = !cameraUp; session.Command(new NetPacket { type = "unique-equip", uniqueEquipped = false }); DeadAirAudio.Play("camera-equip"); }
                if (Input.GetKeyDown(KeyCode.R) && me.uniqueItemEquipped) session.Command(new NetPacket { type = "unique-use" });
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
            Cursor.lockState = controlled || lookingFromLocker ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !controlled && !lookingFromLocker;
            if (controlled) Move(me, state);
            else if (lookingFromLocker) LookFromLocker(me);
            else if (!state.paused && !pausedLocally && string.IsNullOrEmpty(me.hiding)) ApplyGravity(Vector3.zero);
            var authoritativePosition = new Vector3(-me.x, me.y, me.z);
            if (!string.IsNullOrEmpty(me.hiding) && Vector3.Distance(transform.position, authoritativePosition) > .1f)
                Teleport(me.x, me.y, me.z);
            SendMovement(me, state);
            bool moving = controlled && MovementAxes().sqrMagnitude > .01f;
            currentBob = Mathf.Lerp(currentBob, moving ? (Sprinting ? .028f : .018f) : 0, Time.deltaTime * 10);
            footBob += Time.deltaTime * (moving ? (Sprinting ? 13 : 8) : 2);
            view.transform.localPosition = new Vector3(0, me.down ? .55f : 1.65f + Mathf.Sin(footBob) * currentBob, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, me.down ? -12.6f : Mathf.Sin(footBob * .5f) * currentBob * 6.87f);
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, cameraUp ? 57 : Sprinting ? 74 : 68, Time.deltaTime * 5);
            var classData = world.GetClass(me.characterClass);
            flashlight.enabled = lightOn && HoldingFlashlight(me) && !me.down && string.IsNullOrEmpty(me.hiding);
            flashlight.range = classData.bonuses.flashlightDist;
            flashlight.spotAngle = classData.bonuses.flashlightAngle * Mathf.Rad2Deg * 2;
            flashlight.innerSpotAngle = flashlight.spotAngle * .52f;
            UpdateEquipment(me, state, moving ? (Sprinting ? 1.7f : 1f) : 0);
            FindInteraction(me, state);
            if (Input.GetKeyDown(KeyCode.E) && !state.paused && !me.down && !ui.HasModal && !pausedLocally && !string.IsNullOrEmpty(InteractionId))
            {
                if (InteractionId == "broadcast" && !state.calibrated)
                    ui.ShowCalibration();
                else session.Command(new NetPacket { type = "interact", id = InteractionId });
            }
            SyncRemote(state);
        }

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
            firstPerson.Present(me, cameraUp, ui.MapOpen, lightOn, movement, state.paused || pausedLocally);
            firstPerson.SetVisible(state.phase == "playing" && !me.down && string.IsNullOrEmpty(me.hiding));
            flashlight.enabled = lightOn && HoldingFlashlight(me) && !me.down && string.IsNullOrEmpty(me.hiding);
            if(flashlight.enabled && firstPerson.TryGetFlashlightPose(out var position,out var rotation))
                flashlight.transform.SetPositionAndRotation(position,rotation);
        }

        void Move(PlayerState me, RoomState state)
        {
            if(!verificationMovement)ReadLook();
            transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);
            var axes=MovementAxes();
            var forward = axes.y;
            var sideways = axes.x;
            var direction = (transform.forward * forward + transform.right * sideways).normalized;
            bool sprint = direction.sqrMagnitude > 0 && (verificationMovement?verificationSprint:Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && (state.creative || stamina > 2);
            Sprinting = sprint;
            var bonuses = world.GetClass(me.characterClass).bonuses;
            stamina = state.creative ? 100 : Mathf.Clamp(stamina + (sprint ? -23 : 14 * bonuses.staminaRecovery) * Time.deltaTime, 0, 100);
            float speed = sprint ? 4.5f * bonuses.speedMultiplier : 2.6f;
            ApplyGravity(direction * speed);
        }

        void ReadLook()
        {
            // Legacy mouse axes already scale pixel motion by 0.1; restore the web game's 0.002 rad/pixel.
            yaw -= Mathf.Clamp(Input.GetAxisRaw("Mouse X"), -8, 8) * .02f * DeadAirSettings.Sensitivity;
            pitch = Mathf.Clamp(pitch + Mathf.Clamp(Input.GetAxisRaw("Mouse Y"), -8, 8) * .02f * DeadAirSettings.Sensitivity, -1.35f, 1.35f);
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
            if (!controller.enabled) return;
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
            if(me!=null){me.flashlight=on;if(firstPerson!=null)UpdateEquipment(me,session.State,0);}
            flashlight.enabled=on&&HoldingFlashlight(me);
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

        void SendMovement(PlayerState me, RoomState state)
        {
            // Send the stopped state too: opening a map must not leave a network player
            // sprinting indefinitely or keep footsteps/noise active on other clients.
            if (state.paused) return;
            if (Time.time >= nextSend)
            {
                nextSend = Time.time + .066f;
                int sequence=++nextMoveSequence;
                sentMovement[sequence]=transform.position;
                if(sentMovement.Count>256)sentMovement.Remove(sequence-256);
                session.Command(new NetPacket
                {
                    type = "move", x = -transform.position.x, y = transform.position.y, z = transform.position.z,
                    moveSequence=sequence,
                    yaw = yaw, pitch = pitch, flashlight = lightOn && HoldingFlashlight(me) && !me.down && string.IsNullOrEmpty(me.hiding),
                    camera = cameraUp && !me.down && string.IsNullOrEmpty(me.hiding), mapOpen = ui.MapOpen && !me.down && string.IsNullOrEmpty(me.hiding), sprinting = Sprinting
                });
            }
        }

        void ConsiderInteraction(string id, string label, float x, float y, float z, float range, Vector2 origin, Vector2 forward, ref float nearest, float eyeOffset = 0)
        {
            var offset = new Vector2(x, z) - origin;
            float distance = Mathf.Sqrt(offset.sqrMagnitude + Mathf.Pow(y + eyeOffset - transform.position.y - 1, 2));
            if (distance >= nearest || distance > range || (distance > .9f && Vector2.Dot(forward, offset.normalized) < .35f)) return;
            if (!DeadAirRules.HasInteractionSightline(transform.position + Vector3.up * 1.4f,
                new Vector3(-x, y + eyeOffset, z), id)) return;
            nearest = distance; InteractionId = id; InteractionLabel = label;
        }

        void FindInteraction(PlayerState me, RoomState state)
        {
            InteractionId = InteractionLabel = null;
            if (me.down) return;
            if (!string.IsNullOrEmpty(me.hiding)) { InteractionId = "exit-locker"; InteractionLabel = "Вийти з шафи"; return; }
            float nearest = 2.5f;
            var origin = new Vector2(-transform.position.x, transform.position.z);
            var forward = new Vector2(-transform.forward.x, transform.forward.z);
            foreach (var locker in world.lockers)
                ConsiderInteraction(locker.id, "Сховатися: " + locker.label, locker.x, locker.y, locker.z, 2.2f, origin, forward, ref nearest, 1);
            foreach (var door in world.doors)
            {
                var current = state.Door(door.id);
                string label = door.label + (current != null && current.open ? " — зачинити" : " — відчинити");
                if (current != null && current.locked && !me.HasItem(current.requiredKey)) label += " (потрібна картка)";
                ConsiderInteraction(door.id, label, door.x, door.y, door.z, 2.5f, origin, forward, ref nearest);
            }
            if (state.droppedItems != null)
            {
                for (int i = 0; i < state.droppedItems.Count; i++)
                {
                    var dropped = state.droppedItems[i];
                    ConsiderInteraction("dropped:" + dropped.id, "Підняти: " + dropped.label, dropped.x, dropped.y, dropped.z, 2.2f, origin, forward, ref nearest, 1);
                }
            }
            foreach (var obj in world.objects)
            {
                if (state.Collected(obj.id) || obj.id == "power" && state.power || obj.id == "broadcast" && state.broadcast ||
                    obj.id == "fuel-canister" && state.canister || obj.id == "pressure-valve" && state.valveOpened) continue;
                ConsiderInteraction(obj.id, obj.label, obj.x, obj.y, obj.z, 2.5f, origin, forward, ref nearest);
            }
            foreach (var other in state.players)
                if (other.id != session.LocalId && other.down)
                    ConsiderInteraction("revive:" + other.id, "Підняти " + other.name, other.x, other.y, other.z, 2.2f, origin, forward, ref nearest, 1);
        }

        readonly List<string> remoteIdsToRemove = new List<string>();
        void SyncRemote(RoomState state)
        {
            var active = new HashSet<string>();
            foreach (var p in state.players)
            {
                if (p.id == session.LocalId) continue;
                active.Add(p.id);
                if (remoteViews.TryGetValue(p.id, out var oldView) && remoteClasses[p.id] != p.characterClass)
                {
                    Destroy(oldView); remoteViews.Remove(p.id); remoteClasses.Remove(p.id);
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
                    viewObject.transform.position = new Vector3(-p.x, p.y, p.z);
                }
                var motion = viewObject.GetComponent<DeadAirCharacterMotion>();
                if (motion != null) motion.Present(p, state.elapsed, state.paused);
                viewObject.transform.position = Vector3.Lerp(viewObject.transform.position, new Vector3(-p.x, p.y + (p.down ? .2f : 0), p.z), Time.deltaTime * 12);
                viewObject.transform.rotation = Quaternion.Euler(0, 180 - p.yaw * Mathf.Rad2Deg, p.down ? -90 : 0);
                viewObject.SetActive(string.IsNullOrEmpty(p.hiding));
            }
            remoteIdsToRemove.Clear();
            foreach (var id in remoteViews.Keys)
                if (!active.Contains(id)) remoteIdsToRemove.Add(id);
            for (int i = 0; i < remoteIdsToRemove.Count; i++)
            {
                var id = remoteIdsToRemove[i];
                Destroy(remoteViews[id]);
                remoteViews.Remove(id);
                remoteClasses.Remove(id);
            }
            remoteIdsToRemove.Clear();
        }

        void ShowMenuCamera()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            flashlight.enabled = false;
            firstPerson.SetVisible(false);
            InteractionId = InteractionLabel = null;
            menuBackdrop.Frame(view, transform);
            UpdateMenuCharacter();
        }

        void Teleport(float x, float y, float z)
        {
            controller.enabled = false;
            transform.position = new Vector3(-x, y, z);
            verticalVelocity = 0;
            controller.enabled = true;
            sentMovement.Clear();
            reconciledMoveSequence=session.State?.Player(session.LocalId)?.moveSequence??0;
            nextMoveSequence=Mathf.Max(nextMoveSequence,reconciledMoveSequence);
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
            if (menuCharacter != null && menuClass == selected) return;
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
            AddMenuCharacterLight("Operative preview key", new Vector3(1.3f, 2.5f, 2.2f), new Color32(209, 199, 177, 255), .9f);
            AddMenuCharacterLight("Operative preview rim", new Vector3(-1.7f, 2.25f, -1.3f), new Color32(194, 39, 31, 255), 1.35f);
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
            if (open) cameraUp = false;
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
