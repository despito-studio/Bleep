using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // Original faceted fungi. No colliders: a trip never changes the navigation or controls.
    public sealed class DeadAirMushrooms : MonoBehaviour
    {
        public const float TripSeconds = 22, ExtensionSeconds = 13, MaximumTripSeconds = 35;
        public const float EatRange = 2.2f, EyeOffset = .46f;
        DeadAirSession session;
        readonly List<Transform> clusters = new List<Transform>();
        readonly List<Light> lights = new List<Light>();
        readonly List<Material> materials = new List<Material>();
        MushroomData[] placements;
        Mesh stem, cap, spots, gills;

        public static float Remaining(RoomState room, PlayerState player)
        {
            if (room == null || player == null || room.phase != "playing" || player.down) return 0;
            return Mathf.Max(0, player.mushroomTripUntil - room.elapsed);
        }

        public static float Phase(RoomState room, PlayerState player)
        { return Remaining(room, player) > 0 ? Mathf.Max(0, room.elapsed - player.mushroomTripStartedAt) : 0; }

        public static float Intensity(RoomState room, PlayerState player)
        {
            if (room == null || room.paused) return 0;
            float remaining = Remaining(room, player);
            if (remaining <= 0) return 0;
            return Mathf.SmoothStep(0, 1, Mathf.Clamp01(Phase(room, player) / 1.3f)) *
                Mathf.SmoothStep(0, 1, Mathf.Clamp01(remaining / 4));
        }

        public static MushroomData[] CreatePlacements(WorldData world)
        {
            return Array.Empty<MushroomData>();
        }

        static void AddPlacement(WorldData world, List<MushroomData> output, string id, string label,
            float x, float y, float z, int variant, bool atStart = false)
        {
            Vector3 position = new Vector3(x, y, z);
            if (world.navigationNodes != null && world.navigationNodes.Length > 0)
            {
                float nearest = atStart ? 2.5f : 1.6f;
                bool found = false;
                foreach (var node in world.navigationNodes)
                {
                    if (Mathf.Abs(node.y - y) > .15f || OnStair(world, node.x, node.z)) continue;
                    float distance = Vector2.Distance(new Vector2(x, z), new Vector2(node.x, node.z));
                    if (distance >= nearest) continue;
                    nearest = distance; position = new Vector3(node.x, node.y, node.z); found = true;
                }
                if (!found) return;
            }
            else if (!atStart) return;
            if (world.courtyard != null && (position.x < world.courtyard.minX + .5f || position.x > world.courtyard.maxX - .5f ||
                position.z < world.courtyard.minZ + .5f || position.z > world.courtyard.maxZ - .5f)) return;
            foreach (var other in output)
                if (Vector3.Distance(position, new Vector3(other.x, other.y, other.z)) < 1) return;
            output.Add(new MushroomData { id = id, label = label, x = position.x, y = position.y, z = position.z, variant = variant });
        }

        static bool OnStair(WorldData world, float x, float z)
        {
            if (world.stairs != null) foreach (var stair in world.stairs) if (stair.Contains(x, z)) return true;
            return false;
        }

        public void Initialize(WorldData world, DeadAirSession game)
        {
            session = game; placements = world.Mushrooms;
            if (placements == null || placements.Length == 0) return;
            stem = Lathe("Angular mushroom stem", new[] { new Vector2(.082f, .015f), new Vector2(.064f, .2f),
                new Vector2(.074f, .42f), new Vector2(.088f, .6f) }, 7, true);
            cap = Lathe("Faceted umbrella cap", new[] { new Vector2(0, .91f), new Vector2(.14f, .875f),
                new Vector2(.3f, .77f), new Vector2(.37f, .64f), new Vector2(.32f, .59f),
                new Vector2(.07f, .585f), new Vector2(0, .61f) }, 10, false);
            spots = SpotMesh(); gills = GillMesh();
            var stalkMaterial = MakeMaterial("Ivory fungal stalk", new Color(.69f, .71f, .58f), new Color(.015f, .018f, .006f));
            var spotsMaterial = MakeMaterial("Luminous porcelain freckles", new Color(.94f, .91f, .72f), new Color(.33f, .3f, .16f));
            var gillMaterial = MakeMaterial("Dark radial gills", new Color(.17f, .21f, .18f), Color.black);
            Color[] hues = { new Color(.96f, .19f, .27f), new Color(.58f, .28f, .94f), new Color(.17f, .82f, .61f) };
            var capMaterials = new Material[hues.Length];
            for (int i = 0; i < hues.Length; i++) capMaterials[i] = MakeMaterial("Signal mushroom cap " + i, hues[i], hues[i] * .22f);
            for (int i = 0; i < placements.Length; i++)
            {
                var placement = placements[i];
                var cluster = new GameObject("interactive:" + placement.id).transform;
                cluster.SetParent(transform, false);
                cluster.position = new Vector3(-placement.x, placement.y + .015f, placement.z);
                cluster.rotation = Quaternion.Euler(0, i * 71, 0);
                clusters.Add(cluster);
                for (int n = 0; n < 3; n++)
                {
                    var fungus = new GameObject("Fungus " + (n + 1)).transform;
                    fungus.SetParent(cluster, false);
                    fungus.localPosition = n == 0 ? Vector3.zero : new Vector3(n == 1 ? .32f : -.28f, 0, n == 1 ? .17f : -.2f);
                    fungus.localRotation = Quaternion.Euler(n == 2 ? 7 : 0, n * 47, n == 1 ? -8 : 4);
                    fungus.localScale = Vector3.one * (n == 0 ? .8f : n == 1 ? .52f : .39f);
                    Part(fungus, "Stem", stem, stalkMaterial);
                    Part(fungus, "Cap", cap, capMaterials[placement.variant % hues.Length]);
                    Part(fungus, "Freckles", spots, spotsMaterial);
                    Part(fungus, "Gills", gills, gillMaterial);
                }
                var glow = new GameObject("Fungal glow").AddComponent<Light>();
                glow.transform.SetParent(cluster, false); glow.transform.localPosition = Vector3.up * .3f;
                glow.color = hues[placement.variant % hues.Length]; glow.type = LightType.Point;
                glow.range = 1.65f; glow.intensity = .22f; glow.shadows = LightShadows.None;
                glow.renderMode = LightRenderMode.ForceVertex; lights.Add(glow);
                cluster.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (placements == null) return;
            var state = session != null ? session.State : null;
            bool playing = state != null && state.phase == "playing";
            float clock = state != null ? state.elapsed : 0;
            for (int i = 0; i < placements.Length; i++)
            {
                bool visible = playing && !state.Collected(placements[i].id);
                if (clusters[i].gameObject.activeSelf != visible) clusters[i].gameObject.SetActive(visible);
                if (!visible) continue;
                float pulse = Mathf.Sin(clock * 1.6f + i * 1.3f);
                clusters[i].localScale = new Vector3(1, 1 + pulse * .018f, 1);
                lights[i].intensity = .21f + pulse * .035f;
            }
        }

        Material MakeMaterial(string label, Color color, Color emission)
        {
            var material = new Material(Shader.Find("Standard")) { name = label };
            material.color = color; material.SetFloat("_Glossiness", .16f);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", emission);
            materials.Add(material); return material;
        }

        static void Part(Transform parent, string title, Mesh mesh, Material material)
        {
            var part = new GameObject(title); part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
        }

        static Mesh Lathe(string title, Vector2[] profile, int sides, bool bend)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            bool descending = profile[0].y > profile[profile.Length - 1].y;
            for (int ring = 0; ring < profile.Length - 1; ring++)
            for (int side = 0; side < sides; side++)
            {
                var a = RingPoint(profile[ring], side, sides, bend);
                var b = RingPoint(profile[ring], side + 1, sides, bend);
                var c = RingPoint(profile[ring + 1], side, sides, bend);
                var d = RingPoint(profile[ring + 1], side + 1, sides, bend);
                if (profile[ring].x > 0) Triangle(vertices, triangles, a, descending ? b : c, descending ? c : b);
                if (profile[ring + 1].x > 0) Triangle(vertices, triangles, b, descending ? d : c, descending ? c : d);
            }
            return MeshFrom(title, vertices, triangles);
        }

        static Vector3 RingPoint(Vector2 profile, int side, int sides, bool bend)
        {
            float angle = side * Mathf.PI * 2 / sides;
            return new Vector3(Mathf.Cos(angle) * profile.x + (bend ? Mathf.Sin(profile.y * 5) * .025f : 0),
                profile.y, Mathf.Sin(angle) * profile.x);
        }

        static Mesh SpotMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int spot = 0; spot < 7; spot++)
            {
                float angle = spot * 2.39996f;
                float radius = spot == 0 ? .028f : spot % 2 == 0 ? .235f : .13f;
                float height = radius <= .14f ? Mathf.Lerp(.91f, .875f, radius / .14f) :
                    Mathf.Lerp(.875f, .77f, (radius - .14f) / .16f);
                var center = new Vector3(Mathf.Cos(angle) * radius, height + .011f, Mathf.Sin(angle) * radius);
                float size = spot % 3 == 0 ? .049f : .031f;
                for (int side = 0; side < 5; side++)
                {
                    float a = side * Mathf.PI * 2 / 5, b = (side + 1) * Mathf.PI * 2 / 5;
                    var offsetA = new Vector3(Mathf.Cos(a) * size, 0, Mathf.Sin(a) * size);
                    var offsetB = new Vector3(Mathf.Cos(b) * size, 0, Mathf.Sin(b) * size);
                    // Follow the cap's slope so freckles stay on its faceted surface.
                    float slope = radius > .14f ? -.66f : -.25f;
                    offsetA.y = Vector2.Dot(new Vector2(offsetA.x, offsetA.z), new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))) * slope;
                    offsetB.y = Vector2.Dot(new Vector2(offsetB.x, offsetB.z), new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))) * slope;
                    Triangle(vertices, triangles, center, center + offsetB, center + offsetA);
                }
            }
            return MeshFrom("Cap freckles", vertices, triangles);
        }

        static Mesh GillMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int side = 0; side < 10; side++)
            {
                float angle = side * Mathf.PI * 2 / 10;
                var inner = new Vector3(Mathf.Cos(angle) * .09f, .58f, Mathf.Sin(angle) * .09f);
                var outer = new Vector3(Mathf.Cos(angle) * .31f, .585f, Mathf.Sin(angle) * .31f);
                var width = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * .008f;
                Triangle(vertices, triangles, inner - width, outer + width, outer - width);
                Triangle(vertices, triangles, inner - width, inner + width, outer + width);
            }
            return MeshFrom("Ten radial gills", vertices, triangles);
        }

        static void Triangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            int index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
        }

        static Mesh MeshFrom(string title, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = title };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var material in materials) Release(material);
            Release(stem); Release(cap); Release(spots); Release(gills);
        }

        static void Release(UnityEngine.Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }
    }
}
