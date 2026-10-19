using System;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirSuppliesValidation
    {
        [MenuItem("Dead Air/Validate Drawers and Batteries")]
        public static void Validate()
        {
            var world = WorldData.Load();
            ValidateAllDrawers(world);
            ValidateLootAuthority(world);
            ValidateBatteryLife(world);
            ValidateSharedSupplyAndTransfer(world);
            ValidateSerializationResetAndBook(world);
            ValidateDrawerPresentation(world);
            Debug.Log("DEAD AIR SUPPLIES PASSED: all drawers, one-time loot, distance/closed/pause restrictions, finite lamp, manual reload, shared four van cells, drop transfers, snapshots, reset and readable prison book; drawer animation, rewind and idle transforms.");
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Supplies validation: " + message); }

        static DeadAirRules Started(WorldData world, bool solo = true)
        {
            var rules = new DeadAirRules(world, "host", "Supply validation", "engineer", solo, true);
            Require(rules.Start("host") == null, "Could not start the round.");
            rules.State.elapsed = DeadAirVanPresentation.ArrivalDuration + 1;
            return rules;
        }

        static void At(PlayerState player, float x, float y, float z)
        { player.x = x; player.y = worldFloor(y); player.z = z; }
        static float worldFloor(float y) => y < -2 ? -4.2f : y > 3 ? 4.2f : 0;

        static void ValidateAllDrawers(WorldData world)
        {
            var rules = Started(world);
            var actor = rules.State.Player("host");
            Require(world.Drawers.Length > 40, "Universal placements must include every desk and catalog drawer.");
            Require(rules.State.drawers.Count == world.Drawers.Length, "Every drawer needs a host state.");
            Require(DeadAirDrawers.LootForRoll(.499) == "empty" && DeadAirDrawers.LootForRoll(.5) == "coins" &&
                DeadAirDrawers.LootForRoll(.799) == "coins" && DeadAirDrawers.LootForRoll(.8) == DeadAirSupplies.BatteryItem,
                "Loot distribution must remain 50/30/20.");
            rules.ConfigureDrawerRollForVerification(.1f);
            var unique = new System.Collections.Generic.HashSet<string>();
            foreach (var data in world.Drawers)
            {
                Require(unique.Add(data.id), "Drawer IDs must be unique.");
                At(actor, data.x, data.y, data.z);
                Require(rules.Interact("host", data.id) == null, "Cannot open " + data.id);
                var state = rules.State.Drawer(data.id);
                Require(state.open && state.searched && state.loot == "empty", "Empty result must persist.");
                Require(rules.Interact("host", data.id) == null && !state.open, "Cannot close " + data.id);
                rules.ConfigureDrawerRollForVerification(.9f);
                Require(rules.Interact("host", data.id) == null && state.open && state.loot == "empty", "Reopening rerolled loot.");
                rules.ConfigureDrawerRollForVerification(.1f);
            }
            Require(rules.State.directorDrawerOpen && rules.State.quest.directorDrawerOpen, "Director key compatibility must track its drawer.");
        }

        static void ValidateLootAuthority(WorldData world)
        {
            var rules = Started(world, false);
            Require(rules.AddPlayer("guest", "Guest", "operator"), "Guest could not join.");
            var actor = rules.State.Player("host");
            var guest = rules.State.Player("guest");
            var data = world.Drawers[0];
            rules.ConfigureDrawerRollForVerification(.9f);
            Require(rules.Interact("guest", data.id) != null, "A remote player must not open drawers from the van.");
            At(actor, data.x, data.y, data.z); At(guest, data.x, data.y, data.z);
            Require(rules.Interact("host", data.id) == null, "Host could not open drawer.");
            Require(rules.Interact("guest", "drawer-loot:" + data.id) == null && guest.spareBatteries == 1, "Guest must collect shared loot.");
            Require(rules.Interact("host", "drawer-loot:" + data.id) != null && actor.spareBatteries == 0, "Two peers duplicated one battery.");
            data = world.Drawers[1]; rules.ConfigureDrawerRollForVerification(.6f);
            At(actor, data.x, data.y, data.z);
            Require(rules.Interact("host", data.id) == null, "Coin drawer cannot open.");
            Require(rules.Interact("host", data.id) == null, "Coin drawer cannot close.");
            Require(rules.Interact("host", "drawer-loot:" + data.id) != null, "Closed drawers must block pickup.");
            Require(rules.Interact("host", data.id) == null && rules.Interact("host", "drawer-loot:" + data.id) == null &&
                actor.coins >= 2 && actor.coins <= 6, "Coins must enter the collecting player's wallet.");
            int coins = actor.coins;
            Require(rules.Interact("host", "drawer-loot:" + data.id) != null && actor.coins == coins, "Repeated pickup minted coins.");
        }

        static void ValidateBatteryLife(WorldData world)
        {
            var rules = Started(world);
            rules.State.creative = false;
            rules.State.spawnCountdown = 99999;
            var actor = rules.State.Player("host");
            actor.flashlightCharge = .15f; actor.flashlight = true;
            rules.Tick(.1f);
            Require(actor.flashlightCharge > 0 && actor.flashlight, "Lamp exhausted too early.");
            rules.Tick(.1f);
            Require(actor.flashlightCharge == 0 && !actor.flashlight, "Empty battery must extinguish the lamp.");
            rules.Move("host", new NetPacket { x = actor.x, y = actor.y, z = actor.z, flashlight = true });
            Require(!actor.flashlight, "Movement packet relit a dead cell.");
            Require(rules.ReloadFlashlight("host") != null, "Reload succeeded without a spare.");
            var battery = world.Batteries[0]; At(actor, battery.x, battery.y, battery.z);
            Require(rules.Interact("host", battery.id) == null, "Could not obtain spare.");
            rules.Pause("host", true);
            Require(rules.ReloadFlashlight("host") != null && actor.spareBatteries == 1, "Reload consumed a spare while paused.");
            rules.Tick(.2f); Require(actor.flashlightCharge == 0, "Paused round drained a cell.");
            rules.Pause("host", false);
            Require(rules.ReloadFlashlight("host") == null && actor.flashlightCharge == DeadAirSupplies.FlashlightSeconds &&
                actor.spareBatteries == 0 && !actor.HasItem(DeadAirSupplies.BatteryItem) && actor.flashlightReloadSerial == 1,
                "Reload must consume one spare and fill the installed cell.");
            Require(rules.ReloadFlashlight("host") != null, "Duplicate reload packet consumed another battery.");
            actor.flashlight = false; float before = actor.flashlightCharge;
            rules.Tick(.25f); Require(actor.flashlightCharge == before, "Switched-off lamp drained charge.");
            actor.flashlight = true; rules.Pause("host", true); rules.Tick(.25f);
            Require(actor.flashlightCharge == before, "Solo modal pause drained charge.");
            rules.Pause("host", false); rules.SelectSlot("host", actor.inventory.IndexOf("wrench"));
            rules.Tick(.25f); Require(actor.flashlightCharge == before && !actor.flashlight, "Stowed lamp drained charge.");
            rules.State.creative = true; rules.SelectSlot("host", actor.inventory.IndexOf("flashlight"));
            actor.flashlight = true; rules.Tick(.25f);
            Require(actor.flashlightCharge < before, "Exploration must use finite flashlight batteries too.");
        }

        static void ValidateSharedSupplyAndTransfer(WorldData world)
        {
            var rules = Started(world, false);
            rules.AddPlayer("guest", "Guest", "operator");
            var actor = rules.State.Player("host"); var guest = rules.State.Player("guest");
            Require(world.Batteries.Length == 4, "Van must have exactly four shared spares.");
            foreach (var battery in world.Batteries)
            {
                At(actor, battery.x, battery.y, battery.z); At(guest, battery.x, battery.y, battery.z);
                Require(rules.Interact("host", battery.id) == null, "Van pickup failed.");
                Require(rules.Interact("guest", battery.id) != null, "Peer duplicated a van spare.");
            }
            Require(actor.spareBatteries == 4 && guest.spareBatteries == 0, "Van spares are not a shared finite stock.");
            Require(rules.Drop("host", DeadAirSupplies.BatteryItem) == null && actor.spareBatteries == 3, "Dropping must remove one stacked battery.");
            var dropped = rules.State.droppedItems[0]; At(guest, dropped.x, dropped.y, dropped.z);
            Require(rules.Interact("guest", "dropped:" + dropped.id) == null && guest.spareBatteries == 1, "Dropped battery transfer failed.");
            actor.flashlightCharge = 42;
            Require(rules.Drop("host", "flashlight") == null, "Cannot drop a lamp.");
            dropped = rules.State.droppedItems[0];
            Require(rules.Interact("guest", "dropped:" + dropped.id) != null && rules.State.droppedItems.Count == 1,
                "Owning a lamp must prevent deleting another player's dropped lamp.");
            Require(rules.Interact("host", "dropped:" + dropped.id) == null && actor.flashlightCharge == 42, "Drop/pickup refilled the lamp.");
        }

        static void ValidateSerializationResetAndBook(WorldData world)
        {
            var rules = Started(world);
            var actor = rules.State.Player("host"); var drawer = world.Drawers[0];
            rules.ConfigureDrawerRollForVerification(.9f); At(actor, drawer.x, drawer.y, drawer.z);
            rules.Interact("host", drawer.id); rules.Interact("host", "drawer-loot:" + drawer.id);
            actor.coins = 7; actor.flashlightCharge = 31;
            var book = world.Objective(DeadAirPrisonBook.Id);
            Require(book != null && DeadAirPrisonBook.Pages.Length >= 3, "Prison book or its text is missing.");
            actor.x = book.x; actor.y = 0; actor.z = book.z;
            Require(rules.Interact("host", book.id) != null && !actor.HasItem(book.id), "Book must not be collected from the floor above.");
            At(actor, book.x, book.y, book.z);
            Require(rules.Interact("host", book.id) == null && actor.HasItem(book.id), "Prison book must be collectible in its cell.");
            var copy = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State));
            var peer = copy.Player("host");
            Require(peer.spareBatteries == 1 && peer.coins == 7 && peer.flashlightCharge == 31 && peer.HasItem(book.id) &&
                copy.Drawer(drawer.id).lootTaken, "Snapshot omitted supplies, book or one-time loot state.");
            rules.State.phase = "lost"; Require(rules.Restart("host") == null, "Restart failed.");
            Require(actor.flashlightCharge == DeadAirSupplies.FlashlightSeconds && actor.spareBatteries == 0 && actor.coins == 0 &&
                !actor.HasItem(book.id) && !rules.State.Drawer(drawer.id).searched && !rules.State.Drawer(drawer.id).open &&
                !rules.State.Collected(world.Batteries[0].id), "Restart must restore charge/van and reroll all drawers next round.");
        }

        static void ValidateDrawerPresentation(WorldData world)
        {
            var fixture = new GameObject("Drawer idle transform fixture");
            var materials = new System.Collections.Generic.HashSet<Material>();
            try
            {
                var data = world.Drawers[0];
                var drawer = new GameObject("drawer:" + data.id).transform;
                drawer.SetParent(fixture.transform, false); drawer.localPosition = data.localPosition;
                var closed = drawer.localPosition; var opened = closed + Vector3.forward * .38f;
                var presentation = new DeadAirDrawerPresentation(fixture.transform, world);
                foreach (var renderer in fixture.GetComponentsInChildren<Renderer>(true)) materials.Add(renderer.sharedMaterial);
                var state = Started(world).State; state.elapsed = 5;
                var snapshot = state.Drawer(data.id);
                presentation.Apply(state, .1f);
                RequireDrawerIdle(presentation, drawer, state, "Initialized closed drawer");
                snapshot.open = true; drawer.hasChanged = false;
                presentation.Apply(state, 0);
                Require(!drawer.hasChanged, "A paused drawer must not dirty its transform.");
                presentation.Apply(state, .1f);
                Require(drawer.hasChanged && drawer.localPosition.z > closed.z && drawer.localPosition.z < opened.z,
                    "An idle drawer must resume its opening animation.");
                presentation.Apply(state, 1);
                Require(drawer.localPosition.Equals(opened), "Drawer opening must finish at its full extension.");
                RequireDrawerIdle(presentation, drawer, state, "Open drawer");
                snapshot.open = false; presentation.Apply(state, .1f);
                Require(drawer.localPosition.z > closed.z && drawer.localPosition.z < opened.z,
                    "An open idle drawer must resume its closing animation.");
                snapshot.open = true; presentation.Apply(state, 1);
                Require(drawer.localPosition.Equals(opened), "An interrupted closing must reopen the drawer.");
                state.elapsed = 0; snapshot.open = false; drawer.hasChanged = false;
                presentation.Apply(state, 0);
                Require(drawer.hasChanged && drawer.localPosition.Equals(closed),
                    "A rewound snapshot must restore the authored closed position immediately.");
                RequireDrawerIdle(presentation, drawer, state, "Reset closed drawer");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
                foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static void RequireDrawerIdle(DeadAirDrawerPresentation presentation, Transform drawer, RoomState state, string context)
        {
            var position = drawer.localPosition;
            drawer.hasChanged = false;
            for (int i = 0; i < 8; i++) presentation.Apply(state, .1f);
            Require(!drawer.hasChanged && drawer.localPosition.Equals(position),
                context + " must retain its position without writing the transform.");
        }
    }
}
