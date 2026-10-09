using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadAir
{
    [DefaultExecutionOrder(1500)]
    public sealed partial class DeadAirUI : MonoBehaviour
    {
        WorldData world; DeadAirSession session; DeadAirPlayer player;
        Font font, heading; Canvas canvas;
        CanvasGroup interfaceGroup;
        GameObject menuPanel, lobbyPanel, hudPanel, pausePanel, mapPanel, calibrationPanel, endingPanel, settingsPanel, dossierPanel, joinPanel, radioPanel;
        UnityEngine.UI.Text className, classRole, classPerk, lobbyInfo, lobbyPlayers, objectiveText, statusText, messageText, interactionText, roomText, toastText, endingText, mapLocation, frequencyText;
        UnityEngine.UI.InputField nameField, addressField, codeField;
        UnityEngine.UI.Button startButton, restartButton;
        UnityEngine.UI.Image sanityBar, staminaBar;
        UnityEngine.UI.Text roleItemText, inventoryCaption;
        UnityEngine.UI.Slider frequency;
        readonly UnityEngine.UI.Image[] inventorySlotBgs = new UnityEngine.UI.Image[5];
        readonly UnityEngine.UI.Text[] inventorySlotLabels = new UnityEngine.UI.Text[5];
        readonly UnityEngine.UI.Outline[] inventorySlotOutlines = new UnityEngine.UI.Outline[5];
        readonly List<KeyValuePair<string,UnityEngine.UI.Button>> classButtons=new List<KeyValuePair<string,UnityEngine.UI.Button>>();
        readonly List<UnityEngine.UI.Button> graphicsPresetButtons=new List<UnityEngine.UI.Button>();
        readonly List<UnityEngine.UI.Button> shadowSettingButtons=new List<UnityEngine.UI.Button>();
        readonly Dictionary<string,UnityEngine.UI.Button> graphicsToggleButtons=new Dictionary<string,UnityEngine.UI.Button>();
        UnityEngine.UI.Slider renderScaleSetting,brightnessSetting;
        UnityEngine.UI.Text renderScaleValue,brightnessValue,graphicsPresetState;
        GameObject graphicsSettings,audioSettings;
        string selectedClass="engineer", previousPhase, lastMessage;
        float toastUntil, nextHud;
        int lastRadioSequence;
        int hudTapes=-1,hudCanisters=-1,hudFuses=-1,hudTripSeconds=-1,hudBearing=-1,hudDirection=-1;
        int hudCigarettes=-1;
        string hudInteraction,hudRoom,hudRoleClass,hudSelectedItem;
        bool hudRoleEquipped,hudInteractionActive;
        readonly string[] hudSlotItems=new string[5];
        readonly bool[] hudSlotSelected=new bool[5];
        bool hudSlotsInitialized;
        readonly System.Text.StringBuilder lobbyRoster=new System.Text.StringBuilder(256);
        string[] lobbyRosterIds=new string[4],lobbyRosterNames=new string[4],lobbyRosterClasses=new string[4];
        string renderedLobbyCode,renderedLobbyHost;
        int renderedLobbyCount=-1;
        static readonly string[] CompassDirections={"Пн","Пн-Сх","Сх","Пд-Сх","Пд","Пд-Зх","Зх","Пн-Зх"};
        public string SelectedClass=>selectedClass;
        public event Action<string> ClassSelected;
        public bool HasModal=>Active(menuPanel)||Active(lobbyPanel)||Active(pausePanel)||Active(mapPanel)||Active(calibrationPanel)||Active(endingPanel)||Active(settingsPanel)||Active(dossierPanel)||Active(joinPanel)||Active(chapterPanel)||Active(preparationPanel)||Active(networkPanel)||Active(inventoryPanel)||Active(quitPanel)||Active(bookPanel);
        public bool RadioOpen=>Active(radioPanel);
        public bool MapOpen=>Active(mapPanel);
        public bool PresentationHidden=>interfaceGroup!=null&&interfaceGroup.alpha==0;
        static bool Active(GameObject go)=>go!=null&&go.activeSelf;
        static Color Ink=>new Color32(228,221,205,255);
        static Color Muted=>new Color32(157,150,141,255);
        static Color Gold=>new Color32(203,91,68,255);
        static Color PanelInk=>new Color(.065f,.052f,.052f,1);

        public void Initialize(WorldData data,DeadAirSession game,DeadAirPlayer actor)
        {
            world=data;session=game;player=actor;
            font=Resources.Load<Font>("Fonts/IBMPlexMono-Regular")??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            heading=Resources.Load<Font>("Fonts/Oswald")??font;
            if(FindFirstObjectByType<EventSystem>()==null){var events=new GameObject("Event System");events.AddComponent<EventSystem>();events.AddComponent<StandaloneInputModule>();}
            var go=new GameObject("BUREAU-9 interface");canvas=go.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            interfaceGroup=go.AddComponent<CanvasGroup>();
            var scaler=go.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            designRoot=new GameObject("Safe interface frame",typeof(RectTransform)).GetComponent<RectTransform>();designRoot.SetParent(canvas.transform,false);designRoot.anchorMin=designRoot.anchorMax=designRoot.pivot=new Vector2(.5f,.5f);designRoot.sizeDelta=new Vector2(1920,1080);
            BuildMenu();BuildLobby();BuildHud();BuildRadio();BuildPause();BuildMap();BuildCalibration();BuildEnding();BuildSettings();BuildDossier();BuildJoin();
            BuildSupplies();
            toastText=Text(designRoot,"Notification","",20,510,210,900,64,TextAnchor.MiddleCenter);toastText.color=Ink;toastText.gameObject.SetActive(false);
            session.Error+=Toast;session.StateChanged+=RefreshPhase;DeadAirSettings.Changed+=RefreshGraphicsSettings;
            SelectClass(selectedClass);RefreshPhase();FocusPanel(menuPanel);
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
        void Rule(Transform parent,float x,float y,float width){Fill(parent,"Divider",x,y,width,1,new Color(1,1,1,.12f));}
        UnityEngine.UI.Button Button(Transform parent,string name,string value,float x,float y,float w,float h,Action click,bool primary=false)
        {
            var r=Rect(parent,name,x,y,w,h);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Color.white;
            var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();var c=b.colors;c.normalColor=primary?new Color(.53f,.15f,.115f,.94f):new Color(.14f,.12f,.12f,.8f);c.highlightedColor=primary?new Color(.72f,.24f,.17f):new Color(.30f,.21f,.19f,.98f);c.pressedColor=new Color(.39f,.10f,.08f);c.selectedColor=c.highlightedColor;c.disabledColor=new Color(.12f,.105f,.10f,.38f);c.fadeDuration=.12f;b.colors=c;
            Fill(r,"Scratched edge",0,h-1,w,1,new Color(1,1,1,.12f));
            var label=Text(r,"Label",value,21,16,0,w-32,h,TextAnchor.MiddleCenter);label.color=Ink;
            b.onClick.AddListener(()=>{DeadAirAudio.Play("click");click();});return b;
        }
        UnityEngine.UI.InputField Field(Transform parent,string name,string initial,float x,float y,float w,float h)
        {
            var r=Rect(parent,name,x,y,w,h);r.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.09f,.09f,.09f);Fill(r,"Input edge",0,h-1,w,1,new Color(1,1,1,.25f));
            var field=r.gameObject.AddComponent<UnityEngine.UI.InputField>();var label=Text(r,"Value",initial,22,16,0,w-32,h,TextAnchor.MiddleLeft);
            field.textComponent=label;field.text=initial;field.characterLimit=32;field.caretColor=Gold;return field;
        }
        GameObject FullScreen(string name,Color color){var root=Fill(designRoot,name,0,0,1920,1080,Color.clear,true).gameObject;Fill(root.transform,"Screen shade",-1920,-1080,5760,3240,color);return root;}
        GameObject Modal(string name,string title,float width,float height,out Transform card)
        {
            var root=FullScreen(name,new Color(.012f,.008f,.008f,.81f));
            card=Fill(root.transform,name+" card",960-width/2,540-height/2,width,height,PanelInk,true).transform;
            Text(card,"Title",title,42,38,36,width-140,68,title:true);
            Button(card,"Close","×",width-77,24,44,44,()=>Close(root));
            root.SetActive(false);return root;
        }
        void Open(GameObject panel){if(DeadAirScarePresentation.FullscreenActive)return;HideRadio();panel.SetActive(true);panel.transform.SetAsLastSibling();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;ReconcilePause();RefreshModalInput(true);}
        void Close(GameObject panel){panel.SetActive(false);DeadAirSettings.Save();ReconcilePause();if(panel==mapPanel)player.SetMapPresentation(false);RefreshModalInput(true);}
        void ReconcilePause()
        {
            var state=session.State;
            if(!session.IsHost||state==null||state.phase!="playing")return;
            bool paused=state.players.Count==1&&HasModal;
            if(state.paused!=paused)session.Command(new NetPacket{type="pause",paused=paused});
        }
        public void ReturnToMenu(){player.SetPause(false);session.Leave();toastText.gameObject.SetActive(false);}

        void BuildMenu() => BuildHorrorMenu();
        void ClassPicker(Transform p,float x,float y,float w)
        {
            string[] ids={"engineer","operator","journalist","dispatcher"};
            for(int i=0;i<ids.Length;i++){string id=ids[i];var b=Button(p,"Choose "+id,world.GetClass(id).name,x+i*w/4,y,w/4-5,46,()=>SelectClass(id));classButtons.Add(new KeyValuePair<string,UnityEngine.UI.Button>(id,b));}
        }
        void SaveName(){nameField.text=string.IsNullOrWhiteSpace(nameField.text)?"Оператор":nameField.text.Trim();PlayerPrefs.SetString("DeadAir.PlayerName",nameField.text);PlayerPrefs.Save();}
        void StartSolo(bool creative){SaveName();session.Host(nameField.text,selectedClass,true,creative,selectedChapter);if(session.State!=null)session.Command(new NetPacket{type="start"});}
        void SelectClass(string id)
        {
            selectedClass=id;var cls=world.GetClass(id);className.text=cls.name;classRole.text=cls.title;classPerk.text=cls.perk;
            foreach(var pair in classButtons){var colors=pair.Value.colors;colors.normalColor=pair.Key==id?new Color(.43f,.17f,.13f,.95f):new Color(.14f,.12f,.12f,.8f);colors.selectedColor=colors.highlightedColor;pair.Value.colors=colors;}
            if(session.IsConnected&&session.State.phase=="lobby")session.Command(new NetPacket{type="class",classId=id});
            ClassSelected?.Invoke(id);
        }
        void BuildLobby() => BuildHorrorLobby();
        void BuildJoin()
        {
            joinPanel=Modal("Join session","ПРИЄДНАТИСЯ ДО СЕАНСУ",900,820,out var p);
            Text(p,"Network hint","Введіть адресу хоста і код кімнати.\nПотрібна спільна локальна мережа або VPN.",19,38,154,824,70).color=Muted;
            Text(p,"Name label","ІМ’Я ОПЕРАТОРА",15,40,236,824,27).color=Muted;
            var joinName=Field(p,"Guest name",nameField.text,38,272,824,52);
            Text(p,"Address label","IP АДРЕСА ХОСТА",15,40,345,824,27).color=Muted;
            addressField=Field(p,"Host address",PlayerPrefs.GetString("DeadAir.HostAddress","127.0.0.1"),38,382,824,52);
            Text(p,"Code label","КОД КІМНАТИ",15,40,455,824,27).color=Muted;
            codeField=Field(p,"Room code","",38,492,824,52);codeField.characterLimit=5;
            Text(p,"Guest role","ОПЕРАТОР",15,40,574,824,27).color=Muted;ClassPicker(p,38,610,829);
            Button(p,"Connect","УСТАНОВИТИ ЗВ’ЯЗОК",38,724,824,58,()=>{nameField.text=joinName.text;SaveName();PlayerPrefs.SetString("DeadAir.HostAddress",addressField.text);session.Join(addressField.text.Trim(),codeField.text.Trim().ToUpperInvariant(),nameField.text,selectedClass);},true);
        }
        void BuildHud() => BuildHorrorHud();
        void BuildPause() => BuildHorrorPause();
        void BuildRadio()
        {
            radioPanel=Fill(hudPanel.transform,"Quick radio callouts",390,885,1140,106,PanelInk).gameObject;
            Text(radioPanel.transform,"Radio caption","РАЦІЯ · 1–4",16,16,10,1108,26).color=Muted;
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
            mapPanel=FullScreen("Station plan",Color.clear);
            mapPanel.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            var p=mapPanel.transform;
            var strip=Fill(p,"Map heading",530,48,860,100,PanelInk).transform;
            Text(strip,"Title","ПЛАН ОФІСУ",26,24,14,810,40,TextAnchor.MiddleCenter,true).color=Ink;
            mapLocation=Text(strip,"Current room","",14,24,62,810,25,TextAnchor.MiddleCenter);
            Button(p,"Close map","M / ESC — СКЛАСТИ КАРТУ",720,164,480,48,()=>Close(mapPanel));
            mapPanel.SetActive(false);
        }
        void BuildCalibration()
        {
            calibrationPanel=Modal("Frequency calibration","ЧАСТОТА",840,640,out var p);
            Text(p,"Frequency hint","Потрібна частота: 96.4 МГц",21,38,151,764,50).color=Muted;
            frequencyText=Text(p,"Frequency","94.2 МГц",70,40,255,760,98,TextAnchor.MiddleCenter,true);frequencyText.color=Gold;
            frequency=Slider(p,"Frequency dial",40,397,760,87.5f,108,94.2f,v=>{frequencyText.text=v.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" МГц";});
            Text(p,"Band labels","87.5                                                     108.0",15,40,438,760,30).color=Muted;
            Button(p,"Tune down","− 0.1",40,483,177,53,()=>frequency.value=Mathf.Round((frequency.value-.1f)*10)/10);
            Button(p,"Tune up","+ 0.1",232,483,177,53,()=>frequency.value=Mathf.Round((frequency.value+.1f)*10)/10);
            Button(p,"Confirm frequency","ЗАФІКСУВАТИ СИГНАЛ",424,483,376,53,()=>{
                if(Mathf.Abs(frequency.value-96.4f)>.16f){Toast("Сигнал нестійкий. Спробуйте 96.4 МГц.");DeadAirAudio.Play("error");return;}
                session.Command(new NetPacket{type="calibrate",frequency=frequency.value});if(session.State!=null&&session.State.calibrated)Close(calibrationPanel);
            },true);
        }
        UnityEngine.UI.Slider Slider(Transform parent,string name,float x,float y,float width,float min,float max,float initial,Action<float> changed)
        {
            var r=Rect(parent,name,x,y,width,36);var slider=r.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.minValue=min;slider.maxValue=max;
            Art(r,"Track","bar",0,13,width,10,new Color(.27f,.22f,.20f)).raycastTarget=true;
            var fillArea=Rect(r,"Fill area",0,13,width,10);
            var fill=Art(fillArea,"Fill","bar",0,0,0,0,Gold);
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
            settingsPanel=Modal("Settings","НАЛАШТУВАННЯ",1060,1020,out var p);
            Button(p,"Graphics tab","ГРАФІКА",40,123,474,48,()=>ShowSettingsTab(true));
            Button(p,"Audio tab","ЗВУК І КЕРУВАННЯ",534,123,486,48,()=>ShowSettingsTab(false));
            graphicsSettings=Rect(p,"Graphics",40,197,980,734).gameObject;
            audioSettings=Rect(p,"Audio and controls",40,197,980,734).gameObject;
            var g=graphicsSettings.transform;
            Text(g,"Preset label","ЯКІСТЬ",18,0,3,295,37);
            string[] qualityNames={"НИЗЬКА","СЕРЕДНЯ","ВИСОКА"};
            for(int i=0;i<3;i++)
            {
                int q=i;graphicsPresetButtons.Add(Button(g,"Quality "+q,qualityNames[i],310+i*226,0,212,44,()=>DeadAirSettings.ApplyGraphicsPreset(q)));
            }
            graphicsPresetState=Text(g,"Preset state","",13,0,42,295,25);graphicsPresetState.color=Muted;
            renderScaleSetting=GraphicsSlider(g,"Render scale","МАСШТАБ ЗОБРАЖЕННЯ",78,.5f,1,DeadAirSettings.RenderScale,v=>DeadAirSettings.RenderScale=v,out renderScaleValue);
            brightnessSetting=GraphicsSlider(g,"Brightness","ЯСКРАВІСТЬ",170,.8f,1.2f,DeadAirSettings.Brightness,v=>DeadAirSettings.Brightness=v,out brightnessValue);
            Text(g,"Shadow label","ТІНІ",18,0,274,450,40);
            string[] shadowNames={"ВИМКНЕНІ","ПРОСТІ","М’ЯКІ"};
            for(int i=0;i<3;i++)
            {
                int q=i;shadowSettingButtons.Add(Button(g,"Shadows "+q,shadowNames[i],310+i*226,268,212,44,()=>DeadAirSettings.ShadowQuality=q));
            }
            GraphicsToggle(g,"Ambient occlusion","ОБ’ЄМНЕ ЗАТІНЕННЯ",334,()=>DeadAirSettings.AmbientOcclusion,v=>DeadAirSettings.AmbientOcclusion=v);
            GraphicsToggle(g,"Bloom","М’ЯКЕ СВІТІННЯ ЛАМП",398,()=>DeadAirSettings.Bloom,v=>DeadAirSettings.Bloom=v);
            GraphicsToggle(g,"Antialiasing","ЗГЛАДЖУВАННЯ КРАЇВ",462,()=>DeadAirSettings.Antialiasing,v=>DeadAirSettings.Antialiasing=v);
            GraphicsToggle(g,"Reflections","ВІДБИТТЯ",526,()=>DeadAirSettings.Reflections,v=>DeadAirSettings.Reflections=v);
            GraphicsToggle(g,"Grain","ЗЕРНО ПЛІВКИ",590,()=>DeadAirSettings.Grain,v=>DeadAirSettings.Grain=v);
            GraphicsToggle(g,"Flicker","МЕРЕХТІННЯ ТА СПАЛАХИ",654,()=>DeadAirSettings.Flicker,v=>DeadAirSettings.Flicker=v);
            var a=audioSettings.transform;
            SettingSlider(a,"ГУЧНІСТЬ",8,0,1,DeadAirSettings.Volume,v=>DeadAirSettings.Volume=v);
            SettingSlider(a,"ОТОЧЕННЯ",110,0,1,DeadAirSettings.AmbienceVolume,v=>DeadAirSettings.AmbienceVolume=v);
            SettingSlider(a,"ЗВУКОВІ ЕФЕКТИ",212,0,1,DeadAirSettings.EffectsVolume,v=>DeadAirSettings.EffectsVolume=v);
            SettingSlider(a,"ЧУТЛИВІСТЬ МИШІ",314,.3f,2,DeadAirSettings.Sensitivity,v=>DeadAirSettings.Sensitivity=v,true);
            Text(a,"Fullscreen label","ПОВНИЙ ЕКРАН",18,40,432,535,39);Toggle(a,"Fullscreen",630,422,()=>DeadAirSettings.Fullscreen,v=>DeadAirSettings.Fullscreen=v);
            Text(a,"Mute label","БЕЗ ЗВУКУ",18,40,498,535,39);Toggle(a,"Mute",630,488,()=>DeadAirSettings.Muted,v=>DeadAirSettings.Muted=v);
            Button(p,"Save settings","НАЗАД",40,944,980,48,()=>Close(settingsPanel));
            ShowSettingsTab(true);RefreshGraphicsSettings();
        }
        void ShowSettingsTab(bool graphics){graphicsSettings.SetActive(graphics);audioSettings.SetActive(!graphics);}
        UnityEngine.UI.Slider GraphicsSlider(Transform p,string name,string caption,float y,float min,float max,float initial,Action<float> setter,out UnityEngine.UI.Text value)
        {
            Text(p,name+" label",caption,18,0,y,780,32);
            value=Text(p,name+" value","",18,810,y,170,32,TextAnchor.UpperRight);value.color=Ink;
            return Slider(p,name+" slider",0,y+37,980,min,max,initial,setter);
        }
        void GraphicsToggle(Transform p,string name,string caption,float y,Func<bool> get,Action<bool> set)
        {
            Text(p,name+" label",caption,18,0,y+6,650,36);
            graphicsToggleButtons[name]=Button(p,name,"",670,y,310,44,()=>set(!get()));
        }
        void RefreshGraphicsSettings()
        {
            if(renderScaleSetting==null)return;
            renderScaleSetting.SetValueWithoutNotify(DeadAirSettings.RenderScale);brightnessSetting.SetValueWithoutNotify(DeadAirSettings.Brightness);
            renderScaleValue.text=Mathf.RoundToInt(DeadAirSettings.RenderScale*100)+"%";brightnessValue.text=Mathf.RoundToInt(DeadAirSettings.Brightness*100)+"%";
            foreach(var pair in graphicsToggleButtons)
            {
                bool enabled=pair.Key=="Ambient occlusion"?DeadAirSettings.AmbientOcclusion:pair.Key=="Bloom"?DeadAirSettings.Bloom:pair.Key=="Antialiasing"?DeadAirSettings.Antialiasing:pair.Key=="Reflections"?DeadAirSettings.Reflections:pair.Key=="Grain"?DeadAirSettings.Grain:DeadAirSettings.Flicker;
                pair.Value.GetComponentInChildren<UnityEngine.UI.Text>().text=enabled?"УВІМКНЕНО":"ВИМКНЕНО";
                SettingChoice(pair.Value,enabled);
            }
            int q=DeadAirSettings.GraphicsQuality;
            bool preset=Mathf.Abs(DeadAirSettings.RenderScale-(q==0?.65f:q==1?.85f:1))<.005f&&DeadAirSettings.ShadowQuality==q&&DeadAirSettings.AmbientOcclusion==(q>0)&&DeadAirSettings.Bloom==(q>0)&&DeadAirSettings.Reflections==(q>0)&&DeadAirSettings.Antialiasing;
            graphicsPresetState.text=preset?"ПРОФІЛЬ ЗАСТОСОВАНО":"ВЛАСНІ НАЛАШТУВАННЯ";
            for(int i=0;i<graphicsPresetButtons.Count;i++)SettingChoice(graphicsPresetButtons[i],preset&&i==q);
            for(int i=0;i<shadowSettingButtons.Count;i++)SettingChoice(shadowSettingButtons[i],i==DeadAirSettings.ShadowQuality);
        }
        static void SettingChoice(UnityEngine.UI.Button button,bool chosen)
        {
            var colors=button.colors;colors.normalColor=chosen?new Color(.38f,.19f,.13f,.95f):new Color(.13f,.13f,.12f,.9f);button.colors=colors;
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
            dossierPanel=Modal("Case file","КЕРУВАННЯ",1020,590,out var p);
            Text(p,"Controls left","W A S D     Рух\nМИША        Огляд\nSHIFT       Біг\nE           Взаємодія\nM           Карта\nTAB / I     Інвентар",22,40,155,450,220);
            Text(p,"Controls right","1–5 / КОЛЕСО  Обрати річ\nF             Ліхтар\nV             Камера\nQ             Рація\nR             Використати\nG             Викинути",21,525,155,470,220);
            Text(p,"Inventory controls","Особисті речі персонажа лежать в інвентарі.\nESC — пауза або назад.",20,40,399,940,68).color=Muted;
            Button(p,"Close dossier","НАЗАД",40,503,940,52,()=>Close(dossierPanel));
        }
        void BuildEnding()
        {
            endingPanel=Modal("Final report","РЕЗУЛЬТАТ",900,640,out var p);
            endingText=Text(p,"Outcome","",27,40,170,820,233,TextAnchor.MiddleCenter);
            restartButton=Button(p,"Restart","ПОЧАТИ ЗНОВУ",40,448,820,66,()=>session.Command(new NetPacket{type="restart"}),true);
            Button(p,"Return to menu","ПОВЕРНУТИСЯ ДО МЕНЮ  /  ESC",40,538,820,58,ReturnToMenu);
            // A completed operation cannot resume. Every exit from its report
            // must leave the session rather than hide the only available menu.
            var close=p.Find("Close").GetComponent<UnityEngine.UI.Button>();
            close.onClick.RemoveAllListeners();close.onClick.AddListener(ReturnToMenu);
        }
        void RefreshPhase()
        {
            var state=session.State;string phase=state?.phase;
            bool changed=phase!=previousPhase;
            if(changed){previousPhase=phase;HideRadio();foreach(var panel in new[]{pausePanel,mapPanel,calibrationPanel,settingsPanel,dossierPanel,joinPanel,chapterPanel,preparationPanel,networkPanel,inventoryPanel,quitPanel,bookPanel})if(panel!=null)panel.SetActive(false);}
            menuPanel.SetActive(state==null);lobbyPanel.SetActive(phase=="lobby");hudPanel.SetActive(phase=="playing");endingPanel.SetActive(phase=="won"||phase=="lost");
            if(state==null){RefreshModalInput(changed);return;}
            RefreshChapterLabels();
            if(phase=="lobby")
            {
                joinPanel.SetActive(false);
                int count=state.players.Count;
                if(renderedLobbyCode!=state.code||renderedLobbyCount!=count)lobbyInfo.text="КОД СЕАНСУ   "+state.code+"\n"+count+" / 4 ОПЕРАТОРИ";
                bool rosterChanged=renderedLobbyCount!=count||renderedLobbyHost!=state.hostId;
                if(count>lobbyRosterIds.Length){Array.Resize(ref lobbyRosterIds,count);Array.Resize(ref lobbyRosterNames,count);Array.Resize(ref lobbyRosterClasses,count);}
                for(int i=0;i<count;i++)
                {
                    var entry=state.players[i];
                    if(lobbyRosterIds[i]!=entry.id||lobbyRosterNames[i]!=entry.name||lobbyRosterClasses[i]!=entry.characterClass)rosterChanged=true;
                    lobbyRosterIds[i]=entry.id;lobbyRosterNames[i]=entry.name;lobbyRosterClasses[i]=entry.characterClass;
                }
                if(rosterChanged)
                {
                    lobbyRoster.Clear();lobbyRoster.Append("ГРАВЦІ\n\n");
                    foreach(var entry in state.players)lobbyRoster.Append(entry.id==state.hostId?"◆ ":"◇ ").Append(entry.name).Append('\n').Append(world.GetClass(entry.characterClass).role).Append("\n\n");
                    lobbyPlayers.text=lobbyRoster.ToString();
                }
                renderedLobbyCode=state.code;renderedLobbyHost=state.hostId;renderedLobbyCount=count;
                startButton.interactable=state.hostId==session.LocalId;
                lobbyChapterButton.interactable=session.IsHost;
                lobbyStartLabel.text=state.hostId==session.LocalId?"ПОЧАТИ ГРУ":"ОЧІКУЄМО ХОСТА";
            }
            if(phase=="won"||phase=="lost")
            {
                toastText.gameObject.SetActive(false);
                endingText.text=phase=="won"?"Главу завершено":"Ви загинули";
                restartButton.gameObject.SetActive(state.hostId==session.LocalId);
                endingPanel.transform.SetAsLastSibling();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            }
            if(state.calibrated&&Active(calibrationPanel))Close(calibrationPanel);
            if(phase=="playing"&&(state.message!=lastMessage||state.radioSequence!=lastRadioSequence))Toast(state.message);
            lastMessage=state.message;lastRadioSequence=state.radioSequence;
            RefreshModalInput(changed);
        }
        void Update()
        {
            if(toastText==null)return;if(Time.unscaledTime>=toastUntil)toastText.gameObject.SetActive(false);
            if(DeadAirScarePresentation.FullscreenActive)return;
            if((Input.GetKeyDown(KeyCode.I)||Input.GetKeyDown(KeyCode.Tab))&&(InventoryOpen||!HasModal))ToggleInventory();
            if(Time.unscaledTime<nextHud||session.State==null||session.State.phase!="playing")return;nextHud=Time.unscaledTime+.1f;
            var state=session.State;var me=state.Player(session.LocalId);if(me==null)return;
            roomText.text=player.CameraUp?"● REC":"";
            float mushroomTime=DeadAirMushrooms.Remaining(state,me);
            int tripSeconds=mushroomTime>0&&!me.down?Mathf.CeilToInt(mushroomTime):0;
            if(tripSeconds!=hudTripSeconds){hudTripSeconds=tripSeconds;statusText.text=tripSeconds>0?"ГРИБНИЙ ТРІП  ·  "+tripSeconds+" с":"";}
            statusText.color=mushroomTime>0?new Color(.64f,.91f,.76f):Muted;
            bool evidenceChanged=hudTapes!=state.tapes.Count||hudCanisters!=state.canisters.Count||hudFuses!=state.fuses.Count;
            if(hudTapes!=state.tapes.Count){hudTapes=state.tapes.Count;evidenceText.text=hudTapes+" / 3  КАСЕТИ";}
            if(hudCanisters!=state.canisters.Count){hudCanisters=state.canisters.Count;fuelEvidenceText.text=hudCanisters+" / 3  ПАЛИВО";}
            if(hudFuses!=state.fuses.Count){hudFuses=state.fuses.Count;fuseEvidenceText.text=hudFuses+" / 2  ЗАПОБІЖНИКИ";}
            sanityBar.fillAmount=Mathf.Clamp01(me.sanity/100);staminaBar.fillAmount=Mathf.Clamp01(player.Stamina/100);
            var forward=player.ViewCamera.transform.forward;float compass=Mathf.Repeat(Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg,360);
            int bearing=Mathf.RoundToInt(compass),direction=Mathf.RoundToInt(compass/45)%8;
            if(bearing!=hudBearing||direction!=hudDirection){hudBearing=bearing;hudDirection=direction;compassText.text=CompassDirections[direction]+"   ·   "+bearing.ToString("000")+"°";}
            objectiveText.text=state.broadcast?"Поверніться до фургона":state.breakerOn?"Передавач: 96.4 МГц":state.power?(state.fuses.Count>=2?"Увімкніть рубильник у підвалі":"Знайдіть 2 запобіжники для щитка"):state.canisters.Count>=3?"Заправте та увімкніть генератор у дворі":"Знайдіть 3 каністри дизеля для генератора";
            messageText.text=me.down?"ВАС ЗБИТО. ЧЕКАЙТЕ НА ДОПОМОГУ.":!string.IsNullOrEmpty(me.hiding)?"ВИ У СХОВАНЦІ  /  E — ВИЙТИ":"";
            bool hasInteraction=!string.IsNullOrEmpty(player.InteractionId);
            string interaction=hasInteraction?player.InteractionLabel:null;
            if(hasInteraction!=hudInteractionActive||interaction!=hudInteraction){hudInteractionActive=hasInteraction;hudInteraction=interaction;interactionText.text=hasInteraction?"[ E ]  "+interaction:"";}
            string room=world.RoomAt(me.x,me.z,me.y);
            if(room!=hudRoom||evidenceChanged){hudRoom=room;mapLocation.text="ВИ ЗАРАЗ: "+room.ToUpperInvariant()+"    /    КАСЕТИ "+hudTapes+"/3    /    ПАЛИВО "+hudCanisters+"/3    /    ЗАПОБІЖНИКИ "+hudFuses+"/2";}
            string selectedItem=me.SelectedItem;
            if(me.uniqueItemEquipped!=hudRoleEquipped||me.characterClass!=hudRoleClass||selectedItem!=hudSelectedItem||me.cigarettesRemaining!=hudCigarettes)
            {
                hudRoleEquipped=me.uniqueItemEquipped;hudRoleClass=me.characterClass;hudSelectedItem=selectedItem;hudCigarettes=me.cigarettesRemaining;
                roleItemText.text=selectedItem=="flashlight"||selectedItem==DeadAirSupplies.BatteryItem?"R — замінити батарейку":
                    selectedItem==DeadAirPrisonBook.Id?"R — читати справу":me.uniqueItemEquipped?
                    (me.characterClass=="operator"?"R — Закурити ("+me.cigarettesRemaining+")":"R — Використати"):"";
            }
            RefreshSupplies(state,me);
            compassText.gameObject.SetActive(me.characterClass=="dispatcher"&&me.uniqueItemEquipped);
            int page=me.selectedSlot/5*5;
            for(int i=0;i<5;i++)
            {
                int slot=page+i;
                bool isSelected=me.selectedSlot==slot;
                bool hasItem=me.inventory!=null&&slot<me.inventory.Count;
                string itemId=hasItem?me.inventory[slot]:null;
                if(!hudSlotsInitialized||hudSlotItems[i]!=itemId||itemId==DeadAirSupplies.BatteryItem)
                    inventorySlotLabels[i].text=hasItem?StackItemName(itemId,me):"—";
                if(!hudSlotsInitialized||hudSlotItems[i]!=itemId||hudSlotSelected[i]!=isSelected)
                {
                    inventorySlotLabels[i].color=isSelected?Gold:hasItem?Ink:Muted;
                    inventorySlotBgs[i].color=isSelected?new Color(.35f,.16f,.12f,.94f):new Color(.12f,.105f,.095f,.75f);
                    inventorySlotOutlines[i].effectColor=isSelected?Gold:new Color(.6f,.52f,.42f,.17f);
                    inventorySlotOutlines[i].effectDistance=isSelected?new Vector2(2,-2):new Vector2(1,-1);
                }
                if(!hudSlotsInitialized||hudSlotItems[i]!=itemId){quickIcons[i].enabled=hasItem;if(hasItem)quickIcons[i].sprite=DeadAirUIArt.Sprite(ItemIcon(itemId));}
                hudSlotItems[i]=itemId;hudSlotSelected[i]=isSelected;
            }
            hudSlotsInitialized=true;
            if(InventoryOpen)RefreshInventory();
        }
        static string FormatItemName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "—";
            if (itemId == "flashlight") return "Ліхтар";
            if (itemId == DeadAirSupplies.BatteryItem) return "Батарейки";
            if (itemId == DeadAirPrisonBook.Id) return "Справа №07";
            if (itemId == "cigarette-pack") return "Сигарети";
            if (itemId == "wrench") return "Ключ";
            if (itemId == "recorder") return "Диктофон";
            if (itemId == "compass") return "Компас";
            if (itemId.StartsWith("fuel-canister")) return "Каністра";
            if (itemId == "keycard-guard") return "Картка Охр.";
            if (itemId == "keycard-engineer") return "Картка Інж.";
            if (itemId == "key-basement" || itemId == "keycard-basement") return "Картка Під.";
            if (itemId == "key-main") return "Головний Ключ";
            if (itemId.StartsWith("keycard-")) return "Картка";
            if (itemId == "ladder") return "Драбина";
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
        {if(DeadAirScarePresentation.FullscreenActive)return true;if(RadioOpen){HideRadio();return true;}GameObject top=null;foreach(var p in new[]{settingsPanel,dossierPanel,joinPanel,calibrationPanel,mapPanel,chapterPanel,preparationPanel,networkPanel,inventoryPanel,quitPanel,bookPanel,endingPanel})if(Active(p)&&(top==null||p.transform.GetSiblingIndex()>top.transform.GetSiblingIndex()))top=p;if(top==endingPanel&&top!=null){ReturnToMenu();return true;}if(top!=null){Close(top);return true;}return false;}
        public void ToggleRadio(){if(DeadAirScarePresentation.FullscreenActive)return;if(RadioOpen)HideRadio();else if(session.State?.phase=="playing"&&!HasModal)radioPanel.SetActive(true);}
        public void HideRadio(){if(radioPanel!=null)radioPanel.SetActive(false);}
        public void ShowPause(bool shown){if(shown){pausePanel.transform.Find("Pause card/Pause note").GetComponent<UnityEngine.UI.Text>().text=session.State?.players.Count>1?"Мережева гра не призупиняється.":"";Open(pausePanel);}else Close(pausePanel);}
        public void ToggleMap(){if(DeadAirScarePresentation.FullscreenActive)return;if(Active(mapPanel))Close(mapPanel);else if(session.State?.phase=="playing"){if(player.LocalPaused)player.SetPause(false);player.SetMapPresentation(true);Open(mapPanel);}}
        public void ShowCalibration(){Open(calibrationPanel);}
        public void ShowSettings(){RefreshGraphicsSettings();Open(settingsPanel);}
        public void ShowDossier(){Open(dossierPanel);}
        public void Toast(string message){if(toastText==null)return;toastText.text=message;toastText.gameObject.SetActive(true);toastText.transform.SetAsLastSibling();toastUntil=Time.unscaledTime+5;}
        void OnDestroy(){DeadAirSettings.Changed-=RefreshGraphicsSettings;if(session!=null){session.Error-=Toast;session.StateChanged-=RefreshPhase;}}
    }
}
