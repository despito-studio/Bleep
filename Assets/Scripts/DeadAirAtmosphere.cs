using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirAtmosphere : MonoBehaviour
    {
        DeadAirSession session;
        DeadAirPlayer player;
        DeadAirAudio audioEngine;
        WorldData audioWorld;
        DeadAirTapeEffect tape;
        DeadAirScarePresentation scares;
        GameObject phantom;
        DeadAirMonsterMotion phantomMotion;
        readonly Dictionary<string, bool> doorStates = new Dictionary<string, bool>();
        readonly Dictionary<string, HashSet<string>> inventories = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, int> roleActions = new Dictionary<string, int>();
        readonly Dictionary<string, bool> roleCancelled = new Dictionary<string, bool>();
        readonly Dictionary<string, string> playerHiding = new Dictionary<string, string>();
        readonly Dictionary<string, bool> playerDown = new Dictionary<string, bool>();
        readonly Dictionary<string, Vector3> remotePositions = new Dictionary<string, Vector3>();
        readonly Dictionary<string, float> remoteStepTimes = new Dictionary<string, float>();
        readonly HashSet<string> droppedIds = new HashSet<string>();
        readonly List<string> departedActors = new List<string>();
        int lastScare, lastRadioSequence, lastSpider, lastEncounter, lastMushroom;
        bool lastEnemyActive, lastEnemyStunned;
        string lastEnemyBehaviour;
        bool lastBroadcast, lastPower, lastValve, lastCalibrated;
        string lastPhase;
        Vector3 previousPosition;
        float nextPhantom, phantomUntil, nextFlash, flashUntil, nextVisualScare;

        public void Initialize(DeadAirSession game, DeadAirPlayer actor)
        {
            session = game; player = actor; audioWorld = WorldData.Load();
            audioEngine = gameObject.AddComponent<DeadAirAudio>();
            audioEngine.Initialize();
            session.Error += OnError;
            var view = actor.GetComponentInChildren<Camera>();
            if (view != null)
            {
                view.allowHDR = true;
                tape = view.gameObject.AddComponent<DeadAirFrameRenderer>().Initialize(view);
                scares = new GameObject("Horror camera props").AddComponent<DeadAirScarePresentation>();
                scares.Initialize(view);
            }
            var prefab = Resources.Load<GameObject>("World/monster");
            if (prefab != null)
            {
                phantom = Instantiate(prefab, transform);
                phantom.name = "Low sanity peripheral hallucination";
                phantomMotion = phantom.AddComponent<DeadAirMonsterMotion>();
                phantomMotion.Initialize();
                phantom.SetActive(false);
                foreach (var collider in phantom.GetComponentsInChildren<Collider>())
                    if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }
            previousPosition = player.transform.position;
        }

        void OnError(string message) { DeadAirAudio.Play("error"); }

        void LateUpdate()
        {
            if (session == null || audioEngine == null) return;
            var state = session.State;
            var me = state == null ? null : state.Player(session.LocalId);
            string phase = state == null ? "menu" : state.phase;
            if (phase != lastPhase)
            {
                if (phase == "playing")
                {
                    audioEngine.ResetRun();
                    inventories.Clear(); roleActions.Clear(); roleCancelled.Clear();
                    playerHiding.Clear(); playerDown.Clear(); remotePositions.Clear(); remoteStepTimes.Clear();
                    CaptureState(state, me);
                    float now = Time.unscaledTime;
                    nextPhantom = now + 24; nextFlash = now + 32;
                    nextVisualScare = now + 5;
                    previousPosition = player.transform.position;
                }
                else
                {
                    audioEngine.StopRoleActions();
                    if (phase == "won" || phase == "lost") DeadAirAudio.Play(phase == "won" ? "win" : "lose");
                }
                lastPhase = phase;
            }
            bool playing = phase == "playing" && me != null;
            bool paused = !playing || state.paused || player.LocalPaused || player.ModalOpen;
            float trip = !paused && playing ? DeadAirMushrooms.Intensity(state, me) : 0;
            audioEngine.SetTripContext(trip, paused || playing && me.down);
            if (scares != null)
            {
                if (!playing) scares.Clear();
                scares.SetPaused(paused);
            }
            bool hiding = playing && !string.IsNullOrEmpty(me.hiding);
            var movement = player.transform.position - previousPosition;
            previousPosition = player.transform.position;
            bool moving = playing && !paused && !me.down && !hiding && new Vector2(movement.x, movement.z).sqrMagnitude > .000004f && movement.sqrMagnitude < 1;
            float distance = playing ? Vector3.Distance(new Vector3(me.x,me.y,me.z), new Vector3(state.enemy.x,state.enemy.y,state.enemy.z)) : 100;
            bool enemyPresent = playing && state.enemy.active && state.enemy.behaviour != "vanished";
            float threat = enemyPresent ? Mathf.Clamp01(1 - distance / 16) : 0;
            Vector3? enemyPosition = enemyPresent ? new Vector3(-state.enemy.x, state.enemy.y+1.2f, state.enemy.z) : (Vector3?)null;
            bool stairs = playing && IsOnStairs(me.x, me.y, me.z);
            bool outside = playing && !stairs && IsOutside(me.x, me.y, me.z);
            audioEngine.SetContext(phase, outside, playing && me.down, playing ? state.enemy.behaviour : null, stairs);
            audioEngine.Tick(paused, moving, playing && player.Sprinting, hiding, player.Stamina, playing ? me.sanity : 100,
                threat, playing && player.CameraUp, playing && state.power, playing && state.broadcast, enemyPosition, distance, playing && state.breakerOn);
            if (tape != null)
            {
                tape.Threat = Mathf.Lerp(tape.Threat, paused ? 0 : threat, Time.unscaledDeltaTime * 2);
                tape.Insanity = playing ? 1 - me.sanity / 100 : 0;
                tape.Recording = playing && player.CameraUp;
                tape.Nightmare = playing && state.enemy.bloody ? 1 : 0;
                tape.Flash = !paused && Time.unscaledTime < flashUntil ? .16f : 0;
                tape.Trip = trip;
                tape.TripClock = playing ? DeadAirMushrooms.Phase(state, me) : 0;
            }
            if (!playing || paused)
            {
                if (phantom != null) phantom.SetActive(false);
                return;
            }
            DetectEvents(state, me);
            Hallucinations(me);
        }

        void DetectEvents(RoomState state, PlayerState me)
        {
            DetectPlayerSounds(state, me);
            if (me.mushroomTripSerial > lastMushroom && DeadAirMushrooms.Remaining(state, me) > 0)
            {
                audioEngine.Emit("mushroom-eat", .9f);
                audioEngine.Emit("mushroom-trip", .7f);
                nextPhantom = Time.unscaledTime + 1.8f;
            }
            if (state.power && !lastPower) ObjectiveSound("power", "power");
            if (state.valveOpened && !lastValve) ObjectiveSound("pressure-valve", "valve");
            if (state.calibrated && !lastCalibrated) ObjectiveSound("broadcast", "calibrate");
            if (state.broadcast && !lastBroadcast) ObjectiveSound("broadcast", "broadcast-start");
            bool spiderTriggered = me.spiderScareSerial > lastSpider && state.elapsed * 1000 - me.spiderScareAt < 1800;
            if (spiderTriggered)
            {
                scares?.Spider();
            }
            if (state.enemy.encounterSerial > lastEncounter && state.enemy.behaviour == "peek")
                audioEngine.Emit("listening", .65f, 0, new Vector3(-state.enemy.x, state.enemy.y + 1.5f, state.enemy.z));
            if (lastEnemyBehaviour == "peek" && state.enemy.behaviour == "vanished")
                audioEngine.Emit("vanish", .55f, 0, new Vector3(-state.enemy.x, state.enemy.y + 1.3f, state.enemy.z));
            bool visualBreach = state.scare > lastScare && (!lastEnemyActive && state.enemy.active || me.sanity < 45);
            bool signalTriggered = visualBreach && !spiderTriggered && !state.creative && !me.down &&
                string.IsNullOrEmpty(me.hiding) && Time.unscaledTime >= nextVisualScare && !(scares != null && scares.SpiderActive);
            if (signalTriggered)
            {
                scares?.Signal();
                nextVisualScare = Time.unscaledTime + 32;
            }
            else if (!spiderTriggered && !me.down && state.scare > lastScare)
                audioEngine.Emit("scare", .58f);
            if (state.enemy.stunned && !lastEnemyStunned)
                audioEngine.Emit("monster-stun", .85f, 0, new Vector3(-state.enemy.x, state.enemy.y + 1.4f, state.enemy.z));
            foreach (var door in state.doors)
                if (doorStates.TryGetValue(door.id, out bool open) && open != door.open)
                {
                    var data = audioWorld.Door(door.id);
                    if (data != null) DeadAirAudio.PlayAt("door", new Vector3(-data.x, data.y, data.z));
                }
            if (state.droppedItems != null)
                foreach (var item in state.droppedItems)
                    if (!droppedIds.Contains(item.id)) DeadAirAudio.PlayAt("drop", new Vector3(-item.x, item.y + .2f, item.z));
            if (state.radioSequence != lastRadioSequence && state.radioSenderId != session.LocalId)
                DeadAirAudio.Play("radio");
            CaptureState(state, me);
        }

        void ObjectiveSound(string id, string cue)
        {
            var objective = audioWorld.Objective(id);
            if (objective != null) DeadAirAudio.PlayAt(cue, new Vector3(-objective.x, objective.y, objective.z));
            else DeadAirAudio.Play(cue);
        }

        bool IsOnStairs(float x, float y, float z)
        {
            if (audioWorld.stairs != null)
                foreach (var stair in audioWorld.stairs)
                    if (stair.Contains(x, z) && Mathf.Abs(y - stair.HeightAt(z)) < .8f) return true;
            return false;
        }

        bool IsOutside(float x, float y, float z)
        {
            // Both courtyards have room names, so the RoomAt fallback cannot classify terrain.
            foreach (var room in audioWorld.rooms)
                if (DeadAirOfficeLayout.IsInterior(room) && Mathf.Abs(y - room.floorY) < 1 &&
                    x >= room.minX && x <= room.maxX && z >= room.minZ && z <= room.maxZ) return false;
            return true;
        }

        void DetectPlayerSounds(RoomState state, PlayerState me)
        {
            foreach (var actor in state.players)
            {
                bool local = actor.id == me.id;
                var position = new Vector3(-actor.x, actor.y + .8f, actor.z);
                if (inventories.TryGetValue(actor.id, out var previousInventory) && actor.inventory != null)
                    foreach (string item in actor.inventory)
                        if (!previousInventory.Contains(item) && !DeadAirRoleItems.IsRoleItem(item))
                        {
                            string cue = item.StartsWith("tape-") ? "cassette" : item.StartsWith("fuse-") ? "fuse" :
                                item.StartsWith("keycard-") ? "keycard" : item == "fuel-canister" ? "fuel-pickup" : "pickup";
                            audioEngine.Emit(cue, 1, 0, local ? (Vector3?)null : position);
                        }
                if (roleActions.TryGetValue(actor.id, out int action) && actor.uniqueActionSequence > action &&
                    !actor.uniqueActionCancelled && state.elapsed - actor.uniqueActionAt < 1.1f)
                    audioEngine.PlayRoleAction(actor.id, actor.characterClass, local ? (Vector3?)null : position);
                if (actor.uniqueActionCancelled && roleCancelled.TryGetValue(actor.id, out bool wasCancelled) && !wasCancelled)
                    audioEngine.StopRoleAction(actor.id);
                if (!local) audioEngine.UpdateRolePosition(actor.id, position);
                if (playerHiding.TryGetValue(actor.id, out string hiding) && actor.hiding != hiding)
                    audioEngine.Emit(string.IsNullOrEmpty(actor.hiding) ? "locker-exit" : "locker-enter", 1, 0, local ? (Vector3?)null : position);
                if (playerDown.TryGetValue(actor.id, out bool previousDown) && actor.down != previousDown)
                    audioEngine.Emit(actor.down ? "down" : "revive", 1, 0, local ? (Vector3?)null : position);
                if (!local && !actor.down && string.IsNullOrEmpty(actor.hiding) && remotePositions.TryGetValue(actor.id, out var previous))
                {
                    bool moved = new Vector2(position.x - previous.x, position.z - previous.z).sqrMagnitude > .0001f;
                    remoteStepTimes.TryGetValue(actor.id, out float next);
                    if (moved && Time.unscaledTime >= next)
                    {
                        bool stairs = IsOnStairs(actor.x, actor.y, actor.z);
                        bool outdoors = !stairs && IsOutside(actor.x, actor.y, actor.z);
                        string cue = actor.sprinting ? "run-step" : "step";
                        audioEngine.Emit(stairs ? cue + "-metal" : outdoors ? cue + "-gravel" : cue, .85f, 0, position - Vector3.up * .7f);
                        remoteStepTimes[actor.id] = Time.unscaledTime + (actor.sprinting ? .31f : .52f);
                    }
                }
            }
            departedActors.Clear();
            foreach (var entry in roleActions)
                if (state.Player(entry.Key) == null) departedActors.Add(entry.Key);
            foreach (string id in departedActors)
            {
                audioEngine.StopRoleAction(id); roleActions.Remove(id); roleCancelled.Remove(id);
                inventories.Remove(id); playerHiding.Remove(id); playerDown.Remove(id);
                remotePositions.Remove(id); remoteStepTimes.Remove(id);
            }
        }

        void CaptureState(RoomState state, PlayerState me)
        {
            lastScare = state.scare; lastBroadcast = state.broadcast; lastPower = state.power;
            lastValve = state.valveOpened; lastCalibrated = state.calibrated;
            lastRadioSequence = state.radioSequence;
            lastSpider = me == null ? 0 : me.spiderScareSerial;
            lastMushroom = me == null ? 0 : me.mushroomTripSerial;
            lastEncounter = state.enemy.encounterSerial;
            lastEnemyActive = state.enemy.active;
            lastEnemyStunned = state.enemy.stunned;
            lastEnemyBehaviour = state.enemy.behaviour;
            doorStates.Clear();
            foreach (var door in state.doors) doorStates[door.id] = door.open;
            droppedIds.Clear();
            if (state.droppedItems != null) foreach (var item in state.droppedItems) droppedIds.Add(item.id);
            foreach (var actor in state.players)
            {
                if (!inventories.TryGetValue(actor.id, out var items)) { items = new HashSet<string>(); inventories[actor.id] = items; }
                items.Clear(); if (actor.inventory != null) foreach (string item in actor.inventory) items.Add(item);
                roleActions[actor.id] = actor.uniqueActionSequence; roleCancelled[actor.id] = actor.uniqueActionCancelled;
                playerHiding[actor.id] = actor.hiding; playerDown[actor.id] = actor.down;
                remotePositions[actor.id] = new Vector3(-actor.x, actor.y + .8f, actor.z);
            }
        }

        void Hallucinations(PlayerState me)
        {
            float now = Time.unscaledTime;
            bool tripping = DeadAirMushrooms.Intensity(session.State, me) > .1f;
            if ((me.sanity < 40 || tripping) && !me.down && string.IsNullOrEmpty(me.hiding) && now >= nextPhantom && phantom != null)
            {
                var direction = Quaternion.Euler(0, UnityEngine.Random.value > .5f ? 47 : -47, 0) * player.transform.forward;
                phantom.transform.position = player.transform.position + direction * 6.5f;
                phantom.SetActive(true);
                phantomUntil = now + (tripping ? 2.2f : 7);
                nextPhantom = now + (tripping ? UnityEngine.Random.Range(3.4f, 5.5f) : UnityEngine.Random.Range(18, 32));
            }
            if (phantom != null && phantom.activeSelf)
            {
                phantomMotion?.SetPreviewPose("search", now);
                var target = player.transform.position;
                target.y = phantom.transform.position.y;
                phantom.transform.LookAt(target);
                if (now > phantomUntil || me.down || (!tripping && me.sanity >= 40) || Vector3.Distance(phantom.transform.position, player.transform.position) < 3 || !tripping && player.FlashlightOn && player.CameraUp)
                {
                    phantom.SetActive(false);
                    audioEngine.Emit("whisper", .8f, UnityEngine.Random.Range(-.8f, .8f));
                }
            }
            if (DeadAirSettings.Flicker && me.sanity < 22 && now >= nextFlash && !(scares != null && scares.IsFullscreenActive))
            {
                audioEngine.Emit("whisper", .65f, UnityEngine.Random.Range(-.8f, .8f));
                flashUntil = now + .1f;
                nextFlash = now + UnityEngine.Random.Range(22, 37);
            }
        }

        void OnDestroy() { if (session != null) session.Error -= OnError; }
    }
}
