using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // Local, transient camera props. Gameplay penalties come only from the host.
    public sealed class DeadAirScarePresentation : MonoBehaviour
    {
        const int ScareLayer = 31; // Visible even when hiding disables the held-equipment camera.
        GameObject spider, signal;
        DeadAirMonsterMotion signalMotion;
        Light key;
        Material shell, joint, silk;
        Mesh faceted;
        readonly System.Collections.Generic.List<Material> apparitionMaterials = new System.Collections.Generic.List<Material>();
        readonly Transform[] legs = new Transform[8];
        float spiderTime = -1, signalTime = -1;
        bool paused;
        public bool SpiderActive => spider != null && spider.activeSelf;
        public bool SignalActive => signal != null && signal.activeSelf;

        public void Initialize(Camera camera)
        {
            transform.SetParent(camera.transform, false);
            gameObject.name = "Transient horror presentation";
            shell = Material("Spider carapace", new Color(.26f, .17f, .09f));
            joint = Material("Spider legs", new Color(.14f, .10f, .06f));
            silk = Material("Spider thread", new Color(.38f, .37f, .31f));
            faceted = FacetedMesh();
            spider = new GameObject("Locker spider");
            spider.transform.SetParent(transform, false);
            Part("Abdomen", spider.transform, shell, new Vector3(0, 0, .025f), new Vector3(.135f, .095f, .15f), true);
            Part("Thorax", spider.transform, shell, new Vector3(0, 0, -.10f), new Vector3(.09f, .07f, .095f), true);
            for (int side = 0; side < 2; side++)
            for (int n = 0; n < 4; n++)
            {
                float sign = side == 0 ? -1 : 1;
                var pivot = new GameObject("Leg " + (side * 4 + n + 1)).transform;
                pivot.SetParent(spider.transform, false);
                pivot.localPosition = new Vector3(sign * .055f, 0, -.075f + n * .038f);
                pivot.localRotation = Quaternion.Euler(0, sign * (n - 1.5f) * 22, 0);
                legs[side * 4 + n] = pivot;
                Segment("Upper leg", pivot, joint, Vector3.zero, new Vector3(sign * .15f, -.018f, .025f), .015f);
                Segment("Lower leg", pivot, joint, new Vector3(sign * .15f, -.018f, .025f), new Vector3(sign * .26f, -.085f, .04f), .011f);
            }
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1 : 1;
                Segment("Fang", spider.transform, joint, new Vector3(sign * .027f, -.02f, -.16f), new Vector3(sign * .04f, -.065f, -.185f), .012f);
            }
            Segment("Trailing thread", spider.transform, silk, new Vector3(0, 0, .14f), new Vector3(0, .5f, .35f), .0025f);
            spider.SetActive(false);
            var monster = Resources.Load<GameObject>("World/monster");
            if (monster != null)
            {
                signal = Instantiate(monster, transform, false);
                signal.name = "Brief signal apparition";
                signalMotion = signal.AddComponent<DeadAirMonsterMotion>();
                signalMotion.Initialize();
                signalMotion.SetPreviewPose("search", 0);
                signal.transform.localScale = Vector3.one * .7f;
                foreach (var collider in signal.GetComponentsInChildren<Collider>(true)) Release(collider);
                foreach (var renderer in signal.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    var materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (materials[index] == null) continue;
                        var material = new Material(materials[index]);
                        Overlay(material);
                        apparitionMaterials.Add(material); materials[index] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
                signal.transform.localRotation = Quaternion.Euler(0, 180, 0);
                // Fit the actual head in camera coordinates, including pitched views.
                if (signalMotion.HeadPivot != null)
                    signal.transform.localPosition += new Vector3(.10f, -.03f, .55f) - transform.InverseTransformPoint(signalMotion.HeadPivot.position);
                signal.SetActive(false);
            }
            SetLayer(spider.transform);
            if (signal != null) SetLayer(signal.transform);
            key = new GameObject("Scare prop reflected light").AddComponent<Light>();
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(-.10f, .10f, .06f);
            key.type = LightType.Point; key.range = 2; key.intensity = 1.7f;
            key.color = new Color(1, .8f, .58f); key.cullingMask = 1 << ScareLayer;
            key.renderMode = LightRenderMode.ForcePixel; key.shadows = LightShadows.None;
            key.enabled = false;
        }

        public void Spider()
        {
            signalTime = -1;
            if (signal != null) signal.SetActive(false);
            spiderTime = 0; spider.SetActive(true); key.enabled = true;
            PoseSpider();
        }

        public void Signal()
        {
            if (SpiderActive || signal == null) return;
            signalTime = 0; signal.SetActive(true); key.enabled = true;
        }

        public void SetPaused(bool value) { paused = value; }

        public void Clear()
        {
            spiderTime = signalTime = -1;
            if (spider != null) spider.SetActive(false);
            if (signal != null) signal.SetActive(false);
            if (key != null) key.enabled = false;
        }

        void Update()
        {
            if (paused) return;
            if (spiderTime >= 0)
            {
                spiderTime += Time.unscaledDeltaTime;
                PoseSpider();
                if (spiderTime > 1.05f) { spiderTime = -1; spider.SetActive(false); }
            }
            if (signalTime >= 0)
            {
                signalTime += Time.unscaledDeltaTime;
                signalMotion?.SetPreviewPose("search", signalTime * 3);
                if (signalTime > .32f) { signalTime = -1; signal.SetActive(false); }
            }
            if (key != null) key.enabled = SpiderActive || SignalActive;
        }

        void PoseSpider()
        {
            float t = Mathf.Clamp01(spiderTime / 1.05f);
            if (t < .24f)
                spider.transform.localPosition = Vector3.Lerp(new Vector3(.28f, .38f, 1.1f), new Vector3(.04f, .02f, .38f), Mathf.SmoothStep(0, 1, t / .24f));
            else
                spider.transform.localPosition = Vector3.Lerp(new Vector3(.04f, .02f, .38f), new Vector3(-.8f, -.6f, .68f), Mathf.Pow((t - .24f) / .76f, 1.7f));
            spider.transform.localRotation = Quaternion.Euler(-12 + t * 32, Mathf.Sin(t * 8) * 20, -t * 36);
            for (int i = 0; i < legs.Length; i++)
            {
                float sign = i < 4 ? -1 : 1;
                legs[i].localRotation = Quaternion.Euler(Mathf.Sin(spiderTime * 31 + i * 1.7f) * 14, sign * (i % 4 - 1.5f) * 22, Mathf.Sin(spiderTime * 25 + i) * 13);
            }
        }

        static void SetLayer(Transform root)
        { foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ScareLayer; }

        static Material Material(string name, Color color)
        {
            var material = new Material(Shader.Find("Dead Air/Source Surface")) { name = name };
            material.SetColor("_Color", color); material.SetColor("_EmissionColor", Color.black);
            Overlay(material);
            return material;
        }

        // These fleeting camera props must remain visible against a nearby wall
        // or locker grille. The original world materials keep normal depth tests.
        static void Overlay(Material material)
        {
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.SetFloat("_ZWrite", 0);
            material.renderQueue = (int)RenderQueue.Overlay;
        }

        void Part(string name, Transform parent, Material material, Vector3 position, Vector3 scale, bool oval)
        {
            var part = new GameObject(name); part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            var filter = part.AddComponent<MeshFilter>();
            if (oval) filter.sharedMesh = faceted;
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                filter.sharedMesh = box.GetComponent<MeshFilter>().sharedMesh; Release(box);
            }
            var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        void Segment(string name, Transform parent, Material material, Vector3 from, Vector3 to, float width)
        {
            Vector3 direction = to - from;
            Part(name, parent, material, (from + to) * .5f, new Vector3(width, width, direction.magnitude), false);
            parent.GetChild(parent.childCount - 1).localRotation = Quaternion.LookRotation(direction);
        }

        static Mesh FacetedMesh()
        {
            var mesh = new Mesh { name = "Faceted spider shell" };
            Vector3[] corners = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[] faces = { 0,4,3, 0,2,4, 0,5,2, 0,3,5, 1,3,4, 1,4,2, 1,2,5, 1,5,3 };
            var vertices = new Vector3[faces.Length]; var triangles = new int[faces.Length];
            for (int i = 0; i < faces.Length; i++) { vertices[i] = corners[faces[i]]; triangles[i] = i; }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var material in new[] { shell, joint, silk }) if (material != null) Release(material);
            foreach (var material in apparitionMaterials) if (material != null) Release(material);
            if (faceted != null) Release(faceted);
        }

        static void Release(Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
