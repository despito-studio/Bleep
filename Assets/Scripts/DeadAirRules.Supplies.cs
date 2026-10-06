using System;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirRules
    {
        float verificationDrawerRoll = -1;

        void ResetDrawers()
        {
            State.drawers.Clear();
            foreach (var drawer in world.Drawers) State.drawers.Add(new DrawerState { id = drawer.id });
        }

        public bool ConfigureDrawerRollForVerification(float roll)
        {
            if (!HorrorVerificationAllowed || float.IsNaN(roll) || float.IsInfinity(roll)) return false;
            verificationDrawerRoll = Mathf.Clamp01(roll);
            return true;
        }

        string InteractDrawer(PlayerState player, string targetId)
        {
            bool takeLoot = targetId.StartsWith("drawer-loot:", StringComparison.Ordinal);
            string drawerId = takeLoot ? targetId.Substring("drawer-loot:".Length) : targetId;
            var location = world.Drawer(drawerId);
            var drawer = State.Drawer(drawerId);
            if (location == null || drawer == null) return "Невідома шухляда.";
            if (!Near(player, location.x, location.y, location.z, 2.5f, 0, drawerId)) return "Підійдіть ближче до шухляди.";
            if (takeLoot)
            {
                if (!drawer.open || !drawer.searched || drawer.lootTaken || drawer.loot == "empty") return "У шухляді нічого немає.";
                if (drawer.loot == "coins") player.coins += drawer.coins;
                else if (drawer.loot == DeadAirSupplies.BatteryItem) AddBattery(player);
                drawer.lootTaken = true;
                State.message = drawer.loot == "coins" ? player.name + " знайшов монетки: " + drawer.coins + "." :
                    player.name + " знайшов батарейку для ліхтаря.";
            }
            else
            {
                drawer.open = !drawer.open;
                if (drawer.open && !drawer.searched)
                {
                    drawer.searched = true;
                    drawer.loot = DeadAirDrawers.LootForRoll(verificationDrawerRoll >= 0 ? verificationDrawerRoll : random.NextDouble());
                    drawer.coins = drawer.loot == "coins" ? random.Next(2, 7) : 0;
                }
                if (drawerId == "drawer-director") State.directorDrawerOpen = State.quest.directorDrawerOpen = drawer.open;
                State.message = drawer.open ? location.label + ": " +
                    (drawer.lootTaken || drawer.loot == "empty" ? "порожньо." : drawer.loot == "coins" ? "усередині монетки." : "усередині батарейка.") :
                    location.label + " зачинена.";
                HearNoise(location.x, location.y, location.z, 7, 3);
            }
            Dirty = true;
            return null;
        }

        string CollectVanBattery(PlayerState player, string batteryId)
        {
            var battery = world.Battery(batteryId);
            if (battery == null) return "Невідома батарейка.";
            if (State.Collected(batteryId)) return "Батарейку вже забрали.";
            if (!Near(player, battery.x, battery.y, battery.z, 2.5f, 0, batteryId)) return "Підійдіть ближче до фургона.";
            State.collected.Add(batteryId);
            AddBattery(player);
            State.message = player.name + " взяв запасну батарейку з фургона.";
            Dirty = true;
            return null;
        }

        static void AddBattery(PlayerState player)
        {
            player.spareBatteries++;
            if (!player.HasItem(DeadAirSupplies.BatteryItem)) player.inventory.Add(DeadAirSupplies.BatteryItem);
        }

        static void ConsumeBattery(PlayerState player)
        {
            player.spareBatteries = Mathf.Max(0, player.spareBatteries - 1);
            if (player.spareBatteries > 0) return;
            int removed = player.inventory.IndexOf(DeadAirSupplies.BatteryItem);
            player.inventory.Remove(DeadAirSupplies.BatteryItem);
            if (removed >= 0 && removed < player.selectedSlot) player.selectedSlot--;
            player.selectedSlot = Mathf.Clamp(player.selectedSlot, 0, Mathf.Max(0, player.inventory.Count - 1));
        }

        string DropBattery(PlayerState player)
        {
            if (player.spareBatteries <= 0) return "Запасних батарейок немає.";
            ConsumeBattery(player);
            State.droppedItems.Add(new DroppedItem
            {
                id = "drop_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                itemId = DeadAirSupplies.BatteryItem, label = "Батарейка для ліхтаря", x = player.x, y = player.y, z = player.z
            });
            player.uniqueItemEquipped = player.HasSelectedRoleItem && !player.camera && !player.mapOpen;
            if (!player.uniqueItemEquipped) player.uniqueActionCancelled = true;
            State.message = player.name + " викинув батарейку.";
            Dirty = true;
            return null;
        }

        public string ReloadFlashlight(string id)
        {
            var player = State.Player(id);
            if (State.phase != "playing" || State.paused || player == null || player.down || player.ladderClimbing ||
                !string.IsNullOrEmpty(player.hiding)) return "Зараз не можна замінити батарейку.";
            if (player.SelectedItem != "flashlight" && player.SelectedItem != DeadAirSupplies.BatteryItem ||
                !player.HasItem("flashlight") || player.mapOpen || player.camera) return "Оберіть ліхтар або батарейку в інвентарі.";
            if (player.spareBatteries <= 0) return "Запасних батарейок немає. Перевірте шухляди або фургон.";
            if (player.flashlightCharge >= DeadAirSupplies.FlashlightSeconds - 1) return "Батарейка ще повна.";
            ConsumeBattery(player);
            player.flashlightCharge = DeadAirSupplies.FlashlightSeconds;
            player.flashlightReloadSerial++;
            if (player.SelectedItem == "flashlight") player.flashlight = true;
            State.message = player.name + " замінив батарейку ліхтаря.";
            Dirty = true;
            return null;
        }

        void DrainFlashlight(PlayerState player, float dt)
        {
            if (player.down || !string.IsNullOrEmpty(player.hiding) || !player.flashlight ||
                player.mapOpen || !player.HasItem("flashlight")) return;
            player.flashlightCharge = Mathf.Max(0, player.flashlightCharge - dt);
            if (player.flashlightCharge > 0) return;
            player.flashlight = false;
            State.message = player.name + ": батарейка ліхтаря розряджена. R — замінити.";
            Dirty = true;
        }
    }
}
