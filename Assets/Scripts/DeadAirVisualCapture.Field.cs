using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirVisualCapture
    {
        IEnumerator CaptureFieldUpdate()
        {
            Application.targetFrameRate = 30; QualitySettings.vSyncCount = 0;
            yield return new WaitForSecondsRealtime(1);
            var checks = new List<string>();
            var data = WorldData.Load();
            DeadAirSettings.Grain = false; DeadAirSettings.Flicker = false;
            DeadAirSettings.ApplyGraphicsPreset(1);
            ui.ShowClothingEditor();
            yield return Save("field-01-wardrobe");
            Require(ui.HasModal, "Clothing editor must be a working focused modal.");
            GameObject.Find("No clothing").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return Save("field-01b-adult-no-clothing-option");
            ui.TryCloseTopModal();
            session.Host("Field verifier", "engineer", true, true);
            session.Command(new NetPacket { type = "start" });
            yield return null; yield return null;
            Require(player.InTransit && !player.GetComponent<CharacterController>().enabled, "Arrival must lock the real controller and animate in van.");
            yield return Save("field-02-arrival");
            yield return Until(() => session.State.elapsed >= DeadAirVanPresentation.ArrivalDuration + .2f, "Van arrival");
            yield return null;
            Require(!player.InTransit && player.GetComponent<CharacterController>().enabled, "Arrival must return player control.");
            checks.Add("PASSED: actual seated van arrival followed by restored standing controller.");
            var me = session.State.Player(session.LocalId);
            var map = data.Objective("van-map");
            var point = DeadAirVanPresentation.SourcePoint(data, new Vector3(.50f, DeadAirVanPresentation.FloorHeight, -.75f));
            InspectToward(point.x, point.z, point.y, map.x, map.y, map.z);
            player.SetVerificationFlashlight(false);
            yield return Save("field-03-lit-van-map-and-cabin");
            Require(player.InteractionId == "van-map", "The hanging van map must have a usable interaction prompt; actual=" + player.InteractionId);
            ui.OpenVanMap();
            yield return Save("field-04-wall-map");
            Require(ui.MapOpen, "Van board must open its real map UI.");
            ui.TryCloseTopModal();
            me.x = 0; me.y = 0; me.z = 19;
            ui.OpenVanMap(); Require(!ui.MapOpen, "Map must stay unavailable away from board.");
            me.x = point.x; me.y = point.y; me.z = point.z;
            ui.ToggleInventory();
            yield return Save("field-05-mission-journal");
            Require(ui.InventoryOpen, "Tab journal must include a real inventory.");
            ui.TryCloseTopModal();
            checks.Add("PASSED: hanging world map prompt, near-board map only and actual mission/inventory notebook.");
            player.EndInspection(); player.Teleport(me.x, me.y, me.z);
            player.SetVerificationMovement(Vector2.zero, false); player.SetVerificationCrouching(true);
            yield return new WaitForSecondsRealtime(.35f);
            Require(player.Crouching && me.crouching && player.GetComponent<CharacterController>().height < 1.12f,
                "Crouch must resize actual capsule and reach authority.");
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.name = "Capture low headroom";
            obstruction.transform.position = player.transform.position + Vector3.up * 1.44f;
            obstruction.transform.localScale = new Vector3(1, .2f, 1);
            Physics.SyncTransforms();
            player.SetVerificationCrouching(false);
            yield return null;
            Require(player.Crouching && !player.CanStand(), "Standing must be refused under real low obstruction.");
            Destroy(obstruction); yield return null; Physics.SyncTransforms();
            player.SetVerificationCrouching(false);
            yield return new WaitForSecondsRealtime(.35f);
            Require(!player.Crouching && player.GetComponent<CharacterController>().height > 1.68f, "Standing must resume when obstruction is removed.");
            player.ClearVerificationMovement();
            checks.Add("PASSED: crouch capsule/host state, blocked stand-up and restored full height with actual physics.");
            InspectToward(-11.3f, -16.1f, 4.2f, -15, 5.3f, -13);
            session.State.power = session.State.quest.power = session.State.breakerOn = session.State.quest.breakerOn = true;
            player.SetVerificationFlashlight(true);
            yield return Save("field-06-furniture-operating-sides");
            DeadAirSettings.Reflections = false;
            yield return Save("field-07-reflections-disabled");
            Require(Shader.GetGlobalFloat("_DeadAirReflectionsDisabled") == 1 && Shader.GetGlobalFloat("_DeadAirReflectionEnabled") == 0,
                "Reflection switch must disable material light reflection and mirrored floor rendering.");
            DeadAirSettings.Reflections = true;
            checks.Add("PASSED: reflection setting reaches physical material shading and the mirrored-floor camera.");
            session.State.enemy.active = true; session.State.enemy.behaviour = "search";
            session.State.enemy.x = 0; session.State.enemy.y = 0; session.State.enemy.z = -17;
            session.State.enemy.yaw = 0; session.State.paused = true;
            InspectToward(0, -13.5f, 0, 0, 1.25f, -17);
            yield return Save("field-08-new-dictor-model");
            session.State.paused = false;
            player.EndInspection(); ui.ReturnToMenu();
            var peerObject = new GameObject("Field loopback guest");
            var peer = peerObject.AddComponent<DeadAirSession>(); peer.Initialize(data);
            try
            {
                session.Host("Field host", "engineer", false, true);
                peer.Join("127.0.0.1", session.State.code, "Field guest", "dispatcher");
                yield return Until(() => peer.State != null && session.State.players.Count == 2, "Field LAN join");
                peer.SetClothing(DeadAirClothing.Underwear);
                yield return Until(() => session.State.Player(peer.LocalId)?.clothingMask == DeadAirClothing.Underwear &&
                    peer.State.Player(peer.LocalId)?.clothingMask == DeadAirClothing.Underwear, "Shared clothing snapshot");
                session.Command(new NetPacket { type = "start" });
                yield return Until(() => session.State.elapsed > DeadAirVanPresentation.ArrivalDuration, "Coop arrival");
                var guest = session.State.Player(peer.LocalId);
                var guestPoint = DeadAirVanPresentation.SpawnPoint(data, 1);
                peer.Command(new NetPacket { type = "move", x = guestPoint.x, y = guestPoint.y, z = guestPoint.z, crouching = true, moveSequence = 1 });
                yield return Until(() => guest.crouching && peer.State.Player(peer.LocalId).crouching, "Coop crouch snapshot");
                Require(guest.clothingMask == DeadAirClothing.Underwear, "Game start must preserve customized clothing.");
                var hostPoint = DeadAirVanPresentation.SpawnPoint(data, 0);
                InspectToward(hostPoint.x, hostPoint.z, hostPoint.y, guest.x, guest.y + 1.1f, guest.z);
                yield return Save("field-08-coop-clothing-and-crouch");
                player.EndInspection();
                var host = session.State.Player(session.LocalId); var exit = data.Objective("exit");
                var exitPoint = DeadAirVanPresentation.SourcePoint(data, new Vector3(-.5f, .6f, -.3f));
                host.x = exitPoint.x; host.y = exitPoint.y; host.z = exitPoint.z;
                session.State.broadcast = session.State.quest.broadcast = true;
                session.Command(new NetPacket { type = "interact", id = "exit" });
                yield return Until(() => session.State.phase == "departing" && peer.State.phase == "departing", "Coop departure");
                yield return Save("field-09-departure");
                yield return Until(() => session.State.phase == "won" && peer.State.phase == "won", "Animated coop victory");
                checks.Add("PASSED: real LAN guest clothing and crouch replication, synchronized van departure and delayed shared victory.");
            }
            finally { peer.Leave(); Destroy(peerObject); session.Leave(); }
            File.WriteAllLines(Path.Combine(output, "field-runtime-report.txt"), checks);
            Debug.Log("DEAD AIR FIELD RUNTIME PASSED: " + checks.Count + " focused checks with rendered Windows frames.");
            Application.Quit(0);
        }
    }
}
