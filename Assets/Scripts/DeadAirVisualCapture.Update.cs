using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirVisualCapture
    {
        IEnumerator CaptureUpdate()
        {
            Application.targetFrameRate = 30;
            yield return new WaitForSecondsRealtime(1.5f);
            Require(Application.version == DeadAirVersion.Current, "Runtime version must match the compiled release version " + DeadAirVersion.Current + ".");
            yield return Save("update-00-menu");
            session.Host("Update verifier", "engineer", true, true);
            session.Command(new NetPacket { type = "start" });
            yield return Until(() => session.State?.phase == "playing", "Solo update start");
            var data = WorldData.Load();
            var me = session.State.Player(session.LocalId);
            me.invulnerableUntil = 99999;
            var firstBattery = data.Batteries[0];
            InspectToward(firstBattery.x, firstBattery.z - 1.2f, 0, firstBattery.x, firstBattery.y, firstBattery.z);
            yield return Save("update-01a-four-van-batteries");
            foreach (var battery in data.Batteries)
            {
                InspectToward(battery.x, battery.z - 1.2f, 0, battery.x, battery.y, battery.z);
                yield return null;
                session.Command(new NetPacket { type = "interact", id = battery.id });
                yield return null;
            }
            Require(me.spareBatteries == 4 && data.Batteries.All(b => session.State.Collected(b.id)), "Van has exactly four shared collectible batteries.");
            me.flashlightCharge = 0;
            session.Command(new NetPacket { type = "reload" });
            yield return null;
            Require(me.spareBatteries == 3 && me.flashlightCharge > 179, "Reload consumes one battery and restores the torch.");
            yield return Save("update-01-van-supplies");

            var ladder = data.Objective("ladder");
            InspectToward(ladder.x, ladder.z + 1.3f, 0, ladder.x, ladder.y, ladder.z);
            session.Command(new NetPacket { type = "interact", id = "ladder" });
            yield return null;
            Require(me.HasItem("ladder"), "The actual van ladder can be collected.");
            player.SetInspectionPose(-9, 7.55f, 0, 0);
            session.Command(new NetPacket { type = "interact", id = "ladder-spot" });
            yield return null;
            Require(session.State.ladderPlaced, "The ladder can be placed at the director window.");
            yield return Save("update-02-ladder-ready");
            player.EndInspection();
            session.Command(new NetPacket { type = "interact", id = "ladder-climb" });
            yield return Until(() => me.ladderClimbing, "Climb starts on host");
            yield return new WaitForSecondsRealtime(1.4f);
            Require(!session.State.windowBroken && me.y > 0 && me.y < 4.2f, "Climb moves gradually before striking glass.");
            yield return Save("update-03-climbing", false);
            yield return Until(() => session.State.windowBroken, "Glass breaks at the top");
            var pane = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "state:directorWindowPane" && t.gameObject.scene.IsValid());
            Require(pane != null && !pane.gameObject.activeSelf, "Actual glass renderer and collider disappear on break.");
            Require(Resources.FindObjectsOfTypeAll<MeshRenderer>().Count(r => r.name == "Director glass shard" && r.gameObject.activeInHierarchy) == 24,
                "Breaking the pane creates a visible burst of twenty-four glass fragments.");
            yield return Save("update-04-window-break", false);
            yield return Until(() => !me.ladderClimbing, "Climb lands inside");
            Require(Mathf.Abs(me.y - 4.2f) < .1f && me.z < 5.8f && player.GetComponent<CharacterController>().enabled, "Climb restores controller on the director floor.");
            yield return Save("update-05-inside-director");
            session.Command(new NetPacket { type = "interact", id = "ladder-climb" });
            yield return Until(() => me.ladderClimbing, "Descent starts");
            yield return Until(() => !me.ladderClimbing, "Descent finishes");
            Require(Mathf.Abs(me.y) < .1f && me.z > 6.3f && player.GetComponent<CharacterController>().enabled, "Descent returns to the safe yard endpoint.");

            var drawer = data.Drawers.First(d => d.directorKey);
            var furniture = data.props.First(p => p.id == drawer.propId);
            Vector3 front = DeadAirOfficeLayout.Rotation(furniture.rotation) * Vector3.forward;
            AimAtDrawer(drawer, furniture, 1.1f);
            yield return null;
            Require(player.InteractionId == drawer.id, "Reticle selects the director drawer.");
            session.Command(new NetPacket { type = "interact", id = drawer.id });
            yield return null;
            Require(session.State.Drawer(drawer.id).open, "Director drawer opens through host interaction.");
            yield return Save("update-06-director-drawer");
            session.Command(new NetPacket { type = "interact", id = "key-main" });
            yield return null;
            Require(me.HasItem("key-main"), "Director access card remains collectible in its animated tray.");
            var catalog = data.Drawers.First(d => data.props.First(p => p.id == d.propId).type == "cardCatalog");
            furniture = data.props.First(p => p.id == catalog.propId);
            front = DeadAirOfficeLayout.Rotation(furniture.rotation) * Vector3.forward;
            AimAtDrawer(catalog, furniture, 1.1f);
            yield return null;
            Require(player.InteractionId == catalog.id, "Camera pitch selects the lowest filing drawer.");
            session.Command(new NetPacket { type = "interact", id = catalog.id });
            yield return null;
            Require(session.State.Drawer(catalog.id).open, "Ordinary filing cabinet drawers also open.");
            yield return Save("update-07-filing-drawer");
            foreach (var sample in data.Drawers.Where(d => d.propId == drawer.propId || d.propId == catalog.propId))
            {
                var prop = data.props.First(p => p.id == sample.propId);
                if (session.State.Drawer(sample.id).open)
                {
                    AimAtDrawer(sample, prop, 1.1f);
                    yield return null;
                    session.Command(new NetPacket { type = "interact", id = sample.id });
                    Require(!session.State.Drawer(sample.id).open, "Sample drawer closes before testing its physical front.");
                    yield return new WaitForSecondsRealtime(.5f);
                }
                foreach (float distance in new[] { 1.0f, 1.5f })
                {
                    AimAtDrawer(sample, prop, distance);
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    Require(player.InteractionId == sample.id, "Every physical desk/catalog row can be aimed at: " + sample.id + "; actual=" + player.InteractionId + "; distance=" + distance);
                }
            }

            var mushroom = data.Mushrooms.First(m => m.y == 0);
            InspectToward(mushroom.x, mushroom.z + 1, mushroom.y, mushroom.x, mushroom.y + .3f, mushroom.z);
            yield return Save("update-08-backyard-mushrooms");
            Require(player.InteractionId == mushroom.id, "Restored backyard mushrooms have actual E prompts.");
            session.Command(new NetPacket { type = "interact", id = mushroom.id });
            yield return Until(() => me.mushroomTripSerial == 1, "Backyard meal accepted");
            yield return new WaitForSecondsRealtime(1.6f);
            yield return Save("update-09-mushroom-trip");
            me.mushroomTripUntil = session.State.elapsed;

            session.State.power = session.State.quest.power = true;
            session.State.breakerOn = session.State.quest.breakerOn = true;
            InspectToward(-9, -38.4f, -4.2f, -14, -2.8f, -32);
            yield return Save("update-10-prison-gallery");
            var basementMushroom = data.Mushrooms.First(m => m.y < -1);
            InspectToward(basementMushroom.x, basementMushroom.z + 1, basementMushroom.y, basementMushroom.x, basementMushroom.y + .3f, basementMushroom.z);
            yield return Save("update-11-basement-mushrooms");

            // Verify new fields over a real loopback LAN session, including a guest climb.
            player.EndInspection(); ui.ReturnToMenu(); yield return null;
            var peerObject = new GameObject("Update loopback peer");
            var peer = peerObject.AddComponent<DeadAirSession>(); peer.Initialize(data);
            try
            {
                session.Host("Update host", "engineer", false, true);
                peer.Join("127.0.0.1", session.State.code, "Update guest", "operator");
                yield return Until(() => peer.State != null && session.State.players.Count == 2, "Update LAN join");
                session.Command(new NetPacket { type = "start" });
                yield return Until(() => peer.State?.phase == "playing", "Update LAN start");
                var guest = session.State.Player(peer.LocalId);
                var battery = data.Batteries[0]; guest.x = battery.x; guest.y = 0; guest.z = battery.z - 1;
                peer.Command(new NetPacket { type = "interact", id = battery.id });
                yield return Until(() => peer.State.Player(peer.LocalId).spareBatteries == 1, "Guest battery replicated");
                Require(session.State.Collected(battery.id) && peer.State.Collected(battery.id), "Van pickup is shared across LAN.");
                guest.flashlightCharge = 0;
                peer.Command(new NetPacket { type = "reload" });
                yield return Until(() => peer.State.Player(peer.LocalId).flashlightReloadSerial == 1, "Guest reload replicated");
                session.State.ladderPlaced = true; guest.x = -9; guest.y = 0; guest.z = 7.55f;
                peer.Command(new NetPacket { type = "interact", id = "ladder-climb" });
                yield return Until(() => peer.State.Player(peer.LocalId).ladderClimbing, "Guest climb replicated");
                yield return Until(() => peer.State.windowBroken, "Guest breaks shared pane");
                yield return Until(() => !peer.State.Player(peer.LocalId).ladderClimbing, "Guest climb completes");
                Require(Mathf.Abs(peer.State.Player(peer.LocalId).y - 4.2f) < .1f, "Guest arrives on correct floor.");
            }
            finally { peer.Leave(); Destroy(peerObject); session.Leave(); }

            session.Host("Book verifier", "engineer", true, true);
            session.Command(new NetPacket { type = "start" });
            yield return Until(() => session.State?.phase == "playing", "Book solo start");
            me = session.State.Player(session.LocalId);
            session.State.power = session.State.breakerOn = true;
            var book = data.Objective("prison-book");
            InspectToward(book.x, book.z - 1.2f, -4.2f, book.x, book.y, book.z);
            yield return Save("update-12-prison-book");
            Require(player.InteractionId == "prison-book", "Prison book has the actual pickup prompt.");
            session.Command(new NetPacket { type = "interact", id = "prison-book" });
            yield return null;
            Require(me.HasItem("prison-book"), "Prison book enters inventory.");
            ui.ShowBook();
            yield return Save("update-13-reading-book");
            Require(ui.HasModal && session.State.paused, "Book reader pauses solo simulation.");
            player.EndInspection(); ui.ReturnToMenu();
            File.WriteAllText(Path.Combine(output, "update-runtime-report.txt"),
                "PASSED: v" + Application.version + "; GPU menu/van/ladder/climb/glass/director and filing drawers/backyard trip/prison cells/basement mushrooms/book reader; real 4-battery pickup and reload; timed host climb up/down with controller restoration; glass renderer/collider disabled with 24 fragments; all 3 desk and 8 catalog fronts aim correctly at 1/1.5 m; actual mushroom/book E prompts; loopback guest battery/reload/ladder/window snapshots; solo reader pause.\n");
            Debug.Log("DEAD AIR UPDATE RUNTIME PASSED");
            Application.Quit(0);
        }

        void AimAtDrawer(DrawerData drawer, PropData furniture, float distance)
        {
            Require(player.TryGetDrawerAimTarget(drawer.id, out Vector3 point), "Drawer has a physical aim target.");
            Vector3 front = DeadAirOfficeLayout.Rotation(furniture.rotation) * Vector3.forward;
            Vector3 position = point + front * distance;
            InspectToward(-position.x, position.z, furniture.y, -point.x, point.y, point.z);
        }
    }
}
