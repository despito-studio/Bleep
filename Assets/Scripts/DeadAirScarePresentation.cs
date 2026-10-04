using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // Local, transient full-screen scares. Gameplay penalties come only from the host.
    public sealed class DeadAirScarePresentation : MonoBehaviour
    {
        const int ScareLayer = 31;
        public const float SpiderDuration = 1.2f, SignalDuration = .85f;
        static DeadAirScarePresentation activePresentation;
        GameObject spider, signal;
        DeadAirMonsterMotion signalMotion;
        Light key, rim;
        Material shell, joint, silk, eye;
        Mesh faceted;
        Camera sourceCamera, scareCamera;
        bool sourceHadScareLayer;
        float signalScale;
        Vector3 signalHeadOffset;
        readonly System.Collections.Generic.List<Material> apparitionMaterials = new System.Collections.Generic.List<Material>();
        readonly Transform[] legs = new Transform[8];
        float spiderTime = -1, signalTime = -1;
        bool paused;
        public bool SpiderActive => spider != null && spider.activeSelf;
        public bool SignalActive => signal != null && signal.activeSelf;
        public bool IsFullscreenActive => SpiderActive || SignalActive;
        public static bool FullscreenActive => activePresentation != null && activePresentation.IsFullscreenActive;
        public Camera FullscreenCamera => scareCamera;

        public void Initialize(Camera camera)
        {
            sourceCamera = camera;
            sourceHadScareLayer = (camera.cullingMask & (1 << ScareLayer)) != 0;
            camera.cullingMask &= ~(1 << ScareLayer);
            transform.SetParent(camera.transform, false);
            gameObject.name = "Fullscreen horror presentation";
            scareCamera = new GameObject("Fullscreen scare camera").AddComponent<Camera>();
            scareCamera.transform.SetParent(transform, false);
            scareCamera.cullingMask = 1 << ScareLayer;
            scareCamera.clearFlags = CameraClearFlags.SolidColor;
            scareCamera.backgroundColor = new Color(.006f, .004f, .003f, 1);
            scareCamera.nearClipPlane = .025f; scareCamera.farClipPlane = 8;
            scareCamera.fieldOfView = 55; scareCamera.depth = camera.depth + 50;
            scareCamera.allowHDR = false; scareCamera.allowMSAA = false;
            scareCamera.useOcclusionCulling = false; scareCamera.targetDisplay = camera.targetDisplay;
            scareCamera.enabled = false;
            shell = Material("Spider carapace", new Color(.23f, .14f, .07f));
            joint = Material("Spider legs", new Color(.11f, .065f, .035f));
            silk = Material("Spider thread", new Color(.38f, .37f, .31f));
            eye = Material("Spider eyes", new Color(.56f, .11f, .025f));
            eye.SetColor("_EmissionColor", new Color(.22f, .015f, .002f));
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
                pivot.localScale = new Vector3(1.12f, 1, 1);
                legs[side * 4 + n] = pivot;
                Segment("Upper leg", pivot, joint, Vector3.zero, new Vector3(sign * .15f, -.018f, .025f), .015f);
                Segment("Lower leg", pivot, joint, new Vector3(sign * .15f, -.018f, .025f), new Vector3(sign * .26f, -.085f, .04f), .011f);
            }
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1 : 1;
                Segment("Fang", spider.transform, joint, new Vector3(sign * .027f, -.02f, -.16f), new Vector3(sign * .04f, -.065f, -.185f), .012f);
                Part("Mandible", spider.transform, shell, new Vector3(sign * .033f, -.01f, -.155f), new Vector3(.028f, .026f, .044f), true);
                for(int n = 0; n < 3; n++)
                    Part("Eye", spider.transform, eye, new Vector3(sign * (.017f + n * .019f), .044f, -.161f + n * .012f), new Vector3(.009f, .010f, .008f), true);
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
                signal.transform.localScale = Vector3.one;
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
                        ScareSurface(material);
                        apparitionMaterials.Add(material); materials[index] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
                signal.transform.localRotation = Quaternion.Euler(0, 180, 0);
                FitSignal();
                signal.SetActive(false);
            }
            SetLayer(spider.transform);
            if (signal != null) SetLayer(signal.transform);
            key = new GameObject("Scare prop reflected light").AddComponent<Light>();
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(-.3f, .24f, .06f);
            key.type = LightType.Point; key.range = 3; key.intensity = 3.4f;
            key.color = new Color(1, .76f, .52f); key.cullingMask = 1 << ScareLayer;
            key.renderMode = LightRenderMode.ForcePixel; key.shadows = LightShadows.None;
            key.enabled = false;
            rim = new GameObject("Scare cold rim light").AddComponent<Light>();
            rim.transform.SetParent(transform, false); rim.transform.localPosition = new Vector3(.45f, -.1f, .8f);
            rim.type = LightType.Point; rim.range = 2; rim.intensity = 2;
            rim.color = new Color(.45f, .62f, .84f); rim.cullingMask = 1 << ScareLayer;
            rim.renderMode = LightRenderMode.ForcePixel; rim.shadows = LightShadows.None; rim.enabled = false;
        }

        public void Spider()
        {
            signalTime = -1;
            if (signal != null) signal.SetActive(false);
            spiderTime = 0; spider.SetActive(true);
            ActivateFullscreen();
            PoseSpider();
        }

        public void Signal()
        {
            if (SpiderActive || signal == null) return;
            signalTime = 0; signal.SetActive(true);
            ActivateFullscreen(); PoseSignal();
        }

        public void SetPaused(bool value) { paused = value; if (value) Clear(); }

        public void Clear()
        {
            spiderTime = signalTime = -1;
            if (spider != null) spider.SetActive(false);
            if (signal != null) signal.SetActive(false);
            if (key != null) key.enabled = false;
            if (rim != null) rim.enabled = false;
            if (scareCamera != null) scareCamera.enabled = false;
            if (activePresentation == this) activePresentation = null;
        }

        void Update()
        {
            if (paused) return;
            if (spiderTime >= 0)
            {
                spiderTime += Time.unscaledDeltaTime;
                PoseSpider();
                if (spiderTime > SpiderDuration) { spiderTime = -1; spider.SetActive(false); }
            }
            if (signalTime >= 0)
            {
                signalTime += Time.unscaledDeltaTime;
                signalMotion?.SetPreviewPose("search", signalTime * 3);
                PoseSignal();
                if (signalTime > SignalDuration) { signalTime = -1; signal.SetActive(false); }
            }
            if (!IsFullscreenActive) Clear();
        }

        void PoseSpider()
        {
            float t = Mathf.Clamp01(spiderTime / SpiderDuration);
            float rush = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .13f));
            float retreat = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.77f, 1, t));
            float depth = Mathf.Lerp(.75f, .52f, rush) + retreat * .35f;
            float frameHeight = 2 * depth * Mathf.Tan(scareCamera.fieldOfView * Mathf.Deg2Rad * .5f);
            float size = frameHeight * Mathf.Max(2, scareCamera.aspect) / .90f;
            spider.transform.localScale = Vector3.one * size;
            spider.transform.localPosition = new Vector3(Mathf.Sin(t * 73) * .008f, .060f - retreat * .55f, depth);
            spider.transform.localRotation = Quaternion.Euler(-53 + retreat * 20, Mathf.Sin(t * 33) * 7, Mathf.Sin(t * 47) * 5);
            for (int i = 0; i < legs.Length; i++)
            {
                float sign = i < 4 ? -1 : 1;
                legs[i].localRotation = Quaternion.Euler(Mathf.Sin(spiderTime * 31 + i * 1.7f) * 14, sign * (i % 4 - 1.5f) * 22, Mathf.Sin(spiderTime * 25 + i) * 13);
            }
        }

        void ActivateFullscreen()
        {
            activePresentation = this;
            scareCamera.aspect = Mathf.Max(1, Screen.width) / (float)Mathf.Max(1, Screen.height);
            scareCamera.rect = new Rect(0, 0, 1, 1);
            scareCamera.enabled = true; key.enabled = true; rim.enabled = true;
        }

        void FitSignal()
        {
            if (signalMotion.HeadPivot == null) return;
            var headRenderers = signalMotion.HeadPivot.GetComponentsInChildren<Renderer>(true);
            if (headRenderers.Length == 0) return;
            Bounds bounds = headRenderers[0].bounds;
            for (int i = 1; i < headRenderers.Length; i++) bounds.Encapsulate(headRenderers[i].bounds);
            float height = Mathf.Max(.2f, bounds.size.y);
            signalScale = .43f / height;
            signalHeadOffset = signal.transform.InverseTransformPoint(bounds.center);
            signal.transform.localScale = Vector3.one * signalScale;
            PoseSignal();
        }

        void PoseSignal()
        {
            if (signal == null || signalScale <= 0) return;
            float rush = Mathf.SmoothStep(0, 1, Mathf.Clamp01(signalTime / .12f));
            float depth = Mathf.Lerp(.78f, .55f, rush);
            signal.transform.localRotation = Quaternion.Euler(Mathf.Sin(signalTime * 41) * 3, 180 + Mathf.Sin(signalTime * 32) * 5, Mathf.Sin(signalTime * 29) * 4);
            signal.transform.localPosition = new Vector3(Mathf.Sin(signalTime * 63) * .009f, -.035f, depth) - signal.transform.localRotation * (signalHeadOffset * signalScale);
        }

        // Explicit camera composition is also used by the batch capture runner.
        public void RenderFullscreen(RenderTexture target)
        {
            if (!IsFullscreenActive || scareCamera == null) return;
            var old = scareCamera.targetTexture; float aspect = scareCamera.aspect;
            try
            {
                scareCamera.targetTexture = target;
                if (target != null) scareCamera.aspect = target.width / (float)target.height;
                scareCamera.Render();
            }
            finally { scareCamera.targetTexture = old; scareCamera.aspect = aspect; }
        }

        static void SetLayer(Transform root)
        { foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = ScareLayer; }

        static Material Material(string name, Color color)
        {
            var material = new Material(Shader.Find("Dead Air/Source Surface")) { name = name };
            material.SetColor("_Color", color); material.SetColor("_EmissionColor", Color.black);
            ScareSurface(material);
            return material;
        }

        // The isolated full-screen camera clears the world. Keep normal depth
        // tests within the creature so legs, fangs and face occlude correctly.
        static void ScareSurface(Material material)
        {
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            material.SetFloat("_ZWrite", 1);
            material.renderQueue = (int)RenderQueue.Geometry;
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
            Clear();
            if (sourceCamera != null && sourceHadScareLayer) sourceCamera.cullingMask |= 1 << ScareLayer;
            foreach (var material in new[] { shell, joint, silk, eye }) if (material != null) Release(material);
            foreach (var material in apparitionMaterials) if (material != null) Release(material);
            if (faceted != null) Release(faceted);
        }

        static void Release(Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
