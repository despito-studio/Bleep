using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Checks the imported replacement cast, its deformed soles and the actual runtime contact solver.</summary>
    public static class DeadAirIndieCharacterValidation
    {
        static readonly string[] Roles = { "operator", "engineer", "journalist", "dispatcher" };
        static readonly string[] Clips = { "Idle", "Walk", "Run", "UseItem", "WalkBack", "StrafeLeft", "StrafeRight", "CrouchIdle", "CrouchWalk" };
        static readonly string[] RequiredBones = { "root", "pelvis", "spine", "chest", "neck", "head" };
        const string OutputFolder = "Art/Characters/IndieCast/Unity";
        const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>Full replacement-cast gate, including existing owner-body and equipment provenance regressions.</summary>
        public static void ValidateAll()
        {
            Validate();
            DeadAirFirstPersonBodyValidation.Validate();
            DeadAirImportedCharacterValidation.Validate();
        }

        public static void RunRuntimeOnly()
        {
            var report = new List<string> { "INDIE RUNTIME-ONLY CONTACT / EQUIPMENT GATE", "UTC " + DateTime.UtcNow.ToString("O") };
            var failures = new List<Exception>();
            try
            {
                foreach (string role in Roles)
                    RecordCheck(role, failures, report, () => {
                        var instance = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
                        instance.hideFlags = HideFlags.HideAndDontSave;
                        try
                        {
                            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                            var feet = FootVertices(skin);
                            var bones = instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                            var rest = ChainLengths(bones); var roleFailures = new List<Exception>();
                            RecordCheck(role + " flat runtime", roleFailures, report, () => ValidateRuntimeContacts(role, instance, skin, feet, bones, rest, report));
                            RecordCheck(role + " equipment", roleFailures, report, () => ValidateEquipment(role, instance, report));
                            ThrowFailures(role + " runtime", roleFailures);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(instance); }
                    });
                ThrowFailures("Runtime-only cast gate", failures);
                report.Add("PASS: every role checked with physical flat-floor gait and equipment; raw baked clips intentionally checked in the import gate.");
                Debug.Log("DEAD AIR INDIE RUNTIME ONLY PASSED.");
            }
            finally { Directory.CreateDirectory(OutputFolder); File.WriteAllLines(Path.Combine(OutputFolder, "runtime-only-report.txt"), report); }
        }

        public static void RunRuntimeContracts()
        {
            var failures = new List<Exception>(); var report = new List<string>();
            RecordCheck("flat runtime/equipment", failures, report, RunRuntimeOnly);
            RecordCheck("terrain/turn/pause/reapply", failures, report, ValidateTerrainAndTurns);
            ThrowFailures("Complete runtime cast contracts", failures);
        }

        /// <summary>Five physical/contact regressions, separate from the flat import gate.</summary>
        public static void ValidateTerrainAndTurns()
        {
            var report = new List<string> { "INDIE CAST TERRAIN / TRANSITION VALIDATION", "UTC " + DateTime.UtcNow.ToString("O") };
            var failures = new List<Exception>();
            var preview = typeof(DeadAirCharacterMotion).GetMethod("SetLocomotionPreview", PublicInstance);
            Require(preview != null, "Runtime locomotion preview missing.");
            try
            {
                foreach (string role in Roles)
                {
                    RecordCheck(role, failures, report, () => {
                    var instance = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        var feet = FootVertices(skin);
                        var motion = instance.GetComponent<DeadAirCharacterMotion>();
                        var roleFailures = new List<Exception>();
                        RecordCheck(role + " slope", roleFailures, report, () => ValidateSlope(role, instance, skin, feet, motion, preview, report));
                        RecordCheck(role + " step", roleFailures, report, () => ValidateStep(role, instance, skin, feet, motion, preview, report));
                        RecordCheck(role + " turn/stop", roleFailures, report, () => ValidateTurnStop(role, instance, skin, feet, motion, preview, report));
                        RecordCheck(role + " pause", roleFailures, report, () => ValidatePause(role, instance, skin, feet, motion, preview, report));
                        RecordCheck(role + " repeat sampling", roleFailures, report, () => ValidateRepeatedSample(role, instance, skin, feet, motion, preview, report));
                        ThrowFailures(role + " terrain/transition", roleFailures);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instance); }
                    });
                }
                ThrowFailures("Terrain/transition cast gate", failures);
                report.Add("PASS: four roles × slope, 10cm step, turn/stop, pause and repeated runtime contact passes.");
                Debug.Log("DEAD AIR INDIE TERRAIN PASSED: physical slope/step contacts, smooth turn/stop, pause and repeat-safe contact sampling.");
            }
            catch (Exception error) { report.Add("FAIL: " + error); throw; }
            finally { Directory.CreateDirectory(OutputFolder); File.WriteAllLines(Path.Combine(OutputFolder, "terrain-report.txt"), report); }
        }

        static readonly Vector3 FixtureOrigin = new Vector3(1500, 20, 1500);

        static GameObject FixtureFloor(string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name; floor.hideFlags = HideFlags.HideAndDontSave;
            floor.transform.SetPositionAndRotation(center, rotation); floor.transform.localScale = size;
            floor.GetComponent<Renderer>().enabled = false; Physics.SyncTransforms();
            return floor;
        }

        static void FixturePose(GameObject instance, DeadAirCharacterMotion motion, MethodInfo preview, Vector3 velocity,
            float dt, bool reset = false)
        {
            preview.Invoke(motion, new object[] { velocity, false, false, dt, reset });
        }

        static void ValidateSlope(string role, GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet,
            DeadAirCharacterMotion motion, MethodInfo preview, List<string> report)
        {
            var rotation = Quaternion.Euler(-15, 0, 0); var normal = rotation * Vector3.up;
            var floor = FixtureFloor("Indie 15 degree slope", FixtureOrigin - normal * .05f, new Vector3(60, .1f, 60), rotation);
            try
            {
                instance.transform.SetPositionAndRotation(FixtureOrigin, Quaternion.identity);
                float lowest = float.PositiveInfinity, error = 0;
                for (int frame = 0; frame < 120; frame++)
                {
                    var position = instance.transform.position + Vector3.forward * (2.4f / 60);
                    position.y = FixtureOrigin.y + (position.z - FixtureOrigin.z) * Mathf.Tan(15 * Mathf.Deg2Rad);
                    instance.transform.position = position;
                    FixturePose(instance, motion, preview, Vector3.forward * 2.4f, 1f / 60, frame == 0);
                    lowest = Mathf.Min(lowest, SurfaceClearance(skin, feet, point => Vector3.Dot(point - FixtureOrigin, normal)));
                    error = Mathf.Max(error, ReadMetric(motion, "FootContactError"));
                }
                Require(lowest >= -.012f && error <= .065f, role + " 15 degree slope: sole clearance=" + F(lowest) + ", contact error=" + F(error));
                report.Add(role + " physical slope 15 degrees: 120 samples; min surface clearance=" + F(lowest) + ", max contact error=" + F(error) + " m.");
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
        }

        static void ValidateStep(string role, GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet,
            DeadAirCharacterMotion motion, MethodInfo preview, List<string> report)
        {
            var floor = FixtureFloor("Indie step base", FixtureOrigin - Vector3.up * .05f, new Vector3(60, .1f, 60), Quaternion.identity);
            float edge = FixtureOrigin.z + 1.3f;
            var step = FixtureFloor("Indie 10cm step", new Vector3(FixtureOrigin.x, FixtureOrigin.y + .05f, edge + 3), new Vector3(20, .1f, 6), Quaternion.identity);
            try
            {
                instance.transform.SetPositionAndRotation(FixtureOrigin, Quaternion.identity);
                float lowest = float.PositiveInfinity, error = 0;
                for (int frame = 0; frame < 120; frame++)
                {
                    var position = instance.transform.position + Vector3.forward * (2.4f / 60);
                    position.y = FixtureOrigin.y + (position.z >= edge ? .1f : 0);
                    instance.transform.position = position;
                    FixturePose(instance, motion, preview, Vector3.forward * 2.4f, 1f / 60, frame == 0);
                    lowest = Mathf.Min(lowest, SurfaceClearance(skin, feet, point => point.y - FixtureOrigin.y - (point.z >= edge && point.z <= edge + 6 ? .1f : 0)));
                    error = Mathf.Max(error, ReadMetric(motion, "FootContactError"));
                }
                Require(lowest >= -.018f && error <= .08f, role + " 10cm step: sole clearance=" + F(lowest) + ", contact error=" + F(error));
                report.Add(role + " physical 10cm step: 120 samples; min surface clearance=" + F(lowest) + ", max contact error=" + F(error) + " m.");
            }
            finally { UnityEngine.Object.DestroyImmediate(step); UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
        }

        static void ValidateTurnStop(string role, GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet,
            DeadAirCharacterMotion motion, MethodInfo preview, List<string> report)
        {
            var floor = FixtureFloor("Indie turn/stop floor", FixtureOrigin - Vector3.up * .05f, new Vector3(60, .1f, 60), Quaternion.identity);
            try
            {
                instance.transform.SetPositionAndRotation(FixtureOrigin, Quaternion.identity);
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var left = bones.First(t => t.name == "FootContact.L"); var right = bones.First(t => t.name == "FootContact.R");
                var leftAnkle = bones.First(t => t.name == "foot.L"); var rightAnkle = bones.First(t => t.name == "foot.R");
                Vector3 oldLeft = Vector3.zero, oldRight = Vector3.zero;
                Quaternion oldLeftRotation = Quaternion.identity, oldRightRotation = Quaternion.identity;
                float maximumJump = 0, maximumAngle = 0, lowest = float.PositiveInfinity;
                for (int frame = 0; frame < 150; frame++)
                {
                    instance.transform.rotation = Quaternion.Euler(0, Mathf.Min(frame, 90), 0);
                    FixturePose(instance, motion, preview, Vector3.zero, 1f / 60, frame == 0);
                    if (frame > 0)
                    {
                        maximumJump = Mathf.Max(maximumJump, Vector3.Distance(left.position, oldLeft), Vector3.Distance(right.position, oldRight));
                        maximumAngle = Mathf.Max(maximumAngle, Quaternion.Angle(leftAnkle.rotation, oldLeftRotation), Quaternion.Angle(rightAnkle.rotation, oldRightRotation));
                    }
                    oldLeft = left.position; oldRight = right.position; oldLeftRotation = leftAnkle.rotation; oldRightRotation = rightAnkle.rotation;
                    lowest = Mathf.Min(lowest, SurfaceClearance(skin, feet, point => point.y - FixtureOrigin.y));
                }
                Require(maximumJump <= .10f && maximumAngle <= 15 && lowest >= -.012f,
                    role + " turn/stop: foot jump=" + F(maximumJump) + " m, ankle frame jump=" + F(maximumAngle) + " degrees, clearance=" + F(lowest));
                report.Add(role + " turn 90 degrees then stop: 150 samples; foot frame jump=" + F(maximumJump) + " m, ankle frame jump=" + F(maximumAngle) + " degrees.");
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
        }

        static void ValidatePause(string role, GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet,
            DeadAirCharacterMotion motion, MethodInfo preview, List<string> report)
        {
            var floor = FixtureFloor("Indie pause floor", FixtureOrigin - Vector3.up * .05f, new Vector3(60, .1f, 60), Quaternion.identity);
            try
            {
                instance.transform.SetPositionAndRotation(FixtureOrigin, Quaternion.identity);
                for (int frame = 0; frame < 47; frame++)
                {
                    instance.transform.position += Vector3.forward * (2.4f / 60);
                    FixturePose(instance, motion, preview, Vector3.forward * 2.4f, 1f / 60, frame == 0);
                }
                var pose = FootPoints(skin, feet); float phase = ReadMetric(motion, "LocomotionPhase"), difference = 0;
                for (int repeat = 0; repeat < 12; repeat++)
                {
                    FixturePose(instance, motion, preview, Vector3.forward * 2.4f, 0);
                    difference = Mathf.Max(difference, PointDifference(pose, FootPoints(skin, feet)));
                    Require(Mathf.Abs(ReadMetric(motion, "LocomotionPhase") - phase) < .000001f, role + " paused gait advances phase.");
                }
                Require(difference < .001f, role + " paused deformed feet move by " + F(difference) + " m.");
                report.Add(role + " pause: 12 repeat samples, maximum foot displacement=" + F(difference) + " m; phase fixed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
        }

        static void ValidateRepeatedSample(string role, GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet,
            DeadAirCharacterMotion motion, MethodInfo preview, List<string> report)
        {
            var floor = FixtureFloor("Indie runtime repeat floor", FixtureOrigin - Vector3.up * .05f, new Vector3(60, .1f, 60), Quaternion.identity);
            try
            {
                instance.transform.SetPositionAndRotation(FixtureOrigin, Quaternion.identity);
                var actor = new PlayerState { characterClass = role, x = -FixtureOrigin.x, y = FixtureOrigin.y, z = FixtureOrigin.z,
                    inventory = new List<string> { "flashlight" }, uniqueActionCancelled = true };
                motion.ResetLocomotionContacts(); motion.SetLocomotionPose(false, 2.4f, Vector3.forward * 2.4f);
                motion.Present(actor, 100, false, true);
                var pose = FootPoints(skin, feet); float phase = ReadMetric(motion, "LocomotionPhase"), difference = 0;
                var late = typeof(DeadAirCharacterMotion).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                Require(late != null, "Actual runtime LateUpdate missing.");
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    late.Invoke(motion, null);
                    difference = Mathf.Max(difference, PointDifference(pose, FootPoints(skin, feet)));
                    motion.ReapplyGroundContacts();
                    difference = Mathf.Max(difference, PointDifference(pose, FootPoints(skin, feet)));
                    Require(Mathf.Abs(ReadMetric(motion, "LocomotionPhase") - phase) < .000001f, role + " LateUpdate/reapply advances gait twice.");
                }
                Require(difference < .0015f, role + " Present/LateUpdate/reapply accumulates foot displacement " + F(difference) + " m.");
                report.Add(role + " actual Present + 3 LateUpdate/reapply passes: maximum foot difference=" + F(difference) + " m; phase unchanged.");
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
        }

        static Vector3[] FootPoints(SkinnedMeshRenderer skin, HashSet<int> feet)
        {
            var mesh = new Mesh();
            try { skin.BakeMesh(mesh); var vertices = mesh.vertices; return feet.Select(i => skin.transform.TransformPoint(vertices[i])).ToArray(); }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static float PointDifference(Vector3[] a, Vector3[] b)
        {
            Require(a.Length == b.Length, "Repeated contact sampling changed topology.");
            float difference = 0; for (int i = 0; i < a.Length; i++) difference = Mathf.Max(difference, Vector3.Distance(a[i], b[i]));
            return difference;
        }

        static float SurfaceClearance(SkinnedMeshRenderer skin, HashSet<int> feet, Func<Vector3, float> clearance)
        {
            float lowest = float.PositiveInfinity;
            foreach (var point in FootPoints(skin, feet)) { Require(Finite(point), "Terrain solve has nonfinite foot geometry."); lowest = Mathf.Min(lowest, clearance(point)); }
            return lowest;
        }

        [MenuItem("Dead Air/Validate Indie Character Remake")]
        public static void Validate()
        {
            var report = new List<string> { "INDIE CAST VALIDATION", "UTC " + DateTime.UtcNow.ToString("O"), "Unity " + Application.unityVersion };
            var failures = new List<Exception>();
            try
            {
                RecordCheck("base import", failures, report, DeadAirCharacterImporter.Validate);
                foreach (string role in Roles) RecordCheck(role, failures, report, () => ValidateCharacter(role, report));
                ThrowFailures("Imported replacement cast", failures);
                report.Add("PASS: four replacement bodies; palette/UVs/weights/hierarchy; nine complete clip cycles; runtime direction/speed/crouch contact; equipment grip contacts.");
                Debug.Log("DEAD AIR INDIE CAST PASSED: imported skin, full cycles and actual runtime foot-contact solver validated.");
            }
            catch (Exception error)
            {
                report.Add("FAIL: " + error);
                throw;
            }
            finally
            {
                Directory.CreateDirectory(OutputFolder);
                File.WriteAllLines(Path.Combine(OutputFolder, "validation-report.txt"), report);
            }
        }

        static void ValidateCharacter(string role, List<string> report)
        {
            var prefab = Resources.Load<GameObject>("World/character_" + role);
            Require(prefab != null, role + " prefab missing.");
            var sourceSkins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(sourceSkins.Length == 1, role + " must use exactly one replacement body.");
            var skin = sourceSkins[0];
            ValidateSkin(role, skin, prefab.transform, report);
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = new Vector3(1500, 20, 1500);
            try
            {
                var animation = instance.GetComponentInChildren<Animation>(true);
                Require(animation != null, role + " animation missing.");
                animation.playAutomatically = false;
                var bones = instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var mesh = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var footIndices = FootVertices(mesh);
                Require(footIndices.Count > 20, role + " soles must have real foot/toe skin weights.");
                var lengths = ChainLengths(bones);
                var failures = new List<Exception>();
                foreach (string name in Clips) RecordCheck(role + " raw " + name, failures, report, () => ValidateClip(role, name, animation, instance, mesh, footIndices, bones, lengths, report));
                RecordCheck(role + " flat runtime", failures, report, () => ValidateRuntimeContacts(role, instance, mesh, footIndices, bones, lengths, report));
                RecordCheck(role + " equipment", failures, report, () => ValidateEquipment(role, instance, report));
                ThrowFailures(role + " clips/runtime", failures);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        static void ValidateSkin(string role, SkinnedMeshRenderer skin, Transform root, List<string> report)
        {
            var mesh = skin.sharedMesh;
            Require(mesh != null && mesh.isReadable && mesh.vertexCount > 500, role + " readable body mesh missing.");
            int triangles = mesh.triangles.Length / 3;
            Require(triangles < 20000, role + " exceeds the established 20k character triangle budget.");
            Require(mesh.vertices.All(Finite) && mesh.normals.Length == mesh.vertexCount && mesh.normals.All(Finite), role + " nonfinite geometry or missing normals.");
            Require(mesh.uv.Length == mesh.vertexCount && mesh.uv.All(p => Finite(p.x) && Finite(p.y) && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1), role + " palette UV coordinates invalid.");
            Require(skin.bones.Length >= 40 && skin.bones.All(b => b != null && b.IsChildOf(root)), role + " replacement deform skeleton incomplete or external.");
            Require(skin.bones.Select(b => b.name).Distinct().Count() == skin.bones.Length, role + " duplicate deform bone names.");
            Require(mesh.bindposes.Length == skin.bones.Length && mesh.bindposes.All(Finite), role + " bind matrices invalid.");
            var bones = skin.bones.ToDictionary(b => b.name);
            foreach (string name in RequiredBones) Require(bones.ContainsKey(name), role + " missing " + name);
            foreach (string side in new[] { "L", "R" })
            {
                foreach (string name in new[] { "clavicle", "upper_arm", "forearm", "hand", "thigh", "shin", "foot", "toe" })
                    Require(bones.ContainsKey(name + "." + side), role + " missing " + name + "." + side);
                CheckParent(bones, "upper_arm." + side, "clavicle." + side, role);
                CheckParent(bones, "forearm." + side, "upper_arm." + side, role);
                CheckParent(bones, "hand." + side, "forearm." + side, role);
                CheckParent(bones, "shin." + side, "thigh." + side, role);
                CheckParent(bones, "foot." + side, "shin." + side, role);
                CheckParent(bones, "toe." + side, "foot." + side, role);
                Require(skin.bones.Count(b => b.IsChildOf(bones["hand." + side]) && b != bones["hand." + side]) >= 12,
                    role + " missing articulated finger chains on " + side);
            }
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (string marker in new[] { "ItemSocket.L", "ItemSocket.R", "FootContact.L", "FootContact.R", "EyeSocket", "TorsoFront" })
                Require(transforms.Any(t => t.name == marker), role + " missing authored contact " + marker);
            Require(mesh.boneWeights.Length == mesh.vertexCount, role + " skin weight array incomplete.");
            float maximumWeightError = 0;
            foreach (var weight in mesh.boneWeights)
            {
                var weights = new[] { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
                var indices = new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
                float sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    Require(Finite(weights[i]) && weights[i] >= 0 && weights[i] <= 1, role + " skin weight invalid.");
                    if (weights[i] > 0) Require(indices[i] >= 0 && indices[i] < skin.bones.Length, role + " weight addresses missing bone.");
                    sum += weights[i];
                }
                maximumWeightError = Mathf.Max(maximumWeightError, Mathf.Abs(sum - 1));
            }
            Require(maximumWeightError < .001f, role + " unweighted or unnormalised vertices: " + maximumWeightError);
            Require(skin.sharedMaterials.Length == 1 && mesh.subMeshCount == 1, role + " must use one clean palette material.");
            var material = skin.sharedMaterial;
            Require(material != null && material.shader != null && material.shader.name == "Dead Air/Source Surface", role + " surface shader incorrect.");
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Characters/Textures/" + role + "_indie_albedo.png");
            Require(atlas != null && material.mainTexture == atlas, role + " new UVs still use the previous character atlas.");
            Require(material.color == Color.white && material.GetColor("_EmissionColor").maxColorComponent < .001f, role + " palette tint/emission incorrect.");
            Require(material.GetTextureScale("_MainTex") == Vector2.one && material.GetTextureOffset("_MainTex") == Vector2.zero, role + " atlas transform changed.");
            report.Add(role + ": vertices=" + mesh.vertexCount + ", triangles=" + triangles + ", deform bones=" + skin.bones.Length +
                ", max weight error=" + F(maximumWeightError) + ", replacement atlas=" + AssetDatabase.GetAssetPath(atlas));
        }

        static void CheckParent(Dictionary<string, Transform> bones, string child, string parent, string role)
        {
            Require(bones[child].parent == bones[parent], role + " hierarchy broken: " + child + " -> " + parent);
        }

        static HashSet<int> FootVertices(SkinnedMeshRenderer skin)
        {
            var footBones = new HashSet<int>(skin.bones.Select((b, i) => (b, i)).Where(p =>
                p.b.name.StartsWith("foot.", StringComparison.Ordinal) || p.b.name.StartsWith("toe.", StringComparison.Ordinal)).Select(p => p.i));
            var vertices = new HashSet<int>();
            var weights = skin.sharedMesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                float total = (footBones.Contains(w.boneIndex0) ? w.weight0 : 0) + (footBones.Contains(w.boneIndex1) ? w.weight1 : 0) +
                    (footBones.Contains(w.boneIndex2) ? w.weight2 : 0) + (footBones.Contains(w.boneIndex3) ? w.weight3 : 0);
                if (total > .5f) vertices.Add(i);
            }
            return vertices;
        }

        static Dictionary<string, float> ChainLengths(Dictionary<string, Transform> bones)
        {
            var result = new Dictionary<string, float>();
            foreach (string side in new[] { "L", "R" })
            foreach (var pair in new[] { ("upper_arm", "forearm"), ("forearm", "hand"), ("thigh", "shin"), ("shin", "foot") })
            {
                string key = pair.Item1 + "." + side + "/" + pair.Item2 + "." + side;
                result[key] = Vector3.Distance(bones[pair.Item1 + "." + side].position, bones[pair.Item2 + "." + side].position);
                Require(result[key] > .12f && result[key] < .75f, "Incorrect adult limb length: " + key + " " + result[key]);
            }
            return result;
        }

        static void CheckChains(Dictionary<string, Transform> bones, Dictionary<string, float> rest, string context)
        {
            foreach (var pair in rest)
            {
                var names = pair.Key.Split('/');
                float length = Vector3.Distance(bones[names[0]].position, bones[names[1]].position);
                Require(Finite(length) && length > pair.Value * .8f && length < pair.Value * 1.2f, context + " collapsed/stretched " + pair.Key);
            }
        }

        static float SampleSoles(GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet, string context)
        {
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                var vertices = mesh.vertices;
                Require(vertices.Length == skin.sharedMesh.vertexCount && vertices.All(Finite), context + " nonfinite/corrupt deformed skin.");
                float lowest = float.PositiveInfinity;
                foreach (int index in feet)
                {
                    var local = instance.transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[index]));
                    lowest = Mathf.Min(lowest, local.y);
                }
                Require(Finite(lowest), context + " soles could not be measured.");
                return lowest;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static void ValidateClip(string role, string name, Animation animation, GameObject instance, SkinnedMeshRenderer skin,
            HashSet<int> feet, Dictionary<string, Transform> bones, Dictionary<string, float> rest, List<string> report)
        {
            var clip = animation.GetClip(name);
            Require(clip != null && clip.legacy && clip.length > .1f, role + " missing usable " + name);
            float lowest = float.PositiveInfinity, highestMinimum = float.NegativeInfinity;
            int frames = Mathf.Max(24, Mathf.CeilToInt(clip.length * 60));
            for (int frame = 0; frame <= frames; frame++)
            {
                clip.SampleAnimation(animation.gameObject, clip.length * frame / frames);
                CheckChains(bones, rest, role + " " + name + " frame " + frame);
                float sole = SampleSoles(instance, skin, feet, role + " " + name);
                lowest = Mathf.Min(lowest, sole); highestMinimum = Mathf.Max(highestMinimum, sole);
            }
            Require(lowest >= -.008f, role + " " + name + " baked soles penetrate the floor by " + F(-lowest) + " m.");
            if (name != "UseItem") Require(highestMinimum < (name == "Run" ? .15f : .08f), role + " " + name + " loses support for the whole body.");
            report.Add(role + " raw " + name + ": " + (frames + 1) + " samples, lowest sole=" + F(lowest) + " m, highest two-foot minimum=" + F(highestMinimum) + " m.");
        }

        static void ValidateRuntimeContacts(string role, GameObject instance, SkinnedMeshRenderer skin, HashSet<int> feet,
            Dictionary<string, Transform> bones, Dictionary<string, float> rest, List<string> report)
        {
            var motion = instance.GetComponent<DeadAirCharacterMotion>();
            var preview = typeof(DeadAirCharacterMotion).GetMethod("SetLocomotionPreview", PublicInstance);
            Require(preview != null, "Missing deterministic preview for the actual runtime locomotion/contact solver.");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Indie locomotion physical test floor"; floor.hideFlags = HideFlags.HideAndDontSave;
            floor.transform.position = new Vector3(1500, 19.95f, 1500); floor.transform.localScale = new Vector3(60, .1f, 60);
            floor.GetComponent<Renderer>().enabled = false;
            Physics.SyncTransforms();
            var scenarios = new[] {
                ("slow walk", new Vector3(0, 0, .8f), false, false),
                ("walk", new Vector3(0, 0, 2.4f), false, false),
                ("run", new Vector3(0, 0, 5f), false, true),
                ("back", new Vector3(0, 0, -2.4f), false, false),
                ("strafe left", new Vector3(-2.4f, 0, 0), false, false),
                ("strafe right", new Vector3(2.4f, 0, 0), false, false),
                ("crouch", new Vector3(0, 0, 1.4f), true, false),
                ("crouch back", new Vector3(0, 0, -1.4f), true, false),
                ("crouch strafe", new Vector3(1.4f, 0, 0), true, false)
            };
            const float dt = 1f / 60;
            var cadences = new Dictionary<string, float>();
            var failures = new List<Exception>();
            try
            {
            foreach (var test in scenarios)
            {
                RecordCheck(role + " runtime " + test.Item1, failures, report, () => {
                instance.transform.position = new Vector3(1500, 20, 1500); instance.transform.rotation = Quaternion.identity;
                float maxError = 0, maxSlip = 0, minSole = float.PositiveInfinity, cadence = 0;
                for (int frame = 0; frame < 180; frame++)
                {
                    instance.transform.position += test.Item2 * dt;
                    preview.Invoke(motion, new object[] { test.Item2, test.Item3, test.Item4, dt, frame == 0 });
                    CheckChains(bones, rest, role + " runtime " + test.Item1);
                    float sole = SampleSoles(instance, skin, feet, role + " runtime " + test.Item1);
                    // Warmup includes clip blending and the first contact acquisition.
                    if (frame < 30) continue;
                    minSole = Mathf.Min(minSole, sole);
                    maxError = Mathf.Max(maxError, ReadMetric(motion, "FootContactError"));
                    maxSlip = Mathf.Max(maxSlip, ReadMetric(motion, "PlantedFootSlip"));
                    cadence += ReadMetric(motion, "LocomotionCadence");
                }
                cadences[test.Item1] = cadence / 150;
                Require(minSole >= -.012f, role + " runtime " + test.Item1 + " soles penetrate the floor: " + F(minSole));
                Require(maxError <= .065f, role + " runtime " + test.Item1 + " foot contact error " + F(maxError));
                Require(maxSlip <= .035f, role + " runtime " + test.Item1 + " planted foot slips " + F(maxSlip));
                report.Add(role + " runtime " + test.Item1 + ": 150 measured frames, min sole=" + F(minSole) + ", contact error=" + F(maxError) + ", planted slip=" + F(maxSlip) + " m, cadence=" + F(cadences[test.Item1]) + " steps/s.");
                });
            }
            if (cadences.ContainsKey("walk") && cadences.ContainsKey("slow walk") && cadences.ContainsKey("run"))
                RecordCheck(role + " speed/cadence", failures, report, () => Require(cadences["walk"] > cadences["slow walk"] * 1.4f && cadences["run"] > cadences["walk"] * 1.15f,
                    role + " runtime cadence does not respond to the actual walking/running speed."));
            ThrowFailures(role + " runtime gait scenarios", failures);
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
        }

        static float ReadMetric(DeadAirCharacterMotion motion, string name)
        {
            var property = typeof(DeadAirCharacterMotion).GetProperty(name, PublicInstance);
            Require(property != null, "Runtime contact diagnostic missing: " + name);
            float value = Convert.ToSingle(property.GetValue(motion), CultureInfo.InvariantCulture);
            Require(Finite(value) && value >= 0, "Runtime contact diagnostic invalid: " + name);
            return value;
        }

        static void ValidateEquipment(string role, GameObject instance, List<string> report)
        {
            var motion = instance.GetComponent<DeadAirCharacterMotion>();
            float largest = 0;
            var failures = new List<Exception>();
            foreach (string mode in new[] { "flashlight", "camera", "map", "personal", "keycard", "tape", "fuse", "fuel-canister" })
            foreach (float phase in new[] { 0f, .25f, .5f, .75f })
            {
                RecordCheck(role + " equipment " + mode + " phase " + phase, failures, report, () => {
                var actor = new PlayerState { characterClass = role, inventory = new List<string> { mode == "personal" || mode == "camera" || mode == "map" ? "flashlight" : mode },
                    selectedSlot = 0, camera = mode == "camera", mapOpen = mode == "map", uniqueItemEquipped = mode == "personal", cigarettesRemaining = 12 };
                motion.SetEquipmentPreview(actor, mode == "personal" ? "UseItem" : "Idle", phase);
                largest = Mathf.Max(largest, motion.GripContactError);
                Require(Finite(motion.GripContactError) && motion.GripContactError < .07f, role + " " + mode + " hand contact error: " + F(motion.GripContactError));
                });
            }
            report.Add(role + " equipment: 8 modes × 4 phases, maximum grip error=" + F(largest) + " m.");
            ThrowFailures(role + " equipment modes", failures);
        }

        [MenuItem("Dead Air/Capture Indie Character Remake")]
        public static void Capture()
        {
            const int layer = 29;
            var stage = new GameObject("Indie cast validation studio") { hideFlags = HideFlags.HideAndDontSave };
            stage.transform.position = new Vector3(1500, 20, 1500);
            var lightMasks = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(l => (l, l.cullingMask)).ToArray();
            bool oldFog = RenderSettings.fog; var oldAmbient = RenderSettings.ambientMode; var oldColor = RenderSettings.ambientLight;
            RenderTexture target = null; Material floorMaterial = null;
            var oldTarget = RenderTexture.active;
            try
            {
                Directory.CreateDirectory(OutputFolder);
                foreach (var pair in lightMasks) pair.l.cullingMask &= ~(1 << layer);
                RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.18f, .20f, .23f);
                var camera = new GameObject("Cast camera").AddComponent<Camera>(); camera.transform.SetParent(stage.transform, false);
                camera.enabled = false; camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.045f, .055f, .073f); camera.orthographic = true; camera.orthographicSize = 1.38f;
                camera.nearClipPlane = .05f; camera.farClipPlane = 20;
                target = new RenderTexture(1600, 800, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 }; target.Create(); camera.targetTexture = target;
                StudioLight(stage.transform, layer, new Vector3(-3, 4, 5), 1.0f, new Color(1, .87f, .75f));
                StudioLight(stage.transform, layer, new Vector3(3, 3, 2), .55f, new Color(.66f, .80f, 1));
                StudioLight(stage.transform, layer, new Vector3(0, 3, -4), .7f, new Color(.81f, .95f, 1));
                floorMaterial = new Material(Shader.Find("Dead Air/Source Surface")) { color = new Color(.16f, .18f, .21f) };
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(stage.transform, false); floor.layer = layer;
                floor.transform.localPosition = new Vector3(0, -.025f, 0); floor.transform.localScale = new Vector3(9, .05f, 6);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var actors = Roles.Select(role => UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role), stage.transform)).ToArray();
                for (int i = 0; i < actors.Length; i++)
                {
                    foreach (var t in actors[i].GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                    foreach (var skin in actors[i].GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
                    actors[i].transform.localPosition = new Vector3(-2.25f + i * 1.5f, 0, 0);
                    actors[i].GetComponent<DeadAirCharacterMotion>().SetPreview("Idle", .2f);
                }
                camera.transform.localPosition = new Vector3(0, 2.1f, 8); camera.transform.LookAt(stage.transform.position + Vector3.up * 1.02f);
                SaveFrame(camera, target, "lineup-front.png");
                foreach (var actor in actors) actor.transform.localRotation = Quaternion.Euler(0, -55, 0);
                SaveFrame(camera, target, "lineup-three-quarter.png");
                foreach (var actor in actors) actor.transform.localRotation = Quaternion.Euler(0, 90, 0);
                SaveFrame(camera, target, "lineup-side.png");
                foreach (var actor in actors) actor.SetActive(false);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                target = new RenderTexture(384, 480, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 }; target.Create(); camera.targetTexture = target;
                camera.orthographicSize = 1.15f; camera.transform.localPosition = new Vector3(0, 1.5f, 6); camera.transform.LookAt(stage.transform.position + Vector3.up * 1.02f);
                foreach (int i in Enumerable.Range(0, actors.Length))
                {
                    var actor = actors[i]; actor.SetActive(true); actor.transform.localPosition = Vector3.zero; actor.transform.localRotation = Quaternion.Euler(0, -25, 0);
                    var sheet = new Texture2D(1536, 1920, TextureFormat.RGB24, false);
                    try
                    {
                        var poses = new[] { "Walk", "Run", "CrouchWalk", "StrafeLeft" };
                        for (int row = 0; row < poses.Length; row++) for (int column = 0; column < 4; column++)
                        {
                            actor.GetComponent<DeadAirCharacterMotion>().SetPreview(poses[row], column * .25f);
                            camera.Render(); RenderTexture.active = target;
                            var tile = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                            try { tile.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); tile.Apply(); sheet.SetPixels(column * 384, (3 - row) * 480, 384, 480, tile.GetPixels()); }
                            finally { UnityEngine.Object.DestroyImmediate(tile); }
                        }
                        sheet.Apply(); File.WriteAllBytes(Path.Combine(OutputFolder, Roles[i] + "-gait-sheet.png"), sheet.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(sheet); actor.SetActive(false); }
                }
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 }; target.Create(); camera.targetTexture = target;
                floor.transform.localPosition = new Vector3(0, -.025f, 8); floor.transform.localScale = new Vector3(20, .05f, 40); Physics.SyncTransforms();
                CaptureLocomotionFrames(stage.transform, actors, camera, target);
                File.WriteAllText(Path.Combine(OutputFolder, "capture-report.txt"), "Unity prefab renders. Lineup order: Vivian/operator, Victor/engineer, Edgar/journalist, Hazel/dispatcher.\n" +
                    "Gait-sheet rows: Walk, Run, CrouchWalk, StrafeLeft; columns: cycle phase 0, .25, .5, .75.\n" +
                    "runtime-locomotion-24fps/frame-000.png through frame-071.png: 72 actual runtime contact-solver frames at 24 fps; walk 2.4 m/s, run 5 m/s, crouch 1.4 m/s, 24 frames each. The root moves and the camera follows it over a physical floor.\n" +
                    "These are visual evidence; quantitative runtime contacts are in validation-report.txt.\n");
                Debug.Log("INDIE CAST CAPTURES SAVED: " + Path.GetFullPath(OutputFolder));
            }
            finally
            {
                foreach (var pair in lightMasks) if (pair.l != null) pair.l.cullingMask = pair.cullingMask;
                RenderSettings.fog = oldFog; RenderSettings.ambientMode = oldAmbient; RenderSettings.ambientLight = oldColor;
                RenderTexture.active = oldTarget;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        static void CaptureLocomotionFrames(Transform stage, GameObject[] actors, Camera camera, RenderTexture target)
        {
            string folder = Path.Combine(OutputFolder, "runtime-locomotion-24fps"); Directory.CreateDirectory(folder);
            var method = typeof(DeadAirCharacterMotion).GetMethod("SetLocomotionPreview", PublicInstance);
            Require(method != null, "Missing runtime locomotion preview for the animation capture.");
            camera.orthographicSize = 1.65f;
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i].SetActive(true); actors[i].transform.localPosition = new Vector3(-2.25f + i * 1.5f, 0, 0);
                actors[i].transform.localRotation = Quaternion.identity;
            }
            float travelled = 0;
            for (int frame = 0; frame < 72; frame++)
            {
                bool running = frame >= 24 && frame < 48, crouching = frame >= 48;
                var velocity = Vector3.forward * (crouching ? 1.4f : running ? 5 : 2.4f);
                const float dt = 1f / 24;
                travelled += velocity.z * dt;
                foreach (var actor in actors)
                {
                    actor.transform.position += velocity * dt;
                    method.Invoke(actor.GetComponent<DeadAirCharacterMotion>(), new object[] { velocity, crouching, running, dt, frame == 0 });
                }
                camera.transform.localPosition = new Vector3(0, 2.1f, travelled + 8);
                camera.transform.LookAt(stage.position + new Vector3(0, 1.02f, travelled));
                SaveFrame(camera, target, "runtime-locomotion-24fps/frame-" + frame.ToString("D3") + ".png");
            }
        }

        static void StudioLight(Transform parent, int layer, Vector3 position, float intensity, Color color)
        {
            var light = new GameObject("Cast softbox").AddComponent<Light>(); light.transform.SetParent(parent, false);
            light.transform.localPosition = position; light.transform.LookAt(parent.position + Vector3.up);
            light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.cullingMask = 1 << layer; light.shadows = LightShadows.None;
        }

        static void SaveFrame(Camera camera, RenderTexture target, string filename)
        {
            camera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(OutputFolder, filename), pixels.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Matrix4x4 value) { for (int i = 0; i < 16; i++) if (!Finite(value[i])) return false; return true; }
        static string F(float value) => value.ToString("F5", CultureInfo.InvariantCulture);
        static void RecordCheck(string context, List<Exception> failures, List<string> report, Action check)
        {
            try { check(); }
            catch (Exception error)
            {
                if (error is TargetInvocationException invocation && invocation.InnerException != null) error = invocation.InnerException;
                report.Add("FAIL " + context + ": " + error.Message);
                failures.Add(new InvalidOperationException(context + ": " + error.Message, error));
            }
        }
        static void ThrowFailures(string context, List<Exception> failures)
        {
            if (failures.Count > 0) throw new AggregateException(context + ": " + failures.Count + " failed checks.", failures);
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Indie cast validation: " + message); }
    }
}
