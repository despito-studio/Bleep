using System;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirArtMotionValidation
    {
        [MenuItem("Dead Air/Validate Articulation and Mechanism Motion")]
        public static void Validate()
        {
            DeadAirArtMirrorValidation.ValidateMath();
            ValidateEasing();
            ValidateRotorIntegration();
            ValidateSlidingLeaf();
            ValidateSnapshotMechanisms();
            ValidateDrawerCollision();
            ValidateDroppedPropRestingHeight();
            DeadAirDoorDirectionValidation.Validate();
            Debug.Log("DEAD AIR ART MOTION PASSED: frame-independent rotor acceleration/coast, pause, sensor clearance/hold/reversal, articulated state levers and valve, snapshot rewind, moving drawer front collision and existing door replication/collider cases.");
        }

        static void ValidateEasing()
        {
            Require(DeadAirArtMotion.Ease(-1) == 0 && DeadAirArtMotion.Ease(2) == 1, "Motion easing must clamp to physical endpoints.");
            float previous = 0;
            for (int i = 0; i <= 100; i++)
            {
                float value = DeadAirArtMotion.Ease(i / 100f);
                Require(value >= previous && value <= 1, "Motion easing overshoots or reverses.");
                previous = value;
            }
            Require(DeadAirArtMotion.Ease(.01f) < .00002f && 1 - DeadAirArtMotion.Ease(.99f) < .00002f,
                "Mechanisms must enter and leave contact without an abrupt speed change.");
        }

        static void ValidateRotorIntegration()
        {
            var fixture = new GameObject("Rotor frame-rate fixture");
            try
            {
                var a = new GameObject("60 Hz"); a.transform.SetParent(fixture.transform, false);
                var b = new GameObject("20 Hz"); b.transform.SetParent(fixture.transform, false);
                var fast = DeadAirArtMotionRotor.Attach(a.transform, Vector3.forward, 802);
                var slow = DeadAirArtMotionRotor.Attach(b.transform, Vector3.forward, 802);
                fast.Advance(false, 0, true); slow.Advance(false, 0, true);
                for (int i = 0; i < 120; i++) fast.Advance(true, 1 / 60f);
                for (int i = 0; i < 40; i++) slow.Advance(true, 1 / 20f);
                Require(Quaternion.Angle(a.transform.localRotation, b.transform.localRotation) < .03f,
                    "Rotor travel differs between 20 Hz and 60 Hz.");
                Require(Mathf.Abs(fast.Speed - slow.Speed) < .03f && fast.Speed > 600 && fast.Speed < 802,
                    "Rotor acceleration must converge without overshooting its powered speed.");
                a.transform.hasChanged = false; float speed = fast.Speed;
                fast.Advance(false, 0);
                Require(!a.transform.hasChanged && fast.Speed == speed, "A paused fan must retain its pose and momentum.");
                fast.Advance(false, .4f);
                Require(fast.Speed > 0 && fast.Speed < speed, "A fan must coast rather than stop at a power cut.");
                for (int i = 0; i < 300; i++) fast.Advance(false, .1f);
                a.transform.hasChanged = false; fast.Advance(false, .1f);
                Require(fast.Speed == 0 && !a.transform.hasChanged, "A stopped rotor must stop dirtying its transform.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        static void ValidateSlidingLeaf()
        {
            var fixture = new GameObject("School slider fixture");
            try
            {
                fixture.transform.SetPositionAndRotation(new Vector3(12, 4.2f, -3), Quaternion.Euler(0, 90, 0));
                var leaf = new GameObject("Sliding Leaf").transform; leaf.SetParent(fixture.transform, false);
                var shut = new Vector3(.85f, 1.12f, .08f); var opened = new Vector3(1.42f, 1.12f, .08f);
                var slider = DeadAirArtMotionSlidingLeaf.Attach(leaf, shut, opened, fixture.transform);
                Require(slider.IsWithinSensor(fixture.transform.position + Vector3.forward), "A player approaching the doorway must trigger its sensor.");
                Require(!slider.IsWithinSensor(fixture.transform.position + Vector3.up * 4.2f), "The sensor must not activate from another storey.");
                slider.Advance(false, 0, true);
                Require(Vector3.Distance(leaf.localPosition, shut) < .0001f, "Snapshot initialization lost the authored open passage.");
                slider.Advance(true, .35f);
                Require(slider.IsOpen && leaf.localPosition.x > shut.x && leaf.localPosition.x < opened.x, "The sliding leaf must travel along its track.");
                slider.Advance(true, .4f);
                Require(Vector3.Distance(leaf.localPosition, opened) < .0001f, "The sliding leaf does not reach its open stop.");
                slider.Advance(false, 1f);
                Require(slider.IsOpen, "The door must wait for the passage to clear before returning.");
                slider.Advance(false, .65f);
                Require(!slider.IsOpen, "A clear sensor must eventually return the decorative leaf.");
                slider.Advance(true, .8f);
                Require(slider.IsOpen && Vector3.Distance(leaf.localPosition, opened) < .0001f, "An approaching player must reverse a closing leaf.");
                leaf.hasChanged = false; slider.Advance(false, 0);
                Require(!leaf.hasChanged, "Paused sliding leaves must retain their pose.");
                Require(leaf.GetComponentsInChildren<Collider>(true).Length == 0, "Decorative school sliders must not introduce gameplay collision blockers.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        static void ValidateSnapshotMechanisms()
        {
            var fixture = new GameObject("Mechanism snapshot fixture");
            try
            {
                var lever = Child(fixture.transform, "state:powerLever");
                var breaker = Child(fixture.transform, "state:breakerHandle");
                var valve = Child(fixture.transform, "interactive:pressure-valve");
                valve.localRotation = Quaternion.Euler(0, 90, 0);
                var pipe = Child(valve, "Wall-mounted pressure pipe");
                var wheel = Child(valve, "motion:pressureValveWheel");
                var rootRotation = valve.localRotation; var pipeRotation = pipe.rotation;
                var controller = DeadAirArtMotion.Attach(fixture.transform);
                var state = new RoomState { elapsed = 5, phase = "playing" };
                controller.Apply(state, 0);
                var leverOff = lever.localRotation; var breakerOff = breaker.localRotation;
                state.power = state.breakerOn = state.valveOpened = true;
                controller.Apply(state, 0);
                Require(Quaternion.Angle(leverOff, lever.localRotation) < .001f && Quaternion.Angle(breakerOff, breaker.localRotation) < .001f,
                    "A paused state change must wait before animating its lever.");
                controller.Apply(state, .1f);
                Require(Quaternion.Angle(leverOff, lever.localRotation) > .1f && Quaternion.Angle(breakerOff, breaker.localRotation) > .1f,
                    "Host power and breaker changes must animate their visible mechanisms.");
                controller.Apply(state, 1);
                Require(Mathf.Abs(Quaternion.Angle(Quaternion.identity, wheel.localRotation) - 180) < .01f,
                    "The pressure wheel must reach its mechanical stop.");
                Require(Quaternion.Angle(valve.localRotation, rootRotation) < .001f && Quaternion.Angle(pipe.rotation, pipeRotation) < .001f,
                    "Turning a pressure valve must not rotate its fixed pipe or housing.");
                state.elapsed = 0; state.power = state.breakerOn = state.valveOpened = false;
                controller.Apply(state, 0);
                Require(Quaternion.Angle(lever.localRotation, leverOff) < .001f && Quaternion.Angle(wheel.localRotation, Quaternion.identity) < .001f,
                    "Rewinding a snapshot must restore authored mechanism poses.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        static void ValidateDrawerCollision()
        {
            var fixture = new GameObject("Articulated drawer collider fixture");
            var material = new Material(Shader.Find("Standard"));
            try
            {
                var prop = new PropData { id = "art-validation", type = "workstation", w = 1.6f, d = .85f };
                DeadAirDrawerPresentation.BuildFurnitureDrawers(fixture.transform, prop, material, material, material, material);
                var data = DeadAirDrawers.ForProp(prop)[0];
                var drawer = fixture.transform.Find("drawer:" + data.id);
                var front = drawer.Find("Drawer front");
                var collider = front.GetComponent<BoxCollider>();
                Require(collider != null && collider.enabled, "A drawer front must carry its own collision surface.");
                var position = front.position;
                drawer.localPosition += Vector3.forward * .38f;
                Physics.SyncTransforms();
                Require(Vector3.Distance(front.position, position + Vector3.forward * .38f) < .001f && collider.bounds.Contains(front.position),
                    "Drawer collision must follow its moving front panel.");
                Require(drawer.Find("Moving drawer slide rail") != null && fixture.transform.Find("Fixed drawer slide rail") != null,
                    "Drawer slides must remain separate from their fixed cabinet rails.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); UnityEngine.Object.DestroyImmediate(material); }
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }

        static void ValidateDroppedPropRestingHeight()
        {
            var fixture = new GameObject("Dropped equipment grounding fixture");
            var materials = new System.Collections.Generic.HashSet<Material>();
            try
            {
                var flashlight = DeadAirArtMotionProps.Flashlight(fixture.transform);
                flashlight.transform.rotation = Quaternion.Euler(0, 137, 0);
                foreach (var renderer in flashlight.GetComponentsInChildren<Renderer>()) materials.Add(renderer.sharedMaterial);
                Require(flashlight.transform.localScale == Vector3.one && flashlight.transform.Find("Glass lens") != null &&
                    flashlight.transform.Find("Battery access cap") != null, "Dropped flashlight must preserve separate serviceable components and metric root scale.");
                float height = DeadAirArtMotionProps.RestingHeight(flashlight.transform);
                flashlight.transform.position = Vector3.up * height;
                float bottom = float.PositiveInfinity;
                foreach (var renderer in flashlight.GetComponentsInChildren<Renderer>()) bottom = Mathf.Min(bottom, renderer.bounds.min.y);
                Require(Mathf.Abs(bottom - .008f) < .001f, "Dropped equipment must rest on the shared floor rather than float or sink.");
                Require(flashlight.GetComponentsInChildren<Collider>().Length == 0, "Dropped visual equipment must not add a second gameplay collision authority.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
                foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
    }
}
