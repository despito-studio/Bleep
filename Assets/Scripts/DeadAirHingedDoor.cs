using UnityEngine;

namespace DeadAir
{
    // The host publishes the logical state; every peer animates the same hinge locally.
    // Keep the collider on the leaf so it follows the visible door throughout the swing.
    public sealed class DeadAirHingedDoor : MonoBehaviour
    {
        Quaternion closedRotation;
        float openAngle = 95, angle, from, target, elapsed, cycleTime = -1;
        bool initialized, open;
        const float SwingSeconds = .48f;
        public bool IsAjar => Mathf.Abs(angle) > 3 || open || cycleTime >= 0;
        public float Angle => angle;

        public void Initialize(float degrees)
        {
            if (initialized) return;
            initialized = true;
            closedRotation = transform.localRotation;
            openAngle = degrees;
        }

        public void SetOpen(bool value)
        {
            if (!initialized) Initialize(95);
            if (open == value && cycleTime < 0) return;
            open = value; cycleTime = -1;
            from = angle; target = value ? openAngle : 0; elapsed = 0;
        }

        public void PlayAccessCycle()
        {
            if (!initialized) Initialize(95);
            cycleTime = 0; open = false; from = angle;
        }

        public void Advance(float deltaTime)
        {
            if (!initialized || deltaTime <= 0) return;
            if (cycleTime >= 0)
            {
                cycleTime += deltaTime;
                if (cycleTime < .38f) angle = Mathf.Lerp(from, openAngle, Ease(cycleTime / .38f));
                else if (cycleTime < .72f) angle = openAngle;
                else angle = Mathf.Lerp(openAngle, 0, Ease((cycleTime - .72f) / .44f));
                if (cycleTime >= 1.16f) { cycleTime = -1; angle = target = 0; elapsed = SwingSeconds; }
            }
            else
            {
                elapsed += deltaTime;
                angle = Mathf.Lerp(from, target, Ease(elapsed / SwingSeconds));
            }
            transform.localRotation = closedRotation * Quaternion.Euler(0, angle, 0);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    }
}
