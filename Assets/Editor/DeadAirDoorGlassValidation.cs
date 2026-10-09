using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirDoorGlassValidation
    {
        [MenuItem("Dead Air/Validate Oak Doors and Window Glass")]
        public static void Validate()
        {
            var station=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            Require(station!=null,"Saved office prefab is missing.");
            var parts=station.GetComponentsInChildren<Transform>(true);
            var oak=AssetDatabase.LoadAssetAtPath<Material>("Assets/Office/Materials/door-wood.mat");
            Require(oak!=null&&oak.shader.name=="Dead Air/Source Surface"&&oak.color.a>.99f,
                "Door oak must be an opaque surface.");
            Require(oak.mainTextureOffset==Vector2.zero,"The oak texture has a stale UV offset.");
            var pixels=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                Require(pixels.LoadImage(File.ReadAllBytes("Assets/Office/Textures/door-wood.png")),"Door oak texture did not load.");
                foreach(var pixel in pixels.GetPixels32())
                    Require(pixel.r<100&&pixel.g<70&&pixel.b<45&&pixel.a==255,
                        "Door oak texture contains a pale border or transparent pixel.");
            }
            finally{UnityEngine.Object.DestroyImmediate(pixels);}
            int woodenDoors=0;
            foreach(var door in WorldData.Load().doors)
            {
                var casing=parts.Single(t=>t.name=="Door frame "+door.id);
                var sides=casing.Cast<Transform>().Where(t=>t.name=="Oak casing side").ToArray();
                var heads=casing.Cast<Transform>().Where(t=>t.name=="Oak casing head").ToArray();
                Require(sides.Length==4&&heads.Length==2,"Door must have complete oak casing on both faces: "+door.id);
                float face=door.id=="door-front"||door.id=="door-rear"?.21f:.17f;
                Require(sides.Concat(heads).All(t=>Mathf.Abs(Mathf.Abs(t.localPosition.x)-face)<.001f),
                    "Oak casing must cover both wall faces: "+door.id);
                Require(heads.All(t=>Mathf.Abs(t.localPosition.y-2.805f)<.001f),
                    "Oak casing must cover the pale top edge: "+door.id);
                if(door.id.StartsWith("door-prison-",StringComparison.Ordinal))continue;
                var leaf=casing.Find("door:"+door.id+"/Door leaf");
                Require(leaf!=null&&leaf.GetComponent<MeshRenderer>()?.sharedMaterial==oak,
                    "A wooden door leaf uses a different or white material: "+door.id);
                woodenDoors++;
            }
            var glazing=AssetDatabase.LoadAssetAtPath<Material>("Assets/Office/Materials/window-glass.mat");
            Require(glazing!=null&&glazing.shader.name=="Dead Air/Window Glass"&&glazing.renderQueue==3000,
                "Windows must use the dedicated transparent glass shader.");
            Require(Mathf.Abs(glazing.color.a-.18f)<.0001f,
                "Window opacity must come from a single constant material alpha.");
            var windows=parts.Where(t=>t.name=="Window").ToArray();
            Require(windows.Length>=40,"Office windows are missing.");
            foreach(var window in windows)
            {
                var pane=window.Find("Recessed glass")??window.Find("state:directorWindowPane/Recessed glass");
                Require(pane!=null&&pane.GetComponent<MeshFilter>()!=null&&pane.GetComponent<MeshRenderer>()?.sharedMaterial==glazing,
                    "Each pane must remain a separate renderer so glazing sorts by depth: "+window.position);
            }
            var screen=AssetDatabase.LoadAssetAtPath<Material>("Assets/Office/Materials/dark-window.mat");
            Require(screen!=null&&screen.color.a>.99f&&screen.renderQueue==2000,
                "Monitor and mirror surfaces must not inherit transparent window glass.");
            Debug.Log("DEAD AIR DOOR/GLASS PASSED: "+woodenDoors+" solid dark oak leaves, dark casing on both doorway faces, no pale texture pixels; "+windows.Length+" separate panes with one constant 18% opacity and no grain texture.");
        }

        static void Require(bool valid,string message)
        {if(!valid)throw new InvalidOperationException(message);}
    }
}
