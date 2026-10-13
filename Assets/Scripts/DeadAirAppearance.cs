using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>Cached wardrobe on the imported adult rigs, with a neutral body beneath removable garments.</summary>
    public sealed class DeadAirAppearance : MonoBehaviour
    {
        sealed class SourceSurface
        {
            public SkinnedMeshRenderer renderer;
            public Mesh original, mesh;
            public List<int>[][] groups;
            public int Apply(int mask)
            {
                int count = 0;
                for (int submesh = 0; submesh < groups.Length; submesh++)
                {
                    var indices = new List<int>(groups[submesh][0]);
                    for (int group = 1; group <= 5; group++)
                        if ((mask & (1 << (group - 1))) != 0) indices.AddRange(groups[submesh][group]);
                    mesh.SetTriangles(indices, submesh, false);
                    count += indices.Count / 3;
                }
                return count;
            }
        }
        readonly List<SourceSurface> surfaces = new List<SourceSurface>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly Dictionary<int, GameObject> garments = new Dictionary<int, GameObject>();
        Transform[] bones;
        Transform pelvis, spine, chest, head;
        Transform hairProxy, chestProxy, tieProxy;
        DeadAirSecondaryMotion secondary;
        GameObject baseBody;
        string classId;
        int currentMask = -2;
        bool initialized;
        bool authoredOutfit;
        int authoredTriangleCount;
        public bool UsesAuthoredOutfit => authoredOutfit;
        public int CurrentMask => currentMask;
        public DeadAirSecondaryMotion SecondaryMotion => secondary;
        public bool HasAdultBody => baseBody != null;
        public int VisibleSourceTriangleCount { get; private set; }
        public int RetainedSourceTriangleCount => TriangleCount(0);
        public int SourceGarmentTriangleCount(int flag) => TriangleCount(flag == DeadAirClothing.Shirt ? 1 :
            flag == DeadAirClothing.Jacket ? 2 : flag == DeadAirClothing.Trousers ? 3 : flag == DeadAirClothing.Shoes ? 4 : 5);

        int TriangleCount(int group)
        {
            int count = 0;
            foreach (var surface in surfaces) foreach (var submesh in surface.groups) count += submesh[group].Count / 3;
            return count;
        }

        public void Apply(int mask, string role)
        {
            if (!initialized) Initialize(role);
            mask = mask < 0 ? DeadAirClothing.DefaultMask(role) : DeadAirClothing.Sanitize(mask);
            if (currentMask == mask) return;
            currentMask = mask;
            if (authoredOutfit)
            {
                // The rebuilt cast has complete, authored clothing and joint weights.
                // The old colour classifier describes an unrelated source atlas:
                // applying it here would cut arbitrary holes and rebind face vertices.
                VisibleSourceTriangleCount = authoredTriangleCount;
                return;
            }
            VisibleSourceTriangleCount = 0;
            foreach (var surface in surfaces) VisibleSourceTriangleCount += surface.Apply(mask);
            // Full original clothing keeps its authored surface untouched. The
            // neutral body closes all openings when one or more layers is removed.
            if (baseBody != null) baseBody.SetActive(mask != DeadAirClothing.DefaultMask(classId));
            foreach (var garment in garments)
                garment.Value.SetActive((mask & garment.Key) != 0 && (garment.Key != DeadAirClothing.Underwear ||
                    (mask & DeadAirClothing.Shirt) == 0 || (mask & DeadAirClothing.Trousers) == 0));
            if (secondary != null) secondary.ResetMotion();
        }

        void Initialize(string role)
        {
            initialized = true; classId = role;
            var sources = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (sources.Length == 0) return;
            authoredOutfit = DeadAirBodyFit.FindLandmark(transform, "IndieCast") != null ||
                DeadAirBodyFit.FindLandmark(transform, "RestoredCast") != null;
            if (authoredOutfit)
            {
                foreach (var source in sources)
                    if (source.sharedMesh != null)
                        for (int submesh = 0; submesh < source.sharedMesh.subMeshCount; submesh++)
                            authoredTriangleCount += (int)source.sharedMesh.GetIndexCount(submesh) / 3;
                // Restored characters carry explicitly weighted deform chains. Never
                // classify their face/clothing by colour or replace their bind poses.
                secondary = gameObject.GetComponent<DeadAirSecondaryMotion>() ?? gameObject.AddComponent<DeadAirSecondaryMotion>();
                secondary.ConfigureAuthoredRig(role);
                return;
            }
            var found = new Dictionary<string, Transform>();
            foreach (var bone in GetComponentsInChildren<Transform>(true)) if (!found.ContainsKey(bone.name)) found.Add(bone.name, bone);
            found.TryGetValue("pelvis", out pelvis); found.TryGetValue("spine", out spine);
            found.TryGetValue("chest", out chest); found.TryGetValue("head", out head);
            if (pelvis == null || chest == null || head == null) return;
            secondary = gameObject.GetComponent<DeadAirSecondaryMotion>() ?? gameObject.AddComponent<DeadAirSecondaryMotion>();
            hairProxy = Proxy("Secondary hair", head); chestProxy = Proxy("Secondary clothed chest", chest); tieProxy = Proxy("Secondary tie", chest);
            secondary.hair = hairProxy; secondary.chest = chestProxy; secondary.tie = tieProxy;
            var boneList = new List<Transform>(sources[0].bones);
            foreach (var source in sources) foreach (var bone in source.bones) if (!boneList.Contains(bone)) boneList.Add(bone);
            boneList.Add(chestProxy);
            bones = boneList.ToArray();
            var paletteAsset = Resources.Load<TextAsset>("Characters/Appearance/" + role + "-palette");
            byte[] palette = paletteAsset != null ? paletteAsset.bytes : null;
            foreach (var source in sources) SplitSource(source, palette);
            BuildBodyAndMissingGarments(found, palette);
        }

        Transform Proxy(string name, Transform parent)
        {
            var value = new GameObject(name).transform; value.SetParent(parent, false); return value;
        }

        void SplitSource(SkinnedMeshRenderer renderer, byte[] palette)
        {
            var original = renderer.sharedMesh;
            if (original == null || !original.isReadable) return;
            var surface = new SourceSurface { renderer = renderer, original = original,
                mesh = Instantiate(original), groups = new List<int>[original.subMeshCount][] };
            surface.mesh.name = original.name + " / removable adult wardrobe"; surface.mesh.hideFlags = HideFlags.DontSave;
            owned.Add(surface.mesh); renderer.sharedMesh = surface.mesh;
            var vertices = original.vertices; var uv = original.uv; var weights = original.boneWeights;
            var sourceBones = renderer.bones;
            var restPoints = new Vector3[vertices.Length];
            for (int index = 0; index < vertices.Length; index++) restPoints[index] = transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[index]));
            for (int submesh = 0; submesh < original.subMeshCount; submesh++)
            {
                var groups = surface.groups[submesh] = new List<int>[6];
                for (int group = 0; group < 6; group++) groups[group] = new List<int>();
                var indices = original.GetTriangles(submesh);
                for (int index = 0; index < indices.Length; index += 3)
                {
                    int a = indices[index], b = indices[index + 1], c = indices[index + 2];
                    Vector3 center = (restPoints[a] + restPoints[b] + restPoints[c]) / 3;
                    Color color = Palette(palette, uv.Length > a ? (uv[a] + uv[b] + uv[c]) / 3 : Vector2.zero);
                    int group = ClothingGroup(center, color, weights.Length > a ? weights[a] : default, sourceBones);
                    groups[group].Add(a); groups[group].Add(b); groups[group].Add(c);
                }
            }
            BindSecondaryBones(surface.mesh, renderer, restPoints, uv, weights, palette);
            surfaces.Add(surface);
        }

        int ClothingGroup(Vector3 point, Color color, BoneWeight weight, Transform[] sourceBones)
        {
            string bone = DominantBone(weight, sourceBones)?.name ?? "";
            // Faces, source fingers, earrings and every head-bound long hair
            // component survive garment removal, retaining individual identity.
            if (bone == "head")
            {
                // A few imported white collar triangles share the head bone.
                // Their low front position separates them from face and hair.
                float headY = Point(head).y;
                float chroma = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) - Mathf.Min(color.r, Mathf.Min(color.g, color.b));
                if ((classId == "engineer" || classId == "journalist") && point.y < headY - .025f && point.z > .025f && color.grayscale > .58f && chroma < .15f) return 1;
                return 0;
            }
            if (bone.StartsWith("hand.", StringComparison.Ordinal)) return 0;
            float hipY = transform.InverseTransformPoint(pelvis.position).y;
            if (bone.StartsWith("foot.", StringComparison.Ordinal) || point.y < hipY * .20f) return 4;
            if (point.y < hipY - .025f) return 3;
            // Source atlases use skin-like beige cotton. Retaining triangles by
            // colour would leave white shirts behind in the zero-mask outfit.
            // Keep identity at the original head/hands; exposed torso and limbs
            // use the complete underlying body when their covering layer is off.
            if (classId == "engineer") return color.grayscale < .28f ? 2 : 1;
            if (classId == "journalist") return color.grayscale > .60f || Mathf.Abs(point.x) < .095f && point.z > .08f ? 1 : 2;
            return 1; // A dress occupies the shirt and lower-garment bands.
        }

        static bool IsSkin(Color color) => color.r > .30f && color.g > .17f && color.r > color.g * 1.07f &&
            color.g > color.b * 1.03f && color.r - color.g < .29f;
        static Color Palette(byte[] palette, Vector2 uv)
        {
            if (palette == null || palette.Length != 128 * 128 * 3) return Color.gray;
            int index = (Mathf.Clamp(Mathf.FloorToInt(uv.y * 128), 0, 127) * 128 + Mathf.Clamp(Mathf.FloorToInt(uv.x * 128), 0, 127)) * 3;
            return new Color(palette[index] / 255f, palette[index + 1] / 255f, palette[index + 2] / 255f);
        }
        static Transform DominantBone(BoneWeight weight, Transform[] sourceBones)
        {
            int index = weight.boneIndex0; float largest = weight.weight0;
            if (weight.weight1 > largest) { index = weight.boneIndex1; largest = weight.weight1; }
            if (weight.weight2 > largest) { index = weight.boneIndex2; largest = weight.weight2; }
            if (weight.weight3 > largest) index = weight.boneIndex3;
            return index >= 0 && index < sourceBones.Length ? sourceBones[index] : null;
        }

        void BindSecondaryBones(Mesh mesh, SkinnedMeshRenderer renderer, Vector3[] points, Vector2[] uv, BoneWeight[] weights, byte[] palette)
        {
            var sourceBones = renderer.bones; var extendedBones = new List<Transform>(sourceBones);
            var bindposes = new List<Matrix4x4>(mesh.bindposes);
            int headIndex = Array.IndexOf(sourceBones, head), chestIndex = Array.IndexOf(sourceBones, chest);
            if (weights.Length != points.Length || headIndex < 0 || chestIndex < 0) return;
            int hairIndex = extendedBones.Count; extendedBones.Add(hairProxy); bindposes.Add(bindposes[headIndex]);
            int softChestIndex = extendedBones.Count; extendedBones.Add(chestProxy); bindposes.Add(bindposes[chestIndex]);
            int tieIndex = extendedBones.Count; extendedBones.Add(tieProxy); bindposes.Add(bindposes[chestIndex]);
            float headY = transform.InverseTransformPoint(head.position).y, chestY = transform.InverseTransformPoint(chest.position).y;
            for (int index = 0; index < weights.Length; index++)
            {
                var color = Palette(palette, uv.Length > index ? uv[index] : Vector2.zero); var point = points[index];
                var dominant = DominantBone(weights[index], sourceBones);
                bool lightHair = classId == "operator" && color.r > .55f && Mathf.Abs(color.r - color.b) < .18f;
                bool darkHair = color.grayscale < .34f && !IsSkin(color);
                if (dominant == head && (lightHair || darkHair) && (point.y > headY + .05f || point.z < .055f))
                    weights[index] = Replace(weights[index], headIndex, hairIndex);
                else if ((classId == "engineer" || classId == "journalist") && dominant == chest && Mathf.Abs(point.x) < .095f && point.z > .07f &&
                    point.y < chestY + .11f && color.grayscale > .16f && color.grayscale < .64f && !IsSkin(color))
                    weights[index] = Replace(weights[index], chestIndex, tieIndex);
                else if ((classId == "operator" || classId == "dispatcher") && dominant == chest &&
                    Mathf.Abs(point.x) < .19f && point.z > .065f && Mathf.Abs(point.y - chestY) < .14f)
                    weights[index] = Replace(weights[index], chestIndex, softChestIndex);
            }
            mesh.boneWeights = weights; mesh.bindposes = bindposes.ToArray(); renderer.bones = extendedBones.ToArray();
        }
        static BoneWeight Replace(BoneWeight value, int original, int proxy)
        {
            if (value.boneIndex0 == original) value.boneIndex0 = proxy;
            if (value.boneIndex1 == original) value.boneIndex1 = proxy;
            if (value.boneIndex2 == original) value.boneIndex2 = proxy;
            if (value.boneIndex3 == original) value.boneIndex3 = proxy;
            return value;
        }

        void BuildBodyAndMissingGarments(Dictionary<string, Transform> found, byte[] palette)
        {
            Mesh rounded = DeadAirMonsterBody.Ellipsoid(10, 6); owned.Add(rounded);
            Mesh tube = DeadAirMonsterBody.TaperedTube(10); owned.Add(tube);
            Color skinColor = classId == "operator" ? new Color32(172, 139, 74, 255) : classId == "dispatcher" ?
                new Color32(170, 132, 74, 255) : classId == "engineer" ? new Color32(175, 149, 135, 255) : new Color32(192, 160, 154, 255);
            var skin = Surface("Adult neutral body / " + classId, SourceHandSkinColor(palette, skinColor));
            var underwear = Surface("Plain cotton underlayer / " + classId, new Color32(189, 183, 158, 255));
            var shirt = Surface("Cotton shirt / " + classId, new Color32(172, 168, 146, 255));
            var jacket = Surface("Station jacket / " + classId, classId == "engineer" ? new Color32(32, 34, 33, 255) :
                classId == "dispatcher" ? new Color32(52, 46, 37, 255) : classId == "operator" ? new Color32(105, 35, 47, 255) : new Color32(48, 63, 66, 255));
            Vector3 hip = Point(pelvis), upper = Point(chest), neck = Point(head);
            float width = classId == "engineer" ? .32f : classId == "dispatcher" ? .165f : .17f;
            float depth = classId == "engineer" ? .23f : .125f;
            var body = new SkinBuilder(transform, bones);
            // One continuous adult torso, rather than disconnected pelvis,
            // stomach and chest primitives. Narrow shoulder/neck rings meet the
            // original head while the blended weights follow the actual rig.
            body.Loft(new[] { hip + Vector3.down * .15f, hip + Vector3.down * .04f,
                Vector3.Lerp(hip, upper, .25f), Vector3.Lerp(hip, upper, .55f), upper,
                upper + Vector3.up * .10f, Vector3.Lerp(upper + Vector3.up * .10f, neck, .55f), neck + Vector3.up * .025f },
                new[] { new Vector2(width * .52f, depth * .64f), new Vector2(width * .88f, depth * .91f),
                    new Vector2(width * .93f, depth), new Vector2(width * .90f, depth * .95f), new Vector2(width, depth * .92f),
                    new Vector2(width * .83f, depth * .80f), new Vector2(width * .30f, depth * .37f), new Vector2(width * .27f, depth * .34f) },
                new[] { body.Weight(pelvis), body.Weight(pelvis), body.Weight(pelvis, spine ?? chest, .55f),
                    body.Weight(spine ?? chest, chestProxy, .45f), body.Weight(chestProxy), body.Weight(chest),
                    body.Weight(chest, head, .55f), body.Weight(head) });
            foreach (string side in new[] { "R", "L" })
            {
                if (found.TryGetValue("upper_arm." + side, out var arm) && found.TryGetValue("forearm." + side, out var fore) && found.TryGetValue("hand." + side, out var hand))
                {
                    Vector3 shoulder = Point(arm), elbow = Point(fore), wrist = Point(hand);
                    body.Loft(new[] { shoulder + (upper - shoulder).normalized * .045f, shoulder,
                        Vector3.Lerp(shoulder, elbow, .45f), elbow, Vector3.Lerp(elbow, wrist, .42f), wrist },
                        new[] { Vector2.one * width * .29f, Vector2.one * width * .35f, Vector2.one * width * .32f,
                            Vector2.one * width * .25f, Vector2.one * width * .27f, Vector2.one * width * .20f },
                        new[] { body.Weight(arm), body.Weight(arm), body.Weight(arm), body.Weight(arm, fore, .5f), body.Weight(fore), body.Weight(hand) });
                }
                if (found.TryGetValue("thigh." + side, out var thigh) && found.TryGetValue("shin." + side, out var shin) && found.TryGetValue("foot." + side, out var foot))
                {
                    Vector3 legHip = Point(thigh), knee = Point(shin), anklePoint = Point(foot);
                    body.Loft(new[] { legHip + Vector3.up * .055f, legHip, Vector3.Lerp(legHip, knee, .42f), knee,
                        Vector3.Lerp(knee, anklePoint, .30f), Vector3.Lerp(knee, anklePoint, .72f), anklePoint },
                        new[] { Vector2.one * width * .48f, Vector2.one * width * .49f, Vector2.one * width * .43f,
                            Vector2.one * width * .31f, Vector2.one * width * .34f, Vector2.one * width * .25f, Vector2.one * width * .22f },
                        new[] { body.Weight(pelvis, thigh, .65f), body.Weight(thigh), body.Weight(thigh), body.Weight(thigh, shin, .5f),
                            body.Weight(shin), body.Weight(shin), body.Weight(foot) });
                    var ankle = Point(foot);
                    body.Shape(rounded, foot, new Vector3(ankle.x, .075f, ankle.z + .045f), new Vector3(width * .76f, .15f, .265f));
                }
            }
            // Adult body proportions, smooth skin, no explicit genital/nipple
            // geometry and no change in pose for the fully unclothed option.
            baseBody = body.Create("Adult body underneath removable layers", transform, skin, owned);
            var inner = new SkinBuilder(transform, bones);
            inner.Shape(rounded, pelvis, hip + new Vector3(0, -.045f, .005f), new Vector3(width * 1.70f, .255f, depth * 1.87f));
            if (classId == "operator" || classId == "dispatcher")
                inner.Shape(rounded, chest, upper + new Vector3(0, .015f, .02f), new Vector3(width * 2.04f, .155f, depth * 2.07f));
            garments.Add(DeadAirClothing.Underwear, inner.Create("Plain underwear layer", transform, underwear, owned));
            // Missing source layers still have a physical garment. Existing coat,
            // shirt, trousers and shoes keep their original texture and geometry.
            if (!HasSourceGroup(1))
            {
                var layer = new SkinBuilder(transform, bones);
                layer.Shape(rounded, spine ?? chest, Vector3.Lerp(hip, upper, .6f), new Vector3(width * 1.84f, upper.y - hip.y + .24f, depth * 2.05f));
                garments.Add(DeadAirClothing.Shirt, layer.Create("Cotton shirt layer", transform, shirt, owned));
            }
            if (!HasSourceGroup(2))
            {
                var layer = new SkinBuilder(transform, bones);
                layer.Shape(rounded, spine ?? chest, Vector3.Lerp(hip, upper, .47f), new Vector3(width * 2.1f, upper.y - hip.y + .32f, depth * 2.3f));
                foreach (string side in new[] { "R", "L" })
                    if (found.TryGetValue("upper_arm." + side, out var arm) && found.TryGetValue("forearm." + side, out var fore) && found.TryGetValue("hand." + side, out var hand))
                    { layer.Between(tube, arm, Point(arm), Point(fore), width * .55f, width * .54f); layer.Between(tube, fore, Point(fore), Point(hand), width * .40f, width * .40f); }
                garments.Add(DeadAirClothing.Jacket, layer.Create("Station jacket layer", transform, jacket, owned));
            }
        }
        bool HasSourceGroup(int group)
        { foreach (var surface in surfaces) foreach (var submesh in surface.groups) if (submesh[group].Count > 12) return true; return false; }
        Vector3 Point(Transform bone) => transform.InverseTransformPoint(bone.position);
        Color SourceHandSkinColor(byte[] palette, Color fallback)
        {
            if (palette == null) return fallback;
            Color total = Color.black; int count = 0;
            foreach (var surface in surfaces)
            {
                var uv = surface.original.uv; var weights = surface.original.boneWeights; var sourceBones = surface.renderer.bones;
                for (int index = 0; index < weights.Length && index < uv.Length; index++)
                {
                    var bone = DominantBone(weights[index], sourceBones);
                    if (bone == null || !bone.name.StartsWith("hand.", StringComparison.Ordinal)) continue;
                    var color = Palette(palette, uv[index]);
                    if (color.grayscale < .13f || color.r > color.g * 1.6f && color.g < .23f) continue;
                    total += color; count++;
                }
            }
            if (count < 3) return fallback;
            var average = total / count; average.a = 1; return average;
        }
        Material Surface(string name, Color color)
        {
            var material = new Material(Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard"))
                { name = name, color = color, hideFlags = HideFlags.DontSave };
            material.SetColor("_EmissionColor", Color.black); material.SetFloat("_Glossiness", .10f); owned.Add(material); return material;
        }

        sealed class SkinBuilder
        {
            readonly Transform root;
            readonly Transform[] bones;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<BoneWeight> weights = new List<BoneWeight>();
            public SkinBuilder(Transform root, Transform[] bones) { this.root = root; this.bones = bones; }
            public BoneWeight Weight(Transform first, Transform second = null, float blend = 0)
            {
                return new BoneWeight { boneIndex0 = Mathf.Max(0, Array.IndexOf(bones, first)), weight0 = 1 - blend,
                    boneIndex1 = second == null ? 0 : Mathf.Max(0, Array.IndexOf(bones, second)), weight1 = blend };
            }
            public void Loft(Vector3[] centers, Vector2[] radii, BoneWeight[] ringWeights)
            {
                const int sides = 16;
                int start = vertices.Count;
                for (int row = 0; row < centers.Length; row++)
                {
                    Vector3 direction = row == 0 ? centers[1] - centers[0] : row == centers.Length - 1 ? centers[row] - centers[row - 1] : centers[row + 1] - centers[row - 1];
                    Quaternion orientation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
                    for (int side = 0; side < sides; side++)
                    {
                        float angle = side * Mathf.PI * 2 / sides;
                        vertices.Add(centers[row] + orientation * new Vector3(Mathf.Cos(angle) * radii[row].x, 0, Mathf.Sin(angle) * radii[row].y));
                        weights.Add(ringWeights[row]);
                    }
                    if (row == 0) continue;
                    for (int side = 0; side < sides; side++)
                    {
                        int a = start + (row - 1) * sides + side, b = start + (row - 1) * sides + (side + 1) % sides;
                        int c = start + row * sides + side, d = start + row * sides + (side + 1) % sides;
                        triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                }
                int bottom = vertices.Count; vertices.Add(centers[0]); weights.Add(ringWeights[0]);
                int top = vertices.Count; vertices.Add(centers[centers.Length - 1]); weights.Add(ringWeights[ringWeights.Length - 1]);
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    triangles.Add(bottom); triangles.Add(start + side); triangles.Add(start + next);
                    triangles.Add(top); triangles.Add(start + (centers.Length - 1) * sides + next); triangles.Add(start + (centers.Length - 1) * sides + side);
                }
            }
            public void Between(Mesh source, Transform bone, Vector3 a, Vector3 b, float radiusX, float radiusZ)
            {
                var offset = b - a;
                Shape(source, bone, (a + b) * .5f, new Vector3(radiusX * 2, offset.magnitude + .03f, radiusZ * 2), Quaternion.FromToRotation(Vector3.up, offset.normalized));
            }
            public void Shape(Mesh source, Transform bone, Vector3 center, Vector3 size, Quaternion rotation = default)
            {
                // Unity's quaternion == compares orientation via a dot product;
                // the all-zero optional value is never equal even to itself.
                float norm = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
                if (norm < .0001f) rotation = Quaternion.identity;
                int first = vertices.Count, boneIndex = Array.IndexOf(bones, bone);
                var matrix = Matrix4x4.TRS(center, rotation, size);
                foreach (var point in source.vertices)
                { vertices.Add(matrix.MultiplyPoint3x4(point)); weights.Add(new BoneWeight { boneIndex0 = Mathf.Max(0, boneIndex), weight0 = 1 }); }
                foreach (int index in source.triangles) triangles.Add(first + index);
            }
            public GameObject Create(string name, Transform parent, Material material, List<UnityEngine.Object> owned)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.boneWeights = weights.ToArray();
                var poses = new Matrix4x4[bones.Length];
                for (int index = 0; index < bones.Length; index++) poses[index] = bones[index].worldToLocalMatrix * root.localToWorldMatrix;
                mesh.bindposes = poses; mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
                var child = new GameObject(name); child.layer = parent.gameObject.layer; child.transform.SetParent(parent, false);
                var renderer = child.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.sharedMaterial = material;
                renderer.bones = bones; renderer.rootBone = parent; renderer.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * .35f);
                renderer.updateWhenOffscreen = false; renderer.receiveShadows = true; renderer.shadowCastingMode = ShadowCastingMode.On;
                return child;
            }
        }

        void OnDestroy()
        {
            foreach (var surface in surfaces) if (surface.renderer != null) surface.renderer.sharedMesh = surface.original;
            foreach (var item in owned) if (item != null) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
            owned.Clear();
        }
    }
}
