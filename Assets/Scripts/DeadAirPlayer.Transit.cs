using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirPlayer
    {
        public bool Crouching { get; private set; }
        public bool InTransit => wasInTransit;
        public float EyeHeight => Mathf.Lerp(DeadAirLocomotion.CrouchingEye, DeadAirLocomotion.StandingEye,
            Mathf.InverseLerp(DeadAirLocomotion.CrouchingHeight, DeadAirLocomotion.StandingHeight, controller.height));
        bool crouchToggle, wasInTransit;
        readonly Collider[] standingHits = new Collider[32];

        void ResetLocomotion()
        {
            crouchToggle = Crouching = false;
            controller.height = DeadAirLocomotion.StandingHeight;
            controller.center = Vector3.up * controller.height * .5f;
        }

        void UpdateCrouch()
        {
            if (!verificationMovement && Input.GetKeyDown(KeyCode.C)) crouchToggle = !crouchToggle;
            bool requested = crouchToggle || (!verificationMovement &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)));
            if (!requested && Crouching && !CanStand()) requested = true;
            Crouching = requested;
            float height = Mathf.MoveTowards(controller.height,
                requested ? DeadAirLocomotion.CrouchingHeight : DeadAirLocomotion.StandingHeight, Time.deltaTime * 4);
            controller.height = height;
            controller.center = Vector3.up * height * .5f;
            controller.stepOffset = Mathf.Min(.28f, height * .25f);
        }

        public bool CanStand()
        {
            float radius = controller.radius - .015f;
            Vector3 bottom = transform.position + Vector3.up * (radius + .035f);
            Vector3 top = transform.position + Vector3.up * (DeadAirLocomotion.StandingHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, standingHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = standingHits[i];
                if (hit == controller || hit.GetComponentInParent<DeadAirPlayer>() == this ||
                    hit.GetComponentInParent<DeadAirCharacterMotion>() != null) continue;
                return false;
            }
            return count < standingHits.Length;
        }

        public void SetVerificationCrouching(bool value)
        {
            if (!verificationAllowed) throw new System.InvalidOperationException("Crouch verification requires an explicit capture argument.");
            crouchToggle = value;
            UpdateCrouch();
        }

        bool PresentVanTransit(PlayerState me, RoomState state)
        {
            bool departing = state.phase == "departing";
            bool arriving = state.phase == "playing" && state.elapsed < DeadAirVanPresentation.ArrivalDuration;
            if (!arriving && !departing)
            {
                if (wasInTransit)
                {
                    wasInTransit = false;
                    transform.position = new Vector3(-me.x, me.y, me.z);
                    controller.enabled = !Noclip;
                    verticalVelocity = 0;
                    sentMovement.Clear(); acknowledgedMovement.Clear(); nextSend = 0;
                    firstPerson.ResetPresentation();
                }
                return false;
            }
            if (!wasInTransit) firstPerson.Present(me, false, false, false, 0, true, state.elapsed);
            wasInTransit = true;
            controller.enabled = false;
            Sprinting = Crouching = false;
            InteractionId = InteractionLabel = null;
            cameraUp = false; flashlight.enabled = false;
            float age = departing ? state.elapsed - state.departureStartedAt : state.elapsed;
            float duration = departing ? DeadAirVanPresentation.DepartureDuration : DeadAirVanPresentation.ArrivalDuration;
            float progress = Mathf.Clamp01(age / duration);
            transform.position = new Vector3(-me.x, me.y, me.z) + DeadAirVanPresentation.TransitOffset(state);
            // The seated arrival settles into standing; departure sits the crew back down.
            float standing = 1 - DeadAirVanPresentation.SeatedWeight(state);
            float movement = Mathf.Sin(progress * Mathf.PI);
            view.transform.localPosition = new Vector3(Mathf.Sin(age * 4.5f) * .012f * movement,
                Mathf.Lerp(1.12f, DeadAirLocomotion.StandingEye, standing) + Mathf.Sin(age * 9) * .006f * movement, 0);
            // Looking around the cabin stays available while the van is moving.
            if (!ui.HasModal && !state.paused && !DeadAirConsole.IsOpen) ReadLook();
            transform.rotation = Quaternion.Euler(0, 180 - yaw * Mathf.Rad2Deg, 0);
            view.transform.localRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0, Mathf.Sin(age * 3) * .4f * movement);
            view.fieldOfView = 68;
            firstPerson.SetVisible(true);
            firstPerson.PresentTransit(progress, departing);
            Cursor.lockState = ui.HasModal || DeadAirConsole.IsOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            return true;
        }
    }
}
