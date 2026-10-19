using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    public static class DeadAirPrecisionEquipment
    {
        public static void Print(Transform parent,string text,Vector3 at,Quaternion rotation,float width,float height,Color ink)
        {
            var font=Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
            if(font==null)throw new InvalidOperationException("Equipment lettering font is missing.");
            const string path="Assets/Office/Materials/precision-equipment-lettering.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Dead Air/World Sign"));AssetDatabase.CreateAsset(material,path);}
            material.color=Color.white;
            var holder=new GameObject("Printed equipment legend: "+text).transform;holder.SetParent(parent,false);holder.localPosition=at;holder.localRotation=rotation;
            var letters=new GameObject("Ink glyphs").transform;letters.SetParent(holder,false);letters.localRotation=Quaternion.Euler(0,180,0);
            var mesh=letters.gameObject.AddComponent<TextMesh>();mesh.font=font;mesh.text=text;mesh.fontSize=128;mesh.characterSize=.025f;mesh.color=ink;
            mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.richText=false;
            letters.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            letters.gameObject.AddComponent<DeadAirOfficeSign>().Initialize(font,material,width,height);
        }
        sealed class Assembly
        {
            public Transform root;
            public Material enamel,bakelite,chrome,ivory,paper,rubber;
            public Action<Transform,string,Vector3,Quaternion,float,float> sign;
            public Assembly(Transform parent,Vector3 position,string name,Action<Transform,string,Vector3,Quaternion,float,float> label)
            {
                root=new GameObject(name).transform;root.SetParent(parent,false);root.localPosition=position;sign=label;
                enamel=DeadAirArtMaterials.Create("precision-olive-typewriter-enamel","ceramic",new Color(.34f,.39f,.34f),.29f);
                bakelite=DeadAirArtMaterials.Create("precision-dark-brown-bakelite","rubber",new Color(.055f,.045f,.038f),.38f);
                chrome=DeadAirArtMaterials.Create("precision-nickel-fittings","duct-zinc",new Color(.91f,.93f,.94f),.65f);
                ivory=DeadAirArtMaterials.Create("precision-ivory-keycaps","ceramic",new Color(.78f,.75f,.63f),.27f);
                paper=DeadAirArtMaterials.Create("precision-typewriter-paper","paper",new Color(.95f,.91f,.80f),.05f);
                rubber=DeadAirArtMaterials.Create("precision-platen-and-cord","rubber",new Color(.018f,.020f,.019f),.12f);
            }
            public GameObject Mesh(string name,Mesh mesh,Vector3 at,Material material)
                =>DeadAirFurnitureMeshes.Install(root,name,mesh,at,material);
            public GameObject Box(string name,Vector3 at,Vector3 size,Material material,float radius=.008f)
                =>Mesh(name,DeadAirFurnitureMeshes.RoundedBox(size,Mathf.Min(radius,Mathf.Min(size.x,size.z)*.43f),Mathf.Min(.003f,size.y*.2f)),at,material);
            public GameObject Cylinder(string name,Vector3 at,float radius,float length,Material material,Vector3 axis)
            {
                float bevel=Mathf.Min(.0025f,length*.2f);
                var go=Mesh(name,DeadAirFurnitureMeshes.Turned(new[]{0f,bevel,length-bevel,length},new[]{radius*.94f,radius,radius,radius*.94f},32),at,material);
                go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,axis);return go;
            }
            public GameObject Tube(string name,Vector3[] path,Vector2[] radii,Material material,int sides=12)
                =>Mesh(name,DeadAirFurnitureMeshes.Sweep(path,radii,sides),Vector3.zero,material);
            public void Rod(string name,Vector3 a,Vector3 b,float radius,Material material)
                =>DeadAirFurnitureMeshes.Support(root,name,a,b,radius,radius,material);
            public void Label(string text,Vector3 at,float width,float height,Quaternion rotation)
                =>Print(root,text,at,rotation,width,height,new Color(.045f,.040f,.032f));
        }

        public static void Telephone(Transform parent,Vector3 position,Action<Transform,string,Vector3,Quaternion,float,float> sign)
        {
            var c=new Assembly(parent,position,"Period rotary telephone / complete assembly",sign);
            c.Box("Bakelite rounded lower shell",new Vector3(0,.036f,0),new Vector3(.28f,.072f,.23f),c.bakelite,.042f);
            c.Mesh("Sloping bakelite dial console",DeadAirFurnitureMeshes.SlopingConsole(.252f,.175f,.053f,.021f),new Vector3(0,.053f,.013f),c.bakelite);
            c.Box("Shell parting seam",new Vector3(0,.027f,0),new Vector3(.278f,.003f,.228f),c.rubber,.041f);
            foreach(float x in new[]{-.09f,.09f})foreach(float z in new[]{-.072f,.072f})
                c.Cylinder("Rubber desk foot",new Vector3(x,0,z),.014f,.005f,c.rubber,Vector3.up);
            foreach(float x in new[]{-.108f,.108f})
            {
                c.Box("Receiver cradle fork",new Vector3(x,.112f,-.067f),new Vector3(.018f,.043f,.030f),c.bakelite);
                c.Cylinder("Cradle switch plunger",new Vector3(x,.133f,-.067f),.004f,.015f,c.chrome,Vector3.up);
            }
            c.Mesh("Continuous curved bakelite receiver grip",ReceiverMesh(),Vector3.zero,c.bakelite);
            foreach(float x in new[]{-.136f,.136f})
            {
                c.Mesh("Receiver rounded acoustic cup",DeadAirFurnitureMeshes.Revolved(new[]{new Vector2(.029f,0),new Vector2(.041f,.005f),new Vector2(.044f,.014f),new Vector2(.042f,.027f),new Vector2(.032f,.044f),new Vector2(.018f,.049f),new Vector2(.001f,.049f),new Vector2(.001f,0)},40),new Vector3(x,.102f,-.069f),c.bakelite);
                c.Mesh("Receiver cup threaded seam",DeadAirFurnitureMeshes.Ring(.0415f,.0012f,40),new Vector3(x,.110f,-.069f),c.rubber);
                c.Cylinder("Recessed earpiece diaphragm",new Vector3(x,.101f,-.069f),.032f,.002f,c.rubber,Vector3.up);
                for(int i=0;i<7;i++)
                {
                    float a=i*Mathf.PI*2/7;
                    c.Cylinder("Acoustic grille perforation",new Vector3(x+Mathf.Cos(a)*.017f,.1005f,-.069f+Mathf.Sin(a)*.017f),.002f,.001f,c.bakelite,Vector3.down);
                }
            }
            Transform dial=new GameObject("Separate inclined rotary dial").transform;dial.SetParent(c.root,false);dial.localPosition=new Vector3(0,.097f,.012f);dial.localRotation=Quaternion.Euler(11,0,0);
            var assemblyRoot=c.root;c.root=dial;
            c.Cylinder("Recessed dark dial cavity",new Vector3(0,-.006f,0),.103f,.004f,c.rubber,Vector3.up);
            c.Mesh("Ivory annular telephone number plate",DeadAirFurnitureMeshes.Revolved(new[]{new Vector2(.084f,0),new Vector2(.102f,0),new Vector2(.102f,.004f),new Vector2(.084f,.004f)},60),Vector3.zero,c.ivory);
            c.Mesh("Nickel rotary plate / ten genuine through holes",DeadAirFurnitureMeshes.RotaryDial(),new Vector3(0,.0045f,0),c.chrome);
            c.Cylinder("Ivory dial centre label",new Vector3(0,.012f,0),.033f,.002f,c.ivory,Vector3.up);
            c.Mesh("Centre label retaining bezel",DeadAirFurnitureMeshes.Ring(.034f,.0017f,40),new Vector3(0,.014f,0),c.chrome);
            c.Label("03-27",new Vector3(0,.015f,0),.047f,.012f,Quaternion.Euler(-90,0,0));
            for(int i=0;i<10;i++)
            {
                float a=i*Mathf.PI*.2f;Vector3 at=new Vector3(Mathf.Cos(a)*.094f,.005f,Mathf.Sin(a)*.094f);
                c.Label(((i+1)%10).ToString(),at,.010f,.012f,Quaternion.Euler(-90,0,0));
            }
            c.Rod("Raised rotary finger stop",new Vector3(.074f,.015f,.067f),new Vector3(.062f,.025f,.056f),.003f,c.chrome);
            c.Box("Finger stop mounting tongue",new Vector3(.081f,.009f,.073f),new Vector3(.016f,.006f,.018f),c.chrome,.002f);
            c.root=assemblyRoot;var path=new List<Vector3>();var radii=new List<Vector2>();
            path.Add(new Vector3(-.15f,.119f,-.069f));radii.Add(Vector2.one*.003f);
            path.Add(new Vector3(-.185f,.052f,-.024f));radii.Add(Vector2.one*.003f);
            const int coilSteps=144;
            for(int i=0;i<=coilSteps;i++)
            {
                float t=i/(float)coilSteps,a=t*Mathf.PI*2*9;
                path.Add(new Vector3(-.185f+Mathf.Cos(a)*.010f,.026f+Mathf.Sin(a)*.010f,Mathf.Lerp(-.012f,.145f,t)));radii.Add(Vector2.one*.0026f);
            }
            path.Add(new Vector3(-.16f,.021f,.16f));radii.Add(Vector2.one*.0026f);
            path.Add(new Vector3(-.134f,.035f,.086f));radii.Add(Vector2.one*.003f);
            c.Tube("Continuous nine-turn coiled receiver cord",path.ToArray(),radii.ToArray(),c.rubber,8);
        }

        public static Mesh ReceiverMesh()
        {
            var path=new List<Vector3>();var radii=new List<Vector2>();
            for(int i=0;i<=24;i++)
            {
                float t=i/24f,x=Mathf.Lerp(-.136f,.136f,t),arch=Mathf.Clamp01(Mathf.Sin(t*Mathf.PI));
                path.Add(new Vector3(x,.125f+.029f*Mathf.Pow(arch,.62f),-.069f));
                radii.Add(new Vector2(.017f+.003f*arch,.022f-.004f*arch));
            }
            return DeadAirFurnitureMeshes.Sweep(path.ToArray(),radii.ToArray(),16);
        }

        [MenuItem("Dead Air/Repair Telephone Receiver Mesh")]
        public static void RepairTelephoneReceiverMesh()
        {
            var corrected=ReceiverMesh();int repaired=0;
            ValidateReceiverGeometry();
            const string knownPath="Assets/Office/Meshes/furniture-sweep-dc459773a6c69c543d53a990adcd205e.asset";
            const string knownGuid="9071117b9deb8664d913662b2cc75229";
            if(AssetDatabase.AssetPathToGUID(knownPath)!=knownGuid)
                throw new InvalidOperationException("The legacy receiver path/GUID does not match the audited asset.");
            string serialized=System.IO.File.ReadAllText(knownPath);
            if(!System.Text.RegularExpressions.Regex.IsMatch(serialized,@"m_VertexCount:\s*427\b"))
                throw new InvalidOperationException("The legacy receiver no longer has its audited 427-vertex serialized signature.");
            var knownMesh=AssetDatabase.LoadAssetAtPath<Mesh>(knownPath);
            if(knownMesh==null)throw new InvalidOperationException("The legacy receiver must import as a Mesh object before GUID-preserving replacement.");
            ReplaceReceiverAsset(corrected,knownMesh,knownPath,knownGuid);repaired++;
            foreach(string guid in AssetDatabase.FindAssets("t:Mesh",new[]{"Assets/Office/Meshes"}))
            {
                if(guid==knownGuid)continue;
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(!System.IO.Path.GetFileName(path).StartsWith("furniture-sweep-",StringComparison.Ordinal))continue;
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null||ContainsInvalidGeometry(mesh))throw new InvalidOperationException("Unexpected invalid sweep mesh requires separate repair: "+path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("TELEPHONE RECEIVER REPAIR COMPLETE: "+repaired+" invalid source assets replaced; all prefab GUID references retained. Runtime static batches must be regenerated from the authored source worlds.");
        }

        static void ReplaceReceiverAsset(Mesh corrected,Mesh target,string path,string guid)
        {
            int importedBefore=target.vertexCount;
            EditorUtility.CopySerialized(corrected,target);target.RecalculateBounds();EditorUtility.SetDirty(target);
            if(ContainsInvalidGeometry(target)||target.vertexCount!=427||AssetDatabase.AssetPathToGUID(path)!=guid)
                throw new InvalidOperationException("Receiver replacement did not preserve finite geometry and GUID: "+path);
            Debug.Log("TELEPHONE RECEIVER REPAIRED: "+path+"; GUID retained "+guid+"; imported-before="+importedBefore+"; finite-after="+target.vertexCount);
        }

        static bool ContainsInvalidGeometry(Mesh mesh)
        {
            if(mesh.vertexCount==0||mesh.subMeshCount==0||ContainsNonFiniteVertex(mesh)||!FiniteBounds(mesh.bounds))return true;
            for(int i=0;i<mesh.subMeshCount;i++)if(!FiniteBounds(mesh.GetSubMesh(i).bounds))return true;
            return false;
        }

        static bool FiniteBounds(Bounds bounds)
        {
            var c=bounds.center;var e=bounds.extents;
            return Finite(c.x)&&Finite(c.y)&&Finite(c.z)&&Finite(e.x)&&Finite(e.y)&&Finite(e.z)&&e.x>=0&&e.y>=0&&e.z>=0;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        [MenuItem("Dead Air/Validate Receiver Geometry")]
        public static void ValidateReceiverGeometry()
        {
            var receiver=ReceiverMesh();var size=receiver.bounds.size;
            if(ContainsNonFiniteVertex(receiver)||receiver.vertexCount!=427||size.x<.25f||size.y<.04f||size.z<.04f)
                throw new InvalidOperationException("Receiver sweep has invalid coordinates, topology or bounds.");
            bool rejected=false;
            try{DeadAirFurnitureMeshes.Sweep(new[]{Vector3.zero,new Vector3(float.NaN,1,0)},new[]{Vector2.one*.01f,Vector2.one*.01f},8);}
            catch(InvalidOperationException exception){rejected=exception.Message.StartsWith("Non-finite authored vertex",StringComparison.Ordinal);}
            if(!rejected)throw new InvalidOperationException("The mesh writer accepted a non-finite regression fixture.");
            Debug.Log("TELEPHONE RECEIVER GEOMETRY PASSED: 427 finite vertices, physical bounds and rejected NaN persistence fixture.");
        }

        public static bool ContainsNonFiniteVertex(Mesh mesh)
        {
            foreach(var v in mesh.vertices)
                if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z))return true;
            return false;
        }

        public static void Typewriter(Transform parent,Vector3 position,Action<Transform,string,Vector3,Quaternion,float,float> sign)
        {
            var c=new Assembly(parent,position,"Mechanical typewriter / complete assembly",sign);
            c.Box("Pressed steel bottom chassis",new Vector3(0,.025f,0),new Vector3(.56f,.05f,.40f),c.enamel,.035f);
            foreach(float x in new[]{-.22f,.22f})foreach(float z in new[]{-.145f,.145f})c.Cylinder("Typewriter rubber foot",new Vector3(x,0,z),.018f,.009f,c.rubber,Vector3.up);
            c.Mesh("Inclined keyboard support casting",DeadAirFurnitureMeshes.SlopingConsole(.53f,.20f,.08f,.025f),new Vector3(0,.039f,.102f),c.enamel);
            foreach(float x in new[]{-.265f,.265f})c.Mesh("Separate raised housing side cheek",DeadAirFurnitureMeshes.SlopingConsole(.03f,.26f,.13f,.060f),new Vector3(x,.04f,-.068f),c.enamel);
            c.Box("Rear frame behind open mechanism",new Vector3(0,.105f,-.181f),new Vector3(.51f,.075f,.031f),c.enamel,.014f);
            c.Box("Open typebasket dark well",new Vector3(0,.070f,-.065f),new Vector3(.45f,.028f,.16f),c.rubber,.035f);
            var keyAngles=Quaternion.Euler(16,0,0);
            string[] legends={"ЯЧСМИТЬБЮ.","ФІВАПРОЛДЖЄ","ЙЦУКЕНГШЩЗХ","1234567890"};
            for(int row=0;row<4;row++)
            {
                int count=legends[row].Length;float z=.172f-row*.038f,y=.071f+row*.013f;
                for(int col=0;col<count;col++)
                {
                    float x=(col-(count-1)*.5f)*.043f+(row%2==0?-.005f:.005f);
                    c.Rod("Individual keyboard lever",new Vector3(x,.055f,z-.014f),new Vector3(x,y,z),.0024f,c.chrome);
                    var key=c.Cylinder("Round ivory keycap row"+row+"-"+col,new Vector3(x,y,z),.012f,.009f,c.ivory,Vector3.up);key.transform.localRotation=keyAngles;
                    c.Label(legends[row][col].ToString(),new Vector3(x,y+.0095f,z+.0026f),.014f,.012f,Quaternion.Euler(-74,0,0));
                }
            }
            c.Box("Separate curved space bar",new Vector3(0,.072f,.208f),new Vector3(.33f,.016f,.024f),c.bakelite,.011f);
            foreach(float x in new[]{-.249f,.249f})c.Box("Shift key",new Vector3(x,.079f,.166f),new Vector3(.032f,.015f,.026f),c.ivory,.009f);
            var basketPath=new List<Vector3>();var basketRadii=new List<Vector2>();
            for(int i=0;i<=24;i++)
            {
                float a=Mathf.Lerp(-Mathf.PI*.5f,Mathf.PI*.5f,i/24f);
                basketPath.Add(new Vector3(Mathf.Sin(a)*.16f,.093f,-.064f-Mathf.Cos(a)*.085f));basketRadii.Add(new Vector2(.009f,.006f));
            }
            c.Tube("Semicircular open typebasket segment",basketPath.ToArray(),basketRadii.ToArray(),c.chrome,8);
            for(int i=0;i<27;i++)
            {
                float a=Mathf.Lerp(-1.37f,1.37f,i/26f);
                Vector3 start=new Vector3(Mathf.Sin(a)*.151f,.099f,-.062f-Mathf.Cos(a)*.080f);
                Vector3 end=new Vector3(Mathf.Sin(a)*.044f,.157f,-.056f-Mathf.Cos(a)*.023f);
                c.Rod("Individual exposed typebar "+i,start,end,.0018f,c.chrome);
                var slug=c.Box("Separate type slug "+i,end,new Vector3(.006f,.012f,.008f),c.bakelite,.001f);slug.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
            }
            foreach(float x in new[]{-.19f,.19f})
            {
                c.Cylinder("Ribbon spool winding",new Vector3(x,.119f,-.065f),.038f,.017f,c.rubber,Vector3.up);
                c.Mesh("Perforated ribbon spool flange",DeadAirFurnitureMeshes.RotaryDial(.041f,.007f,.003f,6),new Vector3(x,.137f,-.065f),c.chrome);
                c.Cylinder("Ribbon spool shaft",new Vector3(x,.142f,-.065f),.006f,.008f,c.bakelite,Vector3.up);
            }
            c.Box("Visible ink ribbon between spools",new Vector3(0,.161f,-.047f),new Vector3(.335f,.012f,.002f),c.rubber,.0005f);
            foreach(float x in new[]{-.027f,.027f})c.Rod("Ribbon vibrator guide",new Vector3(x,.10f,-.048f),new Vector3(x,.178f,-.048f),.002f,c.chrome);
            c.Cylinder("Cylindrical rubber paper platen",new Vector3(-.257f,.218f,-.154f),.024f,.514f,c.rubber,Vector3.right);
            c.Cylinder("Polished carriage guide rail",new Vector3(-.309f,.171f,-.183f),.006f,.618f,c.chrome,Vector3.right);
            c.Box("Carriage lower runner",new Vector3(0,.183f,-.170f),new Vector3(.57f,.015f,.027f),c.chrome,.006f);
            foreach(float x in new[]{-.292f,.272f})
            {
                c.Cylinder("Paper-feed spindle knob",new Vector3(x,.218f,-.154f),.031f,.020f,c.bakelite,Vector3.right);
                for(int i=0;i<12;i++)
                {
                    float a=i*Mathf.PI/6;
                    c.Rod("Platen knob knurl",new Vector3(x+.001f,.218f+Mathf.Cos(a)*.030f,-.154f+Mathf.Sin(a)*.030f),new Vector3(x+.018f,.218f+Mathf.Cos(a)*.030f,-.154f+Mathf.Sin(a)*.030f),.0015f,c.rubber);
                }
            }
            c.Rod("Carriage return lever axle",new Vector3(-.272f,.20f,-.15f),new Vector3(-.303f,.25f,-.13f),.004f,c.chrome);
            c.Rod("Carriage return lever",new Vector3(-.303f,.25f,-.13f),new Vector3(-.303f,.265f,-.02f),.004f,c.chrome);
            c.Box("Carriage return finger paddle",new Vector3(-.303f,.267f,-.011f),new Vector3(.041f,.009f,.030f),c.bakelite,.009f);
            for(int i=0;i<16;i++)c.Box("Carriage ruler graduation",new Vector3(-.22f+i*.029f,.196f,-.151f),new Vector3(.0015f,.007f,.002f),c.bakelite,.0005f);
            var front=new Vector3[9][];var back=new Vector3[9][];
            for(int i=0;i<9;i++)
            {
                float t=i/8f,y=.234f+t*.197f,z=-.156f-.024f*Mathf.Sin(t*Mathf.PI*.6f);
                front[i]=new[]{new Vector3(-.145f,y,z+.001f),new Vector3(.145f,y,z+.001f)};
                back[i]=new[]{new Vector3(-.145f,y,z-.001f),new Vector3(.145f,y,z-.001f)};
            }
            c.Mesh("Curved front of paper wrapped around platen",DeadAirFurnitureMeshes.Surface("typed-paper-front",front,Vector3.forward),Vector3.zero,c.paper);
            c.Mesh("Curved paper reverse",DeadAirFurnitureMeshes.Surface("typed-paper-back",back,Vector3.back),Vector3.zero,c.paper);
            for(int i=0;i<6;i++)
            {
                float y=.395f-i*.014f,t=(y-.234f)/.197f,z=-.154f-.024f*Mathf.Sin(t*Mathf.PI*.6f);
                c.Box("Typed report line "+i,new Vector3(-.012f+(i%2)*.011f,y,z),new Vector3(.193f-(i%3)*.023f,.0013f,.001f),c.bakelite,.0003f);
            }
            c.Label("УКРАЇНА",new Vector3(0,.045f,.204f),.13f,.018f,Quaternion.identity);
        }
    }
}
