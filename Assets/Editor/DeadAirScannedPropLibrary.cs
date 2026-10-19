using UnityEngine;
using UnityEditor;

namespace DeadAir.Editor
{
    public static class DeadAirScannedPropLibrary
    {
        public static bool TryBuild(Transform root, PropData prop)
        {
            if (prop.type == "wallMap") return BuildMap(root, prop);
            string key;
            float height;
            Vector3 position = Vector3.zero;
            switch (prop.type)
            {
                case "chair": key = "school-chair"; height = .92f; break;
                case "bench": key = "wooden-bench"; height = .49f; break;
                case "sofa": key = "sofa"; height = 1.08f; break;
                case "officePlant": key = "potted-plant"; height = 1.3f; break;
                case "paperCart": case "cleaningCart": key = "storage-cart"; height = .94f; break;
                case "crate": key = "wooden-crate"; height = Mathf.Max(.5f, prop.w); break;
                case "trashBin": key = "trash-can"; height = .61f; break;
                case "deskLamp": key = "desk-lamp"; height = .42f; break;
                case "wallClock": key = "wall-clock"; height = .4f; position.y = 2.35f; break;
                default: return false;
            }
            float width = Mathf.Max(.3f, prop.w), depth = Mathf.Max(.3f, prop.d);
            if (prop.type == "wallClock") { width = .4f; depth = .1f; }
            if (prop.type == "deskLamp") { width = .2f; depth = .22f; }
            return DeadAirScannedModels.TryInstall(root, key, position, Quaternion.identity,
                new Vector3(width, height, depth)) != null;
        }

        static bool BuildMap(Transform root, PropData prop)
        {
            const string texturePath = "Assets/ArtProduction/Graphics/broadcast-network-1988.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) return false;
            float width = Mathf.Max(1.5f, prop.w) - .12f, height = width / 1.5f;
            var frame = DeadAirArtMaterials.Create("Transmission map oak frame", "wood", new Color(.78f, .69f, .56f), .2f);
            DeadAirFurnitureMeshes.Install(root, "Separate oak map frame", DeadAirFurnitureMeshes.RoundedBox(
                new Vector3(width + .12f, height + .12f, .09f), .018f, .005f), new Vector3(0, 1.94f, 0), frame);
            const string materialPath = "Assets/ArtProduction/Graphics/broadcast-network-1988.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Dead Air/Source Surface"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.mainTexture = texture; material.color = Color.white;
            material.SetFloat("_Glossiness", .08f); material.SetFloat("_Cull", 0);
            EditorUtility.SetDirty(material);
            string meshPath = "Assets/ArtProduction/Meshes/map-face-" + Mathf.RoundToInt(width * 1000) + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Broadcast map paper face" };
                mesh.vertices = new[] { new Vector3(-width / 2, -height / 2, 0), new Vector3(width / 2, -height / 2, 0),
                    new Vector3(width / 2, height / 2, 0), new Vector3(-width / 2, height / 2, 0) };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            DeadAirFurnitureMeshes.Install(root, "1988 district transmission survey", mesh, new Vector3(0, 1.94f, .0575f), material);
            return true;
        }
    }
}
