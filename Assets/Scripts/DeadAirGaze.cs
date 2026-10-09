using UnityEngine;

namespace DeadAir
{
    /// <summary>Shared local/remote presentation, independent of the authoritative view direction.</summary>
    public sealed class DeadAirGaze
    {
        public const float IdleFreeAngle = 40, MaximumHeadYaw = 55, MaximumHeadPitch = 55;
        public float BodyYawDegrees { get; private set; }
        public float HeadYawDegrees { get; private set; }
        public float HeadPitchDegrees { get; private set; }
        public Quaternion BodyRotation => Quaternion.Euler(0, BodyYawDegrees, 0);
        public bool TurningInPlace { get; private set; }
        bool initialized;

        public static float UnityYaw(float networkYaw) => Mathf.Repeat(180 - networkYaw * Mathf.Rad2Deg, 360);
        public static Quaternion ViewRotation(float networkYaw, float networkPitch) =>
            Quaternion.Euler(0, UnityYaw(networkYaw), 0) * Quaternion.Euler(-networkPitch * Mathf.Rad2Deg, 0, 0);

        public void Step(float networkYaw, float networkPitch, float speed, float dt, bool reset = false,
            bool bodyLocked = false, float lockedBodyYaw = 0)
        {
            if (!Finite(networkYaw) || !Finite(networkPitch)) return;
            float viewYaw = UnityYaw(networkYaw);
            float pitch = Mathf.Clamp(networkPitch * Mathf.Rad2Deg, -MaximumHeadPitch, MaximumHeadPitch);
            if (!initialized || reset)
            {
                initialized = true;
                BodyYawDegrees = bodyLocked ? Mathf.Repeat(lockedBodyYaw, 360) : viewYaw;
                HeadYawDegrees = Mathf.Clamp(Mathf.DeltaAngle(BodyYawDegrees, viewYaw), -MaximumHeadYaw, MaximumHeadYaw);
                HeadPitchDegrees = pitch;
                TurningInPlace = false;
                return;
            }
            dt = Mathf.Clamp(Finite(dt) ? dt : 0, 0, .1f);
            if (bodyLocked)
            {
                BodyYawDegrees = Mathf.Repeat(lockedBodyYaw, 360);
                TurningInPlace = false;
            }
            else if (dt > 0)
            {
                float difference = Mathf.DeltaAngle(BodyYawDegrees, viewYaw);
                bool walking = speed > .15f;
                if (!walking && Mathf.Abs(difference) > IdleFreeAngle) TurningInPlace = true;
                if (walking || TurningInPlace)
                {
                    // Exponential convergence with an angular speed cap: no yaw
                    // discontinuity at +/-pi and no instant 180-degree body snap.
                    float turn = difference * (1 - Mathf.Exp(-(walking ? 12 : 5) * dt));
                    turn = Mathf.Clamp(turn, -(walking ? 540 : 240) * dt, (walking ? 540 : 240) * dt);
                    BodyYawDegrees = Mathf.Repeat(BodyYawDegrees + turn, 360);
                    if (Mathf.Abs(Mathf.DeltaAngle(BodyYawDegrees, viewYaw)) < 3) TurningInPlace = false;
                }
            }
            if (dt <= 0) return;
            float headTarget = Mathf.Clamp(Mathf.DeltaAngle(BodyYawDegrees, viewYaw), -MaximumHeadYaw, MaximumHeadYaw);
            HeadYawDegrees = Mathf.Clamp(Mathf.Lerp(HeadYawDegrees, headTarget, 1 - Mathf.Exp(-16 * dt)), -MaximumHeadYaw, MaximumHeadYaw);
            HeadPitchDegrees = Mathf.Clamp(Mathf.Lerp(HeadPitchDegrees, pitch, 1 - Mathf.Exp(-18 * dt)), -MaximumHeadPitch, MaximumHeadPitch);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
