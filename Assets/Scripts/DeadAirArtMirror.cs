using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    public sealed class DeadAirArtMirror : MonoBehaviour
    {
        struct HiddenMirror { public Renderer renderer; public bool enabled; }
        struct CulledSurface { public Renderer renderer; public bool enabled; public ShadowCastingMode shadows; }
        static readonly List<DeadAirArtMirror> mirrors = new List<DeadAirArtMirror>();
        static bool renderingReflection;
        static readonly int MirrorTexture = Shader.PropertyToID("_MirrorTex");
        static readonly int MirrorMatrix = Shader.PropertyToID("_MirrorVP");
        static readonly int MirrorReady = Shader.PropertyToID("_ReflectionReady");

        [SerializeField] Vector3 localNormal = Vector3.forward;
        [SerializeField, Range(0, .2f)] float tarnish = .045f;
        [SerializeField, Min(.001f)] float clipOffset = .012f;
        [SerializeField, Min(2)] float maximumDistance = 24;
        readonly List<HiddenMirror> hiddenMirrors = new List<HiddenMirror>();
        readonly List<CulledSurface> culledSurfaces = new List<CulledSurface>();
        readonly Plane[] reflectedFrustum = new Plane[6];
        Renderer pane;
        Renderer[] environmentSurfaces;
        Material originalMaterial, mirrorMaterial;
        MaterialPropertyBlock properties, originalProperties;
        RenderTexture reflection;
        Camera reflectionCamera, lastSource;
        Matrix4x4 lastSourceView, lastSourceProjection;
        int lastRenderedFrame = -1;
        bool initialized;

        public RenderTexture ReflectionFrame => reflection;
        public Camera ReflectionCamera => reflectionCamera;
        public bool HasOwnedResources => reflection != null || reflectionCamera != null || mirrorMaterial != null;
        public Vector3 PlaneNormal => transform.localToWorldMatrix.inverse.transpose.MultiplyVector(localNormal.normalized).normalized;
        public Vector3 PlanePoint
        {
            get
            {
                var filter = GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return transform.position;
                var bounds = filter.sharedMesh.bounds; var n = localNormal.normalized;
                float support = Vector3.Dot(new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z)), bounds.extents);
                return transform.TransformPoint(bounds.center + n * support);
            }
        }

        public static DeadAirArtMirror Attach(Renderer pane, Vector3 localNormal)
        {
            var mirror = pane.GetComponent<DeadAirArtMirror>() ?? pane.gameObject.AddComponent<DeadAirArtMirror>();
            mirror.localNormal = localNormal.sqrMagnitude > .0001f ? localNormal.normalized : Vector3.forward;
            return mirror;
        }

        void OnEnable() { if (!mirrors.Contains(this)) mirrors.Add(this); }
        void OnDisable() { mirrors.Remove(this); ReleaseOwnedResources(); }
        void OnDestroy() { mirrors.Remove(this); ReleaseOwnedResources(); }

        void OnWillRenderObject()
        {
            var source = Camera.current;
            if (source != null) RenderReflection(source);
        }

        bool Initialize()
        {
            if (initialized) return pane != null && mirrorMaterial != null;
            pane = GetComponent<Renderer>();
            if (pane == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return false;
            var shader = Resources.Load<Shader>("Shaders/DeadAirArtMirror") ?? Shader.Find("Dead Air/Art Mirror");
            if (shader == null || !shader.isSupported) return false;
            originalMaterial = pane.sharedMaterial;
            originalProperties = new MaterialPropertyBlock(); pane.GetPropertyBlock(originalProperties);
            properties = new MaterialPropertyBlock();
            mirrorMaterial = new Material(shader) { name = "Silvered vertical mirror / local reflection", hideFlags = HideFlags.HideAndDontSave };
            if (originalMaterial != null)
            {
                if (originalMaterial.HasProperty("_Color")) mirrorMaterial.color = Color.Lerp(Color.white, originalMaterial.color, .22f);
                if (originalMaterial.HasProperty("_MainTex"))
                {
                    mirrorMaterial.mainTexture = originalMaterial.mainTexture;
                    mirrorMaterial.mainTextureScale = originalMaterial.mainTextureScale;
                    mirrorMaterial.mainTextureOffset = originalMaterial.mainTextureOffset;
                }
                if (originalMaterial.HasProperty("_Glossiness")) mirrorMaterial.SetFloat("_Glossiness", originalMaterial.GetFloat("_Glossiness"));
            }
            mirrorMaterial.SetFloat("_Tarnish", tarnish);
            pane.sharedMaterial = mirrorMaterial;
            environmentSurfaces = transform.root.GetComponentsInChildren<Renderer>(true);
            initialized = true;
            return true;
        }

        public bool RenderReflection(Camera source)
        {
            if (renderingReflection || DeadAirArtReflectionProbes.IsCapturing || !isActiveAndEnabled || source == null || source == reflectionCamera || source.cameraType == CameraType.Reflection ||
                source.name == "Dead Air mirrored floor camera") return false;
            if (!Initialize()) return false;
            var normal = PlaneNormal; var point = PlanePoint;
            if (!DeadAirSettings.Reflections || Vector3.Dot(source.transform.position - point, normal) <= .015f ||
                pane.bounds.SqrDistance(source.transform.position) > maximumDistance * maximumDistance)
            {
                SetReflectionReady(false); return false;
            }
            if (lastRenderedFrame == Time.frameCount && lastSource == source && lastSourceView == source.worldToCameraMatrix &&
                lastSourceProjection == source.projectionMatrix && reflection != null)
            {
                SetReflectionReady(true); return true;
            }
            int resolution = DeadAirSettings.GraphicsQuality >= 2 ? 768 : 512;
            EnsureRenderTarget(resolution);
            if (reflectionCamera == null)
            {
                var view = new GameObject("Dead Air vertical mirror camera") { hideFlags = HideFlags.HideAndDontSave };
                reflectionCamera = view.AddComponent<Camera>(); reflectionCamera.enabled = false;
            }
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled = false; reflectionCamera.targetTexture = reflection;
            reflectionCamera.allowHDR = true; reflectionCamera.allowMSAA = false; reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.depthTextureMode = DepthTextureMode.None;
            reflectionCamera.cullingMask = source.cullingMask & ~((1 << 28) | (1 << 29) | (1 << 30));
            reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectionCamera.backgroundColor = source.backgroundColor;
            var reflectionMatrix = ReflectionMatrix(normal, point);
            reflectionCamera.transform.position = reflectionMatrix.MultiplyPoint(source.transform.position);
            var forward = reflectionMatrix.MultiplyVector(source.transform.forward);
            var up = reflectionMatrix.MultiplyVector(source.transform.up);
            reflectionCamera.transform.rotation = Quaternion.LookRotation(forward, up);
            reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * reflectionMatrix;
            var clip = CameraSpacePlane(reflectionCamera.worldToCameraMatrix, point, normal, clipOffset);
            reflectionCamera.projectionMatrix = source.CalculateObliqueMatrix(clip);
            reflectionCamera.cullingMatrix = reflectionCamera.projectionMatrix * reflectionCamera.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(reflectionCamera, reflectedFrustum);
            bool previousCulling = GL.invertCulling;
            var previousTarget = RenderTexture.active;
            try
            {
                renderingReflection = true;
                HideSilvering(); ExposeReflectedSurfaces();
                GL.invertCulling = !previousCulling;
                reflectionCamera.Render();
            }
            finally
            {
                GL.invertCulling = previousCulling; RenderTexture.active = previousTarget;
                RestoreSurfaces(); RestoreSilvering(); renderingReflection = false;
            }
            pane.GetPropertyBlock(properties);
            properties.SetTexture(MirrorTexture, reflection);
            properties.SetMatrix(MirrorMatrix, GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true) * reflectionCamera.worldToCameraMatrix);
            properties.SetFloat(MirrorReady, 1);
            pane.SetPropertyBlock(properties);
            lastSource = source; lastSourceView = source.worldToCameraMatrix; lastSourceProjection = source.projectionMatrix;
            lastRenderedFrame = Time.frameCount;
            return true;
        }

        void EnsureRenderTarget(int resolution)
        {
            if (reflection != null && reflection.width == resolution && reflection.IsCreated()) return;
            ReleaseTarget();
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            reflection = new RenderTexture(resolution, resolution, 24, format, RenderTextureReadWrite.Linear)
                { name = "Vertical mirror reflection", antiAliasing = 1, filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            if (!reflection.Create()) { ReleaseTarget(); throw new System.InvalidOperationException("Unable to allocate vertical mirror reflection."); }
        }

        void SetReflectionReady(bool ready)
        {
            if (pane == null || properties == null) return;
            pane.GetPropertyBlock(properties); properties.SetFloat(MirrorReady, ready ? 1 : 0); pane.SetPropertyBlock(properties);
        }
        void HideSilvering()
        {
            hiddenMirrors.Clear();
            foreach (var mirror in mirrors)
            {
                if (mirror == null) continue;
                var renderer = mirror.pane != null ? mirror.pane : mirror.GetComponent<Renderer>();
                if (renderer == null) continue;
                hiddenMirrors.Add(new HiddenMirror { renderer = renderer, enabled = renderer.enabled }); renderer.enabled = false;
            }
        }
        void RestoreSilvering()
        {
            foreach (var item in hiddenMirrors) if (item.renderer != null) item.renderer.enabled = item.enabled;
            hiddenMirrors.Clear();
        }
        void ExposeReflectedSurfaces()
        {
            culledSurfaces.Clear();
            foreach (var renderer in environmentSurfaces)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy || renderer.name.StartsWith("mirror:") ||
                    renderer.enabled && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
                bool sector = false;
                for (var owner = renderer.transform.parent; owner != null; owner = owner.parent)
                    if (owner.name.StartsWith("sector:")) { sector = true; break; }
                if (!sector || !GeometryUtility.TestPlanesAABB(reflectedFrustum, renderer.bounds)) continue;
                culledSurfaces.Add(new CulledSurface { renderer = renderer, enabled = renderer.enabled, shadows = renderer.shadowCastingMode });
                renderer.enabled = true;
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) renderer.shadowCastingMode = ShadowCastingMode.On;
            }
        }
        void RestoreSurfaces()
        {
            foreach (var surface in culledSurfaces)
                if (surface.renderer != null) { surface.renderer.enabled = surface.enabled; surface.renderer.shadowCastingMode = surface.shadows; }
            culledSurfaces.Clear();
        }

        public static Matrix4x4 ReflectionMatrix(Vector3 normal, Vector3 point)
        {
            var n = normal.normalized; float d = -Vector3.Dot(n, point);
            var matrix = Matrix4x4.identity;
            matrix.m00 = 1 - 2 * n.x * n.x; matrix.m01 = -2 * n.x * n.y; matrix.m02 = -2 * n.x * n.z; matrix.m03 = -2 * d * n.x;
            matrix.m10 = -2 * n.y * n.x; matrix.m11 = 1 - 2 * n.y * n.y; matrix.m12 = -2 * n.y * n.z; matrix.m13 = -2 * d * n.y;
            matrix.m20 = -2 * n.z * n.x; matrix.m21 = -2 * n.z * n.y; matrix.m22 = 1 - 2 * n.z * n.z; matrix.m23 = -2 * d * n.z;
            return matrix;
        }
        public static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 point, Vector3 normal, float offset)
        {
            var p = worldToCamera.MultiplyPoint(point + normal.normalized * offset);
            var n = worldToCamera.MultiplyVector(normal).normalized;
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
        }

        void ReleaseTarget()
        {
            if (reflection == null) return;
            if (reflectionCamera != null) reflectionCamera.targetTexture = null;
            reflection.Release(); DestroyOwned(reflection); reflection = null;
        }
        public void ReleaseOwnedResources()
        {
            if (pane != null && mirrorMaterial != null && pane.sharedMaterial == mirrorMaterial)
            {
                pane.sharedMaterial = originalMaterial;
                if (originalProperties != null) pane.SetPropertyBlock(originalProperties);
            }
            ReleaseTarget();
            if (reflectionCamera != null) { DestroyOwned(reflectionCamera.gameObject); reflectionCamera = null; }
            if (mirrorMaterial != null) { DestroyOwned(mirrorMaterial); mirrorMaterial = null; }
            initialized = false; lastRenderedFrame = -1; lastSource = null;
            environmentSurfaces = null; originalProperties = properties = null;
        }
        static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
