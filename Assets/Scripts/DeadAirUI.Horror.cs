using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeadAir
{
    public sealed partial class DeadAirUI
    {
        RectTransform designRoot;
        GameObject chapterPanel, preparationPanel, networkPanel, inventoryPanel, quitPanel;
        Text preparationChapter, lobbyChapter, compassText, inventoryName, inventoryDescription;
        Text inventoryPageLabel;
        Text inventoryEquipLabel;
        Text lobbyStartLabel;
        Button lobbyChapterButton;
        Button previousInventoryPage,nextInventoryPage;
        Image inventoryPreview;
        Button chapterContinue;
        Text chapterDescription;
        readonly List<KeyValuePair<string,DeadAirChapterCard>> chapterCards = new List<KeyValuePair<string,DeadAirChapterCard>>();
        readonly List<Image> quickIcons = new List<Image>();
        readonly List<Button> bagSlots = new List<Button>();
        readonly List<Image> bagIcons = new List<Image>();
        readonly List<Text> bagLabels = new List<Text>();
        string selectedChapter = DeadAirChapters.DefaultId;
        string renderedSelectedChapter,renderedLobbyChapter;
        bool preparingHost, chapterFromLobby;
        int bagSelection, bagPage;
        readonly string[] renderedBagItems=new string[12];
        int renderedBagPage=-1,renderedBagSelection=-1,renderedBagCount=-1,renderedBagBatteries=-1,renderedBagCigarettes=-1,renderedBagCharge=-1;
        int renderedBagRoleReady=-1;
        string renderedBagSelectedItem;
        public string SelectedChapter => selectedChapter;
        public bool InventoryOpen => Active(inventoryPanel);

        Image Art(Transform parent,string name,string id,float x,float y,float width,float height,Color color)
        {
            var image=Fill(parent,name,x,y,width,height,color);
            image.sprite=DeadAirUIArt.Sprite(id);
            if(id=="frame"||id=="button"||id=="paper")image.type=Image.Type.Sliced;
            return image;
        }

        void SceneVeil(Transform parent,float strength=1)
        {
            var texture=new Texture2D(128,2,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int x=0;x<128;x++)for(int y=0;y<2;y++)
                texture.SetPixel(x,y,new Color(.035f,.02f,.021f,Mathf.Lerp(.96f,.04f,Mathf.SmoothStep(0,1,x/127f))*strength));
            texture.Apply();
            var image=Rect(parent,"Cinematic shade",-420,-200,2760,1480).gameObject.AddComponent<RawImage>();
            image.texture=texture;image.raycastTarget=false;
            parent.gameObject.AddComponent<DeadAirUITextureOwner>().texture=texture;
        }

        Button Navigation(Transform parent,string name,string label,float x,float y,float width,Action action)
        {
            var button=Button(parent,name,"",x,y,width,58,action);
            button.GetComponent<Image>().color=Color.clear;
            button.transition=Selectable.Transition.None;
            button.transform.Find("Scratched edge").gameObject.SetActive(false);
            var text=button.transform.Find("Label").GetComponent<Text>();
            text.text=label;text.fontSize=30;text.alignment=TextAnchor.MiddleLeft;
            text.rectTransform.anchoredPosition=new Vector2(30,0);
            text.rectTransform.sizeDelta=new Vector2(width-50,58);
            var mark=Art(button.transform,"Selection scratch","arrow",0,17,20,24,Gold);
            button.gameObject.AddComponent<DeadAirMenuHover>().Initialize(text,mark,Ink,Gold);
            return button;
        }

        void BuildHorrorMenu()
        {
            var saved=PlayerPrefs.GetString("DeadAir.Chapter",DeadAirChapters.DefaultId);
            selectedChapter=DeadAirChapters.IsPlayable(saved)?saved:DeadAirChapters.DefaultId;
            menuPanel=FullScreen("Main menu",Color.clear);var p=menuPanel.transform;SceneVeil(p);
            Text(p,"Title","DEAD AIR",110,134,235,720,150,title:true);
            Navigation(p,"Solo","Грати",114,453,570,()=>ShowPreparation(false));
            Navigation(p,"Chapters","Глави",114,520,570,ShowChapters);
            Navigation(p,"Network","Мережева гра",114,587,570,()=>Open(networkPanel));
            Navigation(p,"Settings","Налаштування",114,654,570,ShowSettings);
            Navigation(p,"Dossier","Керування",114,721,570,ShowDossier);
            Navigation(p,"Exit","Вийти",114,788,570,()=>Open(quitPanel));
            Text(p,"Build version","v"+Application.version,18,144,968,600,28).color=Muted;
            BuildPreparation();BuildChapters();BuildNetwork();BuildInventory();BuildQuit();RefreshChapterLabels();
        }

        void BuildPreparation()
        {
            preparationPanel=Modal("Prepare operation","ПЕРСОНАЖ",1050,820,out var p);
            preparationChapter=Text(p,"Selected chapter","",20,42,155,950,42);preparationChapter.color=Gold;
            Text(p,"Name caption","ІМ’Я",18,44,217,920,28).color=Muted;
            nameField=Field(p,"Operator name",PlayerPrefs.GetString("DeadAir.PlayerName","Оператор"),42,252,966,53);
            string[] ids={"engineer","operator","journalist","dispatcher"};
            for(int i=0;i<ids.Length;i++)
            {
                string id=ids[i];var cls=world.GetClass(id);
                var button=Button(p,"Choose "+id,cls.name+"\n"+cls.role,42+(i%2)*492,381+(i/2)*88,474,74,()=>SelectClass(id));
                button.GetComponentInChildren<Text>().fontSize=20;classButtons.Add(new KeyValuePair<string,Button>(id,button));
            }
            className=Text(p,"Operative name","",31,44,575,430,45,title:true);
            classRole=Text(p,"Operative role","",17,44,626,430,56);classRole.color=Muted;
            classPerk=Text(p,"Operative perk","",18,510,586,496,105);classPerk.color=Ink;
            Button(p,"Begin operation","ПОЧАТИ ГЛАВУ",42,714,620,62,BeginPreparedOperation,true);
            Button(p,"Explore","ДОСЛІДЖЕННЯ",680,714,328,62,()=>StartSolo(true));
        }

        public void ShowPreparation(bool host=false)
        {
            preparingHost=host;RefreshChapterLabels();
            var begin=preparationPanel.transform.Find("Prepare operation card/Begin operation").GetComponent<Button>();
            begin.GetComponentInChildren<Text>().text=host?"СТВОРИТИ СЕАНС":"ПОЧАТИ ГЛАВУ";
            preparationPanel.transform.Find("Prepare operation card/Explore").gameObject.SetActive(!host);
            Open(preparationPanel);
        }

        void BeginPreparedOperation()
        {
            if(!preparingHost){StartSolo(false);return;}
            SaveName();session.Host(nameField.text,selectedClass,false,false,selectedChapter);
        }

        void BuildChapters()
        {
            chapterPanel=FullScreen("Chapter selection",new Color(.012f,.012f,.015f,1));
            var p=Rect(chapterPanel.transform,"Chapter selection card",0,0,1920,1080);
            var backdrop=Rect(p,"Chapter office backdrop",0,0,1920,1080).gameObject.AddComponent<RawImage>();
            backdrop.texture=Resources.Load<Texture2D>("UI/ChapterBlackpine");backdrop.color=Color.white;backdrop.raycastTarget=false;
            if(backdrop.texture!=null)
            {
                float aspect=(float)backdrop.texture.width/backdrop.texture.height;
                float width=Mathf.Min(1,(16f/9)/aspect),height=Mathf.Min(1,aspect/(16f/9));
                backdrop.uvRect=new Rect((1-width)/2,(1-height)/2,width,height);
            }
            Fill(p,"Chapter scene shade",-1920,-1080,5760,3240,new Color(.012f,.012f,.015f,.64f));
            Text(p,"Title","ГЛАВИ",46,460,89,1000,70,TextAnchor.MiddleCenter,true);
            int index=0;
            foreach(var chapter in DeadAirChapters.Catalog)
            {
                var item=chapter;float x=488+index*504;
                const float cardWidth=440,cardHeight=578;
                Fill(p,"Chapter shadow "+item.id,x+13,235,cardWidth,cardHeight,new Color(0,0,0,.55f));
                var choose=Button(p,"Select "+item.id,"",x,214,cardWidth,cardHeight,()=>SelectChapter(item.id));
                choose.transition=Selectable.Transition.None;choose.GetComponent<Image>().color=Color.clear;
                choose.transform.Find("Scratched edge").gameObject.SetActive(false);
                choose.interactable=item.playable;
                var card=Rect(choose.transform,"Chapter "+item.id,0,0,cardWidth,cardHeight);
                card.pivot=new Vector2(.5f,.5f);card.anchoredPosition=new Vector2(cardWidth/2,-cardHeight/2);
                var border=Fill(card,"Card border",0,0,cardWidth,cardHeight,new Color(.35f,.29f,.25f));
                Fill(card,"Card stock",4,4,cardWidth-8,cardHeight-8,new Color(.065f,.059f,.055f));
                if(item.playable)
                {
                    var image=Rect(card,"Office photograph",7,7,cardWidth-14,cardHeight-14).gameObject.AddComponent<RawImage>();
                    image.texture=Resources.Load<Texture2D>("UI/ChapterBlackpine");
                    float aspect=image.texture!=null?(float)image.texture.width/image.texture.height:16f/9;
                    float cropWidth=(cardWidth-14)/(cardHeight-14)/aspect;
                    image.uvRect=new Rect(.56f-cropWidth/2,0,cropWidth,1);image.color=new Color(.95f,.91f,.85f);image.raycastTarget=false;
                    ChapterPhotoShade(card,cardWidth,cardHeight);
                }
                else
                {
                    Art(card,"Sealed chapter","lock",176,252,88,88,new Color(.44f,.43f,.42f));
                    Text(card,"Unavailable","НЕДОСТУПНО",19,26,377,388,37,TextAnchor.MiddleCenter).color=new Color(.43f,.42f,.40f);
                }
                Text(card,"Chapter number","ГЛАВА "+item.number,17,25,26,390,29,TextAnchor.MiddleCenter).color=item.playable?Ink:new Color(.45f,.43f,.41f);
                Text(card,"Chapter name",item.title,item.playable?46:31,25,62,390,94,TextAnchor.UpperCenter,true).color=item.playable?Ink:new Color(.58f,.55f,.52f);
                var marker=Art(card,"Chapter mark",item.playable?"chapter":"lock",194,493,52,52,item.playable?Ink:new Color(.38f,.37f,.35f));
                var motion=choose.gameObject.AddComponent<DeadAirChapterCard>();motion.Initialize(card,border,marker,item.playable);
                chapterCards.Add(new KeyValuePair<string,DeadAirChapterCard>(item.id,motion));
                index++;
            }
            var description=Fill(p,"Chapter description panel",580,827,760,135,new Color(.043f,.039f,.037f,.94f)).transform;
            chapterDescription=Text(description,"Chapter description","",19,32,25,696,88,TextAnchor.MiddleCenter);
            Button(p,"Close","НАЗАД",110,971,260,58,()=>Close(chapterPanel));
            chapterContinue=Button(p,"Begin chapter","ДАЛІ",1550,971,260,58,()=>{
                if(chapterFromLobby){session.Command(new NetPacket{type="chapter",chapterId=selectedChapter});Close(chapterPanel);}
                else {Close(chapterPanel);ShowPreparation(false);}
            },true);
            chapterPanel.SetActive(false);
            RefreshChapterLabels();
        }

        void ChapterPhotoShade(Transform parent,float width,float height)
        {
            var texture=new Texture2D(2,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<64;y++)for(int x=0;x<2;x++)
            {
                float t=y/63f;
                float alpha=Mathf.Lerp(.16f,.82f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.62f,1,t)));
                if(t<.20f)alpha=Mathf.Lerp(.67f,.16f,t/.20f);
                texture.SetPixel(x,y,new Color(.015f,.012f,.011f,alpha));
            }
            texture.Apply();
            var image=Rect(parent,"Photograph shade",7,7,width-14,height-14).gameObject.AddComponent<RawImage>();
            image.texture=texture;image.raycastTarget=false;
            image.gameObject.AddComponent<DeadAirUITextureOwner>().texture=texture;
        }

        public void ShowChapters()
        {
            chapterFromLobby=session.State?.phase=="lobby";
            if(chapterFromLobby&&!session.IsHost){Toast("Главу обирає хост сеансу.");return;}
            chapterContinue.GetComponentInChildren<Text>().text=chapterFromLobby?"ОБРАТИ":"ДАЛІ";
            RefreshChapterLabels();
            Open(chapterPanel);
        }

        public void SelectChapter(string id)
        {
            if(!DeadAirChapters.IsPlayable(id)){Toast("Ця глава ще недоступна.");return;}
            selectedChapter=id;PlayerPrefs.SetString("DeadAir.Chapter",id);PlayerPrefs.Save();RefreshChapterLabels();
        }

        void RefreshChapterLabels()
        {
            bool selectedChanged=renderedSelectedChapter!=selectedChapter;
            if(selectedChanged)
            {
                var chapter=DeadAirChapters.Find(selectedChapter)??DeadAirChapters.Find(DeadAirChapters.DefaultId);
                if(preparationChapter!=null)preparationChapter.text="Глава "+chapter.number+" — "+chapter.title;
                if(chapterDescription!=null)chapterDescription.text=chapter.description;
                foreach(var pair in chapterCards)pair.Value.SetChosen(pair.Key==selectedChapter);
                renderedSelectedChapter=selectedChapter;
            }
            string currentId=session.State?.chapterId??selectedChapter;
            if(lobbyChapter!=null&&(selectedChanged||renderedLobbyChapter!=currentId))
            {
                var current=DeadAirChapters.Find(currentId)??DeadAirChapters.Find(selectedChapter)??DeadAirChapters.Find(DeadAirChapters.DefaultId);
                lobbyChapter.text="Глава "+current.number+" — "+current.title;renderedLobbyChapter=currentId;
            }
        }

        void BuildNetwork()
        {
            networkPanel=Modal("Network game","МЕРЕЖЕВА ГРА",840,520,out var p);
            Text(p,"Network introduction","До 4 гравців · LAN / VPN",21,42,162,756,60).color=Muted;
            Button(p,"Host","СТВОРИТИ СЕАНС",42,311,756,62,()=>{Close(networkPanel);ShowPreparation(true);},true);
            Button(p,"Join","ПРИЄДНАТИСЯ ДО СЕАНСУ",42,392,756,62,()=>{Close(networkPanel);Open(joinPanel);});
        }

        void BuildHorrorLobby()
        {
            lobbyPanel=FullScreen("Cooperative lobby",Color.clear);var p=lobbyPanel.transform;SceneVeil(p,.92f);
            Text(p,"Lobby title","ЛОБІ",60,112,117,860,100,title:true);
            lobbyInfo=Text(p,"Room code","",25,115,234,720,88);lobbyInfo.color=Gold;
            lobbyPlayers=Text(p,"Team","",22,119,358,740,450);
            lobbyChapter=Text(p,"Lobby chapter","",22,720,626,470,184);lobbyChapter.color=Ink;
            lobbyChapterButton=Button(p,"Lobby chapters","ЗМІНИТИ ГЛАВУ",721,830,467,52,ShowChapters);
            Fill(p,"Lobby footer",0,894,1920,186,new Color(.045f,.032f,.031f,.92f));
            ClassPicker(p,116,963,896);
            startButton=Button(p,"Start operation","ПОЧАТИ ГЛАВУ",1170,954,350,62,()=>session.Command(new NetPacket{type="start"}),true);
            lobbyStartLabel=startButton.GetComponentInChildren<Text>();
            Button(p,"Leave lobby","ВИЙТИ ІЗ СЕАНСУ",1538,954,260,62,()=>session.Leave());lobbyPanel.SetActive(false);
        }

        void BuildHorrorHud()
        {
            hudPanel=FullScreen("HUD",Color.clear);hudPanel.GetComponent<Image>().raycastTarget=false;var p=hudPanel.transform;
            roomText=Text(p,"Location","",17,1270,49,590,79,TextAnchor.UpperRight);roomText.color=Muted;
            compassText=Text(p,"Compass","",14,812,24,296,27,TextAnchor.MiddleCenter);compassText.color=Muted;
            Art(p,"Sanity eye","eye",58,905,44,34,Ink);
            Art(p,"Sanity track","bar",121,913,315,19,new Color(.23f,.12f,.10f,.8f));
            sanityBar=Art(p,"Sanity","bar",121,913,315,19,new Color(.79f,.29f,.19f));sanityBar.type=Image.Type.Filled;sanityBar.fillMethod=Image.FillMethod.Horizontal;
            Art(p,"Stamina feet","stamina",61,954,36,36,Ink);
            Art(p,"Stamina track","bar",121,963,315,13,new Color(.13f,.19f,.13f,.8f));
            staminaBar=Art(p,"Stamina","bar",121,963,315,13,new Color(.54f,.68f,.46f));staminaBar.type=Image.Type.Filled;staminaBar.fillMethod=Image.FillMethod.Horizontal;
            statusText=Text(p,"Status","",15,60,1009,710,47);statusText.color=Muted;
            for(int i=0;i<5;i++)
            {
                float x=1394+i*94;
                var bg=Art(p,"Slot "+i,"button",x,924,86,88,new Color(.14f,.14f,.13f,.85f));
                var outline=bg.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.5f,.4f,.3f,.2f);outline.effectDistance=Vector2.one;
                Text(bg.transform,"Num",(i+1).ToString(),14,5,3,20,20).color=Ink;
                var icon=Art(bg.transform,"Item icon","tape",23,8,40,37,Ink);quickIcons.Add(icon);
                var item=Text(bg.transform,"Item","—",14,3,49,80,34,TextAnchor.MiddleCenter);
                item.resizeTextForBestFit=true;item.resizeTextMinSize=11;item.resizeTextMaxSize=14;
                inventorySlotBgs[i]=bg;inventorySlotOutlines[i]=outline;inventorySlotLabels[i]=item;
            }
            messageText=Text(p,"Transmission","",20,480,872,960,57,TextAnchor.MiddleCenter);messageText.color=Ink;
            interactionText=Text(p,"Interaction","",22,480,792,960,63,TextAnchor.MiddleCenter);interactionText.color=Ink;
            Art(p,"Reticle","cross",956,536,8,8,new Color(.9f,.86f,.78f,.65f));hudPanel.SetActive(false);
        }
        Text evidenceText, fuelEvidenceText, fuseEvidenceText;

        void BuildHorrorPause()
        {
            pausePanel=Modal("Pause","ПАУЗА",760,835,out var p);
            Text(p,"Pause note","",18,42,149,676,57).color=Muted;
            Button(p,"Resume","ПРОДОВЖИТИ",42,230,676,63,()=>player.SetPause(false),true);
            Text(p,"Van map reminder","План офісу — на дошці у фургоні.",18,42,322,676,40).color=Muted;
            Button(p,"Inventory","ПОЛЬОВИЙ ЖУРНАЛ",42,390,676,59,ToggleInventory);
            Button(p,"Options","НАЛАШТУВАННЯ",42,468,676,59,ShowSettings);
            Button(p,"Help","КЕРУВАННЯ",42,546,676,59,ShowDossier);
            Button(p,"Leave","ПОВЕРНУТИСЯ ДО МЕНЮ",42,690,676,59,ReturnToMenu);
            Rule(p,42,656,676);
            var close=p.Find("Close").GetComponent<Button>();close.onClick.RemoveAllListeners();close.onClick.AddListener(()=>player.SetPause(false));
        }

        void BuildInventory() => BuildFieldJournal();

        void EquipInventorySelection()
        {
            var me=session.State?.Player(session.LocalId);if(me==null||bagSelection<0||bagSelection>=me.inventory.Count)return;
            if(me.inventory[bagSelection]==DeadAirPrisonBook.Id){Close(inventoryPanel);ShowBook();return;}
            Close(inventoryPanel);player.SelectInventorySlot(bagSelection);
        }

        public void ToggleInventory()
        {
            if(DeadAirScarePresentation.FullscreenActive)return;
            if(Active(inventoryPanel)){Close(inventoryPanel);return;}
            var me=session.State?.Player(session.LocalId);
            if(session.State?.phase!="playing"||session.State.elapsed<DeadAirVanPresentation.ArrivalDuration||me==null||me.down||me.ladderClimbing||!string.IsNullOrEmpty(me.hiding))return;
            if(player.LocalPaused)player.SetPause(false);
            if(Active(mapPanel))Close(mapPanel);
            bagSelection=me.selectedSlot;bagPage=bagSelection/12;RefreshInventory();RefreshJournal(session.State,me);Open(inventoryPanel);
        }

        void RefreshInventory()
        {
            var me=session.State?.Player(session.LocalId);if(me==null)return;
            bagSelection=Mathf.Clamp(bagSelection,0,Mathf.Max(0,me.inventory.Count-1));
            bool initial=renderedBagPage<0;
            bool selectionChanged=initial||renderedBagPage!=bagPage||renderedBagSelection!=bagSelection;
            bool batteriesChanged=renderedBagBatteries!=me.spareBatteries;
            for(int i=0;i<bagSlots.Count;i++)
            {
                int slot=bagPage*12+i;bool has=slot<me.inventory.Count;string id=has?me.inventory[slot]:null;
                if(initial||renderedBagItems[i]!=id)
                {
                    bagSlots[i].interactable=has;bagIcons[i].enabled=has;
                    if(has)bagIcons[i].sprite=DeadAirUIArt.Sprite(ItemIcon(id));
                }
                if(initial||renderedBagItems[i]!=id||(id==DeadAirSupplies.BatteryItem&&batteriesChanged))
                    bagLabels[i].text=has?StackItemName(id,me):"—";
                if(selectionChanged){var colors=bagSlots[i].colors;colors.normalColor=slot==bagSelection?new Color(.63f,.50f,.35f):new Color(.82f,.77f,.65f);bagSlots[i].colors=colors;}
                renderedBagItems[i]=id;
            }
            if(initial||renderedBagPage!=bagPage||renderedBagCount!=me.inventory.Count)
            {
                inventoryPageLabel.text=me.inventory.Count>12?(bagPage+1)+" / "+Mathf.CeilToInt(me.inventory.Count/12f):"";
                previousInventoryPage.gameObject.SetActive(me.inventory.Count>12);
                nextInventoryPage.gameObject.SetActive(me.inventory.Count>12);
            }
            string selected=bagSelection<me.inventory.Count?me.inventory[bagSelection]:null;
            bool itemChanged=initial||renderedBagSelectedItem!=selected;
            if(itemChanged||(selected=="cigarette-pack"&&renderedBagCigarettes!=me.cigarettesRemaining)||(selected==DeadAirSupplies.BatteryItem&&batteriesChanged))
                inventoryName.text=selected=="cigarette-pack"?"Сигарети · "+me.cigarettesRemaining:selected!=null?StackItemName(selected,me):"Сумка порожня";
            if(itemChanged)
            {
                inventoryPreview.enabled=selected!=null;if(selected!=null)inventoryPreview.sprite=DeadAirUIArt.Sprite(ItemIcon(selected));
                inventoryEquipLabel.text=selected==DeadAirPrisonBook.Id?"ЧИТАТИ СПРАВУ":"ОБРАТИ ПРЕДМЕТ";
            }
            int charge=Mathf.CeilToInt(me.flashlightCharge/DeadAirSupplies.FlashlightSeconds*100);
            bool roleItem=selected==DeadAirRoleItems.ForClass(me.characterClass);
            int ready=Mathf.CeilToInt(DeadAirRoleAbilities.ReadyIn(me,session.State.elapsed));
            if(itemChanged||(selected=="flashlight"&&(charge!=renderedBagCharge||batteriesChanged))||(roleItem&&ready!=renderedBagRoleReady))
            {
                inventoryDescription.text=ItemDescription(selected)+(selected=="flashlight"?"\nЗаряд: "+charge+"%. Запас: "+me.spareBatteries+".":"")+
                    (roleItem?"\n"+DeadAirRoleAbilities.Help(me.characterClass)+"\n"+(ready>0?"Готовність через "+ready+" с":"Готовий до використання"):"");
            }
            renderedBagPage=bagPage;renderedBagSelection=bagSelection;renderedBagCount=me.inventory.Count;
            renderedBagBatteries=me.spareBatteries;renderedBagCigarettes=me.cigarettesRemaining;renderedBagCharge=charge;renderedBagSelectedItem=selected;
            renderedBagRoleReady=ready;
        }

        static string ItemIcon(string id)
        {
            if(string.IsNullOrEmpty(id))return "frame";
            if(id==DeadAirSupplies.BatteryItem)return "fuse";
            if(id==DeadAirPrisonBook.Id)return "chapter";
            if(id.StartsWith("tape"))return "tape";if(id.StartsWith("fuse"))return "fuse";
            if(id.StartsWith("keycard") || id.StartsWith("key-"))return "key";
            if(id=="cigarette-pack"||id=="wrench"||id=="recorder"||id=="compass"||id=="ladder")return id;
            return id=="flashlight"?"flashlight":id.StartsWith("fuel-canister")?"fuel":"chapter";
        }
        static string ItemDescription(string id)
        {
            if(string.IsNullOrEmpty(id))return "";
            if(id=="flashlight")return "F — увімкнути / вимкнути. R — замінити батарейку. Однієї вистачає на 3 хвилини світла.";
            if(id==DeadAirSupplies.BatteryItem)return "Запасні батарейки для ліхтаря. R — вставити одну. G — викинути одну для іншого оператора.";
            if(id==DeadAirPrisonBook.Id)return "Медична картка та біографія ув'язненого з підвалу. Натисніть «Читати справу» або R.";
            if(id=="cigarette-pack")return "R — дістати сигарету й закурити.";
            if(id=="wrench")return "Гайковий ключ. R — оглянути.";
            if(id=="recorder")return "Диктофон. R — увімкнути.";
            if(id=="compass")return "Компас. R — оглянути.";
            if(id=="ladder")return "Складна драбина для проникнення всередину.";
            if(id.StartsWith("tape"))return "Касета для передавача.";
            if(id.StartsWith("keycard") || id.StartsWith("key-"))return "Картка доступу до замкнених приміщень.";
            if(id.StartsWith("fuse"))return "Запобіжник 380В для рубильника освітлення в підвалі.";
            if(id.StartsWith("fuel-canister"))return "Дизельне пальне для генератора на задньому дворі (потрібно 3 шт).";
            return "Особистий предмет оператора.";
        }

        void BuildQuit()
        {
            quitPanel=Modal("Quit game","ВИЙТИ З ГРИ?",730,410,out var p);
            Button(p,"Cancel quit","ЗАЛИШИТИСЯ",42,286,315,62,()=>Close(quitPanel));
            Button(p,"Confirm quit","ВИЙТИ З ГРИ",375,286,313,62,()=>Application.Quit(),true);
        }

        void FocusPanel(GameObject panel)
        {
            if(EventSystem.current==null||panel==null)return;
            Selectable fallback=null;
            if(!panelControls.TryGetValue(panel,out var controls))
            {controls=panel.GetComponentsInChildren<Selectable>(true);panelControls.Add(panel,controls);}
            foreach(var control in controls)
            {
                if(!control.IsActive()||!control.IsInteractable())continue;
                if(control.name.StartsWith("Close"))
                {
                    if(fallback==null||control.name!="Close")fallback=control;
                    continue;
                }
                EventSystem.current.SetSelectedGameObject(control.gameObject);return;
            }
            EventSystem.current.SetSelectedGameObject(fallback!=null?fallback.gameObject:null);
        }

        GameObject[] focusPanels;
        CanvasGroup[] focusGroups;
        CanvasGroup hudFocusGroup;
        readonly Dictionary<GameObject,Selectable[]> panelControls=new Dictionary<GameObject,Selectable[]>();
        GameObject focusedPanel;
        GameObject selectedControlObject;
        Selectable selectedControl;
        bool interfaceHidden;

        void RefreshModalInput(bool focus=false)
        {
            // Raycast overlays stop pointer clicks, but Unity's automatic keyboard
            // navigation also needs underlying Selectables to be non-interactable.
            if(focusPanels==null)
            {
                focusPanels=new[]{menuPanel,lobbyPanel,pausePanel,mapPanel,calibrationPanel,
                    endingPanel,settingsPanel,dossierPanel,joinPanel,chapterPanel,preparationPanel,networkPanel,inventoryPanel,quitPanel,bookPanel};
                focusGroups=new CanvasGroup[focusPanels.Length];
                for(int i=0;i<focusPanels.Length;i++)
                {
                    if(focusPanels[i]==null)continue;
                    var group=focusPanels[i].GetComponent<CanvasGroup>();
                    focusGroups[i]=group!=null?group:focusPanels[i].AddComponent<CanvasGroup>();
                }
                if(hudPanel!=null){hudFocusGroup=hudPanel.GetComponent<CanvasGroup>();if(hudFocusGroup==null)hudFocusGroup=hudPanel.AddComponent<CanvasGroup>();}
            }
            GameObject top=null;
            foreach(var panel in focusPanels)
                if(Active(panel)&&(top==null||panel.transform.GetSiblingIndex()>top.transform.GetSiblingIndex()))top=panel;
            for(int i=0;i<focusPanels.Length;i++)
            {
                var group=focusGroups[i];if(group==null)continue;
                group.interactable=focusPanels[i]==top;
                group.blocksRaycasts=true;
            }
            focusedPanel=top;
            if(hudFocusGroup!=null)
            {
                hudFocusGroup.alpha=top==null?1:0;hudFocusGroup.blocksRaycasts=top==null;
            }
            if(EventSystem.current==null)return;
            if(top==null)
            {
                var selected=EventSystem.current.currentSelectedGameObject;
                if(selected!=null&&selected.transform.IsChildOf(designRoot))EventSystem.current.SetSelectedGameObject(null);
                return;
            }
            if(focus||!HasPanelFocus(top))FocusPanel(top);
        }

        bool HasPanelFocus(GameObject panel)
        {
            var selected=EventSystem.current.currentSelectedGameObject;
            if(selected==null||!selected.activeInHierarchy||!selected.transform.IsChildOf(panel.transform))return false;
            if(selected!=selectedControlObject){selectedControlObject=selected;selectedControl=selected.GetComponent<Selectable>();}
            return selectedControl!=null&&selectedControl.IsActive()&&selectedControl.IsInteractable();
        }

        void LateUpdate()
        {
            // One parent CanvasGroup hides all HUD, inventory, radio, prompts
            // and notifications together for exactly the full-screen scare.
            bool hidden=DeadAirScarePresentation.FullscreenActive;
            if(interfaceGroup!=null&&hidden!=interfaceHidden)
            {
                interfaceGroup.alpha=hidden?0:1;
                interfaceGroup.interactable=!hidden;interfaceGroup.blocksRaycasts=!hidden;
                interfaceHidden=hidden;
            }
            if(hidden)return;
            // Clicking blank modal space clears Unity's selection. Restore it only
            // within an active menu, leaving the gameplay keyboard untouched.
            if(EventSystem.current!=null&&Active(focusedPanel)&&!HasPanelFocus(focusedPanel))FocusPanel(focusedPanel);
        }
    }

    public sealed class DeadAirChapterCard : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
    {
        RectTransform card;Image border,marker;Vector2 restingPosition;
        bool playable,chosen,pointer,focused,animating;

        public void Initialize(RectTransform visual,Image edge,Image mark,bool available)
        {
            card=visual;border=edge;marker=mark;playable=available;
            restingPosition=card.anchoredPosition;Paint(true);
        }

        public void SetChosen(bool selected){bool next=selected&&playable;if(chosen==next)return;chosen=next;animating=true;Paint(!isActiveAndEnabled);}
        void Update(){if(animating)Paint(false);}
        void OnEnable(){pointer=focused=false;Paint(true);}
        void OnDisable(){pointer=focused=false;}
        void Paint(bool snap)
        {
            if(card==null)return;
            bool hover=playable&&(pointer||focused);
            float blend=snap?1:1-Mathf.Exp(-Time.unscaledDeltaTime*14);
            Vector2 target=restingPosition+Vector2.up*(chosen?23:0)+Vector2.up*(hover?5:0);
            card.anchoredPosition=Vector2.Lerp(card.anchoredPosition,target,blend);
            float size=(chosen?1.035f:1)*(hover?1.012f:1);
            card.localScale=Vector3.Lerp(card.localScale,Vector3.one*size,blend);
            Quaternion rotation=Quaternion.Euler(0,0,chosen?1.1f:-1.25f);
            card.localRotation=Quaternion.Slerp(card.localRotation,rotation,blend);
            border.color=chosen?new Color(.65f,.38f,.28f):new Color(.26f,.23f,.21f);
            marker.color=chosen?new Color(.85f,.70f,.55f):new Color(.38f,.37f,.35f);
            if(snap||((card.anchoredPosition-target).sqrMagnitude<.0001f&&(card.localScale-Vector3.one*size).sqrMagnitude<1e-10f&&Quaternion.Angle(card.localRotation,rotation)<.01f))
            {card.anchoredPosition=target;card.localScale=Vector3.one*size;card.localRotation=rotation;animating=false;}
        }
        public void OnPointerEnter(PointerEventData e){pointer=true;animating=true;}
        public void OnPointerExit(PointerEventData e){pointer=false;animating=true;}
        public void OnSelect(BaseEventData e){focused=true;animating=true;}
        public void OnDeselect(BaseEventData e){focused=false;animating=true;}
    }

    public sealed class DeadAirMenuHover : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
    {
        Text label;Image arrow;Color normal,selected;bool pointer,focused;
        public void Initialize(Text text,Image mark,Color ink,Color red){label=text;arrow=mark;normal=ink;selected=red;Paint();}
        void Paint(){if(label==null)return;label.color=pointer||focused?selected:normal;arrow.enabled=pointer||focused;}
        public void OnPointerEnter(PointerEventData e){pointer=true;Paint();}
        public void OnPointerExit(PointerEventData e){pointer=false;Paint();}
        public void OnSelect(BaseEventData e){focused=true;Paint();}
        public void OnDeselect(BaseEventData e){focused=false;Paint();}
        void OnDisable(){pointer=focused=false;Paint();}
    }
    public sealed class DeadAirUITextureOwner : MonoBehaviour
    {
        public Texture2D texture;
        void OnDestroy(){if(texture!=null)Destroy(texture);}
    }
}
