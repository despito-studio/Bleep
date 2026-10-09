using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadAir
{
    public sealed partial class DeadAirVisualCapture
    {
        readonly List<string> graphicsChecks = new List<string>();
        V176Frame menuReference;

        IEnumerator CaptureV176()
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
            yield return new WaitForSecondsRealtime(1.5f);
            Require(captureFrame != null, "Graphics verification requires the actual offscreen GPU capture (-batchmode, without -nographics).");
            if (frameRenderer == null) frameRenderer = player.ViewCamera.GetComponent<DeadAirFrameRenderer>();
            CanonicalV176Settings();
            ui.ReturnToMenu();
            yield return Save("graphics-00-menu-initial");
            CheckV176MenuIsolation();
            menuReference = ReadV176Frame(frameRenderer.SceneFrame);
            Require(V176Luminance(menuReference, new Rect(.35f,.32f,.6f,.6f)) > .01f,
                "The canonical-menu comparison must contain visible rendered architecture.");
            yield return VerifyV176SettingsUi();
            yield return CheckV176Menu("after-settings");

            yield return StartV176Game();
            InspectV176(-4.6f,-18.9f,0,0,1.05f,-22);
            captureWithoutEquipment = true;
            V176Power(false);
            yield return Save("graphics-01-archive-no-power");
            var roomSample = new Rect(.30f,.20f,.58f,.55f);
            float dark = DisplayLuminance(roomSample);
            V176Power(true);
            yield return Save("graphics-02-archive-warm-practicals");
            float lit = DisplayLuminance(roomSample);
            Require(lit > dark + .015f && lit > dark * 1.35f,
                "Restored practical lighting must visibly illuminate the room; dark=" + Number(dark) + ", powered=" + Number(lit));
            graphicsChecks.Add("PASSED: actual room GPU luminance without power=" + Number(dark) + ", powered=" + Number(lit) + "; warm practical lamps illuminate the room without the flashlight.");
            player.SetVerificationFlashlight(true);
            captureWithoutEquipment = false;
            yield return Save("graphics-03-archive-powered-with-flashlight");
            player.SetVerificationFlashlight(false);
            InspectV176(-7.1f,3.8f,0,-13,1.2f,-1);
            yield return Save("graphics-04-reception-warm-practicals-and-blue-night");
            InspectV176(-11.3f,-16.1f,4.2f,-15,5.3f,-13);
            yield return Save("graphics-05-meeting-room-physical-materials");
            captureWithoutEquipment = true;

            yield return VerifyV176RenderedEffects();
            CanonicalV176Settings(); V176Power(true);
            captureWithoutEquipment = false;
            InspectV176(-4.1f,-44.5f,0,0,2,-45);
            player.SetVerificationFlashlight(true);
            yield return Save("graphics-20-upper-stair-brick-undercroft");
            InspectV176(-4,-40.5f,4.2f,0,4.85f,-44);
            player.SetVerificationFlashlight(true);
            yield return Save("graphics-21-upper-guards-above-undercroft");
            InspectV176(-2.4f,-34.8f,-4.2f,-5.2f,-3.1f,-31.8f);
            player.SetVerificationFlashlight(true);
            yield return Save("graphics-22-basement-shelves-against-walls");
            InspectV176(-2,-35.5f,-4.2f,-6,-2.8f,-35.5f);
            player.SetVerificationFlashlight(true);
            yield return Save("graphics-23-basement-west-gateway-clear");
            graphicsChecks.Add("CAPTURED: brick walls below the upper flight with steel guards retained above it; wall-mounted basement shelf fronts and the open west gateway. Physical passage checks run separately in the saved-scene layout validator.");

            // Match states here are visual fixtures. Menu pixels are compared after the
            // real UI exit paths; this does not claim a complete gameplay victory run.
            foreach (string state in new[] { "blackout", "powered", "broadcast", "lost", "won" })
            {
                if (session.State == null) yield return StartV176Game();
                V176Power(state != "blackout");
                session.State.broadcast = session.State.quest.broadcast = state == "broadcast";
                player.SetVerificationFlashlight(state == "blackout");
                if (state == "lost")
                {
                    DefeatEveryone(session);
                    yield return Until(() => session.State?.phase == "lost", "Graphics defeat report");
                    yield return Save("graphics-report-lost");
                    player.EndInspection(); ClickReportButton("Return to menu");
                }
                else if (state == "won")
                {
                    session.State.phase = "won";
                    yield return Save("graphics-report-won");
                    player.EndInspection(); ClickReportButton("Return to menu");
                }
                else
                {
                    yield return Save("graphics-match-" + state);
                    player.EndInspection(); ui.ReturnToMenu();
                }
                yield return CheckV176Menu("after-" + state);
            }
            yield return VerifyV176SettingsRetention();
            File.WriteAllLines(Path.Combine(output,"graphics-runtime-report.txt"),
                new[] { "PASSED: v" + Application.version + " actual Windows GPU presentation and settings regression." }.Concat(graphicsChecks));
            Debug.Log("DEAD AIR GRAPHICS RUNTIME PASSED: actual rendered settings, practical lighting, floor reflections, SSAO, bloom, antialiasing, brightness and canonical menu across five match states.");
            Application.Quit(0);
        }

        void CanonicalV176Settings()
        {
            DeadAirSettings.ApplyGraphicsPreset(2); DeadAirSettings.Brightness = 1;
            DeadAirSettings.Grain = false; DeadAirSettings.Flicker = false;
        }

        IEnumerator StartV176Game()
        {
            session.Host("Graphics verifier", "engineer", true, false);
            session.Command(new NetPacket { type = "start" });
            yield return Until(() => session.State?.phase == "playing", "Graphics solo start");
            var me = session.State.Player(session.LocalId);
            me.invulnerableUntil = 999999; session.State.spawnCountdown = 999999;
            session.State.enemy.active = false;
            // This opt-in visual fixture removes the enemy, while retaining normal HUD,
            // interaction, lighting and shaders from the release executable.
            yield return new WaitForSecondsRealtime(3);
        }

        void V176Power(bool enabled)
        {
            session.State.power = session.State.quest.power = enabled;
            session.State.breakerOn = session.State.quest.breakerOn = enabled;
            session.State.broadcast = session.State.quest.broadcast = false;
            session.State.enemy.active = false;
            player.SetVerificationFlashlight(false);
        }

        void InspectV176(float x,float z,float floorY,float targetX,float targetY,float targetZ)
        {
            InspectToward(x,z,floorY,targetX,targetY,targetZ);
            // SetInspectionPose enables the torch for older inspection captures.
            // Every graphics fixture defaults to actual torch OFF after positioning.
            player.SetVerificationFlashlight(false);
            Require(!player.FlashlightOn && !player.ViewCamera.transform.Find("Flashlight").GetComponent<Light>().enabled,
                "The actual graphics fixture flashlight must be off after positioning.");
        }

        IEnumerator VerifyV176SettingsUi()
        {
            ClickUiButton("Settings","Main menu"); yield return null;
            Require(!UiButton("Solo","Main menu").IsInteractable(), "Settings must block the menu beneath the card.");
            for (int quality = 0; quality < 3; quality++)
            {
                ClickUiButton("Quality " + quality,"Graphics");
                yield return Save("graphics-settings-preset-" + quality);
                float scale = quality == 0 ? .65f : quality == 1 ? .85f : 1;
                Require(DeadAirSettings.GraphicsQuality == quality && Mathf.Abs(DeadAirSettings.RenderScale - scale) < .001f &&
                    DeadAirSettings.ShadowQuality == quality && DeadAirSettings.Reflections == (quality > 0) &&
                    DeadAirSettings.Bloom == (quality > 0) && DeadAirSettings.AmbientOcclusion == (quality > 0),
                    "An actual quality button must apply its complete render preset.");
                Require(frameRenderer.SceneFrame.height == Mathf.Max(240,Mathf.RoundToInt(Screen.height * scale)) &&
                    frameRenderer.SceneFrame.filterMode == FilterMode.Bilinear,
                    "A quality button must resize the actual scene frame with smooth sampling.");
                graphicsChecks.Add("PASSED: clicked quality " + quality + "; actual scene frame " + frameRenderer.SceneFrame.width + "x" + frameRenderer.SceneFrame.height + ", bilinear sampling.");
            }
            foreach (int quality in new[] { 0,1,2 })
            {
                ClickUiButton("Shadows " + quality,"Graphics"); yield return null;
                Require(DeadAirSettings.ShadowQuality == quality, "The actual shadow button must update its saved quality.");
            }
            foreach (string toggle in new[] { "Ambient occlusion","Bloom","Antialiasing","Reflections","Grain","Flicker" })
            {
                bool before = V176Toggle(toggle);
                ClickUiButton(toggle,"Graphics"); yield return null;
                Require(V176Toggle(toggle) != before, "The actual graphics toggle must change its setting: " + toggle);
                ClickUiButton(toggle,"Graphics"); yield return null;
                Require(V176Toggle(toggle) == before, "The graphics toggle must restore its setting: " + toggle);
            }
            SetV176Slider("Render scale slider",.8f);
            SetV176Slider("Brightness slider",1.1f);
            yield return Save("graphics-settings-custom");
            Require(Mathf.Abs(DeadAirSettings.RenderScale-.8f)<.01f && Mathf.Abs(DeadAirSettings.Brightness-1.1f)<.01f,
                "Actual pointer drags must update both graphics sliders.");
            ClickUiButton("Save settings","Settings card"); yield return null;
            ClickUiButton("Settings","Main menu"); yield return null;
            Require(Mathf.Abs(UiElement<UnityEngine.UI.Slider>("Render scale slider","Graphics").value-.8f)<.01f &&
                Mathf.Abs(UiElement<UnityEngine.UI.Slider>("Brightness slider","Graphics").value-1.1f)<.01f &&
                Mathf.Abs(PlayerPrefs.GetFloat("DeadAir.RenderScale")-.8f)<.01f && Mathf.Abs(PlayerPrefs.GetFloat("DeadAir.Brightness")-1.1f)<.01f,
                "Saved custom settings and reopened controls must retain the actual dragged values.");
            ClickUiButton("Audio tab","Settings card"); yield return Save("graphics-settings-audio-tab");
            Require(UiButton("Mute","Audio and controls").isActiveAndEnabled && !UiButton("Bloom","Graphics").isActiveAndEnabled,
                "Audio and graphics tabs must expose their own controls.");
            ClickUiButton("Graphics tab","Settings card"); yield return null;
            ClickUiButton("Quality 2","Graphics"); SetV176Slider("Brightness slider",1);
            ClickUiButton("Save settings","Settings card"); yield return null;
            graphicsChecks.Add("PASSED: pointer-hit-tested quality/shadow/toggle buttons and pointer-dragged scale/brightness sliders; PlayerPrefs.Save and reopened controls retain custom settings; both settings tabs work.");
        }

        static bool V176Toggle(string name) => name == "Ambient occlusion" ? DeadAirSettings.AmbientOcclusion :
            name == "Bloom" ? DeadAirSettings.Bloom : name == "Antialiasing" ? DeadAirSettings.Antialiasing :
            name == "Reflections" ? DeadAirSettings.Reflections : name == "Grain" ? DeadAirSettings.Grain : DeadAirSettings.Flicker;

        void SetV176Slider(string name,float value)
        {
            var slider = UiElement<UnityEngine.UI.Slider>(name,"Graphics");
            Require(slider.IsActive() && slider.IsInteractable(), "Graphics slider unavailable: " + name);
            Canvas.ForceUpdateCanvases();
            var drag = (RectTransform)slider.handleRect.parent;
            Vector3 local = new Vector3(Mathf.Lerp(drag.rect.xMin,drag.rect.xMax,Mathf.InverseLerp(slider.minValue,slider.maxValue,value)),drag.rect.center.y,0);
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(captureUI,drag.TransformPoint(local)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            Require(hits.Count > 0 && (hits[0].gameObject == slider.gameObject || hits[0].gameObject.transform.IsChildOf(slider.transform)),
                "Graphics slider pointer is blocked: " + name);
            // Slider.UpdateDrag uses pressEventCamera from pointerPressRaycast.
            // The real EventSystem sets these before dispatching a pointer press.
            pointer.pointerPressRaycast = hits[0]; pointer.pointerCurrentRaycast = hits[0];
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(slider.gameObject,pointer,ExecuteEvents.pointerUpHandler);
        }

        IEnumerator VerifyV176RenderedEffects()
        {
            var floor = new Rect(.30f,.06f,.60f,.36f);
            var architecture = new Rect(.27f,.08f,.66f,.84f);
            DeadAirSettings.Bloom = false; DeadAirSettings.Antialiasing = false;
            DeadAirSettings.AmbientOcclusion = false; DeadAirSettings.Reflections = false;
            InspectV176(-8,-13,0,-8,.1f,-16.3f);
            yield return Save("graphics-10-floor-reflections-off");
            var noReflection = ReadV176Frame(frameRenderer.SceneFrame);
            DeadAirSettings.Reflections = true;
            yield return Save("graphics-11-floor-reflections-on");
            var reflection = player.ViewCamera.GetComponent<DeadAirPlanarReflections>();
            Require(reflection != null && reflection.IsRenderingReflection && reflection.ReflectionFrame != null &&
                Shader.GetGlobalFloat("_DeadAirReflectionEnabled") > .5f,
                "Reflection settings must enable an actual mirrored GPU camera and texture.");
            var reflectedFrame = ReadV176Frame(frameRenderer.SceneFrame);
            SaveV176MirrorTexture("graphics-11a-actual-mirrored-camera",reflection.ReflectionFrame);
            float reflectionDifference = V176Difference(noReflection,reflectedFrame,floor);
            Require(reflectionDifference > .0003f, "Enabling reflections must change visible floor pixels; difference=" + Number(reflectionDifference));
            graphicsChecks.Add("PASSED: actual mirrored-camera floor reflection texture " + reflection.ReflectionFrame.width + "x" + reflection.ReflectionFrame.height + "; floor GPU mean RGB change=" + Number(reflectionDifference) + ".");
            yield return VerifyV176ReflectionPlacement();

            DeadAirSettings.Reflections = false;
            InspectV176(-4.6f,-18.9f,0,0,1.05f,-22);
            yield return Save("graphics-12-contact-occlusion-off");
            var noOcclusion = ReadV176Frame(frameRenderer.SceneFrame);
            DeadAirSettings.AmbientOcclusion = true;
            yield return Save("graphics-13-contact-occlusion-on");
            var ao = player.ViewCamera.GetComponent<DeadAirAmbientOcclusion>();
            Require(ao != null && ao.IsActive && (player.ViewCamera.depthTextureMode & DepthTextureMode.DepthNormals) != 0,
                "SSAO must consume the actual camera depth and normal buffer.");
            float aoDifference = V176Difference(noOcclusion,ReadV176Frame(frameRenderer.SceneFrame),architecture);
            Require(aoDifference > .0001f, "Enabling ambient occlusion must shade visible scene contacts; difference=" + Number(aoDifference));
            graphicsChecks.Add("PASSED: actual depth/normal SSAO changes GPU contact shading; mean RGB change=" + Number(aoDifference) + ".");
            DeadAirSettings.AmbientOcclusion = false;

            InspectV176(-8,-13,0,-8,3,-20.75f);
            yield return Save("graphics-14-practical-bloom-off");
            var noBloom = ReadV176Frame(captureFrame);
            DeadAirSettings.Bloom = true;
            yield return Save("graphics-15-practical-bloom-on");
            float bloomDifference = V176Difference(noBloom,ReadV176Frame(captureFrame),architecture);
            Require(bloomDifference > .0001f, "Enabling bloom must soften the visible HDR practical light; difference=" + Number(bloomDifference));
            graphicsChecks.Add("PASSED: actual HDR practical-light bloom changes presentation pixels; mean RGB change=" + Number(bloomDifference) + ".");
            DeadAirSettings.Bloom = false;

            InspectV176(-4.1f,-44.5f,0,0,2,-45);
            yield return Save("graphics-16-steel-edges-antialiasing-off");
            var noAa = ReadV176Frame(captureFrame);
            DeadAirSettings.Antialiasing = true;
            yield return Save("graphics-17-steel-edges-antialiasing-on");
            float aaDifference = V176Difference(noAa,ReadV176Frame(captureFrame),architecture);
            Require(aaDifference > .00001f, "Antialiasing must change actual diagonal geometry edges; difference=" + Number(aaDifference));
            graphicsChecks.Add("PASSED: actual antialiasing changes rendered steel/diagonal edges; mean RGB change=" + Number(aaDifference) + ".");

            InspectV176(-4.6f,-18.9f,0,0,1.05f,-22);
            DeadAirSettings.Brightness = .8f;
            yield return Save("graphics-18-brightness-80");
            float low = DisplayLuminance(architecture);
            DeadAirSettings.Brightness = 1.2f;
            yield return Save("graphics-19-brightness-120");
            float high = DisplayLuminance(architecture);
            Require(high > low + .01f, "Brightness must change actual output exposure; low=" + Number(low) + ", high=" + Number(high));
            graphicsChecks.Add("PASSED: brightness changes actual presentation luminance " + Number(low) + " -> " + Number(high) + ".");
            DeadAirSettings.Brightness = 1;
            foreach (int shadows in new[] { 0,1,2 })
            {
                DeadAirSettings.ShadowQuality = shadows;
                yield return SettleGlazingProbe();
                var lamps = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.name.StartsWith("lamp:zone-") && l.enabled).ToArray();
                int actual = lamps.Count(l => l.shadows != LightShadows.None);
                int budget = shadows == 2 ? 3 : shadows == 1 ? 1 : 0;
                Require(lamps.Length > 0 && actual <= budget && (shadows == 0 || actual > 0),
                    "Actual nearby practical shadow maps must honor the selected 0/1/3 budget; setting=" + shadows + ", actual=" + actual);
                if (shadows == 2) Require(lamps.Where(l => l.shadows != LightShadows.None).All(l => l.shadows == LightShadows.Soft),
                    "The high shadow setting must render soft physical shadows.");
                Require(QualitySettings.shadows == (shadows == 0 ? ShadowQuality.Disable : ShadowQuality.All),
                    "The mirror camera must restore the main camera's shadow quality after its cheaper reflection pass.");
                graphicsChecks.Add("PASSED: selected shadow quality " + shadows + "; actual active practical shadow maps=" + actual + ".");
            }
        }

        IEnumerator VerifyV176ReflectionPlacement()
        {
            // This target is above the source camera's upper frustum. Only its
            // geometrically mirrored image can enter the camera's floor region.
            // Predict its location by reflecting the WORLD POINT, independent of
            // the reflection component's view/projection matrices and shader UVs.
            InspectV176(-8,-13,0,-8,.1f,-16.3f);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Reflected-only placement GPU target";
            marker.transform.localScale = new Vector3(.7f,.45f,.025f);
            var material = new Material(Shader.Find("Dead Air/Source Unlit"));
            material.SetFloat("_FogEnabled",0); material.color = new Color(6,0,2,1);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(marker.GetComponent<Collider>());
            try
            {
                foreach (float sourceX in new[] { -8.65f,-7.35f })
                {
                    marker.transform.position = DeadAirOfficeLayout.Position(sourceX,3,-19);
                    marker.SetActive(false); DeadAirSettings.Reflections = false;
                    yield return SettleGlazingProbe();
                    var directBackground = ReadV176Frame(frameRenderer.SceneFrame);
                    DeadAirSettings.Reflections = true;
                    yield return SettleGlazingProbe();
                    var background = ReadV176Frame(frameRenderer.SceneFrame);
                    Vector3 real = player.ViewCamera.WorldToViewportPoint(marker.transform.position);
                    Vector3 image = player.ViewCamera.WorldToViewportPoint(new Vector3(marker.transform.position.x,-marker.transform.position.y,marker.transform.position.z));
                    Require(real.z > 0 && real.y > 1.025f && image.z > 0 && image.x > .15f && image.x < .85f && image.y > .12f && image.y < .65f,
                        "The independent target must be outside the source frustum and its mirrored image must fall on a visible floor region; direct=" + real + ", mirror=" + image);
                    Vector3 floorRay = new Vector3(marker.transform.position.x,-marker.transform.position.y,marker.transform.position.z)-player.ViewCamera.transform.position;
                    Require(Physics.Raycast(player.ViewCamera.transform.position,floorRay.normalized,out var hit,floorRay.magnitude,~0,QueryTriggerInteraction.Ignore) &&
                        Mathf.Abs(hit.point.y) < .12f && hit.normal.y > .8f,
                        "The predicted mirrored target position must correspond to the actual unobstructed saved floor, not an arbitrary screen region.");
                    var region = new Rect(image.x-.03f,image.y-.025f,.06f,.05f);
                    marker.SetActive(true);
                    yield return Save(sourceX < -8 ? "graphics-probe-reflected-target-left" : "graphics-probe-reflected-target-right");
                    var reflected = ReadV176Frame(frameRenderer.SceneFrame);
                    float energy = V176MarkerEnergy(reflected,region)-V176MarkerEnergy(background,region);
                    Require(energy > .02f,
                        "An object visible only to the mirror must render magenta at its independently predicted floor location; measured=" + Number(energy));
                    DeadAirSettings.Reflections = false;
                    yield return SettleGlazingProbe();
                    float disabled = V176MarkerEnergy(ReadV176Frame(frameRenderer.SceneFrame),region)-V176MarkerEnergy(directBackground,region);
                    Require(Mathf.Abs(disabled) < Mathf.Max(.003f,energy*.1f),
                        "The reflected-only marker must disappear at the predicted floor location when reflections are disabled; on=" + Number(energy) + ", off=" + Number(disabled));
                    graphicsChecks.Add("PASSED: reflected-only target at sourceX=" + Number(sourceX) + " lies above the direct camera frustum; actual magenta image at independent predicted floor viewport=" + image + ", on-energy=" + Number(energy) + ", disabled-energy=" + Number(disabled) + ".");
                }
            }
            finally { DeadAirSettings.Reflections = true; Destroy(marker); Destroy(material); }
        }

        static float V176MarkerEnergy(V176Frame frame,Rect region)
        {
            float sum = 0; int count = 0;
            for (int y = Mathf.RoundToInt(region.yMin*frame.height); y < Mathf.RoundToInt(region.yMax*frame.height); y++)
                for (int x = Mathf.RoundToInt(region.xMin*frame.width); x < Mathf.RoundToInt(region.xMax*frame.width); x++)
                { var color = frame.pixels[y*frame.width+x]; sum += color.r + color.b - 2*color.g; count++; }
            Require(count > 100, "Reflection placement requires a real multi-pixel floor region.");
            return sum/count;
        }

        void CheckV176MenuIsolation()
        {
            var backdrop = FindFirstObjectByType<DeadAirMenuBackdrop>();
            Require(backdrop != null && backdrop.IsVisible && player.ViewCamera.cullingMask == ((1 << DeadAirMenuBackdrop.PresentationLayer) | (1 << 29)),
                "The menu camera must see only its independent architecture and operative layers.");
            var tape = frameRenderer.OutputCamera.GetComponent<DeadAirTapeEffect>();
            Require(tape.Threat < .001f && tape.Insanity < .001f && tape.Nightmare < .001f && tape.Flash < .001f && tape.Trip < .001f && !tape.Recording,
                "Match threat, insanity, jump flashes, trip and recording presentation must reset outside a match.");
        }

        IEnumerator CheckV176Menu(string label)
        {
            captureWithoutEquipment = false;
            yield return Save("graphics-menu-" + label);
            RequireMenu("Canonical menu " + label); CheckV176MenuIsolation();
            var current = ReadV176Frame(frameRenderer.SceneFrame);
            // The operative has a deliberately animated idle pose on layer 29. This
            // architectural region omits the figure and its floor reflection.
            float difference = V176Difference(menuReference,current,new Rect(.35f,.32f,.6f,.6f));
            Require(difference < .008f, "Canonical menu architecture must remain identical after " + label + "; GPU mean RGB difference=" + Number(difference));
            graphicsChecks.Add("PASSED: canonical menu " + label + "; independent camera layers, cleared match presentation, background GPU mean RGB difference=" + Number(difference) + ".");
        }

        IEnumerator VerifyV176SettingsRetention()
        {
            ClickUiButton("Settings","Main menu"); yield return null;
            Require(DeadAirSettings.GraphicsQuality == 2 && Mathf.Abs(DeadAirSettings.RenderScale - 1) < .001f &&
                DeadAirSettings.ShadowQuality == 2 && DeadAirSettings.AmbientOcclusion && DeadAirSettings.Bloom &&
                DeadAirSettings.Antialiasing && DeadAirSettings.Reflections && Mathf.Abs(DeadAirSettings.Brightness-1)<.001f,
                "Selected settings must survive multiple match/menu/report transitions.");
            Require(Mathf.Abs(UiElement<UnityEngine.UI.Slider>("Render scale slider","Graphics").value-1)<.001f,
                "Reopened settings controls must show the persisted selection after match transitions.");
            yield return Save("graphics-settings-after-five-match-transitions");
            ClickUiButton("Quality 1","Graphics");
            SetV176Slider("Render scale slider",.8f); SetV176Slider("Brightness slider",1.1f);
            ClickUiButton("Bloom","Graphics"); ClickUiButton("Reflections","Graphics"); ClickUiButton("Antialiasing","Graphics");
            ClickUiButton("Save settings","Settings card");
            yield return StartV176Game();
            yield return Save("graphics-custom-settings-playing");
            Require(frameRenderer.SceneFrame.height == Mathf.Max(240,Mathf.RoundToInt(Screen.height*.8f)),
                "A custom saved render scale must survive starting a new match and reach its actual GPU frame.");
            player.EndInspection(); ui.ReturnToMenu(); yield return null;
            ClickUiButton("Settings","Main menu"); yield return null;
            Require(DeadAirSettings.GraphicsQuality == 1 && Mathf.Abs(DeadAirSettings.RenderScale-.8f)<.01f &&
                Mathf.Abs(DeadAirSettings.Brightness-1.1f)<.01f && DeadAirSettings.ShadowQuality == 1 &&
                DeadAirSettings.AmbientOcclusion && !DeadAirSettings.Bloom && !DeadAirSettings.Reflections && !DeadAirSettings.Antialiasing &&
                Mathf.Abs(UiElement<UnityEngine.UI.Slider>("Render scale slider","Graphics").value-.8f)<.01f &&
                Mathf.Abs(PlayerPrefs.GetFloat("DeadAir.Brightness")-1.1f)<.01f,
                "Nondefault custom settings and reopened controls must survive a real match/menu round trip without being reapplied.");
            yield return Save("graphics-custom-settings-retained-after-match");
            ClickUiButton("Save settings","Settings card");
            graphicsChecks.Add("PASSED: all selected graphics settings and reopened controls retain their values across blackout, power, broadcast, defeat and victory-report menu transitions. Capture exit restores the user's original preference keys.");
            graphicsChecks.Add("PASSED: separately saved nondefault custom scale/brightness, medium shadows and disabled bloom/reflections/AA survive an actual new-match/menu round trip without reapplying the settings; actual GPU scale and reopened controls verified.");
        }

        sealed class V176Frame { public int width,height; public Color[] pixels; }

        void SaveV176MirrorTexture(string name,RenderTexture target)
        {
            var source = ReadV176Frame(target);
            var pixels = new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
            try
            {
                // A display-only tone map makes the actual HDR mirrored-camera
                // texture inspectable. Pixel assertions above use the untouched HDR.
                var colors = source.pixels.Select(c => new Color(Mathf.Max(0,c.r)/(1+Mathf.Max(0,c.r)),
                    Mathf.Max(0,c.g)/(1+Mathf.Max(0,c.g)),Mathf.Max(0,c.b)/(1+Mathf.Max(0,c.b)),1).gamma).ToArray();
                pixels.SetPixels(colors); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());
            }
            finally { Destroy(pixels); }
        }

        V176Frame ReadV176Frame(RenderTexture target)
        {
            Require(target != null && target.IsCreated(), "GPU presentation frame is missing.");
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply();
                return new V176Frame { width = target.width, height = target.height, pixels = pixels.GetPixels() };
            }
            finally { RenderTexture.active = previous; Destroy(pixels); }
        }

        static float V176Difference(V176Frame a,V176Frame b,Rect area)
        {
            Require(a.width == b.width && a.height == b.height, "Compare equal-sized actual GPU frames.");
            float sum = 0; int count = 0;
            for (int y = Mathf.RoundToInt(area.yMin*a.height); y < Mathf.RoundToInt(area.yMax*a.height); y += 2)
                for (int x = Mathf.RoundToInt(area.xMin*a.width); x < Mathf.RoundToInt(area.xMax*a.width); x += 2)
                {
                    var ca = a.pixels[y*a.width+x]; var cb = b.pixels[y*b.width+x];
                    sum += (Mathf.Abs(ca.r-cb.r)+Mathf.Abs(ca.g-cb.g)+Mathf.Abs(ca.b-cb.b))/3; count++;
                }
            Require(count > 1000, "A meaningful architectural/floor image region is required.");
            return sum/count;
        }

        static float V176Luminance(V176Frame frame,Rect area)
        {
            float sum = 0; int count = 0;
            for (int y = Mathf.RoundToInt(area.yMin*frame.height); y < Mathf.RoundToInt(area.yMax*frame.height); y += 2)
                for (int x = Mathf.RoundToInt(area.xMin*frame.width); x < Mathf.RoundToInt(area.xMax*frame.width); x += 2)
                { var color = frame.pixels[y*frame.width+x]; sum += color.r*.2126f+color.g*.7152f+color.b*.0722f; count++; }
            return sum/Mathf.Max(1,count);
        }

        static string Number(float value) => value.ToString("F6",CultureInfo.InvariantCulture);
    }
}
