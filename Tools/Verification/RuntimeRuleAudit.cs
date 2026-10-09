using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DeadAir;
using UnityEngine;

public static class RuntimeRuleAudit
{
    static string Output
    {
        get
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-deadAirGeometryAudit");
            string directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GeometryAudit"));
            return Path.Combine(directory, "RulesReport.txt");
        }
    }
    static readonly List<string> Lines = new List<string>();
    static int Passes, Failures;

    static void Check(string label, bool ok, string detail = "")
    {
        if (ok) Passes++; else Failures++;
        Lines.Add((ok ? "PASS " : "FAIL ") + label + (detail.Length == 0 ? "" : " | " + detail));
    }

    static DeadAirRules Fresh(WorldData world)
    {
        var rules = new DeadAirRules(world, "audit", "Rule auditor", "operator", false, true, movementTime: () => 0);
        Check("Coop participant joins", rules.AddPlayer("peer", "Peer", "engineer"));
        Check("Chapter starts", rules.Start("audit") == null && rules.State.phase == "playing");
        return rules;
    }

    // Positions are set to the real target's interaction point so these are
    // deterministic rule tests. No inventory, fuel, quest or power gates are bypassed.
    static string Use(DeadAirRules rules, WorldData world, string id, string actor = "audit")
    {
        var objective = world.Objective(id);
        if (objective == null) return "Missing real world objective: " + id;
        var player = rules.State.Player(actor);
        player.x = objective.x;
        player.y = objective.y - 1.4f;
        player.z = objective.z;
        return rules.Interact(actor, id);
    }

    static string ErrorText(string error) { return error ?? "accepted"; }

    static void FuelPowerBreaker(WorldData world)
    {
        var rules = Fresh(world);
        string error = Use(rules, world, "power");
        Check("Generator rejects zero canisters", error != null && !rules.State.power && !rules.State.Collected("power"), ErrorText(error));
        string[] fuels = { "fuel-canister", "fuel-canister-2", "fuel-canister-3" };
        for (int i = 0; i < fuels.Length; i++)
        {
            string id = fuels[i];
            error = Use(rules, world, id);
            Check("Collect " + id, error == null && rules.State.Player("audit").HasItem(id) && rules.State.canisters.Count == i + 1, ErrorText(error));
            Check("Fuel readiness at " + (i + 1) + "/3", rules.State.canister == (i == 2) && rules.State.quest.fuelAdded == (i == 2));
            error = Use(rules, world, id);
            Check("Repeated pickup does not duplicate " + id, error == null && rules.State.canisters.Count == i + 1 && rules.State.Player("audit").inventory.Count(x => x == id) == 1);
            if (i < 2)
            {
                error = Use(rules, world, "power");
                Check("Generator rejects " + (i + 1) + "/3", error != null && !rules.State.power && !rules.State.Collected("power"), ErrorText(error));
            }
        }
        error = Use(rules, world, "power");
        Check("Generator starts on three canisters without fuses", error == null && rules.State.power && rules.State.quest.power && rules.State.fuses.Count == 0 && rules.State.quest.stage == 3, ErrorText(error));
        error = Use(rules, world, "breaker");
        Check("Breaker rejects zero fuses", error != null && !rules.State.breakerOn, ErrorText(error));
        error = Use(rules, world, "fuse-1");
        Check("First fuse collects", error == null && rules.State.fuses.Count == 1, ErrorText(error));
        error = Use(rules, world, "breaker");
        Check("Breaker rejects one fuse", error != null && !rules.State.breakerOn, ErrorText(error));
        error = Use(rules, world, "fuse-2");
        Check("Second fuse collects", error == null && rules.State.fuses.Count == 2, ErrorText(error));
        error = Use(rules, world, "breaker");
        Check("Breaker starts with two fuses and power", error == null && rules.State.breakerOn && rules.State.quest.breakerOn, ErrorText(error));
        error = Use(rules, world, "breaker");
        Check("Breaker switches off again", error == null && !rules.State.breakerOn && !rules.State.quest.breakerOn, ErrorText(error));

        error = rules.Drop("audit", "fuel-canister-2");
        Check("Dropping second canister decrements global fuel", error == null && rules.State.canisters.Count == 2 && !rules.State.canister && !rules.State.quest.fuelAdded && !rules.State.Player("audit").HasItem("fuel-canister-2"), ErrorText(error));
        var dropped = rules.State.droppedItems.Find(x => x.itemId == "fuel-canister-2");
        Check("Dropped fuel has stable unique instance and correct item id", dropped != null && dropped.id.StartsWith("drop_") && dropped.itemId == "fuel-canister-2");
        if (dropped != null)
        {
            var peer = rules.State.Player("peer");
            peer.x = dropped.x; peer.y = dropped.y - .4f; peer.z = dropped.z;
            string target = "dropped:" + dropped.id;
            error = rules.Interact("peer", target);
            Check("Coop peer picks dropped canister and restores 3/3", error == null && peer.HasItem("fuel-canister-2") && rules.State.canisters.Count == 3 && rules.State.canister && rules.State.quest.fuelAdded && rules.State.droppedItems.Count == 0, ErrorText(error));
            error = rules.Interact("peer", target);
            Check("Consumed drop cannot duplicate canister", error != null && rules.State.canisters.Count == 3 && peer.inventory.Count(x => x == "fuel-canister-2") == 1, ErrorText(error));
        }
        var snapshot = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State));
        Check("Room snapshot replicates canister list/count and fuel flags", snapshot.canisters.Count == 3 && snapshot.canister && snapshot.quest.fuelAdded && snapshot.Player("peer").HasItem("fuel-canister-2"));

        var unpowered = Fresh(world);
        Use(unpowered, world, "fuse-1"); Use(unpowered, world, "fuse-2");
        error = Use(unpowered, world, "breaker");
        Check("Two fuses alone cannot power breaker", error != null && !unpowered.State.breakerOn && !unpowered.State.power, ErrorText(error));
    }

    static void Quest(WorldData world)
    {
        var rules = Fresh(world);
        Check("Quest begins at stage one", rules.State.quest.stage == 1);
        string error = Use(rules, world, "exit");
        Check("Escape rejects missing broadcast", error != null && rules.State.phase == "playing" && !rules.State.Collected("exit"), ErrorText(error));
        error = Use(rules, world, "broadcast");
        Check("Broadcast rejects missing tapes", error != null && !rules.State.broadcast && !rules.State.Collected("broadcast"), ErrorText(error));
        error = Use(rules, world, "tape-1");
        Check("First tape moves quest to stage two", error == null && rules.State.quest.stage == 2, ErrorText(error));
        Use(rules, world, "tape-2"); Use(rules, world, "tape-3");
        error = Use(rules, world, "broadcast");
        Check("Broadcast rejects missing generator power", error != null && !rules.State.broadcast && !rules.State.Collected("broadcast"), ErrorText(error));
        Use(rules, world, "fuel-canister"); Use(rules, world, "fuel-canister-2"); Use(rules, world, "fuel-canister-3");
        error = Use(rules, world, "power");
        Check("Powered quest advances to stage three", error == null && rules.State.quest.stage == 3, ErrorText(error));
        error = Use(rules, world, "broadcast");
        Check("Broadcast rejects uncalibrated frequency", error != null && !rules.State.broadcast && !rules.State.Collected("broadcast"), ErrorText(error));
        var console = world.Objective("broadcast");
        var actor = rules.State.Player("audit");
        actor.x = console.x; actor.y = console.y - 1.4f; actor.z = console.z;
        error = rules.Calibrate("audit", 95);
        Check("Wrong frequency rejected", error != null && !rules.State.calibrated, ErrorText(error));
        error = rules.Calibrate("audit", float.NaN);
        Check("Nonfinite frequency rejected", error != null && !rules.State.calibrated, ErrorText(error));
        error = rules.Calibrate("audit", 96.4f);
        Check("96.4 MHz accepted", error == null && rules.State.calibrated && rules.State.quest.calibrated, ErrorText(error));
        error = Use(rules, world, "broadcast");
        Check("Three tapes, power and calibration broadcast at stage four", error == null && rules.State.broadcast && rules.State.quest.broadcast && rules.State.quest.stage == 4, ErrorText(error));
        error = Use(rules, world, "exit");
        Check("Broadcast allows complete main quest", error == null && rules.State.phase == "won" && rules.State.quest.stage == 4 && !rules.State.enemy.active, ErrorText(error));
        error = rules.Restart("audit");
        Check("Restart clears fuel power tapes and quest", error == null && rules.State.phase == "playing" && rules.State.canisters.Count == 0 && rules.State.fuses.Count == 0 && rules.State.tapes.Count == 0 && !rules.State.canister && !rules.State.power && !rules.State.broadcast && rules.State.quest.stage == 1, ErrorText(error));
    }

    static void PresentationMapping()
    {
        foreach (string typeName in new[] { "DeadAir.DeadAirFirstPersonRig", "DeadAir.DeadAirCharacterMotion" })
        {
            Type type = typeof(DeadAirRules).Assembly.GetType(typeName);
            MethodInfo map = type == null ? null : type.GetMethod("InventoryMode", BindingFlags.Static | BindingFlags.NonPublic);
            Check(typeName + " mapping is available", map != null);
            if (map == null) continue;
            foreach (string id in new[] { "fuel-canister", "fuel-canister-2", "fuel-canister-3" })
            {
                var mode = (string)map.Invoke(null, new object[] { id });
                Check(typeName + " displays " + id + " as fuel", mode == "fuel-canister", "mode=" + mode);
            }
        }
    }

    public static void Run()
    {
        try
        {
            Lines.Add("Testing rebuilt v" + Application.version + " Assembly-CSharp.dll inside copied player with built WorldData.Load(), Unity math, physics and JsonUtility.");
            Lines.Add("Native current version: " + Application.version + "; Unity: " + Application.unityVersion);
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "build-info.json"));
            using (var manifest = File.OpenRead(manifestPath))
            using (var sha = SHA256.Create())
                Lines.Add("BUILD-INFO-SHA256: " + BitConverter.ToString(sha.ComputeHash(manifest)).Replace("-", ""));
            Lines.Add("Assembly path: " + typeof(DeadAirRules).Assembly.Location);
            var world = WorldData.Load();
            var fuels = world.objects.Where(x => x.id.StartsWith("fuel-canister")).ToArray();
            Check("Built world includes exactly three distinct fuel canisters", fuels.Length == 3 && fuels.Select(x => x.id).Distinct().Count() == 3, String.Join(", ", fuels.Select(x => x.id).ToArray()));
            FuelPowerBreaker(world);
            Quest(world);
            PresentationMapping();
        }
        catch (Exception error)
        {
            Failures++; Lines.Add("EXCEPTION " + error);
        }
        Lines.Insert(0, "Rule audit summary: " + Passes + " passed, " + Failures + " failed.");
        File.WriteAllLines(Output, Lines.ToArray());
        Debug.Log("RuntimeRuleAudit complete: " + Passes + " passed, " + Failures + " failed; " + Output);
    }
}
