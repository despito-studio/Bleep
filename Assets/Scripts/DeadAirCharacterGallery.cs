using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>Inspects the actual shipped prefabs and their imported clips without starting a match.</summary>
    [DefaultExecutionOrder(2000)]
    public sealed class DeadAirCharacterGallery : MonoBehaviour
    {
        const int StageLayer = 26;
        static readonly string[] Roles = { "operator", "engineer", "journalist", "dispatcher" };
        static readonly string[] Names = { "Вівіан", "Віктор", "Едгар", "Хазель" };
        static readonly Color TextColor = new Color(.91f, .92f, .94f);
        static readonly Color Muted = new Color(.60f, .66f, .72f);
        static readonly Color Accent = new Color(.39f, .78f, .72f);
        readonly List<Part> parts = new List<Part>();
        readonly List<AnimationState> clips = new List<AnimationState>();
        readonly List<UnityEngine.UI.Button> clipButtons = new List<UnityEngine.UI.Button>();
        readonly List<BoneLine> boneLines = new List<BoneLine>();
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<BonePose> restPose = new List<BonePose>();
        readonly List<BonePose> bindPose = new List<BonePose>();
        readonly UnityEngine.UI.Button[] characterButtons = new UnityEngine.UI.Button[4];
        GameObject stage, actor;
        Camera viewCamera;
        RenderTexture target;
        Animation animationPlayer;
        DeadAirCharacterMotion motion;
        DeadAirSecondaryMotion secondaryMotion;
        Transform floorTransform;
        Light[] actorLights = Array.Empty<Light>();
        Font font, heading;
        RectTransform viewport, clipContent, partContent, skeletonOverlay;
        UnityEngine.UI.RawImage view, textureView;
        UnityEngine.UI.Text description, timeLabel, textureLabel, stats;
        UnityEngine.UI.Slider timeline, speedControl;
        UnityEngine.UI.Button playButton, skeletonButton, surfaceButton, runtimeButton, physicsButton;
        UnityEngine.UI.Text speedLabel;
        Material clay;
        Light[] sceneLights;
        int[] lightMasks;
        bool ready, playing = true, skeletonVisible, clayVisible, runtimePose, physicsEnabled, runtimeReady, dirty;
        int roleIndex, clipIndex = -1, inspectedPart = -1;
        float clipTime, speed = 1, yaw = 18, pitch = 7, distance = 3, modelHeight = 1.8f;
        Vector3 focus;
        Bounds modelBounds;
        Action close;

        sealed class Part
        {
            public SkinnedMeshRenderer renderer;
            public Material[] materials;
            public UnityEngine.UI.Button visibility, inspect;
            public string id, label;
            public int triangles;
            public bool visible = true;
        }
        struct BonePose { public Transform bone; public Vector3 position, scale; public Quaternion rotation; }
        struct BoneLine { public Transform bone, parent; public RectTransform line; }

        public string CurrentRole => Roles[roleIndex];
        public GameObject ModelRoot => actor;
        public Camera ViewerCamera => viewCamera;
        public int PartCount => parts.Count;
        public int AnimationCount => clips.Count;
        public string CurrentAnimation => clipIndex < 0 ? "Bind pose" : clips[clipIndex].name;
        public bool IsPlaying => playing;
        public float NormalizedTime => clipIndex < 0 ? 0 : clipTime / Mathf.Max(.001f, clips[clipIndex].length);

        public void Initialize(Font bodyFont, Font titleFont, Action onClose)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? bodyFont;
            heading = titleFont; close = onClose;
            BuildInterface(); ready = true;
            if (isActiveAndEnabled) OpenStage();
        }

        void OnEnable() { if (ready) OpenStage(); }
        void OnDisable() { ReleaseStage(); }
        void OnDestroy() { ReleaseStage(); }

        void BuildInterface()
        {
            Label(transform, "Title", "ПЕРСОНАЖІ / АНІМАЦІЇ", 32, 28, 22, 1250, 48, true);
            MakeButton(transform, "Close gallery", "НАЗАД · ESC", 1694, 25, 198, 48, () => close?.Invoke());
            for (int i = 0; i < Roles.Length; i++)
            {
                int role = i;
                characterButtons[i] = MakeButton(transform, "Character " + Roles[i], Names[i], 332 + i * 299, 88, 287, 48, () => SelectCharacter(role));
            }
            Label(transform, "Animation heading", "АНІМАЦІЇ", 18, 24, 145, 290, 28);
            clipContent = ScrollList(transform, "Animation clips", 24, 184, 288, 503);
            playButton = MakeButton(transform, "Play pause", "ПАУЗА", 24, 704, 174, 44, () => SetPlaying(!playing));
            MakeButton(transform, "Previous frame", "‹", 204, 704, 48, 44, () => StepFrame(-1));
            MakeButton(transform, "Next frame", "›", 258, 704, 48, 44, () => StepFrame(1));
            runtimeButton = MakeButton(transform, "Pose mode", "ПОЗА: ІМПОРТОВАНИЙ КЛІП", 24, 762, 282, 52, () => SetRuntimePose(!runtimePose));
            SetFontSize(runtimeButton, 15);
            skeletonButton = MakeButton(transform, "Skeleton", "СКЕЛЕТ: ВИМК.", 24, 826, 137, 46, () => SetSkeletonVisible(!skeletonVisible)); SetFontSize(skeletonButton, 13);
            physicsButton = MakeButton(transform, "Secondary physics", "ФІЗИКА: ВИМК.", 169, 826, 137, 46, () => SetPhysicsEnabled(!physicsEnabled)); SetFontSize(physicsButton, 13);
            surfaceButton = MakeButton(transform, "Material inspection", "МАТЕРІАЛ: ТЕКСТУРИ", 24, 884, 282, 46, () => SetMaterialInspection(!clayVisible));
            SetFontSize(surfaceButton, 16);
            Label(transform, "Pose explanation", "Ігровий рух: справжня швидкість і контакт стоп. Фізика: волосся та одяг. Базова поза показує сам ріг.", 15, 24, 952, 282, 103).color = Muted;

            viewport = Rect(transform, "Model viewport", 332, 158, 1184, 700);
            view = viewport.gameObject.AddComponent<UnityEngine.UI.RawImage>(); view.color = Color.white; view.raycastTarget = true;
            viewport.gameObject.AddComponent<DeadAirGalleryViewportInput>().gallery = this;
            skeletonOverlay = Rect(viewport, "Skeleton overlay", 0, 0, 1184, 700);
            skeletonOverlay.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            Label(transform, "Camera controls", "ЛКМ — обертання   ·   ПКМ / СКМ — зміщення   ·   Колесо — наближення", 16, 332, 870, 1184, 28).color = Muted;
            MakeButton(transform, "Front view", "СПЕРЕДУ", 332, 911, 147, 42, () => SetCameraView(0, 5));
            MakeButton(transform, "Side view", "ЗБОКУ", 489, 911, 130, 42, () => SetCameraView(90, 5));
            MakeButton(transform, "Back view", "ЗЗАДУ", 629, 911, 130, 42, () => SetCameraView(180, 5));
            MakeButton(transform, "Frame full body", "ВСЯ МОДЕЛЬ", 769, 911, 202, 42, FrameAll);
            MakeButton(transform, "Frame face", "ОБЛИЧЧЯ", 981, 911, 164, 42, FrameFace);
            MakeButton(transform, "Frame feet", "СТОПИ", 1155, 911, 152, 42, FrameFeet);
            MakeButton(transform, "Frame selected part", "ОБРАНА ЧАСТИНА", 1317, 911, 199, 42, FrameSelected);
            timeLabel = Label(transform, "Time", "", 16, 332, 965, 770, 26);
            timeline = MakeSlider(transform, "Animation time", 332, 995, 842, 0, 1, 0, value => { SetPlaying(false); SetNormalizedTime(value); });
            speedLabel = Label(transform, "Speed label", "ШВИДКІСТЬ  1.00×", 16, 1200, 965, 316, 26);
            speedControl = MakeSlider(transform, "Animation speed", 1200, 995, 316, .1f, 2, 1, value => { speed = value; speedLabel.text = "ШВИДКІСТЬ  " + speed.ToString("0.00") + "×"; });

            Label(transform, "Parts heading", "ЧАСТИНИ МОДЕЛІ", 18, 1538, 145, 354, 28);
            partContent = ScrollList(transform, "Model parts", 1538, 184, 354, 490);
            MakeButton(transform, "All parts", "ПОКАЗАТИ ВСЕ", 1538, 686, 354, 40, ResetParts);
            textureView = Rect(transform, "Selected part texture", 1538, 772, 208, 208).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            textureView.color = Color.white; textureView.raycastTarget = false;
            textureLabel = Label(transform, "Texture details", "", 14, 1758, 772, 134, 208);
            description = Label(transform, "Selected part", "", 15, 1538, 736, 354, 28);
            stats = Label(transform, "Model statistics", "", 14, 1538, 993, 354, 74); stats.color = Muted;
        }

        void OpenStage()
        {
            if (stage != null) return;
            stage = new GameObject("Character gallery / isolated studio");
            stage.transform.position = new Vector3(300, 0, 300); stage.layer = StageLayer;
            var cameraObject = new GameObject("Character gallery camera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            viewCamera = cameraObject.GetComponent<Camera>(); viewCamera.enabled = false;
            viewCamera.cullingMask = 1 << StageLayer; viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(.105f, .13f, .16f); viewCamera.fieldOfView = 34;
            viewCamera.nearClipPlane = .015f; viewCamera.farClipPlane = 30; viewCamera.useOcclusionCulling = false;
            viewCamera.allowHDR = false; viewCamera.allowMSAA = true; viewCamera.renderingPath = RenderingPath.Forward;
            AddLight("Studio key", new Vector3(2, 4, 4), new Color(1, .96f, .91f), 1.25f);
            AddLight("Studio fill", new Vector3(-3, 2, 2), new Color(.82f, .90f, 1), .85f);
            AddLight("Studio rim", new Vector3(0, 3, -3), Color.white, 1.1f);
            clay = new Material(Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard"));
            clay.name = "Gallery inspection clay"; clay.color = new Color(.68f, .70f, .72f);
            clay.SetFloat("_Glossiness", .17f); ownedMaterials.Add(clay);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floorTransform = floor.transform;
            floor.name = "Gallery floor"; floor.layer = StageLayer; floor.transform.SetParent(stage.transform, false);
            floor.transform.localPosition = new Vector3(0, -.045f, 0); floor.transform.localScale = new Vector3(3.3f, .04f, 3.3f);
            // Keep the real collider for optional runtime contact previews, outside all game maps.
            var floorMaterial = new Material(clay) { name = "Gallery floor material", color = new Color(.16f, .19f, .22f) };
            ownedMaterials.Add(floorMaterial); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            sceneLights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            lightMasks = new int[sceneLights.Length];
            SelectCharacter(roleIndex);
        }

        void AddLight(string name, Vector3 position, Color color, float intensity)
        {
            var lightObject = new GameObject(name, typeof(Light)); lightObject.transform.SetParent(stage.transform, false);
            lightObject.transform.localPosition = position; lightObject.transform.LookAt(stage.transform.position + Vector3.up);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional;
            light.color = color; light.intensity = intensity; light.cullingMask = 1 << StageLayer;
            light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForcePixel;
        }

        public void SelectCharacter(int index)
        {
            if (stage == null) return;
            roleIndex = Mathf.Clamp(index, 0, Roles.Length - 1);
            if (actor != null) { actor.SetActive(false); Destroy(actor); }
            ClearChildren(clipContent); ClearChildren(partContent); ClearChildren(skeletonOverlay);
            parts.Clear(); clips.Clear(); clipButtons.Clear(); boneLines.Clear(); restPose.Clear(); bindPose.Clear();
            var prefab = Resources.Load<GameObject>("World/character_" + CurrentRole);
            if (prefab == null) { description.text = "Модель не знайдена: " + CurrentRole; return; }
            actor = Instantiate(prefab, stage.transform); actor.name = "Gallery " + CurrentRole;
            actor.transform.localPosition = Vector3.zero; actor.transform.localRotation = Quaternion.identity;
            motion = actor.GetComponent<DeadAirCharacterMotion>(); if (motion != null) motion.enabled = false;
            DeadAirClothing.Apply(actor, -1, CurrentRole);
            secondaryMotion = actor.GetComponent<DeadAirSecondaryMotion>(); runtimeReady = false;
            foreach (var child in actor.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = StageLayer;
                restPose.Add(new BonePose { bone = child, position = child.localPosition, rotation = child.localRotation, scale = child.localScale });
            }
            actorLights = actor.GetComponentsInChildren<Light>(true);
            foreach (var light in actorLights) light.enabled = false;
            foreach (var collider in actor.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            animationPlayer = actor.GetComponentInChildren<Animation>(true);
            if (animationPlayer != null)
            {
                animationPlayer.playAutomatically = false; animationPlayer.Stop(); animationPlayer.enabled = false;
                foreach (AnimationState state in animationPlayer) if (state.clip != null && !state.name.StartsWith("__")) clips.Add(state);
                clips.Sort((a, b) => { int order = ClipOrder(a.name).CompareTo(ClipOrder(b.name)); return order != 0 ? order : string.Compare(a.name, b.name, StringComparison.Ordinal); });
            }
            AddClipButton("Базова поза ріга", -1);
            for (int i = 0; i < clips.Count; i++) AddClipButton(ClipLabel(clips[i].name), i);
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                renderer.updateWhenOffscreen = true; renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var part = new Part { renderer = renderer, materials = renderer.sharedMaterials, id = renderer.name };
                part.label = PartLabel(renderer.name);
                for (int submesh = 0; submesh < renderer.sharedMesh.subMeshCount; submesh++) part.triangles += (int)renderer.sharedMesh.GetIndexCount(submesh) / 3;
                parts.Add(part);
            }
            parts.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.Ordinal));
            CaptureBindPose();
            for (int i = 0; i < parts.Count; i++) AddPartRow(i);
            CreateSkeleton();
            for (int i = 0; i < characterButtons.Length; i++) Highlight(characterButtons[i], i == roleIndex);
            clipIndex = clips.FindIndex(clip => clip.name == "Idle"); clipTime = 0;
            SetPlaying(true); SetMaterialInspection(clayVisible); SamplePose();
            modelBounds = BoundsOfParts(); modelHeight = Mathf.Max(.3f, modelBounds.size.y);
            InspectPart(parts.Count == 0 ? -1 : 0); FrameAll(); RefreshClipSelection();
            int triangles = 0; foreach (var part in parts) triangles += part.triangles;
            stats.text = parts.Count + " мешів · " + triangles.ToString("N0") + " трикутників\n" + clips.Count + " кліпів · " + modelHeight.ToString("0.00") + " м\nПід одягом можуть бути приховані ділянки тіла.";
            dirty = true;
        }

        void AddClipButton(string label, int index)
        {
            var button = MakeButton(clipContent, "Clip " + (index < 0 ? "Bind pose" : clips[index].name), label, 0, 0, 272, 44, () => SelectClip(index));
            SetFontSize(button, 17); AddRowSize(button.gameObject, 44); clipButtons.Add(button);
        }
        void AddPartRow(int index)
        {
            var part = parts[index]; var row = Rect(partContent, part.id, 0, 0, 338, 43); AddRowSize(row.gameObject, 43);
            part.visibility = MakeButton(row, "Visibility", "✓", 0, 0, 38, 39, () => SetPartVisible(part.id, !part.visible));
            part.inspect = MakeButton(row, "Inspect", part.label, 43, 0, 225, 39, () => InspectPart(index)); SetFontSize(part.inspect, 15);
            var solo = MakeButton(row, "Solo", "СОЛО", 273, 0, 65, 39, () => SoloPart(part.id)); SetFontSize(solo, 12);
        }

        public void SelectAnimation(string name) { SelectClip(clips.FindIndex(clip => clip.name == name)); }
        void SelectClip(int index)
        {
            clipIndex = Mathf.Clamp(index, -1, clips.Count - 1); clipTime = 0;
            if (clipIndex < 0) SetPlaying(false);
            runtimeReady = false; motion?.ResetLocomotionContacts(); SamplePose(); RefreshClipSelection(); dirty = true;
        }
        public void SetPlaying(bool value)
        {
            playing = value && clipIndex >= 0;
            if (playButton != null) playButton.GetComponentInChildren<UnityEngine.UI.Text>().text = playing ? "ПАУЗА" : "ВІДТВОРИТИ";
        }
        public void SetNormalizedTime(float time)
        {
            clipTime = clipIndex < 0 ? 0 : Mathf.Clamp01(time) * clips[clipIndex].length;
            motion?.ResetLocomotionContacts(); SamplePose(); dirty = true;
        }
        void StepFrame(int direction)
        {
            SetPlaying(false); if (clipIndex < 0) return;
            float rate = Mathf.Max(1, clips[clipIndex].clip.frameRate);
            SetNormalizedTime((clipTime + direction / rate) / Mathf.Max(.001f, clips[clipIndex].length));
        }
        public void SetRuntimePose(bool value)
        {
            runtimePose = value; runtimeButton.GetComponentInChildren<UnityEngine.UI.Text>().text = value ? "ПОЗА: ЯК У ГРІ" : "ПОЗА: ІМПОРТОВАНИЙ КЛІП";
            if (value) SetPhysicsEnabled(true);
            motion?.ResetLocomotionContacts(); SamplePose(); dirty = true;
        }
        public void SetPhysicsEnabled(bool value)
        {
            physicsEnabled = value; physicsButton.GetComponentInChildren<UnityEngine.UI.Text>().text = value ? "ФІЗИКА: УВІМК." : "ФІЗИКА: ВИМК.";
            secondaryMotion?.ResetMotion(); SamplePose(); dirty = true;
        }
        bool RuntimeGait => runtimePose && motion != null && clipIndex >= 0 && IsGait(clips[clipIndex].name);
        static bool IsGait(string name) => name == "Idle" || name == "Walk" || name == "WalkBack" || name == "StrafeLeft" || name == "StrafeRight" || name == "Run" || name == "CrouchIdle" || name == "CrouchWalk";
        void ResetRootPosition()
        {
            var offset = actor.transform.position - stage.transform.position;
            actor.transform.localPosition = Vector3.zero; focus -= offset;
            if (floorTransform != null) floorTransform.localPosition = new Vector3(0, -.045f, 0);
        }
        void SampleRuntimeGait(float dt, bool reset)
        {
            var name = clips[clipIndex].name;
            bool crouched = name.StartsWith("Crouch", StringComparison.Ordinal), running = name == "Run";
            float velocity = name.EndsWith("Idle", StringComparison.Ordinal) ? 0 : running ? DeadAirLocomotion.RunSpeed : crouched ? DeadAirLocomotion.CrouchSpeed : DeadAirLocomotion.WalkSpeed;
            var direction = name == "WalkBack" ? Vector3.back : name == "StrafeLeft" ? Vector3.left : name == "StrafeRight" ? Vector3.right : Vector3.forward;
            var movement = direction * velocity;
            if (reset || !runtimeReady)
            {
                ResetRootPosition(); motion.ResetLocomotionContacts();
                motion.SetLocomotionPreview(movement, crouched, running, 0, true);
                dt = clipTime; runtimeReady = true;
            }
            // A scrub is replayed from a clean state; playing advances only one real timestep.
            for (float remaining = dt; remaining > .00001f;)
            {
                float step = Mathf.Min(1f / 60, remaining); remaining -= step;
                var delta = movement * step; actor.transform.position += delta; focus += delta;
                if (floorTransform != null) floorTransform.position += delta;
                Physics.SyncTransforms();
                motion.SetLocomotionPreview(movement, crouched, running, step);
                if (!physicsEnabled) secondaryMotion?.ResetMotion();
            }
            if (dt <= 0) motion.SetLocomotionPreview(movement, crouched, running, 0);
            if (!physicsEnabled) secondaryMotion?.ResetMotion();
        }
        void SamplePose(float dt = 0, bool resetRuntime = true)
        {
            if (actor == null) return;
            if (RuntimeGait)
            {
                SampleRuntimeGait(dt, resetRuntime);
                FinishSample(); return;
            }
            runtimeReady = false; ResetRootPosition();
            // Reset every local transform so changing clips cannot preserve an unkeyed pose.
            if (motion != null) motion.SetPreview("Idle", 0);
            foreach (var pose in restPose) if (pose.bone != null) { pose.bone.localPosition = pose.position; pose.bone.localRotation = pose.rotation; pose.bone.localScale = pose.scale; }
            if (clipIndex < 0)
                foreach (var pose in bindPose) if (pose.bone != null) { pose.bone.localPosition = pose.position; pose.bone.localRotation = pose.rotation; pose.bone.localScale = pose.scale; }
            if (clipIndex >= 0)
            {
                var clip = clips[clipIndex];
                if (runtimePose && motion != null)
                {
                    var equipment = new PlayerState { characterClass = CurrentRole, crouching = clip.name.StartsWith("Crouch", StringComparison.Ordinal), uniqueItemEquipped = clip.name == "UseItem", inventory = new List<string>() };
                    motion.SetEquipmentPreview(equipment, clip.name, Mathf.Clamp01(clipTime / Mathf.Max(.001f, clip.length)));
                }
                else clip.clip.SampleAnimation(animationPlayer.gameObject, Mathf.Clamp(clipTime, 0, clip.length));
                if (secondaryMotion != null)
                {
                    secondaryMotion.CaptureAnimatedPose();
                    if (physicsEnabled) secondaryMotion.Step(dt, 0, 0, true, resetRuntime);
                    else secondaryMotion.ResetMotion();
                }
            }
            FinishSample();
        }
        void FinishSample()
        {
            foreach (var part in parts) part.renderer.enabled = part.visible;
            // The inspector's neutral lamps are independent from any held flashlight.
            foreach (var light in actorLights) light.enabled = false;
            if (timeline != null) timeline.SetValueWithoutNotify(NormalizedTime);
            if (timeLabel != null) timeLabel.text = clipIndex < 0 ? "БАЗОВА ПОЗА РІГА" : ClipLabel(clips[clipIndex].name) + "   " + clipTime.ToString("0.00") + " / " + clips[clipIndex].length.ToString("0.00") + " с   ·   " + (RuntimeGait ? "ігровий рух" : "кадр " + Mathf.RoundToInt(clipTime * clips[clipIndex].clip.frameRate));
        }

        public void SetPartVisible(string id, bool visible)
        {
            foreach (var part in parts) if (part.id == id) { part.visible = visible; part.renderer.enabled = visible; part.visibility.GetComponentInChildren<UnityEngine.UI.Text>().text = visible ? "✓" : "—"; }
            dirty = true;
        }
        public void SoloPart(string id)
        {
            for (int i = 0; i < parts.Count; i++) { SetPartVisible(parts[i].id, parts[i].id == id); if (parts[i].id == id) InspectPart(i); }
            FrameSelected();
        }
        public void ResetParts() { foreach (var part in parts) SetPartVisible(part.id, true); FrameAll(); }
        void InspectPart(int index)
        {
            inspectedPart = index;
            if (index < 0 || index >= parts.Count) { textureView.texture = null; description.text = ""; textureLabel.text = ""; return; }
            var part = parts[index]; Texture texture = null;
            foreach (var material in part.materials) if (material != null && material.mainTexture != null) { texture = material.mainTexture; break; }
            textureView.texture = texture; textureView.enabled = texture != null;
            description.text = part.label + " · " + part.triangles.ToString("N0") + " трис";
            textureLabel.text = texture != null ? "ALBEDO\n" + texture.width + " × " + texture.height + "\n\n" + texture.name : "Без текстури";
            foreach (var entry in parts) Highlight(entry.inspect, entry == part);
        }
        public void SetMaterialInspection(bool value)
        {
            clayVisible = value;
            foreach (var part in parts)
            {
                if (value) { var materials = new Material[part.materials.Length]; for (int i = 0; i < materials.Length; i++) materials[i] = clay; part.renderer.sharedMaterials = materials; }
                else part.renderer.sharedMaterials = part.materials;
            }
            surfaceButton.GetComponentInChildren<UnityEngine.UI.Text>().text = value ? "МАТЕРІАЛ: СІРА ГЕОМЕТРІЯ" : "МАТЕРІАЛ: ТЕКСТУРИ"; dirty = true;
        }
        public void SetSkeletonVisible(bool value)
        {
            skeletonVisible = value; skeletonOverlay.gameObject.SetActive(value);
            skeletonButton.GetComponentInChildren<UnityEngine.UI.Text>().text = value ? "СКЕЛЕТ: УВІМК." : "СКЕЛЕТ: ВИМК."; dirty = true;
        }
        void CreateSkeleton()
        {
            var bones = new HashSet<Transform>();
            foreach (var part in parts) foreach (var bone in part.renderer.bones) if (bone != null) bones.Add(bone);
            foreach (var bone in bones)
            {
                if (bone.parent == null || !bones.Contains(bone.parent)) continue;
                var line = Rect(skeletonOverlay, bone.name, 0, 0, 1, 2);
                line.pivot = new Vector2(0, .5f);
                var graphic = line.gameObject.AddComponent<UnityEngine.UI.Image>(); graphic.raycastTarget = false;
                graphic.color = bone.name.StartsWith("secondary_", StringComparison.Ordinal) ? new Color(1, .72f, .24f) : Accent;
                boneLines.Add(new BoneLine { bone = bone, parent = bone.parent, line = line });
            }
            SetSkeletonVisible(skeletonVisible);
        }
        void UpdateSkeleton()
        {
            if (!skeletonVisible || viewCamera == null) return;
            foreach (var line in boneLines)
            {
                Vector3 a = viewCamera.WorldToViewportPoint(line.parent.position), b = viewCamera.WorldToViewportPoint(line.bone.position);
                bool visible = a.z > 0 && b.z > 0; line.line.gameObject.SetActive(visible); if (!visible) continue;
                var from = new Vector2(a.x * viewport.rect.width, (a.y - 1) * viewport.rect.height);
                var to = new Vector2(b.x * viewport.rect.width, (b.y - 1) * viewport.rect.height);
                var delta = to - from; line.line.anchoredPosition = from; line.line.sizeDelta = new Vector2(delta.magnitude, 2);
                line.line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
        }

        public void Orbit(Vector2 delta) { yaw -= delta.x * .24f; pitch = Mathf.Clamp(pitch + delta.y * .22f, -75, 80); dirty = true; }
        public void Pan(Vector2 delta)
        {
            if (viewCamera == null) return;
            focus -= (viewCamera.transform.right * delta.x + viewCamera.transform.up * delta.y) * (distance * .0011f); dirty = true;
        }
        public void Zoom(float amount) { distance = Mathf.Clamp(distance * Mathf.Exp(-amount * .16f), .12f, modelHeight * 6); dirty = true; }
        public void SetCameraView(float horizontal, float vertical) { yaw = horizontal; pitch = vertical; dirty = true; }
        public void FrameAll() { focus = modelBounds.center + (actor != null ? actor.transform.position - stage.transform.position : Vector3.zero); distance = Mathf.Max(.3f, modelHeight * 1.95f); dirty = true; }
        public void FrameFace()
        {
            if (actor == null) return;
            var head = FindBone("head"); focus = head != null ? head.position + Vector3.up * modelHeight * .05f : modelBounds.center + Vector3.up * modelHeight * .36f;
            distance = modelHeight * .63f; yaw = 0; pitch = 3; dirty = true;
        }
        public void FrameFeet() { focus = modelBounds.center + (actor != null ? actor.transform.position - stage.transform.position : Vector3.zero); focus.y = modelBounds.min.y + modelHeight * .1f; distance = modelHeight * .70f; yaw = 22; pitch = 15; dirty = true; }
        public void FrameSelected()
        {
            if (inspectedPart < 0 || inspectedPart >= parts.Count) return;
            var bounds = BakedBounds(parts[inspectedPart].renderer); focus = bounds.center;
            distance = Mathf.Max(modelHeight * .19f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) * 2.05f); dirty = true;
        }
        Transform FindBone(string name) { foreach (var pose in restPose) if (pose.bone != null && pose.bone.name == name) return pose.bone; return null; }
        void CaptureBindPose()
        {
            // Prefabs open in Idle. Reconstruct the actual authored rest transforms from
            // each mesh's inverse bind matrices instead of calling that sampled pose a bind pose.
            var world = new Dictionary<Transform, Matrix4x4>();
            foreach (var part in parts)
            {
                var bones = part.renderer.bones; var inverseBinds = part.renderer.sharedMesh.bindposes;
                for (int i = 0; i < Mathf.Min(bones.Length, inverseBinds.Length); i++)
                    if (bones[i] != null && !world.ContainsKey(bones[i])) world.Add(bones[i], part.renderer.localToWorldMatrix * inverseBinds[i].inverse);
            }
            foreach (var entry in world)
            {
                var parent = entry.Key.parent;
                var parentWorld = parent == null ? Matrix4x4.identity : world.TryGetValue(parent, out var bindParent) ? bindParent : parent.localToWorldMatrix;
                var local = parentWorld.inverse * entry.Value;
                bindPose.Add(new BonePose { bone = entry.Key, position = local.GetColumn(3), rotation = local.rotation, scale = local.lossyScale });
            }
        }
        Bounds BoundsOfParts()
        {
            var result = new Bounds(stage.transform.position + Vector3.up, new Vector3(.5f, 2, .5f)); bool first = true;
            foreach (var part in parts) { var bounds = BakedBounds(part.renderer); if (first) { result = bounds; first = false; } else result.Encapsulate(bounds); }
            return result;
        }
        static Bounds BakedBounds(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh(); renderer.BakeMesh(mesh);
            var vertices = mesh.vertices; var matrix = renderer.localToWorldMatrix;
            // A rotated local AABB includes empty space. Frame the deformed surface
            // itself so the displayed height and close-ups match the actual model.
            var result = new Bounds(vertices.Length == 0 ? renderer.transform.position : matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) result.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
            Destroy(mesh); return result;
        }

        void LateUpdate()
        {
            if (stage == null || actor == null) return;
            if (playing && clipIndex >= 0)
            {
                float step = Mathf.Min(Time.unscaledDeltaTime, .1f) * speed;
                clipTime = Mathf.Repeat(clipTime + step, Mathf.Max(.001f, clips[clipIndex].length));
                SamplePose(step, false); dirty = true;
            }
            int width = Mathf.Clamp(Mathf.RoundToInt(viewport.rect.width * GetComponentInParent<Canvas>().scaleFactor), 320, 1600);
            int height = Mathf.RoundToInt(width * viewport.rect.height / viewport.rect.width);
            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) { target.Release(); Destroy(target); }
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Character gallery render", antiAliasing = 2 };
                target.Create(); view.texture = target; viewCamera.targetTexture = target; dirty = true;
            }
            if (!dirty) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            viewCamera.transform.position = focus + rotation * (Vector3.forward * distance);
            viewCamera.transform.LookAt(focus); viewCamera.aspect = (float)target.width / target.height;
            RenderNeutral(); UpdateSkeleton(); dirty = false;
        }
        void RenderNeutral()
        {
            var ambientMode = RenderSettings.ambientMode; var ambient = RenderSettings.ambientLight;
            var probe = RenderSettings.ambientProbe; bool fog = RenderSettings.fog; float reflection = RenderSettings.reflectionIntensity;
            try
            {
                // These values exist only during this camera's render, never between game frames.
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.42f, .44f, .48f);
                var studioProbe = new SphericalHarmonicsL2(); studioProbe.AddAmbientLight(new Color(.32f, .34f, .38f)); RenderSettings.ambientProbe = studioProbe;
                RenderSettings.fog = false; RenderSettings.reflectionIntensity = 0;
                for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i] != null)
                {
                    lightMasks[i] = sceneLights[i].cullingMask;
                    if (!sceneLights[i].transform.IsChildOf(stage.transform)) sceneLights[i].cullingMask &= ~(1 << StageLayer);
                }
                viewCamera.Render();
            }
            finally
            {
                for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i] != null) sceneLights[i].cullingMask = lightMasks[i];
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient; RenderSettings.ambientProbe = probe;
                RenderSettings.fog = fog; RenderSettings.reflectionIntensity = reflection;
            }
        }
        void ReleaseStage()
        {
            if (view != null) view.texture = null;
            if (viewCamera != null) viewCamera.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); target = null; }
            if (stage != null) { stage.SetActive(false); Destroy(stage); stage = null; }
            foreach (var material in ownedMaterials) if (material != null) Destroy(material);
            ownedMaterials.Clear(); actor = null; viewCamera = null; animationPlayer = null; motion = null; secondaryMotion = null; floorTransform = null;
            parts.Clear(); clips.Clear(); restPose.Clear(); bindPose.Clear(); boneLines.Clear();
        }

        void RefreshClipSelection() { for (int i = 0; i < clipButtons.Count; i++) Highlight(clipButtons[i], i == clipIndex + 1); }
        static int ClipOrder(string name)
        {
            switch (name) { case "Idle": return 0; case "Walk": return 1; case "WalkBack": return 2; case "StrafeLeft": return 3; case "StrafeRight": return 4; case "Run": return 5; case "CrouchIdle": return 6; case "CrouchWalk": return 7; case "UseItem": return 8; default: return 10; }
        }
        static string ClipLabel(string name)
        {
            switch (name) { case "Idle": return "Стояння · Idle"; case "Walk": return "Ходьба · Walk"; case "WalkBack": return "Назад · WalkBack"; case "StrafeLeft": return "Вліво · StrafeLeft"; case "StrafeRight": return "Вправо · StrafeRight"; case "Run": return "Біг · Run"; case "CrouchIdle": return "Присідання · CrouchIdle"; case "CrouchWalk": return "Навприсядки · CrouchWalk"; case "UseItem": return "Предмет · UseItem"; case "Climb": return "Драбина · Climb"; case "Downed": return "Збитий · Downed"; case "Death": return "Смерть · Death"; default: return name; }
        }
        static string PartLabel(string name)
        {
            int split = name.LastIndexOf('_'); string id = (split < 0 ? name : name.Substring(split + 1)).ToLowerInvariant();
            switch (id)
            {
                case "body": return "Тіло"; case "hair": return "Волосся"; case "dress": return "Сукня"; case "bra": return "Бюстгальтер"; case "stockings": return "Панчохи"; case "garters": return "Підв’язки";
                case "shoes": return "Взуття"; case "jewelry": return "Прикраси"; case "shirt": return "Сорочка"; case "jacket": return "Піджак / пальто"; case "trousers": return "Штани";
                case "tie": return "Краватка"; case "beard": return "Борода"; case "cap": return "Кашкет"; case "badge": return "Значок"; case "equipment": return "Спорядження"; case "top": case "croppedtop": return "Топ"; case "sleeves": return "Рукави"; case "skirt": case "wrapskirt": return "Спідниця"; default: return name;
            }
        }
        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        UnityEngine.UI.Text Label(Transform parent, string name, string text, int size, float x, float y, float width, float height, bool title = false)
        {
            var label = Rect(parent, name, x, y, width, height).gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = title ? heading : font; label.text = text; label.fontSize = size; label.color = TextColor; label.raycastTarget = false;
            label.supportRichText = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        UnityEngine.UI.Button MakeButton(Transform parent, string name, string text, float x, float y, float width, float height, Action clicked)
        {
            var rect = Rect(parent, name, x, y, width, height); var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.white;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = new Color(.16f, .20f, .25f); colors.highlightedColor = new Color(.24f, .33f, .38f); colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.13f, .36f, .34f); button.colors = colors;
            var label = Label(rect, "Label", text, 17, 7, 0, width - 14, height); label.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(() => clicked()); return button;
        }
        static void SetFontSize(UnityEngine.UI.Button button, int size) { button.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = size; }
        static void Highlight(UnityEngine.UI.Button button, bool selected)
        {
            var colors = button.colors; colors.normalColor = selected ? new Color(.18f, .37f, .36f) : new Color(.16f, .20f, .25f); button.colors = colors;
        }
        static void AddRowSize(GameObject row, float height) { var layout = row.AddComponent<UnityEngine.UI.LayoutElement>(); layout.minHeight = layout.preferredHeight = height; }
        RectTransform ScrollList(Transform parent, string name, float x, float y, float width, float height)
        {
            var root = Rect(parent, name, x, y, width, height); var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var visible = Rect(root, "Viewport", 0, 0, width, height); visible.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.07f, .10f, .13f);
            visible.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = true;
            var content = Rect(visible, "Content", 0, 0, width - 4, height);
            content.anchorMax = new Vector2(1, 1); content.sizeDelta = new Vector2(-4, 0);
            var rows = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); rows.spacing = 5; rows.padding = new RectOffset(4, 4, 4, 4); rows.childControlWidth = rows.childControlHeight = true; rows.childForceExpandWidth = true; rows.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = visible; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28; return content;
        }
        UnityEngine.UI.Slider MakeSlider(Transform parent, string name, float x, float y, float width, float min, float max, float value, Action<float> changed)
        {
            var root = Rect(parent, name, x, y, width, 35); root.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.10f, .15f, .19f);
            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.minValue = min; slider.maxValue = max;
            var area = Rect(root, "Handle area", 8, 0, width - 16, 35);
            var handle = Rect(area, "Handle", 0, 0, 16, 0); handle.pivot = new Vector2(.5f, .5f);
            var image = handle.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Accent;
            slider.handleRect = handle; slider.targetGraphic = image; slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.value = value; slider.onValueChanged.AddListener(v => changed(v)); return slider;
        }
        static void ClearChildren(Transform parent) { for (int i = parent.childCount - 1; i >= 0; i--) { var child = parent.GetChild(i).gameObject; child.SetActive(false); Destroy(child); } }
    }

    public sealed class DeadAirGalleryViewportInput : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
    {
        public DeadAirCharacterGallery gallery;
        public void OnBeginDrag(PointerEventData data) { }
        public void OnDrag(PointerEventData data)
        {
            float scale = GetComponentInParent<Canvas>().scaleFactor;
            if (data.button == PointerEventData.InputButton.Left) gallery.Orbit(data.delta / Mathf.Max(.01f, scale));
            else gallery.Pan(data.delta / Mathf.Max(.01f, scale));
        }
        public void OnScroll(PointerEventData data) { gallery.Zoom(data.scrollDelta.y); }
    }
}
