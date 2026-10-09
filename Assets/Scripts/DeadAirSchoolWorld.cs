using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>
    /// Massive, authentic Japanese School, Residential Neighborhood, Two-Floor Interiors,
    /// Back Sports Yard & Forest, Road, and Traditional Park generator for Chapter 2.
    /// Features grand entrance Genkan, wide 5m central corridors with 2 classrooms on each side,
    /// authentic Japanese sliding doors with nameplates, and realistic switchback U-turn stairwell.
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
        Material matGold, matDoorWood, matShoeLocker;

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
            matGold = CreateMat("SignGold", shader, new Color(0.88f, 0.75f, 0.25f), 0.6f);
            matDoorWood = CreateMat("DoorWood", shader, new Color(0.68f, 0.52f, 0.34f), 0.25f);
            matShoeLocker = CreateMat("GetabakoWood", shader, new Color(0.58f, 0.42f, 0.26f), 0.2f);
        }

        static Material CreateMat(string name, Shader shader, Color color, float gloss)
        {
            var mat = new Material(shader) { name = name, color = color, hideFlags = HideFlags.DontSave };
            mat.SetFloat("_Glossiness", gloss);
            return mat;
        }

        static Material CreateChalkboardMaterial(Shader shader)
        {
            var tex = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            var green = new Color32(28, 56, 36, 255);
            var chalk = new Color32(215, 230, 210, 255);
            var pixels = new Color32[256 * 128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = green;

            for (int x = 20; x < 236; x++)
            {
                if (x % 3 == 0) { pixels[100 * 256 + x] = chalk; pixels[70 * 256 + x] = chalk; pixels[40 * 256 + x] = chalk; }
            }
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

        GameObject CreateRamp(Transform parent, Vector3 center, Vector3 size, float tiltAngleX, string name = "StairRamp")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(tiltAngleX, 0, 0);
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
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
        // 1. ROAD, SIDEWALKS & ZEBRA CROSSWALK
        // ====================================================================
        void BuildRoadAndCrosswalk()
        {
            var roadGroup = new GameObject("Street & Sidewalks").transform;
            roadGroup.SetParent(Root, false);

            CreateBox(roadGroup, new Vector3(0, -0.05f, 43f), new Vector3(120f, 0.1f, 10f), matAsphalt, true, "Main Street Asphalt");

            for (float x = -58f; x <= 58f; x += 4f)
            {
                CreateBox(roadGroup, new Vector3(x, 0.01f, 43f), new Vector3(2.4f, 0.01f, 0.18f), matTorii, false, "Yellow Center Line");
            }

            CreateBox(roadGroup, new Vector3(0, 0.01f, 38.6f), new Vector3(120f, 0.01f, 0.18f), matWhite, false);
            CreateBox(roadGroup, new Vector3(0, 0.01f, 47.4f), new Vector3(120f, 0.01f, 0.18f), matWhite, false);

            for (float zx = -3.5f; zx <= 3.5f; zx += 1.2f)
            {
                CreateBox(roadGroup, new Vector3(zx, 0.015f, 43f), new Vector3(0.65f, 0.01f, 8.8f), matRoadZebra, false, "Zebra Stripe");
            }

            CreateBox(roadGroup, new Vector3(0, 0.09f, 37f), new Vector3(120f, 0.18f, 2f), matConcrete, true, "School Sidewalk");
            CreateBox(roadGroup, new Vector3(0, 0.09f, 49f), new Vector3(120f, 0.18f, 2f), matConcrete, true, "Park Sidewalk");

            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = 38.2f; z <= 47.8f; z += 9.6f)
                {
                    float startX = 4.5f, endX = 55f;
                    float len = endX - startX;
                    float midX = side * (startX + len * 0.5f);
                    CreateBox(roadGroup, new Vector3(midX, 0.75f, z), new Vector3(len, 0.05f, 0.05f), matGuardrail, false, "Guardrail Top");
                    CreateBox(roadGroup, new Vector3(midX, 0.45f, z), new Vector3(len, 0.05f, 0.05f), matGuardrail, false, "Guardrail Mid");
                    for (float px = startX; px <= endX; px += 3f)
                    {
                        CreateCylinder(roadGroup, new Vector3(side * px, 0.45f, z), 0.035f, 0.9f, matGuardrail, false, "Rail Post");
                    }
                }
            }

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
        // 2. RESIDENTIAL HOUSES ON LEFT AND RIGHT
        // ====================================================================
        void BuildLeftNeighborhood()
        {
            var left = new GameObject("Left Neighborhood").transform;
            left.SetParent(Root, false);

            CreateBox(left, new Vector3(-38f, 1.1f, 3f), new Vector3(0.35f, 2.2f, 94f), matBlockWall, true, "Left Perimeter Wall");

            float[] houseZ = { -28f, -10f, 8f, 26f, 44f };
            foreach (var hz in houseZ)
            {
                BuildJapaneseHouse(left, new Vector3(-48f, 0, hz), 0);
            }

            BuildUtilityPole(left, new Vector3(-39.5f, 0, 45f));
            BuildUtilityPole(left, new Vector3(-39.5f, 0, 15f));
            BuildUtilityPole(left, new Vector3(-39.5f, 0, -15f));
        }

        void BuildRightNeighborhood()
        {
            var right = new GameObject("Right Neighborhood").transform;
            right.SetParent(Root, false);

            CreateBox(right, new Vector3(38f, 1.1f, 3f), new Vector3(0.35f, 2.2f, 94f), matBlockWall, true, "Right Perimeter Wall");

            float[] houseZ = { -28f, -10f, 8f, 26f, 44f };
            foreach (var hz in houseZ)
            {
                BuildJapaneseHouse(right, new Vector3(48f, 0, hz), 180);
            }

            BuildUtilityPole(right, new Vector3(39.5f, 0, 45f));
            BuildUtilityPole(right, new Vector3(39.5f, 0, 15f));
            BuildUtilityPole(right, new Vector3(39.5f, 0, -15f));
        }

        void BuildJapaneseHouse(Transform parent, Vector3 pos, float yaw)
        {
            var house = new GameObject("Japanese House").transform;
            house.SetParent(parent, false);
            house.localPosition = pos;
            house.localRotation = Quaternion.Euler(0, yaw, 0);

            float w = 12f, h = 6.8f, d = 11f;
            CreateBox(house, new Vector3(0, 3.4f, 0), new Vector3(w, h, d), matHouseStucco, true, "House Body");
            CreateBox(house, new Vector3(0, 0.8f, 0), new Vector3(w + 0.1f, 1.6f, d + 0.1f), matBark, false, "House Wood Skirt");

            CreateBox(house, new Vector3(0, 7.2f, 0), new Vector3(w + 1.6f, 0.7f, d + 1.6f), matKawaraRoof, true, "Kawara Roof Base");
            CreateBox(house, new Vector3(0, 8.0f, 0), new Vector3(w * 0.7f, 1.0f, d * 0.7f), matKawaraRoof, false, "Kawara Roof Mid");
            CreateBox(house, new Vector3(0, 8.7f, 0), new Vector3(w * 0.4f, 0.6f, d * 0.4f), matKawaraRoof, false, "Kawara Roof Top");

            CreateBox(house, new Vector3(0, 3.8f, d * 0.5f + 0.6f), new Vector3(w * 0.7f, 0.15f, 1.2f), matConcrete, true, "Balcony Floor");
            CreateBox(house, new Vector3(0, 4.3f, d * 0.5f + 1.15f), new Vector3(w * 0.7f, 0.8f, 0.1f), matDarkMetal, false, "Balcony Rail");

            for (int f = 0; f < 2; f++)
            {
                float wy = 1.8f + f * 3.4f;
                var win = CreateBox(house, new Vector3(2.5f, wy, d * 0.5f + 0.05f), new Vector3(2.2f, 1.4f, 0.1f), matWhite, false, "Window Frame");
                CreateBox(win.transform, new Vector3(0, 0, 0.02f), new Vector3(2.0f, 1.2f, 0.05f), matPlaster, false, "Lit Window");
                var l = win.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1.0f, 0.82f, 0.55f);
                l.intensity = 0.8f;
                l.range = 5f;
            }

            CreateBox(house, new Vector3(-3.5f, 0.5f, d * 0.5f + 0.35f), new Vector3(0.9f, 0.65f, 0.4f), matWhite, false, "AC Unit");
        }

        void BuildUtilityPole(Transform parent, Vector3 pos)
        {
            var pole = new GameObject("Utility Pole (電柱)").transform;
            pole.SetParent(parent, false);
            pole.localPosition = pos;
            CreateCylinder(pole, new Vector3(0, 5.0f, 0), 0.18f, 10.0f, matConcrete, true);
            CreateBox(pole, new Vector3(0, 8.8f, 0), new Vector3(2.4f, 0.1f, 0.1f), matDarkMetal, false);
            CreateBox(pole, new Vector3(0, 9.4f, 0), new Vector3(1.8f, 0.1f, 0.1f), matDarkMetal, false);
            CreateCylinder(pole, new Vector3(0.35f, 8.2f, 0), 0.28f, 0.9f, matDarkMetal, false);
        }

        // ====================================================================
        // 3. BACK SCHOOLYARD & DENSE FOREST
        // ====================================================================
        void BuildBackYardAndForest()
        {
            var back = new GameObject("Back Schoolyard & Forest").transform;
            back.SetParent(Root, false);

            CreateBox(back, new Vector3(0, -0.05f, -33f), new Vector3(72f, 0.1f, 22f), matConcrete, true, "Backyard Ground");

            var bikeShed = new GameObject("Bicycle Shed").transform;
            bikeShed.SetParent(back, false);
            bikeShed.localPosition = new Vector3(-18f, 0, -32f);
            CreateBox(bikeShed, new Vector3(0, 2.5f, 0), new Vector3(10f, 0.12f, 3.8f), matDarkMetal, true, "Shed Roof");
            for (float bx = -4.5f; bx <= 4.5f; bx += 4.5f)
            {
                CreateCylinder(bikeShed, new Vector3(bx, 1.25f, -1.6f), 0.05f, 2.5f, matDarkMetal, false);
                CreateCylinder(bikeShed, new Vector3(bx, 1.25f, 1.6f), 0.05f, 2.5f, matDarkMetal, false);
            }
            for (float bx = -4.2f; bx <= 4.2f; bx += 0.8f)
            {
                CreateBox(bikeShed, new Vector3(bx, 0.25f, 0), new Vector3(0.04f, 0.5f, 1.4f), matDarkMetal, false);
            }

            CreateBox(back, new Vector3(18f, 1.6f, -32f), new Vector3(5.2f, 3.2f, 3.6f), matDarkMetal, true, "Storage Lockup");

            var inc = CreateBox(back, new Vector3(26f, 1.2f, -35f), new Vector3(1.8f, 2.4f, 1.8f), matConcrete, true, "Incinerator");
            CreateCylinder(inc.transform, new Vector3(0, 2.0f, 0), 0.22f, 2.2f, matDarkMetal, false, "Chimney");

            CreateBox(back, new Vector3(0, 2.0f, -44f), new Vector3(74f, 4.0f, 0.15f), matDarkMetal, true, "Back Sports Fence");

            CreateBox(back, new Vector3(0, -0.05f, -67f), new Vector3(96f, 0.1f, 46f), matFoliage, true, "Forest Floor");

            var rng = new System.Random(88812);
            for (int i = 0; i < 32; i++)
            {
                float fx = (float)(rng.NextDouble() * 84.0 - 42.0);
                float fz = (float)(rng.NextDouble() * 38.0 - 83.0);
                BuildForestTree(back, new Vector3(fx, 0, fz));
            }
        }

        void BuildForestTree(Transform parent, Vector3 pos)
        {
            var t = new GameObject("Dense Forest Tree").transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            CreateCylinder(t, new Vector3(0, 4.5f, 0), 0.45f, 9.0f, matBark, true);
            CreateCylinder(t, new Vector3(0, 9.0f, 0), 2.8f, 4.5f, matFoliage, false);
            CreateCylinder(t, new Vector3(0, 12.0f, 0), 2.0f, 3.8f, matFoliage, false);
            CreateCylinder(t, new Vector3(0, 14.5f, 0), 1.2f, 2.8f, matFoliage, false);
        }

        // ====================================================================
        // 4. SCHOOL COURTYARD
        // ====================================================================
        void BuildSchoolCourtyard()
        {
            var yard = new GameObject("School Courtyard").transform;
            yard.SetParent(Root, false);

            CreateBox(yard, new Vector3(0, -0.05f, 18f), new Vector3(72f, 0.1f, 36f), matAsphalt, true, "Courtyard Asphalt");

            CreateBox(yard, new Vector3(0, 0.02f, 18f), new Vector3(6.5f, 0.02f, 36f), matConcrete, false, "Approach Path");

            var yellowMat = CreateMat("CircleYellow", matPlaster.shader, new Color(0.92f, 0.78f, 0.20f), 0.2f);
            var blueMat = CreateMat("CircleBlue", matPlaster.shader, new Color(0.24f, 0.60f, 0.88f), 0.2f);
            var redMat = CreateMat("CircleRed", matPlaster.shader, new Color(0.88f, 0.26f, 0.22f), 0.2f);
            var greenMat = CreateMat("CircleGreen", matPlaster.shader, new Color(0.26f, 0.82f, 0.36f), 0.2f);

            CreateRing(yard, new Vector3(8f, 0.015f, 14f), 2.8f, yellowMat);
            CreateRing(yard, new Vector3(-10f, 0.015f, 18f), 2.0f, blueMat);
            CreateRing(yard, new Vector3(14f, 0.015f, 22f), 1.6f, redMat);
            CreateRing(yard, new Vector3(-16f, 0.015f, 12f), 3.4f, yellowMat);
            CreateRing(yard, new Vector3(20f, 0.015f, 15f), 1.4f, greenMat);
            CreateRing(yard, new Vector3(-8f, 0.015f, 26f), 2.3f, blueMat);

            var coneMat = CreateMat("ConeOrange", matPlaster.shader, new Color(0.95f, 0.35f, 0.10f), 0.3f);
            CreateTrafficCone(yard, new Vector3(14f, 0f, 28f), coneMat);
            CreateTrafficCone(yard, new Vector3(16f, 0f, 26f), coneMat);
            CreateTrafficCone(yard, new Vector3(-12f, 0f, 32f), coneMat);

            for (int side = -1; side <= 1; side += 2)
            {
                float wallWidth = 30f;
                float midX = side * (5.5f + wallWidth * 0.5f);
                CreateBox(yard, new Vector3(midX, 1.1f, 36f), new Vector3(wallWidth, 2.2f, 0.35f), matConcrete, true, "Front Wall");
                for (float fx = 6f; fx < 35f; fx += 1.0f)
                {
                    CreateCylinder(yard, new Vector3(side * fx, 2.6f, 36f), 0.025f, 0.8f, matDarkMetal, false);
                }
            }

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
        // 5. SCHOOL EXTERIOR STRUCTURE & GRAND GENKAN ENTRANCE
        // ====================================================================
        void BuildSchoolBuilding()
        {
            var bldg = new GameObject("School Outer Structure").transform;
            bldg.SetParent(Root, false);

            float width = 72f, height = 7.6f, depth = 22f;

            // Roof Parapet and Slab (Y = 7.4 to 8.2)
            CreateBox(bldg, new Vector3(0, 7.55f, -depth * 0.5f), new Vector3(width, 0.25f, depth), matConcrete, true, "Roof Slab");
            CreateBox(bldg, new Vector3(0, 7.95f, 0.1f), new Vector3(width + 0.6f, 0.6f, 0.35f), matConcrete, true, "Roof Parapet South");
            CreateBox(bldg, new Vector3(0, 7.95f, -depth - 0.1f), new Vector3(width + 0.6f, 0.6f, 0.35f), matConcrete, true, "Roof Parapet North");
            CreateBox(bldg, new Vector3(-width * 0.5f - 0.1f, 7.95f, -depth * 0.5f), new Vector3(0.35f, 0.6f, depth + 0.6f), matConcrete, true, "Roof Parapet West");
            CreateBox(bldg, new Vector3(width * 0.5f + 0.1f, 7.95f, -depth * 0.5f), new Vector3(0.35f, 0.6f, depth + 0.6f), matConcrete, true, "Roof Parapet East");

            // West Exterior Wall (X = -36)
            CreateBox(bldg, new Vector3(-36f, height * 0.5f, -depth * 0.5f), new Vector3(0.4f, height, depth), matPlaster, true, "Exterior Wall West");
            // East Exterior Wall (X = +36)
            CreateBox(bldg, new Vector3(36f, height * 0.5f, -depth * 0.5f), new Vector3(0.4f, height, depth), matPlaster, true, "Exterior Wall East");

            // South Exterior Facade (Z = 0)
            CreateBox(bldg, new Vector3(-20.5f, height * 0.5f, 0f), new Vector3(31f, height, 0.35f), matPlaster, true, "South Facade Left Wing");
            CreateBox(bldg, new Vector3(20.5f, height * 0.5f, 0f), new Vector3(31f, height, 0.35f), matPlaster, true, "South Facade Right Wing");

            // Center Facade 2nd floor wall (Y: 3.8 to 7.6 at X: -5 to +5)
            CreateBox(bldg, new Vector3(0, 5.7f, 0f), new Vector3(10f, 3.8f, 0.35f), matPlaster, true, "South Facade Center 2F");
            // Round Clock on Center Facade 2F
            var clock = CreateCylinder(bldg, new Vector3(0, 6.2f, 0.22f), 0.85f, 0.12f, matDarkMetal, false, "Clock");
            clock.transform.rotation = Quaternion.Euler(90, 0, 0);
            CreateCylinder(clock.transform, new Vector3(0, 0.07f, 0), 0.76f, 0.03f, matWhite, false);

            // Exterior Classroom Windows along South Facade
            for (int floor = 0; floor < 2; floor++)
            {
                float wy = 2.2f + floor * 3.6f;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int w = 0; w < 6; w++)
                    {
                        float wx = side * (8.5f + w * 4.6f);
                        CreateBox(bldg, new Vector3(wx, wy, 0.22f), new Vector3(3.6f, 2.0f, 0.12f), matWhite, false);
                        CreateBox(bldg, new Vector3(wx, wy, 0.26f), new Vector3(3.3f, 1.8f, 0.04f), matGlass, false);
                    }
                }
            }

            // North Exterior Facade (Z = -22)
            CreateBox(bldg, new Vector3(-20.5f, height * 0.5f, -22f), new Vector3(31f, height, 0.35f), matPlaster, true, "North Facade Left Wing");
            CreateBox(bldg, new Vector3(20.5f, height * 0.5f, -22f), new Vector3(31f, height, 0.35f), matPlaster, true, "North Facade Right Wing");
            // North Windows along classrooms
            for (int floor = 0; floor < 2; floor++)
            {
                float wy = 2.2f + floor * 3.6f;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int w = 0; w < 6; w++)
                    {
                        float wx = side * (8.5f + w * 4.6f);
                        CreateBox(bldg, new Vector3(wx, wy, -22.22f), new Vector3(3.6f, 2.0f, 0.12f), matWhite, false);
                        CreateBox(bldg, new Vector3(wx, wy, -22.26f), new Vector3(3.3f, 1.8f, 0.04f), matGlass, false);
                    }
                }
            }

            // Center Stairwell Grand North Glass Window (overlooking forest)
            CreateBox(bldg, new Vector3(0, 4.2f, -22.05f), new Vector3(8.5f, 6.8f, 0.12f), matWhite, false, "Stairwell Window Frame");
            CreateBox(bldg, new Vector3(0, 4.2f, -22.08f), new Vector3(8.2f, 6.5f, 0.04f), matGlass, false, "Stairwell Window Glass");
            for (float mx = -3.2f; mx <= 3.2f; mx += 1.6f)
            {
                CreateBox(bldg, new Vector3(mx, 4.2f, -22.12f), new Vector3(0.08f, 6.5f, 0.08f), matDarkMetal, false);
            }

            // Planter boxes with shrubs along South facade
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                {
                    float px = side * (8.5f + i * 5.6f);
                    CreateBox(bldg, new Vector3(px, 0.35f, 1.4f), new Vector3(4.2f, 0.7f, 1.0f), matConcrete, true, "Planter");
                    CreateBox(bldg, new Vector3(px, 0.9f, 1.4f), new Vector3(3.8f, 0.8f, 0.8f), matFoliage, false, "Shrub");
                    if (i == 1 || i == 3)
                    {
                        CreateCylinder(bldg, new Vector3(px + 1.2f, 2.0f, 1.4f), 0.22f, 2.8f, matBark, true);
                        CreateBox(bldg, new Vector3(px + 1.2f, 3.5f, 1.4f), new Vector3(2.4f, 0.8f, 2.4f), matFoliage, false);
                    }
                }
            }

            BuildSchoolEntrance(bldg);
        }

        // ====================================================================
        // GRAND SCHOOL ENTRANCE (GENKAN) - Authentic Japanese Architecture
        // ====================================================================
        void BuildSchoolEntrance(Transform parent)
        {
            var ent = new GameObject("School Entrance (玄関)").transform;
            ent.SetParent(parent, false);

            // Porch Platform (Z: 0 to 4.2, X: -5 to +5, Y: 0 to 0.45)
            CreateBox(ent, new Vector3(0, 0.225f, 2.1f), new Vector3(10.5f, 0.45f, 4.2f), matConcrete, true, "Porch Landing");

            // 3 Wide Porch Steps leading up from courtyard ground (Y = 0 to 0.45)
            CreateBox(ent, new Vector3(0, 0.12f, 4.8f), new Vector3(10.5f, 0.15f, 0.6f), matConcrete, true, "Porch Step 1");
            CreateBox(ent, new Vector3(0, 0.27f, 4.5f), new Vector3(10.5f, 0.15f, 0.6f), matConcrete, true, "Porch Step 2");

            // Overhead Concrete Canopy Roof (Y = 3.8m, depth 4.8m, width 11m)
            CreateBox(ent, new Vector3(0, 3.95f, 2.4f), new Vector3(11.2f, 0.35f, 4.8f), matConcrete, true, "Canopy Roof");
            // Heavy Concrete Support Pillars
            CreateBox(ent, new Vector3(-4.8f, 1.9f, 4.2f), new Vector3(0.7f, 3.8f, 0.7f), matConcrete, true, "Canopy Pillar L");
            CreateBox(ent, new Vector3(4.8f, 1.9f, 4.2f), new Vector3(0.7f, 3.8f, 0.7f), matConcrete, true, "Canopy Pillar R");

            // Japanese School Nameplate Plaque (私立 夜桜学園高等学校)
            var plaque = CreateBox(ent, new Vector3(0, 3.25f, 0.22f), new Vector3(4.8f, 0.65f, 0.08f), matDarkMetal, false, "School Plaque");
            CreateBox(plaque.transform, new Vector3(0, 0, 0.05f), new Vector3(4.5f, 0.48f, 0.04f), matGold, false, "Gold Lettering Plate");

            // Sliding Glass Double Entrance Facade at Z = 0
            // Outer Frame (X: -4.8 to +4.8)
            CreateBox(ent, new Vector3(-4.8f, 1.7f, 0.08f), new Vector3(0.4f, 3.4f, 0.18f), matDarkMetal, true);
            CreateBox(ent, new Vector3(4.8f, 1.7f, 0.08f), new Vector3(0.4f, 3.4f, 0.18f), matDarkMetal, true);
            CreateBox(ent, new Vector3(0, 3.25f, 0.08f), new Vector3(9.6f, 0.35f, 0.18f), matDarkMetal, false);

            // Left Fixed Glass Leaf (X: -4.6 to -2.0)
            CreateBox(ent, new Vector3(-3.3f, 1.6f, 0.08f), new Vector3(2.6f, 2.9f, 0.06f), matGlass, true, "Entrance Glass Fixed L");
            CreateBox(ent, new Vector3(-3.3f, 1.6f, 0.10f), new Vector3(2.6f, 0.08f, 0.08f), matDarkMetal, false);

            // Right Fixed Glass Leaf (X: +2.0 to +4.6)
            CreateBox(ent, new Vector3(3.3f, 1.6f, 0.08f), new Vector3(2.6f, 2.9f, 0.06f), matGlass, true, "Entrance Glass Fixed R");
            CreateBox(ent, new Vector3(3.3f, 1.6f, 0.10f), new Vector3(2.6f, 0.08f, 0.08f), matDarkMetal, false);

            // Center sliding doors: Left sliding door closed, Right sliding door slid open!
            // This leaves a grand 1.8m open entranceway at X: -0.2 to +1.6 for player entry!
            CreateBox(ent, new Vector3(-1.1f, 1.6f, 0.14f), new Vector3(1.8f, 2.9f, 0.06f), matGlass, true, "Entrance Sliding Door Closed");
            CreateBox(ent, new Vector3(2.2f, 1.6f, 0.14f), new Vector3(1.4f, 2.9f, 0.06f), matGlass, false, "Entrance Door Slid Open");

            // Genkan Vestibule Interior (Z: 0 to -8.5, X: -5 to +5)
            // Sunken Tiled Doma Entry Floor (Y = 0.45m, Z: 0 to -3.0)
            CreateBox(ent, new Vector3(0, 0.225f, -1.5f), new Vector3(9.6f, 0.45f, 3.0f), matConcrete, true, "Genkan Doma Floor");

            // Raised Wood Step Threshold (Shikidai / Agari-kamachi) rising to Y = 0.60m
            CreateBox(ent, new Vector3(0, 0.525f, -3.15f), new Vector3(9.6f, 0.15f, 0.35f), matWoodFloor, true, "Shikidai Threshold Step");

            // Raised Parquet Floor of Genkan Hall (Y = 0.60m, Z: -3.3 to -8.5)
            CreateBox(ent, new Vector3(0, 0.30f, -5.9f), new Vector3(9.6f, 0.60f, 5.2f), matWoodFloor, true, "Genkan Hall Floor");

            // Genkan Enclosing Partition Walls (X = -5.0 and X = +5.0)
            CreateBox(ent, new Vector3(-4.9f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Genkan Wall L");
            CreateBox(ent, new Vector3(4.9f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Genkan Wall R");

            // Authentic Getabako Shoe Locker Units (下駄箱) on Left and Right
            BuildShoeLockers(ent, new Vector3(-4.1f, 0.60f, -5.5f), 90);
            BuildShoeLockers(ent, new Vector3(4.1f, 0.60f, -5.5f), -90);

            // Umbrella Stands (傘立て)
            CreateBox(ent, new Vector3(-3.8f, 0.85f, -1.8f), new Vector3(0.6f, 0.8f, 1.4f), matDarkMetal, true, "Umbrella Stand L");
            CreateBox(ent, new Vector3(3.8f, 0.85f, -1.8f), new Vector3(0.6f, 0.8f, 1.4f), matDarkMetal, true, "Umbrella Stand R");

            // Soft warm Genkan ceiling light fixture
            CreateInteriorLamp(ent, new Vector3(0, 3.65f, -4.5f));
        }

        void BuildShoeLockers(Transform parent, Vector3 pos, float yaw)
        {
            var g = new GameObject("Getabako Shoe Lockers").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0, yaw, 0);

            float w = 5.2f, h = 1.9f, d = 0.65f;
            CreateBox(g, new Vector3(0, h * 0.5f, 0), new Vector3(w, h, d), matShoeLocker, true, "Locker Main Body");

            // Grid of shoe cubbies with white Uwabaki indoor slippers inside
            int rows = 4, cols = 10;
            float cellW = (w - 0.2f) / cols;
            float cellH = (h - 0.2f) / rows;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float cx = -w * 0.5f + 0.15f + c * cellW + cellW * 0.5f;
                    float cy = 0.15f + r * cellH + cellH * 0.5f;
                    CreateBox(g, new Vector3(cx, cy, 0.05f), new Vector3(cellW * 0.85f, cellH * 0.85f, d * 0.7f), matDarkMetal, false);
                    // Uwabaki slippers
                    if ((r + c) % 2 == 0)
                    {
                        CreateBox(g, new Vector3(cx - 0.08f, cy - 0.08f, 0.10f), new Vector3(0.09f, 0.05f, 0.22f), matWhite, false);
                        CreateBox(g, new Vector3(cx + 0.08f, cy - 0.08f, 0.10f), new Vector3(0.09f, 0.05f, 0.22f), matWhite, false);
                    }
                }
            }
        }

        // ====================================================================
        // 6. FIRST FLOOR INTERIORS: Wide 5m Corridor & 2 Classrooms Each Side
        // ====================================================================
        void BuildFirstFloorInteriors()
        {
            var floor1 = new GameObject("First Floor Interiors").transform;
            floor1.SetParent(Root, false);

            float floorY = 0.60f;
            float ceilY = 3.80f;

            // First Floor Parquet Slab (X: -36 to +36, Z: 0 to -22)
            CreateBox(floor1, new Vector3(0, floorY * 0.5f, -11f), new Vector3(72f, floorY, 22f), matWoodFloor, true, "Floor 1 Ground");
            // Ceiling Concrete Slab
            CreateBox(floor1, new Vector3(0, ceilY + 0.05f, -11f), new Vector3(72f, 0.15f, 22f), matConcrete, true, "Floor 1 Ceiling");

            // ====================================================================
            // 5-METER CENTRAL CORRIDOR (Z: -8.5 to -13.5, width 5.0m)
            // ====================================================================
            // Corridor ceiling lamps spaced across length
            for (float hx = -30f; hx <= 30f; hx += 10f)
            {
                CreateInteriorLamp(floor1, new Vector3(hx, ceilY - 0.15f, -11f));
            }

            // Green Emergency Exit Signs (誘導灯) glowing on hallway walls
            BuildExitSign(floor1, new Vector3(-20.5f, 2.9f, -8.65f));
            BuildExitSign(floor1, new Vector3(20.5f, 2.9f, -8.65f));
            BuildExitSign(floor1, new Vector3(-5.0f, 2.9f, -13.35f));

            // Fire Extinguisher Hydrant Cabinets (消火栓)
            BuildFireHydrant(floor1, new Vector3(-18f, 1.4f, -8.65f));
            BuildFireHydrant(floor1, new Vector3(18f, 1.4f, -8.65f));

            // Cork Bulletin Boards along corridor
            BuildNoticeBoard(floor1, new Vector3(-12f, 2.0f, -8.65f));
            BuildNoticeBoard(floor1, new Vector3(12f, 2.0f, -8.65f));

            // ====================================================================
            // SOUTH CORRIDOR PARTITION WALL (at Z = -8.5)
            // Separates 5m corridor from 4 South rooms: Class 1-A, 1-B, 1-C, Nurse
            // ====================================================================
            // Wall sections with door cutouts:
            // Doorway 1-A at X = -28.5 (width 1.5m: -29.25 to -27.75)
            // Doorway 1-B at X = -13.0 (width 1.5m: -13.75 to -12.25)
            // Genkan entrance opening at X: -4.8 to +4.8 (open!)
            // Doorway 1-C at X = +13.0 (width 1.5m: 12.25 to 13.75)
            // Doorway Nurse at X = +28.5 (width 1.5m: 27.75 to 29.25)
            CreateBox(floor1, new Vector3(-32.6f, 2.2f, -8.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(-20.75f, 2.2f, -8.5f), new Vector3(14.0f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(-8.5f, 2.2f, -8.5f), new Vector3(7.5f, 3.2f, 0.25f), matPlaster, true);
            // X: -4.8 to +4.8 is the wide Genkan opening!
            CreateBox(floor1, new Vector3(8.5f, 2.2f, -8.5f), new Vector3(7.5f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(20.75f, 2.2f, -8.5f), new Vector3(14.0f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(32.6f, 2.2f, -8.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster, true);

            // Door headers above the 4 doorways
            CreateBox(floor1, new Vector3(-28.5f, 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(-13.0f, 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(13.0f, 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(28.5f, 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);

            // Classroom Sliding Doors (Hikido) set ajar with upper observation glass & Nameplates!
            BuildClassroomDoor(floor1, new Vector3(-28.5f, floorY, -8.5f), 0, "1-A");
            BuildClassroomDoor(floor1, new Vector3(-13.0f, floorY, -8.5f), 0, "1-B");
            BuildClassroomDoor(floor1, new Vector3(13.0f, floorY, -8.5f), 0, "1-C");
            BuildClassroomDoor(floor1, new Vector3(28.5f, floorY, -8.5f), 0, "保健室");

            // ====================================================================
            // NORTH CORRIDOR PARTITION WALL (at Z = -13.5)
            // Separates 5m corridor from 4 North rooms + central Stairwell Bay
            // ====================================================================
            // Doorway Staff at X = -28.5
            // Doorway Principal at X = -13.0
            // Stairwell entrance opening at X: -4.8 to +4.8 (open to stairs!)
            // Doorway PC Lab at X = +13.0
            // Doorway Archive at X = +28.5
            CreateBox(floor1, new Vector3(-32.6f, 2.2f, -13.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(-20.75f, 2.2f, -13.5f), new Vector3(14.0f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(-8.5f, 2.2f, -13.5f), new Vector3(7.5f, 3.2f, 0.25f), matPlaster, true);
            // X: -4.8 to +4.8 is the central stairwell entrance!
            CreateBox(floor1, new Vector3(8.5f, 2.2f, -13.5f), new Vector3(7.5f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(20.75f, 2.2f, -13.5f), new Vector3(14.0f, 3.2f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(32.6f, 2.2f, -13.5f), new Vector3(6.8f, 3.2f, 0.25f), matPlaster, true);

            CreateBox(floor1, new Vector3(-28.5f, 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(-13.0f, 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(13.0f, 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor1, new Vector3(28.5f, 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);

            BuildClassroomDoor(floor1, new Vector3(-28.5f, floorY, -13.5f), 180, "職員室");
            BuildClassroomDoor(floor1, new Vector3(-13.0f, floorY, -13.5f), 180, "校長室");
            BuildClassroomDoor(floor1, new Vector3(13.0f, floorY, -13.5f), 180, "PC室");
            BuildClassroomDoor(floor1, new Vector3(28.5f, floorY, -13.5f), 180, "資料室");

            // ====================================================================
            // ROOM DIVIDING PARTITION WALLS (along X)
            // ====================================================================
            // South Wing Dividers (Z: 0 to -8.5)
            CreateBox(floor1, new Vector3(-20.5f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider 1A-1B");
            CreateBox(floor1, new Vector3(-5.0f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider 1B-Genkan");
            CreateBox(floor1, new Vector3(5.0f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider Genkan-1C");
            CreateBox(floor1, new Vector3(20.5f, 2.2f, -4.25f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider 1C-Nurse");

            // North Wing Dividers (Z: -13.5 to -22)
            CreateBox(floor1, new Vector3(-20.5f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider Staff-Principal");
            CreateBox(floor1, new Vector3(-5.0f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider Principal-Stairs");
            CreateBox(floor1, new Vector3(5.0f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider Stairs-PC");
            CreateBox(floor1, new Vector3(20.5f, 2.2f, -17.75f), new Vector3(0.25f, 3.2f, 8.5f), matPlaster, true, "Divider PC-Archive");

            // ====================================================================
            // 1ST FLOOR ROOM INTERIORS & MAPPING
            // ====================================================================
            // 1. Class 1-A (South West: X: -36 to -20.5, Z: 0 to -8.5)
            BuildClassroom(floor1, -28.25f, floorY, -4.25f, "1-A");

            // 2. Class 1-B (South Center-West: X: -20.5 to -5.0, Z: 0 to -8.5)
            BuildClassroom(floor1, -12.75f, floorY, -4.25f, "1-B");

            // 3. Class 1-C (South Center-East: X: +5.0 to +20.5, Z: 0 to -8.5)
            BuildClassroom(floor1, 12.75f, floorY, -4.25f, "1-C");

            // 4. Nurse's Office (South East: X: +20.5 to +36.0, Z: 0 to -8.5)
            BuildNurseOffice(floor1, 28.25f, floorY);

            // 5. Staff Room / Teachers' Office (North West: X: -36 to -20.5, Z: -13.5 to -22)
            BuildStaffRoom(floor1, -28.25f, floorY);

            // 6. Principal's Office (North Center-West: X: -20.5 to -5.0, Z: -13.5 to -22)
            BuildPrincipalOffice(floor1, -12.75f, floorY);

            // 7. Computer / AV Lab (North Center-East: X: +5.0 to +20.5, Z: -13.5 to -22)
            BuildComputerLab(floor1, 12.75f, floorY);

            // 8. Archive & Club Room (North East: X: +20.5 to +36.0, Z: -13.5 to -22)
            BuildArchiveRoom(floor1, 28.25f, floorY);
        }

        // ====================================================================
        // 7. SECOND FLOOR INTERIORS: Matching Wide 5m Corridor & 2 Rooms Each Side
        // ====================================================================
        void BuildSecondFloorInteriors()
        {
            var floor2 = new GameObject("Second Floor Interiors").transform;
            floor2.SetParent(Root, false);

            float floorY = 3.80f;
            float ceilY = 7.40f;

            // 2nd Floor Parquet Slab (with cutout over Stairwell Bay at X: -5 to +5, Z: -13.5 to -22!)
            CreateBox(floor2, new Vector3(-20.5f, floorY + 0.05f, -11f), new Vector3(31f, 0.1f, 22f), matWoodFloor, true, "Floor 2 Parquet Left");
            CreateBox(floor2, new Vector3(20.5f, floorY + 0.05f, -11f), new Vector3(31f, 0.1f, 22f), matWoodFloor, true, "Floor 2 Parquet Right");
            CreateBox(floor2, new Vector3(0, floorY + 0.05f, -4.25f), new Vector3(10f, 0.1f, 8.5f), matWoodFloor, true, "Floor 2 Parquet South Center");
            CreateBox(floor2, new Vector3(0, floorY + 0.05f, -11.0f), new Vector3(10f, 0.1f, 5.0f), matWoodFloor, true, "Floor 2 Parquet Corridor Center");
            // Ceiling Concrete Slab
            CreateBox(floor2, new Vector3(0, ceilY + 0.05f, -11f), new Vector3(72f, 0.15f, 22f), matConcrete, true, "Floor 2 Ceiling");

            // Corridor ceiling lamps spaced across length
            for (float hx = -30f; hx <= 30f; hx += 10f)
            {
                CreateInteriorLamp(floor2, new Vector3(hx, ceilY - 0.15f, -11f));
            }

            BuildExitSign(floor2, new Vector3(-20.5f, floorY + 2.9f, -8.65f));
            BuildExitSign(floor2, new Vector3(20.5f, floorY + 2.9f, -8.65f));

            // ====================================================================
            // 2ND FLOOR SOUTH CORRIDOR PARTITION WALL (at Z = -8.5)
            // Separates 5m corridor from 4 South rooms: Class 2-A, 2-B, 2-C, Music Room
            // ====================================================================
            CreateBox(floor2, new Vector3(-32.6f, floorY + 1.8f, -8.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(-20.75f, floorY + 1.8f, -8.5f), new Vector3(14.0f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(-8.5f, floorY + 1.8f, -8.5f), new Vector3(7.5f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(0, floorY + 1.8f, -8.5f), new Vector3(10.0f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(8.5f, floorY + 1.8f, -8.5f), new Vector3(7.5f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(20.75f, floorY + 1.8f, -8.5f), new Vector3(14.0f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(32.6f, floorY + 1.8f, -8.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster, true);

            CreateBox(floor2, new Vector3(-28.5f, floorY + 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(-13.0f, floorY + 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(13.0f, floorY + 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(28.5f, floorY + 3.3f, -8.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);

            BuildClassroomDoor(floor2, new Vector3(-28.5f, floorY, -8.5f), 0, "2-A");
            BuildClassroomDoor(floor2, new Vector3(-13.0f, floorY, -8.5f), 0, "2-B");
            BuildClassroomDoor(floor2, new Vector3(13.0f, floorY, -8.5f), 0, "2-C");
            BuildClassroomDoor(floor2, new Vector3(28.5f, floorY, -8.5f), 0, "音楽室");

            // ====================================================================
            // 2ND FLOOR NORTH CORRIDOR PARTITION WALL (at Z = -13.5)
            // ====================================================================
            CreateBox(floor2, new Vector3(-32.6f, floorY + 1.8f, -13.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(-20.75f, floorY + 1.8f, -13.5f), new Vector3(14.0f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(-8.5f, floorY + 1.8f, -13.5f), new Vector3(7.5f, 3.6f, 0.25f), matPlaster, true);
            // X: -4.8 to +4.8 is the stairwell exit and void guardrail!
            CreateBox(floor2, new Vector3(8.5f, floorY + 1.8f, -13.5f), new Vector3(7.5f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(20.75f, floorY + 1.8f, -13.5f), new Vector3(14.0f, 3.6f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(32.6f, floorY + 1.8f, -13.5f), new Vector3(6.8f, 3.6f, 0.25f), matPlaster, true);

            CreateBox(floor2, new Vector3(-28.5f, floorY + 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(-13.0f, floorY + 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(13.0f, floorY + 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);
            CreateBox(floor2, new Vector3(28.5f, floorY + 3.3f, -13.5f), new Vector3(1.5f, 1.0f, 0.25f), matPlaster, true);

            BuildClassroomDoor(floor2, new Vector3(-28.5f, floorY, -13.5f), 180, "理科室");
            BuildClassroomDoor(floor2, new Vector3(-13.0f, floorY, -13.5f), 180, "準備室");
            BuildClassroomDoor(floor2, new Vector3(13.0f, floorY, -13.5f), 180, "図書室");
            BuildClassroomDoor(floor2, new Vector3(28.5f, floorY, -13.5f), 180, "放送室");

            // Room dividers on 2nd floor
            CreateBox(floor2, new Vector3(-20.5f, floorY + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);
            CreateBox(floor2, new Vector3(-5.0f, floorY + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);
            CreateBox(floor2, new Vector3(5.0f, floorY + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);
            CreateBox(floor2, new Vector3(20.5f, floorY + 1.8f, -4.25f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);

            CreateBox(floor2, new Vector3(-20.5f, floorY + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);
            CreateBox(floor2, new Vector3(-5.0f, floorY + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);
            CreateBox(floor2, new Vector3(5.0f, floorY + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);
            CreateBox(floor2, new Vector3(20.5f, floorY + 1.8f, -17.75f), new Vector3(0.25f, 3.6f, 8.5f), matPlaster, true);

            // Protective Balustrade Guardrails around the Stairwell Void on 2nd Floor
            CreateBox(floor2, new Vector3(-4.8f, floorY + 0.5f, -16.0f), new Vector3(0.1f, 1.0f, 5.0f), matDarkMetal, true, "Stair Void Rail W");
            CreateBox(floor2, new Vector3(0f, floorY + 0.5f, -18.5f), new Vector3(9.6f, 1.0f, 0.1f), matDarkMetal, true, "Stair Void Rail N");
            CreateBox(floor2, new Vector3(0f, floorY + 0.5f, -13.5f), new Vector3(4.8f, 1.0f, 0.1f), matDarkMetal, true, "Stair Void Rail S");

            // ====================================================================
            // 2ND FLOOR ROOM INTERIORS & MAPPING
            // ====================================================================
            BuildClassroom(floor2, -28.25f, floorY, -4.25f, "2-A");
            BuildClassroom(floor2, -12.75f, floorY, -4.25f, "2-B");
            BuildClassroom(floor2, 12.75f, floorY, -4.25f, "2-C");
            BuildMusicRoom(floor2, 28.25f, floorY);

            BuildScienceLab(floor2, -28.25f, floorY);
            BuildSciencePrepRoom(floor2, -12.75f, floorY);
            BuildLibrary(floor2, 12.75f, floorY);
            BuildBroadcastRoom(floor2, 28.25f, floorY);
        }

        // ====================================================================
        // 8. GRAND SWITCHBACK U-TURN STAIRWELL (Authentic School Staircase)
        // ====================================================================
        void BuildStairs()
        {
            var stairs = new GameObject("Grand Switchback Stairwell").transform;
            stairs.SetParent(Root, false);

            float startY = 0.60f;
            float midY = 2.20f;
            float endY = 3.80f;

            // Stairwell Bay: X: -5.0 to +5.0, Z: -13.5 to -22.0
            // Midway Landing Platform at Y = 2.20m (Z: -18.2 to -21.8, X: -4.8 to +4.8)
            CreateBox(stairs, new Vector3(0, midY - 0.08f, -20.0f), new Vector3(9.6f, 0.16f, 3.6f), matConcrete, true, "Midway Landing Platform");
            // Central Divider Wall between the two flights (X = 0, Z: -13.5 to -18.2, height 2.8m)
            CreateBox(stairs, new Vector3(0, 2.0f, -15.85f), new Vector3(0.25f, 3.2f, 4.7f), matConcrete, true, "Stair Central Spine");

            // FLIGHT 1 (West half: X: -4.8 to -0.2): Climbs North from Z = -13.5, Y = 0.60m to Z = -18.2, Y = 2.20m
            int stepsFlight1 = 10;
            float rise1 = (midY - startY) / stepsFlight1; // 0.16m per step
            float run1 = 4.7f / stepsFlight1;            // 0.47m tread
            for (int i = 0; i < stepsFlight1; i++)
            {
                float sy = startY + (i + 0.5f) * rise1;
                float sz = -13.5f - (i + 0.5f) * run1;
                CreateBox(stairs, new Vector3(-2.5f, sy, sz), new Vector3(4.5f, rise1, run1), matConcrete, true, "Step F1 " + i);
            }
            // Smooth invisible ramp collider over Flight 1 for butter-smooth character motion!
            CreateRamp(stairs, new Vector3(-2.5f, 1.40f, -15.85f), new Vector3(4.5f, 0.15f, 4.96f), 18.8f, "Flight 1 Smooth Ramp");

            // Outer Handrail for Flight 1 (along West wall X = -4.8)
            CreateBox(stairs, new Vector3(-4.7f, 2.25f, -15.85f), new Vector3(0.08f, 0.9f, 4.7f), matDarkMetal, false, "Handrail F1 Outer");

            // FLIGHT 2 (East half: X: +0.2 to +4.8): Turns 180°! Climbs South from Landing Z = -18.2, Y = 2.20m to Z = -13.5, Y = 3.80m
            int stepsFlight2 = 10;
            float rise2 = (endY - midY) / stepsFlight2; // 0.16m per step
            float run2 = 4.7f / stepsFlight2;            // 0.47m tread
            for (int i = 0; i < stepsFlight2; i++)
            {
                float sy = midY + (i + 0.5f) * rise2;
                float sz = -18.2f + (i + 0.5f) * run2;
                CreateBox(stairs, new Vector3(2.5f, sy, sz), new Vector3(4.5f, rise2, run2), matConcrete, true, "Step F2 " + i);
            }
            // Smooth invisible ramp collider over Flight 2!
            CreateRamp(stairs, new Vector3(2.5f, 3.00f, -15.85f), new Vector3(4.5f, 0.15f, 4.96f), -18.8f, "Flight 2 Smooth Ramp");

            // Outer Handrail for Flight 2 (along East wall X = +4.8)
            CreateBox(stairs, new Vector3(4.7f, 3.85f, -15.85f), new Vector3(0.08f, 0.9f, 4.7f), matDarkMetal, false, "Handrail F2 Outer");

            // Midway Landing Railing under the big window
            CreateBox(stairs, new Vector3(0, midY + 0.5f, -21.8f), new Vector3(9.6f, 1.0f, 0.08f), matDarkMetal, false, "Landing Railing");
        }

        // ====================================================================
        // AUTHENTIC JAPANESE SLIDING CLASSROOM DOOR (引き戸 - Hikido)
        // ====================================================================
        void BuildClassroomDoor(Transform parent, Vector3 basePos, float yaw, string roomName)
        {
            var doorGroup = new GameObject("Sliding Door " + roomName).transform;
            doorGroup.SetParent(parent, false);
            doorGroup.localPosition = basePos;
            doorGroup.localRotation = Quaternion.Euler(0, yaw, 0);

            // Doorway is 1.5m wide, 2.3m high
            // Wooden Door Posts
            CreateBox(doorGroup, new Vector3(-0.75f, 1.15f, 0), new Vector3(0.12f, 2.3f, 0.28f), matDarkMetal, true);
            CreateBox(doorGroup, new Vector3(0.75f, 1.15f, 0), new Vector3(0.12f, 2.3f, 0.28f), matDarkMetal, true);
            CreateBox(doorGroup, new Vector3(0, 2.36f, 0), new Vector3(1.62f, 0.12f, 0.28f), matDarkMetal, false);

            // Authentic Wooden Room Nameplate Plaque (室名札) mounted above door
            var plate = CreateBox(doorGroup, new Vector3(0, 2.58f, 0.15f), new Vector3(0.75f, 0.28f, 0.06f), matWoodFloor, false, "Nameplate");
            CreateBox(plate.transform, new Vector3(0, 0, 0.04f), new Vector3(0.68f, 0.22f, 0.02f), matWhite, false, "Label Base");

            // Japanese Sliding Door Leaf (Hikido):
            // Placed slid to the side (ajar by ~1.1m) so the player walks freely inside!
            var leaf = CreateBox(doorGroup, new Vector3(0.85f, 1.12f, 0.08f), new Vector3(0.95f, 2.20f, 0.05f), matDoorWood, true, "Sliding Leaf");
            // Upper glass observation window pane
            CreateBox(leaf.transform, new Vector3(0, 0.45f, 0), new Vector3(0.75f, 0.75f, 0.03f), matGlass, false, "Door Window Glass");
            // Wooden window mullion crosses
            CreateBox(leaf.transform, new Vector3(0, 0.45f, 0.02f), new Vector3(0.04f, 0.75f, 0.04f), matDoorWood, false);
            CreateBox(leaf.transform, new Vector3(0, 0.45f, 0.02f), new Vector3(0.75f, 0.04f, 0.04f), matDoorWood, false);
            // Recessed door pull handle
            CreateBox(leaf.transform, new Vector3(-0.38f, 0, 0.03f), new Vector3(0.04f, 0.18f, 0.02f), matDarkMetal, false);
        }

        // ====================================================================
        // CLASSROOM BUILDER: Blackboard, Podium, 12 Desks & Rear Cubbies
        // ====================================================================
        void BuildClassroom(Transform parent, float centerX, float floorY, float centerZ, string roomName)
        {
            var room = new GameObject("Classroom " + roomName).transform;
            room.SetParent(parent, false);

            // Front Baked Chalkboard with wooden frame & chalk tray (never bleeds through walls!)
            CreateBox(room, new Vector3(centerX, floorY + 1.85f, centerZ + 4.15f), new Vector3(5.8f, 1.6f, 0.08f), matChalkboard, true, "Chalkboard");
            CreateBox(room, new Vector3(centerX, floorY + 1.0f, centerZ + 4.1f), new Vector3(5.8f, 0.08f, 0.14f), matWoodFloor, false, "Chalk Tray");

            // Teacher Podium & Desk
            CreateBox(room, new Vector3(centerX, floorY + 0.10f, centerZ + 2.8f), new Vector3(2.6f, 0.20f, 1.4f), matWoodFloor, true, "Podium Stage");
            CreateBox(room, new Vector3(centerX, floorY + 0.55f, centerZ + 2.8f), new Vector3(1.8f, 0.75f, 0.85f), matWoodFloor, true, "Teacher Desk");
            CreateBox(room, new Vector3(centerX + 0.4f, floorY + 0.95f, centerZ + 2.8f), new Vector3(0.35f, 0.05f, 0.25f), matBooks, false, "Attendance Book");

            // 12 Student Desks arranged in 3 rows x 4 columns
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float dx = centerX - 4.2f + c * 2.8f;
                    float dz = centerZ + 1.0f - r * 2.2f;
                    BuildDesk(room, new Vector3(dx, floorY, dz));
                }
            }

            // Student Wooden Cubby Shelves (ロッカー棚) along rear wall
            CreateBox(room, new Vector3(centerX, floorY + 0.85f, centerZ - 4.05f), new Vector3(10.0f, 1.6f, 0.55f), matWoodFloor, true, "Cubby Shelves");
            for (int c = 0; c < 8; c++)
            {
                float cx = centerX - 4.2f + c * 1.2f;
                CreateBox(room, new Vector3(cx, floorY + 0.85f, centerZ - 3.8f), new Vector3(1.0f, 1.4f, 0.45f), matDarkMetal, false);
            }

            // Wall Clock above blackboard
            var clk = CreateCylinder(room, new Vector3(centerX, floorY + 3.0f, centerZ + 4.15f), 0.35f, 0.06f, matWhite, false);
            clk.transform.rotation = Quaternion.Euler(90, 0, 0);

            // Ceiling Fluorescent Tube Fixtures
            CreateInteriorLamp(room, new Vector3(centerX - 2.5f, floorY + 3.1f, centerZ));
            CreateInteriorLamp(room, new Vector3(centerX + 2.5f, floorY + 3.1f, centerZ));
        }

        void BuildDesk(Transform parent, Vector3 pos)
        {
            var g = new GameObject("SchoolDesk").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;

            CreateBox(g, new Vector3(0, 0.72f, 0), new Vector3(0.78f, 0.04f, 0.52f), matWoodFloor, true);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    CreateCylinder(g, new Vector3(sx * 0.34f, 0.36f, sz * 0.22f), 0.018f, 0.72f, matDarkMetal, false);
                }
            }
            CreateBox(g, new Vector3(0, 0.42f, 0.46f), new Vector3(0.44f, 0.035f, 0.42f), matWoodFloor, true);
            CreateBox(g, new Vector3(0, 0.80f, 0.64f), new Vector3(0.44f, 0.32f, 0.035f), matWoodFloor, false);
            CreateBox(g, new Vector3(-0.42f, 0.45f, 0), new Vector3(0.12f, 0.35f, 0.24f), matDarkMetal, false, "Backpack");
        }

        // ====================================================================
        // SPECIALIZED FIRST FLOOR ROOMS
        // ====================================================================
        void BuildNurseOffice(Transform parent, float centerX, float floorY)
        {
            var room = new GameObject("Nurse Office (保健室)").transform;
            room.SetParent(parent, false);

            for (int b = 0; b < 2; b++)
            {
                float bz = -3.2f - b * 2.8f;
                CreateBox(room, new Vector3(centerX + 4.2f, floorY + 0.45f, bz), new Vector3(1.3f, 0.4f, 2.3f), matWhite, true, "Bed");
                CreateBox(room, new Vector3(centerX + 4.2f, floorY + 0.70f, bz + 0.9f), new Vector3(0.9f, 0.15f, 0.5f), matPlaster, false, "Pillow");
                CreateBox(room, new Vector3(centerX + 3.0f, floorY + 1.2f, bz), new Vector3(0.05f, 1.8f, 2.2f), matWhite, false, "Privacy Curtain");
            }
            CreateBox(room, new Vector3(centerX - 3.5f, floorY + 1.5f, -6.5f), new Vector3(2.4f, 2.8f, 0.6f), matDarkMetal, true, "Medicine Cabinet");
            CreateBox(room, new Vector3(centerX - 3.5f, floorY + 0.75f, -2.5f), new Vector3(1.8f, 0.75f, 1.0f), matWoodFloor, true, "Nurse Desk");
            CreateBox(room, new Vector3(centerX + 1.0f, floorY + 1.0f, -7.5f), new Vector3(0.35f, 2.0f, 0.35f), matWhite, true, "Height Scale");

            CreateInteriorLamp(room, new Vector3(centerX, floorY + 3.1f, -4.25f));
        }

        void BuildStaffRoom(Transform parent, float centerX, float floorY)
        {
            var room = new GameObject("Staff Room (職員室)").transform;
            room.SetParent(parent, false);

            for (int pod = 0; pod < 2; pod++)
            {
                float pz = -16.0f - pod * 3.4f;
                var podDesk = CreateBox(room, new Vector3(centerX, floorY + 0.75f, pz), new Vector3(7.2f, 0.75f, 1.8f), matWoodFloor, true, "Staff Pod Desk");
                for (float dx = -2.4f; dx <= 2.4f; dx += 2.4f)
                {
                    CreateBox(podDesk.transform, new Vector3(dx, 0.45f, 0.3f), new Vector3(0.4f, 0.25f, 0.3f), matBooks, false, "Paper Stack");
                    CreateBox(podDesk.transform, new Vector3(dx, 0.45f, -0.3f), new Vector3(0.4f, 0.25f, 0.3f), matBooks, false, "Paper Stack");
                }
            }
            CreateBox(room, new Vector3(centerX - 5.5f, floorY + 1.6f, -17.75f), new Vector3(0.8f, 2.8f, 6.0f), matDarkMetal, true, "Filing Cabinets");
            CreateInteriorLamp(room, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        void BuildPrincipalOffice(Transform parent, float centerX, float floorY)
        {
            var room = new GameObject("Principal Office (校長室)").transform;
            room.SetParent(parent, false);

            CreateBox(room, new Vector3(centerX, floorY + 0.75f, -20.0f), new Vector3(2.8f, 0.75f, 1.4f), matWoodFloor, true, "Executive Desk");
            CreateBox(room, new Vector3(centerX, floorY + 0.65f, -20.9f), new Vector3(0.8f, 1.1f, 0.8f), matDarkMetal, true, "Leather Chair");
            CreateBox(room, new Vector3(centerX - 2.5f, floorY + 0.45f, -16.5f), new Vector3(1.8f, 0.65f, 0.8f), matDarkMetal, true, "Sofa L");
            CreateBox(room, new Vector3(centerX + 2.5f, floorY + 0.45f, -16.5f), new Vector3(1.8f, 0.65f, 0.8f), matDarkMetal, true, "Sofa R");
            CreateBox(room, new Vector3(centerX, floorY + 0.35f, -16.5f), new Vector3(1.6f, 0.45f, 0.8f), matWoodFloor, true, "Coffee Table");

            CreateInteriorLamp(room, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        void BuildComputerLab(Transform parent, float centerX, float floorY)
        {
            var room = new GameObject("Computer Lab (PC室)").transform;
            room.SetParent(parent, false);

            for (int r = 0; r < 2; r++)
            {
                float rz = -16.0f - r * 3.2f;
                var table = CreateBox(room, new Vector3(centerX, floorY + 0.72f, rz), new Vector3(8.0f, 0.72f, 1.4f), matLabCounter, true, "Computer Bench");
                for (float mx = -2.8f; mx <= 2.8f; mx += 1.8f)
                {
                    CreateBox(table.transform, new Vector3(mx, 0.55f, 0), new Vector3(0.55f, 0.42f, 0.12f), matDarkMetal, false, "Monitor");
                    CreateBox(table.transform, new Vector3(mx, 0.38f, 0.35f), new Vector3(0.45f, 0.03f, 0.18f), matWhite, false, "Keyboard");
                }
            }
            CreateInteriorLamp(room, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        void BuildArchiveRoom(Transform parent, float centerX, float floorY)
        {
            var room = new GameObject("Archive Room (資料室)").transform;
            room.SetParent(parent, false);

            for (int r = 0; r < 3; r++)
            {
                float rz = -15.5f - r * 2.2f;
                var shelf = CreateBox(room, new Vector3(centerX - 1.5f, floorY + 1.4f, rz), new Vector3(6.5f, 2.6f, 0.7f), matWoodFloor, true, "Archive Shelf");
                for (int lvl = 0; lvl < 3; lvl++)
                {
                    CreateBox(shelf.transform, new Vector3(0, -0.8f + lvl * 0.75f, 0.05f), new Vector3(6.2f, 0.45f, 0.55f), matBooks, false);
                }
            }
            CreateInteriorLamp(room, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        // ====================================================================
        // SPECIALIZED SECOND FLOOR ROOMS
        // ====================================================================
        void BuildMusicRoom(Transform parent, float centerX, float floorY)
        {
            var room = new GameObject("Music Room (音楽室)").transform;
            room.SetParent(parent, false);

            var piano = CreateBox(room, new Vector3(centerX + 3.5f, floorY + 0.7f, -4.25f), new Vector3(1.8f, 1.3f, 1.4f), matDarkMetal, true, "Upright Piano");
            CreateBox(piano.transform, new Vector3(0, -0.1f, 0.65f), new Vector3(1.6f, 0.18f, 0.35f), matWhite, false, "Keyboard");
            CreateBox(room, new Vector3(centerX + 3.5f, floorY + 0.45f, -3.2f), new Vector3(0.6f, 0.45f, 0.4f), matWoodFloor, true, "Piano Stool");

            for (int row = 0; row < 3; row++)
            {
                float rz = -2.5f - row * 1.8f;
                for (int c = 0; c < 4; c++)
                {
                    float cx = centerX - 3.8f + c * 1.8f;
                    CreateBox(room, new Vector3(cx, floorY + 0.45f, rz), new Vector3(0.45f, 0.45f, 0.45f), matWoodFloor, true, "Chair");
                    CreateCylinder(room, new Vector3(cx, floorY + 0.9f, rz + 0.6f), 0.02f, 0.9f, matDarkMetal, false, "Music Stand");
                    CreateBox(room, new Vector3(cx, floorY + 1.2f, rz + 0.6f), new Vector3(0.4f, 0.3f, 0.03f), matDarkMetal, false);
                }
            }
            CreateInteriorLamp(room, new Vector3(centerX, floorY + 3.1f, -4.25f));
        }

        void BuildScienceLab(Transform parent, float centerX, float floorY)
        {
            var lab = new GameObject("Science Lab (理科室)").transform;
            lab.SetParent(parent, false);

            for (int r = 0; r < 2; r++)
            {
                float lz = -16.2f - r * 3.4f;
                var bench = CreateBox(lab, new Vector3(centerX, floorY + 0.45f, lz), new Vector3(7.5f, 0.9f, 1.6f), matLabCounter, true, "Lab Bench");
                CreateBox(bench.transform, new Vector3(2.8f, 0.45f, 0), new Vector3(0.9f, 0.2f, 0.7f), matDarkMetal, false, "Sink");
                CreateCylinder(bench.transform, new Vector3(2.8f, 0.7f, 0), 0.02f, 0.4f, matWhite, false, "Faucet");
                CreateCylinder(bench.transform, new Vector3(-2.2f, 0.55f, 0.2f), 0.06f, 0.22f, matGlass, false, "Beaker");
                CreateCylinder(bench.transform, new Vector3(-1.6f, 0.55f, 0.2f), 0.08f, 0.25f, matGlass, false, "Beaker 2");
                CreateBox(bench.transform, new Vector3(0, 0.55f, -0.3f), new Vector3(0.45f, 0.18f, 0.15f), matWoodFloor, false, "Test Tubes");
            }

            var skel = new GameObject("Skeleton Stand").transform;
            skel.SetParent(lab, false);
            skel.localPosition = new Vector3(centerX + 5.5f, floorY, -20.5f);
            CreateCylinder(skel, new Vector3(0, 0.9f, 0), 0.025f, 1.8f, matDarkMetal, false, "Stand");
            CreateBox(skel, new Vector3(0, 1.6f, 0), new Vector3(0.24f, 0.28f, 0.24f), matWhite, false, "Skull");
            CreateBox(skel, new Vector3(0, 1.15f, 0), new Vector3(0.35f, 0.55f, 0.22f), matWhite, false, "Ribcage");

            CreateInteriorLamp(lab, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        void BuildSciencePrepRoom(Transform parent, float centerX, float floorY)
        {
            var prep = new GameObject("Science Prep (準備室)").transform;
            prep.SetParent(parent, false);

            CreateBox(prep, new Vector3(centerX - 3.5f, floorY + 1.5f, -17.75f), new Vector3(2.0f, 2.8f, 6.0f), matDarkMetal, true, "Reagent Cabinets");
            CreateBox(prep, new Vector3(centerX + 2.0f, floorY + 0.45f, -17.75f), new Vector3(4.5f, 0.9f, 1.6f), matLabCounter, true, "Prep Desk");

            CreateInteriorLamp(prep, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        void BuildLibrary(Transform parent, float centerX, float floorY)
        {
            var lib = new GameObject("Library (図書室)").transform;
            lib.SetParent(parent, false);

            for (int r = 0; r < 3; r++)
            {
                float bz = -15.5f - r * 2.6f;
                var shelf = CreateBox(lib, new Vector3(centerX - 3.0f, floorY + 1.4f, bz), new Vector3(5.5f, 2.8f, 0.65f), matWoodFloor, true, "Bookshelf");
                for (int level = 0; level < 4; level++)
                {
                    CreateBox(shelf.transform, new Vector3(0, -1.0f + level * 0.65f, 0.05f), new Vector3(5.2f, 0.45f, 0.55f), matBooks, false, "Books");
                }
            }

            var table = CreateBox(lib, new Vector3(centerX + 3.2f, floorY + 0.4f, -17.75f), new Vector3(2.6f, 0.8f, 4.8f), matWoodFloor, true, "Study Table");
            for (float tz = -1.5f; tz <= 1.5f; tz += 1.5f)
            {
                var lamp = CreateBox(table.transform, new Vector3(0, 0.55f, tz), new Vector3(0.22f, 0.35f, 0.22f), matTorii, false, "Desk Lamp");
                var l = lamp.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.9f, 1.0f, 0.7f);
                l.intensity = 0.9f;
                l.range = 3.2f;
            }

            CreateInteriorLamp(lib, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        void BuildBroadcastRoom(Transform parent, float centerX, float floorY)
        {
            var av = new GameObject("AV Room (放送室)").transform;
            av.SetParent(parent, false);

            CreateBox(av, new Vector3(centerX, floorY + 0.45f, -17.75f), new Vector3(4.2f, 0.9f, 1.6f), matDarkMetal, true, "Console Desk");
            CreateBox(av, new Vector3(centerX, floorY + 1.25f, -17.75f), new Vector3(3.2f, 0.55f, 0.35f), matLabCounter, false, "Rack Equipment");
            CreateBox(av, new Vector3(centerX - 2.2f, floorY + 1.2f, -17.75f), new Vector3(0.4f, 0.6f, 0.4f), matDarkMetal, false, "Speaker L");
            CreateBox(av, new Vector3(centerX + 2.2f, floorY + 1.2f, -17.75f), new Vector3(0.4f, 0.6f, 0.4f), matDarkMetal, false, "Speaker R");

            CreateInteriorLamp(av, new Vector3(centerX, floorY + 3.1f, -17.75f));
        }

        // ====================================================================
        // INTERIOR HELPERS: Lamps, Exit Signs, Hydrants, Notice Boards
        // ====================================================================
        void CreateInteriorLamp(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Hallway Lamp").transform;
            go.SetParent(parent, false);
            go.localPosition = pos;
            CreateBox(go, Vector3.zero, new Vector3(1.6f, 0.08f, 0.35f), matWhite, false);
            var bulb = CreateBox(go, new Vector3(0, -0.04f, 0), new Vector3(1.4f, 0.04f, 0.25f), matPlaster, false);

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

        void BuildExitSign(Transform parent, Vector3 pos)
        {
            var sign = CreateBox(parent, pos, new Vector3(0.6f, 0.3f, 0.08f), matTorii, false, "ExitSign");
            var l = sign.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.1f, 1.0f, 0.4f);
            l.intensity = 1.1f;
            l.range = 2.8f;
        }

        void BuildFireHydrant(Transform parent, Vector3 pos)
        {
            var hyd = CreateBox(parent, pos, new Vector3(0.7f, 1.2f, 0.15f), matTorii, true, "FireHydrant");
            CreateBox(hyd.transform, new Vector3(0, 0.25f, 0.08f), new Vector3(0.3f, 0.3f, 0.04f), matWhite, false);
        }

        void BuildNoticeBoard(Transform parent, Vector3 pos)
        {
            var cork = CreateBox(parent, pos, new Vector3(2.4f, 1.2f, 0.06f), matWoodFloor, false, "NoticeBoard");
            CreateBox(cork.transform, new Vector3(-0.6f, 0.15f, 0.04f), new Vector3(0.45f, 0.6f, 0.02f), matWhite, false, "Poster 1");
            CreateBox(cork.transform, new Vector3(0.4f, -0.1f, 0.04f), new Vector3(0.55f, 0.45f, 0.02f), matWhite, false, "Poster 2");
        }

        // ====================================================================
        // 9. TRADITIONAL JAPANESE PARK ACROSS THE ROAD
        // ====================================================================
        void BuildTraditionalPark()
        {
            var park = new GameObject("Traditional Japanese Park").transform;
            park.SetParent(Root, false);

            CreateBox(park, new Vector3(0, -0.05f, 80f), new Vector3(90f, 0.1f, 60f), matFoliage, true, "Park Moss Ground");

            for (int side = -1; side <= 1; side += 2)
            {
                float wallW = 38f;
                float midX = side * (5.5f + wallW * 0.5f);
                CreateBox(park, new Vector3(midX, 0.65f, 50f), new Vector3(wallW, 1.3f, 0.45f), matStone, true, "Park Stone Wall");
                CreateBox(park, new Vector3(side * 5.5f, 1.2f, 50f), new Vector3(0.9f, 2.4f, 0.9f), matStone, true, "Park Gate Post");
            }

            CreateBox(park, new Vector3(0, 0.02f, 68f), new Vector3(4.8f, 0.02f, 36f), matStone, false, "Stone Path Main");
            CreateBox(park, new Vector3(-14f, 0.02f, 80f), new Vector3(20f, 0.02f, 3.4f), matStone, false, "Stone Path Left");
            CreateBox(park, new Vector3(14f, 0.02f, 80f), new Vector3(20f, 0.02f, 3.4f), matStone, false, "Stone Path Right");
            CreateBox(park, new Vector3(0, 0.02f, 96f), new Vector3(5.2f, 0.02f, 24f), matStone, false, "Shrine Approach Path");

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

            BuildToriiGate(park, new Vector3(0, 0, 86f));
            BuildShintoShrine(park, new Vector3(0, 0, 100f));
            BuildAzumayaGazebo(park, new Vector3(16f, 0, 72f));

            BuildParkBench(park, new Vector3(-14f, 0, 64f), 45);
            BuildParkBench(park, new Vector3(-16f, 0, 84f), 90);
            BuildParkBench(park, new Vector3(18f, 0, 86f), -90);

            Vector3[] lampPositions = {
                new Vector3(-12f, 0, 48.5f),
                new Vector3(12f, 0, 48.5f),
                new Vector3(-6f, 0, 60f),
                new Vector3(6f, 0, 60f),
                new Vector3(-7f, 0, 75f),
                new Vector3(7f, 0, 75f),
                new Vector3(-5f, 0, 90f),
                new Vector3(5f, 0, 90f)
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
        // 10. JOURNALIST NEWS VAN
        // ====================================================================
        void BuildNewsVan()
        {
            var van = new GameObject("Journalist News Van").transform;
            van.SetParent(Root, false);
            van.localPosition = new Vector3(0, 0, 43f);
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
        // 11. 5 SANITY PILLS & 5 JOURNALIST EVIDENCE TAPES
        // ====================================================================
        void SpawnSanityPills()
        {
            Vector3[] pillPositions = {
                new Vector3(-28.25f, 1.45f, -4.25f), // Class 1-A desk
                new Vector3(28.25f, 1.45f, -4.25f),  // Nurse's office table
                new Vector3(-28.25f, 4.65f, -16.2f), // 2nd Floor Science Lab counter
                new Vector3(12.75f, 4.65f, -17.75f), // 2nd Floor Library table
                new Vector3(0f, 0.90f, 98.0f)        // Shinto Shrine altar in Park
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

                CreateBox(pillGo.transform, new Vector3(0, 0.1f, 0), new Vector3(0.18f, 0.25f, 0.18f), matPlaster, false, "Aura");
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
                new { id = "tape-1", label = "Касета 1: Щоденник чергового учня", pos = new Vector3(-12.75f, 1.45f, -4.25f) }, // Class 1-B
                new { id = "tape-2", label = "Касета 2: Звіт вчительської ради", pos = new Vector3(-28.25f, 1.45f, -16.0f) },    // Staff Room
                new { id = "tape-3", label = "Касета 3: Запис гуртка окультизму", pos = new Vector3(28.25f, 4.65f, -4.25f) },   // Music Room
                new { id = "tape-4", label = "Касета 4: Запис спостереження 2-A", pos = new Vector3(-28.25f, 4.65f, -4.25f) },  // Class 2-A
                new { id = "tape-5", label = "Касета 5: Останній репортаж журналіста", pos = new Vector3(16f, 0.75f, 72f) }     // Park Gazebo
            };

            foreach (var def in tapeDefs)
            {
                var tapeGo = new GameObject("EvidenceTape_" + def.id);
                tapeGo.transform.SetParent(Root, false);
                tapeGo.transform.localPosition = def.pos;

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
