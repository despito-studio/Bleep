using System;
using UnityEngine;

namespace DeadAir
{
    /// <summary>World-space foot contacts layered over the authored, in-place animation.</summary>
    public sealed class DeadAirCharacterLocomotion
    {
        sealed class Leg
        {
            public Transform thigh, shin, foot, toe, contact, surface;
            public Vector3 neutral, soleLocal, plant, surfacePoint, swingFrom, swingTo, normal = Vector3.up;
            public Vector3 toeSoleDelta;
            public Quaternion restRotation;
            public Quaternion plantedRotation;
            public Quaternion orientation;
            public Quaternion toeRest;
            public float upper, lower, phase;
            public float settleAge;
            public bool planted, settling;
        }

        readonly Transform root, pelvis, chest;
        readonly Leg left, right;
        readonly RaycastHit[] hits = new RaycastHit[24];
        Vector3 previousRoot, velocity, acceleration, filteredAcceleration;
        float previousYaw, phase, frequency, crouchWeight, pelvisOffset, turnSpeed, dt;
        bool initialized, suspended, firstPerson, running;
        int stepSerial, appliedSerial = -1;
        public float Phase => phase;
        public float Cadence => frequency * 2;
        public float CrouchWeight => crouchWeight;
        public Vector3 Velocity => velocity;
        public Vector3 LeftContact => left == null ? root.position : left.plant;
        public Vector3 RightContact => right == null ? root.position : right.plant;
        public float ContactError { get; private set; }
        public float PlantedSlip { get; private set; }
        public bool Ready => left != null && right != null && pelvis != null;

        public DeadAirCharacterLocomotion(Transform owner, Transform hips, Transform torso, Transform[] bones)
        {
            root = owner; pelvis = hips; chest = torso;
            left = MakeLeg(bones, "L"); right = MakeLeg(bones, "R");
        }

        Transform Find(Transform[] bones, string name)
        {
            foreach (var bone in bones) if (bone.name == name) return bone;
            return null;
        }

        Leg MakeLeg(Transform[] bones, string side)
        {
            var thigh = Find(bones, "thigh." + side);
            var shin = Find(bones, "shin." + side);
            var foot = Find(bones, "foot." + side);
            if (thigh == null || shin == null || foot == null) return null;
            var marker = Find(bones, "FootContact." + side);
            var toe = Find(bones, "toe." + side);
            var toeContact = Find(bones, "FootToeContact." + side);
            Vector3 sole = marker == null ? new Vector3(foot.position.x, root.position.y, foot.position.z) : marker.position;
            return new Leg
            {
                thigh = thigh, shin = shin, foot = foot, toe = toe, contact = marker,
                toeRest = toe == null ? Quaternion.identity : toe.localRotation,
                neutral = root.InverseTransformPoint(sole), soleLocal = foot.InverseTransformPoint(sole),
                toeSoleDelta = toeContact == null ? foot.InverseTransformVector(root.forward * .14f) :
                    (foot.InverseTransformPoint(toeContact.position) - foot.InverseTransformPoint(sole)) * 1.16f,
                restRotation = Quaternion.Inverse(root.rotation) * foot.rotation,
                upper = Vector3.Distance(thigh.position, shin.position), lower = Vector3.Distance(shin.position, foot.position)
            };
        }

        public void ResetContacts()
        {
            initialized = false; frequency = pelvisOffset = turnSpeed = 0;
            velocity = filteredAcceleration = acceleration = Vector3.zero;
        }

        /// <summary>Advance once per presentation frame. Apply may safely run again after animation evaluation.</summary>
        public void Step(Vector3 worldVelocity, bool crouching, bool isRunning, bool isFirstPerson, float deltaTime,
            bool reset, bool suspend)
        {
            if (!Ready) return;
            stepSerial++;
            dt = Mathf.Clamp(Finite(deltaTime) ? deltaTime : 0, 0, .1f);
            running = isRunning; firstPerson = isFirstPerson; suspended = suspend;
            worldVelocity.y = 0;
            if (!Finite(worldVelocity.x) || !Finite(worldVelocity.z)) worldVelocity = Vector3.zero;
            worldVelocity = Vector3.ClampMagnitude(worldVelocity, 8);
            left.upper = Vector3.Distance(left.thigh.position, left.shin.position);
            left.lower = Vector3.Distance(left.shin.position, left.foot.position);
            right.upper = Vector3.Distance(right.thigh.position, right.shin.position);
            right.lower = Vector3.Distance(right.shin.position, right.foot.position);
            bool movedFar = initialized && (root.position - previousRoot).sqrMagnitude > 4;
            if (!initialized || reset || movedFar)
            {
                initialized = true; phase = 0; velocity = worldVelocity;
                crouchWeight = crouching ? 1 : 0; filteredAcceleration = acceleration = Vector3.zero;
                previousRoot = root.position; previousYaw = root.eulerAngles.y;
                left.orientation = root.rotation * left.restRotation;
                right.orientation = root.rotation * right.restRotation;
                Plant(left, root.TransformPoint(left.neutral)); Plant(right, root.TransformPoint(right.neutral));
                left.phase = 0; right.phase = .5f; pelvisOffset = turnSpeed = 0;
            }
            if (suspended)
            {
                // Ladder, seated transit and a fallen body own their own foot pose.
                initialized = false; return;
            }
            if (dt <= 0) return;
            Vector3 oldVelocity = velocity;
            velocity = Vector3.Lerp(velocity, worldVelocity, 1 - Mathf.Exp(-14 * dt));
            acceleration = Vector3.ClampMagnitude((velocity - oldVelocity) / dt, 12);
            filteredAcceleration = Vector3.Lerp(filteredAcceleration, acceleration, 1 - Mathf.Exp(-7 * dt));
            turnSpeed = Mathf.DeltaAngle(previousYaw, root.eulerAngles.y) / dt;
            previousYaw = root.eulerAngles.y; previousRoot = root.position;
            crouchWeight = Mathf.MoveTowards(crouchWeight, crouching ? 1 : 0, dt / .18f);
            float speed = velocity.magnitude;
            float legLength = (left.upper + left.lower + right.upper + right.lower) * .5f;
            float stride = Mathf.Max(.35f, legLength * (crouching ? 1.05f : isRunning ? 2.05f : 1.7f));
            float targetFrequency = speed > .08f ? Mathf.Clamp(speed / stride, .45f, crouching ? 1.8f : 3.2f) : 0;
            bool turning = speed < .15f && Mathf.Abs(turnSpeed) > 25;
            if (turning) targetFrequency = Mathf.Clamp(Mathf.Abs(turnSpeed) / 150, .65f, 1.5f);
            frequency = Mathf.Lerp(frequency, targetFrequency, 1 - Mathf.Exp(-14 * dt));
            bool stepping = speed > .08f || turning;
            if (stepping) phase = Mathf.Repeat(phase + frequency * dt, 1);
            AdvanceLeg(left, phase, stepping, turning);
            AdvanceLeg(right, Mathf.Repeat(phase + .5f, 1), stepping, turning);
        }

        void AdvanceLeg(Leg leg, float nextPhase, bool stepping, bool turning)
        {
            float stance = running ? .53f : Mathf.Lerp(.61f, .69f, crouchWeight);
            bool wrapped = nextPhase < leg.phase;
            if (leg.planted && leg.surface != null) leg.plant = leg.surface.TransformPoint(leg.surfacePoint);
            Vector3 neutral = root.TransformPoint(leg.neutral);
            neutral.y = root.position.y + leg.neutral.y * root.lossyScale.y;
            if (leg.settling)
            {
                leg.orientation = Quaternion.RotateTowards(leg.orientation, root.rotation * leg.restRotation, 360 * dt);
                AdvanceSettle(leg); leg.phase = nextPhase; return;
            }
            if (!stepping)
            {
                // Finish a lifted foot on stopping instead of snapping it to the floor.
                if (!leg.planted || Vector3.ProjectOnPlane(leg.plant - neutral, root.up).sqrMagnitude > .13f)
                { BeginSettle(leg, neutral); AdvanceSettle(leg); }
                leg.phase = nextPhase; return;
            }
            if (wrapped)
            {
                Plant(leg, leg.swingTo);
            }
            if (nextPhase >= stance && leg.planted)
            {
                leg.planted = false; leg.surface = null; leg.swingFrom = leg.plant;
                float remaining = (1 - stance) / Mathf.Max(.45f, frequency);
                // Place the foot relative to the pelvis at touchdown. Clamping
                // the future root travel as if it were a leg extension leaves
                // fast steps behind the body before the foot has even landed.
                Vector3 placement = velocity * (stance / Mathf.Max(.45f, frequency) * .45f);
                placement = Vector3.ClampMagnitude(placement, (leg.upper + leg.lower) * .48f);
                leg.swingTo = neutral + velocity * remaining + placement;
                if (turning) leg.swingTo = neutral;
                Support(leg, leg.swingTo, out leg.swingTo, out leg.normal, out _);
            }
            if (!leg.planted)
            {
                // A planted foot keeps its yaw through toe-off. Catch up during
                // the swing at a bounded rate, instead of snapping to body yaw.
                leg.orientation = Quaternion.RotateTowards(leg.orientation, root.rotation * leg.restRotation, 360 * dt);
                float t = Mathf.Clamp01((nextPhase - stance) / (1 - stance));
                float ease = t * t * (3 - 2 * t);
                float clearance = Mathf.Lerp(running ? .17f : .115f, .075f, crouchWeight);
                clearance += Mathf.Max(0, leg.swingTo.y - leg.swingFrom.y) * .35f;
                leg.plant = Vector3.Lerp(leg.swingFrom, leg.swingTo, ease) + root.up * (Mathf.Sin(t * Mathf.PI) * clearance);
            }
            // Recovery after a collision/root correction avoids a hyperextended locked leg.
            if (leg.planted && Vector3.ProjectOnPlane(leg.plant - leg.thigh.position, root.up).magnitude >
                (leg.upper + leg.lower) * .85f) BeginSettle(leg, neutral);
            leg.phase = nextPhase;
        }

        void BeginSettle(Leg leg, Vector3 target)
        {
            leg.settling = true; leg.settleAge = 0; leg.planted = false; leg.surface = null;
            leg.swingFrom = leg.plant;
            Support(leg, target, out leg.swingTo, out leg.normal, out _);
        }

        void AdvanceSettle(Leg leg)
        {
            leg.settleAge += dt;
            float t = Mathf.Clamp01(leg.settleAge / .18f);
            leg.plant = Vector3.Lerp(leg.swingFrom, leg.swingTo, t * t * (3 - 2 * t)) + root.up * (Mathf.Sin(t * Mathf.PI) * .04f);
            if (t >= 1) Plant(leg, leg.swingTo);
        }

        void Plant(Leg leg, Vector3 point)
        {
            Support(leg, point, out leg.plant, out leg.normal, out leg.surface);
            leg.plantedRotation = leg.orientation;
            leg.surfacePoint = leg.surface == null ? Vector3.zero : leg.surface.InverseTransformPoint(leg.plant);
            leg.planted = true; leg.settling = false; leg.swingFrom = leg.swingTo = leg.plant;
        }

        bool Probe(Vector3 point, out Vector3 ground, out Vector3 normal, out Transform surface)
        {
            Vector3 up = root.up;
            Vector3 origin = point + up * .55f;
            int count = Physics.RaycastNonAlloc(origin, -up, hits, 1.15f, ~0, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            ground = point; normal = up; surface = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i]; var collider = hit.collider;
                if (collider == null || collider.transform.IsChildOf(root) || collider is CharacterController ||
                    collider.GetComponentInParent<DeadAirCharacterMotion>() != null ||
                    collider.GetComponentInParent<DeadAirPlayer>() != null || Vector3.Dot(hit.normal, up) < .55f) continue;
                if (hit.distance >= closest) continue;
                closest = hit.distance; ground = hit.point; normal = hit.normal; surface = collider.transform;
            }
            // An isolated preview has no scene collision. Root height is its deterministic floor.
            if (surface == null) ground += up * Vector3.Dot(root.position - ground, up);
            return surface != null;
        }

        void Support(Leg leg, Vector3 point, out Vector3 ground, out Vector3 normal, out Transform surface)
        {
            Probe(point, out ground, out normal, out surface);
            Quaternion orientation = Quaternion.FromToRotation(root.up, normal) * leg.orientation;
            Vector3 toe = orientation * Vector3.Scale(leg.toeSoleDelta, leg.foot.lossyScale);
            // The sole is an area. A toe or heel can already overlap the next
            // tread while its centre is still above the lower floor.
            for (int end = -1; end <= 1; end += 2)
            {
                Vector3 sample = ground + toe * end;
                if (!Probe(sample, out Vector3 support, out _, out Transform supportSurface)) continue;
                float rise = Vector3.Dot(support - sample, normal);
                if (rise <= .001f || rise > .30f) continue;
                ground += normal * rise;
                surface = supportSurface;
            }
        }

        public void Apply()
        {
            ContactError = PlantedSlip = 0;
            if (!Ready || !initialized || suspended) return;
            // Reach is calculated from each sampled hip; pelvis lowering keeps both leg solves feasible.
            float needed = Mathf.Max(RequiredPelvisDrop(left), RequiredPelvisDrop(right));
            float bob = velocity.magnitude > .1f ? Mathf.Cos(phase * Mathf.PI * 4) * (running ? .015f : .009f) : 0;
            float targetOffset = -Mathf.Clamp(needed + .003f, 0, .24f) + Mathf.Min(0, bob) * (1 - crouchWeight * .65f);
            if (dt > 0 && appliedSerial != stepSerial)
                pelvisOffset = targetOffset < pelvisOffset ? targetOffset : Mathf.Lerp(pelvisOffset, targetOffset, 1 - Mathf.Exp(-18 * dt));
            appliedSerial = stepSerial;
            pelvis.position += root.up * pelvisOffset;
            if (!firstPerson && velocity.sqrMagnitude > .01f)
                pelvis.position += root.right * (Mathf.Sin(phase * Mathf.PI * 2) * Mathf.Lerp(.012f, .006f, crouchWeight));
            if (!firstPerson && chest != null)
            {
                Vector3 localAcceleration = root.InverseTransformDirection(filteredAcceleration);
                float lean = Mathf.Clamp(localAcceleration.z * .65f + (running ? velocity.magnitude * .6f : 0), -4, 6);
                float side = Mathf.Clamp(-localAcceleration.x * .6f - turnSpeed * .012f, -4, 4);
                chest.rotation = Quaternion.AngleAxis(side, root.forward) * Quaternion.AngleAxis(lean, root.right) * chest.rotation;
            }
            SolveLeg(left); SolveLeg(right);
        }

        float RequiredPelvisDrop(Leg leg)
        {
            Quaternion rotation = GroundRotation(leg);
            Vector3 ankle = leg.plant - rotation * Vector3.Scale(leg.soleLocal, leg.foot.lossyScale);
            Vector3 offset = leg.thigh.position - ankle;
            float horizontal = Vector3.ProjectOnPlane(offset, root.up).magnitude;
            float length = (Vector3.Distance(leg.thigh.position, leg.shin.position) + Vector3.Distance(leg.shin.position, leg.foot.position)) * .985f;
            float height = Mathf.Sqrt(Mathf.Max(.015f, length * length - horizontal * horizontal));
            return Mathf.Max(0, Vector3.Dot(offset, root.up) - height);
        }

        Quaternion GroundRotation(Leg leg)
        {
            Vector3 normal = leg.normal.sqrMagnitude > .1f ? leg.normal.normalized : root.up;
            float tilt = Vector3.Angle(root.up, normal);
            if (tilt > 32) normal = Vector3.Slerp(root.up, normal, 32 / tilt);
            // Feet keep the authored orientation on the ground; swing adds a modest toe lift.
            Quaternion rotation = Quaternion.FromToRotation(root.up, normal) *
                (leg.planted ? leg.plantedRotation : leg.orientation);
            if (!leg.planted && !leg.settling)
            {
                float stance = running ? .53f : Mathf.Lerp(.61f, .69f, crouchWeight);
                float swing = Mathf.Clamp01((leg.phase - stance) / (1 - stance));
                rotation = Quaternion.AngleAxis(-Mathf.Sin(swing * Mathf.PI) * 8, root.right) * rotation;
            }
            return rotation;
        }

        void SolveLeg(Leg leg)
        {
            Quaternion rotation = GroundRotation(leg);
            Vector3 target = leg.plant - rotation * Vector3.Scale(leg.soleLocal, leg.foot.lossyScale);
            Vector3 origin = leg.thigh.position, offset = target - origin;
            float a = Vector3.Distance(origin, leg.shin.position), b = Vector3.Distance(leg.shin.position, leg.foot.position);
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + .002f, a + b - .002f);
            if (distance < .01f || offset.sqrMagnitude < .000001f) return;
            Vector3 direction = offset.normalized;
            float along = (a * a - b * b + distance * distance) / (2 * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            float side = Mathf.Sign(leg.neutral.x);
            if (side == 0) side = Mathf.Sign(root.InverseTransformPoint(leg.thigh.position).x);
            Vector3 hint = root.forward + root.right * (side * .08f);
            hint -= direction * Vector3.Dot(hint, direction);
            if (hint.sqrMagnitude < .001f) hint = root.right;
            Vector3 knee = origin + direction * along + hint.normalized * bend;
            Vector3 reachable = origin + direction * distance;
            leg.thigh.rotation = Quaternion.FromToRotation(leg.shin.position - origin, knee - origin) * leg.thigh.rotation;
            leg.shin.rotation = Quaternion.FromToRotation(leg.foot.position - leg.shin.position, reachable - leg.shin.position) * leg.shin.rotation;
            leg.foot.rotation = rotation;
            // Authored toe-off belongs to the swing. A grounded toe cannot rotate
            // below its support plane after the ankle has been aligned by IK.
            if (leg.toe != null && leg.planted) leg.toe.localRotation = leg.toeRest;
            Vector3 solved = leg.foot.TransformPoint(leg.soleLocal);
            float error = Vector3.Distance(solved, leg.plant);
            ContactError = Mathf.Max(ContactError, error);
            if (leg.planted) PlantedSlip = Mathf.Max(PlantedSlip, Vector3.ProjectOnPlane(solved - leg.plant, root.up).magnitude);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
