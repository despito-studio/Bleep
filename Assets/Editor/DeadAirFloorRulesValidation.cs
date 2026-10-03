using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirFloorRulesValidation
    {
        [MenuItem("Dead Air/Validate Office Floor Rules")]
        public static void Validate()
        {
            var world = WorldData.Load();
            Require(world.stairs != null && world.stairs.Length > 0, "The office needs a connected stair flight.");
            Require(world.rooms.Any(room => room.floorY > 4), "Upper-floor rooms are missing.");
            ValidateVerticalSerialization();
            ValidateInteractionSightlines();
            ValidateFloorInteractions(world);
            ValidateLockerView(world);
            ValidateMovement(world);
            ValidateEnemyFloors(world);
            Debug.Log("DEAD AIR OFFICE RULES PASSED: vertical snapshots, wall/door sightlines, floor interaction boundaries, locker facing/look limits, dropped items, stair movement, upper-floor bounds and enemy floor separation.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Office floor validation: " + message);
        }

        static DeadAirRules Started(WorldData world, bool solo = true)
        {
            var rules = new DeadAirRules(world, "host", "Office validation", "engineer", solo, true);
            rules.Start("host");
            return rules;
        }

        static void ValidateVerticalSerialization()
        {
            var state = new RoomState();
            state.players.Add(new PlayerState { id = "host", y = 4.2f });
            state.enemy.y = 4.2f; state.enemy.radioY = 4.2f;
            state.droppedItems.Add(new DroppedItem { id = "drop", itemId = "flashlight", y = 4.2f });
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(state));
            Require(copy.Player("host").y == 4.2f && copy.enemy.y == 4.2f && copy.enemy.radioY == 4.2f &&
                copy.droppedItems[0].y == 4.2f, "All replicated actors and items must preserve floor height.");
            var packet = JsonUtility.FromJson<NetPacket>(JsonUtility.ToJson(new NetPacket { type = "move", y = 4.2f }));
            Require(packet.y == 4.2f, "Movement packets must preserve floor height.");
        }

        static void ValidateInteractionSightlines()
        {
            var barrier = new GameObject("shell");
            try
            {
                barrier.transform.position = new Vector3(73, 1.4f, 0);
                barrier.AddComponent<BoxCollider>().size = new Vector3(.2f, 2, 2);
                Physics.SyncTransforms();
                var origin = new Vector3(72, 1.4f, 0);
                var target = new Vector3(74, 1.4f, 0);
                Require(!DeadAirRules.HasInteractionSightline(origin, target, "tape"), "An office partition must block picking up items through it.");
                barrier.name = "Door frame validation-door";
                Require(!DeadAirRules.HasInteractionSightline(origin, target, "tape"), "A closed door must block interactions beyond it.");
                Require(DeadAirRules.HasInteractionSightline(origin, target, "validation-door"), "A door must remain directly usable from either side.");
            }
            finally { UnityEngine.Object.DestroyImmediate(barrier); }
        }

        static void ValidateFloorInteractions(WorldData world)
        {
            var rules = Started(world, false);
            Require(rules.AddPlayer("guest", "Other floor", "operator"), "Could not add a second operator.");
            var actor = rules.State.Player("host");
            var upperItem = world.Objective("tape-2");
            actor.x = upperItem.x; actor.z = upperItem.z; actor.y = 0;
            Require(rules.Interact(actor.id, upperItem.id) != null && !rules.State.Collected(upperItem.id),
                "An upper-floor item must not be reachable through the ceiling.");
            actor.y = 4.2f;
            Require(rules.Interact(actor.id, upperItem.id) == null && actor.HasItem(upperItem.id), "The same item must be collectible on its own floor.");
            Require(rules.Drop(actor.id, upperItem.id) == null, "An upper-floor item should be droppable.");
            var dropped = rules.State.droppedItems.Last();
            Require(dropped.y == 4.2f, "Dropped items must retain their floor.");
            actor.y = 0;
            Require(rules.Interact(actor.id, "dropped:" + dropped.id) != null, "Dropped items must not be picked up through the ceiling.");
            var guest = rules.State.Player("guest");
            guest.x = actor.x; guest.z = actor.z; guest.y = 4.2f; guest.down = true;
            Require(rules.Interact(actor.id, "revive:" + guest.id) != null && guest.down, "Reviving must respect the operator's floor.");
            actor.y = 4.2f;
            Require(rules.Interact(actor.id, "revive:" + guest.id) == null && !guest.down, "An operator on the same floor should be revivable.");
            var console = world.Objective("broadcast");
            actor.x = console.x; actor.z = console.z; actor.y = 0;
            Require(rules.Calibrate(actor.id, 96.4f) != null, "Calibration must not work from below the console.");
            actor.y = 4.2f;
            Require(rules.Calibrate(actor.id, 96.4f) == null, "Calibration must follow the relocated console.");
        }

        static void ValidateLockerView(WorldData world)
        {
            var rules = Started(world);
            var actor = rules.State.Player("host");
            var locker = world.lockers.First(item => item.y >= 4);
            actor.x = locker.exitX; actor.z = locker.exitZ; actor.y = locker.ExitFloorY;
            actor.yaw = locker.FacingYaw + Mathf.PI;
            Require(rules.Interact(actor.id, locker.id) == null, "An upper-floor locker should accept its nearby operator.");
            Require(actor.y == locker.y && actor.pitch == 0 && Mathf.Abs(Mathf.DeltaAngle(actor.yaw * Mathf.Rad2Deg,
                locker.FacingYaw * Mathf.Rad2Deg)) < .01f, "Entering a locker must face outward through its grille.");
            rules.Move(actor.id, new NetPacket { x = actor.x, y = actor.y, z = actor.z,
                yaw = locker.FacingYaw + 2, pitch = 1, flashlight = true, camera = true });
            Require(Mathf.Abs(Mathf.DeltaAngle(locker.FacingYaw * Mathf.Rad2Deg, actor.yaw * Mathf.Rad2Deg)) <= 18.01f &&
                Mathf.Abs(actor.pitch * Mathf.Rad2Deg) <= 14.01f && !actor.camera && !actor.flashlight,
                "Hidden movement packets must keep the view within the grille and equipment stowed.");
            Require(rules.Interact(actor.id, "exit-locker") == null && string.IsNullOrEmpty(actor.hiding) &&
                actor.x == locker.exitX && actor.z == locker.exitZ && actor.y == locker.ExitFloorY,
                "Exiting an upper-floor locker must preserve its floor.");
        }

        static void ValidateMovement(WorldData world)
        {
            var rules = Started(world);
            var actor = rules.State.Player("host");
            actor.x = 0; actor.y = 0; actor.z = -28;
            rules.Move(actor.id, new NetPacket { x = 0, y = 4.2f, z = -28 });
            Require(actor.y == 0, "A player must not teleport vertically through the floor.");
            rules.Move(actor.id, new NetPacket { x = 0, y = float.NaN, z = -28 });
            Require(actor.y == 0, "Non-finite vertical movement must be rejected.");
            var stair = world.stairs[0];
            float centerX = (stair.minX + stair.maxX) * .5f;
            float startZ = stair.maxZ - 2;
            actor.x = centerX; actor.z = startZ; actor.y = stair.HeightAt(startZ);
            float nextZ = startZ - .4f;
            rules.Move(actor.id, new NetPacket { x = centerX, y = stair.HeightAt(nextZ), z = nextZ });
            Require(Mathf.Abs(actor.z - nextZ) < .01f && Mathf.Abs(actor.y - stair.HeightAt(nextZ)) < .01f,
                "A short movement up the stair surface must be accepted.");
            actor.x = 17.7f; actor.z = 0; actor.y = 4.2f;
            rules.Move(actor.id, new NetPacket { x = 19, y = 4.2f, z = 0 });
            Require(actor.x == 17.7f, "An upper-floor player must stay inside its walkable floor.");
            Require(world.RoomAt(8, -2, 0) != world.RoomAt(8, -2, 4.2f), "Room labels must distinguish vertically overlapping offices.");
        }

        static void ValidateEnemyFloors(WorldData world)
        {
            var rules = Started(world);
            var actor = rules.State.Player("host");
            actor.x = 0; actor.z = -28; actor.y = 4.2f; actor.invulnerableUntil = 0;
            actor.camera = actor.flashlight = true; actor.yaw = 0;
            rules.State.enemy.active = true; rules.State.enemy.x = 0; rules.State.enemy.z = -28; rules.State.enemy.y = 0;
            rules.Tick(.01f);
            Require(!actor.down && !rules.State.enemy.stunned, "Enemies must neither attack nor be camera-stunned through an intervening floor.");
            actor.camera = actor.flashlight = false;
            actor.invulnerableUntil = 9999;
            rules.State.enemy.x = 0; rules.State.enemy.y = 0; rules.State.enemy.z = -34;
            for (int tick = 0; tick < 320; tick++) rules.Tick(.1f);
            Require(Mathf.Abs(rules.State.enemy.y - 4.2f) < .25f &&
                Vector2.Distance(new Vector2(actor.x, actor.z), new Vector2(rules.State.enemy.x, rules.State.enemy.z)) < 2.5f,
                "An enemy must complete the stair flight and reach the upper corridor over repeated simulation ticks.");
            actor.y = 0;
            for (int tick = 0; tick < 320; tick++) rules.Tick(.1f);
            Require(Mathf.Abs(rules.State.enemy.y) < .25f &&
                Vector2.Distance(new Vector2(actor.x, actor.z), new Vector2(rules.State.enemy.x, rules.State.enemy.z)) < 2.5f,
                "An enemy must find the upper landing by the side passage and descend to the lower corridor.");
        }
    }
}
