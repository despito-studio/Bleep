using System;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirGazeValidation
    {
        [MenuItem("Dead Air/Validate Character Gaze")]
        public static void Validate()
        {
            var idle = new DeadAirGaze(); idle.Step(0, 0, 0, 0, true);
            for (int i = 0; i < 60; i++) idle.Step(-30 * Mathf.Deg2Rad, 20 * Mathf.Deg2Rad, 0, 1f / 60);
            Require(Mathf.Abs(Mathf.DeltaAngle(idle.BodyYawDegrees, 180)) < .001f && Mathf.Abs(idle.HeadYawDegrees - 30) < .01f,
                "Small idle glances must move the head while keeping the feet/body planted.");
            Require(Mathf.Abs(idle.HeadPitchDegrees - 20) < .01f, "Network pitch did not produce a head nod.");
            var walking = new DeadAirGaze(); walking.Step(0, 0, 0, 0, true);
            for (int i = 0; i < 15; i++)
            {
                idle.Step(-90 * Mathf.Deg2Rad, 2, 0, 1f / 60);
                walking.Step(-90 * Mathf.Deg2Rad, 2, 2, 1f / 60);
                Require(Mathf.Abs(idle.HeadYawDegrees) <= 55 && Mathf.Abs(idle.HeadPitchDegrees) <= 55,
                    "A rapid turn exceeded the adult neck limits.");
            }
            Require(Mathf.Abs(Mathf.DeltaAngle(walking.BodyYawDegrees, 270)) < Mathf.Abs(Mathf.DeltaAngle(idle.BodyYawDegrees, 270)),
                "The walking body must follow a turn faster than the idle body.");
            for (int i = 0; i < 120; i++) idle.Step(-90 * Mathf.Deg2Rad, 0, 0, 1f / 60);
            Require(!idle.TurningInPlace && Mathf.Abs(Mathf.DeltaAngle(idle.BodyYawDegrees, 270)) <= 3.01f,
                "An idle turn failed to bring the whole body around.");
            var wrap = new DeadAirGaze(); wrap.Step(179 * Mathf.Deg2Rad, 0, 0, 0, true);
            for (int i = 0; i < 60; i++) wrap.Step(-179 * Mathf.Deg2Rad, 0, 0, 1f / 60);
            Require(Mathf.Abs(wrap.HeadYawDegrees + 2) < .01f && !wrap.TurningInPlace, "Yaw wrapping caused a full-circle turn.");
            float frozenBody = wrap.BodyYawDegrees, frozenHead = wrap.HeadYawDegrees;
            wrap.Step(0, 1, 8, 0);
            Require(wrap.BodyYawDegrees == frozenBody && wrap.HeadYawDegrees == frozenHead, "Paused gaze continued moving.");
            wrap.Step(-90 * Mathf.Deg2Rad, 0, 8, .016f, true, true, 90);
            Require(wrap.BodyYawDegrees == 90 && Mathf.Abs(wrap.HeadYawDegrees) <= 55, "A ladder/transit root was overwritten by body follow.");
            wrap.Step(-45 * Mathf.Deg2Rad, 0, 0, 0, true);
            Require(Mathf.Abs(Mathf.DeltaAngle(wrap.BodyYawDegrees, 225)) < .001f && wrap.HeadYawDegrees == 0,
                "Teleport/reset retained an old pose.");
            Require(Quaternion.Angle(AtRate(30), AtRate(60)) < .5f, "Gaze convergence depended materially on render frame rate.");
            foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" }) ValidateRig(role);
            Debug.Log("DEAD AIR GAZE PASSED: bounded yaw/nods, idle body follow, faster moving turns, wrap-safe smoothing, pause/constrained/reset states and nonaccumulating source head poses on all four rigs.");
        }

        static Quaternion AtRate(int rate)
        {
            var gaze = new DeadAirGaze(); gaze.Step(0, 0, 0, 0, true);
            for (int i = 0; i < rate; i++) gaze.Step(-90 * Mathf.Deg2Rad, 0, 2, 1f / rate);
            return gaze.BodyRotation;
        }

        static void ValidateRig(string role)
        {
            var actor = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
            try
            {
                var motion = actor.GetComponent<DeadAirCharacterMotion>();
                motion.SetEquipmentPreview(new PlayerState { characterClass = role, clothingMask = -1 });
                Transform head = null;
                foreach (var bone in actor.GetComponentsInChildren<Transform>(true)) if (bone.name == "head") { head = bone; break; }
                Require(head != null, role + " lost its actual source head bone.");
                motion.SetGazePreview(0, 0, 0, 0, true);
                Quaternion rest = head.rotation;
                for (int i = 0; i < 60; i++) motion.SetGazePreview(-30 * Mathf.Deg2Rad, 20 * Mathf.Deg2Rad, 1f / 60);
                Require(Quaternion.Angle(rest, head.rotation) > 25 && Quaternion.Angle(rest, head.rotation) < 45,
                    role + " did not apply yaw/pitch to the source head after sampling its authored pose.");
                Quaternion sampled = head.rotation;
                for (int i = 0; i < 30; i++) motion.SetGazePreview(-30 * Mathf.Deg2Rad, 20 * Mathf.Deg2Rad, 0);
                Require(Quaternion.Angle(sampled, head.rotation) < .01f, role + " accumulated a second head rotation when resampling.");
                motion.SetGazePreview(0, 0, 0, 0, true);
                Require(Quaternion.Angle(rest, head.rotation) < .01f, role + " failed to restore its source head rest pose.");
                motion.SetFirstPersonPresentation(true);
                var owner = new PlayerState { characterClass = role, clothingMask = 0, flashlight = true, inventory = new System.Collections.Generic.List<string> { "flashlight" } };
                motion.SetLocomotionPose(true, 2); motion.Present(owner, 1, false);
                Require(actor.GetComponent<DeadAirAppearance>().CurrentMask == 0,
                    role + " owner full body discarded the received clothing mask.");
                foreach (var renderer in actor.GetComponentsInChildren<MeshRenderer>(true))
                    Require(!renderer.gameObject.activeInHierarchy, role + " owner body duplicated visible attached equipment.");
                foreach (var light in actor.GetComponentsInChildren<Light>(true))
                    Require(!light.enabled || !light.gameObject.activeInHierarchy, role + " owner body duplicated an equipment light.");
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Character gaze validation: " + message); }
    }
}
