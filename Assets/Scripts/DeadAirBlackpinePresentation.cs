using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    // Presentation consumes the host snapshot; all progress and echoes originate in Rules.
    public sealed class DeadAirBlackpinePresentation : MonoBehaviour
    {
        static DeadAirBlackpinePresentation current;
        WorldData world;
        DeadAirSession session;
        Transform antenna, resonator;
        Renderer antennaLamp, resonatorLamp;
        Transform tuningNeedle, resonatorRotor;
        AudioSource recordingVoice, radioVoice;
        readonly Dictionary<string,AudioClip> recordings = new Dictionary<string,AudioClip>();
        readonly List<Material> materials = new List<Material>();
        string run;
        float lastElapsed;
        int evidenceSerial, echoSerial, radioSerial;
        bool intercepted, stopped;
        GameObject contents;
        Material metal, dark, brass, porcelain, amber, cyan;

        public void Initialize(WorldData data, DeadAirSession game)
        {
            current = this;
            world = data; session = game;
            contents = new GameObject("Blackpine signal machinery");
            contents.transform.SetParent(transform,false);
            metal = Surface("Cold galvanized steel",new Color(.30f,.36f,.38f));
            dark = Surface("Aged instrument enamel",new Color(.08f,.13f,.14f));
            brass = Surface("Copper contacts",new Color(.47f,.27f,.10f));
            porcelain = Surface("Porcelain insulator",new Color(.75f,.76f,.66f));
            amber = Surface("Amber signal lamp",new Color(.74f,.27f,.06f),true);
            cyan = Surface("Green receiver lamp",new Color(.24f,.74f,.61f),true);
            BuildAntenna(); BuildResonator();
            var office=GameObject.Find("BLACKPINE / original web scene");
            if(office!=null)
                foreach(var lettering in office.GetComponentsInChildren<TextMesh>(true))
                {
                    if(lettering.text=="96.4 MHz / TRANSMIT")lettering.text="RX / ПЕРЕХОПЛЕННЯ";
                    else if(lettering.text.Contains("СХЕМА ПЕРЕДАЧІ"))lettering.text="СХЕМА ПРИЙОМУ / АВАРІЙНА НЕСУЧА";
                    else continue;
                    lettering.GetComponent<DeadAirOfficeSign>()?.FitToPlaque();
                }
            recordingVoice = Voice("Recovered cassette audio",false);
            radioVoice = Voice("Crew radio / impossible reply",true);
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Blackpine")) recordings[clip.name]=clip;
            contents.SetActive(false);
        }

        Material Surface(string name,Color color,bool glow=false)
        {
            var shader=Shader.Find("Dead Air/Source Surface")??Shader.Find("Standard");
            var material=new Material(shader){name="Blackpine / "+name};
            material.SetColor("_Color",color);
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.22f);
            if(glow && material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",color*.65f);
            materials.Add(material);return material;
        }

        AudioSource Voice(string name,bool filtered)
        {
            var voice=new GameObject(name).AddComponent<AudioSource>();
            voice.transform.SetParent(transform,false);voice.playOnAwake=false;
            voice.spatialBlend=0;voice.priority=20;voice.dopplerLevel=0;
            if(filtered)
            {
                var lowpass=voice.gameObject.AddComponent<AudioLowPassFilter>();lowpass.cutoffFrequency=2600;
                var highpass=voice.gameObject.AddComponent<AudioHighPassFilter>();highpass.cutoffFrequency=450;
            }
            return voice;
        }

        Transform Part(Transform parent,string name,Vector3 position,Vector3 scale,Material material,PrimitiveType shape=PrimitiveType.Cube)
        {
            var part=GameObject.CreatePrimitive(shape);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;
            var collider=part.GetComponent<Collider>();if(collider!=null){collider.enabled=false;Destroy(collider);}
            var renderer=part.GetComponent<Renderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            return part.transform;
        }

        void Label(Transform parent,string text,Vector3 at,float size)
        {
            var label=new GameObject(text).AddComponent<TextMesh>();label.transform.SetParent(parent,false);
            label.transform.localPosition=at;label.text=text;label.fontSize=40;label.characterSize=size;
            label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=new Color(.77f,.83f,.73f);
            var font=Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
            if(font!=null){label.font=font;label.GetComponent<Renderer>().sharedMaterial=font.material;}
        }

        void BuildAntenna()
        {
            var point=DeadAirBlackpine.Objective(world,DeadAirBlackpine.AntennaId);
            antenna=new GameObject("interactive:"+DeadAirBlackpine.AntennaId).transform;antenna.SetParent(contents.transform,false);
            antenna.position=new Vector3(-point.x,point.y-1.15f,point.z);
            Part(antenna,"Concrete footing",new Vector3(0,.16f,0),new Vector3(1.2f,.32f,1),metal);
            Part(antenna,"Antenna mast",new Vector3(0,3.2f,.1f),new Vector3(.12f,3.2f,.12f),metal,PrimitiveType.Cylinder);
            for(int i=0;i<4;i++)
                Part(antenna,"Dipole "+i,new Vector3(0,4.1f+i*.58f,.1f),new Vector3(1.5f-i*.16f,.05f,.05f),metal);
            Part(antenna,"Receiver junction housing",new Vector3(0,1.08f,-.12f),new Vector3(.82f,1.14f,.42f),dark);
            Part(antenna,"Service panel rim",new Vector3(0,1.12f,-.35f),new Vector3(.72f,.94f,.035f),metal);
            Part(antenna,"Panel recess",new Vector3(0,1.14f,-.374f),new Vector3(.63f,.77f,.024f),dark);
            for(int i=0;i<3;i++)
            {
                Part(antenna,"Ceramic contact "+i,new Vector3((i-1)*.19f,1.25f,-.4f),new Vector3(.08f,.14f,.08f),porcelain,PrimitiveType.Cylinder);
                Part(antenna,"Copper bridge "+i,new Vector3((i-1)*.19f,1.1f,-.41f),new Vector3(.045f,.2f,.045f),brass);
            }
            tuningNeedle=Part(antenna,"Disconnect handle",new Vector3(0,.91f,-.44f),new Vector3(.46f,.065f,.075f),brass);
            antennaLamp=Part(antenna,"Antenna status lamp",new Vector3(.22f,1.44f,-.43f),Vector3.one*.07f,amber,PrimitiveType.Sphere).GetComponent<Renderer>();
            Label(antenna,"RX / АНТЕНА",new Vector3(0,1.63f,-.39f),.014f);
        }

        void BuildResonator()
        {
            var point=DeadAirBlackpine.Objective(world,DeadAirBlackpine.ResonatorId);
            resonator=new GameObject("interactive:"+DeadAirBlackpine.ResonatorId).transform;resonator.SetParent(contents.transform,false);
            resonator.position=new Vector3(-point.x,point.y-1.15f,point.z);
            resonator.rotation=Quaternion.Euler(0,180,0);
            Part(resonator,"Rubber base",new Vector3(0,.09f,0),new Vector3(1.05f,.18f,.7f),dark);
            Part(resonator,"Resonator cabinet",new Vector3(0,1.1f,0),new Vector3(.94f,2.02f,.6f),metal);
            Part(resonator,"Bakelite face",new Vector3(0,1.16f,-.315f),new Vector3(.85f,1.78f,.035f),dark);
            for(int i=0;i<9;i++)Part(resonator,"Cooling slit "+i,new Vector3(0,.38f+i*.035f,-.344f),new Vector3(.67f,.012f,.01f),metal);
            for(int i=0;i<3;i++)
            {
                float x=(i-1)*.26f;
                Part(resonator,"Ceramic tube mount",new Vector3(x,1.2f,-.38f),new Vector3(.19f,.09f,.19f),porcelain,PrimitiveType.Cylinder);
                Part(resonator,"Copper resonant coil",new Vector3(x,1.43f,-.39f),new Vector3(.11f,.24f,.11f),brass,PrimitiveType.Cylinder);
                for(int ring=0;ring<5;ring++)Part(resonator,"Coil ring",new Vector3(x,1.24f+ring*.085f,-.39f),new Vector3(.17f,.018f,.17f),metal,PrimitiveType.Cylinder);
            }
            Part(resonator,"Meter bezel",new Vector3(0,1.9f,-.35f),new Vector3(.66f,.28f,.025f),porcelain);
            resonatorRotor=Part(resonator,"Resonance meter needle",new Vector3(0,1.88f,-.375f),new Vector3(.012f,.19f,.012f),dark);
            resonatorLamp=Part(resonator,"Resonator live lamp",new Vector3(.31f,.79f,-.39f),Vector3.one*.07f,amber,PrimitiveType.Sphere).GetComponent<Renderer>();
            Part(resonator,"Emergency switch",new Vector3(-.14f,.78f,-.42f),new Vector3(.28f,.09f,.10f),brass);
            Label(resonator,"РЕЗОНАТОР-4",new Vector3(0,2.2f,-.34f),.016f);
        }

        void Update()
        {
            var state=session.State;
            bool active=DeadAirBlackpine.Active(state)&&(state.phase=="playing"||state.phase=="departing");
            if(contents.activeSelf!=active)contents.SetActive(active);
            if(!active){recordingVoice.Stop();radioVoice.Stop();return;}
            string identity=state.code;
            if(run!=identity || state.elapsed<lastElapsed)
            {run=identity;evidenceSerial=echoSerial=radioSerial=0;intercepted=stopped=false;recordingVoice.Stop();radioVoice.Stop();}
            lastElapsed=state.elapsed;
            var chapter=state.blackpine;if(chapter==null)return;
            bool paused=state.paused;
            if(paused){recordingVoice.Pause();radioVoice.Pause();}
            else{recordingVoice.UnPause();radioVoice.UnPause();}
            float volume=PlayerPrefs.GetInt("DeadAir.Muted",0)!=0?0:PlayerPrefs.GetFloat("DeadAir.Volume",.5f)*PlayerPrefs.GetFloat("DeadAir.EffectsVolume",1);
            recordingVoice.volume=volume*.95f;radioVoice.volume=volume*.9f;
            if(chapter.evidenceSerial!=evidenceSerial)
            {
                evidenceSerial=chapter.evidenceSerial;
                if(state.elapsed-chapter.evidenceAt<24)PlayEvidence(chapter.evidenceId,state);
            }
            if(state.radioSequence!=radioSerial)
            {
                radioSerial=state.radioSequence;
                PlayRadio(state,chapter.lastRadioPlayerId,chapter.lastRadioPhrase,false);
            }
            if(chapter.echoSerial!=echoSerial)
            {
                echoSerial=chapter.echoSerial;
                if(state.elapsed-chapter.echoAt<6)PlayRadio(state,chapter.echoPlayerId,chapter.echoPhrase,true);
            }
            if(chapter.intercepted&&!intercepted)
            {
                DeadAirAudio.Play("double-knock");
                var receiver=world.Objective("broadcast");
                if(receiver!=null)DeadAirAudio.PlayAt("calibrate",new Vector3(-receiver.x,receiver.y,receiver.z),.9f);
            }
            if(chapter.resonatorStopped&&!stopped){DeadAirAudio.PlayAt("breaker-toggle",resonator.position+Vector3.up,.9f);recordingVoice.Stop();}
            intercepted=chapter.intercepted;stopped=chapter.resonatorStopped;
            antennaLamp.sharedMaterial=chapter.antennaDisconnected?cyan:amber;
            resonatorLamp.sharedMaterial=chapter.resonatorStopped?cyan:amber;
            tuningNeedle.localRotation=Quaternion.Euler(0,0,chapter.antennaDisconnected?75:0);
            if(!paused)resonatorRotor.localRotation=Quaternion.Euler(0,0,chapter.resonatorStopped?65:Mathf.Sin(state.elapsed*7)*22);
        }

        public static void PlayEvidence(string id,RoomState state)
        {
            if(current==null || string.IsNullOrEmpty(id))return;
            string key=id;
            if(id=="tape-3")key+="-"+Mathf.RoundToInt(DeadAirBlackpine.TargetFrequency(state)*10);
            if(current.recordings.TryGetValue(key,out var clip))
            {current.recordingVoice.clip=clip;current.recordingVoice.pitch=1;current.recordingVoice.Play();}
            DeadAirAudio.Play("cassette");
        }

        void PlayRadio(RoomState state,string actorId,int phrase,bool echo)
        {
            var actor=state.Player(actorId);
            bool female=actor!=null&&(actor.characterClass=="operator"||actor.characterClass=="dispatcher");
            string key="radio-"+(female?"f":"m")+"-"+Mathf.Clamp(phrase,0,3);
            if(recordings.TryGetValue(key,out var clip))
            {radioVoice.clip=clip;radioVoice.pitch=echo?.90f:1;radioVoice.Play();}
            DeadAirAudio.Play("radio");
        }

        void OnDestroy()
        {
            if(current==this)current=null;
            foreach(var material in materials)if(material!=null)Destroy(material);
        }
    }
}
