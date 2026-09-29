using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace DeadAir.Editor
{
    // The source scene is exported in a browser so all procedural canvas textures survive.
    public static class DeadAirSourceImporter
    {
        [Serializable] class Export { public int version, sourceProps; public Geometry[] geometries; public Surface[] materials; public TextureInfo[] textures; public Root[] roots; }
        [Serializable] class Root { public string name; public Node node; }
        [Serializable] class Node { public string name; public float[] p,q,s; public bool visible; public int geometry; public int[] materials; public Node[] children; public Lamp light; }
        [Serializable] class Lamp { public string type,color; public float intensity,range,angle,penumbra; public float[] target; }
        [Serializable] class Geometry { public int id; public float[] positions,normals,uv; public int[] indices; public Group[] groups; }
        [Serializable] class Group { public int start,count,materialIndex; }
        [Serializable] class Surface { public int id,map,emissiveMap; public string color,emissive; public float emission,roughness,metalness,opacity,alphaTest; public bool transparent,doubleSided,unlit,fog,depthWrite,additive; }
        [Serializable] class TextureInfo { public int id; public float[] repeat,offset; public bool wrap,nearest; }
        const string Destination = "Assets/Resources/World";
        static Mesh[] meshes;
        static Material[] materials;
        static Export export;

        [MenuItem("Dead Air/Import Complete Web Scene")]
        public static void Import()
        {
            export = JsonUtility.FromJson<Export>(File.ReadAllText("Assets/SourceExport/scene.json"));
            if (export.version != 2 || export.sourceProps < 300 || export.textures.Length < 30)
                throw new Exception("Incomplete source scene export.");
            Directory.CreateDirectory(Destination + "/Meshes");
            Directory.CreateDirectory(Destination + "/Materials");
            AssetDatabase.Refresh();
            var textures = new Texture2D[export.textures.Length];
            foreach (var t in export.textures)
            {
                string path = "Assets/SourceExport/Textures/texture_" + t.id + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = t.wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.filterMode = t.nearest ? FilterMode.Point : FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                textures[t.id] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            materials = new Material[export.materials.Length];
            foreach (var m in export.materials)
            {
                string path = Destination + "/Materials/surface_" + m.id + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader = Shader.Find(m.unlit ? "Dead Air/Source Unlit" : "Dead Air/Source Surface");
                if (shader == null) throw new Exception("Source material shader missing.");
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat,path); }
                mat.shader = shader;
                var color = Parse(m.color); color.a = m.opacity;
                mat.SetColor("_Color", color);
                mat.SetFloat("_Cull", m.doubleSided ? 0 : 2);
                mat.SetFloat("_Glossiness", 1-m.roughness);
                mat.SetFloat("_Metallic", m.metalness);
                mat.SetColor("_EmissionColor", Parse(m.emissive) * m.emission);
                mat.SetFloat("_FogEnabled",m.fog ? 1 : 0);
                mat.SetFloat("_Cutoff",m.alphaTest);
                if (m.map >= 0)
                {
                    mat.SetTexture("_MainTex", textures[m.map]);
                    var t = export.textures[m.map];
                    mat.SetTextureScale("_MainTex", V2(t.repeat));
                    mat.SetTextureOffset("_MainTex", V2(t.offset));
                }
                if (m.emissiveMap >= 0) mat.SetTexture("_EmissionMap", textures[m.emissiveMap]);
                mat.SetInt("_SrcBlend",(int)(m.transparent ? BlendMode.SrcAlpha : BlendMode.One));
                mat.SetInt("_DstBlend",(int)(m.additive ? BlendMode.One : m.transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                mat.SetInt("_ZWrite",m.transparent && !m.depthWrite ? 0 : 1);
                mat.renderQueue = m.transparent ? 3000 : m.alphaTest > 0 ? 2450 : 2000;
                materials[m.id] = mat;EditorUtility.SetDirty(mat);
            }
            meshes = new Mesh[export.geometries.Length];
            foreach (var g in export.geometries)
            {
                string path = Destination + "/Meshes/geometry_" + g.id + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
                mesh.Clear();mesh.name = "Web geometry " + g.id;
                mesh.indexFormat = g.positions.Length/3 > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = V3Array(g.positions);
                if(g.normals.Length == g.positions.Length) mesh.normals = V3Array(g.normals);
                var uv = new Vector2[g.uv.Length/2];for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(g.uv[i*2],g.uv[i*2+1]);mesh.uv=uv;
                int submeshes = 1;foreach(var group in g.groups)submeshes=Mathf.Max(submeshes,group.materialIndex+1);
                mesh.subMeshCount = submeshes;
                for(int s=0;s<submeshes;s++)
                {
                    var indices = new List<int>();
                    if(g.groups.Length == 0) AddTriangles(indices,g.indices,0,g.indices.Length);
                    else foreach(var group in g.groups)if(group.materialIndex==s)AddTriangles(indices,g.indices,group.start,group.count);
                    mesh.SetTriangles(indices,s);
                }
                if(g.normals.Length==0)mesh.RecalculateNormals();mesh.RecalculateBounds();
                meshes[g.id]=mesh;EditorUtility.SetDirty(mesh);
            }
            foreach(var root in export.roots)
            {
                bool scenery = root.name == "station" || root.name == "exterior";
                var go = Create(root.node,null,scenery);
                go.name = root.name;
                PrefabUtility.SaveAsPrefabAsset(go,Destination+"/"+root.name+".prefab");
                UnityEngine.Object.DestroyImmediate(go);
            }
            PlayerSettings.colorSpace = ColorSpace.Linear;
            QualitySettings.pixelLightCount = 8;
            AssetDatabase.SaveAssets();
            Debug.Log($"Web scene restored: {export.sourceProps} props, {export.geometries.Length} meshes, {materials.Length} materials, {textures.Length} textures, {export.roots.Length} prefabs.");
        }

        static GameObject Create(Node n,Transform parent,bool scenery)
        {
            var go=new GameObject(n.name);go.transform.SetParent(parent,false);
            go.transform.localPosition=V3(n.p);go.transform.localRotation=new Quaternion(n.q[0],-n.q[1],-n.q[2],n.q[3]);go.transform.localScale=new Vector3(n.s[0],n.s[1],n.s[2]);
            if(n.geometry>=0)
            {
                var mesh=meshes[n.geometry];go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();
                var slots=new Material[mesh.subMeshCount];
                for(int i=0;i<slots.Length;i++)slots[i]=materials[n.materials[Mathf.Min(i,n.materials.Length-1)]];
                renderer.sharedMaterials=slots;renderer.shadowCastingMode=ShadowCastingMode.Off;
                if(scenery && !export.materials[n.materials[0]].unlit && !export.materials[n.materials[0]].transparent)
                    go.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            if(n.light!=null && !string.IsNullOrEmpty(n.light.type))
            {
                var l=go.AddComponent<Light>();l.type=n.light.type=="SpotLight"?LightType.Spot:LightType.Point;
                l.color=Parse(n.light.color);l.range=n.light.range>0?n.light.range:12;
                l.intensity=n.light.intensity*.045f;l.shadows=LightShadows.None;l.renderMode=LightRenderMode.ForcePixel;
                if(l.type==LightType.Spot)
                {
                    l.spotAngle=n.light.angle*Mathf.Rad2Deg*2;
                    l.innerSpotAngle=l.spotAngle*(1-n.light.penumbra);
                    if(n.light.target!=null)go.transform.LookAt(V3(n.light.target));
                }
            }
            foreach(var child in n.children)Create(child,go.transform,scenery);
            go.SetActive(n.visible);return go;
        }
        static void AddTriangles(List<int> output,int[] input,int start,int count)
        { for(int i=start;i<start+count;i+=3){output.Add(input[i]);output.Add(input[i+2]);output.Add(input[i+1]);} }
        static Color Parse(string html){ColorUtility.TryParseHtmlString(html,out var color);return color;}
        // Reflect X once at the import boundary; game rules retain source coordinates.
        static Vector3 V3(float[] v)=>new Vector3(-v[0],v[1],v[2]);
        static Vector2 V2(float[] v)=>new Vector2(v[0],v[1]);
        static Vector3[] V3Array(float[] v){var a=new Vector3[v.Length/3];for(int i=0;i<a.Length;i++)a[i]=new Vector3(-v[i*3],v[i*3+1],v[i*3+2]);return a;}
    }
}
