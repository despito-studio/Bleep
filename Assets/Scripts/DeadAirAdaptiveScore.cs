using System;
using UnityEngine;

namespace DeadAir
{
    [Serializable]
    public sealed class DeadAirMusicDiagnostics
    {
        public double dspTime, epoch, transitionAt;
        public bool paused, ready, clockStarted;
        public float[] groupWeights, targetWeights, pendingWeights;
        public int[] stemPositions;
        public float[] stemGains;
        public bool[] stemPlaying;
        public string[] stemNames;
        public long decodedMemoryBytes;
        public int scheduledTransitions;
    }

    /// <summary>
    /// An eight-layer file score sharing one DSP clock. Instrument layers are
    /// composed independently; only their gain changes during gameplay.
    /// </summary>
    public sealed class DeadAirAdaptiveScore
    {
        public const int SampleRate = 48000;
        public const int LoopFrames = 2304000;
        public const double BarSeconds = 1.5;
        public const double LoopSeconds = 48;
        const float MusicBusGain = .75f;
        const float FadeSpeed = 2.8f;
        const double ScheduleLeadSeconds = .25;
        const int LayerCount = 8;

        static readonly string[] Names = {
            "exploration-bed", "exploration-pulse", "tension-bed", "tension-pulse",
            "pursuit-atmosphere", "pursuit-kick", "pursuit-bass-drums", "pursuit-lead"
        };
        static readonly int[] Groups = { 0, 0, 1, 1, 2, 2, 2, 2 };
        readonly AudioSource[] stems = new AudioSource[LayerCount];
        readonly bool[] loadFailureReported = new bool[LayerCount];
        readonly float[] weights = { 1, 0, 0 };
        readonly float[] targets = { 1, 0, 0 };
        readonly float[] pending = { 1, 0, 0 };
        readonly float[] layerLevels = { 1, .65f, 1, .45f, .6f, 1, .75f, .2f };
        readonly AudioSource menu;
        bool validAssets = true, clockStarted, menuStarted, isPaused, rescheduleAfterPause, menuLoadFailureReported;
        double epoch, pauseDsp, transitionAt = -1;
        float menuLevel;
        int scheduledTransitions;

        public DeadAirAdaptiveScore(Transform parent)
        {
            for (int i = 0; i < LayerCount; i++)
            {
                stems[i] = MakeSource(parent, "Adaptive music / " + Names[i]);
                AudioClip clip = Resources.Load<AudioClip>("Audio/Music/" + Names[i]);
                stems[i].clip = clip;
                if (clip == null || clip.frequency != SampleRate || clip.samples != LoopFrames || clip.channels != 2)
                {
                    validAssets = false;
                    Debug.LogWarning("Dead Air adaptive music requires a 48 kHz stereo, 48-second loop: " + Names[i]);
                }
                else if (Application.isPlaying && clip.loadState == AudioDataLoadState.Unloaded)
                    clip.LoadAudioData();
            }
            menu = MakeSource(parent, "Menu music / An Empty Frequency");
            menu.priority = 130;
            menu.clip = Resources.Load<AudioClip>("Audio/Music/music-menu");
            if (menu.clip != null && Application.isPlaying && menu.clip.loadState == AudioDataLoadState.Unloaded)
                menu.clip.LoadAudioData();
            EnsureScheduled();
        }

        static AudioSource MakeSource(Transform parent, string title)
        {
            var child = new GameObject(title);
            if (parent != null) child.transform.SetParent(parent, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0;
            source.pitch = 1;
            source.spatialBlend = 0;
            source.dopplerLevel = 0;
            source.reverbZoneMix = 0;
            source.priority = 70;
            // A game pause is controlled explicitly and must not silence menus.
            source.ignoreListenerPause = true;
            return source;
        }

        bool ClipsReady(bool reloadUnloaded = false)
        {
            if (!validAssets) return false;
            bool ready = true;
            for (int i = 0; i < LayerCount; i++)
            {
                AudioClip clip = stems[i].clip;
                if (clip == null) { ready = false; continue; }
                // Stopping a Streaming AudioClip releases its decoder/load data
                // in a player. Reset must reload every stem before choosing one
                // common future epoch, including asynchronous background loads.
                if (reloadUnloaded && Application.isPlaying && clip.loadState == AudioDataLoadState.Unloaded)
                    clip.LoadAudioData();
                if (clip.loadState == AudioDataLoadState.Failed && !loadFailureReported[i])
                {
                    loadFailureReported[i] = true;
                    Debug.LogError("Dead Air cannot load streamed music resource Audio/Music/" + Names[i] +
                                   ". The synchronized score is waiting for all eight loops.");
                }
                if (clip.loadState != AudioDataLoadState.Loaded) ready = false;
            }
            return ready;
        }

        void EnsureScheduled()
        {
            if (!Application.isPlaying) return;
            if (!menuStarted && menu.clip != null)
            {
                if (menu.clip.loadState == AudioDataLoadState.Unloaded) menu.clip.LoadAudioData();
                if (menu.clip.loadState == AudioDataLoadState.Failed && !menuLoadFailureReported)
                {
                    menuLoadFailureReported = true;
                    Debug.LogError("Dead Air cannot load streamed menu music resource Audio/Music/music-menu.");
                }
            }
            if (!menuStarted && menu.clip != null && menu.clip.loadState == AudioDataLoadState.Loaded)
            {
                menu.PlayScheduled(AudioSettings.dspTime + ScheduleLeadSeconds);
                menuStarted = true;
            }
            if (clockStarted || isPaused || !ClipsReady(true)) return;
            epoch = AudioSettings.dspTime + ScheduleLeadSeconds;
            for (int i = 0; i < LayerCount; i++)
            {
                stems[i].timeSamples = 0;
                stems[i].PlayScheduled(epoch);
            }
            clockStarted = true;
        }

        public void Tick(string phase, bool paused, bool moving, bool sprinting, bool hiding, bool down,
                         float danger, float pursuit, float gain)
        {
            bool playing = phase == "playing";
            SetPaused(playing && paused);
            EnsureScheduled();
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            float bus = Mathf.Clamp01(gain) * MusicBusGain;
            // Menu is independent of the gameplay pause flag and transport.
            bool inMenu = phase == "menu" || phase == "lobby";
            menuLevel = Mathf.MoveTowards(menuLevel, inMenu ? .8f : 0, dt * 1.8f);
            if (menu != null) menu.volume = bus * menuLevel;

            float dangerLevel = Mathf.Clamp01(danger);
            float pursuitLevel = Mathf.Clamp01(pursuit);
            layerLevels[3] = .45f + .55f * dangerLevel;
            layerLevels[4] = .6f + .4f * dangerLevel;
            layerLevels[6] = .75f + .25f * pursuitLevel;
            layerLevels[7] = .2f + .8f * pursuitLevel;

            if (playing && !isPaused)
            {
                float threat = dangerLevel;
                float chase = pursuitLevel;
                float p = chase > .001f ? Mathf.Lerp(.6f, 1, chase) : 0;
                if (moving && sprinting && !hiding && !down) p = Mathf.Max(p, .35f);
                float t = Mathf.SmoothStep(0, .85f, threat) * (1 - p);
                if (down) t *= .6f;
                QueueWeights(1 - p - t, t, p);
                double now = AudioSettings.dspTime;
                if (transitionAt >= 0 && now >= transitionAt)
                {
                    for (int group = 0; group < 3; group++) targets[group] = pending[group];
                    transitionAt = -1;
                    scheduledTransitions++;
                }
                // Identical interpolation across all groups preserves the convex
                // gain sum and the exporter's maximum-subset headroom guarantee.
                float fade = 1 - Mathf.Exp(-FadeSpeed * dt);
                for (int group = 0; group < 3; group++)
                    weights[group] = Mathf.Lerp(weights[group], targets[group], fade);
            }
            for (int i = 0; i < LayerCount; i++)
                stems[i].volume = playing && !isPaused ? bus * weights[Groups[i]] * layerLevels[i] : 0;
        }

        void QueueWeights(float exploration, float tension, float pursuit)
        {
            // Context often changes every frame. Update a queued target without
            // postponing its already-selected bar boundary every frame.
            bool changed = Mathf.Abs(exploration - targets[0]) > .005f ||
                           Mathf.Abs(tension - targets[1]) > .005f ||
                           Mathf.Abs(pursuit - targets[2]) > .005f;
            pending[0] = Mathf.Clamp01(exploration);
            pending[1] = Mathf.Clamp01(tension);
            pending[2] = Mathf.Clamp01(pursuit);
            if (changed && transitionAt < 0 && clockStarted)
            {
                double now = AudioSettings.dspTime;
                transitionAt = now < epoch ? epoch : epoch + (Math.Floor((now - epoch) / BarSeconds) + 1) * BarSeconds;
            }
        }

        void SetPaused(bool pause)
        {
            if (pause == isPaused) return;
            double now = AudioSettings.dspTime;
            if (pause)
            {
                pauseDsp = now;
                rescheduleAfterPause = clockStarted && now < epoch;
                if (Application.isPlaying && clockStarted)
                    for (int i = 0; i < LayerCount; i++)
                        if (rescheduleAfterPause) stems[i].Stop(); else stems[i].Pause();
                isPaused = true;
                return;
            }
            isPaused = false;
            if (rescheduleAfterPause)
            {
                clockStarted = false;
                transitionAt = -1;
                rescheduleAfterPause = false;
                EnsureScheduled();
            }
            else if (clockStarted)
            {
                double elapsed = Math.Max(0, now - pauseDsp);
                epoch += elapsed;
                if (transitionAt >= 0) transitionAt += elapsed;
                if (Application.isPlaying)
                    for (int i = 0; i < LayerCount; i++) stems[i].UnPause();
            }
            else EnsureScheduled();
        }

        public void DuckImmediately(float factor)
        {
            float amount = Mathf.Clamp01(factor);
            for (int i = 0; i < LayerCount; i++) stems[i].volume *= amount;
            if (menu != null) menu.volume *= amount;
        }

        public void MuteImmediately()
        {
            for (int i = 0; i < LayerCount; i++) stems[i].volume = 0;
            if (menu != null) menu.volume = 0;
        }

        public void ResetRun()
        {
            MuteImmediately();
            if (Application.isPlaying)
                for (int i = 0; i < LayerCount; i++) stems[i].Stop();
            weights[0] = targets[0] = pending[0] = 1;
            weights[1] = weights[2] = targets[1] = targets[2] = pending[1] = pending[2] = 0;
            transitionAt = -1;
            isPaused = false;
            rescheduleAfterPause = false;
            clockStarted = false;
            EnsureScheduled();
        }

        public DeadAirMusicDiagnostics CaptureDiagnostics()
        {
            var report = new DeadAirMusicDiagnostics {
                dspTime = AudioSettings.dspTime, epoch = epoch, transitionAt = transitionAt,
                paused = isPaused, ready = ClipsReady(), clockStarted = clockStarted,
                groupWeights = (float[])weights.Clone(), targetWeights = (float[])targets.Clone(),
                pendingWeights = (float[])pending.Clone(), scheduledTransitions = scheduledTransitions,
                stemPositions = new int[LayerCount], stemGains = new float[LayerCount],
                stemPlaying = new bool[LayerCount], stemNames = (string[])Names.Clone()
            };
            for (int i = 0; i < LayerCount; i++)
            {
                report.stemPositions[i] = stems[i].timeSamples;
                report.stemGains[i] = stems[i].volume;
                report.stemPlaying[i] = stems[i].isPlaying;
                if (stems[i].clip != null)
                    report.decodedMemoryBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(stems[i].clip);
            }
            if (menu != null && menu.clip != null)
                report.decodedMemoryBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(menu.clip);
            return report;
        }
    }
}
