using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirCharacterFieldValidation
    {
        [MenuItem("Dead Air/Validate Field Character Update")]
        public static void Validate()
        {
            bool invalidRotation = false;
            Application.LogCallback log = (condition, stack, type) =>
            { if (condition.IndexOf("Quaternion To Matrix conversion failed", StringComparison.Ordinal) >= 0) invalidRotation = true; };
            Application.logMessageReceived += log;
            try
            {
            var world = WorldData.Load();
            var geometryReport = new List<string>();
            foreach (var role in new[] { "operator", "engineer", "journalist", "dispatcher" }) ValidateAppearance(role, geometryReport);
            ValidateTools(world); ValidateFairAttack(world);
            var monster = new GameObject("Adult monster field validation");
            try
            {
                var motion = monster.AddComponent<DeadAirMonsterMotion>(); motion.Initialize();
                Require(motion.AnimatedJointCount == 14 && motion.HasBlindfold && motion.HeadPivot != null,
                    "The adult monster lost its complete rig or opaque blindfold.");
                motion.SetPreviewPose("attack", .35f);
                Require(monster.GetComponentsInChildren<Collider>(true).Length == 0,
                    "Presentation must not add physics to the monster.");
            }
            finally { UnityEngine.Object.DestroyImmediate(monster); }
            Require(!invalidRotation, "Appearance produced an invalid quaternion-to-matrix conversion.");
            Directory.CreateDirectory("Art/FieldUpdate");
            File.WriteAllLines("Art/FieldUpdate/character-geometry-checks.txt", geometryReport);
            Debug.Log("DEAD AIR FIELD CHARACTER PASSED: four authored character outfits with valid body matrices, crouch foot contacts, bounded secondary motion, distinct authoritative tool actions/cooldowns, blind monster and escapable attack wind-up.");
            }
            finally { Application.logMessageReceived -= log; }
        }

        static void ValidateAppearance(string role, List<string> geometryReport)
        {
            var avatar = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
            try
            {
                var motion = avatar.GetComponent<DeadAirCharacterMotion>();
                var state = new PlayerState { characterClass = role, clothingMask = -1 };
                motion.SetEquipmentPreview(state);
                var appearance = avatar.GetComponent<DeadAirAppearance>();
                Require(appearance != null && appearance.CurrentMask == DeadAirClothing.DefaultMask(role), role + " lost its authored default clothes.");
                var baseline = BakedBounds(avatar);
                int fullTriangles = appearance.VisibleSourceTriangleCount;
                Require(fullTriangles > 0, role + " has no visible authored source geometry.");
                state.clothingMask = 0;
                motion.SetEquipmentPreview(state);
                Require(appearance.CurrentMask == DeadAirClothing.DefaultMask(role) && appearance.VisibleSourceTriangleCount == fullTriangles,
                    role + " a legacy serialized outfit edit changed authored clothes.");
                state.clothingMask = -1; motion.SetEquipmentPreview(state);
                Transform head = Find(avatar, "head"), leftFoot = Find(avatar, "foot.L");
                float headY = head.position.y; Vector3 foot = leftFoot.position;
                state.crouching = true; motion.SetEquipmentPreview(state);
                Require(headY - head.position.y > .15f && Vector3.Distance(foot, leftFoot.position) < .045f,
                    role + " crouch needs a lower torso and planted feet.");
                state.crouching = false; motion.SetTransitPose(1); motion.SetEquipmentPreview(state);
                Require(headY - head.position.y > .10f && Mathf.Abs(leftFoot.position.y - foot.y) < .045f,
                    role + " seated arrival needs a lower pelvis and floor-supported feet.");
                motion.SetTransitPose(0); motion.SetEquipmentPreview(state);
                if (role == "dispatcher")
                {
                    state.uniqueItemEquipped = true; state.roleIntelUntil = 100; state.roleIntelKind = "objective:power";
                    state.roleIntelX = -4; state.roleIntelZ = 0; motion.SetEquipmentPreview(state);
                    Require(Vector3.Dot(Find(avatar, "North needle").forward, Vector3.right) > .99f,
                        "The physical compass needle ignored its accepted objective heading.");
                }
                var spring = appearance.SecondaryMotion;
                for (int step = 0; step < 400; step++) spring.Step(step % 17 == 0 ? 2 : .016f, step * .51f, 8, step % 2 == 0);
                Require(!float.IsNaN(spring.MaximumAngle) && spring.MaximumAngle <= 6.001f && spring.ChestDisplacement <= .00601f,
                    role + " secondary motion exceeded its physical bounds.");
                float largestHeight = 0, largestWidth = 0, largestDepth = 0;
                for (int pose = 0; pose < 6; pose++)
                {
                    state.uniqueItemEquipped = false; state.crouching = pose == 1 || pose == 4;
                    motion.SetTransitPose(pose == 5 ? 1 : 0);
                    string clip = pose == 2 || pose == 4 ? "Walk" : pose == 3 ? "Run" : "Idle";
                    motion.SetEquipmentPreview(state, clip, .35f);
                    Require(appearance.CurrentMask == DeadAirClothing.DefaultMask(role) && appearance.VisibleSourceTriangleCount == fullTriangles,
                        role + " locomotion changed authored clothes.");
                    for (int step = 0; step < 20; step++) spring.Step(.05f, step * .3f, 8, true);
                    var bounds = BakedBounds(avatar);
                    // Head-bound long hair can swing while crouched. A detached
                    // skinned vertex/incorrect bind matrix cannot grow the model
                    // beyond its original adult proportions by metres.
                    // A walking stride naturally extends farther in depth than
                    // an idle torso. Bound that reach by the actor's full height.
                    Require(bounds.size.x < baseline.size.x * 1.6f + .35f && bounds.size.z < Mathf.Max(baseline.size.z * 1.6f + .40f, baseline.size.y * .85f) &&
                        bounds.size.y < baseline.size.y * 1.35f + .25f && bounds.min.y > -.35f,
                        role + " pose " + pose + " produced a skinned geometry spike: " + bounds);
                    largestHeight = Mathf.Max(largestHeight, bounds.size.y); largestWidth = Mathf.Max(largestWidth, bounds.size.x); largestDepth = Mathf.Max(largestDepth, bounds.size.z);
                }
                geometryReport.Add(role + ": PASSED authored clothes in six standing/crouching/walking/running/seated poses, discarded legacy serialized edit and finite posed BakeMesh bounds; default=" + baseline.size.ToString("F3") +
                    ", maximum(w,h,d)=" + new Vector3(largestWidth, largestHeight, largestDepth).ToString("F3") + ", source triangles default=" + fullTriangles +
                    ".");
                ValidateLocomotionPosture(avatar, motion, role, geometryReport);
            }
            finally { UnityEngine.Object.DestroyImmediate(avatar); }
        }

        static void ValidateLocomotionPosture(GameObject avatar, DeadAirCharacterMotion motion, string role, List<string> geometryReport)
        {
            var state = new PlayerState { characterClass = role, clothingMask = -1 };
            motion.SetTransitPose(0);
            var shoulders = new[] { Find(avatar, "upper_arm.L"), Find(avatar, "upper_arm.R") };
            var elbows = new[] { Find(avatar, "forearm.L"), Find(avatar, "forearm.R") };
            var wrists = new[] { Find(avatar, "hand.L"), Find(avatar, "hand.R") };
            var feet = new[] { Find(avatar, "foot.L"), Find(avatar, "foot.R") };
            motion.SetEquipmentPreview(state, "Idle", 0);
            var upperLengths = new float[2]; var forearmLengths = new float[2];
            for (int side = 0; side < 2; side++)
            {
                upperLengths[side] = Vector3.Distance(shoulders[side].position, elbows[side].position);
                forearmLengths[side] = Vector3.Distance(elbows[side].position, wrists[side].position);
                Require(upperLengths[side] > .05f && forearmLengths[side] > .05f, role + " has a collapsed arm chain.");
            }
            foreach (string clip in new[] { "Idle", "Walk", "Run" })
            {
                float maximumAbduction = 0;
                var armMin = new[] { float.PositiveInfinity, float.PositiveInfinity };
                var armMax = new[] { float.NegativeInfinity, float.NegativeInfinity };
                var footMin = new[] { float.PositiveInfinity, float.PositiveInfinity };
                var footMax = new[] { float.NegativeInfinity, float.NegativeInfinity };
                for (int phase = 0; phase < 8; phase++)
                {
                    motion.SetEquipmentPreview(state, clip, phase / 8f);
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 shoulder = avatar.transform.InverseTransformPoint(shoulders[side].position);
                        Vector3 elbow = avatar.transform.InverseTransformPoint(elbows[side].position);
                        Vector3 direction = elbow - shoulder;
                        // Fore/aft gait swing is allowed. Only the lateral part
                        // of the actual joint direction measures A/T-pose arms.
                        float abduction = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(direction.x) / direction.magnitude)) * Mathf.Rad2Deg;
                        Require(Finite(abduction) && abduction <= 10.5f && direction.y < -.08f,
                            role + " " + clip + " phase " + phase + " keeps a spread/raised arm: " + abduction.ToString("F2") + " degrees.");
                        // FBX basis conversion can mirror left/right labels.
                        // Compare against the measured side of the real torso.
                        float torsoX = avatar.transform.InverseTransformPoint(Find(avatar, "pelvis").position).x;
                        Require(direction.x * Mathf.Sign(shoulder.x - torsoX) >= -.02f,
                            role + " " + clip + " moved an elbow through the torso.");
                        Require(Mathf.Abs(Vector3.Distance(shoulders[side].position, elbows[side].position) - upperLengths[side]) <= .001f + upperLengths[side] * .01f &&
                            Mathf.Abs(Vector3.Distance(elbows[side].position, wrists[side].position) - forearmLengths[side]) <= .001f + forearmLengths[side] * .01f,
                            role + " " + clip + " posture correction stretched an authored arm.");
                        maximumAbduction = Mathf.Max(maximumAbduction, abduction);
                        armMin[side] = Mathf.Min(armMin[side], direction.z); armMax[side] = Mathf.Max(armMax[side], direction.z);
                        float footZ = avatar.transform.InverseTransformPoint(feet[side].position).z;
                        footMin[side] = Mathf.Min(footMin[side], footZ); footMax[side] = Mathf.Max(footMax[side], footZ);
                    }
                }
                if (clip != "Idle")
                    for (int side = 0; side < 2; side++)
                        Require(armMax[side] - armMin[side] > .02f && footMax[side] - footMin[side] > .06f,
                            role + " " + clip + " lost its actual arm swing or foot stride: arm=" +
                            (armMax[side] - armMin[side]).ToString("F4") + " m, foot=" + (footMax[side] - footMin[side]).ToString("F4") + " m.");
                geometryReport.Add(role + ": PASSED " + clip + " empty-hand posture at eight phases; maximum lateral upper-arm abduction=" +
                    maximumAbduction.ToString("F2") + " degrees, arm fore/aft spans=" + (armMax[0] - armMin[0]).ToString("F3") + "/" +
                    (armMax[1] - armMin[1]).ToString("F3") + " m, foot spans=" + (footMax[0] - footMin[0]).ToString("F3") + "/" +
                    (footMax[1] - footMin[1]).ToString("F3") + " m; source arm lengths preserved.");
            }
        }
        static Transform Find(GameObject root, string name)
        { foreach (var bone in root.GetComponentsInChildren<Transform>(true)) if (bone.name == name) return bone; throw new InvalidOperationException("Missing rig bone " + name); }
        static DeadAirRules Started(WorldData world, string role)
        { var rules = new DeadAirRules(world, "host", "Field character", role, true, true); rules.Start("host"); rules.EquipUnique("host", true); return rules; }
        static void At(PlayerState player, ObjectiveData obj)
        { player.x = obj.x; player.z = obj.z; player.y = obj.y > 3 ? 4.2f : obj.y < -2 ? -4.2f : 0; }

        static void ValidateTools(WorldData world)
        {
            var engineer = Started(world, "engineer"); var actor = engineer.State.Player("host");
            At(actor, world.Objective("power"));
            Require(engineer.UseUnique("host") == null && !engineer.State.power, "A wrench cannot invent missing generator fuel.");
            engineer.State.elapsed = actor.roleReadyAt;
            engineer.State.canisters.AddRange(new[] { "fuel-canister", "fuel-canister-2", "fuel-canister-3" });
            Require(engineer.UseUnique("host") == null && engineer.State.power, "A wrench must start a genuinely fuelled generator.");
            engineer.State.elapsed = actor.roleReadyAt; At(actor, world.Objective("fuse-1"));
            Require(engineer.UseUnique("host") == null && actor.HasItem("fuse-1") && engineer.State.fuses.Count == 1,
                "A wrench must recover the actual nearby authored fuse exactly once.");
            Require(engineer.UseUnique("host") != null && engineer.State.fuses.Count == 1, "Repeated packets bypassed wrench cooldown.");
            var journalist = Started(world, "journalist"); actor = journalist.State.Player("host");
            journalist.State.enemy.active = true; journalist.State.enemy.x = actor.x; journalist.State.enemy.y = actor.y; journalist.State.enemy.z = actor.z + 4;
            Require(journalist.UseUnique("host") == null && actor.roleIntelKind == "enemy" && journalist.State.enemy.heardUntil > 0,
                "The recorder needs nearby acoustic intel and a physical playback lure.");
            var dispatcher = Started(world, "dispatcher"); actor = dispatcher.State.Player("host");
            dispatcher.State.enemy.active = true; dispatcher.State.enemy.x = actor.x; dispatcher.State.enemy.y = actor.y; dispatcher.State.enemy.z = actor.z + 4;
            Require(dispatcher.UseUnique("host") == null && actor.roleIntelKind.StartsWith("objective:", StringComparison.Ordinal) && dispatcher.State.enemy.heardUntil == 0,
                "The compass must locate the next quest objective silently.");
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(dispatcher.State)).Player("host");
            Require(copy.roleReadyAt == actor.roleReadyAt && copy.roleIntelKind == actor.roleIntelKind && copy.roleIntelUntil == actor.roleIntelUntil,
                "Authoritative item cooldown/intel did not survive a snapshot.");
        }
        static void ValidateFairAttack(WorldData world)
        {
            var rules = Started(world, "operator"); var actor = rules.State.Player("host"); actor.invulnerableUntil = 0;
            var enemy = rules.State.enemy; enemy.active = true; enemy.x = actor.x; enemy.y = actor.y; enemy.z = actor.z;
            rules.Tick(.02f);
            Require(!actor.down && enemy.behaviour == "attack", "A close enemy damaged a player without telegraphing.");
            actor.z += 3;
            for (int tick = 0; tick < 10; tick++) rules.Tick(.1f);
            Require(!actor.down && string.IsNullOrEmpty(enemy.attackPlayerId), "Leaving melee reach during wind-up did not avoid the attack.");
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Field character validation: " + message); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static Bounds BakedBounds(GameObject avatar)
        {
            var baked = new Mesh(); var bounds = new Bounds(); bool initialized = false;
            try
            {
                foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.gameObject.activeInHierarchy || !renderer.enabled) continue;
                    renderer.BakeMesh(baked);
                    // BakeMesh retains unused source vertices after submesh
                    // filtering; only actual referenced triangles are visible.
                    var vertices = baked.vertices;
                    foreach (int index in baked.triangles)
                    {
                        var point = avatar.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[index]));
                        Require(Finite(point.x) && Finite(point.y) && Finite(point.z), "Visible skinned geometry contains a non-finite vertex.");
                        if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; } else bounds.Encapsulate(point);
                    }
                    baked.Clear();
                }
                Require(initialized, "No visible adult avatar geometry was baked."); return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
    }
}
