using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>Blackpine's nonblocking winter dressing. All positions at this boundary use Unity X.</summary>
    public sealed class DeadAirWinterWorld : MonoBehaviour
    {
        static DeadAirWinterWorld current;
        const float Cell = 1.25f;
        const int MaxFlakes = 640;
        WorldData world;
        DeadAirSession session;
        Transform office, visuals;
        Camera eye;
        Material snowMaterial, wetMaterial, flakeMaterial;
        Texture2D grain, flakeTexture;
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<RoomData> covered = new List<RoomData>();
        readonly List<Bounds> shelterBounds = new List<Bounds>();
        readonly List<Bounds> canopyRoofs = new List<Bounds>();
        ParticleSystem snowfall;
        ParticleSystem.Particle[] particles;
        Vector3[] positions;
        float[] seeds, skyHeights;
        float gridMinX, gridMinZ;
        int gridWidth, gridDepth, flakeCount;
        bool winterActive, built, capturedAtmosphere;
        Color originalFogColor;
        float originalFogDensity, nextAtmosphere;
        bool originalFog;
        FogMode originalFogMode;
        System.Random random;

        public bool WinterActive => winterActive;
        public int ParticleBudget => flakeCount;
        public int GroundQuadCount { get; private set; }
        public int SurfaceTriangleCount { get; private set; }
        public int CaretakerCanopyCount => canopyRoofs.Count;

        public void Initialize(WorldData data, DeadAirSession game)
        {
            if (built) return;
            world = data;
            session = game;
            var station = GameObject.Find("BLACKPINE / original web scene");
            if (world == null || station == null) return;
            office = station.transform;
            foreach (var room in world.rooms)
                if (room.floorY >= 0 && DeadAirOfficeLayout.IsInterior(room)) covered.Add(room);
            if (world.props != null)
                foreach (var prop in world.props)
                    if (prop.type == "generatorShelter")
                        shelterBounds.Add(new Bounds(new Vector3(-prop.x, 0, prop.z), new Vector3(prop.w, 8, prop.d)));
            visuals = new GameObject("BLACKPINE winter / nonblocking snow and weather").transform;
            visuals.SetParent(office, false);
            random = new System.Random(961401);
            CreateMaterials();
            RegisterCaretakerCanopies();
            Physics.SyncTransforms();
            BuildGround();
            BuildExposedCaps();
            BuildFenceDrifts();
            BuildEntryMarks();
            CacheSkyHeights();
            CreateSnowfall();
            visuals.gameObject.SetActive(false);
            built = true;
            current = this;
        }

        /// <summary>Cheap footstep query: Unity world X, source-compatible Z, player floor Y.</summary>
        public static bool IsSnowGround(WorldData data, float worldX, float z, float floorY)
        {
            if (data?.courtyard == null || floorY < -.15f || floorY > .5f) return false;
            float x = -worldX;
            var yard = data.courtyard;
            if (x < yard.minX || x > yard.maxX || z < yard.minZ || z > yard.maxZ) return false;
            if (current != null && ReferenceEquals(current.world, data) && current.Sheltered(worldX, z)) return false;
            foreach (var room in data.rooms)
                if (room.floorY >= 0 && DeadAirOfficeLayout.IsInterior(room) &&
                    x >= room.minX - .16f && x <= room.maxX + .16f &&
                    z >= room.minZ - .16f && z <= room.maxZ + .16f) return false;
            if (data.props != null)
                foreach (var prop in data.props)
                    if (prop.type == "generatorShelter" && Mathf.Abs(x - prop.x) < prop.w * .5f &&
                        Mathf.Abs(z - prop.z) < prop.d * .5f) return false;
            return true;
        }

        bool IndoorsXZ(float x, float z, float margin = .16f)
        {
            x = -x;
            for (int i = 0; i < covered.Count; i++)
            {
                var room = covered[i];
                if (x >= room.minX - margin && x <= room.maxX + margin &&
                    z >= room.minZ - margin && z <= room.maxZ + margin) return true;
            }
            return false;
        }

        bool Sheltered(float x, float z)
        {
            foreach (var bounds in shelterBounds)
                if (x > bounds.min.x && x < bounds.max.x && z > bounds.min.z && z < bounds.max.z) return true;
            return false;
        }

        void RegisterCaretakerCanopies()
        {
            // The farmyard starts inactive and its scenery intentionally has no colliders.
            // Read its two authored snow-covered roofs in local geometry space, so even
            // inactive renderers supply accurate roof bounds without a scene-wide scan.
            var farmyard = FindFirstObjectByType<DeadAirFarmyard>();
            if (farmyard == null) return;
            foreach (var filter in farmyard.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name != "Settled snow on shelter" && filter.name != "Snow on coop") continue;
                if (filter.sharedMesh == null) continue;
                var local = filter.sharedMesh.bounds;
                var matrix = filter.transform.localToWorldMatrix;
                var a = matrix.MultiplyVector(new Vector3(local.extents.x, 0, 0));
                var b = matrix.MultiplyVector(new Vector3(0, local.extents.y, 0));
                var c = matrix.MultiplyVector(new Vector3(0, 0, local.extents.z));
                var extents = new Vector3(Mathf.Abs(a.x) + Mathf.Abs(b.x) + Mathf.Abs(c.x),
                    Mathf.Abs(a.y) + Mathf.Abs(b.y) + Mathf.Abs(c.y), Mathf.Abs(a.z) + Mathf.Abs(b.z) + Mathf.Abs(c.z));
                var roof = new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2);
                canopyRoofs.Add(roof);
                shelterBounds.Add(new Bounds(new Vector3(roof.center.x, 0, roof.center.z), new Vector3(roof.size.x, 8, roof.size.z)));
            }
        }

        void CreateMaterials()
        {
            var shader = Resources.Load<Shader>("Shaders/DeadAirWinterSnow") ?? Shader.Find("Dead Air/Winter Snow");
            if (shader == null) shader = Shader.Find("Standard");
            snowMaterial = new Material(shader) { name = "Blackpine / granular blue-white snow", hideFlags = HideFlags.DontSave };
            snowMaterial.SetColor("_Color", new Color(.82f, .88f, .94f));
            snowMaterial.SetFloat("_Glossiness", .09f);
            snowMaterial.SetFloat("_Metallic", 0);
            DeadAirArtMaterials.Apply(snowMaterial, "snow");
            foreach (var renderer in office.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsScannedFoliage(renderer.transform)) continue;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetFloat("_SnowCover", .82f);
                block.SetTexture("_SnowTex", snowMaterial.mainTexture);
                renderer.SetPropertyBlock(block);
            }
            wetMaterial = new Material(shader) { name = "Blackpine / wet boot marks", hideFlags = HideFlags.DontSave };
            wetMaterial.SetColor("_Color", new Color(.115f, .14f, .16f));
            wetMaterial.SetFloat("_Glossiness", .62f);
            wetMaterial.SetFloat("_Metallic", 0);
            DeadAirArtMaterials.Apply(wetMaterial, "snow-asphalt");
            var particleShader = Resources.Load<Shader>("Shaders/DeadAirWinterFlake") ?? Shader.Find("Dead Air/Winter Flake");
            if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
            flakeMaterial = new Material(particleShader) { name = "Blackpine / soft snowflakes", hideFlags = HideFlags.DontSave };
            flakeTexture = new Texture2D(32, 32, TextureFormat.RGBA32, true) { name = "Winter / soft snowflake", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var soft = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    byte alpha = (byte)(Mathf.Pow(Mathf.Clamp01(1 - radius), 1.4f) * 255);
                    soft[y * 32 + x] = new Color32(218, 233, 246, alpha);
                }
            flakeTexture.SetPixels32(soft);
            flakeTexture.Apply(true, true);
            flakeMaterial.mainTexture = flakeTexture;
        }

        void BuildGround()
        {
            var yard = world.courtyard;
            var xs = new SortedSet<float>();
            var zs = new SortedSet<float>();
            float minX = -yard.maxX - 8, maxX = -yard.minX + 8, minZ = yard.minZ - 8, maxZ = yard.maxZ + 8;
            AddAxis(xs, minX, maxX);
            AddAxis(zs, minZ, maxZ);
            // Add the actual wall edges to the grid; rectangular room unions retain their exact silhouette.
            foreach (var room in covered)
            {
                xs.Add(-room.maxX - .16f); xs.Add(-room.minX + .16f);
                zs.Add(room.minZ - .16f); zs.Add(room.maxZ + .16f);
            }
            foreach (var bounds in shelterBounds)
            {
                xs.Add(bounds.min.x); xs.Add(bounds.max.x);
                zs.Add(bounds.min.z); zs.Add(bounds.max.z);
            }
            var xValues = new List<float>(xs);
            var zValues = new List<float>(zs);
            var mesh = new SnowMesh();
            for (int xi = 0; xi < xValues.Count - 1; xi++)
                for (int zi = 0; zi < zValues.Count - 1; zi++)
                {
                    float x0 = xValues[xi], x1 = xValues[xi + 1], z0 = zValues[zi], z1 = zValues[zi + 1];
                    float x = (x0 + x1) * .5f, z = (z0 + z1) * .5f;
                    if (IndoorsXZ(x, z) || Sheltered(x, z)) continue;
                    mesh.Quad(GroundVertex(x0, z0), GroundVertex(x0, z1), GroundVertex(x1, z1), GroundVertex(x1, z0),
                        GroundTint(x, z));
                    GroundQuadCount++;
                }
            Publish("Snow cover / exact office footprint", mesh, snowMaterial);
            // Match the existing distant terrain skirt rather than ending the white yard at its fence.
            var skirt = new SnowMesh();
            float sx0 = minX, sx1 = maxX, sz0 = minZ, sz1 = maxZ;
            skirt.Rect(-220, sx0, -260, 220, -.012f, new Color(.66f, .73f, .80f));
            skirt.Rect(sx1, 220, -260, 220, -.012f, new Color(.66f, .73f, .80f));
            skirt.Rect(sx0, sx1, -260, sz0, -.012f, new Color(.66f, .73f, .80f));
            skirt.Rect(sx0, sx1, sz1, 220, -.012f, new Color(.66f, .73f, .80f));
            Publish("Snow beyond fence / terrain skirt", skirt, snowMaterial, false);
        }

        static void AddAxis(SortedSet<float> values, float min, float max)
        {
            values.Add(min);
            for (float at = min + 1.7f; at < max; at += 1.7f) values.Add(at);
            values.Add(max);
        }

        static Vector3 GroundVertex(float x, float z) => new Vector3(x, SnowY(x, z), z);
        static float SnowY(float x, float z) => .038f + Mathf.PerlinNoise(x * .24f + 50, z * .24f + 130) * .023f;

        static Color GroundTint(float x, float z)
        {
            float noise = Mathf.PerlinNoise(x * .19f + 70, z * .19f + 20);
            float path = SegmentDistance(new Vector2(x, z), new Vector2(0, 7), new Vector2(10, 24));
            float worn = Mathf.Clamp01(1 - path / 1.15f) * (.18f + noise * .12f);
            float shade = .82f + noise * .17f - worn;
            return new Color(shade * .94f, shade * .98f, shade);
        }

        static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            var line = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, line) / line.sqrMagnitude);
            return Vector2.Distance(point, start + line * t);
        }

        void BuildExposedCaps()
        {
            var cap = new SnowMesh();
            var station = office.Find("station");
            if (station != null)
                foreach (var box in station.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (box.name == "Roof slab section")
                    {
                        var b = box.bounds;
                        cap.BevelCap(b.min.x, b.max.x, b.min.z, b.max.z, b.max.y, .07f);
                    }
                    else if (box.name == "Facade sill") AddWindowSill(cap, box.bounds);
                }
            var exterior = office.Find("exterior");
            if (exterior != null)
                foreach (var filter in exterior.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    var mesh = filter.sharedMesh;
                    if (renderer == null || !renderer.enabled || mesh == null || !mesh.isReadable) continue;
                    if (IsScannedFoliage(filter.transform)) continue;
                    var lod = filter.GetComponentInParent<LODGroup>();
                    if (lod != null)
                    {
                        bool primary = false;
                        var levels = lod.GetLODs();
                        if (levels.Length > 0)
                            foreach (var entry in levels[0].renderers) if (entry == renderer) { primary = true; break; }
                        if (!primary) continue;
                    }
                    var bounds = renderer.bounds;
                    if (bounds.max.y < .18f || bounds.min.y > 10 || bounds.size.x > 18 || bounds.size.z > 18) continue;
                    if (bounds.center.x < -31 || bounds.center.x > 31 || bounds.center.z < -77 || bounds.center.z > 39) continue;
                    if (IsDynamic(filter.transform)) continue;
                    var vertices = mesh.vertices;
                    var triangles = mesh.triangles;
                    var matrix = filter.transform.localToWorldMatrix;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                        var b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                        var c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                        var normal = Vector3.Cross(b - a, c - a).normalized;
                        var middle = (a + b + c) / 3;
                        if (normal.y < .68f || middle.y < .18f || IndoorsXZ(middle.x, middle.z, 0)) continue;
                        // All caps rest on original upward faces and reject a roof above them.
                        if (Physics.Raycast(middle + Vector3.up * .08f, Vector3.up, 30, ~0, QueryTriggerInteraction.Ignore)) continue;
                        var lift = Vector3.up * .027f;
                        cap.Triangle(a + lift, b + lift, c + lift, new Color(.91f, .95f, 1));
                        SurfaceTriangleCount++;
                    }
                }
            Publish("Exposed snow / roof, sills and yard props", cap, snowMaterial);
        }

        bool IsDynamic(Transform part)
        {
            for (var parent = part; parent != null && parent != office; parent = parent.parent)
                if (parent.name.StartsWith("door:", StringComparison.Ordinal) || parent.name.StartsWith("interactive:", StringComparison.Ordinal) ||
                    parent.name.StartsWith("state:", StringComparison.Ordinal) || parent.name.StartsWith("prop:van-", StringComparison.Ordinal) ||
                    parent.name.StartsWith("vehicle:crew-van", StringComparison.Ordinal) || parent.name.StartsWith("Broadcast service van", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool IsScannedFoliage(Transform part)
        {
            for (var parent = part; parent != null; parent = parent.parent)
                if (parent.name == "Scanned pine-tree" || parent.name == "Scanned fir-tree" || parent.name == "Scanned shrub") return true;
            return false;
        }

        void AddWindowSill(SnowMesh mesh, Bounds bounds)
        {
            // Only the outward half of a structural sill receives windblown snow.
            // Door apertures have no sill collider, so this cannot create a strip through a door.
            const float offset = .24f;
            float y = bounds.max.y + .018f;
            if (bounds.size.x > bounds.size.z)
            {
                float side = !IndoorsXZ(bounds.center.x, bounds.center.z + offset, 0) ? 1 : -1;
                float z = bounds.center.z + side * bounds.size.z * .35f;
                if (!IndoorsXZ(bounds.center.x, z + side * offset, 0))
                    mesh.BevelCap(bounds.min.x + .025f, bounds.max.x - .025f, z - .085f, z + .085f, y, .025f);
            }
            else
            {
                float side = !IndoorsXZ(bounds.center.x + offset, bounds.center.z, 0) ? 1 : -1;
                float x = bounds.center.x + side * bounds.size.x * .35f;
                if (!IndoorsXZ(x + side * offset, bounds.center.z, 0))
                    mesh.BevelCap(x - .085f, x + .085f, bounds.min.z + .025f, bounds.max.z - .025f, y, .025f);
            }
        }

        void BuildFenceDrifts()
        {
            var drift = new SnowMesh();
            var yard = world.courtyard;
            float left = -yard.maxX + .6f, right = -yard.minX - .6f;
            for (float z = yard.minZ + 1; z < yard.maxZ - 1; z += 4.4f)
            {
                drift.Mound(new Vector3(left, .02f, z), .72f, 3.4f, .20f + Hash(z) * .2f);
                drift.Mound(new Vector3(right, .02f, z + 1), .85f, 3.6f, .23f + Hash(z + 10) * .25f);
            }
            for (float x = left + 2; x < right - 1; x += 4.3f)
            {
                drift.Mound(new Vector3(x, .02f, yard.minZ + .7f), 3.6f, .9f, .35f + Hash(x) * .18f);
                // Preserve the service gate and the van's approach.
                if (Mathf.Abs(x - 10) > 4)
                    drift.Mound(new Vector3(x, .02f, yard.maxZ - .7f), 3.5f, .7f, .25f + Hash(x + 40) * .2f);
            }
            Publish("Wind-shaped snowdrifts / fence edges", drift, snowMaterial);
        }

        static float Hash(float number) => Mathf.Repeat(Mathf.Sin(number * 12.9898f + 78.233f) * 43758.5453f, 1);

        void BuildEntryMarks()
        {
            var tracks = new SnowMesh();
            var dirt = new Color(.28f, .33f, .37f);
            for (float z = 20; z < 37; z += .55f)
                foreach (float x in new[] { 8.87f, 11.13f })
                {
                    float curve = Mathf.Max(0, z - 30) * .035f;
                    tracks.Rect(x + curve - .13f, x + curve + .13f, z, z + .48f, SnowY(x, z) + .004f, dirt);
                }
            for (int i = 0; i < 25; i++)
            {
                float z = 7 + i * .6f, x = (z - 7) * .56f + (i % 2 == 0 ? -.14f : .14f);
                tracks.Sole(new Vector3(x, SnowY(x, z) + .004f, z), -28, .12f, .28f, new Color(.43f, .48f, .52f));
            }
            Publish("Compressed tire and boot tracks", tracks, snowMaterial);
            var wet = new SnowMesh();
            var slush = new SnowMesh();
            for (int i = 0; i < 11; i++)
            {
                float z = 6.6f - i * .55f, x = (i % 2 == 0 ? -.18f : .18f) + Mathf.Sin(i * .8f) * .08f;
                float y = z > 6.2f ? SnowY(x, z) + .006f : .024f;
                wet.Sole(new Vector3(x, y, z), i % 2 == 0 ? -8 : 8, .11f, .27f, Color.white);
                if (i < 5)
                    for (int fleck = 0; fleck < 3; fleck++)
                    {
                        float dx = x + (float)random.NextDouble() * .4f - .2f;
                        float dz = z + (float)random.NextDouble() * .34f - .17f;
                        float radius = .018f + (float)random.NextDouble() * .025f;
                        slush.Rect(dx - radius, dx + radius, dz - radius, dz + radius, y + .002f, new Color(.71f, .78f, .83f));
                    }
            }
            Publish("Melted snow / wet boots at main entrance", wet, wetMaterial);
            Publish("Tracked-in slush / entrance only", slush, snowMaterial);
        }

        void CacheSkyHeights()
        {
            gridMinX = -world.courtyard.maxX - 10;
            gridMinZ = world.courtyard.minZ - 10;
            gridWidth = Mathf.CeilToInt((world.courtyard.maxX - world.courtyard.minX + 20) / Cell) + 1;
            gridDepth = Mathf.CeilToInt((world.courtyard.maxZ - world.courtyard.minZ + 20) / Cell) + 1;
            skyHeights = new float[gridWidth * gridDepth];
            for (int zi = 0; zi < gridDepth; zi++)
                for (int xi = 0; xi < gridWidth; xi++)
                {
                    float x = gridMinX + xi * Cell, z = gridMinZ + zi * Cell;
                    float height = .08f;
                    if (Physics.Raycast(new Vector3(x, 25, z), Vector3.down, out var hit, 26, ~0, QueryTriggerInteraction.Ignore))
                        height = Mathf.Max(height, hit.point.y + .12f);
                    if (IndoorsXZ(x, z, .35f)) height = Mathf.Max(height, 8.65f);
                    foreach (var roof in canopyRoofs)
                        if (x >= roof.min.x - .15f && x <= roof.max.x + .15f && z >= roof.min.z - .15f && z <= roof.max.z + .15f)
                            height = Mathf.Max(height, roof.max.y + .12f);
                    skyHeights[zi * gridWidth + xi] = height;
                }
        }

        float SkyHeight(float x, float z)
        {
            int xi = Mathf.Clamp(Mathf.FloorToInt((x - gridMinX) / Cell), 0, gridWidth - 1);
            int zi = Mathf.Clamp(Mathf.FloorToInt((z - gridMinZ) / Cell), 0, gridDepth - 1);
            int nextX = Mathf.Min(xi + 1, gridWidth - 1), nextZ = Mathf.Min(zi + 1, gridDepth - 1);
            // Use the highest corner, not a nearest-cell or interpolated height. A roof
            // boundary cannot average down and leak snow into a room beside a window.
            float height = Mathf.Max(Mathf.Max(skyHeights[zi * gridWidth + xi], skyHeights[zi * gridWidth + nextX]),
                Mathf.Max(skyHeights[nextZ * gridWidth + xi], skyHeights[nextZ * gridWidth + nextX]));
            // The small coop can be narrower than a grid cell. Its analytic footprint
            // keeps individual flakes out even if every grid sample missed the roof.
            foreach (var roof in canopyRoofs)
                if (x >= roof.min.x - .15f && x <= roof.max.x + .15f && z >= roof.min.z - .15f && z <= roof.max.z + .15f)
                    height = Mathf.Max(height, roof.max.y + .12f);
            return height;
        }

        void CreateSnowfall()
        {
            var snowObject = new GameObject("Snowfall / roof-aware fixed particle budget");
            snowObject.transform.SetParent(visuals, false);
            snowfall = snowObject.AddComponent<ParticleSystem>();
            var main = snowfall.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxFlakes;
            main.startSpeed = 0;
            main.startLifetime = 1000;
            main.simulationSpeed = 0;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = snowfall.emission; emission.enabled = false;
            var shape = snowfall.shape; shape.enabled = false;
            var renderer = snowfall.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = flakeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .035f;
            particles = new ParticleSystem.Particle[MaxFlakes];
            positions = new Vector3[MaxFlakes];
            seeds = new float[MaxFlakes];
            for (int i = 0; i < MaxFlakes; i++) seeds[i] = (float)random.NextDouble();
        }

        void LateUpdate()
        {
            if (!built) return;
            var state = session?.State;
            bool active = state != null && !DeadAirChapters.IsChapter2(state.chapterId) &&
                (state.phase == "playing" || state.phase == "departing");
            if (winterActive != active)
            {
                winterActive = active;
                visuals.gameObject.SetActive(active);
                if (active)
                {
                    if (!capturedAtmosphere)
                    {
                        originalFog = RenderSettings.fog; originalFogMode = RenderSettings.fogMode;
                        originalFogColor = RenderSettings.fogColor; originalFogDensity = RenderSettings.fogDensity;
                        capturedAtmosphere = true;
                    }
                    eye = Camera.main;
                    flakeCount = DeadAirSettings.GraphicsQuality == 0 ? 320 : DeadAirSettings.GraphicsQuality == 1 ? 480 : MaxFlakes;
                    for (int i = 0; i < MaxFlakes; i++) SpawnFlake(i, true);
                    snowfall.Play();
                    nextAtmosphere = 0;
                }
                else if (state == null || !DeadAirChapters.IsChapter2(state.chapterId)) RestoreAtmosphere();
            }
            if (!active) return;
            if (eye == null) eye = Camera.main;
            if (eye == null) return;
            if (Time.unscaledTime >= nextAtmosphere)
            {
                nextAtmosphere = Time.unscaledTime + .3f;
                bool inside = IndoorsXZ(eye.transform.position.x, eye.transform.position.z, 0) && eye.transform.position.y < 8.5f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = inside ? new Color(.063f, .079f, .105f) : new Color(.085f, .105f, .135f);
                RenderSettings.fogDensity = inside ? .008f : .0105f;
            }
            float dt = state.paused ? 0 : Mathf.Min(Time.deltaTime, .05f);
            float time = Time.time;
            Vector3 camera = eye.transform.position;
            for (int i = 0; i < flakeCount; i++)
            {
                var position = positions[i];
                float seed = seeds[i];
                bool blowing = i % 9 == 0;
                position.x += dt * (blowing ? 1.8f : .28f + Mathf.Sin(time * .7f + seed * 20) * .35f);
                position.z += dt * (.18f + Mathf.Cos(time * .45f + seed * 30) * .18f);
                position.y -= dt * (blowing ? .24f : .42f + seed * .45f);
                // Check each moved flake against the cached roof height. Nothing falls into a room or through a canopy.
                if (position.y < SkyHeight(position.x, position.z) || Mathf.Abs(position.x - camera.x) > 18 ||
                    Mathf.Abs(position.z - camera.z) > 18 || position.y > camera.y + 11)
                { SpawnFlake(i, false); position = positions[i]; }
                else positions[i] = position;
                particles[i].position = position;
                particles[i].startSize = blowing ? .023f : .022f + seed * .039f;
                particles[i].startColor = new Color32(207, 224, 240, (byte)(blowing ? 100 : 135 + seed * 70));
                particles[i].startLifetime = 1000;
                particles[i].remainingLifetime = 1000;
            }
            snowfall.SetParticles(particles, flakeCount);
        }

        void SpawnFlake(int index, bool fill)
        {
            Vector3 center = eye != null ? eye.transform.position : new Vector3(-world.spawn.x, 1.7f, world.spawn.z);
            float x = center.x + (float)random.NextDouble() * 34 - 17;
            float z = center.z + (float)random.NextDouble() * 34 - 17;
            float minimum = SkyHeight(x, z) + .2f;
            float maximum = Mathf.Max(center.y + 10, minimum + .25f);
            float y = fill ? Mathf.Lerp(minimum, maximum, (float)random.NextDouble()) : maximum - (float)random.NextDouble() * 2;
            if (index % 9 == 0 && minimum < 1) y = minimum + .15f + (float)random.NextDouble() * .55f;
            positions[index] = new Vector3(x, Mathf.Max(minimum, y), z);
        }

        void RestoreAtmosphere()
        {
            if (!capturedAtmosphere) return;
            RenderSettings.fog = originalFog;
            RenderSettings.fogMode = originalFogMode;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogDensity = originalFogDensity;
        }

        void Publish(string name, SnowMesh geometry, Material material, bool shadows = true)
        {
            if (geometry.vertices.Count == 0) return;
            var mesh = geometry.Build(name);
            ownedMeshes.Add(mesh);
            var item = new GameObject(name);
            item.transform.SetParent(visuals, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void OnDestroy()
        {
            if (current == this) current = null;
            if (winterActive && (session?.State == null || !DeadAirChapters.IsChapter2(session.State.chapterId))) RestoreAtmosphere();
            if (visuals != null) DestroyOwned(visuals.gameObject);
            foreach (var mesh in ownedMeshes) DestroyOwned(mesh);
            DestroyOwned(snowMaterial); DestroyOwned(wetMaterial); DestroyOwned(flakeMaterial);
            DestroyOwned(grain); DestroyOwned(flakeTexture);
        }

        static void DestroyOwned(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }

        sealed class SnowMesh
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            void Vertex(Vector3 point, Color tint)
            {
                vertices.Add(point); uvs.Add(new Vector2(point.x, point.z) * .75f); colors.Add(tint);
            }
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                int start = vertices.Count;
                Vertex(a, color); Vertex(b, color); Vertex(c, color);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int start = vertices.Count;
                Vertex(a, color); Vertex(b, color); Vertex(c, color); Vertex(d, color);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            public void Rect(float x0, float x1, float z0, float z1, float y, Color color)
            { Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), color); }
            public void BevelCap(float x0, float x1, float z0, float z1, float y, float depth)
            {
                float inset = Mathf.Min(.11f, (x1 - x0) * .16f, (z1 - z0) * .16f);
                var a = new Vector3(x0, y + .012f, z0); var b = new Vector3(x0, y + .012f, z1);
                var c = new Vector3(x1, y + .012f, z1); var d = new Vector3(x1, y + .012f, z0);
                var aa = a + new Vector3(inset, depth, inset); var bb = b + new Vector3(inset, depth, -inset);
                var cc = c + new Vector3(-inset, depth, -inset); var dd = d + new Vector3(-inset, depth, inset);
                Quad(aa, bb, cc, dd, Color.white);
                Quad(a, b, bb, aa, Color.white); Quad(b, c, cc, bb, Color.white);
                Quad(c, d, dd, cc, Color.white); Quad(d, a, aa, dd, Color.white);
            }
            public void Mound(Vector3 center, float width, float depth, float height)
            {
                const int segments = 10;
                var top = center + Vector3.up * height;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    var va = center + new Vector3(Mathf.Cos(a) * width * .5f, 0, Mathf.Sin(a) * depth * .5f);
                    var vb = center + new Vector3(Mathf.Cos(b) * width * .5f, 0, Mathf.Sin(b) * depth * .5f);
                    var midA = Vector3.Lerp(top, va, .55f) + Vector3.up * height * .18f;
                    var midB = Vector3.Lerp(top, vb, .55f) + Vector3.up * height * .18f;
                    Triangle(top, midB, midA, Color.white);
                    Quad(midA, midB, vb, va, Color.white);
                }
            }
            public void Sole(Vector3 center, float yaw, float halfWidth, float halfLength, Color tint)
            {
                var rotation = Quaternion.Euler(0, yaw, 0);
                const int segments = 10;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    var va = rotation * new Vector3(Mathf.Cos(a) * halfWidth, 0, Mathf.Sin(a) * halfLength);
                    var vb = rotation * new Vector3(Mathf.Cos(b) * halfWidth, 0, Mathf.Sin(b) * halfLength);
                    Triangle(center, center + vb, center + va, tint);
                }
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uvs); mesh.SetColors(colors);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
