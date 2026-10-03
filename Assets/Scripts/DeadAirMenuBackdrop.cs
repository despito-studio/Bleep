using UnityEngine;

namespace DeadAir
{
    // A live view into chapter one's office. These lights belong to the menu,
    // so changing the menu palette never changes illumination during a run.
    public sealed class DeadAirMenuBackdrop : MonoBehaviour
    {
        GameObject presentation;
        Light corridorPractical;
        Material housingMaterial, lensMaterial;

        public static Vector3 OperativePosition => new Vector3(-1.05f, 0, -.9f);
        public static Quaternion OperativeRotation => Quaternion.Euler(0, 23, 0);
        public bool IsVisible => presentation != null && presentation.activeSelf;

        public void SetVisible(bool visible)
        {
            if (visible && presentation == null) CreatePresentation();
            if (presentation != null && presentation.activeSelf != visible) presentation.SetActive(visible);
        }

        public void Frame(Camera camera, Transform actor)
        {
            SetVisible(true);
            float time = Time.unscaledTime;
            // Subtle tripod drift, kept well below the game's head-bob amplitude.
            float drift = Mathf.Sin(time * .11f) * .025f;
            actor.SetPositionAndRotation(new Vector3(.6f + drift, 0, 3), Quaternion.identity);
            camera.transform.localPosition = new Vector3(0, 1.6f + Mathf.Sin(time * .13f) * .012f, 0);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(.45f, 1.48f, -23) - camera.transform.position);
            camera.fieldOfView = 58;
            if (corridorPractical != null)
                corridorPractical.intensity = 1.7f + Mathf.Sin(time * .4f) * .06f;
        }

        void CreatePresentation()
        {
            presentation = new GameObject("Menu only — office emergency lighting");
            corridorPractical = AddLight("Deep red corridor practical", new Vector3(-2.7f, 2.7f, -10.4f),
                new Color32(184, 31, 26, 255), 1.7f, 10.5f);
            AddLight("Entrance bounced fill", new Vector3(1.9f, 2.3f, .3f),
                new Color32(159, 176, 168, 255), .32f, 6.5f);

            var shader = Shader.Find("Unlit/Color");
            if (shader == null) return;
            housingMaterial = new Material(shader) { name = "Menu practical dark housing", color = new Color32(25, 22, 21, 255) };
            lensMaterial = new Material(shader) { name = "Menu practical red glass", color = new Color32(149, 26, 23, 255) };
            AddBlock("Emergency lamp housing", new Vector3(-2.7f, 2.73f, -10.84f), new Vector3(.52f, .22f, .13f), housingMaterial);
            AddBlock("Emergency lamp lens", new Vector3(-2.7f, 2.73f, -10.76f), new Vector3(.42f, .14f, .03f), lensMaterial);
        }

        Light AddLight(string label, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(label).AddComponent<Light>();
            light.transform.SetParent(presentation.transform, false);
            light.transform.position = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            // The operator has its own controlled key and rim on layer 29.
            light.cullingMask = ~(1 << 29);
            return light;
        }

        void AddBlock(string label, Vector3 position, Vector3 scale, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = label;
            block.transform.SetParent(presentation.transform, false);
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
            Destroy(block.GetComponent<Collider>());
        }

        void OnDisable() { SetVisible(false); }

        void OnDestroy()
        {
            if (presentation != null) Destroy(presentation);
            if (housingMaterial != null) Destroy(housingMaterial);
            if (lensMaterial != null) Destroy(lensMaterial);
        }
    }
}
