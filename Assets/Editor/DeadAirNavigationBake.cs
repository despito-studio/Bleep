using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirNavigationBake
    {
        const string Path = "Assets/Resources/officeNavigation.json";

        [MenuItem("Dead Air/Bake Saved Office Navigation")]
        public static void BakeSavedPrefabs()
        {
            var asset = Resources.Load<TextAsset>("world");
            Require(asset != null, "Authored world data is missing.");
            // Always start with the authored graph, even if a previous matching
            // overlay exists. A model/collider repair may change its clearance.
            var world = JsonUtility.FromJson<WorldData>(asset.text);
            var station = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var exterior = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            Require(station != null && exterior != null, "Saved office prefabs are missing.");
            var stage = new GameObject("Static navigation bake");
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.transform.position = new Vector3(1500, 0, 0);
            DeadAirNavigationBakeData bake;
            int blockedNodes, blockedEdges;
            try
            {
                UnityEngine.Object.Instantiate(station, stage.transform, false);
                UnityEngine.Object.Instantiate(exterior, stage.transform, false);
                var physical = new DeadAirNavigationValidation.PhysicalRoutes(world, stage.transform.position, stage.transform);
                var spawn = stage.transform.position + DeadAirOfficeLayout.Position(world.spawn.x, world.spawn.y, world.spawn.z);
                int start = -1; float nearest = float.PositiveInfinity;
                for (int i = 0; i < physical.positions.Length; i++)
                {
                    float distance = (physical.positions[i] - spawn).sqrMagnitude;
                    if (!physical.clear[i] || distance >= nearest || Mathf.Abs(physical.positions[i].y - spawn.y) > .35f || !physical.BodySweep(spawn, physical.positions[i])) continue;
                    start = i; nearest = distance;
                }
                Require(start >= 0 && nearest < 4, "Spawn has no physical navigation connection.");
                var queue = new Queue<int>(); queue.Enqueue(start); physical.reachable.Add(start);
                while (queue.Count > 0)
                    foreach (int next in physical.links[queue.Dequeue()]) if (physical.reachable.Add(next)) queue.Enqueue(next);
                ValidateDestinations(world, physical, stage.transform.position);

                var sources = physical.reachable.OrderBy(i => i).ToArray();
                var remap = sources.Select((source, index) => (source, index)).ToDictionary(p => p.source, p => p.index);
                var links = new List<NavigationLinkData>();
                foreach (int source in sources) foreach (int other in physical.links[source])
                    if (source < other && remap.TryGetValue(other, out int target)) links.Add(new NavigationLinkData { a = remap[source], b = target });
                bake = new DeadAirNavigationBakeData {
                    version = DeadAirNavigationOverlay.Version, worldSha256 = DeadAirNavigationOverlay.Hash(asset.bytes),
                    authoredNodeCount = world.navigationNodes.Length, authoredLinkCount = world.navigationLinks.Length,
                    sourceNodes = sources, links = links.ToArray()
                };
                blockedNodes = physical.blockedNodes; blockedEdges = physical.blockedEdges;
                Require(DeadAirNavigationOverlay.TryApply(world, asset.bytes, bake, out var reason), "Bake failed its load guard: " + reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(stage); Physics.SyncTransforms(); }
            File.WriteAllText(Path, JsonUtility.ToJson(bake, true) + "\n");
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateSaved();
            Directory.CreateDirectory("Art/OfficeRevision");
            string report = "PASS: " + bake.sourceNodes.Length + "/" + bake.authoredNodeCount + " authored nodes; " + bake.links.Length + "/" + bake.authoredLinkCount +
                " authored links; excluded " + blockedNodes + " body-blocked nodes and " + blockedEdges + " blocked edges; spawn-connected campus; all " + world.rooms.Length +
                " rooms, " + world.doors.Length + " doorways and " + world.objects.Length + " objectives have real capsule approaches; SHA256/version/index/shortcut fallback guards.\n";
            File.WriteAllText("Art/OfficeRevision/navigation-bake-report.txt", report);
            Debug.Log("DEAD AIR STATIC NAVIGATION BAKED: " + report);
        }

        static void ValidateDestinations(WorldData world, DeadAirNavigationValidation.PhysicalRoutes physical, Vector3 offset)
        {
            foreach (var door in world.doors)
            {
                var normal = new Vector2(Mathf.Cos(door.rotation), -Mathf.Sin(door.rotation));
                var a = offset + DeadAirOfficeLayout.Position(door.x + normal.x * .75f, door.y - 1.4f, door.z + normal.y * .75f);
                var b = offset + DeadAirOfficeLayout.Position(door.x - normal.x * .75f, door.y - 1.4f, door.z - normal.y * .75f);
                Require(physical.BodyClear(a) && physical.BodyClear(b) && physical.BodySweep(a, b) && physical.ReachableApproach(a, .7f) >= 0 && physical.ReachableApproach(b, .7f) >= 0,
                    "Baked navigation cannot reach both sides of door: " + door.id);
            }
            foreach (var room in world.rooms)
            {
                bool entered = physical.reachable.Any(index => {
                    var position = physical.positions[index] - offset; float x = -position.x;
                    bool floor = Mathf.Abs(position.y - room.floorY) < .3f;
                    if (room.mapLabel == "HALL BASEMENT STAIRS") floor |= world.stairs.Any(s => s.lowerY < 0 && s.Contains(x, position.z) && Mathf.Abs(position.y - s.HeightAt(position.z)) < .3f);
                    return floor && x > room.minX + .25f && x < room.maxX - .25f && position.z > room.minZ + .25f && position.z < room.maxZ - .25f;
                });
                Require(entered, "Baked navigation cannot enter room: " + room.name);
            }
            foreach (var objective in world.objects)
            {
                float floor = objective.y < -1 ? -DeadAirOfficeLayout.FloorHeight : objective.y >= DeadAirOfficeLayout.FloorHeight ? DeadAirOfficeLayout.FloorHeight : 0;
                float vertical = objective.y - floor - 1;
                float range = Mathf.Sqrt(Mathf.Max(0, 2.5f * 2.5f - vertical * vertical));
                Require(physical.ReachableApproach(offset + DeadAirOfficeLayout.Position(objective.x, floor, objective.z), range) >= 0, "Baked quest approach is unreachable: " + objective.id);
            }
        }

        [MenuItem("Dead Air/Validate Baked Navigation Guard")]
        public static void ValidateSaved()
        {
            var source = Resources.Load<TextAsset>("world");
            var saved = AssetDatabase.LoadAssetAtPath<TextAsset>(Path);
            Require(source != null && saved != null, "Navigation bake is missing; regenerate saved prefabs first.");
            var bake = JsonUtility.FromJson<DeadAirNavigationBakeData>(saved.text);
            var world = JsonUtility.FromJson<WorldData>(source.text);
            var nodes = world.navigationNodes; var links = world.navigationLinks;
            var changed = (byte[])source.bytes.Clone(); changed[0] ^= 1;
            Require(!DeadAirNavigationOverlay.TryApply(world, changed, bake, out _), "A stale world hash accepted its old navigation.");
            Require(ReferenceEquals(nodes, world.navigationNodes) && ReferenceEquals(links, world.navigationLinks) && !world.navigationBaked, "Rejected overlay mutated the authored graph.");
            var bad = JsonUtility.FromJson<DeadAirNavigationBakeData>(saved.text); bad.version++;
            Require(!DeadAirNavigationOverlay.TryApply(world, source.bytes, bad, out _), "Unsupported bake schema accepted.");
            bad = JsonUtility.FromJson<DeadAirNavigationBakeData>(saved.text); bad.sourceNodes[0] = nodes.Length;
            Require(!DeadAirNavigationOverlay.TryApply(world, source.bytes, bad, out _), "Out-of-range retained node accepted.");
            bad = JsonUtility.FromJson<DeadAirNavigationBakeData>(saved.text); bad.links[0].a = bad.sourceNodes.Length;
            Require(!DeadAirNavigationOverlay.TryApply(world, source.bytes, bad, out _), "Out-of-range retained edge accepted.");
            bad = JsonUtility.FromJson<DeadAirNavigationBakeData>(saved.text); bad.links[0].a = 0; bad.links[0].b = bad.sourceNodes.Length - 1;
            Require(!DeadAirNavigationOverlay.TryApply(world, source.bytes, bad, out _), "Unauthored long-distance shortcut accepted.");
            var rooms = world.rooms; var props = world.props; var objectives = world.objects; var doors = world.doors; var classes = world.classes;
            Require(DeadAirNavigationOverlay.TryApply(world, source.bytes, bake, out var reason), "Saved navigation does not match world.json: " + reason);
            Require(world.navigationBaked && ReferenceEquals(rooms, world.rooms) && ReferenceEquals(props, world.props) && ReferenceEquals(objectives, world.objects) && ReferenceEquals(doors, world.doors) && ReferenceEquals(classes, world.classes), "Navigation overlay replaced mission or asset fields.");
            Require(world.navigationNodes.Length == bake.sourceNodes.Length && world.navigationLinks.Length == bake.links.Length, "Navigation overlay retained the wrong graph size.");
            Debug.Log("DEAD AIR NAVIGATION HASH GUARD PASSED: matching source applied; stale hash/schema/invalid indices rejected without modifying authored world; mission and class records retained.");
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
