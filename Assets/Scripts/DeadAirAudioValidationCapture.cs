using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace DeadAir
{
    // Device verification is opt-in. Ordinary game launches do not create this component.
    public sealed class DeadAirAudioValidationCapture : MonoBehaviour
    {
        const int LoopFrames = 2304000;
        const int Rate = 48000;
        DeadAirAudio audio;
        string output;
        readonly List<string> checks = new List<string>();
        readonly List<Observation> observations = new List<Observation>();
        readonly Dictionary<string, float?> floats = new Dictionary<string, float?>();
        int? muted;
        DeadAirAudioValidationTap tap;
        bool preferencesCaptured;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-deadAirAudioCapture");
            if (index < 0) return;
            if (index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
            { Debug.LogError("Audio verification requires an absolute output directory."); Application.Quit(2); return; }
            var capture = new GameObject("Opt-in device audio verification").AddComponent<DeadAirAudioValidationCapture>();
            capture.output = args[index + 1];
            Directory.CreateDirectory(capture.output);
            capture.StartCoroutine(capture.Guard(capture.Run()));
        }

        IEnumerator Guard(IEnumerator routine)
        {
            var routines = new Stack<IEnumerator>();
            routines.Push(routine);
            while (routines.Count > 0)
            {
                bool more;
                object current = null;
                try { more = routines.Peek().MoveNext(); if (more) current = routines.Peek().Current; }
                catch (Exception error)
                {
                    Finish(false, error.GetType().Name + ": " + error.Message);
                    yield break;
                }
                if (!more) { routines.Pop(); continue; }
                if (current is IEnumerator nested) routines.Push(nested);
                else yield return current;
            }
        }

        IEnumerator Run()
        {
            Application.targetFrameRate = 60;
            yield return new WaitForSecondsRealtime(.5f);
            foreach (var atmosphere in FindObjectsByType<DeadAirAtmosphere>(FindObjectsSortMode.None)) atmosphere.enabled = false;
            foreach (var engine in FindObjectsByType<DeadAirAudio>(FindObjectsSortMode.None))
            {
                engine.enabled = false;
                foreach (var source in engine.GetComponentsInChildren<AudioSource>()) source.Stop();
            }
            CapturePreferences();
            PlayerPrefs.SetFloat("DeadAir.Volume", .6f);
            PlayerPrefs.SetFloat("DeadAir.AmbienceVolume", 1);
            PlayerPrefs.SetFloat("DeadAir.EffectsVolume", 1);
            PlayerPrefs.SetInt("DeadAir.Muted", 0);

            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Where(l => l.enabled && l.gameObject.activeInHierarchy).ToArray();
            Require(listeners.Length == 1, "The device scene has exactly one enabled AudioListener.");
            tap = listeners[0].gameObject.AddComponent<DeadAirAudioValidationTap>();
            audio = new GameObject("Isolated integrated audio engine").AddComponent<DeadAirAudio>();
            audio.Initialize();
            DeadAirAudio.ApplySettings();
            audio.SetContext("playing", false, false, null, false);
            audio.SetRoomContext("ГОЛОВНИЙ КОРИДОР", 0);
            audio.ResetRun();
            yield return TickFor(2.7f);
            var exploration = Capture("exploration");
            Require(exploration.music.groupWeights.Length == 3 && exploration.music.groupWeights[0] > .8f, "Exploration settles into its own score group.");
            CheckClock(exploration.music, "Exploration starts all eight imported stems on one DSP epoch.");

            yield return TickFor(3.1f, moving: true, sprinting: true);
            var sprint = Capture("sprint-hardstyle");
            Require(sprint.music.groupWeights[2] >= .3f && Gain(sprint.music, "pursuit-kick") > .001f,
                "Sprinting activates the hardstyle kick even without an active monster.");
            CheckClock(sprint.music, "Sprint crossfade keeps the stem phases aligned.");

            audio.SetContext("playing", false, false, "pursuit", false);
            yield return TickFor(3.1f, threat: .95f);
            var threat = Capture("monster-pursuit");
            Require(threat.music.groupWeights[2] > .8f, "An actual pursuit raises the full hardstyle arrangement.");
            float priorGain = threat.music.stemGains.Sum();
            audio.Emit("screamer");
            var scared = Capture("immediate-scare-duck");
            Require(scared.music.stemGains.Sum() < priorGain * .3f, "Fullscreen scare ducks music immediately in the same frame.");
            Require(Playing("screamer"), "The imported fullscreen scare owns a dedicated playing source.");
            audio.Emit("scare");
            Require(Playing("screamer"), "An incidental sting cannot replace the active fullscreen scare.");

            yield return TickFor(.2f, paused: true);
            var pauseBefore = Capture("pause-start");
            yield return TickFor(.4f, paused: true);
            var pauseAfter = Capture("pause-held");
            Require(pauseBefore.music.paused && pauseAfter.music.paused, "Pause reaches the common adaptive transport.");
            Require(pauseBefore.music.stemPositions.SequenceEqual(pauseAfter.music.stemPositions), "Pause freezes every stem at its existing frame.");
            Require(!Playing("screamer") && pauseAfter.music.stemGains.All(g => g <= .0001f), "Pause stops the scare and silences the music bus.");
            double previousEpoch = pauseAfter.music.epoch;
            yield return TickFor(.35f);
            var resumed = Capture("resume");
            Require(!resumed.music.paused && resumed.music.epoch > previousEpoch + .3, "Resume adjusts the DSP epoch by the paused duration.");
            CheckClock(resumed.music, "Resume retains synchronized positions on the adjusted DSP epoch.");

            PlayerPrefs.SetInt("DeadAir.Muted", 1); DeadAirAudio.ApplySettings();
            var mutedState = Capture("mute-immediate");
            Require(audio.GetComponentsInChildren<AudioSource>().All(s => s.volume <= .0001f), "Mute silences every source immediately.");
            PlayerPrefs.SetInt("DeadAir.Muted", 0); DeadAirAudio.ApplySettings();
            yield return TickFor(.35f);

            audio.PlayRoleAction("verification-operator", "operator", null);
            Require(Playing("role-operator"), "A role cue is ready on its first synchronous play.");
            for (int i = 0; i < 96; i++) audio.Emit("click");
            Require(Playing("role-operator"), "Foley pool saturation preserves a seven-second owned role cue.");
            audio.PlayRoleAction("verification-engineer", "engineer", null);
            audio.StopRoleAction("verification-operator");
            Require(!Playing("role-operator") && Playing("role-engineer"), "Cancelling one role leaves another owner's cue intact.");
            audio.StopRoleActions();

            var fallClips = new List<AudioClip>();
            foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
            {
                var clip = Resources.Load<AudioClip>("Audio/Cues/Falls/fall-" + role);
                Require(clip != null && clip.channels == 1, "Fall asset is imported for " + role + ".");
                fallClips.Add(clip);
                audio.PlayCharacterFall(role, null);
                Require(Playing("fall-" + role), "Character fall plays without adding an injury system: " + role + ".");
                for (int i = 0; i < 64; i++) audio.Emit("click");
                Require(Playing("fall-" + role), "Character fall survives incidental foley saturation: " + role + ".");
            }
            Require(fallClips.Distinct().Count() == 4 && fallClips.Select(c => c.samples).Distinct().Count() >= 3,
                "The four classes use four distinct fall compositions.");
            string[] roomNames = { "АРХІВ", "СТУДІЯ 04", "ВЕНТВУЗОЛ", "ЗАДНІЙ ДВІР", "ПІДВАЛ", "КОРИДОР" };
            string[] roomKeys = { "archive", "studio04", "ventilation", "backyard", "basement", "corridor" };
            for (int i = 0; i < roomNames.Length; i++)
            {
                audio.SetContext("playing", i == 3, false, null, false);
                audio.SetRoomContext(roomNames[i], i == 4 ? -2.4f : 0);
                yield return TickFor(1.3f);
                Require(Playing("room-" + roomKeys[i]), "Room context starts its matching streamed ambience: " + roomKeys[i] + ".");
                Require(audio.GetComponentsInChildren<AudioSource>().Count(s => s.isPlaying && s.clip != null && s.clip.name.StartsWith("room-", StringComparison.Ordinal)) <= 2,
                    "Room transitions use at most two concurrent ambience streams.");
            }
            Capture("room-crossfades");
            audio.ResetRun();
            audio.SetContext("playing", false, false, null, false);
            yield return TickFor(2.5f);
            var reset = Capture("reset-run");
            CheckClock(reset.music, "Reset schedules all eight stems together on a fresh epoch.");

            // Observe a real 48-second boundary, rather than moving timeSamples by hand.
            int previousPosition = reset.music.stemPositions[0];
            bool wrapped = false;
            float deadline = Time.realtimeSinceStartup + 50;
            while (Time.realtimeSinceStartup < deadline)
            {
                DriveTick(false, false, false, false, 0);
                var now = ReadMusic();
                if (now.stemPositions[0] < previousPosition - Rate)
                { CheckClock(now, "All eight stems retain alignment through a real loop boundary."); wrapped = true; break; }
                previousPosition = now.stemPositions[0];
                yield return null;
            }
            Require(wrapped, "The adaptive score completes and wraps a 48-second loop in the device build.");
            Capture("loop-wrap");
            Require(tap.callbacks > 0 && tap.frames > Rate && tap.energy > .000001,
                "The standalone listener receives non-silent audio buffers during verification.");
            Require(tap.peak <= 1.001f, "The rendered standalone mix stays below digital clipping.");
            var owned = Resources.Load<AudioClip>("Audio/Cues/Falls/fall-engineer");
            Destroy(audio.gameObject);
            yield return null;
            Require(owned != null && Resources.Load<AudioClip>("Audio/Cues/Falls/fall-engineer") == owned,
                "Destroying the runtime engine preserves imported AudioClip assets.");
            Finish(true, null);
        }

        IEnumerator TickFor(float duration, bool paused = false, bool moving = false, bool sprinting = false, float threat = 0)
        {
            float until = Time.realtimeSinceStartup + duration;
            while (Time.realtimeSinceStartup < until)
            { DriveTick(paused, moving, sprinting, false, threat); yield return null; }
        }

        void DriveTick(bool paused, bool moving, bool sprinting, bool hiding, float threat)
        {
            audio.Tick(paused, moving, sprinting, hiding, 100, 100, threat,
                false, true, false, threat > 0 ? (Vector3?)Vector3.right * 3 : null, threat > 0 ? 3 : 100, true);
        }

        MusicSnapshot ReadMusic()
        {
            var method = typeof(DeadAirAudio).GetMethod("CaptureMusicDiagnostics", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new InvalidOperationException("Adaptive music diagnostics are unavailable.");
            return JsonUtility.FromJson<MusicSnapshot>(JsonUtility.ToJson(method.Invoke(audio, null)));
        }

        Observation Capture(string stage)
        {
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            var observation = new Observation
            {
                stage = stage, music = ReadMusic(), sources = audio.GetComponentsInChildren<AudioSource>().Length,
                loadedClipObjects = clips.Length,
                deviceClipResidentBytes = clips.Sum(c => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c))
            };
            observations.Add(observation);
            File.WriteAllText(Path.Combine(output, "progress.txt"), stage + "\n");
            return observation;
        }

        bool Playing(string name) => audio.GetComponentsInChildren<AudioSource>().Any(s => s.isPlaying && s.clip != null && s.clip.name == name);
        static float Gain(MusicSnapshot snapshot, string stem)
        { int index = Array.IndexOf(snapshot.stemNames, stem); return index >= 0 ? snapshot.stemGains[index] : 0; }

        void CheckClock(MusicSnapshot snapshot, string message)
        {
            Require(snapshot.stemNames.Length == 8 && snapshot.stemPositions.Length == 8 && snapshot.stemPlaying.All(p => p), message + " Sources are playing.");
            AudioSettings.GetDSPBufferSize(out int frames, out int buffers);
            int tolerance = Math.Max(2048, frames * Math.Max(2, buffers));
            int expected = (int)(Math.Max(0, snapshot.dspTime - snapshot.epoch) * Rate) % LoopFrames;
            foreach (int position in snapshot.stemPositions)
            {
                int distance = Math.Abs(position - expected);
                distance = Math.Min(distance, LoopFrames - distance);
                Require(distance <= tolerance, message + " Position is within the DSP buffer window.");
            }
            int reference = snapshot.stemPositions[0];
            foreach (int position in snapshot.stemPositions)
            {
                int distance = Math.Abs(position - reference);
                distance = Math.Min(distance, LoopFrames - distance);
                Require(distance <= Math.Max(1024, frames * 2), message + " Layer phase spread is within two DSP blocks.");
            }
            Require(snapshot.groupWeights.All(v => v >= 0) && snapshot.groupWeights.Sum() <= 1.001f,
                message + " State gains form a bounded crossfade.");
        }

        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }

        void CapturePreferences()
        {
            foreach (string key in new[] { "DeadAir.Volume", "DeadAir.AmbienceVolume", "DeadAir.EffectsVolume" })
                floats[key] = PlayerPrefs.HasKey(key) ? (float?)PlayerPrefs.GetFloat(key) : null;
            muted = PlayerPrefs.HasKey("DeadAir.Muted") ? (int?)PlayerPrefs.GetInt("DeadAir.Muted") : null;
            preferencesCaptured = true;
        }

        void RestorePreferences()
        {
            if (!preferencesCaptured) return;
            foreach (var pair in floats) if (pair.Value.HasValue) PlayerPrefs.SetFloat(pair.Key, pair.Value.Value); else PlayerPrefs.DeleteKey(pair.Key);
            if (muted.HasValue) PlayerPrefs.SetInt("DeadAir.Muted", muted.Value); else PlayerPrefs.DeleteKey("DeadAir.Muted");
            preferencesCaptured = false;
        }

        void Finish(bool passed, string error)
        {
            RestorePreferences();
            AudioSettings.GetDSPBufferSize(out int frames, out int buffers);
            File.WriteAllText(Path.Combine(output, "runtime-audit.json"), JsonUtility.ToJson(new RuntimeAudit
            {
                passed = passed, error = error, checkedAtUtc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
                buildManifestSHA256 = BuildManifestHash(),
                platform = Application.platform.ToString(), outputSampleRate = AudioSettings.outputSampleRate,
                dspBufferFrames = frames, dspBuffers = buffers, checks = checks.ToArray(), observations = observations.ToArray(),
                listenerCallbacks = tap == null ? 0 : tap.callbacks, listenerFrames = tap == null ? 0 : tap.frames,
                mixPeak = tap == null ? 0 : tap.peak, listenerEnergy = tap == null ? 0 : tap.energy,
                memoryNote = "deviceClipResidentBytes measures AudioClip object residency in this Windows build; it excludes DSP streaming decoder/buffer costs. Buffer verification is technical, not a human listening review."
            }, true) + "\n");
            if (passed) Debug.Log("DEAD AIR DEVICE AUDIO VALIDATED: " + checks.Count + " checks.");
            else Debug.LogError("DEAD AIR DEVICE AUDIO FAILED: " + error);
            Application.Quit(passed ? 0 : 2);
        }

        static string BuildManifestHash()
        {
            string manifest = Path.GetFullPath(Path.Combine(Application.dataPath, "../build-info.json"));
            if (!File.Exists(manifest)) return "";
            using (var stream = File.OpenRead(manifest))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        [Serializable] sealed class MusicSnapshot
        {
            public double dspTime, epoch;
            public bool paused;
            public float[] groupWeights, stemGains;
            public int[] stemPositions;
            public bool[] stemPlaying;
            public string[] stemNames;
            public long decodedMemoryBytes;
        }
        [Serializable] sealed class Observation
        {
            public string stage;
            public MusicSnapshot music;
            public int sources, loadedClipObjects;
            public long deviceClipResidentBytes;
        }
        [Serializable] sealed class RuntimeAudit
        {
            public bool passed;
            public string error, checkedAtUtc, unity, platform, memoryNote, buildManifestSHA256;
            public int outputSampleRate, dspBufferFrames, dspBuffers, listenerCallbacks;
            public long listenerFrames;
            public double listenerEnergy;
            public float mixPeak;
            public string[] checks;
            public Observation[] observations;
        }
    }

    // Allocation-free observation of the actual listener output in the opt-in device test.
    public sealed class DeadAirAudioValidationTap : MonoBehaviour
    {
        public int callbacks;
        public long frames;
        public double energy;
        public float peak;
        void OnAudioFilterRead(float[] data, int channels)
        {
            callbacks++; frames += data.Length / Math.Max(1, channels);
            foreach (float value in data) { peak = Math.Max(peak, Math.Abs(value)); energy += value * (double)value; }
        }
    }
}
