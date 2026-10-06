using System;
using UnityEngine;

namespace DeadAir
{
    public static class DeadAirSupplies
    {
        public const float FlashlightSeconds = 180;
        public const string BatteryItem = "flashlight-battery";
        public static bool HasCharge(PlayerState player) => player != null && player.flashlightCharge > 0 && player.HasItem("flashlight");

        public static BatteryData[] CreateVanBatteries(WorldData world)
        {
            var van = world.props == null ? null : Array.Find(world.props, p => p.type == "van");
            if (van == null) return Array.Empty<BatteryData>();
            var result = new BatteryData[4];
            for (int i = 0; i < result.Length; i++)
            {
                // The supply tray sits at the rear of the van, reachable from outside.
                var point = DeadAirOfficeLayout.Position(van.x, van.y, van.z) +
                    DeadAirOfficeLayout.Rotation(van.rotation) * new Vector3((i - 1.5f) * .14f, .9f, -van.d * .5f - .14f);
                result[i] = new BatteryData { id = "van-battery-" + (i + 1), x = -point.x, y = point.y, z = point.z };
            }
            return result;
        }
    }
}
