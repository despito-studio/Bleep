using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirVisualCapture
    {
        IEnumerator CaptureV175Fixes(WorldData data)
        {
            bool grain = DeadAirSettings.Grain, flicker = DeadAirSettings.Flicker;
            DeadAirSettings.Grain = false; DeadAirSettings.Flicker = false;
            if (frameRenderer == null) frameRenderer = player.ViewCamera.GetComponent<DeadAirFrameRenderer>();
            var report = new List<string>();
            session.State.power = session.State.quest.power = true;
            session.State.breakerOn = session.State.quest.breakerOn = true;
            player.SetVerificationFlashlight(true);

            InspectToward(-4, -44, 0, 0, 1.3f, -45);
            yield return Save("v175-01-ground-steel-guards");
            InspectToward(0, -50.6f, 0, 0, -1.6f, -44);
            yield return Save("v175-02-basement-descent");
            InspectToward(0, -38.1f, -4.2f, 0, -1.4f, -46);
            yield return Save("v175-03-basement-rails-upward");
            InspectToward(-4, -40.5f, 4.2f, 0, 4.85f, -44);
            yield return Save("v175-04-second-floor-side-and-end-guards");
            InspectToward(0, -52.2f, 4.2f, 0, 4.9f, -42);
            yield return Save("v175-05-second-floor-open-landing");

            var longest = data.doors.Where(d => !d.id.StartsWith("door-prison-")).OrderByDescending(d => d.label.Length).Take(3);
            foreach (var door in longest)
            {
                float floor = Mathf.Round((door.y - 1.4f) / 4.2f) * 4.2f;
                InspectToward(door.x - Mathf.Cos(door.rotation) * 2.6f, door.z + Mathf.Sin(door.rotation) * 2.6f,
                    floor, door.x, floor + 2.25f, door.z);
                yield return Save("v175-door-and-room-plaque-" + door.id);
            }
            int signs = CheckPhysicalPlaques();
            report.Add("PASSED: " + signs + " actual runtime glyph meshes fit within their persisted physical plaque meshes after dynamic font generation.");

            var cans = data.objects.Where(o => o.type == "canister").ToArray();
            var generator = data.props.Single(p => p.type == "dieselGenerator");
            Require(cans.Length == 3 && cans.Count(c => c.y < -1) == 1 && cans.Count(c => c.y >= -1) == 2,
                "Exactly two yard canisters and one basement canister are required.");
            Require(cans.Where(c => c.y >= -1).All(c => Vector2.Distance(new Vector2(c.x, c.z), new Vector2(generator.x, generator.z)) < 3.5f),
                "Both yard diesel canisters must stand beside the generator.");
            InspectToward(generator.x + 3.0f, generator.z + 5.5f, 0, generator.x - 1.0f, .8f, generator.z + 1);
            yield return Save("v175-06-generator-two-diesel-canisters");
            foreach (var can in cans)
            {
                float floor = can.y < -1 ? -4.2f : 0;
                InspectToward(can.x, can.z + 1.3f, floor, can.x, can.y, can.z);
                yield return Save("v175-canister-pickup-" + can.id);
                Require(player.InteractionId == can.id, "The relocated physical canister must have a usable E prompt: " + can.id + "; actual=" + player.InteractionId);
                session.Command(new NetPacket { type = "interact", id = can.id });
                yield return null;
                Require(session.State.Player(session.LocalId).HasItem(can.id) && session.State.Collected(can.id),
                    "The host must accept pickup at the new canister position: " + can.id);
            }
            report.Add("PASSED: two generator-side diesel canisters and one basement canister render at their new locations; all three actual E prompts and host-authorized pickups work.");

            var pane = Resources.FindObjectsOfTypeAll<Transform>().Single(t => t.name == "state:directorWindowPane" && t.gameObject.scene.IsValid());
            var glass = pane.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Recessed glass");
            Require(!session.State.windowBroken && pane.gameObject.activeInHierarchy && glass.enabled,
                "The GPU glazing fixture must contain an active intact pane, rather than an already-broken window.");
            Require(glass.sharedMaterial.shader.name == "Dead Air/Window Glass", "The live window must use the smooth glazing shader.");
            float alpha = glass.sharedMaterial.color.a;
            Require(alpha > .05f && alpha < .35f, "The live pane must have a low explicit opacity.");
            Vector3 center = glass.bounds.center;
            Vector3 normal = pane.transform.forward;
            Vector3 eye = center + normal * 3;
            InspectToward(-eye.x, eye.z, eye.y - 1.65f, -center.x, center.y, center.z);
            captureWithoutEquipment = true;
            var transmission = new List<float>();
            for (int mode = 0; mode < 4; mode++)
            {
                SetGlazingLighting(mode);
                yield return Save("v175-window-" + GlazingMode(mode));
                Require(Mathf.Abs(glass.sharedMaterial.color.a - alpha) < .0001f, "Glazing opacity must not change with electricity or the flashlight.");
            }

            // A bright/dark opaque target behind the actual saved pane isolates transmitted
            // background contrast from the pane's direct-light highlight. Compare against the
            // same GPU camera with only this pane's renderer hidden. No shader formula is used.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Glazing transmission GPU probe";
            marker.transform.position = center - normal * .55f;
            marker.transform.rotation = pane.transform.rotation;
            marker.transform.localScale = new Vector3(1.3f, .9f, .025f);
            Destroy(marker.GetComponent<Collider>());
            var material = new Material(Shader.Find("Dead Air/Source Unlit"));
            material.SetFloat("_FogEnabled", 0);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            try
            {
                for (int mode = 0; mode < 4; mode++)
                {
                    SetGlazingLighting(mode);
                    material.color = Color.black;
                    glass.enabled = false;
                    yield return SettleGlazingProbe();
                    float clearDark = SceneLuminance();
                    material.color = Color.white;
                    yield return SettleGlazingProbe();
                    float clearLight = SceneLuminance();
                    yield return Save("v175-probe-" + GlazingMode(mode) + "-without-pane", false);
                    glass.enabled = true;
                    Require(pane.gameObject.activeInHierarchy && !session.State.windowBroken,
                        "The actual pane must remain active while measuring transmitted contrast.");
                    material.color = Color.black;
                    yield return SettleGlazingProbe();
                    float paneDark = SceneLuminance();
                    material.color = Color.white;
                    yield return SettleGlazingProbe();
                    float paneLight = SceneLuminance();
                    yield return Save("v175-probe-" + GlazingMode(mode) + "-through-pane", false);
                    float reference = clearLight - clearDark;
                    Require(reference > .6f, "The window transparency probe must see its unobstructed background target.");
                    float ratio = (paneLight - paneDark) / reference;
                    Require(ratio > .55f && ratio < .96f,
                        "The actual pane must transmit visible background contrast with " + GlazingMode(mode) + "; measured=" + ratio);
                    transmission.Add(ratio);
                    report.Add("PASSED: actual GPU window contrast transmission " + GlazingMode(mode) + " = " + ratio.ToString("F4", CultureInfo.InvariantCulture) + ".");
                }
                Require(transmission.Max() - transmission.Min() < .07f, "Power or flashlight illumination must not turn the actual glass opaque.");
            }
            finally
            {
                glass.enabled = true;
                Destroy(marker); Destroy(material);
                captureWithoutEquipment = false;
            }
            session.State.power = session.State.quest.power = false;
            session.State.breakerOn = session.State.quest.breakerOn = false;
            player.SetVerificationFlashlight(true);
            DeadAirSettings.Grain = grain; DeadAirSettings.Flicker = flicker;
            File.WriteAllLines(Path.Combine(output, "v175-visual-runtime-report.txt"), report);
            Debug.Log("DEAD AIR V1.7.5 VISUAL RUNTIME PASSED: rendered guards, doors, fitted plaques, generator/basement diesel pickups and GPU glass transmission across all four lighting states.");
        }

        void SetGlazingLighting(int mode)
        {
            session.State.power = session.State.quest.power = mode >= 2;
            session.State.breakerOn = session.State.quest.breakerOn = mode >= 2;
            player.SetVerificationFlashlight(mode % 2 == 1);
        }

        static string GlazingMode(int mode) => (mode >= 2 ? "powered" : "unpowered") + (mode % 2 == 1 ? "-torch-on" : "-torch-off");

        IEnumerator SettleGlazingProbe()
        { for (int frame = 0; frame < 5; frame++) yield return null; }

        float SceneLuminance()
        {
            var target = frameRenderer.SceneFrame;
            Require(target != null, "Glazing verification requires the real HDR scene frame.");
            int size = 32;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            pixels.ReadPixels(new Rect((target.width - size) / 2, (target.height - size) / 2, size, size), 0, 0);
            pixels.Apply(); RenderTexture.active = previous;
            var colors = pixels.GetPixels();
            float luminance = colors.Average(c => c.r * .2126f + c.g * .7152f + c.b * .0722f);
            Destroy(pixels);
            return luminance;
        }

        int CheckPhysicalPlaques()
        {
            var signs = Resources.FindObjectsOfTypeAll<DeadAirOfficeSign>().Where(s => s.gameObject.scene.IsValid()).ToArray();
            foreach (var sign in signs)
            {
                Require(sign.FitToPlaque() && sign.IsContained(), "Runtime room text does not fit: " + sign.GetComponent<TextMesh>().text);
                var plate = sign.transform.parent.Find("Sign backing").GetComponent<MeshFilter>();
                Require(plate != null && plate.sharedMesh != null, "The physical plaque mesh must survive batching for independent verification.");
                var board = plate.sharedMesh.bounds;
                var ink = sign.GetComponent<MeshRenderer>().localBounds;
                foreach (float x in new[] { ink.min.x, ink.max.x })
                    foreach (float y in new[] { ink.min.y, ink.max.y })
                    {
                        var point = plate.transform.InverseTransformPoint(sign.transform.TransformPoint(new Vector3(x, y, ink.center.z)));
                        Require(point.x >= board.min.x + .015f && point.x <= board.max.x - .015f && point.y >= board.min.y + .01f && point.y <= board.max.y - .01f,
                            "Actual glyphs protrude outside the physical plaque: " + sign.GetComponent<TextMesh>().text);
                    }
            }
            Require(signs.Length > 50, "Inspect the complete runtime office's plaques.");
            return signs.Length;
        }
    }
}
