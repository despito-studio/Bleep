using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>Snapshot-to-authored-action bridge. Missing authored states fail explicitly instead of using the old rig.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DeadAirCharacterAnimator))]
    public sealed class DeadAirCharacterV2Presentation : MonoBehaviour
    {
        [Serializable] public sealed class Gait
        {
            public string action;
            [Tooltip("Distance travelled over one complete authored cycle, measured for this character in metres.")]
            public float cycleDistance;
        }
        [Serializable] public sealed class Prop
        {
            public string id;
            [Tooltip("Inventory presentation mode, or this character's gameplay role for the personal item.")]
            public string mode;
            public Renderer[] renderers = Array.Empty<Renderer>();
            public bool visibleWhileHeld = true;
            public bool visibleDuringAction = true;
            [Range(0, 1)] public float actionStart;
            [Range(0, 1)] public float actionEnd = 1;
            [Tooltip("The authored hold pose follows view pitch using this rig's own arm lengths and calibrated aim pivot.")]
            public bool aimWithView;
        }
        [Serializable] public sealed class ViewPitchProfile
        {
            public Vector3 chestLocalAxis;
            public Vector3 headLocalAxis;
            [Range(0, 1)] public float chestShare;
            public float chestDownLimit, chestUpLimit, headDownLimit, headUpLimit;
            [Tooltip("An explicit chest child on this rig's shoulder/aim line; not a guessed global pivot.")]
            public Transform aimPivot;
            public Vector3 aimPivotLocalAxis;
            public Vector3 leftElbowPole, rightElbowPole;
            public float handContactTolerance;
        }
        struct ViewBonePose { public Transform bone; public Quaternion rotation; }

        static readonly string[] GaitActions = { "Walk", "Run", "WalkBack", "StrafeLeft", "StrafeRight", "CrouchWalk" };
        [SerializeField] Gait[] gaits = Array.Empty<Gait>();
        [SerializeField] Prop[] props = Array.Empty<Prop>();
        [SerializeField] Vector3 ownerHeadCenter;
        [SerializeField] Vector3 ownerHeadRadii;
        [SerializeField] Light flashlightEmitter;
        [SerializeField] ViewPitchProfile viewPitch;
        DeadAirCharacterRigBindings rig;
        DeadAirCharacterAnimator playback;
        DeadAirCharacterWardrobe wardrobe;
        readonly Dictionary<string, float> distances = new Dictionary<string, float>(StringComparer.Ordinal);
        MaterialPropertyBlock viewBlock;
        bool validated, hasSnapshot, hasExternalVelocity;
        bool externalCrouching;
        Vector3 lastPosition, externalVelocity;
        float lastElapsed, movedAt, sampledAt, speed;
        float lastAuthoritativeElapsed;
        double snapshotReceivedAt;
        bool clockPaused;
        public const float MaximumVisualExtrapolation = .15f;
        int lastSequence;
        string reportedError;
        readonly List<ViewBonePose> viewPose = new List<ViewBonePose>();
        bool ownerView;
        public bool FlashlightRequested { get; private set; }
        public float AppliedHeadPitch { get; private set; }
        public float AimContactError { get; private set; }
        public float PresentationTime => lastElapsed;
        public float SeatedWeight { get; set; }
        public float Phase { get; private set; }
        public float Cadence { get; private set; }
        public Vector3 Velocity { get; private set; }
        public string LastError { get; private set; }
        public Light FlashlightEmitter => flashlightEmitter;
        public bool OwnerView
        {
            get => ownerView;
            set { ownerView = value; if (value && flashlightEmitter != null) flashlightEmitter.enabled = false; }
        }

        public void Configure(Gait[] authoredGaits, Prop[] authoredProps, Vector3 headCenter, Vector3 headRadii,
            Light emitter = null, ViewPitchProfile authoredView = null)
        {
            gaits = authoredGaits ?? Array.Empty<Gait>(); props = authoredProps ?? Array.Empty<Prop>();
            ownerHeadCenter = headCenter; ownerHeadRadii = headRadii; flashlightEmitter = emitter;
            viewPitch = authoredView;
            validated = false; ResetPresentation();
        }

        bool Resolve()
        {
            if (rig == null) rig = GetComponent<DeadAirCharacterRigBindings>();
            if (playback == null) playback = GetComponent<DeadAirCharacterAnimator>();
            if (wardrobe == null) wardrobe = GetComponent<DeadAirCharacterWardrobe>();
            return rig != null && playback != null && wardrobe != null;
        }

        public bool ValidateProfile(out string error)
        {
            validated = false; error = null; distances.Clear();
            if (!Resolve() || !playback.ValidateConfiguration(out error)) { error = error ?? "Missing V2 rig/playback/wardrobe."; return false; }
            foreach (var gait in gaits)
            {
                if (gait == null || string.IsNullOrWhiteSpace(gait.action) || !Finite(gait.cycleDistance) || gait.cycleDistance <= 0 ||
                    distances.ContainsKey(gait.action) || !playback.TryGetClip(gait.action, out _))
                { error = "Every gait needs a unique authored clip and measured positive cycle distance."; return false; }
                distances.Add(gait.action, gait.cycleDistance);
            }
            foreach (string action in GaitActions)
                if (!distances.ContainsKey(action)) { error = "Missing measured cycle distance for " + action + "."; return false; }
            if (!Finite(ownerHeadCenter) || !Finite(ownerHeadRadii) || ownerHeadRadii.x <= 0 || ownerHeadRadii.y <= 0 || ownerHeadRadii.z <= 0)
            { error = "Measure the owner-view head ellipsoid on this character's own anatomy."; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var surfaces = new HashSet<Renderer>();
            foreach (var prop in props)
            {
                if (prop == null || string.IsNullOrWhiteSpace(prop.id) || !ids.Add(prop.id) || string.IsNullOrWhiteSpace(prop.mode) ||
                    prop.renderers == null || prop.renderers.Length == 0 || !Finite(prop.actionStart) || !Finite(prop.actionEnd) ||
                    prop.actionStart < 0 || prop.actionEnd > 1 || prop.actionStart > prop.actionEnd)
                { error = "Every new prop needs an explicit mode, unique ID, renderer bindings and valid action window."; return false; }
                foreach (var surface in prop.renderers)
                    if (surface == null || !surface.transform.IsChildOf(transform) || !surfaces.Add(surface) || wardrobe.OwnsSurface(surface))
                    { error = "Prop visibility cannot own a foreign renderer, body or garment."; return false; }
            }
            if (flashlightEmitter != null && !flashlightEmitter.transform.IsChildOf(transform))
            { error = "The flashlight light must belong to this character."; return false; }
            if (!ValidateViewPitch(out error)) return false;
            validated = true; return true;
        }

        bool ValidateViewPitch(out string error)
        {
            error = null;
            if (viewPitch == null || !UnitAxis(viewPitch.chestLocalAxis) || !UnitAxis(viewPitch.headLocalAxis) ||
                !UnitAxis(viewPitch.aimPivotLocalAxis) || !Finite(viewPitch.chestShare) || viewPitch.chestShare < 0 || viewPitch.chestShare > 1 ||
                !PositiveAngle(viewPitch.chestDownLimit) || !PositiveAngle(viewPitch.chestUpLimit) ||
                !PositiveAngle(viewPitch.headDownLimit) || !PositiveAngle(viewPitch.headUpLimit) ||
                viewPitch.chestDownLimit + viewPitch.headDownLimit < -DeadAirLocomotion.MaximumDownwardPitch * Mathf.Rad2Deg ||
                viewPitch.chestUpLimit + viewPitch.headUpLimit < 1.35f * Mathf.Rad2Deg ||
                !Finite(viewPitch.leftElbowPole) || !Finite(viewPitch.rightElbowPole) ||
                viewPitch.leftElbowPole.sqrMagnitude < .0001f || viewPitch.rightElbowPole.sqrMagnitude < .0001f ||
                !Finite(viewPitch.handContactTolerance) || viewPitch.handContactTolerance <= 0 || viewPitch.handContactTolerance > .01f)
            { error = "Supply this rig's calibrated pitch axes, full view range, elbow poles and hand-contact tolerance (at most 1 cm)."; return false; }
            rig.TryGet(DeadAirCharacterRigBindings.Joint.Chest, out Transform chest);
            if (viewPitch.aimPivot == null || viewPitch.aimPivot == chest || !viewPitch.aimPivot.IsChildOf(chest))
            { error = "The calibrated aim pivot must be an explicit child of this character's chest."; return false; }
            foreach (var pair in new[] {
                new[] { DeadAirCharacterRigBindings.Joint.UpperArmLeft, DeadAirCharacterRigBindings.Joint.ForearmLeft, DeadAirCharacterRigBindings.Joint.HandLeft },
                new[] { DeadAirCharacterRigBindings.Joint.UpperArmRight, DeadAirCharacterRigBindings.Joint.ForearmRight, DeadAirCharacterRigBindings.Joint.HandRight }
            })
            {
                rig.TryGet(pair[0], out Transform upper); rig.TryGet(pair[1], out Transform lower); rig.TryGet(pair[2], out Transform hand);
                if (Vector3.Distance(upper.position, lower.position) < .001f || Vector3.Distance(lower.position, hand.position) < .001f)
                { error = "Calibrated arm aiming requires this rig's nonzero upper-arm and forearm lengths."; return false; }
            }
            foreach (var prop in props)
                if ((prop.mode == "flashlight" || prop.mode == "camera") && !prop.aimWithView)
                { error = "Lamp/camera props must declare calibrated view aiming."; return false; }
            return true;
        }
        static bool UnitAxis(Vector3 axis) => Finite(axis) && Mathf.Abs(axis.sqrMagnitude - 1) < .001f;
        static bool PositiveAngle(float value) => Finite(value) && value > 0 && value <= 90;

        public void SetVelocity(bool crouching, Vector3 worldVelocity)
        {
            externalCrouching = crouching; externalVelocity = worldVelocity; externalVelocity.y = 0; hasExternalVelocity = true;
        }

        public bool Present(PlayerState player, float elapsed, bool paused, bool playing)
            => PresentAtWallTime(player, elapsed, paused, playing, Time.unscaledTimeAsDouble);

        /// <summary>Explicit wall time supports deterministic tests; gameplay still owns elapsed and every effect.</summary>
        public bool PresentAtWallTime(PlayerState player, float elapsed, bool paused, bool playing, double wallTime)
        {
            if (player == null || !Finite(elapsed) || !Finite(player.pitch) || double.IsNaN(wallTime) || double.IsInfinity(wallTime)) return false;
            if (!validated && !ValidateProfile(out string error)) return Fail(error);
            if (player.characterClass != rig.CharacterClass) return Fail("Snapshot role differs from the authored V2 character.");
            if (!wardrobe.Apply(player.clothingMask, rig.CharacterClass)) return Fail(wardrobe.LastError);
            Vector3 position = new Vector3(-player.x, player.y, player.z);
            bool reset = !hasSnapshot || elapsed < lastAuthoritativeElapsed || player.uniqueActionSequence < lastSequence ||
                (position - lastPosition).sqrMagnitude >= 9;
            float clock = VisualClock(elapsed, paused, wallTime, reset);
            float dt = paused || reset ? 0 : Mathf.Clamp(clock - lastElapsed, 0, .1f);
            if (reset)
            {
                Phase = 0; Velocity = Vector3.zero; speed = 0; movedAt = -100;
                lastPosition = position; sampledAt = elapsed;
                playback.ResetPresentation();
            }
            Vector3 displacement = position - lastPosition; displacement.y = 0;
            if (!paused && displacement.sqrMagnitude > .000001f)
            {
                Velocity = Vector3.ClampMagnitude(displacement / Mathf.Max(.016f, elapsed - sampledAt), 8);
                speed = Velocity.magnitude; lastPosition = position; sampledAt = movedAt = elapsed;
            }
            if (!paused && clock - movedAt > .22f) { speed = 0; Velocity = Vector3.zero; }
            bool crouching = player.crouching;
            if (hasExternalVelocity)
            {
                Velocity = Vector3.ClampMagnitude(externalVelocity, 8); speed = Velocity.magnitude; crouching = externalCrouching;
                hasExternalVelocity = false;
            }
            if (!player.ladderClimbing && SeatedWeight <= .001f)
                transform.rotation = Quaternion.Euler(0, DeadAirGaze.UnityYaw(player.yaw), 0);
            string action;
            float phase;
            float age = clock - player.uniqueActionAt;
            float duration = rig.CharacterClass == "operator" ? 7 : 2.5f;
            bool personal = !player.down && !player.ladderClimbing && !player.uniqueActionCancelled && player.uniqueItemEquipped &&
                !player.mapOpen && !player.camera && player.uniqueActionSequence > 0 && age >= 0 && age < duration;
            if (player.down) { action = "DownLoop"; phase = ClipPhase(action, clock); }
            else if (player.ladderClimbing)
            { action = player.ladderUp ? "LadderUp" : "LadderDown"; phase = Mathf.Clamp01((clock - player.ladderStartedAt) / DeadAirLadder.Duration); }
            else if (SeatedWeight > .001f) { action = "SitLoop"; phase = ClipPhase(action, clock); }
            else if (personal) { action = "UseItem"; phase = age / duration; }
            else
            {
                action = LocomotionAction(crouching, player.sprinting);
                if (distances.TryGetValue(action, out float distance))
                {
                    Cadence = speed / distance; Phase = Mathf.Repeat(Phase + dt * Cadence, 1); phase = Phase;
                }
                else { Cadence = 0; phase = ClipPhase(action, clock); }
            }
            lastElapsed = clock; lastAuthoritativeElapsed = elapsed; lastSequence = player.uniqueActionSequence; hasSnapshot = true;
            if (!playback.TryGetClip(action, out _)) return Fail("Author this character's '" + action + "' clip before enabling that gameplay state.");
            string mode = EquipmentMode(player);
            string hold = mode == "empty" || personal || player.down || player.ladderClimbing || SeatedWeight > .001f ? null : "Hold_" + mode;
            if (hold != null && (!playback.TryGetClip(hold, out _) || !HasPropMode(mode)))
                return Fail("Missing this character's new prop binding or authored '" + hold + "' clip.");
            if (personal && !HasPropMode(mode)) return Fail("The personal action needs this character's new prop bindings.");
            RestoreViewPose();
            if (!playback.PresentWithUpperBody(action, phase, hold, hold == null ? 0 : ClipPhase(hold, clock), 1,
                player.clothingMask, rig.CharacterClass)) return Fail(playback.LastError);
            // Down, ladder and precise personal contacts retain their authored view pose. Normal/held-item poses use the explicit profile.
            AppliedHeadPitch = AimContactError = 0;
            if (!player.down && !player.ladderClimbing && !personal && !ApplyViewPitch(player.pitch, mode, out string pitchError))
            { RestoreViewPose(); return Fail(pitchError); }
            // Only explicitly bound new props are toggled. Their authored socket/bone animation supplies placement.
            foreach (var prop in props)
            {
                bool visible = mode == prop.mode && (personal ? prop.visibleDuringAction && phase >= prop.actionStart && phase <= prop.actionEnd : prop.visibleWhileHeld);
                foreach (var surface in prop.renderers) surface.enabled = visible;
            }
            FlashlightRequested = playing && !player.down && !player.ladderClimbing && player.flashlight && DeadAirSupplies.HasCharge(player) &&
                !player.mapOpen && !player.uniqueItemEquipped && (mode == "flashlight" || mode == "camera");
            if (FlashlightRequested && flashlightEmitter == null) return Fail("The held lamp requires its own calibrated emitter socket.");
            if (flashlightEmitter != null) flashlightEmitter.enabled = !OwnerView && FlashlightRequested;
            LastError = reportedError = null;
            return true;
        }

        float VisualClock(float elapsed, bool paused, double wallTime, bool reset)
        {
            if (reset)
            {
                snapshotReceivedAt = wallTime; clockPaused = paused;
                return elapsed;
            }
            if (elapsed != lastAuthoritativeElapsed || wallTime < snapshotReceivedAt || clockPaused != paused)
                snapshotReceivedAt = wallTime;
            clockPaused = paused;
            if (paused) return lastElapsed;
            // Guests receive room time in snapshots. Sample between arrivals, but stop within a short bounded
            // horizon on packet loss; a delayed snapshot never rewinds an already presented pose.
            float betweenSnapshots = Mathf.Clamp((float)(wallTime - snapshotReceivedAt), 0, MaximumVisualExtrapolation);
            return Mathf.Max(lastElapsed, elapsed + betweenSnapshots);
        }

        string LocomotionAction(bool crouching, bool running)
        {
            if (speed <= .12f) return crouching ? "CrouchIdle" : "Idle";
            if (crouching) return "CrouchWalk";
            Vector3 local = transform.InverseTransformDirection(Velocity);
            if (Mathf.Abs(local.x) > Mathf.Abs(local.z)) return local.x < 0 ? "StrafeLeft" : "StrafeRight";
            if (local.z < 0) return "WalkBack";
            return running ? "Run" : "Walk";
        }
        float ClipPhase(string action, float clock) => playback.TryGetClip(action, out AnimationClip clip) ? clock / clip.length : 0;
        bool HasPropMode(string mode) { foreach (var prop in props) if (prop.mode == mode) return true; return false; }

        bool ApplyViewPitch(float radians, string mode, out string error)
        {
            error = null;
            float pitch = Mathf.Clamp(radians, DeadAirLocomotion.MaximumDownwardPitch, 1.35f) * Mathf.Rad2Deg;
            rig.TryGet(DeadAirCharacterRigBindings.Joint.Chest, out Transform chest);
            rig.TryGet(DeadAirCharacterRigBindings.Joint.Head, out Transform head);
            float chestAngle = Mathf.Clamp(pitch * viewPitch.chestShare, -viewPitch.chestDownLimit, viewPitch.chestUpLimit);
            float headAngle = Mathf.Clamp(pitch - chestAngle, -viewPitch.headDownLimit, viewPitch.headUpLimit);
            chestAngle = Mathf.Clamp(pitch - headAngle, -viewPitch.chestDownLimit, viewPitch.chestUpLimit);
            RememberViewPose(chest); RememberViewPose(head);
            chest.localRotation *= Quaternion.AngleAxis(chestAngle, viewPitch.chestLocalAxis);
            head.localRotation *= Quaternion.AngleAxis(headAngle, viewPitch.headLocalAxis);
            AppliedHeadPitch = chestAngle + headAngle;
            bool aim = false;
            foreach (var prop in props) if (prop.mode == mode && prop.aimWithView) aim = true;
            if (!aim) return true;
            Transform pivot = viewPitch.aimPivot;
            Quaternion delta = Quaternion.AngleAxis(pitch - chestAngle, pivot.TransformDirection(viewPitch.aimPivotLocalAxis));
            // The pivot is authored on this character's own shoulder line. Both targets undergo the same rigid rotation,
            // preserving two-handed grips; the solver uses actual bone lengths and never stretches the source anatomy.
            foreach (bool left in new[] { true, false })
            {
                rig.TryGet(left ? DeadAirCharacterRigBindings.Joint.UpperArmLeft : DeadAirCharacterRigBindings.Joint.UpperArmRight, out Transform upper);
                rig.TryGet(left ? DeadAirCharacterRigBindings.Joint.ForearmLeft : DeadAirCharacterRigBindings.Joint.ForearmRight, out Transform lower);
                rig.TryGet(left ? DeadAirCharacterRigBindings.Joint.HandLeft : DeadAirCharacterRigBindings.Joint.HandRight, out Transform hand);
                Vector3 target = pivot.position + delta * (hand.position - pivot.position);
                Quaternion rotation = delta * hand.rotation;
                Vector3 pole = pivot.position + delta * pivot.TransformVector(left ? viewPitch.leftElbowPole : viewPitch.rightElbowPole);
                RememberViewPose(upper); RememberViewPose(lower); RememberViewPose(hand);
                if (!SolveArm(upper, lower, hand, target, rotation, pole, out float contactError))
                { error = "The calibrated " + (left ? "left" : "right") + " aim target is unreachable or its elbow pole is degenerate; revise this character's profile/hold pose."; return false; }
                AimContactError = Mathf.Max(AimContactError, contactError);
                if (contactError > viewPitch.handContactTolerance)
                { error = "View aim exceeds this character's hand-contact tolerance: " + contactError.ToString("F4") + " m."; return false; }
            }
            return true;
        }

        static bool SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Quaternion rotation, Vector3 pole, out float error)
        {
            error = float.PositiveInfinity;
            Vector3 origin = upper.position;
            float firstLength = Vector3.Distance(origin, lower.position), secondLength = Vector3.Distance(lower.position, hand.position);
            Vector3 reach = target - origin; float distance = reach.magnitude;
            if (firstLength < .001f || secondLength < .001f || distance < .00001f ||
                distance > firstLength + secondLength + .00001f || distance < Mathf.Abs(firstLength - secondLength) - .00001f) return false;
            Vector3 forward = reach / distance;
            Vector3 bend = Vector3.ProjectOnPlane(pole - origin, forward);
            if (bend.sqrMagnitude < .0000001f) return false;
            bend.Normalize();
            float along = Mathf.Clamp((firstLength * firstLength + distance * distance - secondLength * secondLength) / (2 * distance), -firstLength, firstLength);
            Vector3 elbow = origin + forward * along + bend * Mathf.Sqrt(Mathf.Max(0, firstLength * firstLength - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - origin, elbow - origin) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
            hand.rotation = rotation;
            error = Vector3.Distance(hand.position, target);
            return Finite(error);
        }

        void RememberViewPose(Transform bone)
        {
            foreach (var entry in viewPose) if (entry.bone == bone) return;
            viewPose.Add(new ViewBonePose { bone = bone, rotation = bone.localRotation });
        }
        void RestoreViewPose()
        {
            foreach (var entry in viewPose) if (entry.bone != null) entry.bone.localRotation = entry.rotation;
            viewPose.Clear();
        }
        static string EquipmentMode(PlayerState player)
        {
            if (player.down || player.ladderClimbing || player.mapOpen || !string.IsNullOrEmpty(player.hiding)) return "empty";
            if (player.camera) return "camera";
            if (player.uniqueItemEquipped) return player.characterClass;
            string selected = player.SelectedItem;
            if (string.IsNullOrEmpty(selected) || selected == "empty") return "empty";
            if (selected.StartsWith("fuel-canister", StringComparison.Ordinal)) return "fuel-canister";
            if (selected.StartsWith("keycard", StringComparison.Ordinal) || selected.StartsWith("key-", StringComparison.Ordinal)) return "keycard";
            if (selected.StartsWith("fuse", StringComparison.Ordinal)) return "fuse";
            if (selected.StartsWith("tape", StringComparison.Ordinal)) return "tape";
            return selected;
        }

        public bool Preview(string action, float phase, int mask = -1)
        {
            if (!Resolve()) return Fail("Missing V2 bindings.");
            RestoreViewPose();
            return playback.Present(action, phase, mask, rig.CharacterClass);
        }
        public Vector3 Landmark(DeadAirCharacterRigBindings.Joint semantic)
            => Resolve() && rig.TryGet(semantic, out Transform point) ? point.position : transform.position;

        public bool SetOwnerCamera(Camera camera)
        {
            if (!validated && !ValidateProfile(out string error)) return Fail(error);
            if (!rig.TryGet(DeadAirCharacterRigBindings.Joint.Head, out Transform head)) return Fail("No explicit head binding.");
            if (viewBlock == null) viewBlock = new MaterialPropertyBlock();
            Vector3 eye = camera == null ? Vector3.zero : camera.transform.position;
            foreach (var surface in GetComponentsInChildren<Renderer>(true))
            {
                surface.GetPropertyBlock(viewBlock);
                viewBlock.SetVector("_DeadAirOwnerCamera", new Vector4(eye.x, eye.y, eye.z, camera == null ? 0 : 1));
                viewBlock.SetMatrix("_DeadAirOwnerHeadWorldToLocal", head.worldToLocalMatrix);
                viewBlock.SetVector("_DeadAirOwnerHeadCenter", ownerHeadCenter);
                viewBlock.SetVector("_DeadAirOwnerHeadRadii", ownerHeadRadii);
                surface.SetPropertyBlock(viewBlock);
            }
            return true;
        }

        public void ConfigureFlashlight(ClassBonuses bonuses)
        {
            if (flashlightEmitter == null || bonuses == null) return;
            flashlightEmitter.range = bonuses.flashlightDist; flashlightEmitter.spotAngle = bonuses.flashlightAngle * Mathf.Rad2Deg * 2;
        }
        /// <summary>The owner keeps the existing player Light reference, driven from the new authored socket after pose evaluation.</summary>
        public void PresentOwnerFlashlight(Light ownerLight)
        {
            if (ownerLight == null) return;
            if (flashlightEmitter != null && OwnerView) flashlightEmitter.enabled = false;
            ownerLight.enabled = OwnerView && FlashlightRequested && flashlightEmitter != null;
            if (!ownerLight.enabled) return;
            ownerLight.transform.SetPositionAndRotation(flashlightEmitter.transform.position, flashlightEmitter.transform.rotation);
            ownerLight.range = flashlightEmitter.range; ownerLight.spotAngle = flashlightEmitter.spotAngle;
            ownerLight.innerSpotAngle = flashlightEmitter.innerSpotAngle;
        }
        public void ResetPresentation()
        {
            RestoreViewPose(); FlashlightRequested = false; AppliedHeadPitch = AimContactError = 0;
            hasSnapshot = hasExternalVelocity = false; Phase = Cadence = speed = 0; Velocity = Vector3.zero;
            clockPaused = false; snapshotReceivedAt = 0; lastAuthoritativeElapsed = lastElapsed = 0;
            LastError = reportedError = null;
            if (Resolve()) playback.ResetPresentation();
            if (flashlightEmitter != null) flashlightEmitter.enabled = false;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        bool Fail(string error)
        {
            LastError = error;
            FlashlightRequested = false;
            if (validated)
                foreach (var prop in props) foreach (var surface in prop.renderers) if (surface != null) surface.enabled = false;
            if (flashlightEmitter != null) flashlightEmitter.enabled = false;
            if (reportedError != error) { Debug.LogWarning("V2 presentation on '" + name + "': " + error, this); reportedError = error; }
            return false;
        }
        void OnValidate() { validated = false; }
        void OnDisable() { ResetPresentation(); }
    }
}
