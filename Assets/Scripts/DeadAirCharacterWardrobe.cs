using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>Explicit, per-character garment visibility. Never edits body geometry or rig objects.</summary>
    [DisallowMultipleComponent]
    public sealed class DeadAirCharacterWardrobe : MonoBehaviour
    {
        public enum MaskMatch { All, Any }

        [Serializable]
        public sealed class GarmentBinding
        {
            public string id;
            [Tooltip("Bits from DeadAirClothing. A one-piece dress may require Shirt | Trousers.")]
            public int requiredMask;
            public MaskMatch match;
            public Renderer[] renderers = Array.Empty<Renderer>();
            [Tooltip("Groups are searched for renderers once; their GameObjects and bones are never deactivated.")]
            public GameObject[] groups = Array.Empty<GameObject>();
        }

        [SerializeField] string characterClass = "operator";
        [SerializeField] Renderer[] fullBodyRenderers = Array.Empty<Renderer>();
        [SerializeField] GarmentBinding[] garments = Array.Empty<GarmentBinding>();
        readonly List<Renderer[]> resolved = new List<Renderer[]>();
        bool checkedBindings, validBindings;
        string reportedError;
        public string CharacterClass => characterClass;
        public int CurrentMask { get; private set; } = -2;
        public string LastError { get; private set; }

        /// <summary>Protects body/clothing surfaces from being reused as hideable equipment in V2 profiles.</summary>
        public bool OwnsSurface(Renderer surface)
        {
            if (surface == null) return false;
            if (fullBodyRenderers != null && Array.IndexOf(fullBodyRenderers, surface) >= 0) return true;
            var collected = new List<Renderer[]>();
            if (!CollectBindings(collected, out _)) return true; // Invalid catalogs must not authorize equipment changes.
            foreach (var group in collected) if (Array.IndexOf(group, surface) >= 0) return true;
            return false;
        }

        /// <summary>Authoring/import setup; all references must belong to this character instance.</summary>
        public void ConfigureBindings(string role, Renderer[] completeBody, GarmentBinding[] garmentBindings)
        {
            characterClass = role;
            fullBodyRenderers = completeBody == null ? Array.Empty<Renderer>() : (Renderer[])completeBody.Clone();
            garments = garmentBindings == null ? Array.Empty<GarmentBinding>() : (GarmentBinding[])garmentBindings.Clone();
            InvalidateBindings();
        }

        void OnValidate() { InvalidateBindings(); }

        void InvalidateBindings()
        {
            checkedBindings = validBindings = false;
            resolved.Clear(); CurrentMask = -2; LastError = reportedError = null;
        }

        public bool ValidateBindings(out string error)
        {
            var collected = new List<Renderer[]>();
            bool valid = CollectBindings(collected, out error);
            return valid;
        }

        public bool Apply(int mask, string role)
        {
            // Even a bad garment binding must never switch off a known local body surface.
            EnsureBodyVisible();
            if (!string.Equals(role, characterClass, StringComparison.Ordinal))
                return Fail("Role '" + role + "' does not match this wardrobe's '" + characterClass + "'.");
            if (!checkedBindings)
            {
                resolved.Clear();
                validBindings = CollectBindings(resolved, out string error);
                checkedBindings = true;
                LastError = error;
            }
            if (!validBindings) return Fail(LastError);
            foreach (var group in resolved)
                foreach (var surface in group)
                    if (surface == null) return Fail("A bound garment renderer was destroyed; reconfigure the wardrobe bindings.");

            int selected = mask < 0 ? DeadAirClothing.DefaultMask(characterClass) : DeadAirClothing.Sanitize(mask);
            bool changed = CurrentMask != selected;
            for (int i = 0; i < garments.Length; i++)
            {
                var garment = garments[i];
                bool visible = garment.match == MaskMatch.Any ? (selected & garment.requiredMask) != 0 :
                    (selected & garment.requiredMask) == garment.requiredMask;
                foreach (var surface in resolved[i]) surface.enabled = visible;
            }
            CurrentMask = selected; LastError = reportedError = null;
            if (changed) GetComponent<DeadAirSecondaryMotion>()?.ResetMotion();
            return true;
        }

        bool Fail(string error)
        {
            LastError = error;
            if (reportedError != error)
            {
                Debug.LogWarning("Character wardrobe on '" + name + "': " + error +
                    " Garments were left unchanged; assign explicit body and garment bindings.", this);
                reportedError = error;
            }
            return false;
        }

        void EnsureBodyVisible()
        {
            if (fullBodyRenderers == null) return;
            foreach (var body in fullBodyRenderers)
                if (body != null && IsLocal(body.transform)) body.enabled = true;
        }

        bool IsLocal(Transform item) => item == transform || item.IsChildOf(transform);

        static Mesh SurfaceMesh(Renderer surface)
        {
            if (surface is SkinnedMeshRenderer skin) return skin.sharedMesh;
            var filter = surface.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        bool CollectBindings(List<Renderer[]> collected, out string error)
        {
            error = null;
            if (characterClass != "operator" && characterClass != "engineer" && characterClass != "journalist" && characterClass != "dispatcher")
            { error = "Assign one of the four supported character role IDs."; return false; }
            if (fullBodyRenderers == null || fullBodyRenderers.Length == 0)
            { error = "The complete body has no explicit renderer bindings."; return false; }
            var bodySet = new HashSet<Renderer>();
            var bodyMeshes = new HashSet<Mesh>();
            var bodyBones = new HashSet<Transform>();
            foreach (var body in fullBodyRenderers)
            {
                // Gameplay, pooling and FP equipment own GameObject visibility. A hidden
                // presentation object still has a valid binding and may reappear next frame.
                if (body == null || !IsLocal(body.transform) || !bodySet.Add(body))
                { error = "Body bindings must be unique objects inside this character."; return false; }
                var mesh = SurfaceMesh(body);
                if (mesh == null)
                { error = "A complete body renderer has no mesh."; return false; }
                bodyMeshes.Add(mesh);
                if (body is SkinnedMeshRenderer skin)
                    foreach (var bone in skin.bones) if (bone != null) bodyBones.Add(bone);
            }
            if (garments == null || garments.Length == 0)
            { error = "The wardrobe has no explicit garment entries."; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var assigned = new HashSet<Renderer>();
            foreach (var garment in garments)
            {
                if (garment == null || string.IsNullOrWhiteSpace(garment.id) || !ids.Add(garment.id))
                { error = "Each garment needs a unique, nonempty ID."; return false; }
                if (garment.requiredMask <= 0 || (garment.requiredMask & ~DeadAirClothing.All) != 0 ||
                    (garment.match != MaskMatch.All && garment.match != MaskMatch.Any))
                { error = "Garment '" + garment.id + "' has an invalid mask or matching rule."; return false; }
                var surfaces = new HashSet<Renderer>();
                if (garment.renderers != null)
                    foreach (var surface in garment.renderers) surfaces.Add(surface);
                if (garment.groups != null)
                    foreach (var group in garment.groups)
                    {
                        if (group == null || group.transform == transform || !IsLocal(group.transform))
                        { error = "Garment '" + garment.id + "' has a missing, external or avatar-root group."; return false; }
                        foreach (var bone in bodyBones)
                            if (bone == group.transform || bone.IsChildOf(group.transform))
                            { error = "Garment '" + garment.id + "' contains part of the body's rig hierarchy."; return false; }
                        foreach (var surface in group.GetComponentsInChildren<Renderer>(true)) surfaces.Add(surface);
                    }
                if (surfaces.Count == 0)
                { error = "Garment '" + garment.id + "' has no renderer bindings."; return false; }
                foreach (var surface in surfaces)
                {
                    if (surface == null || !IsLocal(surface.transform))
                    { error = "Garment '" + garment.id + "' references a missing or external renderer."; return false; }
                    if (bodySet.Contains(surface) || bodyMeshes.Contains(SurfaceMesh(surface)))
                    { error = "Garment '" + garment.id + "' references a complete body renderer or its mesh."; return false; }
                    if (!assigned.Add(surface))
                    { error = "A renderer is assigned to more than one garment."; return false; }
                }
                var items = new Renderer[surfaces.Count]; surfaces.CopyTo(items); collected.Add(items);
            }
            return true;
        }
    }
}
