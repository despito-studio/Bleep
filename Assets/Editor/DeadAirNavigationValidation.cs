using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirNavigationValidation
    {
        [MenuItem("Dead Air/Validate Office Navigation Graph")]
        public static void Validate()
        {
            var world=WorldData.Load();
            Require(world.navigationNodes!=null&&world.navigationNodes.Length>0&&world.navigationLinks!=null,
                "The expanded office must provide a navigation graph.");
            var adjacency=new List<int>[world.navigationNodes.Length];
            for(int i=0;i<adjacency.Length;i++)adjacency[i]=new List<int>();
            foreach(var link in world.navigationLinks)
            {
                Require(link.a>=0&&link.b>=0&&link.a<adjacency.Length&&link.b<adjacency.Length,"Navigation edge contains an invalid node index.");
                var a=world.navigationNodes[link.a];var b=world.navigationNodes[link.b];
                float maxRise=.4f;
                foreach(var stair in world.stairs)
                    if(stair.Contains(a.x,a.z)&&stair.Contains(b.x,b.z))
                        maxRise=Mathf.Max(maxRise,(stair.upperY-stair.lowerY)/(stair.maxZ-stair.minZ)+.03f);
                Require(Mathf.Abs(a.y-b.y)<maxRise,
                    "Navigation cannot cross a ceiling or skip the stair flight.");
                adjacency[link.a].Add(link.b);adjacency[link.b].Add(link.a);
            }
            int spawn=Nearest(world,world.spawn.x,world.spawn.y,world.spawn.z);
            var reachable=new HashSet<int>{spawn};var queue=new Queue<int>();queue.Enqueue(spawn);
            while(queue.Count>0)foreach(int next in adjacency[queue.Dequeue()])if(reachable.Add(next))queue.Enqueue(next);
            foreach(var objective in world.objects)
            {
                float floor=objective.y< -1?-DeadAirOfficeLayout.FloorHeight:objective.y>=DeadAirOfficeLayout.FloorHeight?DeadAirOfficeLayout.FloorHeight:0;
                if(DeadAirVanPresentation.TryFloorHeight(world,objective.x,objective.z,out var vanFloor))floor=vanFloor;
                Require(reachable.Contains(Nearest(world,objective.x,floor,objective.z)),
                    "The quest destination is disconnected from the entrance: "+objective.id);
            }
            ValidatePhysicalRoutes(world);
            ValidateBranchedPursuit(world);
            Debug.Log("DEAD AIR NAVIGATION PASSED: valid floor/stair edges, actual capsule-clear doorway/room/quest routes, every quest destination connected to the entrance, live enemy pursuit around the central archive/editorial block.");
        }

        internal sealed class PhysicalRoutes
        {
            const float Radius=.22f, Height=1.7f, StepClearance=.32f;
            readonly WorldData world;
            readonly HashSet<Collider> openableLeaves=new HashSet<Collider>();
            readonly Collider[] overlaps=new Collider[64];
            readonly RaycastHit[] hits=new RaycastHit[64];
            public readonly Vector3[] positions;
            public readonly bool[] clear;
            public readonly List<int>[] links;
            public readonly HashSet<int> reachable=new HashSet<int>();
            public int blockedNodes, blockedEdges;

            public PhysicalRoutes(WorldData data, Vector3 offset = default, Transform onlyRoot = null)
            {
                world=data;var doors=new HashSet<string>();foreach(var door in data.doors)doors.Add("door:"+door.id);
                var colliders=onlyRoot!=null?onlyRoot.GetComponentsInChildren<Collider>(true):UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
                foreach(var collider in colliders)
                    for(var part=collider.transform;part!=null;part=part.parent)
                        if(doors.Contains(part.name)){openableLeaves.Add(collider);break;}
                Require(openableLeaves.Count>=world.doors.Length,
                    "Physical route validation needs actual generated door leaf colliders in the loaded scene.");
                positions=new Vector3[world.navigationNodes.Length];clear=new bool[positions.Length];links=new List<int>[positions.Length];
                Physics.SyncTransforms();
                for(int index=0;index<positions.Length;index++)
                {
                    var node=world.navigationNodes[index];
                    var position=offset+DeadAirOfficeLayout.Position(node.x,node.y,node.z);
                    links[index]=new List<int>(4);
                    if(TrySupport(position,out var floor))
                    {
                        position.y=floor;positions[index]=position;clear[index]=BodyClear(position);
                    }
                    else positions[index]=position;
                    if(!clear[index])blockedNodes++;
                }
                foreach(var edge in world.navigationLinks)
                {
                    if(!clear[edge.a]||!clear[edge.b]||!BodySweep(positions[edge.a],positions[edge.b])){blockedEdges++;continue;}
                    links[edge.a].Add(edge.b);links[edge.b].Add(edge.a);
                }
            }

            bool TrySupport(Vector3 position,out float floor)
            {
                floor=position.y;float highest=float.NegativeInfinity;
                int count=Physics.RaycastNonAlloc(position+Vector3.up*.38f,Vector3.down,hits,.76f,~0,QueryTriggerInteraction.Ignore);
                Require(count<hits.Length,"The physical floor support query overflowed.");
                for(int index=0;index<count;index++)
                {
                    var hit=hits[index];
                    if(openableLeaves.Contains(hit.collider)||hit.normal.y<.6f||Mathf.Abs(hit.point.y-position.y)>.3f)continue;
                    highest=Mathf.Max(highest,hit.point.y);
                }
                if(float.IsNegativeInfinity(highest))return false;
                floor=highest;return true;
            }

            void Capsule(Vector3 position,out Vector3 bottom,out Vector3 top)
            {
                // The player steps over obstacles <=.28m. Start the swept body just
                // above that band so the discrete .175m treads remain traversable;
                // the full torso/head retain the real controller radius and height.
                bottom=position+Vector3.up*(Radius+StepClearance);
                top=position+Vector3.up*(Height-Radius);
            }

            public bool BodyClear(Vector3 position)
            {
                Capsule(position,out var bottom,out var top);
                int count=Physics.OverlapCapsuleNonAlloc(bottom,top,Radius,overlaps,~0,QueryTriggerInteraction.Ignore);
                Require(count<overlaps.Length,"The physical body overlap query overflowed.");
                for(int index=0;index<count;index++)if(!openableLeaves.Contains(overlaps[index]))return false;
                return true;
            }

            public bool BodySweep(Vector3 from,Vector3 to)
            {
                Vector3 offset=to-from;if(offset.sqrMagnitude<.000001f)return true;
                Capsule(from,out var bottom,out var top);
                int count=Physics.CapsuleCastNonAlloc(bottom,top,Radius,offset.normalized,hits,offset.magnitude,~0,QueryTriggerInteraction.Ignore);
                Require(count<hits.Length,"The physical body sweep query overflowed.");
                for(int index=0;index<count;index++)if(!openableLeaves.Contains(hits[index].collider))return false;
                return true;
            }

            public int ReachableApproach(Vector3 destination,float range)
            {
                int result=-1;float best=float.PositiveInfinity;
                foreach(int index in reachable)
                {
                    var origin=positions[index];
                    if(Mathf.Abs(origin.y-destination.y)>.35f)continue;
                    Vector3 flat=new Vector3(destination.x,origin.y,destination.z);
                    float distance=Vector3.Distance(origin,flat);
                    if(distance>range+1||distance>=best+1)continue;
                    // A one-metre grid represents free continuous floor. Check the last
                    // partial metre physically instead of demanding an exact grid centre.
                    Vector3 approach=Vector3.MoveTowards(origin,flat,Mathf.Max(0,distance-range+.02f));
                    if(!BodyClear(approach)||!BodySweep(origin,approach))continue;
                    best=distance;result=index;
                }
                return result;
            }
        }

        static void ValidatePhysicalRoutes(WorldData world)
        {
            var physical=new PhysicalRoutes(world);
            Vector3 spawn=DeadAirOfficeLayout.Position(world.spawn.x,world.spawn.y,world.spawn.z);
            int start=-1;float nearest=float.PositiveInfinity;
            for(int index=0;index<physical.positions.Length;index++)
            {
                float distance=(physical.positions[index]-spawn).sqrMagnitude;
                if(!physical.clear[index]||distance>=nearest||Mathf.Abs(physical.positions[index].y-spawn.y)>.35f||
                    !physical.BodySweep(spawn,physical.positions[index]))continue;
                start=index;nearest=distance;
            }
            Require(start>=0&&nearest<4,"The entrance spawn has no physical capsule-clear navigation connection.");
            var queue=new Queue<int>();queue.Enqueue(start);physical.reachable.Add(start);
            while(queue.Count>0)foreach(int next in physical.links[queue.Dequeue()])if(physical.reachable.Add(next))queue.Enqueue(next);

            foreach(var door in world.doors)
            {
                float floor=door.y-1.4f;
                var normal=new Vector2(Mathf.Cos(door.rotation),-Mathf.Sin(door.rotation));
                var a=DeadAirOfficeLayout.Position(door.x+normal.x*.75f,floor,door.z+normal.y*.75f);
                var b=DeadAirOfficeLayout.Position(door.x-normal.x*.75f,floor,door.z-normal.y*.75f);
                Require(physical.BodyClear(a)&&physical.BodyClear(b)&&physical.BodySweep(a,b),
                    "The generated doorway blocks the player body after its leaf opens: "+door.id);
                Require(physical.ReachableApproach(a,.7f)>=0&&physical.ReachableApproach(b,.7f)>=0,
                    "The generated doorway has an unreachable side: "+door.id);
            }
            foreach(var room in world.rooms)
            {
                bool entered=false;
                foreach(int index in physical.reachable)
                {
                    var p=physical.positions[index];float sourceX=-p.x;
                    bool matchesFloor=Mathf.Abs(p.y-room.floorY)<.3f;
                    if(room.mapLabel=="HALL BASEMENT STAIRS")
                        foreach(var stair in world.stairs)
                            if(stair.lowerY<0&&stair.Contains(sourceX,p.z)&&Mathf.Abs(p.y-stair.HeightAt(p.z))<.3f)matchesFloor=true;
                    if(matchesFloor&&sourceX>room.minX+.25f&&sourceX<room.maxX-.25f&&
                        p.z>room.minZ+.25f&&p.z<room.maxZ-.25f){entered=true;break;}
                }
                Require(entered,"A generated room has no physically reachable interior: "+room.name);
            }
            foreach(var objective in world.objects)
            {
                float floor=objective.y< -1?-DeadAirOfficeLayout.FloorHeight:objective.y>=DeadAirOfficeLayout.FloorHeight?DeadAirOfficeLayout.FloorHeight:0;
                float vertical=objective.y-floor-1;
                float planarRange=Mathf.Sqrt(Mathf.Max(0,2.5f*2.5f-vertical*vertical));
                var point=DeadAirOfficeLayout.Position(objective.x,floor,objective.z);
                Require(physical.ReachableApproach(point,planarRange)>=0,
                    "A quest interaction has no physically reachable approach within its real range: "+objective.id);
            }
            Debug.Log("DEAD AIR PHYSICAL NAVIGATION PASSED: reachable="+physical.reachable.Count+"/"+world.navigationNodes.Length+
                "; body-blocked nodes="+physical.blockedNodes+", edges="+physical.blockedEdges+
                "; every doorway, room and quest approach checked against actual colliders; only known door leaves excluded.");
        }

        static void ValidateBranchedPursuit(WorldData world)
        {
            var rules=new DeadAirRules(world,"host","Route target","engineer",true,true);rules.Start("host");
            var target=rules.State.Player("host");target.x=0;target.y=0;target.z=-27;target.camera=target.flashlight=false;target.invulnerableUntil=9999;
            var enemy=rules.State.enemy;enemy.active=true;enemy.x=0;enemy.y=0;enemy.z=-6;
            float furthestSide=0;
            for(int tick=0;tick<320;tick++)
            {
                if(tick%60==0)rules.Radio(target.id,0);
                rules.Tick(.1f);furthestSide=Mathf.Max(furthestSide,Mathf.Abs(enemy.x));
                Require(!(Mathf.Abs(enemy.x)<5.7f&&enemy.z>-23.7f&&enemy.z<-11.3f),
                    "The enemy crossed the central room's walls instead of following its branch.");
            }
            Require(furthestSide>6.1f&&Vector2.Distance(new Vector2(enemy.x,enemy.z),new Vector2(target.x,target.z))<2.5f,
                "The enemy must use a side corridor and finish its pursuit behind the central room.");
        }

        static int Nearest(WorldData world,float x,float y,float z)
        {
            int result=-1;float best=float.PositiveInfinity;
            for(int i=0;i<world.navigationNodes.Length;i++)
            {
                var n=world.navigationNodes[i];float distance=(new Vector3(n.x,n.y,n.z)-new Vector3(x,y,z)).sqrMagnitude;
                if(distance<best){best=distance;result=i;}
            }
            return result;
        }

        static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Office navigation validation: "+message);}
    }
}
