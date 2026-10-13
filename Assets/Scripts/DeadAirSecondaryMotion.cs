using UnityEngine;

namespace DeadAir
{
    // Render-only dynamics on the authored deform chains; the older wardrobe
    // keeps its bounded proxy fallback. Gameplay capsules are never modified.
    public sealed class DeadAirSecondaryMotion : MonoBehaviour
    {
        public Transform hair, chest, tie;
        Vector3 angle, velocity, chestOffset, chestVelocity;
        float oldYaw, phase;
        bool initialized;
        DeadAirSpringBoneRig authored;
        bool lastClothed = true;
        public bool UsesAuthoredBones => authored != null && authored.BoneCount > 0;
        public int BoneCount => authored == null ? 0 : authored.BoneCount;
        public int HairBoneCount => authored == null ? 0 : authored.HairBoneCount;
        public int ClothBoneCount => authored == null ? 0 : authored.ClothBoneCount;
        public int BreastBoneCount => authored == null ? 0 : authored.BreastBoneCount;
        public float MaximumAngle => UsesAuthoredBones ? authored.MaximumAngle : angle.magnitude;
        public float ChestDisplacement => UsesAuthoredBones ? authored.BreastDisplacement : chestOffset.magnitude;
        public float MaximumCollisionPenetration => authored == null ? 0 : authored.MaximumCollisionPenetration;

        public void ConfigureAuthoredRig(string role)
        {
            authored = new DeadAirSpringBoneRig(transform, role);
            initialized = false;
        }

        public void ResetMotion()
        {
            angle = velocity = chestOffset = chestVelocity = Vector3.zero;
            phase = 0; initialized = false; authored?.Reset(); ApplyPose(true);
        }

        /// <summary>Capture a freshly sampled body pose, never an already offset spring pose.</summary>
        public void CaptureAnimatedPose()
        {
            authored?.CaptureAnimatedPose();
        }

        public void ReapplyPose()
        {
            if (UsesAuthoredBones) authored.Reapply();
            else ApplyPose(lastClothed);
        }

        public void Step(float dt, float yaw, float speed, bool clothed, bool reset = false)
        {
            lastClothed = clothed;
            if (UsesAuthoredBones) { authored.Step(dt, clothed, reset); return; }
            if (reset || !initialized) { ResetMotion(); initialized = true; oldYaw = yaw; }
            dt = Mathf.Clamp(dt, 0, .05f);
            if (dt <= 0) { ApplyPose(clothed); return; }
            float turn = Mathf.Clamp(Mathf.DeltaAngle(oldYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) / dt, -240, 240);
            oldYaw = yaw;
            phase += Mathf.Clamp(speed, 0, 8) * dt * 4;
            var drive = new Vector3(Mathf.Clamp(speed, 0, 8) * -.45f + Mathf.Sin(phase) * speed * .08f, turn * -.018f, turn * -.009f);
            // Stable implicit spring, including a stalled-frame clamp and bounds.
            Damp(ref angle, ref velocity, drive, 13, dt);
            angle = Vector3.ClampMagnitude(angle, 6);
            float vertical = Mathf.Clamp(speed, 0, 8) * (.00035f + Mathf.Sin(phase * 2) * .00020f);
            Damp(ref chestOffset, ref chestVelocity, new Vector3(0, -vertical, 0), 19, dt);
            chestOffset = Vector3.ClampMagnitude(chestOffset, .006f);
            ApplyPose(clothed);
        }

        static void Damp(ref Vector3 position, ref Vector3 speed, Vector3 target, float frequency, float dt)
        {
            float denominator = 1 + 2 * frequency * dt + frequency * frequency * dt * dt;
            speed = (speed + (target - position) * (frequency * frequency * dt)) / denominator;
            position += speed * dt;
        }
        void ApplyPose(bool clothed)
        {
            if (hair != null) hair.localRotation = Quaternion.Euler(angle);
            if (tie != null) tie.localRotation = Quaternion.Euler(angle.x * 1.4f, 0, angle.z * 1.6f);
            if (chest != null) chest.localPosition = chestOffset * (clothed ? 1 : .65f);
        }
        void OnDisable() { ResetMotion(); }
    }
}
