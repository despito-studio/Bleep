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
                player.SetInspectionPose(0, 24, Mathf.PI, -1.35f);
                yield return Save("body-01-" + role + "-standing-look-down");
                var body = player.FirstPersonBody;
                Require(body != null && body.Avatar != null && body.Avatar.activeInHierarchy && body.VisibleTriangles > 500 && body.HiddenTriangles > 0,
                    role + " has no actual visible authored torso/legs.");
                Require(body.Avatar.GetComponent<DeadAirAppearance>().CurrentMask == DeadAirClothing.DefaultMask(role), role + " body lost its original outfit.");
                Require(body.Avatar.GetComponentsInChildren<Light>(true).All(light => !light.enabled || !light.gameObject.activeInHierarchy),
                    role + " local body duplicates its flashlight.");
                if (role == "engineer")
                {
                    player.SetInspectionPose(0, 24, Mathf.PI, 0);
                    yield return Save("body-02-engineer-level-view");
                    player.SetInspectionPose(0, 24, Mathf.PI, -1.35f);
                }
                player.EndInspection(); player.SetVerificationMovement(Vector2.zero, false); player.SetVerificationCrouching(true);
                yield return new WaitForSecondsRealtime(.45f);
                Require(player.Crouching && player.ViewCamera.transform.localPosition.y < 1.03f, role + " actual crouch camera did not descend.");
                yield return Save("body-03-" + role + "-crouched-look-down");
                Require(body.Avatar.activeInHierarchy && Mathf.Abs(body.Motion.HeadPitchDegrees) <= 55.01f, role + " crouched body or neck failed.");
                player.SetVerificationCrouching(false); yield return new WaitForSecondsRealtime(.4f);
                player.ClearVerificationMovement(); player.EndInspection(); ui.ReturnToMenu(); yield return null;
            }
            checks.Add("PASSED: all four authored local bodies rendered standing and through actual crouch eye/capsule transitions without duplicate equipment lights.");
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
                peer.Command(new NetPacket { type = "move", x = spawn.x, y = spawn.y, z = spawn.z, yaw = 0, pitch = -1.35f, moveSequence = 3 });
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
    }
}
