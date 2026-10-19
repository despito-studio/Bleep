using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    // Furniture geometry and snapshot presentation share the same prop-derived drawer IDs.
    public sealed class DeadAirDrawerPresentation
    {
        sealed class View
        {
            public Transform root;
            public Vector3 closed, from;
            public float age, extension;
            public bool open;
            public GameObject battery, coins;
        }
        readonly Dictionary<string, View> drawers = new Dictionary<string, View>();
        readonly Dictionary<string, GameObject> batteries = new Dictionary<string, GameObject>();
        float previousElapsed = -1;

        public DeadAirDrawerPresentation(Transform root, WorldData world)
        {
            var black = Material("Battery casing", new Color(.08f, .075f, .06f));
            var brass = Material("Battery brass", new Color(.7f, .47f, .14f));
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("drawer:")) continue;
                string id = part.name.Substring("drawer:".Length);
                var data = world.Drawer(id);
                if (data == null) continue;
                var view = new View { root = part, closed = part.localPosition, extension = Mathf.Min(.38f, data.depth * .72f) };
                DeadAirArtMotion.MakeDynamic(part);
                view.battery = Battery(part, black, brass);
                view.battery.transform.localPosition = new Vector3(0, -data.height / 2 + .05f, -.12f);
                view.battery.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var coins = new GameObject("drawer loot coins"); coins.transform.SetParent(part, false);
                for (int i = 0; i < 4; i++)
                    Shape(coins.transform, "Coin", PrimitiveType.Cylinder,
                        new Vector3((i % 2 - .5f) * .03f, -data.height / 2 + .024f + (i / 2) * .002f, -.12f),
                        new Vector3(.026f, .001f, .026f), brass, false);
                view.coins = coins;
                view.battery.SetActive(false); view.coins.SetActive(false);
                drawers.Add(id, view);
            }
            foreach (var data in world.Batteries)
            {
                var battery = Battery(root, black, brass);
                battery.name = "interactive:" + data.id;
                battery.transform.position = DeadAirOfficeLayout.Position(data.x, data.y, data.z);
                battery.transform.rotation = Quaternion.Euler(90, 0, 0);
                batteries.Add(data.id, battery);
            }
        }

        public void Apply(RoomState state, float deltaTime)
        {
            bool reset = previousElapsed > state.elapsed || previousElapsed < 0;
            previousElapsed = state.elapsed;
            foreach (var pair in drawers)
            {
                var stateDrawer = state.Drawer(pair.Key);
                var view = pair.Value;
                bool open = stateDrawer != null && stateDrawer.open;
                var target = view.closed + Vector3.forward * (open ? view.extension : 0);
                var current = view.root.localPosition;
                if (reset) { view.open = open; view.from = target; view.age = .36f; }
                else if (view.open != open) { view.open = open; view.from = current; view.age = 0; }
                if (deltaTime > 0) view.age = Mathf.Min(.36f, view.age + deltaTime);
                var next = reset ? target : Vector3.LerpUnclamped(view.from, target, DeadAirArtMotion.Ease(view.age / .36f));
                if (!current.Equals(next)) view.root.localPosition = next;
                bool visible = stateDrawer != null && stateDrawer.open && stateDrawer.searched && !stateDrawer.lootTaken;
                view.battery.SetActive(visible && stateDrawer.loot == DeadAirSupplies.BatteryItem);
                view.coins.SetActive(visible && stateDrawer.loot == "coins");
            }
            foreach (var pair in batteries) pair.Value.SetActive(!state.Collected(pair.Key));
        }

        public static void BuildFurnitureDrawers(Transform root, PropData prop, Material wood, Material trim, Material metal, Material paper)
        {
            var drawers = DeadAirDrawers.ForProp(prop);
            if (drawers.Length == 0) return;
            bool desk = prop.type == "workstation";
            float width = prop.w > 0 ? prop.w : 1, depth = prop.d > 0 ? prop.d : 1;
            float cabinetWidth = desk ? .49f : width, cabinetHeight = desk ? .73f : 1.7f;
            float cabinetX = desk ? -width * .34f : 0;
            float cabinetDepth = desk ? depth * .84f : depth;
            var cabinetMaterial = desk ? trim : metal;
            // Hollow sides/back replace the original solid cube, so extended trays are real cavities.
            Block(root, "Drawer cabinet left", new Vector3(cabinetX - cabinetWidth / 2 + .025f, cabinetHeight / 2, 0),
                new Vector3(.05f, cabinetHeight, cabinetDepth), cabinetMaterial, true);
            Block(root, "Drawer cabinet right", new Vector3(cabinetX + cabinetWidth / 2 - .025f, cabinetHeight / 2, 0),
                new Vector3(.05f, cabinetHeight, cabinetDepth), cabinetMaterial, true);
            Block(root, "Drawer cabinet back", new Vector3(cabinetX, cabinetHeight / 2, -cabinetDepth / 2 + .025f),
                new Vector3(cabinetWidth, cabinetHeight, .05f), cabinetMaterial, true);
            Block(root, "Drawer cabinet top", new Vector3(cabinetX, cabinetHeight - .025f, 0),
                new Vector3(cabinetWidth, .05f, cabinetDepth), cabinetMaterial, true);
            Block(root, "Drawer cabinet foot", new Vector3(cabinetX, .025f, 0),
                new Vector3(cabinetWidth, .05f, cabinetDepth), cabinetMaterial, true);
            if (!desk) Block(root, "Drawer column divider", new Vector3(0, cabinetHeight / 2, 0),
                new Vector3(.035f, cabinetHeight, cabinetDepth), cabinetMaterial, false);
            foreach (var data in drawers)
            {
                var drawer = new GameObject("drawer:" + data.id).transform;
                drawer.SetParent(root, false); drawer.localPosition = data.localPosition;
                float w = data.width, h = data.height, d = data.depth, t = .02f;
                foreach (float sign in new[] { -1f, 1f })
                {
                    var rail = data.localPosition + new Vector3(sign * (w / 2 + .012f), -h * .31f, -.025f);
                    Block(root, "Fixed drawer slide rail", rail, new Vector3(.012f, .022f, d * .9f), metal, false);
                    Block(drawer, "Moving drawer slide rail", new Vector3(sign * (w / 2 + .005f), -h * .31f, 0),
                        new Vector3(.01f, .016f, d * .85f), metal, false);
                }
                Block(drawer, "Drawer tray bottom", new Vector3(0, -h / 2 + t / 2, 0), new Vector3(w - t * 2, t, d - t * 2), wood, false);
                Block(drawer, "Drawer tray back", new Vector3(0, 0, -d / 2 + t / 2), new Vector3(w, h, t), wood, false);
                foreach (float sign in new[] { -1f, 1f })
                    Block(drawer, "Drawer tray side", new Vector3(sign * (w / 2 - t / 2), 0, 0), new Vector3(t, h, d), wood, false);
                Block(drawer, "Drawer front", new Vector3(0, 0, d / 2 + .012f), new Vector3(w + .02f, h + .015f, .025f), trim, true);
                Block(drawer, "Drawer pull", new Vector3(0, desk ? 0 : -.06f, d / 2 + .04f), new Vector3(.17f, .025f, .045f), metal, false);
                if (!desk) Block(drawer, "Drawer label", new Vector3(0, .06f, d / 2 + .03f), new Vector3(.2f, .07f, .015f), paper, false);
                if (!data.directorKey) continue;
                var card = new GameObject("interactive:key-main").transform;
                card.SetParent(drawer, false); card.localPosition = new Vector3(0, -h / 2 + t + .004f, .15f);
                card.localRotation = Quaternion.Euler(0, 15, 0);
                Block(card, "Access card body", Vector3.zero, new Vector3(.14f, .006f, .09f), paper, false);
                Block(card, "Card magnetic stripe", new Vector3(0, .0035f, .026f), new Vector3(.14f, .002f, .018f), trim, false);
                Block(card, "Card chip", new Vector3(-.035f, .0035f, -.012f), new Vector3(.028f, .002f, .024f), wood, false);
                Block(card, "Card photo badge", new Vector3(.035f, .0035f, -.012f), new Vector3(.032f, .002f, .036f), metal, false);
            }
        }

        public static GameObject CreateBattery(Transform parent)
        {
            return Battery(parent, Material("Battery casing", new Color(.08f, .075f, .06f)), Material("Battery brass", new Color(.7f, .47f, .14f)));
        }

        static GameObject Battery(Transform parent, Material body, Material cap)
        {
            var root = new GameObject("Battery"); root.transform.SetParent(parent, false);
            Shape(root.transform, "Battery insulated wrap", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.034f, .029f, .034f), body, false);
            foreach (float side in new[] { -1f, 1f })
            {
                Shape(root.transform, "Battery rolled end seam", PrimitiveType.Cylinder, new Vector3(0, side * .028f, 0), new Vector3(.035f, .0016f, .035f), cap, false);
                Shape(root.transform, "Battery identification band", PrimitiveType.Cylinder, new Vector3(0, side * .018f, 0), new Vector3(.0343f, .0018f, .0343f), cap, false);
            }
            Shape(root.transform, "Battery positive end plate", PrimitiveType.Cylinder, new Vector3(0, .03f, 0), new Vector3(.032f, .001f, .032f), cap, false);
            Shape(root.transform, "Battery positive terminal", PrimitiveType.Cylinder, new Vector3(0, .032f, 0), new Vector3(.011f, .0018f, .011f), cap, false);
            Shape(root.transform, "Battery negative plate", PrimitiveType.Cylinder, new Vector3(0, -.0295f, 0), new Vector3(.029f, .0007f, .029f), cap, false);
            return root;
        }

        static Material Material(string name, Color color)
        {
            var material = new Material(Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard"))
                { name = name, color = color, hideFlags = HideFlags.DontSave };
            material.SetFloat("_Glossiness", .23f);
            DeadAirArtMaterials.Apply(material, name.Contains("brass") ? "brass" : "rubber");
            return material;
        }
        static void Block(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision) =>
            Shape(parent, name, PrimitiveType.Cube, position, size, material, collision);

        static void Shape(Transform parent, string name, PrimitiveType shape, Vector3 position, Vector3 size, Material material, bool collision)
        {
            var obj = GameObject.CreatePrimitive(shape); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (collision) return;
            var collider = obj.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
        }
    }
}
