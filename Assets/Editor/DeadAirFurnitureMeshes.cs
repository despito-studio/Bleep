using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadAir.Editor
{
    /// <summary>Small shared authoring meshes; the office builder merges these into its saved room batches.</summary>
    public static class DeadAirFurnitureMeshes
    {
        const string Folder="Assets/Office/Meshes";
        static readonly Dictionary<string,Mesh> cache=new Dictionary<string,Mesh>();
        sealed class Shape
        {
            public readonly List<Vector3> v=new List<Vector3>();
            public readonly List<Vector2> uv=new List<Vector2>();
            public readonly List<int> t=new List<int>();
            public int Vertex(Vector3 p,Vector2 tex){int i=v.Count;v.Add(p);uv.Add(tex);return i;}
            public void Triangle(int a,int b,int c){t.Add(a);t.Add(b);t.Add(c);}
            public void Quad(int a,int b,int c,int d){Triangle(a,b,c);Triangle(a,c,d);}
        }
        static string Key(string type,params float[] dimensions)
            =>"furniture-"+type+"-"+string.Join("-",Array.ConvertAll(dimensions,n=>n.ToString("F4",CultureInfo.InvariantCulture)));
        static Mesh Save(string key,Shape shape,int[][] submeshes=null)
        {
            if(cache.TryGetValue(key,out var ready))return ready;
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+key+".asset");
            if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,Folder+"/"+key+".asset");}
            mesh.Clear();mesh.name=key;mesh.indexFormat=shape.v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(shape.v);mesh.SetUVs(0,shape.uv);
            mesh.subMeshCount=submeshes==null?1:submeshes.Length;
            if(submeshes==null)mesh.SetTriangles(shape.t,0);else for(int i=0;i<submeshes.Length;i++)mesh.SetTriangles(submeshes[i],i);
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);cache[key]=mesh;return mesh;
        }

        public static GameObject Install(Transform parent,string name,Mesh mesh,Vector3 position,Material material,bool collide=false)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;
            r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
            if(collide){var b=go.AddComponent<BoxCollider>();b.center=mesh.bounds.center;b.size=mesh.bounds.size;}
            return go;
        }

        /// <summary>Rounded horizontal outline with a narrow bevel on the top and bottom edges.</summary>
        public static Mesh RoundedBox(Vector3 size,float radius,float bevel=.012f)
        {
            radius=Mathf.Clamp(radius,.002f,Mathf.Min(size.x,size.z)*.45f);bevel=Mathf.Clamp(bevel,0,Mathf.Min(size.y*.45f,radius*.7f));
            string key=Key("rounded",size.x,size.y,size.z,radius,bevel);if(cache.TryGetValue(key,out var ready))return ready;
            var s=new Shape();int steps=radius<.015f?1:3,count=4*(steps+1);float hx=size.x/2,hz=size.z/2,hy=size.y/2;
            float[] ys=bevel<.00001f?new[]{-hy,hy}:new[]{-hy,-hy+bevel,hy-bevel,hy};
            int rows=ys.Length;
            for(int row=0;row<rows;row++)
            {
                float inset=row==0||row==rows-1?bevel:0,rx=hx-inset,rz=hz-inset,rr=Mathf.Max(.001f,radius-inset);
                for(int corner=0;corner<4;corner++)for(int i=0;i<=steps;i++)
                {
                    float a=(corner*90+i*90f/steps)*Mathf.Deg2Rad;
                    float cx=(corner==0||corner==3?1:-1)*(rx-rr),cz=(corner<2?1:-1)*(rz-rr);
                    s.Vertex(new Vector3(cx+Mathf.Cos(a)*rr,ys[row],cz+Mathf.Sin(a)*rr),new Vector2((corner*(steps+1)+i)/(float)count*(size.x+size.z),ys[row]/2));
                }
            }
            for(int row=0;row<rows-1;row++)for(int i=0;i<count;i++)
            {int a=row*count+i,b=row*count+(i+1)%count;s.Quad(a,a+count,b+count,b);}
            foreach(int row in new[]{0,rows-1})
            {
                int center=s.Vertex(new Vector3(0,ys[row],0),new Vector2(hx/2,hz/2)),first=s.v.Count;
                for(int i=0;i<count;i++){Vector3 p=s.v[row*count+i];s.Vertex(p,new Vector2((p.x+hx)/2,(p.z+hz)/2));}
                for(int i=0;i<count;i++)if(row==0)s.Triangle(center,first+i,first+(i+1)%count);else s.Triangle(center,first+(i+1)%count,first+i);
            }
            return Save(key,s);
        }

        /// <summary>A closed binding with visible thin covers, ivory page surfaces and a faceted rounded spine.</summary>
        public static Mesh Book(float width,float height,float depth,bool label)
        {
            string key=Key(label?"book-label":"book",width,height,depth);if(cache.TryGetValue(key,out var ready))return ready;
            var s=new Shape();var cover=new List<int>();var paper=new List<int>();
            float outer=width/2,inner=outer-.006f,back=-depth/2,pageBack=back+.004f,front=depth/2;
            float radius=Mathf.Min(.006f,width*.12f),spine=front-.025f,half=height/2;
            // Broad outer covers and the front's three visible cloth facets.
            BookFace(s,cover,new Vector3(-outer,-half,back),new Vector3(-outer,half,back),new Vector3(-outer,half,front-radius),new Vector3(-outer,-half,front-radius),Vector3.left);
            BookFace(s,cover,new Vector3(outer,-half,back),new Vector3(outer,-half,front-radius),new Vector3(outer,half,front-radius),new Vector3(outer,half,back),Vector3.right);
            Vector2[] frontProfile={new Vector2(-outer,front-radius),new Vector2(-outer+radius,front),new Vector2(outer-radius,front),new Vector2(outer,front-radius)};
            for(int i=0;i<3;i++)
            {
                var a=frontProfile[i];var b=frontProfile[i+1];Vector3 outward=new Vector3(-(b.y-a.y),0,b.x-a.x).normalized;
                BookFace(s,cover,new Vector3(a.x,-half,a.y),new Vector3(b.x,-half,b.y),new Vector3(b.x,half,b.y),new Vector3(a.x,half,a.y),outward);
            }
            // The back exposes actual cover edges around a recessed page block.
            foreach(float side in new[]{-1f,1f})
            {
                float xOuter=side*outer,xInner=side*inner;
                BookFace(s,cover,new Vector3(xOuter,-half,back),new Vector3(xInner,-half,back),new Vector3(xInner,half,back),new Vector3(xOuter,half,back),Vector3.back);
                BookFace(s,cover,new Vector3(xInner,-half,back),new Vector3(xInner,-half,pageBack),new Vector3(xInner,half,pageBack),new Vector3(xInner,half,back),side<0?Vector3.right:Vector3.left);
            }
            BookFace(s,paper,new Vector3(-inner,-half,pageBack),new Vector3(inner,-half,pageBack),new Vector3(inner,half,pageBack),new Vector3(-inner,half,pageBack),Vector3.back);
            Vector2[] spineCap={new Vector2(-outer,spine),new Vector2(-inner,spine),new Vector2(inner,spine),new Vector2(outer,spine),
                new Vector2(outer,front-radius),new Vector2(outer-radius,front),new Vector2(-outer+radius,front),new Vector2(-outer,front-radius)};
            var capTriangles=Triangulate(spineCap);
            foreach(float y in new[]{-half,half})
            {
                Vector3 normal=y<0?Vector3.down:Vector3.up;
                foreach(float side in new[]{-1f,1f})
                {
                    float xOuter=side*outer,xInner=side*inner;
                    BookFace(s,cover,new Vector3(xOuter,y,back),new Vector3(xInner,y,back),new Vector3(xInner,y,spine),new Vector3(xOuter,y,spine),normal);
                }
                BookFace(s,paper,new Vector3(-inner,y,pageBack),new Vector3(inner,y,pageBack),new Vector3(inner,y,spine),new Vector3(-inner,y,spine),normal);
                int first=s.v.Count;foreach(var point in spineCap)s.Vertex(new Vector3(point.x,y,point.y),new Vector2(point.x/2,point.y/2));
                for(int i=0;i<capTriangles.Count;i+=3)
                {int a=first+capTriangles[i],b=first+capTriangles[i+1],c=first+capTriangles[i+2];if(y>0){int swap=b;b=c;c=swap;}cover.AddRange(new[]{a,b,c});}
            }
            if(label)
            {
                float labelWidth=width*.72f,labelY=-half+.067f;
                BookFace(s,paper,new Vector3(-labelWidth/2,labelY-.021f,front+.0007f),new Vector3(labelWidth/2,labelY-.021f,front+.0007f),
                    new Vector3(labelWidth/2,labelY+.021f,front+.0007f),new Vector3(-labelWidth/2,labelY+.021f,front+.0007f),Vector3.forward);
            }
            return Save(key,s,new[]{cover.ToArray(),paper.ToArray()});
        }

        static void BookFace(Shape shape,List<int> indices,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
        {
            int first=shape.v.Count;
            foreach(var p in new[]{a,b,c,d})
            {
                Vector2 uv=Mathf.Abs(normal.y)>.5f?new Vector2(p.x/2,p.z/2):Mathf.Abs(normal.z)>.5f?new Vector2(p.x/2,p.y/2):new Vector2(p.z/2,p.y/2);
                shape.Vertex(p,uv);
            }
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)>=0)indices.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
            else indices.AddRange(new[]{first,first+2,first+1,first,first+3,first+2});
        }

        /// <summary>Closed vertical tapered cylinder or turned vessel; radii correspond to height rings.</summary>
        public static Mesh Turned(float[] heights,float[] radii,int sides=12)
        {
            if(heights.Length!=radii.Length||heights.Length<2)throw new ArgumentException("Turned mesh needs matching rings.");
            var dimensions=new List<float>(heights);dimensions.AddRange(radii);dimensions.Add(sides);string key=Key("turned",dimensions.ToArray());
            if(cache.TryGetValue(key,out var ready))return ready;var s=new Shape();int rows=heights.Length;
            for(int row=0;row<rows;row++)for(int i=0;i<sides;i++)
            {float a=i*Mathf.PI*2/sides;s.Vertex(new Vector3(Mathf.Cos(a)*radii[row],heights[row],Mathf.Sin(a)*radii[row]),new Vector2(i/(float)sides*Mathf.PI*radii[row],heights[row]/2));}
            for(int row=0;row<rows-1;row++)for(int i=0;i<sides;i++)
            {int a=row*sides+i,b=row*sides+(i+1)%sides;s.Quad(a,a+sides,b+sides,b);}
            foreach(int row in new[]{0,rows-1})
            {
                int center=s.Vertex(new Vector3(0,heights[row],0),Vector2.one*.5f),first=s.v.Count;
                for(int i=0;i<sides;i++){Vector3 p=s.v[row*sides+i];s.Vertex(p,new Vector2(p.x/2+.5f,p.z/2+.5f));}
                for(int i=0;i<sides;i++)if(row==0)s.Triangle(center,first+i,first+(i+1)%sides);else s.Triangle(center,first+(i+1)%sides,first+i);
            }
            return Save(key,s);
        }

        public static GameObject Support(Transform parent,string name,Vector3 a,Vector3 b,float lowerRadius,float upperRadius,Material material)
        {
            Vector3 direction=b-a;float length=direction.magnitude;
            bool grounded=Mathf.Abs(a.y)<.00001f&&direction.y>0;
            string key=Key("support",direction.x,direction.y,direction.z,lowerRadius,upperRadius,grounded?1:0);
            if(!cache.TryGetValue(key,out var mesh))
            {
                var turned=Turned(new[]{0f,length},new[]{lowerRadius,upperRadius},10);
                var shape=new Shape();var rotation=Quaternion.FromToRotation(Vector3.up,direction);
                var vertices=turned.vertices;var tex=turned.uv;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 p=rotation*vertices[i];
                    // Floor-attached slanted legs need sawn horizontal feet,
                    // not tilted cylinder caps cutting through the floor finish.
                    if(grounded&&Mathf.Abs(vertices[i].y)<.00001f)p.y=0;
                    if(grounded&&Mathf.Abs(vertices[i].y-length)<.00001f)p.y=direction.y;
                    shape.Vertex(p,tex[i]);
                }
                shape.t.AddRange(turned.triangles);mesh=Save(key,shape);
            }
            // Persist orientation in the mesh so culling/placement bounds follow
            // its real vertices instead of a rotated, oversized cylinder AABB.
            return Install(parent,name,mesh,a,material);
        }

        /// <summary>Curved solid plywood slab. Bowing across its width gives a proper chair-back silhouette.</summary>
        public static Mesh CurvedPanel(float width,float height,float thickness,float bow)
        {
            string key=Key("plywood",width,height,thickness,bow);if(cache.TryGetValue(key,out var ready))return ready;
            var s=new Shape();const int segments=10;
            for(int face=0;face<2;face++)for(int i=0;i<=segments;i++)
            {
                float x=-width/2+width*i/segments,z=bow*(1-Mathf.Pow(x*2/width,2))+(face==0?-thickness/2:thickness/2);
                s.Vertex(new Vector3(x,-height/2,z),new Vector2(x/2,0));s.Vertex(new Vector3(x,height/2,z),new Vector2(x/2,height/2));
            }
            int side=(segments+1)*2;
            for(int i=0;i<segments;i++)
            {
                int a=i*2,b=a+2;s.Quad(a,a+1,b+1,b);s.Quad(a+side,b+side,b+1+side,a+1+side);
                s.Quad(a,b,b+side,a+side);s.Quad(a+1,a+1+side,b+1+side,b+1);
            }
            s.Quad(0,side,1+side,1);int e=segments*2;s.Quad(e,e+1,e+1+side,e+side);
            return Save(key,s);
        }

        public static Mesh Leaf()
        {
            const string key="furniture-leaf-curved";if(cache.TryGetValue(key,out var ready))return ready;
            var s=new Shape();
            for(int face=0;face<2;face++)
            {
                int first=s.v.Count;
                s.Vertex(new Vector3(0,0,0),new Vector2(.5f,0));s.Vertex(new Vector3(-.085f,.16f,.025f),new Vector2(0,.45f));
                s.Vertex(new Vector3(0,.21f,.060f),new Vector2(.5f,.5f));s.Vertex(new Vector3(.085f,.16f,.025f),new Vector2(1,.45f));
                s.Vertex(new Vector3(0,.42f,.11f),new Vector2(.5f,1));
                int[] indices={0,1,2,0,2,3,1,4,2,2,4,3};
                for(int i=0;i<indices.Length;i+=3)if(face==0)s.Triangle(first+indices[i],first+indices[i+1],first+indices[i+2]);else s.Triangle(first+indices[i],first+indices[i+2],first+indices[i+1]);
            }
            return Save(key,s);
        }

        public static Mesh HangingCoat()
        {
            const string key="furniture-hanging-coat";if(cache.TryGetValue(key,out var ready))return ready;
            Vector2[] outline={new Vector2(-.08f,.52f),new Vector2(-.18f,.43f),new Vector2(-.27f,.13f),new Vector2(-.21f,-.04f),new Vector2(-.15f,.04f),new Vector2(-.15f,-.5f),new Vector2(.15f,-.5f),new Vector2(.15f,.04f),new Vector2(.21f,-.04f),new Vector2(.27f,.13f),new Vector2(.18f,.43f),new Vector2(.08f,.52f)};
            var s=new Shape();int count=outline.Length;
            var cap=Triangulate(outline);
            for(int face=0;face<2;face++)
            {
                int first=s.v.Count;
                foreach(var p in outline)s.Vertex(new Vector3(p.x,p.y,(face==0?-1:1)*(.035f+.01f*Mathf.Cos(p.x*12))),new Vector2(p.x+.5f,p.y+.5f));
                for(int i=0;i<cap.Count;i+=3)if(face==0)s.Triangle(first+cap[i],first+cap[i+2],first+cap[i+1]);else s.Triangle(first+cap[i],first+cap[i+1],first+cap[i+2]);
            }
            for(int i=0;i<count;i++){int a=i,b=(i+1)%count;s.Quad(a,b,b+count,a+count);}
            return Save(key,s);
        }

        static List<int> Triangulate(Vector2[] polygon)
        {
            var remaining=new List<int>();for(int i=0;i<polygon.Length;i++)remaining.Add(i);var result=new List<int>();
            while(remaining.Count>3)
            {
                bool clipped=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                    if(Cross(polygon[b]-polygon[a],polygon[c]-polygon[b])<=.00001f)continue;
                    bool occupied=false;
                    foreach(int point in remaining)
                    {
                        if(point==a||point==b||point==c)continue;Vector2 p=polygon[point];
                        if(Cross(polygon[b]-polygon[a],p-polygon[a])>=0&&Cross(polygon[c]-polygon[b],p-polygon[b])>=0&&Cross(polygon[a]-polygon[c],p-polygon[c])>=0){occupied=true;break;}
                    }
                    if(occupied)continue;result.AddRange(new[]{a,b,c});remaining.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)throw new InvalidOperationException("Coat outline cannot be triangulated.");
            }
            result.AddRange(remaining);return result;
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;

        public static void ValidateMesh(Mesh mesh)
        {
            if(mesh==null||!mesh.isReadable||mesh.vertexCount<8||mesh.triangles.Length<24)throw new InvalidOperationException("Furniture mesh is missing or empty.");
            if(!mesh.HasVertexAttribute(VertexAttribute.Normal)||!mesh.HasVertexAttribute(VertexAttribute.Tangent)||!mesh.HasVertexAttribute(VertexAttribute.TexCoord0))throw new InvalidOperationException("Furniture lacks shading data: "+mesh.name);
            foreach(var p in mesh.vertices)if(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z))throw new InvalidOperationException("Nonfinite furniture geometry: "+mesh.name);
            if(mesh.vertexCount>2000)throw new InvalidOperationException("Shared furniture part exceeds the geometry budget: "+mesh.name);
            if(mesh.name.StartsWith("furniture-book",StringComparison.Ordinal))ValidateClosedBook(mesh);
        }

        static void ValidateClosedBook(Mesh mesh)
        {
            var vertices=mesh.vertices;var triangles=mesh.triangles;var size=mesh.bounds.size;
            bool label=mesh.name.StartsWith("furniture-book-label-",StringComparison.Ordinal);
            float front=mesh.bounds.max.z-(label ? .0007f : 0),depth=front-mesh.bounds.min.z;
            Vector3 area=Vector3.zero;double volume=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                // The paper catalogue label is a tiny external face, not part of the solid binding.
                if(a.z>front+.0001f&&b.z>front+.0001f&&c.z>front+.0001f)continue;
                area+=Vector3.Cross(b-a,c-a)*.5f;volume+=Vector3.Dot(a,Vector3.Cross(b,c))/6.0;
            }
            float radius=Mathf.Min(.006f,size.x*.12f);
            double expected=(size.x*depth-(size.x-.012f)*.004f-radius*radius)*size.y;
            if(area.magnitude>.00002f||Math.Abs(volume-expected)>expected*.002)
                throw new InvalidOperationException("Book binding is open, inverted or retains overlapping internal faces: "+mesh.name);
        }
    }
}
