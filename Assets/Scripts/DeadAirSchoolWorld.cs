using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>
    /// Procedural 3D Japanese School, Courtyard, and Traditional Park generator for Chapter 2.
    /// Replaces the Chapter 1 office with a massive authentic Japanese high school and shrine park.
    /// </summary>
    public sealed class DeadAirSchoolWorld
    {
        public readonly Transform Root;
        readonly WorldData data;
        readonly List<GameObject> createdObjects = new List<GameObject>();
        readonly List<FlickeringLamp> lamps = new List<FlickeringLamp>();
        readonly List<PillItem> pills = new List<PillItem>();
        readonly Material matPlaster, matAsphalt, matWood, matBark, matFoliage, matTorii, matStone, matGlass, matDarkMetal, matConcrete, matChalkboard, matWhite;

        sealed class FlickeringLamp
        {
            public Light light;
            public Renderer bulb;
            public float baseIntensity;
            public bool worksNormally;
            public float flickerOffset;
        }

        public sealed class PillItem
        {
            public string id;
            public GameObject go;
            public Vector3 position;
            public Light light;
            public bool collected;
        }

        public IReadOnlyList<PillItem> Pills => pills;

        public DeadAirSchoolWorld(Transform parent, WorldData data)
        {
            this.data = data;
            var go = new GameObject("CHAPTER 2 / JAPANESE SCHOOL & PARK");
            go.transform.SetParent(parent, false);
            Root = go.transform;

            var litShader = Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard");

            matPlaster = CreateMaterial("SchoolPlaster", litShader, new Color(0.85f, 0.82f, 0.72f), 0.15f);
            matAsphalt = CreateMaterial("WetAsphalt", litShader, new Color(0.18f, 0.20f, 0.19f), 0.35f);
            matWood = CreateMaterial("ClassroomWood", litShader, new Color(0.48f, 0.36f, 0.22f), 0.25f);
            matBark = CreateMaterial("AncientBark", litShader, new Color(0.25f, 0.20f, 0.14f), 0.05f);
            matFoliage = CreateMaterial("ShrineFoliage", litShader, new Color(0.14f, 0.26f, 0.15f), 0.1f);
            matTorii = CreateMaterial("ToriiVermilion", litShader, new Color(0.82f, 0.18f, 0.12f), 0.4f);
            matStone = CreateMaterial("ToroStone", litShader, new Color(0.45f, 0.46f, 0.44f), 0.15f);
            matGlass = CreateMaterial("DarkWindowGlass", litShader, new Color(0.12f, 0.18f, 0.22f), 0.9f);
            matDarkMetal = CreateMaterial("FenceMetal", litShader, new Color(0.18f, 0.20f, 0.18f), 0.6f);
            matConcrete = CreateMaterial("ConcretePorch", litShader, new Color(0.58f, 0.58f, 0.54f), 0.2f);
            matChalkboard = CreateMaterial("DarkGreenChalkboard", litShader, new Color(0.12f, 0.22f, 0.15f), 0.1f);
            matWhite = CreateMaterial("WhiteTrim", litShader, new Color(0.92f, 0.90f, 0.86f), 0.3f);
        }

        static Material CreateMaterial(string name, Shader shader, Color color, float smoothness)
        {
            var mat = new Material(shader) { name = name, color = color, hideFlags = HideFlags.DontSave };
            mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }

        public void Initialize()
        {
            BuildSchoolCourtyard();
            BuildSchoolBuilding();
            BuildSchoolInteriors();
            BuildTraditionalPark();
            BuildNewsVan();
            SpawnSanityPills();
            SetupLighting();
        }

        public void SetActive(bool active)
        {
            if (Root != null) Root.gameObject.SetActive(active);
        }

        GameObject CreateBox(Transform parent, Vector3 localPos, Vector3 size, Material mat, bool addCollider = true, string name = "Cube")
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            if (!addCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
            }
            createdObjects.Add(go);
            return go;
        }

        GameObject CreateCylinder(Transform parent, Vector3 localPos, float radius, float height, Material mat, bool addCollider = true, string name = "Cylinder")
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(radius * 2, height * 0.5f, radius * 2);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            if (!addCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
            }
            createdObjects.Add(go);
            return go;
        }

        void SetupLighting()
        {
            // Ominous Japanese night sky lighting
            var moon = new GameObject("Japanese Night Moonlight");
            moon.transform.SetParent(Root, false);
            var light = moon.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.45f, 0.55f, 0.68f);
            light.intensity = 0.55f;
            light.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(50, -35, 0);

            // Ground fog & mist atmosphere
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.06f, 0.08f, 0.12f);
            RenderSettings.fogDensity = 0.012f;
        }

        // ====================================================================
        // 1. SCHOOL COURTYARD (Photo 1)
        // ====================================================================
        void BuildSchoolCourtyard()
        {
            var yard = new GameObject("School Courtyard").transform;
            yard.SetParent(Root, false);

            // Main asphalt school ground (Z: 0 to 38, X: -35 to 35)
            CreateBox(yard, new Vector3(0, -0.1f, 19f), new Vector3(72f, 0.2f, 38f), matAsphalt, true, "Courtyard Asphalt");

            // Connecting street / road outside gate (Z: 38 to 44, X: -40 to 40)
            CreateBox(yard, new Vector3(0, -0.1f, 41f), new Vector3(80f, 0.2f, 6f), matAsphalt, true, "Street Road");

            // Painted Colored Circles on Courtyard (Photo 1)
            var yellowMat = CreateMaterial("PaintYellow", matPlaster.shader, new Color(0.88f, 0.76f, 0.22f), 0.2f);
            var blueMat = CreateMaterial("PaintBlue", matPlaster.shader, new Color(0.24f, 0.58f, 0.85f), 0.2f);
            var redMat = CreateMaterial("PaintRed", matPlaster.shader, new Color(0.85f, 0.28f, 0.24f), 0.2f);
            var greenMat = CreateMaterial("PaintGreen", matPlaster.shader, new Color(0.28f, 0.78f, 0.38f), 0.2f);

            CreateRing(yard, new Vector3(3f, 0.015f, 14f), 2.6f, yellowMat);  // Big yellow circle
            CreateRing(yard, new Vector3(-6f, 0.015f, 18f), 1.9f, blueMat);   // Blue circle
            CreateRing(yard, new Vector3(8f, 0.015f, 22f), 1.5f, redMat);     // Red circle
            CreateRing(yard, new Vector3(-12f, 0.015f, 12f), 3.2f, yellowMat); // Large yellow ring
            CreateRing(yard, new Vector3(15f, 0.015f, 15f), 1.3f, greenMat);  // Green circle
            CreateRing(yard, new Vector3(-2f, 0.015f, 26f), 2.2f, blueMat);   // Blue circle near gates

            // Traffic cones (Photo 1)
            var coneMat = CreateMaterial("TrafficOrange", matPlaster.shader, new Color(0.92f, 0.32f, 0.12f), 0.3f);
            CreateTrafficCone(yard, new Vector3(14f, 0f, 28f), coneMat);
            CreateTrafficCone(yard, new Vector3(16f, 0f, 26f), coneMat);
            CreateTrafficCone(yard, new Vector3(-8f, 0f, 32f), coneMat);

            // Courtyard Perimeter Concrete Wall & Metal Fence (Z = 38)
            for (int side = -1; side <= 1; side += 2)
            {
                float wallWidth = 28f;
                float centerX = side * (5f + wallWidth * 0.5f);
                // Lower concrete wall
                CreateBox(yard, new Vector3(centerX, 1.0f, 38f), new Vector3(wallWidth, 2.0f, 0.35f), matConcrete, true, "Fence Wall");
                // Upper iron bars
                for (float x = 5.5f; x < 33f; x += 0.8f)
                {
                    CreateCylinder(yard, new Vector3(side * x, 2.4f, 38f), 0.025f, 0.8f, matDarkMetal, false, "Fence Bar");
                }
            }

            // School Gate Pillars at X = -5 and X = 5
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(yard, new Vector3(side * 5f, 1.6f, 38f), new Vector3(0.9f, 3.2f, 0.9f), matConcrete, true, "Gate Pillar");
                CreateBox(yard, new Vector3(side * 5f, 3.25f, 38f), new Vector3(1.1f, 0.2f, 1.1f), matDarkMetal, false, "Gate Cap");
            }

            // School Name Plaque on left gate post: "立花第一中学校 正門"
            var plaque = CreateBox(yard, new Vector3(-5f, 1.8f, 38.48f), new Vector3(0.55f, 1.2f, 0.04f), matDarkMetal, false, "Gate Plaque");
            AddPlaqueText(plaque, "立花第一中学校\n正 門");
        }

        void CreateRing(Transform parent, Vector3 center, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "PaintRing";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = new Vector3(radius * 2, 0.01f, radius * 2);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            createdObjects.Add(go);
        }

        void CreateTrafficCone(Transform parent, Vector3 pos, Material coneMat)
        {
            var g = new GameObject("TrafficCone");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            CreateBox(g.transform, new Vector3(0, 0.02f, 0), new Vector3(0.38f, 0.04f, 0.38f), matDarkMetal, true);
            var cone = CreateCylinder(g.transform, new Vector3(0, 0.36f, 0), 0.12f, 0.72f, coneMat, true);
            // White reflective stripe
            CreateCylinder(cone.transform, new Vector3(0, 0, 0), 0.125f, 0.2f, matWhite, false);
        }

        // ====================================================================
        // 2. THE 3-FLOOR JAPANESE SCHOOL BUILDING (Photo 1)
        // ====================================================================
        void BuildSchoolBuilding()
        {
            var bldg = new GameObject("Japanese School Building").transform;
            bldg.SetParent(Root, false);

            float width = 60f;   // X: -30 to 30
            float height = 11.5f; // 3 floors (~3.8m per floor)
            float depth = 16f;   // Z: -16 to 0

            // Main outer structural facade (Z: -16 to 0)
            CreateBox(bldg, new Vector3(0, height * 0.5f, -depth * 0.5f), new Vector3(width, height, depth), matPlaster, true, "School Main Block");
            // Concrete roof parapet / ledge
            CreateBox(bldg, new Vector3(0, height + 0.35f, -depth * 0.5f), new Vector3(width + 1.2f, 0.7f, depth + 1.2f), matConcrete, true, "Roof Ledge");

            // Central Vertical Glass Stairwell Tower (Photo 1 - extending above roof)
            float towerW = 6.6f, towerH = 14.8f;
            CreateBox(bldg, new Vector3(0, towerH * 0.5f, 0.5f), new Vector3(towerW, towerH, 1.4f), matConcrete, true, "Central Stair Tower");
            for (int f = 0; f < 4; f++)
            {
                CreateBox(bldg, new Vector3(0, 2.5f + f * 3.4f, 1.22f), new Vector3(towerW - 0.8f, 2.4f, 0.06f), matGlass, false, "Tower Glass");
                // Vertical mullions
                for (float mx = -2.2f; mx <= 2.2f; mx += 1.1f)
                {
                    CreateBox(bldg, new Vector3(mx, 2.5f + f * 3.4f, 1.25f), new Vector3(0.08f, 2.4f, 0.08f), matWhite, false, "Tower Mullion");
                }
            }

            // Horizontal Rows of Typical Japanese Classroom Windows (Photo 1)
            for (int floor = 0; floor < 3; floor++)
            {
                float wy = 2.2f + floor * 3.6f;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int w = 0; w < 5; w++)
                    {
                        float wx = side * (6.8f + w * 4.4f);
                        // Window frame & dark glass
                        CreateBox(bldg, new Vector3(wx, wy, 0.06f), new Vector3(3.6f, 2.2f, 0.12f), matWhite, false, "Window Frame");
                        CreateBox(bldg, new Vector3(wx, wy, 0.11f), new Vector3(3.4f, 2.0f, 0.04f), matGlass, false, "Window Glass");
                        // Grid bars
                        CreateBox(bldg, new Vector3(wx, wy, 0.13f), new Vector3(3.4f, 0.05f, 0.06f), matWhite, false, "Window Cross H");
                        for (int div = -1; div <= 1; div++)
                        {
                            CreateBox(bldg, new Vector3(wx + div * 1.05f, wy, 0.13f), new Vector3(0.05f, 2.0f, 0.06f), matWhite, false, "Window Cross V");
                        }
                    }
                }
            }

            // Facade Round Clock on 2nd Floor (Photo 1)
            var clock = CreateCylinder(bldg, new Vector3(14f, 8.2f, 0.18f), 0.9f, 0.14f, matDarkMetal, false, "School Clock");
            clock.transform.rotation = Quaternion.Euler(90, 0, 0);
            var face = CreateCylinder(clock.transform, new Vector3(0, 0.08f, 0), 0.82f, 0.04f, matWhite, false, "Clock Face");
            // Clock hands (showing 02:45)
            CreateBox(clock.transform, new Vector3(0, 0.11f, 0.22f), new Vector3(0.06f, 0.02f, 0.44f), matDarkMetal, false);
            CreateBox(clock.transform, new Vector3(-0.25f, 0.11f, 0), new Vector3(0.52f, 0.02f, 0.06f), matDarkMetal, false);

            // School Emblem / Crest on Facade: "立花第一中学校 / TACHIBANA JUNIOR HIGH"
            var crest = CreateBox(bldg, new Vector3(19f, 9.6f, 0.12f), new Vector3(4.2f, 1.1f, 0.06f), matDarkMetal, false, "School Crest");
            AddPlaqueText(crest, "立花第一中学校\nTACHIBANA JUNIOR HIGH");

            // Entrance Porch / Canopy with Square Pillars (Photo 1)
            float porchW = 12f, porchD = 4f, porchH = 3.6f;
            CreateBox(bldg, new Vector3(10f, porchH + 0.15f, porchD * 0.5f), new Vector3(porchW, 0.35f, porchD), matConcrete, true, "Porch Roof");
            CreateBox(bldg, new Vector3(4.5f, porchH * 0.5f, porchD - 0.3f), new Vector3(0.55f, porchH, 0.55f), matConcrete, true, "Porch Pillar Left");
            CreateBox(bldg, new Vector3(15.5f, porchH * 0.5f, porchD - 0.3f), new Vector3(0.55f, porchH, 0.55f), matConcrete, true, "Porch Pillar Right");
            // Sliding Glass Doors under porch
            CreateBox(bldg, new Vector3(10f, 1.5f, 0.08f), new Vector3(8.5f, 2.8f, 0.14f), matWhite, true, "Porch Sliding Doors");

            // Planter Boxes with Shrubs and Cycad/Palm Trees (Photo 1)
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 4; i++)
                {
                    float px = side * (8f + i * 5.0f);
                    CreateBox(bldg, new Vector3(px, 0.3f, 1.6f), new Vector3(3.8f, 0.6f, 1.0f), matConcrete, true, "Planter Box");
                    CreateBox(bldg, new Vector3(px, 0.8f, 1.6f), new Vector3(3.4f, 0.7f, 0.8f), matFoliage, false, "Green Bush");
                    if (i == 1 || i == 3)
                    {
                        // Cycad / palm tree as seen in Photo 1
                        CreateCylinder(bldg, new Vector3(px + 1.2f, 1.8f, 1.6f), 0.2f, 2.4f, matBark, true, "Palm Trunk");
                        CreateBox(bldg, new Vector3(px + 1.2f, 3.1f, 1.6f), new Vector3(2.2f, 0.6f, 2.2f), matFoliage, false, "Palm Crown");
                    }
                }
            }
        }

        // ====================================================================
        // 3. SCHOOL INTERIORS (Genkan, Classrooms, Nurse Office)
        // ====================================================================
        void BuildSchoolInteriors()
        {
            var inter = new GameObject("School Interiors").transform;
            inter.SetParent(Root, false);

            // Interior Floor (Z: -16 to 0, X: -28 to 28)
            CreateBox(inter, new Vector3(0, 0.05f, -8f), new Vector3(56f, 0.1f, 16f), matWood, true, "Interior Floor");
            CreateBox(inter, new Vector3(0, 3.6f, -8f), new Vector3(56f, 0.1f, 16f), matConcrete, true, "Interior Ceiling");

            // Main Corridor Partition Wall (at Z = -6)
            float[] openings = { -20f, -6f, 6f, 20f };
            float lastX = -28f;
            foreach (var ox in openings)
            {
                float w = (ox - 1.2f) - lastX;
                if (w > 0.5f)
                {
                    CreateBox(inter, new Vector3(lastX + w * 0.5f, 1.8f, -6f), new Vector3(w, 3.6f, 0.25f), matPlaster, true, "Hall Wall");
                }
                lastX = ox + 1.2f;
            }
            if (28f - lastX > 0.5f)
            {
                CreateBox(inter, new Vector3((lastX + 28f) * 0.5f, 1.8f, -6f), new Vector3(28f - lastX, 3.6f, 0.25f), matPlaster, true, "Hall Wall End");
            }

            // Classroom divider walls along Z (-16 to -6)
            float[] dividers = { -14f, 0f, 14f };
            foreach (var dx in dividers)
            {
                CreateBox(inter, new Vector3(dx, 1.8f, -11f), new Vector3(0.25f, 3.6f, 10f), matPlaster, true, "Class Divider");
            }

            // Genkan Shoe Lockers (Gettabako)
            BuildShoeLockers(inter, new Vector3(-3.4f, 0f, -4f));
            BuildShoeLockers(inter, new Vector3(3.4f, 0f, -4f));

            // Classrooms 1-A (X: -28 to -14) & 1-B (X: -14 to 0)
            BuildClassroom(inter, -21f, "1-A");
            BuildClassroom(inter, -7f, "1-B");

            // Nurse's Office (Медпункт / 保健室) at X: 14 to 28
            BuildNurseOffice(inter, 21f);

            // Interior Corridor Lamps (Flickering horror lights)
            for (float hx = -22f; hx <= 22f; hx += 8f)
            {
                CreateInteriorLamp(inter, new Vector3(hx, 3.45f, -3f));
            }
        }

        void BuildShoeLockers(Transform parent, Vector3 pos)
        {
            var g = new GameObject("ShoeLockers");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            CreateBox(g.transform, new Vector3(0, 0.8f, 0), new Vector3(3.8f, 1.6f, 0.6f), matWood, true);
            // Cubbies
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    float cx = -1.6f + col * 0.45f;
                    float cy = 0.25f + row * 0.38f;
                    CreateBox(g.transform, new Vector3(cx, cy, 0.05f), new Vector3(0.4f, 0.32f, 0.52f), matDarkMetal, false);
                }
            }
        }

        void BuildClassroom(Transform parent, float centerX, string className)
        {
            var room = new GameObject("Classroom " + className).transform;
            room.SetParent(parent, false);

            // Large Green Chalkboard on front wall (Z = -15.85)
            var board = CreateBox(room, new Vector3(centerX, 1.8f, -15.85f), new Vector3(4.8f, 1.4f, 0.06f), matChalkboard, true, "Chalkboard");
            AddPlaqueText(board, "【警告】夜間立入禁止\nテケテケ 見テハイケナイ\n(Do not look at Teke-Teke)");

            // Teacher's podium desk
            CreateBox(room, new Vector3(centerX, 0.4f, -14.2f), new Vector3(1.6f, 0.8f, 0.8f), matWood, true, "Teacher Desk");

            // Student desks in 3x3 grid
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float dx = centerX - 2.8f + c * 2.8f;
                    float dz = -12.5f + r * 2.2f;
                    BuildDesk(room, new Vector3(dx, 0f, dz));
                }
            }
        }

        void BuildDesk(Transform parent, Vector3 pos)
        {
            var g = new GameObject("SchoolDesk");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            // Desktop
            CreateBox(g.transform, new Vector3(0, 0.72f, 0), new Vector3(0.75f, 0.04f, 0.5f), matWood, true);
            // Legs
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    CreateCylinder(g.transform, new Vector3(sx * 0.32f, 0.36f, sz * 0.2f), 0.018f, 0.72f, matDarkMetal, false);
                }
            }
            // Attached chair
            CreateBox(g.transform, new Vector3(0, 0.42f, 0.45f), new Vector3(0.42f, 0.035f, 0.4f), matWood, true);
            CreateBox(g.transform, new Vector3(0, 0.78f, 0.62f), new Vector3(0.42f, 0.3f, 0.035f), matWood, false);
        }

        void BuildNurseOffice(Transform parent, float centerX)
        {
            var room = new GameObject("Nurse Office").transform;
            room.SetParent(parent, false);

            // Medical bed
            CreateBox(room, new Vector3(centerX + 3f, 0.45f, -12f), new Vector3(1.2f, 0.4f, 2.2f), matWhite, true, "Hospital Bed");
            CreateBox(room, new Vector3(centerX + 3f, 0.68f, -11.2f), new Vector3(0.8f, 0.14f, 0.45f), matPlaster, false, "Pillow");

            // Medicine Cabinet
            var cab = CreateBox(room, new Vector3(centerX - 3f, 1.4f, -15.6f), new Vector3(1.8f, 2.6f, 0.5f), matDarkMetal, true, "Medicine Cabinet");
            AddPlaqueText(cab, "保健室 / 薬品庫\n鎮静剤・救急箱");
        }

        void CreateInteriorLamp(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Hallway Lamp");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            CreateBox(go.transform, Vector3.zero, new Vector3(1.2f, 0.08f, 0.28f), matWhite, false);
            var bulb = CreateBox(go.transform, new Vector3(0, -0.04f, 0), new Vector3(1.0f, 0.04f, 0.2f), matPlaster, false);

            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.94f, 0.82f);
            l.intensity = 1.6f;
            l.range = 8.5f;

            lamps.Add(new FlickeringLamp
            {
                light = l,
                bulb = bulb.GetComponent<Renderer>(),
                baseIntensity = 1.6f,
                worksNormally = true,
                flickerOffset = UnityEngine.Random.value * 20f
            });
        }

        // ====================================================================
        // 4. TRADITIONAL JAPANESE PARK (Photo 3)
        // ====================================================================
        void BuildTraditionalPark()
        {
            var park = new GameObject("Traditional Japanese Park").transform;
            park.SetParent(Root, false);

            // Park Grass & Moss ground (Z: 44 to 96, X: -40 to 40)
            CreateBox(park, new Vector3(0, -0.1f, 70f), new Vector3(80f, 0.2f, 52f), matFoliage, true, "Park Moss Ground");

            // Winding Dirt Pathways
            CreateBox(park, new Vector3(0, 0.01f, 58f), new Vector3(4.8f, 0.02f, 28f), matWood, false, "Main Path");
            CreateBox(park, new Vector3(-12f, 0.01f, 72f), new Vector3(16f, 0.02f, 3.6f), matWood, false, "Path Left");
            CreateBox(park, new Vector3(12f, 0.01f, 72f), new Vector3(16f, 0.02f, 3.6f), matWood, false, "Path Right");
            CreateBox(park, new Vector3(0, 0.01f, 85f), new Vector3(5.2f, 0.02f, 22f), matWood, false, "Shrine Approach");

            // Ancient Gnarled Japanese Park Trees (Photo 3)
            Vector2[] treeLocations = {
                new Vector2(-14, 52), new Vector2(14, 52),
                new Vector2(-24, 62), new Vector2(24, 62),
                new Vector2(-12, 72), new Vector2(12, 72),
                new Vector2(-22, 82), new Vector2(22, 82),
                new Vector2(-8, 90), new Vector2(8, 90),
                new Vector2(-28, 74), new Vector2(28, 74),
                new Vector2(-18, 56), new Vector2(18, 56)
            };
            foreach (var loc in treeLocations)
            {
                BuildAncientTree(park, new Vector3(loc.x, 0, loc.y));
            }

            // Traditional Stone Toro Lanterns (石灯籠) along the path
            Vector2[] toroLocations = {
                new Vector2(-3.4f, 50), new Vector2(3.4f, 50),
                new Vector2(-3.6f, 64), new Vector2(3.6f, 64),
                new Vector2(-3.6f, 76), new Vector2(3.6f, 76)
            };
            foreach (var loc in toroLocations)
            {
                BuildStoneLantern(park, new Vector3(loc.x, 0, loc.y));
            }

            // Grand Traditional Vermilion Torii Gate (北野神社) at Z = 78
            BuildToriiGate(park, new Vector3(0, 0, 78f));

            // Shinto Shrine Building (Z = 88)
            BuildShintoShrine(park, new Vector3(0, 0, 88f));

            // Park Benches
            BuildParkBench(park, new Vector3(-12f, 0, 58f), 45);
            BuildParkBench(park, new Vector3(12f, 0, 58f), -45);
            BuildParkBench(park, new Vector3(14f, 0, 74f), -60);

            // Street & Park Flickering Lamp Posts ("фонарі які працюють через раз")
            Vector3[] lampPositions = {
                new Vector3(-12f, 0, 41f),
                new Vector3(12f, 0, 41f), // Flaky / intermittently working
                new Vector3(-6f, 0, 52f),
                new Vector3(6f, 0, 52f),  // Flaky
                new Vector3(-7f, 0, 66f), // Flaky
                new Vector3(7f, 0, 66f),
                new Vector3(-5f, 0, 78f),
                new Vector3(5f, 0, 78f)   // Flaky
            };
            for (int i = 0; i < lampPositions.Length; i++)
            {
                bool flaky = (i % 2 == 1);
                BuildStreetLamp(park, lampPositions[i], !flaky);
            }
        }

        void BuildAncientTree(Transform parent, Vector3 pos)
        {
            var tree = new GameObject("Ancient Tree").transform;
            tree.SetParent(parent, false);
            tree.transform.localPosition = pos;

            // Trunk with root flares
            CreateCylinder(tree, new Vector3(0, 2.8f, 0), 0.75f, 5.6f, matBark, true, "Trunk");
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector3 flarePos = new Vector3(Mathf.Sin(angle) * 0.85f, 0.5f, Mathf.Cos(angle) * 0.85f);
                CreateBox(tree, flarePos, new Vector3(0.8f, 1.0f, 0.8f), matBark, true, "Root Flare");
            }

            // Spreading twisting branches
            for (int i = 0; i < 5; i++)
            {
                float ang = (i / 5f) * Mathf.PI * 2f + 0.3f;
                Vector3 branchPos = new Vector3(Mathf.Sin(ang) * 1.8f, 5.4f + (i % 2) * 0.8f, Mathf.Cos(ang) * 1.8f);
                CreateCylinder(tree, branchPos, 0.35f, 4.0f, matBark, false, "Branch");

                // Foliage clump
                Vector3 crownPos = new Vector3(Mathf.Sin(ang) * 3.2f, 7.2f + (i % 2) * 0.9f, Mathf.Cos(ang) * 3.2f);
                CreateBox(tree, crownPos, new Vector3(4.2f, 2.8f, 4.2f), matFoliage, false, "Foliage Cluster");
            }

            // High central crown
            CreateBox(tree, new Vector3(0, 8.8f, 0), new Vector3(6.2f, 3.8f, 6.2f), matFoliage, false, "High Crown");
        }

        void BuildStoneLantern(Transform parent, Vector3 pos)
        {
            var toro = new GameObject("Stone Toro").transform;
            toro.SetParent(parent, false);
            toro.transform.localPosition = pos;

            CreateBox(toro, new Vector3(0, 0.15f, 0), new Vector3(0.9f, 0.3f, 0.9f), matStone, true);
            CreateCylinder(toro, new Vector3(0, 0.85f, 0), 0.24f, 1.1f, matStone, true);
            CreateBox(toro, new Vector3(0, 1.45f, 0), new Vector3(0.85f, 0.18f, 0.85f), matStone, true);
            // Fire box & candle
            CreateBox(toro, new Vector3(0, 1.8f, 0), new Vector3(0.55f, 0.55f, 0.55f), matDarkMetal, true);
            CreateBox(toro, new Vector3(0, 2.2f, 0), new Vector3(1.05f, 0.22f, 1.05f), matStone, true);
            CreateBox(toro, new Vector3(0, 2.38f, 0), new Vector3(0.6f, 0.15f, 0.6f), matStone, false);

            var candleLight = toro.gameObject.AddComponent<Light>();
            candleLight.type = LightType.Point;
            candleLight.color = new Color(1.0f, 0.65f, 0.25f);
            candleLight.intensity = 1.4f;
            candleLight.range = 4.8f;
        }

        void BuildToriiGate(Transform parent, Vector3 pos)
        {
            var torii = new GameObject("Grand Torii Gate").transform;
            torii.SetParent(parent, false);
            torii.transform.localPosition = pos;

            // Two massive vertical red pillars with black pedestals
            for (int side = -1; side <= 1; side += 2)
            {
                CreateCylinder(torii, new Vector3(side * 3.6f, 0.35f, 0), 0.48f, 0.7f, matDarkMetal, true, "Pedestal");
                CreateCylinder(torii, new Vector3(side * 3.6f, 3.8f, 0), 0.38f, 6.4f, matTorii, true, "Pillar");
            }

            // Lower crossbeam (Nuki)
            CreateBox(torii, new Vector3(0, 5.2f, 0), new Vector3(8.4f, 0.42f, 0.42f), matTorii, true, "Nuki Beam");
            // Top main curved crossbeam (Kasagi) with black cap
            CreateBox(torii, new Vector3(0, 6.8f, 0), new Vector3(10.2f, 0.58f, 0.58f), matTorii, true, "Kasagi Beam");
            CreateBox(torii, new Vector3(0, 7.15f, 0), new Vector3(10.5f, 0.18f, 0.65f), matDarkMetal, false, "Kasagi Cap");

            // Central plaque (Gakuzuka): "北野神社"
            var plaque = CreateBox(torii, new Vector3(0, 6.0f, 0.05f), new Vector3(0.95f, 1.25f, 0.06f), matDarkMetal, false, "Shrine Plaque");
            AddPlaqueText(plaque, "北野神社\n安寧祈願");
        }

        void BuildShintoShrine(Transform parent, Vector3 pos)
        {
            var shrine = new GameObject("Shinto Shrine").transform;
            shrine.SetParent(parent, false);
            shrine.transform.localPosition = pos;

            // Shrine main hall
            CreateBox(shrine, new Vector3(0, 2.0f, 0), new Vector3(5.2f, 4.0f, 4.6f), matWood, true, "Shrine Hall");
            // Curved hip-and-gable roof
            CreateBox(shrine, new Vector3(0, 4.3f, 0), new Vector3(6.8f, 0.55f, 5.8f), matDarkMetal, true, "Shrine Roof");
            CreateBox(shrine, new Vector3(0, 4.8f, 0), new Vector3(5.4f, 0.65f, 4.6f), matDarkMetal, false, "Shrine Roof Top");

            // Offering box & bell rope in front
            CreateBox(shrine, new Vector3(0, 0.75f, -2.6f), new Vector3(1.5f, 0.85f, 0.95f), matWood, true, "Offering Box");
        }

        void BuildParkBench(Transform parent, Vector3 pos, float yaw)
        {
            var bench = new GameObject("Park Bench").transform;
            bench.SetParent(parent, false);
            bench.transform.localPosition = pos;
            bench.transform.localRotation = Quaternion.Euler(0, yaw, 0);

            CreateBox(bench, new Vector3(0, 0.45f, 0), new Vector3(2.0f, 0.08f, 0.55f), matWood, true);
            CreateBox(bench, new Vector3(0, 0.85f, 0.24f), new Vector3(2.0f, 0.42f, 0.06f), matWood, false);
            CreateCylinder(bench, new Vector3(-0.8f, 0.22f, 0), 0.03f, 0.45f, matDarkMetal, false);
            CreateCylinder(bench, new Vector3(0.8f, 0.22f, 0), 0.03f, 0.45f, matDarkMetal, false);
        }

        void BuildStreetLamp(Transform parent, Vector3 pos, bool worksNormally)
        {
            var pole = new GameObject("Street Lamp").transform;
            pole.SetParent(parent, false);
            pole.transform.localPosition = pos;

            CreateCylinder(pole, new Vector3(0, 2.8f, 0), 0.09f, 5.6f, matDarkMetal, true);
            CreateBox(pole, new Vector3(0.38f, 5.5f, 0), new Vector3(0.85f, 0.08f, 0.08f), matDarkMetal, false);
            CreateBox(pole, new Vector3(0.78f, 5.35f, 0), new Vector3(0.42f, 0.24f, 0.42f), matDarkMetal, false);
            var bulb = CreateBox(pole, new Vector3(0.78f, 5.2f, 0), new Vector3(0.32f, 0.06f, 0.32f), matWhite, false);

            var l = pole.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.88f, 0.65f);
            l.intensity = worksNormally ? 2.8f : 1.2f;
            l.range = 14f;
            l.shadows = LightShadows.Soft;

            lamps.Add(new FlickeringLamp
            {
                light = l,
                bulb = bulb.GetComponent<Renderer>(),
                baseIntensity = 2.8f,
                worksNormally = worksNormally,
                flickerOffset = UnityEngine.Random.value * 50f
            });
        }

        // ====================================================================
        // 5. JOURNALISTS' NEWS VAN (Arrival / Exit Point)
        // ====================================================================
        void BuildNewsVan()
        {
            var van = new GameObject("Journalist News Van").transform;
            van.SetParent(Root, false);
            van.localPosition = new Vector3(0, 0, 41f);
            van.localRotation = Quaternion.Euler(0, 180, 0);

            // Van body
            CreateBox(van, new Vector3(0, 1.0f, 0), new Vector3(2.4f, 1.4f, 5.2f), matWhite, true, "Van Body");
            CreateBox(van, new Vector3(0, 1.8f, -0.4f), new Vector3(2.35f, 1.2f, 3.6f), matWhite, true, "Van Cabin");
            // Windshield
            CreateBox(van, new Vector3(0, 1.7f, 1.7f), new Vector3(2.25f, 0.95f, 0.95f), matGlass, false, "Windshield");

            // Wheels
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var w = CreateCylinder(van, new Vector3(sx * 1.25f, 0.4f, sz * 1.5f), 0.4f, 0.28f, matDarkMetal, true, "Wheel");
                    w.transform.rotation = Quaternion.Euler(0, 0, 90);
                }
            }

            // Roof Satellite Dish / Antenna
            CreateCylinder(van, new Vector3(0, 2.6f, -0.9f), 0.05f, 0.7f, matDarkMetal, false);
            var dish = CreateCylinder(van, new Vector3(0, 2.95f, -0.9f), 0.65f, 0.14f, matWhite, false);
            dish.transform.rotation = Quaternion.Euler(25, 0, 0);

            // Side News Decal: "TV TOKYO / 怪奇特集取材班"
            var decal = CreateBox(van, new Vector3(1.23f, 1.5f, -0.3f), new Vector3(0.04f, 0.8f, 2.5f), matDarkMetal, false, "News Decal");
            AddPlaqueText(decal, "TV TOKYO NEWS\n怪奇特集取材班");

            // Headlights shining into the school courtyard
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var hl = new GameObject("Headlight");
                hl.transform.SetParent(van, false);
                hl.transform.localPosition = new Vector3(sx * 0.85f, 0.8f, 2.65f);
                var spot = hl.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = new Color(1.0f, 0.96f, 0.85f);
                spot.intensity = 4.5f;
                spot.range = 22f;
                spot.spotAngle = 45f;
            }
        }

        // ====================================================================
        // 6. SPAWN THE 5 RANDOM SANITY PILLS (Таблетки)
        // ====================================================================
        void SpawnSanityPills()
        {
            // Available potential spawn locations across school & park
            Vector3[] candidates = {
                new Vector3(-21f, 0.8f, -12.5f), // Class 1-A desk
                new Vector3(-7f, 0.8f, -12.5f),  // Class 1-B desk
                new Vector3(23f, 0.9f, -12.0f),  // Nurse's office table
                new Vector3(-3.4f, 0.9f, -3.5f), // Genkan shoe cubbies
                new Vector3(3f, 0.15f, 14f),     // Courtyard yellow circle
                new Vector3(8f, 0.15f, 22f),     // Courtyard red circle
                new Vector3(-3.4f, 0.95f, 50f),  // Park stone lantern
                new Vector3(3.4f, 0.95f, 64f),   // Park stone lantern
                new Vector3(-12f, 0.55f, 58f),   // Park bench
                new Vector3(0f, 0.85f, 85.5f)    // Shinto shrine offering box
            };

            // Pick 5 distinct locations
            var rng = new System.Random(54321);
            var shuffled = new List<Vector3>(candidates);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int k = rng.Next(i + 1);
                var temp = shuffled[i]; shuffled[i] = shuffled[k]; shuffled[k] = temp;
            }

            for (int i = 0; i < 5; i++)
            {
                var pos = shuffled[i];
                var pillGo = new GameObject("SanityPill_0" + (i + 1));
                pillGo.transform.SetParent(Root, false);
                pillGo.transform.localPosition = pos;

                // Amber pharmaceutical bottle
                var amberMat = CreateMaterial("PillAmber", matPlaster.shader, new Color(0.92f, 0.62f, 0.22f), 0.5f);
                CreateCylinder(pillGo.transform, new Vector3(0, 0.09f, 0), 0.065f, 0.18f, amberMat, true, "Bottle");
                CreateCylinder(pillGo.transform, new Vector3(0, 0.19f, 0), 0.055f, 0.05f, matWhite, false, "Cap");

                // Glowing Cyan Aura & Light for spot visibility in dark
                var auraMat = CreateMaterial("PillAura", matPlaster.shader, new Color(0.35f, 0.85f, 1.0f), 0.8f);
                CreateBox(pillGo.transform, new Vector3(0, 0.1f, 0), new Vector3(0.18f, 0.25f, 0.18f), auraMat, false, "Aura");

                var light = pillGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.4f, 0.85f, 1.0f);
                light.intensity = 1.8f;
                light.range = 3.2f;

                pills.Add(new PillItem
                {
                    id = "pill-" + (i + 1),
                    go = pillGo,
                    position = pos,
                    light = light,
                    collected = false
                });
            }
        }

        void AddPlaqueText(GameObject target, string text)
        {
            var txtObj = new GameObject("PlaqueText");
            txtObj.transform.SetParent(target.transform, false);
            txtObj.transform.localPosition = new Vector3(0, 0, 0.52f);
            var mesh = txtObj.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 24;
            mesh.characterSize = 0.06f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.95f, 0.90f, 0.75f);
        }

        // ====================================================================
        // UPDATE TICK (Flickering Lamps & Pill Animation)
        // ====================================================================
        public void Update(RoomState state, float dt)
        {
            if (state == null) return;

            // Animate Flickering Street & Park Lamps ("працюють через раз")
            float time = Time.time;
            for (int i = 0; i < lamps.Count; i++)
            {
                var lamp = lamps[i];
                if (lamp.light == null) continue;

                if (lamp.worksNormally)
                {
                    // Subtle occasional pulse
                    lamp.light.intensity = lamp.baseIntensity * (0.95f + Mathf.Sin((time + lamp.flickerOffset) * 2.5f) * 0.05f);
                }
                else
                {
                    // Erratically flickering / cutting out
                    float wave = Mathf.Sin((time + lamp.flickerOffset) * 12.3f) * Mathf.Cos((time + lamp.flickerOffset) * 3.7f);
                    bool on = wave > -0.2f && (Mathf.Sin((time + lamp.flickerOffset) * 1.8f) > -0.6f);
                    lamp.light.intensity = on ? lamp.baseIntensity * UnityEngine.Random.Range(0.6f, 1.1f) : 0f;
                    if (lamp.bulb != null)
                    {
                        lamp.bulb.material.color = on ? Color.white : new Color(0.2f, 0.2f, 0.2f);
                    }
                }
            }

            // Animate Sanity Pills Bob & Glow
            for (int i = 0; i < pills.Count; i++)
            {
                var pill = pills[i];
                if (pill.go == null) continue;
                bool isCollected = state.Collected(pill.id);
                if (isCollected != pill.collected)
                {
                    pill.collected = isCollected;
                    pill.go.SetActive(!isCollected);
                }
                if (!pill.collected)
                {
                    float bob = Mathf.Sin(time * 3f + i) * 0.04f;
                    pill.go.transform.localPosition = pill.position + new Vector3(0, bob, 0);
                    pill.go.transform.Rotate(0, 45f * dt, 0);
                }
            }
        }
    }
}
