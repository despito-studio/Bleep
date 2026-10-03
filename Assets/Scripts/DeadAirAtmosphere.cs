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
        GameObject phantom;
        readonly Dictionary<string, bool> doorStates = new Dictionary<string, bool>();
        int lastScare, lastCollectibles, lastRadioSequence;
        bool lastBroadcast, lastPower, lastCanister, lastValve, lastCalibrated, wasDown;
        string lastPhase, lastHiding;
        Vector3 previousPosition;
        float nextPhantom, phantomUntil, nextFlash, flashUntil;

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
            }
            var prefab = Resources.Load<GameObject>("World/monster");
            if (prefab != null)
            {
                phantom = Instantiate(prefab, transform);
                phantom.name = "Low sanity peripheral hallucination";
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
                    previousPosition = player.transform.position;
                }
                else if (phase == "won" || phase == "lost") DeadAirAudio.Play(phase == "won" ? "win" : "lose");
                lastPhase = phase;
            }
            bool playing = phase == "playing" && me != null;
            bool paused = !playing || state.paused || player.LocalPaused || player.ModalOpen;
            bool hiding = playing && !string.IsNullOrEmpty(me.hiding);
            var movement = player.transform.position - previousPosition;
            previousPosition = player.transform.position;
            bool moving = playing && !paused && !me.down && !hiding && new Vector2(movement.x, movement.z).sqrMagnitude > .000004f && movement.sqrMagnitude < 1;
            float distance = playing ? Vector3.Distance(new Vector3(me.x,me.y,me.z), new Vector3(state.enemy.x,state.enemy.y,state.enemy.z)) : 100;
            float threat = playing && state.enemy.active ? Mathf.Clamp01(1 - distance / 16) : 0;
            Vector3? enemyPosition = playing && state.enemy.active ? new Vector3(-state.enemy.x, state.enemy.y+1.2f, state.enemy.z) : (Vector3?)null;
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
