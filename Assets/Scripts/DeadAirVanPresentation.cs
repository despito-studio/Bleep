using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    /// <summary>The modern crew van's physical deck and deterministic networked transit presentation.</summary>
    public sealed class DeadAirVanPresentation
    {
        public const float ArrivalDuration = 6f, DepartureDuration = 6f;
        public const float FloorHeight = .60f, CeilingHeight = 3f, SpawnYaw = Mathf.PI;
        public const float HalfWidth = 1.46f, Rear = -3.45f, Front = 1.20f;
        readonly List<Transform> transitRoots = new List<Transform>();
        readonly List<Vector3> parkedPositions = new List<Vector3>();
        readonly Light cabinLight;
        readonly Transform slidingDoor;
        float previousElapsed=-1f;
        string previousPhase;
        public Transform VehicleRoot { get; }

        DeadAirVanPresentation(Transform worldRoot,WorldData world)
        {
            foreach(var t in worldRoot.GetComponentsInChildren<Transform>(true))
                if(t.name=="vehicle:crew-van") { VehicleRoot=t; Add(t); break; }
            // These objective models move with the van; the authored collision shell remains parked.
            foreach(var t in worldRoot.GetComponentsInChildren<Transform>(true))
                if(t.name=="interactive:van-map"||t.name=="interactive:ladder"||t.name=="interactive:exit"||t.name.StartsWith("interactive:van-battery-",StringComparison.Ordinal))Add(t);
            cabinLight=VehicleRoot==null?null:VehicleRoot.Find("lamp:van-cabin")?.GetComponent<Light>();
            slidingDoor=VehicleRoot==null?null:VehicleRoot.Find("state:vanSlidingDoor");
        }
        void Add(Transform t){transitRoots.Add(t);parkedPositions.Add(t.position);}
        public static DeadAirVanPresentation Attach(Transform worldRoot,WorldData world)
            =>new DeadAirVanPresentation(worldRoot,world);

        public void Apply(RoomState state)
        {
            if(state==null)return;
            Vector3 offset=TransitOffset(state);
            for(int i=0;i<transitRoots.Count;i++)if(transitRoots[i]!=null)transitRoots[i].position=parkedPositions[i]+offset;
            if(cabinLight!=null){cabinLight.color=new Color(1f,.94f,.84f);cabinLight.intensity=.8f;}
            bool reset=previousElapsed<0||state.elapsed<previousElapsed;
            if(VehicleRoot!=null)
            {
                if(state.phase=="playing"&&state.elapsed<ArrivalDuration&&(reset||previousPhase!="playing")||state.phase=="departing"&&previousPhase!="departing")
                    DeadAirAudio.PlayAt("generator-bed",VehicleRoot.position,.18f);
                if(state.phase=="playing"&&previousElapsed<ArrivalDuration-.65f&&state.elapsed>=ArrivalDuration-.65f||state.phase=="departing"&&previousPhase!="departing")
                    DeadAirAudio.PlayAt("door",VehicleRoot.position,.32f);
            }
            if(slidingDoor!=null)
            {
                float open=state.phase=="playing"?Mathf.SmoothStep(0,1,Mathf.Clamp01((state.elapsed-(ArrivalDuration-.65f))/.65f)):
                    state.phase=="departing"?1f-Mathf.SmoothStep(0,1,Mathf.Clamp01((state.elapsed-state.departureStartedAt)/.55f)):state.phase=="won"?0f:1f;
                var position=slidingDoor.localPosition;position.z=Mathf.Lerp(-.125f,-2.35f,open);slidingDoor.localPosition=position;
            }
            previousElapsed=state.elapsed;previousPhase=state.phase;
        }
        public static Vector3 TransitOffset(RoomState state)
        {
            if(state==null)return Vector3.zero;
            if(state.phase=="playing"&&state.elapsed<ArrivalDuration)
                return Vector3.forward*(12f*(1f-Mathf.SmoothStep(0,1,Mathf.Clamp01(state.elapsed/(ArrivalDuration-.65f)))));
            if(state.phase=="departing"||state.phase=="won")
                return Vector3.forward*(16f*Mathf.SmoothStep(0,1,Mathf.Clamp01((state.elapsed-state.departureStartedAt-.55f)/(DepartureDuration-.55f))));
            return Vector3.zero;
        }
        public static float SeatedWeight(RoomState state)
        {
            if (state == null) return 0;
            if (state.phase == "departing")
                return Mathf.SmoothStep(0, 1, Mathf.Clamp01((state.elapsed - state.departureStartedAt) / DepartureDuration * 3));
            if (state.phase == "playing" && state.elapsed < ArrivalDuration)
                return 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((state.elapsed / ArrivalDuration - .62f) / .30f));
            return 0;
        }
        public static PropData Van(WorldData world)=>world?.props==null?null:Array.Find(world.props,p=>p.type=="van");
        static Vector3 Local(WorldData world,float sourceX,float z)
        {
            var van=Van(world);if(van==null)return new Vector3(float.PositiveInfinity,0,0);
            return Quaternion.Inverse(DeadAirOfficeLayout.Rotation(van.rotation))*(DeadAirOfficeLayout.Position(sourceX,0,z)-DeadAirOfficeLayout.Position(van.x,0,van.z));
        }
        public static PointData SourcePoint(WorldData world,Vector3 local)
        {
            var van=Van(world);if(van==null)return world.spawn;
            var p=DeadAirOfficeLayout.Position(van.x,van.y,van.z)+DeadAirOfficeLayout.Rotation(van.rotation)*local;
            return new PointData{x=-p.x,y=p.y,z=p.z};
        }
        public static PointData SpawnPoint(WorldData world,int index)
            =>SourcePoint(world,new Vector3(index%2==0?-.43f:.43f,FloorHeight,index%4<2?-2.20f:-1.25f));
        public static bool IsInside(WorldData world,float sourceX,float z,float margin=0f)
        {var p=Local(world,sourceX,z);return Mathf.Abs(p.x)<=HalfWidth-margin&&p.z>=Rear+margin&&p.z<=Front-margin;}
        public static bool TryFloorHeight(WorldData world,float sourceX,float z,out float height)
        {
            var p=Local(world,sourceX,z);var van=Van(world);height=float.NaN;if(van==null)return false;
            if(Mathf.Abs(p.x)<=HalfWidth&&p.z>=Rear&&p.z<=Front)height=FloorHeight;
            else if(p.z>=-1f&&p.z<=.75f)
            {
                if(p.x>=-1.59f&&p.x< -HalfWidth)height=FloorHeight;
                else if(p.x>=-1.99f&&p.x< -1.59f)height=.40f;
                else if(p.x>=-2.28f&&p.x< -1.99f)height=.20f;
            }
            if(float.IsNaN(height))return false;height+=van.y;return true;
        }
        public static float HeightAt(WorldData world,float sourceX,float z)
            =>TryFloorHeight(world,sourceX,z,out var height)?height:float.NaN;
    }
}
