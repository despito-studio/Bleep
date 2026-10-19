using System;
using System.Collections.Generic;
using System.Reflection;
using DeadAir;
using UnityEditor;
using UnityEngine;

public static class DeadAirSchoolCollisionValidation
{
    [MenuItem("Dead Air/Validate/School collision authority")]
    public static void Run()
    {
        using (new ExistingCollisionIsolation())
        {
            ValidateChapterIsolation();
            ValidatePlayerSweep();
            ValidateSchoolMovePipeline();
            ValidateDoorActorSensor();
        }
        Debug.Log("SCHOOL COLLISION VALIDATION PASS: chapter-1 door planes excluded from school; physical walls retained; low furniture, head clearance, crouching, step allowance and preserved doorway width verified.");
    }

    private sealed class ExistingCollisionIsolation : IDisposable
    {
        private readonly List<Collider> restored = new List<Collider>();

        public ExistingCollisionIsolation()
        {
            foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!collider.enabled || !collider.gameObject.scene.IsValid() || EditorUtility.IsPersistent(collider)) continue;
                restored.Add(collider);
                collider.enabled = false;
            }
            Physics.SyncTransforms();
        }

        public void Dispose()
        {
            foreach (var collider in restored) if (collider != null) collider.enabled = true;
            Physics.SyncTransforms();
        }
    }

    private static bool Segment(DeadAirRules rules, Vector3 a, Vector3 b)
    {
        var method = typeof(DeadAirRules).GetMethod("SegmentBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
        return (bool)method.Invoke(rules, new object[] { a.x, a.y, a.z, b.x, b.y, b.z });
    }

    private static bool Sweep(Vector3 origin, Vector3 target, bool crouch = false)
    {
        var method = typeof(DeadAirRules).GetMethod("SchoolMovementSweepBlocked", BindingFlags.Static | BindingFlags.NonPublic);
        return (bool)method.Invoke(null, new object[] { origin, target, crouch });
    }

    private static void ValidateChapterIsolation()
    {
        var world = WorldData.Load();
        world.doors = new[] { new DoorData { id = "office-door-overlap-fixture", x = 90f, y = 31.4f, z = 90f, rotation = 0f, w = 2.2f } };
        var rules = new DeadAirRules(world, "host", "School collision", "engineer", true, true);
        Vector3 from = new Vector3(89.4f, 30f, 90f), to = new Vector3(90.6f, 30f, 90f);
        Require(Segment(rules, from, to), "A closed chapter-1 door must retain its authoritative plane.");
        rules.State.chapterId = "coming-soon";
        Require(!Segment(rules, from, to), "A school passage is invisibly sealed by chapter-1 door data.");
        var wall = new GameObject("Actual school wall authority fixture");
        try
        {
            wall.transform.position = new Vector3(-90f, 30.85f, 90f);
            wall.AddComponent<BoxCollider>().size = new Vector3(.1f, 2f, 2f);
            Physics.SyncTransforms();
            Require(Segment(rules, from, to), "Excluding old door data must preserve real wall collision.");
        }
        finally { UnityEngine.Object.DestroyImmediate(wall); }
    }

    private static void ValidatePlayerSweep()
    {
        Vector3 origin = new Vector3(-90f, 50f, 90f), target = origin + Vector3.forward * 1.2f;
        var obstacle = new GameObject("School movement shape fixture");
        var actor = new GameObject("Predicted school actor fixture");
        try
        {
            var box = obstacle.AddComponent<BoxCollider>();
            obstacle.transform.position = origin + new Vector3(0f, .72f, .6f);
            box.size = new Vector3(1.2f, .035f, .35f);
            Physics.SyncTransforms();
            Require(Sweep(origin, target), "The old torso ray misses a .72 m desktop; the player volume must block it.");
            obstacle.transform.position = origin + new Vector3(0f, 1.42f, .6f);
            box.size = new Vector3(1.2f, .12f, .35f);
            Physics.SyncTransforms();
            Require(Sweep(origin, target), "Standing head clearance must block a low overhead beam.");
            Require(!Sweep(origin, target, true), "A crouched actor must fit below a beam above its 1.08 m height.");
            obstacle.transform.position = origin + new Vector3(0f, .09f, .6f);
            box.size = new Vector3(1.2f, .18f, .35f);
            Physics.SyncTransforms();
            Require(!Sweep(origin, target), "The server must allow a local controller's .28 m step allowance.");
            obstacle.transform.position = origin + new Vector3(.75f, 1.15f, .6f);
            box.size = new Vector3(.12f, 2.3f, .28f);
            Physics.SyncTransforms();
            Require(!Sweep(origin, target), "The .22 m local controller must fit through the preserved classroom aperture.");
            Require(Sweep(origin + Vector3.right * .6f, target + Vector3.right * .6f), "A sensor must not let actors clip the actual door jamb.");
            obstacle.SetActive(false);
            var controller = actor.AddComponent<CharacterController>();
            controller.height = 1.7f; controller.radius = .22f; controller.center = Vector3.up * .85f;
            actor.transform.position = target;
            Physics.SyncTransforms();
            Require(!Sweep(origin, target), "The predicted local CharacterController must be excluded from host collision sweeps.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(obstacle);
            UnityEngine.Object.DestroyImmediate(actor);
        }
    }

    private static void ValidateSchoolMovePipeline()
    {
        double clock = 0;
        var rules = new DeadAirRules(WorldData.Load(), "host", "School movement volume", "engineer", true, true,
            "coming-soon", () => clock);
        Require(rules.Start("host") == null, "School movement fixture could not start.");
        rules.State.elapsed = 10;
        var player = rules.State.Player("host");
        player.x = 57f; player.y = 7.9f; player.z = 88.8f;
        var desktop = new GameObject("School desktop packet fixture");
        try
        {
            clock += .5;
            rules.Move("host", new NetPacket { x = 57f, y = 7.9f, z = 90f, moveSequence = 1 });
            Require(Mathf.Abs(player.z - 90f) < .0001f && player.moveSequence == 1,
                "The isolated school movement fixture must accept an unobstructed packet before testing physical rejection.");
            desktop.transform.position = new Vector3(-57f, 8.62f, 90.6f);
            desktop.AddComponent<BoxCollider>().size = new Vector3(1.2f, .035f, .35f);
            Physics.SyncTransforms(); clock += .5;
            rules.Move("host", new NetPacket { x = 57f, y = 7.9f, z = 91.2f, moveSequence = 2 });
            Require(Mathf.Abs(player.z - 90f) < .0001f && player.moveSequence == 2,
                "An actual school move packet crossed the desktop volume or lost its rejection acknowledgement.");
            desktop.SetActive(false); Physics.SyncTransforms(); clock += .5;
            rules.Move("host", new NetPacket { x = 57f, y = 7.9f, z = 91.2f, moveSequence = 3 });
            Require(Mathf.Abs(player.z - 91.2f) < .0001f && player.moveSequence == 3,
                "A valid school move did not recover after the physical obstacle was removed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(desktop); }
    }

    private static void ValidateDoorActorSensor()
    {
        var fixture = new GameObject("School door actor sensor fixture");
        var leaf = new GameObject("Sliding Leaf");
        leaf.transform.SetParent(fixture.transform, false);
        fixture.transform.position = new Vector3(-12f, 3.8f, -8.5f);
        try
        {
            var motion = DeadAirArtMotionSlidingLeaf.Attach(leaf.transform, Vector3.right * .85f, Vector3.right * 1.42f, fixture.transform, 2.5f);
            var state = new RoomState { chapterId = "coming-soon" };
            var method = typeof(DeadAirSchoolWorld).GetMethod("SchoolDoorHasNearbyActor", BindingFlags.Static | BindingFlags.NonPublic);
            state.enemy.active = true; state.enemy.x = 12f; state.enemy.y = 3.8f; state.enemy.z = -7f;
            Require((bool)method.Invoke(null, new object[] { motion, state }), "An active school enemy must open the same doorway as a player using source-to-Unity coordinates.");
            state.enemy.active = false;
            Require(!(bool)method.Invoke(null, new object[] { motion, state }), "An inactive enemy must not hold a school door open.");
            state.enemy.active = true; state.enemy.y = 7.4f;
            Require(!(bool)method.Invoke(null, new object[] { motion, state }), "An enemy on another floor must not open the school door.");
            state.players.Add(new PlayerState { id = "operator", x = 12f, y = 3.8f, z = -7f });
            Require((bool)method.Invoke(null, new object[] { motion, state }), "Adding enemy detection must preserve player proximity detection.");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
