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
        Transform heldEquipment;
        Material coatMaterial;
        GameObject menuCharacter;
        string menuClass;
        readonly Dictionary<string, GameObject> remoteViews = new Dictionary<string, GameObject>();
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
        float yaw, pitch, stamina = 100, nextSend, nextRadio, footBob, currentBob;
        string previousPhase;
        string previousHiding;

        public void Initialize(WorldData data, DeadAirSession game, DeadAirUI gameUI)
        {
            world = data; session = game; ui = gameUI;
            controller = gameObject.AddComponent<CharacterController>();
            controller.height = 1.7f; controller.radius = .22f; controller.center = new Vector3(0, .85f, 0);
            controller.stepOffset = .28f; controller.slopeLimit = 45;
            transform.position = new Vector3(-data.spawn.x, 0, data.spawn.z);
            view = new GameObject("First person camera").AddComponent<Camera>();
            view.tag = "MainCamera";
            view.transform.SetParent(transform, false);
            view.transform.localPosition = new Vector3(0, 1.65f, 0);
            view.fieldOfView = 68; view.nearClipPlane = .08f; view.farClipPlane = 85;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color32(10, 16, 10, 255);
            view.allowHDR = true;
            view.gameObject.AddComponent<AudioListener>();
            view.gameObject.AddComponent<DeadAirAudioLimiter>();
            flashlight = new GameObject("Flashlight").AddComponent<Light>();
            flashlight.transform.SetParent(view.transform, false);
            flashlight.type = LightType.Spot;
            flashlight.range = 24; flashlight.spotAngle = 55.4f; flashlight.innerSpotAngle = 22;
            flashlight.color = new Color32(255, 233, 183, 255);
            flashlight.intensity = 1.44f;
            flashlight.shadows = LightShadows.Soft;
            flashlight.shadowStrength = .8f; flashlight.shadowBias = .025f;
            flashlight.shadowNormalBias = .08f; flashlight.renderMode = LightRenderMode.ForcePixel;
            flashlight.transform.localPosition = new Vector3(.16f, -.12f, .15f);
            CreateHeldEquipment();
        }

        void Update()
        {
            // Result screens and menu dialogs must accept Escape even when the
            // character is dead and gameplay input is no longer being processed.
            bool escapeHandled = Input.GetKeyDown(KeyCode.Escape) && ui.TryCloseTopModal();
            var state = session.State;
            IsPlaying = state != null && state.phase == "playing";
            if (menuCharacter != null) menuCharacter.SetActive(!IsPlaying);
            if (inspectionPose)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
                if (state != null) SyncRemote(state);
                return;
            }
            if (state == null)
            {
                previousPhase = previousHiding = null; pausedLocally = false;
                foreach (var remote in remoteViews.Values) Destroy(remote);
                remoteViews.Clear();ShowMenuCamera();return;
            }
            var me = state.Player(session.LocalId);
            if (me == null) { ShowMenuCamera(); return; }
            if (previousPhase != state.phase && state.phase == "playing")
            {
                pausedLocally = false;
                yaw = pitch = 0; stamina = 100; cameraUp = false; lightOn = true;
                Teleport(me.x, me.z);
            }
            previousPhase = state.phase;
            if (previousHiding != me.hiding)
            {
                Teleport(me.x, me.z);
                previousHiding = me.hiding;
            }
            if (!IsPlaying) { ShowMenuCamera(); SyncRemote(state); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && !escapeHandled)
                SetPause(!pausedLocally);
            if (Input.GetKeyDown(KeyCode.M)) ui.ToggleMap();
            var controlled = !pausedLocally && !state.paused && !ui.HasModal && !me.down && string.IsNullOrEmpty(me.hiding);
            Sprinting = false;
            if (controlled)
            {
                if (Input.GetKeyDown(KeyCode.F)) { lightOn = !lightOn; DeadAirAudio.Play("click"); }
                if (Input.GetKeyDown(KeyCode.V)) { cameraUp = !cameraUp; DeadAirAudio.Play("click"); }
                if (Input.GetKeyDown(KeyCode.Q)) ui.ToggleRadio();
                if (ui.RadioOpen)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1)) SendRadioPhrase(0);
                    else if (Input.GetKeyDown(KeyCode.Alpha2)) SendRadioPhrase(1);
                    else if (Input.GetKeyDown(KeyCode.Alpha3)) SendRadioPhrase(2);
                    else if (Input.GetKeyDown(KeyCode.Alpha4)) SendRadioPhrase(3);
                }
            }
            else ui.HideRadio();
            Cursor.lockState = controlled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !controlled;
            if (controlled) Move(me, state);
            else if (!string.IsNullOrEmpty(me.hiding) && Vector2.Distance(new Vector2(-transform.position.x, transform.position.z), new Vector2(me.x, me.z)) > .1f)
                Teleport(me.x, me.z);
            SendMovement(me, state);
            bool moving = controlled && (Mathf.Abs(Input.GetAxisRaw("Vertical")) + Mathf.Abs(Input.GetAxisRaw("Horizontal")) > .1f);
            currentBob = Mathf.Lerp(currentBob, moving ? (Sprinting ? .028f : .018f) : 0, Time.deltaTime * 10);
            footBob += Time.deltaTime * (moving ? (Sprinting ? 13 : 8) : 2);
            view.transform.localPosition = new Vector3(0, me.down ? .55f : 1.65f + Mathf.Sin(footBob) * currentBob, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, me.down ? -12.6f : Mathf.Sin(footBob * .5f) * currentBob * 6.87f);
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, cameraUp ? 57 : Sprinting ? 74 : 68, Time.deltaTime * 5);
            var classData = world.GetClass(me.characterClass);
            flashlight.enabled = lightOn && !me.down && string.IsNullOrEmpty(me.hiding);
            flashlight.range = classData.bonuses.flashlightDist;
            flashlight.spotAngle = classData.bonuses.flashlightAngle * Mathf.Rad2Deg * 2;
            flashlight.innerSpotAngle = flashlight.spotAngle * .4f;
            heldEquipment.gameObject.SetActive(cameraUp && !me.down && string.IsNullOrEmpty(me.hiding));
            heldEquipment.localPosition = new Vector3(0, Mathf.Sin(footBob) * .008f, 0);
            if (ColorUtility.TryParseHtmlString(classData.color, out var coat)) coatMaterial.color = coat;
            FindInteraction(me, state);
            if (Input.GetKeyDown(KeyCode.E) && !ui.HasModal && !pausedLocally && !string.IsNullOrEmpty(InteractionId))
            {
                if (InteractionId == "broadcast" && !state.calibrated)
                    ui.ShowCalibration();
                else session.Command(new NetPacket { type = "interact", id = InteractionId });
            }
            SyncRemote(state);
        }

        void Move(PlayerState me, RoomState state)
        {
            // Legacy mouse axes already scale pixel motion by 0.1; restore the web game's 0.002 rad/pixel.
            yaw -= Mathf.Clamp(Input.GetAxisRaw("Mouse X"), -8, 8) * .02f * DeadAirSettings.Sensitivity;
            pitch = Mathf.Clamp(pitch + Mathf.Clamp(Input.GetAxisRaw("Mouse Y"), -8, 8) * .02f * DeadAirSettings.Sensitivity, -1.35f, 1.35f);
            if (Input.GetKey(KeyCode.LeftArrow)) yaw += Time.deltaTime * 1.6f;
            if (Input.GetKey(KeyCode.RightArrow)) yaw -= Time.deltaTime * 1.6f;
            if (Input.GetKey(KeyCode.UpArrow)) pitch = Mathf.Min(1.3f, pitch + Time.deltaTime);
            if (Input.GetKey(KeyCode.DownArrow)) pitch = Mathf.Max(-1.3f, pitch - Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, 0);
            var forward = Input.GetAxisRaw("Vertical");
            var sideways = Input.GetAxisRaw("Horizontal");
            var direction = (transform.forward * forward + transform.right * sideways).normalized;
            bool sprint = direction.sqrMagnitude > 0 && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && (state.creative || stamina > 2);
            Sprinting = sprint;
            var bonuses = world.GetClass(me.characterClass).bonuses;
            stamina = state.creative ? 100 : Mathf.Clamp(stamina + (sprint ? -23 : 14 * bonuses.staminaRecovery) * Time.deltaTime, 0, 100);
            float speed = sprint ? 4.5f * bonuses.speedMultiplier : 2.6f;
            controller.Move((direction * speed + Vector3.down * .5f) * Time.deltaTime);
        }

        void SendMovement(PlayerState me, RoomState state)
        {
            // Send the stopped state too: opening a map must not leave a network player
            // sprinting indefinitely or keep footsteps/noise active on other clients.
            if (state.paused) return;
            if (Time.time >= nextSend)
            {
                nextSend = Time.time + .066f;
                session.Command(new NetPacket
                {
                    type = "move", x = -transform.position.x, z = transform.position.z,
                    yaw = yaw, pitch = pitch, flashlight = lightOn && !me.down && string.IsNullOrEmpty(me.hiding),
                    camera = cameraUp && !me.down && string.IsNullOrEmpty(me.hiding), sprinting = Sprinting
                });
            }
        }

        void FindInteraction(PlayerState me, RoomState state)
        {
            InteractionId = InteractionLabel = null;
            if (me.down) return;
            if (!string.IsNullOrEmpty(me.hiding)) { InteractionId = "exit-locker"; InteractionLabel = "Вийти з шафи"; return; }
            float nearest = 2.5f;
            var origin = new Vector2(-transform.position.x, transform.position.z);
            var forward = new Vector2(-transform.forward.x, transform.forward.z);
            Action<string, string, float, float, float> consider = (id, label, x, z, range) =>
            {
                var offset = new Vector2(x, z) - origin;
                float distance = offset.magnitude;
                if (distance >= nearest || distance > range || (distance > .9f && Vector2.Dot(forward, offset.normalized) < .35f)) return;
                nearest = distance; InteractionId = id; InteractionLabel = label;
            };
            foreach (var locker in world.lockers)
                consider(locker.id, "Сховатися: " + locker.label, locker.x, locker.z, 2.2f);
            foreach (var door in world.doors)
            {
                var current = state.Door(door.id);
                string label = door.label + (current != null && current.open ? " — зачинити" : " — відчинити");
                if (current != null && current.locked && !state.keycards.Contains(current.requiredKey)) label += " (картка)";
                consider(door.id, label, door.x, door.z, 2.5f);
            }
            foreach (var obj in world.objects)
            {
                if (state.Collected(obj.id) || obj.id == "power" && state.power || obj.id == "broadcast" && state.broadcast ||
                    obj.id == "fuel-canister" && state.canister || obj.id == "pressure-valve" && state.valveOpened) continue;
                consider(obj.id, obj.label, obj.x, obj.z, 2.5f);
            }
            foreach (var other in state.players)
                if (other.id != session.LocalId && other.down)
                    consider("revive:" + other.id, "Підняти " + other.name, other.x, other.z, 2.2f);
        }

        void SyncRemote(RoomState state)
        {
            var active = new HashSet<string>();
            foreach (var p in state.players)
            {
                if (p.id == session.LocalId) continue;
                active.Add(p.id);
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
                }
                viewObject.transform.position = Vector3.Lerp(viewObject.transform.position, new Vector3(-p.x, p.down ? .2f : 0, p.z), Time.deltaTime * 12);
                viewObject.transform.rotation = Quaternion.Euler(0, 180 - p.yaw * Mathf.Rad2Deg, p.down ? -90 : 0);
                viewObject.SetActive(string.IsNullOrEmpty(p.hiding));
            }
            foreach (var id in new List<string>(remoteViews.Keys))
                if (!active.Contains(id)) { Destroy(remoteViews[id]); remoteViews.Remove(id); }
        }

        void ShowMenuCamera()
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            view.transform.localRotation = Quaternion.identity;
            view.fieldOfView = 54;
            flashlight.enabled = false;
            heldEquipment.gameObject.SetActive(false);
            InteractionId = InteractionLabel = null;
            bool lobby = session.State != null && session.State.phase == "lobby";
            transform.position = new Vector3(lobby ? -.7f : -1.7f, 0, lobby ? 28.5f : 28);
            transform.rotation = Quaternion.identity;
            view.transform.localPosition = new Vector3(0, lobby ? 2.1f : 2, 0);
            var target = new Vector3(lobby ? -.7f : -1.5f, 1.25f, 19.2f);
            view.transform.rotation = Quaternion.LookRotation(target - view.transform.position);
            UpdateMenuCharacter();
        }

        void Teleport(float x, float z)
        {
            controller.enabled = false;
            transform.position = new Vector3(-x, 0, z);
            controller.enabled = true;
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
            menuCharacter.transform.position = new Vector3(-3.4f, 0, 21);
            menuCharacter.transform.rotation = Quaternion.Euler(0, .16f * Mathf.Rad2Deg, 0);
            foreach (var collider in menuCharacter.GetComponentsInChildren<Collider>()) Destroy(collider);
        }

        void CreateHeldEquipment()
        {
            heldEquipment = new GameObject("VHS-C / first person equipment").transform;
            heldEquipment.SetParent(view.transform, false);
            // These shaders are referenced by the imported scene materials, so the
            // player build retains them even when Unity strips unused built-ins.
            var surfaceShader = Shader.Find("Dead Air/Source Surface");
            var unlitShader = Shader.Find("Dead Air/Source Unlit");
            if (surfaceShader == null || unlitShader == null)
                throw new System.InvalidOperationException("The imported world shaders are missing from this build.");
            var skin = new Material(surfaceShader) { color = new Color32(133, 118, 92, 255) };
            coatMaterial = new Material(surfaceShader) { color = new Color32(30, 66, 63, 255) };
            var metal = new Material(surfaceShader) { color = new Color32(37, 43, 40, 255) };
            skin.SetFloat("_Glossiness", .1f); coatMaterial.SetFloat("_Glossiness", .1f); metal.SetFloat("_Glossiness", .15f);
            EquipmentPart("Coat sleeve", PrimitiveType.Cube, new Vector3(.13f, .16f, .3f), new Vector3(.34f, -.4f, .4f), coatMaterial).localRotation = Quaternion.Euler(17.2f, 0, 0);
            EquipmentPart("Hand", PrimitiveType.Cube, new Vector3(.12f, .12f, .15f), new Vector3(.32f, -.32f, .58f), skin);
            EquipmentPart("VHS-C body", PrimitiveType.Cube, new Vector3(.24f, .19f, .34f), new Vector3(.3f, -.25f, .65f), metal);
            EquipmentPart("Lens barrel", PrimitiveType.Cylinder, new Vector3(.15f, .075f, .15f), new Vector3(.3f, -.24f, .9f), metal).localRotation = Quaternion.Euler(90, 0, 0);
            var glass = new Material(unlitShader) { color = new Color32(140, 171, 129, 255) };
            EquipmentPart("Lens", PrimitiveType.Cylinder, new Vector3(.128f, .003f, .128f), new Vector3(.3f, -.24f, .978f), glass).localRotation = Quaternion.Euler(90, 0, 0);
            var led = new Material(unlitShader) { color = new Color32(231, 101, 67, 255) };
            EquipmentPart("Record indicator", PrimitiveType.Cube, new Vector3(.016f, .016f, .012f), new Vector3(.36f, -.21f, .473f), led);
            heldEquipment.gameObject.SetActive(false);
        }

        Transform EquipmentPart(string name, PrimitiveType type, Vector3 size, Vector3 position, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(heldEquipment, false);
            part.transform.localScale = size;
            part.transform.localPosition = position;
            part.GetComponent<Renderer>().sharedMaterial = material;
            var collider = part.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            return part.transform;
        }

        // Coordinates and angles here use the original web map convention, like network state.
        public void SetInspectionPose(float sourceX, float z, float sourceYaw, float sourcePitch, bool equipment = false)
        {
            inspectionPose = true;
            yaw = sourceYaw; pitch = sourcePitch;
            Teleport(sourceX, z);
            transform.rotation = Quaternion.Euler(0, 180 - sourceYaw * Mathf.Rad2Deg, 0);
            view.transform.localPosition = new Vector3(0, 1.65f, 0);
            view.transform.localRotation = Quaternion.Euler(-sourcePitch * Mathf.Rad2Deg, 0, 0);
            view.fieldOfView = equipment ? 57 : 68;
            lightOn = true; cameraUp = equipment;
            flashlight.enabled = true;
            flashlight.range = 30;
            flashlight.spotAngle = world.GetClass("engineer").bonuses.flashlightAngle * Mathf.Rad2Deg * 2;
            flashlight.innerSpotAngle = flashlight.spotAngle * .4f;
            heldEquipment.gameObject.SetActive(equipment);
            var me = session.State?.Player(session.LocalId);
            if (me != null) { me.x = sourceX; me.z = z; me.yaw = sourceYaw; me.pitch = sourcePitch; me.camera = equipment; }
        }

        public void EndInspection() { inspectionPose = false; }

        public void SetPause(bool paused)
        {
            pausedLocally = paused;
            ui.ShowPause(paused);
        }
    }
}
