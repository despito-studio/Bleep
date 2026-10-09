using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    // Validates saved collider geometry and rebuilds only small period prop
    // fixtures, whose source meshes have been removed by room batching.
    public static class DeadAirOfficePlacementValidation
    {
        sealed class Furniture
        {
            public PropData data;
            public Transform root;
            public Collider[] colliders;
        }
        sealed class Piece
        {
            public string owner, label;
            public Bounds bounds;
        }
        static readonly string[] DesktopTypes = { "deskLamp", "rotaryTelephone", "typewriter", "tapeRecorder", "kettle" };
        static readonly string[] FurnishedDesks = { "workstation", "sovietWorkstation", "table", "archivistDesk", "anchorDesk", "meetingTable", "coffeeTable" };

        [MenuItem("Dead Air/Validate Saved Office Placements")]
        public static void Validate()
        {
            var world = WorldData.Load();
            var stationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var exteriorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            if (stationPrefab == null || exteriorPrefab == null) throw new InvalidOperationException("Saved office prefabs are missing.");
            var stage = new GameObject("Saved placement validation");
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.transform.position = new Vector3(1500, 0, 0);
            var palette = new Material(Shader.Find("Dead Air/Source Surface"));
            var issues = new List<string>();
            int propsChecked = 0, desktopsChecked = 0, mountedChecked = 0, doorsChecked = 0;
            try
            {
                var station = UnityEngine.Object.Instantiate(stationPrefab, stage.transform, false);
                var exterior = UnityEngine.Object.Instantiate(exteriorPrefab, stage.transform, false);
                var parts = station.GetComponentsInChildren<Transform>(true).Concat(exterior.GetComponentsInChildren<Transform>(true)).ToArray();
                var allColliders = station.GetComponentsInChildren<Collider>(true).Concat(exterior.GetComponentsInChildren<Collider>(true)).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray();
                Physics.SyncTransforms();
                var walls = allColliders.OfType<BoxCollider>().Where(c => Structural(c.transform) && c.size.y > .5f).ToArray();
                var floorSolids = allColliders.OfType<BoxCollider>().Where(c => Structural(c.transform) && c.size.y < .5f && c.size.x > .01f && c.size.z > .01f && Vector3.Dot(c.transform.up, Vector3.up) > .999f).ToArray();
                foreach (var outside in world.rooms.Where(r => r.mapLabel == "PATH" || r.mapLabel == "COURTYARD"))
                    if (DeadAirOfficeLayout.IsInterior(outside)) Issue("Outdoor recess classified as an office interior: " + outside.name);
                foreach (var court in world.rooms.Where(r => r.mapLabel == "COURTYARD"))
                    foreach (float x in new[] { court.minX + .4f, (court.minX + court.maxX) * .5f, court.maxX - .4f })
                        foreach (float z in new[] { court.minZ + .4f, (court.minZ + court.maxZ) * .5f, court.maxZ - .4f })
                        {
                            var point = stage.transform.position + DeadAirOfficeLayout.Position(x, court.floorY, z);
                            if (!floorSolids.Any(floor => FloorSupports(floor, point))) Issue("Courtyard floor is unsupported: " + court.name + " at " + x + "," + z);
                            if (floorSolids.Any(solid => CoversXZ(solid, point) && solid.bounds.min.y > point.y + 1 && solid.bounds.max.y < point.y + 12))
                                Issue("Open courtyard covered by an office slab/roof: " + court.name + " at " + x + "," + z);
                        }
                var furniture = new List<Furniture>();
                foreach (var prop in world.props)
                {
                    var roots = parts.Where(t => t.name == "prop:" + prop.id || prop.type == "locker" && t.name == "interactive:" + prop.id).ToArray();
                    if (roots.Length != 1) { Issue("Missing/duplicated prop root: " + prop.id); continue; }
                    var root = roots[0];
                    var expected = stage.transform.position + DeadAirOfficeLayout.Position(prop.x, prop.y, prop.z);
                    if (Vector3.Distance(root.position, expected) > .002f || Quaternion.Angle(root.rotation, DeadAirOfficeLayout.Rotation(prop.rotation)) > .02f)
                        Issue("Saved transform disagrees with data: " + prop.id);
                    if (IsFloorProp(prop) && !floorSolids.Any(floor => FloorSupports(floor, root.position)))
                        Issue("Floor prop has no solid support: " + prop.id);
                    furniture.Add(new Furniture { data = prop, root = root, colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray() });
                    propsChecked++;
                }
                Physics.SyncTransforms();

                // Tests actual box/mesh penetration rather than rectangular data
                // envelopes: rotated chairs, open booths and table legs stay valid.
                for (int a = 0; a < furniture.Count; a++)
                {
                    var first = furniture[a];
                    if (DesktopTypes.Contains(first.data.type)) continue;
                    for (int b = a + 1; b < furniture.Count; b++)
                    {
                        var second = furniture[b];
                        if (DesktopTypes.Contains(second.data.type) || Mathf.Abs(first.data.y - second.data.y) > .1f) continue;
                        if (Penetrates(first.colliders, second.colliders, .035f)) Issue("Furniture intersects: " + first.data.id + " / " + second.data.id);
                    }
                    if (Penetrates(first.colliders, walls, .025f)) Issue("Furniture intersects a structural wall: " + first.data.id);
                }

                var desktopPieces = new List<Piece>();
                var tops = allColliders.OfType<BoxCollider>().Where(c => c.name == "Desk top" || c.name == "Low tabletop" || c.name == "Low rounded tabletop" || c.name == "Kitchen worktop").ToArray();
                foreach (var entry in furniture.Where(f => DesktopTypes.Contains(f.data.type) || FurnishedDesks.Contains(f.data.type)))
                {
                    var fixture = new GameObject("Period geometry fixture");
                    fixture.transform.SetParent(stage.transform, false);
                    fixture.transform.SetPositionAndRotation(entry.root.position, entry.root.rotation);
                    bool standalone = DesktopTypes.Contains(entry.data.type);
                    if (standalone) BuildEquipment(fixture.transform, entry.data.type, Vector3.zero, palette);
                    else BuildDeskEquipment(fixture.transform, entry.data, palette);
                    var renderers = fixture.GetComponentsInChildren<MeshRenderer>(true);
                    if (standalone)
                    {
                        var footing = renderers.Single(r => r.name == FootingName(entry.data.type));
                        if (!tops.Any(top => Supports(top, footing))) Issue("Desktop item has no flush tabletop support: " + entry.data.id + " baseY=" + footing.bounds.min.y.ToString("F3"));
                        desktopsChecked++;
                    }
                    foreach (var renderer in renderers)
                    {
                        desktopPieces.Add(new Piece { owner = entry.data.id, label = renderer.name, bounds = renderer.bounds });
                    }
                    UnityEngine.Object.DestroyImmediate(fixture);
                }
                for (int a = 0; a < desktopPieces.Count; a++)
                    for (int b = a + 1; b < desktopPieces.Count; b++)
                        if (desktopPieces[a].owner != desktopPieces[b].owner && VolumeOverlap(desktopPieces[a].bounds, desktopPieces[b].bounds, .004f))
                            Issue("Desktop geometry intersects: " + desktopPieces[a].owner + "/" + desktopPieces[a].label + " / " + desktopPieces[b].owner + "/" + desktopPieces[b].label);
                foreach (var objective in world.objects.Where(o => o.type == "tape" || o.type == "keycard" || o.type == "fuse"))
                {
                    var target = parts.FirstOrDefault(t => t.name == "interactive:" + objective.id);
                    if (target == null) { Issue("Missing quest item: " + objective.id); continue; }
                    var meshes = target.GetComponentsInChildren<MeshRenderer>(true);
                    if (meshes.Length == 0) { Issue("Quest item has no inspectable geometry: " + objective.id); continue; }
                    var bounds = CombinedBounds(meshes);
                    foreach (var item in desktopPieces)
                        if (VolumeOverlap(bounds, item.bounds, .003f)) Issue("Quest item intersects desktop equipment: " + objective.id + " / " + item.owner + "/" + item.label);
                }

                foreach (var entry in furniture)
                {
                    float width, height, centerY, depth;
                    if (entry.data.type == "wallClock") { width = .58f; height = .58f; centerY = 2.55f; depth = .10f; }
                    else if (entry.data.type == "wallMap") { width = entry.data.w; height = 1.7f; centerY = 1.94f; depth = .09f; }
                    else continue;
                    if (!WallBacked(entry.root, width, height, centerY, depth, walls)) Issue("Decoration has no solid wall backing: " + entry.data.id);
                    mountedChecked++;
                }
                foreach (var calendar in parts.Where(t => t.name == "Abandoned 1991 calendar"))
                {
                    if (!WallBacked(calendar, .37f, .51f, 0, .035f, walls)) Issue("Calendar has no solid wall backing: " + calendar.parent.name);
                    mountedChecked++;
                }

                var ownerByCollider = new Dictionary<Collider,string>();
                foreach (var entry in furniture) foreach (var collider in entry.colliders) ownerByCollider[collider] = entry.data.id;
                foreach (var door in world.doors)
                {
                    var center = stage.transform.position + DeadAirOfficeLayout.Position(door.x, door.y - 1.4f, door.z);
                    var normal = DeadAirOfficeLayout.Rotation(door.rotation) * Vector3.right;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var feet = center + normal * side * .65f;
                        foreach (var hit in Physics.OverlapCapsule(feet + Vector3.up * .27f, feet + Vector3.up * 1.47f, .23f, ~0, QueryTriggerInteraction.Ignore))
                            if (ownerByCollider.TryGetValue(hit, out var id)) Issue("Door approach blocked by furniture: " + door.id + " / " + id);
                    }
                    doorsChecked++;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(stage); UnityEngine.Object.DestroyImmediate(palette); Physics.SyncTransforms(); }
            Directory.CreateDirectory("Art/OfficeRevision");
            var summary = propsChecked + " saved props; " + desktopsChecked + " desktop supports; " + mountedChecked + " wall mounts; " + doorsChecked + " doorway approaches.";
            var report = new[] { (issues.Count == 0 ? "PASS: " : "FAIL: ") + summary }.Concat(issues.Distinct()).ToArray();
            File.WriteAllLines("Art/OfficeRevision/placement-report.txt", report);
            if (issues.Count > 0) throw new InvalidOperationException("Office placements: " + string.Join(" | ", issues.Distinct().Take(30)) + " (full report: Art/OfficeRevision/placement-report.txt)");
            Debug.Log("DEAD AIR OFFICE PLACEMENTS PASSED: " + summary);

            void Issue(string message) { if (!issues.Contains(message)) issues.Add(message); }
        }

        static bool Structural(Transform transform)
        {
            for (var part = transform; part != null; part = part.parent)
            {
                if (part.name.StartsWith("door:", StringComparison.Ordinal) || part.name.StartsWith("Door frame ", StringComparison.Ordinal)) return false;
                if (part.name == "shell") return true;
            }
            return false;
        }
        static bool Penetrates(IEnumerable<Collider> first, IEnumerable<Collider> second, float threshold)
        {
            foreach (var a in first) foreach (var b in second)
                if (a.bounds.Intersects(b.bounds) && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out float distance) && distance > threshold) return true;
            return false;
        }
        static bool Supports(BoxCollider top, MeshRenderer footing)
        {
            if (Mathf.Abs(top.bounds.max.y - footing.bounds.min.y) > .009f) return false;
            var bounds = footing.GetComponent<MeshFilter>().sharedMesh.bounds;
            foreach (float x in new[] { bounds.min.x, bounds.max.x }) foreach (float z in new[] { bounds.min.z, bounds.max.z })
            {
                var corner = footing.transform.TransformPoint(new Vector3(x, bounds.min.y, z));
                var local = top.transform.InverseTransformPoint(corner) - top.center;
                if (Mathf.Abs(local.x) > top.size.x * .5f - .002f || Mathf.Abs(local.z) > top.size.z * .5f - .002f) return false;
            }
            return true;
        }
        static bool IsFloorProp(PropData prop)
        {
            // Wall panels use floor-origin transforms despite being mounted above
            // the ground. Desktop origins intentionally use the tabletop height.
            return !DesktopTypes.Contains(prop.type) && !new[] { "corkboard", "toolboard", "directory", "mirror", "whiteboard", "fusePanel", "fan", "wallClock", "wallMap", "fireSafetyBoard" }.Contains(prop.type);
        }
        static bool FloorSupports(BoxCollider floor, Vector3 foot)
        {
            var local = floor.transform.InverseTransformPoint(foot) - floor.center;
            var half = floor.size * .5f;
            return Mathf.Abs(local.x) < half.x + .002f && Mathf.Abs(local.z) < half.z + .002f && Mathf.Abs(local.y - half.y) < .05f;
        }
        static bool CoversXZ(BoxCollider solid, Vector3 point)
        {
            var local = solid.transform.InverseTransformPoint(new Vector3(point.x, solid.bounds.center.y, point.z)) - solid.center;
            return Mathf.Abs(local.x) < solid.size.x * .5f && Mathf.Abs(local.z) < solid.size.z * .5f;
        }
        static bool WallBacked(Transform root, float width, float height, float centerY, float depth, BoxCollider[] walls)
        {
            foreach (float x in new[] { -width * .40f, 0, width * .40f })
                foreach (float y in new[] { centerY - height * .35f, centerY + height * .35f })
                {
                    var back = root.TransformPoint(new Vector3(x, y, -depth * .5f));
                    var probe = back - root.forward * .010f;
                    if (!walls.Any(wall => Contains(wall, probe, .005f) && FlushAt(wall, back, root.forward))) return false;
                }
            return true;
        }
        static bool FlushAt(BoxCollider wall, Vector3 back, Vector3 outward)
        {
            var point = wall.transform.InverseTransformPoint(back) - wall.center;
            var normal = wall.transform.InverseTransformVector(outward);
            int axis = Mathf.Abs(normal.x) > Mathf.Abs(normal.z) ? 0 : 2;
            float face = Mathf.Sign(normal[axis]) * wall.size[axis] * .5f;
            return Mathf.Abs((point[axis] - face) / normal[axis]) <= .009f;
        }
        static bool Contains(BoxCollider collider, Vector3 point, float margin)
        {
            var local = collider.transform.InverseTransformPoint(point) - collider.center;
            var half = collider.size * .5f + Vector3.one * margin;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        static bool VolumeOverlap(Bounds a, Bounds b, float threshold)
        {
            var size = Vector3.Min(a.max, b.max) - Vector3.Max(a.min, b.min);
            return size.x > threshold && size.y > threshold && size.z > threshold;
        }
        static Bounds CombinedBounds(MeshRenderer[] renderers)
        {
            var result = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) result.Encapsulate(renderer.bounds);
            return result;
        }
        static string FootingName(string type)
        {
            switch (type)
            {
                case "deskLamp": return "Desk lamp weighted base";
                case "rotaryTelephone": return "Rotary telephone base";
                case "typewriter": return "Mechanical typewriter body";
                case "tapeRecorder": return "Reel recorder wooden case";
                case "kettle": return "Enamel kettle body";
                default: throw new InvalidOperationException("Unhandled desktop footing: " + type);
            }
        }
        static void BuildEquipment(Transform parent, string type, Vector3 position, Material palette)
        {
            var equipment = new GameObject(type).transform; equipment.SetParent(parent, false); equipment.localPosition = position;
            if (!DeadAirPeriodProps.TryBuild(equipment, new PropData { type = type }, FixtureBlock, FixtureSign, palette, palette, palette, palette, palette, palette))
                throw new InvalidOperationException("Cannot reconstruct desktop geometry: " + type);
        }
        static void BuildDeskEquipment(Transform root, PropData prop, Material palette)
        {
            // Rebuild only equipment. Running the full furniture authoring helper
            // would unnecessarily rewrite shared mesh/material assets during audit.
            if (prop.type == "workstation" || prop.type == "sovietWorkstation")
            {
                BuildEquipment(root, "typewriter", new Vector3(.12f, .82f, -.14f), palette);
                BuildEquipment(root, "rotaryTelephone", new Vector3(-prop.w * .34f, .82f, -prop.d * .20f), palette);
                BuildEquipment(root, "deskLamp", new Vector3(prop.w * .36f, .82f, -prop.d * .27f), palette);
                FixtureBlock(root, "Unfinished typed report", new Vector3(-prop.w * .25f, .826f, prop.d * .21f), new Vector3(.42f, .012f, .30f), palette, false);
            }
            else if (prop.type == "coffeeTable")
                FixtureBlock(root, "Abandoned journal", new Vector3(.15f, .49f, 0), new Vector3(.34f, .035f, .25f), palette, false);
            else
            {
                if (prop.type == "meetingTable") BuildEquipment(root, "rotaryTelephone", new Vector3(0, .82f, 0), palette);
                if (prop.type == "archivistDesk") BuildEquipment(root, "rotaryTelephone", new Vector3(prop.w * .32f, .82f, -prop.d * .24f), palette);
                FixtureBlock(root, "Desk document stack", new Vector3(0, .837f, prop.d * .30f), new Vector3(.37f, .034f, .26f), palette, false);
            }
        }
        static GameObject FixtureBlock(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collide)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            return go;
        }
        static void FixtureSign(Transform parent, string text, Vector3 position, Quaternion rotation, float width, float height) { }
    }
}
