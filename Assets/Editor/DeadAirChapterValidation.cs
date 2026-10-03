using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirChapterValidation
    {
        [MenuItem("Dead Air/Validate Chapters")]
        public static void Validate()
        {
            var world = WorldData.Load();
            ValidateCatalog();
            ValidateRules(world);
            ValidateHosting(world);
            ValidateSerialization();
            ValidateLan(world);
            Debug.Log("DEAD AIR CHAPTERS PASSED: catalog, default office chapter, unavailable/unknown rejection, host-only selection/start, session preservation, snapshot serialization, loopback LAN start and restart.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Chapter validation: " + message);
        }

        static void ValidateCatalog()
        {
            Require(DeadAirChapters.Catalog.Count == 2 && DeadAirChapters.Catalog.Count(chapter => chapter.playable) == 1,
                "There must be exactly one playable chapter and one unavailable placeholder.");
            var office = DeadAirChapters.Find("blackpine");
            Require(office != null && office.playable && office.number == "01" && office.title == "BLACKPINE" &&
                office.subtitle == "Мертвий ефір", "Blackpine must be identified as chapter one.");
            var upcoming = DeadAirChapters.Find("coming-soon");
            Require(upcoming != null && !upcoming.playable && upcoming.number == "02" && upcoming.title == "НЕВІДОМА ЧАСТОТА" &&
                upcoming.description == "Наступна глава готується.", "Chapter two must remain an unavailable placeholder.");
            Require(DeadAirChapters.Find("unknown") == null && !DeadAirChapters.IsPlayable("unknown") &&
                !DeadAirChapters.IsPlayable(null) && !DeadAirChapters.IsPlayable("coming-soon"), "Unknown and unavailable chapter IDs must not be playable.");
        }

        static void ValidateRules(WorldData world)
        {
            var rules = new DeadAirRules(world, "host", "Chapter host", "engineer", false, true);
            Require(rules.State.chapterId == DeadAirChapters.DefaultId, "Legacy callers must default to the original office.");
            Require(rules.AddPlayer("guest", "Chapter guest", "operator"), "A guest should join the selected chapter lobby.");
            rules.ClearDirty();
            foreach (string rejected in new[] { "unknown", "coming-soon", "" })
            {
                Require(rules.SelectChapter("host", rejected) != null && rules.State.chapterId == DeadAirChapters.DefaultId &&
                    !rules.Dirty, "Rejected selection must leave the lobby chapter unchanged.");
                Require(rules.Start("host", rejected) != null && rules.State.phase == "lobby" &&
                    rules.State.chapterId == DeadAirChapters.DefaultId, "Unknown or unavailable chapters must not start.");
                bool rejectedConstructor = false;
                try { new DeadAirRules(world, "host", "Rejected host", "engineer", true, true, rejected); }
                catch (ArgumentException) { rejectedConstructor = true; }
                Require(rejectedConstructor, "Rules construction must reject a nonplayable chapter.");
            }
            Require(rules.SelectChapter("guest", "blackpine") != null && !rules.Dirty, "A guest must not select even an available chapter for the host.");
            Require(rules.Start("guest", "blackpine") != null && rules.State.phase == "lobby", "A guest must not start the host's chapter.");
            rules.State.chapterId = "coming-soon";
            Require(rules.Start("host") != null && rules.State.phase == "lobby", "Start must validate the persisted selection too.");
            Require(rules.SelectChapter("host", "blackpine") == null && rules.Dirty, "The host must be able to select the playable chapter.");
            Require(rules.Start("host") == null && rules.State.phase == "playing", "The selected playable chapter must start.");
            Require(rules.SelectChapter("host", "blackpine") != null, "An active chapter must not be replaced mid-operation.");
            rules.State.phase = "lost";
            Require(rules.Restart("host") == null && rules.State.phase == "playing" && rules.State.chapterId == "blackpine",
                "Restart must retain the chosen chapter.");
            rules.State.phase = "lost"; rules.State.chapterId = "coming-soon";
            Require(rules.Restart("host") != null && rules.State.phase == "lost", "Restart must reject an unavailable persisted chapter.");
        }

        static void ValidateHosting(WorldData world)
        {
            var test = new GameObject("Chapter hosting validation");
            try
            {
                var session = test.AddComponent<DeadAirSession>(); session.Initialize(world);
                session.Host("Default host", "engineer", true, true);
                var original = session.State;
                Require(original != null && original.chapterId == "blackpine" && session.LastError == null,
                    "The four-argument Host API must retain its original default behavior.");
                foreach (string rejected in new[] { "unknown", "coming-soon", null, "" })
                {
                    session.Host("Rejected host", "engineer", true, true, rejected);
                    Require(ReferenceEquals(session.State, original) && session.LocalId == "host" && session.LastError != null,
                        "Invalid hosting must report its error and preserve any existing session.");
                }
                session.Host("Explicit chapter host", "engineer", true, true, "blackpine");
                Require(session.State.chapterId == "blackpine" && session.LastError == null && !ReferenceEquals(session.State, original),
                    "Valid hosting must accept the explicit chapter and clear prior errors.");
            }
            finally { UnityEngine.Object.DestroyImmediate(test); }
        }

        static void ValidateSerialization()
        {
            var state = new RoomState { chapterId = "blackpine" };
            var snapshot = JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(state));
            Require(snapshot.chapterId == state.chapterId, "Room snapshots must preserve the selected chapter.");
            var packet = new NetPacket { type = "chapter", chapterId = "blackpine" };
            var copy = JsonUtility.FromJson<NetPacket>(JsonUtility.ToJson(packet));
            Require(copy.chapterId == packet.chapterId && copy.type == packet.type, "Chapter commands must survive LAN serialization.");
        }

        static void ValidateLan(WorldData world)
        {
            var hostObject = new GameObject("Chapter LAN host");
            var guestObject = new GameObject("Chapter LAN guest");
            var host = hostObject.AddComponent<DeadAirSession>();
            var guest = guestObject.AddComponent<DeadAirSession>();
            var update = typeof(DeadAirSession).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            int guestSnapshots = 0;
            guest.StateChanged += () => guestSnapshots++;
            try
            {
                host.Initialize(world); guest.Initialize(world);
                host.Host("Chapter host", "engineer", false, true, "blackpine");
                Require(host.LastError == null, "Could not open the loopback chapter test host: " + host.LastError);
                guest.Join("127.0.0.1", host.State.code, "Chapter guest", "operator");
                PumpUntil(() => guest.State != null && host.State.players.Count == 2, "Loopback guest did not join.");
                Require(guest.State.chapterId == "blackpine" && host.State.chapterId == guest.State.chapterId,
                    "The guest must receive the selected chapter in its lobby snapshot.");
                guest.Command(new NetPacket { type = "chapter", chapterId = "coming-soon" });
                PumpUntil(() => guest.LastError != null, "Guest chapter selection was not rejected over LAN.");
                Require(host.State.chapterId == "blackpine" && guest.State.chapterId == "blackpine",
                    "A guest command must not change the host's selected chapter.");
                guest.Command(new NetPacket { type = "start", chapterId = "blackpine" });
                PumpUntil(() => guest.LastError == "Сеанс починає господар кімнати.", "Guest start was not rejected over LAN.");
                Require(host.State.phase == "lobby", "A guest must not start the room by supplying a playable chapter.");
                host.Command(new NetPacket { type = "start", chapterId = "coming-soon" });
                Require(host.State.phase == "lobby" && host.LastError != null, "The host must not start the unavailable chapter over its command API.");
                host.Command(new NetPacket { type = "start", chapterId = "blackpine" });
                PumpUntil(() => guest.State.phase == "playing", "Selected chapter start was not synchronized.");
                Require(guest.State.chapterId == "blackpine", "The playing snapshot must retain the host chapter.");
                int snapshotsBeforeRestart = guestSnapshots;
                host.State.phase = "lost";
                host.Command(new NetPacket { type = "restart" });
                PumpUntil(() => guestSnapshots > snapshotsBeforeRestart && guest.State.phase == "playing",
                    "The chapter restart snapshot was not synchronized.");
                Require(host.State.chapterId == "blackpine" && guest.State.chapterId == "blackpine", "Restart must preserve the chapter on both peers.");
            }
            finally
            {
                guest.Leave(); host.Leave();
                UnityEngine.Object.DestroyImmediate(guestObject); UnityEngine.Object.DestroyImmediate(hostObject);
            }

            void PumpUntil(Func<bool> ready, string message)
            {
                for (int attempt = 0; attempt < 240 && !ready(); attempt++)
                { update.Invoke(host, null); update.Invoke(guest, null); Thread.Sleep(10); }
                Require(ready(), message);
            }
        }
    }
}
