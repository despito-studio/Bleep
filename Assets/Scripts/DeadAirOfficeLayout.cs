using UnityEngine;

namespace DeadAir
{
    // All game data uses the original source coordinates. Reflect X only at the visual boundary.
    public sealed class DeadAirOfficeLayout : MonoBehaviour
    {
        public const float FloorHeight = 4.2f;
        public const float BuildingMinX = -22, BuildingMaxX = 22;
        public const float BuildingMinZ = -54, BuildingMaxZ = 6;
        public const float CorridorHalfWidth = 3;
        public const float BasementMinX = -18, BasementMaxX = 18;
        public const float BasementMinZ = -39.5f, BasementMaxZ = -27;
        public const float BasementArea = (BasementMaxX - BasementMinX) * (BasementMaxZ - BasementMinZ);
        public const int Version = 5;
        public int layoutVersion = Version;
        public static bool IsCirculation(RoomData room) => room != null && room.mapLabel != null && room.mapLabel.StartsWith("HALL", System.StringComparison.Ordinal);
        public static bool IsInterior(RoomData room) => room != null && room.mapLabel != "COURTYARD" && room.mapLabel != "PATH" && room.mapLabel != "FRONT YARD" && room.mapLabel != "GENERATOR" && (room.floorY < -1 || (room.minX >= BuildingMinX && room.maxX <= BuildingMaxX && room.minZ >= BuildingMinZ && room.maxZ <= BuildingMaxZ));
        public static bool InteriorAt(WorldData world,float sourceX,float z,float floorY)
        {
            foreach(var room in world.rooms)
                if(IsInterior(room)&&Mathf.Abs(room.floorY-floorY)<.05f&&sourceX>=room.minX&&sourceX<=room.maxX&&z>=room.minZ&&z<=room.maxZ)return true;
            return false;
        }
        public static Vector3 Position(float x, float y, float z) => new Vector3(-x, y, z);
        public static Quaternion Rotation(float radians) => Quaternion.Euler(0, -radians * Mathf.Rad2Deg, 0);
    }
}
