using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadAir
{
    public sealed class DeadAirUI : MonoBehaviour
    {
        WorldData world; DeadAirSession session; DeadAirPlayer player;
        Font font, heading; Canvas canvas;
        GameObject menuPanel, lobbyPanel, hudPanel, pausePanel, mapPanel, calibrationPanel, endingPanel, settingsPanel, dossierPanel, joinPanel, radioPanel;
        UnityEngine.UI.Text className, classRole, classPerk, lobbyInfo, lobbyPlayers, objectiveText, statusText, messageText, interactionText, roomText, toastText, endingText, mapLocation, frequencyText;
        UnityEngine.UI.InputField nameField, addressField, codeField;
        UnityEngine.UI.Button startButton, restartButton;
        UnityEngine.UI.Image sanityBar, staminaBar;
        UnityEngine.UI.Slider frequency;
        readonly UnityEngine.UI.Image[] inventorySlotBgs = new UnityEngine.UI.Image[5];
        readonly UnityEngine.UI.Text[] inventorySlotLabels = new UnityEngine.UI.Text[5];
        readonly UnityEngine.UI.Outline[] inventorySlotOutlines = new UnityEngine.UI.Outline[5];
        readonly List<KeyValuePair<string,UnityEngine.UI.Button>> classButtons=new List<KeyValuePair<string,UnityEngine.UI.Button>>();
        string selectedClass="engineer", previousPhase, lastMessage;
        float toastUntil, nextHud;
        int lastRadioSequence;
        public string SelectedClass=>selectedClass;
        public event Action<string> ClassSelected;
        public bool HasModal=>Active(menuPanel)||Active(lobbyPanel)||Active(pausePanel)||Active(mapPanel)||Active(calibrationPanel)||Active(endingPanel)||Active(settingsPanel)||Active(dossierPanel)||Active(joinPanel);
        public bool RadioOpen=>Active(radioPanel);
        static bool Active(GameObject go)=>go!=null&&go.activeSelf;
        static Color Ink=>new Color32(232,232,215,255);
        static Color Muted=>new Color32(151,166,148,255);
        static Color Gold=>new Color32(216,196,151,255);
        static Color PanelInk=>new Color(.037f,.066f,.054f,.97f);

        public void Initialize(WorldData data,DeadAirSession game,DeadAirPlayer actor)
        {
            world=data;session=game;player=actor;
            font=Resources.Load<Font>("Fonts/IBMPlexMono-Regular")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            heading=Resources.Load<Font>("Fonts/Oswald")??font;
            if(FindFirstObjectByType<EventSystem>()==null){var events=new GameObject("Event System");events.AddComponent<EventSystem>();events.AddComponent<StandaloneInputModule>();}
            var go=new GameObject("BUREAU-9 interface");canvas=go.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            BuildMenu();BuildLobby();BuildHud();BuildRadio();BuildPause();BuildMap();BuildCalibration();BuildEnding();BuildSettings();BuildDossier();BuildJoin();
            toastText=Text(canvas.transform,"Notification","",20,500,108,920,64,TextAnchor.MiddleCenter);toastText.color=Gold;toastText.gameObject.SetActive(false);
            session.Error+=Toast;session.StateChanged+=RefreshPhase;
            SelectClass(selectedClass);RefreshPhase();
        }

        RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
        }
        UnityEngine.UI.Image Fill(Transform parent,string name,float x,float y,float w,float h,Color color,bool raycast=false)
        {var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=raycast;return image;}
        UnityEngine.UI.Text Text(Transform parent,string name,string value,int size,float x,float y,float w,float h,TextAnchor align=TextAnchor.UpperLeft,bool title=false)
        {
            var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=title?heading:font;t.fontSize=size;t.color=Ink;t.text=value;t.alignment=align;
            t.supportRichText=false;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=title?VerticalWrapMode.Overflow:VerticalWrapMode.Truncate;return t;
        }
        void Rule(Transform parent,float x,float y,float width){Fill(parent,"Divider",x,y,width,1,new Color(.75f,.8f,.68f,.22f));}
        UnityEngine.UI.Button Button(Transform parent,string name,string value,float x,float y,float w,float h,Action click,bool primary=false)
        {
            var r=Rect(parent,name,x,y,w,h);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Color.white;
            var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();var c=b.colors;c.normalColor=primary?Gold:new Color(.07f,.105f,.085f,.85f);c.highlightedColor=primary?new Color(.97f,.91f,.73f):new Color(.22f,.27f,.21f,.98f);c.pressedColor=new Color(.34f,.38f,.28f);c.selectedColor=c.normalColor;c.disabledColor=new Color(.1f,.12f,.1f,.5f);b.colors=c;
            var border=r.gameObject.AddComponent<UnityEngine.UI.Outline>();border.effectColor=new Color(.75f,.8f,.68f,.18f);border.effectDistance=new Vector2(1,-1);
            var label=Text(r,"Label",value,19,14,0,w-28,h,TextAnchor.MiddleCenter);label.color=primary?new Color(.06f,.09f,.065f):Ink;
            b.onClick.AddListener(()=>{DeadAirAudio.Play("click");click();});return b;
        }
        UnityEngine.UI.InputField Field(Transform parent,string name,string initial,float x,float y,float w,float h)
        {
            var r=Rect(parent,name,x,y,w,h);r.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.10f,.15f,.12f);
            var field=r.gameObject.AddComponent<UnityEngine.UI.InputField>();var label=Text(r,"Value",initial,22,16,0,w-32,h,TextAnchor.MiddleLeft);
            field.textComponent=label;field.text=initial;field.characterLimit=32;field.caretColor=Gold;return field;
        }
        GameObject FullScreen(string name,Color color){return Fill(canvas.transform,name,0,0,1920,1080,color,true).gameObject;}
        GameObject Modal(string name,string title,float width,float height,out Transform card)
        {
            var root=FullScreen(name,new Color(.005f,.012f,.009f,.74f));
            card=Fill(root.transform,name+" card",960-width/2,540-height/2,width,height,PanelInk,true).transform;
            Fill(card,"Header accent",0,0,width,2,Gold);
            Text(card,"Classification","BUREAU–9    /    BLACKPINE    /    004",14,38,29,width-130,26).color=Muted;
            Text(card,"Title",title,43,38,67,width-100,68,title:true);
            Button(card,"Close","×",width-77,24,44,44,()=>Close(root));
            root.SetActive(false);return root;
        }
        void Open(GameObject panel){HideRadio();panel.SetActive(true);panel.transform.SetAsLastSibling();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;ReconcilePause();}
        void Close(GameObject panel){panel.SetActive(false);DeadAirSettings.Save();ReconcilePause();}
        void ReconcilePause()
        {
            var state=session.State;
            if(!session.IsHost||state==null||state.phase!="playing")return;
            bool paused=state.players.Count==1&&HasModal;
            if(state.paused!=paused)session.Command(new NetPacket{type="pause",paused=paused});
        }
        public void ReturnToMenu(){player.SetPause(false);session.Leave();toastText.gameObject.SetActive(false);}

        void BuildMenu()
        {
            menuPanel=FullScreen("Main menu",Color.clear);
            var gradient=new Texture2D(128,2,TextureFormat.RGBA32,false);gradient.wrapMode=TextureWrapMode.Clamp;
            for(int x=0;x<128;x++)for(int y=0;y<2;y++){float u=x/127f;gradient.SetPixel(x,y,new Color(.025f,.04f,.035f,Mathf.Lerp(.95f,.05f,Mathf.SmoothStep(0,1,u))));}gradient.Apply();
            var veil=Rect(menuPanel.transform,"Scene gradient",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.RawImage>();veil.texture=gradient;veil.raycastTarget=false;
            var p=menuPanel.transform;
            Text(p,"Brand","▣  BUREAU–9    /    FIELD OPERATIONS",17,65,47,790,30).color=Gold;
            Text(p,"Case","АРХІВНА СПРАВА 004   ·   23.10.1996",16,70,254,750,32).color=Muted;
            Text(p,"Title","DEAD AIR",132,65,290,790,175,title:true);
            Text(p,"Subtitle","B L A C K P I N E   B R O A D C A S T",18,73,463,740,35).color=Gold;
            Text(p,"Story","Останній ефір не закінчився.\nЗнайдіть записи. Відновіть сигнал.\nПоверніться до фургона разом.",20,73,523,640,100).color=Muted;
            Text(p,"Name label","ІМ’Я ОПЕРАТОРА",14,75,649,520,25).color=Muted;
            nameField=Field(p,"Operator name",PlayerPrefs.GetString("DeadAir.PlayerName","Оператор"),73,680,490,48);
            Button(p,"Solo","ОДИНОЧНА ОПЕРАЦІЯ  →",73,747,490,58,()=>StartSolo(false),true);
            Button(p,"Host","СТВОРИТИ КООП СЕАНС",73,817,490,52,()=>{SaveName();session.Host(nameField.text,selectedClass,false,false);});
            Button(p,"Join","ПРИЄДНАТИСЯ",73,881,238,50,()=>Open(joinPanel));
            Button(p,"Explore","ДОСЛІДЖЕННЯ",325,881,238,50,()=>StartSolo(true));
            Button(p,"Settings","НАЛАШТУВАННЯ",1305,42,253,45,ShowSettings);
            Button(p,"Dossier","ДОСЬЄ",1572,42,190,45,ShowDossier);
            Text(p,"Operative label","ОБРАНИЙ ОПЕРАТОР",14,1360,709,500,24).color=Muted;
            className=Text(p,"Operative name","",57,1356,740,500,75,title:true);
            classRole=Text(p,"Operative role","",18,1360,818,500,32);classRole.color=Muted;
            ClassPicker(p,1352,867,480);
            classPerk=Text(p,"Operative perk","",17,1360,930,490,65);classPerk.color=Muted;
            Text(p,"Footer","АНАЛОГОВИЙ АРХІВ   /   ЗАПИС 02:17   /   РЕКОМЕНДОВАНО НАВУШНИКИ",13,72,1022,1310,32).color=Muted;
            Text(p,"Version","BLACKPINE  ·  1996",13,1500,1022,345,32,TextAnchor.UpperRight).color=Muted;
        }
        void ClassPicker(Transform p,float x,float y,float w)
        {
            string[] ids={"engineer","operator","journalist","dispatcher"};
            for(int i=0;i<ids.Length;i++){string id=ids[i];var b=Button(p,"Choose "+id,world.GetClass(id).name,x+i*w/4,y,w/4-5,46,()=>SelectClass(id));classButtons.Add(new KeyValuePair<string,UnityEngine.UI.Button>(id,b));}
        }
        void SaveName(){PlayerPrefs.SetString("DeadAir.PlayerName",nameField.text);PlayerPrefs.Save();}
        void StartSolo(bool creative){SaveName();session.Host(nameField.text,selectedClass,true,creative);session.Command(new NetPacket{type="start"});}
        void SelectClass(string id)
        {
            selectedClass=id;var cls=world.GetClass(id);className.text=cls.name;classRole.text=cls.title;classPerk.text=cls.perk;
            foreach(var pair in classButtons){var colors=pair.Value.colors;colors.normalColor=pair.Key==id?new Color(.29f,.32f,.23f,.9f):new Color(.07f,.105f,.085f,.65f);colors.selectedColor=colors.normalColor;pair.Value.colors=colors;}
            if(session.IsConnected&&session.State.phase=="lobby")session.Command(new NetPacket{type="class",classId=id});
            ClassSelected?.Invoke(id);
        }
        void BuildLobby()
        {
            lobbyPanel=FullScreen("Cooperative lobby",new Color(.018f,.035f,.028f,.4f));var p=lobbyPanel.transform;
            Text(p,"Lobby badge","BUREAU–9   /   ПІДГОТОВКА ОПЕРАЦІЇ",25,65,52,1000,48);
            lobbyInfo=Text(p,"Room code","",25,1120,54,730,90,TextAnchor.UpperRight);
            lobbyPlayers=Text(p,"Team","",23,76,224,520,400);
            Fill(p,"Lobby footer",0,820,1920,260,new Color(.02f,.04f,.03f,.93f));
            Text(p,"Class title","ОБЕРІТЬ ОПЕРАТОРА",15,75,861,750,32).color=Muted;ClassPicker(p,72,914,780);
            Text(p,"Briefing","Зберіть три касети та два запобіжники.\nВідновіть ефір і поверніться до фургона.",19,930,900,460,96).color=Muted;
            startButton=Button(p,"Start operation","ПОЧАТИ ЗАПИС  →",1450,897,390,62,()=>session.Command(new NetPacket{type="start"}),true);
            Button(p,"Leave lobby","НАЗАД",1450,972,390,49,()=>session.Leave());
            lobbyPanel.SetActive(false);
        }
        void BuildJoin()
        {
            joinPanel=Modal("Join session","ПРИЄДНАТИСЯ ДО СЕАНСУ",800,610,out var p);
            Text(p,"Network hint","Введіть адресу комп’ютера хоста і код його кімнати.\nГравці мають бути в одній локальній мережі або VPN.",19,38,154,716,94).color=Muted;
            Text(p,"Address label","IP АДРЕСА ХОСТА",15,40,265,700,30).color=Gold;
            addressField=Field(p,"Host address",PlayerPrefs.GetString("DeadAir.HostAddress","127.0.0.1"),38,303,724,55);
            Text(p,"Code label","КОД КІМНАТИ",15,40,385,700,30).color=Gold;
            codeField=Field(p,"Room code","",38,423,724,55);codeField.characterLimit=5;
            Button(p,"Connect","УСТАНОВИТИ ЗВ’ЯЗОК",38,515,724,58,()=>{SaveName();PlayerPrefs.SetString("DeadAir.HostAddress",addressField.text);session.Join(addressField.text.Trim(),codeField.text.Trim().ToUpperInvariant(),nameField.text,selectedClass);},true);
        }
        void BuildHud()
        {
            hudPanel=FullScreen("HUD",Color.clear);hudPanel.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;var p=hudPanel.transform;
            Text(p,"Objective caption","ПОТОЧНЕ ЗАВДАННЯ",14,46,38,660,30).color=Muted;
            objectiveText=Text(p,"Objective","",20,46,81,670,104);
            roomText=Text(p,"Location","",18,1275,39,600,100,TextAnchor.UpperRight);
            statusText=Text(p,"Status","",17,46,845,620,55);
            Text(p,"Inventory caption","ІНВЕНТАР   [ 1–5 ОБРАТИ  /  G ВИКИНУТИ ]",12,48,908,580,20).color=Gold;
            for(int i=0;i<5;i++)
            {
                float slotX=48+i*116;
                var bg=Fill(p,"Slot "+i,slotX,932,110,48,new Color(.05f,.09f,.07f,.85f));
                var outline=bg.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor=new Color(.75f,.8f,.68f,.22f);
                outline.effectDistance=new Vector2(1,-1);
                var num=Text(bg.transform,"Num",(i+1).ToString(),11,6,2,20,16);num.color=Muted;
                var item=Text(bg.transform,"Item","—",14,4,18,102,26,TextAnchor.MiddleCenter);
                inventorySlotBgs[i]=bg;
                inventorySlotOutlines[i]=outline;
                inventorySlotLabels[i]=item;
            }
            Text(p,"Sanity caption","РОЗСУДОК",12,48,1005,145,20).color=Muted;
            Fill(p,"Sanity track",48,1030,150,3,new Color(.3f,.4f,.3f,.4f));sanityBar=Fill(p,"Sanity",48,1030,150,3,new Color(.54f,.75f,.52f));
            Text(p,"Stamina caption","ВИТРИВАЛІСТЬ",12,240,1005,190,20).color=Muted;
            Fill(p,"Stamina track",240,1030,150,3,new Color(.3f,.4f,.3f,.4f));staminaBar=Fill(p,"Stamina",240,1040,150,3,Gold);
            messageText=Text(p,"Transmission","",20,530,865,860,60,TextAnchor.MiddleCenter);
            interactionText=Text(p,"Interaction","",21,480,785,960,65,TextAnchor.MiddleCenter);interactionText.color=Gold;
            Fill(p,"Reticle",958,538,4,4,new Color(.92f,.92f,.8f,.7f));
            Text(p,"Equipment","WASD РУХ   SHIFT БІГ   E ДІЯ   1-5 СЛОТ   G ВИКИНУТИ   F ЛІХТАР   V КАМЕРА   Q РАЦІЯ   M МАПА   ESC ПАУЗА",13,620,1020,1250,30,TextAnchor.MiddleRight).color=Muted;
            hudPanel.SetActive(false);
        }
        void BuildPause()
        {
            pausePanel=Modal("Pause","ПАУЗА ЕФІРУ",720,650,out var p);
            Text(p,"Pause note","Сеанс збережено. Продовжіть запис, коли будете готові.",18,38,148,644,67).color=Muted;
            Button(p,"Resume","ПРОДОВЖИТИ",38,237,644,63,()=>player.SetPause(false),true);
            Button(p,"Map","ПЛАН СТАНЦІЇ",38,318,644,59,ToggleMap);
            Button(p,"Options","НАЛАШТУВАННЯ",38,395,644,59,ShowSettings);
            Button(p,"Help","ДОСЬЄ ТА КЕРУВАННЯ",38,472,644,59,ShowDossier);
            Button(p,"Leave","ПОВЕРНУТИСЯ ДО МЕНЮ",38,549,644,59,ReturnToMenu);
            // Closing the pause card must resume the actual simulation as well.
            var close=p.Find("Close").GetComponent<UnityEngine.UI.Button>();close.onClick.RemoveAllListeners();close.onClick.AddListener(()=>player.SetPause(false));
        }
        void BuildRadio()
        {
            radioPanel=Fill(hudPanel.transform,"Quick radio callouts",390,885,1140,106,PanelInk).gameObject;
            Text(radioPanel.transform,"Radio caption","РАДІОЗВ’ЯЗОК   /   ОБЕРІТЬ 1–4   /   Q АБО ESC — ЗАКРИТИ",13,16,10,1108,26).color=Muted;
            string[] phrases={"1 · До мене","2 · Він поруч","3 · Касета тут","4 · Допоможіть"};
            for(int i=0;i<phrases.Length;i++)
            {
                int phrase=i;
                var button=Button(radioPanel.transform,"Radio phrase "+i,phrases[i],16+i*278,45,268,45,()=>player.SendRadioPhrase(phrase));
                button.GetComponentInChildren<UnityEngine.UI.Text>().fontSize=18;
                var navigation=button.navigation;navigation.mode=UnityEngine.UI.Navigation.Mode.None;button.navigation=navigation;
            }
            radioPanel.SetActive(false);
        }
        void BuildMap()
        {
            mapPanel=Modal("Station plan","ПЛАН BLACKPINE",1170,970,out var p);
            Text(p,"Map note","ВХІД ЗГОРИ / ТЕХНІЧНІ СЕКТОРИ ВНИЗУ СХЕМИ",14,40,143,1050,35).color=Muted;
            Fill(p,"Yard",70,189,1030,115,new Color(.11f,.16f,.11f));Text(p,"Yard label","ПОДВІР’Я   /   ФУРГОН BUREAU–9   /   ЕВАКУАЦІЯ",20,90,229,990,42,TextAnchor.MiddleCenter);
            Fill(p,"Main corridor",533,322,102,476,new Color(.13f,.16f,.115f));
            Text(p,"Corridor label","К\nО\nР\nИ\nД\nО\nР",20,553,385,64,390,TextAnchor.UpperCenter).color=Muted;
            string[] names={"АРХІВ","АПАРАТНА","ГЕНЕРАТОРНА","СТУДІЯ 04","ВЕНТВУЗОЛ","СЕРВЕРНА","ТЕХСКЛАД","ДИСПЕТЧЕРСЬКА"};
            string[] hints={"КАСЕТА 01 · КАРТКА","ПУЛЬТ ПЕРЕДАЧІ","ЖИВЛЕННЯ 380 В","КАСЕТА 02","ЗАПОБІЖНИК · ВЕНТИЛЬ","ЗАПОБІЖНИК","КАСЕТА 03","КОНТРОЛЬ СИГНАЛУ"};
            for(int i=0;i<8;i++)
            {
                float x=i%2==0?70:657,y=322+i/2*124;
                Fill(p,"Room "+i,x,y,441,104,new Color(.10f,.145f,.115f));
                Text(p,"Room name",names[i],24,x+19,y+20,407,37,title:true);
                Text(p,"Room purpose",hints[i],14,x+19,y+66,407,25).color=Muted;
            }
            mapLocation=Text(p,"Current room","",18,72,845,1028,46,TextAnchor.MiddleCenter);mapLocation.color=Gold;
            Button(p,"Close map","ПОВЕРНУТИСЯ ДО ЗАПИСУ",300,905,570,42,()=>Close(mapPanel));
        }
        void BuildCalibration()
        {
            calibrationPanel=Modal("Frequency calibration","НАЛАШТУВАННЯ НЕСУЧОЇ",840,640,out var p);
            Text(p,"Frequency hint","У журналі Артура Коула: 96.4 МГц.\nЗнайдіть частоту й зафіксуйте сигнал.",19,38,151,764,77).color=Muted;
            frequencyText=Text(p,"Frequency","94.2 МГц",70,40,255,760,98,TextAnchor.MiddleCenter,true);frequencyText.color=Gold;
            frequency=Slider(p,"Frequency dial",40,397,760,87.5f,108,94.2f,v=>{frequencyText.text=v.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" МГц";});
            Text(p,"Band labels","87.5                                                     108.0",15,40,438,760,30).color=Muted;
            Button(p,"Tune down","− 0.1",40,483,177,53,()=>frequency.value=Mathf.Round((frequency.value-.1f)*10)/10);
            Button(p,"Tune up","+ 0.1",232,483,177,53,()=>frequency.value=Mathf.Round((frequency.value+.1f)*10)/10);
            Button(p,"Confirm frequency","ЗАФІКСУВАТИ СИГНАЛ",424,483,376,53,()=>{
                if(Mathf.Abs(frequency.value-96.4f)>.16f){Toast("Сигнал нестійкий. Спробуйте 96.4 МГц.");DeadAirAudio.Play("error");return;}
                session.Command(new NetPacket{type="calibrate",frequency=frequency.value});if(session.State!=null&&session.State.calibrated)Close(calibrationPanel);
            },true);
            Text(p,"Calibration footer","VHF  /  АНАЛОГОВИЙ ПЕРЕДАВАЧ  /  КАНАЛ 04",13,40,581,760,27,TextAnchor.MiddleCenter).color=Muted;
        }
        UnityEngine.UI.Slider Slider(Transform parent,string name,float x,float y,float width,float min,float max,float initial,Action<float> changed)
        {
            var r=Rect(parent,name,x,y,width,36);var slider=r.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.minValue=min;slider.maxValue=max;
            Fill(r,"Track",0,15,width,5,new Color(.19f,.24f,.18f),true);
            var fillArea=Rect(r,"Fill area",0,15,width,5);
            var fill=Fill(fillArea,"Fill",0,0,0,0,Gold);
            fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;
            fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;slider.fillRect=fill.rectTransform;
            var handleArea=Rect(r,"Handle area",6,6,width-12,25);
            var handle=Fill(handleArea,"Handle",0,0,12,0,Ink,true);
            handle.rectTransform.pivot=new Vector2(.5f,.5f);handle.rectTransform.anchorMin=Vector2.zero;handle.rectTransform.anchorMax=Vector2.one;
            handle.rectTransform.anchoredPosition=Vector2.zero;
            slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.value=initial;slider.onValueChanged.AddListener(v=>changed(v));return slider;
        }
        void BuildSettings()
        {
            settingsPanel=Modal("Settings","НАЛАШТУВАННЯ",960,870,out var p);
            SettingSlider(p,"ЗАГАЛЬНА ГУЧНІСТЬ",165,0,1,DeadAirSettings.Volume,v=>DeadAirSettings.Volume=v);
            SettingSlider(p,"АТМОСФЕРА",265,0,1,DeadAirSettings.AmbienceVolume,v=>DeadAirSettings.AmbienceVolume=v);
            SettingSlider(p,"ЗВУКОВІ ЕФЕКТИ",365,0,1,DeadAirSettings.EffectsVolume,v=>DeadAirSettings.EffectsVolume=v);
            SettingSlider(p,"ЧУТЛИВІСТЬ МИШІ",465,.3f,2,DeadAirSettings.Sensitivity,v=>DeadAirSettings.Sensitivity=v,true);
            Text(p,"VHS label","ЗЕРНО ТА СКАНЛАЙНИ",17,40,586,500,39);Toggle(p,"Grain",610,576,()=>DeadAirSettings.Grain,v=>DeadAirSettings.Grain=v);
            Text(p,"Flicker label","МЕРЕХТІННЯ ТА СПАЛАХИ",17,40,649,535,39);Toggle(p,"Flicker",610,639,()=>DeadAirSettings.Flicker,v=>DeadAirSettings.Flicker=v);
            Text(p,"Quality label","РОЗДІЛЬНІСТЬ ЗАПИСУ",17,40,715,450,43);
            int[] qualities={360,540,720};for(int i=0;i<3;i++){int q=qualities[i];Button(p,"Quality "+q,q+"p",466+i*154,705,140,46,()=>{DeadAirSettings.Quality=q;Toast("Роздільність запису: "+q+"p");});}
            Button(p,"Save settings","ЗБЕРЕГТИ Й ПОВЕРНУТИСЯ",40,792,880,48,()=>Close(settingsPanel),true);
        }
        void SettingSlider(Transform p,string label,float y,float min,float max,float initial,Action<float> setter,bool multiplier=false)
        {
            Text(p,label,label,17,40,y,725,35);
            var value=Text(p,label+" value",multiplier?initial.ToString("F1")+"×":Mathf.RoundToInt(initial*100)+"%",18,750,y,169,35,TextAnchor.UpperRight);value.color=Gold;
            Slider(p,label+" slider",40,y+37,880,min,max,initial,v=>{setter(v);value.text=multiplier?v.ToString("F1")+"×":Mathf.RoundToInt(v*100)+"%";});
        }
        void Toggle(Transform p,string name,float x,float y,Func<bool> get,Action<bool> set)
        {
            UnityEngine.UI.Button button=null;button=Button(p,name,get()?"УВІМКНЕНО":"ВИМКНЕНО",x,y,310,45,()=>{set(!get());button.GetComponentInChildren<UnityEngine.UI.Text>().text=get()?"УВІМКНЕНО":"ВИМКНЕНО";});
        }
        void BuildDossier()
        {
            dossierPanel=Modal("Case file","ДОСЬЄ 004 / BLACKPINE",1000,875,out var p);
            Text(p,"Dossier intro","23 ЖОВТНЯ 1996   /   ОСТАННІЙ СИГНАЛ: 02:17",16,40,151,920,35).color=Gold;
            Text(p,"Dossier body","Телецентр продовжує передавати ефір після евакуації персоналу. Група BUREAU–9 має вилучити три касети Артура Коула, знайти два запобіжники та відновити живлення станції.\n\nНалаштуйте передавач на 96.4 МГц, запустіть трансляцію і поверніться до фургона. Тримайтеся разом. Диктора можна стримати ліхтарем і камерою; шафи дають тимчасовий прихисток.",22,40,216,920,294);
            Rule(p,40,536,920);Text(p,"Controls title","ПОЛЬОВИЙ ПОСІБНИК",24,40,563,920,44,title:true);
            Text(p,"Controls left","W A S D     Рух\nМИША        Огляд\nSHIFT       Біг\nE           Взаємодія\nM           План станції",19,40,628,456,159).color=Muted;
            Text(p,"Controls right","F           Ліхтар\nV           Камера / спалах\nQ / 1–4     Радіозв’язок\nESC         Пауза / назад\nШАФИ        Сховатися від Диктора",19,521,628,439,159).color=Muted;
            Button(p,"Close dossier","ЗРОЗУМІЛО",40,800,920,44,()=>Close(dossierPanel),true);
        }
        void BuildEnding()
        {
            endingPanel=Modal("Final report","ЗВІТ ПРО ОПЕРАЦІЮ",900,640,out var p);
            endingText=Text(p,"Outcome","",27,40,170,820,233,TextAnchor.MiddleCenter);
            restartButton=Button(p,"Restart","ПОВТОРИТИ ОПЕРАЦІЮ",40,448,820,66,()=>session.Command(new NetPacket{type="restart"}),true);
            Button(p,"Return to menu","ПОВЕРНУТИСЯ ДО МЕНЮ  /  ESC",40,538,820,58,ReturnToMenu);
            // A completed operation cannot resume. Every exit from its report
            // must leave the session rather than hide the only available menu.
            var close=p.Find("Close").GetComponent<UnityEngine.UI.Button>();
            close.onClick.RemoveAllListeners();close.onClick.AddListener(ReturnToMenu);
        }
        void RefreshPhase()
        {
            var state=session.State;string phase=state?.phase;
            if(phase!=previousPhase){previousPhase=phase;HideRadio();foreach(var panel in new[]{pausePanel,mapPanel,calibrationPanel,settingsPanel,dossierPanel,joinPanel})panel.SetActive(false);}
            menuPanel.SetActive(state==null);lobbyPanel.SetActive(phase=="lobby");hudPanel.SetActive(phase=="playing");endingPanel.SetActive(phase=="won"||phase=="lost");
            if(state==null)return;
            if(phase=="lobby")
            {
                joinPanel.SetActive(false);lobbyInfo.text="КОД СЕАНСУ   "+state.code+"\n"+state.players.Count+" / 4 ОПЕРАТОРИ";
                string names="ОПЕРАТИВНА ГРУПА\n\n";foreach(var p in state.players)names+=(p.id==state.hostId?"◆ ":"◇ ")+p.name+"\n"+world.GetClass(p.characterClass).role+"\n\n";lobbyPlayers.text=names;
                startButton.interactable=state.hostId==session.LocalId;
                startButton.GetComponentInChildren<UnityEngine.UI.Text>().text=state.hostId==session.LocalId?"ПОЧАТИ ЗАПИС  →":"ОЧІКУЄМО ХОСТА";
            }
            if(phase=="won"||phase=="lost")
            {
                toastText.gameObject.SetActive(false);
                endingText.text=(phase=="won"?"ЗАПИС ВРЯТОВАНО":"СИГНАЛ УТРАЧЕНО")+"\n\n"+state.message;
                restartButton.gameObject.SetActive(state.hostId==session.LocalId);
                endingPanel.transform.SetAsLastSibling();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            }
            if(state.calibrated&&Active(calibrationPanel))Close(calibrationPanel);
            if(phase=="playing"&&(state.message!=lastMessage||state.radioSequence!=lastRadioSequence))Toast(state.message);
            lastMessage=state.message;lastRadioSequence=state.radioSequence;
        }
        void Update()
        {
            if(toastText==null)return;if(Time.unscaledTime>=toastUntil)toastText.gameObject.SetActive(false);
            if(Time.unscaledTime<nextHud||session.State==null||session.State.phase!="playing")return;nextHud=Time.unscaledTime+.1f;
            var state=session.State;var me=state.Player(session.LocalId);if(me==null)return;
            string room=world.RoomAt(me.x,me.z);roomText.text=(player.CameraUp?"● REC  /  VHS–C":"● REC  /  BUREAU–9")+"\n"+room.ToUpperInvariant();
            statusText.text=world.GetClass(me.characterClass).name+"  /  "+Mathf.RoundToInt(me.sanity)+"%\nКАСЕТИ "+state.tapes.Count+"/3   ЗАПОБІЖНИКИ "+state.fuses.Count+"/2   КАРТКИ "+state.keycards.Count+"/2";
            sanityBar.rectTransform.sizeDelta=new Vector2(150*Mathf.Clamp01(me.sanity/100),3);staminaBar.rectTransform.sizeDelta=new Vector2(150*Mathf.Clamp01(player.Stamina/100),3);
            objectiveText.text=state.broadcast?"ПОВЕРНІТЬСЯ ДО ФУРГОНА\nЕфір активний. Час залишити станцію.":state.power?"ВІДНОВІТЬ ТРАНСЛЯЦІЮ\nАпаратна · частота 96.4 МГц.":state.fuses.Count>=2?"УВІМКНІТЬ ЖИВЛЕННЯ\nЩит 380 В у генераторній.":"ЗБЕРІТЬ ДОКАЗИ\n3 касети Артура Коула · 2 запобіжники.";
            messageText.text=me.down?"ВАС ЗБИТО. ЧЕКАЙТЕ НА ДОПОМОГУ.":!string.IsNullOrEmpty(me.hiding)?"ВИ У СХОВАНЦІ  /  E — ВИЙТИ":"";
            interactionText.text=string.IsNullOrEmpty(player.InteractionId)?"":"[ E ]  "+player.InteractionLabel;
            mapLocation.text="ВИ ЗАРАЗ: "+room.ToUpperInvariant()+"    /    КАСЕТИ "+state.tapes.Count+"/3    /    ЗАПОБІЖНИКИ "+state.fuses.Count+"/2";
            for(int i=0;i<5;i++)
            {
                bool isSelected=me.selectedSlot==i;
                bool hasItem=me.inventory!=null&&i<me.inventory.Count;
                string itemId=hasItem?me.inventory[i]:null;
                inventorySlotLabels[i].text=hasItem?FormatItemName(itemId):"—";
                inventorySlotLabels[i].color=isSelected?Gold:hasItem?Ink:Muted;
                inventorySlotBgs[i].color=isSelected?new Color(.18f,.24f,.15f,.95f):new Color(.05f,.09f,.07f,.75f);
                inventorySlotOutlines[i].effectColor=isSelected?Gold:new Color(.75f,.8f,.68f,.22f);
                inventorySlotOutlines[i].effectDistance=isSelected?new Vector2(2,-2):new Vector2(1,-1);
            }
        }
        static string FormatItemName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "—";
            if (itemId == "flashlight") return "Ліхтар";
            if (itemId == "fuel-canister") return "Каністра";
            if (itemId == "keycard-guard") return "Картка Охр.";
            if (itemId == "keycard-engineer") return "Картка Інж.";
            if (itemId.StartsWith("keycard-")) return "Картка";
            if (itemId == "tape-1") return "Касета #1";
            if (itemId == "tape-2") return "Касета #2";
            if (itemId == "tape-3") return "Касета #3";
            if (itemId.StartsWith("tape-")) return "Касета";
            if (itemId == "fuse-1") return "Запобіжн. 1";
            if (itemId == "fuse-2") return "Запобіжн. 2";
            if (itemId.StartsWith("fuse-")) return "Запобіжн.";
            return itemId;
        }
        public bool TryCloseTopModal()
        {if(RadioOpen){HideRadio();return true;}foreach(var p in new[]{settingsPanel,dossierPanel,joinPanel,calibrationPanel,mapPanel})if(Active(p)){Close(p);return true;}if(Active(endingPanel)){ReturnToMenu();return true;}return false;}
        public void ToggleRadio(){if(RadioOpen)HideRadio();else if(session.State?.phase=="playing"&&!HasModal)radioPanel.SetActive(true);}
        public void HideRadio(){if(radioPanel!=null)radioPanel.SetActive(false);}
        public void ShowPause(bool shown){if(shown)Open(pausePanel);else Close(pausePanel);}
        public void ToggleMap(){if(Active(mapPanel))Close(mapPanel);else if(session.State?.phase=="playing")Open(mapPanel);}
        public void ShowCalibration(){Open(calibrationPanel);}
        public void ShowSettings(){Open(settingsPanel);}
        public void ShowDossier(){Open(dossierPanel);}
        public void Toast(string message){if(toastText==null)return;toastText.text=message;toastText.gameObject.SetActive(true);toastText.transform.SetAsLastSibling();toastUntil=Time.unscaledTime+5;}
        void OnDestroy(){if(session!=null){session.Error-=Toast;session.StateChanged-=RefreshPhase;}}
    }
}
