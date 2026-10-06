using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed class DeadAirConsole : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        public static DeadAirConsole Instance { get; private set; }

        DeadAirSession session;
        DeadAirPlayer player;
        WorldData world;

        string inputBuffer = "";
        readonly List<string> logs = new List<string>();
        readonly List<string> history = new List<string>();
        int historyIndex = -1;
        Vector2 scrollPosition;
        float lastToggleTime;
        int lastExecuteFrame = -1;

        GUIStyle windowBoxStyle, titleBarStyle, titleTextStyle, logTextStyle, promptStyle, inputStyle, btnStyle, closeBtnStyle;
        Texture2D winTex, titleTex, btnTex, closeHoverTex;
        bool focusInput;

        public void Initialize(DeadAirSession game, DeadAirPlayer actor, WorldData data)
        {
            Instance = this;
            session = game;
            player = actor;
            world = data;

            logs.Clear();
            logs.Add("Microsoft Windows [Version 10.0.19045.3803]");
            logs.Add("(c) Корпорація Майкрософт (Microsoft Corporation). Всі права захищені.");
            logs.Add("");
            logs.Add("C:\\air\\Dear-air> deadair.exe --debug-subsystem");
            logs.Add("[DEAD AIR CONSOLE ACTIVE] Натисніть ~ (тильда), ` або F1 щоб приховати.");
            logs.Add("Введіть 'help' для списку команд, 'noclip' для польоту крізь стіни.");
            logs.Add("");
        }

        public void Log(string message)
        {
            logs.Add(message);
            if (logs.Count > 250) logs.RemoveAt(0);
            scrollPosition.y = float.MaxValue;
        }

        void Update()
        {
            if (!IsOpen)
            {
                bool req = false;
                if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.F2))
                {
                    req = true;
                }
                else if (!string.IsNullOrEmpty(Input.inputString))
                {
                    foreach (char ch in Input.inputString)
                    {
                        if (ch == '`' || ch == '~' || ch == 'ё' || ch == 'Ё' || ch == '₴')
                        {
                            req = true;
                            break;
                        }
                    }
                }
                if (req)
                {
                    Toggle();
                }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.F2) || Input.GetKeyDown(KeyCode.Escape))
                {
                    Toggle();
                }
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    ExecuteInput();
                }
            }
        }

        public void Toggle()
        {
            if (Time.unscaledTime - lastToggleTime < 0.2f) return;
            lastToggleTime = Time.unscaledTime;

            IsOpen = !IsOpen;
            if (IsOpen)
            {
                focusInput = true;
                inputBuffer = "";
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                inputBuffer = "";
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnGUI()
        {
            Event e = Event.current;
            if (e != null && e.isKey && (e.type == EventType.KeyDown || e.rawType == EventType.KeyDown))
            {
                if (e.keyCode == KeyCode.BackQuote || e.keyCode == KeyCode.F1 || e.keyCode == KeyCode.F2)
                {
                    Toggle();
                    e.Use();
                    return;
                }
                if (!IsOpen && (e.character == '`' || e.character == '~' || e.character == 'ё' || e.character == 'Ё' || e.character == '₴'))
                {
                    Toggle();
                    e.Use();
                    return;
                }
                if (IsOpen && e.keyCode == KeyCode.Escape)
                {
                    Toggle();
                    e.Use();
                    return;
                }
            }

            if (!IsOpen) return;

            InitStyles();

            float winW = Mathf.Clamp(Screen.width * 0.72f, 620f, 960f);
            float winH = Mathf.Clamp(Screen.height * 0.56f, 360f, 580f);
            float winX = (Screen.width - winW) * 0.5f;
            float winY = 28f;

            // 1. Intercept keyboard commands BEFORE IMGUI controls consume events
            bool submitRequested = false;
            bool historyChanged = false;

            if (e != null && (e.type == EventType.KeyDown || e.rawType == EventType.KeyDown))
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n' || e.character == '\r')
                {
                    submitRequested = true;
                    e.Use();
                }
                else if (e.keyCode == KeyCode.UpArrow)
                {
                    if (history.Count > 0)
                    {
                        if (historyIndex < 0) historyIndex = history.Count - 1;
                        else if (historyIndex > 0) historyIndex--;
                        inputBuffer = history[historyIndex];
                        historyChanged = true;
                    }
                    e.Use();
                }
                else if (e.keyCode == KeyCode.DownArrow)
                {
                    if (history.Count > 0 && historyIndex >= 0)
                    {
                        if (historyIndex < history.Count - 1)
                        {
                            historyIndex++;
                            inputBuffer = history[historyIndex];
                        }
                        else
                        {
                            historyIndex = -1;
                            inputBuffer = "";
                        }
                        historyChanged = true;
                    }
                    e.Use();
                }
            }

            if (historyChanged)
            {
                SetCaretToEnd();
            }

            // Windows CMD Window Box
            GUI.Box(new Rect(winX, winY, winW, winH), "", windowBoxStyle);

            // Windows Title Bar
            float titleH = 28f;
            GUI.Box(new Rect(winX, winY, winW, titleH), "", titleBarStyle);
            GUI.Label(new Rect(winX + 10f, winY + 4f, winW - 120f, 20f), "C:\\Windows\\System32\\cmd.exe - Dead Air Command Prompt", titleTextStyle);

            // Title Bar Buttons [ - ] [ □ ] [ X ]
            float btnW = 32f;
            if (GUI.Button(new Rect(winX + winW - btnW * 3, winY, btnW, titleH), "_", btnStyle))
            {
                Toggle();
            }
            if (GUI.Button(new Rect(winX + winW - btnW * 2, winY, btnW, titleH), "□", btnStyle))
            {
                // Maximize / normal
            }
            if (GUI.Button(new Rect(winX + winW - btnW, winY, btnW, titleH), "✕", closeBtnStyle))
            {
                Toggle();
            }

            // Command output area
            float logAreaY = winY + titleH + 6f;
            float logAreaH = winH - titleH - 38f;
            float innerW = winW - 20f;

            GUILayout.BeginArea(new Rect(winX + 10f, logAreaY, innerW, logAreaH));
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
            foreach (var line in logs)
            {
                GUILayout.Label(line, logTextStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Prompt Line: C:\air> [input] [ ↵ ]
            float inputY = winY + winH - 28f;
            string promptStr = "C:\\air> ";
            float promptW = 65f;
            float submitBtnW = 34f;
            float fieldW = innerW - promptW - submitBtnW - 8f;

            GUI.Label(new Rect(winX + 10f, inputY, promptW, 22f), promptStr, promptStyle);

            GUI.SetNextControlName("CmdInput");
            inputBuffer = GUI.TextField(new Rect(winX + 10f + promptW, inputY, fieldW, 22f), inputBuffer, inputStyle);

            // Clickable submit button [ ↵ ]
            if (GUI.Button(new Rect(winX + 10f + promptW + fieldW + 6f, inputY, submitBtnW, 22f), "↵", btnStyle))
            {
                submitRequested = true;
            }

            // Fallback newline detection (if pasted or typed via IME)
            if (inputBuffer.Contains("\n") || inputBuffer.Contains("\r"))
            {
                inputBuffer = inputBuffer.Replace("\r", "").Replace("\n", "");
                submitRequested = true;
            }

            // Sanitize single stray toggle character on initial open
            if (inputBuffer.Length == 1 && (inputBuffer[0] == '`' || inputBuffer[0] == '~' || inputBuffer[0] == 'ё' || inputBuffer[0] == 'Ё' || inputBuffer[0] == '₴'))
            {
                inputBuffer = "";
            }

            // Retain input focus reliably
            if (focusInput || (Event.current.type == EventType.Repaint && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())))
            {
                GUI.FocusControl("CmdInput");
                focusInput = false;
            }
            else if (e != null && e.isKey && GUI.GetNameOfFocusedControl() != "CmdInput")
            {
                GUI.FocusControl("CmdInput");
            }

            if (submitRequested)
            {
                ExecuteInput();
            }
        }

        void SetCaretToEnd()
        {
            try
            {
                TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                if (te != null)
                {
                    te.text = inputBuffer;
                    te.cursorIndex = inputBuffer.Length;
                    te.selectIndex = inputBuffer.Length;
                }
            }
            catch (Exception) { }
        }

        void ExecuteInput()
        {
            if (Time.frameCount == lastExecuteFrame) return;
            lastExecuteFrame = Time.frameCount;

            string cmd = (inputBuffer ?? "").Replace("\r", "").Replace("\n", "").Trim();
            inputBuffer = "";
            focusInput = true;

            if (string.IsNullOrWhiteSpace(cmd))
            {
                Log("C:\\air> ");
                return;
            }

            if (history.Count == 0 || history[history.Count - 1] != cmd)
            {
                history.Add(cmd);
            }
            historyIndex = -1;
            Log("C:\\air> " + cmd);

            string lower = cmd.ToLowerInvariant();
            if (lower == "cls" || lower == "clear")
            {
                logs.Clear();
                return;
            }
            if (lower == "exit" || lower == "quit")
            {
                Toggle();
                return;
            }
            if (lower == "ver" || lower == "version")
            {
                Log("Microsoft Windows [Version 10.0.19045.3803]");
                Log("Dead Air Debug Subsystem (Build 2026.10)");
                return;
            }
            if (lower.StartsWith("echo "))
            {
                Log(cmd.Substring(5));
                return;
            }
            if (lower == "echo")
            {
                Log("ECHO включено.");
                return;
            }
            if (lower == "dir")
            {
                var s = session?.State;
                Log(" Том у пристрої C: не має мітки.");
                Log(" Серійний номер тому: 4A21-89DF");
                Log("");
                Log(" Вміст папки C:\\air\\station");
                Log("");
                Log("05.10.2026  22:00    <DIR>          .");
                Log("05.10.2026  22:00    <DIR>          ..");
                Log("05.10.2026  22:00    [" + (s != null && s.power ? "УВІМКНЕНО" : "ВИМКНЕНО ") + "]  diesel_generator.exe");
                Log("05.10.2026  22:00    [" + (s != null && s.breakerOn ? "УВІМКНЕНО" : "ВИМКНЕНО ") + "]  power_breaker.sys");
                Log("05.10.2026  22:00    [" + (s != null && s.ladderPlaced ? "ВСТАНОВЛ." : "У ФУРГОНІ") + "]  folding_ladder.obj");
                Log("05.10.2026  22:00    [" + (s != null && s.windowBroken ? "РОЗБИТО  " : "ЦІЛЕ     ") + "]  director_window.glass");
                Log("05.10.2026  22:00    [" + (s != null && s.directorDrawerOpen ? "ВІДКРИТО " : "ЗАЧИНЕНО ") + "]  director_drawer.dat");
                Log("05.10.2026  22:00    [" + (s != null && s.broadcast ? "В ЕФІРІ  " : "ОЧІКУЄ   ") + "]  broadcast_signal.wav");
                return;
            }
            if (lower == "help" || lower == "/?" || lower == "-h")
            {
                Log("=== КОМАНДИ WINDOWS / DEAD AIR ===");
                Log("  NOCLIP (NC)   - Режим польоту крізь стіни (WASD + Space/Ctrl + Shift)");
                Log("  TP <location> - Телепорт: director, basement, van, front, generator");
                Log("  GIVE <item>   - Видати: ladder, key-main, key-basement, all");
                Log("  POWER         - Перемкнути генератор на задньому дворі");
                Log("  BREAKER       - Перемкнути рубильник світла в підвалі");
                Log("  HEAL / GOD    - Відновити повну стаміну та розсудок");
                Log("  DIR           - Статус станції, генератора та предметів");
                Log("  CLS           - Очистити екран");
                Log("  VER           - Версія системи");
                Log("  EXIT          - Закрити консоль");
                return;
            }
            if (lower == "noclip" || lower == "nc")
            {
                if (player != null) player.ToggleNoclip();
                bool isNc = player != null && player.Noclip;
                string res = isNc ? "Noclip УВІМКНЕНО (WASD - політ, Space - вгору, Ctrl - вниз, Shift - турбо)" : "Noclip ВИМКНЕНО";
                Log(res);
                if (session != null) session.ExecuteConsole("noclip");
                return;
            }
            if (lower.StartsWith("tp ") || lower.StartsWith("teleport "))
            {
                string target = lower.Substring(lower.IndexOf(' ') + 1).Trim();
                if (target == "director") { if (player != null) player.Teleport(-12f, 4.2f, -1.5f); Log("Телепортовано в кабінет директора."); return; }
                if (target == "basement") { if (player != null) player.Teleport(0f, -4.2f, -54f); Log("Телепортовано в електрощитову підвалу."); return; }
                if (target == "van") { if (player != null) player.Teleport(-8.5f, 0f, 24f); Log("Телепортовано до фургона знімальної групи."); return; }
                if (target == "front") { if (player != null) player.Teleport(0f, 0f, 14f); Log("Телепортовано до головного входу."); return; }
                if (target == "generator") { if (player != null) player.Teleport(-11f, 0f, -65f); Log("Телепортовано до генератора на задньому дворі."); return; }
            }

            if (session != null)
            {
                string result = session.ExecuteConsole(cmd);
                if (!string.IsNullOrEmpty(result))
                {
                    Log(result);
                    return;
                }
            }

            Log("'" + cmd + "' не визначено як внутрішню чи зовнішню команду. Введіть 'help'.");
        }

        void InitStyles()
        {
            if (windowBoxStyle != null) return;

            winTex = MakeTex(1, 1, new Color(0.047f, 0.047f, 0.047f, 0.98f)); // #0C0C0C
            titleTex = MakeTex(1, 1, new Color(0.12f, 0.12f, 0.12f, 1f));     // #1F1F1F
            btnTex = MakeTex(1, 1, new Color(0.18f, 0.18f, 0.18f, 1f));
            closeHoverTex = MakeTex(1, 1, new Color(0.75f, 0.15f, 0.15f, 1f)); // Red close

            windowBoxStyle = new GUIStyle(GUI.skin.box);
            windowBoxStyle.normal.background = winTex;

            titleBarStyle = new GUIStyle(GUI.skin.box);
            titleBarStyle.normal.background = titleTex;

            titleTextStyle = new GUIStyle();
            titleTextStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            titleTextStyle.fontSize = 12;
            titleTextStyle.alignment = TextAnchor.MiddleLeft;

            logTextStyle = new GUIStyle();
            logTextStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            logTextStyle.fontSize = 13;
            logTextStyle.wordWrap = true;

            promptStyle = new GUIStyle();
            promptStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            promptStyle.fontStyle = FontStyle.Bold;
            promptStyle.fontSize = 13;
            promptStyle.alignment = TextAnchor.MiddleLeft;

            inputStyle = new GUIStyle(GUI.skin.textField);
            inputStyle.normal.background = winTex;
            inputStyle.normal.textColor = Color.white;
            inputStyle.fontSize = 13;
            inputStyle.border = new RectOffset(0, 0, 0, 0);

            btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.normal.background = titleTex;
            btnStyle.hover.background = btnTex;
            btnStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            btnStyle.fontSize = 12;
            btnStyle.alignment = TextAnchor.MiddleCenter;

            closeBtnStyle = new GUIStyle(GUI.skin.button);
            closeBtnStyle.normal.background = titleTex;
            closeBtnStyle.hover.background = closeHoverTex;
            closeBtnStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            closeBtnStyle.fontSize = 12;
            closeBtnStyle.alignment = TextAnchor.MiddleCenter;
        }

        static Texture2D MakeTex(int w, int h, Color col)
        {
            Texture2D tex = new Texture2D(w, h);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }
    }
}
