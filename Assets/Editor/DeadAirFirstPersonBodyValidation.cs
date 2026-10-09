using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Checks the real owner-body adapter and its imported skinned surfaces.</summary>
    public static class DeadAirFirstPersonBodyValidation
    {
        static readonly string[] Roles = { "operator", "journalist", "engineer", "dispatcher" };

        [MenuItem("Dead Air/Validate First Person Body")]
        public static void Validate()
        {
            DeadAirGazeValidation.Validate();
            DeadAirCharacterFieldValidation.Validate();
            foreach (string role in Roles) ValidateRole(role);
            PlayerSettings.bundleVersion = DeadAirVersion.Current;
            AssetDatabase.SaveAssets();
            Debug.Log("DEAD AIR FIRST PERSON BODY PASSED: four actual owner bodies, authored atlas/material identity, clipped head/neck/arms, complete shadows, suppressed duplicate items/lights, finite standing/crouched/walk/run skins and supported source feet.");
        }

        static void ValidateRole(string role)
        {
            var prefab = Resources.Load<GameObject>("World/character_" + role);
            Require(prefab != null, role + " has no imported source prefab.");
            var authored = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(authored.Length == 1 && authored[0].sharedMesh != null, role + " needs exactly one supplied skinned source.");
            var atlas = authored[0].sharedMaterial != null ? authored[0].sharedMaterial.mainTexture : null;
            Require(atlas != null, role + " has no source texture atlas.");
            var owner = new GameObject("First person body validation / " + role);
            try
            {
                owner.transform.position = new Vector3(5, 2, -7);
                owner.transform.rotation = Quaternion.Euler(0, 180, 0);
                var body = owner.AddComponent<DeadAirFirstPersonBody>();
                var player = new PlayerState { id = "body-fixture", characterClass = role, clothingMask = -1,
                    inventory = new List<string> { "flashlight" }, flashlight = true, cigarettesRemaining = 12 };
                var state = new RoomState { phase = "playing", elapsed = DeadAirVanPresentation.ArrivalDuration + 1 };
                state.players.Add(player);
                body.Present(player, state, owner.transform, 0, -1.35f, false, false);
                Require(body.Avatar != null && body.Motion != null && body.Avatar.activeInHierarchy,
                    role + " actual Present failed to create a visible imported body.");
                Require(body.VisibleTriangles > 0 && body.HiddenTriangles > 0,
                    role + " needs both retained torso/leg triangles and removed head/arm triangles.");
                Require(body.Avatar.GetComponentsInChildren<Collider>(true).Length == 0,
                    role + " owner appearance added collision beside the player's capsule.");

                var visible = ActiveSkins(body.Avatar).Where(s => s.shadowCastingMode != ShadowCastingMode.ShadowsOnly).ToArray();
                var shadows = ActiveSkins(body.Avatar).Where(s => s.shadowCastingMode == ShadowCastingMode.ShadowsOnly).ToArray();
                Require(visible.Length == 1 && shadows.Length == 1,
                    role + " must draw one authored torso/leg surface and one complete shadow surface.");
                var surface = visible[0]; var shadow = shadows[0];
                Require(surface.shadowCastingMode == ShadowCastingMode.Off && !shadow.receiveShadows,
                    role + " clipped body/shadow modes would double-cast or draw the original head.");
                Require(shadow.sharedMesh != surface.sharedMesh && shadow.transform.parent == surface.transform,
                    role + " full shadow must share the source rig and renderer frame, not the clipped mesh.");
                Require(shadow.bones.SequenceEqual(surface.bones) && shadow.rootBone == surface.rootBone,
                    role + " complete shadow lost the source's actual animated bones.");
                Require(TriangleCount(surface.sharedMesh) == body.VisibleTriangles &&
                    TriangleCount(shadow.sharedMesh) == body.VisibleTriangles + body.HiddenTriangles &&
                    TriangleCount(shadow.sharedMesh) == TriangleCount(authored[0].sharedMesh),
                    role + " clipping counts or the full authored shadow topology are incomplete.");
                Require(surface.sharedMesh.vertexCount == authored[0].sharedMesh.vertexCount &&
                    shadow.sharedMesh.vertexCount == authored[0].sharedMesh.vertexCount &&
                    surface.sharedMesh.uv.SequenceEqual(authored[0].sharedMesh.uv),
                    role + " torso/legs lost their authored vertices or atlas UVs.");
                ValidateMaterials(role, surface, authored[0], atlas);
                ValidateMaterials(role, shadow, authored[0], atlas);
                Require(ForbiddenTriangleCount(surface, body.Avatar.transform) == 0,
                    role + " visible owner mesh retains triangles weighted to head, neck or source arms.");
                Require(ForbiddenTriangleCount(shadow, body.Avatar.transform) == body.HiddenTriangles,
                    role + " full shadow must preserve precisely the triangles clipped from the camera body.");

                Transform leftFoot = Find(body.Avatar, "foot.L"), rightFoot = Find(body.Avatar, "foot.R");
                Require(leftFoot != null && rightFoot != null, role + " lost source foot bones.");
                Vector3 standingLeft = Vector3.zero, standingRight = Vector3.zero;
                int poseIndex = 0;
                foreach (var fixture in new[] { new PoseFixture("standing", "Idle", false, false, 0),
                    new PoseFixture("crouched", "Idle", true, false, 0),
                    new PoseFixture("walk", "Walk", false, false, DeadAirLocomotion.WalkSpeed),
                    new PoseFixture("run", "Run", false, true, DeadAirLocomotion.RunSpeed) })
                {
                    state.elapsed += .1f;
                    player.crouching = fixture.crouching; player.sprinting = fixture.sprinting;
                    if (fixture.speed > 0) owner.transform.position += owner.transform.forward * (fixture.speed * .02f);
                    // Present exercises the runtime body, role/default clothes and root placement.
                    body.Present(player, state, owner.transform, 0, -1.35f, fixture.crouching, fixture.sprinting);
                    // Editor deltaTime need not advance. Sample the same public imported-pose
                    // pipeline deterministically on this actual owner instance, not a substitute.
                    body.Motion.SetEquipmentPreview(player, fixture.clip, .35f);
                    Require(body.Avatar.GetComponent<DeadAirAppearance>().CurrentMask == DeadAirClothing.DefaultMask(role),
                        role + " " + fixture.name + " changed the supplied outfit.");
                    Require(TriangleCount(surface.sharedMesh) == body.VisibleTriangles && ForbiddenTriangleCount(surface, body.Avatar.transform) == 0,
                        role + " " + fixture.name + " resurrected the owner's head or source arms.");
                    ValidateSuppressedEquipment(role, body.Avatar);
                    var bounds = BakeBounds(surface, owner.transform, role + " " + fixture.name);
                    var fullBounds = BakeBounds(shadow, owner.transform, role + " " + fixture.name + " full shadow");
                    Require(bounds.size.y > .6f && bounds.size.y < 2.7f && bounds.size.x < 2.2f && bounds.size.z < 2.3f &&
                        fullBounds.size.y > .8f && fullBounds.size.y < 3.1f,
                        role + " " + fixture.name + " produced an implausible detached skin: " + bounds);
                    Require(bounds.min.y > -.15f && bounds.min.y < .30f,
                        role + " " + fixture.name + " source soles left the owner's floor: " + bounds.min.y.ToString("F4"));
                    Vector3 left = owner.transform.InverseTransformPoint(leftFoot.position), right = owner.transform.InverseTransformPoint(rightFoot.position);
                    float raisedFootLimit = fixture.speed > 0 ? 1 : .55f;
                    Require(Finite(left) && Finite(right) && left.y > -.2f && right.y > -.2f && left.y < raisedFootLimit && right.y < raisedFootLimit,
                        role + " " + fixture.name + " has implausible source foot bones: " + left + " / " + right);
                    if (poseIndex == 0) { standingLeft = left; standingRight = right; }
                    if (fixture.crouching)
                        Require(Vector3.Distance(standingLeft, left) < .045f && Vector3.Distance(standingRight, right) < .045f,
                            role + " crouch failed to plant both source feet.");
                    poseIndex++;
                }

                // Equipment flags on the real owner adapter must continue using the existing FPS rig.
                player.camera = true; player.uniqueItemEquipped = true; player.uniqueActionCancelled = false;
                player.uniqueActionSequence = 1; player.uniqueActionAt = state.elapsed - 2.5f;
                player.clothingMask = 0;
                body.Present(player, state, owner.transform, 0, 0, false, false);
                Require(body.Avatar.GetComponent<DeadAirAppearance>().CurrentMask == DeadAirClothing.DefaultMask(role),
                    role + " old outfit data replaced its authored camera body.");
                ValidateSuppressedEquipment(role, body.Avatar);
                Require(TriangleCount(surface.sharedMesh) == body.VisibleTriangles, role + " item switch changed the clipped body topology.");
                player.down = true; body.Present(player, state, owner.transform, 0, 0, false, false);
                Require(!body.Avatar.activeSelf, role + " downed owner still drew its standing camera body.");
                player.down = false; player.hiding = "locker"; body.Present(player, state, owner.transform, 0, 0, false, false);
                Require(!body.Avatar.activeSelf, role + " hidden owner still drew its camera body.");
                Debug.Log("OWNER BODY VALIDATED: " + role + " retained=" + body.VisibleTriangles + " hidden=" + body.HiddenTriangles + " full-shadow=" + TriangleCount(shadow.sharedMesh) + " poses=standing,crouched,walk,run.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        readonly struct PoseFixture
        {
            public readonly string name, clip;
            public readonly bool crouching, sprinting;
            public readonly float speed;
            public PoseFixture(string name, string clip, bool crouching, bool sprinting, float speed)
            { this.name = name; this.clip = clip; this.crouching = crouching; this.sprinting = sprinting; this.speed = speed; }
        }

        static IEnumerable<SkinnedMeshRenderer> ActiveSkins(GameObject avatar) =>
            avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled && s.gameObject.activeInHierarchy);
        static int TriangleCount(Mesh mesh)
        {
            Require(mesh != null, "A body renderer has no mesh.");
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) count += (int)mesh.GetIndexCount(s) / 3;
            return count;
        }
        static void ValidateMaterials(string role, SkinnedMeshRenderer actual, SkinnedMeshRenderer source, Texture atlas)
        {
            Require(actual.sharedMaterials.SequenceEqual(source.sharedMaterials), role + " cloned or replaced the original body materials.");
            foreach (var material in actual.sharedMaterials)
                Require(material != null && material.mainTexture == atlas && material.shader != null && material.shader.name == "Dead Air/Source Surface",
                    role + " body material lost its original source atlas/surface shader.");
        }
        static int ForbiddenTriangleCount(SkinnedMeshRenderer renderer, Transform root)
        {
            var mesh = renderer.sharedMesh; var weights = mesh.boneWeights; var bones = renderer.bones;
            Require(weights.Length == mesh.vertexCount && mesh.bindposes.Length == bones.Length, renderer.name + " has inconsistent skin bindings.");
            var hidden = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                for (var bone = bones[i]; bone != null && bone != root; bone = bone.parent)
                    if (bone.name == "head" || bone.name == "neck" || bone.name.StartsWith("upper_arm.", StringComparison.Ordinal) ||
                        bone.name.StartsWith("forearm.", StringComparison.Ordinal) || bone.name.StartsWith("hand.", StringComparison.Ordinal))
                    { hidden[i] = true; break; }
            var forbidden = new bool[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                forbidden[i] = HiddenWeight(w.boneIndex0, w.weight0, hidden) + HiddenWeight(w.boneIndex1, w.weight1, hidden) +
                    HiddenWeight(w.boneIndex2, w.weight2, hidden) + HiddenWeight(w.boneIndex3, w.weight3, hidden) > .5f;
            }
            int count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetTriangles(s);
                for (int t = 0; t < indices.Length; t += 3)
                    if (forbidden[indices[t]] || forbidden[indices[t + 1]] || forbidden[indices[t + 2]]) count++;
            }
            return count;
        }
        static float HiddenWeight(int index, float weight, bool[] hidden)
        {
            Require(!float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= 0, "A source vertex has invalid skin weight.");
            Require(weight == 0 || index >= 0 && index < hidden.Length, "A source vertex references a missing bone.");
            return index >= 0 && index < hidden.Length && hidden[index] ? weight : 0;
        }
        static Bounds BakeBounds(SkinnedMeshRenderer renderer, Transform owner, string label)
        {
            var baked = new Mesh(); var bounds = new Bounds(); bool initialized = false;
            try
            {
                renderer.BakeMesh(baked); var vertices = baked.vertices;
                // Clipping keeps unused vertices. Inspect referenced indices, not mesh.bounds.
                foreach (int index in baked.triangles)
                {
                    var point = owner.InverseTransformPoint(renderer.transform.TransformPoint(vertices[index]));
                    Require(Finite(point), label + " baked a non-finite visible vertex.");
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
                Require(initialized, label + " has no referenced baked triangles.");
                return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        static void ValidateSuppressedEquipment(string role, GameObject avatar)
        {
            foreach (var renderer in avatar.GetComponentsInChildren<MeshRenderer>(true))
                Require(!renderer.enabled || !renderer.gameObject.activeInHierarchy, role + " camera body duplicated visible equipment: " + renderer.name);
            foreach (var light in avatar.GetComponentsInChildren<Light>(true))
                Require(light.name == "Subtle first person body bounce" ? light.cullingMask == 1 << 27 && light.shadows == LightShadows.None && light.intensity <= .5f :
                    !light.enabled || !light.gameObject.activeInHierarchy, role + " camera body duplicated an equipment light: " + light.name);
        }
        static Transform Find(GameObject avatar, string name)
        { foreach (var bone in avatar.GetComponentsInChildren<Transform>(true)) if (bone.name == name) return bone; return null; }
        static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("First person body validation: " + message); }
    }
}
