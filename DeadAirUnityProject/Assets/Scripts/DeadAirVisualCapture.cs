using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadAir
{
    // Opt-in build verification: -deadAirCapture <absolute output folder>.
    // Uses real rendered frames (including the game UI and post processing).
    public sealed class DeadAirVisualCapture : MonoBehaviour
    {
        DeadAirSession session;
        DeadAirPlayer player;
        DeadAirUI ui;
        string output;
        Camera captureUI;
        Canvas captureCanvas;
        RenderTexture captureFrame;
        DeadAirFrameRenderer frameRenderer;

        public void Initialize(DeadAirSession game, DeadAirPlayer actor)
        {
            session = game; player = actor;
            ui = FindFirstObjectByType<DeadAirUI>();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-deadAirCapture") output = args[i + 1];
            if (string.IsNullOrEmpty(output)) { enabled = false; return; }
            if (!Path.IsPathRooted(output)) throw new ArgumentException("-deadAirCapture requires an absolute output folder.");
            Directory.CreateDirectory(output);
            if(Application.isBatchMode)InitializeOffscreenCapture();
            StartCoroutine(Capture());
        }

        void InitializeOffscreenCapture()
        {
            // A hidden Windows batch player has no presented backbuffer. Render
            // the same game camera and UI explicitly into a real GPU target.
            captureFrame=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            captureFrame.Create();
            captureCanvas=GameObject.Find("BUREAU-9 interface").GetComponent<Canvas>();
            foreach(var item in captureCanvas.GetComponentsInChildren<Transform>(true))item.gameObject.layer=5;
            captureUI=new GameObject("Capture UI camera").AddComponent<Camera>();
            captureUI.enabled=false;captureUI.orthographic=true;captureUI.cullingMask=1<<5;
            captureUI.clearFlags=CameraClearFlags.Depth;captureUI.nearClipPlane=.1f;captureUI.farClipPlane=10;
            captureUI.targetTexture=captureFrame;
            captureCanvas.renderMode=RenderMode.ScreenSpaceCamera;captureCanvas.worldCamera=captureUI;captureCanvas.planeDistance=1;
            player.ViewCamera.enabled=false;player.ViewCamera.cullingMask&=~(1<<5);
            frameRenderer=player.ViewCamera.GetComponent<DeadAirFrameRenderer>();
            frameRenderer.OutputCamera.enabled=false;frameRenderer.OutputCamera.targetTexture=captureFrame;
        }

        void LateUpdate()
        {
            if(captureFrame==null)return;
            Canvas.ForceUpdateCanvases();
            frameRenderer.EnsureTargets();
            player.ViewCamera.Render();frameRenderer.OutputCamera.Render();captureUI.Render();
        }

        IEnumerator Capture()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            yield return new WaitForSecondsRealtime(2);
            var audio = FindFirstObjectByType<DeadAirAudio>();
            if (audio != null)
            {
                try { audio.ValidateInstruments(); }
                catch (Exception error) { Debug.LogException(error); Application.Quit(3); yield break; }
            }
            yield return Save("01-menu");
            OpenRequiredPanel(ui, "ShowSettings");
            yield return Save("01b-settings");
            ui.TryCloseTopModal();
            OpenRequiredPanel(ui, "ShowDossier");
            yield return Save("01c-dossier");
            ui.TryCloseTopModal();
            session.Host("Victor", "engineer", true, true);
            session.Command(new NetPacket { type = "start" });
            yield return new WaitForSecondsRealtime(.5f);
            player.SetInspectionPose(-5, 19, -.3671738f, .0035897f);
            yield return Save("02-courtyard");
            player.SetInspectionPose(0, 1, 0, 0);
            yield return Save("03-corridor");
            player.SetInspectionPose(-5, -1, .9272952f, -.1292768f);
            yield return Save("04-archive");
            player.SetInspectionPose(4, -2, -Mathf.PI * .5f, -.03f, true);
            yield return Save("05-control-room");
            ui.ToggleRadio();
            yield return Save("05b-radio");
            ui.HideRadio();
            player.SetPause(true);
            yield return Save("06-pause");
            ui.ToggleMap();
            yield return Save("07-map");
            ui.TryCloseTopModal();
            player.SetPause(false);
            player.SetInspectionPose(8.5f, -2, -Mathf.PI * .5f, -.03f);
            ui.ShowCalibration();
            yield return Save("08-calibration");
            ui.TryCloseTopModal();
            player.EndInspection();
            yield return VerifyEndScreenNavigation();
            Debug.Log("DEAD AIR VISUAL CAPTURE COMPLETE: " + output);
            Application.Quit(0);
        }

        IEnumerator VerifyEndScreenNavigation()
        {
            ui.ReturnToMenu();
            foreach(string exit in new[]{"Return to menu","Close","Escape"})
            {
                session.Host("Solo check", "engineer", true, false);
                session.Command(new NetPacket{type="start"});
                yield return null;
                if(exit=="Return to menu")
                {
                    ui.ToggleMap();Require(session.State.paused,"Solo map must pause the game");
                    ui.ShowSettings();ui.TryCloseTopModal();
                    Require(session.State.paused,"Closing settings above the map must preserve the pause");
                    ui.TryCloseTopModal();Require(!session.State.paused,"Closing the last solo modal must resume");
                }
                DefeatEveryone(session);
                yield return Until(()=>session.State?.phase=="lost","Solo defeat");
                if(exit=="Return to menu")yield return Save("09-solo-defeat");
                if(exit=="Escape")Require(ui.TryCloseTopModal(),"Escape must handle the defeat screen");
                else ClickReportButton(exit);
                yield return null;
                RequireMenu("Solo "+exit);
            }

            var peerObject=new GameObject("Capture LAN guest");
            var peer=peerObject.AddComponent<DeadAirSession>();peer.Initialize(WorldData.Load());
            session.Host("Host check","engineer",false,false);
            peer.Join("127.0.0.1",session.State.code,"Guest check","operator");
            yield return Until(()=>peer.State!=null&&session.State.players.Count==2,"Guest join");
            session.Command(new NetPacket{type="start"});
            yield return Until(()=>peer.State?.phase=="playing","Coop start");
            player.SetPause(true);Require(!session.State.paused,"Host menu must not freeze a coop session");player.SetPause(false);
            DefeatEveryone(session);
            yield return Until(()=>session.State?.phase=="lost"&&peer.State?.phase=="lost","Coop host defeat");
            yield return Save("10-coop-host-defeat");
            ClickReportButton("Return to menu");yield return null;RequireMenu("Coop host");
            yield return Until(()=>peer.State==null,"Host exit must disconnect guests");
            peer.Leave();Destroy(peerObject);

            peerObject=new GameObject("Capture LAN host");
            peer=peerObject.AddComponent<DeadAirSession>();peer.Initialize(WorldData.Load());
            peer.Host("Remote host","engineer",false,false);
            session.Join("127.0.0.1",peer.State.code,"Local guest","operator");
            yield return Until(()=>session.State!=null&&peer.State.players.Count==2,"Local guest join");
            peer.Command(new NetPacket{type="start"});
            yield return Until(()=>session.State?.phase=="playing","Guest start");
            DefeatEveryone(peer);
            yield return Until(()=>session.State?.phase=="lost","Coop guest defeat");
            yield return Save("11-coop-guest-defeat");
            Require(!ReportButton("Restart").gameObject.activeInHierarchy,"Guest must not restart the host's game");
            var fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var closedPeer=typeof(DeadAirSession).GetField("server",fields).GetValue(session);
            var lateSnapshot=new NetPacket{type="state",stateJson=JsonUtility.ToJson(session.State)};
            ClickReportButton("Return to menu");yield return null;RequireMenu("Coop guest");
            typeof(DeadAirSession).GetMethod("ClientPacket",fields).Invoke(session,new object[]{closedPeer,lateSnapshot});
            yield return null;RequireMenu("Late packet after guest exit");
            peer.Leave();Destroy(peerObject);
            yield return Save("12-returned-to-menu");
            Debug.Log("DEAD AIR MENU REGRESSION PASSED: solo button/close/Escape, host defeat, guest defeat, nested solo pause and coop pause.");
        }

        static void DefeatEveryone(DeadAirSession game)
        {
            game.State.enemy.active=true;
            foreach(var actor in game.State.players){actor.down=true;actor.sanity=0;}
        }

        IEnumerator Until(Func<bool> ready,string label)
        {
            float deadline=Time.realtimeSinceStartup+15;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(ready(),label+" timed out");
            yield return null;
        }

        UnityEngine.UI.Button ReportButton(string name)
        {
            var canvas=GameObject.Find("BUREAU-9 interface");
            foreach(var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if(button.name==name&&button.transform.parent.name=="Final report card")return button;
            throw new InvalidOperationException("Missing result-screen button: "+name);
        }

        void ClickReportButton(string name)
        {
            var button=ReportButton(name);
            Require(button.isActiveAndEnabled&&button.interactable,"Result-screen button unavailable: "+name);
            Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;
            var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(captureUI,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            Require(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),"Result-screen button blocked by another panel: "+name);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }

        void RequireMenu(string label)
        {
            Require(session.State==null&&GameObject.Find("Main menu")!=null&&Cursor.visible&&Cursor.lockState==CursorLockMode.None,label+" did not return to an interactive main menu");
        }

        static void Require(bool condition,string message)
        {
            if(condition)return;
            Debug.LogError("DEAD AIR MENU REGRESSION FAILED: "+message);Application.Quit(4);
            throw new InvalidOperationException(message);
        }

        static void OpenRequiredPanel(DeadAirUI ui, string method)
        {
            var action=typeof(DeadAirUI).GetMethod(method);
            if(action==null)throw new MissingMethodException("The capture requires a working UI panel: "+method);
            action.Invoke(ui,null);
        }

        IEnumerator Save(string name)
        {
            // A few rendered frames let UI/state listeners and realtime lights settle.
            for (int frame = 0; frame < 5; frame++) yield return null;
            float seconds = 0, slowest = 0;
            const int measuredFrames = 8;
            for (int frame = 0; frame < measuredFrames; frame++)
            {
                yield return null;
                seconds += Time.unscaledDeltaTime;
                slowest = Mathf.Max(slowest, Time.unscaledDeltaTime);
            }
            Debug.Log("DEAD AIR FRAME TIMING: " + name + " averageMs=" + (seconds * 1000 / measuredFrames).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " slowestMs=" + (slowest * 1000).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " fps=" + (measuredFrames / Mathf.Max(.0001f, seconds)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " captureCap=30");
            string path = Path.Combine(output, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            if(captureFrame!=null)
            {
                var previous=RenderTexture.active;RenderTexture.active=captureFrame;
                var pixels=new Texture2D(captureFrame.width,captureFrame.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,captureFrame.width,captureFrame.height),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());Destroy(pixels);RenderTexture.active=previous;
            }
            else ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 15;
            do { yield return null; } while (!File.Exists(path) && Time.realtimeSinceStartup < deadline);
            if (!File.Exists(path)) { Debug.LogError("Screenshot failed: " + path); Application.Quit(2); yield break; }
            yield return null;
            Debug.Log("DEAD AIR SCREENSHOT: " + path);
        }
    }
}
