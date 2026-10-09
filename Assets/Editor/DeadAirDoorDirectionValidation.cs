using System;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirDoorDirectionValidation
    {
        [MenuItem("Dead Air/Validate Door Opening Direction")]
        public static void Validate()
        {
            var source = WorldData.Load();
            var world = JsonUtility.FromJson<WorldData>(JsonUtility.ToJson(source));
            int cases = 0;
            foreach (var authored in source.doors)
            {
                var door = JsonUtility.FromJson<DoorData>(JsonUtility.ToJson(authored));
                // Keep the saved rotation and signed angle, isolate the interaction
                // from whatever scene happens to be open in the Editor.
                door.x = -75; door.y = 1.4f; door.z = 75;
                world.doors = new[] { door };
                var rules = new DeadAirRules(world, "host", "Door validation", "engineer", false, true);
                Require(rules.Start("host") == null, "Could not start the host fixture.");
                var actor = rules.State.Player("host");
                if (!string.IsNullOrEmpty(door.requires)) actor.inventory.Add(door.requires);
                var normal = DeadAirOfficeLayout.Rotation(door.rotation) * Vector3.right;
                foreach (float side in new[] { -1f, 1f })
                {
                    var center = DeadAirOfficeLayout.Position(door.x, 0, door.z);
                    var position = center - normal * side * 1.1f;
                    actor.x = -position.x; actor.y = 0; actor.z = position.z;
                    var forward = normal * side;
                    Face(actor, forward);
                    CheckSwing(rules, door, forward); cases++;

                    // Looking back while standing on the same side must pull the
                    // leaf along the view rather than always push it away.
                    Face(actor, -forward);
                    CheckSwing(rules, door, -forward); cases++;

                    // If the view runs along the doorway, use the approach side.
                    Face(actor, DeadAirOfficeLayout.Rotation(door.rotation) * Vector3.forward);
                    CheckSwing(rules, door, forward); cases++;
                }
            }
            ValidateAccessCycleDirection();
            ValidateIdleAndInterruptedSwing();
            Debug.Log("DEAD AIR DOOR DIRECTION PASSED: " + source.doors.Length +
                " authored doors, " + cases + " opening cases; both sides, push/pull view, parallel-view fallback, replicated swing, closing and collider motion; repeatable locker access cycles, interrupted swing and idle transforms.");
        }

        static void CheckSwing(DeadAirRules rules, DoorData door, Vector3 expectedDirection)
        {
            Require(rules.Interact("host", door.id) == null, "Host could not open " + door.id);
            var state = rules.State.Door(door.id);
            Require(state.open, "Host did not open " + door.id);
            var snapshot = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State)).Door(door.id);
            var hostFrame = new GameObject("Door frame " + door.id);
            var peerFrame = new GameObject("Replicated frame " + door.id);
            try
            {
                var host = Hinge(hostFrame.transform, door);
                var peer = Hinge(peerFrame.transform, door);
                var leaf = host.transform.Find("Door leaf");
                var collider = leaf.GetComponent<BoxCollider>();
                var closedCenter = leaf.position;
                host.SetOpen(state.open, state.openSign);
                peer.SetOpen(snapshot.open, snapshot.openSign);
                host.Advance(.24f); peer.Advance(.24f);
                Physics.SyncTransforms();
                Require(Vector3.Dot(leaf.position - closedCenter, expectedDirection) > .01f,
                    "Intermediate swing opposes the player's view: " + door.id);
                Require(collider.bounds.Contains(leaf.position), "The collider did not follow the leaf: " + door.id);
                host.Advance(.24f); peer.Advance(.24f);
                Require(Vector3.Dot(leaf.position - closedCenter, expectedDirection) > .1f,
                    "Open leaf opposes the player's view: " + door.id);
                Require(Quaternion.Angle(host.transform.rotation, peer.transform.rotation) < .001f,
                    "Host and guest disagree about swing direction: " + door.id);
                Require(rules.Interact("host", door.id) == null && !state.open,
                    "Host could not close " + door.id);
                host.SetOpen(false, state.openSign); host.Advance(.48f);
                Require(Vector3.Distance(leaf.position, closedCenter) < .001f,
                    "Closing must restore the authored position: " + door.id);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hostFrame);
                UnityEngine.Object.DestroyImmediate(peerFrame);
            }
        }

        static DeadAirHingedDoor Hinge(Transform frame, DoorData door)
        {
            frame.position = DeadAirOfficeLayout.Position(door.x, 0, door.z);
            frame.rotation = DeadAirOfficeLayout.Rotation(door.rotation);
            var pivot = new GameObject("door:" + door.id).transform;
            pivot.SetParent(frame, false);
            pivot.localPosition = new Vector3(0, 0, -door.w * .5f);
            var leaf = new GameObject("Door leaf").transform;
            leaf.SetParent(pivot, false);
            leaf.localPosition = new Vector3(0, 1.37f, door.w * .5f);
            leaf.gameObject.AddComponent<BoxCollider>().size = new Vector3(.1f, 2.74f, door.w - .08f);
            var hinge = pivot.gameObject.AddComponent<DeadAirHingedDoor>();
            hinge.Initialize(Mathf.Abs(door.openAngle) > .1f ? door.openAngle : 90);
            return hinge;
        }

        static void Face(PlayerState actor, Vector3 direction)
        {
            actor.yaw = (180 - Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        }

        static void ValidateAccessCycleDirection()
        {
            var fixture = new GameObject("Locker access direction fixture");
            try
            {
                var hinge = fixture.AddComponent<DeadAirHingedDoor>(); hinge.Initialize(-105);
                for (int i = 0; i < 2; i++)
                {
                    hinge.PlayAccessCycle(-1); hinge.Advance(.5f);
                    Require(hinge.Angle > 100, "Repeated negative access cycles must keep the same direction.");
                    hinge.transform.hasChanged = false; hinge.Advance(.1f);
                    Require(!hinge.transform.hasChanged, "A held access-cycle leaf must not dirty its transform.");
                    hinge.Advance(.7f);
                    Require(Mathf.Abs(hinge.Angle) < .001f && !hinge.IsAjar, "Access cycle must finish shut.");
                    RequireIdle(hinge, "Completed access cycle");
                }
                hinge.SetOpen(true); hinge.Advance(.48f);
                Require(hinge.Angle < -100, "Access cycles must preserve the authored opening angle.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        static void ValidateIdleAndInterruptedSwing()
        {
            var fixture = new GameObject("Door idle transform fixture");
            try
            {
                fixture.transform.localRotation = Quaternion.Euler(3, 17, 2);
                var closed = fixture.transform.localRotation;
                var hinge = fixture.AddComponent<DeadAirHingedDoor>(); hinge.Initialize(95);
                RequireIdle(hinge, "Initialized closed door");
                hinge.SetOpen(true); hinge.Advance(.12f);
                Require(hinge.Angle > 0 && hinge.Angle < 95 && fixture.transform.hasChanged,
                    "A settled door must resume its opening animation.");
                hinge.SetOpen(false); hinge.Advance(.48f);
                Require(Quaternion.Angle(closed, fixture.transform.localRotation) < .001f,
                    "An interrupted opening must close to its authored rotation.");
                RequireIdle(hinge, "Closed door");
                hinge.SetOpen(true, -1); hinge.Advance(.48f);
                Require(hinge.Angle < -94, "A closed idle door must reopen in the new direction.");
                RequireIdle(hinge, "Open door");
                hinge.SetOpen(false); hinge.Advance(.48f);
                RequireIdle(hinge, "Reclosed door");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        static void RequireIdle(DeadAirHingedDoor hinge, string context)
        {
            float angle = hinge.Angle;
            hinge.transform.hasChanged = false;
            for (int i = 0; i < 8; i++) hinge.Advance(.1f);
            Require(!hinge.transform.hasChanged && hinge.Angle == angle,
                context + " must retain its angle without writing the transform.");
        }

        static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Door direction validation: " + message);
        }
    }
}
