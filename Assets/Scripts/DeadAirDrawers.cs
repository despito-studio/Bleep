using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public static class DeadAirDrawers
    {
        public static DrawerData[] CreatePlacements(WorldData world)
        {
            var result = new List<DrawerData>();
            if (world.props != null)
                foreach (var prop in world.props) result.AddRange(ForProp(prop));
            return result.ToArray();
        }

        public static DrawerData[] ForProp(PropData prop)
        {
            if (prop.type != "workstation" && prop.type != "cardCatalog") return Array.Empty<DrawerData>();
            bool desk = prop.type == "workstation";
            bool director = desk && prop.label != null && prop.label.Contains("директора");
            float width = prop.w > 0 ? prop.w : 1, depth = prop.d > 0 ? prop.d : 1;
            int count = desk ? 3 : 8;
            var result = new DrawerData[count];
            for (int i = 0; i < count; i++)
            {
                var local = desk ? new Vector3(-width * .34f, .18f + i * .21f, 0) :
                    new Vector3(((i % 2) - .5f) * width * .48f, .23f + (i / 2) * .4f, 0);
                // Keep the existing objective and key tray exactly where earlier builds placed it.
                if (director && i == 2) local.y = .6f;
                var point = DeadAirOfficeLayout.Position(prop.x, prop.y, prop.z) +
                    DeadAirOfficeLayout.Rotation(prop.rotation) * (local + Vector3.forward * (depth * .5f + .06f));
                result[i] = new DrawerData
                {
                    id = director && i == 2 ? "drawer-director" : "drawer:" + prop.id + ":" + i,
                    propId = prop.id, label = desk ? "Шухляда столу" : "Шухляда картотеки",
                    localPosition = local, x = -point.x, y = point.y, z = point.z,
                    width = desk ? .45f : width * .46f,
                    height = desk ? .18f : .37f,
                    depth = desk ? depth * .8f : depth * .9f,
                    directorKey = director && i == 2
                };
            }
            return result;
        }

        // Half the drawers are empty, thirty percent contain coins, twenty percent a battery.
        public static string LootForRoll(double roll) => roll < .5 ? "empty" : roll < .8 ? "coins" : DeadAirSupplies.BatteryItem;
    }
}
