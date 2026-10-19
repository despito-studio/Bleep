using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    internal static class DeadAirSchoolMeshes
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        internal static Mesh Box(Vector3 size, float bevel)
        {
            string key = "box:" + size.ToString("F4") + ":" + bevel.ToString("F4");
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            Vector3 h = size * 0.5f;
            float r = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.4f);
            Vector3 c = h - Vector3.one * r;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = Vector3.zero; n[axis] = sign;
                    var points = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = h[axis] * sign;
                        p[u] = c[u] * ((i == 0 || i == 3) ? -1 : 1);
                        p[v] = c[v] * ((i < 2) ? -1 : 1);
                        points[i] = p;
                    }
                    Face(vertices, normals, uv, triangles, points, n);
                }
            }
            if (r > 0f)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    for (int su = -1; su <= 1; su += 2)
                    for (int sv = -1; sv <= 1; sv += 2)
                    {
                        Vector3 a = Vector3.zero; a[axis] = -c[axis]; a[u] = h[u] * su; a[v] = c[v] * sv;
                        Vector3 b = a; b[axis] = c[axis];
                        Vector3 d = a; d[u] = c[u] * su; d[v] = h[v] * sv;
                        Vector3 e = d; e[axis] = c[axis];
                        Vector3 n = Vector3.zero; n[u] = su; n[v] = sv;
                        Face(vertices, normals, uv, triangles, new[] { a, b, e, d }, n.normalized);
                    }
                }
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 a = new Vector3(h.x * x, c.y * y, c.z * z);
                    Vector3 b = new Vector3(c.x * x, h.y * y, c.z * z);
                    Vector3 d = new Vector3(c.x * x, c.y * y, h.z * z);
                    Face(vertices, normals, uv, triangles, new[] { a, b, d }, new Vector3(x, y, z).normalized);
                }
            }
            return CacheMesh(key, vertices, normals, uv, triangles);
        }

        internal static Mesh Cylinder(float radius, float height, float innerRadius = 0f, int sides = 20)
        {
            string key = $"cylinder:{radius:F4}:{height:F4}:{innerRadius:F4}:{sides}";
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            float half = height * 0.5f;
            float rim = Mathf.Min(0.006f, Mathf.Min(radius * 0.08f, height * 0.08f));
            float[] levels = { -half, -half + rim, half - rim, half };
            float[] radii = { radius - rim, radius, radius, radius - rim };
            for (int ring = 0; ring < 3; ring++)
            {
                for (int i = 0; i <= sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    for (int j = 0; j < 2; j++)
                    {
                        vertices.Add(radial * radii[ring + j] + Vector3.up * levels[ring + j]);
                        normals.Add((radial + Vector3.up * (ring == 0 ? -1f : ring == 2 ? 1f : 0f)).normalized);
                        uv.Add(new Vector2(angle * radius, levels[ring + j]));
                    }
                    if (i < sides)
                    {
                        int k = vertices.Count - 2;
                        triangles.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
                    }
                }
            }
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 ra = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 rb = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                float capRadius = radius - rim;
                if (innerRadius > 0f)
                {
                    Face(vertices, normals, uv, triangles, new[] { ra * capRadius + Vector3.up * half, rb * capRadius + Vector3.up * half, rb * innerRadius + Vector3.up * half, ra * innerRadius + Vector3.up * half }, Vector3.up);
                    Face(vertices, normals, uv, triangles, new[] { ra * innerRadius + Vector3.up * half, rb * innerRadius + Vector3.up * half, rb * innerRadius - Vector3.up * (half - rim), ra * innerRadius - Vector3.up * (half - rim) }, -(ra + rb).normalized);
                    Face(vertices, normals, uv, triangles, new[] { Vector3.down * (half - rim), ra * innerRadius - Vector3.up * (half - rim), rb * innerRadius - Vector3.up * (half - rim) }, Vector3.up);
                }
                else Face(vertices, normals, uv, triangles, new[] { Vector3.up * half, ra * capRadius + Vector3.up * half, rb * capRadius + Vector3.up * half }, Vector3.up);
                Face(vertices, normals, uv, triangles, new[] { Vector3.down * half, ra * capRadius - Vector3.up * half, rb * capRadius - Vector3.up * half }, Vector3.down);
            }
            return CacheMesh(key, vertices, normals, uv, triangles);
        }

        internal static Mesh Branch(float baseRadius, float tipRadius, float height)
        {
            string key = $"branch:{baseRadius:F4}:{tipRadius:F4}:{height:F4}";
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            Mesh mesh = Object.Instantiate(Cylinder(baseRadius, height));
            mesh.name = "School Metric " + key;
            Vector3[] points = mesh.vertices;
            for (int i = 0; i < points.Length; i++)
            {
                float factor = Mathf.Lerp(1f, tipRadius / baseRadius, Mathf.InverseLerp(-height * 0.5f, height * 0.5f, points[i].y));
                points[i].x *= factor; points[i].z *= factor;
            }
            mesh.vertices = points;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Cache[key] = mesh;
            return mesh;
        }

        internal static Mesh FloorWithPlasterCeiling(Vector3 size, float bevel)
        {
            string key = "ceiling:" + size.ToString("F4") + ":" + bevel.ToString("F4");
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var mesh = Object.Instantiate(Box(size, bevel));
            mesh.name = "School Metric " + key;
            Vector3[] normals = mesh.normals;
            int[] indices = mesh.triangles;
            var upper = new List<int>();
            var underside = new List<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                float vertical = normals[indices[i]].y + normals[indices[i + 1]].y + normals[indices[i + 2]].y;
                var target = vertical < -1.5f ? underside : upper;
                target.Add(indices[i]); target.Add(indices[i + 1]); target.Add(indices[i + 2]);
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(upper, 0);
            mesh.SetTriangles(underside, 1);
            Cache[key] = mesh;
            return mesh;
        }

        private static void Face(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> triangles, Vector3[] points, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f)
                System.Array.Reverse(points);
            int start = vertices.Count;
            Vector3 abs = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            foreach (Vector3 p in points)
            {
                vertices.Add(p); normals.Add(normal);
                uv.Add(abs.y >= abs.x && abs.y >= abs.z ? new Vector2(p.x, p.z) : abs.x >= abs.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
            }
            for (int i = 1; i < points.Length - 1; i++) triangles.AddRange(new[] { start, start + i, start + i + 1 });
        }

        private static Mesh CacheMesh(string key, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> triangles)
        {
            var mesh = new Mesh { name = "School Metric " + key, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Cache[key] = mesh;
            return mesh;
        }
    }
}
