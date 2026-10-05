using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirMushroomValidation
    {
        [MenuItem("Dead Air/Validate Psychedelic Mushrooms")]
        public static void Validate()
        {
            ValidatePlacements();
            ValidateAuthorityAndConsumption();
            ValidateGuardsAndSightline();
            ValidateTimingAndLifecycle();
            ValidatePeerAndSerialization();
            ValidateGeometry();
            Debug.Log("DEAD AIR MUSHROOM RULES PASSED: nine deterministic walkable placements, spawn access, host-authoritative one-use meals, lobby/pause/down/hiding/range/floor/wall guards, 22s onset/fade and 35s total cap, pause/death/restart cleanup, peer ownership and snapshot serialization, outward faceted geometry without movement colliders.");
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Mushroom validation: " + message); }

        static WorldData IsolatedWorld()
        {
            return new WorldData { spawn = new PointData { x = 80, z = 70 },
                courtyard = new BoundsData { minX = 60, maxX = 95, minZ = 55, maxZ = 95 },
                rooms = Array.Empty<RoomData>(), stairs = Array.Empty<StairData>(),
                doors = Array.Empty<DoorData>(), lockers = Array.Empty<LockerData>(),
                objects = Array.Empty<ObjectiveData>(), props = Array.Empty<PropData>(), classes = WorldData.Load().classes };
        }

        static DeadAirRules Start(WorldData world, bool solo = true, bool creative = false)
        {
            var rules = new DeadAirRules(world, "host", "Mushroom host", "engineer", solo, creative);
            if (!solo) Require(rules.AddPlayer("guest", "Mushroom guest", "operator"), "Guest setup failed.");
            Require(rules.Start("host") == null, "Round setup failed.");
            foreach (var player in rules.State.players) player.invulnerableUntil = 9999;
            return rules;
        }

        static void Approach(PlayerState actor, MushroomData mushroom)
        { actor.x = mushroom.x; actor.y = mushroom.y; actor.z = mushroom.z + .8f; }

        static void ValidatePlacements()
        {
            var world = WorldData.Load(); var first = world.Mushrooms;
            var other = DeadAirMushrooms.CreatePlacements(world);
            Require(first.Length == 9 && first.Select(item => item.id).Distinct().Count() == first.Length,
                "The office must have nine distinct mushroom patches.");
            for (int i = 0; i < first.Length; i++)
            {
                var a = first[i]; var b = other[i];
                Require(a.id == b.id && a.x == b.x && a.y == b.y && a.z == b.z,
                    "Independent hosts and clients must generate identical placements.");
                Require(world.navigationNodes.Any(node => Mathf.Abs(node.x - a.x) < .001f &&
                    Mathf.Abs(node.y - a.y) < .001f && Mathf.Abs(node.z - a.z) < .001f),
                    "Every patch must use an authored walkable navigation point.");
                Require(!world.stairs.Any(stair => stair.Contains(a.x, a.z)), "Fungi must not occupy stair treads.");
            }
            var arrival = first[0];
            Require(Vector3.Distance(new Vector3(arrival.x, arrival.y, arrival.z),
                new Vector3(world.spawn.x, world.spawn.y, world.spawn.z)) < 2,
                "The first mushroom must be directly accessible beside spawn.");
            Require(first.Any(item => item.y > 4), "The second floor needs its own fungus patches.");
        }

        static void ValidateAuthorityAndConsumption()
        {
            var world = IsolatedWorld(); var mushroom = world.Mushrooms[0];
            var rules = new DeadAirRules(world, "host", "Mushroom host", "engineer", true, false);
            var actor = rules.State.Player("host"); Approach(actor, mushroom);
            Require(rules.Interact(actor.id, mushroom.id) != null && actor.mushroomTripSerial == 0,
                "Lobby commands must not begin a trip.");
            rules.Start("host"); Approach(actor, mushroom); actor.sanity = 70;
            rules.State.enemy.active = true; rules.State.enemy.x = actor.x + 5;
            rules.State.enemy.y = actor.y; rules.State.enemy.z = actor.z;
            Require(rules.Interact(actor.id, mushroom.id) == null && rules.State.Collected(mushroom.id),
                "An accepted host meal must collect the patch exactly once.");
            Require(actor.mushroomTripSerial == 1 && actor.mushroomTripUntil == 22 && actor.sanity == 64,
                "The host meal must publish its duration, event counter and modest sanity cost.");
            Require(rules.State.enemy.heardUntil == 6 && rules.State.enemy.heardX == actor.x,
                "Eating must attract an active nearby blind enemy to the sound's actual location.");
            float until = actor.mushroomTripUntil;
            Require(rules.Interact(actor.id, mushroom.id) != null && actor.mushroomTripSerial == 1 &&
                actor.mushroomTripUntil == until, "Repeated or racing meal commands must not extend the effect.");
            Require(rules.Interact(actor.id, "mushroom-forged") != null && actor.mushroomTripSerial == 1,
                "Forged patch IDs must not be accepted.");
            Require(actor.inventory.Count == 2 && !actor.inventory.Contains(mushroom.id),
                "Ground meals must not become inventory or quest items.");

            rules = Start(world, creative: true); actor = rules.State.Player("host"); Approach(actor, mushroom);
            rules.State.enemy.active = true; rules.State.enemy.x = actor.x; rules.State.enemy.z = actor.z;
            Require(rules.Interact(actor.id, mushroom.id) == null && actor.sanity == 100 &&
                rules.State.enemy.heardUntil == 0, "Creative mode must permit the effect without adding threat or sanity loss.");
        }

        static void ValidateGuardsAndSightline()
        {
            var world = IsolatedWorld(); var rules = Start(world); var mushroom = world.Mushrooms[0];
            var actor = rules.State.Player("host");
            Approach(actor, mushroom); actor.x += 12;
            Require(rules.Interact(actor.id, mushroom.id) != null, "Out-of-range meals must be rejected.");
            Approach(actor, mushroom); actor.y += 4.2f;
            Require(rules.Interact(actor.id, mushroom.id) != null, "Meals across floors must be rejected.");
            Approach(actor, mushroom); actor.down = true;
            Require(rules.Interact(actor.id, mushroom.id) != null, "Downed operators cannot eat."); actor.down = false;
            actor.hiding = "verification-locker";
            Require(rules.Interact(actor.id, mushroom.id) != null, "Hidden operators cannot eat."); actor.hiding = null;
            rules.Pause("host", true);
            Require(rules.Interact(actor.id, mushroom.id) != null, "Paused rounds cannot eat."); rules.Pause("host", false);
            var blocker = new GameObject("shell");
            try
            {
                blocker.transform.position = new Vector3(-mushroom.x, mushroom.y + .93f, mushroom.z + .4f);
                blocker.AddComponent<BoxCollider>().size = new Vector3(1.4f, 1.4f, .15f);
                Physics.SyncTransforms();
                Require(rules.Interact(actor.id, mushroom.id) != null && !rules.State.Collected(mushroom.id),
                    "A wall must reject a meal even when the patch is within reach.");
            }
            finally { UnityEngine.Object.DestroyImmediate(blocker); Physics.SyncTransforms(); }
            Require(rules.Interact(actor.id, mushroom.id) == null, "Clearing the wall must make the same meal valid.");
        }

        static void ValidateTimingAndLifecycle()
        {
            var world = IsolatedWorld(); var rules = Start(world); var actor = rules.State.Player("host");
            Approach(actor, world.Mushrooms[0]); rules.Interact(actor.id, world.Mushrooms[0].id);
            Require(DeadAirMushrooms.Intensity(rules.State, actor) == 0, "A trip must ease in from zero.");
            rules.State.elapsed = 2;
            Require(DeadAirMushrooms.Intensity(rules.State, actor) > .99f && DeadAirMushrooms.Phase(rules.State, actor) == 2,
                "The presentation clock must use authoritative elapsed time.");
            rules.Pause("host", true); rules.Tick(.25f);
            Require(rules.State.elapsed == 2 && DeadAirMushrooms.Intensity(rules.State, actor) == 0,
                "Pause must hide the presentation and freeze the trip clock."); rules.Pause("host", false);
            Approach(actor, world.Mushrooms[1]); rules.Interact(actor.id, world.Mushrooms[1].id);
            Require(actor.mushroomTripSerial == 2 && actor.mushroomTripStartedAt == 0 && actor.mushroomTripUntil == 35,
                "An additional meal must extend the trip with a 35-second total cap.");
            rules.State.elapsed = 33;
            float fading = DeadAirMushrooms.Intensity(rules.State, actor);
            Require(fading > 0 && fading < 1, "The final four seconds must ease back to normal.");
            rules.State.elapsed = 35; rules.Tick(.1f);
            Require(DeadAirMushrooms.Intensity(rules.State, actor) == 0 && DeadAirMushrooms.Remaining(rules.State, actor) == 0,
                "The effect must expire without touching movement controls.");
            actor.mushroomTripUntil = 80; actor.down = true; rules.Tick(.1f);
            Require(actor.mushroomTripUntil == 0, "Downed state must clear the trip before a revive.");
            rules.State.phase = "lost"; rules.Restart("host"); actor = rules.State.Player("host");
            Require(actor.mushroomTripSerial == 0 && actor.mushroomTripUntil == 0 && !rules.State.Collected(world.Mushrooms[0].id),
                "Restart must reset effect events and regrow every patch.");
            Require(DeadAirMushrooms.Intensity(null, actor) == 0 && DeadAirMushrooms.Intensity(rules.State, null) == 0,
                "Leaving or losing the local player must clear presentation immediately.");
        }

        static void ValidatePeerAndSerialization()
        {
            var world = IsolatedWorld(); var rules = Start(world, false); var mushroom = world.Mushrooms[0];
            var guest = rules.State.Player("guest"); var host = rules.State.Player("host"); Approach(guest, mushroom);
            Require(rules.Interact(guest.id, mushroom.id) == null && guest.mushroomTripSerial == 1 && host.mushroomTripSerial == 0,
                "A guest meal must affect only the requesting operator.");
            Approach(host, mushroom);
            Require(rules.Interact(host.id, mushroom.id) != null && host.mushroomTripSerial == 0,
                "A patch eaten by a guest must be unavailable to the host.");
            rules.State.elapsed = 2;
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State));
            var copiedGuest = copy.Player(guest.id);
            Require(copy.Collected(mushroom.id) && copiedGuest.mushroomTripSerial == 1 &&
                copiedGuest.mushroomTripStartedAt == 0 && copiedGuest.mushroomTripUntil == 22 &&
                DeadAirMushrooms.Intensity(copy, copiedGuest) > .99f,
                "Consumption and presentation counters/timing must survive LAN snapshots.");
        }

        static void ValidateGeometry()
        {
            var root = new GameObject("Mushroom geometry validation");
            try
            {
                var session = root.AddComponent<DeadAirSession>(); var world = IsolatedWorld(); session.Initialize(world);
                var presentation = root.AddComponent<DeadAirMushrooms>(); presentation.Initialize(world, session);
                Require(root.GetComponentsInChildren<Collider>(true).Length == 0,
                    "Fungi must not block operators, doors or enemy navigation.");
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                Require(filters.Length == world.Mushrooms.Length * 12, "Every patch must have three complete original mushrooms.");
                var cap = filters.First(filter => filter.gameObject.name == "Cap").sharedMesh;
                Require(cap.vertices.Length < 400 && cap.normals.Any(normal => normal.y > .75f),
                    "Cap geometry must remain faceted and face outward for lighting.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
