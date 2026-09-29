using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    public sealed class DeadAirSky
    {
        readonly GameObject skyRoot;
        readonly Transform domeTransform;
        readonly Transform starTransform;
        readonly Transform moonTransform;
        readonly Transform haloTransform;
        readonly Light moonLight;

        public DeadAirSky(Transform parent)
        {
            skyRoot = new GameObject("Dead Air Night Sky & Moon");
            skyRoot.transform.SetParent(parent, false);

            var unlitShader = Shader.Find("Dead Air/Source Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Texture");

            // 1. Celestial Sky Dome with horizon fog gradient
            var domeObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            domeObj.name = "SkyDome";
            domeObj.transform.SetParent(skyRoot.transform, false);
            domeObj.transform.localScale = new Vector3(-240f, 240f, 240f);
            var domeCol = domeObj.GetComponent<Collider>();
            if (domeCol != null) UnityEngine.Object.Destroy(domeCol);
            domeTransform = domeObj.transform;

            var skyTex = new Texture2D(16, 256, TextureFormat.RGB24, false);
            skyTex.wrapMode = TextureWrapMode.Clamp;
            skyTex.filterMode = FilterMode.Bilinear;

            var fogCol = new Color(10f / 255f, 16f / 255f, 10f / 255f);     // Ground fog #0a100a
            var midCol = new Color(11f / 255f, 18f / 255f, 28f / 255f);     // Midnight navy
            var zenithCol = new Color(4f / 255f, 7f / 255f, 13f / 255f);    // Deep cosmic blue-black

            for (int y = 0; y < 256; y++)
            {
                float v = y / 255f;
                Color col;
                if (v < 0.30f) col = fogCol;
                else if (v < 0.65f)
                {
                    float t = (v - 0.30f) / 0.35f;
                    col = Color.Lerp(fogCol, midCol, t);
                }
                else
                {
                    float t = (v - 0.65f) / 0.35f;
                    col = Color.Lerp(midCol, zenithCol, t);
                }
                for (int x = 0; x < 16; x++) skyTex.SetPixel(x, y, col);
            }
            skyTex.Apply();

            var skyMat = new Material(unlitShader);
            skyMat.name = "SkyDome_Material";
            skyMat.mainTexture = skyTex;
            skyMat.SetFloat("_FogEnabled", 0f);
            skyMat.SetFloat("_Cull", 0f);
            skyMat.SetFloat("_ZWrite", 0f);
            skyMat.renderQueue = 1000;
            domeObj.GetComponent<MeshRenderer>().sharedMaterial = skyMat;

            // 2. Starfield: sharp, delicate twinkling stars across celestial hemisphere
            var starMesh = new Mesh { name = "StarFieldMesh" };
            var starVerts = new List<Vector3>();
            var starUVs = new List<Vector2>();
            var starIndices = new List<int>();

            var starRng = new System.Random(1337);
            float domeRadius = 110f;
            for (int i = 0; i < 240; i++)
            {
                float azimuth = (float)(starRng.NextDouble() * Math.PI * 2);
                float elevation = (float)(0.20f + starRng.NextDouble() * 1.30f);
                float rHoriz = Mathf.Cos(elevation) * domeRadius;
                float py = Mathf.Sin(elevation) * domeRadius;
                float px = Mathf.Cos(azimuth) * rHoriz;
                float pz = Mathf.Sin(azimuth) * rHoriz;
                var starPos = new Vector3(px, py, pz);

                float sz = 0.55f + (float)starRng.NextDouble() * 0.45f;
                int idx = starVerts.Count;
                var toCenter = -starPos.normalized;
                var right = Vector3.Cross(toCenter, Vector3.up).normalized * sz;
                var up = Vector3.Cross(right, toCenter).normalized * sz;

                starVerts.Add(starPos - right - up);
                starVerts.Add(starPos + right - up);
                starVerts.Add(starPos + right + up);
                starVerts.Add(starPos - right + up);

                starUVs.Add(new Vector2(0, 0));
                starUVs.Add(new Vector2(1, 0));
                starUVs.Add(new Vector2(1, 1));
                starUVs.Add(new Vector2(0, 1));

                starIndices.Add(idx);
                starIndices.Add(idx + 1);
                starIndices.Add(idx + 2);
                starIndices.Add(idx);
                starIndices.Add(idx + 2);
                starIndices.Add(idx + 3);
            }

            starMesh.SetVertices(starVerts);
            starMesh.SetUVs(0, starUVs);
            starMesh.SetTriangles(starIndices, 0);
            starMesh.RecalculateBounds();

            var starTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
            {
                float dy = (y - 15.5f) / 15.5f;
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x - 15.5f) / 15.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d * d);
                    starTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            starTex.Apply();

            var starObj = new GameObject("StarField");
            starObj.transform.SetParent(skyRoot.transform, false);
            starTransform = starObj.transform;
            starObj.AddComponent<MeshFilter>().sharedMesh = starMesh;
            var starMr = starObj.AddComponent<MeshRenderer>();
            var starMat = new Material(unlitShader);
            starMat.name = "StarField_Material";
            starMat.mainTexture = starTex;
            starMat.color = new Color(1.6f, 1.7f, 1.9f, 1f);
            starMat.SetFloat("_FogEnabled", 0f);
            starMat.SetFloat("_Cull", 0f);
            starMat.SetFloat("_ZWrite", 0f);
            starMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            starMat.SetFloat("_DstBlend", (float)BlendMode.One);
            starMat.renderQueue = 1002;
            starMr.sharedMaterial = starMat;

            // 3. 3D Glowing Moon Sphere with spherical lunar procedural noise
            var moonObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moonObj.name = "3D_Moon";
            moonObj.transform.SetParent(skyRoot.transform, false);
            moonObj.transform.position = new Vector3(12f, 32f, -10f);
            moonObj.transform.localScale = new Vector3(7.2f, 7.2f, 7.2f);
            moonObj.transform.rotation = Quaternion.Euler(15f, 120f, 0f);
            var moonCol = moonObj.GetComponent<Collider>();
            if (moonCol != null) UnityEngine.Object.Destroy(moonCol);
            moonTransform = moonObj.transform;

            int mw = 512, mh = 256;
            var moonTex = new Texture2D(mw, mh, TextureFormat.RGB24, false);
            moonTex.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < mh; y++)
            {
                float phi = (y / (float)(mh - 1) - 0.5f) * Mathf.PI;
                float cosPhi = Mathf.Cos(phi);
                float sinPhi = Mathf.Sin(phi);

                for (int x = 0; x < mw; x++)
                {
                    float theta = (x / (float)mw) * Mathf.PI * 2f;
                    float px = cosPhi * Mathf.Cos(theta);
                    float py = sinPhi;
                    float pz = cosPhi * Mathf.Sin(theta);

                    float n1 = Mathf.PerlinNoise(px * 3.5f + 10f, pz * 3.5f + 10f);
                    float n2 = Mathf.PerlinNoise(px * 7f + 25f, py * 7f + 25f) * 0.4f;
                    float n3 = Mathf.PerlinNoise(py * 14f + 50f, pz * 14f + 50f) * 0.2f;
                    float val = Mathf.Clamp01(n1 * 0.65f + n2 + n3);

                    var basalt = new Color(0.55f, 0.58f, 0.62f);
                    var highlands = new Color(0.96f, 0.97f, 1.0f);
                    var col = Color.Lerp(basalt, highlands, val);
                    moonTex.SetPixel(x, y, col);
                }
            }
            moonTex.Apply();

            var moonMat = new Material(unlitShader);
            moonMat.name = "Moon_Material";
            moonMat.mainTexture = moonTex;
            moonMat.SetFloat("_FogEnabled", 0f);
            moonMat.color = new Color(1.25f, 1.30f, 1.40f, 1f);
            moonMat.renderQueue = 1005;
            moonObj.GetComponent<MeshRenderer>().sharedMaterial = moonMat;

            // 4. Soft Atmospheric Lunar Corona / Halo with zero border alpha
            var haloObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            haloObj.name = "Moon_Halo";
            haloObj.transform.SetParent(skyRoot.transform, false);
            haloObj.transform.position = moonObj.transform.position;
            haloObj.transform.localScale = new Vector3(26f, 26f, 1f);
            haloObj.transform.LookAt(new Vector3(5f, 1.65f, 19f));
            var haloCol = haloObj.GetComponent<Collider>();
            if (haloCol != null) UnityEngine.Object.Destroy(haloCol);
            haloTransform = haloObj.transform;

            var haloTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            for (int y = 0; y < 128; y++)
            {
                float dy = (y - 64f) / 64f;
                for (int x = 0; x < 128; x++)
                {
                    float dx = (x - 64f) / 64f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(Mathf.Exp(-dist * 3.8f) * Mathf.Clamp01(1f - dist));
                    haloTex.SetPixel(x, y, new Color(0.55f, 0.68f, 0.85f, alpha * 0.40f));
                }
            }
            haloTex.Apply();

            var haloMat = new Material(unlitShader);
            haloMat.name = "MoonHalo_Material";
            haloMat.mainTexture = haloTex;
            haloMat.SetFloat("_FogEnabled", 0f);
            haloMat.SetFloat("_ZWrite", 0f);
            haloMat.SetFloat("_Cull", 0f);
            haloMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            haloMat.SetFloat("_DstBlend", (float)BlendMode.One);
            haloMat.renderQueue = 1010;
            haloObj.GetComponent<MeshRenderer>().sharedMaterial = haloMat;

            // 5. Directional Moonlight with soft shadows
            var lightObj = new GameObject("Directional Moonlight");
            lightObj.transform.SetParent(skyRoot.transform, false);
            moonLight = lightObj.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.55f, 0.65f, 0.78f);
            moonLight.intensity = 0.32f;
            moonLight.shadows = LightShadows.Hard;
            moonLight.shadowStrength = 0.65f;
            moonLight.shadowBias = 0.05f;
            moonLight.shadowNormalBias = 0.08f;
            lightObj.transform.rotation = Quaternion.Euler(46f, -32f, 0f);

            // Hide old primitive flat circle if present in exterior
            var exterior = parent.Find("exterior");
            if (exterior != null)
            {
                foreach (var t in exterior.GetComponentsInChildren<Transform>(true))
                {
                    if (t.position.y > 20f && t.name == "Mesh") t.gameObject.SetActive(false);
                }
            }
        }

        public void UpdatePosition(Vector3 cameraPosition)
        {
            if (domeTransform != null) domeTransform.position = cameraPosition;
            if (starTransform != null) starTransform.position = cameraPosition;
        }
    }
}
