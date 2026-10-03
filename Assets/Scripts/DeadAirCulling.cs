using System.Collections.Generic;
using UnityEngine;

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
            public LightVolume[] lights;
            public bool visible = true;
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
        }
        readonly List<Sector> sectors = new List<Sector>();
        readonly Plane[] planes = new Plane[6];
        readonly WorldData world;
        readonly List<LightVolume> shadowCandidates=new List<LightVolume>();
        static readonly RaycastHit[] occlusionHits=new RaycastHit[32];
        const int PracticalShadowBudget=8;
        float nextUpdate;

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
                foreach (var light in lights) bounds.Encapsulate(light.transform.position);
                // Account for moving door leaves and illumination across doorways.
                bounds.Expand(new Vector3(4, 1, 4));
                var volumes=new LightVolume[lights.Length];
                for(int i=0;i<lights.Length;i++)
                {
                    var light=lights[i];
                    volumes[i]=new LightVolume {light=light,bounds=IlluminationBounds(light),
                        ceilingFixture=light.name.StartsWith("lamp:zone-"),
                        floor=Mathf.FloorToInt(light.transform.position.y/DeadAirOfficeLayout.FloorHeight),shadows=light.shadows};
                    var volume=volumes[i];
                    if(volume.ceilingFixture)
                    {
                        foreach(var room in world.rooms)
                            if(Mathf.Abs(room.floorY-volume.floor*DeadAirOfficeLayout.FloorHeight)<.01f &&
                                Contains(room,light.transform.position)){volume.room=room;break;}
                        // The scene is presented at a VHS-sized resolution. A local
                        // 256px hard shadow retains its readable contact edges.
                        light.shadowCustomResolution=256;
                    }
                }
                sectors.Add(new Sector { bounds = bounds, renderers = renderers, lights = volumes });
            }
        }

        static bool Contains(RoomData room,Vector3 position) => -position.x>=room.minX && -position.x<=room.maxX &&
            position.z>=room.minZ && position.z<=room.maxZ;

        static bool ClearSample(Vector3 origin,Vector3 sample)
        {
            var ray=sample-origin;float distance=ray.magnitude;
            if(distance<.05f)return true;
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

        static bool VisiblePractical(LightVolume volume,Vector3 cameraPosition,int cameraFloor)
        {
            if(!volume.ceilingFixture)return true;
            if(volume.room!=null && volume.floor==cameraFloor && Contains(volume.room,cameraPosition))return true;
            var position=volume.light.transform.position;
            // A closed office wall/door hides the lamp's entire light pool. Sample
            // multiple heights so an open doorway or a desk does not hide it falsely.
            return ClearSample(cameraPosition,position) ||
                ClearSample(cameraPosition,position+Vector3.down*1.2f) ||
                ClearSample(cameraPosition,new Vector3(position.x,volume.floor*DeadAirOfficeLayout.FloorHeight+.4f,position.z));
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
            if(x<DeadAirOfficeLayout.BuildingMinX || x>DeadAirOfficeLayout.BuildingMaxX ||
                z<DeadAirOfficeLayout.BuildingMinZ || z>DeadAirOfficeLayout.BuildingMaxZ || floor<0 || floor>1)return false;
            // Both levels remain lit when looking through the open stairwell.
            if(world.stairs!=null)
                foreach(var stair in world.stairs)
                    if(x>=stair.minX-1 && x<=stair.maxX+1 && z>=stair.minZ-1 && z<=stair.maxZ+1)return false;
            return true;
        }

        public void Update(RoomState state, Camera camera, bool force = false)
        {
            if (camera == null || !force && Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .1f;
            var cameraPosition=camera.transform.position;
            bool inside=InteriorFloor(cameraPosition,out int cameraFloor);
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            shadowCandidates.Clear();
            foreach (var sector in sectors)
            {
                float distanceSquared = sector.bounds.SqrDistance(cameraPosition);
                bool visible = distanceSquared < 9 || distanceSquared < 2500 && GeometryUtility.TestPlanesAABB(planes, sector.bounds);
                if (sector.visible != visible)
                {
                    sector.visible = visible;
                    foreach (var renderer in sector.renderers) if (renderer != null) renderer.enabled = visible;
                }
                foreach(var volume in sector.lights)
                {
                    var light=volume.light;if(light==null)continue;
                    bool floorRelevant=!inside || !volume.ceilingFixture || volume.floor==cameraFloor;
                    // Test each actual light volume rather than the whole room's large
                    // 25m neighbourhood. Preserve nearby lamps behind the camera.
                    bool nearby=(light.transform.position-cameraPosition).sqrMagnitude<light.range*light.range;
                    bool relevant=floorRelevant && (nearby || GeometryUtility.TestPlanesAABB(planes,volume.bounds));
                    light.enabled=relevant && VisiblePractical(volume,cameraPosition,cameraFloor);
                    if(volume.ceilingFixture && volume.shadows!=LightShadows.None)
                    {
                        if(light.enabled)
                        {
                            volume.priority=(light.transform.position-cameraPosition).sqrMagnitude;
                            // Keep current room contact shadows before a distant doorway.
                            if(volume.room!=null && volume.floor==cameraFloor && Contains(volume.room,cameraPosition))volume.priority-=1000;
                            shadowCandidates.Add(volume);
                        }
                        else if(light.shadows!=LightShadows.None)light.shadows=LightShadows.None;
                    }
                }
            }
            shadowCandidates.Sort((a,b)=>a.priority.CompareTo(b.priority));
            for(int i=0;i<shadowCandidates.Count;i++)
            {
                var volume=shadowCandidates[i];
                var shadows=i<PracticalShadowBudget?volume.shadows:LightShadows.None;
                if(volume.light.shadows!=shadows)volume.light.shadows=shadows;
            }
        }
    }
}
