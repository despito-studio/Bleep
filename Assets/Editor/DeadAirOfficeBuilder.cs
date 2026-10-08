using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Builds saved, editable office prefabs from the same data used by authoritative game rules.</summary>
    public static class DeadAirOfficeBuilder
    {
        const string Folder = "Assets/Office";
        const string Library = Folder + "/Library";
        const float Storey = DeadAirOfficeLayout.FloorHeight;
        const float Ceiling = 3.92f;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Transform> sectors = new Dictionary<string, Transform>();
        static WorldData world;
        static GameObject station, exterior;
        static int meshSerial;
        static Font signFont;
        static Material plaster, brick, trim, floor, carpet, ceilingPanel, metal, wood, doorWood, asphalt, grass, glass, lightSurface, paper, black, signLetters, cobwebSilk, wastePaper, doorSteelWhite, signPlateWhite, blood;

        [MenuItem("Dead Air/Refresh Volumetric Cobwebs")]
        public static void RefreshCobwebGeometry()
        {
            meshes.Remove("cobweb-strands");meshes.Remove("cobweb-locker-strands");
            var wallMesh=CobwebMesh(false);var lockerMesh=CobwebMesh(true);
            var silk=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/cobweb-silk.mat");
            if(silk==null)throw new InvalidOperationException("Saved cobweb silk material is missing.");
            silk.mainTexture=null;silk.color=new Color(.39f,.42f,.37f);silk.SetFloat("_Glossiness",.10f);
            EditorUtility.SetDirty(silk);
            int count=0;
            foreach(string path in new[]{"Assets/Resources/World/station.prefab","Assets/Resources/World/exterior.prefab"})
            {
                var contents=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach(var oldBatch in contents.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Batched surfaces / cobweb-silk").ToArray())
                        UnityEngine.Object.DestroyImmediate(oldBatch.gameObject);
                    foreach(var web in contents.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Cobweb:",StringComparison.Ordinal)).ToArray())
                    {
                        var strands=web.Find("Web strands");if(strands==null)strands=Group(web,"Web strands");strands.localScale=Vector3.one;
                        var filter=strands.GetComponent<MeshFilter>();if(filter==null)filter=strands.gameObject.AddComponent<MeshFilter>();
                        filter.sharedMesh=web.name.StartsWith("Cobweb:locker:",StringComparison.Ordinal)?lockerMesh:wallMesh;
                        var renderer=strands.GetComponent<MeshRenderer>();if(renderer==null)renderer=strands.gameObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial=silk;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
                        count++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(contents);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Refreshed "+count+" cobwebs as curved closed silk tubes without textures.");
        }

        [MenuItem("Dead Air/Build Expanded Office")]
        public static void Build()
        {
            world = WorldData.Load();
            if (world.rooms == null || world.rooms.Length < 18 || world.stairs == null || world.stairs.Length == 0)
                throw new InvalidOperationException("Expanded office data is missing. Run node scripts/build-office-data.cjs first.");
            foreach (string folder in new[] { Folder, Library, Folder + "/Materials", Folder + "/Textures", Folder + "/Meshes" })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CacheFurniture();
            PrepareMaterials();
            signFont = Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
            materials.Clear(); meshes.Clear(); sectors.Clear(); meshSerial = 0;
            RepairFuelCanisterTemplate();
            station = new GameObject("station");
            exterior = new GameObject("exterior");
            station.AddComponent<DeadAirOfficeLayout>();
            exterior.AddComponent<DeadAirOfficeLayout>();
            try
            {
                BuildShell();
                BuildCourtyards();
                foreach (var room in world.rooms)
                {
                    var parent = room.floorY < -1 ? station.transform : (room.minZ >= 6 || room.maxZ <= -54 || room.maxX <= -18 || room.minX >= 18 ? exterior.transform : station.transform);
                    sectors[SectorKey(room)] = Group(parent, "sector:" + room.name);
                }
                foreach (var prop in world.props) BuildProp(prop);
                foreach (var door in world.doors) BuildDoor(door);
                foreach (var locker in world.lockers) BuildLocker(locker);
                foreach (var objective in world.objects) BuildObjective(objective);
                BuildOfficeDressing();
                BuildLighting();
                BatchStaticGeometry(station.transform.Find("shell"), "station-shell");
                BatchStaticGeometry(exterior.transform.Find("shell"), "exterior-shell");
                foreach (var entry in sectors) BatchStaticGeometry(entry.Value, "room-" + Array.IndexOf(world.rooms,world.rooms.First(r=>SectorKey(r)==entry.Key)));
                PrefabUtility.SaveAsPrefabAsset(station, "Assets/Resources/World/station.prefab");
                PrefabUtility.SaveAsPrefabAsset(exterior, "Assets/Resources/World/exterior.prefab");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(station);
                UnityEngine.Object.DestroyImmediate(exterior);
            }
            Validate();
            Debug.Log("Branched Blackpine office saved: two floors, " + world.rooms.Count(r=>DeadAirOfficeLayout.IsInterior(r)&&!DeadAirOfficeLayout.IsCirculation(r)) + " rooms, cross halls and side galleries, " + world.doors.Length + " animated doors, eight vented lockers.");
        }

        // Map Builder can edit the saved records and regenerate their geometry without resetting any placement.
        public static void RebuildFromSavedData() => Build();

        [MenuItem("Dead Air/Refresh Generator Appearance")]
        public static void RefreshGeneratorAppearance()
        {
            const string prefabPath="Assets/Resources/World/exterior.prefab";
            var data=WorldData.Load();
            var shelter=data.props.FirstOrDefault(p=>p.type=="generatorShelter");
            if(shelter==null)throw new InvalidOperationException("Generator shelter is missing from saved world data.");
            var content=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var shelterRoot=content.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="prop:"+shelter.id);
                if(shelterRoot==null)throw new InvalidOperationException("Persisted generator shelter is missing. Rebuild the expanded office once.");
                ApplyGeneratorMaterials(content);
                GeneratorServiceLamp(shelterRoot);
                PrefabUtility.SaveAsPrefabAsset(content,prefabPath);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(content); }
            Debug.Log("Generator appearance refreshed: isolated engine/radiator material copies and a warm local service lamp; saved exterior geometry preserved.");
        }

        static Material GeneratorMaterial(int sourceId)
        {
            string sourcePath="Assets/Resources/World/Materials/surface_"+sourceId+".mat";
            string path=Folder+"/Materials/generator-surface_"+sourceId+".mat";
            var source=AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if(source==null)throw new InvalidOperationException("Source generator material is missing: "+sourcePath);
            var copy=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(copy==null)
            {
                copy=new Material(source);copy.name="Generator surface "+sourceId;
                AssetDatabase.CreateAsset(copy,path);
            }
            else { copy.shader=source.shader;copy.CopyPropertiesFromMaterial(source); }
            // The original dark tint multiplied an already shaded pixel texture. Keep the texture
            // and roughness, while making the real casing readable beneath its service shelter.
            copy.SetColor("_Color",new Color(.78f,.82f,.74f,1));
            EditorUtility.SetDirty(copy);
            return copy;
        }

        static void ApplyGeneratorMaterials(GameObject content)
        {
            foreach(int id in new[]{285,286})
            {
                var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/World/Materials/surface_"+id+".mat");
                var copy=GeneratorMaterial(id);
                foreach(var renderer in content.GetComponentsInChildren<Renderer>(true))
                {
                    var slots=renderer.sharedMaterials;
                    bool changed=false;
                    for(int i=0;i<slots.Length;i++)if(slots[i]==source){slots[i]=copy;changed=true;}
                    if(changed)renderer.sharedMaterials=slots;
                }
            }
        }

        static void GeneratorServiceLamp(Transform shelter)
        {
            const string name="lamp:generator-service";
            var lamp=shelter.Find(name);
            if(lamp==null)lamp=Group(shelter,name);
            lamp.localPosition=new Vector3(0,3.15f,1.4f);lamp.localRotation=Quaternion.Euler(90,0,0);
            var light=lamp.GetComponent<Light>();if(light==null)light=lamp.gameObject.AddComponent<Light>();
            light.type=LightType.Spot;light.color=new Color(.95f,.82f,.63f);light.range=5.5f;
            light.spotAngle=115;light.innerSpotAngle=80;light.intensity=2.1f;light.renderMode=LightRenderMode.ForcePixel;
            light.shadows=LightShadows.Hard;light.shadowBias=.02f;light.shadowNormalBias=.10f;
            if(shelter.Find("Generator service lamp housing")==null)
            {
                var housingMaterial=metal??AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/steel.mat");
                var diffuserMaterial=lightSurface??AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/light-panel.mat");
                Block(shelter,"Generator service lamp housing",new Vector3(0,3.35f,1.4f),new Vector3(.6f,.10f,.34f),housingMaterial,false);
                Block(shelter,"Generator service lamp diffuser",new Vector3(0,3.285f,1.4f),new Vector3(.50f,.03f,.26f),diffuserMaterial,false);
            }
            const string floodName="lamp:generator-front-service";
            var flood=shelter.Find(floodName);
            if(flood==null)
            {
                var housingMaterial=metal??AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/steel.mat");
                var diffuserMaterial=lightSurface??AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/light-panel.mat");
                // A real front-beam work light illuminates the vertical controls/casing.
                // The overhead light remains useful on the cover and the service floor.
                Block(shelter,"Shelter front mounting beam",new Vector3(0,3.25f,3.3f),new Vector3(7.6f,.14f,.14f),housingMaterial,false);
                Block(shelter,"Generator flood mounting bracket",new Vector3(0,3.22f,3.22f),new Vector3(.12f,.23f,.23f),housingMaterial,false);
                flood=Group(shelter,floodName);
                flood.localPosition=new Vector3(0,3.015f,3.075f);
                flood.localRotation=Quaternion.LookRotation(new Vector3(0,-1.85f,-2.28f));
                Block(flood,"Service flood housing",new Vector3(0,0,-.14f),new Vector3(.55f,.32f,.20f),housingMaterial,false);
                Block(flood,"Service flood diffuser",new Vector3(0,0,-.026f),new Vector3(.45f,.24f,.02f),diffuserMaterial,false);
            }
            var fill=flood.GetComponent<Light>();if(fill==null)fill=flood.gameObject.AddComponent<Light>();
            fill.type=LightType.Spot;fill.color=new Color(.99f,.91f,.73f);fill.range=7;
            fill.spotAngle=90;fill.innerSpotAngle=62;fill.intensity=3.2f;fill.renderMode=LightRenderMode.ForcePixel;
            fill.shadows=LightShadows.Hard;fill.shadowBias=.02f;fill.shadowNormalBias=.10f;
        }

        static void CacheFurniture()
        {
            templates.Clear();
            var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var originalExterior = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            var source = new Dictionary<string, Vector3>
            {
                {"archivistDesk", new Vector3(-8,0,-3)}, {"table",new Vector3(-8,0,-26.5f)},
                {"chair",new Vector3(-8,0,-1.92f)}, {"shelf",new Vector3(-6.75f,0,-8.5f)},
                {"cardCatalog",new Vector3(-11,0,-8.5f)}, {"serverRack",new Vector3(6.5f,0,-34.5f)},
                {"mixingConsole",new Vector3(11,0,-1.75f)}, {"anchorDesk",new Vector3(6.25f,0,-20)},
                {"camera",new Vector3(4,0,-21)}, {"micStand",new Vector3(4.25f,0,-19.75f)},
                {"dieselGenerator",new Vector3(-7.25f,0,-21.75f)}, {"fuelCanister",new Vector3(-10.5f,0,-12.5f)},
                {"fusePanel",new Vector3(-11.75f,0,-16.5f)}, {"airDuct",new Vector3(-7.25f,0,-33.25f)},
                {"monitorWall",new Vector3(8.5f,0,-53.5f)}, {"crt",new Vector3(7.75f,1.3f,-39.75f)},
                {"cableSpool",new Vector3(11.5f,0,3.75f)}, {"trashBin",new Vector3(2.75f,0,0)},
                {"abandonedCar",new Vector3(12.25f,0,24.75f)}
            };
            foreach (var entry in source)
            {
                string path = Library + "/" + entry.Key + ".prefab";
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (saved == null && original != null && original.GetComponent<DeadAirOfficeLayout>() == null)
                {
                    var unityPosition = DeadAirOfficeLayout.Position(entry.Value.x, entry.Value.y, entry.Value.z);
                    var match = original.transform.Cast<Transform>().FirstOrDefault(t =>
                        Vector3.Distance(t.localPosition, unityPosition) < .04f && t.GetComponentsInChildren<Renderer>(true).Length > 0 &&
                        !t.name.StartsWith("interactive:") && !t.name.StartsWith("door:"));
                    if (match != null)
                    {
                        var copy = UnityEngine.Object.Instantiate(match.gameObject);
                        copy.name = entry.Key;
                        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        StripDynamicNames(copy);
                        PrefabUtility.SaveAsPrefabAsset(copy, path);
                        UnityEngine.Object.DestroyImmediate(copy);
                        saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    }
                }
                if (saved != null) templates[entry.Key] = saved;
            }
            string vanPath = Library + "/van.prefab";
            var van = AssetDatabase.LoadAssetAtPath<GameObject>(vanPath);
            if (van == null && originalExterior != null)
            {
                var match = originalExterior.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Broadcast service van"));
                if (match != null)
                {
                    var copy = UnityEngine.Object.Instantiate(match.gameObject);
                    copy.name = "van"; copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    StripDynamicNames(copy);
                    PrefabUtility.SaveAsPrefabAsset(copy, vanPath); UnityEngine.Object.DestroyImmediate(copy);
                    van = AssetDatabase.LoadAssetAtPath<GameObject>(vanPath);
                }
            }
            if (van != null) templates["van"] = van;
            AssetDatabase.SaveAssets();
        }

        static void StripDynamicNames(GameObject copy)
        {
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains(":")) t.name = "detail " + t.name.Replace(":", " ");
            foreach (var light in copy.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
        }

        static void RepairFuelCanisterTemplate()
        {
            // The web exporter batched the metal body outside its label group.
            // A cached prefab may therefore exist while containing only that plane.
            if (templates.TryGetValue("fuelCanister", out var saved) && HasFuelCanisterBody(saved.transform, out _)) return;
            var replacement = new GameObject("fuelCanister");
            try
            {
                BuildFuelCanister(replacement.transform);
                if (!HasFuelCanisterBody(replacement.transform, out _)) throw new InvalidOperationException("Generated diesel canister has no solid body.");
                string path = Library + "/fuelCanister.prefab";
                PrefabUtility.SaveAsPrefabAsset(replacement, path);
                templates["fuelCanister"] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.Log("Rebuilt incomplete fuel-canister template with a metal body, cap, carry handle and diesel labels.");
            }
            finally { UnityEngine.Object.DestroyImmediate(replacement); }
        }

        static void BuildFuelCanister(Transform parent)
        {
            Block(parent, "Fuel canister body", new Vector3(0, .26f, 0), new Vector3(.42f, .52f, .30f), trim, false);
            Block(parent, "Canister shoulder", new Vector3(0, .5575f, 0), new Vector3(.38f, .075f, .29f), trim, false);
            foreach (float x in new[] { -.115f, .115f })
                Block(parent, "Carry handle support", new Vector3(x, .645f, 0), new Vector3(.04f, .10f, .075f), metal, false);
            Block(parent, "Carry handle", new Vector3(0, .7125f, 0), new Vector3(.27f, .035f, .075f), metal, false);
            Block(parent, "Diesel screw cap", new Vector3(.145f, .6175f, -.075f), new Vector3(.08f, .045f, .08f), metal, false);
            foreach (float face in new[] { -1f, 1f })
            {
                Block(parent, "Pressed metal rib", new Vector3(0, .10f, face * .155f), new Vector3(.30f, .025f, .012f), metal, false);
                Block(parent, "Pressed metal rib", new Vector3(0, .455f, face * .155f), new Vector3(.30f, .025f, .012f), metal, false);
                Sign(parent, "DIESEL\nBUREAU 9", new Vector3(0, .28f, face * .166f), Quaternion.Euler(0, face < 0 ? 180 : 0, 0), .27f, .14f);
            }
        }

        static bool HasFuelCanisterBody(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool initialized = false, solidMesh = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Vector3 size = mesh.bounds.size;
                solidMesh |= size.x > .05f && size.y > .05f && size.z > .05f;
                Matrix4x4 local = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 center = mesh.bounds.center, extent = mesh.bounds.extents;
                foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
                {
                    Vector3 point = local.MultiplyPoint3x4(center + Vector3.Scale(extent, new Vector3(x, y, z)));
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return initialized && solidMesh && bounds.size.x >= .30f && bounds.size.y >= .60f && bounds.size.z >= .20f;
        }

        static void PrepareMaterials()
        {
            plaster = Surface("plaster", new Color(.57f,.59f,.52f), 0);
            brick = Surface("brick",new Color(.32f,.35f,.29f),1);
            trim = Surface("painted-trim",new Color(.22f,.29f,.27f),2);
            floor = Surface("floor-tile",new Color(.43f,.46f,.39f),3);
            carpet = Surface("office-carpet",new Color(.24f,.28f,.26f),4);
            ceilingPanel = Surface("acoustic-ceiling",new Color(.60f,.63f,.56f),6);
            metal = Surface("steel",new Color(.3f,.34f,.32f),2);
            wood = Surface("wood",new Color(.31f,.23f,.15f),5);
            doorWood = Surface("door-wood",new Color(.26f,.18f,.12f),10);
            asphalt = Surface("asphalt",new Color(.19f,.22f,.19f),4);
            grass = Surface("grass",new Color(.15f,.23f,.14f),4);
            glass = Surface("dark-window",new Color(.22f,.35f,.38f),2,null,0.28f);
            lightSurface = Surface("light-panel",new Color(.20f,.22f,.18f),0, Color.black);
            paper = Surface("sign-board",new Color(.8f,.78f,.62f),0);
            black = Surface("dark-detail",new Color(.07f,.09f,.08f),0);
            cobwebSilk = Surface("cobweb-silk",new Color(.28f,.30f,.26f),0);
            cobwebSilk.mainTexture=null;cobwebSilk.color=new Color(.39f,.42f,.37f);
            cobwebSilk.SetFloat("_Glossiness",.10f);
            wastePaper = Surface("waste-paper",new Color(.57f,.54f,.43f),0);
            doorSteelWhite = Surface("door-frame-steel", new Color(.86f, .88f, .90f), 2);
            signPlateWhite = Surface("sign-plate-white", new Color(.94f, .95f, .94f), 0);
            blood = Surface("blood-pool", new Color(.30f, .03f, .03f), 0);
            blood.SetFloat("_Glossiness", .85f);
            string signPath=Folder+"/Materials/sign-font.mat";
            signLetters=AssetDatabase.LoadAssetAtPath<Material>(signPath);
            var signShader=Shader.Find("Dead Air/World Sign");
            if(signShader==null)throw new InvalidOperationException("Depth-tested world sign shader is missing.");
            if(signLetters==null){signLetters=new Material(signShader);AssetDatabase.CreateAsset(signLetters,signPath);}
            signLetters.shader=signShader;signLetters.color=Color.white;signLetters.mainTexture=null;
            EditorUtility.SetDirty(signLetters);
        }

        static Material Surface(string name, Color color, int pattern, Color? emission = null, float alpha = 1f)
        {
            string texturePath = Folder + "/Textures/" + name + ".png";
            int resolution = pattern == 10 ? 256 : 64;
            var texture = new Texture2D(resolution,resolution,TextureFormat.RGBA32,false);
            var colors = new Color[resolution * resolution];
            for(int z=0;z<resolution;z++) for(int x=0;x<resolution;x++)
            {
                uint hash = (uint)((x*374761393) ^ (z*668265263) ^ 1274126177);
                hash = (hash ^ (hash >> 13)) * 1274126177;
                float noise = ((hash & 255) /255f -.5f)*.055f;
                float shade = 1 + noise;
                if(pattern==1 && (z%12==0 || (x+(z/12%2)*16)%32==0)) shade=.69f;
                if(pattern==2 && z%16==0) shade=.87f;
                if(pattern==3 && (x%16==0 || z%16==0)) shade=.73f;
                if(pattern==5) shade+= Mathf.Sin(z*.42f+x*.035f)*.11f;
                if(pattern==6)
                {
                    if(x%20==0||z%20==0)shade=.66f;
                    else if(x%3==1&&z%3==1)shade=.90f;
                }
                if(pattern==10)
                {
                    float grain = Mathf.Sin(x * .65f + Mathf.Sin(z * .04f) * 2.5f) * .075f;
                    float ring = Mathf.Sin(x * 1.8f + z * .08f) * .035f;
                    float fine = ((hash & 255) / 255f - .5f) * .025f;
                    shade = 1f + grain + ring + fine;
                }
                colors[z*resolution+x] = color*shade; colors[z*resolution+x].a=alpha;
            }
            texture.SetPixels(colors); texture.Apply(); File.WriteAllBytes(texturePath,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.filterMode=FilterMode.Point; importer.wrapMode=TextureWrapMode.Repeat;
            importer.mipmapEnabled=true; importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None; importer.anisoLevel=0; importer.SaveAndReimport();
            string materialPath = Folder + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader = Shader.Find("Dead Air/Source Surface");
            if(shader==null) throw new InvalidOperationException("Dead Air surface shader is missing.");
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}
            material.shader=shader; material.color=new Color(1,1,1,alpha);
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.mainTextureScale=Vector2.one; material.SetFloat("_Glossiness",alpha<.99f?.85f:.03f);
            material.SetFloat("_Cull",2); material.SetFloat("_ZWrite",alpha<.99f?0:1);
            material.SetFloat("_SrcBlend",(float)(alpha<.99f?BlendMode.SrcAlpha:BlendMode.One));
            material.SetFloat("_DstBlend",(float)(alpha<.99f?BlendMode.OneMinusSrcAlpha:BlendMode.Zero));
            material.renderQueue = alpha<.99f ? 3000 : 2000;
            material.SetFloat("_FogEnabled",1); material.SetFloat("_Cutoff",0);
            material.SetColor("_EmissionColor",emission??Color.black);material.enableInstancing=true;
            EditorUtility.SetDirty(material);return material;
        }

        static Transform Group(Transform parent,string name)
        {var group=new GameObject(name).transform;group.SetParent(parent,false);return group;}

        static string SectorKey(RoomData room)=>room.name+"@"+room.floorY.ToString("F1",System.Globalization.CultureInfo.InvariantCulture);
        static Transform Sector(float x,float y,float z)
        {
            var room=world.rooms.FirstOrDefault(r=>y>=r.floorY-.25f&&y<r.floorY+Storey-.1f&&x>=r.minX&&x<=r.maxX&&z>=r.minZ&&z<=r.maxZ);
            return room!=null&&sectors.TryGetValue(SectorKey(room),out var sector)?sector:
                y<-1?station.transform:
                z>6||z<-54||Mathf.Abs(x)>18?exterior.transform:station.transform;
        }

        static void BuildShell()
        {
            var shell=Group(station.transform,"shell");
            // Ground slab sections with opening under stairs for basement descent
            Box(shell,"Ground left slab",-9.90f,-.18f,-24,16.20f,.36f,60,floor);
            Box(shell,"Ground right slab",9.90f,-.18f,-24,16.20f,.36f,60,floor);
            Box(shell,"Ground corridor front",0,-.18f,-16.5f,3.70f,.36f,45,floor);
            Box(shell,"Ground rear landing",0,-.18f,-52.5f,3.70f,.36f,3,floor);
            // Landing under door-basement from z = -51 to z = -49.5 at y = 0
            Box(shell,"Basement landing slab",0,-.18f,-50.25f,3.70f,.36f,1.5f,floor);

            // Wall enclosing under-stairs space at z = -51 (with doorway for door-basement)
            Box(shell,"Under-stairs wall left",-1.425f,2.1f,-51f,.75f,4.2f,.22f,brick);
            Box(shell,"Under-stairs wall right",1.425f,2.1f,-51f,.75f,4.2f,.22f,brick);
            Box(shell,"Under-stairs wall header",0,3.5f,-51f,2.1f,1.4f,.22f,brick);

            // Basement stair shaft enclosure (walls and foundation from z = -51 to z = -39.5)
            Box(shell,"Basement stair west wall",-1.75f,-2.1f,-45.25f,.22f,4.2f,11.5f,brick);
            Box(shell,"Basement stair east wall",1.75f,-2.1f,-45.25f,.22f,4.2f,11.5f,brick);
            Box(shell,"Basement stair rear wall",0,-2.1f,-51f,3.6f,4.2f,.22f,brick);
            Box(shell,"Basement stair foundation",0,-Storey-.18f,-45.25f,3.72f,.36f,11.5f,floor);

            BuildBasement(shell);
            Box(shell,"Upper left slab",-9.93f,Storey-.16f,-24,16.14f,.32f,60,carpet);
            Box(shell,"Upper right slab",9.93f,Storey-.16f,-24,16.14f,.32f,60,carpet);
            Box(shell,"Upper corridor front",0,Storey-.16f,-16.5f,3.72f,.32f,45,carpet);
            Box(shell,"Upper rear landing",0,Storey-.16f,-52.5f,3.72f,.32f,3,carpet);
            Box(shell,"Roof slab",0,Storey+Ceiling+.17f,-24,36.7f,.34f,60.7f,brick);
            // Thin acoustic panels sit below their structural slabs. Ground panels use the same
            // stair opening as the upper floor, so no ceiling panel cuts through the flight.
            float lowerCeiling=Storey-.355f;
            Box(shell,"Lower ceiling left",-9.93f,lowerCeiling,-24,16.14f,.03f,60,ceilingPanel,false);
            Box(shell,"Lower ceiling right",9.93f,lowerCeiling,-24,16.14f,.03f,60,ceilingPanel,false);
            Box(shell,"Lower ceiling corridor front",0,lowerCeiling,-16.5f,3.72f,.03f,45,ceilingPanel,false);
            Box(shell,"Lower ceiling rear landing",0,lowerCeiling,-52.5f,3.72f,.03f,3,ceilingPanel,false);
            Box(shell,"Upper acoustic ceiling",0,Storey+Ceiling-.035f,-24,36,.03f,60,ceilingPanel,false);
            for(int f=0;f<2;f++)
            {
                float y=f*Storey;
                SideFacade(shell,-18,y);SideFacade(shell,18,y);
                EndFacade(shell,6,y,f==0?"door-front":null);
                EndFacade(shell,-54,y,f==0?"door-rear":null);
                BuildRoomPartitions(shell,y);
                HangingSign(shell,"WEST OFFICES  <  /  >  EAST OFFICES",new Vector3(0,y+3.2f,-8.9f),4.3f,.43f,y+Ceiling);
                HangingSign(shell,"STAIRS  /  SERVICE ROOMS",new Vector3(0,y+3.2f,-26),3.8f,.43f,y+Ceiling);
            }
            BuildStair(shell);
            // Entrance canopy is above the route rather than intersecting its signs or doorway.
            Box(shell,"Entrance canopy",0,3.3f,8.2f,7,.18f,4.2f,metal);
            Box(shell,"Entrance lamp housing",0,3.16f,8.2f,.65f,.10f,.40f,metal,false);
            Box(shell,"Entrance lamp diffuser",0,3.09f,8.2f,.55f,.03f,.30f,lightSurface,false);
            foreach(float x in new[]{-3.2f,3.2f}) Box(shell,"Canopy column",x,1.55f,9.6f,.14f,3.1f,.14f,metal);
            // Lettering is in front of a physical backing fixed flush to the brick face.
            Sign(shell,"BLACKPINE\nBROADCAST CENTRE",new Vector3(0,7.68f,6.19f),Quaternion.identity,6,.48f);
            Sign(shell,"SERVICE / GENERATOR",new Vector3(5.5f,2.9f,-54.19f),Quaternion.Euler(0,180,0),3,.30f);
        }

        static void BuildBasement(Transform shell)
        {
            const float minX=DeadAirOfficeLayout.BasementMinX,maxX=DeadAirOfficeLayout.BasementMaxX;
            const float minZ=DeadAirOfficeLayout.BasementMinZ,maxZ=DeadAirOfficeLayout.BasementMaxZ;
            const float height=Storey-.40f;
            float middleZ=(minZ+maxZ)/2,middleY=-Storey+height/2;
            Box(shell,"Basement floor slab",0,-Storey-.18f,middleZ,maxX-minX+.22f,.36f,maxZ-minZ+.22f,floor);
            Box(shell,"Basement ceiling",0,-.43f,middleZ,maxX-minX+.22f,.06f,maxZ-minZ+.22f,ceilingPanel,false);
            Box(shell,"Basement north wall",0,middleY,maxZ+.11f,maxX-minX+.22f,height,.22f,brick);
            // The unchanged stair shaft opens into the original electrical room.
            foreach(int side in new[]{-1,1})
            {
                Box(shell,"Basement south wall "+side,side*(maxX+1.7f)/2,middleY,minZ-.11f,maxX-1.7f,height,.22f,brick);
                Box(shell,"Basement exterior side "+side,side*(maxX+.11f),middleY,middleZ,.22f,height,maxZ-minZ+.22f,brick);
            }
            Box(shell,"Basement stair opening header",0,-Storey+3.35f,minZ,3.4f,.9f,.22f,brick);
            foreach(int side in new[]{-1,1})
            {
                Partition(true,side*6,minZ,maxZ,false);
                for(int i=0;i<3;i++)
                {
                    float a=maxZ-(i+1)*(maxZ-minZ)/3,b=maxZ-i*(maxZ-minZ)/3;
                    Partition(true,side*11,a,b,true);
                    if(i>0)Partition(false,b,side<0?minX:11,side<0?-11:maxX,false);
                }
            }

            void Partition(bool vertical,float coordinate,float from,float to,bool bars)
            {
                var openings=world.doors.Where(d=>Mathf.Abs(d.y-1.4f+Storey)<.1f&&
                    (vertical?Mathf.Abs(Mathf.Sin(d.rotation))<.1f&&Mathf.Abs(d.x-coordinate)<.1f:
                    Mathf.Abs(Mathf.Cos(d.rotation))<.1f&&Mathf.Abs(d.z-coordinate)<.1f)&&
                    (vertical?d.z:d.x)>from&&(vertical?d.z:d.x)<to).OrderBy(d=>vertical?d.z:d.x);
                float cursor=from;
                foreach(var doorway in openings)
                {
                    float center=vertical?doorway.z:doorway.x,half=doorway.w/2+.10f;
                    Section(cursor,center-half,bars);
                    Solid(center-half,center+half,2.88f,height-2.88f);
                    cursor=center+half;
                }
                Section(cursor,to,bars);

                void Section(float a,float b,bool barred)
                {
                    if(b-a<.001f)return;
                    if(!barred){Solid(a,b,0,height);return;}
                    float center=(a+b)/2,length=b-a;
                    var grille=Group(shell,"Prison barred partition");
                    grille.localPosition=DeadAirOfficeLayout.Position(vertical?coordinate:center,-Storey,vertical?center:coordinate);
                    var blocker=grille.gameObject.AddComponent<BoxCollider>();
                    blocker.center=new Vector3(0,1.44f,0);
                    blocker.size=vertical?new Vector3(.10f,2.88f,length):new Vector3(length,2.88f,.10f);
                    int count=Mathf.Max(2,Mathf.CeilToInt(length/.24f));
                    for(int bar=0;bar<count;bar++)
                    {
                        float offset=-length/2+length*(bar+.5f)/count;
                        Block(grille,"Cell vertical bar",new Vector3(vertical?0:offset,1.44f,vertical?offset:0),new Vector3(.055f,2.88f,.055f),metal,false);
                    }
                    foreach(float railY in new[]{.10f,1.02f,2.78f})
                        Block(grille,"Cell cross rail",new Vector3(0,railY,0),vertical?new Vector3(.065f,.07f,length):new Vector3(length,.07f,.065f),metal,false);
                    Solid(a,b,2.88f,height-2.88f);
                }
                void Solid(float a,float b,float bottom,float wallHeight)
                {
                    if(b-a<.001f)return;
                    Box(shell,"Basement partition",vertical?coordinate:(a+b)/2,-Storey+bottom+wallHeight/2,vertical?(a+b)/2:coordinate,
                        vertical?.22f:b-a,wallHeight,vertical?b-a:.22f,brick);
                }
            }
        }

        sealed class PartitionRun
        {
            public bool vertical;
            public float coordinate;
            public readonly SortedSet<int> units = new SortedSet<int>();
        }

        static void BuildRoomPartitions(Transform shell,float baseY)
        {
            // Merge shared edges before cutting real door apertures. Adjacent circulation
            // rectangles deliberately contribute no partitions, leaving intersecting halls open.
            var runs=new Dictionary<string,PartitionRun>();
            foreach(var room in world.rooms.Where(r=>Mathf.Abs(r.floorY-baseY)<.01f&&DeadAirOfficeLayout.IsInterior(r)&&!DeadAirOfficeLayout.IsCirculation(r)))
            {
                Edge(true,room.minX,room.minZ,room.maxZ);Edge(true,room.maxX,room.minZ,room.maxZ);
                Edge(false,room.minZ,room.minX,room.maxX);Edge(false,room.maxZ,room.minX,room.maxX);
                bool tiled=room.mapLabel=="WC"||room.mapLabel=="KITCHEN"||room.mapLabel=="MAINTENANCE";
                Box(shell,"Room floor "+room.mapLabel,(room.minX+room.maxX)/2,baseY+.008f,(room.minZ+room.maxZ)/2,
                    room.maxX-room.minX-.26f,.016f,room.maxZ-room.minZ-.26f,tiled?floor:carpet,false);
            }
            foreach(var run in runs.Values)
            {
                int start=int.MinValue,last=int.MinValue;
                foreach(int unit in run.units)
                {
                    if(start==int.MinValue){start=last=unit;continue;}
                    if(unit!=last+1){CutRun(run,start,last+1);start=unit;}
                    last=unit;
                }
                if(start!=int.MinValue)CutRun(run,start,last+1);
            }

            void Edge(bool vertical,float coordinate,float from,float to)
            {
                if(vertical&&(Mathf.Abs(coordinate)==18)||!vertical&&(coordinate==6||coordinate==-54))return;
                string key=(vertical?"X":"Z")+coordinate;
                if(!runs.TryGetValue(key,out var run)){run=new PartitionRun{vertical=vertical,coordinate=coordinate};runs[key]=run;}
                for(int i=Mathf.RoundToInt(from);i<Mathf.RoundToInt(to);i++)run.units.Add(i);
            }
            void CutRun(PartitionRun run,float from,float to)
            {
                var openings=world.doors.Where(d=>Mathf.Abs(d.y-1.4f-baseY)<.1f&&
                    (run.vertical?Mathf.Abs(Mathf.Sin(d.rotation))<.1f&&Mathf.Abs(d.x-run.coordinate)<.1f:
                    Mathf.Abs(Mathf.Cos(d.rotation))<.1f&&Mathf.Abs(d.z-run.coordinate)<.1f)&&
                    (run.vertical?d.z:d.x)>from&&(run.vertical?d.z:d.x)<to)
                    .OrderBy(d=>run.vertical?d.z:d.x);
                float cursor=from;
                foreach(var door in openings)
                {
                    float middle=run.vertical?door.z:door.x,half=door.w/2+.10f;
                    Solid(run,cursor,middle-half,0,Ceiling);
                    Solid(run,middle-half,middle+half,2.88f,Ceiling-2.88f);
                    cursor=middle+half;
                }
                Solid(run,cursor,to,0,Ceiling);
            }
            void Solid(PartitionRun run,float from,float to,float bottom,float height)
            {
                if(to-from<.01f)return;
                float center=(from+to)/2;
                Box(shell,"Office partition",run.vertical?run.coordinate:center,baseY+bottom+height/2,
                    run.vertical?center:run.coordinate,run.vertical?.22f:to-from,height,run.vertical?to-from:.22f,plaster);
                if(bottom>.1f)return;
                // Raised skirting on both faces is separated from the plaster plane.
                foreach(float side in new[]{-.13f,.13f})
                    Box(shell,"Partition skirting",run.vertical?run.coordinate+side:center,baseY+.1f,
                        run.vertical?center:run.coordinate+side,run.vertical?.035f:to-from,.2f,run.vertical?to-from:.035f,trim,false);
            }
        }

        static void SideFacade(Transform shell,float x,float baseY)
        {
            Box(shell,"Facade sill",x,baseY+.62f,-24,.35f,1.24f,60,brick);
            Box(shell,"Facade head",x,baseY+3.52f,-24,.35f,.8f,60,brick);
            // Bays follow the adjacent room edges. Every internal partition meets a full
            // brick pier, instead of cutting through the old repeating window grid.
            var breaks=FacadeBreaks(true,x,baseY);
            for(int i=0;i<breaks.Length-1;i++)FacadeBay(shell,true,x,baseY,breaks[i],breaks[i+1]);
        }

        static void EndFacade(Transform shell,float z,float y,string doorway)
        {
            if(doorway!=null)
            {
                float half=doorway=="door-front"?1.4f:1.2f;
                Box(shell,"End facade left",(-18-half)/2,y+Ceiling/2,z,18-half,Ceiling,.35f,brick);
                Box(shell,"End facade right",(18+half)/2,y+Ceiling/2,z,18-half,Ceiling,.35f,brick);
                Box(shell,"Entrance header",0,y+(2.8f+Ceiling)/2,z,half*2,Ceiling-2.8f,.35f,brick);
            }
            else
            {
                Box(shell,"End sill",0,y+.62f,z,36,1.24f,.35f,brick);
                Box(shell,"End head",0,y+3.52f,z,36,.8f,.35f,brick);
                var breaks=FacadeBreaks(false,z,y);
                for(int i=0;i<breaks.Length-1;i++)FacadeBay(shell,false,z,y,breaks[i],breaks[i+1]);
            }
        }

        static float[] FacadeBreaks(bool side,float coordinate,float floorY)
        {
            var breaks=new SortedSet<float>{side?-54:-18,side?6:18};
            foreach(var room in world.rooms.Where(r=>DeadAirOfficeLayout.IsInterior(r)&&Mathf.Abs(r.floorY-floorY)<.01f))
            {
                bool touches=side?Mathf.Abs(room.minX-coordinate)<.01f||Mathf.Abs(room.maxX-coordinate)<.01f:
                    Mathf.Abs(room.minZ-coordinate)<.01f||Mathf.Abs(room.maxZ-coordinate)<.01f;
                if(!touches)continue;
                breaks.Add(side?room.minZ:room.minX);breaks.Add(side?room.maxZ:room.maxX);
            }
            return breaks.ToArray();
        }

        static void FacadeBay(Transform shell,bool side,float coordinate,float floorY,float from,float to)
        {
            float length=to-from;
            int windows=Mathf.Max(1,Mathf.FloorToInt((length-.7f)/3.8f));
            float spacing=length/windows,width=Mathf.Min(2.45f,spacing-1.0f),cursor=from;
            for(int i=0;i<windows;i++)
            {
                float center=from+spacing*(i+.5f),left=center-width/2,right=center+width/2;
                Pier(cursor,left);
                Window(shell,side?new Vector3(-coordinate,floorY+2.18f,center):new Vector3(-center,floorY+2.18f,coordinate),
                    side?Quaternion.Euler(0,90,0):Quaternion.identity,width,1.88f);
                cursor=right;
            }
            Pier(cursor,to);
            void Pier(float a,float b)
            {
                if(b-a<.001f)return;
                Box(shell,"Aligned window pier",side?coordinate:(a+b)/2,floorY+2.18f,side?(a+b)/2:coordinate,
                    side?.35f:b-a,1.88f,side?b-a:.35f,brick);
            }
        }

        static void Window(Transform shell,Vector3 position,Quaternion rotation,float width,float height)
        {
            var root=Group(shell,"Window");root.localPosition=position;root.localRotation=rotation;
            bool isDirectorWindow = Mathf.Abs(position.x - 9f) < .25f && Mathf.Abs(position.y - 6.38f) < .25f && Mathf.Abs(position.z - 6f) < .25f;
            // A breakable pane must retain its renderer and collider after shell batching.
            var pane=isDirectorWindow?Group(root,"state:directorWindowPane"):root;
            Block(pane,"Recessed glass",Vector3.zero,new Vector3(width-.08f,height-.08f,.035f),glass,true);
            foreach(float x in new[]{-width/2+.025f,width/2-.025f})Block(root,"Window reveal side",new Vector3(x,0,0),new Vector3(.05f,height,.35f),trim,false);
            foreach(float y in new[]{-height/2+.025f,height/2-.025f})Block(root,"Window reveal horizontal",new Vector3(0,y,0),new Vector3(width,.05f,.35f),trim,false);
            foreach(float face in new[]{-.185f,.185f})
            {
                foreach(float x in new[]{-width/2,width/2})Block(root,"Window frame side",new Vector3(x,0,face),new Vector3(.075f,height+.08f,.055f),trim,false);
                foreach(float y in new[]{-height/2,height/2})Block(root,"Window frame horizontal",new Vector3(0,y,face),new Vector3(width+.075f,.075f,.055f),trim,false);
            }
            Block(pane,"Mullion",Vector3.zero,new Vector3(.045f,height,.35f),trim,false);
            Block(root,"Interior sill",new Vector3(0,-height/2-.045f,0),new Vector3(width+.15f,.08f,.46f),trim,false);
            if (isDirectorWindow)
            {
                var brokenRoot = Group(root, "state:directorWindowBroken");
                float halfW = (width - .08f) / 2, halfH = (height - .08f) / 2;
                // Jagged perimeter shards along the bottom sill
                Block(brokenRoot, "Broken glass bottom 1", new Vector3(-halfW * .65f, -halfH + .20f, 0), new Vector3(.45f, .40f, .035f), glass, false);
                Block(brokenRoot, "Broken glass bottom 2", new Vector3(-halfW * .15f, -halfH + .14f, 0), new Vector3(.38f, .28f, .035f), glass, false);
                Block(brokenRoot, "Broken glass bottom 3", new Vector3(halfW * .35f, -halfH + .18f, 0), new Vector3(.42f, .36f, .035f), glass, false);
                Block(brokenRoot, "Broken glass bottom 4", new Vector3(halfW * .75f, -halfH + .26f, 0), new Vector3(.35f, .52f, .035f), glass, false);
                // Jagged shards hanging from top frame
                Block(brokenRoot, "Broken glass top 1", new Vector3(-halfW * .55f, halfH - .20f, 0), new Vector3(.48f, .40f, .035f), glass, false);
                Block(brokenRoot, "Broken glass top 2", new Vector3(halfW * .10f, halfH - .15f, 0), new Vector3(.40f, .30f, .035f), glass, false);
                Block(brokenRoot, "Broken glass top 3", new Vector3(halfW * .65f, halfH - .24f, 0), new Vector3(.42f, .48f, .035f), glass, false);
                // Jagged shards on side reveals
                Block(brokenRoot, "Broken glass left", new Vector3(-halfW + .10f, 0, 0), new Vector3(.20f, halfH * .8f, .035f), glass, false);
                Block(brokenRoot, "Broken glass right", new Vector3(halfW - .10f, 0, 0), new Vector3(.20f, halfH * .85f, .035f), glass, false);
                // Broken mullion remnants at top and bottom (leaving the middle open)
                Block(brokenRoot, "Broken mullion top", new Vector3(0, halfH - .22f, 0), new Vector3(.045f, .44f, .35f), trim, false);
                Block(brokenRoot, "Broken mullion bottom", new Vector3(0, -halfH + .18f, 0), new Vector3(.045f, .36f, .35f), trim, false);
                brokenRoot.gameObject.SetActive(false);
            }
        }

        static void BuildStair(Transform shell)
        {
            foreach(var stair in world.stairs)
            {
                int count=Mathf.Max(1,stair.steps);
                float depth=(stair.maxZ-stair.minZ)/count,rise=(stair.upperY-stair.lowerY)/count;
                float width=stair.maxX-stair.minX;
                for(int i=0;i<count;i++)
                {
                    float top=stair.lowerY+(i+1)*rise;
                    Box(shell,"Stair tread "+(i+1),(stair.minX+stair.maxX)/2,top-.09f,stair.maxZ-(i+.5f)*depth,width,.18f,depth,trim);
                    Box(shell,"Stair riser "+(i+1),(stair.minX+stair.maxX)/2,top-rise/2,stair.maxZ-i*depth,width,rise,.055f,metal);

                    if(i%3==0) foreach(float x in new[]{stair.minX-.05f,stair.maxX+.05f})
                        Box(shell,"Stair handrail post",x,top+.52f,stair.maxZ-(i+.5f)*depth,.05f,1.04f,.05f,metal);
                }
                // Continuous handrails follow the exact stair slope; ground bypasses remain on either side.
                foreach(float x in new[]{stair.minX-.05f,stair.maxX+.05f})
                {
                    Vector3 lowerPos = DeadAirOfficeLayout.Position(x,stair.lowerY+1.06f,stair.maxZ);
                    Vector3 upperPos = DeadAirOfficeLayout.Position(x,stair.upperY+1.06f,stair.minZ);
                    Beam(shell,"Sloping handrail",lowerPos,upperPos,.06f,metal);
                }
                if (stair.upperY >= Storey)
                {
                    // Solid sloped soffit / ceiling sealing the underside of the upper stair flight so there is no hole/void
                    Vector3 soffitA = DeadAirOfficeLayout.Position((stair.minX + stair.maxX) / 2, stair.lowerY - .08f, stair.maxZ);
                    Vector3 soffitB = DeadAirOfficeLayout.Position((stair.minX + stair.maxX) / 2, stair.upperY - .08f, stair.minZ);
                    var soffit = Block(shell, "Stair flight soffit", (soffitA + soffitB) / 2, new Vector3(width, .10f, Vector3.Distance(soffitA, soffitB)), brick, true);
                    soffit.transform.localRotation = Quaternion.LookRotation(soffitB - soffitA);

                    // Solid side walls enclosing the basement descent under the stairs and matching brick texture on the sides
                    for(int i = 0; i < count; i++)
                    {
                        float top = stair.lowerY + (i + 1) * rise;
                        float wallH = Mathf.Max(0.01f, top - stair.lowerY);
                        float zPos = stair.maxZ - (i + .5f) * depth;
                        foreach(float x in new[]{-1.75f, 1.75f})
                            Box(shell, "Stair side wall " + (x < 0 ? "west " : "east ") + (i + 1), x, stair.lowerY + wallH / 2, zPos, .22f, wallH, depth, brick);
                    }

                    foreach(float x in new[]{stair.minX-.08f,stair.maxX+.08f})
                        Beam(shell,"Upper opening rail",DeadAirOfficeLayout.Position(x,Storey+1.05f,stair.maxZ),DeadAirOfficeLayout.Position(x,Storey+1.05f,stair.minZ),.055f,metal);
                    for(int i=0;i<7;i++) foreach(float x in new[]{stair.minX-.08f,stair.maxX+.08f})
                        Box(shell,"Upper guard post",x,Storey+.52f,stair.minZ+i*2,.05f,1.04f,.05f,metal);
                    HangingSign(shell,"2 FLOOR  /  STUDIO + CONTROL",new Vector3(0,3.36f,-37.9f),3.4f,.4f,Storey-.355f);
                }
            }
        }

        static void BuildCourtyards()
        {
            var shell=Group(exterior.transform,"shell");
            Box(shell,"Front yard",0,-.18f,22,60,.36f,32,asphalt);
            var placedLadderRoot = Group(exterior.transform, "state:placedLadder");
            placedLadderRoot.localPosition = new Vector3(9f, 0, 7.26f);
            placedLadderRoot.localRotation = Quaternion.Euler(-10.5f, 0, 0);
            foreach(int side in new[]{-1, 1})
                Block(placedLadderRoot, "Placed ladder rail", new Vector3(side * .24f, 2.9f, 0), new Vector3(.055f, 5.8f, .055f), metal, true);
            for(int step = 0; step < 18; step++)
                Block(placedLadderRoot, "Placed ladder rung", new Vector3(0, .32f + step * .30f, 0), new Vector3(.48f, .035f, .035f), metal, false);
            Box(shell,"Rear yard",0,-.18f,-65,60,.36f,22,asphalt);
            // Ground broken glass shards directly under the director's window (small glass fragments only)
            var groundGlass = Group(shell, "Director window ground glass");
            groundGlass.localPosition = new Vector3(9f, 0.01f, 7.1f);
            for (int g = 0; g < 32; g++)
            {
                float gx = Mathf.Sin(g * 1.57f) * (0.15f + (g % 5) * 0.14f);
                float gz = Mathf.Cos(g * 1.93f) * (0.12f + (g % 4) * 0.16f);
                float rot = g * 47f;
                float sq = 0.045f + (g % 5) * 0.012f;
                float sq2 = 0.040f + ((g + 2) % 4) * 0.014f;
                var shard = Block(groundGlass, "Ground glass shard " + g, new Vector3(gx, 0.003f, gz), new Vector3(sq, 0.004f, sq2), glass, false);
                shard.transform.localRotation = Quaternion.Euler(0, rot, 0);
            }
            Box(shell,"Left service path",-24,-.18f,-24,12,.36f,60,asphalt);
            Box(shell,"Right service path",24,-.18f,-24,12,.36f,60,asphalt);
            // Raised kerbs define the entrance path and parking without overlapping the ground plane.
            foreach(float x in new[]{-4.5f,4.5f})Box(shell,"Path kerb",x,.04f,16,.14f,.08f,18,trim);
            for(int i=0;i<9;i++)Box(shell,"Pedestrian paver",0,.015f,8+i*1.8f,8.8f,.03f,1.73f,floor,false);
            for(int i=0;i<4;i++)Box(shell,"Parking stripe",7.3f+i*5,.03f,28,.08f,.035f,6.5f,paper,false);
            Box(shell,"Parking front stripe",15,.03f,31.4f,20,.035f,.08f,paper,false);
            // Fence keeps the walkable boundaries obvious; the front gate leaves an arrival opening.
            Fence(shell,-30,-76,-30,38);Fence(shell,30,-76,30,38);Fence(shell,-30,-76,30,-76);
            Fence(shell,-30,38,-5,38);Fence(shell,5,38,30,38);
            for(int side=-1;side<=1;side+=2)for(int i=0;i<10;i++)Tree(shell,side*(32+(i%3)), -72+i*11);
            for(int i=0;i<8;i++)Tree(shell,-28+i*8,-80);
            BuildBeyondFence();
            Sign(shell,"BLACKPINE  /  STAFF PARKING",new Vector3(19,1.9f,35.2f),Quaternion.identity,3.2f,.5f);
            foreach(float x in new[]{18.1f,19.9f})Block(shell,"Parking sign post",new Vector3(x,1.025f,35.165f),new Vector3(.07f,2.05f,.07f),metal,true);
        }

        static void Fence(Transform parent,float x1,float z1,float x2,float z2)
        {
            var a=DeadAirOfficeLayout.Position(x1,0,z1);var b=DeadAirOfficeLayout.Position(x2,0,z2);
            int count=Mathf.CeilToInt(Vector3.Distance(a,b)/3);
            for(int i=0;i<=count;i++)Block(parent,"Fence post",Vector3.Lerp(a,b,(float)i/count)+Vector3.up*1.15f,new Vector3(.09f,2.3f,.09f),metal,true);
            for(int i=0;i<=count*3;i++)Block(parent,"Fence picket",Vector3.Lerp(a,b,(float)i/(count*3))+Vector3.up*1.1f,new Vector3(.03f,2.15f,.03f),metal,false);
            foreach(float y in new[]{.3f,1.1f,2.1f})Beam(parent,"Fence rail",a+Vector3.up*y,b+Vector3.up*y,.05f,metal);
            // A single solid collision envelope catches diagonal approaches between thin pickets.
            var barrier=Group(parent,"Fence collision");barrier.localPosition=(a+b)/2+Vector3.up;
            var collider=barrier.gameObject.AddComponent<BoxCollider>();
            collider.size=new Vector3(Mathf.Abs(b.x-a.x)+.08f,2,Mathf.Abs(b.z-a.z)+.08f);
        }

        static void Tree(Transform parent,float x,float z)
        {
            var t=Group(parent,"Courtyard birch");t.localPosition=DeadAirOfficeLayout.Position(x,-.03f,z);
            Block(t,"Tree trunk",new Vector3(0,2.4f,0),new Vector3(.26f,4.8f,.26f),wood,false);
            for(int i=0;i<3;i++)Block(t,"Tree crown",new Vector3((i-1)*.5f,4.6f+i*.75f,.15f*(i%2)),new Vector3(3.2f-i*.55f,1.6f,3.1f-i*.6f),grass,false);
        }

        static void BuildBeyondFence()
        {
            var background=Group(exterior.transform,"Beyond the fence");
            // These continuous, embedded slabs close every edge of the lot. They
            // are scenery only; the existing fence and authoritative bounds stay put.
            Box(background,"Distant ground west",-125,-.21f,-20,190,.36f,500,grass,false);
            Box(background,"Distant ground east",125,-.21f,-20,190,.36f,500,grass,false);
            Box(background,"Distant ground rear",0,-.21f,-173,60,.36f,194,grass,false);
            Box(background,"Distant ground front",0,-.21f,134,60,.36f,192,grass,false);
            Box(background,"Access road",0,-.06f,49,330,.12f,8,asphalt,false);
            Box(background,"Arrival road",0,-.06f,41.5f,9,.12f,7,asphalt,false);
            for(int i=-13;i<=13;i++)Box(background,"Faded road centre dash",i*12,.006f,49,4,.012f,.11f,wastePaper,false);
            // Irregular rows overlap in depth so the skyline is visible from both
            // storeys without a flat billboard edge or a hole under the near trees.
            for(int side=-1;side<=1;side+=2)for(int i=0;i<13;i++)
            {
                float x=side*(42+(i%4)*7),z=-102+i*13;
                Tree(background,x,z);
                if(i%3==0)Tree(background,side*(79+(i%2)*12),z+7);
            }
            for(int i=0;i<16;i++)
            {
                Tree(background,-79+i*10,-99-(i%3)*13);
                if(i%2==0)Tree(background,-65+i*9,69+(i%3)*12);
            }
            DistantBuilding(background,-68,-62,18,10,17);
            DistantBuilding(background,72,-31,20,13,24);
            DistantBuilding(background,-52,83,28,8,16);
            DistantBuilding(background,54,90,23,11,20);
            DistantBuilding(background,0,-129,38,6,19);
            var tower=Group(background,"Distant transmission mast");
            tower.localPosition=DeadAirOfficeLayout.Position(44,-.03f,-91);
            foreach(float x in new[]{-.7f,.7f})
                Beam(tower,"Mast leg",new Vector3(x,0,0),new Vector3(x*.1f,22,0),.12f,metal);
            for(int i=0;i<6;i++)
            {
                float y=2+i*3.1f;
                Beam(tower,"Mast brace",new Vector3(-.7f*(1-y/23),y,0),new Vector3(.7f*(1-(y+3)/23),y+3,0),.075f,metal);
            }
            Block(tower,"Mast aerial",new Vector3(0,20.5f,0),new Vector3(5.5f,.13f,.13f),metal,false);
            BatchStaticGeometry(background,"outside-background");
            foreach(var renderer in background.GetComponentsInChildren<Renderer>(true))renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        static void DistantBuilding(Transform parent,float x,float z,float width,float height,float depth)
        {
            var root=Group(parent,"Distant service building");root.localPosition=DeadAirOfficeLayout.Position(x,-.03f,z);
            Block(root,"Distant building body",new Vector3(0,height/2,0),new Vector3(width,height,depth),brick,false);
            Block(root,"Distant building roof",new Vector3(0,height+.14f,0),new Vector3(width+.5f,.28f,depth+.5f),black,false);
            for(int row=0;row<Mathf.FloorToInt(height/3);row++)for(int column=0;column<Mathf.FloorToInt(width/4);column++)
                Block(root,"Distant dark window",new Vector3(-width/2+2+column*4,1.7f+row*3,depth/2+.014f),new Vector3(1.4f,1.15f,.025f),glass,false);
        }

        static void BuildOfficeDressing()
        {
            var random=new System.Random(96403);
            int webIndex=0,litterIndex=0;
            foreach(var room in world.rooms.Where(DeadAirOfficeLayout.IsInterior))
            {
                var parent=sectors[SectorKey(room)];
                bool circulation=DeadAirOfficeLayout.IsCirculation(room);
                if(!circulation)
                {
                    // The web sits just inside the two real wall faces, under the
                    // acoustic ceiling. It is a strand mesh, never a filled plane.
                    float xInset=Mathf.Abs(room.minX)==18?.175f:.11f;
                    float zInset=room.minZ==-54?.175f:.11f;
                    float ceilingY=room.floorY+(room.floorY<1?Storey-.37f:Ceiling-.05f);
                    var corner=DeadAirOfficeLayout.Position(room.minX+xInset+.009f,ceilingY-.009f,room.minZ+zInset+.009f);
                    var web=Cobweb(parent,"Cobweb:wall:"+(webIndex++),corner,Quaternion.Euler(0,180,0),new Vector2(.85f+(webIndex%3)*.17f,.72f+(webIndex%2)*.18f));
                    WebAnchor(web,"Web anchor ceiling",new Vector3(1,0,0));
                    WebAnchor(web,"Web anchor wall",new Vector3(0,-1,0));
                }
                int count=circulation?3:5;
                for(int i=0;i<count;i++)
                {
                    for(int attempt=0;attempt<45;attempt++)
                    {
                        float x=Mathf.Lerp(room.minX+.60f,room.maxX-.60f,(float)random.NextDouble());
                        float z=Mathf.Lerp(room.minZ+.60f,room.maxZ-.60f,(float)random.NextDouble());
                        if(!ClearDressingFloor(x,room.floorY,z))continue;
                        FloorLitter(parent,"Floor litter:"+(litterIndex++),x,room.floorY+(circulation?0:.016f),z,
                            random.Next(4),(float)random.NextDouble()*360);
                        break;
                    }
                }
            }
            foreach(var room in world.rooms.Where(r=>!DeadAirOfficeLayout.IsInterior(r)))
            {
                var parent=sectors[SectorKey(room)];
                for(int i=0;i<5;i++)for(int attempt=0;attempt<30;attempt++)
                {
                    float x=Mathf.Lerp(room.minX+.8f,room.maxX-.8f,(float)random.NextDouble());
                    float z=Mathf.Lerp(room.minZ+.8f,room.maxZ-.8f,(float)random.NextDouble());
                    if(!ClearDressingFloor(x,0,z)||Mathf.Abs(x)<4.7f&&z>6&&z<25)continue;
                    FloorLitter(parent,"Floor litter:"+(litterIndex++),x,0,z,random.Next(4),(float)random.NextDouble()*360);
                    break;
                }
            }
            // Blood pool and splatters on the floor under the broken window where we climb inside
            var entryRoom = world.rooms.FirstOrDefault(r => r.floorY > 4f && 9f >= r.minX && 9f <= r.maxX && 4.5f >= r.minZ && 4.5f <= r.maxZ);
            if (entryRoom != null && sectors.TryGetValue(SectorKey(entryRoom), out var entrySector))
            {
                var bloodRoot = Group(entrySector, "Window entry blood");
                bloodRoot.localPosition = DeadAirOfficeLayout.Position(9f, entryRoom.floorY + 0.016f, 4.2f);
                // Large irregular pool
                Block(bloodRoot, "Blood main pool", Vector3.zero, new Vector3(0.95f, 0.005f, 0.75f), blood, false);
                Block(bloodRoot, "Blood pool streak 1", new Vector3(-0.35f, 0.001f, -0.25f), new Vector3(0.55f, 0.004f, 0.40f), blood, false);
                Block(bloodRoot, "Blood pool streak 2", new Vector3(0.28f, 0.001f, 0.22f), new Vector3(0.42f, 0.004f, 0.35f), blood, false);
                // Droplets and smears leading into the room
                for (int d = 0; d < 8; d++)
                {
                    float dx = Mathf.Sin(d * 1.7f) * 0.75f;
                    float dz = -0.4f - d * 0.22f + Mathf.Cos(d * 1.3f) * 0.25f;
                    float sz = 0.08f + (d % 3) * 0.04f;
                    Block(bloodRoot, "Blood splatter " + d, new Vector3(dx, 0.001f, dz), new Vector3(sz, 0.003f, sz * 0.8f), blood, false);
                }
            }

            // Blood in Director's office (КАБІНЕТ ДИРЕКТОРА) on the floor
            var dirRoom = world.rooms.FirstOrDefault(r => r.name.Contains("ДИРЕКТОР") || r.mapLabel == "DIRECTOR");
            if (dirRoom != null && sectors.TryGetValue(SectorKey(dirRoom), out var dirSector))
            {
                var dirBlood = Group(dirSector, "Director office blood");
                dirBlood.localPosition = DeadAirOfficeLayout.Position(-12f, dirRoom.floorY + 0.016f, 0f);
                Block(dirBlood, "Director blood pool", Vector3.zero, new Vector3(1.10f, 0.005f, 0.85f), blood, false);
                Block(dirBlood, "Director blood lobe", new Vector3(0.35f, 0.001f, -0.2f), new Vector3(0.60f, 0.004f, 0.45f), blood, false);
                for (int d = 0; d < 10; d++)
                {
                    float dx = Mathf.Sin(d * 2.1f) * 0.85f;
                    float dz = Mathf.Cos(d * 1.6f) * 0.70f;
                    float sz = 0.07f + (d % 4) * 0.035f;
                    Block(dirBlood, "Director blood drop " + d, new Vector3(dx, 0.001f, dz), new Vector3(sz, 0.003f, sz * 0.9f), blood, false);
                }
            }
        }

        static bool ClearDressingFloor(float x,float y,float z)
        {
            if(world.stairs.Any(s=>(Mathf.Abs(s.lowerY-y)<.1f||Mathf.Abs(s.upperY-y)<.1f)&&
                x>s.minX-.45f&&x<s.maxX+.45f&&z>s.minZ-.5f&&z<s.maxZ+.5f))return false;
            if(world.doors.Any(d=>Mathf.Abs(d.y-1.4f-y)<.1f&&Mathf.Abs(x-d.x)<1.6f&&Mathf.Abs(z-d.z)<1.6f))return false;
            if(world.objects.Any(o=>Mathf.Abs(o.y-y)<2&&Mathf.Abs(x-o.x)<.7f&&Mathf.Abs(z-o.z)<.7f))return false;
            foreach(var prop in world.props)
            {
                if(Mathf.Abs(prop.y-y)>.1f||new[]{"corkboard","toolboard","whiteboard","mirror","directory","fan","yardLamp","generatorShelter"}.Contains(prop.type))continue;
                float dx=x-prop.x,dz=z-prop.z,c=Mathf.Cos(prop.rotation),s=Mathf.Sin(prop.rotation);
                if(Mathf.Abs(dx*c-dz*s)<prop.w/2+.3f&&Mathf.Abs(dx*s+dz*c)<prop.d/2+.3f)return false;
            }
            return true;
        }

        static void FloorLitter(Transform parent,string name,float x,float floorY,float z,int type,float rotation)
        {
            var root=Group(parent,name);root.localPosition=DeadAirOfficeLayout.Position(x,floorY+.001f,z);
            root.localRotation=Quaternion.Euler(0,rotation,0);
            // All lowest vertices rest on the floor finish. No collision envelopes
            // are introduced, so a sheet cannot catch the player or an opening door.
            switch(type)
            {
                case 0:
                    Block(root,"Discarded page",new Vector3(0,.002f,0),new Vector3(.31f,.004f,.22f),wastePaper,false);
                    for(int i=0;i<4;i++)Block(root,"Page print",new Vector3(-.018f,.0047f,-.06f+i*.03f),new Vector3(.19f,.0014f,.004f),black,false);
                    break;
                case 1:
                    Block(root,"Flattened cardboard",new Vector3(0,.003f,0),new Vector3(.48f,.006f,.31f),wood,false);
                    Block(root,"Cardboard crease",new Vector3(0,.0068f,0),new Vector3(.011f,.0016f,.28f),black,false);
                    Block(root,"Torn paper label",new Vector3(-.13f,.007f,.02f),new Vector3(.12f,.002f,.08f),wastePaper,false);
                    break;
                case 2:
                    var cup=Group(root,"Tipped paper cup");
                    cup.localRotation=Quaternion.Euler(0,0,70);
                    var cupMesh=PaperCupMesh();
                    cup.localPosition=new Vector3(0,-cupMesh.vertices.Min(v=>(cup.localRotation*v).y),0);
                    cup.gameObject.AddComponent<MeshFilter>().sharedMesh=cupMesh;
                    var renderer=cup.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=wastePaper;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
                    break;
                default:
                    Block(root,"Abandoned file cover",new Vector3(0,.003f,0),new Vector3(.37f,.006f,.25f),trim,false);
                    Block(root,"Loose file page",new Vector3(.025f,.008f,.012f),new Vector3(.28f,.004f,.20f),wastePaper,false);
                    break;
            }
        }

        static Mesh PaperCupMesh()
        {
            const string key="paper-cup";
            if(meshes.TryGetValue(key,out var cached))return cached;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int i=0;i<10;i++)
            {
                float a=i*Mathf.PI*.2f,b=(i+1)*Mathf.PI*.2f;
                Vector3 bottomA=new Vector3(Mathf.Cos(a)*.044f,-.086f,Mathf.Sin(a)*.044f),bottomB=new Vector3(Mathf.Cos(b)*.044f,-.086f,Mathf.Sin(b)*.044f);
                Vector3 topA=new Vector3(Mathf.Cos(a)*.067f,.086f,Mathf.Sin(a)*.067f),topB=new Vector3(Mathf.Cos(b)*.067f,.086f,Mathf.Sin(b)*.067f);
                MeshQuad(vertices,uv,triangles,bottomA,bottomB,topB,topA);
                MeshQuad(vertices,uv,triangles,topA,topB,topB*.90f+Vector3.up*.0086f,topA*.90f+Vector3.up*.0086f);
                MeshQuad(vertices,uv,triangles,Vector3.down*.086f,bottomB,bottomA,Vector3.down*.086f);
            }
            return SavedDressingMesh(key,vertices,uv,triangles);
        }

        static Transform Cobweb(Transform parent,string name,Vector3 position,Quaternion rotation,Vector2 size)
        {
            var root=Group(parent,name);root.localPosition=position;root.localRotation=rotation;root.localScale=new Vector3(size.x,size.y,1);
            var strands=Group(root,"Web strands");strands.gameObject.AddComponent<MeshFilter>().sharedMesh=CobwebMesh(name.StartsWith("Cobweb:locker:",StringComparison.Ordinal));
            var renderer=strands.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=cobwebSilk;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            WebAnchor(root,"Web anchor corner",Vector3.zero);
            return root;
        }

        static void WebAnchor(Transform web,string name,Vector3 point)=>Group(web,name).localPosition=point;

        static Mesh CobwebMesh(bool locker=false)
        {
            string key=locker?"cobweb-locker-strands":"cobweb-strands";
            if(meshes.TryGetValue(key,out var cached))return cached;
            var generated=DeadAirWebGeometry.CreateMesh(locker ? .035f : -.115f);
            try{return SavedDressingMesh(key,new List<Vector3>(generated.vertices),new List<Vector2>(generated.uv),new List<int>(generated.triangles));}
            finally{UnityEngine.Object.DestroyImmediate(generated);}
        }

        static void MeshQuad(List<Vector3> vertices,List<Vector2> uv,List<int> indices,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            int first=vertices.Count;
            vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
            indices.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
            vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
            indices.AddRange(new[]{first+4,first+6,first+5,first+4,first+7,first+6});
        }

        static Mesh SavedDressingMesh(string key,List<Vector3> vertices,List<Vector2> uv,List<int> triangles)
        {
            string path=Folder+"/Meshes/"+key+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();mesh.name=key;mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);meshes[key]=mesh;return mesh;
        }

        static Transform PropRoot(PropData p)
        {
            var root=Group(Sector(p.x,p.y,p.z),"prop:"+p.id);
            root.localPosition=DeadAirOfficeLayout.Position(p.x,p.y,p.z);root.localRotation=DeadAirOfficeLayout.Rotation(p.rotation);
            return root;
        }

        static void BuildProp(PropData p)
        {
            if(p.type=="locker")return; // The animated locker is generated once from LockerData.
            var root=PropRoot(p);
            bool generated=p.type=="table"||p.type=="archivistDesk"||p.type=="workstation"||p.type=="chair"||p.type=="shelf"||p.type=="cardCatalog"||p.type=="serverRack"||p.type=="crt"||
                p.type=="dieselGenerator"||p.type=="fusePanel"||p.type=="monitorWall"||p.type=="mixingConsole"||p.type=="anchorDesk";
            if(!generated&&templates.TryGetValue(p.type,out var template))
            {
                // The source export batched the shared desk body outside this decoration group.
                if(p.type=="archivistDesk")Table(root,p.w>0?p.w:2.4f,p.d>0?p.d:1.3f);
                var visual=UnityEngine.Object.Instantiate(template,root,false);visual.name=p.label;
                if(p.type=="van")
                    foreach(var obsoleteCone in visual.GetComponentsInChildren<Renderer>(true)
                        .Where(renderer=>renderer.sharedMaterials.Any(material=>material!=null&&material.name=="surface_697")).ToArray())
                        // The browser export faked headlights with translucent cone
                        // meshes. Real lights inside those shells would be shadowed.
                        UnityEngine.Object.DestroyImmediate(obsoleteCone.gameObject);
                if(p.type=="dieselGenerator")ApplyGeneratorMaterials(visual);
                foreach(var r in visual.GetComponentsInChildren<Renderer>(true)){r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;}
                if(p.type=="van")
                {
                    // Retain only the two glass lenses as individual renderers.
                    // The rest of the van still joins the exterior's static batches.
                    foreach(var lens in visual.GetComponentsInChildren<Renderer>(true))
                        if(lens.sharedMaterials.Any(material=>material!=null&&material.name=="surface_677"))
                            lens.name="headlamp-lens:"+(root.InverseTransformPoint(lens.bounds.center).x<0?"left":"right");
                    DeadAirWorld.InstallVanHeadlamps(root);
                }
                // Keep furniture's original proportions and UVs. Dimensions in data describe collision footprint only.
                return;
            }
            float w=p.w>0?p.w:1,d=p.d>0?p.d:1;
            switch(p.type)
            {
                case "bench":
                    Block(root,"Bench seat",new Vector3(0,.48f,0),new Vector3(w,.12f,d),wood,true);
                    Block(root,"Bench back",new Vector3(0,.83f,-d*.45f),new Vector3(w,.65f,.1f),wood,true);
                    foreach(float x in new[]{-.8f,.8f})Block(root,"Bench leg",new Vector3(x,.24f,0),new Vector3(.1f,.48f,.5f),metal,true);break;
                case "planter":
                    Block(root,"Planter rim",new Vector3(0,.27f,0),new Vector3(w,.54f,d),brick,true);
                    Block(root,"Planter soil",new Vector3(0,.55f,0),new Vector3(w-.16f,.06f,d-.16f),wood,false);
                    for(int i=0;i<5;i++)Block(root,"Box shrub",new Vector3(-w*.35f+i*w*.175f,.83f,(i%2-.5f)*.23f),new Vector3(.45f,.55f,.45f),grass,false);break;
                case "yardLamp":
                    Block(root,"Lamp pole",new Vector3(0,2.25f,0),new Vector3(.14f,4.5f,.14f),metal,true);
                    Block(root,"Lamp head",new Vector3(0,4.5f,0),new Vector3(.5f,.18f,.5f),lightSurface,false);
                    Practical(root,"lamp:yard-"+p.id,new Vector3(0,4.25f,0),10,1.2f,new Color(.90f,.91f,.76f));break;
                case "generatorShelter":
                    Block(root,"Weather roof",new Vector3(0,3.5f,0),new Vector3(w,.2f,d),metal,true);
                    foreach(float x in new[]{-w/2+.2f,w/2-.2f})foreach(float z in new[]{-d/2+.2f,d/2-.2f})Block(root,"Shelter column",new Vector3(x,1.7f,z),new Vector3(.12f,3.4f,.12f),metal,true);
                    Block(root,"Generator weather wall",new Vector3(0,1.5f,-d/2),new Vector3(w,3,.14f),brick,true);
                    GeneratorServiceLamp(root);break;
                case "fuelStore":
                    Block(root,"Fuel store base",new Vector3(0,.18f,0),new Vector3(w,.36f,d),metal,true);
                    for(int i=0;i<3;i++)Block(root,"Fuel drum",new Vector3(-.9f+i*.9f,.78f,0),new Vector3(.62f,.9f,.62f),metal,true);break;
                case "meetingTable":
                    Table(root,w,d);Paperwork(root,new Vector3(-w*.25f,.83f,0));
                    Paperwork(root,new Vector3(w*.25f,.83f,0));
                    Block(root,"Meeting telephone",new Vector3(0,.89f,0),new Vector3(.32f,.14f,.22f),black,false);break;
                case "workstation":
                    Table(root,w,d);Monitor(root,new Vector3(.25f,.84f,-d*.27f));
                    Block(root,"Keyboard",new Vector3(.25f,.835f,d*.12f),new Vector3(.62f,.035f,.21f),paper,false);
                    Block(root,"Computer tower",new Vector3(w*.32f,.35f,-d*.18f),new Vector3(.34f,.70f,.52f),paper,true);
                    DeadAirDrawerPresentation.BuildFurnitureDrawers(root,p,wood,trim,metal,paper);
                    Paperwork(root,new Vector3(-w*.30f,.83f,d*.12f));break;
                case "prisonBed":
                    Block(root,"Steel bed frame",new Vector3(0,.37f,0),new Vector3(w,.12f,d),metal,true);
                    Block(root,"Worn prison mattress",new Vector3(0,.49f,0),new Vector3(w-.12f,.14f,d-.10f),carpet,true);
                    foreach(float x in new[]{-w*.44f,w*.44f})foreach(float z in new[]{-d*.38f,d*.38f})
                        Block(root,"Bed leg",new Vector3(x,.18f,z),new Vector3(.07f,.36f,.07f),metal,true);
                    Block(root,"Prison pillow",new Vector3(-w*.31f,.59f,0),new Vector3(.45f,.10f,d*.75f),wastePaper,false);
                    Block(root,"Folded blanket",new Vector3(w*.29f,.59f,0),new Vector3(.56f,.08f,d*.85f),trim,false);break;
                case "prisonSanitary":
                    Block(root,"Cell toilet pedestal",new Vector3(0,.21f,.18f),new Vector3(.38f,.42f,.40f),paper,true);
                    Block(root,"Cell toilet bowl",new Vector3(0,.43f,.20f),new Vector3(.50f,.15f,.61f),paper,true);
                    Block(root,"Cell toilet seat",new Vector3(0,.515f,.20f),new Vector3(.43f,.025f,.49f),black,false);
                    Block(root,"Cell cistern",new Vector3(0,.67f,-.21f),new Vector3(.48f,.50f,.19f),paper,true);
                    Block(root,"Cell basin",new Vector3(0,.94f,-.21f),new Vector3(.55f,.12f,.34f),metal,true);
                    Block(root,"Cell faucet",new Vector3(.14f,1.04f,-.28f),new Vector3(.05f,.14f,.05f),metal,false);break;
                case "coffeeTable":
                    Block(root,"Low tabletop",new Vector3(0,.42f,0),new Vector3(w,.10f,d),wood,true);
                    foreach(float x in new[]{-w*.38f,w*.38f})Block(root,"Low table foot",new Vector3(x,.20f,0),new Vector3(.10f,.40f,d*.7f),metal,true);
                    Paperwork(root,new Vector3(.2f,.48f,0));break;
                case "sofa":
                    Block(root,"Sofa base",new Vector3(0,.30f,0),new Vector3(w,.46f,d),trim,true);
                    Block(root,"Upholstered back",new Vector3(0,.79f,-d*.40f),new Vector3(w,.70f,.22f),carpet,true);
                    foreach(float x in new[]{-w/2+.09f,w/2-.09f})Block(root,"Padded arm",new Vector3(x,.61f,0),new Vector3(.18f,.4f,d),carpet,true);
                    for(int i=0;i<3;i++)Block(root,"Seat cushion",new Vector3(-w*.31f+i*w*.31f,.57f,.05f),new Vector3(w*.29f,.13f,d*.72f),carpet,false);break;
                case "officePlant":
                    Block(root,"Plant pot",new Vector3(0,.27f,0),new Vector3(.48f,.54f,.48f),brick,true);
                    for(int i=0;i<5;i++){float a=i*2.4f;Block(root,"Broad leaves",new Vector3(Mathf.Sin(a)*.17f,.73f+i*.07f,Mathf.Cos(a)*.17f),new Vector3(.18f,.68f,.18f),grass,false);}break;
                case "coatRack":
                    Block(root,"Coat rack foot",new Vector3(0,.045f,0),new Vector3(w,.09f,d),metal,true);
                    foreach(float x in new[]{-w*.42f,w*.42f})Block(root,"Coat rack upright",new Vector3(x,1,0),new Vector3(.045f,2,.045f),metal,true);
                    Block(root,"Coat rail",new Vector3(0,1.95f,0),new Vector3(w,.045f,.045f),metal,false);
                    for(int i=0;i<3;i++)Block(root,"Hanging overcoat",new Vector3(-w*.3f+i*w*.3f,1.24f,0),new Vector3(.30f,1.1f,.15f),carpet,false);break;
                case "corkboard":case "toolboard":case "directory":
                    Block(root,"Notice board frame",new Vector3(0,1.78f,0),new Vector3(w,1.28f,.09f),wood,false);
                    Block(root,"Notice board face",new Vector3(0,1.78f,.055f),new Vector3(w-.09f,1.19f,.02f),p.type=="toolboard"?metal:carpet,false);
                    for(int i=0;i<6;i++)Block(root,p.type=="toolboard"?"Mounted tool":"Pinned memo",new Vector3(-w*.32f+(i%3)*w*.32f,1.54f+(i/3)*.46f,.077f),new Vector3(p.type=="toolboard"?.055f:.36f,.31f,.022f),paper,false);
                    if(p.type=="directory")Sign(root,"BLACKPINE / FLOOR "+(p.y>1?"02":"01")+"\nWEST  /  EAST  /  STAIRS",new Vector3(0,1.82f,.11f),Quaternion.identity,w-.2f,.75f);break;
                case "mirror":
                    Block(root,"Mirror frame",new Vector3(0,1.85f,0),new Vector3(w,1.1f,.08f),metal,false);
                    Block(root,"Dark mirror",new Vector3(0,1.85f,.052f),new Vector3(w-.1f,1,.022f),glass,false);break;
                case "copier":
                    Block(root,"Copier body",new Vector3(0,.52f,0),new Vector3(w,1.04f,d),paper,true);
                    Block(root,"Copier lid",new Vector3(0,1.07f,-.06f),new Vector3(w+.06f,.12f,d*.78f),trim,true);
                    Block(root,"Copier controls",new Vector3(w*.27f,1.14f,d*.32f),new Vector3(.30f,.06f,.24f),black,false);
                    Block(root,"Paper output",new Vector3(-w*.34f,.7f,d*.53f),new Vector3(.48f,.08f,.30f),metal,false);break;
                case "paperCart":case "cleaningCart":
                    foreach(float y in new[]{.18f,.66f})Block(root,"Utility cart shelf",new Vector3(0,y,0),new Vector3(w,.08f,d),metal,true);
                    foreach(float x in new[]{-w*.43f,w*.43f})foreach(float z in new[]{-d*.43f,d*.43f})Block(root,"Cart frame",new Vector3(x,.46f,z),new Vector3(.05f,.90f,.05f),metal,true);
                    if(p.type=="paperCart")Paperwork(root,new Vector3(0,.72f,0));
                    else Block(root,"Cleaning bucket",new Vector3(0,.91f,0),new Vector3(.45f,.45f,.45f),trim,false);break;
                case "wardrobe":case "radioCabinet":case "vendingMachine":
                    Block(root,"Equipment cabinet",new Vector3(0,1,0),new Vector3(w,2,d),trim,true);
                    Block(root,"Cabinet inset",new Vector3(0,1.27f,d/2+.02f),new Vector3(w*.77f,1.05f,.025f),p.type=="vendingMachine"?glass:black,false);
                    for(int i=0;i<4;i++)Block(root,"Equipment label",new Vector3(-w*.15f,.84f+i*.24f,d/2+.04f),new Vector3(w*.34f,.05f,.018f),paper,false);
                    Block(root,"Cabinet handle",new Vector3(w*.34f,1.0f,d/2+.065f),new Vector3(.035f,.25f,.04f),metal,false);break;
                case "kitchen":
                    Block(root,"Kitchen cupboards",new Vector3(0,.45f,0),new Vector3(w,.9f,d),trim,true);
                    Block(root,"Kitchen worktop",new Vector3(0,.94f,0),new Vector3(w+.04f,.08f,d+.05f),paper,true);
                    for(int i=0;i<5;i++)Block(root,"Cupboard seam",new Vector3(-w/2+i*w/4,.46f,d/2+.012f),new Vector3(.012f,.72f,.012f),black,false);
                    Block(root,"Kitchen sink",new Vector3(-w*.25f,.985f,0),new Vector3(.70f,.025f,d*.65f),metal,false);
                    Block(root,"Kitchen tap",new Vector3(-w*.25f,1.1f,-d*.30f),new Vector3(.04f,.25f,.04f),metal,false);
                    Block(root,"Counter microwave",new Vector3(w*.24f,1.19f,0),new Vector3(.65f,.40f,.45f),paper,false);
                    Block(root,"Microwave window",new Vector3(w*.24f,1.2f,.235f),new Vector3(.46f,.24f,.023f),black,false);break;
                case "fridge":
                    Block(root,"Refrigerator",new Vector3(0,.9f,0),new Vector3(w,1.8f,d),paper,true);
                    Block(root,"Fridge handle",new Vector3(w*.3f,1.2f,d*.5f+.05f),new Vector3(.05f,.36f,.04f),metal,false);break;
                case "sink":
                    Block(root,"Sink cabinet",new Vector3(0,.43f,0),new Vector3(w,.86f,d),paper,true);
                    Block(root,"Sink basin",new Vector3(0,.9f,0),new Vector3(w,.1f,d),metal,true);
                    Block(root,"Sink tap",new Vector3(0,1.02f,-d*.4f),new Vector3(.04f,.24f,.04f),metal,false);break;
                case "toilet":
                    Block(root,"WC pan",new Vector3(0,.28f,0),new Vector3(.5f,.56f,.65f),paper,true);
                    Block(root,"WC cistern",new Vector3(0,.72f,-.35f),new Vector3(.52f,.55f,.25f),paper,true);
                    foreach(float x in new[]{-1.35f,1.35f})Block(root,"Cubicle divider",new Vector3(x,1.3f,-.25f),new Vector3(.05f,2.6f,3.1f),trim,true);
                    // A one metre doorway leaves every cubicle accessible from the washbasins.
                    foreach(float x in new[]{-.9f,.9f})Block(root,"Cubicle front side",new Vector3(x,1.3f,1.3f),new Vector3(.8f,2.6f,.06f),trim,true);
                    Block(root,"Cubicle door head",new Vector3(0,2.48f,1.3f),new Vector3(1,.24f,.06f),trim,true);break;
                case "whiteboard":
                    Block(root,"Whiteboard",new Vector3(0,1.9f,0),new Vector3(w,1.2f,.06f),paper,false);
                    Block(root,"Marker tray",new Vector3(0,1.25f,.05f),new Vector3(w,.06f,.14f),metal,false);break;
                case "chromakeyBackdrop":
                    Block(root,"Studio curtain",new Vector3(0,1.5f,0),new Vector3(w,3,.08f),grass,true);
                    foreach(float x in new[]{-w/2,w/2})Block(root,"Backdrop support",new Vector3(x,1.6f,0),new Vector3(.08f,3.2f,.1f),metal,true);break;
                case "fan":
                    var fan=Group(root,"fan:"+p.id);fan.localPosition=new Vector3(0,1.8f,0);
                    Block(root,"Fan mounting plate",new Vector3(0,1.8f,-.035f),new Vector3(1.2f,1.2f,.05f),black,false);
                    foreach(float x in new[]{-.6f,.6f})Block(root,"Fan side frame",new Vector3(x,1.8f,.035f),new Vector3(.08f,1.28f,.17f),metal,false);
                    foreach(float y in new[]{1.2f,2.4f})Block(root,"Fan horizontal frame",new Vector3(0,y,.035f),new Vector3(1.28f,.08f,.17f),metal,false);
                    for(int i=0;i<4;i++){var blade=Block(fan,"Fan blade",new Vector3(0,.28f,.015f),new Vector3(.16f,.55f,.04f),metal,false);blade.transform.RotateAround(fan.position,fan.forward,i*90);}
                    for(int i=0;i<7;i++)Block(root,"Fan safety grille",new Vector3(-.48f+i*.16f,1.8f,.135f),new Vector3(.015f,1.05f,.018f),metal,false);
                    break;
                case "gateBarrier":
                    Block(root,"Barrier control box",new Vector3(-3,.65f,0),new Vector3(.6f,1.3f,.5f),trim,true);
                    Block(root,"Raised barrier",new Vector3(-3,2.7f,0),new Vector3(.12f,3,.12f),paper,false);break;
                case "dumpster":
                    Block(root,"Service container",new Vector3(0,.7f,0),new Vector3(w,1.4f,d),trim,true);
                    Block(root,"Container lid",new Vector3(0,1.45f,0),new Vector3(w+.1f,.1f,d+.1f),metal,true);break;
                case "crate":
                    Block(root,"Wood crate",new Vector3(0,w*.5f,0),new Vector3(w,w,d),wood,true);
                    foreach(float x in new[]{-w*.35f,w*.35f})Block(root,"Crate band",new Vector3(x,w*.5f,d*.5f+.012f),new Vector3(.06f,w,.025f),trim,false);break;
                case "archivistDesk":
                    Table(root,w,d);Paperwork(root,new Vector3(-w*.33f,.83f,-d*.20f));
                    Block(root,"Desk telephone",new Vector3(w*.32f,.88f,-d*.24f),new Vector3(.32f,.12f,.25f),black,false);break;
                case "table":case "anchorDesk":
                    Table(root,w,d);break;
                case "mixingConsole":
                    Table(root,w,d);
                    Block(root,"Control console body",new Vector3(0,.975f,0),new Vector3(w-.15f,.30f,d*.70f),metal,true);
                    for(int i=0;i<8;i++)
                    {
                        Block(root,"Mixer channel",new Vector3(-w*.35f+i*w*.1f,1.13f,d*.16f),new Vector3(.018f,.012f,.22f),black,false);
                        Block(root,"Mixer fader",new Vector3(-w*.35f+i*w*.1f,1.15f,d*.14f),new Vector3(.06f,.035f,.04f),paper,false);
                    }
                    break;
                case "fusePanel":
                    Block(root,"Mounted fuse cabinet",new Vector3(0,1.65f,0),new Vector3(w,1.15f,.22f),metal,false);
                    Block(root,"Fuse cabinet face",new Vector3(0,1.65f,.12f),new Vector3(w-.08f,1.07f,.025f),trim,false);
                    for(int i=0;i<4;i++)Block(root,"Fuse switch",new Vector3((i%2-.5f)*w*.45f,1.48f+(i/2)*.32f,.15f),new Vector3(.10f,.17f,.055f),black,false);
                    Sign(root,"380 V",new Vector3(0,2.01f,.15f),Quaternion.identity,w*.70f,.14f);break;
                case "monitorWall":
                    // The source export contained screen faces while their cabinet was
                    // batched elsewhere. Give each bank a complete floor-supported rack.
                    Block(root,"Monitor bank cabinet",new Vector3(0,1.20f,0),new Vector3(w,2.40f,.58f),metal,true);
                    for(int i=0;i<3;i++)for(int j=0;j<2;j++)
                    {
                        float x=(j-.5f)*w*.46f,y=.44f+i*.74f;
                        Block(root,"CRT surround",new Vector3(x,y,.31f),new Vector3(w*.44f,.63f,.12f),trim,false);
                        Block(root,"monitor:screen "+i+"-"+j,new Vector3(x,y,.378f),new Vector3(w*.36f,.46f,.022f),glass,false);
                    }
                    break;
                case "dieselGenerator":
                    // Build complete solids; the original source group held isolated
                    // radiator faces, fan and a luminous floating hazard label.
                    Block(root,"Generator skid",new Vector3(0,.12f,0),new Vector3(w+.12f,.24f,d+.12f),metal,true);
                    Block(root,"Generator enclosure",new Vector3(0,1.00f,0),new Vector3(w,1.76f,d),trim,true);
                    Block(root,"Generator top cover",new Vector3(0,1.90f,0),new Vector3(w+.08f,.12f,d+.08f),metal,true);
                    Block(root,"Radiator recess",new Vector3(w*.28f,.98f,d/2+.012f),new Vector3(w*.35f,1.10f,.035f),black,false);
                    for(int i=0;i<8;i++)Block(root,"Radiator louvre",new Vector3(w*.28f,.51f+i*.135f,d/2+.045f),new Vector3(w*.33f,.045f,.06f),metal,false);
                    Block(root,"Generator service door",new Vector3(w*.20f,.95f,-d/2-.012f),new Vector3(w*.52f,1.34f,.03f),metal,false);
                    foreach(float x in new[]{-w*.42f,w*.42f})Block(root,"Generator lifting eye",new Vector3(x,2.0f,0),new Vector3(.13f,.12f,.18f),metal,false);
                    Block(root,"Exhaust stack",new Vector3(-w*.34f,2.10f,-d*.28f),new Vector3(.16f,.42f,.16f),metal,false);
                    Sign(root,"DIESEL / 380 V",new Vector3(w*.28f,1.68f,d/2+.025f),Quaternion.identity,w*.37f,.20f);break;
                case "chair":
                    Block(root,"Chair seat",new Vector3(0,.48f,0),new Vector3(.6f,.1f,.6f),trim,true);
                    Block(root,"Chair back",new Vector3(0,.87f,-.27f),new Vector3(.6f,.65f,.08f),trim,true);
                    foreach(float x in new[]{-.24f,.24f})foreach(float z in new[]{-.24f,.24f})Block(root,"Chair leg",new Vector3(x,.23f,z),new Vector3(.045f,.46f,.045f),metal,true);break;
                case "cardCatalog":
                    DeadAirDrawerPresentation.BuildFurnitureDrawers(root,p,wood,trim,metal,paper);break;
                case "serverRack":
                    Block(root,"Server chassis",new Vector3(0,1,0),new Vector3(w,2,d),metal,true);
                    for(int i=0;i<7;i++){
                        Block(root,"Rack unit",new Vector3(0,.20f+i*.25f,d/2+.01f),new Vector3(w*.86f,.21f,.025f),black,false);
                        Block(root,"Rack indicator",new Vector3(w*.30f,.20f+i*.25f,d/2+.03f),new Vector3(.045f,.025f,.014f),lightSurface,false);
                    }break;
                case "crt":Monitor(root,Vector3.zero);break;
                case "shelf":
                    Block(root,"Storage left",new Vector3(-w/2,.95f,0),new Vector3(.07f,1.9f,d),metal,true);
                    Block(root,"Storage right",new Vector3(w/2,.95f,0),new Vector3(.07f,1.9f,d),metal,true);
                    for(int i=0;i<5;i++){
                        float level=.2f+i*.4f;Block(root,"Storage shelf",new Vector3(0,level,0),new Vector3(w,.05f,d),wood,true);
                        for(int j=0;j<5;j++)Block(root,"Stored binder",new Vector3(-w*.36f+j*w*.18f,level+.16f,0),new Vector3(w*.12f,.27f,d*.74f),((i+j)%3==0)?paper:trim,false);
                    }break;
                default:
                    Block(root,p.label,new Vector3(0,.45f,0),new Vector3(w,.9f,d),metal,true);break;
            }
        }

        static void Table(Transform parent,float w,float d)
        {
            Block(parent,"Desk top",new Vector3(0,.78f,0),new Vector3(w,.08f,d),wood,true);
            foreach(float x in new[]{-w*.4f,w*.4f})foreach(float z in new[]{-d*.35f,d*.35f})
                Block(parent,"Desk leg",new Vector3(x,.38f,z),new Vector3(.08f,.76f,.08f),metal,true);
        }

        static void Paperwork(Transform parent,Vector3 position)
        {
            Block(parent,"Paper stack",position+Vector3.up*.025f,new Vector3(.38f,.05f,.29f),paper,false);
            for(int i=0;i<3;i++)Block(parent,"Typed page line",position+new Vector3(0,.052f,-.07f+i*.045f),new Vector3(.25f,.003f,.012f),trim,false);
        }

        static void Monitor(Transform parent,Vector3 position)
        {
            Block(parent,"CRT stand",position+new Vector3(0,.05f,0),new Vector3(.34f,.1f,.29f),metal,false);
            Block(parent,"CRT housing",position+new Vector3(0,.34f,-.06f),new Vector3(.62f,.52f,.49f),paper,false);
            Block(parent,"CRT screen",position+new Vector3(0,.35f,.195f),new Vector3(.48f,.36f,.022f),glass,false);
            for(int i=0;i<4;i++)Block(parent,"CRT signal line",position+new Vector3(-.04f,.45f-i*.062f,.210f),new Vector3(.28f,.014f,.01f),trim,false);
        }

        static void BuildDoor(DoorData d)
        {
            float baseY=Mathf.Round((d.y-1.4f)/Storey)*Storey;
            float width=d.w>0?d.w:2.2f;
            var casing=Group(station.transform.Find("shell"),"Door frame "+d.id);
            casing.localPosition=DeadAirOfficeLayout.Position(d.x,baseY,d.z);
            casing.localRotation=DeadAirOfficeLayout.Rotation(d.rotation);
            foreach(float z in new[]{-width/2-.055f,width/2+.055f})Block(casing,"Door jamb",new Vector3(0,1.4f,z),new Vector3(.3f,2.8f,.1f),doorWood,true);
            Block(casing,"Door head",new Vector3(0,2.83f,0),new Vector3(.3f,.1f,width+.2f),doorWood,true);
            var pivot=Group(casing,"door:"+d.id);pivot.localPosition=new Vector3(0,0,-width/2);
            if(d.id.StartsWith("door-prison-",StringComparison.Ordinal))
            {
                var leaf=Group(pivot,"Door leaf");
                var collider=leaf.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,1.37f,width/2);
                collider.size=new Vector3(.10f,2.74f,width-.08f);
                int bars=Mathf.CeilToInt((width-.12f)/.22f);
                for(int i=0;i<bars;i++)
                    Block(leaf,"Prison door bar",new Vector3(0,1.37f,.06f+(width-.12f)*(i+.5f)/bars),new Vector3(.055f,2.74f,.055f),metal,false);
                foreach(float y in new[]{.10f,1.04f,2.64f})
                    Block(leaf,"Prison door cross rail",new Vector3(0,y,width/2),new Vector3(.07f,.075f,width-.08f),metal,false);
                Block(leaf,"Prison lock plate",new Vector3(.065f,1.17f,width-.25f),new Vector3(.07f,.32f,.28f),metal,false);
            }
            else
            {
                // Solid wooden door leaf without white border or panels
                Block(pivot,"Door leaf",new Vector3(0,1.37f,width/2),new Vector3(.10f,2.74f,width-.08f),doorWood,true);
            }
            foreach(float x in new[]{-.06f,.06f})
            {
                Block(pivot,"Door handle plate",new Vector3(x,1.17f,width-.28f),new Vector3(.016f,.28f,.07f),metal,false);
                Block(pivot,"Door handle",new Vector3(x*1.6f,1.17f,width-.28f),new Vector3(.06f,.035f,.18f),metal,false);
            }
            float ledFace=d.id=="door-front"||d.id=="door-rear"?.204f:.140f;
            var led=Block(casing,"door-led:"+d.id,new Vector3(ledFace,1.48f,-width/2-.28f),new Vector3(.055f,.07f,.12f),lightSurface,false);
            var lamp=Group(casing,"door-light:"+d.id);lamp.localPosition=led.transform.localPosition+Vector3.right*.05f;
            var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Point;light.range=.7f;light.intensity=.07f;light.shadows=LightShadows.None;
            string text=d.label.Replace("Двері: ","");
            // Signs fit above the casing, with their board proud of the wall. Both corridor sides can be read.
            // Width enlarged to cleanly contain full names like "КАБІНЕТ ДИРЕКТОРА".
            float signFace=d.id=="door-front"||d.id=="door-rear"?.188f:.123f;
            float neededWidth = Mathf.Max(1.45f, text.Length * 0.11f + 0.35f);
            float signW = Mathf.Clamp(neededWidth, 1.45f, Mathf.Min(width + 0.2f, 2.15f));
            float signH = 0.32f;
            Sign(casing,text,new Vector3(signFace,3.15f,0),Quaternion.Euler(0,90,0),signW,signH);
            Sign(casing,text,new Vector3(-signFace,3.15f,0),Quaternion.Euler(0,-90,0),signW,signH);
        }

        static void BuildLocker(LockerData l)
        {
            var root=Group(Sector(l.x,l.y,l.z),"interactive:"+l.id);
            root.localPosition=DeadAirOfficeLayout.Position(l.x,l.y,l.z);root.localRotation=DeadAirOfficeLayout.Rotation(l.ry);
            const float w=1.12f,d=.95f,h=2.18f;
            Block(root,"Locker back",new Vector3(0,h/2,-d/2),new Vector3(w,h,.055f),metal,true);
            foreach(float x in new[]{-w/2,w/2})Block(root,"Locker side",new Vector3(x,h/2,0),new Vector3(.055f,h,d),metal,true);
            foreach(float y in new[]{.05f,h})Block(root,"Locker cap",new Vector3(0,y,0),new Vector3(w,.055f,d),metal,true);
            var pivot=Group(root,"locker-door:"+l.id);pivot.localPosition=new Vector3(-w/2+.03f,0,d/2+.015f);
            float leaf=w-.06f;
            Block(pivot,"Locker lower door",new Vector3(leaf/2,.64f,0),new Vector3(leaf,1.25f,.045f),trim,true);
            Block(pivot,"Locker upper door",new Vector3(leaf/2,2.05f,0),new Vector3(leaf,.2f,.045f),trim,true);
            foreach(float x in new[]{.025f,leaf-.025f})Block(pivot,"Vent frame",new Vector3(x,1.64f,0),new Vector3(.05f,.72f,.05f),trim,false);
            // These are individual physical louvers with real empty sight gaps at the player's eye height.
            // No backing panel or transparent rectangle crosses the grille opening.
            for(int i=0;i<6;i++)Block(pivot,"Open grille slat "+i,new Vector3(leaf/2,1.32f+i*.12f,0),new Vector3(leaf-.08f,.034f,.045f),metal,false);
            Block(pivot,"Locker handle",new Vector3(leaf-.14f,1.10f,.075f),new Vector3(.035f,.19f,.045f),metal,false);
            Block(root,"Locker shelf",new Vector3(0,2.04f,-.08f),new Vector3(w-.1f,.03f,d-.12f),metal,false);
            // Keep strands behind the eye/grille so hiding and its forward view
            // remain clear. The web is fixed to the back and side, not the leaf.
            var web=Cobweb(root,"Cobweb:locker:"+l.id,new Vector3(-w/2+.04f,2.01f,-d/2+.037f),Quaternion.identity,new Vector2(.33f,.27f));
            WebAnchor(web,"Web anchor ceiling",new Vector3(1,0,0));
            WebAnchor(web,"Web anchor wall",new Vector3(0,-1,0));
            Sign(pivot,l.id.Replace("locker-","LOCKER "),new Vector3(leaf/2,.89f,.034f),Quaternion.identity,.6f,.15f);
        }

        static void BuildObjective(ObjectiveData o)
        {
            if (station != null && station.GetComponentsInChildren<Transform>(true).Any(t => t.name == "interactive:" + o.id))
                return;
            float baseY=o.y < -1 ? -Storey : o.y >= Storey ? Storey : 0;
            var root=Group(Sector(o.x,baseY,o.z),"interactive:"+o.id);
            root.localPosition=DeadAirOfficeLayout.Position(o.x,o.y,o.z);
            switch(o.type)
            {
                case "book":
                    root.localRotation=Quaternion.Euler(0,-16,0);
                    // Real folder/dossier lying on the floor in the basement
                    Block(root,"Folder bottom cover",new Vector3(0,-.012f,0),new Vector3(.26f,.006f,.34f),paper,false);
                    Block(root,"Folder pages",new Vector3(.005f,0,0),new Vector3(.24f,.018f,.32f),wastePaper,false);
                    Block(root,"Folder top cover",new Vector3(0,.012f,0),new Vector3(.26f,.006f,.34f),paper,false);
                    Block(root,"Folder spine tape",new Vector3(-.125f,0,0),new Vector3(.016f,.032f,.34f),black,false);
                    Block(root,"Folder metal clip",new Vector3(-.08f,.016f,.12f),new Vector3(.035f,.008f,.04f),metal,false);
                    Block(root,"Folder label plate",new Vector3(.02f,.016f,-.02f),new Vector3(.17f,.003f,.12f),signPlateWhite,false);
                    Block(root,"Folder red stamp",new Vector3(.06f,.018f,.02f),new Vector3(.045f,.002f,.028f),trim,false);
                    for(int line=0;line<4;line++)
                        Block(root,"Folder label text "+line,new Vector3(-.01f,.018f,-.05f+line*.022f),new Vector3(.10f,.002f,.006f),black,false);
                    break;
                case "key":
                    Block(root,"Key bow",Vector3.zero,new Vector3(.06f,.012f,.06f),wood,false);
                    Block(root,"Key shaft",new Vector3(0,0,.045f),new Vector3(.018f,.010f,.06f),metal,false);
                    Block(root,"Key bit",new Vector3(.016f,0,.065f),new Vector3(.022f,.010f,.025f),metal,false);
                    break;
                case "ladder":
                    Vector3 ladderRight = new Vector3(0, 0, 1);
                    Vector3 ladderUp = new Vector3(Mathf.Sin(10.5f * Mathf.Deg2Rad), Mathf.Cos(10.5f * Mathf.Deg2Rad), 0);
                    Vector3 ladderFwd = Vector3.Cross(ladderUp, ladderRight);
                    root.localRotation = Quaternion.LookRotation(ladderFwd, ladderUp);
                    foreach(int side in new[]{-1, 1})
                        Block(root,"Ladder rail",new Vector3(side*.22f,.9f,0),new Vector3(.045f,1.8f,.045f),metal,true);
                    for(int step=0;step<6;step++)
                        Block(root,"Ladder rung",new Vector3(0,.25f+step*.28f,0),new Vector3(.44f,.03f,.03f),metal,false);
                    break;
                case "breaker":
                    root.localRotation=Quaternion.Euler(0,180,0);
                    Block(root,"Breaker panel casing",Vector3.zero,new Vector3(.55f,.75f,.22f),metal,true);
                    var breakerLever=Group(root,"state:breakerHandle");
                    breakerLever.localPosition=new Vector3(0,-.05f,.12f);
                    Block(breakerLever,"Breaker lever arm",new Vector3(0,.12f,0),new Vector3(.06f,.25f,.06f),black,false);
                    Block(breakerLever,"Breaker knob",new Vector3(0,.26f,0),new Vector3(.09f,.08f,.09f),wood,false);
                    Block(root,"state:breakerLamp",new Vector3(.18f,.24f,.12f),new Vector3(.08f,.08f,.03f),lightSurface,false);
                    Sign(root,"380 V\nMAIN BREAKER",new Vector3(0,.34f,.123f),Quaternion.identity,.48f,.16f);
                    break;
                case "tape":
                    Block(root,"Betacam tape",Vector3.zero,new Vector3(.27f,.09f,.17f),black,false);
                    Block(root,"Tape label",new Vector3(0,.052f,0),new Vector3(.18f,.01f,.10f),paper,false);break;
                case "keycard":
                    Block(root,"Access card",Vector3.zero,new Vector3(.15f,.012f,.09f),paper,false);
                    Block(root,"Card stripe",new Vector3(0,.009f,.02f),new Vector3(.15f,.006f,.02f),trim,false);break;
                case "fuse":
                    Block(root,"Ceramic fuse",Vector3.zero,new Vector3(.08f,.07f,.23f),paper,false);
                    foreach(float z in new[]{-.11f,.11f})Block(root,"Fuse terminal",new Vector3(0,0,z),new Vector3(.10f,.09f,.035f),metal,false);break;
                case "canister":
                    if(templates.TryGetValue("fuelCanister",out var canister))
                    {
                        var copy=UnityEngine.Object.Instantiate(canister,root,false);
                        copy.transform.localPosition=Vector3.up*(baseY-o.y);
                    }
                    else
                    {
                        var fallback = Group(root, "fuelCanister");
                        fallback.localPosition = Vector3.up * (baseY - o.y);
                        BuildFuelCanister(fallback);
                    }
                    break;
                case "valve":
                    // A wheel and pipe assembly faces back into the room, away from the outside wall.
                    root.localRotation=Quaternion.Euler(0,90,0);
                    Block(root,"Valve housing",new Vector3(0,0,-.08f),new Vector3(.4f,.4f,.16f),metal,false);
                    Block(root,"Wall-mounted pressure pipe",new Vector3(0,0,-.12f),new Vector3(.10f,2.2f,.10f),metal,false);
                    foreach(float y in new[]{-.80f,.80f})Block(root,"Pressure pipe wall bracket",new Vector3(0,y,-.15f),new Vector3(.30f,.07f,.18f),metal,false);
                    for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Block(root,"Valve wheel",new Vector3(Mathf.Sin(a)*.23f,Mathf.Cos(a)*.23f,.07f),new Vector3(.12f,.12f,.06f),trim,false);}break;
                case "power":
                    root.localRotation=Quaternion.Euler(0,0,0);
                    Block(root,"Power control cabinet",new Vector3(0,.10f,0),new Vector3(1,1.25f,.22f),metal,true);
                    var lever=Group(root,"state:powerLever");lever.localPosition=new Vector3(0,-.17f,.18f);
                    Block(root,"Power switch axle",new Vector3(0,.01f,.135f),new Vector3(.16f,.16f,.07f),black,false);
                    Block(lever,"Main switch handle",new Vector3(0,.18f,0),new Vector3(.08f,.38f,.08f),wood,false);
                    Block(root,"state:powerLamp",new Vector3(.3f,.22f,.135f),new Vector3(.1f,.1f,.035f),lightSurface,false);
                    Sign(root,"GENERATOR / 380 V",new Vector3(0,.59f,.123f),Quaternion.identity,.86f,.23f);break;
                case "broadcast":
                    Block(root,"Broadcast terminal",new Vector3(0,0,0),new Vector3(.7f,.15f,.45f),metal,false);
                    Block(root,"state:broadcastBulb",new Vector3(.22f,.105f,0),new Vector3(.07f,.07f,.07f),lightSurface,false);
                    foreach(float x in new[]{-.25f,.25f})Block(root,"Terminal sign bracket",new Vector3(x,.16f,.21f),new Vector3(.025f,.18f,.025f),metal,false);
                    Sign(root,"96.4 MHz / TRANSMIT",new Vector3(0,.19f,.235f),Quaternion.identity,.75f,.12f);break;
                case "exit":
                    Block(root,"state:exitLamp",new Vector3(0,.25f,0),new Vector3(.12f,.08f,.03f),lightSurface,false);break;
            }
        }

        static void BuildLighting()
        {
            foreach(var room in world.rooms.Where(DeadAirOfficeLayout.IsInterior))
            {
                float width=room.maxX-room.minX,depth=room.maxZ-room.minZ;
                bool circulation=DeadAirOfficeLayout.IsCirculation(room);
                int across=circulation?Mathf.Max(1,Mathf.CeilToInt(width/10)):width>depth&&width>10?2:1;
                int along=circulation?Mathf.Max(1,Mathf.CeilToInt(depth/9)):depth>=width&&depth>10?2:1;
                var parent=sectors[SectorKey(room)];
                for(int ix=0;ix<across;ix++)for(int iz=0;iz<along;iz++)
                {
                    float x=room.minX+width*(ix+.5f)/across,z=room.minZ+depth*(iz+.5f)/along;
                    if(room.floorY<1&&world.stairs.Any(stair=>stair.Contains(x,z)))continue;
                    Fixture(parent,"zone-"+SectorKey(room)+"-"+ix+"-"+iz,x,room.floorY,z,circulation?2.4f:3.0f);
                }
            }
            Practical(exterior.transform,"lamp:front-entry",new Vector3(0,2.85f,8.2f),8,1.2f,new Color(.94f,.95f,.82f));
            // The service shelter supplies the visible rear fixture and its light.
        }

        static void Fixture(Transform parent,string id,float x,float floorY,float z,float intensity)
        {
            Box(parent,"Fluorescent housing",x,floorY+Ceiling-.14f,z,1.7f,.12f,.45f,metal,false);
            Box(parent,"Fluorescent diffuser",x,floorY+Ceiling-.22f,z,1.52f,.04f,.30f,lightSurface,false);
            var light=Practical(parent,"lamp:"+id,DeadAirOfficeLayout.Position(x,floorY+Ceiling-.38f,z),7.5f,intensity,new Color(.94f,.95f,.82f));
            light.type=LightType.Spot;light.transform.localRotation=Quaternion.Euler(90,0,0);
            light.spotAngle=120;light.innerSpotAngle=85;light.renderMode=LightRenderMode.ForcePixel;
            light.shadows=LightShadows.Hard;light.shadowBias=.02f;light.shadowNormalBias=.1f;
        }

        static Light Practical(Transform parent,string name,Vector3 position,float range,float intensity,Color color)
        {
            var root=Group(parent,name);root.localPosition=position;
            var light=root.gameObject.AddComponent<Light>();light.type=LightType.Point;light.color=color;
            light.range=range;light.intensity=intensity;light.renderMode=LightRenderMode.ForcePixel;light.shadows=LightShadows.None;
            return light;
        }

        static void HangingSign(Transform parent,string text,Vector3 position,float width,float height,float ceilingY)
        {
            Sign(parent,text,position,Quaternion.identity,width,height);
            Sign(parent,text,position-new Vector3(0,0,.03f),Quaternion.Euler(0,180,0),width,height);
            float top=position.y+height/2,length=ceilingY-top;
            foreach(float x in new[]{-width*.36f,width*.36f})
                Block(parent,"Wayfinding sign hanger",new Vector3(position.x+x,top+length/2,position.z),new Vector3(.026f,length,.026f),metal,false);
        }

        static void Sign(Transform parent,string text,Vector3 position,Quaternion rotation,float width,float height)
        {
            var sign=Group(parent,"Fitted sign: "+text.Replace('\n',' '));sign.localPosition=position;sign.localRotation=rotation;
            // Sleek metallic frame border around the plaque
            Block(sign,"Sign frame",Vector3.zero,new Vector3(width+.02f,height+.02f,.022f),metal,false);
            // High-contrast clean white backing plate
            Block(sign,"Sign backing",new Vector3(0,0,.002f),new Vector3(width,height,.020f),signPlateWhite,false);
            var letters=Group(sign,"Sign lettering");letters.localPosition=new Vector3(0,0,.018f);letters.localRotation=Quaternion.Euler(0,180,0);
            var mesh=letters.gameObject.AddComponent<TextMesh>();mesh.text=text;mesh.fontSize=128;mesh.characterSize=.025f;
            mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;
            mesh.color=new Color(.08f,.08f,.08f,1f); // Black text on white backing
            if(signFont!=null)
            {
                mesh.font=signFont;
                letters.gameObject.AddComponent<DeadAirOfficeSign>().Initialize(signFont,signLetters);
            }
            mesh.richText=false;
            var renderer=letters.GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;
            // Scale text to cleanly fit inside the physical plaque without ever protruding
            int maxLineChars = 0;
            string[] lines = text.Split('\n');
            foreach (var l in lines) if (l.Length > maxLineChars) maxLineChars = l.Length;
            // IBMPlexMono character advance at fontSize 128 & characterSize 0.025 is ~0.192m wide, ~0.385m tall
            float approxTextW = Mathf.Max(0.1f, maxLineChars * 0.192f);
            float approxTextH = Mathf.Max(0.1f, lines.Length * 0.385f);
            if (renderer.localBounds.size.x > 0.1f)
                approxTextW = Mathf.Max(approxTextW, renderer.localBounds.size.x);
            if (renderer.localBounds.size.y > 0.1f)
                approxTextH = Mathf.Max(approxTextH, renderer.localBounds.size.y);
            float fitScale = Mathf.Min((width * 0.82f) / approxTextW, (height * 0.75f) / approxTextH);
            letters.localScale = Vector3.one * Mathf.Clamp(fitScale, 0.04f, 1.0f);
        }

        static GameObject Box(Transform parent,string name,float x,float y,float z,float w,float h,float d,Material material,bool collide=true)
            =>Block(parent,name,DeadAirOfficeLayout.Position(x,y,z),new Vector3(w,h,d),material,collide);

        static GameObject Block(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collide)
        {
            if(size.x<=0||size.y<=0||size.z<=0)throw new ArgumentOutOfRangeException(name,"Generated solid must have a positive size.");
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=position;
            string key=size.x.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+"_"+size.y.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+"_"+size.z.ToString("F4",System.Globalization.CultureInfo.InvariantCulture);
            if(!meshes.TryGetValue(key,out var mesh))
            {
                string path=Folder+"/Meshes/box_"+key+".asset";
                mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.name="Metre tiled box "+key;
                var v=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
                Vector3 half=size/2;
                Face(v,uv,indices,new Vector3(-half.x,-half.y,half.z),new Vector3(size.x,0,0),new Vector3(0,size.y,0));
                Face(v,uv,indices,new Vector3(half.x,-half.y,-half.z),new Vector3(-size.x,0,0),new Vector3(0,size.y,0));
                Face(v,uv,indices,new Vector3(half.x,-half.y,half.z),new Vector3(0,0,-size.z),new Vector3(0,size.y,0));
                Face(v,uv,indices,new Vector3(-half.x,-half.y,-half.z),new Vector3(0,0,size.z),new Vector3(0,size.y,0));
                Face(v,uv,indices,new Vector3(-half.x,half.y,half.z),new Vector3(size.x,0,0),new Vector3(0,0,-size.z));
                Face(v,uv,indices,new Vector3(-half.x,-half.y,-half.z),new Vector3(size.x,0,0),new Vector3(0,0,size.z));
                mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);meshes[key]=mesh;meshSerial++;
            }
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            if(collide)obj.AddComponent<BoxCollider>().size=size;
            return obj;
        }

        static void Face(List<Vector3> v,List<Vector2> uv,List<int> indices,Vector3 origin,Vector3 right,Vector3 up)
        {
            int first=v.Count;v.Add(origin);v.Add(origin+right);v.Add(origin+right+up);v.Add(origin+up);
            uv.Add(Vector2.zero);uv.Add(new Vector2(right.magnitude/2,0));uv.Add(new Vector2(right.magnitude/2,up.magnitude/2));uv.Add(new Vector2(0,up.magnitude/2));
            indices.Add(first);indices.Add(first+1);indices.Add(first+2);indices.Add(first);indices.Add(first+2);indices.Add(first+3);
        }

        static void Beam(Transform parent,string name,Vector3 a,Vector3 b,float width,Material material)
        {
            var obj=Block(parent,name,(a+b)/2,new Vector3(width,width,Vector3.Distance(a,b)),material,false);
            obj.transform.localRotation=Quaternion.LookRotation(b-a);
        }

        static bool AnimatedAncestor(Transform t,Transform boundary)
        {
            while(t!=null&&t!=boundary)
            {
                if(t.name.StartsWith("interactive:")||t.name.StartsWith("door:")||t.name.StartsWith("door-led:")||
                   t.name.StartsWith("locker-door:")||t.name.StartsWith("drawer:")||t.name.StartsWith("state:")||t.name.StartsWith("fan:")||t.name.StartsWith("monitor:")||
                   t.name.StartsWith("headlamp-lens:")||t.name.StartsWith("Cobweb:")) return true;
                t=t.parent;
            }
            return false;
        }

        // Persist merged meshes rather than relying on runtime static batching. This keeps the larger
        // shell/fences cheap to render, and leaves each room as an independent culling sector.
        static void BatchStaticGeometry(Transform parent,string groupId)
        {
            if(parent==null)return;
            var sources=parent.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.gameObject.activeInHierarchy&&
                r.GetComponent<TextMesh>()==null&&r.TryGetComponent<MeshFilter>(out var filter)&&filter.sharedMesh!=null&&
                !AnimatedAncestor(r.transform,parent)).ToArray();
            var groups=new Dictionary<Material,List<CombineInstance>>();
            foreach(var renderer in sources)
            {
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var material=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];
                    if(material==null)continue;
                    if(!groups.TryGetValue(material,out var list)){list=new List<CombineInstance>();groups.Add(material,list);}
                    list.Add(new CombineInstance{mesh=mesh,subMeshIndex=sub,transform=parent.worldToLocalMatrix*renderer.transform.localToWorldMatrix});
                }
            }
            int index=0;
            foreach(var entry in groups)
            {
                string path=Folder+"/Meshes/static_"+groupId+"_"+(index++)+".asset";
                var combined=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(combined==null){combined=new Mesh();AssetDatabase.CreateAsset(combined,path);}
                combined.Clear();combined.name="Office static "+groupId+" / "+entry.Key.name;
                combined.indexFormat=IndexFormat.UInt32;combined.CombineMeshes(entry.Value.ToArray(),true,true,false);
                combined.RecalculateBounds();EditorUtility.SetDirty(combined);
                var obj=new GameObject("Batched surfaces / "+entry.Key.name);obj.transform.SetParent(parent,false);
                obj.AddComponent<MeshFilter>().sharedMesh=combined;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=entry.Key;
                renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            }
            foreach(var renderer in sources)
            {
                UnityEngine.Object.DestroyImmediate(renderer.GetComponent<MeshFilter>());
                UnityEngine.Object.DestroyImmediate(renderer);
            }
        }

        [MenuItem("Dead Air/Validate Expanded Office")]
        public static void Validate()
        {
            var data=WorldData.Load();
            var interior=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            var outside=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab");
            if(interior==null||outside==null||interior.GetComponent<DeadAirOfficeLayout>()==null)throw new Exception("Office prefabs are missing.");
            var transforms=interior.GetComponentsInChildren<Transform>(true).Concat(outside.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach(var o in data.objects)if(transforms.Count(t=>t.name=="interactive:"+o.id)!=1)throw new Exception("Objective missing or duplicated: "+o.id);
            foreach(var d in data.doors)if(transforms.Count(t=>t.name=="door:"+d.id)!=1)throw new Exception("Door hinge missing or duplicated: "+d.id);
            foreach(var l in data.lockers)
            {
                var locker=transforms.Single(t=>t.name=="interactive:"+l.id);
                var leaf=locker.Find("locker-door:"+l.id);
                if(leaf==null||leaf.Cast<Transform>().Count(t=>t.name.StartsWith("Open grille slat"))!=6)throw new Exception("Locker grille is not open: "+l.id);
                if(Vector3.Distance(locker.localPosition,DeadAirOfficeLayout.Position(l.x,l.y,l.z))>.01f)throw new Exception("Locker visual/data mismatch: "+l.id);
            }
            foreach(var texturePath in AssetDatabase.FindAssets("t:Texture2D",new[]{Folder+"/Textures"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
                if(importer.filterMode!=FilterMode.Point||importer.wrapMode!=TextureWrapMode.Repeat||importer.npotScale!=TextureImporterNPOTScale.None)
                    throw new Exception("Inconsistent office texture sampling: "+texturePath);
            }
            ValidateDieselCanisters();
            Debug.Log("Expanded office validated: saved prefabs, matching objectives and hinged doors, physical locker sight gaps, uniform point-filtered tile textures.");
        }

        [MenuItem("Dead Air/Validate Diesel Canister Geometry")]
        public static void ValidateDieselCanisters()
        {
            var data = WorldData.Load();
            var cached = AssetDatabase.LoadAssetAtPath<GameObject>(Library + "/fuelCanister.prefab");
            if (cached == null || !HasFuelCanisterBody(cached.transform, out _))
                throw new InvalidOperationException("Cached fuel canister must contain a full 3D body, not only its imported label.");
            // Reproduce the exact previous failure without modifying its saved asset.
            var flat = new GameObject("Flat fuel label regression");
            var label = new Mesh();
            try
            {
                label.vertices = new[] { new Vector3(-.12f, -.06f, 0), new Vector3(.12f, -.06f, 0), new Vector3(.12f, .06f, 0), new Vector3(-.12f, .06f, 0) };
                label.triangles = new[] { 0, 1, 2, 0, 2, 3 }; label.RecalculateBounds();
                flat.AddComponent<MeshFilter>().sharedMesh = label;
                if (HasFuelCanisterBody(flat.transform, out _)) throw new InvalidOperationException("A flat imported label must never satisfy the fuel-body cache check.");
            }
            finally { UnityEngine.Object.DestroyImmediate(flat); UnityEngine.Object.DestroyImmediate(label); }
            var roots = new[] { "Assets/Resources/World/station.prefab", "Assets/Resources/World/exterior.prefab" }
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(p => p != null)
                .SelectMany(p => p.GetComponentsInChildren<Transform>(true)).ToArray();
            var canisters = data.objects.Where(o => o.type == "canister").ToArray();
            if (canisters.Length != 3) throw new InvalidOperationException("The saved world must contain all three diesel canisters.");
            foreach (var objective in canisters)
            {
                var root = roots.Single(t => t.name == "interactive:" + objective.id);
                if (!HasFuelCanisterBody(root, out var bounds)) throw new InvalidOperationException("Saved fuel objective has no volumetric body: " + objective.id);
                float expectedFloor = objective.y < -1 ? -Storey : objective.y >= Storey ? Storey : 0;
                if (Mathf.Abs(root.TransformPoint(bounds.min).y - expectedFloor) > .02f)
                    throw new InvalidOperationException("Fuel canister is not supported by its floor: " + objective.id);
                if (root.GetComponentsInChildren<Transform>(true).All(t => t.name != "Carry handle"))
                    throw new InvalidOperationException("Saved fuel canister has no carry handle: " + objective.id);
            }
            Debug.Log("DEAD AIR DIESEL GEOMETRY PASSED: repaired persistent template; flat-label regression rejected; all three saved objectives have a full body, carry handle and floor support.");
        }
    }
}
