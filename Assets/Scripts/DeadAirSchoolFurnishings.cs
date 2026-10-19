using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    public sealed partial class DeadAirSchoolWorld
    {
        private Material matSchoolFabric;
        private Material matSchoolRubber;
        private Material matSchoolCeramic;
        private Material matSchoolCeiling;
        private Material[] matSchoolBookBindings;
        private DeadAirArtMotionSlidingLeaf[] schoolDoorLeaves = new DeadAirArtMotionSlidingLeaf[0];
        private float schoolMotionElapsed = -1f;
        private string schoolMotionChapter;
        private bool schoolArtBatched;

        private void UpdateSchoolDoorMotion(RoomState state, float deltaTime)
        {
            if (state == null) return;
            bool reset = schoolMotionElapsed < 0f || state.elapsed < schoolMotionElapsed || schoolMotionChapter != state.chapterId;
            schoolMotionElapsed = state.elapsed;
            schoolMotionChapter = state.chapterId;
            float step = state.paused ? 0f : Mathf.Max(0f, deltaTime);
            foreach (var leaf in schoolDoorLeaves)
            {
                if (leaf == null || !leaf.gameObject.activeInHierarchy) continue;
                leaf.Advance(SchoolDoorHasNearbyActor(leaf, state), step, reset);
            }
        }

        private static bool SchoolDoorHasNearbyActor(DeadAirArtMotionSlidingLeaf leaf, RoomState state)
        {
            if (state.players != null)
                foreach (var player in state.players)
                    if (player != null && leaf.IsWithinSensor(DeadAirOfficeLayout.Position(player.x, player.y, player.z))) return true;
            var enemy = state.enemy;
            return enemy != null && enemy.active && leaf.IsWithinSensor(DeadAirOfficeLayout.Position(enemy.x, enemy.y, enemy.z));
        }

        private void BatchSchoolArchitecture()
        {
            if (!Application.isPlaying || schoolArtBatched || !Root.gameObject.activeInHierarchy) return;
            var animatedBulbs = new HashSet<Renderer>();
            foreach (var lamp in lamps) if (lamp.bulb != null) animatedBulbs.Add(lamp.bulb);
            var candidates = new List<GameObject>();
            foreach (var renderer in Root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.isPartOfStaticBatch || renderer.HasPropertyBlock()) continue;
                if (renderer.GetComponentInParent<DeadAirArtMirror>() != null) continue;
                if (animatedBulbs.Contains(renderer) || renderer == generatorIndicatorRenderer || renderer == breakerIndicatorRenderer) continue;
                if (renderer.GetComponent<Light>() != null || renderer.GetComponentInParent<LODGroup>() != null) continue;
                if (renderer.GetComponentInParent<DeadAirArtMotionSlidingLeaf>() != null || renderer.GetComponentInParent<DeadAirArtMotionRotor>() != null) continue;
                if (breakerHandleTransform != null && renderer.transform.IsChildOf(breakerHandleTransform)) continue;
                bool pickup = false;
                for (Transform ancestor = renderer.transform; ancestor != null && ancestor != Root; ancestor = ancestor.parent)
                {
                    if (ancestor.name.StartsWith("SanityPill_", System.StringComparison.Ordinal) || ancestor.name.StartsWith("EvidenceTape_", System.StringComparison.Ordinal)) { pickup = true; break; }
                }
                if (pickup) continue;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                candidates.Add(renderer.gameObject);
            }
            if (candidates.Count > 1) StaticBatchingUtility.Combine(candidates.ToArray(), Root.gameObject);
            schoolArtBatched = true;
        }

        private static Transform Assembly(Transform parent, string name, Vector3 position, float yaw = 0f)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return root;
        }

        private void BuildTeachingWall(Transform parent, Vector3 floorAnchor, float yaw)
        {
            Transform root = Assembly(parent, "Teaching wall assembly", floorAnchor, yaw);
            CreateBox(root, new Vector3(0f, 1.85f, 0f), new Vector3(6.2f, 1.6f, .08f), matChalkboard, true, "Teaching board face");
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(root, new Vector3(side * 3.13f, 1.85f, .025f), new Vector3(.055f, 1.7f, .045f), matDarkMetal, false, "Board side frame");
                CreateBox(root, new Vector3(0f, 1.85f + side * .825f, .025f), new Vector3(6.3f, .05f, .045f), matDarkMetal, false, "Board horizontal frame");
                CreateBox(root, new Vector3(side * 2.5f, 1.85f, -.1f), new Vector3(.12f, .6f, .2f), matDarkMetal, false, "Board wall bracket");
            }
            CreateBox(root, new Vector3(0f, 1.0f, .08f), new Vector3(6.2f, .08f, .18f), matDarkMetal, false, "Chalk Tray");
            CreateBox(root, new Vector3(-.8f, 1.055f, .1f), new Vector3(.08f, .03f, .03f), matPaper, false, "Chalk Stick");
            CreateBox(root, new Vector3(.5f, 1.055f, .1f), new Vector3(.06f, .03f, .03f), matTorii, false, "Red Chalk");
            CreateBox(root, new Vector3(1.2f, 1.065f, .09f), new Vector3(.18f, .05f, .09f), matSchoolFabric, false, "Felt Eraser");
            float wallBehindBoard = Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 1f ? .11f :
                Mathf.Abs(floorAnchor.x + 35.7f) < .01f ? .10f : floorAnchor.y > 7f ? .19f : .175f;
            const float clockDepth = .065f;
            BuildSchoolWallClock(root, new Vector3(0f, 2.95f, -wallBehindBoard + clockDepth * .5f + .002f), .38f);
        }

        private static float ClassroomTeachingSideX(float centerX, float centerZ)
        {
            if (centerX < -20f) return -35.7f;
            if (centerX < 0f) return -20.2f;
            if (centerX < 20f) return centerZ < -13.5f ? 6.3f : 5.3f;
            return 20.8f;
        }

        private void BuildSchoolWallClock(Transform parent, Vector3 center, float diameter)
        {
            if (DeadAirSchoolPhysics.InstallScanned(parent, "wall-clock", center - Vector3.up * diameter * .5f,
                Quaternion.identity, new Vector3(diameter, diameter, Mathf.Max(.065f, diameter * .07f))) != null) return;
            Transform clock = Assembly(parent, "Fallback readable wall clock", center);
            Transform bezel = CreateCylinder(clock, Vector3.zero, diameter * .5f, .04f, matDarkMetal, false, "Clock bezel").transform;
            bezel.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(bezel, Vector3.up * .024f, diameter * .45f, .008f, matPaper, false, "Clock face");
            for (int tick = 0; tick < 12; tick++)
            {
                float angle = tick * Mathf.PI / 6f;
                CreateBox(clock, new Vector3(Mathf.Sin(angle) * diameter * .37f, Mathf.Cos(angle) * diameter * .37f, .032f),
                    new Vector3(diameter * .018f, diameter * .075f, .008f), matDarkMetal, false, "Hour index").transform.localRotation = Quaternion.Euler(0f, 0f, -tick * 30f);
            }
            Transform hour = Assembly(clock, "Hour hand pivot", new Vector3(0f, 0f, .045f));
            hour.localRotation = Quaternion.Euler(0f, 0f, 60f);
            CreateBox(hour, Vector3.up * diameter * .1f, new Vector3(diameter * .035f, diameter * .22f, .009f), matDarkMetal, false, "Hour hand");
            Transform minute = Assembly(clock, "Minute hand pivot", new Vector3(0f, 0f, .055f));
            minute.localRotation = Quaternion.Euler(0f, 0f, -48f);
            CreateBox(minute, Vector3.up * diameter * .15f, new Vector3(diameter * .025f, diameter * .32f, .009f), matDarkMetal, false, "Minute hand");
        }

        private bool TryCreateFurnishedAssembly(Transform parent, Vector3 position, Vector3 size, Material material, bool collision, string name, out GameObject result)
        {
            result = null;
            switch (name)
            {
                case "Teacher Desk": case "Nurse Desk": case "Staff Pod Desk": case "Executive Desk":
                case "Computer Bench": case "Study Table": case "Coffee Table": case "Prep Desk": case "Console Desk":
                    result = BuildWorktable(parent, position, size, material, name, collision).gameObject;
                    return true;
                case "Classroom Storage": case "Cleaning Supply Cabinet": case "Media Archive Cabinets":
                case "Medicine Cabinet": case "Filing Cabinets": case "Reagent Cabinets":
                    result = BuildStorageCabinet(parent, position, size, material, name, collision).gameObject;
                    return true;
                case "Archive Shelf": case "Bookshelf": case "Cubby Unit":
                    result = BuildOpenShelving(parent, position, size, material, name, name == "Cubby Unit" ? 1 : 4, true, collision).gameObject;
                    return true;
                case "Chair": case "Leather Chair":
                    result = BuildChair(parent, position, size, name == "Leather Chair", name, collision).gameObject;
                    return true;
                case "Sofa L": case "Sofa R":
                    result = BuildSofa(parent, position, size, name, collision).gameObject;
                    return true;
                case "Bed":
                    result = BuildInfirmaryBed(parent, position, size, collision).gameObject;
                    return true;
                case "Monitor":
                    result = BuildMonitor(parent, position, size).gameObject;
                    return true;
                case "Desk Lamp":
                    result = BuildScannedFixture(parent, position, size, "desk-lamp", name);
                    return result != null;
                case "Generator Body":
                    result = BuildScannedFixture(parent, position, size, "generator", name);
                    if (result != null && collision) AddCollisionProxy(result.transform, Vector3.zero, size);
                    return result != null;
                case "Shrub": case "Planter Shrubs":
                    result = BuildScannedPlanterShrubs(parent, position, size, name);
                    return result != null;
                case "Keyboard":
                    if (size.x < 0.8f) { result = BuildComputerKeyboard(parent, position, size).gameObject; return true; }
                    return false;
            }
            return false;
        }

        private GameObject BuildScannedFixture(Transform parent, Vector3 position, Vector3 size, string key, string name)
        {
            Transform root = Assembly(parent, name, position);
            GameObject model = DeadAirSchoolPhysics.InstallScanned(root, key, Vector3.down * size.y * 0.5f, Quaternion.identity, size);
            if (model != null) return root.gameObject;
            if (Application.isPlaying) Object.Destroy(root.gameObject); else Object.DestroyImmediate(root.gameObject);
            return null;
        }

        private void AddCollisionProxy(Transform root, Vector3 center, Vector3 size, string name = "Collision proxy")
        {
            Transform proxy = Assembly(root, name, center);
            DeadAirSchoolPhysics.AddBox(proxy, size);
        }

        private Transform BuildWorktable(Transform parent, Vector3 position, Vector3 size, Material material, string name, bool collision, bool sinkOpening = false)
        {
            Transform root = Assembly(parent, name, position);
            float top = size.y * 0.5f, bottom = -size.y * 0.5f;
            if (sinkOpening)
            {
                AddCounterSection(root, new Vector3(-0.6925f, top - 0.035f, 0f), new Vector3(6.115f, 0.07f, size.z), material, collision);
                AddCounterSection(root, new Vector3(3.4925f, top - 0.035f, 0f), new Vector3(0.515f, 0.07f, size.z), material, collision);
                for (int end = -1; end <= 1; end += 2)
                    AddCounterSection(root, new Vector3(2.8f, top - 0.035f, end * 0.5675f), new Vector3(0.87f, 0.07f, 0.465f), material, collision);
            }
            else CreateBox(root, new Vector3(0f, top - 0.035f, 0f), new Vector3(size.x, 0.07f, size.z), material, false, "Tabletop with eased edge");
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 p = new Vector3(x * (size.x * 0.5f - 0.09f), -0.035f, z * (size.z * 0.5f - 0.09f));
                CreateCylinder(root, p, 0.032f, size.y - 0.07f, matDarkMetal, false, "Steel table leg");
                CreateCylinder(root, new Vector3(p.x, bottom + 0.022f, p.z), 0.039f, 0.044f, matSchoolRubber, false, "Table foot cap");
                if (collision) AddCollisionProxy(root, p, new Vector3(0.07f, size.y - 0.07f, 0.07f), "Leg collision");
            }
            CreateBox(root, new Vector3(0f, top - 0.17f, size.z * 0.5f - 0.1f), new Vector3(size.x - 0.2f, 0.16f, 0.035f), material, false, "Rear modesty rail");
            if (size.x > 3f)
            {
                for (float x = -size.x * 0.25f; x <= size.x * 0.25f; x += Mathf.Max(1f, size.x * 0.5f))
                    CreateBox(root, new Vector3(x, -0.035f, 0f), new Vector3(0.065f, size.y - 0.07f, size.z - 0.2f), matDarkMetal, false, "Intermediate table trestle");
            }
            if (collision && !sinkOpening) AddCollisionProxy(root, new Vector3(0f, top - 0.035f, 0f), new Vector3(size.x, 0.07f, size.z), "Tabletop collision");
            return root;
        }

        private void AddCounterSection(Transform root, Vector3 position, Vector3 size, Material material, bool collision)
        {
            CreateBox(root, position, size, material, false, "Counter section around sink opening");
            if (collision) AddCollisionProxy(root, position, size, "Counter section collision");
        }

        private Transform BuildOpenShelving(Transform parent, Vector3 position, Vector3 size, Material material, string name, int rows, bool books, bool collision)
        {
            Transform root = Assembly(parent, name, position);
            const float board = 0.035f;
            int bays = Mathf.Max(1, Mathf.CeilToInt(size.x / 1.35f));
            CreateBox(root, new Vector3(0f, 0f, -size.z * 0.5f + 0.012f), new Vector3(size.x, size.y, 0.024f), material, false, "Recessed shelf backing");
            for (int i = 0; i <= bays; i++)
            {
                float x = -size.x * 0.5f + board * 0.5f + i * (size.x - board) / bays;
                CreateBox(root, new Vector3(x, 0f, 0f), new Vector3(board, size.y, size.z), material, collision, "Shelf upright " + i);
            }
            float rowHeight = (size.y - board) / rows;
            for (int row = 0; row <= rows; row++)
            {
                float y = -size.y * 0.5f + board * 0.5f + row * rowHeight;
                CreateBox(root, new Vector3(0f, y, 0f), new Vector3(size.x, board, size.z), material, collision, "Shelf board " + row);
                if (!books || row == rows) continue;
                for (int bay = 0; bay < bays; bay++)
                {
                    float left = -size.x * 0.5f + 0.09f + bay * (size.x - board) / bays;
                    float cellWidth = (size.x - board) / bays - 0.15f;
                    for (int book = 0; book < 5; book++)
                    {
                        if ((row + bay + book) % 7 == 0) continue;
                        float width = Mathf.Min(0.11f, cellWidth / 7f);
                        float height = Mathf.Min(rowHeight - 0.08f, 0.24f + (book % 3) * 0.045f);
                        float x = left + book * (width + 0.016f);
                        Transform volume = Assembly(root, "Individual bound volume", new Vector3(x, y + board * 0.5f + height * 0.5f, 0.04f));
                        Material binding = matSchoolBookBindings[(book + row * 3 + bay) % matSchoolBookBindings.Length];
                        CreateBox(volume, Vector3.zero, new Vector3(width, height, size.z * 0.7f), binding, false, "Book binding");
                        CreateBox(volume, new Vector3(0f, 0f, size.z * 0.35f + 0.001f), new Vector3(width * 0.72f, height * 0.85f, 0.003f), matPaper, false, "Book spine label");
                    }
                }
            }
            return root;
        }

        private Transform BuildStorageCabinet(Transform parent, Vector3 position, Vector3 size, Material material, string name, bool collision)
        {
            bool rotate = size.z > size.x * 1.5f;
            Vector3 cabinetSize = rotate ? new Vector3(size.z, size.y, size.x) : size;
            Transform root = Assembly(parent, name, position, rotate ? 90f : 0f);
            BuildOpenShelving(root, Vector3.zero, cabinetSize, material, "Cabinet carcass", 3, false, collision);
            int doors = Mathf.Max(2, Mathf.CeilToInt(cabinetSize.x / 0.65f));
            float width = cabinetSize.x / doors;
            for (int i = 0; i < doors; i++)
            {
                float x = -cabinetSize.x * 0.5f + width * (i + 0.5f);
                Transform leaf = Assembly(root, "Separate cabinet door " + i, new Vector3(x, 0f, cabinetSize.z * 0.5f + 0.01f));
                CreateBox(leaf, Vector3.zero, new Vector3(width - 0.014f, cabinetSize.y - 0.045f, 0.027f), material, false, "Door panel");
                CreateBox(leaf, new Vector3(0f, cabinetSize.y * 0.29f, 0.016f), new Vector3(Mathf.Min(0.17f, width * 0.5f), 0.045f, 0.006f), matPaper, false, "Number card holder");
                CreateBox(leaf, new Vector3(width * 0.28f, 0f, 0.036f), new Vector3(0.018f, 0.14f, 0.025f), matDarkMetal, false, "Cabinet pull");
                for (int vent = 0; vent < 3; vent++)
                    CreateBox(leaf, new Vector3(0f, -cabinetSize.y * 0.32f + vent * 0.025f, 0.015f), new Vector3(width * 0.48f, 0.008f, 0.003f), matDarkMetal, false, "Vent slot");
                if (collision) AddCollisionProxy(leaf, Vector3.zero, new Vector3(width - 0.014f, cabinetSize.y - 0.045f, 0.035f), "Door collision");
            }
            return root;
        }

        private Transform BuildChair(Transform parent, Vector3 position, Vector3 size, bool upholstered, string name, bool collision)
        {
            Transform root = Assembly(parent, name, position);
            Material finish = upholstered ? matSchoolFabric : matDeskWood;
            float seatY = -size.y * 0.03f, floor = -size.y * 0.5f;
            CreateBox(root, new Vector3(0f, seatY, 0f), new Vector3(size.x, 0.035f, size.z * 0.9f), finish, collision, "Separate shaped chair seat");
            Transform back = Assembly(root, "Chair back assembly", new Vector3(0f, size.y * 0.3f, size.z * 0.42f));
            back.localRotation = Quaternion.Euler(-7f, 0f, 0f);
            CreateBox(back, Vector3.zero, new Vector3(size.x * 0.93f, size.y * 0.35f, 0.038f), finish, false, "Separate chair backrest");
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 p = new Vector3(x * size.x * 0.36f, (floor + seatY) * 0.5f, z * size.z * 0.33f);
                CreateCylinder(root, p, 0.015f, seatY - floor, matDarkMetal, false, "Chair tubular leg");
                CreateCylinder(root, new Vector3(p.x, floor + 0.014f, p.z), 0.019f, 0.028f, matSchoolRubber, false, "Chair foot ferrule");
                if (collision) AddCollisionProxy(root, p, new Vector3(0.04f, seatY - floor, 0.04f), "Chair leg collision");
                if (z == 1) CreateCylinder(root, new Vector3(p.x, size.y * 0.17f, p.z), 0.014f, size.y * 0.43f, matDarkMetal, false, "Back support tube");
            }
            return root;
        }

        private void BuildPianoStool(Transform parent, Vector3 floorPosition)
        {
            Transform root = Assembly(parent, "Piano Stool", floorPosition);
            CreateBox(root, new Vector3(0f, .4475f, 0f), new Vector3(.6f, .065f, .4f), matDeskWood, true, "Piano stool seat");
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                CreateBox(root, new Vector3(x * .24f, .2075f, z * .14f), new Vector3(.045f, .415f, .045f), matDeskWood, true, "Piano stool leg");
            for (int z = -1; z <= 1; z += 2)
                CreateBox(root, new Vector3(0f, .16f, z * .14f), new Vector3(.48f, .025f, .025f), matDeskWood, false, "Piano stool stretcher");
        }

        private void BuildMusicStand(Transform parent, Vector3 floorPosition)
        {
            Transform root = Assembly(parent, "Music Stand", floorPosition);
            CreateCylinder(root, new Vector3(0f, .44f, 0f), .018f, .8f, matDarkMetal, false, "Music stand lower post");
            CreateCylinder(root, new Vector3(0f, .99f, 0f), .012f, .48f, matDarkMetal, false, "Music stand telescoping post");
            CreateCylinder(root, new Vector3(0f, .045f, 0f), .05f, .09f, matDarkMetal, false, "Music stand base hub");
            for (int foot = 0; foot < 3; foot++)
            {
                float angle = foot * Mathf.PI * 2f / 3f;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                CreateCylinder(root, direction * .14f + Vector3.up * .024f, .012f, .28f, matDarkMetal, false, "Music stand tripod arm")
                    .transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                CreateCylinder(root, direction * .28f + Vector3.up * .02f, .022f, .04f, matSchoolRubber, false, "Music stand rubber foot");
            }
            Transform tray = Assembly(root, "Music stand tray", new Vector3(0f, 1.2f, 0f));
            tray.localRotation = Quaternion.Euler(12f, 0f, 0f);
            CreateBox(tray, Vector3.zero, new Vector3(.44f, .32f, .025f), matDarkMetal, false, "Music stand sheet plate");
            CreateBox(tray, new Vector3(0f, -.145f, -.033f), new Vector3(.44f, .025f, .065f), matDarkMetal, false, "Music stand sheet ledge");
        }

        private Transform BuildSofa(Transform parent, Vector3 position, Vector3 size, string name, bool collision)
        {
            Transform root = Assembly(parent, name, position);
            CreateBox(root, new Vector3(0f, -size.y * 0.24f, 0f), new Vector3(size.x, size.y * 0.26f, size.z), matDarkMetal, collision, "Sofa timber base");
            for (int i = 0; i < 2; i++) CreateBox(root, new Vector3((i - 0.5f) * size.x * 0.43f, 0f, -0.04f), new Vector3(size.x * 0.42f, size.y * 0.27f, size.z * 0.78f), matSchoolFabric, false, "Separate seat cushion");
            CreateBox(root, new Vector3(0f, size.y * 0.28f, size.z * 0.39f), new Vector3(size.x, size.y * 0.65f, size.z * 0.18f), matSchoolFabric, collision, "Upholstered sofa back");
            for (int side = -1; side <= 1; side += 2) CreateBox(root, new Vector3(side * size.x * 0.46f, size.y * 0.08f, 0f), new Vector3(size.x * 0.08f, size.y * 0.54f, size.z), matSchoolFabric, collision, "Sofa arm");
            return root;
        }

        private Transform BuildInfirmaryBed(Transform parent, Vector3 position, Vector3 size, bool collision)
        {
            Transform root = Assembly(parent, "Infirmary bed", position);
            CreateBox(root, new Vector3(0f, 0.04f, 0f), new Vector3(size.x, 0.19f, size.z), matSchoolFabric, collision, "Separate cotton mattress");
            CreateBox(root, new Vector3(0f, -0.09f, 0f), new Vector3(size.x + 0.04f, 0.045f, size.z + 0.04f), matWhite, false, "Bed deck");
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                CreateCylinder(root, new Vector3(x * size.x * 0.43f, -0.17f, z * size.z * 0.44f), 0.026f, 0.42f, matWhite, collision, "Bed tubular leg");
            for (int end = -1; end <= 1; end += 2)
            {
                CreateCylinder(root, new Vector3(0f, 0.22f, end * size.z * 0.48f), 0.023f, size.x, matWhite, false, "Bed end rail").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                for (int x = -1; x <= 1; x += 2) CreateCylinder(root, new Vector3(x * size.x * 0.43f, 0.05f, end * size.z * 0.48f), 0.023f, 0.4f, matWhite, false, "Bed end upright");
            }
            return root;
        }

        private Transform BuildMonitor(Transform parent, Vector3 position, Vector3 size)
        {
            Transform root = Assembly(parent, "CRT workstation monitor", position);
            if (DeadAirSchoolPhysics.InstallScanned(root, "television", Vector3.down * size.y * 0.5f, Quaternion.identity, new Vector3(size.x, size.y, 0.3f)) != null) return root;
            CreateBox(root, new Vector3(0f, -size.y * 0.47f, 0f), new Vector3(size.x * 0.48f, 0.025f, size.z * 1.5f), matWhite, false, "Monitor pedestal foot");
            CreateBox(root, new Vector3(0f, -size.y * 0.31f, -0.015f), new Vector3(0.07f, 0.11f, 0.07f), matWhite, false, "Monitor pedestal stem");
            CreateBox(root, new Vector3(0f, 0.015f, -0.06f), new Vector3(size.x, size.y * 0.8f, 0.24f), matWhite, false, "CRT rear casing");
            CreateBox(root, new Vector3(0f, 0.018f, 0.064f), new Vector3(size.x * 0.86f, size.y * 0.65f, 0.015f), matDarkMetal, false, "Recessed CRT face");
            CreateBox(root, new Vector3(0f, 0.025f, 0.075f), new Vector3(size.x * 0.79f, size.y * 0.58f, 0.008f), matGlass, false, "CRT screen glazing");
            return root;
        }

        private Transform BuildComputerKeyboard(Transform parent, Vector3 position, Vector3 size)
        {
            Transform root = Assembly(parent, "Computer keyboard", position);
            CreateBox(root, Vector3.zero, size, matWhite, false, "Keyboard housing");
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 12; col++)
                CreateBox(root, new Vector3((col - 5.5f) * size.x / 13f, size.y * 0.5f + 0.003f, (row - 1.5f) * size.z / 5f), new Vector3(size.x / 15f, 0.006f, size.z / 6.5f), matPaper, false, "Separate keycap");
            return root;
        }

        private void BuildUprightPiano(Transform parent, Vector3 position)
        {
            Transform root = Assembly(parent, "Upright Piano", position);
            CreateBox(root, new Vector3(0f, 0.73f, -0.22f), new Vector3(1.48f, 1.15f, 0.43f), matDarkMetal, true, "Piano soundboard cabinet");
            CreateBox(root, new Vector3(0f, 1.32f, -0.22f), new Vector3(1.6f, 0.055f, 0.49f), matDarkMetal, true, "Separate piano top lid");
            CreateBox(root, new Vector3(0f, 0.72f, 0.19f), new Vector3(1.5f, 0.08f, 0.44f), matDarkMetal, true, "Keyboard bed");
            for (int key = 0; key < 36; key++)
            {
                float x = -0.69f + key * 0.039f;
                CreateBox(root, new Vector3(x, 0.773f, 0.21f), new Vector3(0.037f, 0.025f, 0.29f), matSchoolCeramic, false, "Separate ivory piano key");
                int note = key % 7;
                if (note != 2 && note != 6 && key < 35)
                    CreateBox(root, new Vector3(x + 0.0195f, 0.793f, 0.12f), new Vector3(0.021f, 0.026f, 0.17f), matDarkMetal, false, "Raised black piano key");
            }
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(root, new Vector3(side * 0.76f, 0.4f, 0.23f), new Vector3(0.065f, 0.8f, 0.065f), matDarkMetal, true, "Piano front leg");
                CreateCylinder(root, new Vector3(side * 0.76f, 0.045f, 0.23f), 0.035f, 0.04f, matGold, false, "Piano caster").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            for (int pedal = -1; pedal <= 1; pedal++)
                CreateBox(root, new Vector3(pedal * 0.1f, 0.09f, 0.13f), new Vector3(0.045f, 0.024f, 0.16f), matGold, false, "Separate brass pedal");
            CreateBox(root, new Vector3(0f, 0.98f, 0.035f), new Vector3(0.65f, 0.3f, 0.022f), matDarkMetal, false, "Music rest").transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
        }

        private void CreateTaperedBranch(Transform parent, Vector3 start, Vector3 end, float baseRadius, float tipRadius, bool collision)
        {
            Transform branch = Assembly(parent, "Tapered bark branch", (start + end) * 0.5f);
            branch.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
            branch.gameObject.AddComponent<MeshFilter>().sharedMesh = DeadAirSchoolMeshes.Branch(baseRadius, tipRadius, Vector3.Distance(start, end));
            branch.gameObject.AddComponent<MeshRenderer>().sharedMaterial = matBark;
            if (collision)
            {
                DeadAirSchoolPhysics.AddCapsule(branch, baseRadius, Vector3.Distance(start, end));
            }
        }

        private void BuildRefinedForestTree(Transform parent, Vector3 position)
        {
            Transform root = Assembly(parent, "Dense Forest Tree", position);
            float variation = Mathf.Repeat(Mathf.Abs(position.x * 0.17f + position.z * 0.11f), 1f);
            float height = 11.5f + variation * 3.5f;
            string treeKey = variation < .45f ? "pine-tree" : "fir-tree";
            if (TryInstallSchoolTree(root, treeKey, Vector3.zero, new Vector3(5.8f + variation, height, 5.8f + variation), .45f, 9f,
                Mathf.Repeat(position.x * 19f + position.z * 11f, 360f))) return;
            CreateTaperedBranch(root, Vector3.zero, new Vector3(0.25f, height - 1.5f, 0.1f), 0.45f, 0.08f, true);
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int tier = 0; tier < 5; tier++)
            {
                float y = height * 0.43f + tier * height * 0.105f;
                float radius = 2.7f - tier * 0.35f;
                for (int branch = 0; branch < 3; branch++)
                {
                    float angle = branch * Mathf.PI * 2f / 3f + tier * 0.82f + variation * Mathf.PI;
                    Vector3 endpoint = new Vector3(Mathf.Cos(angle) * radius * 0.68f, y + 0.32f, Mathf.Sin(angle) * radius * 0.68f);
                    CreateTaperedBranch(root, new Vector3(0f, y - 0.38f, 0f), endpoint, 0.11f - tier * 0.012f, 0.025f, false);
                    AddCanopyLobe(vertices, triangles, endpoint, new Vector3(radius * 0.7f, 0.8f + radius * 0.28f, radius * 0.7f), 9, 5);
                }
            }
            AddParkMesh(root, "Layered irregular forest canopy", vertices, triangles, matFoliage, true);
        }

        private static void RemoveScannedTreeMeshColliders(GameObject model)
        {
            foreach (var collider in model.GetComponentsInChildren<MeshCollider>(true))
            {
                collider.enabled = false;
                if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            }
        }

        private bool TryInstallSchoolTree(Transform parent, string key, Vector3 position, Vector3 bounds, float trunkRadius, float trunkHeight, float yaw = 0f)
        {
            var model = DeadAirSchoolPhysics.InstallScanned(parent, key, position, Quaternion.Euler(0f, yaw, 0f), bounds);
            if (model == null) return false;
            RemoveScannedTreeMeshColliders(model);
            DeadAirSchoolPhysics.AddCapsule(model.transform, trunkRadius, trunkHeight, Vector3.up * trunkHeight * .5f);
            return true;
        }

        private bool TryInstallSchoolShrub(Transform parent, Vector3 position, Vector3 bounds, float yaw)
        {
            var model = DeadAirSchoolPhysics.InstallScanned(parent, "shrub", position, Quaternion.Euler(0f, yaw, 0f), bounds);
            if (model == null) return false;
            RemoveScannedTreeMeshColliders(model);
            return true;
        }

        private GameObject BuildScannedPlanterShrubs(Transform parent, Vector3 position, Vector3 size, string name)
        {
            Transform root = Assembly(parent, name, position);
            int clusters = Mathf.Max(1, Mathf.CeilToInt(size.x / 1.35f));
            float width = size.x / clusters;
            for (int i = 0; i < clusters; i++)
            {
                Vector3 anchor = new Vector3(-size.x * .5f + width * (i + .5f), -size.y * .5f, 0f);
                if (TryInstallSchoolShrub(root, anchor, new Vector3(width, size.y, size.z), i * 47f)) continue;
                if (Application.isPlaying) Object.Destroy(root.gameObject); else Object.DestroyImmediate(root.gameObject);
                return null;
            }
            return root.gameObject;
        }

        private void BuildPitchedRoof(Transform parent, Vector3 center, Vector3 size)
        {
            Transform roof = Assembly(parent, "Tiled gable roof", center);
            float slope = 23f;
            float halfRun = size.z * 0.5f;
            float panelLength = halfRun / Mathf.Cos(slope * Mathf.Deg2Rad);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform panel = Assembly(roof, "Separate roof slope", new Vector3(0f, -Mathf.Tan(slope * Mathf.Deg2Rad) * halfRun * 0.5f, side * halfRun * 0.5f));
                panel.localRotation = Quaternion.Euler(side * slope, 0f, 0f);
                CreateBox(panel, Vector3.zero, new Vector3(size.x, 0.1f, panelLength), matKawaraRoof, false, "Ceramic tile roof plane");
                CreateBox(panel, new Vector3(0f, -0.1f, side * panelLength * 0.5f), new Vector3(size.x + 0.08f, 0.18f, 0.13f), matBark, false, "Exposed eave fascia");
                for (float x = -size.x * 0.5f + 0.18f; x < size.x * 0.5f; x += 0.36f)
                    CreateCylinder(panel, new Vector3(x, 0.052f, 0f), 0.055f, panelLength, matKawaraRoof, false, "Kawara tile roll").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            CreateCylinder(roof, new Vector3(0f, 0.06f, 0f), 0.13f, size.x + 0.12f, matKawaraRoof, false, "Ceramic ridge cap").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private void BuildRefinedNewsVan()
        {
            Transform root = Assembly(Root, "Journalist News Van", new Vector3(0f, 0f, 43f), 180f);
            CreateBox(root, new Vector3(0f, 0.64f, 0f), new Vector3(2.22f, 0.38f, 5.05f), matDarkMetal, true, "Van chassis collision body");
            CreateBox(root, new Vector3(0f, 1.1f, -0.3f), new Vector3(2.28f, 0.66f, 4.25f), matWhite, true, "Lower van body shell");
            CreateBox(root, new Vector3(0f, 2.37f, -0.3f), new Vector3(2.28f, 0.11f, 4.3f), matWhite, false, "Separate van roof");
            CreateBox(root, new Vector3(0f, 1.83f, -2.39f), new Vector3(2.28f, 1.08f, 0.06f), matWhite, true, "Rear cargo panel");
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(root, new Vector3(side * 1.1f, 1.82f, -0.8f), new Vector3(0.06f, 1.05f, 3.15f), matWhite, true, "Side cargo skin");
                CreateBox(root, new Vector3(side * 1.1f, 1.89f, 1.13f), new Vector3(0.014f, 0.8f, 0.72f), matGlass, false, "Clear cab side window");
                for (int pillar = -1; pillar <= 1; pillar += 2)
                    CreateBox(root, new Vector3(side * 1.1f, 1.86f, 1.13f + pillar * 0.4f), new Vector3(0.07f, 1.04f, 0.07f), matWhite, false, "Cab window pillar");
                CreateBox(root, new Vector3(side * 1.26f, 1.7f, 1.44f), new Vector3(0.22f, 0.28f, 0.12f), matDarkMetal, false, "Separate wing mirror");
                CreateBox(root, new Vector3(side * 1.145f, 1.2f, 0.9f), new Vector3(0.035f, 0.04f, 0.18f), matDarkMetal, false, "Cab door handle");
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    Vector3 wheel = new Vector3(side * 1.12f, 0.44f, axle * 1.58f);
                    CreateCylinder(root, wheel, 0.42f, 0.25f, matSchoolRubber, false, "Rubber van tyre").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    CreateCylinder(root, wheel + Vector3.right * side * 0.135f, 0.24f, 0.025f, matGuardrail, false, "Separate steel wheel hub").transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
            CreateBox(root, new Vector3(0f, 1.89f, 1.56f), new Vector3(2.1f, 0.85f, 0.015f), matGlass, false, "Separate clear windshield");
            CreateBox(root, new Vector3(0f, 1.32f, 1.66f), new Vector3(2.2f, 0.11f, 0.9f), matWhite, true, "Van bonnet");
            CreateBox(root, new Vector3(0f, 1.04f, 2.1f), new Vector3(2.2f, 0.5f, 0.07f), matWhite, true, "Van front grille surround");
            CreateBox(root, new Vector3(0f, 0.78f, 2.2f), new Vector3(2.33f, 0.13f, 0.18f), matDarkMetal, true, "Separate bumper");
            for (int slat = 0; slat < 5; slat++) CreateBox(root, new Vector3(0f, 0.9f + slat * 0.045f, 2.145f), new Vector3(1.0f, 0.025f, 0.015f), matDarkMetal, false, "Grille slat");
            for (int side = -1; side <= 1; side += 2)
            {
                Transform lamp = Assembly(root, "Headlight", new Vector3(side * 0.83f, 1.11f, 2.15f));
                CreateBox(lamp, Vector3.zero, new Vector3(0.34f, 0.23f, 0.035f), matSchoolCeramic, false, "Headlamp lens");
                Light light = lamp.gameObject.AddComponent<Light>();
                light.type = LightType.Spot; light.color = new Color(1f, 0.96f, 0.85f); light.intensity = 5f; light.range = 24f; light.spotAngle = 48f;
                CreateBox(root, new Vector3(side * 0.83f, 1.03f, -2.435f), new Vector3(0.15f, 0.3f, 0.025f), matTorii, false, "Rear lamp lens");
            }
            CreateBox(root, new Vector3(0f, 1.25f, 1.1f), new Vector3(1.8f, 0.14f, 0.2f), matDarkMetal, false, "Visible dashboard behind glazing");
            CreateCylinder(root, new Vector3(0f, 2.67f, -0.9f), 0.05f, 0.5f, matDarkMetal, false, "Satellite dish mast");
            Transform dish = Assembly(root, "Satellite dish", new Vector3(0f, 2.96f, -0.9f));
            dish.localRotation = Quaternion.Euler(25f, 0f, 0f);
            CreateCylinder(dish, Vector3.zero, 0.65f, 0.045f, matWhite, false, "Dish reflector");
            CreateCylinder(dish, new Vector3(0f, 0.15f, 0f), 0.015f, 0.3f, matDarkMetal, false, "Dish feed support");
        }

        private Transform BuildSinkBench(Transform parent, Vector3 position)
        {
            Transform root = Assembly(parent, "Lab Bench", position);
            BuildWorktable(root, Vector3.zero, new Vector3(7.5f, 0.9f, 1.6f), matLabCounter, "Laboratory steel frame", true, true);
            // The counter is assembled around the bowl opening so glass and basin interiors stay visible.
            Transform bowl = Assembly(root, "Recessed sink bowl", new Vector3(2.8f, 0.45f, 0f));
            CreateBox(bowl, new Vector3(0f, -0.13f, 0f), new Vector3(.815f, .035f, .605f), matSchoolCeramic, false, "Sink bowl bottom");
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox(bowl, new Vector3(side * 0.42f, -0.045f, 0f), new Vector3(0.035f, 0.2f, 0.67f), matSchoolCeramic, false, "Sink side wall");
                CreateBox(bowl, new Vector3(0f, -0.045f, side * 0.315f), new Vector3(0.84f, 0.2f, 0.035f), matSchoolCeramic, false, "Sink end wall");
            }
            CreateCylinder(bowl, new Vector3(0f, -0.107f, 0f), 0.035f, 0.008f, matDarkMetal, false, "Sink drain");
            CreateCylinder(root, new Vector3(2.8f, 0.67f, -0.42f), 0.019f, 0.44f, matWhite, false, "Separate faucet stem");
            CreateCylinder(root, new Vector3(2.8f, 0.87f, -0.23f), 0.019f, 0.38f, matWhite, false, "Faucet spout").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, new Vector3(-2.2f, 0.56f, 0.2f), 0.06f, 0.22f, matGlass, false, "Beaker");
            CreateCylinder(root, new Vector3(-1.6f, 0.575f, 0.2f), 0.08f, 0.25f, matGlass, false, "Beaker 2");
            CreateBox(root, new Vector3(0f, 0.49f, -0.3f), new Vector3(0.45f, 0.07f, 0.18f), matDeskWood, false, "Test tube rack base");
            for (int i = 0; i < 5; i++) CreateCylinder(root, new Vector3(-0.16f + i * 0.08f, 0.6f, -0.3f), 0.016f, 0.22f, matGlass, false, "Test tube");
            DeadAirSchoolPhysics.InstallScanned(root, "microscope", new Vector3(-0.65f, 0.45f, 0.05f), Quaternion.Euler(0f, -20f, 0f), new Vector3(0.18f, 0.35f, 0.23f));
            return root;
        }
    }
}
