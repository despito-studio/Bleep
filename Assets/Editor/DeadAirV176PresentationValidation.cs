using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirV176PresentationValidation
    {
        [MenuItem("Dead Air/Validate v1.7.6 Physical Presentation Assets")]
        public static void Validate()
        {
            Require(PlayerSettings.colorSpace == ColorSpace.Linear, "Physical light and material response requires linear scene rendering.");
            var floor = Material("floor-tile");
            var plaster = Material("plaster");
            var steel = Material("steel");
            var wood = Material("wood");
            var carpet = Material("office-carpet");
            foreach (var surface in new[] { floor,plaster,steel,wood,carpet,Material("brick"),Material("door-wood") })
            {
                Require(surface.shader.name == "Dead Air/Source Surface", "Actual office surfaces need the physical surface shader: " + surface.name);
                Require(surface.GetColor("_EmissionColor").maxColorComponent < .001f,
                    "Nonluminous surfaces must respond to scene lights rather than emit: " + surface.name);
                Require(!surface.HasProperty("_SurfaceGrain") && !surface.HasProperty("_LightBanding"),
                    "Obsolete procedural noise/light-band controls must not remain on physical surfaces: " + surface.name);
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(surface.mainTexture)) as TextureImporter;
                Require(importer != null && importer.filterMode != FilterMode.Point && importer.mipmapEnabled && importer.anisoLevel >= 2,
                    "Saved material textures must filter smoothly at oblique floor/wall distances: " + surface.name);
            }
            Require(floor.GetFloat("_Glossiness") > .6f && floor.GetFloat("_Glossiness") > plaster.GetFloat("_Glossiness") + .3f &&
                floor.GetFloat("_Glossiness") > carpet.GetFloat("_Glossiness") + .3f && floor.GetFloat("_PlanarReflection") > .5f,
                "Polished tiles must differ materially from matte plaster and carpet and opt into actual scene reflections.");
            Require(steel.GetFloat("_Metallic") > .4f && wood.GetFloat("_Metallic") < .05f && floor.GetFloat("_Metallic") < .05f,
                "Steel must respond as a metal while oak and ceramic remain dielectric surfaces.");
            CheckMap(floor,"_BumpMap",TextureImporterType.NormalMap);
            CheckMap(floor,"_SurfaceMap",TextureImporterType.Default);
            Require(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Shaders/DeadAirAmbientOcclusion.shader") != null &&
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Shaders/DeadAirBloom.shader") != null,
                "Depth-based ambient occlusion and HDR bloom shaders must be available to the saved build.");
            var station = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            Require(station != null, "The saved furnished station is missing.");
            var floorMeshes = station.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterials.Contains(floor))
                .Select(r => r.GetComponent<MeshFilter>()?.sharedMesh).Distinct().ToArray();
            Require(floorMeshes.Length > 0 && floorMeshes.All(m => m != null),
                "Inspect the saved geometry which actually renders the normal-mapped polished floor.");
            foreach (var mesh in floorMeshes)
            {
                Require(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent),
                    "Saved polished-floor geometry needs a real tangent basis for normal mapping: " + mesh.name);
                var tangents = mesh.tangents;
                Require(tangents.Length == mesh.vertexCount,
                    "The saved floor tangent channel must cover every vertex: " + mesh.name);
                foreach (var tangent in tangents)
                    Require(Finite(tangent.x) && Finite(tangent.y) && Finite(tangent.z) && Finite(tangent.w) &&
                        new Vector3(tangent.x,tangent.y,tangent.z).sqrMagnitude > .25f && Mathf.Abs(tangent.w) > .5f && Mathf.Abs(tangent.w) <= 1.001f,
                        "The physical floor tangent basis must be finite, nonzero and have a valid handedness: " + mesh.name);
            }
            foreach (var filter in station.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.sharedMaterials.Contains(floor)) continue;
                Require(filter.sharedMesh != null && filter.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent),
                    "Normal-mapped saved floor meshes require an actual tangent basis: " + filter.name);
            }
            var lamps = station.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("lamp:zone-")).ToArray();
            var bounce = station.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("lamp:bounce-")).ToArray();
            Require(lamps.Length >= 30 && bounce.Length >= lamps.Length/2,
                "Furnished rooms and passages need primary practical lamps and local diffuse fill.");
            Require(lamps.All(l => l.type == LightType.Point && l.shadows == LightShadows.Soft && l.range >= 8 && l.intensity > .3f),
                "Practical lamps must spread light softly throughout their physical room rather than draw hard cone scallops.");
            Require(bounce.All(l => l.type == LightType.Point && l.shadows == LightShadows.None && l.range >= 4),
                "Local diffuse fill must use bounded, shadow-free light pools.");
            Debug.Log("DEAD AIR V1.7.6 PRESENTATION ASSETS PASSED: linear physical surfaces, polished normal/finish-mapped tiles, dielectric oak/ceramic and metallic steel, smooth mipmapped textures, " + lamps.Length + " soft practicals and " + bounce.Length + " local fills. Actual image effects and reflected scene pixels require the GPU runtime capture.");
        }

        static Material Material(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Office/Materials/" + name + ".mat");
            Require(material != null, "Saved material missing: " + name);
            return material;
        }

        static void CheckMap(Material surface,string property,TextureImporterType type)
        {
            var texture = surface.GetTexture(property);
            Require(texture != null, "Saved polished tile detail map missing: " + property);
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            Require(importer != null && importer.textureType == type && !importer.sRGBTexture && importer.mipmapEnabled && importer.filterMode != FilterMode.Point,
                "Surface detail maps must have linear, filtered, correctly typed import settings: " + property);
        }

        static void Require(bool condition,string message)
        { if (!condition) throw new InvalidOperationException("v1.7.6 presentation asset validation: " + message); }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
