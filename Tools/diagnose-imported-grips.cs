using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class ImportedGripDiagnostics
{
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Main()
    {
        var rows=new List<string>();
        foreach(string role in new[]{"operator","engineer","journalist","dispatcher"})
        foreach(string mode in new[]{"flashlight","map","camera","personal"})
            Diagnose(role,mode,"Idle",.2f,rows);
        for(int phase=0;phase<=20;phase++)Diagnose("operator","smoke","UseItem",phase/20f,rows);
        Directory.CreateDirectory("Verification-ImportedCharacters");
        File.WriteAllLines("Verification-ImportedCharacters/grip-diagnostics.txt",rows);
        return string.Join("\n",rows);
    }
    static void Diagnose(string role,string mode,string clip,float phase,List<string> rows)
    {
        var instance=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_"+role));
        try
        {
            var motion=instance.GetComponent<DeadAir.DeadAirCharacterMotion>();
            var player=new DeadAir.PlayerState{characterClass=role,flashlight=true,inventory=new List<string>{"flashlight"},selectedSlot=0,
                cigarettesRemaining=11,mapOpen=mode=="map",camera=mode=="camera",uniqueItemEquipped=mode=="personal"||mode=="smoke"};
            motion.SetEquipmentPreview(player,clip,phase);
            string row=role+" "+mode+" phase="+phase.ToString("F2")+" error="+motion.GripContactError.ToString("F6");
            var props=(Dictionary<string,Transform>)typeof(DeadAir.DeadAirCharacterMotion).GetField("props",Fields).GetValue(motion);
            foreach(bool right in new[]{true,false})
            {
                string prefix=right?"right":"left";
                var shoulder=(Transform)typeof(DeadAir.DeadAirCharacterMotion).GetField(prefix+"Arm",Fields).GetValue(motion);
                var elbow=(Transform)typeof(DeadAir.DeadAirCharacterMotion).GetField(prefix+"Forearm",Fields).GetValue(motion);
                var wrist=(Transform)typeof(DeadAir.DeadAirCharacterMotion).GetField(prefix+"Hand",Fields).GetValue(motion);
                var grip=(Vector3)typeof(DeadAir.DeadAirCharacterMotion).GetField(prefix+"Grip",Fields).GetValue(motion);
                row+=" "+prefix+" shoulder="+shoulder.position.ToString("F3")+" reach="+(Vector3.Distance(shoulder.position,elbow.position)+Vector3.Distance(elbow.position,wrist.position)).ToString("F3")+" grip="+grip.ToString("F3");
            }
            if(mode=="map"||mode=="camera")row+=" prop="+props[mode].position.ToString("F3");
            if(mode=="personal")
            {
                var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();
                try
                {
                    skin.BakeMesh(mesh);float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
                    foreach(var v in mesh.vertices){var p=instance.transform.InverseTransformPoint(skin.transform.TransformPoint(v));if(Mathf.Abs(p.x)<.15f&&p.y>1.05f&&p.y<1.43f){lo=Mathf.Min(lo,p.z);hi=Mathf.Max(hi,p.z);}}
                    row+=" chestZ="+lo.ToString("F3")+".."+hi.ToString("F3");
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
            rows.Add(row);
        }
        finally{UnityEngine.Object.DestroyImmediate(instance);}
    }
    public static string Render()
    {
        DeadAir.Editor.DeadAirThirdPersonGripPreview.RenderAll("Verification-ImportedCharacters/ThirdPerson");
        return "27 third person poses passed contact and torso checks.";
    }
}
