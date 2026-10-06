using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>Plays the authored Blender skeleton and presents the equipment in network snapshots.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DeadAirCharacterMotion : MonoBehaviour
    {
        const float SmokingDuration = 7f, PersonalActionDuration = 2.5f;
        [SerializeField] string classId = "engineer";
        public string ClassId { get => classId; set => classId = value; }
        public float GripContactError { get; private set; }

        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly Dictionary<string, Transform> props = new Dictionary<string, Transform>();
        readonly List<Transform> packedCigarettes = new List<Transform>();
        readonly List<Transform> smoke = new List<Transform>();
        Animation animationPlayer;
        Transform rightSocket, leftSocket, rightArm, leftArm, rightForearm, leftForearm, rightHand, leftHand, head, packLid, cigarette, lighter, flame, ember, compassNeedle, pendulum;
        Vector3 rightGrip, leftGrip, mouthLocal = new Vector3(0, .092f, .132f);
        Material dark, metal, brass, paper, red, glass, smokeMaterial;
        Mesh boxMesh, tubeMesh;
        string clipName = "Idle", previousClip, propMode;
        float clipTime, previousClipTime, transition = 1, moveSpeed, movementAt = -100, sampleAt;
        float lastElapsed, actionTime = -1;
        int lastSequence = -1;
        Vector2 lastPosition;
        bool initialized, hasSnapshot, preview;

        void Awake() { Initialize(); }

        void LateUpdate()
        {
            if (preview) return;
            if (hasSnapshot)
            {
                // Apply hand contacts after Unity's legacy animation evaluation and
                // after the network view has moved/turned its root this frame.
                SamplePose();
                PoseEquipment();
                return;
            }
            Initialize();
            if (animationPlayer == null) return;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            SelectClip("Idle");
            Advance(dt);
            SamplePose();
        }

        public void Present(PlayerState player, float elapsed, bool paused)
        {
            if (player == null) return;
            Initialize();
            preview = false;
            float dt = paused || player.down ? 0 : Mathf.Min(Time.deltaTime, .1f);
            var position = new Vector2(player.x, player.z);
            bool reset = !hasSnapshot || elapsed < lastElapsed || player.uniqueActionSequence < lastSequence;
            if (reset)
            {
                lastPosition = position;
                sampleAt = elapsed;
                moveSpeed = 0;
                movementAt = -100;
            }
            else if (Vector2.SqrMagnitude(position - lastPosition) > .000001f)
            {
                float interval = Mathf.Max(.016f, elapsed - sampleAt);
                float distance = Vector2.Distance(position, lastPosition);
                // Spawn/teleport corrections must not look like sprinting.
                moveSpeed = distance < 3 ? Mathf.Min(distance / interval, 8) : 0;
                movementAt = elapsed;
                sampleAt = elapsed;
                lastPosition = position;
            }
            if (!paused && elapsed - movementAt > .22f) moveSpeed = 0;
            hasSnapshot = true;
            lastElapsed = elapsed;
            lastSequence = player.uniqueActionSequence;
            float duration = classId == "operator" ? SmokingDuration : PersonalActionDuration;
            float acceptedAge = elapsed - player.uniqueActionAt;
            actionTime = !player.uniqueActionCancelled && player.uniqueItemEquipped && !player.mapOpen && !player.camera && player.uniqueActionSequence > 0
                && acceptedAge >= 0 && acceptedAge < duration ? acceptedAge : -1;

            if (!paused && !player.down)
            {
                SelectClip(actionTime >= 0 ? "UseItem" : moveSpeed > .15f ? (player.sprinting ? "Run" : "Walk") : "Idle");
                Advance(dt);
                if (actionTime >= 0 && animationPlayer != null && animationPlayer["UseItem"] != null)
                    clipTime = actionTime / duration * animationPlayer["UseItem"].length;
            }
            SamplePose();
            ShowEquipment(player);
            PoseEquipment();
        }

        /// <summary>Samples the exact imported clip for editor captures without starting gameplay.</summary>
        public void SetPreview(string name, float normalizedTime = 0)
        {
            Initialize();
            preview = true;
            SelectClip(name);
            previousClip = null;
            transition = 1;
            if (animationPlayer != null && animationPlayer[clipName] != null)
                clipTime = Mathf.Clamp01(normalizedTime) * animationPlayer[clipName].length;
            actionTime = clipName == "UseItem" ? Mathf.Clamp01(normalizedTime) * (classId == "operator" ? SmokingDuration : PersonalActionDuration) : -1;
            lastElapsed = Mathf.Max(0, actionTime);
            SamplePose();
            ShowEquipment(new PlayerState { characterClass = classId, uniqueItemEquipped = true, cigarettesRemaining = 12 });
            PoseEquipment();
        }

        public void SetEquipmentPreview(PlayerState equipment, string name = "Idle", float normalizedTime = 0)
        {
            if (equipment == null) return;
            SetPreview(name, normalizedTime);
            SamplePose();
            ShowEquipment(equipment);
            PoseEquipment();
        }

        void Initialize()
        {
            if (initialized) return;
            animationPlayer = GetComponentInChildren<Animation>(true);
            if (animationPlayer == null) return;
            initialized = true;
            animationPlayer.playAutomatically = false;
            animationPlayer.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState state in animationPlayer)
            {
                state.speed = 0;
                state.enabled = false;
                state.weight = 0;
                state.wrapMode = state.name == "UseItem" ? WrapMode.ClampForever : WrapMode.Loop;
            }
            var transforms = GetComponentsInChildren<Transform>(true);
            rightSocket = FindSocket(transforms, "ItemSocket.R", "hand.R", "RightHand");
            leftSocket = FindSocket(transforms, "ItemSocket.L", "hand.L", "LeftHand");
            foreach (var bone in transforms)
            {
                if (bone.name == "upper_arm.R") rightArm = bone;
                else if (bone.name == "upper_arm.L") leftArm = bone;
                else if (bone.name == "forearm.R") rightForearm = bone;
                else if (bone.name == "forearm.L") leftForearm = bone;
                else if (bone.name == "hand.R") rightHand = bone;
                else if (bone.name == "hand.L") leftHand = bone;
                else if (bone.name == "head") head = bone;
            }
            if (rightSocket == null || leftSocket == null) return;
            if (rightHand != null && leftHand != null)
            {
                // The imported socket marks the palm. Measure the actual finger pads,
                // including the engineer's larger hands, before attaching equipment.
                rightGrip = MeasureGrip(rightHand);
                leftGrip = MeasureGrip(leftHand);
                rightSocket = Group("Right finger-pad equipment contact", rightHand);
                rightSocket.localPosition = rightGrip;
                leftSocket = Group("Left finger-pad equipment contact", leftHand);
                leftSocket.localPosition = leftGrip;
                MeasureMouth();
            }
            CreateEquipment();
        }

        Vector3 MeasureGrip(Transform hand)
        {
            var bounds = new Bounds();
            var points = new List<Vector3>();
            bool found = false;
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int boneIndex = Array.IndexOf(renderer.bones, hand);
                if (boneIndex < 0 || renderer.sharedMesh == null) continue;
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    var vertices = baked.vertices;
                    var weights = renderer.sharedMesh.boneWeights;
                    for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                    {
                        var w = weights[i];
                        if (!((w.boneIndex0 == boneIndex && w.weight0 > .8f) || (w.boneIndex1 == boneIndex && w.weight1 > .8f) ||
                            (w.boneIndex2 == boneIndex && w.weight2 > .8f) || (w.boneIndex3 == boneIndex && w.weight3 > .8f))) continue;
                        var point = hand.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                        points.Add(point);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                finally { if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked); }
            }
            if (!found) return new Vector3(0, .09f, -.025f);
            var target = new Vector3(bounds.center.x * .25f, Mathf.Lerp(bounds.min.y, bounds.max.y, .68f),
                Mathf.Lerp(bounds.min.z, bounds.max.z, .30f));
            Vector3 contact = points[0];
            float nearest = float.MaxValue;
            foreach (var point in points)
            {
                float distance = Vector3.SqrMagnitude(point - target);
                if (distance < nearest) { nearest = distance; contact = point; }
            }
            return contact;
        }

        void MeasureMouth()
        {
            if (head == null || classId != "operator") return;
            foreach (var marker in head.GetComponentsInChildren<Transform>(true))
                if (marker.name == "MouthSocket")
                {
                    mouthLocal = head.InverseTransformPoint(marker.position);
                    return;
                }
            var bounds = new Bounds();
            bool found = false;
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    var vertices = baked.vertices;
                    var materials = renderer.sharedMaterials;
                    for (int submesh = 0; submesh < materials.Length && submesh < renderer.sharedMesh.subMeshCount; submesh++)
                    {
                        if (materials[submesh] == null || materials[submesh].name.IndexOf("LipDark", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        foreach (int index in renderer.sharedMesh.GetTriangles(submesh))
                        {
                            var point = head.InverseTransformPoint(renderer.transform.TransformPoint(vertices[index]));
                            if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                            else bounds.Encapsulate(point);
                        }
                    }
                }
                finally { if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked); }
            }
            if (found) mouthLocal = bounds.center;
        }

        Transform FindSocket(Transform[] bones, string socketName, string boneName, string alternative)
        {
            foreach (var bone in bones) if (bone.name == socketName) return bone;
            foreach (var bone in bones)
                if (bone.name == boneName || bone.name == alternative || bone.name.EndsWith("_" + boneName, StringComparison.Ordinal))
                {
                    var socket = new GameObject(socketName).transform;
                    socket.SetParent(bone, false);
                    socket.localPosition = new Vector3(0, .04f, 0);
                    return socket;
                }
            return null;
        }

        void SelectClip(string name)
        {
            if (animationPlayer == null || animationPlayer[name] == null || name == clipName) return;
            previousClip = clipName;
            previousClipTime = clipTime;
            clipName = name;
            clipTime = 0;
            transition = 0;
        }

        void Advance(float dt)
        {
            clipTime += dt;
            previousClipTime += dt;
            transition = Mathf.MoveTowards(transition, 1, dt / .14f);
            if (transition >= 1) previousClip = null;
        }

        void SamplePose()
        {
            if (animationPlayer == null) return;
            foreach (AnimationState state in animationPlayer) { state.enabled = false; state.weight = 0; }
            var current = animationPlayer[clipName];
            if (current == null) return;
            current.enabled = true;
            current.weight = previousClip == null ? 1 : transition;
            current.time = clipTime;
            if (previousClip != null && animationPlayer[previousClip] != null)
            {
                var previous = animationPlayer[previousClip];
                previous.enabled = true;
                previous.weight = 1 - transition;
                previous.time = previousClipTime;
            }
            animationPlayer.Sample();
        }

        void ShowEquipment(PlayerState player)
        {
            if (props.Count == 0) return;
            string selected = player.inventory != null && player.selectedSlot >= 0 && player.selectedSlot < player.inventory.Count
                ? player.inventory[player.selectedSlot] : "empty";
            string mode = player.down || !string.IsNullOrEmpty(player.hiding) ? "empty" : player.mapOpen ? "map" : player.camera ? "camera"
                : player.uniqueItemEquipped ? classId : InventoryMode(selected);
            if (mode != propMode)
            {
                propMode = mode;
                foreach (var prop in props) prop.Value.gameObject.SetActive(prop.Key == mode);
            }
            if (glass != null) glass.color = player.flashlight ? new Color32(241, 219, 156, 255) : new Color32(40, 56, 55, 255);
            if (cigarette != null)
            {
                bool smoking = mode == "operator" && actionTime > .5f;
                bool lighting = smoking && actionTime > 1.5f && actionTime < 3.25f;
                cigarette.gameObject.SetActive(smoking);
                lighter.gameObject.SetActive(lighting);
                props["operator"].gameObject.SetActive(mode == "operator" && !lighting);
                flame.gameObject.SetActive(smoking && actionTime > 2 && actionTime < 3);
                ember.gameObject.SetActive(smoking && actionTime > 2.25f);
                packLid.localRotation = Quaternion.Euler(mode == "operator" && actionTime >= 0 ? -105 : -12, 0, 0);
                for (int i = 0; i < packedCigarettes.Count; i++) packedCigarettes[i].gameObject.SetActive(i < Mathf.CeilToInt(player.cigarettesRemaining / 2f));
                for (int i = 0; i < smoke.Count; i++)
                {
                    bool visible = smoking && actionTime > 3;
                    smoke[i].gameObject.SetActive(visible);
                    if (!visible) continue;
                    float age = Mathf.Repeat((actionTime - 3) * .6f + i / (float)smoke.Count, 1);
                    smoke[i].position = cigarette.TransformPoint(new Vector3(0, .082f, 0)) +
                        new Vector3(Mathf.Sin(age * 9 + i) * age * .025f, age * .22f, age * .04f);
                    smoke[i].localScale = Vector3.one * (.008f + age * .035f);
                }
            }
            if (compassNeedle != null)
            {
                compassNeedle.localRotation = Quaternion.Euler(0, -player.yaw * Mathf.Rad2Deg, 0);
                pendulum.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(lastElapsed * (actionTime >= 0 ? 7 : 2.5f)) * (actionTime >= 0 ? 22 : 5));
            }
        }

        void PoseEquipment()
        {
            GripContactError = 0;
            if (propMode == "empty" || string.IsNullOrEmpty(propMode)) return;
            if (propMode == "map") { PoseMap(); return; }
            if (propMode == "camera") { PoseCamera(); return; }
            if (propMode == "operator" && actionTime >= 0) { PoseSmoking(); return; }
            if (propMode == "dispatcher" && actionTime >= 0) { PoseCompass(); return; }
            if ((propMode == "engineer" || propMode == "journalist") && actionTime >= 0) { PosePersonalInspection(); return; }
            if (actionTime >= 0) return;
            bool pack = propMode == "operator";
            const float shoulder = -12, elbow = -35;
            if (!pack)
            {
                if (rightArm != null) rightArm.localRotation *= Quaternion.Euler(shoulder, 0, 0);
                if (rightForearm != null) rightForearm.localRotation *= Quaternion.Euler(elbow, 0, 0);
            }
            if (pack)
            {
                if (leftArm != null) leftArm.localRotation *= Quaternion.Euler(shoulder, 0, 0);
                if (leftForearm != null) leftForearm.localRotation *= Quaternion.Euler(elbow, 0, 0);
            }
            if (propMode == "flashlight" && rightHand != null)
                rightHand.rotation = Quaternion.LookRotation(-transform.forward, -transform.up);
            if (propMode == "dispatcher") PoseCompass();
        }

        void PoseCompass()
        {
            if (rightHand == null) return;
            Quaternion rotation = Quaternion.LookRotation(-transform.up, transform.forward);
            if (actionTime >= 0 && rightArm != null)
            {
                float inspect = Mathf.Sin(Mathf.Clamp01(actionTime / PersonalActionDuration) * Mathf.PI);
                Vector3 contact = rightArm.position + transform.right * .10f - transform.up * (.20f - inspect * .05f) + transform.forward * .32f;
                FitSingleHandContact(ref contact, rotation);
                SolveHand(true, contact, rotation);
            }
            else rightHand.rotation = rotation;
            var compass = props["dispatcher"];
            compass.rotation = transform.rotation;
            compass.position = rightSocket.position - compass.rotation * new Vector3(.056f, .035f, 0);
            Vector3 north = compass.InverseTransformDirection(Vector3.forward);
            compassNeedle.localRotation = Quaternion.Euler(0, Mathf.Atan2(north.x, north.z) * Mathf.Rad2Deg, 0);
        }

        void PoseMap()
        {
            if (rightHand == null || leftHand == null) return;
            var map = props["map"];
            float shoulderHeight = (transform.InverseTransformPoint(rightArm.position).y + transform.InverseTransformPoint(leftArm.position).y) * .5f;
            // The engineer's broad chest and tie need the paper above the belly.
            // Other bodies can read the usual downward-tilted map at chest level.
            map.localPosition = new Vector3(0, shoulderHeight + (classId == "engineer" ? .04f : -.21f), .50f);
            map.localRotation = Quaternion.Euler(classId == "engineer" ? -10 : -35, 0, 0);
            Quaternion rightRotation = Quaternion.LookRotation(-map.forward, -map.right);
            Quaternion leftRotation = Quaternion.LookRotation(-map.forward, map.right);
            Vector3 origin = map.position;
            FitContactOrigin(ref origin, map.TransformVector(new Vector3(.22f, -.025f, 0)), rightRotation,
                map.TransformVector(new Vector3(-.22f, -.025f, 0)), leftRotation);
            map.position = origin;
            Vector3 rightContact = map.TransformPoint(new Vector3(.22f, -.025f, 0));
            Vector3 leftContact = map.TransformPoint(new Vector3(-.22f, -.025f, 0));
            // Fingers lie across the paper edge; both thumbs and palms stay on that edge.
            SolveHand(true, rightContact, rightRotation);
            SolveHand(false, leftContact, leftRotation);
        }

        void PoseCamera()
        {
            if (rightHand == null || leftHand == null) return;
            var camera = props["camera"];
            float shoulderHeight = (transform.InverseTransformPoint(rightArm.position).y + transform.InverseTransformPoint(leftArm.position).y) * .5f;
            camera.localPosition = new Vector3(0, shoulderHeight - (classId == "engineer" ? .035f : .07f), .50f);
            camera.localRotation = Quaternion.Euler(classId == "engineer" ? 25 : 0, 0, 0);
            Quaternion rightRotation = Quaternion.LookRotation(-camera.up, camera.forward);
            Quaternion leftRotation = rightRotation;
            Vector3 origin = camera.position;
            // Both palms support real bottom corners. This keeps the rear of the
            // camcorder clear of the supplied torsos while respecting arm reach.
            FitContactOrigin(ref origin, camera.TransformVector(new Vector3(.085f, .015f, .025f)), rightRotation,
                camera.TransformVector(new Vector3(-.085f, .015f, .025f)), leftRotation);
            camera.position = origin;
            SolveHand(true, camera.TransformPoint(new Vector3(.085f, .015f, .025f)), rightRotation);
            SolveHand(false, camera.TransformPoint(new Vector3(-.085f, .015f, .025f)), leftRotation);
        }

        void PosePersonalInspection()
        {
            if (rightHand == null || rightArm == null) return;
            float inspect = Mathf.Sin(Mathf.Clamp01(actionTime / PersonalActionDuration) * Mathf.PI);
            bool wrench = propMode == "engineer";
            Quaternion rotation = wrench ? Quaternion.LookRotation(-transform.up, transform.forward)
                : Quaternion.LookRotation(-transform.forward, -transform.up);
            rotation = Quaternion.AngleAxis(Mathf.Sin(actionTime * 3f) * inspect * 10f, transform.forward) * rotation;
            Vector3 contact = rightArm.position + transform.right * .10f - transform.up * (wrench ? .18f - inspect * .04f : .15f - inspect * .03f)
                + transform.forward * (wrench ? .35f + inspect * .03f : .30f);
            FitSingleHandContact(ref contact, rotation);
            SolveHand(true, contact, rotation);
        }

        void FitSingleHandContact(ref Vector3 contact, Quaternion rotation)
        {
            if (rightForearm == null || rightHand == null) return;
            Vector3 gripOffset = rotation * Vector3.Scale(rightGrip, rightHand.lossyScale);
            float reach = Vector3.Distance(rightArm.position, rightForearm.position) + Vector3.Distance(rightForearm.position, rightHand.position) - .002f;
            contact = rightArm.position + Vector3.ClampMagnitude(contact - gripOffset - rightArm.position, reach) + gripOffset;
        }

        void PoseSmoking()
        {
            if (rightHand == null || leftHand == null || cigarette == null) return;
            float inhale = Mathf.SmoothStep(0, 1, Mathf.Clamp01((actionTime - 3.0f) / .7f)) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((actionTime - 4.65f) / .7f)));
            Vector3 contact = rightSocket.position;
            Quaternion rightRotation = rightHand.rotation;
            Quaternion leftRotation = Quaternion.LookRotation(-transform.forward, -transform.up);
            if (head != null && inhale > 0)
            {
                Vector3 mouth = head.TransformPoint(mouthLocal);
                contact = Vector3.Lerp(contact, mouth + transform.forward * .035f, inhale);
            }
            // During lighting both hands must reach the same cigarette/lighter
            // assembly. Bring the assembly into their common reach before IK.
            if (lighter.gameObject.activeSelf)
                FitContactOrigin(ref contact, Vector3.zero, rightRotation,
                    transform.forward * .0385f - transform.up * .067f, leftRotation);
            if (inhale > 0 || lighter.gameObject.activeSelf) SolveHand(true, contact, rightRotation);
            cigarette.rotation = Quaternion.FromToRotation(Vector3.up, transform.forward);
            cigarette.position = contact - transform.forward * .035f;
            if (lighter.gameObject.activeSelf)
            {
                lighter.rotation = transform.rotation;
                lighter.position = cigarette.TransformPoint(new Vector3(0, .08f, 0)) -
                    lighter.rotation * new Vector3(.045f, .107f, 0);
                SolveHand(false, lighter.TransformPoint(new Vector3(.045f, .04f, -.0065f)), leftRotation);
            }
            for (int i = 0; i < smoke.Count; i++)
                if (smoke[i].gameObject.activeSelf)
                {
                    float age = Mathf.Repeat((actionTime - 3) * .6f + i / (float)smoke.Count, 1);
                    smoke[i].position = cigarette.TransformPoint(new Vector3(0, .082f, 0)) +
                        new Vector3(Mathf.Sin(age * 9 + i) * age * .025f, age * .22f, age * .04f);
                }
        }

        bool FitContactOrigin(ref Vector3 origin, Vector3 rightContactOffset, Quaternion rightRotation,
            Vector3 leftContactOffset, Quaternion leftRotation)
        {
            if (rightArm == null || leftArm == null || rightForearm == null || leftForearm == null || rightHand == null || leftHand == null) return false;
            // A fixed finger contact and hand rotation turn each wrist's reach
            // into a sphere for the common prop origin. Only the prop moves.
            Vector3 a = rightArm.position - rightContactOffset + rightRotation * Vector3.Scale(rightGrip, rightHand.lossyScale);
            Vector3 b = leftArm.position - leftContactOffset + leftRotation * Vector3.Scale(leftGrip, leftHand.lossyScale);
            float ra = Vector3.Distance(rightArm.position, rightForearm.position) + Vector3.Distance(rightForearm.position, rightHand.position) - .002f;
            float rb = Vector3.Distance(leftArm.position, leftForearm.position) + Vector3.Distance(leftForearm.position, leftHand.position) - .002f;
            float distance = Vector3.Distance(a, b);
            if (ra <= 0 || rb <= 0 || distance > ra + rb) return false;
            if (Inside(origin, a, ra) && Inside(origin, b, rb)) return true;
            Vector3 onA = a + Vector3.ClampMagnitude(origin - a, ra);
            if (Inside(onA, b, rb)) { origin = onA; return true; }
            Vector3 onB = b + Vector3.ClampMagnitude(origin - b, rb);
            if (Inside(onB, a, ra)) { origin = onB; return true; }
            if (distance < .00001f) { origin = a + Vector3.ClampMagnitude(origin - a, Mathf.Min(ra, rb)); return true; }
            // Both limits are active: the closest feasible point lies on the
            // circle where the two reach spheres meet.
            Vector3 axis = (b - a) / distance;
            float along = (ra * ra - rb * rb + distance * distance) / (2 * distance);
            Vector3 centre = a + axis * along;
            float radius = Mathf.Sqrt(Mathf.Max(0, ra * ra - along * along));
            Vector3 radial = origin - centre;
            radial -= axis * Vector3.Dot(radial, axis);
            if (radial.sqrMagnitude < .000001f) radial = Vector3.Cross(axis, transform.up);
            if (radial.sqrMagnitude < .000001f) radial = Vector3.Cross(axis, transform.forward);
            origin = centre + radial.normalized * radius;
            return true;
        }

        static bool Inside(Vector3 point, Vector3 centre, float radius) => Vector3.SqrMagnitude(point - centre) <= radius * radius + .0000001f;

        void SolveHand(bool right, Vector3 contact, Quaternion handRotation)
        {
            var upper = right ? rightArm : leftArm;
            var forearm = right ? rightForearm : leftForearm;
            var hand = right ? rightHand : leftHand;
            Vector3 grip = right ? rightGrip : leftGrip;
            if (upper == null || forearm == null || hand == null) return;
            Vector3 wristTarget = contact - handRotation * Vector3.Scale(grip, hand.lossyScale);
            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float forearmLength = Vector3.Distance(forearm.position, hand.position);
            Vector3 toTarget = wristTarget - shoulder;
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - forearmLength) + .001f,
                upperLength + forearmLength - .001f);
            Vector3 direction = toTarget.normalized;
            float along = (upperLength * upperLength - forearmLength * forearmLength + distance * distance) / (2 * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            float elbowDrop = propMode == "camera" && classId == "engineer" ? 1.25f : .25f;
            Vector3 hint = transform.right * (right ? 1 : -1) - transform.up * elbowDrop - transform.forward * .1f;
            hint -= direction * Vector3.Dot(hint, direction);
            if (hint.sqrMagnitude < .0001f) hint = Vector3.Cross(direction, transform.up);
            Vector3 elbow = shoulder + direction * along + hint.normalized * bend;
            wristTarget = shoulder + direction * distance;
            upper.rotation = Quaternion.FromToRotation(forearm.position - shoulder, elbow - shoulder) * upper.rotation;
            forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, wristTarget - forearm.position) * forearm.rotation;
            hand.rotation = handRotation;
            GripContactError = Mathf.Max(GripContactError, Vector3.Distance(hand.TransformPoint(grip), contact));
        }

        static void AnchorProp(Transform prop, Vector3 gripPoint, Vector3 angles = default)
        {
            prop.localRotation = Quaternion.Euler(angles);
            prop.localPosition = -(prop.localRotation * gripPoint);
        }

        static string InventoryMode(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "empty") return "empty";
            if (id == "flashlight" || id == "fuel-canister") return id;
            if (id.StartsWith("keycard", StringComparison.Ordinal) || id.StartsWith("key-", StringComparison.Ordinal)) return "keycard";
            if (id.StartsWith("fuse", StringComparison.Ordinal)) return "fuse";
            return "tape";
        }

        void CreateEquipment()
        {
            var shader = Shader.Find("Dead Air/Source Surface");
            if (shader == null) shader = Shader.Find("Standard");
            dark = Material(shader, "Worn polymer", new Color32(31, 37, 37, 255));
            metal = Material(shader, "Dull steel", new Color32(124, 137, 132, 255));
            brass = Material(shader, "Aged brass", new Color32(169, 126, 56, 255));
            paper = Material(shader, "Cream paper", new Color32(220, 209, 171, 255));
            red = Material(shader, "Red markings", new Color32(169, 49, 38, 255));
            glass = Material(shader, "Lamp glass", new Color32(241, 219, 156, 255));
            smokeMaterial = Material(shader, "Low poly smoke", new Color(.66f, .71f, .70f, .25f));
            smokeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            smokeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            smokeMaterial.SetFloat("_ZWrite", 0);
            smokeMaterial.renderQueue = 3000;
            boxMesh = Own(BoxMesh());
            tubeMesh = Own(TubeMesh());

            var g = Prop("flashlight", rightSocket);
            Part("Grip", g, tubeMesh, dark, Vector3.zero, new Vector3(.075f, .17f, .075f));
            Part("Lamp head", g, tubeMesh, metal, new Vector3(0, .11f, 0), new Vector3(.12f, .075f, .12f));
            Part("Lens", g, tubeMesh, glass, new Vector3(0, .152f, 0), new Vector3(.10f, .004f, .10f));
            Part("Switch", g, boxMesh, red, new Vector3(0, .01f, -.04f), new Vector3(.02f, .03f, .006f));
            AnchorProp(g, new Vector3(0, 0, -.0375f), new Vector3(-90, 0, 0));

            g = Prop("map", transform);
            Part("Folded station map", g, boxMesh, paper, Vector3.zero, new Vector3(.44f, .28f, .004f));
            for (int i = 0; i < 6; i++)
            {
                Part("Station corridor", g, boxMesh, dark, new Vector3(-.12f + i * .048f, .01f, -.003f), new Vector3(.008f, .18f - (i % 3) * .035f, .002f));
                Part("Station room", g, boxMesh, red, new Vector3(-.12f + i * .048f, -.02f + (i % 2) * .09f, -.003f), new Vector3(.026f, .015f, .002f));
            }

            g = Prop("camera", transform);
            Part("VHS-C body", g, boxMesh, dark, new Vector3(0, .08f, .025f), new Vector3(.17f, .13f, .24f));
            Part("Tape door", g, boxMesh, metal, new Vector3(-.088f, .08f, .025f), new Vector3(.005f, .10f, .15f));
            Part("Focus ring", g, tubeMesh, metal, new Vector3(0, .08f, .18f), new Vector3(.12f, .08f, .12f), new Vector3(90, 0, 0));
            Part("Lens glass", g, tubeMesh, glass, new Vector3(0, .08f, .225f), new Vector3(.095f, .004f, .095f), new Vector3(90, 0, 0));
            Part("Recording indicator", g, boxMesh, red, new Vector3(.066f, .12f, -.098f), new Vector3(.013f, .013f, .005f));
            Part("Video light bracket", g, boxMesh, metal, new Vector3(-.04f, .15f, .08f), new Vector3(.035f, .018f, .06f));
            Part("Mounted video light", g, boxMesh, dark, new Vector3(-.04f, .175f, .08f), new Vector3(.066f, .045f, .056f));
            Part("Video light reflector", g, tubeMesh, metal, new Vector3(-.04f, .175f, .113f), new Vector3(.054f, .007f, .054f), new Vector3(90, 0, 0));
            Part("Video light glass", g, tubeMesh, glass, new Vector3(-.04f, .175f, .118f), new Vector3(.044f, .003f, .044f), new Vector3(90, 0, 0));

            foreach (string mode in new[] { "tape", "keycard", "fuse", "fuel-canister" })
            {
                g = Prop(mode, rightSocket);
                Vector3 size = mode == "fuel-canister" ? new Vector3(.18f, .26f, .13f) : mode == "keycard" ? new Vector3(.09f, .06f, .004f)
                    : mode == "fuse" ? new Vector3(.025f, .13f, .025f) : new Vector3(.13f, .085f, .023f);
                Part(mode, g, boxMesh, mode == "fuel-canister" ? red : mode == "keycard" ? paper : dark, new Vector3(0, .035f, 0), size);
                Part("Label", g, boxMesh, paper, new Vector3(0, .035f, -size.z / 2 - .001f), new Vector3(size.x * .7f, size.y * .25f, .002f));
                if (mode == "fuel-canister")
                {
                    Part("Handle support left", g, boxMesh, metal, new Vector3(-.044f, .190f, 0), new Vector3(.018f, .055f, .026f));
                    Part("Handle support right", g, boxMesh, metal, new Vector3(.044f, .190f, 0), new Vector3(.018f, .055f, .026f));
                    Part("Carry handle", g, boxMesh, metal, new Vector3(0, .222f, 0), new Vector3(.105f, .025f, .026f));
                    AnchorProp(g, new Vector3(0, .222f, -.013f), new Vector3(180, 0, 0));
                }
                else AnchorProp(g, new Vector3(0, .035f, -size.z / 2), new Vector3(180, 0, 0));
            }

            if (classId == "operator") CreatePack();
            else if (classId == "engineer")
            {
                g = Prop(classId, rightSocket);
                Part("Wrench handle", g, boxMesh, metal, new Vector3(0, .085f, 0), new Vector3(.027f, .25f, .015f));
                Part("Wrench shoulder", g, boxMesh, metal, new Vector3(0, .215f, 0), new Vector3(.07f, .04f, .018f));
                Part("Open jaw left", g, boxMesh, metal, new Vector3(-.027f, .25f, 0), new Vector3(.020f, .055f, .019f));
                Part("Open jaw right", g, boxMesh, metal, new Vector3(.027f, .25f, 0), new Vector3(.020f, .055f, .019f));
                AnchorProp(g, new Vector3(0, .035f, -.0075f));
            }
            else if (classId == "journalist")
            {
                g = Prop(classId, rightSocket);
                Part("Cassette recorder", g, boxMesh, dark, new Vector3(0, .065f, 0), new Vector3(.075f, .15f, .033f));
                Part("Tape window", g, boxMesh, glass, new Vector3(0, .075f, -.018f), new Vector3(.052f, .038f, .003f));
                Part("REC button", g, boxMesh, red, new Vector3(.015f, .015f, -.019f), new Vector3(.018f, .012f, .004f));
                for (int i = 0; i < 4; i++) Part("Speaker grille", g, boxMesh, metal, new Vector3(0, .112f + i * .008f, -.018f), new Vector3(.052f, .002f, .002f));
                AnchorProp(g, new Vector3(0, .035f, -.0165f), new Vector3(180, 0, 0));
            }
            else
            {
                g = Prop(classId, rightSocket);
                Part("Compass case", g, tubeMesh, brass, new Vector3(0, .035f, 0), new Vector3(.115f, .022f, .115f));
                Part("Compass dial", g, tubeMesh, paper, new Vector3(0, .048f, 0), new Vector3(.096f, .003f, .096f));
                compassNeedle = Group("North needle", g);
                compassNeedle.localPosition = new Vector3(0, .052f, 0);
                Part("Needle north", compassNeedle, boxMesh, red, new Vector3(0, 0, .019f), new Vector3(.009f, .003f, .038f));
                Part("Needle south", compassNeedle, boxMesh, dark, new Vector3(0, 0, -.019f), new Vector3(.009f, .003f, .038f));
                pendulum = Group("Pendulum", g);
                Part("Pendulum chain", pendulum, tubeMesh, brass, new Vector3(0, -.095f, 0), new Vector3(.006f, .19f, .006f));
                Part("Pendulum weight", pendulum, tubeMesh, brass, new Vector3(0, -.205f, 0), new Vector3(.042f, .038f, .042f));
            }
            foreach (var prop in props) prop.Value.gameObject.SetActive(false);
        }

        void CreatePack()
        {
            var pack = Prop("operator", leftSocket);
            Part("Cigarette packet", pack, boxMesh, paper, new Vector3(0, .02f, 0), new Vector3(.065f, .087f, .029f));
            Part("Red packet band", pack, boxMesh, red, new Vector3(0, -.005f, -.015f), new Vector3(.066f, .022f, .002f));
            packLid = Group("Flip top", pack); packLid.localPosition = new Vector3(0, .066f, .015f);
            Part("Packet lid", packLid, boxMesh, paper, new Vector3(0, .01f, -.015f), new Vector3(.066f, .021f, .03f));
            for (int i = 0; i < 6; i++) packedCigarettes.Add(Part("Packed cigarette", pack, tubeMesh, paper, new Vector3((i % 3 - 1) * .017f, .043f, (i / 3 - .5f) * .012f), new Vector3(.008f, .04f, .008f)));
            AnchorProp(pack, new Vector3(0, .02f, -.0145f), new Vector3(180, 0, 0));
            cigarette = Group("Drawn cigarette", transform);
            Part("Paper cigarette", cigarette, tubeMesh, paper, new Vector3(0, .045f, 0), new Vector3(.008f, .065f, .008f));
            Part("Filter", cigarette, tubeMesh, brass, new Vector3(0, .008f, 0), new Vector3(.008f, .020f, .008f));
            ember = Part("Burning tip", cigarette, tubeMesh, red, new Vector3(0, .08f, 0), new Vector3(.009f, .004f, .009f));
            lighter = Group("Cigarette lighter", transform);
            Part("Lighter body", lighter, boxMesh, red, new Vector3(.045f, .035f, 0), new Vector3(.024f, .06f, .013f));
            Part("Lighter cap", lighter, boxMesh, metal, new Vector3(.045f, .072f, 0), new Vector3(.024f, .018f, .014f));
            flame = Part("Lighter flame", lighter, tubeMesh, glass, new Vector3(.045f, .094f, 0), new Vector3(.009f, .026f, .008f));
            for (int i = 0; i < 6; i++) smoke.Add(Part("Smoke puff", cigarette, tubeMesh, smokeMaterial, Vector3.zero, Vector3.one * .01f));
            cigarette.gameObject.SetActive(false);
            lighter.gameObject.SetActive(false);
        }

        Transform Prop(string id, Transform socket) { var g = Group("Held " + id, socket); props.Add(id, g); return g; }
        static Transform Group(string name, Transform parent)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            g.gameObject.layer = parent.gameObject.layer;
            return g;
        }
        static Transform Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale, Vector3 angles = default)
        {
            var g = Group(name, parent);
            g.localPosition = position; g.localScale = scale; g.localRotation = Quaternion.Euler(angles);
            g.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = g.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return g;
        }
        Material Material(Shader shader, string name, Color color)
        {
            var material = Own(new Material(shader) { name = name, color = color, enableInstancing = true });
            material.SetFloat("_Glossiness", .08f);
            return material;
        }
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        static Mesh BoxMesh()
        {
            Vector3[] v = { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            return FlatMesh(v, new[] { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,4,7,0,7,3, 1,2,6,1,6,5, 3,7,6,3,6,2, 0,1,5,0,5,4 });
        }
        static Mesh TubeMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int sides = 8;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                vertices.Add(new Vector3(Mathf.Cos(a) * .5f, -.5f, Mathf.Sin(a) * .5f));
                vertices.Add(new Vector3(Mathf.Cos(a) * .5f, .5f, Mathf.Sin(a) * .5f));
            }
            vertices.Add(new Vector3(0,-.5f,0)); vertices.Add(new Vector3(0,.5f,0));
            for (int i = 0; i < sides; i++)
            {
                int b = i * 2, n = (i + 1) % sides * 2;
                triangles.AddRange(new[] { b,b+1,n+1,b,n+1,n, 16,b,n, 17,n+1,b+1 });
            }
            return FlatMesh(vertices.ToArray(), triangles.ToArray());
        }
        static Mesh FlatMesh(Vector3[] source, int[] indices)
        {
            var vertices = new Vector3[indices.Length];
            var triangles = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++) { vertices[i] = source[indices[i]]; triangles[i] = i; }
            var mesh = new Mesh { name = "Faceted world equipment", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        void OnDestroy()
        {
            foreach (var resource in owned)
                if (resource != null) { if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource); }
            owned.Clear();
        }
    }
}
