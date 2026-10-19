using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirMovementValidation
    {
        [MenuItem("Dead Air/Validate Movement Reconciliation Rules")]
        public static void Validate()
        {
            using (new ExistingCollisionIsolation())
            {
            var source=WorldData.Load();
            var world=new WorldData{spawn=new PointData{x=80,z=70},courtyard=new BoundsData{minX=-95,maxX=95,minZ=-95,maxZ=95},
                rooms=Array.Empty<RoomData>(),doors=Array.Empty<DoorData>(),lockers=Array.Empty<LockerData>(),
                objects=Array.Empty<ObjectiveData>(),props=Array.Empty<PropData>(),classes=source.classes};
            ValidateArrivalGate(world);
            foreach(var role in source.classes)
                foreach(float frameTime in new[]{1f/120,1f/15,1f/10,.4f,.7f})
                {
                    ValidateLongSprint(world,role.id,true,frameTime);
                    ValidateLongSprint(world,role.id,false,frameTime);
                }
            ValidatePacketBurst(world);
            ValidateActorAndWallCollision(world);
            ValidateSequenceSerialization();
            }
            Debug.Log("DEAD AIR MOVEMENT RULES PASSED: all roles, long solo/host/guest sprint at 120/15/10 FPS and 400/700ms frames; bounded packet bursts; own capsule excluded, walls retained; rejected/duplicate acknowledgements; snapshot sequences.");
        }

        sealed class ExistingCollisionIsolation : IDisposable
        {
            readonly List<Collider> restored = new List<Collider>();
            public ExistingCollisionIsolation()
            {
                foreach(var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                {
                    if(!collider.enabled || !collider.gameObject.scene.IsValid() || EditorUtility.IsPersistent(collider))continue;
                    restored.Add(collider);collider.enabled=false;
                }
                Physics.SyncTransforms();
            }
            public void Dispose()
            {
                foreach(var collider in restored)if(collider!=null)collider.enabled=true;
                Physics.SyncTransforms();
            }
        }

        static void StartAfterArrival(DeadAirRules rules)
        {
            Require(rules.Start("host")==null,"Movement fixture could not start.");
            rules.State.elapsed=DeadAirVanPresentation.ArrivalDuration+.01f;
        }

        static void ValidateArrivalGate(WorldData world)
        {
            var rules=new DeadAirRules(world,"host","Arrival check","engineer",true,true,movementTime:()=>0);
            Require(rules.Start("host")==null,"Arrival fixture could not start.");
            var actor=rules.State.Player("host");float start=actor.z;
            var packet=new NetPacket{x=actor.x,y=actor.y,z=start+.2f,moveSequence=1};
            rules.Move(actor.id,packet);
            Require(actor.z==start&&actor.moveSequence==0,"Movement must remain locked during the van arrival.");
            rules.State.elapsed=DeadAirVanPresentation.ArrivalDuration+.01f;
            rules.Move(actor.id,packet);
            Require(Mathf.Abs(actor.z-packet.z)<.0001f&&actor.moveSequence==1,"Movement must unlock when van arrival completes.");
        }

        static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Movement validation: "+message);}

        static void ValidateLongSprint(WorldData world,string role,bool solo,float dt)
        {
            double clock=0;
            var rules=new DeadAirRules(world,"host","Sprint host",role,solo,true,movementTime:()=>clock);
            if(!solo)Require(rules.AddPlayer("guest","Sprint guest",role),"Guest setup failed.");
            StartAfterArrival(rules);
            float speed=4.5f*world.GetClass(role).bonuses.speedMultiplier;
            int steps=Mathf.CeilToInt(60/dt);
            int[] directions={1,1};
            for(int step=1;step<=steps;step++)
            {
                clock+=dt;
                for(int index=0;index<rules.State.players.Count;index++)
                {
                    var actor=rules.State.players[index];
                    if(actor.z>84)directions[index]=-1;else if(actor.z<62)directions[index]=1;
                    float destination=actor.z+speed*dt*directions[index];
                    rules.Move(actor.id,new NetPacket{x=actor.x,y=actor.y,z=destination,sprinting=true,moveSequence=step});
                    Require(Mathf.Abs(actor.z-destination)<.0001f&&actor.moveSequence==step,
                        role+" "+actor.id+" sprint rejected at frame "+step+", dt="+dt);
                }
            }
        }

        static void ValidatePacketBurst(WorldData world)
        {
            double clock=0;
            var rules=new DeadAirRules(world,"host","Burst check","engineer",true,true,movementTime:()=>clock);
            StartAfterArrival(rules);var actor=rules.State.Player("host");
            float start=actor.z;
            clock+=.2;
            rules.Move(actor.id,new NetPacket{x=actor.x,z=start+1,moveSequence=1});
            Require(actor.z==start+1,"Initial physical step should be accepted.");
            for(int packet=2;packet<=20;packet++)
                rules.Move(actor.id,new NetPacket{x=actor.x,z=start+2,moveSequence=packet});
            Require(actor.z==start+1&&actor.moveSequence==20,"A packet burst must be rejected but acknowledged.");
            clock+=.25;
            rules.Move(actor.id,new NetPacket{x=actor.x,z=start+2,moveSequence=21});
            Require(actor.z==start+2,"Speed credit must recover with time.");
            rules.Move(actor.id,new NetPacket{x=actor.x,z=start,moveSequence=20});
            Require(actor.z==start+2&&actor.moveSequence==21,"An older sequence must not rewind movement.");
            clock+=5;
            rules.Move(actor.id,new NetPacket{x=actor.x,z=start+12,moveSequence=22});
            Require(actor.z==start+2&&actor.moveSequence==22,"Idle time must not accumulate unlimited teleport credit.");
        }

        static void ValidateActorAndWallCollision(WorldData world)
        {
            double clock=0;
            var rules=new DeadAirRules(world,"host","Collision check","engineer",true,true,movementTime:()=>clock);
            StartAfterArrival(rules);var actor=rules.State.Player("host");
            var capsule=new GameObject("Predicted controller collision check");
            var wall=new GameObject("shell");
            try
            {
                float destination=actor.z+1.35f;
                var controller=capsule.AddComponent<CharacterController>();
                controller.height=1.7f;controller.radius=.22f;controller.center=Vector3.up*.85f;
                capsule.transform.position=new Vector3(-actor.x,0,destination);
                Physics.SyncTransforms();clock+=.3;
                rules.Move(actor.id,new NetPacket{x=actor.x,z=destination,moveSequence=1});
                Require(actor.z==destination,"The predicted local capsule must not reject a legitimate sprint step.");
                var blocker=wall.AddComponent<BoxCollider>();blocker.size=new Vector3(1,2,.1f);
                wall.transform.position=new Vector3(-actor.x,.75f,destination+.65f);
                Physics.SyncTransforms();clock+=.5;
                rules.Move(actor.id,new NetPacket{x=actor.x,z=destination+1.3f,moveSequence=2});
                Require(actor.z==destination&&actor.moveSequence==2,"Filtering the actor must preserve actual wall collision and rejected-input acknowledgements.");
                wall.SetActive(false);Physics.SyncTransforms();clock+=.5;
                rules.Move(actor.id,new NetPacket{x=actor.x,z=destination+1.3f,moveSequence=3});
                Require(Mathf.Abs(actor.z-(destination+1.3f))<.0001f&&actor.moveSequence==3,"Removing the wall must permit the same valid move; a budget or unrelated collider must not impersonate physical rejection.");
            }
            finally{UnityEngine.Object.DestroyImmediate(capsule);UnityEngine.Object.DestroyImmediate(wall);}
        }

        static void ValidateSequenceSerialization()
        {
            var packet=JsonUtility.FromJson<NetPacket>(JsonUtility.ToJson(new NetPacket{type="move",moveSequence=87}));
            var state=new RoomState();state.players.Add(new PlayerState{id="guest",moveSequence=86});
            var copy=JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(state));
            Require(packet.moveSequence==87&&copy.Player("guest").moveSequence==86,"Input acknowledgements must survive the LAN packet and snapshot formats.");
        }
    }
}
