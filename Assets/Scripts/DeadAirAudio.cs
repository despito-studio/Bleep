using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    // File-backed cues; musical layers share a DSP clock in DeadAirAdaptiveScore.
    public sealed partial class DeadAirAudio : MonoBehaviour
    {
        const int Rate = 32000;
        static DeadAirAudio instance;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> voices = new List<AudioSource>();
        readonly Dictionary<AudioSource, float> voiceLevels = new Dictionary<AudioSource, float>();
        readonly Dictionary<string, AudioSource> roleVoices = new Dictionary<string, AudioSource>();
        readonly Dictionary<AudioSource, string> voiceOwners = new Dictionary<AudioSource, string>();
        AudioSource hum, air, radio, dread, tinnitus, generator, fluorescent, scareVoice, tripLoop;
        DeadAirAdaptiveScore adaptiveScore;
        string phase = "menu", surface = "tile", enemyBehaviour;
        bool outside, down, gameplayPaused;
        float musicDuckUntil, scareLevel = 1, tripIntensity, tripOnsetUntil;
        bool tripPaused;
        float master = .5f, ambience = 1, effects = 1;
        int voiceIndex, stepSide;
        float nextStep, nextHeart, nextBreath, nextMonster, nextWhisper, nextAlarm, nextKnock;
        bool wasMoving;

        public void Initialize()
        {
            if (clips.Count > 0) return;
            instance = this;
            LoadImportedCues();
            for (int i = 0; i < 32; i++)
            {
                var voice = Source("Effect voice " + (i + 1), false);
                voices.Add(voice); voiceLevels.Add(voice, 1);
            }
            // Scares never enter the shared foley pool: footsteps and remote role actions
            // cannot steal the source while its scream or impact is still releasing.
            scareVoice = Source("Dedicated fullscreen scream / impact", false);
            scareVoice.priority = 8;
            tripLoop = Source("Mushroom trip / glassy flutter bed", true);
            tripLoop.clip = clips["mushroom-bed"]; tripLoop.priority = 150;
            hum = Loop("Station transformer / 48 Hz", "hum");
            air = Loop("Station ventilation", "air");
            radio = Loop("Receiver static", "static");
            dread = Loop("Threat subharmonic", "dread");
            tinnitus = Loop("Low sanity tinnitus", "tinnitus");
            generator = Loop("Generator motor", "generator-bed");
            fluorescent = Loop("Station fluorescent ballast", "fluorescent-bed");
            generator.spatialBlend = 1; generator.minDistance = 2.5f; generator.maxDistance = 24;
            var powerObject = WorldData.Load().Objective("power");
            if (powerObject != null) generator.transform.position = new Vector3(-powerObject.x, powerObject.y + .8f, powerObject.z);
            adaptiveScore = new DeadAirAdaptiveScore(transform);
            InitializeRooms();
            nextKnock = Time.unscaledTime + UnityEngine.Random.Range(14, 26);
            ApplySettings();
            DeadAirSettings.Changed += ApplySettings;
        }

        AudioSource Source(string title, bool loop)
        {
            var go = new GameObject(title);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0;
            source.dopplerLevel = 0;
            source.minDistance = 2;
            source.maxDistance = 16;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.reverbZoneMix = 0;
            source.priority = loop ? 180 : 100;
            return source;
        }

        AudioSource Loop(string title, string clip)
        {
            var source = Source(title, true);
            source.clip = clips[clip];
            if (Application.isPlaying) source.Play();
            return source;
        }

        public void SetContext(string currentPhase, bool isOutside, bool isDown, string behaviour, bool onStairs)
        {
            phase = currentPhase; outside = isOutside; down = isDown; enemyBehaviour = behaviour;
            surface = onStairs ? "metal" : outside ? "gravel" : "tile";
        }

        public void SetSnowSurface(bool snow) { if (snow) surface = "snow"; }

        public static void ApplySettings()
        {
            if (instance == null) return;
            instance.master = PlayerPrefs.GetInt("DeadAir.Muted", 0) != 0 ? 0 : Mathf.Clamp01(PlayerPrefs.GetFloat("DeadAir.Volume", .5f));
            instance.ambience = Mathf.Clamp01(PlayerPrefs.GetFloat("DeadAir.AmbienceVolume", 1));
            instance.effects = Mathf.Clamp01(PlayerPrefs.GetFloat("DeadAir.EffectsVolume", 1));
            foreach (var voice in instance.voices) voice.volume = instance.master * instance.effects * .85f * instance.voiceLevels[voice];
            if (instance.scareVoice != null) instance.scareVoice.volume = instance.master * instance.effects * .95f * instance.scareLevel;
            if (instance.tripLoop != null) instance.tripLoop.volume = instance.tripPaused ? 0 : instance.master * instance.effects * instance.tripIntensity * .6f;
            if (instance.master <= 0)
            {
                instance.adaptiveScore?.MuteImmediately();
                foreach (var source in instance.GetComponentsInChildren<AudioSource>()) source.volume = 0;
            }
        }

        public static void Play(string kind)
        {
            if (instance != null) instance.Emit(kind, 1, 0, null);
        }

        public static void PlayAt(string kind, Vector3 position, float level = 1)
        {
            if (instance != null) instance.Emit(kind, level, 0, position);
        }

        public static void StopScare()
        {
            if (instance == null) return;
            if (instance.scareVoice != null) instance.scareVoice.Stop();
            instance.musicDuckUntil = 0;
        }

        public static void StopMushroomTrip()
        {
            if (instance == null) return;
            instance.StopTripOnset();
            if (instance.tripLoop != null) { instance.tripLoop.Stop(); instance.tripLoop.volume = 0; }
        }

        void StopTripOnset()
        {
            tripOnsetUntil = 0;
            if (!clips.TryGetValue("mushroom-trip", out var trip)) return;
            foreach (var voice in voices)
                if (voice.clip == trip) voice.Stop();
        }

        public void SetTripContext(float intensity, bool paused)
        {
            tripIntensity = Mathf.Clamp01(intensity); tripPaused = paused;
            if (tripLoop == null) return;
            if (paused || tripIntensity <= .001f)
            {
                // Intensity starts at zero while the host clock enters the trip. Preserve
                // the fresh onset through that ramp; pausing/death still cancels it now.
                if (paused || Time.unscaledTime >= tripOnsetUntil) StopTripOnset();
                if (paused) { tripLoop.volume = 0; tripLoop.Stop(); return; }
            }
            else if (Application.isPlaying && !tripLoop.isPlaying) tripLoop.Play();
            float target = master * effects * tripIntensity * .6f * (Time.unscaledTime < musicDuckUntil ? .25f : 1);
            Fade(tripLoop, target, target > tripLoop.volume ? .3f : .55f);
            tripLoop.pitch = 1 + tripIntensity * (.035f * Mathf.Sin(Time.unscaledTime * .73f) + .012f * Mathf.Sin(Time.unscaledTime * 2.3f));
            if (target <= 0 && tripLoop.volume < .0001f) { tripLoop.volume = 0; tripLoop.Stop(); }
        }

        static bool IsScare(string kind) => kind == "spider" || kind == "screamer" || kind == "scare";

        void EmitScare(string kind, AudioClip clip, float level)
        {
            if (gameplayPaused || scareVoice == null) return;
            // An incidental threat sting cannot replace the creature currently on screen.
            if (scareVoice.isPlaying && scareVoice.clip != null && kind == "scare") return;
            scareVoice.Stop();
            scareVoice.clip = clip; scareVoice.spatialBlend = 0; scareVoice.panStereo = 0; scareVoice.pitch = 1;
            scareLevel = Mathf.Clamp01(level);
            scareVoice.volume = master * effects * .95f * scareLevel;
            scareVoice.Play();
            musicDuckUntil = Time.unscaledTime + clip.length + .25f;
            // The sting starts in this frame; the score must make room in this frame too.
            adaptiveScore?.DuckImmediately(.14f);
            DuckRooms(.3f);
            foreach (var bed in new[] { hum, air, radio, dread, tinnitus, generator, fluorescent })
                if (bed != null) bed.volume *= .3f;
            if (tripLoop != null) tripLoop.volume *= .25f;
        }

        public void StopLocalRoleAction()
        {
            foreach (var voice in voices)
                if (voice.spatialBlend == 0 && voice.clip != null && voice.clip.name.Contains("role-"))
                {
                    if (voiceOwners.TryGetValue(voice, out string actorId)) StopRoleAction(actorId);
                    else voice.Stop();
                }
        }

        public void PlayRoleAction(string actorId, string characterClass, Vector3? position)
        {
            StopRoleAction(actorId);
            Emit("role-" + characterClass, 1, 0, position, actorId);
        }

        public void StopRoleAction(string actorId)
        {
            if (!roleVoices.TryGetValue(actorId, out var voice)) return;
            voice.Stop(); roleVoices.Remove(actorId); voiceOwners.Remove(voice);
        }

        public void StopRoleActions()
        {
            foreach (var voice in roleVoices.Values) voice.Stop();
            roleVoices.Clear(); voiceOwners.Clear();
        }

        public void UpdateRolePosition(string actorId, Vector3 position)
        {
            if (roleVoices.TryGetValue(actorId, out var voice) && voice.spatialBlend > 0) voice.transform.position = position;
        }

        public void Emit(string kind, float level = 1, float pan = 0, Vector3? position = null, string ownerId = null)
        {
            if (!Application.isPlaying || !clips.TryGetValue(kind, out var clip) || voices.Count == 0) return;
            if (IsScare(kind)) { EmitScare(kind, clip, level); return; }
            if (gameplayPaused && (kind == "mushroom-eat" || kind == "mushroom-trip")) return;
            // Reuse idle voices first so an interface click cannot cut off the ending chord.
            AudioSource source = null;
            for (int i = 0; i < voices.Count; i++)
            {
                var candidate = voices[(voiceIndex + i) % voices.Count];
                if (!candidate.isPlaying) { source = candidate; voiceIndex = (voiceIndex + i + 1) % voices.Count; break; }
            }
            if (source == null)
            {
                // Dialogue and a character's full fall must survive busy multiplayer foley.
                for (int i = 0; i < voices.Count; i++)
                {
                    var candidate = voices[(voiceIndex + i) % voices.Count];
                    if (candidate.priority <= 40 || voiceOwners.ContainsKey(candidate)) continue;
                    source = candidate; voiceIndex = (voiceIndex + i + 1) % voices.Count; break;
                }
                if (source == null) return;
                source.Stop();
            }
            // Ownership follows the pooled source, so cancelling one actor cannot stop a later cue.
            if (voiceOwners.TryGetValue(source, out string previousOwner))
            {
                roleVoices.Remove(previousOwner); voiceOwners.Remove(source);
            }
            if (ownerId != null) { roleVoices[ownerId] = source; voiceOwners[source] = ownerId; }
            source.spatialBlend = position.HasValue ? 1 : 0;
            if (position.HasValue) source.transform.position = position.Value;
            source.panStereo = Mathf.Clamp(pan, -1, 1);
            source.pitch = kind.Contains("step") || kind == "door" || kind == "locker" ? UnityEngine.Random.Range(.94f, 1.06f) : 1;
            source.volume = master * effects * .85f;
            source.clip = clip;
            voiceLevels[source] = Mathf.Clamp01(level);
            source.volume *= voiceLevels[source];
            source.priority = ownerId != null || kind.StartsWith("fall-", StringComparison.Ordinal) ? 40 : 100;
            source.Play();
            if (kind == "mushroom-trip") tripOnsetUntil = Time.unscaledTime + clip.length;
        }

        public void Tick(bool paused, bool moving, bool sprinting, bool hiding, float stamina, float sanity,
            float threat, bool camera, bool power, bool broadcast, Vector3? monsterPosition, float monsterDistance, bool breakerOn = true, bool crouching = false)
        {
            float now = Time.unscaledTime;
            gameplayPaused = paused;
            if (paused && scareVoice != null && scareVoice.isPlaying) StopScare();
            if (paused) StopMushroomTrip();
            float insanity = 1 - Mathf.Clamp01(sanity / 100);
            float duck = now < musicDuckUntil ? .14f : 1;
            float bed = paused ? 0 : master * ambience * (hiding ? .35f : 1) * duck;
            Fade(hum, bed * (power && breakerOn && !outside ? 1.45f : 0), .7f);
            Fade(air, bed * (power && breakerOn && !outside ? (.035f + threat * .042f) : 0), .5f);
            Fade(radio, bed * (camera ? .02f + threat * .018f : .002f), .18f);
            Fade(dread, bed * (threat * .05f + insanity * .02f), .3f);
            dread.pitch = (71 + Mathf.Sin(now * .24f) * 1.5f + threat * 10) / 71;
            Fade(tinnitus, bed * Mathf.Max(0, insanity - .45f) * .03f, .5f);
            Fade(generator, bed * (power ? .95f : 0), 1.1f);
            Fade(fluorescent, bed * (power && breakerOn && !outside ? .11f : 0), .7f);
            bool inRun = phase == "playing";
            float danger = Mathf.Clamp01(threat * 1.15f + Mathf.Max(0, insanity - .35f) * .55f);
            float pursuit = inRun && !paused && !down && !hiding && monsterPosition.HasValue &&
                (enemyBehaviour == "pursuit" || broadcast || threat > .78f) ? Mathf.Max(.6f, threat) : 0;
            adaptiveScore.Tick(phase, paused, moving, sprinting, hiding, down, danger, pursuit, master * ambience * duck);
            TickRooms(paused || !inRun, bed);
            if (paused)
            {
                wasMoving = false;
                nextStep = now + .1f;
                nextHeart = now + .4f;
                return;
            }
            if (moving && !hiding)
            {
                if (!wasMoving) nextStep = now + .06f;
                if (now >= nextStep)
                {
                    stepSide = stepSide == 1 ? -1 : 1;
                    string step = surface == "tile" ? sprinting ? "run-step" : "step" : (sprinting ? "run-step-" : "step-") + surface;
                    Emit(step, crouching ? .25f : 1, stepSide * .12f);
                    nextStep = now + (crouching ? .75f : sprinting ? .31f : .52f) + UnityEngine.Random.Range(0, .035f);
                }
            }
            wasMoving = moving;
            if (stamina < 30 && now >= nextBreath)
            {
                Emit("breath", 1 - stamina / 30, UnityEngine.Random.Range(-.1f, .1f));
                nextBreath = now + UnityEngine.Random.Range(.85f, 1.05f);
            }
            if ((threat > .15f || hiding && threat > .05f || insanity > .6f) && now >= nextHeart)
            {
                Emit("heartbeat", .35f + threat * .65f);
                nextHeart = now + 1.2f - threat * .72f;
            }
            if (monsterPosition.HasValue && monsterDistance < 16 && now >= nextMonster)
            {
                Emit("monster", 1, 0, monsterPosition);
                nextMonster = now + UnityEngine.Random.Range(1.6f, 3.8f) - (broadcast ? .8f : 0);
            }
            if (insanity > .45f && now >= nextWhisper)
            {
                Emit("whisper", insanity, UnityEngine.Random.Range(-.9f, .9f));
                nextWhisper = now + UnityEngine.Random.Range(6, 16);
            }
            if (broadcast && now >= nextAlarm) { Emit("alarm"); nextAlarm = now + 2.4f; }
            if (now >= nextKnock)
            {
                Emit(UnityEngine.Random.value > .55f ? "double-knock" : "knock", .42f, UnityEngine.Random.Range(-.8f, .8f));
                nextKnock = now + UnityEngine.Random.Range(16, 36);
            }
        }

        static void Fade(AudioSource source, float target, float seconds)
        {
            source.volume = Mathf.Lerp(source.volume, Mathf.Clamp01(target), 1 - Mathf.Exp(-Time.unscaledDeltaTime / seconds));
        }

        public void ResetRun()
        {
            StopScare(); gameplayPaused = false;
            adaptiveScore?.ResetRun();
            tripIntensity = tripOnsetUntil = 0; tripPaused = false;
            if (tripLoop != null) { tripLoop.Stop(); tripLoop.volume = 0; }
            foreach (var source in voices) source.Stop();
            roleVoices.Clear(); voiceOwners.Clear();
            float now = Time.unscaledTime;
            nextStep = now + .1f; nextHeart = now + .4f; nextBreath = now;
            nextMonster = now; nextWhisper = now + 6; nextAlarm = now;
            nextKnock = now + UnityEngine.Random.Range(14, 26); wasMoving = false;
        }

        // Streaming/compressed clips are audited from source WAVs by the Editor validator.
        public void ValidateInstruments()
        {
            float largestPeak = 0;
            foreach (var entry in clips)
            {
                if (entry.Value.samples <= 0 || entry.Value.frequency != 48000)
                    throw new InvalidOperationException("Invalid imported audio metadata: " + entry.Key);
                if (entry.Value.loadType != AudioClipLoadType.DecompressOnLoad) continue;
                if (entry.Value.loadState != AudioDataLoadState.Loaded) entry.Value.LoadAudioData();
                var values = new float[entry.Value.samples * entry.Value.channels];
                if (!entry.Value.GetData(values, 0)) throw new InvalidOperationException("Cannot read audio: " + entry.Key);
                double energy = 0;
                float peak = 0;
                foreach (float value in values)
                {
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Invalid audio sample: " + entry.Key);
                    peak = Mathf.Max(peak, Mathf.Abs(value));
                    energy += value * value;
                }
                double rms = Math.Sqrt(energy / values.Length);
                if (peak > 1 || rms < .000001) throw new InvalidOperationException("Clipped or silent audio: " + entry.Key);
                if (IsScare(entry.Key))
                {
                    double tailEnergy = 0;
                    int tail = Mathf.Min(values.Length, entry.Value.frequency * entry.Value.channels / 50);
                    for (int i = values.Length - tail; i < values.Length; i++) tailEnergy += values[i] * values[i];
                    if (Math.Sqrt(tailEnergy / tail) > .002)
                        throw new InvalidOperationException("Cue must naturally release to silence: " + entry.Key);
                    if (IsScare(entry.Key) && rms < .018)
                        throw new InvalidOperationException("Scare cue lacks audible impact: " + entry.Key);
                }
                largestPeak = Mathf.Max(largestPeak, peak);
                Debug.Log("DEAD AIR AUDIO: " + entry.Key + " peak=" + peak.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + " rms=" + rms.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
            }
            Debug.Log("DEAD AIR AUDIO VALIDATED: " + clips.Count + " imported cues; decoded short cues finite, maximum peak=" + largestPeak.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
        }

        public int GeneratedClipCount => clips.Count;

        // In the Editor, copy lossless source files so streamed clips need no PCM decode.
        public int ExportLibrary(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            var looping = new HashSet<AudioClip>();
            foreach (var source in GetComponentsInChildren<AudioSource>())
                if (source.loop && source.clip != null) looping.Add(source.clip);
            var exportClips = new Dictionary<string, AudioClip>(clips);
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Music")) exportClips[clip.name] = clip;
            foreach (var entry in exportClips)
            {
                AudioClip clip = entry.Value;
#if UNITY_EDITOR
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(clip);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    System.IO.File.Copy(assetPath, System.IO.Path.Combine(folder, entry.Key + ".wav"), true);
                    continue;
                }
#endif
                var values = new float[clip.samples * clip.channels];
                if (!clip.GetData(values, 0)) throw new InvalidOperationException("Cannot export audio: " + entry.Key);
                bool isLoop = looping.Contains(clip);
                using (var writer = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(folder, entry.Key + ".wav"))))
                {
                    int bytes = values.Length * 2;
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes + (isLoop ? 68 : 0));
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
                    writer.Write((short)clip.channels); writer.Write(clip.frequency); writer.Write(clip.frequency * clip.channels * 2);
                    writer.Write((short)(clip.channels * 2)); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                    foreach (float sample in values) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1, 1) * 32767));
                    if (isLoop)
                    {
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("smpl")); writer.Write(60);
                        writer.Write(0); writer.Write(0); writer.Write(Mathf.RoundToInt(1000000000f / clip.frequency));
                        writer.Write(60); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(1); writer.Write(0);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(clip.samples - 1); writer.Write(0); writer.Write(0);
                    }
                }
            }
            return exportClips.Count;
        }

        void OnDestroy()
        {
            DeadAirSettings.Changed -= ApplySettings;
            if (instance == this) instance = null;
            // Resources owns imported clips. Destroying them breaks other listeners/export tools.
            clips.Clear();
        }

        void Add(string key, Action<Instrument> compose, float seconds = 4)
        {
            var instrument = new Instrument(seconds, clips.Count * 7919 + 1096);
            compose(instrument);
            clips.Add(key, instrument.Clip(key));
        }

        void BuildInstruments()
        {
            Add("click", b => b.Tone(0, 810, 320, .035f, .045f, Wave.Triangle), .08f);
            Add("pickup", b => { b.Knock(0, .45f); b.Tone(.055f, 520, 390, .16f, .045f, Wave.Triangle); b.Noise(.1f, .17f, .038f, 2400); }, .35f);
            Add("power", b => { b.Knock(0, .95f); b.Knock(.15f, .55f); b.Tone(.15f, 31, 96, 1.25f, .14f, Wave.Triangle, .14f); b.Noise(.05f, .65f, .11f, 900, .015f); }, 1.8f);
            Add("error", b => { b.Tone(0, 132, 113, .1f, .048f, Wave.Triangle); b.Tone(.14f, 132, 113, .1f, .048f, Wave.Triangle); }, .3f);
            Add("scare", b => b.HorrorSting(0), 1.9f);
            Add("screamer", b => b.HorrorSting(1), 1.55f);
            Add("spider", b => b.HorrorSting(2), 1.55f);
            Add("mushroom-eat", b => {
                b.Noise(0, .15f, .14f, 1550, .004f); b.Knock(.025f, .75f);
                b.Noise(.16f, .24f, .085f, 720, .015f); b.Tone(.26f, 190, 85, .33f, .07f, Wave.Triangle, .018f);
            }, .9f);
            Add("mushroom-trip", b => b.MushroomBloom(), 3.8f);
            Add("listening", b => { b.Noise(0, .6f, .035f, 620, .12f); b.Tone(.1f, 72, 49, .8f, .04f, Wave.Triangle, .12f); }, 1.1f);
            Add("vanish", b => { b.Noise(0, .2f, .065f, 1700, .02f); b.Tone(.015f, 210, 38, .35f, .06f, Wave.Triangle, .025f); }, .5f);
            Add("radio", b => { b.Noise(0, .04f, .08f, 3200); b.Tone(.03f, 1240, 1100, .08f, .05f); b.Noise(.09f, .25f, .07f, 1800, .02f); }, .45f);
            Add("locker", b => { b.Knock(0, .7f); b.Tone(.03f, 180, 75, .15f, .06f, Wave.Triangle); }, .4f);
            Add("door", b => { b.Noise(0, .3f, .05f, 850, .015f); b.Tone(.02f, 170, 95, .3f, .025f, Wave.Triangle); b.Knock(.3f, .7f); }, .7f);
            Add("alarm", b => { b.Tone(0, 440, 330, .9f, .09f, Wave.Saw, .08f); b.Tone(.95f, 330, 440, .9f, .08f, Wave.Saw, .08f); }, 2.1f);
            Add("win", b => { float[] tones = { 164.81f, 246.94f, 329.63f, 369.99f }; for (int i = 0; i < tones.Length; i++) b.Tone(i * .43f, tones[i], tones[i] * .998f, 2.1f, .078f, Wave.Sine, .07f); });
            Add("lose", b => { b.Tone(0, 86, 23, 2.2f, .16f, Wave.Triangle, .035f); b.Noise(0, 1.25f, .14f, 750, .05f); }, 2.5f);
            for (int s = 0; s < 2; s++)
            {
                float level = s == 1 ? 1.15f : .8f;
                Add(s == 1 ? "run-step" : "step", b => { b.Tone(0, 83, 34, .16f, .094f * level); b.Noise(0, .105f, .09f * level, 450, .003f); b.Noise(.075f, .11f, .025f * level, 1500); }, .3f);
            }
            Add("breath", b => { b.Noise(0, .38f, .05f, 540, .08f); b.Noise(.45f, .42f, .04f, 460, .09f); }, 1);
            Add("heartbeat", b => { b.Tone(0, 62, 36, .14f, .1f, Wave.Sine, .012f); b.Tone(.19f, 56, 32, .17f, .075f, Wave.Sine, .01f); }, .5f);
            Add("monster", b => { b.Knock(0, .7f); b.Noise(.05f, .22f, .06f, 1400, .01f); b.Tone(.08f, 45, 22, .35f, .08f, Wave.Triangle); }, .6f);
            Add("whisper", b => { b.Noise(0, .7f, .05f, 1100, .1f); b.Tone(.05f, 220, 180, .6f, .02f); }, .9f);
            Add("knock", b => b.Knock(0, 1), .4f);
            Add("double-knock", b => { b.Knock(0, 1); b.Knock(.28f, .57f); }, .7f);
            Add("hum", b => b.Bed(0), 8);
            Add("air", b => b.Bed(1), 8);
            Add("static", b => b.Bed(2), 8);
            Add("dread", b => b.Bed(3), 8);
            Add("tinnitus", b => b.Bed(4), 8);
            Add("teke-crawl", b => { b.Knock(0, .8f); b.Noise(.02f, .18f, .07f, 1600, .01f); b.Knock(.12f, .6f); }, .35f);
            Add("teke-scream", b => { b.Tone(0, 1100, 750, .9f, .12f, Wave.Saw, .08f); b.Noise(.05f, .8f, .09f, 3200, .05f); b.HorrorSting(1); }, 1.8f);
            Add("teke-groan", b => { b.Tone(0, 160, 110, 1.4f, .11f, Wave.Triangle, .08f); b.Noise(.1f, 1.2f, .06f, 540, .09f); }, 1.6f);
            AddProceduralScoreAndFoley();
        }

        enum Wave { Sine, Triangle, Saw }

        sealed partial class Instrument
        {
            float[] samples;
            readonly System.Random random;
            bool loop, stereo;
            public Instrument(float seconds, int seed) { samples = new float[Mathf.CeilToInt(seconds * Rate)]; random = new System.Random(seed); }

            static double Envelope(double time, double duration, double gain, double attack)
            {
                attack = Math.Min(attack, duration * .45);
                if (time < attack) return gain * time / Math.Max(.0001, attack);
                if (time <= duration) return gain * Math.Pow(.0001 / gain, (time - attack) / (duration - attack));
                return .0001 * Math.Max(0, 1 - (time - duration) / .025);
            }

            public void Tone(float at, float start, float end, float duration, float gain, Wave wave = Wave.Sine, float attack = .007f)
            {
                int offset = Mathf.RoundToInt(at * Rate), count = Mathf.CeilToInt((duration + .025f) * Rate);
                double phase = 0, logRatio = Math.Log(Math.Max(10, end) / start);
                for (int i = 0; i < count && i + offset < samples.Length; i++)
                {
                    double t = i / (double)Rate, frequency = start * Math.Exp(logRatio * Math.Min(1, t / duration));
                    phase += frequency / Rate;
                    double signal = 0;
                    // Band limited partials avoid harsh digital aliasing on the falling sawtooth stingers.
                    if (wave == Wave.Sine) signal = Math.Sin(phase * Math.PI * 2);
                    else if (wave == Wave.Triangle)
                        for (int k = 1; k <= 9 && frequency * k < Rate * .45; k += 2)
                            signal += Math.Sin(phase * Math.PI * 2 * k) * (((k - 1) / 2) % 2 == 0 ? 1 : -1) / (k * k) * .810569;
                    else
                        for (int k = 1; k <= 12 && frequency * k < Rate * .45; k++) signal += -Math.Sin(phase * Math.PI * 2 * k) / k * .63662;
                    samples[offset + i] += (float)(signal * Envelope(t, duration, gain, attack));
                }
            }

            public void Noise(float at, float duration, float gain, float frequency, float attack = .007f)
            {
                int offset = Mathf.RoundToInt(at * Rate), count = Mathf.CeilToInt((duration + .025f) * Rate);
                var filter = new BandPass(frequency);
                for (int i = 0; i < count && offset + i < samples.Length; i++)
                {
                    double n = random.NextDouble() * 2 - 1;
                    samples[offset + i] += (float)(filter.Tick(n) * Envelope(i / (double)Rate, duration, gain, attack));
                }
            }

            public void Knock(float at, float level)
            {
                Tone(at, 183, 183, .24f, .065f * level, Wave.Sine, .002f);
                Tone(at, 411, 411, .15f, .023f * level, Wave.Sine, .002f);
                Tone(at, 687, 687, .08f, .012f * level, Wave.Sine, .002f);
                Noise(at, .04f, .04f * level, 900, .002f);
            }

            public void Bed(int kind)
            {
                loop = true;
                double b0 = 0, b1 = 0, b2 = 0, low = 0, dc = 0;
                var filter = new BandPass(1800);
                for (int i = 0; i < samples.Length; i++)
                {
                    double t = i / (double)Rate, n = random.NextDouble() * 2 - 1, value;
                    if (kind == 0)
                    {
                        double drift = Math.Sin(2 * Math.PI * .125 * t) * .025;
                        value = .036 * Math.Sin(2 * Math.PI * 48 * t + drift) + .012 * Math.Asin(Math.Sin(2 * Math.PI * 96.25 * t + drift)) * 2 / Math.PI + .006 * Math.Sin(2 * Math.PI * 143.375 * t + drift);
                    }
                    else if (kind == 1)
                    {
                        b0 = .99765 * b0 + n * .099046; b1 = .963 * b1 + n * .2965164; b2 = .57 * b2 + n * 1.0526913;
                        double pink = (b0 + b1 + b2 + n * .1848) * .11;
                        low += .218 * (pink - low); dc += .0204 * (low - dc); value = low - dc;
                    }
                    else if (kind == 2) value = filter.Tick(n);
                    else value = Math.Sin(2 * Math.PI * (kind == 3 ? 71 : 5200) * t);
                    samples[i] = (float)value;
                }
                // Overlap the tail with the head, then start after that overlap. Blending into
                // the head without trimming it would jump backwards by 50 ms at every loop.
                if (kind == 1 || kind == 2)
                {
                    int fade = Rate / 20;
                    var periodic = new float[samples.Length - fade];
                    Array.Copy(samples, fade, periodic, 0, periodic.Length);
                    for (int i = 0; i < fade; i++)
                        periodic[periodic.Length - fade + i] = Mathf.Lerp(samples[samples.Length - fade + i], samples[i], i / (float)(fade - 1));
                    samples = periodic;
                }
            }

            public AudioClip Clip(string name)
            {
                if (!loop)
                {
                    // A restrained early reflection recreates the original 14% filtered reverb send.
                    var dry = (float[])samples.Clone();
                    int[] delays = { 2656, 4896, 7712 };
                    float[] amounts = { .065f, .045f, .03f };
                    for (int d = 0; d < delays.Length; d++)
                        for (int i = delays[d]; i < samples.Length; i++) samples[i] += dry[i - delays[d]] * amounts[d];
                    // End every one-shot at silence even if a future layer reaches the clip
                    // boundary. This prevents clicks when the dedicated scare voice releases.
                    int release = Mathf.Min(Rate / 40, samples.Length);
                    for (int i = 0; i < release; i++) samples[samples.Length - release + i] *= 1 - i / (float)Mathf.Max(1, release - 1);
                }
                // Filtering bounded noise can overshoot its input range. Preserve its waveform
                // with one gain reduction after all layering and loop crossfades are complete.
                float peak = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]))
                        throw new InvalidOperationException("Invalid generated audio sample: " + name);
                    peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                }
                if (peak > .9f)
                {
                    float gain = .9f / peak;
                    for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
                }
                float[] output = samples;
                if (stereo)
                {
                    output = new float[samples.Length * 2];
                    int delay = Rate / 83;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float delayed = loop ? samples[(i + samples.Length - delay) % samples.Length] : i >= delay ? samples[i - delay] : 0;
                        output[i * 2] = samples[i] * .82f + delayed * .18f;
                        output[i * 2 + 1] = samples[i] * .82f - delayed * .18f;
                    }
                    if (!loop)
                    {
                        int release = Mathf.Min(Rate / 40, samples.Length);
                        for (int i = 0; i < release; i++)
                        {
                            float gain = 1 - i / (float)Mathf.Max(1, release - 1);
                            int index = (samples.Length - release + i) * 2;
                            output[index] *= gain; output[index + 1] *= gain;
                        }
                    }
                }
                var result = AudioClip.Create("DEAD AIR / " + name, samples.Length, stereo ? 2 : 1, Rate, false);
                result.SetData(output, 0);
                return result;
            }
        }

        sealed class BandPass
        {
            readonly double b0, b2, a1, a2;
            double x1, x2, y1, y2;
            public BandPass(double frequency)
            {
                double omega = 2 * Math.PI * frequency / Rate, alpha = Math.Sin(omega) / (2 * .65), a0 = 1 + alpha;
                b0 = alpha / a0; b2 = -b0; a1 = -2 * Math.Cos(omega) / a0; a2 = (1 - alpha) / a0;
            }
            public double Tick(double value)
            {
                double output = b0 * value + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = value; y2 = y1; y1 = output;
                return output;
            }
        }
    }

    // Protect the final mix when multiple cues overlap. At normal game levels it is transparent.
    // Runs on the listener's audio thread, without allocations or Unity object access.
    public sealed class DeadAirAudioLimiter : MonoBehaviour
    {
        double gain = 1, release;
        void Awake() { release = Math.Exp(-1.0 / (Math.Max(8000, AudioSettings.outputSampleRate) * .15)); }
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return;
            for (int frame = 0; frame < data.Length; frame += channels)
            {
                double peak = 0;
                for (int c = 0; c < channels && frame + c < data.Length; c++) peak = Math.Max(peak, Math.Abs(data[frame + c]));
                double target = peak > .9 ? .9 / peak : 1;
                gain = target < gain ? target : target + (gain - target) * release;
                for (int c = 0; c < channels && frame + c < data.Length; c++) data[frame + c] *= (float)gain;
            }
        }
    }
}
