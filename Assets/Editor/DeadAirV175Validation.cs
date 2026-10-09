using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirV175Validation
    {
        [MenuItem("Dead Air/Validate v1.7.5 Stair Guards and Fuel Locations")]
        public static void Validate()
        {
            var data = WorldData.Load();
            var station = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/station.prefab"));
            var exterior = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/World/exterior.prefab"));
            Require(station != null && exterior != null, "The saved station and courtyard must exist.");
            var offset = new Vector3(1000, 0, 0);
            station.transform.position = exterior.transform.position = offset;
            try
            {
                Physics.SyncTransforms();
                var parts = station.GetComponentsInChildren<Transform>(true);
                Require(!parts.Any(t => t.name.StartsWith("Stair side wall ", StringComparison.Ordinal)),
                    "The opaque stepped walls above the ground floor must be removed.");
                foreach (var stair in data.stairs)
                {
                    string level = stair.lowerY < 0 ? "basement" : "upper";
                    var sloping = parts.Single(t => t.name == "Steel stair rails:" + level);
                    var opening = parts.Single(t => t.name == "Steel opening rails:" + level);
                    foreach (float height in new[] { .24f, .64f, 1.06f })
                    {
                        for (int sample = 0; sample <= 24; sample++)
                        {
                            float z = Mathf.Lerp(stair.minZ, stair.maxZ, sample / 24f);
                            foreach (float x in new[] { stair.minX + .09f, stair.maxX - .09f })
                                GuardAt(sloping, Point(x, stair.HeightAt(z) + height, z), "Both sides of the " + level + " flight need continuous physical steel rails.");
                            foreach (float x in new[] { -1.89f, 1.89f })
                                GuardAt(opening, Point(x, stair.upperY + height, z), "The " + level + " floor opening needs guards along its complete edges.");
                        }
                        if (stair.upperY > 1)
                            for (int sample = 0; sample <= 12; sample++)
                                GuardAt(opening, Point(Mathf.Lerp(-1.89f, 1.89f, sample / 12f), stair.upperY + height, stair.maxZ),
                                    "The exposed far edge of the second-floor opening needs a complete end guard.");
                    }
                    Require(!opening.GetComponentsInChildren<Collider>().Any(c => Vector3.Distance(c.ClosestPoint(Point(0, stair.upperY + .64f, stair.minZ)), Point(0, stair.upperY + .64f, stair.minZ)) < .1f),
                        "The upper landing must remain open for the player to enter and leave the stairs.");
                }

                var cans = data.objects.Where(o => o.type == "canister").ToArray();
                var generator = data.props.Single(p => p.type == "dieselGenerator");
                Require(cans.Length == 3 && cans.Count(c => c.y < -1) == 1 && cans.Count(c => c.y >= -1) == 2,
                    "Exactly two diesel canisters must be in the yard and one in the basement.");
                foreach (var can in cans.Where(c => c.y >= -1))
                    Require(Vector2.Distance(new Vector2(can.x, can.z), new Vector2(generator.x, generator.z)) < 3.5f,
                        "A yard canister is too far from the diesel generator: " + can.id);
                foreach (var can in cans)
                {
                    var root = station.GetComponentsInChildren<Transform>(true).Concat(exterior.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "interactive:" + can.id);
                    Require(Vector3.Distance(root.position, Point(can.x, can.y, can.z)) < .001f,
                        "The saved physical canister must match its pickup position: " + can.id);
                }
                int signs = ValidatePlaques(station) + ValidatePlaques(exterior);
                Require(signs > data.doors.Length * 2, "Validate the saved room plaques and wayfinding signs.");
                Debug.Log("DEAD AIR V1.7.5 GEOMETRY PASSED: both stair flights, all floor-opening edges, open entry landings, two generator canisters and one basement canister, and " + signs + " physical fitted plaques.");

                Vector3 Point(float x, float y, float z) => DeadAirOfficeLayout.Position(x, y, z) + offset;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(station);
                UnityEngine.Object.DestroyImmediate(exterior);
                Physics.SyncTransforms();
            }
        }

        static int ValidatePlaques(GameObject root)
        {
            int count = 0;
            foreach (var sign in root.GetComponentsInChildren<DeadAirOfficeSign>(true))
            {
                Require(sign.FitToPlaque() && sign.IsContained(), "The generated glyph bounds must fit the plaque: " + sign.GetComponent<TextMesh>().text);
                var plate = sign.transform.parent.Find("Sign backing").GetComponent<MeshFilter>();
                Require(plate != null && plate.sharedMesh != null, "The persisted plaque mesh is needed to independently check its physical dimensions.");
                var board = plate.sharedMesh.bounds;
                var ink = sign.GetComponent<MeshRenderer>().localBounds;
                foreach (float x in new[] { ink.min.x, ink.max.x })
                    foreach (float y in new[] { ink.min.y, ink.max.y })
                    {
                        var point = plate.transform.InverseTransformPoint(sign.transform.TransformPoint(new Vector3(x, y, ink.center.z)));
                        Require(point.x >= board.min.x + .015f && point.x <= board.max.x - .015f && point.y >= board.min.y + .010f && point.y <= board.max.y - .010f,
                            "Visible text protrudes outside its physical plaque: " + sign.GetComponent<TextMesh>().text);
                    }
                count++;
            }
            return count;
        }

        static void GuardAt(Transform root, Vector3 point, string message)
        {
            Require(root.GetComponentsInChildren<Collider>().Any(c => c.enabled && Vector3.Distance(c.ClosestPoint(point), point) < .035f), message + " Sample: " + (point - new Vector3(1000, 0, 0)));
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("v1.7.5 visual geometry: " + message); }
    }
}
