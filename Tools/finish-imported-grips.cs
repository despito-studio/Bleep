using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FinishImportedGrips
{
    public static string Diagnose()
    {
        var rows = new List<string>();
        foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
        {
            var instance = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
            try
            {
                var motion = instance.GetComponent<DeadAir.DeadAirCharacterMotion>();
                motion.SetPreview("Idle", .2f);
                var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked);
                    rows.Add(role);
                    for (float y = 1.05f; y < 1.8f; y += .05f)
                    {
                        float front = float.NegativeInfinity;
                        foreach (var vertex in baked.vertices)
                        {
                            Vector3 p = instance.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                            if (Mathf.Abs(p.x) < .18f && Mathf.Abs(p.y - y) < .035f) front = Mathf.Max(front, p.z);
                        }
                        rows.Add("body y=" + y.ToString("F3") + " front=" + front.ToString("F3"));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        File.WriteAllLines("Verification-ImportedCharacters/torso-diagnostics.txt", rows);
        return "torso diagnostics saved";
    }

    public static string ValidateAndRender()
    {
        return Queue(() =>
        {
            DeadAir.Editor.DeadAirImportedCharacterValidation.Validate();
            DeadAir.Editor.DeadAirThirdPersonGripPreview.RenderAll("Verification-ImportedCharacters/ThirdPerson");
            PhaseChecks();
        });
    }

    public static string RenderOnly()
    {
        return Queue(() => DeadAir.Editor.DeadAirThirdPersonGripPreview.RenderAll("Verification-ImportedCharacters/ThirdPerson"));
    }

    static string Queue(Action action)
    {
        File.WriteAllText("Logs/grip-finish-result.txt", "RUNNING");
        EditorApplication.delayCall += () =>
        {
            try { action(); File.WriteAllText("Logs/grip-finish-result.txt", "PASSED"); }
            catch (Exception e) { File.WriteAllText("Logs/grip-finish-result.txt", e.ToString()); Debug.LogException(e); }
        };
        return "grip checks queued";
    }

    static void PhaseChecks()
    {
        var rows = new List<string>();
        int total = 0;
        float maximum = 0;
        foreach (string role in new[] { "operator", "engineer", "journalist", "dispatcher" })
        {
            var instance = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("World/character_" + role));
            try
            {
                var motion = instance.GetComponent<DeadAir.DeadAirCharacterMotion>();
                foreach (string clip in new[] { "Idle", "Walk", "Run", "UseItem" })
                foreach (string mode in new[] { "map", "camera", "personal" })
                for (int phase = 0; phase <= 20; phase++)
                {
                    var player = new DeadAir.PlayerState { characterClass = role, flashlight = true,
                        inventory = new List<string> { "flashlight" }, selectedSlot = 0, cigarettesRemaining = 11,
                        mapOpen = mode == "map", camera = mode == "camera", uniqueItemEquipped = mode == "personal" };
                    motion.SetEquipmentPreview(player, clip, phase / 20f);
                    float error = motion.GripContactError;
                    if (float.IsNaN(error) || float.IsInfinity(error) || error > .01f)
                        throw new InvalidOperationException(role + " " + clip + " " + mode + " " + phase + " contact error " + error);
                    maximum = Mathf.Max(maximum, error);
                    total++;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        rows.Add(total + " equipment/animation phase samples: max finger contact error " + maximum.ToString("F6") + " metres.");
        File.WriteAllLines("Verification-ImportedCharacters/phase-grip-report.txt", rows);
    }
}
