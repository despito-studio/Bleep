using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    public sealed class DeadAirWorld
    {
        readonly WorldData data;
        readonly Transform root;
        readonly Dictionary<string, Transform> interactables = new Dictionary<string, Transform>();
        readonly Dictionary<string, Transform> doorViews = new Dictionary<string, Transform>();
        readonly Dictionary<string, DeadAirHingedDoor> doorAnimations = new Dictionary<string, DeadAirHingedDoor>();
        readonly Dictionary<string, DeadAirHingedDoor> lockerAnimations = new Dictionary<string, DeadAirHingedDoor>();
        readonly HashSet<string> occupiedLockers = new HashSet<string>();
        readonly Dictionary<string, Transform> stateViews = new Dictionary<string, Transform>();
        readonly Dictionary<string, Quaternion> interactionRotations = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Renderer> doorLeds = new Dictionary<string, Renderer>();
        readonly Dictionary<string, Light> doorLights = new Dictionary<string, Light>();
        readonly List<Transform> fans = new List<Transform>();
        readonly List<Renderer> monitors = new List<Renderer>();
        readonly List<Practical> lamps = new List<Practical>();
        readonly GameObject anchor, bloodyAnchor;
        readonly Dictionary<string, GameObject> droppedViews = new Dictionary<string, GameObject>();
        readonly HashSet<string> activeDropped = new HashSet<string>();
        readonly List<string> droppedToRemove = new List<string>();
        Vector3 lastEnemyPosition;
        bool lastPower, lastBroadcast;
        readonly DeadAirSky sky;
        readonly DeadAirCulling culling;
        sealed class Practical { public Light light; public float intensity; public Color color; }

        public DeadAirWorld(WorldData data)
        {
            this.data = data;
            var sceneRoot=GameObject.Find("BLACKPINE / original web scene");
            root = sceneRoot!=null?sceneRoot.transform:new GameObject("BLACKPINE / original web scene").transform;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.095f,.12f,.165f);
            RenderSettings.ambientEquatorColor = new Color(.045f,.052f,.067f);
            RenderSettings.ambientGroundColor = new Color(.025f,.026f,.033f);
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            ColorUtility.TryParseHtmlString("#101621",out var night);
            RenderSettings.fogColor = night;
            RenderSettings.fogDensity = .014f;
            QualitySettings.pixelLightCount = 4;
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadowDistance = 45f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            if(root.Find("station")==null) Instantiate("station").name="station";
            if(root.Find("exterior")==null) Instantiate("exterior").name="exterior";
            CalibrateExteriorLighting(root.Find("exterior"));
            var dynamicRoots = new HashSet<Transform>();
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(t.name.StartsWith("interactive:"))
                {
                    string id=t.name.Substring(12);interactables[id]=t;interactionRotations[id]=t.localRotation;dynamicRoots.Add(t);
                }
                else if(t.name.StartsWith("door:"))
                {
                    string id=t.name.Substring(5);
                    doorViews[id] = t; dynamicRoots.Add(t);
                    var hinge=t.GetComponent<DeadAirHingedDoor>()??t.gameObject.AddComponent<DeadAirHingedDoor>();
                    var specification=data.Door(id);
                    hinge.Initialize(specification!=null&&Mathf.Abs(specification.openAngle)>.1f?specification.openAngle:90);
                    doorAnimations[id]=hinge;
                }
                else if(t.name.StartsWith("locker-door:"))
                {
                    string id=t.name.Substring(12);dynamicRoots.Add(t);
                    var hinge=t.GetComponent<DeadAirHingedDoor>()??t.gameObject.AddComponent<DeadAirHingedDoor>();
                    hinge.Initialize(-105);lockerAnimations[id]=hinge;
                }
                else if(t.name.StartsWith("door-led:")) doorLeds[t.name.Substring(9)] = t.GetComponent<Renderer>();
                else if(t.name.StartsWith("door-light:")) doorLights[t.name.Substring(11)] = t.GetComponent<Light>();
                else if(t.name.StartsWith("state:")) { stateViews[t.name.Substring(6)] = t; dynamicRoots.Add(t); }
                else if(t.name.StartsWith("fan:")) { fans.Add(t); dynamicRoots.Add(t); }
                else if(t.name.StartsWith("monitor:")) monitors.Add(t.GetComponent<Renderer>());
                else if(t.name.StartsWith("lamp:"))
                {
                    var l=t.GetComponent<Light>();lamps.Add(new Practical {light=l,intensity=l.intensity,color=l.color});
                }
            }
            sky = new DeadAirSky(root);
            culling = new DeadAirCulling(data,root.Find("station"),root.Find("exterior"),dynamicRoots,doorAnimations);
            anchor=Instantiate("monster");bloodyAnchor=Instantiate("monster_bloody");
            anchor.SetActive(false);bloodyAnchor.SetActive(false);
        }

        static void CalibrateExteriorLighting(Transform exterior)
        {
            // The source's broad yard and van lamps use distance^1.5 attenuation.
            // Unity's built-in falloff retains much more light across their ranges;
            // keep these two lamps local and leave the interior practicals unchanged.
            foreach(var light in exterior.GetComponentsInChildren<Light>(true))
            {
                if(light.type==LightType.Spot && light.range>25)
                {
                    light.intensity=100f*.045f*.55f;
                    light.renderMode = LightRenderMode.Auto;
                }
                else if(light.type==LightType.Point && light.range>25)
                {
                    light.intensity=73f*.045f*.45f;
                    light.renderMode = LightRenderMode.Auto;
                }
            }
        }

        GameObject Instantiate(string id)
        {
            var prefab=Resources.Load<GameObject>("World/"+id);
            if(prefab==null)throw new InvalidOperationException("Missing textured scene asset: "+id+". Run Dead Air / Import Complete Web Scene.");
            return UnityEngine.Object.Instantiate(prefab,root,false);
        }

        public void Apply(RoomState state)
        {
            if(state==null) return;
            foreach(var door in state.doors)
            {
                if(doorAnimations.TryGetValue(door.id,out var animation))animation.SetOpen(door.open);
                var color=door.locked?new Color(.88f,.27f,.20f):door.open?new Color(.31f,.78f,.47f):new Color(.84f,.74f,.47f);
                if(doorLeds.TryGetValue(door.id,out var led)&&led!=null) SetTint(led,color);
                if(doorLights.TryGetValue(door.id,out var light)&&light!=null)light.color=color;
            }
            float animationDelta=state.paused?0:Time.deltaTime;
            foreach(var animation in doorAnimations.Values)animation.Advance(animationDelta);
            foreach(var entry in lockerAnimations)
            {
                bool occupied=state.players.Exists(p=>p.hiding==entry.Key);
                if(occupied!=occupiedLockers.Contains(entry.Key))
                {
                    entry.Value.PlayAccessCycle();
                    if(occupied)occupiedLockers.Add(entry.Key);else occupiedLockers.Remove(entry.Key);
                }
                entry.Value.Advance(animationDelta);
            }
            foreach(var obj in data.objects)
            {
                if(!interactables.TryGetValue(obj.id,out var view))continue;
                if(obj.type=="tape"||obj.type=="fuse"||obj.type=="keycard")view.gameObject.SetActive(!state.Collected(obj.id));
                if(obj.id=="fuel-canister")view.gameObject.SetActive(!state.canister&&!state.quest.fuelAdded);
                if(obj.id=="pressure-valve")view.localRotation=interactionRotations[obj.id]*Quaternion.Euler(0,0,state.valveOpened?180:0);
            }
            bool flicker=DeadAirSettings.Flicker;
            for(int i=0;i<lamps.Count;i++)
            {
                var lamp=lamps[i];float time=Time.time+i*3.77f;
                lamp.light.color=state.broadcast?new Color(1,.082f,.02f):lamp.color;
                float wave=flicker?Mathf.Sin(time*1.7f)*Mathf.Sin(time*13.6f):0;
                lamp.light.intensity=lamp.intensity*(state.broadcast?(flicker?.7f+Mathf.Sin(Time.time*3)*.2f:.8f):wave>.94f?.15f:.91f);
            }
            if(state.power)
                foreach(var fan in fans)fan.Rotate(0,0,-Time.deltaTime*14*Mathf.Rad2Deg,Space.Self);
            if(stateViews.TryGetValue("powerLever",out var lever))lever.localRotation=Quaternion.Euler(0,0,-(state.power?-.7f:.55f)*Mathf.Rad2Deg);
            if(lastPower!=state.power||lastBroadcast!=state.broadcast)
            {
                TintState("powerLamp",state.power?new Color(.53f,.78f,.53f):new Color(.88f,.32f,.17f));
                TintState("broadcastBulb",state.broadcast?new Color(.87f,.13f,.067f):new Color(.58f,.27f,.16f));
                TintState("exitLamp",state.broadcast?new Color(.2f,1,.33f):new Color(.87f,.2f,.13f));
                foreach(var monitor in monitors)SetTint(monitor,state.power?Color.white:new Color(.39f,.47f,.41f));
                lastPower=state.power;lastBroadcast=state.broadcast;
            }
            anchor.SetActive(state.enemy.active&&!state.broadcast);
            bloodyAnchor.SetActive(state.enemy.active&&state.broadcast);
            if(state.enemy.active)
            {
                var monster=state.broadcast?bloodyAnchor:anchor;
                var position=new Vector3(-state.enemy.x,state.enemy.y,state.enemy.z);
                var direction=position-lastEnemyPosition;
                if(direction.sqrMagnitude>.0001f)monster.transform.rotation=Quaternion.LookRotation(direction);
                monster.transform.position=position;
                lastEnemyPosition=position;
            }
            ApplyDropped(state);
            var cam = Camera.main;
            var camPos = cam != null ? cam.transform.position : new Vector3(5f, 1.65f, 19f);
            if (sky != null) sky.UpdatePosition(camPos);
            if (culling != null) culling.Update(state, camPos);
        }

        void ApplyDropped(RoomState state)
        {
            activeDropped.Clear();
            if (state.droppedItems != null)
            {
                for (int i = 0; i < state.droppedItems.Count; i++)
                {
                    var item = state.droppedItems[i];
                    activeDropped.Add(item.id);
                    if (!droppedViews.TryGetValue(item.id, out var view) || view == null)
                    {
                        view = CreateDroppedView(item);
                        droppedViews[item.id] = view;
                    }
                    if (view != null)
                    {
                        view.transform.position = new Vector3(-item.x, item.y + 0.15f, item.z);
                    }
                }
            }
            droppedToRemove.Clear();
            foreach (var id in droppedViews.Keys)
            {
                if (!activeDropped.Contains(id)) droppedToRemove.Add(id);
            }
            for (int i = 0; i < droppedToRemove.Count; i++)
            {
                var id = droppedToRemove[i];
                if (droppedViews.TryGetValue(id, out var view) && view != null)
                {
                    UnityEngine.Object.Destroy(view);
                }
                droppedViews.Remove(id);
            }
        }

        GameObject CreateDroppedView(DroppedItem item)
        {
            string type = string.IsNullOrEmpty(item.itemId) ? item.id : item.itemId;
            GameObject view = null;
            if (type == "flashlight")
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                view.name = "dropped_flashlight_" + item.id;
                view.transform.SetParent(root, false);
                view.transform.localScale = new Vector3(0.08f, 0.16f, 0.08f);
                view.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 90);
                var ren = view.GetComponent<Renderer>();
                if (ren != null) ren.material.color = new Color(0.18f, 0.18f, 0.18f);
                var lightObj = new GameObject("dropped_light");
                lightObj.transform.SetParent(view.transform, false);
                var l = lightObj.AddComponent<Light>();
                lightObj.transform.localPosition=new Vector3(0,1.05f,0);
                lightObj.transform.localRotation=Quaternion.Euler(-90,0,0);
                l.type = LightType.Spot;
                l.range = 15;
                l.spotAngle=48;l.innerSpotAngle=23;
                l.intensity = 1.55f;
                l.shadows=LightShadows.Hard;l.shadowBias=.015f;l.shadowNormalBias=.025f;
                l.color = new Color(1f, .96f, .88f);
            }
            else if (interactables.TryGetValue(type, out var orig) && orig != null)
            {
                view = UnityEngine.Object.Instantiate(orig.gameObject, root, false);
                view.name = "dropped_" + type + "_" + item.id;
                view.SetActive(true);
            }
            else
            {
                view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                view.name = "dropped_prop_" + item.id;
                view.transform.SetParent(root, false);
                view.transform.localScale = new Vector3(0.25f, 0.12f, 0.25f);
                var ren = view.GetComponent<Renderer>();
                if (ren != null) ren.material.color = type.StartsWith("keycard") ? new Color(0.3f, 0.7f, 1f) : new Color(0.8f, 0.8f, 0.3f);
            }
            foreach (var col in view.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(col);
            return view;
        }
        static readonly MaterialPropertyBlock sharedBlock = new MaterialPropertyBlock();
        static void SetTint(Renderer renderer,Color color)
        {
            if(renderer==null)return;
            renderer.GetPropertyBlock(sharedBlock);
            sharedBlock.SetColor("_Color",color);
            renderer.SetPropertyBlock(sharedBlock);
        }
        void TintState(string id,Color color)
        {
            if(!stateViews.TryGetValue(id,out var view))return;
            var renderer=view.GetComponent<Renderer>();if(renderer==null)return;
            renderer.GetPropertyBlock(sharedBlock);
            sharedBlock.SetColor("_Color",color);
            sharedBlock.SetColor("_EmissionColor",color);
            renderer.SetPropertyBlock(sharedBlock);
        }
    }
}
