using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirWindowBreak
    {
        sealed class Shard
        {
            public Transform view;
            public Vector3 origin, velocity;
            public Quaternion rotation;
        }
        readonly GameObject pane;
        readonly GameObject brokenFrame;
        readonly List<Shard> shards = new List<Shard>();
        bool observed, wasBroken;
        float burstAge = 2;

        public DeadAirWindowBreak(GameObject pane, GameObject brokenFrame = null)
        {
            this.pane = pane;
            this.brokenFrame = brokenFrame;
            if (brokenFrame != null) brokenFrame.SetActive(false);
            if (pane == null) return;
            var source = pane.GetComponentInChildren<MeshRenderer>(true);
            if (source == null)
            {
                Debug.LogError("Director window pane is baked into static surfaces. Rebuild the expanded office before shipping.");
                return;
            }
            // The intact pane stays transparent. Fresh fracture facets catch
            // enough moonlight to read for the brief burst even with the lamp stowed.
            var material = new Material(source.sharedMaterial)
            {
                name = "Director glass / fracture facets",
                hideFlags = HideFlags.DontSave,
                color = new Color(.57f, .72f, .78f, .67f),
                renderQueue = 3005
            };
            if(material.HasProperty("_MainTex"))material.mainTexture=Texture2D.whiteTexture;
            if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor", Color.black);
            material.SetFloat("_Glossiness", .92f);
            if(material.HasProperty("_ReflectionStrength"))material.SetFloat("_ReflectionStrength", .18f);
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic", .03f);
            if(material.HasProperty("_SrcBlend"))material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if(material.HasProperty("_DstBlend"))material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if(material.HasProperty("_ZWrite"))material.SetFloat("_ZWrite", 0);
            var filter = source.GetComponent<MeshFilter>();
            Bounds bounds = filter != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(2.37f, 1.80f, .035f));
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 4; column++)
                    for (int half = 0; half < 2; half++)
                    {
                        float width = bounds.size.x / 4, height = bounds.size.y / 3;
                        var local = bounds.min + new Vector3((column + .5f) * width, (row + .5f) * height, bounds.size.z / 2);
                        var vertices = half == 0 ? new[] {new Vector3(-width/2,-height/2,0),new Vector3(width/2,-height/2,0),new Vector3(-width/2,height/2,0)} :
                            new[] {new Vector3(width/2,height/2,0),new Vector3(-width/2,height/2,0),new Vector3(width/2,-height/2,0)};
                        var mesh = new Mesh { name = "Director window shard",
                            vertices = new[] {vertices[0],vertices[1],vertices[2],vertices[2],vertices[1],vertices[0]},
                            triangles = new[] {0,1,2,3,4,5},
                            uv = new[] {Vector2.zero,Vector2.right,Vector2.up,Vector2.up,Vector2.right,Vector2.zero} };
                        mesh.RecalculateNormals(); mesh.RecalculateBounds();
                        var piece = new GameObject("Director glass shard");
                        piece.transform.SetParent(pane.transform.parent, false);
                        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = piece.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        int index = shards.Count;
                        shards.Add(new Shard {view=piece.transform, origin=source.transform.TransformPoint(local), rotation=source.transform.rotation,
                            velocity=new Vector3(Mathf.Sin(index*2.31f)*1.3f,.35f+Mathf.Abs(Mathf.Sin(index*1.7f))*.75f,-1.1f-Mathf.Abs(Mathf.Cos(index))*.7f)});
                        piece.SetActive(false);
                    }
        }

        public void Apply(bool broken, float deltaTime)
        {
            if (pane == null) return;
            // One root owns both rendered glass and its collider. The frame and
            // sill stay in the facade, and the burst has no blocking colliders.
            pane.SetActive(!broken);
            if (brokenFrame != null) brokenFrame.SetActive(broken);
            if (observed && broken && !wasBroken)
            {
                burstAge = 0; DeadAirAudio.Play("window-shatter");
                foreach (var shard in shards) shard.view.gameObject.SetActive(true);
            }
            if (!broken)
            {
                burstAge = 2;
                foreach (var shard in shards) shard.view.gameObject.SetActive(false);
            }
            observed = true; wasBroken = broken;
            if (burstAge >= 1.8f) return;
            burstAge += Mathf.Max(0, deltaTime);
            for (int i = 0; i < shards.Count; i++)
            {
                var shard = shards[i];
                shard.view.position = shard.origin + shard.velocity * burstAge + Vector3.down * (4.9f * burstAge * burstAge);
                shard.view.rotation = shard.rotation * Quaternion.Euler(burstAge*(85+i*11),burstAge*(110-i*7),burstAge*(50+i*17));
                if (burstAge >= 1.8f) shard.view.gameObject.SetActive(false);
            }
        }
    }
}
