using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirCoopFlashlightValidation
    {
        const string Output = "Art/OfficeRevision/CoopFlashlight";

        [MenuItem("Dead Air/Validate Cooperative Flashlights")]
        public static void Validate() => Run(false);

        [MenuItem("Dead Air/Validate Cooperative Flashlights With GPU")]
        public static void ValidateWithGpu() => Run(true);

        static void Run(bool gpu)
        {
            var world = WorldData.Load();
            var evidence = new List<string>();
            foreach (string role in new[] { "engineer", "operator", "journalist", "dispatcher" })
                ValidateRole(world, role, evidence);
            ValidateLan(world, gpu, evidence);
            Directory.CreateDirectory(Output);
            File.WriteAllLines(Path.Combine(Output, gpu ? "gpu-report.txt" : "structural-report.txt"), evidence);
            Debug.Log("DEAD AIR COOP FLASHLIGHT PASSED: four roles, pitch/yaw and physical lens origins, class optics, suppression states, host and two actual loopback guests" +
                (gpu ? ", GPU illumination on replicated light." : "."));
        }

        static GameObject Avatar(WorldData world, string role)
        {
            var prefab = Resources.Load<GameObject>("World/character_" + role);
            Require(prefab != null, "Missing remote avatar for " + role);
            var avatar = UnityEngine.Object.Instantiate(prefab);
            avatar.name = "Coop light validation / " + role;
            avatar.hideFlags = HideFlags.HideAndDontSave;
            var motion = avatar.GetComponent<DeadAirCharacterMotion>();
            Require(motion != null, "Missing motion driver for " + role);
            motion.ConfigureFlashlight(world.GetClass(role).bonuses);
            return avatar;
        }

        static void Present(GameObject avatar, PlayerState snapshot, float elapsed = 1, bool playing = true)
        {
            avatar.transform.position = new Vector3(-snapshot.x, snapshot.y, snapshot.z);
            avatar.transform.rotation = Quaternion.Euler(0, 180 - snapshot.yaw * Mathf.Rad2Deg, 0);
            avatar.GetComponent<DeadAirCharacterMotion>().Present(snapshot, elapsed, false, playing);
        }

        static void ValidateRole(WorldData world, string role, List<string> evidence)
        {
            var avatar = Avatar(world, role);
            try
            {
                var snapshot = new PlayerState { characterClass = role, inventory = new List<string> { "flashlight", DeadAirRoleItems.ForClass(role) },
                    flashlight = true, x = 2, y = 4.2f, z = -3 };
                var motion = avatar.GetComponent<DeadAirCharacterMotion>();
                foreach (float yaw in new[] { 0f, 1.1f, 3.14f })
                foreach (float pitch in new[] { -1.1f, 0f, .9f })
                foreach (bool camera in new[] { false, true })
                {
                    snapshot.yaw = yaw; snapshot.pitch = pitch; snapshot.camera = camera;
                    var copy = JsonUtility.FromJson<PlayerState>(JsonUtility.ToJson(snapshot));
                    Present(avatar, copy);
                    AssertBeam(avatar, copy, world.GetClass(role).bonuses);
                    Require(!camera || motion.GripContactError < .011f, role + " camera pitch broke the two hand grip.");
                }
                snapshot.camera = false;
                snapshot.flashlight = false; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Switch off did not extinguish " + role);
                snapshot.flashlight = true; snapshot.flashlightCharge = 0; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Empty battery still lit " + role);
                snapshot.flashlightCharge = 180;
                snapshot.down = true; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Downed player still lit " + role); snapshot.down = false;
                snapshot.hiding = "test"; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Hidden player still lit " + role); snapshot.hiding = null;
                snapshot.ladderClimbing = true; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Climbing player still lit " + role); snapshot.ladderClimbing = false;
                snapshot.mapOpen = true; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Reading map still lit " + role); snapshot.mapOpen = false;
                snapshot.selectedSlot = 1; snapshot.uniqueItemEquipped = true; Present(avatar, snapshot); Require(!motion.FlashlightEmitter.enabled, "Personal item still lit " + role);
                snapshot.selectedSlot = 0; snapshot.uniqueItemEquipped = false;
                Present(avatar, snapshot, playing: false); Require(!motion.FlashlightEmitter.enabled, "Lobby/finished match still lit " + role);
                Present(avatar, snapshot); Require(motion.FlashlightEmitter.enabled, "Restored valid equipment did not light " + role);
                motion.SetEquipmentPreview(snapshot); Require(!motion.FlashlightEmitter.enabled, "Studio preview leaked a world beam " + role);
                Present(avatar, snapshot); avatar.SetActive(false);
                // Edit mode does not call OnDisable on this runtime-only driver.
                // The light still stops rendering through its inactive hierarchy.
                Require(!motion.FlashlightEmitter.isActiveAndEnabled, "Inactive avatar kept an active light " + role);
                evidence.Add("PASSED: " + role + ": 18 serialized yaw/pitch/equipment poses, physical lens origin, matching class optics, camera grip; off/depleted/down/hiding/ladder/map/personal/lobby/preview/inactive suppression.");
            }
            finally { UnityEngine.Object.DestroyImmediate(avatar); }
        }

        static void AssertBeam(GameObject avatar, PlayerState state, ClassBonuses bonuses)
        {
            var motion = avatar.GetComponent<DeadAirCharacterMotion>();
            var light = motion.FlashlightEmitter;
            Require(light != null && light.enabled && light.type == LightType.Spot, "Replicated flashlight is missing its real spot light.");
            Require(light.renderMode == LightRenderMode.ForcePixel && light.shadows == LightShadows.Soft, "World beam needs surface lighting and physical occlusion.");
            Require((light.cullingMask & ((1 << 28) | (1 << 29) | (1 << 30))) == 0, "Remote beam reaches menu or first-person layers.");
            Require(Mathf.Abs(light.range - bonuses.flashlightDist) < .001f && Mathf.Abs(light.spotAngle - bonuses.flashlightAngle * Mathf.Rad2Deg * 2) < .001f,
                "Remote optics differ from the owning player's class.");
            var expected = Quaternion.Euler(0, 180 - state.yaw * Mathf.Rad2Deg, 0) * Quaternion.Euler(-state.pitch * Mathf.Rad2Deg, 0, 0) * Vector3.forward;
            Require(Vector3.Angle(light.transform.forward, expected) < .01f, "Remote beam ignores replicated yaw or pitch.");
            Transform lens = null;
            foreach (var part in avatar.GetComponentsInChildren<Transform>(true))
                if (part.name == (state.camera ? "Video light glass" : "Lens")) { lens = part; break; }
            Require(lens != null && Vector3.Distance(light.transform.position, lens.position) < .005f, "Remote beam is detached from its visible lens.");
            Require(Vector3.Angle(state.camera ? lens.parent.forward : lens.parent.up, expected) < .01f,
                "Visible equipment points away from its beam.");
        }

        static void ValidateLan(WorldData world, bool gpu, List<string> evidence)
        {
            var hostObject = new GameObject("Flashlight loopback host");
            var guestObject = new GameObject("Flashlight loopback guest");
            var observerObject = new GameObject("Flashlight loopback observer");
            var host = hostObject.AddComponent<DeadAirSession>();
            var guest = guestObject.AddComponent<DeadAirSession>();
            var observer = observerObject.AddComponent<DeadAirSession>();
            var update = typeof(DeadAirSession).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            GameObject hostAvatar = null, guestAvatar = null;
            try
            {
                host.Initialize(world); guest.Initialize(world); observer.Initialize(world);
                host.Host("Light host", "engineer", false, true);
                Require(host.LastError == null, "Loopback host failed: " + host.LastError);
                guest.Join("127.0.0.1", host.State.code, "Light guest", "operator");
                PumpUntil(() => guest.State != null && host.State.players.Count == 2, "First loopback guest join");
                observer.Join("127.0.0.1", host.State.code, "Light observer", "journalist");
                PumpUntil(() => observer.State != null && host.State.players.Count == 3, "Second loopback guest join");
                host.Command(new NetPacket { type = "start" });
                PumpUntil(() => guest.State.phase == "playing" && observer.State.phase == "playing", "LAN start");
                hostAvatar = Avatar(world, "engineer"); guestAvatar = Avatar(world, "operator");
                int sequence = 1;
                foreach (float pitch in new[] { -.65f, 0f, .65f })
                {
                    SendPose(host, .7f, pitch, true, false, sequence);
                    SendPose(guest, 1.3f, pitch, true, false, sequence);
                    int wanted = sequence++;
                    PumpUntil(() => guest.State.Player(host.LocalId).moveSequence >= wanted && observer.State.Player(guest.LocalId).moveSequence >= wanted &&
                        host.State.Player(guest.LocalId).moveSequence >= wanted, "Host/guest aim snapshots");
                    Present(hostAvatar, guest.State.Player(host.LocalId)); AssertBeam(hostAvatar, guest.State.Player(host.LocalId), world.GetClass("engineer").bonuses);
                    Present(guestAvatar, host.State.Player(guest.LocalId)); AssertBeam(guestAvatar, host.State.Player(guest.LocalId), world.GetClass("operator").bonuses);
                    Present(guestAvatar, observer.State.Player(guest.LocalId)); AssertBeam(guestAvatar, observer.State.Player(guest.LocalId), world.GetClass("operator").bonuses);
                }
                SendPose(guest, Mathf.PI, 0, false, false, sequence);
                int offSequence = sequence++;
                PumpUntil(() => observer.State.Player(guest.LocalId).moveSequence >= offSequence, "Guest switched off snapshot");
                Present(guestAvatar, observer.State.Player(guest.LocalId)); Require(!guestAvatar.GetComponent<DeadAirCharacterMotion>().FlashlightEmitter.enabled, "Guest off beam remained visible to observer.");
                SendPose(guest, Mathf.PI, 0, true, true, sequence);
                int cameraSequence = sequence++;
                PumpUntil(() => observer.State.Player(guest.LocalId).moveSequence >= cameraSequence, "Guest video light snapshot");
                Present(guestAvatar, observer.State.Player(guest.LocalId)); AssertBeam(guestAvatar, observer.State.Player(guest.LocalId), world.GetClass("operator").bonuses);
                if (gpu) ValidateGpu(guestAvatar, observer.State.Player(guest.LocalId), evidence);
                host.State.Player(guest.LocalId).flashlightCharge = 0;
                SendPose(guest, Mathf.PI, 0, true, false, sequence);
                int emptySequence = sequence++;
                PumpUntil(() => observer.State.Player(guest.LocalId).moveSequence >= emptySequence, "Host-authorized battery depletion snapshot");
                Present(guestAvatar, observer.State.Player(guest.LocalId)); Require(!guestAvatar.GetComponent<DeadAirCharacterMotion>().FlashlightEmitter.enabled, "Depleted guest still lights observer world.");
                guest.Leave();
                PumpUntil(() => host.State.players.Count == 2 && observer.State.players.Count == 2, "Guest disconnect snapshot");
                evidence.Add("PASSED: real TCP host + two loopback guests: host light reaches guest; guest aim reaches host and observer; vertical aim, toggling, camera light, host battery depletion and disconnect snapshots.");
            }
            finally
            {
                observer.Leave(); guest.Leave(); host.Leave();
                if (hostAvatar != null) UnityEngine.Object.DestroyImmediate(hostAvatar);
                if (guestAvatar != null) UnityEngine.Object.DestroyImmediate(guestAvatar);
                UnityEngine.Object.DestroyImmediate(observerObject); UnityEngine.Object.DestroyImmediate(guestObject); UnityEngine.Object.DestroyImmediate(hostObject);
            }

            void PumpUntil(Func<bool> ready, string message)
            {
                for (int attempt = 0; attempt < 300 && !ready(); attempt++)
                { update.Invoke(host, null); update.Invoke(guest, null); update.Invoke(observer, null); Thread.Sleep(10); }
                Require(ready(), message + " did not arrive over loopback LAN.");
            }
        }

        static void SendPose(DeadAirSession session, float yaw, float pitch, bool on, bool camera, int sequence)
        {
            var player = session.State.Player(session.LocalId);
            session.Command(new NetPacket { type = "move", x = player.x, y = player.y, z = player.z, yaw = yaw, pitch = pitch,
                flashlight = on, camera = camera, moveSequence = sequence });
        }

        static void ValidateGpu(GameObject avatar, PlayerState replicated, List<string> evidence)
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "GPU illumination check needs a graphics device.");
            const int layer = 27;
            var stage = new GameObject("Coop light GPU fixture");
            var activeLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var enabled = new bool[activeLights.Length];
            var oldAmbientMode = RenderSettings.ambientMode; var oldAmbient = RenderSettings.ambientLight;
            var oldProbe = RenderSettings.ambientProbe; bool oldFog = RenderSettings.fog;
            Material material = null; RenderTexture frame = null;
            var oldTarget = RenderTexture.active;
            try
            {
                for (int i = 0; i < activeLights.Length; i++) { enabled[i] = activeLights[i].enabled; activeLights[i].enabled = false; }
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.fog = false;
                var camera = new GameObject("Observer camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform); camera.enabled = false; camera.cullingMask = 1 << layer;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = .05f; camera.farClipPlane = 15; camera.fieldOfView = 55;
                camera.transform.position = new Vector3(1.6f, 1.3f, -.4f); camera.transform.LookAt(new Vector3(0, 1.3f, 5));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Spotlight receiver"; wall.layer = layer;
                wall.transform.SetParent(stage.transform); wall.transform.position = new Vector3(0, 1.3f, 5);
                wall.transform.localScale = new Vector3(7, 5, .15f);
                material = new Material(Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard")) { color = new Color(.6f, .6f, .6f) };
                wall.GetComponent<Renderer>().sharedMaterial = material;
                foreach (var part in avatar.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
                var light = avatar.GetComponent<DeadAirCharacterMotion>().FlashlightEmitter;
                light.cullingMask = 1 << layer;
                frame = new RenderTexture(480, 320, 24, RenderTextureFormat.ARGB32); frame.Create(); camera.targetTexture = frame;
                var snapshot = JsonUtility.FromJson<PlayerState>(JsonUtility.ToJson(replicated));
                snapshot.x = .8f; snapshot.y = 0; snapshot.z = 0; snapshot.yaw = Mathf.PI; snapshot.pitch = 0;
                snapshot.flashlight = false; Present(avatar, snapshot); float dark = Capture(camera, frame, "observer-light-off.png");
                snapshot.flashlight = true; Present(avatar, snapshot); float lit = Capture(camera, frame, "observer-light-on.png");
                Require(lit > dark + .012f, "Replicated light is enabled but fails to illuminate actual rendered surfaces: off=" + dark + ", on=" + lit);
                evidence.Add("PASSED: observer GPU frame mean luminance: light OFF=" + dark.ToString("F4") + ", replicated camera light ON=" + lit.ToString("F4") + ".");
            }
            finally
            {
                RenderTexture.active = oldTarget;
                if (frame != null) { frame.Release(); UnityEngine.Object.DestroyImmediate(frame); }
                UnityEngine.Object.DestroyImmediate(stage); if (material != null) UnityEngine.Object.DestroyImmediate(material);
                RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientProbe = oldProbe; RenderSettings.fog = oldFog;
                for (int i = 0; i < activeLights.Length; i++) if (activeLights[i] != null) activeLights[i].enabled = enabled[i];
                avatar.GetComponent<DeadAirCharacterMotion>().FlashlightEmitter.cullingMask = ~((1 << 28) | (1 << 29) | (1 << 30));
            }
        }

        static float Capture(Camera camera, RenderTexture frame, string name)
        {
            camera.Render(); RenderTexture.active = frame;
            var pixels = new Texture2D(frame.width, frame.height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0); pixels.Apply();
                float mean = 0; foreach (var pixel in pixels.GetPixels()) mean += pixel.grayscale;
                Directory.CreateDirectory(Output); File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
                return mean / (frame.width * frame.height);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Coop flashlight validation: " + message);
        }
    }
}
