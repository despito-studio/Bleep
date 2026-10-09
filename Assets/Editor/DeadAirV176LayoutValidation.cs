using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirV176LayoutValidation
    {
        [MenuItem("Dead Air/Validate v1.7.6 Undercroft and Basement Storage")]
        public static void Validate()
        {
            var data = WorldData.Load();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            Require(prefab != null, "The regenerated station prefab is missing.");
            var station = UnityEngine.Object.Instantiate(prefab);
            var offset = new Vector3(1500, 0, 0);
            station.transform.position = offset;
            try
            {
                var parts = station.GetComponentsInChildren<Transform>(true);
                Physics.SyncTransforms();
                ValidateUndercroft(data, parts, offset);
                foreach (var door in data.doors)
                    foreach (var collider in parts.Single(t => t.name == "door:" + door.id).GetComponentsInChildren<Collider>(true))
                        collider.enabled = false;
                Physics.SyncTransforms();
                ValidateStorage(data, parts, offset);
                ValidateRoutes(data, offset);
                Debug.Log("DEAD AIR V1.7.6 LAYOUT PASSED: stepped brick sides below the upper treads, open steel guards and both stair flights, wall-facing basement storage, supported capsule passages to all cells, shelves, mushrooms, breaker, diesel canister and diary.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(station);
                Physics.SyncTransforms();
            }
        }

        static void ValidateUndercroft(WorldData data, Transform[] parts, Vector3 offset)
        {
            var upper = data.stairs.Single(s => s.lowerY == 0 && s.upperY > 1);
            var root = parts.Single(t => t.name == "Upper stair undercroft");
            float rise = (upper.upperY - upper.lowerY) / upper.steps;
            float depth = (upper.maxZ - upper.minZ) / upper.steps;
            var walls = root.GetComponentsInChildren<BoxCollider>(true);
            int expectedCount = Enumerable.Range(1, upper.steps).Count(step => step * rise - .175f > .001f) * 2;
            Require(walls.Length == expectedCount, "Each elevated upper tread needs both undercroft sides.");
            for (int step = 1; step <= upper.steps; step++)
            {
                float height = step * rise - .175f;
                float z = upper.maxZ - (step - .5f) * depth;
                foreach (float x in new[] { upper.minX + .05f, upper.maxX - .05f })
                {
                    string name = "Undercroft brick " + (x < 0 ? "west " : "east ") + step;
                    if (height <= .001f)
                    {
                        Require(!walls.Any(c => c.name == name), "No brick may project above the lowest tread.");
                        continue;
                    }
                    var wall = walls.Single(c => c.name == name);
                    Require(wall.enabled && !wall.isTrigger && wall.transform.parent == root,
                        "Undercroft sides must be physical walls confined to the upper flight: " + name);
                    Require(Vector3.Distance(wall.bounds.center, Point(x, upper.lowerY + height / 2, z, offset)) < .002f &&
                        Vector3.Distance(wall.size, new Vector3(.22f, height, depth)) < .002f,
                        "The brick enclosure must follow the underside of its tread: " + name);
                    Require(Mathf.Abs(wall.bounds.min.y - upper.lowerY) < .002f &&
                        wall.bounds.max.y <= upper.lowerY + step * rise - .17f,
                        "Brick must run from the first-floor surface to below the tread, leaving its rail space open.");
                }
            }
            foreach (var stair in data.stairs)
            {
                string level = stair.lowerY < 0 ? "basement" : "upper";
                var rails = parts.Single(t => t.name == "Steel stair rails:" + level).GetComponentsInChildren<Collider>();
                float stairRise = (stair.upperY - stair.lowerY) / stair.steps;
                float stairDepth = (stair.maxZ - stair.minZ) / stair.steps;
                for (int step = 1; step <= stair.steps; step++)
                {
                    float z = stair.maxZ - (step - .5f) * stairDepth;
                    foreach (float x in new[] { stair.minX + .09f, stair.maxX - .09f })
                        foreach (float height in new[] { .24f, .64f, 1.06f })
                        {
                            var sample = Point(x, stair.HeightAt(z) + height, z, offset);
                            Require(rails.Any(c => c.enabled && Vector3.Distance(c.ClosestPoint(sample), sample) < .035f),
                                "Both sides of the " + level + " flight must retain their steel guards.");
                            if (stair.lowerY >= 0)
                                Require(walls.All(c => Vector3.Distance(c.ClosestPoint(sample), sample) > .15f),
                                    "The restored brick must stay below the open upper-flight guard rails.");
                        }
                    var feet = Point(0, stair.lowerY + step * stairRise, z, offset);
                    Clear(feet, "Centre of the " + level + " flight");
                    Support(feet, "Centre of the " + level + " flight");
                }
            }
            Clear(Point(0, 0, -50.5f, offset), "Basement entrance landing below the upper flight");
            Support(Point(0, 0, -50.5f, offset), "Basement entrance landing below the upper flight");
        }

        static void ValidateStorage(WorldData data, Transform[] parts, Vector3 offset)
        {
            float floor = -DeadAirOfficeLayout.FloorHeight;
            var shelves = data.props.Where(p => p.type == "shelf" && p.y < -1).ToArray();
            Require(shelves.Length == 2, "Keep both basement storage shelves.");
            var tools = shelves.Single(p => p.label == "Стелаж з інструментами");
            var spares = shelves.Single(p => p.label == "Стелаж із запчастинами");
            Shelf(tools, -5.5f, -31.5f, new Vector3(-1, 0, 0));
            Shelf(spares, -3.7f, -39, Vector3.forward);
            var centre = Point(0, floor, -35.5f, offset);
            Passage(centre, Point(-8.5f, floor, -35.5f, offset), "West-cell gateway between the old shelf positions");
            Passage(centre, Point(8.5f, floor, -35.5f, offset), "East-cell gateway");
            Passage(Point(0, floor, -39.1f, offset), Point(0, floor, -28.4f, offset), "Main basement aisle from stair exit to breaker");
            Passage(Point(0, floor, -31.5f, offset), Point(-4.5f, floor, -31.5f, offset), "Tool shelf front");
            Passage(Point(0, floor, -38.1f, offset), Point(-3.7f, floor, -38.1f, offset), "Spare-parts shelf front");

            void Shelf(PropData spec, float x, float z, Vector3 forward)
            {
                var root = parts.Single(t => t.name == "prop:" + spec.id);
                Require(Vector3.Distance(root.position, Point(x, floor, z, offset)) < .002f &&
                    Vector3.Dot(root.forward, forward) > .999f,
                    "A storage shelf must sit against its wall with its front facing the electrical room: " + spec.id);
                Require(root.GetComponentsInChildren<Collider>(true).Count(c => c.enabled && !c.isTrigger) >= 7,
                    "The relocated shelf must retain its physical sides and all five shelves.");
            }
        }

        static void ValidateRoutes(WorldData data, Vector3 offset)
        {
            float floor = -DeadAirOfficeLayout.FloorHeight;
            var adjacency = data.navigationNodes.Select(_ => new List<int>()).ToArray();
            foreach (var link in data.navigationLinks) { adjacency[link.a].Add(link.b); adjacency[link.b].Add(link.a); }
            int start = Closest(new Vector3(data.spawn.x, data.spawn.y, data.spawn.z));
            var reached = new HashSet<int> { start };
            var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (int next in adjacency[queue.Dequeue()]) if (reached.Add(next)) queue.Enqueue(next);
            Route(new Vector3(-4.5f, floor, -31.5f), "Tool shelf front");
            Route(new Vector3(-3.7f, floor, -38.1f), "Spare-parts shelf front");
            Route(new Vector3(0, floor, -28.4f), "Breaker approach");
            Route(new Vector3(.5f, floor, -32), "Basement diesel canister approach");
            Route(new Vector3(-13.3f, floor, -29.45f), "Prison diary approach");
            foreach (var door in data.doors.Where(d => d.id.StartsWith("door-prison-", StringComparison.Ordinal)))
            {
                float side = Mathf.Sign(door.x);
                var a = new Vector3(side * 8.5f, floor, door.z);
                var b = new Vector3(side * 12.25f, floor, door.z);
                Route(a, door.id + " gallery"); Route(b, door.id + " cell approach");
                Passage(Point(a.x, a.y, a.z, offset), Point(b.x, b.y, b.z, offset), door.id + " doorway");
            }
            foreach (var mushroom in data.Mushrooms.Where(m => m.y < -1))
                Route(new Vector3(mushroom.x, mushroom.y, mushroom.z), mushroom.id);

            int Closest(Vector3 target) => Enumerable.Range(0, data.navigationNodes.Length).OrderBy(i =>
                (new Vector3(data.navigationNodes[i].x, data.navigationNodes[i].y, data.navigationNodes[i].z) - target).sqrMagnitude).First();
            void Route(Vector3 source, string label)
            {
                int index = Closest(source); var node = data.navigationNodes[index];
                Require(reached.Contains(index) && Vector3.Distance(new Vector3(node.x, node.y, node.z), source) < .8f,
                    "An authored route from the entrance must reach " + label);
                var feet = Point(source.x, source.y, source.z, offset);
                Clear(feet, label); Support(feet, label);
            }
        }

        static Vector3 Point(float x, float y, float z, Vector3 offset) => DeadAirOfficeLayout.Position(x, y, z) + offset;
        static void Clear(Vector3 feet, string label)
        {
            var hits = Physics.OverlapCapsule(feet + Vector3.up * .26f, feet + Vector3.up * 1.48f, .22f, ~0, QueryTriggerInteraction.Ignore);
            Require(hits.Length == 0, "Player body is obstructed at " + label + ": " + string.Join(", ", hits.Select(c => c.name)));
        }
        static void Support(Vector3 feet, string label)
        {
            Require(Physics.Raycast(feet + Vector3.up * .15f, Vector3.down, out var hit, .30f, ~0, QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(hit.point.y - feet.y) < .02f, "A physical floor must support " + label);
        }
        static void Passage(Vector3 from, Vector3 to, string label)
        {
            var delta = to - from;
            Require(!Physics.CapsuleCast(from + Vector3.up * .26f, from + Vector3.up * 1.48f, .22f,
                delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore),
                "Player body passage is blocked at " + label + (hit.collider != null ? ": " + hit.collider.name : "."));
            for (int i = 0; i <= 12; i++) { var feet = Vector3.Lerp(from, to, i / 12f); Clear(feet, label); Support(feet, label); }
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("v1.7.6 layout: " + message); }
    }
}
