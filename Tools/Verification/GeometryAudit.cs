using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DeadAir;
using UnityEngine;
using UnityEngine.UI;

// External audit only. This file is compiled into a DLL and installed in a copy
// of the requested player; no game source or original build is changed.
public sealed class GeometryAudit : MonoBehaviour
{
    readonly List<string> report = new List<string>();
    int failures;
    string output;
    DeadAirSession session;
    DeadAirPlayer player;
    DeadAirBootstrap bootstrap;
    WorldData data;
    Canvas captureCanvas;
    Camera captureUI;
    [Serializable] sealed class BuildMetadata { public string version = ""; public string buildResult = ""; }
    string capturePrefix;

    public static void Install()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Contains("-deadAirGeometryAudit")) return;
        new GameObject("External release geometry audit").AddComponent<GeometryAudit>();
    }

    IEnumerator Start()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-deadAirGeometryAudit");
        output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.dataPath, "GeometryAudit");
        Directory.CreateDirectory(output);
        Application.runInBackground = true;
        yield return new WaitForSecondsRealtime(1.5f);
        IEnumerator run = Run();
        while (true)
        {
            object current;
            try { if (!run.MoveNext()) break; current = run.Current; }
            catch (Exception error) { Check(false, "Unhandled audit error: " + error); break; }
            yield return current;
        }
        report.Add("COMPLETE: failures=" + failures);
        File.WriteAllLines(Path.Combine(output, "geometry-runtime-report.txt"), report);
        Debug.Log("EXTERNAL GEOMETRY AUDIT COMPLETE failures=" + failures + " output=" + output);
        Application.Quit(failures == 0 ? 0 : 8);
    }

    IEnumerator Run()
    {
        session = UnityEngine.Object.FindAnyObjectByType<DeadAirSession>();
        player = UnityEngine.Object.FindAnyObjectByType<DeadAirPlayer>();
        bootstrap = UnityEngine.Object.FindAnyObjectByType<DeadAirBootstrap>();
        data = WorldData.Load();
        Check(session != null && player != null && bootstrap != null, "Actual bootstrap/session/player exist.");
        if (session == null || player == null || bootstrap == null) yield break;
        report.Add("Auditing rebuilt player. Runtime internal version=" + Application.version + "; unity=" + Application.unityVersion + "; graphics=" + SystemInfo.graphicsDeviceName);
        string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "build-info.json"));
        using (var manifest = File.OpenRead(manifestPath))
        using (var sha = SHA256.Create())
            report.Add("BUILD-INFO-SHA256: " + BitConverter.ToString(sha.ComputeHash(manifest)).Replace("-", ""));
        var metadata = JsonUtility.FromJson<BuildMetadata>(File.ReadAllText(manifestPath));
        string expectedVersion = metadata != null ? metadata.version ?? "" : "";
        Check(metadata != null && metadata.buildResult == "Succeeded" && !String.IsNullOrEmpty(expectedVersion), "Copied player retains successful build metadata.");
        Check(Application.version == expectedVersion, "Native player version equals " + expectedVersion + ".");
        capturePrefix = "v" + expectedVersion.Replace(".", "");
        // Allow the separately supplied rules harness to share this native process.
        Type rulesAudit = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RuntimeRuleAudit")).FirstOrDefault(t => t != null);
        if (rulesAudit == null)
        {
            try { rulesAudit = Assembly.Load("RuntimeRuleAudit").GetType("RuntimeRuleAudit"); }
            catch (Exception error) { report.Add("Rule audit load: " + error.Message); }
        }
        if (rulesAudit != null)
        {
            try { rulesAudit.GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, null); report.Add("Native runtime rule helper invoked."); }
            catch (Exception error) { Check(false, "Native runtime rule helper: " + error); }
        }
        session.Leave();
        session.Host("External geometry audit", "engineer", true, true);
        session.Command(new NetPacket { type = "start" });
        for (int i = 0; i < 120 && (session.State == null || session.State.phase != "playing"); i++) yield return null;
        Check(session.State != null && session.State.phase == "playing", "Creative round starts in actual rebuilt player.");
        if (session.State == null || session.State.phase != "playing") yield break;
        captureCanvas = GameObject.Find("BUREAU-9 interface").GetComponent<Canvas>();
        foreach (var part in captureCanvas.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 5;
        captureUI = new GameObject("External audit UI camera").AddComponent<Camera>();
        captureUI.enabled = false; captureUI.orthographic = true; captureUI.cullingMask = 1 << 5;
        captureUI.clearFlags = CameraClearFlags.Depth; captureUI.nearClipPlane = .1f; captureUI.farClipPlane = 10;
        captureCanvas.renderMode = RenderMode.ScreenSpaceCamera; captureCanvas.worldCamera = captureUI; captureCanvas.planeDistance = 1;
        player.ViewCamera.enabled = false; player.ViewCamera.cullingMask &= ~(1 << 5);
        player.ViewCamera.clearFlags = CameraClearFlags.SolidColor;
        session.State.power = session.State.quest.power = true;
        session.State.breakerOn = session.State.quest.breakerOn = true;
        bootstrap.World.Apply(session.State);
        yield return null;
        ValidateRails();
        ValidateDoors();
        ValidateSigns();
        ValidateUndercroftAndBasementStorage();
        yield return ValidatePromptsAndSurfaces();
        yield return ValidateWindow();
        InspectToward(0, -37, 0, 0, 3.5f, -45);
        yield return Save(capturePrefix + "-stair-rails");
        InspectToward(0, -52.3f, 4.2f, -3.3f, 5.3f, -44);
        yield return Save(capturePrefix + "-mezzanine-rails");
        InspectToward(-4.1f, -44.5f, 0, 0, 2, -45);
        yield return Save(capturePrefix + "-brick-undercroft");
        InspectToward(-2.4f, -34.8f, -4.2f, -5.2f, -3.1f, -31.8f);
        yield return Save(capturePrefix + "-basement-wall-storage");
        InspectToward(-2, -35.5f, -4.2f, -6, -2.8f, -35.5f);
        yield return Save(capturePrefix + "-basement-west-gateway");
        yield return CaptureDoorAndPlaque();
        yield return ValidateEquippedCanisters();
        yield return ValidateHudRows();
        player.EndInspection(); session.Leave();
    }

    void ValidateRails()
    {
        Physics.SyncTransforms();
        var parts = SceneObjects<Transform>();
        Check(!parts.Any(t => t.name.StartsWith("Stair side wall ", StringComparison.Ordinal)),
            "Obsolete full-height stair-side walls remain absent; restored masonry is confined below upper treads.");
        var middleRails = new List<BoxCollider>();
        foreach (var stair in data.stairs)
        {
            string level = stair.lowerY < 0 ? "basement" : "upper";
            var flight = parts.FirstOrDefault(t => t.name == "Steel stair rails:" + level);
            var opening = parts.FirstOrDefault(t => t.name == "Steel opening rails:" + level);
            Check(flight != null && opening != null, "Actual " + level + " flight and floor opening have steel guardrail groups.");
            if (flight == null || opening == null) continue;
            ValidateSteelGuard(flight.Find("Sloping west"),
                DeadAirOfficeLayout.Position(stair.minX + .09f, stair.lowerY, stair.maxZ),
                DeadAirOfficeLayout.Position(stair.minX + .09f, stair.upperY, stair.minZ), middleRails);
            ValidateSteelGuard(flight.Find("Sloping east"),
                DeadAirOfficeLayout.Position(stair.maxX - .09f, stair.lowerY, stair.maxZ),
                DeadAirOfficeLayout.Position(stair.maxX - .09f, stair.upperY, stair.minZ), middleRails);
            ValidateSteelGuard(opening.Find("Opening west"),
                DeadAirOfficeLayout.Position(-1.89f, stair.upperY, stair.minZ),
                DeadAirOfficeLayout.Position(-1.89f, stair.upperY, stair.maxZ), middleRails);
            ValidateSteelGuard(opening.Find("Opening east"),
                DeadAirOfficeLayout.Position(1.89f, stair.upperY, stair.minZ),
                DeadAirOfficeLayout.Position(1.89f, stair.upperY, stair.maxZ), middleRails);
            if (stair.upperY > 1)
                ValidateSteelGuard(opening.Find("Opening front"),
                    DeadAirOfficeLayout.Position(-1.89f, stair.upperY, stair.maxZ),
                    DeadAirOfficeLayout.Position(1.89f, stair.upperY, stair.maxZ), middleRails);
            Vector3 entry = DeadAirOfficeLayout.Position(0, stair.upperY + .64f, stair.minZ);
            Check(!opening.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && Vector3.Distance(c.ClosestPoint(entry), entry) < .10f),
                "The " + level + " stair entry landing stays open.");
        }
        var controller = player.GetComponent<CharacterController>();
        Check(controller != null, "Original released player CharacterController is present.");
        Physics.SyncTransforms();
        foreach (var guard in middleRails)
        {
            Vector3 side = guard.transform.right.normalized;
            foreach (float sample in new[] { .2f, .5f, .8f })
            {
                Vector3 center = guard.transform.TransformPoint(guard.center + Vector3.forward * guard.size.z * (sample - .5f));
                RaycastHit hit;
                bool rayHit = guard.Raycast(new Ray(center + side, -side), out hit, 2);
                string context = guard.transform.parent.parent.name + "/" + guard.transform.parent.name + "@" + sample;
                Check(guard.enabled && !guard.isTrigger && rayHit, "Physical ray crosses steel middle rail " + context + " at " + center);
                if (controller == null) continue;
                Vector3 feet = center + side * .70f - Vector3.up * .64f;
                player.SetInspectionPose(-feet.x, feet.z, 0, 0, false, feet.y);
                Physics.SyncTransforms();
                CollisionFlags flags = controller.Move(-side * 1.40f);
                float finalSide = Vector3.Dot(player.transform.position - center, side);
                report.Add("Controller guard=" + context + "; center=" + center + "; stoppedSide=" + finalSide + "; flags=" + flags);
                Check(finalSide > .10f && (flags & CollisionFlags.Sides) != 0, "Original controller cannot cross " + context + " laterally.");
            }
        }
    }

    void ValidateUndercroftAndBasementStorage()
    {
        var parts = SceneObjects<Transform>();
        var upper = data.stairs.Single(s => s.lowerY == 0 && s.upperY > 1);
        var undercroft = parts.FirstOrDefault(t => t.name == "Upper stair undercroft");
        Check(undercroft != null, "The actual released upper stair has its restored brick undercroft.");
        if (undercroft == null) return;
        player.SetInspectionPose(data.spawn.x, data.spawn.z, 0, 0, false, data.spawn.y);
        Physics.SyncTransforms();
        var walls = undercroft.GetComponentsInChildren<BoxCollider>(true);
        float rise = (upper.upperY - upper.lowerY) / upper.steps;
        float depth = (upper.maxZ - upper.minZ) / upper.steps;
        int expectedCount = Enumerable.Range(1, upper.steps).Count(step => step * rise - .175f > .001f) * 2;
        Check(walls.Length == expectedCount, "Actual undercroft has every stepped brick side segment (expected=" + expectedCount + ", actual=" + walls.Length + ").");
        for (int step = 1; step <= upper.steps; step++)
        {
            float height = step * rise - .175f;
            float z = upper.maxZ - (step - .5f) * depth;
            foreach (float x in new[] { upper.minX + .05f, upper.maxX - .05f })
            {
                string name = "Undercroft brick " + (x < 0 ? "west " : "east ") + step;
                var matches = walls.Where(c => c.name == name).ToArray();
                if (height <= .001f) { Check(matches.Length == 0, "Lowest tread meets the ground without projecting masonry above it."); continue; }
                Check(matches.Length == 1, "Exactly one actual physical brick segment exists: " + name);
                if (matches.Length != 1) continue;
                var wall = matches[0];
                Check(wall.enabled && !wall.isTrigger && wall.transform.parent == undercroft &&
                    Vector3.Distance(wall.bounds.center, NativePoint(x, upper.lowerY + height / 2, z)) < .002f &&
                    Vector3.Distance(wall.size, new Vector3(.22f, height, depth)) < .002f,
                    name + " forms a physical stepped side from the first floor to the upper tread underside.");
                Check(Mathf.Abs(wall.bounds.min.y - upper.lowerY) < .002f && wall.bounds.max.y <= upper.lowerY + step * rise - .17f,
                    name + " leaves the upper flight and guard-rail space open.");
                foreach (float railHeight in new[] { .24f, .64f, 1.06f })
                {
                    var sample = NativePoint(x < 0 ? upper.minX + .09f : upper.maxX - .09f, upper.HeightAt(z) + railHeight, z);
                    Check(walls.All(c => Vector3.Distance(c.ClosestPoint(sample), sample) > .15f),
                        "Restored brick stays below upper steel-guard sample " + sample);
                }
            }
        }
        foreach (var stair in data.stairs)
        {
            float stairRise = (stair.upperY - stair.lowerY) / stair.steps;
            float stairDepth = (stair.maxZ - stair.minZ) / stair.steps;
            for (int step = 1; step <= stair.steps; step++)
            {
                var feet = NativePoint(0, stair.lowerY + step * stairRise, stair.maxZ - (step - .5f) * stairDepth);
                NativeClear(feet, (stair.lowerY < 0 ? "Basement" : "Upper") + " stair tread " + step);
                NativeSupport(feet, "Stair tread " + step);
            }
        }
        NativeClear(NativePoint(0, 0, -50.5f), "Ground-floor basement entrance landing");
        NativeSupport(NativePoint(0, 0, -50.5f), "Ground-floor basement entrance landing");
        var shelves = data.props.Where(p => p.type == "shelf" && p.y < -1).ToArray();
        Check(shelves.Length == 2, "Both basement shelves are retained.");
        NativeShelf(shelves.FirstOrDefault(p => p.label == "Стелаж з інструментами"), -5.5f, -31.5f, new Vector3(-1, 0, 0));
        NativeShelf(shelves.FirstOrDefault(p => p.label == "Стелаж із запчастинами"), -3.7f, -39, Vector3.forward);
        var doorLeaves = parts.Where(t => t.name.StartsWith("door:", StringComparison.Ordinal)).Select(t => t.GetComponent<DeadAirHingedDoor>()).Where(h => h != null).ToArray();
        var previousAngles = doorLeaves.Select(h => h.Angle).ToArray();
        try
        {
            foreach (var hinge in doorLeaves) { hinge.SetOpen(true); hinge.Advance(.5f); }
            Physics.SyncTransforms();
            float floor = -DeadAirOfficeLayout.FloorHeight;
            var centre = NativePoint(0, floor, -35.5f);
            NativePassage(centre, NativePoint(-8.5f, floor, -35.5f), "West-cell gateway where the old shelves faced each other");
            NativePassage(centre, NativePoint(8.5f, floor, -35.5f), "East-cell gateway");
            NativePassage(NativePoint(0, floor, -39.1f), NativePoint(0, floor, -28.4f), "Main basement aisle from stair exit to breaker");
            NativePassage(NativePoint(0, floor, -31.5f), NativePoint(-4.5f, floor, -31.5f), "Tool-shelf front access");
            NativePassage(NativePoint(0, floor, -38.1f), NativePoint(-3.7f, floor, -38.1f), "Spare-parts shelf front access");
            foreach (var door in data.doors.Where(d => d.id.StartsWith("door-prison-", StringComparison.Ordinal)))
                NativePassage(NativePoint(Mathf.Sign(door.x) * 8.5f, floor, door.z), NativePoint(Mathf.Sign(door.x) * 12.25f, floor, door.z), door.id + " physical cell doorway");
            NativeClear(NativePoint(.5f, floor, -32), "Basement diesel canister approach"); NativeSupport(NativePoint(.5f, floor, -32), "Basement diesel canister approach");
            NativeClear(NativePoint(-13.3f, floor, -29.45f), "Prison diary approach"); NativeSupport(NativePoint(-13.3f, floor, -29.45f), "Prison diary approach");
            foreach (var mushroom in data.Mushrooms.Where(m => m.y < -1))
            {
                NativeClear(NativePoint(mushroom.x, mushroom.y, mushroom.z), mushroom.id + " actual patch");
                NativeSupport(NativePoint(mushroom.x, mushroom.y, mushroom.z), mushroom.id + " actual patch");
            }
        }
        finally
        {
            for (int i = 0; i < doorLeaves.Length; i++) { doorLeaves[i].SetOpen(Mathf.Abs(previousAngles[i]) > 1); doorLeaves[i].Advance(.5f); }
            Physics.SyncTransforms();
        }

        void NativeShelf(PropData spec, float x, float z, Vector3 forward)
        {
            var root = spec == null ? null : parts.FirstOrDefault(t => t.name == "prop:" + spec.id);
            Check(root != null, "Actual wall-aligned basement shelf is present at source " + x + "," + z);
            if (root == null) return;
            Check(Vector3.Distance(root.position, NativePoint(x, -4.2f, z)) < .002f && Vector3.Dot(root.forward, forward) > .999f,
                spec.id + " stands against its wall with its front facing the open electrical room.");
            Check(root.GetComponentsInChildren<Collider>(true).Count(c => c.enabled && !c.isTrigger) >= 7,
                spec.id + " retains both physical sides and all five shelf boards.");
        }
    }

    static Vector3 NativePoint(float x, float y, float z) => DeadAirOfficeLayout.Position(x, y, z);
    bool IsAuditActor(Collider collider) => collider is CharacterController || collider.GetComponentInParent<DeadAirPlayer>() != null || collider.GetComponentInParent<DeadAirCharacterMotion>() != null;
    void NativeClear(Vector3 feet, string label)
    {
        var hits = Physics.OverlapCapsule(feet + Vector3.up * .26f, feet + Vector3.up * 1.48f, .22f, ~0, QueryTriggerInteraction.Ignore).Where(c => !IsAuditActor(c)).ToArray();
        Check(hits.Length == 0, "Actual player-sized capsule fits " + label + " (blocking=" + string.Join(", ", hits.Select(c => c.name)) + ").");
    }
    void NativeSupport(Vector3 feet, string label)
    {
        var hits = Physics.RaycastAll(feet + Vector3.up * .15f, Vector3.down, .30f, ~0, QueryTriggerInteraction.Ignore).Where(h => !IsAuditActor(h.collider)).OrderBy(h => h.distance).ToArray();
        Check(hits.Length > 0 && Mathf.Abs(hits[0].point.y - feet.y) < .02f, "Actual physical floor supports " + label);
    }
    void NativePassage(Vector3 from, Vector3 to, string label)
    {
        var delta = to - from;
        var hits = Physics.CapsuleCastAll(from + Vector3.up * .26f, from + Vector3.up * 1.48f, .22f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Where(h => !IsAuditActor(h.collider)).ToArray();
        Check(hits.Length == 0, "Actual complete capsule passage stays clear: " + label + " (blocking=" + string.Join(", ", hits.Select(h => h.collider.name)) + ").");
        for (int i = 0; i <= 12; i++) { var feet = Vector3.Lerp(from, to, i / 12f); NativeClear(feet, label + "@" + i); NativeSupport(feet, label + "@" + i); }
    }

    void ValidateSteelGuard(Transform root, Vector3 floorA, Vector3 floorB, List<BoxCollider> middleRails)
    {
        Check(root != null, "Complete physical guard exists between " + floorA + " and " + floorB + ".");
        if (root == null) return;
        var colliders = root.GetComponentsInChildren<BoxCollider>(true);
        var rails = colliders.Where(c => c.name.StartsWith("Steel rail ", StringComparison.Ordinal)).ToArray();
        var posts = colliders.Where(c => c.name.StartsWith("Steel guard post ", StringComparison.Ordinal)).ToArray();
        string context = root.parent.name + "/" + root.name;
        Check(rails.Length == 3 && posts.Length >= 2 && colliders.All(c => c.enabled && !c.isTrigger),
            context + " has three continuous solid steel rails and supporting solid posts.");
        bool covered = true;
        foreach (float height in new[] { .24f, .64f, 1.06f })
            for (int sample = 0; sample <= 24; sample++)
            {
                Vector3 point = Vector3.Lerp(floorA, floorB, sample / 24f) + Vector3.up * height;
                if (rails.Any(c => c.enabled && Vector3.Distance(c.ClosestPoint(point), point) < .035f)) continue;
                covered = false; report.Add("Missing steel rail sample " + context + ": " + point);
            }
        Check(covered, context + " covers its whole authored edge at lower, middle and handrail height.");
        var positions = posts.Select(p => p.transform.position - Vector3.up * .54f)
            .OrderBy(p => Vector3.Dot(p - floorA, floorB - floorA)).ToArray();
        bool supported = positions.Length >= 2 && Vector3.Distance(positions[0], floorA) < .01f && Vector3.Distance(positions[positions.Length - 1], floorB) < .01f;
        for (int i = 1; i < positions.Length; i++)
            supported &= Vector2.Distance(new Vector2(positions[i - 1].x, positions[i - 1].z), new Vector2(positions[i].x, positions[i].z)) <= .901f;
        Check(supported, context + " has posts at both ends and no unsupported span over 90 cm.");
        var middle = rails.FirstOrDefault(c => Mathf.Abs(c.transform.position.y - (floorA.y + floorB.y) * .5f - .64f) < .01f);
        if (middle != null) middleRails.Add(middle);
        Check(middle != null, context + " has an actual middle rail for player collision sampling.");
    }

    void ValidateDoors()
    {
        var roots = SceneObjects<Transform>().Where(t => t.name.StartsWith("door:") && !t.name.StartsWith("door:door-prison-")).ToArray();
        int expectedDoors = data.doors.Count(d => !d.id.StartsWith("door-prison-", StringComparison.Ordinal));
        Check(roots.Length == expectedDoors, "All " + expectedDoors + " ordinary doors are present.");
        foreach (var root in roots)
        {
            Transform leaf = root.Find("Door leaf");
            var renderer = leaf == null ? null : leaf.GetComponent<Renderer>();
            var collider = leaf == null ? null : leaf.GetComponent<BoxCollider>();
            var material = renderer == null ? null : renderer.sharedMaterial;
            var texture = material == null ? null : material.mainTexture;
            Check(collider != null && collider.enabled && !collider.isTrigger, root.name + " retains physical leaf collision.");
            Check(material != null && material.name.Contains("door-wood") && texture != null && texture.width >= 256,
                root.name + " uses new wood material and texture (actual=" + (material == null ? "null" : material.name) + ").");
            Check(root.Find("Door upper panel") == null && root.Find("Door lower panel") == null && leaf != null,
                root.name + " has a solid oak leaf without obsolete inset panels.");
            var casing = root.parent;
            Check(casing != null && casing.Cast<Transform>().Count(t => t.name == "Oak casing side") == 4 && casing.Cast<Transform>().Count(t => t.name == "Oak casing head") == 2,
                root.name + " has complete dark oak casing on both wall faces.");
            Check(material != null && material.shader.name == "Dead Air/Source Surface" && material.color.a > .99f && material.renderQueue == 2000,
                root.name + " retains an opaque oak surface.");
            var hinge = root.GetComponent<DeadAirHingedDoor>();
            if (hinge == null) continue;
            var spec = data.Door(root.name.Substring(5));
            hinge.SetOpen(true); hinge.Advance(.5f);
            Check(spec != null && Mathf.Sign(hinge.Angle) == Mathf.Sign(spec.openAngle), root.name + " preserves authored hinge opening direction.");
            hinge.SetOpen(false); hinge.Advance(.5f);
            hinge.SetOpen(true); hinge.Advance(.5f);
            Check(spec != null && Mathf.Sign(hinge.Angle) == Mathf.Sign(spec.openAngle), root.name + " repeats the same opening direction after closing.");
            hinge.SetOpen(false); hinge.Advance(.5f);
        }
        foreach (var locker in SceneObjects<DeadAirHingedDoor>().Where(h => h.name.StartsWith("locker-door:")))
        {
            locker.PlayAccessCycle(); locker.Advance(.4f); float first = locker.Angle;
            locker.Advance(1f); locker.PlayAccessCycle(); locker.Advance(.4f);
            Check(first < 0 && Mathf.Abs(first - locker.Angle) < .1f, locker.name + " repeats authored negative access swing.");
            locker.Advance(1f);
        }
    }

    void ValidateSigns()
    {
        var signs = SceneObjects<TextMesh>().Where(t => t.name == "Sign lettering").ToArray();
        Check(signs.Length >= 20, "Actual room and wayfinding lettering is present.");
        foreach (var text in signs)
        {
            var frame = text.transform.parent.Find("Sign frame");
            var backing = text.transform.parent.Find("Sign backing");
            var renderer = text.GetComponent<Renderer>();
            Check(text.fontSize >= 128 && frame != null && backing != null && renderer != null && renderer.sharedMaterial != null,
                "Plaque has high resolution text, metal frame and physical backing: " + text.text.Replace("\n", " / "));
            if (backing == null || renderer == null) continue;
            var plate = backing.GetComponent<MeshFilter>();
            Check(plate != null && plate.sharedMesh != null, "Plaque retains its physical mesh for independent text containment: " + text.text.Replace("\n", " / "));
            if (plate == null || plate.sharedMesh == null) continue;
            Bounds board = plate.sharedMesh.bounds, ink = renderer.localBounds;
            bool contained = ink.size.x > .00001f && ink.size.y > .00001f;
            foreach (float x in new[] { ink.min.x, ink.max.x }) foreach (float y in new[] { ink.min.y, ink.max.y })
            {
                Vector3 point = plate.transform.InverseTransformPoint(text.transform.TransformPoint(new Vector3(x, y, ink.center.z)));
                contained &= point.x >= board.min.x + .015f && point.x <= board.max.x - .015f && point.y >= board.min.y + .010f && point.y <= board.max.y - .010f;
            }
            Check(contained, "Actual visible glyphs fit inside the physical plaque with clear margins: " + text.text.Replace("\n", " / "));
            report.Add("Plaque=" + text.text.Replace("\n", " / ") + "; glyphBounds=" + renderer.bounds + "; physicalPlate=" + board);
        }
    }

    IEnumerator ValidatePromptsAndSurfaces()
    {
        var cans = data.objects.Where(o => o.type == "canister").ToArray();
        var generator = data.props.FirstOrDefault(p => p.type == "dieselGenerator");
        Check(cans.Length == 3 && cans.Count(c => c.y < -1) == 1 && cans.Count(c => c.y >= -1) == 2,
            "Exactly two actual canister pickups stand in the yard and one stands in the basement.");
        Check(generator != null && cans.Where(c => c.y >= -1).All(c => Vector2.Distance(new Vector2(c.x, c.z), new Vector2(generator.x, generator.z)) < 3.5f),
            "Both yard canisters stand within 3.5 m of the diesel generator.");
        foreach (string id in new[] { "key-basement", "fuel-canister", "fuel-canister-2", "fuel-canister-3" })
        {
            var obj = data.Objective(id);
            var actual = SceneObjects<Transform>().FirstOrDefault(t => t.name == "interactive:" + id);
            Check(obj != null && actual != null, "Physical objective exists: " + id);
            if (obj == null || actual == null) continue;
            Check(Vector3.Distance(actual.position, DeadAirOfficeLayout.Position(obj.x, obj.y, obj.z)) < .001f,
                "Rendered objective matches its actual pickup location: " + id);
            float floor = obj.y < -2 ? -4.2f : obj.y > 4 ? 4.2f : 0;
            var visual = actual.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(); bool initialized = false;
            foreach (var renderer in visual) { if (!initialized) { bounds = renderer.bounds; initialized = true; } else bounds.Encapsulate(renderer.bounds); }
            Check(visual.Length > 0, "Objective has rendered geometry: " + id);
            report.Add(id + " source=" + obj.x + "," + obj.y + "," + obj.z + "; actual=" + actual.position + "; bounds=" + bounds);
            if (id == "key-basement")
            {
                var card = actual.Find("Access card");
                Check(card != null, "Basement access item renders as a card.");
                RaycastHit hit;
                bool supported = Physics.Raycast(actual.position + Vector3.up * .15f, Vector3.down, out hit, .4f, ~0, QueryTriggerInteraction.Ignore);
                Check(supported && hit.collider.name != "Ground floor", "Access card lies directly above furniture surface (support=" + (supported ? hit.collider.name : "none") + ").");
            }
            else if (initialized)
            {
                Check(Mathf.Abs(bounds.min.y - floor) < .15f, "Canister visual stands on the actual floor: " + id);
                Check(bounds.size.x > .12f && bounds.size.y > .25f && bounds.size.z > .08f,
                    "Canister has a volumetric body rather than a flat label: " + id + " size=" + bounds.size);
                Check(visual.Length >= 5 && actual.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 8),
                    "Canister includes body and handle meshes: " + id + " renderers=" + visual.Length);
            }
            bool prompt = false;
            foreach (Vector2 offset in new[] { new Vector2(0, -1.25f), new Vector2(0, 1.25f), new Vector2(-1.25f, 0), new Vector2(1.25f, 0) })
            {
                InspectToward(obj.x + offset.x, obj.z + offset.y, floor, obj.x, obj.y, obj.z);
                for (int i = 0; i < 5; i++) yield return null;
                if (player.InteractionId == id) { prompt = true; break; }
            }
            Check(prompt, "Actual E reticle selects " + id + " (actual=" + player.InteractionId + ").");
            yield return Save(capturePrefix + "-" + id);
        }
    }

    IEnumerator ValidateWindow()
    {
        var pane = SceneObjects<Transform>().FirstOrDefault(t => t.name == "state:directorWindowPane");
        var broken = SceneObjects<Transform>().FirstOrDefault(t => t.name == "state:directorWindowBroken");
        Check(pane != null && broken != null, "Both intact pane and new broken window frame exist in actual build.");
        if (pane == null || broken == null) yield break;
        session.State.windowBroken = session.State.quest.windowBroken = false;
        bootstrap.World.Apply(session.State);
        Check(pane.gameObject.activeSelf && !broken.gameObject.activeSelf, "Intact window hides all broken remnants.");
        session.State.windowBroken = session.State.quest.windowBroken = true;
        bootstrap.World.Apply(session.State);
        Check(!pane.gameObject.activeSelf && broken.gameObject.activeSelf, "Broken state swaps intact glass for new permanent remnants.");
        var pieces = broken.GetComponentsInChildren<MeshRenderer>(true);
        string[] expectedRemnants = {
            "Broken glass bottom 1", "Broken glass bottom 2", "Broken glass bottom 3", "Broken glass bottom 4",
            "Broken glass top 1", "Broken glass top 2", "Broken glass top 3",
            "Broken glass left", "Broken glass right", "Broken mullion top", "Broken mullion bottom"
        };
        Check(pieces.Length == expectedRemnants.Length && expectedRemnants.All(name => pieces.Count(p => p.name == name) == 1),
            "Broken window has all four bottom, three top and two side glass fragments plus both mullion remnants (pieces=" + pieces.Length + ").");
        Check(pieces.Where(p => p.name.StartsWith("Broken glass ", StringComparison.Ordinal)).All(p => p.sharedMaterial != null && p.sharedMaterial.shader.name == "Dead Air/Window Glass"),
            "Every permanent glass remnant uses the dedicated transparent shader.");
        Check(broken.GetComponentsInChildren<Collider>(true).Length == 0, "Broken remnants cannot obstruct the ladder path.");
        // The shattered-glass effect lasts about two seconds. Wait for the
        // transient shards before inspecting the permanent frame composition.
        yield return new WaitForSecondsRealtime(2.2f);
        InspectToward(-9, 4.2f, 4.2f, -9, 6.1f, 6);
        yield return Save(capturePrefix + "-broken-window-inside");
        InspectToward(-9, 9, 0, -9, 6.1f, 6);
        yield return Save(capturePrefix + "-broken-window-outside");
    }

    IEnumerator CaptureDoorAndPlaque()
    {
        var doors = SceneObjects<Transform>().Where(t => t.name.StartsWith("door:") && !t.name.StartsWith("door:door-prison-", StringComparison.Ordinal) && t.Find("Door leaf") != null).ToArray();
        var signs = SceneObjects<TextMesh>().Where(t => t.name == "Sign lettering").ToArray();
        Transform chosen = null; TextMesh plaque = null; DoorData spec = null;
        foreach (var door in doors)
        {
            var candidate = data.Door(door.name.Substring(5));
            if (candidate == null || Mathf.Abs(candidate.y - 1.4f) > .2f || Mathf.Abs(candidate.x) < .5f || Mathf.Abs(Mathf.Cos(candidate.rotation)) < .9f) continue;
            Vector3 corridorView = new Vector3(-candidate.x + Mathf.Sign(candidate.x) * 3.2f, 1.65f, candidate.z);
            var nearest = signs.Where(t => (t.transform.position - door.position).sqrMagnitude <= 16)
                .OrderBy(t => (t.transform.position - corridorView).sqrMagnitude).FirstOrDefault();
            if (nearest == null) continue;
            chosen = door; plaque = nearest; spec = candidate; break;
        }
        Check(chosen != null && plaque != null, "Focused door capture has an actual ordinary door and adjacent plaque.");
        if (chosen == null || plaque == null) yield break;
        var leaf = chosen.Find("Door leaf").GetComponent<Renderer>();
        var textRenderer = plaque.GetComponent<Renderer>();
        Vector3 focus = (leaf.bounds.center + textRenderer.bounds.center) * .5f;
        float sourceX = spec.x - Mathf.Sign(spec.x) * 3.2f;
        InspectToward(sourceX, focus.z, 0, -focus.x, focus.y, focus.z);
        report.Add("Focused door capture: " + chosen.name + "; plaque=" + plaque.text.Replace("\n", " / ") + "; focus=" + focus);
        yield return Save(capturePrefix + "-door-and-plaques", false);
    }

    IEnumerator ValidateEquippedCanisters()
    {
        var firstPerson = player.GetComponent<DeadAirFirstPersonRig>();
        var itemField = typeof(DeadAirFirstPersonRig).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);
        var modeField = typeof(DeadAirFirstPersonRig).GetField("currentMode", BindingFlags.Instance | BindingFlags.NonPublic);
        var items = firstPerson == null || itemField == null ? null : itemField.GetValue(firstPerson) as Dictionary<string, Transform>;
        Check(firstPerson != null && items != null && items.ContainsKey("fuel-canister"), "Actual first person canister prop exists.");
        if (firstPerson == null || items == null || !items.ContainsKey("fuel-canister")) yield break;
        // A rendered remote snapshot exercises the real SyncRemote/Present path.
        // It is deliberately local test data, not a claim about TCP transport.
        var remote = new PlayerState { id = "geometry-audit-peer", name = "Fuel presentation peer", characterClass = "engineer", x = 0, y = 0, z = 21.2f, yaw = Mathf.PI };
        session.State.players.Add(remote);
        var portraitLight = new GameObject("External audit remote portrait light").AddComponent<Light>();
        portraitLight.type = LightType.Spot;
        portraitLight.transform.position = new Vector3(-remote.x + 1.1f, 2.3f, remote.z + 2.1f);
        portraitLight.transform.rotation = Quaternion.LookRotation(new Vector3(-remote.x, 1.1f, remote.z) - portraitLight.transform.position);
        portraitLight.range = 8; portraitLight.spotAngle = 85; portraitLight.intensity = 3;
        portraitLight.color = new Color(1, .96f, .88f); portraitLight.shadows = LightShadows.None;
        portraitLight.enabled = false;
        report.Add("Remote equipment assertions use an injected player snapshot through actual SyncRemote/DeadAirCharacterMotion.Present; no TCP guest is connected.");
        report.Add("Remote canister captures use a temporary audit-only spotlight and a 40-degree camera placed beside the actual held body bounds, plus a second three-quarter view. The canister and character pose are unchanged.");
        foreach (string id in new[] { "fuel-canister", "fuel-canister-2", "fuel-canister-3" })
        {
            var obj = data.Objective(id);
            if (obj == null) { Check(false, "Missing canister for equip test: " + id); continue; }
            float floor = obj.y < -2 ? -4.2f : obj.y > 4 ? 4.2f : 0;
            bool reached = false;
            foreach (Vector2 offset in new[] { new Vector2(0, -1.25f), new Vector2(0, 1.25f), new Vector2(-1.25f, 0), new Vector2(1.25f, 0) })
            {
                InspectToward(obj.x + offset.x, obj.z + offset.y, floor, obj.x, obj.y, obj.z);
                for (int wait = 0; wait < 5; wait++) yield return null;
                if (player.InteractionId == id) { reached = true; break; }
            }
            Check(reached, "Normal reticle can collect " + id + " for equipment validation.");
            if (reached) session.Command(new NetPacket { type = "interact", id = id });
            var me = session.State.Player(session.LocalId);
            Check(me.HasItem(id), "Actual session inventory contains collected " + id + ".");
            if (!me.HasItem(id)) continue;
            session.Command(new NetPacket { type = "slot", phrase = me.inventory.IndexOf(id) });
            remote.inventory.Clear(); remote.inventory.Add(id); remote.selectedSlot = 0;
            remote.camera = remote.mapOpen = remote.uniqueItemEquipped = false;
            InspectToward(0, 23.8f, 0, remote.x, 1.1f, remote.z);
            yield return new WaitForSecondsRealtime(.35f);
            firstPerson.CompleteEquipTransition();
            Transform held = items["fuel-canister"];
            string actualMode = modeField == null ? "unknown" : (string)modeField.GetValue(firstPerson);
            Check(me.SelectedItem == id && actualMode == "fuel-canister" && held.gameObject.activeInHierarchy,
                "Selected " + id + " activates actual first person canister model (mode=" + actualMode + ").");
            var body = held.Find("Steel fuel can");
            var bodyRenderer = body == null ? null : body.GetComponent<Renderer>();
            Check(bodyRenderer != null && bodyRenderer.enabled && body.gameObject.activeInHierarchy && bodyRenderer.bounds.size.x > .1f && bodyRenderer.bounds.size.y > .15f && bodyRenderer.bounds.size.z > .07f,
                "Actual first person " + id + " has a visible volumetric steel body.");
            yield return Save(capturePrefix + "-equipped-first-person-" + id);

            var motion = SceneObjects<DeadAirCharacterMotion>().FirstOrDefault(m => m.name == remote.name + " / " + remote.characterClass);
            Renderer remoteBody = null;
            Check(motion != null, "Actual remote character presentation exists for " + id + ".");
            if (motion != null)
            {
                var propField = typeof(DeadAirCharacterMotion).GetField("props", BindingFlags.Instance | BindingFlags.NonPublic);
                var props = propField == null ? null : propField.GetValue(motion) as Dictionary<string, Transform>;
                Transform fuel;
                bool active = props != null && props.TryGetValue("fuel-canister", out fuel) && fuel.gameObject.activeInHierarchy;
                Check(active, "Actual remote " + id + " activates Held fuel-canister.");
                if (props != null && props.ContainsKey("fuel-canister"))
                {
                    var rendered = props["fuel-canister"].GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    Check(rendered.Any(r => r.bounds.size.x > .08f && r.bounds.size.y > .15f && r.bounds.size.z > .05f), "Actual remote " + id + " has a visible volumetric canister body.");
                    remoteBody = rendered.FirstOrDefault(r => r.name == "fuel-canister");
                    if (remoteBody != null)
                    {
                        report.Add("REMOTE CAN DIAGNOSIS " + id + ": worldBodyBounds=" + remoteBody.bounds + "; characterRoot=" + motion.transform.position + "; characterLocalCanCenter=" + motion.transform.InverseTransformPoint(remoteBody.bounds.center)
                            + "; rendererEnabled=" + remoteBody.enabled + "; layer=" + remoteBody.gameObject.layer + "; viewCameraMask=" + player.ViewCamera.cullingMask + "; material=" + remoteBody.sharedMaterial.name);
                        Check((player.ViewCamera.cullingMask & (1 << remoteBody.gameObject.layer)) != 0, "Remote " + id + " body layer is rendered by the actual world camera.");
                    }
                    Check(!props.ContainsKey("tape") || !props["tape"].gameObject.activeInHierarchy, "Remote " + id + " does not display a cassette prop.");
                }
            }
            portraitLight.enabled = true;
            float previousFov = player.ViewCamera.fieldOfView;
            player.ViewCamera.fieldOfView = 40;
            if (remoteBody != null)
            {
                Vector3 bodyCenter = remoteBody.bounds.center;
                Vector3 outside = bodyCenter - motion.transform.position;
                outside.y = 0;
                if (outside.sqrMagnitude < .01f) outside = motion.transform.right;
                outside.Normalize();
                Vector3 source = bodyCenter + outside * 1.65f;
                InspectToward(-source.x, source.z, 0, -bodyCenter.x, bodyCenter.y + .06f, bodyCenter.z);
                player.ViewCamera.fieldOfView = 40;
                portraitLight.transform.position = player.ViewCamera.transform.position + player.ViewCamera.transform.right * .45f + Vector3.up * .25f;
                portraitLight.transform.rotation = Quaternion.LookRotation(bodyCenter - portraitLight.transform.position);
                Vector3 viewport = player.ViewCamera.WorldToViewportPoint(bodyCenter);
                report.Add("REMOTE SIDE CAPTURE " + id + ": camera=" + player.ViewCamera.transform.position + "; canViewport=" + viewport + "; outsideDirection=" + outside);
                Check(viewport.z > 0 && viewport.x > .15f && viewport.x < .85f && viewport.y > .15f && viewport.y < .85f, "Remote " + id + " canister is centered in the hand-side camera view.");
            }
            if (motion != null && remoteBody != null)
            {
                var fuelRoot = motion.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Held fuel-canister");
                ValidateVisibleCanPixels(motion, fuelRoot, remoteBody, "engineer " + id);
            }
            yield return Save(capturePrefix + "-equipped-remote-" + id, false);
            if (remoteBody != null)
            {
                Vector3 bodyCenter = remoteBody.bounds.center;
                Vector3 outside = bodyCenter - motion.transform.position; outside.y = 0;
                if (outside.sqrMagnitude < .01f) outside = motion.transform.right;
                outside = (outside.normalized + motion.transform.forward * .7f).normalized;
                Vector3 source = bodyCenter + outside * 1.65f;
                InspectToward(-source.x, source.z, 0, -bodyCenter.x, bodyCenter.y + .06f, bodyCenter.z);
                player.ViewCamera.fieldOfView = 40;
                portraitLight.transform.position = player.ViewCamera.transform.position + player.ViewCamera.transform.right * .45f + Vector3.up * .25f;
                portraitLight.transform.rotation = Quaternion.LookRotation(bodyCenter - portraitLight.transform.position);
                yield return Save(capturePrefix + "-equipped-remote-three-quarter-" + id, false);
            }
            player.ViewCamera.fieldOfView = previousFov;
            portraitLight.enabled = false;
        }
        yield return ValidateAllRoleFuelPoses(remote, portraitLight);
        Destroy(portraitLight.gameObject);
        session.State.players.Remove(remote);
        yield return null;
    }

    IEnumerator ValidateAllRoleFuelPoses(PlayerState remote, Light portraitLight)
    {
        report.Add("Fuel pose coverage: real SyncRemote prefabs for all four roles; production SetEquipmentPreview samples Idle/Walk/Run at 0, .33 and .67. No hand/prop transforms or character meshes are modified by the helper.");
        foreach (string role in new[] { "engineer", "operator", "journalist", "dispatcher" })
        {
            remote.characterClass = role;
            remote.inventory.Clear(); remote.inventory.Add("fuel-canister"); remote.selectedSlot = 0;
            yield return new WaitForSecondsRealtime(.25f);
            var motion = SceneObjects<DeadAirCharacterMotion>().FirstOrDefault(m => m.name == remote.name + " / " + role);
            Check(motion != null && motion.ClassId == role, "Actual SyncRemote fuel pose model initialized for " + role + ".");
            if (motion == null) continue;
            var fuel = motion.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Held fuel-canister");
            var body = fuel == null ? null : fuel.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "fuel-canister");
            var handle = fuel == null ? null : fuel.Find("Carry handle");
            var fingers = motion.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Right finger-pad equipment contact");
            Check(fuel != null && body != null && handle != null && fingers != null, "Actual canister body, handle and finger contact exist for " + role + ".");
            if (fuel == null || body == null || handle == null || fingers == null) continue;
            var animation = motion.GetComponentInChildren<Animation>(true);
            foreach (string id in new[] { "fuel-canister", "fuel-canister-2", "fuel-canister-3" })
            {
                remote.inventory.Clear(); remote.inventory.Add(id); remote.selectedSlot = 0;
                foreach (string clip in new[] { "Idle", "Walk", "Run" })
                {
                    Check(animation != null && animation[clip] != null, role + " has authored " + clip + " animation for fuel pose sampling.");
                    foreach (float phase in new[] { 0f, .33f, .67f })
                    {
                        motion.SetEquipmentPreview(remote, clip, phase);
                        string context = role + " " + id + " " + clip + "@" + phase;
                        Check(fuel.gameObject.activeInHierarchy && motion.GripContactError < .015f,
                            "Fuel pose solves finger contact: " + context + " error=" + motion.GripContactError);
                        Check(Vector3.Distance(handle.position, fingers.position) < .025f,
                            "Actual canister handle stays at finger grip: " + context + " distance=" + Vector3.Distance(handle.position, fingers.position));
                    }
                }
                motion.SetEquipmentPreview(remote, "Idle", .33f);
                Vector3 center = body.bounds.center;
                Vector3 outside = center - motion.transform.position; outside.y = 0;
                if (outside.sqrMagnitude < .01f) outside = motion.transform.right;
                outside.Normalize();
                Vector3 source = center + outside * 1.65f;
                InspectToward(-source.x, source.z, 0, -center.x, center.y + .06f, center.z);
                player.ViewCamera.fieldOfView = 40;
                portraitLight.enabled = true;
                portraitLight.transform.position = player.ViewCamera.transform.position + player.ViewCamera.transform.right * .45f + Vector3.up * .25f;
                portraitLight.transform.rotation = Quaternion.LookRotation(center - portraitLight.transform.position);
                report.Add("ROLE CAN BODY " + role + " " + id + ": localCenter=" + motion.transform.InverseTransformPoint(center) + "; worldBounds=" + body.bounds);
                ValidateVisibleCanPixels(motion, fuel, body, role + " " + id);
                portraitLight.enabled = false;
            }
        }
    }

    void ValidateVisibleCanPixels(DeadAirCharacterMotion motion, Transform fuel, Renderer body, string context)
    {
        if (fuel == null || body == null) { Check(false, "Missing canister renderer for pixel visibility: " + context); return; }
        Bounds bounds = body.bounds;
        Vector3 min = bounds.min, max = bounds.max;
        float lowX = 1, highX = 0, lowY = 1, highY = 0;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
        {
            Vector3 point = player.ViewCamera.WorldToViewportPoint(new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z));
            lowX = Mathf.Min(lowX, point.x); highX = Mathf.Max(highX, point.x);
            lowY = Mathf.Min(lowY, point.y); highY = Mathf.Max(highY, point.y);
        }
        var roi = new RectInt(Mathf.Clamp(Mathf.FloorToInt(lowX * 1280) - 12, 0, 1279), Mathf.Clamp(Mathf.FloorToInt(lowY * 720) - 12, 0, 719), 0, 0);
        roi.width = Mathf.Max(1, Mathf.Clamp(Mathf.CeilToInt(highX * 1280) + 12, 1, 1280) - roi.x);
        roi.height = Mathf.Max(1, Mathf.Clamp(Mathf.CeilToInt(highY * 720) + 12, 1, 720) - roi.y);
        Texture2D on = null, off = null;
        bool wasActive = fuel.gameObject.activeSelf;
        try
        {
            // Both renders occur in this exact frame with the same character,
            // lights, camera and time. Only the canister toggles briefly.
            // An occluded canister cannot contribute body-region pixels.
            on = CaptureWorldPixels();
            fuel.gameObject.SetActive(false);
            off = CaptureWorldPixels();
            var a = on.GetPixels32(); var b = off.GetPixels32();
            int different = 0;
            for (int y = roi.y; y < roi.yMax; y++) for (int x = roi.x; x < roi.xMax; x++)
            {
                int pixel = y * 1280 + x;
                int delta = Math.Abs(a[pixel].r - b[pixel].r) + Math.Abs(a[pixel].g - b[pixel].g) + Math.Abs(a[pixel].b - b[pixel].b);
                if (delta >= 24) different++;
            }
            report.Add("VISIBLE CAN PIXELS " + context + ": count=" + different + "; roi=" + roi + "; localCanCenter=" + motion.transform.InverseTransformPoint(body.bounds.center));
            Check(different >= 20, "Remote canister body contributes visible pixels: " + context + " pixels=" + different);
            File.WriteAllBytes(Path.Combine(output, "visibility-" + context.Replace(' ', '-') + ".png"), on.EncodeToPNG());
        }
        finally
        {
            fuel.gameObject.SetActive(wasActive);
            if (on != null) Destroy(on);
            if (off != null) Destroy(off);
        }
    }

    Texture2D CaptureWorldPixels()
    {
        var frame = SceneObjects<DeadAirFrameRenderer>().First();
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        var previousTarget = frame.OutputCamera.targetTexture;
        var previousUI = captureUI.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            Canvas.ForceUpdateCanvases(); frame.EnsureTargets(); bootstrap.World.PrepareRender(player.ViewCamera);
            player.ViewCamera.Render();
            frame.OutputCamera.targetTexture = target; frame.OutputCamera.Render();
            captureUI.targetTexture = target; captureUI.Render();
            RenderTexture.active = target;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            return pixels;
        }
        finally
        {
            frame.OutputCamera.targetTexture = previousTarget;
            captureUI.targetTexture = previousUI;
            RenderTexture.active = previousActive;
            target.Release(); Destroy(target);
        }
    }

    IEnumerator ValidateHudRows()
    {
        // HUD updates at 10 Hz, so wait for the actual post-pickup strings.
        yield return new WaitForSecondsRealtime(.2f);
        Canvas.ForceUpdateCanvases();
        var evidence = SceneObjects<Text>().FirstOrDefault(t => t.name == "Evidence counts");
        var fuel = SceneObjects<Text>().FirstOrDefault(t => t.name == "Fuel counts");
        var fuses = SceneObjects<Text>().FirstOrDefault(t => t.name == "Fuse counts");
        Text[] rows = { evidence, fuel, fuses };
        string[] expected = { session.State.tapes.Count + " / 3  КАСЕТИ", session.State.canisters.Count + " / 3  ПАЛИВО", session.State.fuses.Count + " / 2  ЗАПОБІЖНИКИ" };
        string[] names = { "Evidence counts", "Fuel counts", "Fuse counts" };
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            Check(row != null, names[i] + " HUD row exists.");
            if (row == null) continue;
            report.Add("HUD " + names[i] + ": text=" + row.text + "; fontSize=" + row.fontSize + "; rect=" + row.rectTransform.rect + "; anchored=" + row.rectTransform.anchoredPosition + "; preferredHeight=" + row.preferredHeight);
            Check(row.text == expected[i] && !row.text.Contains("\n"), names[i] + " contains its separate correct counter.");
            Check(Mathf.Abs(row.rectTransform.rect.height - 32) < .5f, names[i] + " has the repaired 32-unit row height.");
            Check(row.preferredHeight <= row.rectTransform.rect.height + .5f, names[i] + " fits without vertical clipping.");
        }
        if (rows.All(r => r != null))
        {
            Check(Mathf.Abs(evidence.rectTransform.anchoredPosition.y - fuel.rectTransform.anchoredPosition.y) >= 32 && Mathf.Abs(fuel.rectTransform.anchoredPosition.y - fuses.rectTransform.anchoredPosition.y) >= 32,
                "All three evidence HUD rows occupy separate nonoverlapping vertical slots.");
        }
        yield return Save(capturePrefix + "-hud-fuel-fuses", false);
    }

    void InspectToward(float sourceX, float sourceZ, float floorY, float targetX, float targetY, float targetZ)
    {
        float dx = targetX - sourceX, dz = targetZ - sourceZ;
        player.SetInspectionPose(sourceX, sourceZ, Mathf.Atan2(-dx, -dz), Mathf.Atan2(targetY - floorY - 1.65f, Mathf.Sqrt(dx * dx + dz * dz)), false, floorY);
    }

    IEnumerator Save(string name, bool equipment = true)
    {
        for (int i = 0; i < 8; i++) yield return null;
        var frame = SceneObjects<DeadAirFrameRenderer>().FirstOrDefault();
        Check(frame != null && frame.OutputCamera != null, "GPU output renderer available for " + name);
        if (frame == null || frame.OutputCamera == null) yield break;
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        var previousTarget = frame.OutputCamera.targetTexture;
        var previousActive = RenderTexture.active;
        var rig = player.GetComponent<DeadAirFirstPersonRig>();
        try
        {
            if (!equipment && rig != null) rig.SetVisible(false);
            Canvas.ForceUpdateCanvases(); frame.EnsureTargets(); bootstrap.World.PrepareRender(player.ViewCamera);
            player.ViewCamera.Render(); frame.RenderEquipment();
            frame.OutputCamera.targetTexture = target; frame.OutputCamera.Render();
            captureUI.targetTexture = target; captureUI.Render();
            RenderTexture.active = target;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            Destroy(pixels);
        }
        finally { if (!equipment && rig != null) rig.SetVisible(true); frame.OutputCamera.targetTexture = previousTarget; RenderTexture.active = previousActive; target.Release(); Destroy(target); }
        report.Add("CAPTURE " + name + ".png");
    }

    static T[] SceneObjects<T>() where T : UnityEngine.Object
    {
        return Resources.FindObjectsOfTypeAll<T>().Where(x => {
            var component = x as Component;
            return component != null && component.gameObject.scene.IsValid();
        }).ToArray();
    }

    void Check(bool condition, string message)
    {
        if (!condition) failures++;
        string line = (condition ? "PASS: " : "FAIL: ") + message;
        report.Add(line); Debug.Log("GEOMETRY AUDIT " + line);
    }
}
