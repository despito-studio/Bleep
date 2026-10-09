using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    /// <summary>A distant sky: celestial features depend on viewing direction, never player position.</summary>
    public sealed class DeadAirSky
    {
        static readonly Vector3 MoonDirection = new Vector3(.35f, .47f, -.81f).normalized;
        readonly Transform domeTransform;

        public DeadAirSky(Transform parent)
        {
            var previous = parent.Find("Dead Air Night Sky & Moon");
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(previous.gameObject); else Object.DestroyImmediate(previous.gameObject);
            }
            var skyRoot = new GameObject("Dead Air Night Sky & Moon");
            skyRoot.transform.SetParent(parent, false);
            var dome = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dome.name = "Distant night sky";
            dome.transform.SetParent(skyRoot.transform, false);
            dome.transform.localScale = Vector3.one * 160;
            domeTransform = dome.transform;
            var collider = dome.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);

            var shader = Resources.Load<Shader>("Shaders/DeadAirNightSky");
            if (shader == null) shader = Shader.Find("Dead Air/Night Sky");
            if (shader != null)
            {
                var material = new Material(shader) { name = "Cold night / distant moon", hideFlags = HideFlags.DontSave };
                // Author the upper sky as linear radiance above the tape tone
                // mapper's black floor, while the exact horizon still meets fog.
                material.SetVector("_FogEdge", Linear(new Color32(16, 22, 33, 255)));
                material.SetVector("_Horizon", new Vector4(.028f, .047f, .085f, 1));
                material.SetVector("_Zenith", new Vector4(.012f, .020f, .045f, 1));
                material.SetVector("_MoonDirection", MoonDirection);
                material.SetFloat("_MoonRadius", .012f);
                var renderer = dome.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.allowOcclusionWhenDynamic = false;
                skyRoot.AddComponent<DeadAirSkyResources>().material = material;
            }
            else dome.SetActive(false);

            var lightObject = new GameObject("Directional moonlight");
            lightObject.transform.SetParent(skyRoot.transform, false);
            lightObject.transform.rotation = Quaternion.LookRotation(-MoonDirection, Vector3.up);
            var moon = lightObject.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color32(159, 179, 212, 255);
            moon.intensity = .19f;
            moon.shadows = LightShadows.Hard;
            moon.shadowStrength = .62f;
            moon.shadowBias = .025f;
            moon.shadowNormalBias = .06f;
            moon.cullingMask = ~((1 << 29) | (1 << 30));
        }

        static Vector4 Linear(Color display)
        {
            var color = QualitySettings.activeColorSpace == ColorSpace.Linear ? display.linear : display;
            return new Vector4(color.r, color.g, color.b, 1);
        }

        public void UpdatePosition(Vector3 cameraPosition)
        {
            if (domeTransform != null) domeTransform.position = cameraPosition;
        }
    }

    public sealed class DeadAirSkyResources : MonoBehaviour
    {
        public Material material;
        void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
