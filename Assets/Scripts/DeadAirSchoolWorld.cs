using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>
    /// Massive, authentic Japanese School, Residential Neighborhood, Two-Floor Interiors,
    /// Back Sports Yard & Forest, Road, and Traditional Park generator for Chapter 2.
    /// Completely replaces Chapter 1 with full standalone journalist mechanics.
    /// </summary>
    public sealed class DeadAirSchoolWorld
    {
        public readonly Transform Root;
        readonly WorldData data;
        readonly List<GameObject> createdObjects = new List<GameObject>();
        readonly List<FlickeringLamp> lamps = new List<FlickeringLamp>();
        readonly List<PillItem> pills = new List<PillItem>();
        readonly List<EvidenceTapeItem> evidenceTapes = new List<EvidenceTapeItem>();

        Material matPlaster, matAsphalt, matRoadZebra, matWoodFloor, matBark, matFoliage;
        Material matTorii, matStone, matGlass, matDarkMetal, matConcrete, matChalkboard, matWhite;
        Material matHouseStucco, matKawaraRoof, matBlockWall, matGuardrail, matBooks, matLabCounter;

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
            public bool collected;
        }

        public sealed class EvidenceTapeItem
        {
            public string id;
            public string label;
            public GameObject go;
            public Vector3 position;
            public bool collected;
        }

        public IReadOnlyList<PillItem> Pills => pills;
        public IReadOnlyList<EvidenceTapeItem> EvidenceTapes => evidenceTapes;

        public DeadAirSchoolWorld(Transform parent, WorldData data)
        {
            this.data = data;
            var go = new GameObject("CHAPTER 2 / JAPANESE SCHOOL & NEIGHBORHOOD");
            go.transform.SetParent(parent, false);
            Root = go.transform;

            InitMaterials();
        }

        void InitMaterials()
        {
            var shader = Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard");

            matPlaster = CreateMat("SchoolPlaster", shader, new Color(0.86f, 0.83f, 0.74f), 0.15f);
            matAsphalt = CreateMat("RoadAsphalt", shader, new Color(0.18f, 0.19f, 0.20f), 0.35f);
            matRoadZebra = CreateMat("RoadZebraWhite", shader, new Color(0.92f, 0.92f, 0.88f), 0.25f);
            matWoodFloor = CreateMat("ParquetWood", shader, new Color(0.52f, 0.38f, 0.22f), 0.3f);
            matBark = CreateMat("DarkBark", shader, new Color(0.24f, 0.19f, 0.14f), 0.05f);
            matFoliage = CreateMat("DeepFoliage", shader, new Color(0.12f, 0.22f, 0.14f), 0.08f);
            matTorii = CreateMat("ShrineVermilion", shader, new Color(0.85f, 0.20f, 0.12f), 0.4f);
            matStone = CreateMat("WeatheredStone", shader, new Color(0.46f, 0.47f, 0.45f), 0.15f);
            matGlass = CreateMat("DarkGlass", shader, new Color(0.10f, 0.15f, 0.18f), 0.9f);
            matDarkMetal = CreateMat("MetalDark", shader, new Color(0.18f, 0.20f, 0.18f), 0.6f);
            matConcrete = CreateMat("ConcreteLight", shader, new Color(0.60f, 0.60f, 0.56f), 0.2f);
            matChalkboard = CreateChalkboardMaterial(shader);
            matWhite = CreateMat("TrimWhite", shader, new Color(0.92f, 0.90f, 0.86f), 0.3f);
            matHouseStucco = CreateMat("HouseStucco", shader, new Color(0.78f, 0.76f, 0.70f), 0.1f);
            matKawaraRoof = CreateMat("KawaraRoof", shader, new Color(0.22f, 0.25f, 0.28f), 0.35f);
            matBlockWall = CreateMat("BlockWall", shader, new Color(0.55f, 0.54f, 0.50f), 0.15f);
            matGuardrail = CreateMat("GuardrailWhite", shader, new Color(0.88f, 0.88f, 0.88f), 0.5f);
            matBooks = CreateMat("BookCovers", shader, new Color(0.42f, 0.28f, 0.20f), 0.15f);
            matLabCounter = CreateMat("LabBlackStone", shader, new Color(0.14f, 0.15f, 0.15f), 0.7f);
        }

        static Material CreateMat(string name, Shader shader, Color color, float gloss)
        {
            var mat = new Material(shader) { name = name, color = color, hideFlags = HideFlags.DontSave };
            mat.SetFloat("_Glossiness", gloss);
            return mat;
        }

        static Material CreateChalkboardMaterial(Shader shader)
        {
            // Procedural dark green chalkboard with baked chalk graffiti so it NEVER bleeds through walls!
            var tex = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            var green = new Color32(28, 56, 36, 255);
            var chalk = new Color32(215, 230, 210, 255);
            var pixels = new Color32[256 * 128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = green;

            // Draw chalk lines and symbols
            for (int x = 20; x < 236; x++)
            {
                if (x % 3 == 0) { pixels[100 * 256 + x] = chalk; pixels[70 * 256 + x] = chalk; pixels[40 * 256 + x] = chalk; }
            }
            // Japanese chalk strokes
            for (int y = 50; y < 90; y++)
            {
                pixels[y * 256 + 60] = chalk; pixels[y * 256 + 120] = chalk; pixels[y * 256 + 180] = chalk;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var mat = new Material(shader) { name = "ChalkboardBaked", mainTexture = tex, hideFlags = HideFlags.DontSave };
            mat.SetFloat("_Glossiness", 0.05f);
            return mat;
        }

        public void Initialize()
        {
            BuildRoadAndCrosswalk();
            BuildLeftNeighborhood();
            BuildRightNeighborhood();
            BuildBackYardAndForest();
            BuildSchoolCourtyard();
            BuildSchoolBuilding();
            BuildFirstFloorInteriors();
            BuildSecondFloorInteriors();
            BuildStairs();
            BuildTraditionalPark();
            BuildNewsVan();
            SpawnSanityPills();
            SpawnEvidenceTapes();
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
            var moon = new GameObject("Japanese Night Moonlight");
            moon.transform.SetParent(Root, false);
            var light = moon.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.48f, 0.58f, 0.72f);
            light.intensity = 0.65f;
            light.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(52, -40, 0);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.11f);
            RenderSettings.fogDensity = 0.010f;
        }

        // ====================================================================
        // 1. ROAD, SIDEWALKS & ZEBRA CROSSWALK (Request 1)
        // ====================================================================
        void BuildRoadAndCrosswalk()
        {
            var roadGroup = new GameObject("Street & Sidewalks").transform;
            roadGroup.SetParent(Root, false);

            // Two-lane asphalt street (Z: 38 to 48, X: -60 to 60)
            CreateBox(roadGroup, new Vector3(0, -0.05f, 43f), new Vector3(120f, 0.1f, 10f), matAsphalt, true, "Main Street Asphalt");

            // Broken yellow central lane line (Z = 43)
            for (float x = -58f; x <= 58f; x += 4f)
            {
                CreateBox(roadGroup, new Vector3(x, 0.01f, 43f), new Vector3(2.4f, 0.01f, 0.18f), matTorii, false, "Yellow Center Line");
            }

            // White shoulder lines
            CreateBox(roadGroup, new Vector3(0, 0.01f, 38.6f), new Vector3(120f, 0.01f, 0.18f), matWhite, false);
            CreateBox(roadGroup, new Vector3(0, 0.01f, 47.4f), new Vector3(120f, 0.01f, 0.18f), matWhite, false);

            // Large White Zebra Pedestrian Crosswalk right in front of school gates (Z: 38 to 48, X: -4 to 4)
            for (float zx = -3.5f; zx <= 3.5f; zx += 1.2f)
            {
                CreateBox(roadGroup, new Vector3(zx, 0.015f, 43f), new Vector3(0.65f, 0.01f, 8.8f), matRoadZebra, false, "Zebra Stripe");
            }

            // Raised concrete sidewalk (Тротуар) on School side (Z: 36 to 38)
            CreateBox(roadGroup, new Vector3(0, 0.09f, 37f), new Vector3(120f, 0.18f, 2f), matConcrete, true, "School Sidewalk");
            // Raised concrete sidewalk on Park side (Z: 48 to 50)
            CreateBox(roadGroup, new Vector3(0, 0.09f, 49f), new Vector3(120f, 0.18f, 2f), matConcrete, true, "Park Sidewalk");

            // Japanese White Street Guardrails along sidewalks (with openings for crosswalk at X: -4 to 4)
            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = 38.2f; z <= 47.8f; z += 9.6f)
                {
                    float startX = 4.5f, endX = 55f;
                    float len = endX - startX;
                    float midX = side * (startX + len * 0.5f);
                    // Upper and lower rail
                    CreateBox(roadGroup, new Vector3(midX, 0.75f, z), new Vector3(len, 0.05f, 0.05f), matGuardrail, false, "Guardrail Top");
                    CreateBox(roadGroup, new Vector3(midX, 0.45f, z), new Vector3(len, 0.05f, 0.05f), matGuardrail, false, "Guardrail Mid");
                    // Vertical posts
                    for (float px = startX; px <= endX; px += 3f)
                    {
                        CreateCylinder(roadGroup, new Vector3(side * px, 0.45f, z), 0.035f, 0.9f, matGuardrail, false, "Rail Post");
                    }
                }
            }

            // Curved Japanese Traffic Safety Mirrors (カーブミラー) at street corners
            BuildTrafficMirror(roadGroup, new Vector3(-6f, 0, 48.5f));
            BuildTrafficMirror(roadGroup, new Vector3(6f, 0, 36.5f));
        }

        void BuildTrafficMirror(Transform parent, Vector3 pos)
        {
            var g = new GameObject("Curved Traffic Mirror").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            CreateCylinder(g, new Vector3(0, 1.8f, 0), 0.05f, 3.6f, matGuardrail, true);
            var mirror = CreateCylinder(g, new Vector3(0, 3.3f, 0.2f), 0.45f, 0.08f, matTorii, false);
            mirror.transform.rotation = Quaternion.Euler(75, 0, 0);
            CreateCylinder(mirror.transform, new Vector3(0, 0.04f, 0), 0.40f, 0.02f, matGlass, false);
        }

        // ====================================================================
        // 2. NEIGHBORHOOD HOUSES ON LEFT AND RIGHT (Request 2)
        // ====================================================================
        void BuildLeftNeighborhood()
        {
            var left = new GameObject("Left Neighborhood").transform;
            left.SetParent(Root, false);

            // Perimeter Concrete Block Wall (X = -32, Z: -20 to 50)
            CreateBox(left, new Vector3(-32f, 1.1f, 15f), new Vector3(0.35f, 2.2f, 70f), matBlockWall, true, "Left Perimeter Wall");

            // Row of 4 Japanese Two-Story Residential Houses behind the wall
            float[] houseZ = { -10f, 8f, 26f, 44f };
            foreach (var hz in houseZ)
            {
                BuildJapaneseHouse(left, new Vector3(-42f, 0, hz), 0);
            }

            // Utility Telephone Poles & Power Wires (電柱) along the street
            BuildUtilityPole(left, new Vector3(-34f, 0, 45f));
            BuildUtilityPole(left, new Vector3(-34f, 0, 15f));
        }

        void BuildRightNeighborhood()
        {
            var right = new GameObject("Right Neighborhood").transform;
            right.SetParent(Root, false);

            // Perimeter Concrete Block Wall (X = 32, Z: -20 to 50)
            CreateBox(right, new Vector3(32f, 1.1f, 15f), new Vector3(0.35f, 2.2f, 70f), matBlockWall, true, "Right Perimeter Wall");

            // Row of 4 Japanese Two-Story Residential Houses
            float[] houseZ = { -10f, 8f, 26f, 44f };
            foreach (var hz in houseZ)
            {
                BuildJapaneseHouse(right, new Vector3(42f, 0, hz), 180);
            }

            BuildUtilityPole(right, new Vector3(34f, 0, 45f));
            BuildUtilityPole(right, new Vector3(34f, 0, 15f));
        }

        void BuildJapaneseHouse(Transform parent, Vector3 pos, float yaw)
        {
            var house = new GameObject("Japanese House").transform;
            house.SetParent(parent, false);
            house.localPosition = pos;
            house.localRotation = Quaternion.Euler(0, yaw, 0);

            float w = 12f, h = 6.8f, d = 11f;
            // First & Second Floor Stucco Body
            CreateBox(house, new Vector3(0, 3.4f, 0), new Vector3(w, h, d), matHouseStucco, true, "House Body");
            // Wooden wainscot skirt
            CreateBox(house, new Vector3(0, 0.8f, 0), new Vector3(w + 0.1f, 1.6f, d + 0.1f), matBark, false, "House Wood Skirt");

            // Traditional Kawara Tiled Roof (Hipped roof)
            CreateBox(house, new Vector3(0, 7.2f, 0), new Vector3(w + 1.6f, 0.7f, d + 1.6f), matKawaraRoof, true, "Kawara Roof Base");
            CreateBox(house, new Vector3(0, 8.0f, 0), new Vector3(w * 0.7f, 1.0f, d * 0.7f), matKawaraRoof, false, "Kawara Roof Mid");
            CreateBox(house, new Vector3(0, 8.7f, 0), new Vector3(w * 0.4f, 0.6f, d * 0.4f), matKawaraRoof, false, "Kawara Roof Top");

            // 2nd Floor Balcony with Railing
            CreateBox(house, new Vector3(0, 3.8f, d * 0.5f + 0.6f), new Vector3(w * 0.7f, 0.15f, 1.2f), matConcrete, true, "Balcony Floor");
            CreateBox(house, new Vector3(0, 4.3f, d * 0.5f + 1.15f), new Vector3(w * 0.7f, 0.8f, 0.1f), matDarkMetal, false, "Balcony Rail");

            // Windows with soft warm interior amber glow
            for (int f = 0; f < 2; f++)
            {
                float wy = 1.8f + f * 3.4f;
                var win = CreateBox(house, new Vector3(2.5f, wy, d * 0.5f + 0.05f), new Vector3(2.2f, 1.4f, 0.1f), matWhite, false, "Window Frame");
                var glass = CreateBox(win.transform, new Vector3(0, 0, 0.02f), new Vector3(2.0f, 1.2f, 0.05f), matPlaster, false, "Lit Window");
                // Soft amber light from window
                var l = win.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1.0f, 0.82f, 0.55f);
                l.intensity = 0.8f;
                l.range = 5f;
            }

            // Air conditioner outdoor unit on wall
            CreateBox(house, new Vector3(-3.5f, 0.5f, d * 0.5f + 0.35f), new Vector3(0.9f, 0.65f, 0.4f), matWhite, false, "AC Unit");
        }

        void BuildUtilityPole(Transform parent, Vector3 pos)
        {
            var pole = new GameObject("Utility Pole (電柱)").transform;
            pole.SetParent(parent, false);
            pole.localPosition = pos;
            CreateCylinder(pole, new Vector3(0, 5.0f, 0), 0.18f, 10.0f, matConcrete, true);
            // Crossarms for wires
            CreateBox(pole, new Vector3(0, 8.8f, 0), new Vector3(2.4f, 0.1f, 0.1f), matDarkMetal, false);
            CreateBox(pole, new Vector3(0, 9.4f, 0), new Vector3(1.8f, 0.1f, 0.1f), matDarkMetal, false);
            // Transformer can
            CreateCylinder(pole, new Vector3(0.35f, 8.2f, 0), 0.28f, 0.9f, matDarkMetal, false);
        }

        // ====================================================================
        // 3. BACK SCHOOLYARD & DENSE FOREST (Request 3)
        // ====================================================================
        void BuildBackYardAndForest()
        {
            var back = new GameObject("Back Schoolyard & Forest").transform;
            back.SetParent(Root, false);

            // Back schoolyard dirt/gravel ground (Z: -16 to -36, X: -32 to 32)
            CreateBox(back, new Vector3(0, -0.05f, -26f), new Vector3(64f, 0.1f, 20f), matConcrete, true, "Backyard Ground");

            // Bicycle Parking Canopy Shed (駐輪場 - chūrinjō) at X: -16, Z: -26
            var bikeShed = new GameObject("Bicycle Shed").transform;
            bikeShed.SetParent(back, false);
            bikeShed.localPosition = new Vector3(-16f, 0, -25f);
            CreateBox(bikeShed, new Vector3(0, 2.5f, 0), new Vector3(10f, 0.12f, 3.8f), matDarkMetal, true, "Shed Roof");
            for (float bx = -4.5f; bx <= 4.5f; bx += 4.5f)
            {
                CreateCylinder(bikeShed, new Vector3(bx, 1.25f, -1.6f), 0.05f, 2.5f, matDarkMetal, false);
                CreateCylinder(bikeShed, new Vector3(bx, 1.25f, 1.6f), 0.05f, 2.5f, matDarkMetal, false);
            }
            // Bicycle tire racks
            for (float bx = -4.2f; bx <= 4.2f; bx += 0.8f)
            {
                CreateBox(bikeShed, new Vector3(bx, 0.25f, 0), new Vector3(0.04f, 0.5f, 1.4f), matDarkMetal, false);
            }

            // School Metal Storage Lockup / Tool Shed at X: 16, Z: -25
            CreateBox(back, new Vector3(16f, 1.6f, -25f), new Vector3(5.2f, 3.2f, 3.6f), matDarkMetal, true, "Storage Lockup");

            // School Incinerator (焼却炉) at X: 24, Z: -28
            var inc = CreateBox(back, new Vector3(24f, 1.2f, -28f), new Vector3(1.8f, 2.4f, 1.8f), matConcrete, true, "Incinerator");
            CreateCylinder(inc.transform, new Vector3(0, 2.0f, 0), 0.22f, 2.2f, matDarkMetal, false, "Chimney");

            // Chain-link Sports Perimeter Fence (4m high, at Z = -35, X: -32 to 32)
            CreateBox(back, new Vector3(0, 2.0f, -35f), new Vector3(64f, 4.0f, 0.15f), matDarkMetal, true, "Back Sports Fence");

            // Dense Dark Japanese Pine/Cedar Forest (杉の森) (Z: -36 to -80, X: -45 to 45)
            CreateBox(back, new Vector3(0, -0.05f, -58f), new Vector3(90f, 0.1f, 46f), matFoliage, true, "Forest Floor");

            // 32 Dense Forest Trees behind the fence
            var rng = new System.Random(88812);
            for (int i = 0; i < 32; i++)
            {
                float fx = (float)(rng.NextDouble() * 80.0 - 40.0);
                float fz = (float)(rng.NextDouble() * 38.0 - 74.0);
                BuildForestTree(back, new Vector3(fx, 0, fz));
            }
        }

        void BuildForestTree(Transform parent, Vector3 pos)
        {
            var t = new GameObject("Dense Forest Tree").transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            CreateCylinder(t, new Vector3(0, 4.5f, 0), 0.45f, 9.0f, matBark, true);
            // Thick dark conical foliage
            CreateCylinder(t, new Vector3(0, 9.0f, 0), 2.8f, 4.5f, matFoliage, false);
            CreateCylinder(t, new Vector3(0, 12.0f, 0), 2.0f, 3.8f, matFoliage, false);
            CreateCylinder(t, new Vector3(0, 14.5f, 0), 1.2f, 2.8f, matFoliage, false);
        }

        // ====================================================================
        // 4. SCHOOL COURTYARD (Photo 1)
        // ====================================================================
        void BuildSchoolCourtyard()
        {
            var yard = new GameObject("School Courtyard").transform;
            yard.SetParent(Root, false);

            // Main courtyard asphalt ground (Z: 0 to 36, X: -32 to 32)
            CreateBox(yard, new Vector3(0, -0.05f, 18f), new Vector3(64f, 0.1f, 36f), matAsphalt, true, "Courtyard Asphalt");

            // Colorful Painted Circles on Ground (Photo 1)
            var yellowMat = CreateMat("CircleYellow", matPlaster.shader, new Color(0.92f, 0.78f, 0.20f), 0.2f);
            var blueMat = CreateMat("CircleBlue", matPlaster.shader, new Color(0.24f, 0.60f, 0.88f), 0.2f);
            var redMat = CreateMat("CircleRed", matPlaster.shader, new Color(0.88f, 0.26f, 0.22f), 0.2f);
            var greenMat = CreateMat("CircleGreen", matPlaster.shader, new Color(0.26f, 0.82f, 0.36f), 0.2f);

            CreateRing(yard, new Vector3(3f, 0.015f, 14f), 2.8f, yellowMat);
            CreateRing(yard, new Vector3(-6f, 0.015f, 18f), 2.0f, blueMat);
            CreateRing(yard, new Vector3(8f, 0.015f, 22f), 1.6f, redMat);
            CreateRing(yard, new Vector3(-12f, 0.015f, 12f), 3.4f, yellowMat);
            CreateRing(yard, new Vector3(15f, 0.015f, 15f), 1.4f, greenMat);
            CreateRing(yard, new Vector3(-2f, 0.015f, 26f), 2.3f, blueMat);

            // Traffic cones (Photo 1)
            var coneMat = CreateMat("ConeOrange", matPlaster.shader, new Color(0.95f, 0.35f, 0.10f), 0.3f);
            CreateTrafficCone(yard, new Vector3(14f, 0f, 28f), coneMat);
            CreateTrafficCone(yard, new Vector3(16f, 0f, 26f), coneMat);
            CreateTrafficCone(yard, new Vector3(-8f, 0f, 32f), coneMat);

            // Front Concrete Perimeter Wall (at Z = 36)
            for (int side = -1; side <= 1; side += 2)
            {
                float wallWidth = 26f;
                float midX = side * (5.5f + wallWidth * 0.5f);
                CreateBox(yard, new Vector3(midX, 1.1f, 36f), new Vector3(wallWidth, 2.2f, 0.35f), matConcrete, true, "Front Wall");
                // Metal fence top
                for (float fx = 6f; fx < 31f; fx += 1.0f)
                {
                    CreateCylinder(yard, new Vector3(side * fx, 2.6f, 36f), 0.025f, 0.8f, matDarkMetal, false);
                }
            }

            // School Gate Pillars at X = -5.5 and X = 5.5
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(yard, new Vector3(side * 5.5f, 1.8f, 36f), new Vector3(1.0f, 3.6f, 1.0f), matConcrete, true, "Gate Pillar");
                CreateBox(yard, new Vector3(side * 5.5f, 3.65f, 36f), new Vector3(1.2f, 0.2f, 1.2f), matDarkMetal, false);
            }
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
            var g = new GameObject("TrafficCone").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            CreateBox(g, new Vector3(0, 0.02f, 0), new Vector3(0.4f, 0.04f, 0.4f), matDarkMetal, true);
            var cone = CreateCylinder(g, new Vector3(0, 0.38f, 0), 0.13f, 0.76f, coneMat, true);
            CreateCylinder(cone.transform, Vector3.zero, 0.135f, 0.22f, matWhite, false);
        }

        // ====================================================================
        // 5. THE 3-FLOOR SCHOOL BUILDING (Photo 1)
        // ====================================================================
        void BuildSchoolBuilding()
        {
            var bldg = new GameObject("School Outer Structure").transform;
            bldg.SetParent(Root, false);

            float width = 60f, height = 11.5f, depth = 16f;

            // Outer Facade Wall Shell (Z: -16 to 0, X: -30 to 30)
            CreateBox(bldg, new Vector3(0, height * 0.5f, -depth * 0.5f), new Vector3(width, height, depth), matPlaster, true, "Building Mass");
            // Roof Concrete Parapet
            CreateBox(bldg, new Vector3(0, height + 0.35f, -depth * 0.5f), new Vector3(width + 1.2f, 0.7f, depth + 1.2f), matConcrete, true, "Roof Parapet");

            // Central Vertical Glass Stairwell Tower (Photo 1 - extending above roof)
            float towerW = 6.8f, towerH = 15.0f;
            CreateBox(bldg, new Vector3(0, towerH * 0.5f, 0.5f), new Vector3(towerW, towerH, 1.4f), matConcrete, true, "Glass Stair Tower");
            for (int f = 0; f < 4; f++)
            {
                CreateBox(bldg, new Vector3(0, 2.6f + f * 3.5f, 1.22f), new Vector3(towerW - 0.8f, 2.4f, 0.06f), matGlass, false, "Tower Pane");
                for (float mx = -2.2f; mx <= 2.2f; mx += 1.1f)
                {
                    CreateBox(bldg, new Vector3(mx, 2.6f + f * 3.5f, 1.25f), new Vector3(0.08f, 2.4f, 0.08f), matWhite, false, "Tower Mullion");
                }
            }

            // 3 Rows of Japanese Classroom Windows along the front facade
            for (int floor = 0; floor < 3; floor++)
            {
                float wy = 2.2f + floor * 3.6f;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int w = 0; w < 5; w++)
                    {
                        float wx = side * (7.0f + w * 4.4f);
                        CreateBox(bldg, new Vector3(wx, wy, 0.06f), new Vector3(3.6f, 2.2f, 0.12f), matWhite, false);
                        CreateBox(bldg, new Vector3(wx, wy, 0.11f), new Vector3(3.4f, 2.0f, 0.04f), matGlass, false);
                    }
                }
            }

            // Entrance Porch / Canopy with Square Pillars (Photo 1)
            float porchW = 12f, porchD = 4f, porchH = 3.8f;
            CreateBox(bldg, new Vector3(10f, porchH + 0.15f, porchD * 0.5f), new Vector3(porchW, 0.35f, porchD), matConcrete, true, "Porch Roof");
            CreateBox(bldg, new Vector3(4.5f, porchH * 0.5f, porchD - 0.3f), new Vector3(0.6f, porchH, 0.6f), matConcrete, true, "Porch Pillar Left");
            CreateBox(bldg, new Vector3(15.5f, porchH * 0.5f, porchD - 0.3f), new Vector3(0.6f, porchH, 0.6f), matConcrete, true, "Porch Pillar Right");
            CreateBox(bldg, new Vector3(10f, 1.5f, 0.08f), new Vector3(8.5f, 2.8f, 0.14f), matWhite, true, "Sliding Glass Entrance");

            // Planter boxes with shrubs & cycad palm trees along the building (Photo 1)
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 4; i++)
                {
                    float px = side * (8.5f + i * 5.0f);
                    CreateBox(bldg, new Vector3(px, 0.35f, 1.6f), new Vector3(3.8f, 0.7f, 1.0f), matConcrete, true, "Planter");
                    CreateBox(bldg, new Vector3(px, 0.9f, 1.6f), new Vector3(3.4f, 0.8f, 0.8f), matFoliage, false, "Shrub");
                    if (i == 1 || i == 3)
                    {
                        CreateCylinder(bldg, new Vector3(px + 1.2f, 2.0f, 1.6f), 0.22f, 2.8f, matBark, true);
                        CreateBox(bldg, new Vector3(px + 1.2f, 3.5f, 1.6f), new Vector3(2.4f, 0.8f, 2.4f), matFoliage, false);
                    }
                }
            }

            // Facade Round Clock on 2nd Floor
            var clock = CreateCylinder(bldg, new Vector3(14f, 8.4f, 0.18f), 0.95f, 0.14f, matDarkMetal, false, "Clock");
            clock.transform.rotation = Quaternion.Euler(90, 0, 0);
            CreateCylinder(clock.transform, new Vector3(0, 0.08f, 0), 0.85f, 0.04f, matWhite, false);
        }

        // ====================================================================
        // 6. FIRST FLOOR INTERIORS (Request 4)
        // ====================================================================
        void BuildFirstFloorInteriors()
        {
            var floor1 = new GameObject("First Floor Interiors").transform;
            floor1.SetParent(Root, false);

            // Floor & Ceiling (Z: -16 to 0, X: -28 to 28, Y = 0 to 3.8)
            CreateBox(floor1, new Vector3(0, 0.05f, -8f), new Vector3(56f, 0.1f, 16f), matWoodFloor, true, "Floor 1 Ground");
            CreateBox(floor1, new Vector3(0, 3.75f, -8f), new Vector3(56f, 0.15f, 16f), matConcrete, true, "Floor 1 Ceiling");

            // Corridor Partition Wall along Z = -6
            float[] openings = { -20f, -6f, 6f, 20f };
            float lastX = -28f;
            foreach (var ox in openings)
            {
                float w = (ox - 1.2f) - lastX;
                if (w > 0.5f) CreateBox(floor1, new Vector3(lastX + w * 0.5f, 1.85f, -6f), new Vector3(w, 3.7f, 0.25f), matPlaster, true);
                lastX = ox + 1.2f;
            }
            if (28f - lastX > 0.5f) CreateBox(floor1, new Vector3((lastX + 28f) * 0.5f, 1.85f, -6f), new Vector3(28f - lastX, 3.7f, 0.25f), matPlaster, true);

            // Dividing Walls between rooms (Z: -16 to -6)
            CreateBox(floor1, new Vector3(-14f, 1.85f, -11f), new Vector3(0.25f, 3.7f, 10f), matPlaster, true);
            CreateBox(floor1, new Vector3(0f, 1.85f, -11f), new Vector3(0.25f, 3.7f, 10f), matPlaster, true);
            CreateBox(floor1, new Vector3(14f, 1.85f, -11f), new Vector3(0.25f, 3.7f, 10f), matPlaster, true);

            // Genkan Shoe Lockers (Gettabako)
            BuildShoeLockers(floor1, new Vector3(-3.5f, 0, -4f));
            BuildShoeLockers(floor1, new Vector3(3.5f, 0, -4f));

            // Class 1-A (X: -28 to -14)
            BuildClassroom(floor1, -21f, "1-A");
            // Class 1-B (X: -14 to 0)
            BuildClassroom(floor1, -7f, "1-B");

            // Nurse's Office (Медпункт / 保健室) at X: 14 to 28
            BuildNurseOffice(floor1, 21f);

            // Hallway corridor lights
            for (float hx = -22f; hx <= 22f; hx += 8f)
            {
                CreateInteriorLamp(floor1, new Vector3(hx, 3.65f, -3f));
            }

            // Green Emergency Exit Signs (誘導灯) glowing on hallway walls
            BuildExitSign(floor1, new Vector3(-20f, 2.8f, -5.85f));
            BuildExitSign(floor1, new Vector3(20f, 2.8f, -5.85f));
        }

        void BuildExitSign(Transform parent, Vector3 pos)
        {
            var sign = CreateBox(parent, pos, new Vector3(0.6f, 0.3f, 0.08f), matTorii, false, "ExitSign");
            var l = sign.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.1f, 1.0f, 0.4f);
            l.intensity = 1.1f;
            l.range = 2.8f;
        }

        void BuildShoeLockers(Transform parent, Vector3 pos)
        {
            var g = new GameObject("Shoe Lockers").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            CreateBox(g, new Vector3(0, 0.85f, 0), new Vector3(4.0f, 1.7f, 0.65f), matWoodFloor, true);
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    float cx = -1.75f + c * 0.48f;
                    float cy = 0.28f + r * 0.38f;
                    CreateBox(g, new Vector3(cx, cy, 0.05f), new Vector3(0.42f, 0.32f, 0.55f), matDarkMetal, false);
                }
            }
        }

        void BuildClassroom(Transform parent, float centerX, string roomName)
        {
            var room = new GameObject("Classroom " + roomName).transform;
            room.SetParent(parent, false);

            // Baked Chalkboard on Front Wall (Z = -15.85) - uses baked texture with NO bleed through walls!
            CreateBox(room, new Vector3(centerX, 1.85f, -15.85f), new Vector3(5.2f, 1.5f, 0.08f), matChalkboard, true, "Chalkboard");

            // Teacher Podium
            CreateBox(room, new Vector3(centerX, 0.45f, -14.2f), new Vector3(1.8f, 0.85f, 0.85f), matWoodFloor, true, "Teacher Desk");

            // 3x3 Grid of Student Desks with Chairs
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float dx = centerX - 2.8f + c * 2.8f;
                    float dz = -12.5f + r * 2.3f;
                    BuildDesk(room, new Vector3(dx, 0f, dz));
                }
            }
        }

        void BuildDesk(Transform parent, Vector3 pos)
        {
            var g = new GameObject("SchoolDesk").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            // Desktop
            CreateBox(g, new Vector3(0, 0.72f, 0), new Vector3(0.78f, 0.04f, 0.52f), matWoodFloor, true);
            // Legs
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    CreateCylinder(g, new Vector3(sx * 0.34f, 0.36f, sz * 0.22f), 0.018f, 0.72f, matDarkMetal, false);
                }
            }
            // Attached Chair
            CreateBox(g, new Vector3(0, 0.42f, 0.46f), new Vector3(0.44f, 0.035f, 0.42f), matWoodFloor, true);
            CreateBox(g, new Vector3(0, 0.80f, 0.64f), new Vector3(0.44f, 0.32f, 0.035f), matWoodFloor, false);
            // School backpack hanging on desk hook
            CreateBox(g, new Vector3(-0.42f, 0.45f, 0), new Vector3(0.12f, 0.35f, 0.24f), matDarkMetal, false, "Backpack");
        }

        void BuildNurseOffice(Transform parent, float centerX)
        {
            var room = new GameObject("Nurse Office").transform;
            room.SetParent(parent, false);

            // Two Medical Hospital Beds
            for (int b = 0; b < 2; b++)
            {
                float bz = -13.5f + b * 2.8f;
                CreateBox(room, new Vector3(centerX + 3.2f, 0.45f, bz), new Vector3(1.3f, 0.4f, 2.3f), matWhite, true, "Bed");
                CreateBox(room, new Vector3(centerX + 3.2f, 0.70f, bz + 0.9f), new Vector3(0.9f, 0.15f, 0.5f), matPlaster, false, "Pillow");
            }
            // Medicine Cabinets
            CreateBox(room, new Vector3(centerX - 3.2f, 1.5f, -15.6f), new Vector3(2.0f, 2.8f, 0.6f), matDarkMetal, true, "Medicine Cabinet");
            CreateBox(room, new Vector3(centerX - 1.0f, 1.5f, -15.6f), new Vector3(1.8f, 2.8f, 0.6f), matDarkMetal, true, "First Aid Cabinet");
        }

        // ====================================================================
        // 7. SECOND FLOOR INTERIORS: Science Lab & Library (Request 4)
        // ====================================================================
        void BuildSecondFloorInteriors()
        {
            var floor2 = new GameObject("Second Floor Interiors").transform;
            floor2.SetParent(Root, false);

            float floorY = 3.8f;
            // Floor & Ceiling (Y: 3.8 to 7.5)
            CreateBox(floor2, new Vector3(0, floorY + 0.05f, -8f), new Vector3(56f, 0.1f, 16f), matWoodFloor, true, "Floor 2 Parquet");
            CreateBox(floor2, new Vector3(0, floorY + 3.65f, -8f), new Vector3(56f, 0.15f, 16f), matConcrete, true, "Floor 2 Ceiling");

            // Corridor Partition Wall along Z = -6
            float[] openings = { -20f, -6f, 6f, 20f };
            float lastX = -28f;
            foreach (var ox in openings)
            {
                float w = (ox - 1.2f) - lastX;
                if (w > 0.5f) CreateBox(floor2, new Vector3(lastX + w * 0.5f, floorY + 1.8f, -6f), new Vector3(w, 3.6f, 0.25f), matPlaster, true);
                lastX = ox + 1.2f;
            }
            if (28f - lastX > 0.5f) CreateBox(floor2, new Vector3((lastX + 28f) * 0.5f, floorY + 1.8f, -6f), new Vector3(28f - lastX, 3.6f, 0.25f), matPlaster, true);

            // Room dividers on 2nd floor
            CreateBox(floor2, new Vector3(-14f, floorY + 1.8f, -11f), new Vector3(0.25f, 3.6f, 10f), matPlaster, true);
            CreateBox(floor2, new Vector3(0f, floorY + 1.8f, -11f), new Vector3(0.25f, 3.6f, 10f), matPlaster, true);
            CreateBox(floor2, new Vector3(14f, floorY + 1.8f, -11f), new Vector3(0.25f, 3.6f, 10f), matPlaster, true);

            // SCIENCE LAB (Кабінет хімії та біології) at X: -28 to -14
            BuildScienceLab(floor2, -21f, floorY);

            // SCHOOL LIBRARY (Шкільна бібліотека) at X: -14 to 0
            BuildLibrary(floor2, -7f, floorY);

            // AUDIO / BROADCAST ROOM (Радіорубка) at X: 14 to 28
            BuildBroadcastRoom(floor2, 21f, floorY);

            // 2nd Floor Corridor Lamps
            for (float hx = -22f; hx <= 22f; hx += 8f)
            {
                CreateInteriorLamp(floor2, new Vector3(hx, floorY + 3.55f, -3f));
            }
        }

        void BuildScienceLab(Transform parent, float centerX, float floorY)
        {
            var lab = new GameObject("Science Lab (理科室)").transform;
            lab.SetParent(parent, false);

            // Dark Stone Chemistry Lab Benches with Sinks
            for (int r = 0; r < 2; r++)
            {
                float lz = -13.5f + r * 3.2f;
                var bench = CreateBox(lab, new Vector3(centerX, floorY + 0.45f, lz), new Vector3(5.5f, 0.9f, 1.4f), matLabCounter, true, "Lab Bench");
                // Sink & water faucet
                CreateBox(bench.transform, new Vector3(2.2f, 0.45f, 0), new Vector3(0.8f, 0.2f, 0.6f), matDarkMetal, false, "Sink");
                CreateCylinder(bench.transform, new Vector3(2.2f, 0.7f, 0), 0.02f, 0.4f, matWhite, false, "Faucet");
                // Glass beakers & test tube rack
                CreateCylinder(bench.transform, new Vector3(-1.8f, 0.55f, 0.2f), 0.06f, 0.22f, matGlass, false, "Beaker 1");
                CreateCylinder(bench.transform, new Vector3(-1.2f, 0.55f, 0.2f), 0.08f, 0.25f, matGlass, false, "Beaker 2");
                CreateBox(bench.transform, new Vector3(0, 0.55f, -0.3f), new Vector3(0.45f, 0.18f, 0.15f), matWoodFloor, false, "Test Tubes");
            }

            // Human Skeleton Model Stand in the corner! (Photo classic horror element)
            var skel = new GameObject("Skeleton Stand").transform;
            skel.SetParent(lab, false);
            skel.localPosition = new Vector3(centerX + 5.5f, floorY, -15.2f);
            CreateCylinder(skel, new Vector3(0, 0.9f, 0), 0.025f, 1.8f, matDarkMetal, false, "Stand");
            CreateBox(skel, new Vector3(0, 1.6f, 0), new Vector3(0.24f, 0.28f, 0.24f), matWhite, false, "Skull");
            CreateBox(skel, new Vector3(0, 1.15f, 0), new Vector3(0.35f, 0.55f, 0.22f), matWhite, false, "Ribcage");
        }

        void BuildLibrary(Transform parent, float centerX, float floorY)
        {
            var lib = new GameObject("Library (図書室)").transform;
            lib.SetParent(parent, false);

            // Rows of tall wooden bookshelves packed with books
            for (int r = 0; r < 3; r++)
            {
                float bz = -15.5f + r * 2.8f;
                var shelf = CreateBox(lib, new Vector3(centerX - 2.5f, floorY + 1.4f, bz), new Vector3(4.8f, 2.8f, 0.6f), matWoodFloor, true, "Bookshelf");
                // Book rows
                for (int level = 0; level < 4; level++)
                {
                    CreateBox(shelf.transform, new Vector3(0, -1.0f + level * 0.65f, 0.05f), new Vector3(4.5f, 0.45f, 0.5f), matBooks, false, "Books");
                }
            }

            // Long study tables with reading lamps
            var table = CreateBox(lib, new Vector3(centerX + 3.2f, floorY + 0.4f, -11f), new Vector3(2.4f, 0.8f, 4.2f), matWoodFloor, true, "Study Table");
            for (float tz = -1.2f; tz <= 1.2f; tz += 1.2f)
            {
                var lamp = CreateBox(table.transform, new Vector3(0, 0.55f, tz), new Vector3(0.22f, 0.35f, 0.22f), matTorii, false, "Desk Lamp");
                var l = lamp.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.9f, 1.0f, 0.7f);
                l.intensity = 0.9f;
                l.range = 3.2f;
            }
        }

        void BuildBroadcastRoom(Transform parent, float centerX, float floorY)
        {
            var av = new GameObject("AV Room (放送室)").transform;
            av.SetParent(parent, false);

            // Audio Mixing Desk
            CreateBox(av, new Vector3(centerX, floorY + 0.45f, -14f), new Vector3(3.6f, 0.9f, 1.4f), matDarkMetal, true, "Console Desk");
            CreateBox(av, new Vector3(centerX, floorY + 1.15f, -14f), new Vector3(2.8f, 0.45f, 0.3f), matLabCounter, false, "Rack Equipment");
            // Speaker monitors
            CreateBox(av, new Vector3(centerX - 1.8f, floorY + 1.1f, -14f), new Vector3(0.35f, 0.55f, 0.35f), matDarkMetal, false, "Speaker L");
            CreateBox(av, new Vector3(centerX + 1.8f, floorY + 1.1f, -14f), new Vector3(0.35f, 0.55f, 0.35f), matDarkMetal, false, "Speaker R");
        }

        // ====================================================================
        // 8. CENTRAL STAIRWELL TO 2ND FLOOR (Request 4)
        // ====================================================================
        void BuildStairs()
        {
            var stairs = new GameObject("Central Stairwell Steps").transform;
            stairs.SetParent(Root, false);
            stairs.localPosition = new Vector3(0, 0, -4.5f);

            // Stair treads climbing from Y = 0 to Y = 3.8m
            int numSteps = 19;
            float stepRise = 3.8f / numSteps; // ~0.20m per step
            float stepRun = 0.30f;            // 30cm tread

            for (int i = 0; i < numSteps; i++)
            {
                float sy = (i + 0.5f) * stepRise;
                float sz = -i * stepRun;
                CreateBox(stairs, new Vector3(0, sy, sz), new Vector3(3.2f, stepRise, stepRun), matConcrete, true, "Stair Step " + i);
            }

            // Stair handrails
            CreateBox(stairs, new Vector3(-1.65f, 2.2f, -numSteps * stepRun * 0.5f), new Vector3(0.08f, 0.9f, numSteps * stepRun), matDarkMetal, false, "Handrail L");
            CreateBox(stairs, new Vector3(1.65f, 2.2f, -numSteps * stepRun * 0.5f), new Vector3(0.08f, 0.9f, numSteps * stepRun), matDarkMetal, false, "Handrail R");
        }

        void CreateInteriorLamp(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Hallway Lamp").transform;
            go.SetParent(parent, false);
            go.localPosition = pos;
            CreateBox(go, Vector3.zero, new Vector3(1.4f, 0.08f, 0.3f), matWhite, false);
            var bulb = CreateBox(go, new Vector3(0, -0.04f, 0), new Vector3(1.2f, 0.04f, 0.22f), matPlaster, false);

            var l = go.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.95f, 0.84f);
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
        // 9. TRADITIONAL JAPANESE PARK ACROSS THE ROAD (Request 1)
        // ====================================================================
        void BuildTraditionalPark()
        {
            var park = new GameObject("Traditional Japanese Park").transform;
            park.SetParent(Root, false);

            // Park moss/grass ground (Z: 50 to 110, X: -45 to 45)
            CreateBox(park, new Vector3(0, -0.05f, 80f), new Vector3(90f, 0.1f, 60f), matFoliage, true, "Park Moss Ground");

            // Park Entrance Stone Wall & Pillars (at Z = 50)
            for (int side = -1; side <= 1; side += 2)
            {
                float wallW = 38f;
                float midX = side * (5.5f + wallW * 0.5f);
                CreateBox(park, new Vector3(midX, 0.65f, 50f), new Vector3(wallW, 1.3f, 0.45f), matStone, true, "Park Stone Wall");
                CreateBox(park, new Vector3(side * 5.5f, 1.2f, 50f), new Vector3(0.9f, 2.4f, 0.9f), matStone, true, "Park Gate Post");
            }

            // Stone Paved Main Pathway (石畳 - ishidatami) leading straight from zebra crosswalk
            CreateBox(park, new Vector3(0, 0.02f, 68f), new Vector3(4.8f, 0.02f, 36f), matStone, false, "Stone Path Main");
            CreateBox(park, new Vector3(-14f, 0.02f, 80f), new Vector3(20f, 0.02f, 3.4f), matStone, false, "Stone Path Left");
            CreateBox(park, new Vector3(14f, 0.02f, 80f), new Vector3(20f, 0.02f, 3.4f), matStone, false, "Stone Path Right");
            CreateBox(park, new Vector3(0, 0.02f, 96f), new Vector3(5.2f, 0.02f, 24f), matStone, false, "Shrine Approach Path");

            // 16 Ancient Gnarled Japanese Park Trees with multi-tier crowns
            Vector2[] treeCoords = {
                new Vector2(-16, 56), new Vector2(16, 56),
                new Vector2(-26, 68), new Vector2(26, 68),
                new Vector2(-12, 78), new Vector2(12, 78),
                new Vector2(-24, 88), new Vector2(24, 88),
                new Vector2(-8, 98), new Vector2(8, 98),
                new Vector2(-30, 82), new Vector2(30, 82),
                new Vector2(-20, 62), new Vector2(20, 62),
                new Vector2(-34, 98), new Vector2(34, 98)
            };
            foreach (var tc in treeCoords)
            {
                BuildAncientTree(park, new Vector3(tc.x, 0, tc.y));
            }

            // 8 Stone Toro Lanterns (石灯籠) lining the pathway with warm flickering candles
            Vector2[] toroCoords = {
                new Vector2(-3.4f, 54), new Vector2(3.4f, 54),
                new Vector2(-3.6f, 68), new Vector2(3.6f, 68),
                new Vector2(-3.6f, 82), new Vector2(3.6f, 82),
                new Vector2(-3.6f, 94), new Vector2(3.6f, 94)
            };
            foreach (var loc in toroCoords)
            {
                BuildStoneLantern(park, new Vector3(loc.x, 0, loc.y));
            }

            // Grand Vermilion Torii Gate (北野神社) at Z = 86
            BuildToriiGate(park, new Vector3(0, 0, 86f));

            // Shinto Shrine Hall (Z = 100)
            BuildShintoShrine(park, new Vector3(0, 0, 100f));

            // Japanese Wooden Rest Gazebo / Pavilion (四阿 - Azumaya) at X: 16, Z: 72
            BuildAzumayaGazebo(park, new Vector3(16f, 0, 72f));

            // Park Benches
            BuildParkBench(park, new Vector3(-14f, 0, 64f), 45);
            BuildParkBench(park, new Vector3(-16f, 0, 84f), 90);
            BuildParkBench(park, new Vector3(18f, 0, 86f), -90);

            // Street & Park Flickering Lamp Posts ("працюють через раз")
            Vector3[] lampPositions = {
                new Vector3(-12f, 0, 48.5f),
                new Vector3(12f, 0, 48.5f), // Flaky
                new Vector3(-6f, 0, 60f),
                new Vector3(6f, 0, 60f),    // Flaky
                new Vector3(-7f, 0, 75f),   // Flaky
                new Vector3(7f, 0, 75f),
                new Vector3(-5f, 0, 90f),
                new Vector3(5f, 0, 90f)     // Flaky
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
            tree.localPosition = pos;

            CreateCylinder(tree, new Vector3(0, 3.2f, 0), 0.85f, 6.4f, matBark, true, "Trunk");
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                CreateBox(tree, new Vector3(Mathf.Sin(angle) * 0.95f, 0.6f, Mathf.Cos(angle) * 0.95f), new Vector3(0.9f, 1.2f, 0.9f), matBark, true);
            }

            for (int i = 0; i < 5; i++)
            {
                float ang = (i / 5f) * Mathf.PI * 2f + 0.35f;
                Vector3 branchPos = new Vector3(Mathf.Sin(ang) * 2.2f, 6.0f + (i % 2) * 0.9f, Mathf.Cos(ang) * 2.2f);
                CreateCylinder(tree, branchPos, 0.38f, 4.4f, matBark, false);
                Vector3 crownPos = new Vector3(Mathf.Sin(ang) * 3.6f, 7.8f + (i % 2) * 1.0f, Mathf.Cos(ang) * 3.6f);
                CreateBox(tree, crownPos, new Vector3(4.8f, 3.2f, 4.8f), matFoliage, false);
            }
            CreateBox(tree, new Vector3(0, 9.6f, 0), new Vector3(7.0f, 4.2f, 7.0f), matFoliage, false);
        }

        void BuildStoneLantern(Transform parent, Vector3 pos)
        {
            var toro = new GameObject("Stone Toro").transform;
            toro.SetParent(parent, false);
            toro.localPosition = pos;

            CreateBox(toro, new Vector3(0, 0.15f, 0), new Vector3(0.95f, 0.3f, 0.95f), matStone, true);
            CreateCylinder(toro, new Vector3(0, 0.85f, 0), 0.26f, 1.1f, matStone, true);
            CreateBox(toro, new Vector3(0, 1.45f, 0), new Vector3(0.88f, 0.18f, 0.88f), matStone, true);
            // Fire box & candle light
            CreateBox(toro, new Vector3(0, 1.8f, 0), new Vector3(0.58f, 0.55f, 0.58f), matDarkMetal, true);
            CreateBox(toro, new Vector3(0, 2.2f, 0), new Vector3(1.1f, 0.22f, 1.1f), matStone, true);
            CreateBox(toro, new Vector3(0, 2.4f, 0), new Vector3(0.65f, 0.18f, 0.65f), matStone, false);

            var l = toro.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.68f, 0.28f);
            l.intensity = 1.5f;
            l.range = 5.2f;
        }

        void BuildToriiGate(Transform parent, Vector3 pos)
        {
            var torii = new GameObject("Grand Torii Gate").transform;
            torii.SetParent(parent, false);
            torii.localPosition = pos;

            for (int side = -1; side <= 1; side += 2)
            {
                CreateCylinder(torii, new Vector3(side * 3.8f, 0.4f, 0), 0.52f, 0.8f, matDarkMetal, true);
                CreateCylinder(torii, new Vector3(side * 3.8f, 4.2f, 0), 0.40f, 6.8f, matTorii, true);
            }
            // Beams
            CreateBox(torii, new Vector3(0, 5.6f, 0), new Vector3(8.8f, 0.45f, 0.45f), matTorii, true);
            CreateBox(torii, new Vector3(0, 7.2f, 0), new Vector3(10.8f, 0.6f, 0.6f), matTorii, true);
            CreateBox(torii, new Vector3(0, 7.6f, 0), new Vector3(11.2f, 0.2f, 0.7f), matDarkMetal, false);
        }

        void BuildShintoShrine(Transform parent, Vector3 pos)
        {
            var shrine = new GameObject("Shinto Shrine").transform;
            shrine.SetParent(parent, false);
            shrine.localPosition = pos;

            CreateBox(shrine, new Vector3(0, 2.2f, 0), new Vector3(5.6f, 4.4f, 5.0f), matWoodFloor, true, "Shrine Hall");
            CreateBox(shrine, new Vector3(0, 4.7f, 0), new Vector3(7.2f, 0.6f, 6.2f), matKawaraRoof, true);
            CreateBox(shrine, new Vector3(0, 5.3f, 0), new Vector3(5.8f, 0.7f, 5.0f), matKawaraRoof, false);
            CreateBox(shrine, new Vector3(0, 0.8f, -2.8f), new Vector3(1.6f, 0.9f, 1.0f), matWoodFloor, true, "Offering Box");
        }

        void BuildAzumayaGazebo(Transform parent, Vector3 pos)
        {
            var gaz = new GameObject("Azumaya Gazebo (四阿)").transform;
            gaz.SetParent(parent, false);
            gaz.localPosition = pos;

            CreateBox(gaz, new Vector3(0, 0.25f, 0), new Vector3(4.8f, 0.5f, 4.8f), matWoodFloor, true);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    CreateCylinder(gaz, new Vector3(sx * 2.0f, 1.8f, sz * 2.0f), 0.12f, 3.2f, matWoodFloor, false);
                }
            }
            CreateBox(gaz, new Vector3(0, 3.6f, 0), new Vector3(5.8f, 0.5f, 5.8f), matKawaraRoof, true);
            CreateBox(gaz, new Vector3(0, 4.2f, 0), new Vector3(4.2f, 0.7f, 4.2f), matKawaraRoof, false);
        }

        void BuildParkBench(Transform parent, Vector3 pos, float yaw)
        {
            var bench = new GameObject("Park Bench").transform;
            bench.SetParent(parent, false);
            bench.localPosition = pos;
            bench.localRotation = Quaternion.Euler(0, yaw, 0);

            CreateBox(bench, new Vector3(0, 0.45f, 0), new Vector3(2.2f, 0.08f, 0.6f), matWoodFloor, true);
            CreateBox(bench, new Vector3(0, 0.88f, 0.26f), new Vector3(2.2f, 0.45f, 0.06f), matWoodFloor, false);
            CreateCylinder(bench, new Vector3(-0.9f, 0.22f, 0), 0.035f, 0.45f, matDarkMetal, false);
            CreateCylinder(bench, new Vector3(0.9f, 0.22f, 0), 0.035f, 0.45f, matDarkMetal, false);
        }

        void BuildStreetLamp(Transform parent, Vector3 pos, bool worksNormally)
        {
            var pole = new GameObject("Street Lamp").transform;
            pole.SetParent(parent, false);
            pole.localPosition = pos;

            CreateCylinder(pole, new Vector3(0, 3.0f, 0), 0.09f, 6.0f, matDarkMetal, true);
            CreateBox(pole, new Vector3(0.42f, 5.8f, 0), new Vector3(0.9f, 0.08f, 0.08f), matDarkMetal, false);
            CreateBox(pole, new Vector3(0.85f, 5.65f, 0), new Vector3(0.45f, 0.25f, 0.45f), matDarkMetal, false);
            var bulb = CreateBox(pole, new Vector3(0.85f, 5.48f, 0), new Vector3(0.35f, 0.06f, 0.35f), matWhite, false);

            var l = pole.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.90f, 0.68f);
            l.intensity = worksNormally ? 3.0f : 1.4f;
            l.range = 15f;
            l.shadows = LightShadows.Soft;

            lamps.Add(new FlickeringLamp
            {
                light = l,
                bulb = bulb.GetComponent<Renderer>(),
                baseIntensity = 3.0f,
                worksNormally = worksNormally,
                flickerOffset = UnityEngine.Random.value * 50f
            });
        }

        // ====================================================================
        // 10. JOURNALIST NEWS VAN (Exit & Spawn)
        // ====================================================================
        void BuildNewsVan()
        {
            var van = new GameObject("Journalist News Van").transform;
            van.SetParent(Root, false);
            van.localPosition = new Vector3(0, 0, 43f); // Parked on the street right at the crosswalk
            van.localRotation = Quaternion.Euler(0, 180, 0);

            CreateBox(van, new Vector3(0, 1.05f, 0), new Vector3(2.4f, 1.4f, 5.2f), matWhite, true, "Van Body");
            CreateBox(van, new Vector3(0, 1.85f, -0.4f), new Vector3(2.35f, 1.2f, 3.6f), matWhite, true, "Van Cabin");
            CreateBox(van, new Vector3(0, 1.75f, 1.75f), new Vector3(2.25f, 0.95f, 0.95f), matGlass, false, "Windshield");

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var w = CreateCylinder(van, new Vector3(sx * 1.25f, 0.4f, sz * 1.5f), 0.4f, 0.28f, matDarkMetal, true);
                    w.transform.rotation = Quaternion.Euler(0, 0, 90);
                }
            }
            CreateCylinder(van, new Vector3(0, 2.65f, -0.9f), 0.05f, 0.7f, matDarkMetal, false);
            var dish = CreateCylinder(van, new Vector3(0, 3.0f, -0.9f), 0.65f, 0.14f, matWhite, false);
            dish.transform.rotation = Quaternion.Euler(25, 0, 0);

            // Headlights shining along the street
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var hl = new GameObject("Headlight").transform;
                hl.SetParent(van, false);
                hl.localPosition = new Vector3(sx * 0.85f, 0.85f, 2.65f);
                var spot = hl.gameObject.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = new Color(1.0f, 0.96f, 0.85f);
                spot.intensity = 5.0f;
                spot.range = 24f;
                spot.spotAngle = 48f;
            }
        }

        // ====================================================================
        // 11. 5 SANITY PILLS & 5 JOURNALIST EVIDENCE TAPES (Request 6)
        // ====================================================================
        void SpawnSanityPills()
        {
            Vector3[] pillPositions = {
                new Vector3(-21f, 0.85f, -12.5f), // Class 1-A desk
                new Vector3(21f, 0.95f, -12.0f),  // Nurse's office table
                new Vector3(-21f, 4.65f, -13.5f), // 2nd Floor Science Lab counter
                new Vector3(-7f, 4.65f, -11.0f),  // 2nd Floor Library table
                new Vector3(0f, 0.90f, 98.0f)     // Shinto Shrine in Park
            };

            for (int i = 0; i < pillPositions.Length; i++)
            {
                var pos = pillPositions[i];
                var pillGo = new GameObject("SanityPill_0" + (i + 1));
                pillGo.transform.SetParent(Root, false);
                pillGo.transform.localPosition = pos;

                var amberMat = CreateMat("PillAmber", matPlaster.shader, new Color(0.95f, 0.65f, 0.20f), 0.5f);
                CreateCylinder(pillGo.transform, new Vector3(0, 0.09f, 0), 0.07f, 0.18f, amberMat, true, "Bottle");
                CreateCylinder(pillGo.transform, new Vector3(0, 0.19f, 0), 0.06f, 0.05f, matWhite, false, "Cap");

                var aura = CreateBox(pillGo.transform, new Vector3(0, 0.1f, 0), new Vector3(0.18f, 0.25f, 0.18f), matPlaster, false, "Aura");
                var l = pillGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.4f, 0.88f, 1.0f);
                l.intensity = 1.8f;
                l.range = 3.5f;

                pills.Add(new PillItem
                {
                    id = "pill-" + (i + 1),
                    go = pillGo,
                    position = pos,
                    collected = false
                });
            }
        }

        void SpawnEvidenceTapes()
        {
            var tapeDefs = new[] {
                new { id = "tape-1", label = "Касета 1: Щоденник чергового учня", pos = new Vector3(-7f, 0.85f, -12.5f) },     // Class 1-B
                new Vector3(23f, 0.85f, -14.5f) != null ? new { id = "tape-2", label = "Касета 2: Звіт шкільної медсестри", pos = new Vector3(23f, 0.85f, -14.5f) } : null, // Nurse Office
                new { id = "tape-3", label = "Касета 3: Запис гуртка окультизму", pos = new Vector3(-21f, 4.65f, -15.0f) },   // Science Lab
                new { id = "tape-4", label = "Касета 4: Старий архів про інцидент", pos = new Vector3(-7f, 4.65f, -15.5f) },    // Library
                new { id = "tape-5", label = "Касета 5: Останній репортаж журналіста", pos = new Vector3(16f, 0.75f, 72f) }    // Park Gazebo
            };

            foreach (var def in tapeDefs)
            {
                var tapeGo = new GameObject("EvidenceTape_" + def.id);
                tapeGo.transform.SetParent(Root, false);
                tapeGo.transform.localPosition = def.pos;

                // 3D Audio Cassette Model
                CreateBox(tapeGo.transform, new Vector3(0, 0.03f, 0), new Vector3(0.25f, 0.05f, 0.16f), matDarkMetal, true, "Cassette Body");
                CreateBox(tapeGo.transform, new Vector3(0, 0.06f, 0), new Vector3(0.20f, 0.015f, 0.09f), matWhite, false, "Cassette Label");

                var l = tapeGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1.0f, 0.85f, 0.35f);
                l.intensity = 1.6f;
                l.range = 3.2f;

                evidenceTapes.Add(new EvidenceTapeItem
                {
                    id = def.id,
                    label = def.label,
                    go = tapeGo,
                    position = def.pos,
                    collected = false
                });
            }
        }

        // ====================================================================
        // UPDATE TICK
        // ====================================================================
        public void Update(RoomState state, float dt)
        {
            if (state == null) return;
            float time = Time.time;

            // Update Flickering Street & Park Lamps
            for (int i = 0; i < lamps.Count; i++)
            {
                var lamp = lamps[i];
                if (lamp.light == null) continue;

                if (lamp.worksNormally)
                {
                    lamp.light.intensity = lamp.baseIntensity * (0.95f + Mathf.Sin((time + lamp.flickerOffset) * 2.5f) * 0.05f);
                }
                else
                {
                    float wave = Mathf.Sin((time + lamp.flickerOffset) * 12.3f) * Mathf.Cos((time + lamp.flickerOffset) * 3.7f);
                    bool on = wave > -0.2f && (Mathf.Sin((time + lamp.flickerOffset) * 1.8f) > -0.6f);
                    lamp.light.intensity = on ? lamp.baseIntensity * UnityEngine.Random.Range(0.6f, 1.1f) : 0f;
                    if (lamp.bulb != null) lamp.bulb.material.color = on ? Color.white : new Color(0.2f, 0.2f, 0.2f);
                }
            }

            // Update Sanity Pills Animation
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

            // Update Evidence Tapes Animation
            for (int i = 0; i < evidenceTapes.Count; i++)
            {
                var tape = evidenceTapes[i];
                if (tape.go == null) continue;
                bool isCollected = state.Collected(tape.id);
                if (isCollected != tape.collected)
                {
                    tape.collected = isCollected;
                    tape.go.SetActive(!isCollected);
                }
                if (!tape.collected)
                {
                    float bob = Mathf.Sin(time * 2.5f + i) * 0.03f;
                    tape.go.transform.localPosition = tape.position + new Vector3(0, bob, 0);
                }
            }
        }
    }
}
