using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    [Serializable] public class PointData { public float x, y, z; }
    [Serializable] public class BoundsData { public float minX, maxX, minZ, maxZ; }
    [Serializable] public class RoomData { public string name; public float minX, maxX, minZ, maxZ; }
    [Serializable] public class ObjectiveData { public string id, type, label; public float x, y, z; }
    [Serializable] public class DoorData { public string id, label, requires; public float x, y, z; }
    [Serializable] public class LockerData { public string id, label; public float x, y, z, ry, exitX, exitZ; }
    [Serializable] public class PropData
    {
        public string id, type, label, color;
        public float x, y, z, w, d, size, rotation, exitX, exitZ;
    }
    [Serializable] public class ClassBonuses
    {
        public float flashlightDist = 24, flashlightAngle = .48f, staminaRecovery = 1,
            speedMultiplier = 1, stunBonus = 1, sanityResistance = 1, radioCooldown = 3000,
            monsterHearingRange = 16;
    }
    [Serializable] public class ClassData
    {
        public string id, name, title, role, gender, color, badge, perk, description;
        public ClassBonuses bonuses;
    }
    [Serializable] public class WorldData
    {
        public PointData spawn, extraction;
        public BoundsData courtyard;
        public BoundsData[] exteriorObstacles;
        public RoomData[] rooms;
        public ObjectiveData[] objects;
        public DoorData[] doors;
        public LockerData[] lockers;
        public PropData[] props;
        public ClassData[] classes;

        public static WorldData Load()
        {
            var asset = Resources.Load<TextAsset>("world");
            if (asset == null) throw new Exception("Assets/Resources/world.json is missing. Run node scripts/export-unity-data.mjs.");
            return JsonUtility.FromJson<WorldData>(asset.text);
        }

        public ClassData GetClass(string id)
        {
            foreach (var item in classes) if (item.id == id) return item;
            return classes[0];
        }

        public ObjectiveData Objective(string id)
        {
            foreach (var item in objects) if (item.id == id) return item;
            return null;
        }

        public DoorData Door(string id)
        {
            foreach (var item in doors) if (item.id == id) return item;
            return null;
        }

        public LockerData Locker(string id)
        {
            foreach (var item in lockers) if (item.id == id) return item;
            return null;
        }

        public string RoomAt(float x, float z)
        {
            foreach (var room in rooms)
                if (x >= room.minX && x <= room.maxX && z >= room.minZ && z <= room.maxZ)
                    return room.name;
            return "ПОЗА СТАНЦІЄЮ";
        }
    }

    [Serializable] public class PlayerState
    {
        public string id, name, characterClass, hiding;
        public float x, z, yaw, pitch, sanity = 100, stamina = 100, invulnerableUntil;
        public bool flashlight = true, camera, sprinting, down;
    }
    [Serializable] public class DoorState
    {
        public string id, requiredKey;
        public bool locked, open;
    }
    [Serializable] public class EnemyState
    {
        public float x, z, stunnedUntil, radioUntil, radioX, radioZ;
        public bool active, stunned, bloody;
    }
    [Serializable] public class QuestState
    {
        public int stage = 1;
        public bool fuelAdded, valveOpened, calibrated, power, broadcast;
    }
    [Serializable] public class RoomState
    {
        public string code, hostId, phase = "lobby", message, radioSenderId;
        public bool solo, creative, paused, canister, valveOpened, calibrated, power, broadcast;
        public float spawnCountdown = 35, elapsed;
        public int scare, radioSequence;
        public List<PlayerState> players = new List<PlayerState>();
        public List<string> tapes = new List<string>(), fuses = new List<string>(), keycards = new List<string>();
        public List<DoorState> doors = new List<DoorState>();
        public QuestState quest = new QuestState();
        public EnemyState enemy = new EnemyState();

        public PlayerState Player(string id) { return players.Find(p => p.id == id); }
        public DoorState Door(string id) { return doors.Find(d => d.id == id); }
        public bool Collected(string id) { return tapes.Contains(id) || fuses.Contains(id) || keycards.Contains(id); }
    }

    [Serializable] public class NetPacket
    {
        public string type, id, name, classId, code, playerId, stateJson, error;
        public bool solo, creative, flashlight, camera, sprinting, paused, ok;
        public int phrase;
        public float x, z, yaw, pitch, frequency;
    }
}
