using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // A deliberately adult, long-limbed station worker. All geometry belongs to
    // the existing fourteen animation pivots; the eye wrap stays opaque.
    internal static class DeadAirMonsterBody
    {
        internal sealed class Rig
        {
            public Transform model, spine, head, leftArm, rightArm, leftElbow, rightElbow,
                leftHand, rightHand, leftLeg, rightLeg, leftKnee, rightKnee, leftFoot, rightFoot;
        }

        internal static Rig Build(Transform parent, List<Object> owned)
        {
            var rig = new Rig();
            var shader = Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard");
            var skin = Surface("Dиктор / cold adult skin", shader, new Color32(126, 136, 125, 255), owned);
            var coat = Surface("Диктор / rotted station coat", shader, new Color32(45, 56, 48, 255), owned);
            var cloth = Surface("Диктор / worn opaque bandage", shader, new Color32(103, 104, 82, 255), owned);
            var dark = Surface("Диктор / recessed mouth and nails", shader, new Color32(30, 31, 28, 255), owned);
            var rust = Surface("Диктор / old identification ribbon", shader, new Color32(102, 48, 38, 255), owned);
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
                { name = "Диктор / woven coat wear", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[1024];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                byte tone = (byte)(151 + (x * 17 + y * 31 + x * y * 3) % 60 + ((x + y) % 5 == 0 ? 24 : 0));
                pixels[y * 32 + x] = new Color32(tone, tone, tone, 255);
            }
            texture.SetPixels32(pixels); texture.Apply(false, true); owned.Add(texture);
            coat.mainTexture = cloth.mainTexture = texture;
            Mesh round = Ellipsoid(10, 6); owned.Add(round);
            Mesh tube = TaperedTube(10); owned.Add(tube);
            rig.model = Group("Anchor / adult listening humanoid", parent, Vector3.zero);
            rig.spine = Group("Anchor spine", rig.model, new Vector3(0, 1.15f, 0));
            // Profile broadens at the shoulders and hollows at the waist. The
            // hem has irregular lengths instead of a rectangular torso block.
            var torso = Profile(new[] { new Vector4(0, -.26f, .25f, .14f), new Vector4(0, .15f, .20f, .125f),
                new Vector4(.018f, .72f, .34f, .17f), new Vector4(.01f, 1.01f, .36f, .17f),
                new Vector4(0, 1.10f, .12f, .10f) }, 12, true);
            owned.Add(torso); Part("Torn overcoat torso", rig.spine, torso, coat, Vector3.zero, Vector3.one);
            Part("Sunken breastbone", rig.spine, round, skin, new Vector3(0, .78f, .15f), new Vector3(.115f, .28f, .06f));
            Part("Collar left", rig.spine, tube, cloth, new Vector3(-.09f, .84f, .16f), new Vector3(.07f, .37f, .045f), new Vector3(0, 0, -17));
            Part("Collar right", rig.spine, tube, cloth, new Vector3(.09f, .85f, .16f), new Vector3(.07f, .39f, .045f), new Vector3(0, 0, 14));
            Part("Faded service ribbon", rig.spine, tube, rust, new Vector3(.12f, .56f, .166f), new Vector3(.038f, .30f, .015f), new Vector3(0, 0, 7));
            for (int button = 0; button < 4; button++) Part("Coat fastener", rig.spine, round, dark,
                new Vector3(-.032f, .56f - button * .17f, .156f), new Vector3(.025f, .025f, .018f));
            rig.head = Group("Adult listening head", rig.spine, new Vector3(0, 1.35f, .015f));
            Part("Long mature skull", rig.head, round, skin, new Vector3(0, .035f, 0), new Vector3(.33f, .49f, .34f));
            Part("Hollow left cheek", rig.head, round, skin, new Vector3(-.114f, -.035f, .124f), new Vector3(.11f, .16f, .055f));
            Part("Hollow right cheek", rig.head, round, skin, new Vector3(.117f, -.040f, .124f), new Vector3(.10f, .17f, .052f));
            Part("Angular nose", rig.head, tube, skin, new Vector3(0, -.018f, .18f), new Vector3(.052f, .12f, .072f), new Vector3(-12, 0, 0));
            Part("Asymmetric jaw", rig.head, round, skin, new Vector3(.012f, -.145f, .073f), new Vector3(.21f, .115f, .19f));
            Part("Recessed mouth", rig.head, round, dark, new Vector3(.008f, -.118f, .153f), new Vector3(.108f, .045f, .028f));
            Part("Sunken lower lip", rig.head, round, skin, new Vector3(.008f, -.147f, .158f), new Vector3(.107f, .018f, .025f));
            Part("Ear left", rig.head, round, skin, new Vector3(-.172f, .015f, .005f), new Vector3(.052f, .10f, .058f));
            Part("Ear right", rig.head, round, skin, new Vector3(.172f, .015f, .005f), new Vector3(.052f, .105f, .058f));
            Part("Opaque eye wrap", rig.head, tube, cloth, new Vector3(0, .071f, .013f), new Vector3(.36f, .115f, .355f));
            Part("Eye wrap knot", rig.head, round, cloth, new Vector3(.088f, .067f, -.167f), new Vector3(.10f, .09f, .067f));
            Part("Neck tendons", rig.spine, tube, skin, new Vector3(0, 1.17f, 0), new Vector3(.15f, .29f, .15f));
            for (int side = -1; side <= 1; side += 2)
            {
                var leg = Group(side < 0 ? "Anchor hip left" : "Anchor hip right", rig.model, new Vector3(side * .17f, 1.15f, 0));
                var knee = Group("Anchor knee", leg, new Vector3(0, -.64f, 0));
                var foot = Group("Anchor ankle", knee, new Vector3(0, -.41f, 0));
                Part("Worn trouser thigh", leg, tube, coat, new Vector3(0, -.30f, 0), new Vector3(.22f, .69f, .20f));
                Part("Bony knee", knee, round, skin, new Vector3(0, .015f, .03f), new Vector3(.135f, .15f, .14f));
                Part("Trouser calf", knee, tube, coat, new Vector3(0, -.205f, 0), new Vector3(.15f, .43f, .14f));
                Part("Cracked work shoe", foot, round, dark, new Vector3(0, -.055f, .065f), new Vector3(.19f, .145f, .37f));
                var arm = Group(side < 0 ? "Anchor shoulder left" : "Anchor shoulder right", rig.spine, new Vector3(side * .365f, 1.00f, 0));
                var elbow = Group("Anchor elbow", arm, new Vector3(side * .015f, -.64f, 0));
                var hand = Group("Anchor wrist", elbow, new Vector3(0, -.49f, .02f));
                Part("Loose coat sleeve", arm, tube, coat, new Vector3(side * .01f, -.295f, 0), new Vector3(.18f, .68f, .19f));
                Part("Elbow bone", elbow, round, skin, new Vector3(0, 0, .02f), new Vector3(.115f, .13f, .14f));
                Part("Exposed sinewy forearm", elbow, tube, skin, new Vector3(0, -.235f, .01f), new Vector3(.10f, .49f, .10f));
                Part("Long adult palm", hand, round, skin, new Vector3(0, -.087f, .018f), new Vector3(.14f, .22f, .073f));
                for (int finger = 0; finger < 4; finger++)
                {
                    float length = .13f + Mathf.Sin(finger * .8f) * .04f;
                    float x = (finger - 1.5f) * .032f;
                    Part("Long finger", hand, tube, skin, new Vector3(x, -.19f - length * .35f, .025f), new Vector3(.025f, length, .027f), new Vector3(-8 - finger * 2, 0, (finger - 1.5f) * 5));
                    Part("Dark nail", hand, round, dark, new Vector3(x, -.19f - length * .82f, .043f), new Vector3(.020f, .034f, .009f));
                }
                Part("Adult thumb", hand, tube, skin, new Vector3(-side * .068f, -.10f, .033f), new Vector3(.033f, .125f, .036f), new Vector3(-14, 0, -side * 29));
                if (side < 0) { rig.leftLeg = leg; rig.leftKnee = knee; rig.leftFoot = foot; rig.leftArm = arm; rig.leftElbow = elbow; rig.leftHand = hand; }
                else { rig.rightLeg = leg; rig.rightKnee = knee; rig.rightFoot = foot; rig.rightArm = arm; rig.rightElbow = elbow; rig.rightHand = hand; }
            }
            // Pieces with the same material and pivot become one renderer. A
            // finger never adds another per-frame renderer or physics body.
            CombinePivotParts(rig.model, owned);
            return rig;
        }

        static Material Surface(string name, Shader shader, Color color, List<Object> owned)
        {
            var value = new Material(shader) { name = name, color = color, hideFlags = HideFlags.DontSave };
            value.SetColor("_EmissionColor", Color.black); value.SetFloat("_Glossiness", .04f); owned.Add(value); return value;
        }
        static Transform Group(string name, Transform parent, Vector3 position)
        { var value = new GameObject(name).transform; value.SetParent(parent, false); value.localPosition = position; return value; }
        static void Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 scale, Vector3 rotation = default)
        {
            var part = Group(name, parent, position); part.localScale = scale; part.localRotation = Quaternion.Euler(rotation);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
        }
        internal static Mesh Ellipsoid(int sides, int rings)
        {
            var rows = new Vector4[rings + 1];
            for (int row = 0; row <= rings; row++)
            { float angle = row * Mathf.PI / rings; rows[row] = new Vector4(0, -Mathf.Cos(angle) * .5f, Mathf.Sin(angle) * .5f, Mathf.Sin(angle) * .5f); }
            return Profile(rows, sides, false);
        }
        internal static Mesh TaperedTube(int sides) => Profile(new[] { new Vector4(0, -.5f, .33f, .33f),
            new Vector4(0, -.33f, .44f, .44f), new Vector4(0, .30f, .5f, .5f), new Vector4(0, .5f, .4f, .4f) }, sides, false);
        static Mesh Profile(Vector4[] rows, int sides, bool ragged)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int row = 0; row < rows.Length; row++) for (int side = 0; side <= sides; side++)
            {
                float angle = side * Mathf.PI * 2 / sides; var p = rows[row];
                float hem = ragged && row == 0 ? ((side * 7) % 5) * .019f : 0;
                vertices.Add(new Vector3(p.x + Mathf.Cos(angle) * p.z, p.y - hem, Mathf.Sin(angle) * p.w));
                uv.Add(new Vector2(side / (float)sides, row / (float)(rows.Length - 1)));
                if (row == 0 || side == sides) continue;
                int a = (row - 1) * (sides + 1) + side, b = row * (sides + 1) + side;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + 1); triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
            }
            for (int end = 0; end < 2; end++)
            {
                int row = end == 0 ? 0 : rows.Length - 1, center = vertices.Count, first = row * (sides + 1);
                vertices.Add(new Vector3(rows[row].x, rows[row].y, 0)); uv.Add(new Vector2(.5f, end));
                for (int side = 0; side < sides; side++)
                { triangles.Add(center); triangles.Add(first + (end == 0 ? side : side + 1)); triangles.Add(first + (end == 0 ? side + 1 : side)); }
            }
            var mesh = new Mesh { name = "Adult humanoid sculpted profile", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static void CombinePivotParts(Transform parent, List<Object> owned)
        {
            var children = new List<Transform>(); foreach (Transform child in parent) children.Add(child);
            var groups = new Dictionary<Material, List<Transform>>();
            foreach (var child in children)
            {
                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null) { CombinePivotParts(child, owned); continue; }
                if (!groups.TryGetValue(renderer.sharedMaterial, out var list)) groups.Add(renderer.sharedMaterial, list = new List<Transform>());
                list.Add(child);
            }
            foreach (var group in groups)
            {
                if (group.Value.Count < 2) continue;
                var instances = new CombineInstance[group.Value.Count];
                for (int index = 0; index < instances.Length; index++)
                    instances[index] = new CombineInstance { mesh = group.Value[index].GetComponent<MeshFilter>().sharedMesh,
                        transform = parent.worldToLocalMatrix * group.Value[index].localToWorldMatrix };
                var mesh = new Mesh { name = parent.name + " / articulated surface", hideFlags = HideFlags.DontSave };
                mesh.CombineMeshes(instances); owned.Add(mesh);
                Part(group.Key.name, parent, mesh, group.Key, Vector3.zero, Vector3.one);
                foreach (var child in group.Value) { if (Application.isPlaying) Object.Destroy(child.gameObject); else Object.DestroyImmediate(child.gameObject); }
            }
        }
    }
}
