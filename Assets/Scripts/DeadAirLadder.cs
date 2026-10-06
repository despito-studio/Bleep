using UnityEngine;

namespace DeadAir
{
    // The host and every camera follow the same staged route. Feet rise above
    // the real window sill before crossing the facade, then settle on the floor.
    public static class DeadAirLadder
    {
        public const float Duration = 5.15f;
        public const float BreakAt = 3.70f;
        public static readonly Vector3 Foot = new Vector3(9, 0, 7.55f);
        public static readonly Vector3 Inside = new Vector3(9, 4.2f, 5.0f);
        static readonly Vector3 Top = new Vector3(9, 4.18f, 6.82f);
        static readonly Vector3 SillOutside = new Vector3(9, 5.52f, 6.60f);
        static readonly Vector3 SillInside = new Vector3(9, 5.52f, 5.20f);

        public static void Begin(PlayerState actor, RoomState state, bool up)
        {
            actor.ladderFromX = actor.x; actor.ladderFromY = actor.y; actor.ladderFromZ = actor.z;
            actor.ladderStartedAt = state.elapsed; actor.ladderUp = up; actor.ladderClimbing = true;
            actor.camera = actor.flashlight = actor.sprinting = actor.mapOpen = actor.uniqueItemEquipped = false;
            actor.uniqueActionCancelled = true;
        }

        public static float PathAge(float age, bool up) => up ? age : Duration + .35f - age;

        public static Vector3 Position(PlayerState actor, float age)
        {
            if (age <= .35f)
                return Vector3.Lerp(new Vector3(-actor.ladderFromX, actor.ladderFromY, actor.ladderFromZ),
                    actor.ladderUp ? Foot : Inside, Smooth(age / .35f));
            return Route(PathAge(Mathf.Clamp(age, .35f, Duration), actor.ladderUp));
        }

        static Vector3 Route(float t)
        {
            if (t <= .35f) return Foot;
            if (t < 3.45f) return Vector3.Lerp(Foot, Top, Smooth((t - .35f) / 3.10f));
            if (t < 3.80f) return Top;
            if (t < 4.22f) return Vector3.Lerp(Top, SillOutside, Smooth((t - 3.80f) / .42f));
            if (t < 4.70f) return Vector3.Lerp(SillOutside, SillInside, Smooth((t - 4.22f) / .48f));
            return Vector3.Lerp(SillInside, Inside, Smooth((t - 4.70f) / .45f));
        }

        public static float EyeHeight(float age, bool up)
        {
            float t = age < .35f ? (up ? .35f : Duration) : PathAge(age, up);
            float crouch = Smooth((t - 3.80f) / .42f) * (1 - Smooth((t - 4.70f) / .45f));
            return Mathf.Lerp(1.65f, .90f, crouch);
        }

        public static bool Advance(RoomState state)
        {
            bool changed = false;
            foreach (var actor in state.players)
            {
                if (!actor.ladderClimbing) continue;
                changed = true;
                if (state.phase != "playing" || !state.ladderPlaced || actor.down || !string.IsNullOrEmpty(actor.hiding))
                {
                    Cancel(actor); continue;
                }
                float age = Mathf.Max(0, state.elapsed - actor.ladderStartedAt);
                if (actor.ladderUp && age >= BreakAt && !state.windowBroken)
                {
                    state.windowBroken = state.quest.windowBroken = true;
                    state.message = actor.name + " розбив вікно кабінету директора.";
                }
                Vector3 position = Position(actor, age);
                actor.x = -position.x; actor.y = position.y; actor.z = position.z;
                actor.yaw = 0; actor.pitch = 0;
                actor.flashlight = actor.camera = actor.sprinting = false;
                if (age < Duration) continue;
                actor.ladderClimbing = false;
                actor.yaw = actor.ladderUp ? 0 : Mathf.PI;
                state.message = actor.ladderUp ? "Ви проникли в кабінет директора." : "Ви спустилися по драбині на передній двір.";
            }
            return changed;
        }

        public static void Cancel(PlayerState actor)
        {
            Vector3 endpoint = actor.ladderUp ? Foot : Inside;
            actor.x = -endpoint.x; actor.y = endpoint.y; actor.z = endpoint.z;
            actor.yaw = actor.ladderUp ? Mathf.PI : 0; actor.pitch = 0;
            actor.ladderClimbing = false;
        }

        static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
    }
}
