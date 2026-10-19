using UnityEngine;

namespace DeadAir
{
    [DisallowMultipleComponent]
    public sealed class DeadAirArtMotionSlidingLeaf : MonoBehaviour
    {
        [SerializeField] Vector3 closedLocalPosition, openLocalPosition;
        [SerializeField] Transform sensorOrigin;
        [SerializeField, Min(.1f)] float activationRadius = 2.5f;
        [SerializeField, Min(.1f)] float travelSeconds = .7f;
        [SerializeField, Min(0)] float holdOpenSeconds = 1.6f;
        Vector3 from;
        float age, clearAge;
        bool initialized, open;
        public bool IsOpen => open;

        public static DeadAirArtMotionSlidingLeaf Attach(Transform leaf, Vector3 closedLocalPosition,
            Vector3 openLocalPosition, Transform sensorOrigin, float activationRadius = 2.5f)
        {
            var slider = leaf.GetComponent<DeadAirArtMotionSlidingLeaf>() ?? leaf.gameObject.AddComponent<DeadAirArtMotionSlidingLeaf>();
            slider.closedLocalPosition = closedLocalPosition; slider.openLocalPosition = openLocalPosition;
            slider.sensorOrigin = sensorOrigin; slider.activationRadius = activationRadius;
            DeadAirArtMotion.MakeDynamic(leaf);
            return slider;
        }

        public bool IsWithinSensor(Vector3 worldPosition)
        {
            var center = sensorOrigin != null ? sensorOrigin.position : transform.parent != null ? transform.parent.position : transform.position;
            var offset = worldPosition - center;
            return Mathf.Abs(offset.y) < 2.4f && offset.x * offset.x + offset.z * offset.z < activationRadius * activationRadius;
        }

        public void Advance(bool nearby, float deltaTime, bool reset = false)
        {
            if (!initialized || reset)
            {
                initialized = true; open = nearby; age = travelSeconds; clearAge = 0;
                transform.localPosition = nearby ? openLocalPosition : closedLocalPosition;
                from = transform.localPosition;
                return;
            }
            if (deltaTime <= 0) return;
            clearAge = nearby ? 0 : clearAge + deltaTime;
            bool desired = nearby || open && clearAge < holdOpenSeconds;
            if (desired != open)
            {
                open = desired; from = transform.localPosition; age = 0;
            }
            if (age >= travelSeconds) return;
            age = Mathf.Min(travelSeconds, age + deltaTime);
            transform.localPosition = Vector3.LerpUnclamped(from, open ? openLocalPosition : closedLocalPosition,
                DeadAirArtMotion.Ease(age / Mathf.Max(.01f, travelSeconds)));
        }
    }
}
