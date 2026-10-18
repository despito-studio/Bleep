using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // Shared, deterministic source-coordinate positions keep host range checks and visuals aligned.
    public sealed class DeadAirFarmyard : MonoBehaviour
    {
        public const string GoatId = "farm-goat", ChickenId = "farm-chicken";
        public const float InteractionRange = 2.35f;
        // Existing owner body/equipment use 27/30; this local carry surface uses 26.
        // Its small bounce light therefore cannot illuminate the yard or other players.
        public const int OwnerCarryLayer = 26;
        public static readonly Vector3 GoatHome = new Vector3(-19, 0, -65.5f);
        public static readonly Vector3 ChickenHome = new Vector3(-20, 0, -68);
        public static bool IsCarrying(RoomState room, PlayerState actor) =>
            room != null && actor != null && room.blackpine != null &&
            !room.blackpine.chickenRescued && room.blackpine.chickenCarrierId == actor.id;

        public static Vector3 Position(WorldData world, RoomState room, string id)
        {
            if (room == null || room.blackpine == null) return id == GoatId ? GoatHome : ChickenHome;
            var farm = room.blackpine;
            if (id == ChickenId)
            {
                if (farm.chickenRescued)
                {
                    var perch = world != null ? DeadAirVanPresentation.SourcePoint(world, new Vector3(-.77f, .68f, -.25f)) : null;
                    return perch != null ? new Vector3(perch.x, perch.y, perch.z) : ChickenHome;
                }
                var carrier = string.IsNullOrEmpty(farm.chickenCarrierId) ? null : room.Player(farm.chickenCarrierId);
                if (carrier != null)
                    return new Vector3(carrier.x - Mathf.Sin(carrier.yaw) * .52f, carrier.y + .93f,
                        carrier.z - Mathf.Cos(carrier.yaw) * .52f);
                if (farm.chickenDropped) return new Vector3(farm.chickenX, farm.chickenY, farm.chickenZ);
                var chicken = Roam(ChickenHome, room.elapsed, .85f, 1.4f);
                float startledAge = room.elapsed - farm.chickenSquawkAt;
                if (farm.chickenSquawkSerial > 0 && startledAge >= 0 && startledAge < 3)
                {
                    // Retreat toward the open coop, then cautiously return. This exact arc is
                    // evaluated by host interaction checks as well as every guest presentation.
                    float retreat = Mathf.Sin(Mathf.PI * startledAge / 3);
                    chicken += new Vector3(farm.chickenSquawkSerial % 2 == 0 ? -.24f : .24f, 0, -1.15f) * retreat;
                }
                return chicken;
            }
            return Roam(GoatHome, room.elapsed, 1.25f, 0);
        }

        static Vector3 Roam(Vector3 home, float clock, float radius, float phase)
        {
            // A pause at every destination gives animals time to graze/peck.
            float route = Mathf.Max(0, clock * .105f + phase);
            int segment = Mathf.FloorToInt(route);
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((route - segment) / .64f));
            float a = segment * 2.399963f, b = (segment + 1) * 2.399963f;
            var from = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a) * .66f) * radius;
            var to = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b) * .66f) * radius;
            return home + Vector3.Lerp(from, to, t);
        }

        DeadAirSession session;
        WorldData world;
        GameObject yard;
        DeadAirFarmyardModel goat, chicken;
        AudioSource goatAudio, chickenAudio;
        AudioClip bleat, cluck, squawk;
        readonly List<Material> materials = new List<Material>();
        Transform supportHands;
        Camera ownerCamera;
        DeadAirPlayer ownerPlayer;
        int lastPet, lastSquawk, lastRadio;
        float nextGoatSound, nextChickenSound;
        float observedClock = -1, observedClockAt;
        Vector3 previousGoat, previousChicken;
        bool wasPlaying;

        public void Initialize(WorldData data, DeadAirSession game)
        {
            world = data; session = game;
            yard = new GameObject("Blackpine farmyard / caretaker animals");
            yard.transform.SetParent(transform, false);
            CreateShelter();
            goat = DeadAirFarmyardModel.Load("goat", yard.transform, "interactive:" + GoatId);
            chicken = DeadAirFarmyardModel.Load("rooster", yard.transform, "interactive:" + ChickenId);
            goatAudio = AnimalAudio(goat.transform, 16);
            chickenAudio = AnimalAudio(chicken.transform, 11);
            bleat = Voice("Goat bleat", true, false);
            cluck = Voice("Hen quiet cluck", false, false);
            squawk = Voice("Hen startled squawk", false, true);
            previousGoat = GoatHome; previousChicken = ChickenHome;
            yard.SetActive(false);
        }

        void Update()
        {
            var room = session != null ? session.State : null;
            bool playing = room != null && (room.phase == "playing" || room.phase == "departing") && !DeadAirChapters.IsChapter2(room.chapterId);
            if (yard == null) return;
            if (yard.activeSelf != playing) yard.SetActive(playing);
            if (!playing) { wasPlaying = false; return; }
            var farm = room.blackpine;
            if (farm == null) return;
            float clock = room.elapsed;
            if (clock != observedClock) { observedClock = clock; observedClockAt = Time.unscaledTime; }
            float animationClock = clock + (room.paused ? 0 : Mathf.Min(.15f, Time.unscaledTime - observedClockAt));
            if (!wasPlaying)
            {
                lastPet = farm.goatPetSerial; lastSquawk = farm.chickenSquawkSerial; lastRadio = room.radioSequence;
                nextGoatSound = clock + 8; nextChickenSound = clock + 3;
                previousGoat = Position(world, room, GoatId); previousChicken = Position(world, room, ChickenId);
                wasPlaying = true;
            }
            bool paused = room.paused;
            Vector3 g = Position(world, room, GoatId), c = Position(world, room, ChickenId);
            var carrier = string.IsNullOrEmpty(farm.chickenCarrierId) ? null : room.Player(farm.chickenCarrierId);
            bool carrying = carrier != null && !farm.chickenRescued;
            bool threat = room.enemy != null && room.enemy.active && !room.enemy.stunned &&
                Vector2.Distance(new Vector2(room.enemy.x, room.enemy.z), new Vector2(g.x, g.z)) < 19;
            Vector3 goatLook = g - previousGoat;
            // Determine gait from the shared route phase, so a guest does not alternate idle/walk
            // between 10 Hz room snapshots. Mesh animation extrapolates only 150ms locally.
            bool goatMoving = Mathf.Repeat(clock * .105f, 1) < .64f && !paused;
            bool chickenMoving = !farm.chickenDropped && !farm.chickenRescued && !carrying && !paused &&
                (Mathf.Repeat(clock * .105f + 1.4f, 1) < .64f ||
                 farm.chickenSquawkSerial > 0 && clock - farm.chickenSquawkAt < 3);
            float petAge = clock - farm.goatPetAt;
            if (threat) goatLook = new Vector3(room.enemy.x - g.x, 0, room.enemy.z - g.z);
            else if (farm.goatFriendlyUntil > clock && (!goatMoving || petAge >= 0 && petAge < 1.4f))
            {
                var nearest = NearestPlayer(room, g, 4);
                if (nearest != null) goatLook = new Vector3(nearest.x - g.x, 0, nearest.z - g.z);
            }
            Present(goat.transform, g, goatLook, 0);
            var chickenLook = carrying ? new Vector3(-Mathf.Sin(carrier.yaw), 0, -Mathf.Cos(carrier.yaw)) : c - previousChicken;
            Present(chicken.transform, c, chickenLook, carrying ? -9 : 0);
            if (farm.chickenRescued) chicken.transform.position += DeadAirVanPresentation.TransitOffset(room);
            bool receivingPet = farm.goatFriendlyUntil > clock && petAge >= 0 && petAge < 1.4f;
            goat.Sample(threat || !goatMoving || receivingPet ? "idle" : "walk", animationClock * (goatMoving ? 1.3f : 1));
            bool flapping = clock - farm.chickenSquawkAt < 1.1f && farm.chickenSquawkSerial > 0;
            chicken.Sample(flapping ? "attack" : carrying || !chickenMoving ? "idle" : "walk", animationClock * (chickenMoving ? 1.7f : 1));
            if (farm.goatFriendlyUntil > clock && petAge >= 0 && petAge < 1.4f)
                goat.transform.localRotation *= Quaternion.Euler(0, 0, Mathf.Sin(petAge * 5) * 4);
            previousGoat = g; previousChicken = c;
            if (paused) { goatAudio.Stop(); chickenAudio.Stop(); return; }
            float volume = DeadAirSettings.Volume * DeadAirSettings.EffectsVolume;
            goatAudio.volume = volume * .52f; chickenAudio.volume = volume * .45f;
            if (farm.goatPetSerial != lastPet)
            { lastPet = farm.goatPetSerial; if (clock - farm.goatPetAt < 2) goatAudio.PlayOneShot(bleat, .75f); }
            if (farm.chickenSquawkSerial != lastSquawk)
            { lastSquawk = farm.chickenSquawkSerial; if (clock - farm.chickenSquawkAt < 2) chickenAudio.PlayOneShot(squawk); }
            if (room.radioSequence != lastRadio)
            {
                lastRadio = room.radioSequence;
                var speaker = room.Player(room.radioSenderId);
                if (speaker != null && (new Vector3(speaker.x, speaker.y, speaker.z) - g).sqrMagnitude < 100)
                { goatAudio.PlayOneShot(bleat, .8f); nextGoatSound = clock + 12; }
            }
            if (clock >= nextGoatSound) { goatAudio.PlayOneShot(bleat, threat ? .85f : .6f); nextGoatSound = clock + (threat ? 5.5f : 16.7f); }
            if (clock >= nextChickenSound) { chickenAudio.PlayOneShot(cluck, carrying ? .9f : .6f); nextChickenSound = clock + (carrying ? 4.9f : 9.3f); }
        }

        static PlayerState NearestPlayer(RoomState room, Vector3 source, float maximum)
        {
            PlayerState nearest = null; float range = maximum * maximum;
            foreach (var player in room.players)
            {
                if (player.down || !string.IsNullOrEmpty(player.hiding)) continue;
                float d = (new Vector3(player.x, player.y, player.z) - source).sqrMagnitude;
                if (d >= range) continue; range = d; nearest = player;
            }
            return nearest;
        }

        static void Present(Transform actor, Vector3 source, Vector3 direction, float tilt)
        {
            actor.position = DeadAirOfficeLayout.Position(source.x, source.y + .025f, source.z);
            var forward = new Vector3(-direction.x, 0, direction.z);
            if (forward.sqrMagnitude > .0000001f)
            {
                var desired = Quaternion.LookRotation(forward.normalized) * Quaternion.Euler(tilt, 0, 0);
                actor.rotation = Quaternion.Slerp(actor.rotation, desired, 1 - Mathf.Exp(-Time.deltaTime * 8));
            }
        }

        static AudioSource AnimalAudio(Transform animal, float distance)
        {
            var audio = animal.gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false;
            audio.spatialBlend = 1; audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 1.5f; audio.maxDistance = distance; audio.dopplerLevel = 0; return audio;
        }

        static AudioClip Voice(string name, bool goatVoice, bool alarm)
        {
            const int rate = 22050; float duration = goatVoice ? .82f : alarm ? .6f : .34f;
            var samples = new float[Mathf.CeilToInt(rate * duration)]; uint rng = 19283;
            float phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate, u = t / duration;
                rng = rng * 1664525 + 1013904223;
                float noise = ((rng >> 8) / 16777216f) * 2 - 1;
                float frequency = goatVoice ? 142 + Mathf.Sin(t * 54) * 19 + Mathf.Sin(t * 17) * 9 :
                    alarm ? Mathf.Lerp(810, 470, u) + Mathf.Sin(t * 105) * 150 : 370 + Mathf.Sin(t * 63) * 95;
                phase += frequency / rate * Mathf.PI * 2;
                float envelope = Mathf.Min(1, t * 40) * Mathf.Pow(1 - u, goatVoice ? .6f : 1.4f);
                float pulses = goatVoice ? .65f + .35f * Mathf.Sin(t * 43) : .36f + .64f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * (alarm ? 35 : 48))), 2);
                float voice = Mathf.Sin(phase) * .43f + Mathf.Sin(phase * 2) * .22f + Mathf.Sin(phase * 3) * .14f + noise * (goatVoice ? .08f : .17f);
                samples[i] = voice * envelope * pulses * .6f;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }

        // Shelter and coop are open-front scenery, outside all traversal routes.
        void CreateShelter()
        {
            var shed = new GameObject("Caretaker goat shelter and hen coop").transform;
            shed.SetParent(yard.transform, false); shed.localPosition = DeadAirOfficeLayout.Position(-20.7f, 0, -72.3f);
            var timber = Material("Weathered pine planks", new Color(.23f, .18f, .13f));
            var dark = Material("Farmyard iron", new Color(.075f, .08f, .08f));
            var straw = Material("Dry hay", new Color(.39f, .32f, .16f));
            var snow = Material("Shelter roof snow", new Color(.68f, .75f, .8f));
            var woodLight = Material("Worn plank edges", new Color(.33f, .265f, .19f));
            var roof = Material("Oxidised roof tin", new Color(.16f, .2f, .19f));
            for (int i = 0; i < 4; i++)
                Box(shed, "Shelter post", new Vector3(i < 2 ? -1.45f : 1.45f, 1.1f, i % 2 == 0 ? -1 : 1), new Vector3(.16f, 2.2f, .16f), timber);
            for (int i = 0; i < 12; i++)
                Box(shed, "Back wall plank", new Vector3(-1.37f + i * .25f, .92f, -1), new Vector3(.235f, 1.8f, .065f), i % 3 == 0 ? woodLight : timber);
            for (int i = 0; i < 6; i++)
                Box(shed, "Side wall plank", new Vector3(-1.45f, .92f, -.86f + i * .31f), new Vector3(.065f, 1.8f, .29f), timber);
            Box(shed, "Roof tin", new Vector3(0, 2.22f, 0), new Vector3(3.35f, .1f, 2.5f), roof, new Vector3(-5, 0, 0));
            Box(shed, "Settled snow on shelter", new Vector3(0, 2.31f, -.03f), new Vector3(3.42f, .14f, 2.58f), snow, new Vector3(-5, 0, 0));
            for (int i = 0; i < 3; i++)
            {
                var bale = Box(shed, "Stacked hay bale", new Vector3(.9f, .18f + i * .28f, -.6f), new Vector3(.8f, .3f, .6f), straw);
                Box(bale, "Bale binding", Vector3.zero, new Vector3(.045f, .32f, .62f), dark);
            }
            Box(shed, "Feeding trough bottom", new Vector3(-.55f, .23f, -.5f), new Vector3(1.05f, .12f, .4f), woodLight);
            for (int i = 0; i < 2; i++) Box(shed, "Trough rim", new Vector3(-.55f, .33f, -.5f + (i == 0 ? -.23f : .23f)), new Vector3(1.12f, .18f, .08f), timber);
            Box(shed, "Feed in trough", new Vector3(-.55f, .31f, -.5f), new Vector3(.94f, .04f, .34f), straw);
            var coop = new GameObject("Hen coop / open nesting box").transform;
            coop.SetParent(shed, false); coop.localPosition = new Vector3(-2.5f, 0, .2f);
            for (int i = 0; i < 4; i++) Box(coop, "Coop leg", new Vector3(i < 2 ? -.44f : .44f, .25f, i % 2 == 0 ? -.4f : .4f), new Vector3(.09f, .5f, .09f), timber);
            Box(coop, "Nesting floor", new Vector3(0, .5f, 0), new Vector3(1, .1f, .95f), timber);
            Box(coop, "Nesting straw", new Vector3(0, .57f, 0), new Vector3(.88f, .08f, .8f), straw);
            Box(coop, "Coop back", new Vector3(0, .91f, -.45f), new Vector3(1, .8f, .08f), timber);
            for (int i = 0; i < 2; i++) Box(coop, "Coop side", new Vector3(i == 0 ? -.47f : .47f, .91f, 0), new Vector3(.08f, .8f, .92f), timber);
            Box(coop, "Coop roof", new Vector3(0, 1.35f, -.03f), new Vector3(1.2f, .12f, 1.17f), roof, new Vector3(-7, 0, 0));
            Box(coop, "Snow on coop", new Vector3(0, 1.44f, -.03f), new Vector3(1.24f, .1f, 1.2f), snow, new Vector3(-7, 0, 0));
            Box(coop, "Hen ramp", new Vector3(0, .28f, .94f), new Vector3(.42f, .07f, 1.3f), woodLight, new Vector3(24, 0, 0));
            for (int i = 0; i < 5; i++) Box(coop, "Ramp foothold", new Vector3(0, .49f - i * .09f, .48f + i * .21f), new Vector3(.46f, .045f, .065f), timber);
            Box(shed, "Caretaker warm lantern", new Vector3(.9f, 1.75f, .82f), new Vector3(.17f, .23f, .17f), dark);
            var glass = Material("Caretaker lantern amber glass", new Color(1, .64f, .27f));
            glass.EnableKeyword("_EMISSION"); glass.SetColor("_EmissionColor", new Color(1.9f, .83f, .22f));
            Box(shed, "Lantern luminous glass", new Vector3(.9f, 1.75f, .927f), new Vector3(.12f, .15f, .065f), glass);
            var lightObject = new GameObject("Shelter warm lamp"); lightObject.transform.SetParent(shed, false);
            lightObject.transform.localPosition = new Vector3(.9f, 1.73f, 1.01f);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1, .68f, .36f);
            light.range = 8; light.intensity = 1.1f; light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            Box(shed, "Feed sack", new Vector3(-1.1f, .25f, -.6f), new Vector3(.42f, .5f, .36f), straw, new Vector3(0, 9, -6));
        }

        Material Material(string name, Color color)
        {
            var mat = new Material(Shader.Find("Standard")) { name = name, color = color };
            mat.SetFloat("_Glossiness", .15f); materials.Add(mat); return mat;
        }

        static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, Vector3 angles = default)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.SetParent(parent, false); box.transform.localPosition = position;
            box.transform.localRotation = Quaternion.Euler(angles); box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            // Scenery has no movement collider: shared navigation remains the single collision authority.
            Destroy(box.GetComponent<Collider>()); return box.transform;
        }

        void OnDestroy()
        {
            if (bleat != null) Destroy(bleat); if (cluck != null) Destroy(cluck); if (squawk != null) Destroy(squawk);
            foreach (var material in materials) if (material != null) Destroy(material);
        }

        void LateUpdate() => PrepareOwnerCarry();

        // Explicit view binding also supports manual offscreen Camera.Render(), where Unity
        // Camera.main returns null because the real first-person camera is intentionally disabled.
        public void PrepareOwnerCarry(Camera cameraOverride = null)
        {
            var room = session != null ? session.State : null;
            var me = room != null ? room.Player(session.LocalId) : null;
            bool holding = yard != null && yard.activeSelf && IsCarrying(room, me) && !me.down;
            if (supportHands != null && supportHands.gameObject.activeSelf != holding) supportHands.gameObject.SetActive(holding);
            if (!holding || chicken == null)
            {
                if (chicken != null) chicken.gameObject.layer = 0;
                return;
            }
            if (cameraOverride != null) ownerCamera = cameraOverride;
            else
            {
                if (ownerPlayer == null) ownerPlayer = FindFirstObjectByType<DeadAirPlayer>();
                if (ownerPlayer != null) ownerCamera = ownerPlayer.ViewCamera;
            }
            if (ownerCamera == null) return;
            // Owner-only view placement follows the camera after camera bob/crouch are applied.
            // Shared source coordinates stay at the carrier's chest for handoff range checks.
            var camera = ownerCamera.transform;
            chicken.gameObject.layer = OwnerCarryLayer;
            chicken.transform.position = camera.TransformPoint(new Vector3(.04f, -.48f, .81f));
            chicken.transform.rotation = camera.rotation * Quaternion.Euler(-7, -12, 3);
            if (supportHands == null) CreateSupportHands();
            supportHands.gameObject.SetActive(true);
            supportHands.position = camera.TransformPoint(new Vector3(.04f, -.36f, .81f));
            supportHands.rotation = camera.rotation;
        }

        void CreateSupportHands()
        {
            supportHands = new GameObject("Owner hen carry / supporting gloves").transform;
            supportHands.SetParent(yard.transform, false);
            var sleeve = Material("Caretaker animal-handling sleeves", new Color(.12f, .16f, .18f));
            var glove = Material("Canvas animal-handling gloves", new Color(.29f, .265f, .205f));
            for (int side = -1; side <= 1; side += 2)
            {
                var cuff = Box(supportHands, "Supporting sleeve", new Vector3(side * .19f, -.12f, -.13f), new Vector3(.13f, .13f, .24f), sleeve, new Vector3(8, side * 17, side * -12));
                var palm = Box(supportHands, "Supporting gloved palm", new Vector3(side * .145f, -.055f, -.01f), new Vector3(.11f, .075f, .15f), glove, new Vector3(-8, side * -12, side * -14));
                Box(palm, "Glove fingers curled under hen", new Vector3(-side * .12f, .42f, .34f), new Vector3(.9f, .8f, .38f), glove, new Vector3(-32, 0, 0));
                Box(palm, "Glove thumb", new Vector3(-side * .44f, .27f, -.08f), new Vector3(.27f, .75f, .54f), glove, new Vector3(0, side * -22, side * 17));
                foreach (var renderer in cuff.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var renderer in supportHands.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var part in supportHands.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = OwnerCarryLayer;
            var bounce = new GameObject("Subtle carried hen bounce light").AddComponent<Light>();
            bounce.transform.SetParent(supportHands, false);
            bounce.transform.localRotation = Quaternion.Euler(28, -28, 0);
            bounce.type = LightType.Directional; bounce.color = new Color(.77f, .83f, .93f);
            bounce.intensity = .5f; bounce.cullingMask = 1 << OwnerCarryLayer;
            bounce.shadows = LightShadows.None; bounce.renderMode = LightRenderMode.ForcePixel;
        }
    }
}
