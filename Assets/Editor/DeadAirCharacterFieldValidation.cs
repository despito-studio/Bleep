using System;
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
            Require(DeadAirClothing.Sanitize(-1) == -1 && DeadAirClothing.Sanitize(0) == 0 && DeadAirClothing.Sanitize(255) == 31,
                "Wardrobe sentinel, full removal and unknown-bit sanitization changed.");
            foreach (var role in new[] { "operator", "engineer", "journalist", "dispatcher" }) ValidateAppearance(role);
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
            Debug.Log("DEAD AIR FIELD CHARACTER PASSED: four adult wardrobes with valid body matrices, crouch foot contacts, bounded secondary motion, distinct authoritative tool actions/cooldowns, blind monster and escapable attack wind-up.");
            }
            finally { Application.logMessageReceived -= log; }
        }

        static void ValidateAppearance(string role)
        {
            var avatar = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
            try
            {
                var motion = avatar.GetComponent<DeadAirCharacterMotion>();
                var state = new PlayerState { characterClass = role, clothingMask = -1 };
                motion.SetEquipmentPreview(state);
                var appearance = avatar.GetComponent<DeadAirAppearance>();
                Require(appearance != null && appearance.HasAdultBody && appearance.CurrentMask == DeadAirClothing.DefaultMask(role), role + " lost its original default and underlying adult body.");
                Transform head = Find(avatar, "head"), leftFoot = Find(avatar, "foot.L");
                float headY = head.position.y; Vector3 foot = leftFoot.position;
                state.crouching = true; motion.SetEquipmentPreview(state);
                Require(headY - head.position.y > .15f && Vector3.Distance(foot, leftFoot.position) < .045f,
                    role + " crouch needs a lower torso and planted feet.");
                state.clothingMask = 0; motion.SetEquipmentPreview(state);
                Require(appearance.CurrentMask == 0 && !DeadAirClothing.FirstPersonSleeves(0, role), role + " full removal left sleeves enabled.");
                foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name != "Adult body underneath removable layers") continue;
                    Require(renderer.sharedMesh.bounds.size.y > .9f, role + " underlying body collapsed during shape construction.");
                    foreach (var vertex in renderer.sharedMesh.vertices)
                        Require(Finite(vertex.x) && Finite(vertex.y) && Finite(vertex.z), role + " underlying body contains an invalid vertex.");
                }
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
            }
            finally { UnityEngine.Object.DestroyImmediate(avatar); }
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
    }
}
