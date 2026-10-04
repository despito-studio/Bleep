using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    // Dusty silk is actual closed thread geometry, with empty space between
    // curved strands. The support endpoints stay in the wall/ceiling plane.
    public static class DeadAirWebGeometry
    {
        public static Mesh CreateMesh(float depth = -.115f)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            const int spokes = 10;
            Vector3 Point(float spoke, float amount)
            {
                float angle = spoke * Mathf.PI * .5f / (spokes - 1);
                float x = Mathf.Cos(angle), y = Mathf.Sin(angle);
                float radius = amount / (x + y);
                float sag = depth * Mathf.Sin(amount * Mathf.PI) * (.7f + .3f * Mathf.Sin(angle * 2));
                return new Vector3(x * radius, -y * radius, sag);
            }
            for (int spoke = 0; spoke < spokes; spoke++)
            {
                var path = new Vector3[8];
                for (int step = 0; step < path.Length; step++) path[step] = Point(spoke, step / (float)(path.Length - 1));
                Tube(path, .0055f, vertices, uv, triangles);
            }
            foreach (float amount in new[] { .19f, .35f, .52f, .70f, .86f, 1f })
            {
                var path = new Vector3[(spokes - 1) * 3 + 1];
                for (int step = 0; step < path.Length; step++)
                {
                    float spoke = step / 3f;
                    path[step] = Point(spoke, amount);
                    // A slight sag between radial supports gives each ring
                    // its own silhouette without moving any attachment point.
                    path[step].z += Mathf.Sign(depth) * .012f * Mathf.Sin((spoke % 1) * Mathf.PI) * amount;
                }
                Tube(path, .0042f, vertices, uv, triangles);
            }
            var mesh = new Mesh { name = "Volumetric cobweb threads" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static void Tube(Vector3[] path, float radius, List<Vector3> vertices, List<Vector2> uv, List<int> triangles)
        {
            const int sides = 6;
            int first = vertices.Count;
            for (int step = 0; step < path.Length; step++)
            {
                Vector3 direction = path[Mathf.Min(step + 1, path.Length - 1)] - path[Mathf.Max(step - 1, 0)];
                direction.Normalize();
                Vector3 right = Vector3.Cross(direction, Vector3.forward).normalized;
                Vector3 up = Vector3.Cross(right, direction).normalized;
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2 / sides;
                    vertices.Add(path[step] + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
                    uv.Add(new Vector2(side / (float)sides, step / (float)(path.Length - 1)));
                }
            }
            for (int step = 0; step < path.Length - 1; step++)
            for (int side = 0; side < sides; side++)
            {
                int a = first + step * sides + side, b = first + step * sides + (side + 1) % sides;
                int c = b + sides, d = a + sides;
                triangles.AddRange(new[] { a, c, b, a, d, c });
            }
            int start = vertices.Count;
            vertices.Add(path[0]); uv.Add(Vector2.zero);
            vertices.Add(path[path.Length - 1]); uv.Add(Vector2.one);
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                triangles.AddRange(new[] { start, first + side, first + next });
                int end = first + (path.Length - 1) * sides;
                triangles.AddRange(new[] { start + 1, end + next, end + side });
            }
        }
    }
}
