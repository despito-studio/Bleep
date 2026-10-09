using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>The owner's authored torso and legs, rendered in the world with normal depth.</summary>
    public sealed class DeadAirFirstPersonBody : MonoBehaviour
    {
        readonly PlayerState pose = new PlayerState();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<SkinnedMeshRenderer> ownerSurfaces = new List<SkinnedMeshRenderer>();
        MaterialPropertyBlock bodyView;
        public const float HiddenWeightThreshold = .02f;
        GameObject avatar;
        string role;
        DeadAirCharacterMotion motion;
        Light bodyBounce;
        float eyeClearance;
        Vector3 previousFeet;
        bool hasFeet;
        public GameObject Avatar => avatar;
        public DeadAirCharacterMotion Motion => motion;
        public int VisibleTriangles { get; private set; }
        public int HiddenTriangles { get; private set; }

        public void Present(PlayerState player, RoomState state, Transform feet, float yaw, float pitch, bool crouching, bool sprinting, Camera camera = null)
        {
            bool visible = player != null && state != null && (state.phase == "playing" || state.phase == "departing") &&
                !player.down && string.IsNullOrEmpty(player.hiding);
            if (!visible) { SetVisible(false); return; }
            if (avatar == null || role != player.characterClass) CreateAvatar(player.characterClass);
            if (avatar == null) return;
            if (bodyView == null) bodyView = new MaterialPropertyBlock();
            avatar.SetActive(true);
            // Sample at the collision feet, then fit the visual rig to its actual
            // source eye. Bone pivots and face depth differ between the models.
            avatar.transform.position = feet.position - feet.forward * .12f;
            float seated = DeadAirVanPresentation.SeatedWeight(state);
            if (player.ladderClimbing || seated > .001f) avatar.transform.rotation = feet.rotation;
            pose.characterClass = player.characterClass;
            pose.x = -feet.position.x; pose.y = feet.position.y; pose.z = feet.position.z;
            pose.yaw = yaw; pose.pitch = pitch; pose.crouching = crouching; pose.sprinting = sprinting;
            pose.down = false; pose.ladderClimbing = player.ladderClimbing;
            pose.ladderUp = player.ladderUp; pose.ladderStartedAt = player.ladderStartedAt;
            pose.clothingMask = -1;
            float distance = hasFeet ? Vector3.Distance(new Vector3(feet.position.x, 0, feet.position.z), new Vector3(previousFeet.x, 0, previousFeet.z)) : 0;
            float speed = distance < 3 && Time.deltaTime > 0 ? Mathf.Clamp(distance / Time.deltaTime, 0, 8) : 0;
            motion.SetLocomotionPose(crouching, speed);
            motion.SetTransitPose(seated);
            motion.Present(pose, state.elapsed, state.paused, true);
            bodyBounce.transform.rotation = avatar.transform.rotation * Quaternion.Euler(35, 180, 0);
            Quaternion look = camera != null ? camera.transform.rotation : DeadAirGaze.ViewRotation(yaw, pitch);
            Vector3 eye = camera != null ? camera.transform.position : feet.position + Vector3.up *
                (crouching ? DeadAirLocomotion.CrouchingEye : DeadAirLocomotion.StandingEye) + feet.forward * .08f;
            Vector3 eyeDelta = eye - motion.FirstPersonEyePosition - avatar.transform.forward * eyeClearance;
            eyeDelta.y = 0;
            avatar.transform.position += eyeDelta;
            bodyView.SetVector("_DeadAirOwnerBodyEye", new Vector4(eye.x, eye.y, eye.z, 1));
            bodyView.SetVector("_DeadAirOwnerBodyUp", look * Vector3.up);
            bodyView.SetVector("_DeadAirOwnerBodyForward", look * Vector3.forward);
            bodyView.SetVector("_DeadAirOwnerBodyClearance", new Vector4(.06f, .04f, 0, .12f));
            foreach (var surface in ownerSurfaces) surface.SetPropertyBlock(bodyView);
            previousFeet = feet.position; hasFeet = true;
        }

        void CreateAvatar(string selectedRole)
        {
            DisposeAvatar(); role = selectedRole;
            var prefab = Resources.Load<GameObject>("World/character_" + role);
            if (prefab == null) return;
            avatar = Instantiate(prefab, transform);
            avatar.name = "First person body / " + role;
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) Release(collider);
            motion = avatar.GetComponent<DeadAirCharacterMotion>() ?? avatar.AddComponent<DeadAirCharacterMotion>();
            motion.ClassId = role;
            motion.SetFirstPersonPresentation(true);
            DeadAirClothing.Apply(avatar, -1, role);
            foreach (var part in avatar.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 27;
            bodyBounce = new GameObject("Subtle first person body bounce").AddComponent<Light>();
            bodyBounce.transform.SetParent(avatar.transform, false);
            bodyBounce.type = LightType.Directional; bodyBounce.cullingMask = 1 << 27;
            bodyBounce.color = new Color32(192, 202, 215, 255); bodyBounce.intensity = .48f;
            bodyBounce.shadows = LightShadows.None; bodyBounce.renderMode = LightRenderMode.ForcePixel;
            Vector3 sourceEye = DeadAirBodyFit.SourceEye(role);
            avatar.transform.localScale *= DeadAirLocomotion.StandingEye / sourceEye.y;
            eyeClearance = Mathf.Max(.08f, (DeadAirBodyFit.TorsoFront(role) - sourceEye.z) * avatar.transform.lossyScale.z + .08f);
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
            }
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
        public void ResetPose(float yaw, float pitch) { hasFeet = false; if (motion != null) motion.ResetGaze(yaw, pitch); }
        void DisposeAvatar()
        {
            if (avatar != null) Release(avatar);
            foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh);
            ownedMeshes.Clear(); ownerSurfaces.Clear(); avatar = null; motion = null; hasFeet = false;
        }
        static void Release(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() { DisposeAvatar(); }
    }
}
