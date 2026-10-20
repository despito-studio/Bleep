using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir
{
    // Room groups replace the fixed, one-storey coordinate sectors. Structural
    // surfaces stay enabled so they continue to occlude rooms and cast shadows.
    public sealed class DeadAirCulling
    {
        sealed class Sector
        {
            public Bounds bounds;
            public Renderer[] renderers;
            public ShadowCastingMode[] shadowModes;
            public Bounds[] rendererBounds;
            public float[] rendererExtents;
            public bool[] dynamicBounds;
            public bool[] shadowOnly;
            public bool[] lodCulled;
            public Bounds casterBounds;
            public bool hasCaster;
            public LightVolume[] lights;
            public bool visible = true;
            public bool nextVisible, keepShadows;
            public Vector3 lastEye;
        }
        sealed class LightVolume
        {
            public Light light;
            public Bounds bounds;
            public bool ceilingFixture;
            public int floor;
            public RoomData room;
            public LightShadows shadows;
            public float priority;
            // A lamp behind a wall is resolved with up to three raycasts. Its
            // answer only changes when a door moves or the eye travels, so it is
            // reused briefly instead of being sampled on every visibility tick.
            public bool visibilityCached;
            public Vector3 visibilityEye;
            public float visibilityTime;
            public bool visibilityResult;
        }
        readonly List<Sector> sectors = new List<Sector>();
        readonly Plane[] planes = new Plane[6];
        readonly Plane[] reflectionPlanes = new Plane[6];
        readonly WorldData world;
        readonly List<LightVolume> shadowCandidates=new List<LightVolume>();
        readonly List<LightVolume> enabledCandidates=new List<LightVolume>(256);
        static readonly RaycastHit[] occlusionHits=new RaycastHit[32];
        float nextUpdate;
        public int VisibilityRaycasts { get; private set; }
        public int EnabledPracticalCount { get; private set; }
        public int ShadowPracticalCount { get; private set; }
        public int ShadowOnlyRendererCount { get; private set; }

        public DeadAirCulling(WorldData world, Transform station, Transform exterior,
            HashSet<Transform> dynamicRoots, Dictionary<string, DeadAirHingedDoor> doors)
        {
            this.world=world;
            if (station == null) return;
            foreach (var group in station.GetComponentsInChildren<Transform>(true))
            {
                if (!group.name.StartsWith("sector:")) continue;
                var renderers = group.GetComponentsInChildren<Renderer>(true);
                var lights = group.GetComponentsInChildren<Light>(true);
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var shadowModes=new ShadowCastingMode[renderers.Length];
                var rendererBounds=new Bounds[renderers.Length];
                var rendererExtents=new float[renderers.Length];
                var dynamicBounds=new bool[renderers.Length];
                Bounds casterBounds=new Bounds();bool hasCaster=false;
                for(int i=0;i<renderers.Length;i++)
                {
                    rendererBounds[i]=renderers[i].bounds;
                    var size=rendererBounds[i].size;
                    rendererExtents[i]=Mathf.Max(size.x,Mathf.Max(size.y,size.z));
                    for(var owner=renderers[i].transform;owner!=null&&owner!=station;owner=owner.parent)
                        if(dynamicRoots.Contains(owner)){dynamicBounds[i]=true;break;}
                    shadowModes[i]=renderers[i].shadowCastingMode;
                    if(shadowModes[i]==ShadowCastingMode.Off)continue;
                    if(!hasCaster){casterBounds=renderers[i].bounds;hasCaster=true;}
                    else casterBounds.Encapsulate(renderers[i].bounds);
                }
                foreach (var light in lights) bounds.Encapsulate(light.transform.position);
                // Account for moving door leaves and illumination across doorways.
                bounds.Expand(new Vector3(4, 1, 4));
                var volumes=new LightVolume[lights.Length];
                for(int i=0;i<lights.Length;i++)
                {
                    var light=lights[i];
                    volumes[i]=new LightVolume {light=light,bounds=IlluminationBounds(light),
                        ceilingFixture=light.name.StartsWith("lamp:zone-")||light.name.StartsWith("lamp:bounce-zone-"),
                        floor=Mathf.FloorToInt(light.transform.position.y/DeadAirOfficeLayout.FloorHeight),shadows=light.shadows};
                    var volume=volumes[i];
                    if(volume.ceilingFixture)
                    {
                        foreach(var room in world.rooms)
                            if(Mathf.Abs(room.floorY-volume.floor*DeadAirOfficeLayout.FloorHeight)<.01f &&
                                Contains(room,light.transform.position)){volume.room=room;break;}
                        light.shadowCustomResolution=512;
                    }
                }
                sectors.Add(new Sector { bounds = bounds, renderers = renderers, shadowModes=shadowModes,
                    rendererBounds=rendererBounds,rendererExtents=rendererExtents,dynamicBounds=dynamicBounds,
                    shadowOnly=new bool[renderers.Length],lodCulled=new bool[renderers.Length],
                    casterBounds=casterBounds,hasCaster=hasCaster,lights = volumes });
            }
        }

        static bool Contains(RoomData room,Vector3 position) => -position.x>=room.minX && -position.x<=room.maxX &&
            position.z>=room.minZ && position.z<=room.maxZ;

        bool ClearSample(Vector3 origin,Vector3 sample)
        {
            var ray=sample-origin;float distance=ray.magnitude;
            if(distance<.05f)return true;
            VisibilityRaycasts++;
            int count=Physics.RaycastNonAlloc(origin,ray/distance,occlusionHits,distance-.03f,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var collider=occlusionHits[i].collider;if(collider==null)continue;
                if(collider is CharacterController || collider.GetComponentInParent<DeadAirPlayer>()!=null ||
                    collider.GetComponentInParent<DeadAirCharacterMotion>()!=null)continue;
                return false;
            }
            return true;
        }

        bool VisiblePractical(LightVolume volume,Vector3 cameraPosition,int cameraFloor)
        {
            if(!volume.ceilingFixture)return true;
            if(volume.room!=null && volume.floor==cameraFloor && Contains(volume.room,cameraPosition))return true;
            float now=Time.unscaledTime;
            if(volume.visibilityCached && now-volume.visibilityTime<.3f &&
                (volume.visibilityEye-cameraPosition).sqrMagnitude<1.44f)return volume.visibilityResult;
            var position=volume.light.transform.position;
            // A closed office wall/door hides the lamp's entire light pool. Sample
            // multiple heights so an open doorway or a desk does not hide it falsely.
            bool visible=ClearSample(cameraPosition,position) ||
                ClearSample(cameraPosition,position+Vector3.down*1.2f) ||
                ClearSample(cameraPosition,new Vector3(position.x,volume.floor*DeadAirOfficeLayout.FloorHeight+.4f,position.z));
            volume.visibilityCached=true;volume.visibilityEye=cameraPosition;
            volume.visibilityTime=now;volume.visibilityResult=visible;
            return visible;
        }

        static Bounds IlluminationBounds(Light light)
        {
            float range=light.range;
            if(light.type!=LightType.Spot)
                return new Bounds(light.transform.position,Vector3.one*range*2);
            // Enclose the cone clipped to its spherical range. This is conservative
            // enough to keep a lamp whose bulb is outside the view but whose pool is visible.
            float radius=range*Mathf.Sin(light.spotAngle*.5f*Mathf.Deg2Rad);
            var right=light.transform.right;var up=light.transform.up;var forward=light.transform.forward;
            var extents=new Vector3(Mathf.Abs(right.x)*radius+Mathf.Abs(up.x)*radius+Mathf.Abs(forward.x)*range*.5f,
                Mathf.Abs(right.y)*radius+Mathf.Abs(up.y)*radius+Mathf.Abs(forward.y)*range*.5f,
                Mathf.Abs(right.z)*radius+Mathf.Abs(up.z)*radius+Mathf.Abs(forward.z)*range*.5f);
            return new Bounds(light.transform.position+forward*range*.5f,extents*2);
        }

        bool InteriorFloor(Vector3 position,out int floor)
        {
            floor=Mathf.FloorToInt(position.y/DeadAirOfficeLayout.FloorHeight);
            float x=-position.x,z=position.z;
            if(floor<0 || floor>1)return false;
            // Both levels remain lit when looking through the open stairwell.
            if(world.stairs!=null)
                foreach(var stair in world.stairs)
                    if(x>=stair.minX-1 && x<=stair.maxX+1 && z>=stair.minZ-1 && z<=stair.maxZ+1)return false;
            // The envelope also contains open courtyards and recesses in the
            // stepped building. Only an authored interior room implies a ceiling.
            return DeadAirOfficeLayout.InteriorAt(world,x,z,floor*DeadAirOfficeLayout.FloorHeight);
        }

        public void Update(RoomState state, Camera camera, bool force = false)
        {
            if (camera == null || !force && Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .15f;
            var cameraPosition=camera.transform.position;
            bool inside=InteriorFloor(cameraPosition,out int cameraFloor);
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            bool reflectedView=DeadAirSettings.Reflections;
            var reflections=camera.GetComponent<DeadAirPlanarReflections>();
            float reflectedFloor=cameraPosition.y>4.6f?4.2f:cameraPosition.y<-1?-4.2f:0;
            if(reflectedView&&reflections!=null)reflectedView=reflections.HasVisibleReceiver(reflectedFloor);
            if(reflectedView)
            {
                var mirror=Matrix4x4.identity;mirror.m11=-1;mirror.m13=2*reflectedFloor;
                GeometryUtility.CalculateFrustumPlanes(camera.projectionMatrix*camera.worldToCameraMatrix*mirror,reflectionPlanes);
            }
            shadowCandidates.Clear();
            enabledCandidates.Clear();
            VisibilityRaycasts=0;EnabledPracticalCount=0;ShadowPracticalCount=0;
            int shadowQuality=DeadAirSettings.ShadowQuality;
            int graphicsQuality=DeadAirSettings.GraphicsQuality;
            foreach (var sector in sectors)
            {
                float distanceSquared = sector.bounds.SqrDistance(cameraPosition);
                bool visible = distanceSquared < 9 || distanceSquared < 2500 && (GeometryUtility.TestPlanesAABB(planes, sector.bounds)||
                    reflectedView&&GeometryUtility.TestPlanesAABB(reflectionPlanes,sector.bounds));
                sector.nextVisible=visible;
                foreach(var volume in sector.lights)
                {
                    var light=volume.light;if(light==null)continue;
                    bool floorRelevant=!inside || !volume.ceilingFixture || volume.floor==cameraFloor;
                    // Test each actual light volume rather than the whole room's large
                    // 25m neighbourhood. Preserve nearby lamps behind the camera.
                    bool nearby=(light.transform.position-cameraPosition).sqrMagnitude<light.range*light.range;
                    bool relevant=floorRelevant && (nearby || GeometryUtility.TestPlanesAABB(planes,volume.bounds)||
                        reflectedView&&GeometryUtility.TestPlanesAABB(reflectionPlanes,volume.bounds));
                    // A powered-off fixture has no light pool to expose and needs
                    // neither occlusion queries nor a place in the shadow budget.
                    bool candidate=light.intensity>.0001f && relevant && VisiblePractical(volume,cameraPosition,cameraFloor);
                    if(!candidate)
                    {
                        if(light.enabled)light.enabled=false;
                        continue;
                    }
                    volume.priority=(light.transform.position-cameraPosition).sqrMagnitude;
                    // Keep the player's own room lit before any distant doorway.
                    if(volume.room!=null && volume.floor==cameraFloor && Contains(volume.room,cameraPosition))volume.priority-=1000f;
                    enabledCandidates.Add(volume);
                    if(volume.ceilingFixture && volume.shadows!=LightShadows.None)
                    {
                        if(shadowQuality>0)shadowCandidates.Add(volume);
                        else if(volume.light.shadows!=LightShadows.None)volume.light.shadows=LightShadows.None;
                    }
                }
            }
            // Deferred lighting evaluates every enabled light for every pixel tile.
            // Shading a couple of dozen practicals keeps the visible pools intact
            // while dropping the rest, which are only glimpsed through doorways.
            int lightBudget=graphicsQuality==2?18:graphicsQuality==1?12:8;
            int enabledCount=enabledCandidates.Count;
            if(enabledCount>lightBudget)
            {
                for(int i=0;i<lightBudget;i++)
                {
                    int nearest=i;
                    for(int j=i+1;j<enabledCount;j++)
                        if(enabledCandidates[j].priority<enabledCandidates[nearest].priority)nearest=j;
                    var selected=enabledCandidates[nearest];enabledCandidates[nearest]=enabledCandidates[i];enabledCandidates[i]=selected;
                }
                for(int i=lightBudget;i<enabledCount;i++)
                    if(enabledCandidates[i].light.enabled)enabledCandidates[i].light.enabled=false;
                enabledCount=lightBudget;
            }
            for(int i=0;i<Mathf.Min(lightBudget,enabledCandidates.Count);i++)
                if(!enabledCandidates[i].light.enabled)enabledCandidates[i].light.enabled=true;
            EnabledPracticalCount=enabledCount;
            // Two shadowed practicals cover the room the player stands in; a third
            // point light costs six more shadow faces per frame for little gain.
            int shadowBudget=shadowQuality==0?0:shadowQuality==2?2:1;
            // Only the nearest one/three candidates matter. Select that prefix
            // without sorting every visible fixture or allocating a comparer.
            for(int i=0;i<Mathf.Min(shadowBudget,shadowCandidates.Count);i++)
            {
                int nearest=i;
                for(int j=i+1;j<shadowCandidates.Count;j++)
                    if(shadowCandidates[j].priority<shadowCandidates[nearest].priority)nearest=j;
                var selected=shadowCandidates[nearest];shadowCandidates[nearest]=shadowCandidates[i];shadowCandidates[i]=selected;
            }
            for(int i=0;i<shadowCandidates.Count;i++)
            {
                var volume=shadowCandidates[i];
                var shadows=i<shadowBudget?volume.shadows:LightShadows.None;
                // Practicals light a small pool; a 512 map is indistinguishable from
                // 1024 here and halves their shadow pass cost.
                int resolution=512;
                if(volume.light.shadowCustomResolution!=resolution)volume.light.shadowCustomResolution=resolution;
                if(volume.light.shadows!=shadows)volume.light.shadows=shadows;
                if(shadows!=LightShadows.None)ShadowPracticalCount++;
            }
            ShadowOnlyRendererCount=0;
            float shadowReach=QualitySettings.shadowDistance+6;
            float shadowReachSquared=shadowReach*shadowReach;
            bool moonRelevant=cameraPosition.y>=-1;
            foreach(var sector in sectors)
            {
                bool keepShadows=false;
                if(!sector.nextVisible&&sector.hasCaster&&shadowQuality>0)
                {
                    if(sector.casterBounds.SqrDistance(cameraPosition)<shadowReachSquared)
                        keepShadows=CasterRelevant(sector.casterBounds,reflectedFloor,moonRelevant,reflectedView,shadowBudget);
                }
                // A room intersecting one light volume need not submit every
                // hidden desk and bookcase. Test immutable renderer bounds only
                // after the broad sector test; moving doors use their live bounds.
                bool eyeMoved=(sector.lastEye-cameraPosition).sqrMagnitude>2.25f;
                if(sector.visible!=sector.nextVisible||sector.keepShadows!=keepShadows||keepShadows||
                    sector.nextVisible&&eyeMoved)
                {
                    bool visibilityChanged=sector.visible!=sector.nextVisible;
                    sector.visible=sector.nextVisible;sector.keepShadows=keepShadows;sector.lastEye=cameraPosition;
                    float lodExtent=graphicsQuality==2?.5f:graphicsQuality==1?.8f:1.2f;
                    float lodDistance=graphicsQuality==2?20f:graphicsQuality==1?15f:10f;
                    float lodDistanceSquared=lodDistance*lodDistance;
                    for(int i=0;i<sector.renderers.Length;i++)
                    {
                        var renderer=sector.renderers[i];if(renderer==null)continue;
                        bool shadowOnly=false;
                        if(!sector.visible&&keepShadows&&sector.shadowModes[i]!=ShadowCastingMode.Off)
                        {
                            var bounds=sector.dynamicBounds[i]?renderer.bounds:sector.rendererBounds[i];
                            shadowOnly=bounds.SqrDistance(cameraPosition)<shadowReachSquared&&
                                CasterRelevant(bounds,reflectedFloor,moonRelevant,reflectedView,shadowBudget);
                        }
                        // Scanned props have no authored LODs yet, so small pieces of
                        // set dressing drop out past the distance at which they cover
                        // only a few pixels. Moving parts keep their live bounds.
                        bool lodCulled=sector.visible&&!sector.dynamicBounds[i]&&
                            sector.rendererExtents[i]<lodExtent&&
                            sector.rendererBounds[i].SqrDistance(cameraPosition)>lodDistanceSquared;
                        if(visibilityChanged||eyeMoved||sector.shadowOnly[i]!=shadowOnly||sector.lodCulled[i]!=lodCulled)
                        {
                            renderer.shadowCastingMode=shadowOnly?ShadowCastingMode.ShadowsOnly:sector.shadowModes[i];
                            renderer.enabled=(sector.visible&&!lodCulled)||shadowOnly;
                            sector.shadowOnly[i]=shadowOnly;sector.lodCulled[i]=lodCulled;
                        }
                        if(shadowOnly)ShadowOnlyRendererCount++;
                    }
                }
            }
        }

        bool CasterRelevant(Bounds caster,float floor,bool moonRelevant,bool reflectedView,int shadowBudget)
        {
            if(moonRelevant&&caster.max.y>floor+.01f)
            {
                var projected=MoonShadowBounds(caster,floor);
                if(GeometryUtility.TestPlanesAABB(planes,projected)||
                    reflectedView&&GeometryUtility.TestPlanesAABB(reflectionPlanes,projected))return true;
            }
            for(int i=0;i<Mathf.Min(shadowBudget,shadowCandidates.Count);i++)
            {
                var light=shadowCandidates[i].light;
                if(caster.SqrDistance(light.transform.position)<light.range*light.range)return true;
            }
            return false;
        }

        static Bounds MoonShadowBounds(Bounds caster,float floor)
        {
            var towardMoon=DeadAirSky.DirectionToMoon;
            float lower=Mathf.Max(0,caster.min.y-floor),upper=Mathf.Max(lower,caster.max.y-floor);
            float height=(lower+upper)*.5f;
            var center=new Vector3(caster.center.x-towardMoon.x/towardMoon.y*height,floor,
                caster.center.z-towardMoon.z/towardMoon.y*height);
            var size=new Vector3(caster.size.x+Mathf.Abs(towardMoon.x/towardMoon.y)*(upper-lower),.15f,
                caster.size.z+Mathf.Abs(towardMoon.z/towardMoon.y)*(upper-lower));
            return new Bounds(center,size);
        }
    }
}
