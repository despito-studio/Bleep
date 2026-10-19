using UnityEngine;

namespace DeadAir
{
    [DisallowMultipleComponent]
    public sealed class DeadAirArtMotionRotor : MonoBehaviour
    {
        [SerializeField] Vector3 localAxis = Vector3.forward;
        [SerializeField] float degreesPerSecond = 160;
        [SerializeField] bool requiresPower = true;
        [SerializeField, Min(.01f)] float accelerationSeconds = .9f, coastSeconds = 1.8f;
        Quaternion authored;
        float angle, speed;
        bool initialized;
        public float Speed => speed;

        public static DeadAirArtMotionRotor Attach(Transform pivot, Vector3 localAxis, float degreesPerSecond, bool requiresPower = true)
        {
            var rotor = pivot.GetComponent<DeadAirArtMotionRotor>() ?? pivot.gameObject.AddComponent<DeadAirArtMotionRotor>();
            rotor.localAxis = localAxis.sqrMagnitude > .0001f ? localAxis.normalized : Vector3.forward;
            rotor.degreesPerSecond = degreesPerSecond; rotor.requiresPower = requiresPower;
            return rotor;
        }

        public void Advance(bool powered, float deltaTime, bool reset = false, float clock = 0)
        {
            if (!initialized)
            {
                initialized = true; authored = transform.localRotation;
                DeadAirArtMotion.MakeDynamic(transform);
            }
            float target = !requiresPower || powered ? degreesPerSecond : 0;
            if (reset)
            {
                speed = target; angle = Mathf.Repeat(clock * target, 360);
                transform.localRotation = authored * Quaternion.AngleAxis(angle, localAxis);
                return;
            }
            if (deltaTime <= 0 || Mathf.Abs(speed) < .001f && target == 0) return;
            float duration = Mathf.Max(.01f, target == 0 ? coastSeconds : accelerationSeconds);
            float decay = Mathf.Exp(-deltaTime / duration);
            // Integrate the acceleration curve so the spin is independent of render frame rate.
            float travel = target * deltaTime + (speed - target) * duration * (1 - decay);
            speed = target + (speed - target) * decay;
            if (target == 0 && Mathf.Abs(speed) < .01f) speed = 0;
            angle = Mathf.Repeat(angle + travel, 360);
            transform.localRotation = authored * Quaternion.AngleAxis(angle, localAxis);
        }
    }
}
