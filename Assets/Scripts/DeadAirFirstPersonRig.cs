using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>Camera-space, faceted hands and the equipment actually selected by the player.</summary>
    public sealed class DeadAirFirstPersonRig : MonoBehaviour
    {
        const int EquipmentLayer = 30;
        const float SmokingDuration = 7f;
        const float PersonalActionDuration = 2.5f;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly Dictionary<string, Transform> items = new Dictionary<string, Transform>();
        readonly List<Transform> packCigarettes = new List<Transform>();
        readonly List<Transform> smoke = new List<Transform>();
        readonly Dictionary<Transform, HandGeometry> hands = new Dictionary<Transform, HandGeometry>();
        readonly List<HandPartBinding> handParts = new List<HandPartBinding>(44);
        readonly Dictionary<string, ImportedHandParts> importedHands = new Dictionary<string, ImportedHandParts>();
        sealed class HandGeometry
        {
            public int side;
            public Transform palm, forearm, cuff, thumb, thumbMiddle, thumbTip;
            public HandAttachmentFrames attachments;
            public readonly Transform[] proximal = new Transform[4], middle = new Transform[4], distal = new Transform[4], tip = new Transform[4];
        }
        sealed class HandAttachmentFrames
        {
            public Vector3 palmWrist, armStart, armWrist, cuffCentre;
        }
        sealed class HandPartBinding
        {
            public string sourceName;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public Mesh proceduralMesh;
            public Material[] proceduralMaterials;
        }
        sealed class ImportedHandParts
        {
            public Mesh[] meshes;
            public Material[][] materials;
            public HandAttachmentFrames rightAttachments, leftAttachments;
        }
        Camera sourceCamera, equipmentCamera;
        Transform rig, rightHand, leftHand, packLid, cigarette, lighter, flame, ember, compassNeedle, pendulum, mapMarker;
        Material skin, sleeve, cuff, dark, metal, brass, paper, red, teal, lens, smokeMaterial, burgundy, olive, flameMaterial;
        WorldData world;
        Mesh boxMesh, tubeMesh, palmMesh;
        string currentMode, currentClass;
        float clock, bob, equip, actionTime = -1, synchronizedAge = -1, mapMinX, mapMinZ, mapMaxX, mapMaxZ;
        bool visible, hadEquipmentLayer, synchronized;
        bool bareArms;
        Vector3 previousForward;
        public bool IsUsingUnique => actionTime >= 0;
        public bool UsingImportedHands { get; private set; }
        public string ImportedHandsRole { get; private set; }

        public void CompleteEquipTransition()
        {
            if (rig != null) rig.localPosition += Vector3.up * equip;
            equip = 0;
        }

        public void Initialize(Camera view, WorldData data)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (sourceCamera != null)
            {
                if (sourceCamera != view) throw new InvalidOperationException("The first person rig already has a view camera.");
                return;
            }
            sourceCamera = view;
            world = data;
            rig = Group("Low-poly first person hands", view.transform);
            previousForward = view.transform.forward;
            var surface = Shader.Find("Dead Air/Source Surface");
            var unlit = Shader.Find("Dead Air/Source Unlit");
            if (surface == null || unlit == null)
                throw new InvalidOperationException("The first person rig requires the imported Source shaders.");
            skin = MakeMaterial(surface, "Skin", new Color32(203, 161, 139, 255));
            sleeve = MakeMaterial(surface, "Class sleeves", new Color32(61, 32, 45, 255));
            cuff = MakeMaterial(surface, "Cuffs", new Color32(197, 188, 168, 255));
            dark = MakeMaterial(surface, "Worn black polymer", new Color32(31, 37, 37, 255));
            burgundy = MakeMaterial(surface, "Cigarette pack burgundy", new Color(.20f, .035f, .065f));
            olive = MakeMaterial(surface, "Fuel can paint", new Color(.17f, .22f, .12f));
            metal = MakeMaterial(surface, "Oxidised steel", new Color32(119, 129, 127, 255));
            brass = MakeMaterial(surface, "Old brass", new Color32(166, 125, 57, 255));
            paper = MakeMaterial(surface, "Paper", new Color32(219, 207, 166, 255));
            red = MakeMaterial(unlit, "Red recording light", new Color32(205, 62, 38, 255));
            teal = MakeMaterial(unlit, "LCD phosphor", new Color32(118, 155, 124, 255));
            lens = MakeMaterial(unlit, "Flashlight glass", new Color32(234, 216, 163, 255));
            flameMaterial = MakeMaterial(unlit, "Lighter flame", new Color(1, .68f, .22f));
            smokeMaterial = MakeMaterial(unlit, "Cigarette smoke", new Color(.78f, .80f, .79f, .18f));
            smokeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            smokeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            smokeMaterial.SetFloat("_ZWrite", 0);
            smokeMaterial.SetFloat("_Cull", 0);
            smokeMaterial.renderQueue = 3000;
            smokeMaterial.mainTexture = CreateSmokeTexture();
            boxMesh = Own(CreateBox());
            tubeMesh = Own(CreateTube(8));
            palmMesh = Own(CreatePalm());
            rightHand = CreateHand("Right articulated hand", rig, 1);
            leftHand = CreateHand("Left articulated hand", rig, -1);
            CreateFlashlight();
            CreateCamera();
            CreateMap();
            CreateInventoryItems();
            CreateCigarettes();
            CreateWrench();
            CreateRecorder();
            CreateCompass();
            // Equipment draws after the world camera has resolved its HDR image,
            // before the existing VHS output camera presents the completed frame.
            var cameraObject = new GameObject("First person equipment camera");
            cameraObject.transform.SetParent(view.transform, false);
            equipmentCamera = cameraObject.AddComponent<Camera>();
            equipmentCamera.enabled = false;
            equipmentCamera.clearFlags = CameraClearFlags.Depth;
            equipmentCamera.cullingMask = 1 << EquipmentLayer;
            equipmentCamera.nearClipPlane = .025f;
            equipmentCamera.farClipPlane = 3;
            equipmentCamera.fieldOfView = 62;
            equipmentCamera.allowHDR = view.allowHDR;
            equipmentCamera.allowMSAA = false;
            equipmentCamera.useOcclusionCulling = false;
            equipmentCamera.targetDisplay = view.targetDisplay;
            var fillObject = new GameObject("Subtle equipment bounce light");
            fillObject.transform.SetParent(cameraObject.transform, false);
            fillObject.transform.localPosition = new Vector3(-.25f, .18f, 0);
            fillObject.transform.localRotation = Quaternion.Euler(25, -25, 0);
            var fillLight = fillObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color32(191, 202, 219, 255);
            fillLight.intensity = .27f;
            fillLight.cullingMask = 1 << EquipmentLayer;
            fillLight.shadows = LightShadows.None;
            fillLight.renderMode = LightRenderMode.ForcePixel;
            hadEquipmentLayer = (view.cullingMask & (1 << EquipmentLayer)) != 0;
            view.cullingMask &= ~(1 << EquipmentLayer);
            SetVisible(false);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (rig != null) rig.gameObject.SetActive(value);
            if (equipmentCamera != null)
                equipmentCamera.enabled = value && sourceCamera != null && sourceCamera.enabled && isActiveAndEnabled;
            if (!value) { actionTime = -1; currentMode = null; }
        }

        public void TriggerUniqueAction()
        {
            // Standalone previews can advance an action without a room clock.
            synchronized = false;
            synchronizedAge = -1;
            actionTime = 0;
        }

        public void SynchronizeUniqueAction(float acceptedAge)
        {
            synchronized = true;
            synchronizedAge = float.IsNaN(acceptedAge) || float.IsInfinity(acceptedAge) || acceptedAge < 0 ? -1 : acceptedAge;
            actionTime = synchronizedAge >= 0 && synchronizedAge < ActionDuration(currentClass) ? synchronizedAge : -1;
        }

        static float ActionDuration(string id) => id == "operator" ? SmokingDuration : PersonalActionDuration;

        public void ResetPresentation()
        {
            actionTime = -1;
            synchronized = false;
            synchronizedAge = -1;
            currentMode = null;
            clock = bob = 0;
            equip = .15f;
            if (sourceCamera != null) previousForward = sourceCamera.transform.forward;
            if (rig != null) { rig.localPosition = Vector3.zero; rig.localRotation = Quaternion.identity; }
            foreach (var item in items) item.Value.gameObject.SetActive(false);
            foreach (var puff in smoke) puff.gameObject.SetActive(false);
            if (rightHand != null) rightHand.gameObject.SetActive(false);
            if (leftHand != null) leftHand.gameObject.SetActive(false);
            if (cigarette != null) cigarette.gameObject.SetActive(false);
            if (lighter != null) lighter.gameObject.SetActive(false);
            if (flame != null) flame.gameObject.SetActive(false);
            if (ember != null) ember.gameObject.SetActive(false);
        }

        public void Present(PlayerState me, bool cameraUp, bool mapOpen, bool flashlightOn, float movementAmount, bool paused)
        {
            if (rig == null || me == null) return;
            if (me.down || !string.IsNullOrEmpty(me.hiding)) { SetVisible(false); return; }
            if (!visible) SetVisible(true);
            if (currentClass != me.characterClass)
            {
                if (currentClass != null)
                {
                    bool wasSynchronized = synchronized;
                    float acceptedAge = synchronizedAge;
                    ResetPresentation();
                    synchronized = wasSynchronized;
                    synchronizedAge = acceptedAge;
                }
                SetClass(me.characterClass);
            }
            float dt = paused ? 0 : Mathf.Min(Time.deltaTime, .05f);
            // Raising the map is presentation: solo play can already be paused
            // by its modal while this transition still has to finish.
            float presentationDt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            clock += dt;
            bob = Mathf.Lerp(bob, Mathf.Clamp01(movementAmount), dt * 9);
            if (synchronized)
                actionTime = synchronizedAge >= 0 && synchronizedAge < ActionDuration(me.characterClass) ? synchronizedAge : -1;
            // The selected equipment and visible prop must agree. Stowing a
            // personal item cancels its presentation after the accepted use.
            if (!me.uniqueItemEquipped || cameraUp || mapOpen) actionTime = -1;
            if (actionTime >= 0 && !synchronized)
            {
                actionTime += paused ? 0 : Time.deltaTime;
                if (actionTime >= ActionDuration(me.characterClass)) actionTime = -1;
            }
            string selected = me.inventory != null && me.selectedSlot >= 0 && me.selectedSlot < me.inventory.Count
                ? me.inventory[me.selectedSlot] : "empty";
            string mode = mapOpen ? "map" : cameraUp ? "camera" : me.uniqueItemEquipped ? me.characterClass : InventoryMode(selected);
            if (currentMode != mode)
            {
                currentMode = mode;
                equip = .15f;
                foreach (var item in items) item.Value.gameObject.SetActive(item.Key == mode);
            }
            equip = Mathf.MoveTowards(equip, 0, presentationDt * .9f);
            float swayX = Mathf.Sin(clock * 7.5f) * .008f * bob;
            float swayY = Mathf.Abs(Mathf.Cos(clock * 7.5f)) * .007f * bob;
            var turn = sourceCamera.transform.InverseTransformDirection(previousForward - sourceCamera.transform.forward);
            previousForward = Vector3.Lerp(previousForward, sourceCamera.transform.forward, dt * 12);
            float handLift = mode == "map" ? 0 : .09f;
            rig.localPosition = new Vector3(swayX + Mathf.Clamp(turn.x * .05f, -.025f, .025f), swayY + handLift - equip, 0);
            rig.localRotation = Quaternion.Euler(Mathf.Clamp(turn.y * 4, -2, 2), 0, Mathf.Sin(clock * 3.75f) * bob);
            RelaxHand(rightHand, new Vector3(.25f, -.30f, .43f), new Vector3(-18, -12, -10));
            RelaxHand(leftHand, new Vector3(-.25f, -.31f, .43f), new Vector3(-18, 12, 10));
            rightHand.gameObject.SetActive(mode != "empty");
            leftHand.gameObject.SetActive(mode == "map" || mode == "camera" || mode == "operator");
            lens.color = flashlightOn ? new Color32(251, 231, 176, 255) : new Color32(49, 62, 60, 255);
            foreach (var puff in smoke) puff.gameObject.SetActive(false);
            cigarette.gameObject.SetActive(false);
            lighter.gameObject.SetActive(false);
            flame.gameObject.SetActive(false);
            if (mode == "map")
            {
                GripMap(rightHand, 1);
                GripMap(leftHand, -1);
                float px = Mathf.InverseLerp(mapMinX, mapMaxX, me.x), pz = Mathf.InverseLerp(mapMinZ, mapMaxZ, me.z);
                bool upstairs=me.y>2.1f;
                mapMarker.localPosition = new Vector3(Mathf.Lerp(upstairs?.017f:-.302f, upstairs?.302f:-.017f, px), Mathf.Lerp(-.205f, .205f, pz), -.004f);
            }
            else if (mode == "camera")
            {
                GripCamera();
            }
            else if (mode == "operator") AnimateCigarette(me.cigarettesRemaining);
            else if (mode == "engineer")
            {
                float t = IsUsingUnique ? Mathf.Sin(Mathf.Clamp01(actionTime / PersonalActionDuration) * Mathf.PI) : 0;
                items[mode].localRotation = Quaternion.Euler(-10 + t * 18, -20 + t * 30, -17 - t * 35);
                GripVerticalHandle(rightHand, items[mode], items[mode].Find("Wrapped handle"));
            }
            else if (mode == "journalist")
            {
                float lift = IsUsingUnique ? Mathf.Sin(Mathf.Clamp01(actionTime / PersonalActionDuration) * Mathf.PI) : 0;
                items[mode].localPosition = new Vector3(.25f, -.17f + lift * .075f, .48f - lift * .045f);
                GripBox(rightHand, items[mode], items[mode].Find("Pocket recorder shell"), 1);
            }
            else if (mode == "dispatcher")
            {
                // A real north needle, with a free-swinging brass pendulum below the case.
                // WorldData north is +Z; yaw zero faces the station's -Z corridor.
                compassNeedle.localRotation = Quaternion.Euler(0, 0, 180 + me.yaw * Mathf.Rad2Deg);
                pendulum.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(clock * (IsUsingUnique ? 7 : 2.5f)) * (IsUsingUnique ? 22 : 5));
                GripCompass();
            }
            else if (mode == "flashlight") GripFlashlight();
            else if (mode == "keycard") GripBox(rightHand, items[mode], items[mode].Find("ID card"), 1);
            else if (mode == "tape") GripBox(rightHand, items[mode], items[mode].Find("Tape casing"), 1);
            else if (mode == "fuse") GripVerticalHandle(rightHand, items[mode], items[mode].Find("Ceramic fuse"));
            else if (mode == "fuel-canister") GripFuelCan();
        }

        public void PresentLadder(float age, bool up, bool intactWindow)
        {
            if (rig == null || !visible) return;
            currentMode = null;
            actionTime = -1;
            foreach (var item in items) item.Value.gameObject.SetActive(false);
            foreach (var puff in smoke) puff.gameObject.SetActive(false);
            cigarette.gameObject.SetActive(false); lighter.gameObject.SetActive(false);
            flame.gameObject.SetActive(false); ember.gameObject.SetActive(false);
            rig.localPosition = Vector3.zero; rig.localRotation = Quaternion.identity;
            rightHand.gameObject.SetActive(true); leftHand.gameObject.SetActive(true);
            float route = DeadAirLadder.PathAge(age, up);
            float cycle = (route - .35f) * Mathf.PI * 4.3f;
            bool rungs = route > .35f && route < 3.45f;
            float rightReach = rungs ? Mathf.Sin(cycle) * .11f : 0;
            float leftReach = rungs ? Mathf.Sin(cycle + Mathf.PI) * .11f : 0;
            // Reach one hand at a time with a quiet camera, rather than rolling
            // the whole view from side to side as the old climb did.
            RelaxHand(rightHand, new Vector3(.21f, -.15f + rightReach, .47f), new Vector3(12, -8, -6));
            RelaxHand(leftHand, new Vector3(-.21f, -.15f + leftReach, .47f), new Vector3(12, 8, 6));
            if (up && intactWindow && route >= 3.45f && route <= 3.80f)
            {
                float strike = Mathf.Sin(Mathf.Clamp01((route - 3.45f) / .35f) * Mathf.PI);
                RelaxHand(rightHand, new Vector3(.19f - strike * .07f, -.12f + strike * .07f, .43f + strike * .20f),
                    new Vector3(-8 + strike * 35, -8, -8));
            }
            else if (route >= 3.80f)
            {
                RelaxHand(rightHand, new Vector3(.28f, -.19f, .50f), new Vector3(24, -12, -15));
                RelaxHand(leftHand, new Vector3(-.28f, -.19f, .50f), new Vector3(24, 12, 15));
            }
        }

        public void ConfigureRenderer(RenderTexture frame)
        {
            if (sourceCamera == null || equipmentCamera == null) return;
            equipmentCamera.targetTexture = frame;
            equipmentCamera.aspect = sourceCamera.aspect;
            equipmentCamera.rect = sourceCamera.rect;
            equipmentCamera.depth = sourceCamera.depth + .5f;
            equipmentCamera.targetDisplay = sourceCamera.targetDisplay;
            equipmentCamera.enabled = visible && sourceCamera.enabled && isActiveAndEnabled;
        }

        public void RenderEquipment(RenderTexture frame)
        {
            if (!visible || sourceCamera == null || equipmentCamera == null || !isActiveAndEnabled) return;
            ConfigureRenderer(frame);
            equipmentCamera.Render();
        }

        /// <summary>World-space emitter pose follows the visible animated lamp, including hand sway.</summary>
        public bool TryGetFlashlightPose(out Vector3 position, out Quaternion rotation)
        {
            position = sourceCamera != null ? sourceCamera.transform.position : transform.position;
            rotation = sourceCamera != null ? sourceCamera.transform.rotation : transform.rotation;
            if (!visible || rig == null || !rig.gameObject.activeInHierarchy ||
                (currentMode != "flashlight" && currentMode != "camera") || !items.TryGetValue(currentMode, out var item)) return false;
            var emitter = currentMode == "camera" ? new Vector3(-.065f, .120f, .150f) : new Vector3(0, 0, .176f);
            position = item.TransformPoint(emitter);
            rotation = item.rotation;
            return true;
        }

        void OnDisable()
        {
            if (equipmentCamera != null) equipmentCamera.enabled = false;
        }

        void SetClass(string id)
        {
            currentClass = id;
            bool bare = id == "operator" || id == "dispatcher";
            bareArms = bare;
            if (ApplyImportedHands(id)) return;
            // Match the Blender cast palette, including their clothing materials.
            Color tint = id == "operator" ? new Color(.72f, .49f, .42f)
                : id == "engineer" ? new Color(.53f, .39f, .29f) : new Color(.63f, .38f, .10f);
            SetTint(skin, tint);
            Color coat = id == "engineer" ? new Color(.019f, .029f, .033f) : new Color(.008f, .12f, .13f);
            SetTint(sleeve, bare ? tint : coat);
            SetTint(cuff, bare ? new Color(.77f, .44f, .09f) : new Color(.69f, .68f, .58f));
            foreach (var hand in new[] { rightHand, leftHand })
            {
                hand.Find("Tailored forearm").localScale = bare ? new Vector3(.073f, .24f, .071f) : new Vector3(.104f, .25f, .10f);
                hand.Find("Cuff or bracelet").localScale = bare ? new Vector3(.075f, .012f, .071f) : new Vector3(.091f, .037f, .086f);
            }
        }

        bool ApplyImportedHands(string role)
        {
            if (!importedHands.TryGetValue(role, out var imported))
            {
                imported = LoadImportedHands(role);
                // Loading and hierarchy lookup happen once per role, never during grip updates.
                importedHands.Add(role, imported);
            }
            bool complete = imported != null;
            for (int i = 0; i < handParts.Count; i++)
            {
                var target = handParts[i];
                target.filter.sharedMesh = complete ? imported.meshes[i] : target.proceduralMesh;
                target.renderer.sharedMaterials = complete ? imported.materials[i] : target.proceduralMaterials;
            }
            hands[rightHand].attachments = complete ? imported.rightAttachments : null;
            hands[leftHand].attachments = complete ? imported.leftAttachments : null;
            UsingImportedHands = complete;
            ImportedHandsRole = complete ? role : null;
            return complete;
        }

        ImportedHandParts LoadImportedHands(string role)
        {
            var prefab = Resources.Load<GameObject>("Characters/Hands/hand_" + role);
            if (prefab == null) return null;
            var sources = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (sources.ContainsKey(filter.name))
                {
                    Debug.LogWarning("Dead Air imported hands for " + role + " contain duplicate part " + filter.name + ". Using procedural hands.", this);
                    return null;
                }
                sources.Add(filter.name, filter);
            }
            var imported = new ImportedHandParts { meshes = new Mesh[handParts.Count], materials = new Material[handParts.Count][] };
            for (int i = 0; i < handParts.Count; i++)
            {
                string name = handParts[i].sourceName;
                if (!sources.TryGetValue(name, out var filter) || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                {
                    Debug.LogWarning("Dead Air imported hands for " + role + " are missing mesh part " + name + ". Using procedural hands.", this);
                    return null;
                }
                var renderer = filter.GetComponent<MeshRenderer>();
                var materials = renderer != null ? renderer.sharedMaterials : null;
                bool assigned = materials != null && materials.Length >= filter.sharedMesh.subMeshCount;
                if (assigned)
                    foreach (var material in materials) if (material == null) { assigned = false; break; }
                if (!assigned)
                {
                    Debug.LogWarning("Dead Air imported hands for " + role + " are missing materials for " + name + ". Using procedural hands.", this);
                    return null;
                }
                imported.meshes[i] = filter.sharedMesh;
                imported.materials[i] = materials;
            }
            imported.rightAttachments = MeasureAttachments(sources, "Right_");
            imported.leftAttachments = MeasureAttachments(sources, "Left_");
            return imported;
        }

        static HandAttachmentFrames MeasureAttachments(Dictionary<string, MeshFilter> sources, string prefix)
        {
            var palm = sources[prefix + "palm"].sharedMesh;
            var arm = sources[prefix + "forearm"].sharedMesh;
            var cuff = sources[prefix + "cuff"].sharedMesh;
            // Source fragments can taper and their end caps need not be centred
            // on the unit bounding box. Cache their actual cap centres once.
            var palmWrist = EndSectionCentre(palm, false);
            palmWrist.y += palm.bounds.size.y * .006f;
            return new HandAttachmentFrames {
                palmWrist = palmWrist,
                armStart = EndSectionCentre(arm, false),
                armWrist = EndSectionCentre(arm, true),
                cuffCentre = (EndSectionCentre(cuff, false) + EndSectionCentre(cuff, true)) * .5f
            };
        }

        static Vector3 EndSectionCentre(Mesh mesh, bool upper)
        {
            Bounds bounds = mesh.bounds;
            float end = upper ? bounds.max.y : bounds.min.y;
            float tolerance = Mathf.Max(.00001f, bounds.size.y * .0001f);
            Vector3 lower = new Vector3(float.PositiveInfinity, end, float.PositiveInfinity);
            Vector3 higher = new Vector3(float.NegativeInfinity, end, float.NegativeInfinity);
            foreach (var vertex in mesh.vertices)
                if (Mathf.Abs(vertex.y - end) <= tolerance)
                {
                    lower.x = Mathf.Min(lower.x, vertex.x); lower.z = Mathf.Min(lower.z, vertex.z);
                    higher.x = Mathf.Max(higher.x, vertex.x); higher.z = Mathf.Max(higher.z, vertex.z);
                }
            return (lower + higher) * .5f;
        }

        static string InventoryMode(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "empty") return "empty";
            if (id == "flashlight") return id;
            if (id.StartsWith("fuel-canister", StringComparison.Ordinal)) return "fuel-canister";
            if (id.StartsWith("keycard", StringComparison.Ordinal) || id.StartsWith("key-", StringComparison.Ordinal)) return "keycard";
            if (id.StartsWith("tape", StringComparison.Ordinal)) return "tape";
            if (id.StartsWith("fuse", StringComparison.Ordinal)) return "fuse";
            return "empty";
        }

        void AnimateCigarette(int remaining)
        {
            var pack = items["operator"];
            pack.localPosition = new Vector3(-.225f, -.195f, .46f);
            pack.localRotation = Quaternion.Euler(-12, -12, 12);
            GripBox(leftHand, pack, pack.Find("Burgundy cigarette pack"), -1);
            RelaxHand(rightHand, new Vector3(.235f, -.30f, .42f), new Vector3(-25, -8, -18));
            for (int i = 0; i < packCigarettes.Count; i++) packCigarettes[i].gameObject.SetActive(i < remaining);
            packLid.localRotation = Quaternion.Euler(IsUsingUnique ? -105 * Mathf.SmoothStep(0, 1, actionTime / .55f) : -12, 0, 0);
            if (!IsUsingUnique) return;
            float t = actionTime;
            cigarette.gameObject.SetActive(t > .45f);
            Quaternion cigaretteRotation = Quaternion.Euler(15, 0, -30);
            Vector3 rest = new Vector3(.235f, -.20f, .42f);
            Vector3 take = pack.localPosition + pack.localRotation * new Vector3(-.011f, .070f, -.009f)
                + cigaretteRotation * new Vector3(0, .017f, 0);
            Vector3 light = new Vector3(.07f, -.09f, .37f);
            Vector3 mouth = new Vector3(.055f, -.027f, .28f);
            Vector3 finish = new Vector3(.21f, -.11f, .43f);
            Vector3 position = t < .65f ? Vector3.Lerp(rest, take, Smooth(t / .65f))
                : t < 1.5f ? Vector3.Lerp(take, light, Smooth((t - .65f) / .85f))
                : t < 3.15f ? light
                : t < 3.7f ? Vector3.Lerp(light, mouth, Smooth((t - 3.15f) / .55f))
                : t < 4.65f ? mouth
                : Vector3.Lerp(mouth, finish, Smooth((t - 4.65f) / .6f));
            cigarette.localPosition = position;
            cigarette.localRotation = cigaretteRotation;
            if (t > .45f) GripCigarette();
            lighter.gameObject.SetActive(t > 1.5f && t < 3.25f);
            flame.gameObject.SetActive(t > 2.0f && t < 3.0f);
            ember.gameObject.SetActive(t > 2.25f);
            float packStow = Smooth((t - 1.05f) / .40f) * (1 - Smooth((t - 3.25f) / .40f));
            pack.localPosition += new Vector3(-.03f, -.45f, -.03f) * packStow;
            if (t > 1.5f && t < 3.25f)
            {
                Vector3 tip = rig.InverseTransformPoint(cigarette.TransformPoint(new Vector3(0, .066f, 0)));
                // Place the flame's upper edge against the exposed tobacco.
                float lighterLower = .30f * (1 - Smooth((t - 1.5f) / .35f)) + .22f * Smooth((t - 3.0f) / .25f);
                lighter.localPosition = tip + new Vector3(.005f, -.055f - lighterLower, 0);
                GripBox(leftHand, lighter, lighter.Find("Lighter body"), -1);
            }
            else GripBox(leftHand, pack, pack.Find("Burgundy cigarette pack"), -1);
            flame.localScale = new Vector3(.009f, .024f + Mathf.Sin(clock * 37) * .004f, .007f);
            if (t > 3)
                for (int i = 0; i < smoke.Count; i++)
                {
                    float age = Mathf.Repeat((t - 3) * .55f + i / (float)smoke.Count, 1);
                    smoke[i].gameObject.SetActive(true);
                    Vector3 origin = t > 4.65f ? new Vector3(-.02f, -.085f, .28f)
                        : rig.InverseTransformPoint(cigarette.TransformPoint(new Vector3(0, .071f, 0)));
                    smoke[i].localPosition = origin + new Vector3(Mathf.Sin(age * 10 + i) * age * .025f, age * .11f, age * .10f);
                    float size = .012f + age * .022f;
                    smoke[i].localScale = new Vector3(size * .7f, size, 1);
                    smoke[i].localRotation = Quaternion.Euler(0, 0, age * 190 + i * 50);
                }
        }

        static float Smooth(float t) { return Mathf.SmoothStep(0, 1, Mathf.Clamp01(t)); }
        static void PoseHand(Transform hand, Vector3 position, Vector3 angles) { hand.localPosition = position; hand.localRotation = Quaternion.Euler(angles); }

        Transform CreateHand(string name, Transform parent, int side)
        {
            var root = Group(name, parent);
            var anatomy = new HandGeometry { side = side };
            anatomy.forearm = Part("Tailored forearm", root, tubeMesh, sleeve, Vector3.zero, Vector3.one);
            anatomy.cuff = Part("Cuff or bracelet", root, tubeMesh, cuff, Vector3.zero, Vector3.one);
            anatomy.palm = Part("Faceted palm", root, palmMesh, skin, Vector3.zero, Vector3.one);
            for (int i = 0; i < 4; i++)
            {
                anatomy.proximal[i] = Part("Finger " + i + " proximal", root, tubeMesh, skin, Vector3.zero, Vector3.one);
                anatomy.middle[i] = Part("Finger " + i + " middle", root, tubeMesh, skin, Vector3.zero, Vector3.one);
                anatomy.distal[i] = Part("Finger " + i + " distal", root, tubeMesh, skin, Vector3.zero, Vector3.one);
                anatomy.tip[i] = Part("Finger " + i + " curled tip", root, tubeMesh, skin, Vector3.zero, Vector3.one);
            }
            anatomy.thumb = Part("Opposed thumb", root, tubeMesh, skin, Vector3.zero, Vector3.one);
            anatomy.thumbMiddle = Part("Thumb middle", root, tubeMesh, skin, Vector3.zero, Vector3.one);
            anatomy.thumbTip = Part("Thumb tip", root, tubeMesh, skin, Vector3.zero, Vector3.one);
            hands.Add(root, anatomy);
            string prefix = side == 1 ? "Right_" : "Left_";
            BindHandPart(prefix + "forearm", anatomy.forearm);
            BindHandPart(prefix + "cuff", anatomy.cuff);
            BindHandPart(prefix + "palm", anatomy.palm);
            for (int i = 0; i < 4; i++)
            {
                string finger = prefix + "finger" + i + "_";
                BindHandPart(finger + "proximal", anatomy.proximal[i]);
                BindHandPart(finger + "middle", anatomy.middle[i]);
                BindHandPart(finger + "distal", anatomy.distal[i]);
                BindHandPart(finger + "tip", anatomy.tip[i]);
            }
            BindHandPart(prefix + "thumb", anatomy.thumb);
            BindHandPart(prefix + "thumb_middle", anatomy.thumbMiddle);
            BindHandPart(prefix + "thumb_tip", anatomy.thumbTip);
            return root;
        }

        void BindHandPart(string sourceName, Transform part)
        {
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            handParts.Add(new HandPartBinding {
                sourceName = sourceName, filter = filter, renderer = renderer,
                proceduralMesh = filter.sharedMesh, proceduralMaterials = renderer.sharedMaterials
            });
        }

        // Every grip is described in the moving item's coordinates. The whole
        // hand follows that frame, while the phalanges curve to its actual faces.
        void HandFrame(Transform hand, Transform item)
        {
            hand.position = item.position;
            hand.rotation = item.rotation;
            hand.localScale = Vector3.one;
        }

        void Palm(Transform hand, Vector3 centre, Vector3 angles, Vector3 wrist, Vector3 armEnd)
        {
            var h = hands[hand];
            h.palm.localPosition = centre;
            h.palm.localRotation = Quaternion.Euler(angles);
            h.palm.localScale = new Vector3(.078f, .090f, .035f);
            if (h.attachments != null)
            {
                // Preserve the item grip and palm pose. Only the arm's distal
                // end follows the source palm's real proximal cap, so a tapered
                // imported hand cannot leave the legacy box-palm wrist in air.
                wrist = centre + h.palm.localRotation * Vector3.Scale(h.attachments.palmWrist, h.palm.localScale);
                FitEndSections(h.forearm, h.attachments.armStart, h.attachments.armWrist,
                    armEnd, wrist, bareArms ? .074f : .102f, bareArms ? .069f : .095f);
            }
            else Bone(h.forearm, armEnd, wrist, bareArms ? .074f : .102f, bareArms ? .069f : .095f);
            h.cuff.localPosition = wrist;
            h.cuff.localRotation = h.forearm.localRotation;
            h.cuff.localScale = bareArms ? new Vector3(.076f, .012f, .072f) : new Vector3(.093f, .035f, .087f);
            if (h.attachments != null)
                h.cuff.localPosition -= h.cuff.localRotation * Vector3.Scale(h.attachments.cuffCentre, h.cuff.localScale);
        }

        static void FitEndSections(Transform part, Vector3 capStart, Vector3 capEnd,
            Vector3 start, Vector3 end, float width, float depth)
        {
            Vector3 direction = end - start;
            Vector3 capDirection = capEnd - capStart;
            float radialLengthSquared = Mathf.Pow(capDirection.x * width, 2) + Mathf.Pow(capDirection.z * depth, 2);
            float length = Mathf.Sqrt(Mathf.Max(.000016f, direction.sqrMagnitude - radialLengthSquared)) /
                Mathf.Max(.0001f, Mathf.Abs(capDirection.y));
            Vector3 scale = new Vector3(width, length, depth);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.Scale(capDirection, scale), direction);
            part.localScale = scale;
            part.localRotation = rotation;
            part.localPosition = start - rotation * Vector3.Scale(capStart, scale);
        }

        static void Bone(Transform part, Vector3 start, Vector3 end, float width = .018f, float depth = .019f)
        {
            Vector3 direction = end - start;
            part.localPosition = (start + end) * .5f;
            part.localRotation = direction.sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, direction) : Quaternion.identity;
            part.localScale = new Vector3(width, Mathf.Max(.004f, direction.magnitude + .004f), depth);
        }

        Vector3 FingerBase(Transform hand, int finger)
        {
            var h = hands[hand];
            return h.palm.localPosition + h.palm.localRotation * new Vector3(h.side * (finger - 1.5f) * .018f, .038f, 0);
        }

        void Finger(Transform hand, int finger, Vector3 joint, Vector3 lastJoint, float width = .020f)
        {
            var h = hands[hand];
            Bone(h.proximal[finger], FingerBase(hand, finger), joint, width, width * 1.10f);
            Bone(h.middle[finger], joint, lastJoint, width * .95f, width);
            Bone(h.distal[finger], lastJoint, h.tip[finger].localPosition, width * .85f, width * .90f);
        }

        void Thumb(Transform hand, Vector3 start, Vector3 joint)
        {
            var h = hands[hand];
            Bone(h.thumb, start, joint, .023f, .024f);
            Bone(h.thumbMiddle, joint, h.thumbTip.localPosition, .021f, .022f);
        }

        void Pad(Transform hand, Transform pad, Transform surface, Vector3 point, Vector3 normal, Vector3 tangent, bool thumb = false)
        {
            Vector3 worldNormal = surface.localToWorldMatrix.inverse.transpose.MultiplyVector(normal).normalized;
            Vector3 n = hand.InverseTransformDirection(worldNormal);
            Vector3 t = hand.InverseTransformDirection(surface.TransformDirection(tangent));
            t = (t - n * Vector3.Dot(t, n)).normalized;
            if (t.sqrMagnitude < .01f) t = Vector3.Cross(n, Vector3.right).normalized;
            float radius = thumb ? .011f : .009f;
            pad.localPosition = hand.InverseTransformPoint(surface.TransformPoint(point)) + n * (radius + .0007f);
            pad.localRotation = Quaternion.LookRotation(n, t);
            pad.localScale = thumb ? new Vector3(.022f, .028f, .022f) : new Vector3(.018f, .026f, .018f);
        }

        void TubePad(Transform hand, Transform pad, Transform surface, float along, float degrees, bool thumb = false)
        {
            // Use the middle of an octagonal facet, including the slight taper.
            float a = degrees * Mathf.Deg2Rad;
            float radius = (.465f - .07f * along) * Mathf.Cos(Mathf.PI / 8);
            Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Pad(hand, pad, surface, radial * radius + Vector3.up * along,
                radial + Vector3.up * (.07f * Mathf.Cos(Mathf.PI / 8)), Vector3.up, thumb);
        }

        void RelaxHand(Transform hand, Vector3 position, Vector3 angles)
        {
            PoseHand(hand, position, angles);
            Palm(hand, new Vector3(0, .037f, 0), Vector3.zero, Vector3.zero, new Vector3(0, -.23f, -.08f));
            var h = hands[hand];
            for (int i = 0; i < 4; i++)
            {
                float x = h.side * (i - 1.5f) * .018f;
                h.tip[i].localPosition = new Vector3(x, .106f - i * .003f, -.025f);
                h.tip[i].localRotation = Quaternion.Euler(-80, 0, 0);
                h.tip[i].localScale = new Vector3(.018f, .026f, .018f);
                Finger(hand, i, new Vector3(x, .095f, -.005f), h.tip[i].localPosition + new Vector3(0, -.007f, .010f));
            }
            h.thumbTip.localPosition = new Vector3(-h.side * .040f, .066f, -.024f);
            h.thumbTip.localRotation = Quaternion.Euler(-55, 0, h.side * 20);
            h.thumbTip.localScale = new Vector3(.022f, .028f, .022f);
            Thumb(hand, new Vector3(-h.side * .026f, .014f, 0), new Vector3(-h.side * .048f, .038f, -.006f));
        }

        void GripMap(Transform hand, int side)
        {
            Transform item = items["map"], sheet = item.Find("Folded station plan");
            HandFrame(hand, item);
            Palm(hand, new Vector3(side * .360f, -.173f, .039f), new Vector3(0, side * -12, side * -20),
                new Vector3(side * .383f, -.209f, .026f), new Vector3(side * .440f, -.385f, -.075f));
            var h = hands[hand];
            float thumbX = side * .325f, thumbZ = PaperDepth(thumbX);
            Vector3 front = new Vector3(.05735f, 0, -1).normalized;
            Pad(hand, h.thumbTip, sheet, new Vector3(thumbX, -.170f, thumbZ), front, Vector3.up, true);
            Thumb(hand, new Vector3(side * .365f, -.199f, .028f),
                new Vector3(side * .353f, -.181f, -.007f));
            for (int i = 0; i < 4; i++)
            {
                float x = side * (.326f + i * .002f), y = -.126f - i * .021f;
                Pad(hand, h.tip[i], sheet, new Vector3(x, y, PaperDepth(x)), -front, Vector3.up);
                Finger(hand, i, new Vector3(side * .358f, y + .014f, .041f),
                    h.tip[i].localPosition - Vector3.up * .011f);
            }
        }

        static float PaperDepth(float x)
        {
            float u = Mathf.InverseLerp(-.34f, .34f, x) * 3;
            int panel = Mathf.Clamp(Mathf.FloorToInt(u), 0, 2);
            return Mathf.Lerp(panel % 2 == 0 ? 0 : .013f, panel % 2 == 0 ? .013f : 0, u - panel);
        }

        void GripBox(Transform hand, Transform item, Transform surface, int side)
        {
            HandFrame(hand, item);
            float halfX = surface.localScale.x * .5f, halfZ = surface.localScale.z * .5f;
            Vector3 centre = surface.localPosition;
            float palmY = centre.y - Mathf.Min(.040f, surface.localScale.y * .23f);
            Palm(hand, centre + new Vector3(side * (halfX + .018f), palmY - centre.y, halfZ + .026f),
                new Vector3(0, -side * 12, -side * 22),
                centre + new Vector3(side * (halfX + .041f), palmY - centre.y - .040f, halfZ + .017f),
                centre + new Vector3(side * (halfX + .090f), palmY - centre.y - .230f, -.075f));
            var h = hands[hand];
            float edgeX = side * Mathf.Max(0, .5f - .012f / surface.localScale.x);
            float thumbY = Mathf.Clamp((palmY - centre.y) / surface.localScale.y, -.35f, .2f);
            Pad(hand, h.thumbTip, surface, new Vector3(edgeX, thumbY, -.5f), Vector3.back, Vector3.up, true);
            Thumb(hand, centre + new Vector3(side * (halfX + .034f), palmY - centre.y - .026f, halfZ + .012f),
                centre + new Vector3(side * (halfX + .018f), palmY - centre.y, -halfZ - .013f));
            for (int i = 0; i < 4; i++)
            {
                float y = Mathf.Clamp(.22f - i * .020f / surface.localScale.y, -.40f, .38f);
                Pad(hand, h.tip[i], surface, new Vector3(edgeX, y, .5f), Vector3.forward, Vector3.up);
                Vector3 p = surface.TransformPoint(new Vector3(side * .5f, y, .5f));
                Vector3 joint = hand.InverseTransformPoint(p) + new Vector3(side * .023f, .010f, .018f);
                Finger(hand, i, joint, h.tip[i].localPosition - Vector3.up * .011f);
            }
        }

        void GripVerticalHandle(Transform hand, Transform item, Transform surface)
        {
            HandFrame(hand, item);
            Vector3 centre = surface.localPosition;
            float halfX = surface.localScale.x * .5f;
            Palm(hand, centre + new Vector3(halfX + .028f, 0, .013f), new Vector3(0, 90, -90),
                centre + new Vector3(halfX + .049f, -.036f, .002f),
                centre + new Vector3(halfX + .073f, -.232f, -.075f));
            var h = hands[hand];
            for (int i = 0; i < 4; i++)
            {
                TubePad(hand, h.tip[i], surface, .31f - i * .205f, 202.5f);
                float y = h.tip[i].localPosition.y;
                Finger(hand, i, centre + new Vector3(halfX + .010f, y - centre.y, -.034f),
                    centre + new Vector3(-halfX * .45f, y - centre.y, -.034f), .024f);
            }
            TubePad(hand, h.thumbTip, surface, -.20f, 337.5f, true);
            Thumb(hand, centre + new Vector3(halfX + .026f, -.045f, .002f),
                centre + new Vector3(halfX + .032f, -.014f, -.035f));
        }

        void GripFlashlight()
        {
            Transform item = items["flashlight"], surface = item.Find("Ribbed rubber grip");
            HandFrame(rightHand, item);
            Palm(rightHand, new Vector3(.015f, -.055f, -.020f), new Vector3(90, 0, 90),
                new Vector3(.035f, -.073f, -.060f), new Vector3(.095f, -.270f, -.125f));
            var h = hands[rightHand];
            for (int i = 0; i < 4; i++)
            {
                TubePad(rightHand, h.tip[i], surface, -.18f + i * .12f, 202.5f);
                float z = h.tip[i].localPosition.z;
                Finger(rightHand, i, new Vector3(-.047f, -.027f, z), new Vector3(-.049f, -.005f, z), .024f);
            }
            TubePad(rightHand, h.thumbTip, surface, -.06f, 337.5f, true);
            Thumb(rightHand, new Vector3(.051f, -.047f, -.045f), new Vector3(.052f, -.002f, -.034f));
        }

        void GripCompass()
        {
            Transform item = items["dispatcher"], surface = item.Find("Brass compass case");
            HandFrame(rightHand, item);
            Palm(rightHand, new Vector3(.064f, -.037f, .044f), new Vector3(0, -10, -25),
                new Vector3(.082f, -.077f, .033f), new Vector3(.124f, -.258f, -.067f));
            var h = hands[rightHand];
            Pad(rightHand, h.thumbTip, surface, new Vector3(.31f, .5f, -.18f), Vector3.up, Vector3.forward, true);
            Thumb(rightHand, new Vector3(.082f, -.060f, .030f), new Vector3(.074f, -.020f, .033f));
            for (int i = 0; i < 4; i++)
            {
                float x = .31f - i * .035f, z = -.12f + i * .105f;
                Pad(rightHand, h.tip[i], surface, new Vector3(x, -.5f, z), Vector3.down, Vector3.forward);
                Vector3 point = h.tip[i].localPosition;
                Finger(rightHand, i, new Vector3(.075f, point.y + .012f, .025f),
                    new Vector3(.066f, point.y, -.025f));
            }
        }

        void GripCamera()
        {
            Transform item = items["camera"], shell = item.Find("VHS-C shell");
            HandFrame(rightHand, item);
            Palm(rightHand, new Vector3(.142f, -.032f, -.060f), new Vector3(0, 90, 0),
                new Vector3(.155f, -.075f, -.080f), new Vector3(.215f, -.268f, -.150f));
            var h = hands[rightHand];
            for (int i = 0; i < 4; i++)
            {
                Pad(rightHand, h.tip[i], shell, new Vector3(.5f, .23f, -.04f - i * .10f), Vector3.right, Vector3.up);
                Vector3 p = h.tip[i].localPosition;
                Finger(rightHand, i, new Vector3(.152f, .035f, p.z), p - Vector3.up * .011f, .023f);
            }
            Pad(rightHand, h.thumbTip, shell, new Vector3(.36f, -.30f, -.5f), Vector3.back, Vector3.up, true);
            Thumb(rightHand, new Vector3(.150f, -.067f, -.100f), new Vector3(.131f, -.049f, -.148f));
            HandFrame(leftHand, item);
            Palm(leftHand, new Vector3(-.028f, -.097f, -.095f), new Vector3(90, 0, 0),
                new Vector3(-.039f, -.126f, -.128f), new Vector3(-.123f, -.270f, -.214f));
            var support = hands[leftHand];
            for (int i = 0; i < 4; i++)
            {
                Pad(leftHand, support.tip[i], shell, new Vector3(-.32f + i * .20f, -.5f, -.06f), Vector3.down, Vector3.forward);
                Vector3 p = support.tip[i].localPosition;
                Finger(leftHand, i, p + new Vector3(0, -.012f, -.029f), p - Vector3.forward * .011f, .022f);
            }
            Pad(leftHand, support.thumbTip, shell, new Vector3(-.37f, -.37f, -.5f), Vector3.back, Vector3.up, true);
            Thumb(leftHand, new Vector3(-.070f, -.111f, -.105f), new Vector3(-.089f, -.080f, -.145f));
        }

        void GripFuelCan()
        {
            Transform item = items["fuel-canister"], handle = item.Find("Handle top");
            HandFrame(rightHand, item);
            Palm(rightHand, new Vector3(.013f, .152f, .012f), new Vector3(60, 0, -10),
                new Vector3(.064f, .143f, -.017f), new Vector3(.161f, -.350f, -.119f));
            var h = hands[rightHand];
            for (int i = 0; i < 4; i++)
            {
                Pad(rightHand, h.tip[i], handle, new Vector3(-.34f + i * .22f, -.15f, .5f), Vector3.forward, Vector3.up);
                Vector3 p = h.tip[i].localPosition;
                Finger(rightHand, i, new Vector3(p.x, .150f, .071f), p + Vector3.up * .011f, .023f);
            }
            Pad(rightHand, h.thumbTip, handle, new Vector3(.28f, .5f, -.12f), Vector3.up, Vector3.right, true);
            Thumb(rightHand, new Vector3(.058f, .153f, -.007f), new Vector3(.056f, .127f, .017f));
        }

        void GripCigarette()
        {
            Transform filter = cigarette.Find("Ochre filter");
            HandFrame(rightHand, cigarette);
            Palm(rightHand, new Vector3(.015f, -.083f, .033f), new Vector3(-20, -5, 10),
                new Vector3(.035f, -.124f, .023f), new Vector3(.108f, -.282f, -.077f));
            var h = hands[rightHand];
            // Only the index and middle fingers pinch the filter. The ring and
            // little fingers fold into the palm instead of forming a large fist.
            TubePad(rightHand, h.tip[0], filter, -.10f, 202.5f);
            TubePad(rightHand, h.tip[1], filter, .10f, 22.5f);
            Finger(rightHand, 0, new Vector3(-.017f, -.031f, -.012f), h.tip[0].localPosition - Vector3.up * .010f);
            Finger(rightHand, 1, new Vector3(.016f, -.030f, -.006f), h.tip[1].localPosition - Vector3.up * .010f);
            for (int i = 2; i < 4; i++)
            {
                h.tip[i].localPosition = new Vector3(.020f + (i - 2) * .018f, -.077f, .005f);
                h.tip[i].localRotation = Quaternion.Euler(-105, 0, 12);
                h.tip[i].localScale = new Vector3(.018f, .026f, .018f);
                Finger(rightHand, i, new Vector3(.025f + (i - 2) * .020f, -.042f, .023f),
                    h.tip[i].localPosition + Vector3.up * .014f);
            }
            h.thumbTip.localPosition = new Vector3(-.015f, -.079f, -.002f);
            h.thumbTip.localRotation = Quaternion.Euler(-60, 0, -25);
            h.thumbTip.localScale = new Vector3(.022f, .028f, .022f);
            Thumb(rightHand, new Vector3(-.021f, -.102f, .030f), new Vector3(-.031f, -.089f, .002f));
        }

        void CreateFlashlight()
        {
            var g = Item("flashlight", new Vector3(.25f, -.18f, .49f), new Vector3(-4, -8, -8));
            Part("Ribbed rubber grip", g, tubeMesh, dark, Vector3.zero, new Vector3(.073f, .18f, .073f), new Vector3(90, 0, 0));
            for (int i = 0; i < 5; i++) Part("Grip rib", g, tubeMesh, metal, new Vector3(0, 0, i * .022f - .07f), new Vector3(.077f, .007f, .077f), new Vector3(90, 0, 0));
            Part("Octagonal lamp head", g, tubeMesh, dark, new Vector3(0, 0, .115f), new Vector3(.118f, .083f, .118f), new Vector3(90, 0, 0));
            Part("Reflector rim", g, tubeMesh, metal, new Vector3(0, 0, .16f), new Vector3(.111f, .012f, .111f), new Vector3(90, 0, 0));
            Part("Switch", g, boxMesh, red, new Vector3(0, .037f, .035f), new Vector3(.020f, .009f, .031f));
            Part("Glass lens", g, tubeMesh, lens, new Vector3(0, 0, .168f), new Vector3(.090f, .004f, .090f), new Vector3(90, 0, 0));
            Part("Wrist loop", g, tubeMesh, dark, new Vector3(.017f, -.075f, -.078f), new Vector3(.018f, .15f, .012f), new Vector3(0, 0, -16));
        }

        void CreateCamera()
        {
            var g = Item("camera", new Vector3(.21f, -.16f, .58f), new Vector3(-3, -8, 0));
            Part("VHS-C shell", g, boxMesh, dark, Vector3.zero, new Vector3(.22f, .155f, .28f));
            Part("Tape door", g, boxMesh, metal, new Vector3(-.114f, 0, -.003f), new Vector3(.01f, .12f, .17f));
            Part("Viewfinder rubber", g, boxMesh, dark, new Vector3(.05f, .082f, -.12f), new Vector3(.09f, .07f, .12f));
            Part("Viewfinder glass", g, boxMesh, teal, new Vector3(.05f, .084f, -.182f), new Vector3(.068f, .043f, .004f));
            Part("Lens focus ring", g, tubeMesh, dark, new Vector3(0, 0, .173f), new Vector3(.142f, .07f, .142f), new Vector3(90, 0, 0));
            Part("Lens rim", g, tubeMesh, metal, new Vector3(0, 0, .214f), new Vector3(.130f, .018f, .130f), new Vector3(90, 0, 0));
            Part("Lens glass", g, tubeMesh, teal, new Vector3(0, 0, .225f), new Vector3(.105f, .004f, .105f), new Vector3(90, 0, 0));
            Part("Video light mounting bracket", g, boxMesh, metal, new Vector3(-.065f, .085f, .090f), new Vector3(.045f, .024f, .080f));
            Part("Mounted video light housing", g, boxMesh, dark, new Vector3(-.065f, .120f, .100f), new Vector3(.078f, .055f, .072f));
            Part("Mounted video light reflector", g, tubeMesh, metal, new Vector3(-.065f, .120f, .137f), new Vector3(.065f, .009f, .065f), new Vector3(90, 0, 0));
            Part("Mounted video light glass", g, tubeMesh, lens, new Vector3(-.065f, .120f, .143f), new Vector3(.053f, .003f, .053f), new Vector3(90, 0, 0));
            Label(g, "LIGHT", new Vector3(-.065f, .120f, .063f), .059f, .018f, dark, new Color32(214, 218, 180, 255));
            Part("Hand strap", g, boxMesh, dark, new Vector3(.164f, -.029f, -.02f), new Vector3(.010f, .067f, .15f));
            Part("REC indicator", g, boxMesh, red, new Vector3(-.073f, .044f, -.143f), new Vector3(.014f, .014f, .005f));
            Label(g, "VHS-C  /  REC", new Vector3(0, -.035f, -.143f), .16f, .035f, dark, new Color32(214, 218, 180, 255));
        }

        void CreateInventoryItems()
        {
            var g = Item("keycard", new Vector3(.25f, -.135f, .465f), new Vector3(-5, -18, -10));
            Part("ID card", g, boxMesh, paper, Vector3.zero, new Vector3(.11f, .16f, .006f));
            Part("Blue access stripe", g, boxMesh, teal, new Vector3(0, .052f, -.004f), new Vector3(.106f, .028f, .002f));
            Label(g, "B9\nACCESS", new Vector3(0, .012f, -.005f), .095f, .056f, paper, new Color32(44, 65, 62, 255));
            for (int i = 0; i < 9; i++) Part("Barcode", g, boxMesh, dark, new Vector3(-.038f + i * .009f, -.050f, -.004f), new Vector3(i % 2 == 0 ? .004f : .002f, .024f, .002f));
            g = Item("tape", new Vector3(.25f, -.15f, .48f), new Vector3(-10, -18, -10));
            Part("Tape casing", g, boxMesh, dark, Vector3.zero, new Vector3(.155f, .105f, .034f));
            Part("Tape label", g, boxMesh, paper, new Vector3(0, .027f, -.018f), new Vector3(.135f, .033f, .002f));
            Label(g, "NIGHT SHIFT", new Vector3(0, .028f, -.020f), .126f, .022f, paper, new Color32(59, 57, 42, 255));
            for (int i = -1; i <= 1; i += 2) Part("Tape spool", g, tubeMesh, metal, new Vector3(i * .043f, -.011f, -.019f), new Vector3(.036f, .003f, .036f), new Vector3(90, 0, 0));
            g = Item("fuse", new Vector3(.25f, -.15f, .47f), new Vector3(-8, 0, -20));
            Part("Ceramic fuse", g, tubeMesh, paper, Vector3.zero, new Vector3(.055f, .13f, .055f));
            for (int i = -1; i <= 1; i += 2) Part("Fuse terminal", g, tubeMesh, brass, new Vector3(0, i * .07f, 0), new Vector3(.058f, .026f, .058f));
            Label(g, "380V", new Vector3(0, 0, -.029f), .046f, .028f, paper, new Color32(43, 51, 39, 255));
            g = Item("fuel-canister", new Vector3(.28f, -.19f, .54f), new Vector3(-5, -13, -8));
            Part("Steel fuel can", g, boxMesh, olive, new Vector3(0, -.07f, .04f), new Vector3(.22f, .28f, .15f));
            Part("Handle top", g, boxMesh, metal, new Vector3(0, .10f, .04f), new Vector3(.12f, .025f, .037f));
            for (int i = -1; i <= 1; i += 2) Part("Handle support", g, boxMesh, metal, new Vector3(i * .055f, .073f, .04f), new Vector3(.023f, .055f, .037f));
            Part("Screw cap", g, tubeMesh, brass, new Vector3(.075f, .08f, .04f), new Vector3(.055f, .045f, .055f));
            Label(g, "FUEL\nBUREAU 9", new Vector3(0, -.067f, -.037f), .15f, .12f, olive, new Color32(224, 197, 125, 255));
        }

        void CreateCigarettes()
        {
            var g = Item("operator", new Vector3(-.225f, -.195f, .46f), new Vector3(-12, -12, 12));
            Part("Burgundy cigarette pack", g, boxMesh, burgundy, Vector3.zero, new Vector3(.082f, .11f, .039f));
            Part("Ivory pack band", g, boxMesh, paper, new Vector3(0, .023f, -.021f), new Vector3(.077f, .056f, .002f));
            Label(g, "NOCTURNE\n20", new Vector3(0, .020f, -.023f), .071f, .04f, paper, new Color32(91, 29, 39, 255));
            Part("Silver foil", g, boxMesh, metal, new Vector3(0, .055f, 0), new Vector3(.073f, .005f, .032f));
            packLid = Group("Hinged lid", g);
            packLid.localPosition = new Vector3(0, .053f, .018f);
            Part("Pack lid", packLid, boxMesh, burgundy, new Vector3(0, .018f, -.018f), new Vector3(.083f, .037f, .040f));
            for (int i = 0; i < 12; i++) packCigarettes.Add(Part("Cigarette in pack", g, tubeMesh, paper, new Vector3(-.028f + (i % 6) * .011f, .064f, -.009f + (i / 6) * .016f), new Vector3(.0075f, .018f, .0075f)));
            cigarette = Group("Cigarette held between fingers", rig);
            Part("Cigarette paper", cigarette, tubeMesh, paper, new Vector3(0, .028f, 0), new Vector3(.0065f, .070f, .0065f));
            Part("Ochre filter", cigarette, tubeMesh, brass, new Vector3(0, -.017f, 0), new Vector3(.0067f, .022f, .0067f));
            ember = Part("Glowing ember", cigarette, tubeMesh, red, new Vector3(0, .064f, 0), new Vector3(.007f, .004f, .007f));
            lighter = Group("Brass flip lighter", rig);
            lighter.localPosition = new Vector3(.033f, -.16f, .435f);
            Part("Lighter body", lighter, boxMesh, brass, Vector3.zero, new Vector3(.032f, .045f, .018f));
            Part("Spark wheel", lighter, tubeMesh, metal, new Vector3(.008f, .028f, 0), new Vector3(.012f, .012f, .012f), new Vector3(90, 0, 0));
            Part("Open lid", lighter, boxMesh, brass, new Vector3(-.026f, .022f, 0), new Vector3(.027f, .027f, .019f), new Vector3(0, 0, -100));
            flame = Part("Small flame", lighter, tubeMesh, flameMaterial, new Vector3(-.005f, .043f, 0), new Vector3(.009f, .026f, .007f));
            var flameLight = flame.gameObject.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.color = new Color32(255, 165, 75, 255);
            flameLight.intensity = .38f;
            flameLight.range = 1.25f;
            flameLight.renderMode = LightRenderMode.ForcePixel;
            flameLight.shadows = LightShadows.None;
            flameLight.cullingMask = ~(1 << 29);
            var smokeMesh = Own(CreateQuad(1, 1));
            for (int i = 0; i < 9; i++) smoke.Add(Part("Soft smoke wisp", rig, smokeMesh, smokeMaterial, Vector3.zero, Vector3.one * .01f));
        }

        Texture2D CreateSmokeTexture()
        {
            const int size = 32;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float radius = new Vector2((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1).magnitude;
                float alpha = Mathf.Pow(Mathf.Clamp01(1 - radius), 1.6f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255));
            }
            var texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, false));
            texture.name = "Soft cigarette smoke";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void CreateWrench()
        {
            var g = Item("engineer", new Vector3(.25f, -.10f, .48f), new Vector3(-10, -20, -17));
            Part("Heavy wrench shaft", g, boxMesh, metal, new Vector3(0, .008f, 0), new Vector3(.037f, .285f, .028f));
            Part("Wrapped handle", g, tubeMesh, dark, new Vector3(0, -.075f, 0), new Vector3(.047f, .14f, .040f));
            Part("Wrench jaw base", g, boxMesh, metal, new Vector3(0, .173f, 0), new Vector3(.095f, .070f, .04f));
            Part("Fixed jaw", g, boxMesh, metal, new Vector3(-.043f, .221f, 0), new Vector3(.033f, .077f, .04f), new Vector3(0, 0, -15));
            Part("Sliding jaw", g, boxMesh, metal, new Vector3(.039f, .208f, 0), new Vector3(.037f, .064f, .04f), new Vector3(0, 0, 15));
            Part("Adjustment wheel", g, tubeMesh, dark, new Vector3(0, .164f, -.025f), new Vector3(.032f, .040f, .032f), new Vector3(0, 0, 90));
            Label(g, "B9", new Vector3(0, .045f, -.016f), .030f, .035f, metal, new Color32(52, 58, 52, 255));
        }

        void CreateRecorder()
        {
            var g = Item("journalist", new Vector3(.25f, -.17f, .48f), new Vector3(-8, -12, -10));
            Part("Pocket recorder shell", g, boxMesh, dark, Vector3.zero, new Vector3(.091f, .178f, .045f));
            Part("Tape window", g, boxMesh, teal, new Vector3(0, .019f, -.025f), new Vector3(.071f, .058f, .003f));
            for (int i = 0; i < 5; i++) Part("Microphone grille", g, boxMesh, metal, new Vector3(0, .070f - i * .006f, -.025f), new Vector3(.065f, .002f, .003f));
            for (int i = 0; i < 3; i++) Part("Transport button", g, boxMesh, i == 0 ? red : metal, new Vector3(-.026f + i * .026f, -.054f, -.025f), new Vector3(.018f, .024f, .008f));
            Label(g, "VOICE\nMEMO", new Vector3(0, .016f, -.027f), .063f, .045f, teal, new Color32(29, 49, 38, 255));
        }

        void CreateCompass()
        {
            var g = Item("dispatcher", new Vector3(.25f, -.11f, .47f), new Vector3(-12, -10, -8));
            Part("Brass compass case", g, tubeMesh, brass, Vector3.zero, new Vector3(.125f, .026f, .125f), new Vector3(90, 0, 0));
            Part("Ivory compass face", g, tubeMesh, paper, new Vector3(0, 0, -.016f), new Vector3(.105f, .004f, .105f), new Vector3(90, 0, 0));
            Label(g, "N\n+\nS", new Vector3(0, 0, -.019f), .030f, .094f, paper, new Color32(39, 60, 50, 255));
            compassNeedle = Group("North needle", g);
            compassNeedle.localPosition = new Vector3(0, 0, -.022f);
            Part("Red north pointer", compassNeedle, palmMesh, red, new Vector3(0, .020f, 0), new Vector3(.014f, .043f, .003f));
            Part("South pointer", compassNeedle, palmMesh, metal, new Vector3(0, -.020f, 0), new Vector3(.014f, .043f, .003f));
            pendulum = Group("Swinging chain", g);
            pendulum.localPosition = new Vector3(0, -.055f, 0);
            Part("Fine brass chain", pendulum, tubeMesh, brass, new Vector3(0, -.075f, 0), new Vector3(.003f, .15f, .003f));
            Part("Pendulum weight", pendulum, palmMesh, brass, new Vector3(0, -.158f, 0), new Vector3(.030f, .046f, .023f));
        }

        void CreateMap()
        {
            var g = Item("map", new Vector3(0, -.105f, .68f), new Vector3(10, 0, 0));
            var mapMaterial = MakeMaterial(Shader.Find("Dead Air/Source Surface"), "Station floor plan", Color.white);
            mapMaterial.mainTexture = DrawMap();
            var mesh = Own(CreateFoldedPaper());
            Part("Folded station plan", g, mesh, mapMaterial, Vector3.zero, Vector3.one);
            for (int i = -1; i <= 1; i += 2) Part("Paper crease", g, boxMesh, paper, new Vector3(i * .113f, 0, -.001f), new Vector3(.002f, .50f, .001f));
            mapMarker = Part("Current position", g, tubeMesh, red, Vector3.zero, new Vector3(.011f, .002f, .011f), new Vector3(90, 0, 0));
        }

        Texture2D DrawMap()
        {
            const int width = 768, height = 576;
            var pixels = new Color32[width * height];
            Color32 baseColor = new Color32(213, 205, 163, 255), ink = new Color32(39, 64, 57, 255), grid = new Color32(186, 183, 145, 255);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = x % 32 == 0 || y % 32 == 0 ? grid : baseColor;
            mapMinX = world?.courtyard?.minX ?? -30; mapMaxX = world?.courtyard?.maxX ?? 30;
            mapMinZ = world?.courtyard?.minZ ?? -76; mapMaxZ = world?.courtyard?.maxZ ?? 38;
            const int bottom = 52, top = 524;
            Func<float, int> mz = z => Mathf.RoundToInt(Mathf.Lerp(bottom, top, Mathf.InverseLerp(mapMinZ, mapMaxZ, z)));
            DrawText(pixels, width, height, "BLACKPINE / OFFICE PLAN", 38, 545, 3, ink);
            Fill(pixels,width,height,382,45,3,484,ink);
            for(int floor=0;floor<2;floor++)
            {
                int left=floor==0?42:403,right=floor==0?365:726;
                Func<float,int> mx=x=>Mathf.RoundToInt(Mathf.Lerp(left,right,Mathf.InverseLerp(mapMinX,mapMaxX,x)));
                DrawText(pixels,width,height,floor==0?"FLOOR 1 + YARDS":"FLOOR 2",left,523,2,ink);
                if (world != null && world.rooms != null)
                for (int i = 0; i < world.rooms.Length; i++)
                {
                    var room = world.rooms[i];
                    if((room.floorY>2.1f?1:0)!=floor)continue;
                    int x0 = mx(room.minX), x1 = mx(room.maxX), y0 = mz(room.minZ), y1 = mz(room.maxZ);
                    DrawRect(pixels, width, height, x0, y0, x1, y1, ink, 2);
                    string label=string.IsNullOrEmpty(room.mapLabel)?"R"+(i+1):room.mapLabel;
                    if(label.Length*6>x1-x0-4)label="R"+(i+1);
                    if(!DeadAirOfficeLayout.IsCirculation(room))
                        DrawText(pixels, width, height, label, (x0 + x1 - label.Length * 6) / 2, (y0 + y1 - 7) / 2, 1, ink);
                }
                // Connected halls share open edges. Do not draw imaginary walls
                // across the new side passages and cross corridors on the map.
                if(world?.rooms!=null)
                for(int a=0;a<world.rooms.Length;a++)for(int b=a+1;b<world.rooms.Length;b++)
                {
                    var one=world.rooms[a];var two=world.rooms[b];
                    if(!DeadAirOfficeLayout.IsCirculation(one)||!DeadAirOfficeLayout.IsCirculation(two)||
                        (one.floorY>2.1f?1:0)!=floor||Mathf.Abs(one.floorY-two.floorY)>.1f)continue;
                    float z0=Mathf.Max(one.minZ,two.minZ),z1=Mathf.Min(one.maxZ,two.maxZ);
                    if(z1>z0 && (Mathf.Abs(one.maxX-two.minX)<.05f||Mathf.Abs(two.maxX-one.minX)<.05f))
                    {
                        float x=Mathf.Abs(one.maxX-two.minX)<.05f?one.maxX:two.maxX;
                        Fill(pixels,width,height,mx(x)-2,mz(z0)+2,4,Mathf.Max(0,mz(z1)-mz(z0)-4),baseColor);
                    }
                    float x0=Mathf.Max(one.minX,two.minX),x1=Mathf.Min(one.maxX,two.maxX);
                    if(x1>x0 && (Mathf.Abs(one.maxZ-two.minZ)<.05f||Mathf.Abs(two.maxZ-one.minZ)<.05f))
                    {
                        float z=Mathf.Abs(one.maxZ-two.minZ)<.05f?one.maxZ:two.maxZ;
                        Fill(pixels,width,height,mx(x0)+2,mz(z)-2,Mathf.Max(0,mx(x1)-mx(x0)-4),4,baseColor);
                    }
                }
                if (world != null && world.doors != null)
                foreach (var door in world.doors)
                    if((door.y>4?1:0)==floor)Fill(pixels, width, height, mx(door.x) - 4, mz(door.z) - 3, 8, 6, new Color32(186, 133, 58, 255));
                if (world != null && world.stairs != null)
                foreach(var stair in world.stairs)
                {
                    int x=mx((stair.minX+stair.maxX)*.5f),y=mz((stair.minZ+stair.maxZ)*.5f);
                    DrawText(pixels,width,height,"STAIRS",x-18,y,1,ink);
                }
                if (floor==0 && world != null && world.extraction != null)
                {
                    int x = mx(world.extraction.x), y = mz(world.extraction.z);
                    DrawRect(pixels, width, height, x - 7, y - 6, x + 7, y + 6, ink, 2);
                    DrawText(pixels, width, height, "EXIT", x - 12, y + 10, 1, ink);
                }
            }
            DrawText(pixels, width, height, "RED: YOU   GOLD: DOORS   N ^", 38, 20, 2, ink);
            var texture = Own(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.name = "Station plan generated from WorldData";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        void Label(Transform parent, string text, Vector3 position, float width, float height, Material background, Color32 ink)
        {
            int tw = 256, th = text.Contains("\n") ? 96 : 48;
            var pixels = new Color32[tw * th];
            var fill = (Color32)background.color;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int s = Mathf.Min(4, Mathf.Max(1, (tw - 12) / Mathf.Max(1, lines[i].Length * 6)));
                DrawText(pixels, tw, th, lines[i], (tw - lines[i].Length * 6 * s) / 2, th - (i + 1) * th / lines.Length + (th / lines.Length - 7 * s) / 2, s, ink);
            }
            var texture = Own(new Texture2D(tw, th, TextureFormat.RGBA32, false));
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels); texture.Apply(false, true);
            var material = MakeMaterial(Shader.Find("Dead Air/Source Surface"), text, Color.white);
            material.mainTexture = texture;
            Part(text + " printed label", parent, Own(CreateQuad(width, height)), material, position, Vector3.one);
        }

        Transform Item(string id, Vector3 position, Vector3 angles)
        {
            var g = Group(id, rig); g.localPosition = position; g.localRotation = Quaternion.Euler(angles);
            items.Add(id, g); g.gameObject.SetActive(false); return g;
        }

        Transform Group(string name, Transform parent)
        {
            var g = new GameObject(name); g.layer = EquipmentLayer; g.transform.SetParent(parent, false); return g.transform;
        }

        Transform Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 size, Vector3 angles = default)
        {
            var t = Group(name, parent); t.localPosition = position; t.localScale = size; t.localRotation = Quaternion.Euler(angles);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return t;
        }

        Material MakeMaterial(Shader shader, string name, Color color)
        {
            var material = Own(new Material(shader)); material.name = "First person / " + name;
            material.enableInstancing = true;
            SetTint(material, color);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .08f);
            if (material.HasProperty("_FogEnabled")) material.SetFloat("_FogEnabled", 0);
            return material;
        }

        static void SetTint(Material material, Color color)
        {
            material.color = color;
            // Skin, cloth, metal, labels and paper reflect nearby light. Only
            // actual displays, flame and lamp glass use explicit unlit materials.
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
        }

        T Own<T>(T asset) where T : UnityEngine.Object { owned.Add(asset); return asset; }

        static Mesh CreateBox()
        {
            var v = new[] { new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f) };
            return FlatMesh("Faceted box", v, new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5});
        }

        static Mesh CreateTube(int sides)
        {
            var v = new List<Vector3>(); var tris = new List<int>();
            for (int ring = 0; ring < 2; ring++) for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides; float r = ring == 0 ? .50f : .43f;
                v.Add(new Vector3(Mathf.Cos(a) * r, ring - .5f, Mathf.Sin(a) * r));
            }
            v.Add(new Vector3(0, -.5f, 0)); v.Add(new Vector3(0, .5f, 0));
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                tris.AddRange(new[] {i, sides+i, sides+j, i, sides+j, j, sides*2, i, j, sides*2+1, sides+j, sides+i});
            }
            return FlatMesh("Eight sided tapered prism", v.ToArray(), tris.ToArray());
        }

        static Mesh CreatePalm()
        {
            var outline = new[] { new Vector2(-.35f,-.5f),new Vector2(.32f,-.5f),new Vector2(.5f,.24f),new Vector2(.26f,.5f),new Vector2(-.38f,.46f),new Vector2(-.50f,.02f) };
            var v = new List<Vector3>(); var tris = new List<int>();
            for (int ring = 0; ring < 2; ring++) foreach (var p in outline) v.Add(new Vector3(p.x,p.y,ring == 0 ? -.5f : .5f));
            for (int i = 1; i < 5; i++) { tris.AddRange(new[]{0,i+1,i,6,6+i,7+i}); }
            for (int i = 0; i < 6; i++) { int j=(i+1)%6; tris.AddRange(new[]{i,j,j+6,i,j+6,i+6}); }
            return FlatMesh("Angular palm wedge", v.ToArray(), tris.ToArray());
        }

        static Mesh FlatMesh(string name, Vector3[] source, int[] triangles)
        {
            var vertices = new Vector3[triangles.Length]; var indices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++) { vertices[i] = source[triangles[i]]; indices[i] = i; }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = indices };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh CreateQuad(float width, float height)
        {
            var mesh = new Mesh { name = "Printed face" };
            mesh.vertices = new[] { new Vector3(-width*.5f,-height*.5f,0),new Vector3(width*.5f,-height*.5f,0),new Vector3(width*.5f,height*.5f,0),new Vector3(-width*.5f,height*.5f,0) };
            mesh.uv = new[] {Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; mesh.triangles = new[] {0,2,1,0,3,2};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh CreateFoldedPaper()
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                float x = Mathf.Lerp(-.34f,.34f,i/3f), z = i % 2 == 0 ? 0 : .013f;
                v.Add(new Vector3(x,-.25f,z)); v.Add(new Vector3(x,.25f,z));
                uv.Add(new Vector2(i/3f,0)); uv.Add(new Vector2(i/3f,1));
                if(i<3) {int k=i*2;tr.AddRange(new[]{k,k+1,k+3,k,k+3,k+2});}
            }
            var mesh = new Mesh { name="Three panel folded paper",vertices=v.ToArray(),uv=uv.ToArray(),triangles=tr.ToArray() };
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        static void Fill(Color32[] pixels, int w, int h, int x, int y, int width, int height, Color32 color)
        {
            for (int py=Mathf.Max(0,y);py<Mathf.Min(h,y+height);py++) for(int px=Mathf.Max(0,x);px<Mathf.Min(w,x+width);px++) pixels[py*w+px]=color;
        }
        static void DrawRect(Color32[] p,int w,int h,int x0,int y0,int x1,int y1,Color32 c,int thickness)
        {
            Fill(p,w,h,x0,y0,x1-x0,thickness,c);Fill(p,w,h,x0,y1-thickness,x1-x0,thickness,c);
            Fill(p,w,h,x0,y0,thickness,y1-y0,c);Fill(p,w,h,x1-thickness,y0,thickness,y1-y0,c);
        }
        // Tiny raster lettering keeps the physical map and labels crisp at retro resolutions.
        static readonly Dictionary<char,string> Glyphs = new Dictionary<char,string>
        {
            {'A',"01110100011000111111100011000110001"},{'B',"11110100011000111110100011000111110"},{'C',"01111100001000010000100001000001111"},
            {'D',"11110100011000110001100011000111110"},{'E',"11111100001000011110100001000011111"},{'F',"11111100001000011110100001000010000"},
            {'G',"01111100001000010111100011000101111"},{'H',"10001100011000111111100011000110001"},{'I',"11111001000010000100001000010011111"},
            {'J',"00111000100001000010000101001001100"},{'K',"10001100101010011000101001001010001"},{'L',"10000100001000010000100001000011111"},
            {'M',"10001110111010110101100011000110001"},{'N',"10001110011010110011100011000110001"},{'O',"01110100011000110001100011000101110"},
            {'P',"11110100011000111110100001000010000"},{'Q',"01110100011000110001101011001001101"},{'R',"11110100011000111110101001001010001"},
            {'S',"01111100001000001110000010000111110"},{'T',"11111001000010000100001000010000100"},{'U',"10001100011000110001100011000101110"},
            {'V',"10001100011000110001100010101000100"},{'W',"10001100011000110101101011101110001"},{'X',"10001100010101000100010101000110001"},
            {'Y',"10001100010101000100001000010000100"},{'Z',"11111000010001000100010001000011111"},{'0',"01110100011001110101110011000101110"},
            {'1',"00100011000010000100001000010001110"},{'2',"01110100010000100010001000100011111"},{'3',"11110000010000101110000010000111110"},
            {'4',"00010001100101010010111110001000010"},{'5',"11111100001000011110000010000111110"},{'6',"01110100001000011110100011000101110"},
            {'7',"11111000010001000100010000100001000"},{'8',"01110100011000101110100011000101110"},{'9',"01110100011000101111000010000101110"},
            {'/',"00001000010001000100010001000010000"},{'-',"00000000000000011111000000000000000"},{'+',"00000001000010011111001000010000000"},
            {':',"00000001000010000000001000010000000"},{'^',"00100010101000100000000000000000000"}
        };
        static void DrawText(Color32[] pixels,int w,int h,string value,int x,int y,int scale,Color32 color)
        {
            foreach(char letter in value)
            {
                if(Glyphs.TryGetValue(char.ToUpperInvariant(letter),out var glyph))
                    for(int row=0;row<7;row++)for(int col=0;col<5;col++)if(glyph[row*5+col]=='1')Fill(pixels,w,h,x+col*scale,y+(6-row)*scale,scale,scale,color);
                x+=6*scale;
            }
        }

        void OnDestroy()
        {
            if(sourceCamera != null && hadEquipmentLayer) sourceCamera.cullingMask |= 1 << EquipmentLayer;
            if(equipmentCamera != null) Release(equipmentCamera.gameObject);
            if(rig != null) Release(rig.gameObject);
            foreach(var resource in owned) Release(resource);
            owned.Clear();
        }

        static void Release(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
