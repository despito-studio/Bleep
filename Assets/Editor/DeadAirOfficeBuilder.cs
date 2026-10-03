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
        static Material plaster, brick, trim, floor, carpet, ceilingPanel, metal, wood, asphalt, grass, glass, lightSurface, paper, black, signLetters;

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
                    var parent = room.minZ >= 6 || room.maxZ <= -54 || room.maxX <= -18 || room.minX >= 18 ? exterior.transform : station.transform;
                    sectors[SectorKey(room)] = Group(parent, "sector:" + room.name);
                }
                foreach (var prop in world.props) BuildProp(prop);
                foreach (var door in world.doors) BuildDoor(door);
                foreach (var locker in world.lockers) BuildLocker(locker);
                foreach (var objective in world.objects) BuildObjective(objective);
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
            asphalt = Surface("asphalt",new Color(.19f,.22f,.19f),4);
            grass = Surface("grass",new Color(.15f,.23f,.14f),4);
            glass = Surface("dark-window",new Color(.09f,.17f,.18f),2);
            lightSurface = Surface("light-panel",new Color(.79f,.83f,.68f),0, new Color(.5f,.54f,.39f));
            paper = Surface("sign-board",new Color(.8f,.78f,.62f),0);
            black = Surface("dark-detail",new Color(.07f,.09f,.08f),0);
            string signPath=Folder+"/Materials/sign-font.mat";
            signLetters=AssetDatabase.LoadAssetAtPath<Material>(signPath);
            var signShader=Shader.Find("Dead Air/World Sign");
            if(signShader==null)throw new InvalidOperationException("Depth-tested world sign shader is missing.");
            if(signLetters==null){signLetters=new Material(signShader);AssetDatabase.CreateAsset(signLetters,signPath);}
            signLetters.shader=signShader;signLetters.color=Color.white;signLetters.mainTexture=null;
            EditorUtility.SetDirty(signLetters);
        }

        static Material Surface(string name, Color color, int pattern, Color? emission = null)
        {
            string texturePath = Folder + "/Textures/" + name + ".png";
            const int resolution = 64;
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
                colors[z*resolution+x] = color*shade; colors[z*resolution+x].a=1;
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
            material.shader=shader; material.color=Color.white;
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.mainTextureScale=Vector2.one; material.SetFloat("_Glossiness",.03f);
            material.SetFloat("_Cull",2); material.SetFloat("_ZWrite",1);
            material.SetFloat("_SrcBlend",(float)BlendMode.One); material.SetFloat("_DstBlend",(float)BlendMode.Zero);
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
                z>6||z<-54||Mathf.Abs(x)>18?exterior.transform:station.transform;
        }

        static void BuildShell()
        {
            var shell=Group(station.transform,"shell");
            // One non-overlapping slab per floor section. The upper stair opening is a real hole.
            Box(shell,"Ground slab",0,-.18f,-24,36,.36f,60,floor);
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
            // A full-depth reveal bridges the brick opening to the recessed glazing.
            // Both interior and exterior have a frame; the glass never floats at the wall face.
            Block(root,"Recessed glass",Vector3.zero,new Vector3(width-.08f,height-.08f,.035f),glass,true);
            foreach(float x in new[]{-width/2+.025f,width/2-.025f})Block(root,"Window reveal side",new Vector3(x,0,0),new Vector3(.05f,height,.35f),trim,false);
            foreach(float y in new[]{-height/2+.025f,height/2-.025f})Block(root,"Window reveal horizontal",new Vector3(0,y,0),new Vector3(width,.05f,.35f),trim,false);
            foreach(float face in new[]{-.185f,.185f})
            {
                foreach(float x in new[]{-width/2,width/2})Block(root,"Window frame side",new Vector3(x,0,face),new Vector3(.075f,height+.08f,.055f),trim,false);
                foreach(float y in new[]{-height/2,height/2})Block(root,"Window frame horizontal",new Vector3(0,y,face),new Vector3(width+.075f,.075f,.055f),trim,false);
            }
            Block(root,"Mullion",Vector3.zero,new Vector3(.045f,height,.35f),trim,false);
            Block(root,"Interior sill",new Vector3(0,-height/2-.045f,0),new Vector3(width+.15f,.08f,.46f),trim,false);
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
                    Beam(shell,"Sloping handrail",DeadAirOfficeLayout.Position(x,stair.lowerY+1.06f,stair.maxZ),DeadAirOfficeLayout.Position(x,stair.upperY+1.06f,stair.minZ),.06f,metal);
                foreach(float x in new[]{stair.minX-.08f,stair.maxX+.08f})
                    Beam(shell,"Upper opening rail",DeadAirOfficeLayout.Position(x,Storey+1.05f,stair.maxZ),DeadAirOfficeLayout.Position(x,Storey+1.05f,stair.minZ),.055f,metal);
                for(int i=0;i<7;i++) foreach(float x in new[]{stair.minX-.08f,stair.maxX+.08f})
                    Box(shell,"Upper guard post",x,Storey+.52f,stair.minZ+i*2,.05f,1.04f,.05f,metal);
                HangingSign(shell,"2 FLOOR  /  STUDIO + CONTROL",new Vector3(0,3.36f,-37.9f),3.4f,.4f,Storey-.355f);
            }
        }

        static void BuildCourtyards()
        {
            var shell=Group(exterior.transform,"shell");
            Box(shell,"Front yard",0,-.18f,22,60,.36f,32,asphalt);
            Box(shell,"Rear yard",0,-.18f,-65,60,.36f,22,asphalt);
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
            var t=Group(parent,"Courtyard birch");t.localPosition=DeadAirOfficeLayout.Position(x,0,z);
            Block(t,"Tree trunk",new Vector3(0,2.4f,0),new Vector3(.26f,4.8f,.26f),wood,false);
            for(int i=0;i<3;i++)Block(t,"Tree crown",new Vector3((i-1)*.5f,4.6f+i*.75f,.15f*(i%2)),new Vector3(3.2f-i*.55f,1.6f,3.1f-i*.6f),grass,false);
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
            bool generated=p.type=="table"||p.type=="archivistDesk"||p.type=="chair"||p.type=="shelf"||p.type=="cardCatalog"||p.type=="serverRack"||p.type=="crt"||
                p.type=="dieselGenerator"||p.type=="fusePanel"||p.type=="monitorWall"||p.type=="mixingConsole"||p.type=="anchorDesk";
            if(!generated&&templates.TryGetValue(p.type,out var template))
            {
                // The source export batched the shared desk body outside this decoration group.
                if(p.type=="archivistDesk")Table(root,p.w>0?p.w:2.4f,p.d>0?p.d:1.3f);
                var visual=UnityEngine.Object.Instantiate(template,root,false);visual.name=p.label;
                if(p.type=="dieselGenerator")ApplyGeneratorMaterials(visual);
                foreach(var r in visual.GetComponentsInChildren<Renderer>(true)){r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;}
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
                    Block(root,"Desk drawers",new Vector3(-w*.34f,.36f,0),new Vector3(.47f,.72f,d*.82f),trim,true);
                    for(int i=0;i<3;i++)Block(root,"Drawer pull",new Vector3(-w*.34f,.18f+i*.21f,d*.43f),new Vector3(.21f,.03f,.045f),metal,false);
                    Paperwork(root,new Vector3(-w*.30f,.83f,d*.12f));break;
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
                    Block(root,"Filing cabinet",new Vector3(0,.85f,0),new Vector3(w,1.7f,d),metal,true);
                    for(int i=0;i<4;i++)for(int j=0;j<2;j++){
                        float x=(j-.5f)*w*.48f,y=.23f+i*.40f;
                        Block(root,"Card drawer",new Vector3(x,y,d/2+.012f),new Vector3(w*.46f,.37f,.023f),trim,false);
                        Block(root,"Card drawer label",new Vector3(x,y+.06f,d/2+.03f),new Vector3(.20f,.07f,.015f),paper,false);
                        Block(root,"Card drawer handle",new Vector3(x,y-.06f,d/2+.055f),new Vector3(.16f,.025f,.035f),metal,false);
                    }break;
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
            foreach(float z in new[]{-width/2-.055f,width/2+.055f})Block(casing,"Door jamb",new Vector3(0,1.4f,z),new Vector3(.3f,2.8f,.1f),trim,true);
            Block(casing,"Door head",new Vector3(0,2.83f,0),new Vector3(.3f,.1f,width+.2f),trim,true);
            var pivot=Group(casing,"door:"+d.id);pivot.localPosition=new Vector3(0,0,-width/2);
            Block(pivot,"Door leaf",new Vector3(0,1.37f,width/2),new Vector3(.10f,2.74f,width-.08f),wood,true);
            Block(pivot,"Door inset",new Vector3(.065f,1.55f,width/2),new Vector3(.02f,.78f,width*.48f),metal,false);
            foreach(float x in new[]{-.10f,.10f})Block(pivot,"Door handle",new Vector3(x,1.17f,width-.28f),new Vector3(.08f,.045f,.26f),metal,false);
            float ledFace=d.id=="door-front"||d.id=="door-rear"?.204f:.140f;
            var led=Block(casing,"door-led:"+d.id,new Vector3(ledFace,1.48f,-width/2-.28f),new Vector3(.055f,.07f,.12f),lightSurface,false);
            var lamp=Group(casing,"door-light:"+d.id);lamp.localPosition=led.transform.localPosition+Vector3.right*.05f;
            var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Point;light.range=.7f;light.intensity=.07f;light.shadows=LightShadows.None;
            string text=d.label.Replace("Двері: ","");
            // Signs fit above the casing, with their board proud of the wall. Both corridor sides can be read.
            float signFace=d.id=="door-front"||d.id=="door-rear"?.188f:.123f;
            Sign(casing,text,new Vector3(signFace,3.15f,0),Quaternion.Euler(0,90,0),2.25f,.4f);
            Sign(casing,text,new Vector3(-signFace,3.15f,0),Quaternion.Euler(0,-90,0),2.25f,.4f);
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
            Sign(pivot,l.id.Replace("locker-","LOCKER "),new Vector3(leaf/2,.89f,.034f),Quaternion.identity,.6f,.15f);
        }

        static void BuildObjective(ObjectiveData o)
        {
            float baseY=o.y>=Storey?Storey:0;
            var root=Group(Sector(o.x,baseY,o.z),"interactive:"+o.id);
            root.localPosition=DeadAirOfficeLayout.Position(o.x,o.y,o.z);
            switch(o.type)
            {
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
                    if(templates.TryGetValue("fuelCanister",out var canister)){var copy=UnityEngine.Object.Instantiate(canister,root,false);copy.transform.localPosition=Vector3.down*o.y;}
                    else{Block(root,"Fuel canister",Vector3.zero,new Vector3(.42f,.65f,.3f),trim,false);Block(root,"Canister handle",new Vector3(0,.38f,0),new Vector3(.3f,.08f,.08f),metal,false);}break;
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
            Block(sign,"Sign backing",Vector3.zero,new Vector3(width,height,.025f),paper,false);
            var letters=Group(sign,"Sign lettering");letters.localPosition=new Vector3(0,0,.022f);letters.localRotation=Quaternion.Euler(0,180,0);
            var mesh=letters.gameObject.AddComponent<TextMesh>();mesh.text=text;mesh.fontSize=64;mesh.characterSize=.05f;
            mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=new Color(.07f,.11f,.09f);
            if(signFont!=null)
            {
                mesh.font=signFont;
                letters.gameObject.AddComponent<DeadAirOfficeSign>().Initialize(signFont,signLetters);
            }
            mesh.richText=false;
            var renderer=letters.GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;
            // TextMesh's font em varies by typeface: fit measured local bounds to the physical board.
            var bounds=renderer.bounds;
            float scale=Mathf.Min(width*.88f/Mathf.Max(.01f,bounds.size.x),height*.75f/Mathf.Max(.01f,bounds.size.y));
            if(scale>0&&!float.IsInfinity(scale)&&!float.IsNaN(scale))letters.localScale=Vector3.one*scale;
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
                   t.name.StartsWith("locker-door:")||t.name.StartsWith("state:")||t.name.StartsWith("fan:")||t.name.StartsWith("monitor:")) return true;
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
            Debug.Log("Expanded office validated: saved prefabs, matching objectives and hinged doors, physical locker sight gaps, uniform point-filtered tile textures.");
        }
    }
}
