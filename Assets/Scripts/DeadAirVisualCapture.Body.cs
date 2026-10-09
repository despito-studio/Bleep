using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirVisualCapture
    {
        IEnumerator CaptureFirstPersonBody()
        {
            var captures = new Stack<IEnumerator>(); captures.Push(CaptureFirstPersonBodyFrames());
            while (captures.Count > 0)
            {
                object current = null; bool moved = false; System.Exception failure = null;
                try { moved = captures.Peek().MoveNext(); if (moved) current = captures.Peek().Current; }
                catch (System.Exception error) { failure = error; }
                if (failure != null) { Debug.LogException(failure); Application.Quit(2); yield break; }
                if (!moved) { captures.Pop(); continue; }
                if (current is IEnumerator nested) captures.Push(nested); else yield return current;
            }
        }

        IEnumerator CaptureFirstPersonBodyFrames()
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
            yield return new WaitForSecondsRealtime(1);
            var checks = new List<string>(); var data = WorldData.Load();
            DeadAirSettings.Grain = false; DeadAirSettings.Flicker = false;
            DeadAirSettings.ApplyGraphicsPreset(1);
            Require(!PlayerPrefs.HasKey("DeadAir.ClothingMask"), "Legacy outfit preference was not removed.");
            Require(!FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(button => button.name.Contains("Clothing") || button.name.Contains("Wardrobe")), "An outfit editing button remains.");
            yield return Save("body-00-character-menu");
            checks.Add("PASSED: outfit editor removed and legacy clothing preference cleared.");
            foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
            {
                session.Host("Body verifier", role, true, true); session.Command(new NetPacket { type = "start" });
                session.State.elapsed = DeadAirVanPresentation.ArrivalDuration + 1;
                session.State.enemy.active = false;
                yield return null; yield return null;
                player.SetInspectionPose(0, 24, Mathf.PI, DeadAirLocomotion.MaximumDownwardPitch);
                yield return Save("body-01-" + role + "-standing-look-down");
                var body = player.FirstPersonBody;
                Require(body != null && body.Avatar != null && body.Avatar.activeInHierarchy && body.VisibleTriangles > 500 && body.HiddenTriangles > 0,
                    role + " has no actual visible authored torso/legs.");
                Require(body.Avatar.GetComponent<DeadAirAppearance>().CurrentMask == DeadAirClothing.DefaultMask(role), role + " body lost its original outfit.");
                Require(body.Avatar.GetComponentsInChildren<Light>(true).All(light => light.name == "Subtle first person body bounce" ?
                    light.cullingMask == 1 << 27 && light.shadows == LightShadows.None : !light.enabled || !light.gameObject.activeInHierarchy),
                    role + " local body duplicates its flashlight.");
                if (role == "engineer")
                {
                    player.SetInspectionPose(0, 24, Mathf.PI, 0);
                    yield return Save("body-02-engineer-level-view");
                    player.SetInspectionPose(0, 24, Mathf.PI, DeadAirLocomotion.MaximumDownwardPitch);
                }
                player.EndInspection(); player.SetVerificationMovement(Vector2.zero, false); player.SetVerificationCrouching(true);
                yield return new WaitForSecondsRealtime(.45f);
                Require(player.Crouching && player.ViewCamera.transform.localPosition.y < 1.03f, role + " actual crouch camera did not descend.");
                yield return Save("body-03-" + role + "-crouched-look-down");
                Require(body.Avatar.activeInHierarchy && Mathf.Abs(body.Motion.HeadPitchDegrees) <= 55.01f, role + " crouched body or neck failed.");
                player.SetVerificationCrouching(false); yield return new WaitForSecondsRealtime(.4f);
                player.ClearVerificationMovement(); player.EndInspection(); ui.ReturnToMenu(); yield return null;
            }
            yield return CaptureRelaxedCharacterPostures();
            checks.Add("PASSED: all four authored local bodies rendered standing and through actual crouch eye/capsule transitions without duplicate equipment lights; full source models rendered in relaxed idle, walk, run and flashlight poses.");
            var peerObject = new GameObject("Body loopback guest"); var peer = peerObject.AddComponent<DeadAirSession>(); peer.Initialize(data);
            try
            {
                session.Host("Gaze host", "engineer", false, true);
                peer.Join("127.0.0.1", session.State.code, "Gaze guest", "dispatcher");
                yield return Until(() => peer.State != null && session.State.players.Count == 2, "Body LAN join");
                session.Command(new NetPacket { type = "start" });
                session.State.elapsed = DeadAirVanPresentation.ArrivalDuration + 1;
                session.State.enemy.active = false;
                yield return Until(() => peer.State?.phase == "playing", "Body LAN start");
                var guest = session.State.Player(peer.LocalId); var spawn = DeadAirVanPresentation.SpawnPoint(data, 1);
                InspectToward(spawn.x - .5f, spawn.z - 1.4f, spawn.y, spawn.x, spawn.y + 1.4f, spawn.z);
                DeadAirCharacterMotion remote = null;
                yield return Until(() => (remote = FindObjectsByType<DeadAirCharacterMotion>(FindObjectsSortMode.None)
                    .FirstOrDefault(actor => actor.name == "Gaze guest / dispatcher")) != null, "Body LAN avatar");
                peer.Command(new NetPacket { type = "move", x = spawn.x, y = spawn.y, z = spawn.z, yaw = Mathf.PI, pitch = 0, moveSequence = 1 });
                yield return Until(() => Mathf.Abs(guest.yaw - Mathf.PI) < .001f, "Baseline network yaw");
                yield return new WaitForSecondsRealtime(1.6f);
                float bodyBefore = remote.BodyYawDegrees;
                peer.Command(new NetPacket { type = "move", x = spawn.x, y = spawn.y, z = spawn.z, yaw = Mathf.PI - 25 * Mathf.Deg2Rad, pitch = .6f, moveSequence = 2 });
                yield return Until(() => guest.moveSequence >= 2 && Mathf.Abs(remote.HeadPitchDegrees) > 15 && Mathf.Abs(remote.HeadYawDegrees) > 15,
                    "Replicated head nod/glance");
                Require(Mathf.Abs(Mathf.DeltaAngle(bodyBefore, remote.BodyYawDegrees)) < 3.1f, "Small glance rotates idle feet rather than the head.");
                yield return Save("body-04-coop-head-nod");
                peer.Command(new NetPacket { type = "move", x = spawn.x, y = spawn.y, z = spawn.z, yaw = 0, pitch = DeadAirLocomotion.MaximumDownwardPitch, moveSequence = 3 });
                yield return Until(() => guest.moveSequence >= 3, "Network 180-degree turn");
                float deadline = Time.realtimeSinceStartup + 2.2f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    Require(Mathf.Abs(remote.HeadYawDegrees) <= 55.01f && Mathf.Abs(remote.HeadPitchDegrees) <= 55.01f, "Remote neck exceeded its natural range.");
                    yield return null;
                }
                Require(Mathf.Abs(Mathf.DeltaAngle(remote.BodyYawDegrees, 180)) <= 3.1f, "Remote torso stayed facing forward after looking behind.");
                yield return Save("body-05-coop-body-turned");
                checks.Add("PASSED: real LAN pitch/yaw snapshots move the source head, keep small idle glances planted and bring the whole torso around on a 180-degree turn with bounded neck motion.");
            }
            finally { peer.Leave(); Destroy(peerObject); player.EndInspection(); session.Leave(); }
            File.WriteAllLines(Path.Combine(output, "body-runtime-report.txt"), checks);
            Debug.Log("DEAD AIR BODY RUNTIME PASSED: " + checks.Count + " focused checks with rendered Windows frames.");
            Application.Quit(0);
        }

        IEnumerator CaptureRelaxedCharacterPostures()
        {
            session.Host("Posture verifier", "engineer", true, true); session.Command(new NetPacket { type = "start" });
            session.State.elapsed = DeadAirVanPresentation.ArrivalDuration + 1; session.State.enemy.active = false;
            yield return null; yield return null;
            var actors = new List<DeadAirCharacterMotion>();
            var stage = new GameObject("Source posture capture stage");
            try
            {
                int index = 0;
                foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
                {
                    var avatar = Instantiate(Resources.Load<GameObject>("World/character_" + role), stage.transform);
                    avatar.name = "Posture capture / " + role;
                    avatar.transform.position = new Vector3(-2.7f + index++ * 1.8f, 0, 20);
                    foreach (var child in avatar.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 26;
                    foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    actors.Add(avatar.GetComponent<DeadAirCharacterMotion>());
                }
                // Illuminate only the supplied character surfaces for an unobscured
                // full-body comparison. This light exists solely in explicit captures.
                var lightObject = new GameObject("Posture comparison light"); lightObject.transform.SetParent(stage.transform, false);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
                light.color = new Color(.85f, .89f, 1); light.intensity = .85f;
                light.shadows = LightShadows.None; light.cullingMask = 1 << 26;
                lightObject.transform.rotation = Quaternion.Euler(35, 180, 0);
                // Keep the comparison actors in their authored forward direction.
                foreach (var actor in actors) actor.transform.rotation = Quaternion.identity;
                InspectToward(0, 26, 0, 0, 1.05f, 20);
                captureWithoutEquipment = true; captureCanvas.enabled = false;
                int frame = 0;
                foreach (string clip in new[] { "Idle", "Walk", "Run", "Flashlight" })
                {
                    foreach (float phase in clip == "Walk" || clip == "Run" ? new[] { .25f, .75f } : new[] { .25f })
                    {
                        foreach (var actor in actors)
                        {
                            var state = new PlayerState { characterClass = actor.ClassId, clothingMask = -1,
                                inventory = new List<string> { "flashlight" }, selectedSlot = clip == "Flashlight" ? 0 : -1 };
                            // Empty hands exercise free arm swing. The final view uses
                            // the production held-light contact and gaze direction.
                            if (clip != "Flashlight") state.inventory.Clear();
                            actor.SetEquipmentPreview(state, clip == "Flashlight" ? "Idle" : clip, phase);
                        }
                        yield return Save("posture-0" + ++frame + "-" + clip.ToLowerInvariant() + "-" + Mathf.RoundToInt(phase * 100));
                    }
                }
            }
            finally
            {
                captureWithoutEquipment = false; captureCanvas.enabled = true;
                Destroy(stage); player.EndInspection(); ui.ReturnToMenu();
            }
            yield return null;
        }
    }
}
