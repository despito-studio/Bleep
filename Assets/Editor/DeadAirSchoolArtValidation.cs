using System;
using System.Linq;
using DeadAir;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class DeadAirSchoolArtValidation
{
    [MenuItem("Dead Air/Validate/School production art")]
    public static void Run()
    {
        var fixture = new GameObject("School production art validation");
        try
        {
            var school = new DeadAirSchoolWorld(fixture.transform, WorldData.Load());
            school.Initialize();
            var parts = school.Root.GetComponentsInChildren<Transform>(true);
            var assemblies = parts.Where(p => p.GetComponent<Renderer>() == null && p.GetComponent<Collider>() == null).ToArray();
            var renderers = school.Root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var collider in school.Root.GetComponentsInChildren<Collider>(true))
                Require(!collider.enabled || collider.transform.localToWorldMatrix.determinant > 0f,
                    "School collider retained an unsupported reflected physics basis: " + collider.name);
            RequireColliderCornerEquivalence(school.Root);
            foreach (var meshCollider in school.Root.GetComponentsInChildren<MeshCollider>(true).Where(c => c.enabled).Take(12))
            {
                var visual = meshCollider.GetComponentInParent<MeshFilter>();
                Require(visual != null && meshCollider.sharedMesh != visual.sharedMesh,
                    "Scanned school collision did not receive its reflected mesh copy.");
                Vector3[] source = visual.sharedMesh.vertices, corrected = meshCollider.sharedMesh.vertices;
                Require(source.Length == corrected.Length, "Scanned collision changed its authored surface topology.");
                int[] samples = { 0, source.Length / 3, source.Length / 2, source.Length - 1 };
                Require(samples.All(i => Vector3.Distance(visual.transform.TransformPoint(source[i]),
                    meshCollider.transform.TransformPoint(corrected[i])) < .0001f),
                    "Scanned collision counterreflection changed the fitted world surface.");
            }
            foreach (var renderer in renderers)
            {
                Require((renderer.transform.localScale - Vector3.one).sqrMagnitude < 0.000001f,
                    "Scaled school mesh root: " + renderer.name);
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Require(mesh != null && mesh.vertexCount > 0, "Missing mesh: " + renderer.name);
                Require(mesh.uv.Length == mesh.vertexCount, "Missing metric UVs: " + renderer.name);
                Require(mesh.tangents.Length == mesh.vertexCount, "Missing normal-map tangents: " + renderer.name);
            }
            Require(school.Pills.Count == 9 && school.EvidenceTapes.Count == 5, "School pickup count changed.");
            foreach (var pill in school.Pills)
                Require(Vector3.Distance(pill.go.transform.localPosition, pill.position) < 0.001f, "Pill anchor changed: " + pill.id);
            foreach (var tape in school.EvidenceTapes)
                Require(Vector3.Distance(tape.go.transform.localPosition, tape.position) < 0.001f, "Tape anchor changed: " + tape.id);
            Physics.SyncTransforms();
            foreach (var pill in school.Pills) RequirePickupSupport(school.Root, pill.go.transform, pill.id);
            foreach (var tape in school.EvidenceTapes) RequirePickupSupport(school.Root, tape.go.transform, tape.id);
            var teachingWalls = assemblies.Where(p => p.name == "Teaching wall assembly").ToArray();
            Require(teachingWalls.Length == 26, "Classroom teaching walls were not rebuilt consistently; actual=" + teachingWalls.Length);
            foreach (var teaching in teachingWalls)
            {
                var board = teaching.Find("Teaching board face");
                Vector3 normal = board.localToWorldMatrix.inverse.transpose.MultiplyVector(Vector3.forward).normalized;
                var backing = Physics.RaycastAll(board.position - normal * .06f, -normal, .4f, ~0, QueryTriggerInteraction.Ignore);
                Require(backing.Any(hit => hit.transform.IsChildOf(school.Root) && !hit.transform.IsChildOf(teaching) &&
                    hit.collider.GetComponentInParent<Renderer>()?.sharedMaterial?.name == "SchoolPlaster"),
                    "A teaching board is mounted across glazing or is floating away from an opaque classroom partition.");
                if (Resources.Load<GameObject>("ArtProduction/Models/wall-clock") != null)
                    Require(teaching.Find("Scanned wall-clock") != null, "A classroom retained an unreadable primitive clock disc.");
            }
            string[] visibleCeilingNames = { "Ceiling Left Wing", "Ceiling Right Wing", "Ceiling Center South", "Canopy Roof" };
            Require(visibleCeilingNames.All(name => renderers.Count(r => r.name == name) == 1),
                "A visible classroom or entrance soffit is missing from the ceiling coverage check.");
            var ceilingSlabs = renderers.Where(r => r.GetComponent<MeshFilter>().sharedMesh.name.StartsWith("School Metric ceiling:") ||
                visibleCeilingNames.Contains(r.name)).ToArray();
            Require(ceilingSlabs.Length >= 10, "School floor/roof slab undersides did not receive plaster ceiling surfaces.");
            var plaster = Resources.Load<Material>("ArtProduction/Materials/plaster");
            Require(plaster != null && plaster.GetTexture("_MainTex") != null, "School ceiling plaster source is missing.");
            foreach (var ceiling in ceilingSlabs)
            {
                var materials = ceiling.sharedMaterials;
                var ceilingMesh = ceiling.GetComponent<MeshFilter>().sharedMesh;
                string details = ceiling.name + "; submeshes=" + ceilingMesh.subMeshCount +
                    "; materials=" + string.Join(", ", materials.Select(m => m == null ? "null" :
                        m.name + " [gloss=" + m.GetFloat("_Glossiness") + ", bump=" + m.GetFloat("_BumpScale") +
                        ", mask=" + m.GetFloat("_MaskMapEnabled") + ", metal=" + m.GetFloat("_Metallic") + "]"));
                Require(ceilingMesh.subMeshCount == 2 && materials.Length == 2 && materials[1] != null && materials[1] != materials[0] &&
                    materials[1].GetTexture("_MainTex") == plaster.GetTexture("_MainTex") &&
                    materials[1].GetFloat("_Glossiness") <= .05f && materials[1].GetFloat("_BumpScale") <= .13f &&
                    materials[1].GetFloat("_MaskMapEnabled") <= .01f && materials[1].GetFloat("_Metallic") <= .01f,
                    "School ceiling reused floor finish or excessive gloss/normal strength: " + details);
                int[] underside = ceilingMesh.GetTriangles(1);
                Require(underside.Length >= 6 && underside.All(index => ceilingMesh.normals[index].y < -.5f),
                    "School plaster material is not assigned to its visible downward faces: " + details);
            }

            if (Resources.Load<GameObject>("ArtProduction/Models/wooden-bench") != null)
            {
                var benches = assemblies.Where(p => p.name == "Scanned wooden-bench").ToArray();
                Require(benches.Length == 5, "School benches are missing their fitted scanned seats; actual=" + benches.Length);
                foreach (var bench in benches)
                {
                    float foot = bench.position.y;
                    float top = bench.GetComponentsInChildren<Renderer>(true).Max(r => r.bounds.max.y);
                    Require(top - foot >= .47f && top - foot <= .51f, "Backless school bench seat was stretched to backrest height.");
                    foreach (var collider in bench.GetComponentsInChildren<Collider>(true))
                        Require(collider.bounds.max.y <= foot + .53f, "Backless school bench retained an invisible backrest collider.");
                }
            }
            var trafficMirrors = school.Root.GetComponentsInChildren<DeadAirArtMirror>(true);
            Require(trafficMirrors.Length == 2, "School traffic mirrors are missing actual reflection components.");
            foreach (var mirror in trafficMirrors)
            {
                Require(mirror.name == "mirror:traffic", "Traffic mirror render pane has no semantic mirror name.");
                Require(Vector3.Dot(mirror.PlaneNormal, mirror.transform.TransformDirection(Vector3.up)) > .999f,
                    "Traffic mirror plane normal does not match the cylinder's local-Y front face.");
            }

            var sliders = school.Root.GetComponentsInChildren<DeadAirArtMotionSlidingLeaf>(true).Select(leaf => leaf.transform).ToArray();
            Require(sliders.Length > 20, "Classroom door family is missing; actual=" + sliders.Length);
            foreach (var slider in sliders)
            {
                Require(slider.GetComponentsInChildren<Collider>(true).Length == 0, "Decorative slider blocks a gameplay passage.");
                var glass = slider.Find("Door Window Glass");
                Require(glass != null && glass.GetComponent<MeshRenderer>() != null, "Door glass missing.");
                var pane = glass.GetComponent<MeshFilter>().sharedMesh.bounds;
                Vector3 testPoint = glass.localPosition + new Vector3(pane.extents.x * 0.5f, 0f, 0f);
                foreach (var opaque in slider.GetComponentsInChildren<MeshRenderer>())
                {
                    if (opaque.transform == glass) continue;
                    var bounds = opaque.GetComponent<MeshFilter>().sharedMesh.bounds;
                    Require(!bounds.Contains(opaque.transform.InverseTransformPoint(slider.TransformPoint(testPoint))),
                        "Opaque panel fills the door glazing opening: " + opaque.name);
                }
            }
            var shoeLockers = assemblies.Where(p => p.name == "Getabako Shoe Lockers").ToArray();
            Require(shoeLockers.Length == 2, "School entrance shoe-locker assembly count changed; actual=" + shoeLockers.Length);
            foreach (var root in shoeLockers)
            {
                int partitions = root.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == "Cubby vertical partition");
                Require(partitions == 11, "Shoe locker visible partitions changed; actual=" + partitions + "; position=" + root.localPosition);
                Require(root.Find("Locker Main Body") == null, "Solid locker volume still occludes cubbies.");
            }
            var musicRoom = assemblies.Single(p => p.name == "Music Room (音楽室)");
            var musicChairs = musicRoom.Cast<Transform>().Where(p => p.name == "Chair").ToArray();
            Require(musicChairs.Length == 12, "Music-room seating count changed; actual=" + musicChairs.Length);
            foreach (var chair in musicChairs)
            {
                var chairParts = chair.GetComponentsInChildren<MeshRenderer>();
                float height = chairParts.Max(r => r.bounds.max.y) - chairParts.Min(r => r.bounds.min.y);
                Require(height >= .85f && height <= .95f, "Music chair retained miniature cube dimensions.");
                RequireFloorContact(school.Root, chair, "Chair foot ferrule", 4);
                Vector3 front = chair.localToWorldMatrix.MultiplyVector(Vector3.back).normalized;
                Vector3 towardsStand = school.Root.localToWorldMatrix.MultiplyVector(Vector3.forward).normalized;
                Require(Vector3.Dot(front, towardsStand) > .99f, "Music chair faces away from its music stand.");
            }
            var stool = musicRoom.Find("Piano Stool");
            Require(stool != null && stool.Find("Piano stool seat") != null, "Piano stool retained a solid cube body.");
            RequireFloorContact(school.Root, stool, "Piano stool leg", 4);
            var musicStands = musicRoom.Cast<Transform>().Where(p => p.name == "Music Stand").ToArray();
            Require(musicStands.Length == 12, "Music stand count changed; actual=" + musicStands.Length);
            foreach (var stand in musicStands) RequireFloorContact(school.Root, stand, "Music stand rubber foot", 3);
            var sinks = assemblies.Where(p => p.name == "Recessed sink bowl").ToArray();
            Require(sinks.Length == 2 && sinks.All(p => p.GetComponentsInChildren<MeshRenderer>(true).Length >= 5),
                "Laboratory sink has no open bowl assembly; actual=" + sinks.Length);
            foreach (var sink in sinks)
            {
                var bottom = sink.Find("Sink bowl bottom");
                Bounds bottomBounds = bottom.GetComponent<MeshFilter>().sharedMesh.bounds;
                bottomBounds.center += bottom.localPosition;
                foreach (Transform wall in sink)
                {
                    if (wall.GetComponent<MeshRenderer>() == null) continue;
                    int axis = wall.name == "Sink side wall" ? 0 : wall.name == "Sink end wall" ? 2 : -1;
                    if (axis < 0) continue;
                    Bounds wallBounds = wall.GetComponent<MeshFilter>().sharedMesh.bounds;
                    wallBounds.center += wall.localPosition;
                    float overlap = wall.localPosition[axis] > 0f ? bottomBounds.max[axis] - wallBounds.min[axis] :
                        wallBounds.max[axis] - bottomBounds.min[axis];
                    Require(overlap >= .0045f && overlap <= .0055f && bottomBounds.max.y > wallBounds.min.y,
                        "Sink bottom does not seal the beveled bowl wall: " + wall.name + "; overlap=" + overlap);
                }
            }
            int counterSections = renderers.Count(r => r.name == "Counter section around sink opening");
            Require(counterSections == 8, "Laboratory counters do not preserve both sink openings; actual=" + counterSections);
            Require(renderers.Any(r => r.name == "Tabletop with eased edge" && r.GetComponentsInChildren<Collider>(true).Length == 0 && r.shadowCastingMode == ShadowCastingMode.On),
                "Collision and shadow policies are still coupled.");
            if (Resources.Load<GameObject>("ArtProduction/Models/pine-tree") != null && Resources.Load<GameObject>("ArtProduction/Models/fir-tree") != null)
            {
                var scannedTrees = assemblies.Where(p => p.name == "Scanned pine-tree" || p.name == "Scanned fir-tree").ToArray();
                Require(scannedTrees.Length >= 32, "School forest is still using primitive crowns despite available scanned tree resources; actual=" + scannedTrees.Length);
                foreach (var tree in scannedTrees)
                {
                    Require((tree.localScale - Vector3.one).sqrMagnitude < .000001f, "A scanned school tree has a scaled root.");
                    Require(tree.GetComponentsInChildren<CapsuleCollider>(true).Length == 1 && tree.GetComponentsInChildren<MeshCollider>(true).Length == 0,
                        "Scanned school trees must use a cheap trunk capsule, without dense foliage mesh collision.");
                }
            }
            foreach (var shrub in assemblies.Where(p => p.name == "Scanned shrub"))
                Require(shrub.GetComponentsInChildren<Collider>(true).Length == 0, "School shrub foliage introduced an invisible movement barrier.");
            Debug.Log($"SCHOOL ART VALIDATION PASS: {renderers.Length} renderers; {sliders.Length} separate sliding leaves; 14 supported pickup anchors; unit-scale mesh roots, UV/tangent data, bench seat height, traffic mirror planes, hollow shelves and sink openings verified.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequirePickupSupport(Transform school, Transform pickup, string id)
    {
        Vector3 anchor = pickup.position;
        var hits = Physics.RaycastAll(anchor + Vector3.up * .10f, Vector3.down, .35f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (!hit.transform.IsChildOf(school) || hit.transform.IsChildOf(pickup) || hit.normal.y < .5f) continue;
            float gap = anchor.y - hit.point.y;
            if (gap >= -.015f && gap <= .12f) return;
        }
        throw new InvalidOperationException("School pickup has no floor/furniture support within 12 cm of its unchanged anchor: " + id);
    }

    private static void RequireFloorContact(Transform school, Transform furniture, string footName, int count)
    {
        var feet = furniture.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == footName).ToArray();
        Require(feet.Length == count, "Missing furniture feet: " + furniture.name + "; " + footName);
        foreach (var foot in feet)
        {
            Vector3 origin = foot.bounds.center;
            origin.y = foot.bounds.min.y + .2f;
            var supports = Physics.RaycastAll(origin, Vector3.down, .4f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.transform.IsChildOf(school) && !hit.transform.IsChildOf(furniture) && hit.normal.y > .9f)
                .OrderBy(hit => hit.distance).ToArray();
            Require(supports.Length > 0 && Mathf.Abs(foot.bounds.min.y - supports[0].point.y) <= .003f,
                "Music furniture foot floats above or sinks into the actual floor: " + furniture.name + "; " + footName);
        }
    }

    private static void RequireColliderCornerEquivalence(Transform school)
    {
        var fixture = new GameObject("School collider basis validation").transform;
        fixture.SetParent(school, false);
        fixture.localPosition = new Vector3(110f, 24f, 130f);
        fixture.localRotation = Quaternion.Euler(18f, 31f, -12f);
        Vector3 center = new Vector3(.23f, .6f, -.17f), size = new Vector3(1.4f, 2.1f, .75f);
        try
        {
            var box = DeadAirSchoolPhysics.AddBox(fixture, size, center);
            Require(box.transform != fixture && box.name == fixture.name,
                "Reflected school collider did not receive a named positive-basis child.");
            RequireBoxCorners(fixture, center, size, box);
            fixture.localPosition += new Vector3(1.7f, -.4f, .8f);
            fixture.localRotation *= Quaternion.Euler(-11f, 27f, 9f);
            RequireBoxCorners(fixture, center, size, box);
            var capsule = DeadAirSchoolPhysics.AddCapsule(fixture, .18f, 1.2f, center, 0);
            Vector3 axis = fixture.TransformVector(Vector3.right).normalized;
            Vector3 capsuleCenter = fixture.TransformPoint(center);
            Vector3 extent = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)) * .42f + Vector3.one * .18f;
            Physics.SyncTransforms();
            Require(capsule.transform.localToWorldMatrix.determinant > 0f &&
                Vector3.Distance(capsule.bounds.center, capsuleCenter) < .002f && Vector3.Distance(capsule.bounds.extents, extent) < .002f,
                "School capsule counterreflection changed an off-center rotated capsule's physical surface.");
            fixture.gameObject.SetActive(false);
            Require(!box.gameObject.activeInHierarchy, "School collider became detached from its visual parent's activation.");
            fixture.gameObject.SetActive(true);
            var positive = new GameObject("Positive school collider ancestor").transform;
            positive.SetParent(fixture, false);
            positive.localScale = new Vector3(-1f, 1f, 1f);
            var positiveBox = DeadAirSchoolPhysics.AddBox(positive, size, center);
            Require(positiveBox.transform == positive, "Positive school ancestor was reflected unnecessarily.");
            RequireBoxCorners(positive, center, size, positiveBox);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture.gameObject);
        }
    }

    private static void RequireBoxCorners(Transform authored, Vector3 center, Vector3 size, BoxCollider box)
    {
        Require(box.transform.localToWorldMatrix.determinant > 0f && box.size.x > 0f && box.size.y > 0f && box.size.z > 0f,
            "School box collider has an invalid effective matrix or size.");
        Vector3[] expected = new Vector3[8], actual = new Vector3[8];
        Bounds expectedBounds = new Bounds();
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
            expected[corner] = authored.TransformPoint(center + Vector3.Scale(size * .5f, sign));
            actual[corner] = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f, sign));
            if (corner == 0) expectedBounds = new Bounds(expected[corner], Vector3.zero);
            else expectedBounds.Encapsulate(expected[corner]);
        }
        Require(expected.All(p => actual.Any(q => (p - q).sqrMagnitude < .00000001f)),
            "School physics counterreflection changed an off-center rotated box's world corners.");
        Physics.SyncTransforms();
        Require(Vector3.Distance(box.bounds.min, expectedBounds.min) < .002f && Vector3.Distance(box.bounds.max, expectedBounds.max) < .002f,
            "PhysX box bounds no longer match the authored reflected geometry.");
    }
}
