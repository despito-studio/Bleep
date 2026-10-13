using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    /// <summary>Exercises weighted imported bones, including repeated animation samples and owner rendering.</summary>
    public static class DeadAirSecondaryMotionValidation
    {
        [MenuItem("Dead Air/Validate Restored Cast Secondary Physics")]
        public static void Validate()
        {
            var report = new List<string>();
            var errors = new List<Exception>();
            foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
            {
                try { ValidateRole(role, report); }
                catch (Exception error) { report.Add("FAIL " + role + ": " + error.Message); errors.Add(error); }
            }
            Directory.CreateDirectory("Art/Characters/RestoredCast/Unity");
            File.WriteAllLines("Art/Characters/RestoredCast/Unity/secondary-motion-report.txt", report);
            if (errors.Count > 0) throw new AggregateException("Restored secondary physics failed", errors);
            Debug.Log("RESTORED SECONDARY PHYSICS PASSED: weighted deform bones, real movement, bounded spring tips, idempotent reapply, pause, teleport, stall and owner-body integration.");
        }

        static void ValidateRole(string role, List<string> report)
        {
            var prefab = Resources.Load<GameObject>("World/character_" + role);
            Require(prefab != null, role + " prefab missing");
            var avatar = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var motion = avatar.GetComponent<DeadAirCharacterMotion>() ?? avatar.AddComponent<DeadAirCharacterMotion>();
                motion.ClassId = role;
                motion.SetPreview("Idle");
                DeadAirClothing.Apply(avatar, -1, role);
                var appearance = avatar.GetComponent<DeadAirAppearance>();
                var spring = appearance == null ? null : appearance.SecondaryMotion;
                Require(spring != null && spring.UsesAuthoredBones, role + " has no active authored secondary physics");
                bool female = role == "operator" || role == "dispatcher";
                Require(spring.ClothBoneCount > 0, role + " has no clothing chain");
                Require(role == "engineer" || spring.HairBoneCount > 0, role + " has no hair/beard chain");
                Require(!female || spring.BreastBoneCount == 2, role + " requires both weighted breast bones");
                Require(Find(avatar, "Secondary hair") == null, role + " incorrectly added legacy proxy hair");
                var weights = CountWeightedVertices(avatar);
                Require(weights[1] >= 12, role + " clothing bones do not deform a meaningful garment surface");
                Require(role == "engineer" || weights[0] >= 12, role + " hair bones have no weighted hair surface");
                Require(!female || weights[2] >= 12, role + " breast bones have no weighted surface");
                if (role == "engineer") ValidateAnimatedClothBase(avatar, motion, spring, report);
                var peakGeometryMotion = new float[3];
                float maxAngle = 0, maxBreast = 0, maxPenetration = 0;
                const float dt = 1f / 60;
                for (int frame = 0; frame < 240; frame++)
                {
                    float t = frame * dt;
                    avatar.transform.position = new Vector3(.19f * Mathf.Sin(t * 3.1f), .018f * Mathf.Sin(t * 10), t * 1.4f);
                    avatar.transform.rotation = Quaternion.Euler(0, 28 * Mathf.Sin(t * 2.4f), 0);
                    motion.SetPreview(frame < 120 ? "Walk" : "CrouchWalk", Mathf.Repeat(t * 1.3f, 1));
                    var before = frame % 12 == 0 ? Bake(avatar) : null;
                    spring.Step(dt, 0, 2.4f, true, frame == 0);
                    maxAngle = Mathf.Max(maxAngle, spring.MaximumAngle);
                    maxBreast = Mathf.Max(maxBreast, spring.ChestDisplacement);
                    maxPenetration = Mathf.Max(maxPenetration, spring.MaximumCollisionPenetration);
                    if (before != null) MeasureGeometryMotion(avatar, before, peakGeometryMotion, role, frame);
                    foreach (var bone in SecondaryBones(avatar))
                        Require(Finite(bone.localPosition) && Finite(bone.localRotation), role + " non-finite secondary bone " + bone.name);
                }
                Require(maxAngle <= 26.02f, role + " secondary cone exceeded: " + F(maxAngle));
                Require(maxBreast <= .0061f, role + " clothed breast displacement exceeded: " + F(maxBreast));
                Require(maxPenetration <= .012f, role + " body collision penetration: " + F(maxPenetration));
                Require(peakGeometryMotion[1] > .0005f, role + " cloth is weighted but does not actually move");
                Require(role == "engineer" || peakGeometryMotion[0] > .0005f, role + " hair is weighted but does not actually move");
                Require(!female || peakGeometryMotion[2] > .0001f, role + " chest surface has no visible secondary deformation");

                var dynamicPose = Pose(avatar);
                for (int i = 0; i < 100; i++) spring.ReapplyPose();
                ComparePose(dynamicPose, role + " repeated reapply accumulates offsets");
                spring.Step(0, 4, 8, true);
                ComparePose(dynamicPose, role + " pause changed the spring pose");
                // SamplePose restores all skeletal transforms; the second presentation
                // pass must restore the existing spring offset, without another timestep.
                motion.SetPreview("CrouchWalk", Mathf.Repeat(239 * dt * 1.3f, 1));
                spring.ReapplyPose();
                ComparePose(dynamicPose, role + " sample then reapply lost secondary pose");
                avatar.transform.position += new Vector3(100, 20, -60);
                spring.Step(dt, 0, 0, true);
                Require(spring.MaximumAngle < .02f && spring.ChestDisplacement < .00001f, role + " teleport caused a physics impulse");
                spring.Step(2, 0, 0, true);
                Require(spring.MaximumAngle < .02f && spring.ChestDisplacement < .00001f, role + " long stall caused a physics impulse");
                spring.ResetMotion();
                spring.ReapplyPose();
                Require(spring.MaximumAngle == 0 && spring.ChestDisplacement == 0, role + " reset kept stale dynamics");
                report.Add("PASS " + role + ": bones=" + spring.BoneCount + ", weighted vertices hair/cloth/breast=" +
                    string.Join("/", weights) + ", peak actual mesh displacement=" + F(peakGeometryMotion[0]) + "/" + F(peakGeometryMotion[1]) + "/" + F(peakGeometryMotion[2]) +
                    " m, max angle=" + F(maxAngle) + " deg, breast=" + F(maxBreast) + " m, collision penetration=" + F(maxPenetration) + " m; reapply/pause/teleport/stall/reset passed.");
                ValidateOwner(role, report);
            }
            finally { UnityEngine.Object.DestroyImmediate(avatar); }
        }

        static void ValidateAnimatedClothBase(GameObject avatar, DeadAirCharacterMotion motion,
            DeadAirSecondaryMotion spring, List<string> report)
        {
            motion.SetPreview("Idle", .35f);
            var standing = Pose(avatar);
            motion.SetPreview("CrouchWalk", .35f);
            var crouched = Pose(avatar);
            float authoredAngle = 0;
            foreach (var rest in crouched)
                if (rest.bone.name.StartsWith("secondary_cloth.tie.", StringComparison.Ordinal))
                    authoredAngle = Mathf.Max(authoredAngle, Quaternion.Angle(rest.rotation,
                        standing.Find(value => value.bone == rest.bone).rotation));
            Require(authoredAngle > 1, "Engineer crouch clip has no authored tie corrective pose");
            spring.Step(0, 0, 0, true, true);
            ComparePose(crouched, "Secondary reset erased the authored crouch tie pose");
            for (int repeat = 0; repeat < 10; repeat++) spring.ReapplyPose();
            spring.Step(0, 0, 0, true);
            ComparePose(crouched, "Secondary reapply/pause erased the authored crouch tie pose");
            motion.SetPreview("Idle", .35f);
            spring.ReapplyPose();
            ComparePose(standing, "Secondary reapply retained the previous clip's tie base");
            motion.SetPreview("CrouchWalk", .35f);
            spring.ResetMotion();
            spring.ReapplyPose();
            ComparePose(crouched, "ResetMotion reverted the newly sampled crouch corrective pose");
            report.Add("PASS engineer authored tie correction=" + F(authoredAngle) + " deg survives reset, pause, repeated reapply and clip changes.");
        }

        static void ValidateOwner(string role, List<string> report)
        {
            var holder = new GameObject("Owner secondary validation");
            try
            {
                var body = holder.AddComponent<DeadAirFirstPersonBody>();
                var state = new RoomState { phase = "playing", elapsed = 20 };
                var player = new PlayerState { characterClass = role, clothingMask = -1 };
                body.Present(player, state, holder.transform, 0, 0, false, false);
                Require(body.Avatar != null, role + " owner body missing");
                var spring = body.Avatar.GetComponent<DeadAirAppearance>().SecondaryMotion;
                Require(spring != null && spring.UsesAuthoredBones, role + " owner body physics is disconnected");
                bool shadow = false;
                foreach (var skin in body.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) shadow = true;
                    Require(skin.bones.Length > 0, role + " owner skin has no bone bindings");
                }
                Require(shadow, role + " owner character has no full animated shadow");
                body.Avatar.transform.position += Vector3.forward * .045f;
                spring.Step(1f / 60, 0, 2.4f, true);
                foreach (var bone in SecondaryBones(body.Avatar)) Require(Finite(bone.localRotation), role + " owner secondary rotation invalid");
                report.Add("PASS " + role + " owner: active authored solver and shared weighted shadow bones.");
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        static Dictionary<SkinnedMeshRenderer, Vector3[]> Bake(GameObject avatar)
        {
            var values = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
            foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = new Mesh();
                try { skin.BakeMesh(mesh); values.Add(skin, mesh.vertices); }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            return values;
        }

        static void MeasureGeometryMotion(GameObject avatar, Dictionary<SkinnedMeshRenderer, Vector3[]> before, float[] peak, string role, int frame)
        {
            var after = Bake(avatar);
            foreach (var pair in before)
            {
                var skin = pair.Key; var weights = skin.sharedMesh.boneWeights; var bones = skin.bones;
                Vector3[] end = after[skin];
                for (int v = 0; v < pair.Value.Length; v++)
                {
                    float distance = Vector3.Distance(skin.transform.TransformPoint(pair.Value[v]), skin.transform.TransformPoint(end[v]));
                    Require(!float.IsNaN(distance) && distance < .5f, role + " secondary deformation detached " + skin.name +
                        " at frame " + frame + ", vertex " + v + ": displacement=" + F(distance) + " m");
                    int kind = WeightKind(weights[v], bones);
                    if (kind >= 0) peak[kind] = Mathf.Max(peak[kind], distance);
                }
            }
        }

        static int[] CountWeightedVertices(GameObject avatar)
        {
            var result = new int[3];
            foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var weight in skin.sharedMesh.boneWeights)
                { int kind = WeightKind(weight, skin.bones); if (kind >= 0) result[kind]++; }
            return result;
        }

        static int WeightKind(BoneWeight weight, Transform[] bones)
        {
            int kind = Kind(bones, weight.boneIndex0, weight.weight0);
            if (kind < 0) kind = Kind(bones, weight.boneIndex1, weight.weight1);
            if (kind < 0) kind = Kind(bones, weight.boneIndex2, weight.weight2);
            if (kind < 0) kind = Kind(bones, weight.boneIndex3, weight.weight3);
            return kind;
        }
        static int Kind(Transform[] bones, int index, float weight)
        {
            if (weight < .03f || index < 0 || index >= bones.Length || bones[index] == null) return -1;
            string name = bones[index].name;
            return name.StartsWith("secondary_hair.") ? 0 : name.StartsWith("secondary_cloth.") ? 1 : name.StartsWith("secondary_breast.") ? 2 : -1;
        }
        struct Rest { public Transform bone; public Vector3 position; public Quaternion rotation; }
        static List<Rest> Pose(GameObject avatar)
        {
            var result = new List<Rest>();
            foreach (var bone in SecondaryBones(avatar)) result.Add(new Rest { bone = bone, position = bone.localPosition, rotation = bone.localRotation });
            return result;
        }
        static void ComparePose(List<Rest> pose, string message)
        {
            foreach (var rest in pose)
                Require(Vector3.Distance(rest.position, rest.bone.localPosition) < .000001f && Quaternion.Angle(rest.rotation, rest.bone.localRotation) < .05f, message);
        }
        static IEnumerable<Transform> SecondaryBones(GameObject avatar)
        {
            foreach (var bone in avatar.GetComponentsInChildren<Transform>(true))
                if (bone.name.StartsWith("secondary_") && !bone.name.EndsWith("_tip")) yield return bone;
        }
        static Transform Find(GameObject avatar, string name)
        { foreach (var t in avatar.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);
        static bool Finite(Quaternion q) => !float.IsNaN(q.x + q.y + q.z + q.w) && !float.IsInfinity(q.x + q.y + q.z + q.w);
        static string F(float n) => n.ToString("F5", CultureInfo.InvariantCulture);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
