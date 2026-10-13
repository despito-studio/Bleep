using System;
using System.Collections.Generic;
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
            public Vector3[] soleFootprint;
            public SoleVertex[] soleVertices;
            public Quaternion restRotation;
            public Quaternion plantedRotation;
            public Quaternion orientation;
            public Quaternion toeRest;
            public float upper, lower, phase;
            public float settleAge;
            public bool planted, settling;
        }

        // Cached skinning samples, not per-frame mesh baking. The lower hull
        // is bounded to 32 samples per shoe, including the raised toe edge.
        sealed class SoleVertex
        {
            public readonly Transform[] bones = new Transform[4];
            public readonly Vector3[] positions = new Vector3[4];
            public readonly float[] weights = new float[4];
            public Vector3 initial;
            public Vector3 WorldPoint()
            {
                Vector3 point = Vector3.zero;
                for (int i = 0; i < 4; i++)
                    if (bones[i] != null && weights[i] > 0) point += bones[i].TransformPoint(positions[i]) * weights[i];
                return point;
            }
        }

        readonly Transform root, pelvis, chest;
        readonly Leg left, right;
        readonly RaycastHit[] hits = new RaycastHit[24];
        Vector3 previousRoot, velocity, acceleration, filteredAcceleration, pelvisOffset;
        float previousYaw, phase, frequency, crouchWeight, turnSpeed, dt;
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
            var leg = new Leg
            {
                thigh = thigh, shin = shin, foot = foot, toe = toe, contact = marker,
                toeRest = toe == null ? Quaternion.identity : toe.localRotation,
                neutral = root.InverseTransformPoint(sole), soleLocal = foot.InverseTransformPoint(sole),
                toeSoleDelta = toeContact == null ? foot.InverseTransformVector(root.forward * .14f) :
                    (foot.InverseTransformPoint(toeContact.position) - foot.InverseTransformPoint(sole)) * 1.16f,
                restRotation = Quaternion.Inverse(root.rotation) * foot.rotation,
                upper = Vector3.Distance(thigh.position, shin.position), lower = Vector3.Distance(shin.position, foot.position)
            };
            leg.soleFootprint = MeasureSole(leg, sole);
            return leg;
        }

        Vector3[] MeasureSole(Leg leg, Vector3 sole)
        {
            // Use the actual shoe perimeter. A single toe marker underestimates
            // wide boots and can leave the toe inside the next stair tread.
            var candidates = new List<SoleVertex>();
            float minHeight = float.PositiveInfinity;
            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool separateShoes = Array.Exists(skins, skin => skin.name.EndsWith("_Shoes", StringComparison.Ordinal));
            foreach (var skin in skins)
            {
                if (skin.sharedMesh == null) continue;
                if (separateShoes && !skin.name.EndsWith("_Shoes", StringComparison.Ordinal)) continue;
                int footIndex = Array.IndexOf(skin.bones, leg.foot), toeIndex = Array.IndexOf(skin.bones, leg.toe);
                if (footIndex < 0) continue;
                var weights = skin.sharedMesh.boneWeights;
                var vertices = skin.sharedMesh.vertices;
                var bind = skin.sharedMesh.bindposes;
                for (int v = 0; v < vertices.Length && v < weights.Length; v++)
                {
                    var w = weights[v];
                    float influence = SoleWeight(w.boneIndex0, w.weight0, footIndex, toeIndex) +
                        SoleWeight(w.boneIndex1, w.weight1, footIndex, toeIndex) +
                        SoleWeight(w.boneIndex2, w.weight2, footIndex, toeIndex) +
                        SoleWeight(w.boneIndex3, w.weight3, footIndex, toeIndex);
                    if (influence <= .5f) continue;
                    var sample = new SoleVertex();
                    int[] indices = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                    float[] amounts = { w.weight0, w.weight1, w.weight2, w.weight3 };
                    for (int i = 0; i < 4; i++)
                    {
                        int index = indices[i];
                        if (amounts[i] <= 0 || index >= skin.bones.Length || index >= bind.Length) continue;
                        sample.bones[i] = skin.bones[index]; sample.weights[i] = amounts[i];
                        sample.positions[i] = bind[index].MultiplyPoint3x4(vertices[v]);
                    }
                    sample.initial = sample.WorldPoint(); candidates.Add(sample);
                    minHeight = Mathf.Min(minHeight, Vector3.Dot(sample.initial - sole, root.up));
                }
            }
            if (candidates.Count == 0)
            { leg.soleVertices = Array.Empty<SoleVertex>(); return new[] { leg.toeSoleDelta, -leg.toeSoleDelta }; }
            // Preserve each sample's actual height: Edgar's curved sole rises
            // away from its heel, so a 12mm bottom slice loses the entire toe.
            float band = Mathf.Max(.055f * root.lossyScale.y, Mathf.Abs(Vector3.Dot(leg.foot.position - sole, root.up)) * .55f);
            candidates.RemoveAll(p => Vector3.Dot(p.initial - sole, root.up) > minHeight + band);
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = minX, maxZ = maxX;
            foreach (var p in candidates)
            {
                Vector3 d = p.initial - sole; float x = Vector3.Dot(d, root.right), z = Vector3.Dot(d, root.forward);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
            }
            var selected = new SoleVertex[32];
            var values = new float[32];
            for (int i = 0; i < 32; i++) values[i] = i < 24 ? float.PositiveInfinity : float.NegativeInfinity;
            foreach (var p in candidates)
            {
                Vector3 d = p.initial - sole; float x = Vector3.Dot(d, root.right), z = Vector3.Dot(d, root.forward), y = Vector3.Dot(d, root.up);
                int cell = Mathf.Min(3, Mathf.FloorToInt(Mathf.InverseLerp(minX, maxX, x) * 4)) +
                    4 * Mathf.Min(5, Mathf.FloorToInt(Mathf.InverseLerp(minZ, maxZ, z) * 6));
                if (y < values[cell]) { values[cell] = y; selected[cell] = p; }
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4, distance = x * Mathf.Cos(angle) + z * Mathf.Sin(angle);
                    if (distance > values[24 + i]) { values[24 + i] = distance; selected[24 + i] = p; }
                }
            }
            var hull = new List<SoleVertex>(); var footprint = new List<Vector3>();
            foreach (var p in selected) if (p != null && !hull.Contains(p))
            { hull.Add(p); footprint.Add(leg.foot.InverseTransformVector(p.initial - sole)); }
            leg.soleVertices = hull.ToArray(); return footprint.ToArray();
        }

        static float SoleWeight(int index, float weight, int foot, int toe) => index == foot || index == toe ? weight : 0;

        public void ResetContacts()
        {
            initialized = false; frequency = turnSpeed = 0; pelvisOffset = Vector3.zero;
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
            bool acquire = !initialized || reset || movedFar;
            if (acquire)
            {
                initialized = true; phase = 0; velocity = worldVelocity;
                crouchWeight = crouching ? 1 : 0; filteredAcceleration = acceleration = Vector3.zero;
                previousRoot = root.position; previousYaw = root.eulerAngles.y;
                left.orientation = root.rotation * left.restRotation;
                right.orientation = root.rotation * right.restRotation;
                Plant(left, root.TransformPoint(left.neutral)); Plant(right, root.TransformPoint(right.neutral));
                left.phase = 0; right.phase = .5f; pelvisOffset = Vector3.zero; turnSpeed = 0;
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
            // This distance is a complete left/right cycle, not one step.
            // Running needs a longer stride with an aerial phase; using a walk
            // duty factor at sprint speed produces six tiny steps per second.
            float walkStride = Mathf.Lerp(1.65f, 2.25f, Mathf.InverseLerp(.7f, 2.5f, speed));
            float stride = Mathf.Max(.35f, legLength * (crouching ? 1.65f : isRunning ? 3.1f : walkStride));
            float targetFrequency = speed > .08f ? Mathf.Clamp(speed / stride, .45f, crouching ? 1.8f : 3.2f) : 0;
            bool turning = speed < .15f && Mathf.Abs(turnSpeed) > 25;
            if (turning) targetFrequency = Mathf.Clamp(Mathf.Abs(turnSpeed) / 150, .65f, 1.5f);
            frequency = acquire ? targetFrequency : Mathf.Lerp(frequency, targetFrequency, 1 - Mathf.Exp(-14 * dt));
            if (acquire && speed > .08f)
            {
                // A remote avatar may first appear halfway through movement.
                // Acquire a valid pair of contacts for the current gait rather
                // than dragging both neutral feet through a complete stance.
                AcquireMovingLeg(left, 0);
                AcquireMovingLeg(right, .5f);
            }
            bool stepping = speed > .08f || turning;
            if (stepping) phase = Mathf.Repeat(phase + frequency * dt, 1);
            AdvanceLeg(left, phase, stepping, turning);
            AdvanceLeg(right, Mathf.Repeat(phase + .5f, 1), stepping, turning);
            ClearSwing(left); ClearSwing(right);
        }

        void AcquireMovingLeg(Leg leg, float atPhase)
        {
            Vector3 neutral = root.TransformPoint(leg.neutral);
            float stance = Stance;
            Vector3 from = neutral + velocity * ((stance * .45f - atPhase) / frequency);
            Plant(leg, from);
            if (atPhase < stance) return;
            // At a running spawn the opposite leg is already in flight. Its
            // endpoints belong to the surrounding two touchdowns, not to the
            // root's current position.
            leg.planted = false; leg.surface = null;
            leg.swingFrom = leg.plant;
            Vector3 to = neutral + velocity * ((1 - atPhase + stance * .45f) / frequency);
            Support(leg, to, out leg.swingTo, out leg.normal, out _);
        }

        void AdvanceLeg(Leg leg, float nextPhase, bool stepping, bool turning)
        {
            float stance = Stance;
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
                // A recovery settle can finish after the scheduled toe-off.
                // Predict only the time still left in this swing; a full swing
                // duration would put a short leg's touchdown beyond its reach.
                float remaining = (1 - nextPhase) / Mathf.Max(.45f, frequency);
                // Place the foot relative to the pelvis at touchdown. Clamping
                // the future root travel as if it were a leg extension leaves
                // fast steps behind the body before the foot has even landed.
                Vector3 placement = velocity * (stance / Mathf.Max(.45f, frequency) * .45f);
                placement = Vector3.ClampMagnitude(placement, (leg.upper + leg.lower) * .64f);
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
            // Reach belongs to the ankle, not an offset marker on a wide shoe.
            // While moving, the settling contact must also account for the
            // root travel before it lands, just like a regular swing target.
            Vector3 ankle = leg.plant - GroundRotation(leg) * Vector3.Scale(leg.soleLocal, leg.foot.lossyScale);
            if (leg.planted && Vector3.ProjectOnPlane(ankle - leg.thigh.position, root.up).magnitude >
                (leg.upper + leg.lower) * .85f) BeginSettle(leg, neutral + velocity * .18f);
            leg.phase = nextPhase;
        }

        float Stance => running ? .38f : Mathf.Lerp(.61f, .69f, crouchWeight);

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
            // Keep the whole sole supported, including the outside corners.
            float raise = 0;
            foreach (Vector3 solePoint in leg.soleFootprint)
            {
                Vector3 sample = ground + orientation * Vector3.Scale(solePoint, leg.foot.lossyScale);
                if (!Probe(sample, out Vector3 support, out _, out Transform supportSurface)) continue;
                float rise = Vector3.Dot(support - sample, root.up);
                if (rise <= raise || rise > .30f) continue;
                raise = rise;
                surface = supportSurface;
            }
            ground += root.up * raise;
        }

        void ClearSwing(Leg leg)
        {
            if (leg.planted) return;
            // The centre arc alone does not guarantee clearance when a boot's
            // leading edge reaches a raised tread before its centre does.
            Quaternion orientation = GroundRotation(leg);
            float raise = 0;
            foreach (Vector3 solePoint in leg.soleFootprint)
            {
                Vector3 point = leg.plant + orientation * Vector3.Scale(solePoint, leg.foot.lossyScale);
                if (!Probe(point, out Vector3 ground, out _, out _)) continue;
                raise = Mathf.Max(raise, Vector3.Dot(ground - point, root.up));
            }
            leg.plant += root.up * Mathf.Clamp(raise, 0, .30f);
        }

        public void Apply()
        {
            ContactError = PlantedSlip = 0;
            if (!Ready || !initialized || suspended) return;
            float bob = velocity.magnitude > .1f ? Mathf.Cos(phase * Mathf.PI * 4) * (running ? .015f : .009f) : 0;
            Vector3 correction = pelvisOffset;
            bool advanceCorrection = dt > 0 && appliedSerial != stepSerial;
            if (advanceCorrection)
            {
                correction *= Mathf.Exp(-18 * dt);
                correction += root.up * (Mathf.Min(0, bob) * (1 - crouchWeight * .65f));
                if (!firstPerson && velocity.sqrMagnitude > .01f)
                    correction += root.right * (Mathf.Sin(phase * Mathf.PI * 2) * Mathf.Lerp(.012f, .006f, crouchWeight));
            }
            // Project the sampled pelvis into both legs' actual reachable
            // shells. Vertical-only lowering cannot fix a backward/sideways
            // target, and unequal thighs/calves also have a minimum reach.
            correction = ReachablePelvisOffset(correction);
            appliedSerial = stepSerial;
            pelvis.position += correction;
            if (!firstPerson && chest != null)
            {
                Vector3 localAcceleration = root.InverseTransformDirection(filteredAcceleration);
                float lean = Mathf.Clamp(localAcceleration.z * .65f + (running ? velocity.magnitude * .6f : 0), -4, 6);
                float side = Mathf.Clamp(-localAcceleration.x * .6f - turnSpeed * .012f, -4, 4);
                chest.rotation = Quaternion.AngleAxis(side, root.forward) * Quaternion.AngleAxis(lean, root.right) * chest.rotation;
            }
            SolveLeg(left); SolveLeg(right);
            // Final terrain clearance uses weighted shoe vertices in their
            // solved pose, including toe deformation and their real heights.
            bool raised = RaiseSole(left) | RaiseSole(right);
            if (raised)
            {
                Vector3 clearanceCorrection = ReachablePelvisOffset(Vector3.zero);
                pelvis.position += clearanceCorrection; correction += clearanceCorrection;
                SolveLeg(left); SolveLeg(right);
            }
            pelvisOffset = correction;
            ContactError = PlantedSlip = 0;
            RecordContact(left); RecordContact(right);
        }

        Vector3 ReachablePelvisOffset(Vector3 correction)
        {
            for (int iteration = 0; iteration < 10; iteration++)
            {
                Vector3 previous = correction;
                FitReach(left, ref correction); FitReach(right, ref correction);
                if ((correction - previous).sqrMagnitude < .00000001f) break;
            }
            return correction;
        }

        void FitReach(Leg leg, ref Vector3 correction)
        {
            Quaternion rotation = GroundRotation(leg);
            Vector3 ankle = leg.plant - rotation * Vector3.Scale(leg.soleLocal, leg.foot.lossyScale);
            Vector3 offset = leg.thigh.position + correction - ankle;
            float a = Vector3.Distance(leg.thigh.position, leg.shin.position), b = Vector3.Distance(leg.shin.position, leg.foot.position);
            float maxReach = a + b - .006f, minReach = Mathf.Abs(a - b) + .006f;
            float distance = offset.magnitude;
            if (distance > maxReach && distance > .00001f) correction -= offset * ((distance - maxReach) / distance);
            else if (distance < minReach)
            {
                float horizontal = Vector3.ProjectOnPlane(offset, root.up).sqrMagnitude;
                float requiredHeight = Mathf.Sqrt(Mathf.Max(0, minReach * minReach - horizontal));
                correction += root.up * Mathf.Max(0, requiredHeight - Vector3.Dot(offset, root.up));
            }
        }

        bool RaiseSole(Leg leg)
        {
            float raise = 0;
            foreach (var sample in leg.soleVertices)
            {
                Vector3 point = sample.WorldPoint();
                if (Probe(point, out Vector3 ground, out _, out _))
                    raise = Mathf.Max(raise, Vector3.Dot(ground - point, root.up));
            }
            if (raise <= .0005f) return false;
            leg.plant += root.up * Mathf.Min(raise + .001f, .30f);
            if (leg.surface != null) leg.surfacePoint = leg.surface.InverseTransformPoint(leg.plant);
            return true;
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
                float stance = Stance;
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
        }

        void RecordContact(Leg leg)
        {
            Vector3 solved = leg.foot.TransformPoint(leg.soleLocal);
            float error = Vector3.Distance(solved, leg.plant);
            ContactError = Mathf.Max(ContactError, error);
            if (leg.planted) PlantedSlip = Mathf.Max(PlantedSlip, Vector3.ProjectOnPlane(solved - leg.plant, root.up).magnitude);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
