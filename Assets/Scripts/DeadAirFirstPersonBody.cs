using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>The owner's authored torso and legs, rendered in the world with normal depth.</summary>
    public sealed class DeadAirFirstPersonBody : MonoBehaviour
    {
        /// <summary>Current owner controls for visuals only; never written back into the authoritative snapshot.</summary>
        public readonly struct OwnerEquipmentState
        {
            public readonly bool mapOpen, cameraUp, flashlightOn;
            public OwnerEquipmentState(bool mapOpen, bool cameraUp, bool flashlightOn)
            { this.mapOpen = mapOpen; this.cameraUp = cameraUp; this.flashlightOn = flashlightOn; }
        }

        [SerializeField] GameObject[] v2PreviewPrefabs = System.Array.Empty<GameObject>();
        readonly PlayerState pose = new PlayerState();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<SkinnedMeshRenderer> ownerSurfaces = new List<SkinnedMeshRenderer>();
        readonly List<SkinnedMeshRenderer> ownerShadows = new List<SkinnedMeshRenderer>();
        MaterialPropertyBlock bodyView;
        public const float HiddenWeightThreshold = .5f;
        GameObject avatar;
        string role;
        DeadAirCharacterMotion motion;
        Light bodyBounce;
        float eyeClearance;
        float authoredBodyOffset;
        bool authoredCast;
        bool v2Avatar;
        bool v2ProfileValid;
        Vector3 previousFeet;
        bool hasFeet;
        public GameObject Avatar => avatar;
        public DeadAirCharacterMotion Motion => motion;
        public bool UsesV2 => v2Avatar;
        public int VisibleTriangles { get; private set; }
        public int HiddenTriangles { get; private set; }

        /// <summary>Explicit pilot/scene binding; never rewrites Resources or affects other character roles.</summary>
        public void ConfigureV2Prefabs(GameObject[] prefabs)
        {
            v2PreviewPrefabs = prefabs == null ? System.Array.Empty<GameObject>() : (GameObject[])prefabs.Clone();
            DisposeAvatar();
        }

        public void Present(PlayerState player, RoomState state, Transform feet, float yaw, float pitch, bool crouching, bool sprinting,
            Camera camera = null, OwnerEquipmentState? ownerEquipment = null)
        {
            bool visible = player != null && state != null && (state.phase == "playing" || state.phase == "departing") &&
                !player.down && string.IsNullOrEmpty(player.hiding);
            if (!visible) { SetVisible(false); return; }
            if (avatar == null || role != player.characterClass) CreateAvatar(player.characterClass, player.clothingMask);
            if (avatar == null) return;
            if (v2Avatar && !v2ProfileValid) return;
            if (bodyView == null) bodyView = new MaterialPropertyBlock();
            avatar.SetActive(true);
            // Sample at the collision feet, then fit the visual rig to its actual
            // source eye. Bone pivots and face depth differ between the models.
            avatar.transform.position = v2Avatar ? feet.position : feet.position - feet.forward * (authoredCast ? authoredBodyOffset : .12f);
            float seated = DeadAirVanPresentation.SeatedWeight(state);
            if (player.ladderClimbing || seated > .001f) avatar.transform.rotation = feet.rotation;
            pose.characterClass = player.characterClass;
            pose.x = -feet.position.x; pose.y = feet.position.y; pose.z = feet.position.z;
            pose.yaw = yaw; pose.pitch = pitch; pose.crouching = crouching; pose.sprinting = sprinting;
            pose.down = false; pose.ladderClimbing = player.ladderClimbing;
            pose.ladderUp = player.ladderUp; pose.ladderStartedAt = player.ladderStartedAt;
            pose.clothingMask = player.clothingMask;
            if (v2Avatar)
            {
                pose.uniqueActionSequence = player.uniqueActionSequence; pose.uniqueActionAt = player.uniqueActionAt;
                pose.uniqueActionCancelled = player.uniqueActionCancelled; pose.uniqueItemEquipped = player.uniqueItemEquipped;
                pose.mapOpen = ownerEquipment?.mapOpen ?? player.mapOpen;
                pose.camera = ownerEquipment?.cameraUp ?? player.camera;
                pose.flashlight = ownerEquipment?.flashlightOn ?? player.flashlight;
                pose.flashlightCharge = player.flashlightCharge; pose.inventory = player.inventory; pose.selectedSlot = player.selectedSlot;
                pose.cigarettesRemaining = player.cigarettesRemaining; pose.hiding = player.hiding;
            }
            Vector3 displacement = hasFeet ? feet.position - previousFeet : Vector3.zero;
            displacement.y = 0;
            float distance = displacement.magnitude;
            float speed = distance < 3 && Time.deltaTime > 0 ? Mathf.Clamp(distance / Time.deltaTime, 0, 8) : 0;
            Vector3 velocity = distance > .0001f ? displacement.normalized * speed : Vector3.zero;
            motion.SetLocomotionPose(crouching, speed, velocity);
            motion.SetTransitPose(seated);
            motion.Present(pose, state.elapsed, state.paused, true);
            if (v2Avatar)
            {
                // Preserve authored scale, head, arms and every triangle. Only the owner camera's fragments are clipped.
                GetComponent<DeadAirFirstPersonRig>()?.SetVisible(false);
                avatar.GetComponent<DeadAirCharacterV2Presentation>()?.SetOwnerCamera(camera);
                if (bodyBounce != null) bodyBounce.transform.rotation = avatar.transform.rotation * Quaternion.Euler(35, 180, 0);
                previousFeet = feet.position; hasFeet = true;
                return;
            }
            SynchronizeWardrobeShadows();
            bodyBounce.transform.rotation = avatar.transform.rotation * Quaternion.Euler(35, 180, 0);
            Quaternion look = camera != null ? camera.transform.rotation : DeadAirGaze.ViewRotation(yaw, pitch);
            Vector3 eye = camera != null ? camera.transform.position : feet.position + Vector3.up *
                (crouching ? DeadAirLocomotion.CrouchingEye : DeadAirLocomotion.StandingEye) + feet.forward * .08f;
            // Bending brings the face in front of the chest, so crouch only
            // needs the small lens clearance rather than the upright coat fit.
            Vector3 eyeDelta = eye - motion.FirstPersonEyePosition - avatar.transform.forward * (crouching ? .08f : eyeClearance);
            eyeDelta.y = 0;
            // Authored rigs keep a stable root before foot IK. Moving the root
            // after contact solving drags both planted feet whenever the head bobs.
            if (!authoredCast) avatar.transform.position += eyeDelta;
            bodyView.SetVector("_DeadAirOwnerBodyEye", new Vector4(eye.x, eye.y, eye.z, 1));
            bodyView.SetVector("_DeadAirOwnerBodyUp", look * Vector3.up);
            bodyView.SetVector("_DeadAirOwnerBodyForward", look * Vector3.forward);
            bodyView.SetVector("_DeadAirOwnerBodyClearance", new Vector4(.06f, .04f, 0, .12f));
            foreach (var surface in ownerSurfaces) surface.SetPropertyBlock(bodyView);
            previousFeet = feet.position; hasFeet = true;
        }

        void CreateAvatar(string selectedRole, int clothingMask)
        {
            DisposeAvatar(); role = selectedRole;
            GameObject prefab = null;
            foreach (var candidate in v2PreviewPrefabs)
            {
                var candidateRig = candidate == null ? null : candidate.GetComponent<DeadAirCharacterRigBindings>();
                if (candidateRig == null || candidateRig.CharacterClass != role) continue;
                if (prefab != null) { Debug.LogError("Multiple explicit V2 owner prefabs for " + role, this); return; }
                prefab = candidate;
            }
            if (prefab == null) prefab = Resources.Load<GameObject>("World/character_" + role);
            if (prefab == null) return;
            avatar = Instantiate(prefab, transform);
            avatar.name = "First person body / " + role;
            v2Avatar = avatar.GetComponent<DeadAirCharacterRigBindings>() != null;
            // Eye-based fitting must use standing landmarks even if an older
            // prefab was serialized while another animation take was sampled.
            var sourceAnimation = avatar.GetComponentInChildren<Animation>(true);
            var standingClip = sourceAnimation != null ? sourceAnimation.GetClip("Idle") : null;
            if (!v2Avatar && standingClip != null) standingClip.SampleAnimation(sourceAnimation.gameObject, 0);
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) Release(collider);
            motion = avatar.GetComponent<DeadAirCharacterMotion>() ?? avatar.AddComponent<DeadAirCharacterMotion>();
            motion.ClassId = role;
            motion.SetFirstPersonPresentation(true);
            DeadAirClothing.Apply(avatar, clothingMask, role);
            foreach (var part in avatar.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 27;
            bodyBounce = new GameObject("Subtle first person body bounce").AddComponent<Light>();
            bodyBounce.transform.SetParent(avatar.transform, false);
            bodyBounce.type = LightType.Directional; bodyBounce.cullingMask = 1 << 27;
            bodyBounce.color = new Color32(192, 202, 215, 255); bodyBounce.intensity = .48f;
            bodyBounce.shadows = LightShadows.None; bodyBounce.renderMode = LightRenderMode.ForcePixel;
            if (v2Avatar)
            {
                var presentation = avatar.GetComponent<DeadAirCharacterV2Presentation>();
                if (presentation == null || !presentation.ValidateProfile(out _))
                {
                    Debug.LogError("The V2 owner body needs a validated, explicit presentation profile; no legacy fitting was applied.", this);
                    avatar.SetActive(false); return;
                }
                v2ProfileValid = true;
                VisibleTriangles = HiddenTriangles = 0;
                foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh != null)
                        for (int i = 0; i < renderer.sharedMesh.subMeshCount; i++)
                            VisibleTriangles += (int)renderer.sharedMesh.GetIndexCount(i) / 3;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.updateWhenOffscreen = true;
                }
                return;
            }
            authoredCast = DeadAirBodyFit.FindLandmark(avatar.transform, "EyeSocket") != null;
            Vector3 sourceEye = DeadAirBodyFit.SourceEye(role, avatar.transform);
            avatar.transform.localScale *= DeadAirLocomotion.StandingEye / sourceEye.y;
            // Keep the owner's removed neckline outside the downward viewport.
            // Retain the complete authored chest instead of drawing a cap over it.
            eyeClearance = Mathf.Max(authoredCast ? .12f : .30f,
                (DeadAirBodyFit.TorsoFront(role, avatar.transform) - sourceEye.z) * avatar.transform.lossyScale.z + .08f);
            authoredBodyOffset = Mathf.Clamp(sourceEye.z * avatar.transform.lossyScale.z + eyeClearance - .08f, .02f, .26f);
            VisibleTriangles = HiddenTriangles = 0;
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = renderer.sharedMesh;
                if (source == null || !source.isReadable || !renderer.gameObject.activeInHierarchy) continue;
                var weights = source.boneWeights;
                if (weights.Length != source.vertexCount) continue;
                var hiddenBones = new bool[renderer.bones.Length];
                for (int b = 0; b < hiddenBones.Length; b++) hiddenBones[b] = HiddenBone(renderer.bones[b], avatar.transform);
                var hiddenVertices = new bool[weights.Length];
                for (int v = 0; v < weights.Length; v++)
                {
                    var w = weights[v];
                    hiddenVertices[v] = HiddenWeight(w.boneIndex0, w.weight0, hiddenBones) + HiddenWeight(w.boneIndex1, w.weight1, hiddenBones) +
                        HiddenWeight(w.boneIndex2, w.weight2, hiddenBones) + HiddenWeight(w.boneIndex3, w.weight3, hiddenBones) > HiddenWeightThreshold;
                }
                // A separate shadow surface retains the complete silhouette. It
                // never draws the owner's head or a second pair of arms on screen.
                var shadow = new GameObject("Full operative shadow").AddComponent<SkinnedMeshRenderer>();
                shadow.transform.SetParent(renderer.transform, false);
                shadow.gameObject.layer = 27;
                shadow.sharedMesh = source; shadow.sharedMaterials = renderer.sharedMaterials;
                shadow.bones = renderer.bones; shadow.rootBone = renderer.rootBone; shadow.localBounds = renderer.localBounds;
                shadow.shadowCastingMode = ShadowCastingMode.ShadowsOnly; shadow.receiveShadows = false;
                shadow.enabled = renderer.enabled;
                var clipped = Instantiate(source); clipped.name = source.name + " / owner torso and legs";
                clipped.hideFlags = HideFlags.DontSave; ownedMeshes.Add(clipped);
                for (int s = 0; s < source.subMeshCount; s++)
                {
                    var triangles = source.GetTriangles(s); var kept = new List<int>(triangles.Length);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        if (hiddenVertices[a] || hiddenVertices[b] || hiddenVertices[c]) { HiddenTriangles++; continue; }
                        kept.Add(a); kept.Add(b); kept.Add(c); VisibleTriangles++;
                    }
                    clipped.SetTriangles(kept, s, false);
                }
                renderer.sharedMesh = clipped; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.updateWhenOffscreen = true;
                ownerSurfaces.Add(renderer);
                ownerShadows.Add(shadow);
            }
        }

        void SynchronizeWardrobeShadows()
        {
            for (int i = 0; i < ownerSurfaces.Count; i++)
                if (ownerSurfaces[i] != null && ownerShadows[i] != null)
                    ownerShadows[i].enabled = ownerSurfaces[i].enabled;
        }

        static float HiddenWeight(int index, float weight, bool[] hidden) => index >= 0 && index < hidden.Length && hidden[index] ? weight : 0;
        static bool HiddenBone(Transform bone, Transform root)
        {
            for (var parent = bone; parent != null && parent != root; parent = parent.parent)
                if (parent.name == "head" || parent.name == "neck" || parent.name.StartsWith("upper_arm.") ||
                    parent.name.StartsWith("forearm.") || parent.name.StartsWith("hand.")) return true;
            return false;
        }
        public void SetVisible(bool visible) { if (avatar != null) avatar.SetActive(visible); }
        public void ResetPose(float yaw, float pitch)
        {
            hasFeet = false;
            if (motion == null) return;
            motion.ResetGaze(yaw, pitch);
            motion.ResetLocomotionContacts();
        }
        void DisposeAvatar()
        {
            if (avatar != null) Release(avatar);
            foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh);
            ownedMeshes.Clear(); ownerSurfaces.Clear(); ownerShadows.Clear(); avatar = null; motion = null; hasFeet = false;
            v2Avatar = v2ProfileValid = false; bodyBounce = null;
        }
        static void Release(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() { DisposeAvatar(); }
    }
}
