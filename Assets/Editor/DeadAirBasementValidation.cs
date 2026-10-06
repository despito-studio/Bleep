using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirBasementValidation
    {
        [MenuItem("Dead Air/Validate Basement Cells and Diary")]
        public static void Validate()
        {
            var data=WorldData.Load();
            var rooms=data.rooms.Where(r=>r.floorY< -1&&r.mapLabel!="HALL BASEMENT STAIRS").ToArray();
            var cells=rooms.Where(r=>r.mapLabel.StartsWith("CELL ",StringComparison.Ordinal)).ToArray();
            Require(cells.Length==6,"Six independent prison cells must be present.");
            float area=rooms.Sum(r=>(r.maxX-r.minX)*(r.maxZ-r.minZ));
            Require(Mathf.Abs(area-450)<.01f,"The basement must occupy 450 m², three times its previous 150 m².");
            for(int i=0;i<rooms.Length;i++)for(int j=i+1;j<rooms.Length;j++)
                Require(Mathf.Min(rooms[i].maxX,rooms[j].maxX)-Mathf.Max(rooms[i].minX,rooms[j].minX)<.01f||
                    Mathf.Min(rooms[i].maxZ,rooms[j].maxZ)-Mathf.Max(rooms[i].minZ,rooms[j].minZ)<.01f,"Basement rooms must not overlap.");
            var book=data.Objective(DeadAirPrisonBook.Id);
            Require(book!=null&&book.type=="book"&&Contains(cells.Single(r=>r.mapLabel=="CELL 01"),book.x,book.z),"The readable diary must be in cell 01.");
            Require(DeadAirPrisonBook.Pages.Length>=3&&DeadAirPrisonBook.Pages.All(p=>p.Length>120),"The diary needs readable authored pages.");
            Require(data.stairs.Any(s=>s.lowerY<0&&s.minZ==-49.5f&&s.maxZ==-39.5f),"The original basement stair flight must be retained.");
            ValidateGraph(data,cells);
            ValidateGeometry(data,cells,book);
            ValidateEnemyDescent(data);
            Debug.Log("DEAD AIR BASEMENT PASSED: 450 m², six separate barred cells, stair-connected navigation, physical gallery/cell passages, floor support, a reachable readable diary in cell 01 and monster descent to the basement breaker.");
        }

        static void ValidateEnemyDescent(WorldData data)
        {
            var rules=new DeadAirRules(data,"host","Basement route","engineer",true,true);rules.Start("host");
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var floorMethod=typeof(DeadAirRules).GetMethod("EnemyFloorHeight",flags);
            var waypointMethod=typeof(DeadAirRules).GetMethod("EnemyWaypoint",flags);
            Require(floorMethod!=null&&waypointMethod!=null,"Authoritative monster route methods are missing.");
            float Floor(float x,float z,float y)=>(float)floorMethod.Invoke(rules,new object[]{x,z,y});
            var basement=data.stairs.Single(s=>s.lowerY<0);
            float height=0;
            for(int step=1;step<=100;step++)
            {
                float z=Mathf.Lerp(basement.minZ,basement.maxZ,step/100f);
                height=Floor(0,z,height);
                Require(Mathf.Abs(height-basement.HeightAt(z))<.01f,"Monster must descend the basement flight despite its shared X/Z with the upper flight.");
            }
            Require(Mathf.Abs(Floor(0,basement.maxZ+.2f,height)-basement.lowerY)<.01f,"Monster must exit onto the basement floor.");
            // The normal upstairs flight must remain selectable from the ground floor.
            var upper=data.stairs.Single(s=>s.lowerY==0);height=0;
            for(int step=1;step<=100;step++)
            {
                float z=Mathf.Lerp(upper.maxZ,upper.minZ,step/100f);
                height=Floor(0,z,height);
                Require(Mathf.Abs(height-upper.HeightAt(z))<.01f,"Basement support must preserve the upper-floor route.");
            }
            foreach(var door in rules.State.doors){door.locked=false;door.open=true;}
            var enemy=rules.State.enemy;enemy.active=true;enemy.x=0;enemy.y=0;enemy.z=-50.5f;
            var breaker=data.Objective("breaker");var goal=new Vector3(breaker.x,basement.lowerY,breaker.z-.8f);
            for(int step=0;step<600;step++)
            {
                rules.State.elapsed+=.1f;
                var waypoint=(Vector2)waypointMethod.Invoke(rules,new object[]{enemy,goal.x,goal.y,goal.z});
                var next=Vector2.MoveTowards(new Vector2(enemy.x,enemy.z),waypoint,.12f);
                enemy.y=Floor(next.x,next.y,enemy.y);enemy.x=next.x;enemy.z=next.y;
                if(Vector3.Distance(new Vector3(enemy.x,enemy.y,enemy.z),goal)<.4f)break;
            }
            Require(Mathf.Abs(enemy.y-basement.lowerY)<.1f&&Vector2.Distance(new Vector2(enemy.x,enemy.z),new Vector2(goal.x,goal.z))<.5f,
                "Monster must use the shared stair footprint and reach the basement breaker approach.");
        }

        static void ValidateGraph(WorldData data,RoomData[] cells)
        {
            var links=data.navigationNodes.Select(_=>new List<int>()).ToArray();
            foreach(var edge in data.navigationLinks){links[edge.a].Add(edge.b);links[edge.b].Add(edge.a);}
            int start=Enumerable.Range(0,data.navigationNodes.Length).OrderBy(i=>
                (new Vector3(data.navigationNodes[i].x,data.navigationNodes[i].y,data.navigationNodes[i].z)-new Vector3(data.spawn.x,data.spawn.y,data.spawn.z)).sqrMagnitude).First();
            var reached=new HashSet<int>{start};var queue=new Queue<int>();queue.Enqueue(start);
            while(queue.Count>0)foreach(int next in links[queue.Dequeue()])if(reached.Add(next))queue.Enqueue(next);
            foreach(var cell in cells)
                Require(reached.Any(i=>Mathf.Abs(data.navigationNodes[i].y-cell.floorY)<.01f&&Contains(cell,data.navigationNodes[i].x,data.navigationNodes[i].z)),
                    "Cell has no authored route from the entrance: "+cell.mapLabel);
            Require(reached.Any(i=>data.navigationNodes[i].y> -3.9f&&data.navigationNodes[i].y< -.3f&&data.navigationNodes[i].z< -39.5f),
                "Basement graph must cross the actual stair flight.");
        }

        static void ValidateGeometry(WorldData data,RoomData[] cells,ObjectiveData book)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab");
            Require(prefab!=null,"Regenerated station prefab is missing.");
            var root=UnityEngine.Object.Instantiate(prefab);root.name="Basement geometry validation";
            var offset=new Vector3(500,0,0);root.transform.position=offset;
            try
            {
                var parts=root.GetComponentsInChildren<Transform>(true);
                Require(parts.Count(t=>t.name=="interactive:"+book.id)==1,"Diary visual must survive prefab batching exactly once.");
                foreach(var door in data.doors)
                {
                    var leaf=parts.Single(t=>t.name=="door:"+door.id);
                    foreach(var collider in leaf.GetComponentsInChildren<Collider>())collider.enabled=false;
                    if(door.id.StartsWith("door-prison-",StringComparison.Ordinal))
                        Require(leaf.GetComponentsInChildren<Transform>().Count(t=>t.name=="Prison door bar")>=7,"Prison doors must be physical barred leaves.");
                }
                Physics.SyncTransforms();
                foreach(var cell in cells)
                {
                    float side=cell.maxX<0?-1:1,z=(cell.minZ+cell.maxZ)/2;
                    Vector3 a=Point(side*4.8f,-35.5f),b=Point(side*8.5f,-35.5f),c=Point(side*8.5f,z),d=Point(side*12.25f,z);
                    Clear(a);Clear(b);Clear(c);Clear(d);
                    Passage(a,b);Passage(b,c);Passage(c,d);
                    Require(Physics.Raycast(d+Vector3.up*.15f,Vector3.down,out var support,.30f,~0,QueryTriggerInteraction.Ignore)&&
                        Mathf.Abs(support.point.y+DeadAirOfficeLayout.FloorHeight)<.02f,"Cell floor is missing or at the wrong elevation: "+cell.mapLabel);
                }
                var approach=Point(-13.3f,-29.45f);Clear(approach);
                Require(Vector3.Distance(approach+Vector3.up,DeadAirOfficeLayout.Position(book.x,book.y,book.z)+offset)<2.5f,
                    "The diary needs a body-clear approach in interaction range.");
                Require(DeadAirRules.HasInteractionSightline(approach+Vector3.up,DeadAirOfficeLayout.Position(book.x,book.y,book.z)+offset,book.id),
                    "The diary must be visible for interaction from its reachable approach.");

                Vector3 Point(float x,float z)=>DeadAirOfficeLayout.Position(x,-DeadAirOfficeLayout.FloorHeight,z)+offset;
                void Clear(Vector3 feet)
                {
                    Require(!Physics.CheckCapsule(feet+Vector3.up*.54f,feet+Vector3.up*1.48f,.22f,~0,QueryTriggerInteraction.Ignore),
                        "A basement approach is blocked by physical geometry: "+(feet-offset));
                }
                void Passage(Vector3 from,Vector3 to)
                {
                    Vector3 delta=to-from;if(delta.sqrMagnitude<.0001f)return;
                    Require(!Physics.CapsuleCast(from+Vector3.up*.54f,from+Vector3.up*1.48f,.22f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore),
                        "A basement gallery or cell doorway blocks the player capsule.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root);Physics.SyncTransforms(); }
        }

        static bool Contains(RoomData room,float x,float z)=>x>room.minX&&x<room.maxX&&z>room.minZ&&z<room.maxZ;
        static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Basement validation: "+message);}
    }
}
