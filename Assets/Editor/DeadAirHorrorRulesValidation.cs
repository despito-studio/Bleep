using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirHorrorRulesValidation
    {
        [MenuItem("Dead Air/Validate Blind Monster and Locker Scares")]
        public static void Validate()
        {
            ValidateHearing();
            ValidateLockerSpiders();
            ValidateAnimatedDoorPassage();
            ValidateOfficePeek();
            Debug.Log("DEAD AIR HORROR RULES PASSED: blind hearing of accepted footsteps/radio/doors, silent positions never tracked, last-heard memory expires, floor attenuation, animated door delay and locked-door refusal, host-only spider rolls/half-current sanity/cooldown/re-entry/occupancy/reset, physical cover peeks vanish on approach and resume calm without noise, replicated event counters.");
        }

        static void Require(bool condition,string message)
        { if(!condition)throw new InvalidOperationException("Horror rules validation: "+message); }

        static WorldData IsolatedWorld(bool door=false,bool locked=false)
        {
            var world=JsonUtility.FromJson<WorldData>(JsonUtility.ToJson(WorldData.Load()));
            world.spawn=new PointData{x=70,y=0,z=0};
            world.courtyard=new BoundsData{minX=60,maxX=95,minZ=-20,maxZ=20};
            world.rooms=new[]{new RoomData{name="Isolated hearing corridor",minX=60,maxX=95,minZ=-20,maxZ=20},
                new RoomData{name="Upper hearing corridor",minX=60,maxX=95,minZ=-20,maxZ=20,floorY=4.2f}};
            world.objects=Array.Empty<ObjectiveData>();world.stairs=Array.Empty<StairData>();world.props=Array.Empty<PropData>();
            world.doors=door?new[]{new DoorData{id="horror-validation-door",x=78,y=1.4f,z=0,w=2.2f,
                requires=locked?"keycard-security":null}}:Array.Empty<DoorData>();
            world.lockers=Enumerable.Range(0,4).Select(index=>new LockerData{id="horror-locker-"+index,
                x=72+index*3,y=0,z=3,exitX=72+index*3,exitY=0,exitZ=2}).ToArray();
            world.navigationNodes=new[]{new NavigationNodeData{x=70},new NavigationNodeData{x=74},
                new NavigationNodeData{x=76},new NavigationNodeData{x=77.5f},new NavigationNodeData{x=78.5f},
                new NavigationNodeData{x=80},new NavigationNodeData{x=84}};
            world.navigationLinks=Enumerable.Range(0,world.navigationNodes.Length-1)
                .Select(index=>new NavigationLinkData{a=index,b=index+1}).ToArray();
            foreach(var role in world.classes)role.bonuses.monsterHearingRange=16;
            return world;
        }

        static DeadAirRules Start(WorldData world,bool solo=true,Func<double> clock=null)
        {
            var rules=new DeadAirRules(world,"host","Horror validation","engineer",solo,false,movementTime:clock);
            rules.Start("host");rules.State.Player("host").invulnerableUntil=9999;
            return rules;
        }

        static void Advance(DeadAirRules rules,float seconds)
        { for(int tick=0;tick<Mathf.CeilToInt(seconds/.1f);tick++)rules.Tick(.1f); }

        static void Position(PlayerState actor,float x,float y=0,float z=0)
        { actor.x=x;actor.y=y;actor.z=z;actor.flashlight=true;actor.camera=false; }

        static void Move(DeadAirRules rules,PlayerState actor,float x,bool sprint=false)
        {
            rules.Move(actor.id,new NetPacket{x=x,y=actor.y,z=actor.z,sprinting=sprint,flashlight=true});
        }

        static void ValidateHearing()
        {
            double clock=0;
            var rules=Start(IsolatedWorld(),clock:()=>clock);
            var actor=rules.State.Player("host");var enemy=rules.State.enemy;
            enemy.active=true;enemy.x=70;enemy.y=0;enemy.z=0;
            Position(actor,78);clock=.1;Move(rules,actor,78,true);rules.Tick(.1f);
            Require(enemy.heardUntil==0,"Stationary sprint packets must not create footsteps or an aggro target.");
            Position(actor,75);clock+=.2;Move(rules,actor,75.08f);rules.Tick(.1f);
            Require(enemy.heardUntil==0,"Slow quiet movement several metres away must be able to pass without attracting the blind enemy.");
            Advance(rules,.6f);Position(actor,76);clock+=.6;Move(rules,actor,76.5f,true);
            Require(Mathf.Abs(enemy.heardX-76.5f)<.001f&&enemy.heardUntil>rules.State.elapsed,
                "An accepted sprinting footstep must remember its actual sound position.");
            float rememberedX=enemy.heardX,rememberedUntil=enemy.heardUntil;
            Position(actor,84);Advance(rules,1);
            Require(enemy.heardX==rememberedX&&enemy.heardUntil==rememberedUntil,
                "Silently changing the operator's position must not update the last-heard destination.");
            Advance(rules,9);
            Require(enemy.heardUntil<rules.State.elapsed&&enemy.behaviour=="patrol",
                "After sound memory expires the enemy must return to calm patrol, even with a living visible operator.");
            rules.Radio(actor.id,0);
            Require(enemy.heardX==actor.x&&enemy.heardUntil>rules.State.elapsed,"Radio speech must attract the blind enemy.");

            clock=0;rules=Start(IsolatedWorld(),clock:()=>clock);actor=rules.State.Player("host");enemy=rules.State.enemy;
            enemy.active=true;enemy.x=70;enemy.y=0;enemy.z=0;
            Position(actor,80,4.2f);clock=.1;Move(rules,actor,80.5f,true);
            Require(enemy.heardUntil==0,"A distant sprint upstairs must be attenuated by its intervening floor.");
            Position(actor,72,4.2f);rules.Radio(actor.id,0);
            Require(enemy.heardY==4.2f&&enemy.heardUntil>0,"A nearby loud radio must retain its actual floor when heard.");
        }

        static void Enter(DeadAirRules rules,LockerData locker,string player="host")
        {
            var actor=rules.State.Player(player);Position(actor,locker.exitX,locker.ExitFloorY,locker.exitZ);
            Require(rules.Interact(player,locker.id)==null&&actor.hiding==locker.id,"A valid locker must still function normally.");
        }

        static void ValidateLockerSpiders()
        {
            var world=IsolatedWorld();var rules=Start(world,false);var actor=rules.State.Player("host");
            Require(rules.AddPlayer("guest","Occupancy validation","operator"),"Could not add a locker occupancy peer.");
            Require(rules.ConfigureLockerRollForVerification(.1f),"Editor verification roll must be configurable.");
            actor.sanity=80;Enter(rules,world.lockers[0]);
            Require(actor.sanity==40&&actor.spiderScareSerial==1,"A successful spider roll must remove half current sanity, not fifty fixed points.");
            var guest=rules.State.Player("guest");Position(guest,world.lockers[0].exitX,0,world.lockers[0].exitZ);
            Require(rules.Interact(guest.id,world.lockers[0].id)!=null&&guest.spiderScareSerial==0,
                "An occupied locker must reject entry before any spider roll.");
            rules.Interact(actor.id,"exit-locker");Enter(rules,world.lockers[0]);
            Require(actor.spiderScareSerial==1&&actor.sanity==40,"Repeated entry must not reroll the same locker.");
            rules.Interact(actor.id,"exit-locker");Enter(rules,world.lockers[1]);
            Require(actor.spiderScareSerial==1,"A second locker must respect the player scare cooldown.");
            rules.Interact(actor.id,"exit-locker");rules.State.elapsed=46;
            actor.sanity=60;Enter(rules,world.lockers[2]);
            Require(actor.sanity==30&&actor.spiderScareSerial==2&&actor.spiderScareAt==46000,
                "A new locker after cooldown must publish the host event time and halve its current sanity.");
            rules.Interact(actor.id,"exit-locker");rules.State.elapsed=92;rules.ConfigureLockerRollForVerification(.25f);
            actor.sanity=54;Enter(rules,world.lockers[3]);
            Require(actor.sanity==54&&actor.spiderScareSerial==2,"The 25 percent boundary must fail the roll and leave sanity unchanged.");
            rules.Interact(actor.id,"exit-locker");rules.ConfigureLockerRollForVerification(0);Enter(rules,world.lockers[3]);
            Require(actor.spiderScareSerial==2,"A failed first roll must also be consumed for that locker this round.");
            var copy=JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State));
            Require(copy.Player(actor.id).spiderScareSerial==2&&copy.Player(actor.id).spiderScareAt==46000,
                "Spider counters and timestamps must survive network serialization.");
            rules.State.phase="lost";rules.Restart("host");actor=rules.State.Player("host");
            Require(actor.spiderScareSerial==0,"A restarted round must reset spider event history.");
            Enter(rules,world.lockers[0]);Require(actor.spiderScareSerial==1,"A new round must allow its first locker roll again.");
        }

        static void ValidateAnimatedDoorPassage()
        {
            var hingeObject=new GameObject("door:horror-validation-door");
            try
            {
                hingeObject.transform.position=new Vector3(-78,0,-1.1f);
                var leaf=new GameObject("Animated validation leaf");leaf.transform.SetParent(hingeObject.transform,false);
                leaf.transform.localPosition=new Vector3(0,1.4f,1.1f);
                leaf.AddComponent<BoxCollider>().size=new Vector3(.12f,2.8f,2.2f);
                var hinge=hingeObject.AddComponent<DeadAirHingedDoor>();hinge.Initialize(95);
                var rules=Start(IsolatedWorld(true));var actor=rules.State.Player("host");var enemy=rules.State.enemy;
                Position(actor,80);enemy.active=true;enemy.x=76.5f;enemy.y=0;enemy.z=0;
                Physics.SyncTransforms();rules.Radio(actor.id,0);
                bool opened=false,waited=false;
                for(int tick=0;tick<60;tick++)
                {
                    rules.Tick(.1f);var door=rules.State.Door("horror-validation-door");
                    hinge.SetOpen(door.open);hinge.Advance(.1f);Physics.SyncTransforms();
                    if(!door.open)continue;opened=true;
                    if(rules.State.elapsed-door.openedAt<.6f)
                    {Require(enemy.x<78,"The enemy must not cross before the visible hinge finishes opening.");waited=true;}
                }
                Require(opened&&waited&&enemy.x>79.1f&&hinge.Angle>90,
                    "An investigating enemy must open the real leaf, wait for its swing, and then pass through the clear portal.");
                hinge.SetOpen(false);hinge.Advance(1);Physics.SyncTransforms();
                rules=Start(IsolatedWorld(true,true));actor=rules.State.Player("host");enemy=rules.State.enemy;
                Position(actor,80);enemy.active=true;enemy.x=76.5f;enemy.y=0;enemy.z=0;rules.Radio(actor.id,0);
                Advance(rules,6);
                Require(rules.State.Door("horror-validation-door").locked&&!rules.State.Door("horror-validation-door").open&&enemy.x<78,
                    "Hearing an operator beyond a key-locked door must not bypass its quest lock or physical leaf.");
            }
            finally{UnityEngine.Object.DestroyImmediate(hingeObject);Physics.SyncTransforms();}
        }

        static void ValidateOfficePeek()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Blackpine.unity");
            var world=WorldData.Load();var rules=Start(world);var actor=rules.State.Player("host");
            var openedLeaves=new List<Collider>();
            try
            {
                // Reproduce doors already opened by an operator while retaining every
                // actual frame, partition and furnishing collider for the cover checks.
                var unlocked=new HashSet<string>(world.doors.Where(data=>string.IsNullOrEmpty(data.requires)).Select(data=>"door:"+data.id));
                foreach(var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                    for(var part=collider.transform;part!=null;part=part.parent)
                        if(unlocked.Contains(part.name)&&collider.enabled){openedLeaves.Add(collider);collider.enabled=false;break;}
                Physics.SyncTransforms();bool found=false;
                var positions=new[]{new Vector2(0,4),new Vector2(7.2f,-5),new Vector2(-7.2f,-5),
                    new Vector2(7.2f,-24),new Vector2(-7.2f,-24),new Vector2(0,-29),new Vector2(0,-40)};
                foreach(float floor in new[]{0f,4.2f})
                {
                    if(found)break;
                    foreach(var position in positions)
                    {
                        Position(actor,position.x,floor,position.y);
                        for(int heading=0;heading<8&&!found;heading++)
                        {actor.yaw=heading*Mathf.PI*.25f;found=rules.TryStartPeekForVerification(actor.id);}
                        if(found)break;
                    }
                }
                Require(found,"The actual office must provide at least one safe partly occluded stalking encounter.");
                var enemy=rules.State.enemy;
                Vector3 observer=new Vector3(-actor.x,actor.y+1.4f,actor.z),body=new Vector3(-enemy.x,enemy.y+1.35f,enemy.z);
                Vector3 head=new Vector3(-enemy.x,enemy.y+2.48f,enemy.z)+
                    Quaternion.Euler(0,enemy.yaw*Mathf.Rad2Deg,0)*Vector3.right*enemy.peekLean*.41f;
                Require(!DeadAirRules.HasInteractionSightline(observer,body,null)&&DeadAirRules.HasInteractionSightline(observer,head,null),
                    "The actual wall must cover the body while the leaned head is visible around its edge.");
                var copy=JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(rules.State));
                Require(copy.enemy.behaviour=="peek"&&copy.enemy.encounterSerial==1&&copy.enemy.watchingPlayerId==actor.id,
                    "A stalking encounter must replicate its phase, subject and event counter.");
                Position(actor,enemy.x+2,enemy.y,enemy.z);rules.Tick(.1f);
                Require(enemy.behaviour=="vanished"&&!actor.down,"Approaching the watched corner must make the stalker disappear without an attack.");
                Advance(rules,3.2f);
                Require(enemy.behaviour=="patrol"&&enemy.heardUntil==0,
                    "After disappearing, a blind monster must resume calm patrol when no noise was heard.");
            }
            finally{foreach(var collider in openedLeaves)if(collider!=null)collider.enabled=true;Physics.SyncTransforms();}
        }
    }
}
