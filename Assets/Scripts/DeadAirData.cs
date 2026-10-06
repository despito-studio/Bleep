using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    [Serializable] public class PointData { public float x, y, z; }
    [Serializable] public class BoundsData { public float minX, maxX, minZ, maxZ; }
    [Serializable] public class RoomData { public string name, mapLabel; public float minX, maxX, minZ, maxZ, floorY; }
    [Serializable] public class StairData
    {
        public float minX, maxX, minZ, maxZ, lowerY, upperY;
        public int steps = 20;
        // The flight rises from maxZ (lower landing) toward minZ (upper landing).
        public bool Contains(float x, float z, float margin = 0) =>
            x >= minX + margin && x <= maxX - margin && z >= minZ && z <= maxZ;
        public float HeightAt(float z) => Mathf.Lerp(lowerY, upperY, Mathf.InverseLerp(maxZ, minZ, z));
    }
    [Serializable] public class ObjectiveData { public string id, type, label; public float x, y, z; }
    [Serializable] public class DoorData
    {
        public string id, label, requires;
        public float x, y, z, rotation, w = 2.2f, openAngle = 90;
    }
    [Serializable] public class LockerData
    {
        public string id, label;
        public float x, y, z, ry, exitX, exitY, exitZ;
        public float FacingYaw => Mathf.Atan2(-(exitX - x), -(exitZ - z));
        public float ExitFloorY => exitY == 0 && y > 1 ? y : exitY;
    }
    [Serializable] public class PropData
    {
        public string id, type, label, color;
        public float x, y, z, w, d, size, rotation, exitX, exitZ;
    }
    [Serializable] public class NavigationNodeData { public float x, y, z; }
    [Serializable] public class NavigationLinkData { public int a, b; }
    [Serializable] public class MushroomData
    {
        public string id, label;
        public float x, y, z;
        public int variant;
    }
    public static class DeadAirRoleItems
    {
        public static string ForClass(string classId) => classId == "operator" ? "cigarette-pack" :
            classId == "engineer" ? "wrench" : classId == "journalist" ? "recorder" : classId == "dispatcher" ? "compass" : null;
        public static bool IsRoleItem(string id) => id == "cigarette-pack" || id == "wrench" || id == "recorder" || id == "compass";
        public static string Label(string id) => id == "cigarette-pack" ? "Пачка сигарет" : id == "wrench" ? "Гайковий ключ" :
            id == "recorder" ? "Диктофон" : id == "compass" ? "Маятник-компас" : id;
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
        public StairData[] stairs;
        public NavigationNodeData[] navigationNodes;
        public NavigationLinkData[] navigationLinks;
        public ObjectiveData[] objects;
        public DoorData[] doors;
        public LockerData[] lockers;
        public PropData[] props;
        public ClassData[] classes;
        [NonSerialized] MushroomData[] mushrooms;
        // Rules, interaction prompts and presentation share exactly the same placements.
        public MushroomData[] Mushrooms => mushrooms ?? (mushrooms = DeadAirMushrooms.CreatePlacements(this));
        public MushroomData Mushroom(string id) => Array.Find(Mushrooms, item => item.id == id);

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

        public string RoomAt(float x, float z, float y = 0)
        {
            if (stairs != null)
                foreach (var stair in stairs)
                    if (stair.Contains(x, z) && Mathf.Abs(y - stair.HeightAt(z)) < .75f) return stair.lowerY < 0 ? "СХОДИ / ПІДВАЛ" : "СХОДИ / 1–2 ПОВЕРХ";
            foreach (var room in rooms)
                if (Mathf.Abs(y - room.floorY) < 1 && x >= room.minX && x <= room.maxX && z >= room.minZ && z <= room.maxZ)
                    return room.name;
            return "ПОЗА СТАНЦІЄЮ";
        }
    }

    [Serializable] public class PlayerState
    {
        public string id, name, characterClass, hiding;
        public float x, y, z, yaw, pitch, sanity = 100, stamina = 100, invulnerableUntil;
        public bool flashlight = true, camera, sprinting, down, mapOpen, uniqueItemEquipped, noclip;
        public bool uniqueActionCancelled = true;
        public int uniqueActionSequence, cigarettesRemaining;
        // Host-authored event counters survive skipped snapshots without repeating a scare.
        public int spiderScareSerial, spiderScareAt;
        // Only the host can start/extend a trip; these fields travel in room snapshots.
        public float mushroomTripStartedAt, mushroomTripUntil;
        public int mushroomTripSerial;
        public float uniqueActionAt = -100;
        public List<string> inventory = new List<string>();
        public int selectedSlot;
        public int moveSequence;
        public string SelectedItem => inventory != null && selectedSlot >= 0 && selectedSlot < inventory.Count ? inventory[selectedSlot] : null;
        public bool HasSelectedRoleItem => SelectedItem != null && SelectedItem == DeadAirRoleItems.ForClass(characterClass);
        public bool HasItem(string itemId) { return inventory != null && inventory.Contains(itemId); }
    }
    [Serializable] public class DroppedItem
    {
        public string id, itemId, label;
        public float x, y, z;
    }
    [Serializable] public class DoorState
    {
        public string id, requiredKey;
        public bool locked, open;
        public float openedAt = -100;
        public float openSign = 1f;
    }
    [Serializable] public class EnemyState
    {
        public float x, y, z, stunnedUntil, radioUntil, radioX, radioY, radioZ;
        public bool active, stunned, bloody;
        public string behaviour = "patrol", watchingPlayerId, doorId;
        public float yaw, peekLean;
        public float heardX, heardY, heardZ, heardUntil;
        public int encounterSerial, encounterAt;
    }
    [Serializable] public class QuestState
    {
        public int stage = 1;
        public bool fuelAdded, valveOpened, calibrated, power, broadcast;
        public bool ladderPlaced, windowBroken, breakerOn, directorDrawerOpen;
    }
    [Serializable] public class RoomState
    {
        public string code, hostId, phase = "lobby", message, radioSenderId;
        public string chapterId = DeadAirChapters.DefaultId;
        public bool solo, creative, paused, canister, valveOpened, calibrated, power, broadcast;
        public bool ladderPlaced, windowBroken, breakerOn, directorDrawerOpen;
        public float spawnCountdown = 35, elapsed;
        public int scare, radioSequence;
        public List<PlayerState> players = new List<PlayerState>();
        public List<string> tapes = new List<string>(), fuses = new List<string>(), keycards = new List<string>();
        public List<string> collected = new List<string>();
        public List<DroppedItem> droppedItems = new List<DroppedItem>();
        public List<DoorState> doors = new List<DoorState>();
        public QuestState quest = new QuestState();
        public EnemyState enemy = new EnemyState();

        public PlayerState Player(string id) { return players.Find(p => p.id == id); }
        public DoorState Door(string id) { return doors.Find(d => d.id == id); }
        public bool Collected(string id) { return collected.Contains(id) || tapes.Contains(id) || fuses.Contains(id) || keycards.Contains(id); }
    }

    [Serializable] public class NetPacket
    {
        public string type, id, name, classId, code, playerId, stateJson, error, chapterId;
        public bool solo, creative, flashlight, camera, sprinting, paused, ok, mapOpen, uniqueEquipped, noclip;
        public int phrase, slot, moveSequence;
        public float x, y, z, yaw, pitch, frequency;
    }
}
