using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Joint = DeadAir.DeadAirCharacterRigBindings.Joint;

namespace DeadAir.Editor
{
    /// <summary>In-memory contract/playback fixtures; does not certify any authored anatomy or imported FBX.</summary>
    public static class DeadAirV2RuntimeValidation
    {
        [MenuItem("Dead Air/Production V2/Validate Runtime Support")]
        public static void Validate()
        {
            ValidateExplicitRig();
            ValidatePlayback();
            ValidateImportGuards();
            ValidateSnapshotBridge();
            ValidateOwnerBody();
            ValidateViewAimAndLightOwnership();
            ValidateGuestVisualClock();
            foreach (string name in new[] { "Dead Air/Character Surface V2", "Dead Air/Character Sheer V2" })
            {
                var shader = Shader.Find(name);
                Require(shader != null && !ShaderUtil.ShaderHasError(shader), "V2 shader import failed: " + name);
            }
            DeadAirWardrobeValidation.Validate();
            Debug.Log("DEAD AIR V2 RUNTIME SUPPORT PASSED: explicit character/rig ownership, hierarchy and scale rejection, " +
                "Generic manual pose sampling, replay/pause/reset, blend phases, unchanged body geometry under wardrobe masks, " +
                "legacy driver rejection, isolated import paths, snapshot state dispatch, owner mesh preservation and unambiguous exact bindings. " +
                "In-memory fixtures only; no authored anatomy, contact polish, FBX round trip or game integration certified.");
        }

        static void ValidateExplicitRig()
        {
            using (var fixture = new Fixture())
            {
                Require(fixture.Rig.ValidateBindings(out string error), error);
                Require(fixture.Rig.TryGet(Joint.HandLeft, out Transform hand) && hand == fixture.Nodes[Joint.HandLeft],
                    "Explicit binding did not resolve the character's arbitrarily named hand.");
                var binding = fixture.Bindings.First(item => item.semantic == Joint.HandLeft);
                Transform original = binding.target;
                var outside = new GameObject("external hand");
                try
                {
                    binding.target = outside.transform; fixture.ApplyRig();
                    Require(!fixture.Rig.ValidateBindings(out _), "The rig accepted a foreign transform.");
                }
                finally { UnityEngine.Object.DestroyImmediate(outside); binding.target = original; fixture.ApplyRig(); }
                fixture.Nodes[Joint.ForearmLeft].localScale = new Vector3(1, 2, 1);
                Require(!fixture.Rig.ValidateBindings(out _), "The rig accepted scaled anatomy.");
                fixture.Nodes[Joint.ForearmLeft].localScale = Vector3.one;
                fixture.Nodes[Joint.HandLeft].SetParent(fixture.Nodes[Joint.ForearmRight], false);
                Require(!fixture.Rig.ValidateBindings(out _), "The rig accepted a hand under the opposite arm.");
                fixture.Nodes[Joint.HandLeft].SetParent(fixture.Nodes[Joint.ForearmLeft], false);
                fixture.Rig.Configure("victor", "operator", "fixture-1", fixture.Bindings);
                Require(!fixture.Rig.ValidateBindings(out _), "The rig accepted mismatched character and role.");
                fixture.ApplyRig();
                DeadAirV2CharacterImporter.ValidateImportedMeshes(fixture.Actor);
            }
        }

        static void ValidatePlayback()
        {
            using (var fixture = new Fixture())
            {
                Require(fixture.Playback.ValidateConfiguration(out string error), error);
                int[] triangles = fixture.Body.sharedMesh.triangles;
                Vector3[] vertices = fixture.Body.sharedMesh.vertices;
                Vector3 actorPosition = fixture.Actor.transform.position;
                Require(fixture.Playback.Present("Walk", .25f, 0, "operator"), fixture.Playback.LastError);
                Require(fixture.Body.enabled && !fixture.Dress.enabled, "A bare-body sample did not preserve the complete body.");
                Vector3 chestPose = fixture.Nodes[Joint.Chest].localPosition;
                Require(Mathf.Abs(chestPose.x - .05f) < .001f, "Generic playback did not sample the authored chest curve: " + chestPose);
                for (int i = 0; i < 4; i++) fixture.Playback.Present("Walk", .25f, 0, "operator");
                Require((fixture.Nodes[Joint.Chest].localPosition - chestPose).sqrMagnitude < 1e-10f,
                    "Repeated snapshots advanced or accumulated pose offsets.");
                Require(fixture.Playback.Present("Walk", 1.25f, 1, "operator") && fixture.Dress.enabled,
                    "Loop phase or dressed state did not sample.");
                Require((fixture.Nodes[Joint.Chest].localPosition - chestPose).sqrMagnitude < 1e-8f, "Loop wrap changed phase.");
                Require(fixture.Playback.Present("UseItem", 2, 0, "operator") && Mathf.Abs(fixture.Nodes[Joint.Chest].localPosition.x - .2f) < .001f,
                    "One-shot action did not clamp to its final pose.");
                Require(fixture.Playback.PresentBlend("Idle", 0, "Walk", .5f, .5f, 0, "operator") &&
                    Mathf.Abs(fixture.Nodes[Joint.Chest].localPosition.x - .05f) < .001f, "Generic two-pose blend failed.");
                Require(fixture.Playback.PresentWithUpperBody("Walk", .25f, "Hold_operator", .5f, 1, 0, "operator") &&
                    Mathf.Abs(fixture.Nodes[Joint.Chest].localPosition.x - .3f) < .001f &&
                    Mathf.Abs(fixture.Nodes[Joint.Pelvis].localPosition.x) < .001f,
                    "The explicit chest layer failed to apply upper motion or leaked its pelvis curve into locomotion.");
                fixture.Actor.SetActive(false); fixture.Actor.SetActive(true);
                Require(fixture.Playback.Present("Walk", .25f, 0, "operator"), "Graph did not recreate after pooling reset.");
                Require((fixture.Actor.transform.position - actorPosition).sqrMagnitude < 1e-10f &&
                    !fixture.Actor.GetComponent<Animator>().applyRootMotion, "Visual playback moved the gameplay root.");
                Require(vertices.SequenceEqual(fixture.Body.sharedMesh.vertices) && triangles.SequenceEqual(fixture.Body.sharedMesh.triangles),
                    "The V2 wardrobe/player changed complete body geometry.");
                fixture.Playback.ResetPresentation();
                var legacy = fixture.Actor.AddComponent<Animation>();
                Require(!fixture.Playback.ValidateConfiguration(out _), "Two animation owners were permitted on one rig.");
                UnityEngine.Object.DestroyImmediate(legacy);
                var wrongVersion = fixture.Clips.Select(item => new DeadAirCharacterAnimator.ClipBinding {
                    action = item.action, clip = item.clip, loop = item.loop, rigVersion = "another-rig"
                }).ToArray();
                fixture.Playback.Configure(wrongVersion);
                Require(!fixture.Playback.ValidateConfiguration(out _), "Clips for another rig revision were accepted.");
            }
        }

        static void ValidateImportGuards()
        {
            Require(!DeadAirV2CharacterImporter.IsV2Path("Assets/Resources/Characters/character_operator.fbx"), "Legacy path entered V2 import.");
            Require(!DeadAirV2CharacterImporter.IsV2Path("Assets/Characters/ProductionV2/vivian/../victor/Models/model.fbx"), "Traversal path entered V2 import.");
            using (var fixture = new Fixture())
            {
                string path = AnimationUtility.CalculateTransformPath(fixture.Nodes[Joint.Chest], fixture.Actor.transform);
                Require(DeadAirV2CharacterImporter.ExactTransform(fixture.Actor.transform, path) == fixture.Nodes[Joint.Chest], "Exact path lookup failed.");
                var ambiguous = new GameObject(fixture.Nodes[Joint.Chest].name);
                ambiguous.transform.SetParent(fixture.Nodes[Joint.Chest].parent, false);
                ExpectRejected(() => DeadAirV2CharacterImporter.ExactTransform(fixture.Actor.transform, path), "Ambiguous sibling path accepted.");
                var manifest = new DeadAirV2CharacterImporter.Manifest {
                    characterId = "vivian", characterClass = "operator", rigVersion = "fixture-1",
                    modelPath = "Assets/Resources/Characters/character_operator.fbx",
                    prefabPath = "Assets/Resources/World/character_operator.prefab"
                };
                ExpectRejected(() => DeadAirV2CharacterImporter.ValidateManifest(manifest), "Old cast output accepted.");
            }
        }

        static void ValidateSnapshotBridge()
        {
            using (var fixture = new Fixture())
            {
                var bridge = fixture.AddBridge();
                var motion = fixture.Actor.AddComponent<DeadAirCharacterMotion>(); motion.ClassId = "operator";
                Require(motion.UsesV2, "Motion did not recognize V2.");
                Require(bridge.ValidateProfile(out string error), error);
                Require(fixture.Actor.GetComponent<DeadAirAppearance>() == null && fixture.Actor.GetComponent<Animation>() == null,
                    "Motion initialized an old body classifier or old animation player on V2.");
                var state = new PlayerState { characterClass = "operator", x = -3, z = 2, yaw = Mathf.PI, clothingMask = 0 };
                motion.Present(state, 0, false);
                motion.SetLocomotionPose(false, 2.6f, Vector3.forward * 2.6f); motion.Present(state, .1f, false);
                Require(fixture.Playback.CurrentAction == "Walk" && bridge.Phase > 0, "Snapshot gait did not use the new Walk clip.");
                float phase = bridge.Phase;
                motion.SetLocomotionPose(false, 2.6f, Vector3.forward * 2.6f); motion.Present(state, .1f, false);
                Require(Mathf.Abs(bridge.Phase - phase) < .00001f, "Repeated snapshot advanced gait twice.");
                state.uniqueItemEquipped = true; state.uniqueActionCancelled = false; state.uniqueActionSequence = 1; state.uniqueActionAt = 0;
                motion.Present(state, 3.5f, false);
                Require(fixture.Playback.CurrentAction == "UseItem" && Mathf.Abs(fixture.Playback.CurrentPhase - .5f) < .0001f,
                    "Vivian personal action ignored the authoritative seven-second clock.");
                motion.Present(state, 4, true);
                Require(Mathf.Abs(fixture.Playback.CurrentPhase - .5f) < .0001f, "Paused personal action advanced.");
                state.uniqueActionCancelled = true; state.uniqueItemEquipped = false; state.ladderClimbing = true; state.ladderUp = true;
                state.ladderStartedAt = 4;
                motion.Present(state, 4 + DeadAirLadder.Duration / 2, false);
                Require(fixture.Playback.CurrentAction == "LadderUp" && Mathf.Abs(fixture.Playback.CurrentPhase - .5f) < .0001f,
                    "Ladder playback did not use the authoritative route phase.");
                state.ladderClimbing = false; motion.SetTransitPose(1); motion.Present(state, 8, false);
                Require(fixture.Playback.CurrentAction == "SitLoop", "Transit did not select the authored seated pose.");
                motion.Present(state, 8.25f, false);
                Require(Mathf.Abs(fixture.Playback.CurrentPhase - .25f) < .0001f, "The seated loop is frozen instead of advancing its authored phase.");
                motion.SetTransitPose(0); state.down = true; motion.Present(state, 9, false);
                Require(fixture.Playback.CurrentAction == "DownLoop", "Down state did not select the authored down pose.");
                motion.Present(state, 9.5f, false);
                Require(Mathf.Abs(fixture.Playback.CurrentPhase - .5f) < .0001f, "The down loop is frozen instead of advancing its authored phase.");
                motion.Present(state, 9.8f, true);
                Require(Mathf.Abs(fixture.Playback.CurrentPhase - .5f) < .0001f, "Paused down loop advanced.");
                state.down = false; state.inventory.Add("unconfigured-item");
                Require(!bridge.Present(state, 10, false, true) && bridge.LastError.Contains("Hold_unconfigured-item"),
                    "Missing authored item animation silently fell back to another rig/action.");
                Require(fixture.Body.enabled && !fixture.Dress.enabled, "Bridge lost bare-body wardrobe state.");
                Require(float.IsNaN(motion.FootContactError), "Unmeasured V2 foot contact was reported as a measured zero error.");
            }
        }

        static void ValidateViewAimAndLightOwnership()
        {
            using (var fixture = new Fixture())
            {
                var bridge = fixture.AddBridge();
                bridge.OwnerView = true;
                var ownerLightObject = new GameObject("fixture owner output light");
                try
                {
                    var output = ownerLightObject.AddComponent<Light>(); output.type = LightType.Spot;
                    var state = new PlayerState { characterClass = "operator", yaw = Mathf.PI, x = -3, z = 2,
                        inventory = new List<string> { "flashlight" }, flashlight = true };
                    Require(bridge.Present(state, 0, false, true), bridge.LastError);
                    bridge.PresentOwnerFlashlight(output);
                    Require(output.enabled && !bridge.FlashlightEmitter.enabled, "Owner V2 enabled two flashlight emitters.");
                    Vector3 handBefore = fixture.Nodes[Joint.HandRight].position;
                    Vector3 upperLength = fixture.Nodes[Joint.ForearmRight].localPosition;
                    Vector3 lowerLength = fixture.Nodes[Joint.HandRight].localPosition;
                    state.pitch = .5f;
                    Require(bridge.Present(state, .25f, false, true), bridge.LastError);
                    bridge.PresentOwnerFlashlight(output);
                    Quaternion expected = Quaternion.AngleAxis(-.5f * Mathf.Rad2Deg, Vector3.right);
                    Require(Quaternion.Angle(fixture.Nodes[Joint.Head].rotation, expected) < .02f &&
                        Vector3.Angle(output.transform.forward, expected * Vector3.forward) < .02f,
                        "Pitch did not reach the explicitly bound head and lamp aim.");
                    Require(Mathf.Abs(fixture.Nodes[Joint.HandRight].position.y - handBefore.y) > .05f && bridge.AimContactError < .001f,
                        "Pitch did not move the authored hand target or broke its calibrated contact.");
                    Quaternion headPose = fixture.Nodes[Joint.Head].rotation, handPose = fixture.Nodes[Joint.HandRight].rotation;
                    Vector3 handPosition = fixture.Nodes[Joint.HandRight].position;
                    for (int i = 0; i < 8; i++) Require(bridge.Present(state, .25f, false, true), bridge.LastError);
                    Require(Quaternion.Angle(fixture.Nodes[Joint.Head].rotation, headPose) < .002f &&
                        Quaternion.Angle(fixture.Nodes[Joint.HandRight].rotation, handPose) < .002f &&
                        Vector3.Distance(fixture.Nodes[Joint.HandRight].position, handPosition) < .00001f,
                        "Repeated presentation accumulated pitch or hand offsets.");
                    Require(fixture.Nodes[Joint.ForearmRight].localPosition == upperLength && fixture.Nodes[Joint.HandRight].localPosition == lowerLength,
                        "Aiming stretched the character's authored arm lengths.");
                    bridge.OwnerView = false;
                    Require(bridge.Present(state, .3f, false, true), bridge.LastError); bridge.PresentOwnerFlashlight(output);
                    Require(!output.enabled && bridge.FlashlightEmitter.enabled, "Third-person and owner light ownership did not switch cleanly.");
                    state.flashlight = false;
                    Require(bridge.Present(state, .4f, false, true), bridge.LastError); bridge.PresentOwnerFlashlight(output);
                    Require(!output.enabled && !bridge.FlashlightEmitter.enabled, "Authoritative lamp-off left an emitter enabled.");
                    typeof(DeadAirCharacterV2Presentation).GetField("viewPitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .SetValue(bridge, null);
                    Require(!bridge.ValidateProfile(out _), "An uncalibrated V2 view profile was accepted.");
                    Require(!bridge.Present(state, .5f, false, true) && !bridge.FlashlightRequested,
                        "An uncalibrated view continued presenting incorrect controls.");
                }
                finally { UnityEngine.Object.DestroyImmediate(ownerLightObject); }
            }
        }

        static void ValidateGuestVisualClock()
        {
            using (var fixture = new Fixture())
            {
                var bridge = fixture.AddBridge();
                var state = new PlayerState { characterClass = "operator", yaw = Mathf.PI, x = -3, z = 2 };
                Require(bridge.PresentAtWallTime(state, 20, false, true, 100), bridge.LastError);
                Require(bridge.PresentAtWallTime(state, 20, false, true, 100.05), bridge.LastError);
                Require(Mathf.Abs(fixture.Playback.CurrentPhase - .05f) < .0001f,
                    "Guest animation did not advance between authoritative snapshots.");
                float phase = fixture.Playback.CurrentPhase;
                Require(bridge.PresentAtWallTime(state, 20, false, true, 100.05), bridge.LastError);
                Require(Mathf.Abs(fixture.Playback.CurrentPhase - phase) < .0001f, "Same-frame visual-clock sampling advanced twice.");
                Require(bridge.PresentAtWallTime(state, 20, false, true, 100.5), bridge.LastError);
                Require(bridge.PresentationTime <= 20 + DeadAirCharacterV2Presentation.MaximumVisualExtrapolation + .0001f,
                    "Visual prediction ran indefinitely after packet loss.");
                Require(bridge.PresentAtWallTime(state, 20.1f, false, true, 100.6), bridge.LastError);
                Require(bridge.PresentationTime >= 20.15f - .0001f, "A delayed snapshot rewound the presentation clock.");
                Require(bridge.PresentAtWallTime(state, 20.2f, false, true, 100.7), bridge.LastError);
                float beforePause = bridge.PresentationTime;
                Require(bridge.PresentAtWallTime(state, 20.2f, true, true, 101), bridge.LastError);
                Require(bridge.PresentAtWallTime(state, 20.2f, true, true, 102), bridge.LastError);
                Require(bridge.PresentationTime == beforePause, "Paused guest presentation kept extrapolating.");
                Require(bridge.PresentAtWallTime(state, 20.2f, false, true, 103), bridge.LastError);
                Require(bridge.PresentAtWallTime(state, 20.2f, false, true, 103.05), bridge.LastError);
                Require(bridge.PresentationTime > beforePause, "Guest playback did not resume after pause.");
                Require(bridge.PresentAtWallTime(state, 2, false, true, 104), bridge.LastError);
                Require(bridge.PresentationTime == 2, "Authoritative clock reset retained stale predicted time.");
            }
        }

        static void ValidateOwnerBody()
        {
            using (var fixture = new Fixture())
            {
                fixture.AddBridge();
                fixture.Actor.AddComponent<DeadAirCharacterMotion>().ClassId = "operator";
                var owner = new GameObject("V2 owner fixture");
                var cameraObject = new GameObject("V2 owner camera");
                try
                {
                    owner.transform.position = new Vector3(1, 0, 4);
                    var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = owner.transform.position + Vector3.up * 1.65f;
                    var body = owner.AddComponent<DeadAirFirstPersonBody>(); body.ConfigureV2Prefabs(new[] { fixture.Actor });
                    var player = new PlayerState { characterClass = "operator", clothingMask = 0, yaw = Mathf.PI };
                    body.Present(player, new RoomState { phase = "playing", elapsed = 0 }, owner.transform, Mathf.PI, 0, false, false, camera);
                    Require(body.UsesV2 && body.Avatar != null && body.HiddenTriangles == 0, "Owner body used legacy triangle clipping.");
                    Require(body.Avatar.transform.localScale == Vector3.one && body.Avatar.transform.position == owner.transform.position,
                        "Owner body scaled to a shared eye height or offset the authored feet.");
                    var surfaces = body.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Require(surfaces.Length == fixture.Actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,
                        "Owner V2 body added duplicate shadow/geometry surfaces.");
                    var actualBody = surfaces.Single(surface => surface.name == fixture.Body.name);
                    Require(actualBody.sharedMesh == fixture.Body.sharedMesh && actualBody.enabled &&
                        actualBody.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On, "Owner changed complete body mesh or removed its shadow.");
                    var block = new MaterialPropertyBlock(); actualBody.GetPropertyBlock(block);
                    Vector4 ownerCamera = block.GetVector("_DeadAirOwnerCamera");
                    Require(ownerCamera.w == 1 && ((Vector3)ownerCamera - camera.transform.position).sqrMagnitude < 1e-10f,
                        "Owner camera shader visibility was not configured explicitly.");
                    var overlay = owner.AddComponent<DeadAirFirstPersonRig>(); overlay.SetVisible(true);
                    var visibleField = typeof(DeadAirFirstPersonRig).GetField("visible", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Require(!(bool)visibleField.GetValue(overlay), "Legacy hand overlay was enabled above the new complete V2 hands.");
                    body.SetVisible(false); body.SetVisible(true);
                    body.Present(player, new RoomState { phase = "playing", elapsed = .1f }, owner.transform, Mathf.PI, 0, false, false, camera);
                    Require(body.Avatar.GetComponent<DeadAirCharacterAnimator>().LastError == null, "Owner pose failed after pooling visibility reset.");

                    var bridge = body.Avatar.GetComponent<DeadAirCharacterV2Presentation>();
                    var output = owner.AddComponent<Light>();
                    var lampSurface = surfaces.Single(surface => surface.name == "fixture lamp surface");
                    var cameraSurface = surfaces.Single(surface => surface.name == "fixture camera surface");
                    player.inventory = new List<string> { "flashlight" }; player.flashlight = true;
                    void PresentEquipment(bool mapOpen, bool cameraUp, bool lightOn)
                    {
                        body.Present(player, new RoomState { phase = "playing", elapsed = .2f }, owner.transform, Mathf.PI, 0, false, false,
                            camera, new DeadAirFirstPersonBody.OwnerEquipmentState(mapOpen, cameraUp, lightOn));
                        Require(bridge.LastError == null, bridge.LastError);
                        bridge.PresentOwnerFlashlight(output);
                    }
                    // Reproduce an input immediately after a send: the authoritative echo still says on.
                    PresentEquipment(false, false, false);
                    Require(!output.enabled && !bridge.FlashlightEmitter.enabled && lampSurface.enabled && player.flashlight,
                        "A stale on snapshot re-enabled the owner's locally disabled lamp or mutated authority.");
                    player.flashlight = false;
                    PresentEquipment(false, false, true);
                    Require(output.enabled && !bridge.FlashlightEmitter.enabled && !player.flashlight,
                        "Owner lamp-on waited for a snapshot echo or changed authoritative equipment state.");
                    PresentEquipment(false, true, true);
                    Require(cameraSurface.enabled && !lampSurface.enabled && output.enabled && !player.camera,
                        "Local camera mode did not override the delayed snapshot presentation.");
                    PresentEquipment(true, true, true);
                    Require(!cameraSurface.enabled && !lampSurface.enabled && !output.enabled && !player.mapOpen,
                        "Local map presentation did not hide held equipment and light immediately.");
                    player.mapOpen = player.camera = true;
                    PresentEquipment(false, false, true);
                    Require(lampSurface.enabled && !cameraSurface.enabled && output.enabled && player.mapOpen && player.camera,
                        "Closing local map/camera retained the stale remote hand mode or mutated the snapshot.");
                    player.flashlightCharge = 0;
                    PresentEquipment(false, false, true);
                    Require(!output.enabled, "Local lamp override bypassed the authoritative charge guard.");
                    player.flashlightCharge = 100;
                    body.Present(player, new RoomState { phase = "playing", elapsed = .3f }, owner.transform, Mathf.PI, 0, false, false, camera);
                    bridge.PresentOwnerFlashlight(output);
                    Require(!output.enabled && !lampSurface.enabled && !cameraSurface.enabled,
                        "Owner override leaked into a later snapshot-only presentation call.");
                }
                finally
                {
                    // EditMode fixtures call public presentation directly; PlayMode lifetime callbacks are not guaranteed here.
                    foreach (var playback in owner.GetComponentsInChildren<DeadAirCharacterAnimator>(true)) playback.ResetPresentation();
                    UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
        }

        static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        static void ExpectRejected(Action action, string error)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(error);
        }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject Actor = new GameObject("V2 runtime fixture");
            public readonly Dictionary<Joint, Transform> Nodes = new Dictionary<Joint, Transform>();
            public readonly DeadAirCharacterRigBindings.Binding[] Bindings;
            public readonly DeadAirCharacterAnimator.ClipBinding[] Clips;
            public readonly DeadAirCharacterRigBindings Rig;
            public readonly DeadAirCharacterAnimator Playback;
            public readonly SkinnedMeshRenderer Body, Dress;
            public readonly Transform AimPivot;
            readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

            public Fixture()
            {
                Actor.transform.position = new Vector3(3, 0, 2);
                Add(Joint.Root, null); Add(Joint.Pelvis, Joint.Root); Add(Joint.Chest, Joint.Pelvis);
                Add(Joint.Head, Joint.Chest); Add(Joint.EyeReference, Joint.Head);
                Add(Joint.UpperArmLeft, Joint.Chest); Add(Joint.ForearmLeft, Joint.UpperArmLeft); Add(Joint.HandLeft, Joint.ForearmLeft);
                Add(Joint.UpperArmRight, Joint.Chest); Add(Joint.ForearmRight, Joint.UpperArmRight); Add(Joint.HandRight, Joint.ForearmRight);
                Add(Joint.ThighLeft, Joint.Pelvis); Add(Joint.ShinLeft, Joint.ThighLeft); Add(Joint.FootLeft, Joint.ShinLeft); Add(Joint.ToeLeft, Joint.FootLeft);
                Add(Joint.ThighRight, Joint.Pelvis); Add(Joint.ShinRight, Joint.ThighRight); Add(Joint.FootRight, Joint.ShinRight); Add(Joint.ToeRight, Joint.FootRight);
                Add(Joint.GripLeft, Joint.HandLeft); Add(Joint.GripRight, Joint.HandRight);
                Add(Joint.SoleHeelLeft, Joint.FootLeft); Add(Joint.SoleToeLeft, Joint.FootLeft);
                Add(Joint.SoleHeelRight, Joint.FootRight); Add(Joint.SoleToeRight, Joint.FootRight);
                Add(Joint.ItemStow, Joint.Pelvis); Add(Joint.MouthContact, Joint.Head);
                Nodes[Joint.Pelvis].localPosition = new Vector3(0, .9f, 0);
                Nodes[Joint.Chest].localPosition = new Vector3(0, .35f, 0);
                Nodes[Joint.Head].localPosition = new Vector3(0, .35f, 0);
                Nodes[Joint.UpperArmLeft].localPosition = new Vector3(-.19f, 0, 0);
                Nodes[Joint.UpperArmRight].localPosition = new Vector3(.19f, 0, 0);
                Nodes[Joint.ForearmLeft].localPosition = Nodes[Joint.ForearmRight].localPosition = new Vector3(0, -.20f, .15f);
                Nodes[Joint.HandLeft].localPosition = Nodes[Joint.HandRight].localPosition = new Vector3(0, -.18f, .08f);
                AimPivot = new GameObject("fixture calibrated shoulder pivot").transform;
                AimPivot.SetParent(Nodes[Joint.Chest], false);
                Bindings = Nodes.Select(pair => new DeadAirCharacterRigBindings.Binding { semantic = pair.Key, target = pair.Value }).ToArray();
                Rig = Actor.AddComponent<DeadAirCharacterRigBindings>(); ApplyRig();
                Body = Surface("fixture body"); Dress = Surface("fixture dress");
                var wardrobe = Actor.AddComponent<DeadAirCharacterWardrobe>();
                wardrobe.ConfigureBindings("operator", new Renderer[] { Body }, new[] {
                    new DeadAirCharacterWardrobe.GarmentBinding { id = "dress", requiredMask = 1, renderers = new Renderer[] { Dress } }
                });
                var animator = Actor.AddComponent<Animator>();
                animator.avatar = AvatarBuilder.BuildGenericAvatar(Actor, Nodes[Joint.Root].name); owned.Add(animator.avatar);
                Clips = DeadAirCharacterAnimator.CoreActions.Concat(new[] { "SitLoop", "LadderUp", "LadderDown", "DownLoop", "Hold_operator", "Hold_flashlight", "Hold_camera" }).Select(action => {
                    var clip = new AnimationClip { name = "AN_vivian_" + action + "_fixture", frameRate = 30 };
                    string path = AnimationUtility.CalculateTransformPath(Nodes[Joint.Chest], Actor.transform);
                    bool held = action.StartsWith("Hold_", StringComparison.Ordinal);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"),
                        AnimationCurve.Linear(0, held ? .2f : 0, 1, held ? .4f : .2f));
                    if (held)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                            AnimationUtility.CalculateTransformPath(Nodes[Joint.Pelvis], Actor.transform), typeof(Transform), "m_LocalPosition.x"),
                            AnimationCurve.Linear(0, 3, 1, 5));
                    var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = action != "UseItem" && action != "LadderUp" && action != "LadderDown";
                    AnimationUtility.SetAnimationClipSettings(clip, settings); owned.Add(clip);
                    return new DeadAirCharacterAnimator.ClipBinding { action = action, clip = clip, loop = settings.loopTime, rigVersion = "fixture-1" };
                }).ToArray();
                Playback = Actor.AddComponent<DeadAirCharacterAnimator>(); Playback.Configure(Clips);
            }

            public void ApplyRig() => Rig.Configure("vivian", "operator", "fixture-1", Bindings);
            public DeadAirCharacterV2Presentation AddBridge()
            {
                var bridge = Actor.AddComponent<DeadAirCharacterV2Presentation>();
                var lamp = new GameObject("fixture calibrated lamp").AddComponent<Light>();
                lamp.transform.SetParent(Nodes[Joint.HandRight], false); lamp.type = LightType.Spot; lamp.enabled = false;
                bridge.Configure(new[] { "Walk", "Run", "WalkBack", "StrafeLeft", "StrafeRight", "CrouchWalk" }
                    .Select(action => new DeadAirCharacterV2Presentation.Gait { action = action, cycleDistance = 1.1f }).ToArray(),
                    new[] {
                        new DeadAirCharacterV2Presentation.Prop { id = "fixture personal prop", mode = "operator",
                            renderers = new Renderer[] { Surface("fixture prop") } },
                        new DeadAirCharacterV2Presentation.Prop { id = "fixture lamp", mode = "flashlight", aimWithView = true,
                            renderers = new Renderer[] { Surface("fixture lamp surface") } },
                        new DeadAirCharacterV2Presentation.Prop { id = "fixture camera", mode = "camera", aimWithView = true,
                            renderers = new Renderer[] { Surface("fixture camera surface") } }
                    }, Vector3.zero, new Vector3(.2f, .25f, .2f), lamp,
                    new DeadAirCharacterV2Presentation.ViewPitchProfile {
                        chestLocalAxis = Vector3.left, headLocalAxis = Vector3.left, aimPivotLocalAxis = Vector3.left,
                        chestShare = .2f, chestDownLimit = 25, chestUpLimit = 25, headDownLimit = 75, headUpLimit = 75,
                        aimPivot = AimPivot, leftElbowPole = new Vector3(-.3f, -.2f, -.2f),
                        rightElbowPole = new Vector3(.3f, -.2f, -.2f), handContactTolerance = .001f
                    });
                return bridge;
            }
            void Add(Joint semantic, Joint? parent)
            {
                // Names intentionally do not resemble either the old skeleton or the V2 authoring grammar.
                var node = new GameObject("independent_node_" + Nodes.Count).transform;
                node.SetParent(parent.HasValue ? Nodes[parent.Value] : Actor.transform, false);
                Nodes.Add(semantic, node);
            }

            SkinnedMeshRenderer Surface(string name)
            {
                var node = new GameObject(name); node.transform.SetParent(Actor.transform, false);
                var surface = node.AddComponent<SkinnedMeshRenderer>();
                var mesh = new Mesh { name = name + " in-memory validation triangle" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Nodes[Joint.Pelvis].worldToLocalMatrix * node.transform.localToWorldMatrix };
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
                mesh.RecalculateBounds(); owned.Add(mesh);
                surface.sharedMesh = mesh; surface.bones = new[] { Nodes[Joint.Pelvis] }; surface.rootBone = Nodes[Joint.Root];
                return surface;
            }

            public void Dispose()
            {
                foreach (var playback in Actor.GetComponentsInChildren<DeadAirCharacterAnimator>(true)) playback.ResetPresentation();
                UnityEngine.Object.DestroyImmediate(Actor);
                foreach (var item in owned) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
