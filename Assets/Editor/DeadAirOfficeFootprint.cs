using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeadAir.Editor
{
    // Geometry follows the same occupied room union as navigation and room culling.
    public static class DeadAirOfficeFootprint
    {
        public struct Edge
        {
            public bool vertical;
            public float coordinate,from,to,outward;
        }
        public static IEnumerable<Edge> Boundary(WorldData data,float floorY)
        {
            var rooms=data.rooms.Where(r=>DeadAirOfficeLayout.IsInterior(r)&&Mathf.Abs(r.floorY-floorY)<.05f).ToArray();
            var xs=rooms.SelectMany(r=>new[]{r.minX,r.maxX}).Concat(new[]{-18f,18f}).Distinct().OrderBy(v=>v).ToArray();
            var zs=rooms.SelectMany(r=>new[]{r.minZ,r.maxZ}).Distinct().OrderBy(v=>v).ToArray();
            foreach(float x in xs)for(int j=0;j<zs.Length-1;j++)
            {
                float z=(zs[j]+zs[j+1])/2;
                bool left=DeadAirOfficeLayout.InteriorAt(data,x-.02f,z,floorY),right=DeadAirOfficeLayout.InteriorAt(data,x+.02f,z,floorY);
                if(left!=right)yield return new Edge{vertical=true,coordinate=x,from=zs[j],to=zs[j+1],outward=left?1:-1};
            }
            foreach(float z in zs)for(int i=0;i<xs.Length-1;i++)
            {
                float x=(xs[i]+xs[i+1])/2;
                bool below=DeadAirOfficeLayout.InteriorAt(data,x,z-.02f,floorY),above=DeadAirOfficeLayout.InteriorAt(data,x,z+.02f,floorY);
                if(below!=above)yield return new Edge{vertical=false,coordinate=z,from=xs[i],to=xs[i+1],outward=below?1:-1};
            }
        }
        public static IEnumerable<Rect> Slabs(WorldData data,float floorY,bool stairOpening)
        {
            var rooms=data.rooms.Where(r=>DeadAirOfficeLayout.IsInterior(r)&&Mathf.Abs(r.floorY-floorY)<.05f).ToArray();
            var xs=new SortedSet<float>(rooms.SelectMany(r=>new[]{r.minX,r.maxX}));
            var zs=new SortedSet<float>(rooms.SelectMany(r=>new[]{r.minZ,r.maxZ}));
            foreach(var s in data.stairs)if(stairOpening&&Mathf.Abs(s.upperY-floorY)<.05f)
            {xs.Add(s.minX);xs.Add(s.maxX);zs.Add(s.minZ);zs.Add(s.maxZ);}
            // Shadow-caster meshes and physical slab sections remain spatially bounded.
            for(float x=-20;x<=20;x+=10)xs.Add(x);
            for(float z=-50;z<=0;z+=10)zs.Add(z);
            var xx=xs.ToArray();var zz=zs.ToArray();
            for(int j=0;j<zz.Length-1;j++)
            {
                float z=(zz[j]+zz[j+1])/2,start=float.NaN;
                for(int i=0;i<xx.Length;i++)
                {
                    bool filled=false;
                    if(i<xx.Length-1)
                    {
                        float x=(xx[i]+xx[i+1])/2;
                        filled=DeadAirOfficeLayout.InteriorAt(data,x,z,floorY)&&(!stairOpening||!data.stairs.Any(s=>Mathf.Abs(s.upperY-floorY)<.05f&&x>s.minX&&x<s.maxX&&z>s.minZ&&z<s.maxZ));
                    }
                    if(filled&&float.IsNaN(start))start=xx[i];
                    bool split=filled&&i>0&&xx[i]%10==0&&!float.IsNaN(start)&&xx[i]>start;
                    if((!filled||split)&&!float.IsNaN(start))
                    {yield return Rect.MinMaxRect(start,zz[j],xx[i],zz[j+1]);start=filled?xx[i]:float.NaN;}
                }
            }
        }
    }
}
