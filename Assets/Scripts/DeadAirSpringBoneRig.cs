using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>World-space secondary tips on authored deform bones, evaluated after the animated body.</summary>
    sealed class DeadAirSpringBoneRig
    {
        enum Kind { Hair, Cloth, Breast }
        sealed class Joint
        {
            public Transform bone;
            public Kind kind;
            public Vector3 basePosition, localTip, tip, velocity;
            public Quaternion baseRotation, proposedDelta, delta = Quaternion.identity;
            public float frequency, damping, gravity, angleLimit, radius;
            public float[] collisionRadii;
        }
        struct BodyVolume
        {
            public Transform a, b;
            public Vector3 localA, localB;
            public float radius;
            public Vector3 A => a.TransformPoint(localA);
            public Vector3 B => b.TransformPoint(localB);
        }

        readonly Transform root;
        static readonly float[] SegmentSamples = { .35f, .7f, 1f };
        readonly List<Joint> joints = new List<Joint>();
        readonly List<List<Joint>> chains = new List<List<Joint>>();
        readonly List<BodyVolume> volumes = new List<BodyVolume>();
        Vector3[] volumeA, volumeB;
        float scale;
        Vector3 previousRoot;
        Quaternion previousRotation;
        bool ready;
        public int BoneCount => joints.Count;
        public int HairBoneCount { get; private set; }
        public int ClothBoneCount { get; private set; }
        public int BreastBoneCount { get; private set; }
        public float MaximumAngle { get; private set; }
        public float BreastDisplacement { get; private set; }
        public float MaximumCollisionPenetration { get; private set; }

        public DeadAirSpringBoneRig(Transform root, string role)
        {
            this.root = root;
            scale = ReadScale();
            var found = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!found.ContainsKey(t.name)) found.Add(t.name, t);
            foreach (var pair in found)
            {
                string name = pair.Key;
                if (name.EndsWith("_tip", StringComparison.Ordinal)) continue;
                Kind kind;
                if (name.StartsWith("secondary_hair.", StringComparison.Ordinal)) kind = Kind.Hair;
                else if (name.StartsWith("secondary_cloth.", StringComparison.Ordinal)) kind = Kind.Cloth;
                else if (name.StartsWith("secondary_breast.", StringComparison.Ordinal)) kind = Kind.Breast;
                else continue;
                var bone = pair.Value;
                Transform tip = null;
                // Bone axes change in FBX conversion. The child/tip is the authority.
                foreach (Transform child in bone)
                    if (child.name.StartsWith("secondary_", StringComparison.Ordinal)) { tip = child; break; }
                if (tip == null) found.TryGetValue(name + "_tip", out tip);
                if (tip == null || Vector3.Distance(bone.position, tip.position) < .001f)
                    throw new InvalidOperationException("Secondary bone needs an authored child or tip: " + name);
                var joint = new Joint { bone = bone, kind = kind, basePosition = bone.localPosition,
                    baseRotation = bone.localRotation, localTip = bone.InverseTransformPoint(tip.position) };
                if (kind == Kind.Hair)
                {
                    joint.frequency = 18; joint.damping = .85f; joint.gravity = .34f;
                    joint.angleLimit = 26; joint.radius = .009f; HairBoneCount++;
                }
                else if (kind == Kind.Cloth)
                {
                    joint.frequency = name.Contains("tie") ? 20 : 24;
                    joint.damping = .9f; joint.gravity = .25f;
                    joint.angleLimit = name.Contains("tie") ? 25 : 19;
                    joint.radius = .012f; ClothBoneCount++;
                }
                else
                {
                    joint.frequency = 38; joint.damping = .92f; joint.gravity = .12f;
                    joint.angleLimit = 6; joint.radius = 0; BreastBoneCount++;
                }
                joints.Add(joint);
            }
            joints.Sort((a, b) => Depth(a.bone).CompareTo(Depth(b.bone)));
            BuildChains();
            BuildVolumes(found, role);
            volumeA = new Vector3[volumes.Count]; volumeB = new Vector3[volumes.Count];
            CacheVolumes();
            foreach (var joint in joints)
                joint.collisionRadii = new float[volumes.Count * SegmentSamples.Length];
            UpdateCollisionRadii();
            Reset();
        }

        static int Depth(Transform t) { int depth = 0; while (t != null) { depth++; t = t.parent; } return depth; }

        void BuildChains()
        {
            var byBone = new Dictionary<Transform, List<Joint>>();
            foreach (var joint in joints)
            {
                if (joint.bone.parent == null || !byBone.TryGetValue(joint.bone.parent, out var chain))
                {
                    chain = new List<Joint>();
                    chains.Add(chain);
                }
                chain.Add(joint); byBone.Add(joint.bone, chain);
            }
            foreach (var chain in chains)
            {
                if (chain[0].kind == Kind.Breast) continue;
                float reach = 0;
                foreach (var joint in chain) reach += joint.bone.TransformVector(joint.localTip).magnitude / Scale;
                // A per-link cone allowed three 25-degree swings to accumulate
                // along a metre-long garment. Share one travel budget across
                // the whole chain, with longer strands receiving smaller angles.
                float travel = chain[0].kind == Kind.Hair ? .20f : .16f;
                float cone = 2 * Mathf.Asin(Mathf.Min(1, travel / Mathf.Max(.001f, 2 * reach))) * Mathf.Rad2Deg;
                foreach (var joint in chain) joint.angleLimit = Mathf.Min(joint.angleLimit, cone) / chain.Count;
            }
        }

        void BuildVolumes(Dictionary<string, Transform> found, string role)
        {
            float width = role == "engineer" ? .145f : .095f;
            AddVolume(found, "pelvis", "chest", width);
            AddVolume(found, "upper_arm.L", "upper_arm.R", role == "engineer" ? .12f : .075f);
            AddVolume(found, "neck", "head", .085f);
            foreach (string side in new[] { "L", "R" })
            {
                AddVolume(found, "thigh." + side, "shin." + side, role == "engineer" ? .092f : .056f);
                AddVolume(found, "shin." + side, "foot." + side, .038f);
            }
            if (found.TryGetValue("head", out var head))
            {
                // Head origin is below the cranium. Store this character-up point in
                // head-local space so the collision volume follows look and head turns.
                Vector3 center = head.position + root.up * (.078f * Scale);
                Vector3 local = head.InverseTransformPoint(center);
                volumes.Add(new BodyVolume { a = head, b = head, localA = local, localB = local, radius = .095f });
            }
        }

        void AddVolume(Dictionary<string, Transform> found, string a, string b, float radius)
        {
            if (found.TryGetValue(a, out var start) && found.TryGetValue(b, out var end))
                volumes.Add(new BodyVolume { a = start, b = end, radius = radius });
        }

        float Scale => scale;
        float ReadScale()
        {
            Vector3 value = root.lossyScale;
            return Mathf.Max(.001f, (Mathf.Abs(value.x) + Mathf.Abs(value.y) + Mathf.Abs(value.z)) / 3);
        }
        void CacheVolumes()
        {
            for (int i = 0; i < volumes.Count; i++) { volumeA[i] = volumes[i].A; volumeB[i] = volumes[i].B; }
        }

        void UpdateCollisionRadii()
        {
            foreach (var joint in joints)
            {
                Vector3 tip = joint.bone.TransformPoint(joint.localTip);
                for (int i = 0; i < volumes.Count; i++)
                    for (int sample = 0; sample < SegmentSamples.Length; sample++)
                    {
                        Vector3 point = Vector3.Lerp(joint.bone.position, tip, SegmentSamples[sample]);
                        float distance = Vector3.Distance(point, Nearest(point, volumeA[i], volumeB[i]));
                        // Coarse capsules can overlap the authored crouch/garment.
                        // Calibrate against this clean animated pose so zero
                        // spring offset remains feasible, not a standing-only fit.
                        joint.collisionRadii[i * SegmentSamples.Length + sample] =
                            Mathf.Max(0, Mathf.Min(volumes[i].radius + joint.radius, distance / Scale - .002f));
                    }
            }
        }

        /// <summary>Called only after a fresh animation/body sample, before any spring offset.</summary>
        public void CaptureAnimatedPose()
        {
            foreach (var joint in joints)
            {
                joint.basePosition = joint.bone.localPosition;
                joint.baseRotation = joint.bone.localRotation;
            }
            // Keep the integrated offset/velocity for this frame. Reapply can
            // attach that same offset to a freshly sampled base without a step.
        }

        public void Reset()
        {
            scale = ReadScale();
            RestoreBasePose();
            foreach (var joint in joints)
            {
                joint.tip = joint.bone.TransformPoint(joint.localTip);
                joint.velocity = Vector3.zero;
                joint.delta = Quaternion.identity;
            }
            previousRoot = root.position; previousRotation = root.rotation;
            MaximumAngle = BreastDisplacement = MaximumCollisionPenetration = 0;
            ready = false;
        }

        void RestoreBasePose()
        {
            foreach (var joint in joints)
            {
                joint.bone.localPosition = joint.basePosition;
                joint.bone.localRotation = joint.baseRotation;
            }
        }

        /// <summary>Idempotent after another animation/IK sample; never integrates a second time.</summary>
        public void Reapply()
        {
            foreach (var joint in joints)
            {
                joint.bone.localPosition = joint.basePosition;
                joint.bone.localRotation = joint.baseRotation * joint.delta;
            }
        }

        public void Step(float dt, bool clothed, bool reset)
        {
            scale = ReadScale();
            if (!Finite(dt) || !Finite(root.position)) { Reset(); return; }
            RestoreBasePose();
            CacheVolumes();
            UpdateCollisionRadii();
            if (reset || !ready || dt > .25f || Vector3.Distance(previousRoot, root.position) > 1.25f * Scale ||
                Quaternion.Angle(previousRotation, root.rotation) > 100)
            {
                Reset(); ready = true;
                // Teleports and long stalls restart on the authored pose, without an impulse.
                return;
            }
            if (dt <= 0) { Reapply(); return; }
            previousRoot = root.position; previousRotation = root.rotation;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 120)), 1, 30);
            float h = dt / steps;
            MaximumAngle = BreastDisplacement = MaximumCollisionPenetration = 0;
            for (int step = 0; step < steps; step++)
            {
                RestoreBasePose();
                foreach (var joint in joints)
                {
                    // Parents have already been solved for this substep.
                    Transform bone = joint.bone;
                    Vector3 origin = bone.position;
                    Vector3 restTip = bone.TransformPoint(joint.localTip);
                    Vector3 rest = restTip - origin;
                    float length = rest.magnitude;
                    if (length < .001f) continue;
                    float stiffness = joint.frequency * joint.frequency;
                    float denominator = 1 + 2 * joint.damping * joint.frequency * h + stiffness * h * h;
                    Vector3 acceleration = Physics.gravity * (joint.gravity * Scale);
                    joint.velocity = (joint.velocity + ((restTip - joint.tip) * stiffness + acceleration) * h) / denominator;
                    Vector3 freeTip = joint.tip + joint.velocity * h;
                    Vector3 result = Constrain(joint, origin, rest, freeTip, clothed);
                    // Constraint impulses are also bounded; accumulated hidden velocity
                    // must not be released when a strand clears the shoulder capsule.
                    joint.velocity += (result - freeTip) / h;
                    joint.velocity = Vector3.ClampMagnitude(joint.velocity, 8 * Scale);
                    joint.tip = result;
                    Quaternion swing = Quaternion.FromToRotation(rest, result - origin);
                    bone.rotation = swing * bone.rotation;
                    joint.delta = Quaternion.Inverse(joint.baseRotation) * bone.localRotation;
                    if (!Finite(joint.tip) || !Finite(joint.velocity) || !Finite(joint.delta)) { Reset(); return; }
                    MaximumAngle = Mathf.Max(MaximumAngle, Quaternion.Angle(Quaternion.identity, joint.delta));
                    if (joint.kind == Kind.Breast) BreastDisplacement = Mathf.Max(BreastDisplacement, Vector3.Distance(restTip, result));
                }
                ResolveChainCollisions(h);
            }
        }

        Vector3 Constrain(Joint joint, Vector3 origin, Vector3 rest, Vector3 tip, bool clothed)
        {
            float angle = joint.angleLimit;
            float length = rest.magnitude;
            if (joint.kind == Kind.Breast)
            {
                // Tail movement is capped in metres as well as degrees, across avatar scales.
                float maxDisplacement = (clothed ? .006f : .008f) * Scale;
                angle = Mathf.Min(angle, 2 * Mathf.Asin(Mathf.Min(1, maxDisplacement / (2 * length))) * Mathf.Rad2Deg);
            }
            tip = Limit(origin, rest, tip, angle);
            if (joint.kind == Kind.Breast) return tip;
            // Alternate collision projection and fixed-length/cone projection. The
            // authored rest direction is a feasible fallback at the attachment.
            for (int iteration = 0; iteration < 6; iteration++)
            {
                for (int i = 0; i < volumes.Count; i++)
                {
                    for (int sample = 0; sample < SegmentSamples.Length; sample++)
                    {
                        float fraction = SegmentSamples[sample];
                        float radius = joint.collisionRadii[i * SegmentSamples.Length + sample] * Scale;
                        Vector3 point = Vector3.Lerp(origin, tip, fraction);
                        Vector3 center = Nearest(point, volumeA[i], volumeB[i]);
                        Vector3 outward = point - center;
                        if (outward.sqrMagnitude < radius * radius)
                        {
                            Vector3 surface = center + (outward.sqrMagnitude > .00000001f ? outward.normalized : rest.normalized) * radius;
                            tip += (surface - point) / fraction;
                        }
                    }
                }
                tip = Limit(origin, rest, tip, angle);
            }
            return tip;
        }

        float ChainPenetration(List<Joint> chain)
        {
            float maximum = 0;
            foreach (var joint in chain)
            {
                Vector3 tip = joint.bone.TransformPoint(joint.localTip);
                for (int i = 0; i < volumes.Count; i++)
                    for (int sample = 0; sample < SegmentSamples.Length; sample++)
                    {
                        Vector3 point = Vector3.Lerp(joint.bone.position, tip, SegmentSamples[sample]);
                        maximum = Mathf.Max(maximum, joint.collisionRadii[i * SegmentSamples.Length + sample] * Scale -
                            Vector3.Distance(point, Nearest(point, volumeA[i], volumeB[i])));
                    }
            }
            return maximum;
        }

        static void ApplyChainFraction(List<Joint> chain, float fraction)
        {
            foreach (var joint in chain)
                joint.bone.localRotation = joint.baseRotation * Quaternion.Slerp(Quaternion.identity, joint.proposedDelta, fraction);
        }

        void ResolveChainCollisions(float dt)
        {
            foreach (var chain in chains)
            {
                if (chain[0].kind == Kind.Breast) continue;
                if (ChainPenetration(chain) > .0005f * Scale)
                {
                    foreach (var joint in chain) joint.proposedDelta = joint.delta;
                    // Local projection may become infeasible after a parent
                    // swings. Retreat the entire chain toward its known-safe
                    // animated pose, instead of leaving a child inside the body.
                    float low = 0, high = 1;
                    for (int iteration = 0; iteration < 8; iteration++)
                    {
                        float fraction = (low + high) * .5f;
                        ApplyChainFraction(chain, fraction);
                        if (ChainPenetration(chain) <= .0005f * Scale) low = fraction; else high = fraction;
                    }
                    ApplyChainFraction(chain, low);
                    foreach (var joint in chain)
                    {
                        joint.delta = Quaternion.Slerp(Quaternion.identity, joint.proposedDelta, low);
                        Vector3 tip = joint.bone.TransformPoint(joint.localTip);
                        joint.velocity = Vector3.ClampMagnitude(joint.velocity + (tip - joint.tip) / dt, 8 * Scale);
                        joint.tip = tip;
                    }
                }
                MaximumCollisionPenetration = Mathf.Max(MaximumCollisionPenetration, ChainPenetration(chain));
            }
        }

        static Vector3 Limit(Vector3 origin, Vector3 rest, Vector3 tip, float angle)
        {
            Vector3 direction = tip - origin;
            if (direction.sqrMagnitude < .00000001f) direction = rest;
            return origin + Vector3.RotateTowards(rest, direction.normalized * rest.magnitude, angle * Mathf.Deg2Rad, 0);
        }

        static Vector3 Nearest(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 segment = b - a;
            return a + segment * (segment.sqrMagnitude < .00000001f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, segment) / segment.sqrMagnitude));
        }
        static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
    }
}
