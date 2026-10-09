using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirFieldUpdateValidation
    {
        public static void Prepare()
        {
            PlayerSettings.bundleVersion = DeadAirVersion.Current;
            DeadAirOfficeBuilder.Build();
            DeadAirEditorSetup.CreateScene();
            Validate();
            DeadAirCharacterFieldValidation.Validate();
        }

        public static void Validate()
        {
            var checks = new List<string>();
            var data = WorldData.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var exterior = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/exterior"));
            var interior = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/station"));
            try
            {
                Physics.SyncTransforms();
                for (int i = 0; i < 4; i++)
                {
                    var spawn = DeadAirVanPresentation.SpawnPoint(data, i);
                    Vector3 feet = new Vector3(-spawn.x, spawn.y, spawn.z);
                    Require(Physics.Raycast(feet + Vector3.up * .12f, Vector3.down, out var floor, .25f) && floor.normal.y > .9f,
                        "Van spawn has no physical floor: " + i);
                    Require(!Physics.CheckCapsule(feet + Vector3.up * .25f, feet + Vector3.up * 1.47f, .20f, ~0, QueryTriggerInteraction.Ignore),
                        "Van spawn intersects seats/walls/ceiling: " + i);
                }
                checks.Add("PASSED: four real standing-height spawn capsules supported by van floor; no seats, wall or ceiling intersections.");
                double now = 0;
                var rules = new DeadAirRules(data, "host", "Crew", "engineer", false, true, movementTime: () => now);
                rules.AddPlayer("guest", "Guest", "dispatcher"); rules.Start("host");
                var actor = rules.State.Player("host");
                float x = actor.x;
                rules.Move("host", new NetPacket { type = "move", x = x + .1f, y = actor.y, z = actor.z });
                Require(actor.x == x, "Movement must stay locked during arrival.");
                rules.State.elapsed = DeadAirVanPresentation.ArrivalDuration + .1f;
                actor.x = 0; actor.y = 0; actor.z = 19;
                now += .1;
                rules.Move("host", new NetPacket { type = "move", x = .05f, y = 0, z = 19, crouching = true, sprinting = true, moveSequence = 1 });
                Require(actor.crouching && !actor.sprinting && Mathf.Abs(actor.x - .05f) < .001f, "Crouch must move and prohibit sprinting on authority.");
                checks.Add("PASSED: arrival movement lock and host-authoritative crouch/walk/run state.");
                rules.EquipMap("host", true);
                Require(!actor.mapOpen, "A map cannot open away from the van.");
                var board = data.Objective("van-map");
                Require(board != null, "Van map objective missing.");
                var mapStand = DeadAirVanPresentation.SourcePoint(data, new Vector3(.50f, DeadAirVanPresentation.FloorHeight, -.75f));
                actor.x = mapStand.x; actor.y = mapStand.y; actor.z = mapStand.z;
                rules.EquipMap("host", true);
                Require(actor.mapOpen && DeadAirUI.CanOpenVanMap(data, rules.State, actor), "Standing at wall map must open it in both UI and authority.");
                rules.EquipMap("host", false);
                checks.Add("PASSED: world map available at van board only; UI and host agree.");
                rules.State.phase = "lobby";
                rules.SetClothing("host", 0);
                Require(actor.clothingMask == 0, "No-clothing preset must survive authority.");
                rules.SetClothing("host", DeadAirClothing.All);
                Require(actor.clothingMask == DeadAirClothing.All, "Garment flags must survive authority.");
                rules.State.phase = "playing";
                rules.SetClothing("host", 0);
                Require(actor.clothingMask == DeadAirClothing.All, "Clothing edits must remain lobby-only.");
                checks.Add("PASSED: clothing presets and garment flags accepted only in lobby.");
                var exit = data.Objective("exit"); rules.State.broadcast = true;
                var hostExit = DeadAirVanPresentation.SourcePoint(data, new Vector3(-.5f, .6f, -.3f));
                actor.x = hostExit.x; actor.y = hostExit.y; actor.z = hostExit.z;
                var guest = rules.State.Player("guest"); guest.x = 0; guest.y = 0; guest.z = 19;
                Require(rules.Interact("host", "exit") != null && rules.State.phase == "playing", "Extraction must wait for living crew.");
                var guestSeat = DeadAirVanPresentation.SpawnPoint(data, 1);
                guest.x = guestSeat.x; guest.y = guestSeat.y; guest.z = guestSeat.z;
                Require(rules.Interact("host", "exit") == null && rules.State.phase == "departing", "Extraction must play van departure before result.");
                for (int i = 0; i < 25; i++) rules.Tick(.25f);
                Require(rules.State.phase == "won", "Departure must complete into final report.");
                checks.Add("PASSED: crew gathering, shared departure timeline and victory after animation.");
                Directory.CreateDirectory("Art/FieldUpdate");
                File.WriteAllLines("Art/FieldUpdate/editor-checks.txt", checks);
                Debug.Log("DEAD AIR FIELD UPDATE EDITOR PASSED: " + checks.Count + " focused checks.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(exterior); UnityEngine.Object.DestroyImmediate(interior);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
