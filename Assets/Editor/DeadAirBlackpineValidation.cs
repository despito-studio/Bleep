using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirBlackpineValidation
    {
        [MenuItem("Dead Air/Validate Blackpine Investigation")]
        public static void Validate()
        {
            var world = WorldData.Load();
            ValidateSoloArc(world);
            ValidateThreatAndCancellation(world);
            ValidateEvidenceVariants(world);
            ValidateCoopAndAnimals(world);
            ValidateChapterTwo(world);
            Debug.Log("DEAD AIR BLACKPINE PASSED: solo entry/fuel/evidence/power gates, frequency variants, resumable capture and operations, midpoint danger, pause/range/down/disconnect, coop handoff and rescue, snapshot serialization, all-crew escape and chapter two regression.");
        }
        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Blackpine: " + message);
        }
        static DeadAirRules New(WorldData world, bool solo = true, bool creative = true, string chapter = "blackpine")
        {
            var rules = new DeadAirRules(world, "host", "Host", "operator", solo, creative, chapter);
            if (!solo) Require(rules.AddPlayer("guest", "Guest", "journalist"), "Guest joins.");
            Require(rules.Start("host") == null, "Host starts.");
            foreach (var player in rules.State.players) player.invulnerableUntil = 100000;
            return rules;
        }
        static void At(PlayerState player, ObjectiveData objective)
        {
            player.x = objective.x; player.y = objective.y - 1; player.z = objective.z;
            player.down = player.ladderClimbing = false; player.hiding = null;
        }
        static void At(WorldData world, DeadAirRules rules, string id, string player = "host")
        {
            var objective = DeadAirBlackpine.Objective(world, id) ?? world.Objective(id);
            At(rules.State.Player(player), DeadAirBlackpine.EvidenceObjective(world, rules.State, objective));
        }
        static void Step(DeadAirRules rules, float seconds)
        {
            int ticks = Mathf.CeilToInt(seconds * 4);
            for (int i = 0; i < ticks; i++) rules.Tick(.25f);
        }
        static void Prepared(DeadAirRules rules)
        {
            rules.State.power = rules.State.breakerOn = true;
            foreach (var id in new[] { "fuse-1", "fuse-2" }) if (!rules.State.fuses.Contains(id)) rules.State.fuses.Add(id);
            foreach (var id in new[] { "tape-1", "tape-2", "tape-3" }) if (!rules.State.tapes.Contains(id)) rules.State.tapes.Add(id);
        }
        static void Intercept(WorldData world, DeadAirRules rules)
        {
            Prepared(rules); At(world, rules, "broadcast");
            Require(rules.Calibrate("host", DeadAirBlackpine.TargetFrequency(rules.State)) == null, "Correct carrier begins capture.");
            Step(rules, DeadAirBlackpine.CaptureSeconds + .5f);
            Require(rules.State.blackpine.intercepted && !rules.State.broadcast && rules.State.phase == "playing", "Interception is the midpoint, not escape.");
        }
        static void ValidateSoloArc(WorldData world)
        {
            var rules = New(world);
            var state = rules.State; var actor = state.Player("host");
            Require(DeadAirBlackpine.ObjectiveText(state).Contains("драбину"), "Journal begins with the ladder.");
            At(world, rules, "broadcast");
            Require(rules.Interact("host", "broadcast") != null && rules.Calibrate("host", 96.4f) != null, "Unpowered receiver is gated.");
            At(world, rules, "power"); Require(rules.Interact("host", "power") != null, "Generator requires fuel.");
            At(world, rules, "fuel-canister"); Require(rules.Interact("host", "fuel-canister") == null, "Fuel can be collected.");
            Require(state.canister && state.canisters.Count == 1, "Only one canister is required.");
            At(world, rules, "power"); Require(rules.Interact("host", "power") == null && state.power, "One canister starts generator.");
            foreach (var id in new[] { "fuse-1", "fuse-2" }) { At(world, rules, id); Require(rules.Interact("host", id) == null, "Fuse collected."); }
            foreach (var id in new[] { "tape-1", "tape-2", "tape-3" })
            {
                At(world, rules, id); Require(rules.Interact("host", id) == null, "Distinct evidence collected.");
                Require(state.blackpine.evidenceId == id && state.message.Contains(DeadAirBlackpine.EvidenceTitle(id)), "Each tape publishes its revelation.");
            }
            Require(state.blackpine.evidenceSerial == 3 && DeadAirBlackpine.Knowledge(state).Contains("Резонатор"), "All revelations remain in journal.");
            At(world, rules, "broadcast");
            Require(rules.Calibrate("host", DeadAirBlackpine.TargetFrequency(state)) != null, "Receiver needs physical breaker too.");
            At(world, rules, "breaker"); Require(rules.Interact("host", "breaker") == null && state.breakerOn, "Breaker powers receiver.");
            At(world, rules, "broadcast");
            Require(rules.Calibrate("host", float.NaN) != null && rules.Calibrate("host", float.PositiveInfinity) != null && rules.Calibrate("host", 88) != null, "Invalid/wrong frequencies rejected.");
            Require(rules.Calibrate("host", DeadAirBlackpine.TargetFrequency(state)) == null, "Matching carrier begins capture.");
            Step(rules, DeadAirBlackpine.CaptureSeconds + .5f);
            Require(state.blackpine.intercepted && !state.broadcast && state.phase == "playing", "Midpoint has two operations remaining.");
            At(world, rules, DeadAirBlackpine.ResonatorId);
            Require(rules.Interact("host", DeadAirBlackpine.ResonatorId) != null, "Antenna must be disconnected first.");
            At(world, rules, DeadAirBlackpine.AntennaId); Require(rules.Interact("host", DeadAirBlackpine.AntennaId) == null, "Any role starts antenna.");
            Step(rules, DeadAirBlackpine.AntennaSeconds + .5f);
            Require(state.blackpine.antennaDisconnected && !state.broadcast, "Antenna completion does not end chapter.");
            At(world, rules, "exit"); Require(rules.Interact("host", "exit") != null, "Cannot leave before resonator.");
            At(world, rules, DeadAirBlackpine.ResonatorId); Require(rules.Interact("host", DeadAirBlackpine.ResonatorId) == null, "Solo completes second operation sequentially.");
            int echoBeforeShutdown = state.blackpine.echoSerial;
            Step(rules, DeadAirBlackpine.ResonatorSeconds + .5f);
            Require(state.blackpine.resonatorStopped && state.broadcast && state.phase == "playing", "Final escape remains playable after source shutdown.");
            Require(state.blackpine.echoSerial == echoBeforeShutdown, "Resonator shutdown begins with silence.");
            Step(rules, 2); Require(state.blackpine.echoSerial == echoBeforeShutdown, "Final voice waits the full three seconds.");
            Step(rules, .75f); Require(state.blackpine.echoSerial == echoBeforeShutdown + 1, "Voice enters radio after three seconds, including exploration mode.");
            At(world, rules, "exit"); Require(rules.Interact("host", "exit") == null && state.phase == "departing", "Ready solo player can leave.");
            Step(rules, DeadAirVanPresentation.DepartureDuration + .5f); Require(state.phase == "won", "Departure completes victory.");
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(state));
            Require(copy.blackpine.intercepted && copy.blackpine.antennaDisconnected && copy.blackpine.resonatorStopped && copy.blackpine.evidenceSerial == 3, "Chapter events survive snapshots.");
            state.phase = "lost"; rules.Restart("host");
            Require(!state.blackpine.intercepted && state.blackpine.interceptProgress == 0 && !state.broadcast, "Restart clears progress/events.");
            var variants = new[] { "A", "B", "C" }.Select(code => DeadAirBlackpine.TargetFrequency(new RoomState { code = code })).Distinct().Count();
            Require(variants == 3, "Room codes select three deterministic frequency variants.");
        }
        static void ValidateThreatAndCancellation(WorldData world)
        {
            var rules = New(world, true, false); var state = rules.State; var actor = state.Player("host");
            Step(rules, 45); Require(!state.enemy.active, "No automatic 35-second lethal spawn.");
            At(world, rules, "tape-1"); rules.Interact("host", "tape-1"); Step(rules, 1);
            Require(!state.enemy.active, "First evidence builds tension without early lethal enemy.");
            Prepared(rules); At(world, rules, "broadcast");
            Require(rules.Calibrate("host", DeadAirBlackpine.TargetFrequency(state)) == null, "Capture starts.");
            Step(rules, 2); float saved = state.blackpine.interceptProgress;
            rules.Pause("host", true); Step(rules, 2); Require(state.blackpine.interceptProgress == saved, "Solo pause freezes operation.");
            rules.Pause("host", false); actor.x += 6; Step(rules, 1);
            Require(state.blackpine.interceptProgress == saved && state.blackpine.operationPlayerId == null, "Leaving receiver cancels operator and preserves progress.");
            Require(rules.Interact("host", "broadcast") != null, "Remote capture rejected.");
            At(world, rules, "broadcast"); Require(rules.Interact("host", "broadcast") == null, "E resumes capture.");
            Step(rules, 7);
            Require(state.blackpine.intercepted && state.enemy.active && state.blackpine.echoSerial > 0 && !state.broadcast, "Interception activates danger and false voice.");
            At(world, rules, DeadAirBlackpine.AntennaId); Require(rules.Interact("host", DeadAirBlackpine.AntennaId) == null, "Antenna starts.");
            Step(rules, 1); saved = state.blackpine.antennaProgress; actor.down = true; Step(rules, 1);
            Require(state.blackpine.operationPlayerId == null && state.blackpine.antennaProgress == saved, "Downed operator stops work without losing progress.");
            state.phase = "playing"; actor.down = false; At(world, rules, DeadAirBlackpine.AntennaId); actor.x += 1.6f;
            var target = DeadAirBlackpine.Objective(world, DeadAirBlackpine.AntennaId);
            var wall = new GameObject("shell");
            try
            {
                wall.transform.position = new Vector3(-(actor.x + target.x) * .5f, target.y, target.z);
                wall.AddComponent<BoxCollider>().size = new Vector3(.15f, 4, 4);
                Physics.SyncTransforms();
                Require(rules.Interact("host", DeadAirBlackpine.AntennaId) != null, "Operations cannot be started through an office wall.");
            }
            finally { UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms(); }
        }
        static void ValidateCoopAndAnimals(WorldData world)
        {
            var rules = New(world, false); var state = rules.State; var host = state.Player("host"); var guest = state.Player("guest");
            Intercept(world, rules);
            At(world, rules, DeadAirBlackpine.AntennaId); At(world, rules, DeadAirBlackpine.AntennaId, "guest");
            Require(rules.Interact("host", DeadAirBlackpine.AntennaId) == null && rules.Interact("guest", DeadAirBlackpine.AntennaId) != null, "Only one valid operator owns the interaction.");
            Step(rules, 2); float saved = state.blackpine.antennaProgress;
            host.inventory.Add("key-basement"); rules.RemovePlayer("host");
            Require(state.blackpine.operationPlayerId == null && state.droppedItems.Any(drop => drop.itemId == "key-basement"), "Disconnect releases operation and critical key.");
            Require(rules.Interact("guest", DeadAirBlackpine.AntennaId) == null, "Guest takes over disconnected operator.");
            Step(rules, DeadAirBlackpine.AntennaSeconds - saved + .5f); Require(state.blackpine.antennaDisconnected, "Guest retains previous operator progress.");
            At(world, rules, DeadAirBlackpine.ResonatorId, "guest"); rules.Interact("guest", DeadAirBlackpine.ResonatorId); Step(rules, DeadAirBlackpine.ResonatorSeconds + .5f);
            Require(rules.AddPlayer("friend", "Friend", "engineer"), "A friend can join an active investigation.");
            state.Player("friend").x = 12; state.Player("friend").y = 0; state.Player("friend").z = 12;
            At(world, rules, "exit", "guest"); Require(rules.Interact("guest", "exit") != null, "Escape waits for every living friend.");
            var friend = state.Player("friend"); At(world, rules, "exit", "friend");
            var chicken = DeadAirFarmyard.Position(world, state, "farm-chicken");
            guest.x = chicken.x; guest.y = chicken.y; guest.z = chicken.z;
            Require(rules.Interact("guest", "farm-chicken") == null && state.blackpine.chickenCarrierId == "guest", "Chicken can be picked up.");
            Require(guest.selectedSlot == -1 && guest.SelectedItem == null, "Carrying chicken clears legacy and V2 equipment selection.");
            friend.x = guest.x; friend.y = guest.y; friend.z = guest.z;
            Require(rules.Interact("friend", "farm-chicken") == null && state.blackpine.chickenCarrierId == "friend", "Chicken passes to a nearby friend.");
            Require(guest.selectedSlot >= 0 && friend.selectedSlot == -1, "Handoff restores former carrier equipment and clears new carrier hands.");
            rules.SelectSlot("friend", friend.inventory.IndexOf("wrench"));
            rules.Move("friend", new NetPacket { x = friend.x, y = friend.y, z = friend.z, yaw = friend.yaw, pitch = friend.pitch,
                flashlight = true, camera = true, sprinting = true, mapOpen = true });
            Require(friend.selectedSlot == -1 && !friend.flashlight && !friend.camera && !friend.sprinting && !friend.uniqueItemEquipped && !friend.mapOpen,
                "Inventory selection and movement packets cannot re-equip light, role item, camera, map or sprint while carrying.");
            Require(rules.Drop("friend", "flashlight") != null && friend.selectedSlot == -1, "Generic inventory drops cannot bypass carrying equipment state.");
            Require(rules.Interact("friend", "farm-chicken-drop") == null && state.blackpine.chickenDropped && state.blackpine.chickenCarrierId == null, "G drops chicken without inventory hacks.");
            Require(friend.selectedSlot >= 0, "Putting chicken down restores a valid equipment slot.");
            Require(rules.Interact("guest", "farm-chicken") == null, "Dropped chicken can be picked up again.");
            rules.RemovePlayer("guest"); Require(state.blackpine.chickenCarrierId == null && state.blackpine.chickenDropped, "Carrier disconnect safely releases chicken.");
            Require(rules.Interact("friend", "farm-chicken") == null, "Chicken remains recoverable after disconnect.");
            friend.down = true; Step(rules, .25f); Require(state.blackpine.chickenCarrierId == null, "Downed carrier releases chicken.");
            friend.down = false; Require(rules.Interact("friend", "farm-chicken") == null, "Revived carrier can recover chicken.");
            At(world, rules, "exit", "friend"); Require(rules.Interact("friend", "exit") == null && state.blackpine.chickenRescued && friend.selectedSlot >= 0, "Chicken rescue is optional, recorded at departure and restores equipment selection.");
        }
        static void ValidateChapterTwo(WorldData world)
        {
            var rules = New(world, true, true, "coming-soon");
            foreach (string id in new[] { "tape-school-1", "tape-school-2", "tape-school-3", "tape-school-4", "tape-school-5" })
                Require(rules.Interact("host", id) == null, "School evidence flow is preserved.");
            Require(rules.State.tapes.Count == 5 && !rules.State.blackpine.intercepted && rules.Interact("host", "van-exit") == null && rules.State.phase == "won", "School still exits with five evidence tapes.");
            Require(rules.State.blackpine.evidenceSerial == 0, "Blackpine evidence/operations never enter chapter two.");
        }
        static void ValidateEvidenceVariants(WorldData world)
        {
            var original2 = world.Objective("tape-2"); var original3 = world.Objective("tape-3");
            float original2X = original2.x, original2Z = original2.z, original3X = original3.x, original3Z = original3.z;
            foreach (string code in new[] { "A", "B" })
            {
                var rules = New(world); rules.State.code = code;
                var evidence2 = DeadAirBlackpine.EvidenceObjective(world, rules.State, original2);
                var evidence3 = DeadAirBlackpine.EvidenceObjective(world, rules.State, original3);
                bool swapped = code == "A";
                Require(evidence2.id == original2.id && evidence2.label == original2.label && evidence2.type == original2.type &&
                    evidence3.id == original3.id && evidence3.label == original3.label && evidence3.type == original3.type,
                    "Evidence variation preserves identity, labels and recorded revelations.");
                Require(evidence2.x == (swapped ? original3X : original2X) && evidence2.z == (swapped ? original3Z : original2Z) &&
                    evidence3.x == (swapped ? original2X : original3X) && evidence3.z == (swapped ? original2Z : original3Z),
                    "Room seed deterministically swaps the two upstairs desks.");
                Require(ReferenceEquals(DeadAirBlackpine.EvidenceObjective(world, rules.State, world.Objective("tape-1")), world.Objective("tape-1")),
                    "First evidence remains in archive for both variants.");
                if (swapped)
                {
                    At(rules.State.Player("host"), original2);
                    Require(rules.Interact("host", "tape-2") != null && !rules.State.tapes.Contains("tape-2"), "Original desk cannot remotely collect relocated evidence.");
                }
                foreach (string id in new[] { "tape-2", "tape-3" })
                { At(world, rules, id); Require(rules.Interact("host", id) == null, "Both evidence variants are reachable through their shared helper coordinates."); }
            }
            Require(original2.x == original2X && original2.z == original2Z && original3.x == original3X && original3.z == original3Z,
                "Evidence variants never mutate shared authored world data.");
        }
    }
}
