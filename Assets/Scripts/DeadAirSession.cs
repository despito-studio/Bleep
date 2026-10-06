using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DeadAir
{
    sealed class LanPeer : IDisposable
    {
        public string id;
        public readonly TcpClient socket;
        readonly StreamReader reader;
        readonly StreamWriter writer;
        readonly object writeLock = new object();
        readonly ConcurrentQueue<Action> mainQueue;
        readonly Action<LanPeer, NetPacket> onPacket;
        readonly Action<LanPeer> onClose;
        Thread thread;

        public LanPeer(TcpClient socket, ConcurrentQueue<Action> mainQueue,
            Action<LanPeer, NetPacket> onPacket, Action<LanPeer> onClose)
        {
            this.socket = socket;
            this.mainQueue = mainQueue;
            this.onPacket = onPacket;
            this.onClose = onClose;
            socket.NoDelay = true;
            var stream = socket.GetStream();
            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public void Start()
        {
            thread = new Thread(ReadLoop) { IsBackground = true, Name = "Dead Air LAN reader" };
            thread.Start();
        }

        void ReadLoop()
        {
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    NetPacket packet;
                    try { packet = JsonUtility.FromJson<NetPacket>(line); }
                    catch { continue; }
                    if (packet != null) mainQueue.Enqueue(() => onPacket(this, packet));
                }
            }
            catch (Exception) { /* A closed socket ends the reader. */ }
            finally { mainQueue.Enqueue(() => onClose(this)); }
        }

        public void Send(NetPacket packet)
        {
            if (!socket.Connected) return;
            try { lock (writeLock) writer.WriteLine(JsonUtility.ToJson(packet)); }
            catch (Exception) { mainQueue.Enqueue(() => onClose(this)); }
        }

        public void Dispose() { try { socket.Close(); } catch (Exception) { } }
    }

    public sealed class DeadAirSession : MonoBehaviour
    {
        public const int Port = 32004;
        readonly ConcurrentQueue<Action> mainQueue = new ConcurrentQueue<Action>();
        readonly List<LanPeer> guests = new List<LanPeer>();
        TcpListener listener;
        LanPeer server;
        DeadAirRules rules;
        WorldData world;
        int sessionGeneration;
        public RoomState State { get; private set; }
        public string LocalId { get; private set; }
        public string LastError { get; private set; }
        public bool IsHost => rules != null;
        public bool IsConnected => State != null;
        public event Action StateChanged;
        public event Action<string> Error;

        public void Initialize(WorldData data) { world = data; }

        public void Host(string name, string classId, bool solo, bool creative, string chapterId = DeadAirChapters.DefaultId)
        {
            string chapterError = DeadAirChapters.SelectionError(chapterId);
            if (chapterError != null) { Report(chapterError); return; }
            Leave();
            LastError = null;
            LocalId = "host";
            rules = new DeadAirRules(world, LocalId, name, classId, solo, creative, chapterId);
            State = rules.State;
            if (!solo)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, Port);
                    listener.Start();
                    var activeListener = listener;
                    var thread = new Thread(() => AcceptLoop(activeListener)) { IsBackground = true, Name = "Dead Air LAN host" };
                    thread.Start();
                }
                catch (Exception ex) { Report("Не вдалося відкрити порт " + Port + ": " + ex.Message); }
            }
            StateChanged?.Invoke();
        }

        void AcceptLoop(TcpListener activeListener)
        {
            try
            {
                while (listener == activeListener)
                {
                    var socket = activeListener.AcceptTcpClient();
                    mainQueue.Enqueue(() =>
                    {
                        if (listener != activeListener) { socket.Close(); return; }
                        var peer = new LanPeer(socket, mainQueue, HostPacket, GuestClosed);
                        guests.Add(peer);
                        peer.Start();
                    });
                }
            }
            catch (Exception) { /* Listener was stopped. */ }
        }

        public void Join(string host, string code, string name, string classId)
        {
            Leave();
            LastError = null;
            int generation = sessionGeneration;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var socket = new TcpClient();
                    socket.Connect(host, Port);
                    mainQueue.Enqueue(() =>
                    {
                        if (generation != sessionGeneration) { socket.Close(); return; }
                        server = new LanPeer(socket, mainQueue, ClientPacket, ServerClosed);
                        server.Start();
                        server.Send(new NetPacket { type = "hello", code = code.Trim().ToUpperInvariant(), name = name, classId = classId });
                    });
                }
                catch (Exception ex) { mainQueue.Enqueue(() => { if (generation == sessionGeneration) Report("З’єднання не встановлено: " + ex.Message); }); }
            });
        }

        void HostPacket(LanPeer peer, NetPacket packet)
        {
            if (rules == null || !guests.Contains(peer)) return;
            if (packet.type == "hello")
            {
                if (packet.code != State.code || State.solo || State.players.Count >= 4)
                {
                    peer.Send(new NetPacket { type = "error", error = "Код кімнати неправильний або кімната заповнена." });
                    peer.Dispose(); return;
                }
                peer.id = Guid.NewGuid().ToString("N");
                if (!rules.AddPlayer(peer.id, packet.name, packet.classId))
                {
                    peer.Send(new NetPacket { type = "error", error = "Не вдалося приєднатися." });
                    peer.Dispose(); return;
                }
                peer.Send(new NetPacket { type = "welcome", playerId = peer.id, code = State.code, chapterId = State.chapterId });
                Publish();
                return;
            }
            if (string.IsNullOrEmpty(peer.id)) return;
            ApplyCommand(peer.id, packet);
        }

        void ClientPacket(LanPeer peer, NetPacket packet)
        {
            // Queued packets from a closed connection must not reopen the result
            // screen after the player has already returned to the main menu.
            if (server != peer) return;
            if (packet.type == "welcome") { LocalId = packet.playerId; return; }
            if (packet.type == "error") { Report(packet.error); return; }
            if (packet.type == "state")
            {
                try
                {
                    State = JsonUtility.FromJson<RoomState>(packet.stateJson);
                    // Older room snapshots predate chapters; retain the playable original office.
                    if (State != null && string.IsNullOrEmpty(State.chapterId))
                        State.chapterId = string.IsNullOrEmpty(packet.chapterId) ? DeadAirChapters.DefaultId : packet.chapterId;
                    StateChanged?.Invoke();
                }
                catch (Exception ex) { Report("Не вдалося прочитати стан кімнати: " + ex.Message); }
            }
        }

        void GuestClosed(LanPeer peer)
        {
            if (!guests.Remove(peer)) return;
            if (!string.IsNullOrEmpty(peer.id)) { rules?.RemovePlayer(peer.id); Publish(); }
            peer.Dispose();
        }

        void ServerClosed(LanPeer peer)
        {
            if (server != peer) return;
            server = null; State = null; LocalId = null;
            Report("Зв’язок із хостом втрачено.");
            StateChanged?.Invoke();
        }

        public void Command(NetPacket packet)
        {
            if (rules != null) ApplyCommand(LocalId, packet);
            else server?.Send(packet);
        }

        void ApplyCommand(string id, NetPacket packet)
        {
            if (rules == null) return;
            string error = null;
            switch (packet.type)
            {
                case "chapter": error = rules.SelectChapter(id, packet.chapterId); break;
                case "start": error = rules.Start(id, packet.chapterId); break;
                case "restart": error = rules.Restart(id); break;
                case "pause": rules.Pause(id, packet.paused); break;
                case "class": rules.SetClass(id, packet.classId); break;
                case "move": rules.Move(id, packet); break;
                case "radio": rules.Radio(id, packet.phrase); break;
                case "interact": error = rules.Interact(id, packet.id); break;
                case "calibrate": error = rules.Calibrate(id, packet.frequency); break;
                case "drop": error = rules.Drop(id, packet.id); break;
                case "slot": rules.SelectSlot(id, packet.phrase); break;
                case "unique-equip": rules.EquipUnique(id, packet.uniqueEquipped); break;
                case "map-equip": rules.EquipMap(id, packet.mapOpen); break;
                case "unique-use": error = rules.UseUnique(id); break;
                case "console": error = rules.ExecuteCommand(id, packet.id); break;
            }
            if (error != null)
            {
                if (id == LocalId) Report(error);
                else guests.Find(p => p.id == id)?.Send(new NetPacket { type = "error", error = error });
            }
            if (rules.Dirty && packet.type != "move") Publish();
        }

        public string ExecuteConsole(string cmd)
        {
            if (rules != null) return rules.ExecuteCommand(LocalId, cmd);
            Command(new NetPacket { type = "console", id = cmd });
            return null;
        }

        void Update()
        {
            while (mainQueue.TryDequeue(out var action)) action();
            if (rules == null) return;
            rules.Tick(Time.deltaTime);
            if (rules.Dirty) Publish();
        }

        void Publish()
        {
            if (rules == null) return;
            State = rules.State;
            var packet = new NetPacket { type = "state", stateJson = JsonUtility.ToJson(State), chapterId = State.chapterId };
            foreach (var peer in guests) if (!string.IsNullOrEmpty(peer.id)) peer.Send(packet);
            rules.ClearDirty();
            StateChanged?.Invoke();
        }

        void Report(string error)
        {
            LastError = error;
            Error?.Invoke(error);
        }

        public void Leave()
        {
            sessionGeneration++;
            if (listener != null) { try { listener.Stop(); } catch (Exception) { } listener = null; }
            foreach (var peer in guests) peer.Dispose();
            guests.Clear();
            server?.Dispose(); server = null;
            rules = null; State = null; LocalId = null;
            StateChanged?.Invoke();
        }

        void OnDestroy()
        {
            // Scene teardown may destroy the UI before the session. Close the
            // connections without notifying views that are being destroyed.
            StateChanged = null;
            Error = null;
            Leave();
        }
    }
}
