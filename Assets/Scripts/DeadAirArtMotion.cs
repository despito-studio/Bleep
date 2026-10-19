using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirArtMotion
    {
        sealed class RotationChannel
        {
            readonly Transform pivot;
            readonly Quaternion authored;
            readonly Vector3 axis;
            readonly float offAngle, onAngle, duration;
            float angle, from, target, age;
            bool observed, previous;

            public RotationChannel(Transform pivot, Vector3 axis, float offAngle, float onAngle, float duration)
            {
                this.pivot = pivot; this.axis = axis;
                this.offAngle = offAngle; this.onAngle = onAngle; this.duration = duration;
                authored = pivot.localRotation;
                MakeDynamic(pivot);
            }

            public void Apply(bool value, float deltaTime, bool reset)
            {
                if (pivot == null) return;
                if (!observed || reset)
                {
                    observed = true; previous = value;
                    angle = from = target = value ? onAngle : offAngle; age = duration;
                    pivot.localRotation = authored * Quaternion.AngleAxis(angle, axis);
                    return;
                }
                if (value != previous)
                {
                    previous = value; from = angle; target = value ? onAngle : offAngle; age = 0;
                }
                if (age >= duration || deltaTime <= 0) return;
                age = Mathf.Min(duration, age + deltaTime);
                angle = Mathf.LerpUnclamped(from, target, Ease(age / duration));
                pivot.localRotation = authored * Quaternion.AngleAxis(angle, axis);
            }
        }

        readonly List<DeadAirArtMotionRotor> rotors = new List<DeadAirArtMotionRotor>();
        readonly List<DeadAirArtMotionSlidingLeaf> sliders = new List<DeadAirArtMotionSlidingLeaf>();
        readonly RotationChannel powerLever, breakerHandle, pressureWheel;
        float previousElapsed = -1;
        string previousChapter;

        DeadAirArtMotion(Transform root)
        {
            if (root == null) return;
            Transform valveRoot = null, valveWheel = null;
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
            {
                if (part.name == "state:powerLever")
                    powerLever = new RotationChannel(part, Vector3.forward, -.55f * Mathf.Rad2Deg, .7f * Mathf.Rad2Deg, .32f);
                else if (part.name == "state:breakerHandle")
                    breakerHandle = new RotationChannel(part, Vector3.right, 45, -45, .24f);
                else if (part.name == "interactive:pressure-valve") valveRoot = part;
                else if (part.name == "motion:pressureValveWheel") valveWheel = part;
                if (part.name.StartsWith("mirror:", StringComparison.Ordinal) && part.GetComponent<Renderer>() != null && part.GetComponent<DeadAirArtMirror>() == null)
                    DeadAirArtMirror.Attach(part.GetComponent<Renderer>(), Vector3.forward);
                if (part.name.StartsWith("fan:", StringComparison.Ordinal) && part.GetComponent<DeadAirArtMotionRotor>() == null)
                {
                    // Older saved offices put just the blades beneath the fan anchor.
                    if (part.GetComponentInChildren<DeadAirArtMotionRotor>(true) == null)
                        DeadAirArtMotionRotor.Attach(part, Vector3.forward, -14 * Mathf.Rad2Deg);
                }
            }
            if (valveWheel == null && valveRoot != null)
            {
                var parts = new List<Transform>();
                foreach (Transform part in valveRoot)
                    if (part.name == "Valve wheel" || part.name == "Valve wheel hub" || part.name == "Valve spoke") parts.Add(part);
                if (parts.Count > 0)
                {
                    valveWheel = new GameObject("motion:pressureValveWheel").transform;
                    valveWheel.SetParent(valveRoot, false);
                    foreach (var part in parts) part.SetParent(valveWheel, true);
                }
            }
            if (valveWheel != null)
                pressureWheel = new RotationChannel(valveWheel, Vector3.forward, 0, 180, .85f);
            rotors.AddRange(root.GetComponentsInChildren<DeadAirArtMotionRotor>(true));
            sliders.AddRange(root.GetComponentsInChildren<DeadAirArtMotionSlidingLeaf>(true));
        }

        public static DeadAirArtMotion Attach(Transform root) => new DeadAirArtMotion(root);

        public void Apply(RoomState state, float deltaTime)
        {
            if (state == null) return;
            bool reset = previousElapsed < 0 || state.elapsed < previousElapsed || previousChapter != state.chapterId;
            previousElapsed = state.elapsed; previousChapter = state.chapterId;
            float step = state.paused ? 0 : Mathf.Max(0, deltaTime);
            bool powered = state.power && state.breakerOn;
            powerLever?.Apply(state.power, step, reset);
            breakerHandle?.Apply(state.breakerOn, step, reset);
            pressureWheel?.Apply(state.valveOpened, step, reset);
            foreach (var rotor in rotors)
                if (rotor != null && rotor.gameObject.activeInHierarchy) rotor.Advance(powered, step, reset, state.elapsed);
            foreach (var slider in sliders)
            {
                if (slider == null || !slider.gameObject.activeInHierarchy) continue;
                bool nearby = false;
                if (state.players != null)
                    foreach (var player in state.players)
                    {
                        if (player == null) continue;
                        var position = DeadAirOfficeLayout.Position(player.x, player.y, player.z);
                        if (slider.IsWithinSensor(position)) { nearby = true; break; }
                    }
                slider.Advance(nearby, step, reset);
            }
        }

        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        public static void MakeDynamic(Transform pivot)
        {
            if (pivot == null) return;
            foreach (var part in pivot.GetComponentsInChildren<Transform>(true)) part.gameObject.isStatic = false;
            foreach (var renderer in pivot.GetComponentsInChildren<Renderer>(true)) renderer.allowOcclusionWhenDynamic = false;
        }
    }
}
