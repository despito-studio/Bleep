using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>Semantic references to one authored rig. No bone-name guessing or shared anatomy.</summary>
    [DisallowMultipleComponent]
    public sealed class DeadAirCharacterRigBindings : MonoBehaviour
    {
        public enum Joint
        {
            Root, Pelvis, Chest, Head, EyeReference,
            UpperArmLeft, ForearmLeft, HandLeft, UpperArmRight, ForearmRight, HandRight,
            ThighLeft, ShinLeft, FootLeft, ToeLeft, ThighRight, ShinRight, FootRight, ToeRight,
            GripLeft, GripRight, SoleHeelLeft, SoleToeLeft, SoleHeelRight, SoleToeRight, ItemStow,
            MouthContact, RecorderButton, CompassRead, WrenchGrip
        }

        [Serializable]
        public sealed class Binding
        {
            public Joint semantic;
            public Transform target;
        }

        [SerializeField] string characterId;
        [SerializeField] string characterClass;
        [SerializeField] string rigVersion;
        [SerializeField] Binding[] bindings = Array.Empty<Binding>();
        readonly Dictionary<Joint, Transform> resolved = new Dictionary<Joint, Transform>();
        bool validated;
        public string CharacterId => characterId;
        public string CharacterClass => characterClass;
        public string RigVersion => rigVersion;

        public static string RoleFor(string id)
        {
            switch (id)
            {
                case "vivian": return "operator";
                case "victor": return "engineer";
                case "edgar": return "journalist";
                case "hazel": return "dispatcher";
                default: return null;
            }
        }

        public void Configure(string id, string role, string version, Binding[] explicitBindings)
        {
            characterId = id; characterClass = role; rigVersion = version;
            bindings = explicitBindings == null ? Array.Empty<Binding>() : new Binding[explicitBindings.Length];
            for (int i = 0; i < bindings.Length; i++)
                if (explicitBindings[i] != null)
                    bindings[i] = new Binding { semantic = explicitBindings[i].semantic, target = explicitBindings[i].target };
            Invalidate();
        }

        void OnValidate() { Invalidate(); }
        void Invalidate() { validated = false; resolved.Clear(); }

        public bool TryGet(Joint semantic, out Transform target)
        {
            target = null;
            if (!validated && !ValidateBindings(out _)) return false;
            return resolved.TryGetValue(semantic, out target) && target != null;
        }

        public bool ValidateBindings(out string error)
        {
            error = null; Invalidate();
            if (RoleFor(characterId) == null || RoleFor(characterId) != characterClass || string.IsNullOrWhiteSpace(rigVersion))
            { error = "Assign a matching character ID, gameplay role and explicit rig version."; return false; }
            if (bindings == null) { error = "The rig binding list is missing."; return false; }
            var nodes = new HashSet<Transform>();
            foreach (var binding in bindings)
            {
                if (binding == null || !Enum.IsDefined(typeof(Joint), binding.semantic) || binding.target == null ||
                    binding.target == transform || !binding.target.IsChildOf(transform) ||
                    resolved.ContainsKey(binding.semantic) || !nodes.Add(binding.target))
                { error = "Rig semantics must refer to unique, nonempty transforms inside this character."; return false; }
                resolved.Add(binding.semantic, binding.target);
                for (Transform current = binding.target; current != null; current = current.parent)
                {
                    if ((current.localScale - Vector3.one).sqrMagnitude > 1e-8f)
                    { error = "Rig transforms must use unit scale: " + current.name; return false; }
                    if (current == transform) break;
                }
            }
            for (int i = 0; i <= (int)Joint.ItemStow; i++)
                if (!resolved.ContainsKey((Joint)i))
                { error = "Missing rig semantic: " + (Joint)i; return false; }
            Joint personal = characterId == "vivian" ? Joint.MouthContact : characterId == "victor" ? Joint.WrenchGrip :
                characterId == "edgar" ? Joint.RecorderButton : Joint.CompassRead;
            if (!resolved.ContainsKey(personal)) { error = "Missing personal contact: " + personal; return false; }
            foreach (var pair in resolved)
                if (pair.Key != Joint.Root && !pair.Value.IsChildOf(resolved[Joint.Root]))
                { error = pair.Key + " is outside this character's export root."; return false; }
            Joint[,] chains = {
                { Joint.Root, Joint.Pelvis }, { Joint.Pelvis, Joint.Chest }, { Joint.Chest, Joint.Head },
                { Joint.Head, Joint.EyeReference }, { Joint.Chest, Joint.UpperArmLeft },
                { Joint.UpperArmLeft, Joint.ForearmLeft }, { Joint.ForearmLeft, Joint.HandLeft },
                { Joint.Chest, Joint.UpperArmRight }, { Joint.UpperArmRight, Joint.ForearmRight },
                { Joint.ForearmRight, Joint.HandRight }, { Joint.Pelvis, Joint.ThighLeft },
                { Joint.ThighLeft, Joint.ShinLeft }, { Joint.ShinLeft, Joint.FootLeft }, { Joint.FootLeft, Joint.ToeLeft },
                { Joint.Pelvis, Joint.ThighRight }, { Joint.ThighRight, Joint.ShinRight },
                { Joint.ShinRight, Joint.FootRight }, { Joint.FootRight, Joint.ToeRight },
                { Joint.HandLeft, Joint.GripLeft }, { Joint.HandRight, Joint.GripRight },
                { Joint.FootLeft, Joint.SoleHeelLeft }, { Joint.FootLeft, Joint.SoleToeLeft },
                { Joint.FootRight, Joint.SoleHeelRight }, { Joint.FootRight, Joint.SoleToeRight }
            };
            for (int i = 0; i < chains.GetLength(0); i++)
                if (!resolved[chains[i, 1]].IsChildOf(resolved[chains[i, 0]]))
                { error = chains[i, 1] + " must descend from " + chains[i, 0] + "."; return false; }
            if (characterId == "vivian" && !resolved[Joint.MouthContact].IsChildOf(resolved[Joint.Head]))
            { error = "Vivian's mouth contact must follow her head."; return false; }
            validated = true;
            return true;
        }
    }
}
