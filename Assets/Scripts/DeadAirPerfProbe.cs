using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeadAir
{
    // Opt-in measurement pass: -deadAirPerf <absolute output folder> [ -deadAirPerfLabel <name> ].
    // It renders the real office through the same cameras, post chain and UI as the
    // game, then reports frame time per view and per disabled feature.
    public sealed class DeadAirPerfProbe : MonoBehaviour
    {
        const string Argument = "-deadAirPerf";
        const string LabelArgument = "-deadAirPerfLabel";
        const int SettleFrames = 8;

        sealed class Sample
        {
            public string name;
            public readonly List<float> frames = new List<float>();
            public readonly List<float> gpu = new List<float>();
            public readonly List<float> cpu = new List<float>();
            public float slowest;
            public int renderers, shadowCasters, lights, shadowLights;
            public int drawCalls, batches, setPassCalls, triangles;
        }

        DeadAirSession session;
        DeadAirPlayer player;
        DeadAirWorld world;
        DeadAirFrameRenderer frameRenderer;
        DeadAirScarePresentation scares;
        Camera view;
        Canvas canvas;
        Camera uiCamera;
        RenderTexture target;
        string output;
        string label;
        readonly List<Sample> samples = new List<Sample>();
        readonly List<string> notes = new List<string>();
        bool preferencesCaptured;
        float originalRenderScale;
        int originalGraphics, originalShadows;
        bool originalAo, originalBloom, originalReflections;

        public static bool IsRequested => Array.IndexOf(Environment.GetCommandLineArgs(), Argument) >= 0;

        public void Initialize(DeadAirSession game, DeadAirPlayer actor)
        {
            session = game;
            player = actor;
            world = FindFirstObjectByType<DeadAirBootstrap>()?.World;
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, Argument);
            if (index < 0 || index + 1 >= args.Length) { enabled = false; return; }
            output = args[index + 1];
            int labelIndex = Array.IndexOf(args, LabelArgument);
            label = labelIndex >= 0 && labelIndex + 1 < args.Length ? args[labelIndex + 1] : "probe";
            if (!Path.IsPathRooted(output)) throw new ArgumentException("-deadAirPerf requires an absolute output folder.");
            Directory.CreateDirectory(output);
            view = actor.ViewCamera;
            CapturePreferences();
            if (Application.isBatchMode) InitializeOffscreen();
            StartCoroutine(Run());
        }

        void InitializeOffscreen()
        {
            // A hidden batch window never presents a backbuffer, so the world,
            // equipment, presentation camera and UI are rendered explicitly.
            int width = Application.isBatchMode ? 1280 : Mathf.Max(640, Screen.width);
            int height = Application.isBatchMode ? 720 : Mathf.Max(360, Screen.height);
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            var canvasObject = GameObject.Find("BUREAU-9 interface");
            canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
            if (canvas != null)
            {
                foreach (var item in canvas.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 5;
                uiCamera = new GameObject("Performance probe UI camera").AddComponent<Camera>();
                uiCamera.enabled = false; uiCamera.orthographic = true; uiCamera.cullingMask = 1 << 5;
                uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 10;
                uiCamera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1;
            }
            view.enabled = false;
            view.cullingMask &= ~(1 << 5);
            view.clearFlags = CameraClearFlags.SolidColor;
            frameRenderer = view.GetComponent<DeadAirFrameRenderer>();
            frameRenderer.OutputCamera.enabled = false;
            frameRenderer.OutputCamera.targetTexture = target;
            scares = FindFirstObjectByType<DeadAirScarePresentation>();
            notes.Add("offscreen:" + target.width + "x" + target.height);
        }

        void LateUpdate()
        {
            if (target == null || frameRenderer == null) return;
            Canvas.ForceUpdateCanvases();
            frameRenderer.EnsureTargets();
            world.PrepareRender(view);
            view.Render();
            frameRenderer.RenderEquipment();
            frameRenderer.OutputCamera.Render();
            if (uiCamera != null) uiCamera.Render();
            scares?.RenderFullscreen(target);
        }

        IEnumerator Run()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            yield return new WaitForSecondsRealtime(2f);
            Application.targetFrameRate = -1;
            DeadAirSettings.ApplyGraphicsPreset(2);
            yield return new WaitForSecondsRealtime(.5f);
            CountScene();

            yield return Measure("menu", null, 32);

            session.Host("Perf probe", "engineer", true, true);
            session.Command(new NetPacket { type = "start" });
            yield return Until(() => session.State != null && session.State.phase == "playing", "solo start");
            yield return new WaitForSecondsRealtime(.6f);

            yield return Measure("yard-facing-office", new Pose(7, 19.5f, .478f, -.136f, false, 0), 32);
            yield return Measure("corridor", new Pose(0, 1, 0, 0, false, 0), 40);
            yield return Measure("archive", new Pose(-5, -1, .9272952f, -.1292768f, false, 0), 32);
            yield return Measure("control-room", new Pose(4, -2, -Mathf.PI * .5f, -.03f, true, 0), 32);
            yield return Measure("upper-corridor", new Pose(0, -20, Mathf.PI, 0, false, 4.2f), 40);

            yield return Measure("corridor-high", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.Reflections = false;
            yield return Measure("corridor-no-reflections", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.Reflections = true;
            DeadAirSettings.AmbientOcclusion = false;
            yield return Measure("corridor-no-ao", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.AmbientOcclusion = true;
            DeadAirSettings.Bloom = false;
            yield return Measure("corridor-no-bloom", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.Bloom = true;
            DeadAirSettings.ShadowQuality = 0;
            yield return Measure("corridor-no-shadows", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.ShadowQuality = 2;
            DeadAirSettings.RenderScale = .65f;
            yield return Measure("corridor-scale-65", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.RenderScale = 1;
            DeadAirSettings.ApplyGraphicsPreset(2);
            QualitySettings.shadowDistance = 12;
            yield return Measure("corridor-shadow-distance-12", new Pose(0, 1, 0, 0, false, 0), 32);
            DeadAirSettings.ApplyGraphicsPreset(2);
            yield return new WaitForSecondsRealtime(.4f);
            yield return Measure("corridor-final", new Pose(0, 1, 0, 0, false, 0), 32);
            yield return MeasureMoving("corridor-moving", new Pose(0, 1, 0, 0, false, 0), 40);

            var moon = GameObject.Find("Dead Air Night Sky & Moon/Directional moonlight")?.GetComponent<Light>();
            if (moon != null)
            {
                var prior = moon.shadows;
                moon.shadows = LightShadows.None;
                yield return Measure("corridor-moon-shadow-off", new Pose(0, 1, 0, 0, false, 0), 32);
                moon.shadows = prior;
            }

            var instanced = new List<Material>();
            var seen = new HashSet<Material>();
            var station = GameObject.Find("BLACKPINE / original web scene");
            if (station != null)
            {
                foreach (var renderer in station.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && material.enableInstancing && seen.Add(material)) instanced.Add(material);
                foreach (var material in instanced) material.enableInstancing = false;
                yield return Measure("corridor-no-instancing", new Pose(0, 1, 0, 0, false, 0), 32);
                foreach (var material in instanced) material.enableInstancing = true;
                notes.Add("instanced materials=" + instanced.Count);
            }

            yield return MeasureRoomEntrySpike();
            WriteReport();
            RestorePreferences();
            Debug.Log("DEAD AIR PERF COMPLETE: " + output);
            Application.Quit(0);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }

        struct Pose
        {
            public readonly float x, z, yaw, pitch, floor;
            public readonly bool equipment;
            public Pose(float x, float z, float yaw, float pitch, bool equipment, float floor)
            { this.x = x; this.z = z; this.yaw = yaw; this.pitch = pitch; this.equipment = equipment; this.floor = floor; }
        }

        IEnumerator Measure(string name, Pose? pose, int frames)
        {
            if (pose.HasValue)
            {
                var p = pose.Value;
                player.SetInspectionPose(p.x, p.z, p.yaw, p.pitch, p.equipment, p.floor);
            }
            for (int i = 0; i < SettleFrames; i++) yield return null;
            var sample = new Sample { name = name };
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                sample.frames.Add(Time.unscaledDeltaTime * 1000f);
                sample.slowest = Mathf.Max(sample.slowest, Time.unscaledDeltaTime * 1000f);
                CaptureFrameTiming(sample);
            }
            CountScene(sample);
#if UNITY_EDITOR
            sample.drawCalls = UnityEditor.UnityStats.drawCalls;
            sample.setPassCalls = UnityEditor.UnityStats.setPassCalls;
            sample.triangles = UnityEditor.UnityStats.triangles;
#endif
            samples.Add(sample);
            SaveFrame(name);
            Debug.Log("DEAD AIR PERF: " + name + " medianMs=" + Median(sample.frames).ToString("F1", CultureInfo.InvariantCulture) +
                " meanMs=" + Mean(sample.frames).ToString("F1", CultureInfo.InvariantCulture) +
                " p95Ms=" + Percentile(sample.frames, .95f).ToString("F1", CultureInfo.InvariantCulture) +
                " slowestMs=" + sample.slowest.ToString("F1", CultureInfo.InvariantCulture) +
                " fps=" + (1000f / Mathf.Max(.01f, Median(sample.frames))).ToString("F1", CultureInfo.InvariantCulture) +
                " renderers=" + sample.renderers + " shadowCasters=" + sample.shadowCasters +
                " lights=" + sample.lights + " shadowLights=" + sample.shadowLights +
                " draws=" + sample.drawCalls + " batches=" + sample.batches + " tris=" + sample.triangles);
        }

        static void CaptureFrameTiming(Sample sample)
        {
            FrameTimingManager.CaptureFrameTimings();
            var timings = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                if (timings[0].gpuFrameTime > 0) sample.gpu.Add((float)timings[0].gpuFrameTime);
                if (timings[0].cpuMainThreadFrameTime > 0) sample.cpu.Add((float)timings[0].cpuMainThreadFrameTime);
            }
        }

        void CountScene(Sample sample = null)
        {
            int renderers = 0, casters = 0, lights = 0, shadowLights = 0;
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!renderer.enabled) continue;
                renderers++;
                if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
            }
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!light.enabled || light.intensity <= .0001f) continue;
                lights++;
                if (light.shadows != LightShadows.None) shadowLights++;
            }
            if (sample == null) { notes.Add("scene renderers=" + renderers + " shadowCasters=" + casters + " lights=" + lights + " shadowLights=" + shadowLights); return; }
            sample.renderers = renderers; sample.shadowCasters = casters; sample.lights = lights; sample.shadowLights = shadowLights;
        }

        IEnumerator MeasureRoomEntrySpike()
        {
            // Crossing a room boundary schedules a real environment probe capture,
            // which renders the whole office six times. Report those frames alone.
            player.SetInspectionPose(0, 1, 0, 0, false, 0);
            for (int i = 0; i < 10; i++) yield return null;
            var sample = new Sample { name = "room-entry-archive" };
            player.SetInspectionPose(-5, -1, .9272952f, -.1292768f, false, 0);
            for (int i = 0; i < 24; i++)
            {
                yield return null;
                sample.frames.Add(Time.unscaledDeltaTime * 1000f);
                sample.slowest = Mathf.Max(sample.slowest, Time.unscaledDeltaTime * 1000f);
                CaptureFrameTiming(sample);
            }
            samples.Add(sample);
            Debug.Log("DEAD AIR PERF: room-entry medianMs=" + Median(sample.frames).ToString("F1", CultureInfo.InvariantCulture) +
                " maxMs=" + sample.slowest.ToString("F1", CultureInfo.InvariantCulture));
        }

        IEnumerator MeasureMoving(string name, Pose pose, int frames)
        {
            // A real player turns and steps, which invalidates the cached floor
            // reflection. Sweep the view slowly so the moving cost is measured.
            player.SetInspectionPose(pose.x, pose.z, pose.yaw, pose.pitch, pose.equipment, pose.floor);
            for (int i = 0; i < SettleFrames; i++) yield return null;
            var sample = new Sample { name = name };
            float yaw = pose.yaw;
            for (int i = 0; i < frames; i++)
            {
                yaw += .02f;
                player.SetInspectionPose(pose.x, pose.z, yaw, pose.pitch, pose.equipment, pose.floor);
                yield return null;
                sample.frames.Add(Time.unscaledDeltaTime * 1000f);
                sample.slowest = Mathf.Max(sample.slowest, Time.unscaledDeltaTime * 1000f);
                CaptureFrameTiming(sample);
            }
            CountScene(sample);
#if UNITY_EDITOR
            sample.drawCalls = UnityEditor.UnityStats.drawCalls;
            sample.setPassCalls = UnityEditor.UnityStats.setPassCalls;
            sample.triangles = UnityEditor.UnityStats.triangles;
#endif
            samples.Add(sample);
            SaveFrame(name);
            Debug.Log("DEAD AIR PERF: " + name + " medianMs=" + Median(sample.frames).ToString("F1", CultureInfo.InvariantCulture) +
                " meanMs=" + Mean(sample.frames).ToString("F1", CultureInfo.InvariantCulture) +
                " p95Ms=" + Percentile(sample.frames, .95f).ToString("F1", CultureInfo.InvariantCulture) +
                " slowestMs=" + sample.slowest.ToString("F1", CultureInfo.InvariantCulture) +
                " fps=" + (1000f / Mathf.Max(.01f, Median(sample.frames))).ToString("F1", CultureInfo.InvariantCulture) +
                " draws=" + sample.drawCalls + " tris=" + sample.triangles);
        }

        void SaveFrame(string name)
        {
            if (target == null || !target.IsCreated()) return;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, "view-" + name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(pixels);
            }
        }

        IEnumerator Until(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Perf probe timed out: " + message);
                yield return null;
            }
        }

        static float Mean(List<float> values)
        {
            if (values.Count == 0) return 0;
            double sum = 0; foreach (var value in values) sum += value;
            return (float)(sum / values.Count);
        }

        static float Median(List<float> values) => Percentile(values, .5f);

        static float Percentile(List<float> values, float percentile)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<float>(values); sorted.Sort();
            int index = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        void WriteReport()
        {
            var csv = new StringBuilder();
            csv.AppendLine("sample,frames,medianMs,meanMs,p95Ms,slowestMs,fps,gpuMedianMs,cpuMedianMs,renderers,shadowCasters,lights,shadowLights,drawCalls,batches,setPassCalls,triangles");
            foreach (var sample in samples)
            {
                csv.Append(sample.name).Append(',').Append(sample.frames.Count).Append(',')
                   .Append(Median(sample.frames).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                   .Append(Mean(sample.frames).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                   .Append(Percentile(sample.frames, .95f).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                   .Append(sample.slowest.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                   .Append((1000f / Mathf.Max(.01f, Median(sample.frames))).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                   .Append(sample.gpu.Count > 0 ? Median(sample.gpu).ToString("F2", CultureInfo.InvariantCulture) : "").Append(',')
                   .Append(sample.cpu.Count > 0 ? Median(sample.cpu).ToString("F2", CultureInfo.InvariantCulture) : "").Append(',')
                   .Append(sample.renderers).Append(',').Append(sample.shadowCasters).Append(',')
                   .Append(sample.lights).Append(',').Append(sample.shadowLights).Append(',')
                   .Append(sample.drawCalls).Append(',').Append(sample.batches).Append(',')
                   .Append(sample.setPassCalls).Append(',').Append(sample.triangles).AppendLine();
            }
            File.WriteAllText(Path.Combine(output, "perf-" + label + ".csv"), csv.ToString());
            File.WriteAllLines(Path.Combine(output, "perf-" + label + "-notes.txt"), notes);
            Debug.Log("DEAD AIR PERF REPORT: " + Path.Combine(output, "perf-" + label + ".csv"));
        }

        void CapturePreferences()
        {
            originalRenderScale = DeadAirSettings.RenderScale;
            originalGraphics = DeadAirSettings.GraphicsQuality;
            originalShadows = DeadAirSettings.ShadowQuality;
            originalAo = DeadAirSettings.AmbientOcclusion;
            originalBloom = DeadAirSettings.Bloom;
            originalReflections = DeadAirSettings.Reflections;
            preferencesCaptured = true;
        }

        void RestorePreferences()
        {
            if (!preferencesCaptured) return;
            DeadAirSettings.RenderScale = originalRenderScale;
            DeadAirSettings.GraphicsQuality = originalGraphics;
            DeadAirSettings.ShadowQuality = originalShadows;
            DeadAirSettings.AmbientOcclusion = originalAo;
            DeadAirSettings.Bloom = originalBloom;
            DeadAirSettings.Reflections = originalReflections;
            DeadAirSettings.Save();
            preferencesCaptured = false;
        }

        void OnDestroy() => RestorePreferences();
    }
}
