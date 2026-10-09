using UnityEngine;

namespace DeadAir
{
    // The host publishes the logical state; every peer animates the same hinge locally.
    // Keep the collider on the leaf so it follows the visible door throughout the swing.
    public sealed class DeadAirHingedDoor : MonoBehaviour
    {
        Quaternion closedRotation;
        float openAngle = 95, angle, from, target, elapsed, cycleAngle, cycleTime = -1;
        bool initialized, open;
        const float SwingSeconds = .48f;
        public bool IsAjar => Mathf.Abs(angle) > 3 || open || cycleTime >= 0;
        public float Angle => angle;

        public static float OpeningSign(DoorData door, PlayerState player)
        {
            // The authored leaf extends along local +Z, so positive yaw moves it
            // towards the casing's +X face. Some doors have a negative authored
            // angle: compensate for that before publishing the swing multiplier.
            var normal = DeadAirOfficeLayout.Rotation(door.rotation) * Vector3.right;
            var look = Quaternion.Euler(0, 180 - player.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
            float direction = Vector3.Dot(look, normal);
            if (Mathf.Abs(direction) < .05f)
            {
                var toDoor = DeadAirOfficeLayout.Position(door.x - player.x, 0, door.z - player.z);
                direction = Vector3.Dot(toDoor, normal);
            }
            float authoredSign = door.openAngle < -.1f ? -1f : 1f;
            return (direction >= 0 ? 1f : -1f) * authoredSign;
        }

        public void Initialize(float degrees)
        {
            if (initialized) return;
            initialized = true;
            closedRotation = transform.localRotation;
            openAngle = degrees;
            elapsed = SwingSeconds;
        }

        public void SetOpen(bool value, float sign = 1f)
        {
            if (!initialized) Initialize(95);
            float desiredTarget = value ? openAngle * (sign < 0 ? -1f : 1f) : 0;
            if (open == value && Mathf.Abs(target - desiredTarget) < .1f && cycleTime < 0) return;
            open = value; cycleTime = -1;
            from = angle; target = desiredTarget; elapsed = 0;
        }

        public void PlayAccessCycle(float sign = 1f)
        {
            if (!initialized) Initialize(95);
            cycleAngle = openAngle * (sign < 0 ? -1f : 1f);
            cycleTime = 0; open = false; from = angle;
        }

        public void Advance(float deltaTime)
        {
            if (!initialized || deltaTime <= 0) return;
            if (cycleTime < 0 && elapsed >= SwingSeconds) return;
            float previousAngle = angle;
            if (cycleTime >= 0)
            {
                cycleTime += deltaTime;
                if (cycleTime < .38f) angle = Mathf.Lerp(from, cycleAngle, Ease(cycleTime / .38f));
                else if (cycleTime < .72f) angle = cycleAngle;
                else angle = Mathf.Lerp(cycleAngle, 0, Ease((cycleTime - .72f) / .44f));
                if (cycleTime >= 1.16f) { cycleTime = -1; angle = target = 0; elapsed = SwingSeconds; }
            }
            else
            {
                elapsed += deltaTime;
                angle = Mathf.Lerp(from, target, Ease(elapsed / SwingSeconds));
            }
            if (angle != previousAngle)
                transform.localRotation = closedRotation * Quaternion.Euler(0, angle, 0);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    }
}
