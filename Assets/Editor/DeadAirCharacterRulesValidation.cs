using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirCharacterRulesValidation
    {
        [MenuItem("Dead Air/Validate Character Rules")]
        public static void Validate()
        {
            var world = WorldData.Load();
            ValidateObjectiveRetries(world);
            ValidatePersonalItems(world);
            ValidateUniqueCancellation(world);
            ValidateRoleChangesAndRestrictions(world);
            ValidateInventoryAndSerialization(world);
            ValidateFuelCanisterPresentation(world);
            ValidateCameraFlashlightStun(world);
            ValidatePausedMapAndLateJoin(world);
            Debug.Log("DEAD AIR CHARACTER RULES PASSED: objective retries, role items, cooldowns, depletion, restart, cancellation, restrictions, inventory, network serialization, all fuel canisters in first/third person, blind enemy ignores camera/light, explicit timed engineer defence, paused maps and late joins.");
        }

        static DeadAirRules Started(WorldData world, string role)
        {
            var rules = new DeadAirRules(world, "host", "Character validation", role, true, true);
            rules.Start("host");
            return rules;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Character validation: " + message);
        }

        static string InteractAt(DeadAirRules rules, WorldData world, string objectId)
        {
            var location = world.Objective(objectId);
            Require(location != null, "Missing objective " + objectId);
            var actor = rules.State.Player("host");
            actor.x = location.x; actor.y = location.y >= 4.2f ? 4.2f : location.y <= -2f ? -4.2f : 0;
            actor.z = location.z;
            return rules.Interact("host", objectId);
        }

        static void RequireRejectedObjective(DeadAirRules rules, WorldData world, string id)
        {
            Require(InteractAt(rules, world, id) != null, id + " should reject missing prerequisites.");
            Require(!rules.State.Collected(id), id + " must remain available after rejection.");
        }

        static void ValidateObjectiveRetries(WorldData world)
        {
            var rules = Started(world, "engineer");
            RequireRejectedObjective(rules, world, "power");
            RequireRejectedObjective(rules, world, "broadcast");
            RequireRejectedObjective(rules, world, "exit");
            foreach (string id in new[] { "tape-1", "tape-2", "tape-3" })
                Require(InteractAt(rules, world, id) == null, "Could not collect " + id);
            RequireRejectedObjective(rules, world, "broadcast");
            foreach (string id in new[] { "fuse-1", "fuse-2" })
                Require(InteractAt(rules, world, id) == null, "Could not collect " + id);
            RequireRejectedObjective(rules, world, "power");
            foreach (string id in new[] { "fuel-canister", "fuel-canister-2", "fuel-canister-3" })
                Require(InteractAt(rules, world, id) == null, "Could not collect " + id);
            Require(InteractAt(rules, world, "power") == null && rules.State.power, "Power must succeed after collecting fuel canisters.");
            RequireRejectedObjective(rules, world, "broadcast");
            var actor = rules.State.Player("host");
            var console = world.Objective("broadcast");
            actor.x = console.x; actor.y = console.y >= 4.2f ? 4.2f : 0; actor.z = console.z;
            Require(rules.Calibrate("host", 96.4f) == null, "Calibration failed.");
            Require(InteractAt(rules, world, "broadcast") == null && rules.State.broadcast, "Broadcast retry failed.");
            Require(InteractAt(rules, world, "exit") == null && rules.State.phase == "won", "Extraction retry failed.");
        }

        static void ValidatePersonalItems(WorldData world)
        {
            var rules = Started(world, "operator");
            var actor = rules.State.Player("host");
            Require(actor.cigarettesRemaining == 12 && actor.inventory.Count == 2 && actor.inventory[1] == "cigarette-pack",
                "A fresh cigarette pack must be visible in the regular inventory.");
            Require(rules.UseUnique("host") != null, "Using a stowed personal item must fail.");
            rules.SelectSlot("host", actor.inventory.IndexOf("cigarette-pack"));
            Require(actor.HasSelectedRoleItem && actor.uniqueItemEquipped, "Selecting the pack slot must equip it.");
            Require(rules.Drop("host", "cigarette-pack") != null && actor.HasItem("cigarette-pack"), "Permanent role gear must remain in inventory.");
            float sanity = actor.sanity, stamina = actor.stamina;
            Require(rules.UseUnique("host") == null, "First cigarette should be usable immediately.");
            Require(actor.cigarettesRemaining == 11 && actor.uniqueActionSequence == 1 && !actor.uniqueActionCancelled,
                "One cigarette use must consume exactly one cigarette and start its presentation.");
            Require(actor.sanity == sanity && actor.stamina == stamina, "Smoking must not grant a health or stamina bonus.");
            Require(rules.UseUnique("host") != null, "Repeated packets must not bypass the cooldown.");
            rules.State.elapsed = 6.99f;
            Require(rules.UseUnique("host") != null && actor.cigarettesRemaining == 11, "Operator cooldown must last seven seconds.");
            rules.State.elapsed = 7;
            Require(rules.UseUnique("host") == null, "Use must become available at seven seconds.");
            while (actor.cigarettesRemaining > 0)
            {
                rules.State.elapsed += 7;
                Require(rules.UseUnique("host") == null, "Remaining cigarettes should be usable after cooldown.");
            }
            rules.State.elapsed += 7;
            Require(rules.UseUnique("host") != null && actor.uniqueActionSequence == 12 && actor.cigarettesRemaining == 0,
                "An empty pack must neither animate a new use nor underflow.");
            rules.State.phase = "lost";
            rules.Restart("host");
            Require(actor.cigarettesRemaining == 12 && actor.uniqueActionSequence == 0 && actor.uniqueActionAt < 0 &&
                !actor.uniqueItemEquipped && !actor.mapOpen && actor.uniqueActionCancelled,
                "Restart must restore the pack and reset presentation state.");

            foreach (string role in new[] { "engineer", "journalist", "dispatcher" })
            {
                rules = Started(world, role);
                actor = rules.State.Player("host");
                string item=DeadAirRoleItems.ForClass(role);
                Require(actor.inventory.Count==2&&actor.inventory[1]==item,role+" gear must occupy its regular inventory slot.");
                rules.SelectSlot("host",actor.inventory.IndexOf(item));
                Require(actor.HasSelectedRoleItem&&actor.uniqueItemEquipped,role+" slot selection must equip its gear.");
                Require(rules.Drop("host",item)!=null&&actor.HasItem(item),role+" permanent gear must not disappear through dropping.");
                Require(rules.UseUnique("host") == null && actor.cigarettesRemaining == 0, role + " must have its own reusable item.");
                rules.State.elapsed = 2.49f;
                Require(rules.UseUnique("host") != null, role + " cooldown was skipped.");
                rules.State.elapsed = 2.5f;
                Require(rules.UseUnique("host") == null && actor.uniqueActionSequence == 2, role + " cooldown should end after 2.5 seconds.");
            }
        }

        static void ValidateUniqueCancellation(WorldData world)
        {
            foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
                foreach (string interruption in new[] { "stow", "map", "camera", "hide" })
                {
                    var rules = Started(world, role);
                    var actor = rules.State.Player("host");
                    Require(actor.uniqueActionCancelled, "Fresh role items must have no pending presentation.");
                    rules.EquipUnique(actor.id, true);
                    Require(rules.UseUnique(actor.id) == null && !actor.uniqueActionCancelled, "Accepted use must clear presentation cancellation.");
                    float acceptedAt = actor.uniqueActionAt;
                    int sequence = actor.uniqueActionSequence, remaining = actor.cigarettesRemaining;
                    rules.State.elapsed = acceptedAt + .5f;
                    if (interruption == "stow") rules.EquipUnique(actor.id, false);
                    else if (interruption == "map") rules.EquipMap(actor.id, true);
                    else if (interruption == "camera") CameraMove(rules, actor, true, true);
                    else
                    {
                        var locker = world.lockers[0];
                        actor.x = locker.x; actor.y = locker.y; actor.z = locker.z;
                        Require(rules.Interact(actor.id, locker.id) == null, "Could not hide during cancellation validation.");
                    }
                    Require(actor.uniqueActionCancelled && !actor.uniqueItemEquipped, role + " " + interruption + " must cancel the accepted presentation.");
                    var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State)).Player(actor.id);
                    Require(copy.uniqueActionCancelled && copy.uniqueActionSequence == sequence && copy.uniqueActionAt == acceptedAt,
                        "Cancellation and the accepted action must survive network snapshots.");
                    if (interruption == "hide") Require(rules.Interact(actor.id, "exit-locker") == null, "Could not exit the locker.");
                    rules.EquipUnique(actor.id, true);
                    Require(actor.uniqueItemEquipped && actor.uniqueActionCancelled, "Re-equipping must not resume a cancelled action.");
                    Require(rules.UseUnique(actor.id) != null && actor.uniqueActionAt == acceptedAt && actor.uniqueActionSequence == sequence &&
                        actor.cigarettesRemaining == remaining && actor.uniqueActionCancelled,
                        "Cancelling and re-equipping must neither bypass the cooldown nor consume another cigarette.");
                    rules.State.elapsed = acceptedAt + (role == "operator" ? 7f : 2.5f);
                    Require(rules.UseUnique(actor.id) == null && !actor.uniqueActionCancelled && actor.uniqueActionSequence == sequence + 1 &&
                        actor.cigarettesRemaining == remaining - (role == "operator" ? 1 : 0),
                        "The next valid use must start a fresh presentation after the original cooldown.");
                }

            var attackRules = new DeadAirRules(world, "host", "Downed action validation", "operator", false, true);
            Require(attackRules.AddPlayer("guest", "Reviver", "engineer"), "Could not add a reviver.");
            attackRules.Start("host");
            var downed = attackRules.State.Player("host");
            attackRules.EquipUnique(downed.id, true);
            Require(attackRules.UseUnique(downed.id) == null, "Could not begin an action before the attack.");
            downed.invulnerableUntil = 0;
            AimEnemyAt(attackRules, downed, 0);
            attackRules.Tick(.01f);
            Require(downed.down && downed.uniqueActionCancelled && !downed.uniqueItemEquipped,
                "An enemy attack must cancel the downed player's personal action.");
            Require(attackRules.Interact("guest", "revive:host") == null && !downed.down, "The companion must be able to revive the player.");
            attackRules.EquipUnique(downed.id, true);
            Require(downed.uniqueActionCancelled && attackRules.UseUnique(downed.id) != null && downed.uniqueActionSequence == 1 &&
                downed.cigarettesRemaining == 11 && downed.uniqueActionAt == 0,
                "Reviving and re-equipping must preserve cancellation, the original cooldown and consumed cigarette.");
        }

        static void ValidateRoleChangesAndRestrictions(WorldData world)
        {
            var rules = new DeadAirRules(world, "host", "Role validation", "engineer", true, true);
            var actor = rules.State.Player("host");
            rules.EquipUnique("host", true);
            Require(!actor.uniqueItemEquipped && rules.UseUnique("host") != null, "Role items must be unavailable in the lobby.");
            rules.SetClass("host", "operator");
            Require(actor.characterClass == "operator" && actor.cigarettesRemaining == 12, "Lobby class changes must initialize the role item.");
            rules.Start("host");
            rules.SetClass("host", "engineer");
            Require(actor.characterClass == "operator", "Changing role during a round must be rejected.");
            rules.State.paused = true;
            rules.EquipUnique("host", true);
            Require(!actor.uniqueItemEquipped && rules.UseUnique("host") != null, "Paused players cannot equip or use personal items.");
            rules.State.paused = false;
            actor.down = true;
            rules.EquipUnique("host", true);
            Require(!actor.uniqueItemEquipped && rules.UseUnique("host") != null, "Downed players cannot equip or use personal items.");
            actor.down = false;
            rules.EquipUnique("host", true);
            var locker = world.lockers[0];
            actor.x = locker.x; actor.y = locker.y; actor.z = locker.z;
            Require(rules.Interact("host", locker.id) == null && !actor.uniqueItemEquipped && !actor.mapOpen, "Hiding must stow equipment.");
            rules.EquipUnique("host", true);
            Require(!actor.uniqueItemEquipped && rules.UseUnique("host") != null, "Hidden players cannot equip or use personal items.");
            rules.Move("host", new NetPacket { x = actor.x, y = actor.y, z = actor.z, mapOpen = true, camera = true });
            Require(!actor.mapOpen && !actor.camera, "Hidden players cannot publish a raised map or camera.");
            Require(actor.cigarettesRemaining == 12 && actor.uniqueActionSequence == 0, "Rejected actions must not consume items or play actions.");
        }

        static void ValidateInventoryAndSerialization(WorldData world)
        {
            var rules = Started(world, "operator");
            var actor = rules.State.Player("host");
            foreach (string id in new[] { "tape-1", "tape-2", "tape-3", "fuse-1", "fuse-2", "keycard-guard" })
                actor.inventory.Add(id);
            rules.SelectSlot("host", 100);
            Require(actor.selectedSlot == actor.inventory.Count - 1 && actor.selectedSlot > 4, "Every inventory item must be selectable.");
            Require(rules.Drop("host", actor.inventory[actor.selectedSlot]) == null && actor.selectedSlot == actor.inventory.Count - 1,
                "Dropping the final selected item must leave a valid selection.");
            rules.SelectSlot("host", -1);
            Require(actor.selectedSlot == 0, "Negative inventory selection must be clamped.");
            rules.EquipUnique("host", true);
            Require(rules.UseUnique("host") == null, "Serialization setup failed.");
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State)).Player("host");
            Require(copy.uniqueItemEquipped && copy.uniqueActionSequence == 1 && copy.uniqueActionAt == 0 && copy.cigarettesRemaining == 11,
                "Role-item state must survive network snapshot serialization.");
            Require(!copy.uniqueActionCancelled, "An accepted active presentation must survive network snapshots.");
            var movement = new NetPacket { type = "move", x = actor.x, y = actor.y, z = actor.z, mapOpen = true };
            rules.Move("host", movement);
            Require(actor.mapOpen && !actor.uniqueItemEquipped, "Raising a map must stow the unique item.");
            copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State)).Player("host");
            Require(copy.mapOpen, "Map state must survive network snapshot serialization.");
            var packet = JsonUtility.FromJson<NetPacket>(JsonUtility.ToJson(new NetPacket { type = "unique-equip", uniqueEquipped = true, mapOpen = true }));
            Require(packet.uniqueEquipped && packet.mapOpen, "Equipment commands must survive network packet serialization.");
            rules.EquipUnique("host", true);
            Require(actor.uniqueItemEquipped && !actor.mapOpen && !actor.camera, "Equipping a personal item must lower the map and camera.");
        }

        static void ValidateFuelCanisterPresentation(WorldData world)
        {
            var firstPersonMode = typeof(DeadAirFirstPersonRig).GetMethod("InventoryMode", BindingFlags.NonPublic | BindingFlags.Static);
            var thirdPersonMode = typeof(DeadAirCharacterMotion).GetMethod("InventoryMode", BindingFlags.NonPublic | BindingFlags.Static);
            Require(firstPersonMode != null && thirdPersonMode != null, "Missing equipped item presentation mapping.");
            var rules = Started(world, "operator");
            var actor = rules.State.Player("host");
            foreach (string id in new[] { "fuel-canister", "fuel-canister-2", "fuel-canister-3" })
            {
                Require(InteractAt(rules, world, id) == null, "Could not collect " + id + " for presentation validation.");
                rules.SelectSlot(actor.id, actor.inventory.IndexOf(id));
                Require(actor.SelectedItem == id, "The selected fuel slot must retain its item identity: " + id);
                var selected = new object[] { actor.SelectedItem };
                Require((string)firstPersonMode.Invoke(null, selected) == "fuel-canister", id + " must show a canister in the owner's hands.");
                Require((string)thirdPersonMode.Invoke(null, selected) == "fuel-canister", id + " must show a canister to other players.");
            }
        }

        static void CameraMove(DeadAirRules rules, PlayerState actor, bool flashlight, bool camera, float yaw = 0)
        {
            rules.Move(actor.id, new NetPacket { type = "move", x = actor.x, y = actor.y, z = actor.z, yaw = yaw,
                flashlight = flashlight, camera = camera });
        }

        static void AimEnemyAt(DeadAirRules rules, PlayerState actor, float distance = 4)
        {
            rules.State.enemy.active = true;
            rules.State.enemy.x = actor.x; rules.State.enemy.y = actor.y;
            rules.State.enemy.z = actor.z - distance;
        }

        static void ValidateCameraFlashlightStun(WorldData world)
        {
            var rules = new DeadAirRules(world, "host", "Camera validation", "journalist", true, false);
            rules.Start("host");
            var actor = rules.State.Player("host");
            AimEnemyAt(rules, actor);
            CameraMove(rules, actor, false, false);
            rules.Tick(.1f);
            Require(!rules.State.enemy.stunned, "The enemy must move when neither camera nor flashlight is raised.");
            AimEnemyAt(rules, actor);
            CameraMove(rules, actor, true, false);
            rules.Tick(.1f);
            Require(actor.flashlight && !rules.State.enemy.stunned, "A flashlight alone must not stun the enemy.");
            AimEnemyAt(rules, actor);
            CameraMove(rules, actor, false, true);
            rules.Tick(.1f);
            Require(actor.camera && !actor.flashlight && !rules.State.enemy.stunned, "A dark camera must not stun the enemy.");
            AimEnemyAt(rules, actor);
            CameraMove(rules, actor, true, true);
            rules.Tick(.1f);
            Require(!rules.State.enemy.stunned&&rules.State.enemy.heardUntil==0,
                "A blind enemy must neither see the camera beam nor gain a target from stationary equipment packets.");
            CameraMove(rules, actor, false, true);
            rules.Tick(.1f);
            Require(!rules.State.enemy.stunned, "Light toggles must not stun a blind enemy.");
            AimEnemyAt(rules, actor);
            CameraMove(rules, actor, true, true, Mathf.PI);
            rules.Tick(.1f);
            Require(!rules.State.enemy.stunned, "A camera aimed away from the enemy must not stun it.");
            AimEnemyAt(rules, actor, 10);
            CameraMove(rules, actor, true, true);
            rules.Tick(.1f);
            Require(!rules.State.enemy.stunned, "Camera stun must respect the nine metre range.");
            AimEnemyAt(rules, actor);
            CameraMove(rules, actor, true, true);
            rules.Tick(.1f);
            Require(!rules.State.enemy.stunned, "A blind enemy must ignore a nearby camera and light.");
            Require(rules.Drop(actor.id, "flashlight") == null && !actor.flashlight, "Dropping the flashlight must turn it off.");
            CameraMove(rules, actor, true, true);
            rules.Tick(.1f);
            Require(actor.camera && !actor.flashlight && !rules.State.enemy.stunned,
                "A client cannot relight a lost flashlight or stun with an unlit camera.");

            var engineer=Started(world,"engineer");
            var defender=engineer.State.Player("host");defender.invulnerableUntil=9999;
            AimEnemyAt(engineer,defender,1.5f);
            engineer.EquipUnique(defender.id,true);
            Require(engineer.UseUnique(defender.id)==null,"Engineer defence action should be accepted.");
            var before=new Vector2(engineer.State.enemy.x,engineer.State.enemy.z);
            engineer.Tick(.1f);
            Require(engineer.State.enemy.stunned&&before==new Vector2(engineer.State.enemy.x,engineer.State.enemy.z),
                "An explicit close-range engineer action must apply a timed stun.");
            for(int tick=0;tick<30;tick++)engineer.Tick(.1f);
            Require(!engineer.State.enemy.stunned,"An engineer stun must expire without camera/light toggles.");
        }

        static void ValidatePausedMapAndLateJoin(WorldData world)
        {
            var rules = new DeadAirRules(world, "host", "Map validation", "operator", false, true);
            var actor = rules.State.Player("host");
            rules.EquipMap(actor.id, true);
            Require(!actor.mapOpen, "Maps must remain stowed in the lobby.");
            rules.Start("host");
            rules.EquipUnique(actor.id, true);
            Require(actor.uniqueItemEquipped, "Map setup must have a visible personal item.");
            rules.Pause(actor.id, true);
            Require(rules.State.paused, "A host alone in a multiplayer room must be able to pause.");
            rules.EquipMap(actor.id, true);
            Require(actor.mapOpen && !actor.camera && !actor.uniqueItemEquipped && !actor.flashlight && rules.State.paused,
                "Opening a map during a solo pause must publish the map and stow every other held item.");
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State)).Player(actor.id);
            Require(copy.mapOpen && !copy.flashlight, "The paused map state must survive network snapshots.");
            var command = JsonUtility.FromJson<NetPacket>(JsonUtility.ToJson(new NetPacket { type = "map-equip", mapOpen = true }));
            Require(command.type == "map-equip" && command.mapOpen, "Map equipment commands must survive network serialization.");
            rules.ClearDirty();
            rules.EquipMap(actor.id, false);
            Require(!actor.mapOpen && !actor.flashlight && rules.State.paused && rules.Dirty,
                "Closing a paused map must publish the stowed state without relighting the flashlight.");
            rules.EquipMap(actor.id, true);
            Require(rules.AddPlayer("guest", "Late guest", "engineer"), "A guest must be able to join an active room.");
            Require(!rules.State.paused && actor.mapOpen && rules.State.players.Count == 2,
                "A late guest must clear the obsolete global pause while the host's map remains raised.");
            float before = rules.State.elapsed;
            rules.Tick(.1f);
            Require(rules.State.elapsed > before, "An active multiplayer room must resume simulation after a late join.");
            rules.Pause(actor.id, true);
            Require(!rules.State.paused, "A host with a connected guest must not globally pause the round.");
            rules.Move(actor.id, new NetPacket { x = actor.x, y = actor.y, z = actor.z, mapOpen = true, camera = true, flashlight = true });
            Require(actor.mapOpen && !actor.camera && !actor.flashlight,
                "Movement packets must not raise a camera or light behind an equipped map.");
            rules.EquipMap(actor.id, false);
            actor.down = true;
            rules.EquipMap(actor.id, true);
            Require(!actor.mapOpen, "Downed players must not equip a map.");
            actor.down = false;
            var locker = world.lockers[0];
            actor.x = locker.x; actor.y = locker.y; actor.z = locker.z;
            Require(rules.Interact(actor.id, locker.id) == null, "Could not enter a locker for map validation.");
            rules.EquipMap(actor.id, true);
            Require(!actor.mapOpen, "Hidden players must not equip a map.");
        }
    }
}
