using UnityEngine;

namespace DeadAir
{
    // All game data uses the original source coordinates. Reflect X only at the visual boundary.
    public sealed class DeadAirOfficeLayout : MonoBehaviour
    {
        public const float FloorHeight = 4.2f;
        public const float BuildingMinX = -18, BuildingMaxX = 18;
        public const float BuildingMinZ = -54, BuildingMaxZ = 6;
        public const float CorridorHalfWidth = 3;
        public const int Version = 2;
        public int layoutVersion = Version;
        public static bool IsCirculation(RoomData room) => room != null && room.mapLabel != null && room.mapLabel.StartsWith("HALL", System.StringComparison.Ordinal);
        public static bool IsInterior(RoomData room) => room != null && room.minX >= BuildingMinX && room.maxX <= BuildingMaxX && room.minZ >= BuildingMinZ && room.maxZ <= BuildingMaxZ;
        public static Vector3 Position(float x, float y, float z) => new Vector3(-x, y, z);
        public static Quaternion Rotation(float radians) => Quaternion.Euler(0, -radians * Mathf.Rad2Deg, 0);
    }
}
