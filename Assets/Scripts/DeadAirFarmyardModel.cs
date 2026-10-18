using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // Animation vertices baked from the bundled CC0 skeletal GLBs. Only two tiny meshes are
    // sampled; no importer package, Animator controller, runtime file IO or network is needed.
    public sealed class DeadAirFarmyardModel : MonoBehaviour
    {
        sealed class Clip
        {
            public string name;
            public float fps;
            public Vector3[][] positions, normals;
        }
        readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
        Mesh mesh;
        Material material;
        string sampledClip;
        int sampledFrame = -1;

        public static DeadAirFarmyardModel Load(string resource, Transform parent, string name)
        {
            var actor = new GameObject(name); actor.transform.SetParent(parent, false);
            var presentation = actor.AddComponent<DeadAirFarmyardModel>();
            var asset = Resources.Load<TextAsset>("Farmyard/" + resource);
            var texture = Resources.Load<Texture2D>("Farmyard/" + resource);
            if (asset == null || texture == null)
            {
                Debug.LogError("Farmyard animal asset missing: " + resource);
                return presentation;
            }
            presentation.Read(asset.bytes, texture, resource);
            return presentation;
        }

        void Read(byte[] bytes, Texture2D texture, string resource)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                if (reader.ReadInt32() != 0x44414631) throw new InvalidDataException("Invalid farmyard mesh header.");
                int vertexCount = reader.ReadInt32(), indexCount = reader.ReadInt32();
                if (vertexCount <= 0 || vertexCount > 32000 || indexCount <= 0 || indexCount > 100000)
                    throw new InvalidDataException("Invalid farmyard mesh dimensions.");
                var triangles = new int[indexCount]; for (int i = 0; i < indexCount; i++) triangles[i] = reader.ReadInt32();
                var uv = new Vector2[vertexCount]; for (int i = 0; i < vertexCount; i++) uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                int clipCount = reader.ReadInt32();
                if (clipCount < 1 || clipCount > 8) throw new InvalidDataException("Invalid farmyard animation count.");
                for (int c = 0; c < clipCount; c++)
                {
                    int nameBytes = reader.ReadInt32(); if (nameBytes < 1 || nameBytes > 40) throw new InvalidDataException("Invalid clip name.");
                    var clip = new Clip { name = Encoding.UTF8.GetString(reader.ReadBytes(nameBytes)) };
                    int frames = reader.ReadInt32(); clip.fps = reader.ReadSingle();
                    if (frames < 1 || frames > 200 || clip.fps <= 0 || clip.fps > 120) throw new InvalidDataException("Invalid clip duration.");
                    clip.positions = new Vector3[frames][]; clip.normals = new Vector3[frames][];
                    for (int f = 0; f < frames; f++) { clip.positions[f] = Vectors(reader, vertexCount); clip.normals[f] = Vectors(reader, vertexCount); }
                    clips.Add(clip.name, clip);
                }
                mesh = new Mesh { name = "CC0 " + resource + " / sampled skeletal mesh" };
                mesh.MarkDynamic(); mesh.vertices = clips["idle"].positions[0]; mesh.normals = clips["idle"].normals[0];
                mesh.uv = uv; mesh.triangles = triangles;
                // Include every authored pose once; avoid per-frame bounds scans and clipping horns/wings.
                var bounds = new Bounds(Vector3.zero, Vector3.one * .01f);
                foreach (var clip in clips.Values) foreach (var frame in clip.positions) foreach (var p in frame) bounds.Encapsulate(p);
                bounds.Expand(.12f); mesh.bounds = bounds;
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                material = new Material(Shader.Find("Standard")) { name = "CC0 " + resource + " / lit atlas", color = new Color(.87f, .87f, .87f) };
                material.mainTexture = texture; material.SetFloat("_Glossiness", .13f);
                var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                renderer.allowOcclusionWhenDynamic = false;
            }
        }

        static Vector3[] Vectors(BinaryReader reader, int count)
        {
            var result = new Vector3[count];
            for (int i = 0; i < count; i++) result[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return result;
        }

        public void Sample(string name, float clock)
        {
            if (mesh == null || !clips.TryGetValue(name, out var clip)) return;
            // Quantising to 24fps preserves the authored motion with negligible mesh upload cost.
            int f = Mathf.FloorToInt(Mathf.Repeat(clock * clip.fps, clip.positions.Length));
            if (sampledClip == name && sampledFrame == f) return;
            sampledClip = name; sampledFrame = f;
            mesh.vertices = clip.positions[f]; mesh.normals = clip.normals[f];
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh); if (material != null) Destroy(material);
        }
    }
}
