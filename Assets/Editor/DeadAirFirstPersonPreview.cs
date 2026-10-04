using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadAir.Editor
{
    /// <summary>Fast equipment renders from the real first person rig, without starting a game.</summary>
    public static class DeadAirFirstPersonPreview
    {
        const int Width = 1366, Height = 768;
        static readonly string[] Roles = { "operator", "engineer", "journalist", "dispatcher" };

        [Serializable]
        sealed class CaptureResult
        {
            public string file, role, mode;
            public float actionAge;
            public int foregroundPixels, coloredPixels;
            public int[] foregroundBounds;
            public List<ContactResult> contacts;
            public List<WristResult> wrists;
        }

        [Serializable]
        sealed class WristResult
        {
            public string hand, parts;
            public float surfaceGapMm;
            public bool passed;
        }

        [Serializable]
        sealed class ContactResult
        {
            public string hand, pad, surface, error;
            public float centreSurfaceDistanceMm, padExtentMm, signedClearanceMm, vertexPenetrationMm, surfaceSideMm;
            public bool passed, requiredTouch = true;
        }

        [Serializable]
        sealed class CaptureReport
        {
            public int width = Width, height = Height;
            public List<CaptureResult> captures = new List<CaptureResult>();
            public int contactFailures;
            public int wristFailures;
        }

        sealed class PreviewCase
        {
            public readonly string Role, Mode;
            public readonly float Age;
            public PreviewCase(string role, string mode, float age = -1)
            {
                Role = role; Mode = mode; Age = age;
            }
            public string Filename => Role + "-" + Mode + ".png";
        }

        [MenuItem("Dead Air/Render First Person Equipment")]
        public static void RenderAll() => RenderAll(null);

        public static void RenderAll(string outputDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Render first person equipment after leaving Play mode.");

            string directory = Path.GetFullPath(outputDirectory ?? Path.Combine(Application.dataPath,
                "../Art/Characters/Previews/first-person"));
            Directory.CreateDirectory(directory);
            var report = new CaptureReport();
            var world = WorldData.Load();
            var previousTarget = RenderTexture.active;
            var originalLights = new List<KeyValuePair<Light, bool>>();
            var previewScene = default(Scene);
            GameObject stage = null;
            RenderTexture target = null;
            try
            {
                // Scene lights can otherwise affect layer 30 even with an isolated
                // preview camera. Restore their exact enabled states in finally.
                foreach (var light in Resources.FindObjectsOfTypeAll<Light>())
                    if (light.gameObject.scene.IsValid() && light.gameObject.scene.isLoaded)
                    {
                        originalLights.Add(new KeyValuePair<Light, bool>(light, light.enabled));
                        light.enabled = false;
                    }

                previewScene = EditorSceneManager.NewPreviewScene();
                stage = new GameObject("First person equipment validation")
                    { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(stage, previewScene);
                var view = stage.AddComponent<Camera>();
                view.scene = previewScene;
                view.enabled = false;
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = new Color(.024f, .031f, .037f, 1);
                view.cullingMask = 0;
                view.nearClipPlane = .025f;
                view.farClipPlane = 3;
                view.fieldOfView = 62;
                view.aspect = Width / (float)Height;
                view.allowHDR = false;
                view.allowMSAA = false;
                view.useOcclusionCulling = false;

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB) { name = "First person validation frame", antiAliasing = 1 };
                target.Create();
                view.targetTexture = target;
                var rig = stage.AddComponent<DeadAirFirstPersonRig>();
                rig.Initialize(view, world);
                // The generated overlay camera shares the isolated preview scene.
                foreach (var camera in stage.GetComponentsInChildren<Camera>(true)) camera.scene = previewScene;

                var cases = new List<PreviewCase>();
                foreach (string role in Roles) cases.Add(new PreviewCase(role, "flashlight"));
                foreach (string role in Roles) cases.Add(new PreviewCase(role, "personal"));
                cases.Add(new PreviewCase("operator", "lighting", 2.3f));
                cases.Add(new PreviewCase("operator", "smoke", 4.5f));
                cases.Add(new PreviewCase("operator", "taking", .6f));
                cases.Add(new PreviewCase("operator", "drawing", 1f));
                cases.Add(new PreviewCase("operator", "exhaling", 6f));
                cases.Add(new PreviewCase("engineer", "inspecting", 1.25f));
                cases.Add(new PreviewCase("journalist", "inspecting", 1.25f));
                cases.Add(new PreviewCase("dispatcher", "inspecting", 1.25f));
                cases.Add(new PreviewCase("operator", "map"));
                cases.Add(new PreviewCase("operator", "camera"));
                foreach (string item in new[] { "keycard", "tape", "fuse", "fuel-canister" })
                    cases.Add(new PreviewCase("operator", item));

                foreach (var item in cases)
                {
                    var result = RenderCase(item, world, view, rig, target, directory);
                    report.captures.Add(result);
                    foreach (var contact in result.contacts) if (!contact.passed) report.contactFailures++;
                    foreach (var wrist in result.wrists) if (!wrist.passed) report.wristFailures++;
                    // Keep completed evidence if a later case catches an empty frame.
                    File.WriteAllText(Path.Combine(directory, "capture-report.json"), JsonUtility.ToJson(report, true));
                }
                if (report.contactFailures > 0)
                    throw new InvalidOperationException("First person grip validation failed: " + report.contactFailures +
                        " contacts. All " + report.captures.Count + " PNGs and capture-report.json saved in " + directory);
                if (report.wristFailures > 0)
                    throw new InvalidOperationException("First person wrist continuity failed: " + report.wristFailures +
                        " separated mesh pairs. PNGs and capture-report.json saved in " + directory);
                Debug.Log("FIRST PERSON PREVIEW PASSED: " + report.captures.Count + " colored captures with verified mesh contacts in " + directory);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (stage != null) UnityEngine.Object.DestroyImmediate(stage);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
                foreach (var pair in originalLights) if (pair.Key != null) pair.Key.enabled = pair.Value;
            }
        }

        static CaptureResult RenderCase(PreviewCase item, WorldData world, Camera view,
            DeadAirFirstPersonRig rig, RenderTexture target, string directory)
        {
            bool map = item.Mode == "map", camera = item.Mode == "camera";
            bool personal = item.Mode == "personal" || item.Age >= 0;
            string selectedItem = personal || map || camera ? "flashlight" : item.Mode;
            var player = new PlayerState
            {
                id = "equipment-preview", name = "Equipment preview", characterClass = item.Role,
                inventory = new List<string> { selectedItem }, selectedSlot = 0,
                flashlight = true, camera = camera, mapOpen = map, uniqueItemEquipped = personal,
                cigarettesRemaining = item.Role == "operator" ? item.Age >= 0 ? 11 : 12 : 0,
                uniqueActionSequence = item.Age >= 0 ? 1 : 0, uniqueActionAt = item.Age >= 0 ? 0 : -100,
                x = world.spawn != null ? world.spawn.x : 0, z = world.spawn != null ? world.spawn.z : 0
            };
            rig.ResetPresentation();
            rig.SynchronizeUniqueAction(item.Age);
            // Pausing freezes sway and flame flicker; accepted action age still
            // determines the cigarette, lighter, ember, and smoke positions.
            rig.Present(player, camera, map, true, 0, true);
            rig.SetVisible(true);
            var hands = view.transform.Find("Low-poly first person hands");
            if (hands == null) throw new InvalidOperationException("First person rig has no equipment root.");
            rig.CompleteEquipTransition();
            hands.localRotation = Quaternion.identity;

            view.Render();
            rig.RenderEquipment(target);
            RenderTexture.active = target;
            Texture2D image = null;
            try
            {
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(Path.Combine(directory, item.Filename), image.EncodeToPNG());
                var pixels = image.GetPixels32();
                Color32 background = pixels[(Height - 1) * Width];
                int changed = 0, colored = 0, minX = Width, minY = Height, maxX = -1, maxY = -1;
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 pixel = pixels[index];
                    int difference = Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b);
                    if (difference <= 30) continue;
                    changed++;
                    int x = index % Width, y = index / Width;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    int spread = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) - Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));
                    if (spread > 8) colored++;
                }
                if (changed < 600 || colored < 250)
                    throw new InvalidOperationException("Empty first person preview: " + item.Filename +
                        " (foreground=" + changed + ", colored=" + colored + "). PNG saved for inspection.");
                return new CaptureResult
                {
                    file = item.Filename, role = item.Role, mode = item.Mode, actionAge = item.Age,
                    foregroundPixels = changed, coloredPixels = colored,
                    foregroundBounds = new[] { minX, minY, maxX, maxY }, contacts = ValidateContacts(item, hands),
                    wrists = rig.UsingImportedHands ? ValidateWrists(hands) : new List<WristResult>()
                };
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }

        static List<WristResult> ValidateWrists(Transform root)
        {
            var results = new List<WristResult>();
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform hand = root.Find(side + " articulated hand");
                if (hand == null || !hand.gameObject.activeInHierarchy) continue;
                MeshFilter palm = FindMesh(hand, "Faceted palm"), arm = FindMesh(hand, "Tailored forearm"),
                    cuff = FindMesh(hand, "Cuff or bracelet");
                AddWrist(results, side, palm, arm);
                AddWrist(results, side, palm, cuff);
                AddWrist(results, side, cuff, arm);
            }
            return results;
        }

        static void AddWrist(List<WristResult> results, string side, MeshFilter first, MeshFilter second)
        {
            float distance = first != null && second != null ? SurfaceGap(WorldTriangles(first), WorldTriangles(second))
                : float.PositiveInfinity;
            results.Add(new WristResult { hand = side, parts = (first != null ? first.name : "missing") + " / " +
                (second != null ? second.name : "missing"), surfaceGapMm = distance * 1000,
                passed = distance <= .0015f });
        }

        // Measure the rendered mesh surfaces independently of the runtime cap
        // anchors. Intersection, vertex-to-face and edge-to-edge distances catch
        // floating wrists even when their world bounding boxes overlap.
        static float SurfaceGap(Triangle[] first, Triangle[] second)
        {
            float nearest = float.PositiveInfinity;
            foreach (var a in first)
                foreach (var b in second)
                {
                    if (SegmentHitsTriangle(a.A, a.B, b) || SegmentHitsTriangle(a.B, a.C, b) ||
                        SegmentHitsTriangle(a.C, a.A, b) || SegmentHitsTriangle(b.A, b.B, a) ||
                        SegmentHitsTriangle(b.B, b.C, a) || SegmentHitsTriangle(b.C, b.A, a)) return 0;
                    nearest = Mathf.Min(nearest, (a.A - ClosestPointOnTriangle(a.A, b.A, b.B, b.C)).sqrMagnitude,
                        (a.B - ClosestPointOnTriangle(a.B, b.A, b.B, b.C)).sqrMagnitude,
                        (a.C - ClosestPointOnTriangle(a.C, b.A, b.B, b.C)).sqrMagnitude,
                        (b.A - ClosestPointOnTriangle(b.A, a.A, a.B, a.C)).sqrMagnitude,
                        (b.B - ClosestPointOnTriangle(b.B, a.A, a.B, a.C)).sqrMagnitude,
                        (b.C - ClosestPointOnTriangle(b.C, a.A, a.B, a.C)).sqrMagnitude);
                    nearest = Mathf.Min(nearest, SegmentGapSquared(a.A, a.B, b.A, b.B),
                        SegmentGapSquared(a.A, a.B, b.B, b.C), SegmentGapSquared(a.A, a.B, b.C, b.A),
                        SegmentGapSquared(a.B, a.C, b.A, b.B), SegmentGapSquared(a.B, a.C, b.B, b.C),
                        SegmentGapSquared(a.B, a.C, b.C, b.A), SegmentGapSquared(a.C, a.A, b.A, b.B),
                        SegmentGapSquared(a.C, a.A, b.B, b.C), SegmentGapSquared(a.C, a.A, b.C, b.A));
                    if (nearest < 1e-12f) return 0;
                }
            return Mathf.Sqrt(nearest);
        }

        static bool SegmentHitsTriangle(Vector3 start, Vector3 end, Triangle triangle)
        {
            Vector3 direction = end - start, edge1 = triangle.B - triangle.A, edge2 = triangle.C - triangle.A;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-12f) return false;
            float inverse = 1 / determinant;
            Vector3 offset = start - triangle.A;
            float u = Vector3.Dot(offset, p) * inverse;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            float along = Vector3.Dot(edge2, q) * inverse;
            return along >= 0 && along <= 1;
        }

        static float SegmentGapSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 u = b - a, v = d - c, offset = a - c;
            float aa = Vector3.Dot(u, u), vv = Vector3.Dot(v, v), uv = Vector3.Dot(u, v),
                uw = Vector3.Dot(u, offset), vw = Vector3.Dot(v, offset);
            if (aa <= 1e-12f) return (a - (c + v * (vv > 1e-12f ? Mathf.Clamp01(vw / vv) : 0))).sqrMagnitude;
            if (vv <= 1e-12f) return (c - (a + u * Mathf.Clamp01(-uw / aa))).sqrMagnitude;
            float denominator = aa * vv - uv * uv;
            float s = denominator > 1e-20f ? Mathf.Clamp01((uv * vw - uw * vv) / denominator) : 0;
            float t = (uv * s + vw) / vv;
            if (t < 0) { t = 0; s = Mathf.Clamp01(-uw / aa); }
            else if (t > 1) { t = 1; s = Mathf.Clamp01((uv - uw) / aa); }
            return (a + u * s - c - v * t).sqrMagnitude;
        }

        static List<ContactResult> ValidateContacts(PreviewCase item, Transform root)
        {
            var results = new List<ContactResult>();
            if (item.Mode == "map")
            {
                AddPinch(results, root, "Left", "Folded station plan", true);
                AddPinch(results, root, "Right", "Folded station plan", true);
            }
            else if (item.Mode == "camera")
            {
                AddWrap(results, root, "Right", "VHS-C shell");
                AddWrap(results, root, "Left", "VHS-C shell");
                results.Add(CheckContact(root, "Left", "Faceted palm", "VHS-C shell", false));
            }
            else if (item.Mode == "personal" || item.Age >= 0)
            {
                if (item.Role == "operator")
                {
                    if (item.Age >= .45f)
                    {
                        results.Add(CheckContact(root, "Right", "Finger 0 curled tip", "Ochre filter", false));
                        results.Add(CheckContact(root, "Right", "Finger 1 curled tip", "Ochre filter", false));
                        results.Add(CheckContact(root, "Right", "Faceted palm", "Ochre filter", false, false));
                        AddSegments(results, root, "Right", "Ochre filter");
                    }
                    AddPinch(results, root, "Left", item.Age > 1.5f && item.Age < 3.25f
                        ? "Lighter body" : "Burgundy cigarette pack");
                }
                else if (item.Role == "engineer") AddWrap(results, root, "Right", "Wrapped handle");
                else AddPinch(results, root, "Right", item.Role == "journalist" ? "Pocket recorder shell" : "Brass compass case");
            }
            else
            {
                string surface = item.Mode == "flashlight" ? "Ribbed rubber grip"
                    : item.Mode == "keycard" ? "ID card" : item.Mode == "tape" ? "Tape casing"
                    : item.Mode == "fuse" ? "Ceramic fuse" : "Handle top";
                if (item.Mode == "flashlight" || item.Mode == "fuse" || item.Mode == "fuel-canister")
                    AddWrap(results, root, "Right", surface);
                else AddPinch(results, root, "Right", surface);
            }
            return results;
        }

        static void AddPinch(List<ContactResult> results, Transform root, string hand, string surface, bool twoSided = false)
        {
            var thumb = CheckContact(root, hand, "Thumb tip", surface, twoSided);
            var index = CheckContact(root, hand, "Finger 0 curled tip", surface, twoSided);
            if (twoSided && thumb.passed && index.passed && thumb.surfaceSideMm * index.surfaceSideMm >= 0)
            {
                thumb.passed = index.passed = false;
                thumb.error = index.error = "Thumb and index must pinch opposite faces of the paper.";
            }
            results.Add(thumb);
            results.Add(index);
            if (!twoSided)
            {
                results.Add(CheckContact(root, hand, "Faceted palm", surface, false, false));
                AddSegments(results, root, hand, surface);
            }
        }

        static void AddWrap(List<ContactResult> results, Transform root, string hand, string surface)
        {
            AddPinch(results, root, hand, surface);
            for (int finger = 1; finger < 4; finger++)
                results.Add(CheckContact(root, hand, "Finger " + finger + " curled tip", surface, false));
        }

        static void AddSegments(List<ContactResult> results, Transform root, string hand, string surface)
        {
            for (int finger = 0; finger < 4; finger++)
                foreach (string segment in new[] { "proximal", "middle", "distal" })
                    results.Add(CheckContact(root, hand, "Finger " + finger + " " + segment, surface, false, false));
            results.Add(CheckContact(root, hand, "Opposed thumb", surface, false, false));
            results.Add(CheckContact(root, hand, "Thumb middle", surface, false, false));
        }

        // Distances use actual transformed triangles and the actual pad mesh's
        // extent toward the nearest surface. An AABB overlap does not prove a grip.
        static ContactResult CheckContact(Transform root, string side, string padName, string surfaceName, bool twoSided,
            bool requiredTouch = true)
        {
            var result = new ContactResult { hand = side, pad = padName, surface = surfaceName, requiredTouch = requiredTouch };
            Transform hand = root.Find(side + " articulated hand");
            MeshFilter pad = FindMesh(hand, padName), surface = FindMesh(root, surfaceName);
            if (pad == null || surface == null || !pad.gameObject.activeInHierarchy || !surface.gameObject.activeInHierarchy)
            {
                result.error = "Missing or inactive expected pad/surface.";
                return result;
            }
            var triangles = WorldTriangles(surface);
            Vector3 centre = pad.transform.TransformPoint(pad.sharedMesh.bounds.center);
            Vector3 closest, normal;
            float distance = NearestSurface(centre, triangles, out closest, out normal);
            Vector3 direction = distance > .000001f ? (closest - centre) / distance : -normal;
            float extent = 0, penetration = 0;
            var vertices = pad.sharedMesh.vertices;
            var padVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = pad.transform.TransformPoint(vertices[i]);
                padVertices[i] = worldVertex;
                extent = Mathf.Max(extent, Vector3.Dot(worldVertex - centre, direction));
                if (!twoSided) penetration = Mathf.Max(penetration, InteriorDepth(worldVertex, triangles));
            }
            if (!twoSided)
            {
                // A long phalanx may cross a thin prop with both end caps outside.
                // Sample its centre and actual triangle edges/faces as well.
                penetration = Mathf.Max(penetration, InteriorDepth(centre, triangles));
                var indices = pad.sharedMesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = padVertices[indices[i]], b = padVertices[indices[i + 1]], c = padVertices[indices[i + 2]];
                    penetration = Mathf.Max(penetration, InteriorDepth((a + b) * .5f, triangles));
                    penetration = Mathf.Max(penetration, InteriorDepth((b + c) * .5f, triangles));
                    penetration = Mathf.Max(penetration, InteriorDepth((c + a) * .5f, triangles));
                    penetration = Mathf.Max(penetration, InteriorDepth((a + b + c) / 3, triangles));
                }
            }
            float signedDistance = twoSided || !InsideConvexSurface(centre, triangles) ? distance : -distance;
            float clearance = signedDistance - extent;
            result.centreSurfaceDistanceMm = distance * 1000;
            result.surfaceSideMm = Vector3.Dot(centre - closest, normal) * 1000;
            result.padExtentMm = extent * 1000;
            result.signedClearanceMm = clearance * 1000;
            result.vertexPenetrationMm = penetration * 1000;
            result.passed = requiredTouch ? clearance <= .004f && clearance >= -.004f && penetration <= .004f
                : penetration <= .004f && signedDistance >= -.004f;
            if (!result.passed) result.error = requiredTouch && clearance > .004f ? "Pad floats clear of the prop surface."
                : "Pad penetrates the prop surface by more than 4 mm.";
            return result;
        }

        static float InteriorDepth(Vector3 point, Triangle[] triangles)
        {
            if (!InsideConvexSurface(point, triangles)) return 0;
            Vector3 hit, outward;
            return NearestSurface(point, triangles, out hit, out outward);
        }

        static bool InsideConvexSurface(Vector3 point, Triangle[] triangles)
        {
            // The held closed surfaces are boxes and octagonal frusta. A point
            // lies inside only when it is behind every outward face plane.
            // One nearest face is ambiguous at a cap edge and can give a false
            // interior result far outside a tapered prism.
            foreach (var triangle in triangles)
                if (Vector3.Dot(point - triangle.A, triangle.Normal) > .000001f) return false;
            return true;
        }

        static MeshFilter FindMesh(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.name == name && mesh.sharedMesh != null) return mesh;
            return null;
        }

        struct Triangle { public Vector3 A, B, C, Normal; }

        static Triangle[] WorldTriangles(MeshFilter source)
        {
            var vertices = source.sharedMesh.vertices;
            var indices = source.sharedMesh.triangles;
            var triangles = new Triangle[indices.Length / 3];
            for (int i = 0; i < triangles.Length; i++)
            {
                Vector3 a = source.transform.TransformPoint(vertices[indices[i * 3]]);
                Vector3 b = source.transform.TransformPoint(vertices[indices[i * 3 + 1]]);
                Vector3 c = source.transform.TransformPoint(vertices[indices[i * 3 + 2]]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                // Unity's normalized returns zero below a fixed length epsilon;
                // cigarette cap triangles are smaller than that in metres.
                Vector3 normal = cross.sqrMagnitude > 1e-20f ? cross / Mathf.Sqrt(cross.sqrMagnitude) : Vector3.zero;
                triangles[i] = new Triangle { A = a, B = b, C = c, Normal = normal };
            }
            return triangles;
        }

        static float NearestSurface(Vector3 point, Triangle[] triangles, out Vector3 closest, out Vector3 normal)
        {
            float best = float.PositiveInfinity;
            closest = Vector3.zero; normal = Vector3.forward;
            foreach (var triangle in triangles)
            {
                Vector3 candidate = ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
                float distance = (point - candidate).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; closest = candidate; normal = triangle.Normal;
            }
            return Mathf.Sqrt(best);
        }

        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = 1 / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }
    }
}
