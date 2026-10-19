using UnityEngine;

namespace DeadAir
{
    public static class DeadAirArtMotionProps
    {
        public static GameObject Flashlight(Transform parent)
        {
            var root = new GameObject("Articulated service flashlight"); root.transform.SetParent(parent, false);
            var rubber = Material("Service flashlight / rubber", "rubber", new Color(.10f, .12f, .12f), .24f);
            var steel = Material("Service flashlight / brushed steel", "steel", new Color(.46f, .50f, .49f), .46f);
            var glass = Material("Service flashlight / lens", "glass", new Color(.64f, .72f, .70f), .91f);
            Cylinder(root.transform, "Ribbed rubber grip", Vector3.zero, .073f, .18f, rubber);
            for (int i = 0; i < 5; i++)
                Cylinder(root.transform, "Grip rib", new Vector3(0, 0, i * .022f - .07f), .077f, .007f, rubber);
            Cylinder(root.transform, "Service lamp head", new Vector3(0, 0, .115f), .118f, .083f, rubber);
            Cylinder(root.transform, "Reflector rim", new Vector3(0, 0, .16f), .111f, .012f, steel);
            Cylinder(root.transform, "Glass lens", new Vector3(0, 0, .168f), .09f, .004f, glass);
            Cylinder(root.transform, "Battery access cap", new Vector3(0, 0, -.1f), .076f, .02f, steel);
            var button = Shape(root.transform, "Recessed power switch", PrimitiveType.Cube, new Vector3(0, .037f, .035f),
                new Vector3(.02f, .009f, .031f), rubber);
            button.transform.localRotation = Quaternion.identity;
            return root;
        }

        static Material Material(string name, string surface, Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Dead Air/Source Surface") ?? Shader.Find("Standard"))
                { name = name, color = color, hideFlags = HideFlags.DontSave };
            material.SetFloat("_Glossiness", smoothness);
            DeadAirArtMaterials.Apply(material, surface);
            return material;
        }

        static void Cylinder(Transform parent, string name, Vector3 position, float diameter, float length, Material material)
        {
            var part = Shape(parent, name, PrimitiveType.Cylinder, position, new Vector3(diameter, length / 2, diameter), material);
            part.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        static GameObject Shape(Transform parent, string name, PrimitiveType shape, Vector3 position, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(shape); part.name = name; part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = size;
            part.GetComponent<Renderer>().sharedMaterial = material;
            var collider = part.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            return part;
        }

        public static float RestingHeight(Transform root)
        {
            bool observed = false;
            float bottom = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                float localBottom = renderer.bounds.min.y - root.position.y;
                if (!observed || localBottom < bottom) { observed = true; bottom = localBottom; }
            }
            return observed ? .008f - bottom : .025f;
        }
    }
}
