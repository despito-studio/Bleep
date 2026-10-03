using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirAtmosphere : MonoBehaviour
    {
        DeadAirSession session;
        DeadAirPlayer player;
        DeadAirAudio audioEngine;
        DeadAirTapeEffect tape;
        DeadAirScarePresentation scares;
        GameObject phantom;
        DeadAirMonsterMotion phantomMotion;
        readonly Dictionary<string, bool> doorStates = new Dictionary<string, bool>();
        int lastScare, lastCollectibles, lastRadioSequence, lastSpider, lastEncounter;
        bool lastEnemyActive;
        string lastEnemyBehaviour;
        bool lastBroadcast, lastPower, lastCanister, lastValve, lastCalibrated, wasDown;
        string lastPhase, lastHiding;
        Vector3 previousPosition;
        float nextPhantom, phantomUntil, nextFlash, flashUntil, nextVisualScare;

        public void Initialize(DeadAirSession game, DeadAirPlayer actor)
        {
            session = game; player = actor;
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
                    CaptureState(state, me);
                    float now = Time.unscaledTime;
                    nextPhantom = now + 24; nextFlash = now + 32;
                    nextVisualScare = now + 5;
                    previousPosition = player.transform.position;
                }
                else if (phase == "won" || phase == "lost") DeadAirAudio.Play(phase == "won" ? "win" : "lose");
                lastPhase = phase;
            }
            bool playing = phase == "playing" && me != null;
            bool paused = !playing || state.paused || player.LocalPaused || player.ModalOpen;
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
            audioEngine.Tick(paused, moving, playing && player.Sprinting, hiding, player.Stamina, playing ? me.sanity : 100,
                threat, playing && player.CameraUp, playing && state.power, playing && state.broadcast, enemyPosition, distance);
            if (tape != null)
            {
                tape.Threat = Mathf.Lerp(tape.Threat, paused ? 0 : threat, Time.unscaledDeltaTime * 2);
                tape.Insanity = playing ? 1 - me.sanity / 100 : 0;
                tape.Recording = playing && player.CameraUp;
                tape.Nightmare = playing && state.enemy.bloody ? 1 : 0;
                tape.Flash = !paused && Time.unscaledTime < flashUntil ? .16f : 0;
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
            int collectibles = state.tapes.Count + state.fuses.Count + state.keycards.Count;
            if (collectibles > lastCollectibles || state.canister && !lastCanister) DeadAirAudio.Play("pickup");
            if (state.power && !lastPower) DeadAirAudio.Play("power");
            if (state.valveOpened && !lastValve) DeadAirAudio.Play("click");
            if (state.calibrated && !lastCalibrated) DeadAirAudio.Play("radio");
            if (state.scare > lastScare || state.broadcast && !lastBroadcast) DeadAirAudio.Play("scare");
            if (me.spiderScareSerial > lastSpider && state.elapsed * 1000 - me.spiderScareAt < 1800)
            {
                scares?.Spider();
                audioEngine.Emit("spider", .85f);
            }
            if (state.enemy.encounterSerial > lastEncounter && state.enemy.behaviour == "peek")
                audioEngine.Emit("listening", .65f, 0, new Vector3(-state.enemy.x, state.enemy.y + 1.5f, state.enemy.z));
            if (lastEnemyBehaviour == "peek" && state.enemy.behaviour == "vanished")
                audioEngine.Emit("vanish", .55f, 0, new Vector3(-state.enemy.x, state.enemy.y + 1.3f, state.enemy.z));
            bool visualBreach = state.scare > lastScare && (!lastEnemyActive && state.enemy.active || me.sanity < 45);
            if (visualBreach && !state.creative && !me.down && string.IsNullOrEmpty(me.hiding) && Time.unscaledTime >= nextVisualScare)
            {
                scares?.Signal();
                audioEngine.Emit("screamer", .6f);
                nextVisualScare = Time.unscaledTime + 32;
            }
            if (me.hiding != lastHiding) DeadAirAudio.Play("locker");
            if (!me.down && wasDown) DeadAirAudio.Play("pickup");
            foreach (var door in state.doors)
                if (doorStates.TryGetValue(door.id, out bool open) && open != door.open) DeadAirAudio.Play("door");
            if (state.radioSequence != lastRadioSequence && state.radioSenderId != session.LocalId)
                DeadAirAudio.Play("radio");
            CaptureState(state, me);
        }

        void CaptureState(RoomState state, PlayerState me)
        {
            lastCollectibles = state.tapes.Count + state.fuses.Count + state.keycards.Count;
            lastScare = state.scare; lastBroadcast = state.broadcast; lastPower = state.power;
            lastCanister = state.canister; lastValve = state.valveOpened; lastCalibrated = state.calibrated;
            lastHiding = me == null ? null : me.hiding; wasDown = me != null && me.down;
            lastRadioSequence = state.radioSequence;
            lastSpider = me == null ? 0 : me.spiderScareSerial;
            lastEncounter = state.enemy.encounterSerial;
            lastEnemyActive = state.enemy.active;
            lastEnemyBehaviour = state.enemy.behaviour;
            doorStates.Clear();
            foreach (var door in state.doors) doorStates[door.id] = door.open;
        }

        void Hallucinations(PlayerState me)
        {
            float now = Time.unscaledTime;
            if (me.sanity < 40 && now >= nextPhantom && phantom != null)
            {
                var direction = Quaternion.Euler(0, UnityEngine.Random.value > .5f ? 47 : -47, 0) * player.transform.forward;
                phantom.transform.position = player.transform.position + direction * 6.5f;
                phantom.SetActive(true);
                phantomUntil = now + 7;
                nextPhantom = now + UnityEngine.Random.Range(18, 32);
            }
            if (phantom != null && phantom.activeSelf)
            {
                phantomMotion?.SetPreviewPose("search", now);
                var target = player.transform.position;
                target.y = phantom.transform.position.y;
                phantom.transform.LookAt(target);
                if (now > phantomUntil || Vector3.Distance(phantom.transform.position, player.transform.position) < 3 || player.FlashlightOn && player.CameraUp)
                {
                    phantom.SetActive(false);
                    audioEngine.Emit("whisper", .8f, UnityEngine.Random.Range(-.8f, .8f));
                }
            }
            if (DeadAirSettings.Flicker && me.sanity < 22 && now >= nextFlash)
            {
                DeadAirAudio.Play("screamer");
                flashUntil = now + .1f;
                nextFlash = now + UnityEngine.Random.Range(22, 37);
            }
        }

        void OnDestroy() { if (session != null) session.Error -= OnError; }
    }
}
