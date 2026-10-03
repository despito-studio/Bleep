using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    // The WebAudio instruments in src/audio.js, rendered into reusable local clips.
    // No downloads, microphone, or external audio middleware are needed.
    public sealed class DeadAirAudio : MonoBehaviour
    {
        const int Rate = 32000;
        static DeadAirAudio instance;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> voices = new List<AudioSource>();
        AudioSource hum, air, radio, dread, tinnitus;
        float master = .5f, ambience = 1, effects = 1;
        int voiceIndex, stepSide;
        float nextStep, nextHeart, nextBreath, nextMonster, nextWhisper, nextAlarm, nextKnock;
        bool wasMoving;

        public void Initialize()
        {
            instance = this;
            BuildInstruments();
            for (int i = 0; i < 20; i++) voices.Add(Source("Effect voice " + (i + 1), false));
            hum = Loop("Station transformer / 48 Hz", "hum");
            air = Loop("Station ventilation", "air");
            radio = Loop("Receiver static", "static");
            dread = Loop("Threat subharmonic", "dread");
            tinnitus = Loop("Low sanity tinnitus", "tinnitus");
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

        public static void ApplySettings()
        {
            if (instance == null) return;
            instance.master = PlayerPrefs.GetInt("DeadAir.Muted", 0) != 0 ? 0 : Mathf.Clamp01(PlayerPrefs.GetFloat("DeadAir.Volume", .5f));
            instance.ambience = Mathf.Clamp01(PlayerPrefs.GetFloat("DeadAir.AmbienceVolume", 1));
            instance.effects = Mathf.Clamp01(PlayerPrefs.GetFloat("DeadAir.EffectsVolume", 1));
            foreach (var voice in instance.voices) voice.volume = instance.master * instance.effects * .85f;
        }

        public static void Play(string kind)
        {
            if (instance != null) instance.Emit(kind, 1, 0, null);
        }

        public void Emit(string kind, float level = 1, float pan = 0, Vector3? position = null)
        {
            if (!Application.isPlaying || !clips.TryGetValue(kind, out var clip) || voices.Count == 0) return;
            // Reuse idle voices first so an interface click cannot cut off the ending chord.
            AudioSource source = null;
            for (int i = 0; i < voices.Count; i++)
            {
                var candidate = voices[(voiceIndex + i) % voices.Count];
                if (!candidate.isPlaying) { source = candidate; voiceIndex = (voiceIndex + i + 1) % voices.Count; break; }
            }
            if (source == null) { source = voices[voiceIndex]; voiceIndex = (voiceIndex + 1) % voices.Count; source.Stop(); }
            source.spatialBlend = position.HasValue ? 1 : 0;
            if (position.HasValue) source.transform.position = position.Value;
            source.panStereo = Mathf.Clamp(pan, -1, 1);
            source.pitch = kind == "step" || kind == "run-step" ? UnityEngine.Random.Range(.94f, 1.06f) : 1;
            source.volume = master * effects * .85f;
            source.PlayOneShot(clip, Mathf.Clamp01(level));
        }

        public void Tick(bool paused, bool moving, bool sprinting, bool hiding, float stamina, float sanity,
            float threat, bool camera, bool power, bool broadcast, Vector3? monsterPosition, float monsterDistance)
        {
            float now = Time.unscaledTime;
            float insanity = 1 - Mathf.Clamp01(sanity / 100);
            float bed = paused ? 0 : master * ambience * (hiding ? .35f : 1);
            Fade(hum, bed * (power ? 1.45f : .7f), .7f);
            Fade(air, bed * (.04f + threat * .042f), .5f);
            Fade(radio, bed * (camera ? .02f + threat * .018f : .002f), .18f);
            Fade(dread, bed * (threat * .05f + insanity * .02f), .3f);
            dread.pitch = (71 + Mathf.Sin(now * .24f) * 1.5f + threat * 10) / 71;
            Fade(tinnitus, bed * Mathf.Max(0, insanity - .45f) * .03f, .5f);
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
                    Emit(sprinting ? "run-step" : "step", 1, stepSide * .12f);
                    nextStep = now + (sprinting ? .31f : .52f) + UnityEngine.Random.Range(0, .035f);
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
            foreach (var source in voices) source.Stop();
            float now = Time.unscaledTime;
            nextStep = now + .1f; nextHeart = now + .4f; nextBreath = now;
            nextMonster = now; nextWhisper = now + 6; nextAlarm = now;
            nextKnock = now + UnityEngine.Random.Range(14, 26); wasMoving = false;
        }

        // Used by the opt-in render check, so a successful build also verifies the generated sound data.
        public void ValidateInstruments()
        {
            float largestPeak = 0;
            foreach (var entry in clips)
            {
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
                largestPeak = Mathf.Max(largestPeak, peak);
                Debug.Log("DEAD AIR AUDIO: " + entry.Key + " peak=" + peak.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + " rms=" + rms.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
            }
            Debug.Log("DEAD AIR AUDIO VALIDATED: " + clips.Count + " non-silent finite clips, maximum peak=" + largestPeak.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
        }

        void OnDestroy()
        {
            DeadAirSettings.Changed -= ApplySettings;
            if (instance == this) instance = null;
            foreach (var clip in clips.Values)
                if (Application.isPlaying) Destroy(clip); else DestroyImmediate(clip);
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
            Add("scare", b => { b.Noise(0, .9f, .22f, 1100, .045f); b.Tone(0, 115, 36, 1.4f, .19f, Wave.Triangle, .04f); b.Tone(.07f, 340, 140, .75f, .075f, Wave.Sine, .04f); b.Tone(.12f, 360, 150, .85f, .062f, Wave.Sine, .04f); }, 1.7f);
            Add("screamer", b => { b.Noise(0, .35f, .3f, 2800, .005f); b.Tone(0, 720, 85, .45f, .25f, Wave.Saw, .005f); }, .6f);
            Add("spider", b => { b.Noise(0, .16f, .12f, 3500, .008f); b.Tone(.015f, 950, 145, .22f, .09f, Wave.Saw, .01f); for (int i = 0; i < 5; i++) b.Knock(.18f + i * .05f, .18f); }, .55f);
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
        }

        enum Wave { Sine, Triangle, Saw }

        sealed class Instrument
        {
            float[] samples;
            readonly System.Random random;
            bool loop;
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
                var result = AudioClip.Create("DEAD AIR / " + name, samples.Length, 1, Rate, false);
                result.SetData(samples, 0);
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
