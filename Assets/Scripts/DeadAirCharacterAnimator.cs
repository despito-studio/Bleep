using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DeadAir
{
    /// <summary>Explicit Generic rig playback. Gameplay supplies phases; this component never advances gameplay.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator), typeof(DeadAirCharacterRigBindings), typeof(DeadAirCharacterWardrobe))]
    public sealed class DeadAirCharacterAnimator : MonoBehaviour
    {
        [Serializable]
        public sealed class ClipBinding
        {
            public string action;
            public AnimationClip clip;
            public string rigVersion;
            public bool loop;
        }

        public static readonly string[] CoreActions = {
            "Idle", "Walk", "Run", "WalkBack", "StrafeLeft", "StrafeRight", "CrouchIdle", "CrouchWalk", "UseItem"
        };
        [SerializeField] ClipBinding[] clips = Array.Empty<ClipBinding>();
        readonly Dictionary<string, int> actionIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationMixerPlayable upperMixer;
        AnimationLayerMixerPlayable layers;
        AnimationClipPlayable[] players;
        AnimationClipPlayable[] upperPlayers;
        AvatarMask upperMask;
        Animator animator;
        DeadAirCharacterRigBindings rig;
        DeadAirCharacterWardrobe wardrobe;
        string reportedError;
        public string LastError { get; private set; }
        public string CurrentAction { get; private set; }
        public float CurrentPhase { get; private set; }

        public void Configure(ClipBinding[] authoredClips)
        {
            ReleaseGraph();
            clips = authoredClips == null ? Array.Empty<ClipBinding>() : new ClipBinding[authoredClips.Length];
            for (int i = 0; i < clips.Length; i++)
                if (authoredClips[i] != null)
                    clips[i] = new ClipBinding { action = authoredClips[i].action, clip = authoredClips[i].clip,
                        rigVersion = authoredClips[i].rigVersion, loop = authoredClips[i].loop };
            LastError = reportedError = null;
        }

        public bool ValidateConfiguration(out string error)
        {
            animator = GetComponent<Animator>(); rig = GetComponent<DeadAirCharacterRigBindings>();
            wardrobe = GetComponent<DeadAirCharacterWardrobe>();
            error = null;
            if (animator == null || rig == null || wardrobe == null)
            { error = "The V2 animator, explicit rig and wardrobe must share one character root."; return false; }
            if (!rig.ValidateBindings(out error) || !wardrobe.ValidateBindings(out error)) return false;
            if (wardrobe.CharacterClass != rig.CharacterClass)
            { error = "The rig and wardrobe refer to different characters."; return false; }
            if (animator.avatar == null || !animator.avatar.isValid || animator.avatar.isHuman || animator.runtimeAnimatorController != null)
            { error = "Assign this rig's valid Generic Avatar and leave the controller empty; the V2 graph owns playback."; return false; }
            var legacyDriver = GetComponentInChildren<DeadAirCharacterMotion>(true);
            var parentDriver = transform.parent == null ? null : transform.parent.GetComponentInParent<DeadAirCharacterMotion>();
            if (GetComponentInChildren<Animation>(true) != null ||
                (legacyDriver != null && (legacyDriver.gameObject != gameObject || !legacyDriver.UsesV2)) || parentDriver != null)
            { error = "A legacy animation driver would write this rig alongside V2 playback."; return false; }
            var actions = new HashSet<string>(StringComparer.Ordinal);
            if (clips == null) { error = "No authored clip set is configured."; return false; }
            foreach (var binding in clips)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.action) || !actions.Add(binding.action) ||
                    binding.clip == null || binding.clip.legacy || binding.clip.humanMotion || binding.clip.length <= 0 ||
                    binding.rigVersion != rig.RigVersion ||
                    !binding.clip.name.StartsWith("AN_" + rig.CharacterId + "_" + binding.action + "_", StringComparison.Ordinal))
                { error = "Every action needs a unique, nonlegacy clip authored for this character and rig version."; return false; }
                if (binding.clip.isLooping != binding.loop)
                { error = "Loop metadata differs from the imported clip: " + binding.clip.name; return false; }
            }
            foreach (string action in CoreActions)
                if (!actions.Contains(action)) { error = "The authored core set is missing " + action + "."; return false; }
            return true;
        }

        /// <summary>Phase is authoritative normalized clip time. Repeat calls at the same phase are idempotent.</summary>
        public bool Present(string action, float phase, int clothingMask, string role)
            => PresentBlend(action, phase, null, 0, 0, clothingMask, role);

        public bool TryGetClip(string action, out AnimationClip clip)
        {
            clip = null;
            foreach (var binding in clips)
                if (binding != null && binding.action == action) { clip = binding.clip; return clip != null; }
            return false;
        }

        public bool PresentWithUpperBody(string action, float phase, string upperAction, float upperPhase, float upperWeight,
            int clothingMask, string role)
        {
            if (!PresentBlend(action, phase, null, 0, 0, clothingMask, role)) return false;
            if (string.IsNullOrEmpty(upperAction)) return true;
            if (!Finite(upperPhase) || !Finite(upperWeight) || !actionIndices.TryGetValue(upperAction, out int index))
                return Fail("Missing/invalid authored upper-body action: " + upperAction);
            double seconds = (clips[index].loop ? Mathf.Repeat(upperPhase, 1) : Mathf.Clamp01(upperPhase)) * clips[index].clip.length;
            upperPlayers[index].SetTime(seconds);
            upperMixer.SetInputWeight(index, 1);
            layers.SetInputWeight(1, Mathf.Clamp01(upperWeight));
            graph.Evaluate(0);
            return true;
        }

        /// <summary>Optional two-action blend; contact and corrective passes run after this method returns.</summary>
        public bool PresentBlend(string action, float phase, string otherAction, float otherPhase, float blend,
            int clothingMask, string role)
        {
            if (!isActiveAndEnabled) return false;
            if (!Finite(phase) || !Finite(otherPhase) || !Finite(blend)) return Fail("Animation phases and blend must be finite.");
            if (!graph.IsValid() && !CreateGraph()) return false;
            if (role != rig.CharacterClass) return Fail("The presentation role differs from the bound V2 character.");
            if (!actionIndices.TryGetValue(action ?? "", out int index)) return Fail("Missing authored action: " + action);
            int other = -1;
            if (!string.IsNullOrEmpty(otherAction) && !actionIndices.TryGetValue(otherAction, out other))
                return Fail("Missing authored blend action: " + otherAction);
            if (other == index) return Fail("Blend inputs must identify different authored actions.");
            if (!wardrobe.Apply(clothingMask, role)) return Fail(wardrobe.LastError);
            animator.applyRootMotion = false;
            layers.SetInputWeight(1, 0);
            for (int i = 0; i < upperPlayers.Length; i++) upperMixer.SetInputWeight(i, 0);
            float weight = other < 0 ? 0 : Mathf.Clamp01(blend);
            for (int i = 0; i < players.Length; i++) mixer.SetInputWeight(i, 0);
            Sample(index, phase); mixer.SetInputWeight(index, 1 - weight);
            if (other >= 0) { Sample(other, otherPhase); mixer.SetInputWeight(other, weight); }
            // Manual zero-delta evaluation prevents snapshot replay, pause or multiple views from advancing time twice.
            graph.Evaluate(0);
            CurrentAction = action; CurrentPhase = clips[index].loop ? Mathf.Repeat(phase, 1) : Mathf.Clamp01(phase);
            LastError = reportedError = null;
            return true;
        }

        void Sample(int index, float phase)
        {
            float normalized = clips[index].loop ? Mathf.Repeat(phase, 1) : Mathf.Clamp01(phase);
            double seconds = normalized * clips[index].clip.length;
            players[index].SetTime(seconds);
        }

        bool CreateGraph()
        {
            if (!ValidateConfiguration(out string error)) return Fail(error);
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            // Authority can sample inactive-camera actors and contact probes without renderer-dependent timing.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("V2 " + rig.CharacterId);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            upperMixer = AnimationMixerPlayable.Create(graph, clips.Length);
            layers = AnimationLayerMixerPlayable.Create(graph, 2);
            players = new AnimationClipPlayable[clips.Length]; actionIndices.Clear();
            upperPlayers = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                actionIndices.Add(clips[i].action, i);
                players[i] = AnimationClipPlayable.Create(graph, clips[i].clip);
                players[i].SetApplyFootIK(false); players[i].SetApplyPlayableIK(false); players[i].SetSpeed(0);
                graph.Connect(players[i], 0, mixer, i);
                upperPlayers[i] = AnimationClipPlayable.Create(graph, clips[i].clip);
                upperPlayers[i].SetApplyFootIK(false); upperPlayers[i].SetApplyPlayableIK(false); upperPlayers[i].SetSpeed(0);
                graph.Connect(upperPlayers[i], 0, upperMixer, i);
            }
            graph.Connect(mixer, 0, layers, 0); layers.SetInputWeight(0, 1);
            graph.Connect(upperMixer, 0, layers, 1); layers.SetInputWeight(1, 0);
            rig.TryGet(DeadAirCharacterRigBindings.Joint.Chest, out Transform chest);
            var upperNodes = chest.GetComponentsInChildren<Transform>(true);
            upperMask = new AvatarMask { name = rig.CharacterId + " explicit upper body", transformCount = upperNodes.Length };
            for (int i = 0; i < upperNodes.Length; i++)
            {
                var segments = new List<string>();
                for (Transform node = upperNodes[i]; node != transform; node = node.parent) segments.Insert(0, node.name);
                upperMask.SetTransformPath(i, string.Join("/", segments)); upperMask.SetTransformActive(i, true);
            }
            layers.SetLayerMaskFromAvatarMask(1, upperMask);
            var output = AnimationPlayableOutput.Create(graph, "Authored character pose", animator);
            output.SetSourcePlayable(layers); graph.Play();
            return true;
        }

        public void ResetPresentation()
        {
            ReleaseGraph();
            GetComponent<DeadAirSecondaryMotion>()?.ResetMotion();
            CurrentAction = null; CurrentPhase = 0; LastError = reportedError = null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        bool Fail(string error)
        {
            LastError = error;
            if (reportedError != error) { Debug.LogWarning("V2 character animation on '" + name + "': " + error, this); reportedError = error; }
            return false;
        }
        void ReleaseGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            if (upperMask != null) { if (Application.isPlaying) Destroy(upperMask); else DestroyImmediate(upperMask); }
            upperMask = null; players = upperPlayers = null; actionIndices.Clear();
        }
        void OnDisable() { ResetPresentation(); }
        void OnDestroy() { ReleaseGraph(); }
        void OnValidate() { ReleaseGraph(); }
    }
}
