using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    /// <summary>In-memory fixtures only. Does not import, save or replace character assets.</summary>
    public static class DeadAirWardrobeValidation
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Roles = { "operator", "engineer", "journalist", "dispatcher" };

        [MenuItem("Dead Air/Validate Explicit Character Wardrobe")]
        public static void Validate()
        {
            foreach (string role in Roles)
            {
                ValidateMasks(role);
                ValidatePresentation(role);
            }
            ValidateInvalidBindings();
            ValidateSecondaryMotion();
            ValidateFirstPersonSleeves();
            ValidateSnapshot();
            Debug.Log("DEAD AIR EXPLICIT WARDROBE PASSED: four role defaults, zero/combined masks, one-piece rules, unchanged complete body geometry/UVs/weights/bones, invalid-binding rejection, FP/TP state and shadow visibility, configured secondary motion, explicit FP sleeve catalogs/class switches, and preserved/sanitized network snapshot masks. In-memory fixtures only; no character assets created or changed.");
        }

        static void ValidateMasks(string role)
        {
            using (var fixture = new Fixture(role))
            {
                Require(fixture.Wardrobe.ValidateBindings(out string error), error);
                DeadAirClothing.Apply(fixture.Actor, -1, role);
                int defaults = DeadAirClothing.DefaultMask(role);
                Require(fixture.Wardrobe.CurrentMask == defaults && fixture.Shirt.enabled && fixture.Shoes.enabled &&
                    fixture.Jacket.enabled == ((defaults & DeadAirClothing.Jacket) != 0), role + " default mask differs.");
                Require(fixture.Actor.GetComponent<DeadAirAppearance>() == null, "Explicit wardrobe added the legacy body classifier.");
                DeadAirClothing.Apply(fixture.Actor, 0, role);
                Require(fixture.Body.enabled && !fixture.Shirt.enabled && !fixture.Jacket.enabled && !fixture.Shoes.enabled,
                    role + " mask zero removed the body or retained a garment.");
                Require(fixture.Shirt.gameObject.activeSelf && fixture.Bone.gameObject.activeSelf,
                    "Garment removal deactivated scene objects or bones.");
                DeadAirClothing.Apply(fixture.Actor, DeadAirClothing.Shirt | DeadAirClothing.Shoes | 512, role);
                Require(fixture.Shirt.enabled && fixture.Shoes.enabled && !fixture.Jacket.enabled &&
                    fixture.Wardrobe.CurrentMask == (DeadAirClothing.Shirt | DeadAirClothing.Shoes), "Combined mask was not sanitized/applied.");

                fixture.Wardrobe.ConfigureBindings(role, new Renderer[] { fixture.Body }, new[] {
                    new DeadAirCharacterWardrobe.GarmentBinding { id = "one-piece", requiredMask = DeadAirClothing.Shirt | DeadAirClothing.Trousers,
                        renderers = new Renderer[] { fixture.Shirt } },
                    new DeadAirCharacterWardrobe.GarmentBinding { id = "either-layer-detail", requiredMask = DeadAirClothing.Shirt | DeadAirClothing.Jacket,
                        match = DeadAirCharacterWardrobe.MaskMatch.Any, renderers = new Renderer[] { fixture.Jacket } }
                });
                fixture.Wardrobe.Apply(DeadAirClothing.Shirt, role);
                Require(!fixture.Shirt.enabled && fixture.Jacket.enabled, "Compound garment rules ignored All/Any matching.");
                fixture.Wardrobe.Apply(DeadAirClothing.Shirt | DeadAirClothing.Trousers, role);
                Require(fixture.Shirt.enabled, "A complete compound outfit did not enable its one-piece garment.");
                for (int i = 0; i < 10; i++) fixture.Wardrobe.Apply(i % 2 == 0 ? 0 : -1, role);
                fixture.RequireBodyUnchanged();
            }
        }

        static void ValidateInvalidBindings()
        {
            using (var fixture = new Fixture("operator"))
            {
                var bodyEntry = new DeadAirCharacterWardrobe.GarmentBinding { id = "invalid-body", requiredMask = DeadAirClothing.Shirt,
                    renderers = new Renderer[] { fixture.Body } };
                fixture.Wardrobe.ConfigureBindings("operator", new Renderer[] { fixture.Body }, new[] { bodyEntry });
                Require(!fixture.Wardrobe.ValidateBindings(out _), "A garment was allowed to point at the complete body.");
                fixture.Body.enabled = false;
                int warnings = 0;
                Application.LogCallback observe = (message, stack, type) => { if (type == LogType.Warning && message.StartsWith("Character wardrobe on '", StringComparison.Ordinal)) warnings++; };
                Application.logMessageReceived += observe;
                try
                {
                    DeadAirClothing.Apply(fixture.Actor, 0, "operator");
                    DeadAirClothing.Apply(fixture.Actor, 0, "operator");
                }
                finally { Application.logMessageReceived -= observe; }
                Require(warnings == 1 && fixture.Body.enabled && fixture.Shirt.enabled && fixture.Wardrobe.CurrentMask == -2,
                    "Invalid bindings changed garments, hid the body or repeated the same warning.");
                Require(fixture.Actor.GetComponent<DeadAirAppearance>() == null, "Invalid catalog fell back to the legacy body classifier.");

                var meshAlias = fixture.AddSurface("body mesh alias", fixture.Body.sharedMesh);
                ExpectInvalid(fixture, new DeadAirCharacterWardrobe.GarmentBinding { id = "alias", requiredMask = 1, renderers = new Renderer[] { meshAlias } });
                ExpectInvalid(fixture, new DeadAirCharacterWardrobe.GarmentBinding { id = "root", requiredMask = 1, groups = new[] { fixture.Actor } });
                ExpectInvalid(fixture, new DeadAirCharacterWardrobe.GarmentBinding { id = "rig", requiredMask = 1, groups = new[] { fixture.Bone.gameObject } });
                ExpectInvalid(fixture, new DeadAirCharacterWardrobe.GarmentBinding { id = "empty", requiredMask = 1 });
                ExpectInvalid(fixture, new DeadAirCharacterWardrobe.GarmentBinding { id = "unknown bits", requiredMask = 512, renderers = new Renderer[] { fixture.Shirt } });
                using (var other = new Fixture("engineer"))
                    ExpectInvalid(fixture, new DeadAirCharacterWardrobe.GarmentBinding { id = "foreign", requiredMask = 1, renderers = new Renderer[] { other.Shirt } });
                var first = new DeadAirCharacterWardrobe.GarmentBinding { id = "first", requiredMask = 1, renderers = new Renderer[] { fixture.Shirt } };
                var duplicate = new DeadAirCharacterWardrobe.GarmentBinding { id = "second", requiredMask = 2, renderers = new Renderer[] { fixture.Shirt } };
                fixture.Wardrobe.ConfigureBindings("operator", new Renderer[] { fixture.Body }, new[] { first, duplicate });
                Require(!fixture.Wardrobe.ValidateBindings(out _), "Two entries were allowed to fight over a renderer.");
                fixture.RequireBodyUnchanged();
            }
        }

        static void ExpectInvalid(Fixture fixture, DeadAirCharacterWardrobe.GarmentBinding binding)
        {
            fixture.Wardrobe.ConfigureBindings("operator", new Renderer[] { fixture.Body }, new[] { binding });
            Require(!fixture.Wardrobe.ValidateBindings(out _), "Invalid binding accepted: " + binding.id);
        }

        static void ValidatePresentation(string role)
        {
            using (var fixture = new Fixture(role))
            {
                var motion = fixture.Actor.AddComponent<DeadAirCharacterMotion>();
                motion.ClassId = role;
                var player = new PlayerState { characterClass = role, clothingMask = 0, flashlight = false };
                motion.Present(player, 0, true, false);
                Require(fixture.Wardrobe.CurrentMask == 0 && !fixture.Shirt.enabled, role + " TP discarded the snapshot mask.");
                player.clothingMask = DeadAirClothing.Shirt;
                motion.SetEquipmentPreview(player);
                Require(fixture.Shirt.enabled && !fixture.Shoes.enabled, "Equipment preview replaced the selected outfit.");

                var owner = new GameObject("Wardrobe owner fixture");
                try
                {
                    fixture.Actor.transform.SetParent(owner.transform, false);
                    var firstPerson = owner.AddComponent<DeadAirFirstPersonBody>();
                    // Inject an in-memory avatar into the real owner adapter; no Resources asset is manufactured or modified.
                    Set(firstPerson, "avatar", fixture.Actor); Set(firstPerson, "role", role); Set(firstPerson, "motion", motion);
                    Set(firstPerson, "authoredCast", true);
                    var bounce = new GameObject("Fixture owner bounce"); bounce.transform.SetParent(owner.transform);
                    Set(firstPerson, "bodyBounce", bounce.AddComponent<Light>());
                    var surfaces = (List<SkinnedMeshRenderer>)Get(firstPerson, "ownerSurfaces");
                    var shadows = (List<SkinnedMeshRenderer>)Get(firstPerson, "ownerShadows");
                    surfaces.Add(fixture.Body); surfaces.Add(fixture.Shirt);
                    var bodyShadow = fixture.AddSurface("body shadow", fixture.Body.sharedMesh);
                    var shirtShadow = fixture.AddSurface("shirt shadow", fixture.Shirt.sharedMesh);
                    shadows.Add(bodyShadow); shadows.Add(shirtShadow);
                    motion.SetFirstPersonPresentation(true);
                    var state = new RoomState { phase = "playing", paused = true };
                    player.clothingMask = 0;
                    firstPerson.Present(player, state, owner.transform, 0, 0, false, false);
                    Require(fixture.Wardrobe.CurrentMask == 0 && !fixture.Shirt.enabled && !shirtShadow.enabled && bodyShadow.enabled,
                        role + " owner reset the mask or retained the removed garment's shadow.");
                    player.clothingMask = DeadAirClothing.Shirt;
                    firstPerson.Present(player, state, owner.transform, 0, 0, false, false);
                    Require(fixture.Shirt.enabled && shirtShadow.enabled && !fixture.Shoes.enabled,
                        role + " owner failed to restore a previously hidden garment/shadow.");
                    fixture.RequireBodyUnchanged();
                }
                finally { UnityEngine.Object.DestroyImmediate(owner); }
            }
        }

        static void ValidateSecondaryMotion()
        {
            using (var fixture = new Fixture("engineer"))
            {
                var secondary = fixture.Actor.AddComponent<DeadAirSecondaryMotion>();
                secondary.hair = new GameObject("Explicit fixture hair control").transform;
                secondary.chest = new GameObject("Explicit fixture soft tissue control").transform;
                secondary.hair.SetParent(fixture.Actor.transform, false); secondary.chest.SetParent(fixture.Actor.transform, false);
                var motion = fixture.Actor.AddComponent<DeadAirCharacterMotion>(); motion.ClassId = "engineer";
                fixture.Wardrobe.Apply(0, "engineer");
                for (int i = 0; i < 20; i++) motion.SetLocomotionPreview(Vector3.forward * 2, false, false, .016f, i == 0);
                Require(secondary.ChestDisplacement > 0 && Quaternion.Angle(secondary.hair.localRotation, Quaternion.identity) > .01f,
                    "Explicit wardrobe disconnected a separately configured secondary-motion component.");
                Require(!(bool)Get(secondary, "lastClothed"), "Secondary motion ignored the explicit unclothed mask.");
                Quaternion expected = secondary.hair.localRotation;
                secondary.hair.localRotation = Quaternion.identity;
                motion.ReapplyGroundContacts();
                Require(Quaternion.Angle(secondary.hair.localRotation, expected) < .001f, "Ground-contact reapplication lost explicit secondary motion.");
                fixture.Wardrobe.Apply(DeadAirClothing.Shirt, "engineer");
                motion.SetLocomotionPreview(Vector3.forward * 2, false, false, .016f);
                Require((bool)Get(secondary, "lastClothed"), "Secondary motion did not receive the selected clothing support state.");
                motion.ResetLocomotionContacts();
                Require(secondary.ChestDisplacement == 0 && secondary.chest.localPosition == Vector3.zero &&
                    Quaternion.Angle(secondary.hair.localRotation, Quaternion.identity) < .001f, "Contact reset left stale explicit secondary offsets.");
                motion.Present(new PlayerState { characterClass = "engineer", clothingMask = 0, flashlight = false }, 0, true, false);
                Require(!(bool)Get(secondary, "lastClothed") && fixture.Actor.GetComponent<DeadAirAppearance>() == null,
                    "Snapshot secondary evaluation required the legacy appearance component or used its default mask.");
                fixture.RequireBodyUnchanged();
            }
        }

        static void ValidateFirstPersonSleeves()
        {
            using (var first = new Fixture("operator"))
            using (var second = new Fixture("engineer"))
            {
                var owner = new GameObject("Explicit FP sleeve fixture");
                try
                {
                    first.Actor.transform.SetParent(owner.transform, false);
                    second.Actor.transform.SetParent(owner.transform, false);
                    var overlay = owner.AddComponent<DeadAirFirstPersonRig>();
                    Set(overlay, "firstPersonWardrobes", new[] { first.Wardrobe, second.Wardrobe });
                    var apply = typeof(DeadAirFirstPersonRig).GetMethod("ApplySleeves", PrivateInstance);
                    var player = new PlayerState { characterClass = "operator", clothingMask = 0 };
                    // Equipment reset/one-hand poses hide parents before ApplySleeves. Hidden
                    // objects are still valid bindings; wardrobe must not reactivate them.
                    first.Actor.SetActive(false); first.Shirt.gameObject.SetActive(false); first.Body.gameObject.SetActive(false);
                    apply.Invoke(overlay, new object[] { player });
                    Require(!first.Shirt.enabled && first.Body.enabled && second.Shirt.enabled, "FP sleeves ignored the selected role or zero mask.");
                    Require(!first.Actor.activeSelf && !first.Shirt.gameObject.activeSelf && !first.Body.gameObject.activeSelf,
                        "FP wardrobe changed externally controlled hand/skin visibility.");
                    first.Actor.SetActive(true); first.Shirt.gameObject.SetActive(true); first.Body.gameObject.SetActive(true);
                    first.Shirt.enabled = true;
                    apply.Invoke(overlay, new object[] { player });
                    Require(!first.Shirt.enabled, "An unchanged FP mask failed to reapply after another presentation pass.");
                    player.characterClass = "engineer";
                    second.Shirt.gameObject.SetActive(false);
                    apply.Invoke(overlay, new object[] { player });
                    Require(!second.Shirt.enabled && second.Body.enabled, "A class switch with the same numeric mask reused the old FP catalog.");
                    Require(!second.Shirt.gameObject.activeSelf, "FP wardrobe overrode an inactive equipment hand.");
                    second.Shirt.gameObject.SetActive(true);
                    player.clothingMask = DeadAirClothing.Shirt;
                    apply.Invoke(overlay, new object[] { player });
                    Require(second.Shirt.enabled && !second.Shoes.enabled && second.Body.enabled, "Explicit FP sleeve restoration failed.");
                    first.RequireBodyUnchanged(); second.RequireBodyUnchanged();
                    // An unconfigured legacy overlay records state, but cannot turn its combined forearm mesh into bare skin.
                    Set(overlay, "firstPersonWardrobes", Array.Empty<DeadAirCharacterWardrobe>());
                    Set(overlay, "currentClass", "engineer"); player.clothingMask = 0;
                    apply.Invoke(overlay, new object[] { player });
                    Require((int)Get(overlay, "currentClothing") == 0, "Legacy overlay replaced the requested mask with the class default.");
                }
                finally { UnityEngine.Object.DestroyImmediate(owner); }
            }
        }

        static void ValidateSnapshot()
        {
            var host = new RoomState();
            foreach (int mask in new[] { -1, 0, 9, 31, 520, -42 })
                host.players.Add(new PlayerState { characterClass = "engineer", clothingMask = mask });
            var owner = new GameObject("Wardrobe snapshot fixture");
            try
            {
                var session = owner.AddComponent<DeadAirSession>();
                // A null peer matches this unconnected fixture's server field. Exercise the actual receive/deserialize path.
                typeof(DeadAirSession).GetMethod("ClientPacket", PrivateInstance).Invoke(session, new object[] {
                    null, new NetPacket { type = "state", stateJson = JsonUtility.ToJson(host) }
                });
                Require(session.State != null && session.State.players.Select(p => p.clothingMask).SequenceEqual(new[] { -1, 0, 9, 31, 8, -1 }),
                    "Snapshot ingestion replaced valid wardrobe masks or failed to sanitize invalid bits.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        static object Get(object target, string name) => target.GetType().GetField(name, PrivateInstance).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Explicit wardrobe validation: " + message); }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject Actor;
            public readonly Transform Bone;
            public readonly DeadAirCharacterWardrobe Wardrobe;
            public readonly SkinnedMeshRenderer Body, Shirt, Jacket, Shoes;
            readonly List<Mesh> meshes = new List<Mesh>();
            readonly Mesh original;
            readonly Vector3[] vertices;
            readonly Vector2[] uvs;
            readonly BoneWeight[] weights;
            readonly int[] triangles;
            readonly Matrix4x4[] bindposes;
            readonly Transform[] bones;

            public Fixture(string role)
            {
                Actor = new GameObject("Wardrobe fixture " + role);
                Bone = new GameObject("Fixture body bone").transform; Bone.SetParent(Actor.transform, false);
                Body = AddSurface("Complete body", MakeMesh());
                Shirt = AddSurface("Top", MakeMesh()); Jacket = AddSurface("Outer layer", MakeMesh()); Shoes = AddSurface("Footwear", MakeMesh());
                original = Body.sharedMesh; vertices = original.vertices; triangles = original.triangles;
                uvs = original.uv; weights = original.boneWeights;
                bindposes = original.bindposes; bones = Body.bones;
                Wardrobe = Actor.AddComponent<DeadAirCharacterWardrobe>();
                Wardrobe.ConfigureBindings(role, new Renderer[] { Body }, new[] {
                    new DeadAirCharacterWardrobe.GarmentBinding { id = "top", requiredMask = DeadAirClothing.Shirt, groups = new[] { Shirt.gameObject } },
                    new DeadAirCharacterWardrobe.GarmentBinding { id = "outer", requiredMask = DeadAirClothing.Jacket, renderers = new Renderer[] { Jacket } },
                    new DeadAirCharacterWardrobe.GarmentBinding { id = "footwear", requiredMask = DeadAirClothing.Shoes, renderers = new Renderer[] { Shoes } }
                });
            }

            Mesh MakeMesh()
            {
                var mesh = new Mesh { name = "In-memory wardrobe triangle fixture" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity };
                mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
                meshes.Add(mesh); return mesh;
            }

            public SkinnedMeshRenderer AddSurface(string name, Mesh mesh)
            {
                var child = new GameObject(name); child.transform.SetParent(Actor.transform, false);
                var skin = child.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
                skin.bones = new[] { Bone }; skin.rootBone = Bone; return skin;
            }

            public void RequireBodyUnchanged()
            {
                Require(Body.enabled && Body.sharedMesh == original && original.vertices.SequenceEqual(vertices) &&
                    original.uv.SequenceEqual(uvs) && original.boneWeights.SequenceEqual(weights) &&
                    original.triangles.SequenceEqual(triangles) && original.bindposes.SequenceEqual(bindposes) && Body.bones.SequenceEqual(bones),
                    "Wardrobe changes altered the complete body mesh, topology, UVs, weights, bind poses or rig references.");
            }

            public void Dispose()
            {
                if (Actor != null) UnityEngine.Object.DestroyImmediate(Actor);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
