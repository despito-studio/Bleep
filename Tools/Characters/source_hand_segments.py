"""Export the 44 static meshes consumed by DeadAirFirstPersonRig.

Call from Blender, after normalizing a textured T-pose source to metres,
Z-up and -Y-forward. The input body and its mesh are never modified. Source
forearm, cuff and palm slices retain their loop UVs; the individually posed
phalanges use atlas samples from the source hand instead of invented skin
materials. All output objects have identity transforms and centred canonical
mesh coordinates: Blender X=width, Z=length, Y=-Unity depth.
"""

from pathlib import Path
import math

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree


_PALM_OUTLINE = (
    (-.35, -.50), (.32, -.50), (.50, .24),
    (.26, .50), (-.38, .46), (-.50, .02),
)
_PHALANGES = ("proximal", "middle", "distal", "tip")


def _source_samples(body, uv_name):
    """Face-centre atlas samples avoid averaging across UV seams."""
    uv_data = body.data.uv_layers[uv_name].data
    samples = []
    for face in body.data.polygons:
        if not face.loop_total:
            continue
        point = Vector((0, 0, 0))
        uv = Vector((0, 0))
        for loop_index in face.loop_indices:
            point += body.matrix_world @ body.data.vertices[
                body.data.loops[loop_index].vertex_index
            ].co
            uv += uv_data[loop_index].uv
        point /= face.loop_total
        uv /= face.loop_total
        if all(math.isfinite(value) for value in (*point, *uv)):
            samples.append((point, tuple(uv), face.material_index))
    if not samples:
        raise ValueError("The source hand body has no usable atlas UV samples.")
    tree = KDTree(len(samples))
    for index, (point, _, _) in enumerate(samples):
        tree.insert(point, index)
    tree.balance()
    return tree, samples


def _sample(tree, samples, point, material_count):
    _, index, distance = tree.find(Vector(point))
    _, uv, material_index = samples[index]
    return {
        "uv": uv,
        "material_index": max(0, min(material_count - 1, material_index)),
        "distance_m": float(distance),
    }


def _normalize_canonical(bm, side=None):
    """Bake a source X-aligned slice, or canonical geometry, to unit bounds."""
    if side is not None:
        # This is a proper rotation: outward s*X becomes +Z, source -s*Z
        # becomes canonical +X, and source Y remains negative Unity depth.
        for vertex in bm.verts:
            x, y, z = vertex.co
            vertex.co = Vector((-side * z, y, side * x))
    if not bm.verts or not bm.faces:
        raise ValueError("Empty source hand slice.")
    lower = Vector(tuple(min(v.co[axis] for v in bm.verts) for axis in range(3)))
    upper = Vector(tuple(max(v.co[axis] for v in bm.verts) for axis in range(3)))
    extent = upper - lower
    if min(extent) < 1e-6:
        raise ValueError("Source hand slice has a degenerate dimension.")
    centre = (lower + upper) * .5
    for vertex in bm.verts:
        vertex.co = Vector(tuple(
            (vertex.co[axis] - centre[axis]) / extent[axis]
            for axis in range(3)
        ))
    return tuple(float(value) for value in extent)


def _cap_cut(bm, side, boundary, uv_layer, sample):
    boundary_edges = [
        edge for edge in bm.edges
        if edge.is_boundary and all(
            abs(side * vertex.co.x - boundary) < 2e-5
            for vertex in edge.verts
        )
    ]
    if not boundary_edges:
        return 0
    try:
        result = bmesh.ops.holes_fill(bm, edges=boundary_edges, sides=0)
    except (RuntimeError, ValueError):
        # An already open source shell can end in an incomplete edge loop.
        # The retained textured shell is still a valid renderable fragment.
        return 0
    faces = result.get("faces", [])
    for face in faces:
        face.material_index = sample["material_index"]
        face.smooth = False
        for loop in face.loops:
            loop[uv_layer].uv = sample["uv"]
    return len(faces)


def _finish_mesh(bm, name, uv_name, material_count):
    if len(bm.verts) < 6 or len(bm.faces) < 4:
        raise ValueError("Source hand slice is too small to use.")
    uv_layer = bm.loops.layers.uv.get(uv_name)
    if uv_layer is None:
        raise ValueError("A hand part lost its source atlas UV layer.")
    for face in bm.faces:
        face.material_index = max(0, min(material_count - 1, face.material_index))
        for loop in face.loops:
            if not all(math.isfinite(value) for value in loop[uv_layer].uv):
                raise ValueError("A hand part contains non-finite atlas UVs.")
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.normal_update()
    mesh = bpy.data.meshes.new(name + "_Mesh")
    try:
        bm.to_mesh(mesh)
        mesh.update()
        if not mesh.uv_layers or not mesh.polygons:
            raise ValueError("A hand part has no UVs or triangles.")
        return mesh
    except Exception:
        bpy.data.meshes.remove(mesh)
        raise


def _source_fragment(body, name, side, lower, upper, uv_name, sample,
                     material_count):
    bm = bmesh.new()
    try:
        bm.from_mesh(body.data)
        bm.transform(body.matrix_world)
        normal = Vector((side, 0, 0))
        for boundary, inner in ((lower, True), (upper, False)):
            bmesh.ops.bisect_plane(
                bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                dist=1e-7, plane_co=normal * boundary, plane_no=normal,
                clear_inner=inner, clear_outer=not inner,
            )
        uv_layer = bm.loops.layers.uv.get(uv_name)
        if uv_layer is None:
            raise ValueError("Source slice has no atlas UV layer.")
        caps = sum(_cap_cut(bm, side, boundary, uv_layer, sample)
                   for boundary in (lower, upper))
        original_extent = _normalize_canonical(bm, side)
        mesh = _finish_mesh(bm, name, uv_name, material_count)
        return mesh, {
            "method": "source_fragment",
            "signed_x_band_m": [float(lower), float(upper)],
            "source_extent_m": list(original_extent),
            "cap_faces": caps,
        }
    finally:
        bm.free()


def _canonical_mesh(name, shape, uv_name, sample, material_count, nail_sample=None):
    """Small anatomical meshes, using only the source's materials and atlas."""
    vertices = []
    faces = []
    nail_faces = []
    if shape == "palm":
        for depth in (-.5, .5):
            vertices.extend((x, depth, along) for x, along in _PALM_OUTLINE)
        count = len(_PALM_OUTLINE)
        faces.extend((tuple(range(count - 1, -1, -1)),
                      tuple(range(count, count * 2))))
        for index in range(count):
            next_index = (index + 1) % count
            faces.append((index, next_index, next_index + count, index + count))
    else:
        # +Z is longitudinal. The broader middle rings read as knuckles;
        # rounded end pieces retain a full unit length for runtime pad poses.
        rings = (
            ((-.5, .40), (-.3, .49), (-.06, .50), (.28, .44), (.5, .28))
            if shape == "tip" else
            ((-.5, .50), (-.30, .48), (.15, .45), (.5, .43))
        )
        sides = 8
        for along, radius in rings:
            for index in range(sides):
                angle = index * math.tau / sides
                vertices.append((math.cos(angle) * radius,
                                 math.sin(angle) * radius, along))
        for ring in range(len(rings) - 1):
            for index in range(sides):
                next_index = (index + 1) % sides
                faces.append((ring * sides + index, ring * sides + next_index,
                              (ring + 1) * sides + next_index,
                              (ring + 1) * sides + index))
        faces.extend((tuple(range(sides - 1, -1, -1)),
                      tuple((len(rings) - 1) * sides + index
                            for index in range(sides))))
        if shape == "tip":
            # Nail stays inside this part's bounds and is integrated into the
            # same mesh, so the runtime still receives exactly 44 objects.
            offset = len(vertices)
            for along, width in ((-.18, .21), (.23, .18)):
                radius = next(
                    radius_a + (radius_b - radius_a) * (along - along_a) / (along_b - along_a)
                    for (along_a, radius_a), (along_b, radius_b) in zip(rings, rings[1:])
                    if along_a <= along <= along_b
                )
                for x in (-width, 0, width):
                    # Follow both dorsal octagon facets instead of floating a
                    # flat nail across the underlying finger's centre ridge.
                    depth = -radius + abs(x) * math.tan(math.pi / 8) - .003
                    vertices.append((x, depth, along))
            for indices in ((0, 1, 4, 3), (1, 2, 5, 4)):
                nail_faces.append(len(faces))
                faces.append(tuple(offset + index for index in indices))
    mesh = bpy.data.meshes.new(name + "_Construction")
    bm = bmesh.new()
    try:
        mesh.from_pydata(vertices, [], faces)
        bm.from_mesh(mesh)
        bm.faces.ensure_lookup_table()
        layer = bm.loops.layers.uv.new(uv_name)
        for index, face in enumerate(bm.faces):
            atlas = nail_sample if index in nail_faces and nail_sample else sample
            face.material_index = atlas["material_index"]
            face.smooth = False
            for loop in face.loops:
                loop[layer].uv = atlas["uv"]
        _normalize_canonical(bm)
        return _finish_mesh(bm, name, uv_name, material_count)
    finally:
        bm.free()
        bpy.data.meshes.remove(mesh)


def _validate_parts(objects):
    expected = set()
    for side in ("Left", "Right"):
        prefix = side + "_"
        expected.update(prefix + name for name in
                        ("forearm", "cuff", "palm", "thumb", "thumb_middle", "thumb_tip"))
        expected.update(prefix + "finger%d_%s" % (index, segment)
                        for index in range(4) for segment in _PHALANGES)
    actual = {obj.name for obj in objects}
    if len(objects) != 44 or actual != expected:
        raise ValueError("The FPS hand export does not contain all 44 exact part names.")
    for obj in objects:
        mesh = obj.data
        if not mesh.vertices or not mesh.polygons or not mesh.uv_layers.active:
            raise ValueError("Invalid FPS hand mesh: " + obj.name)
        if not mesh.materials or any(material is None for material in mesh.materials):
            raise ValueError("Unassigned FPS hand materials: " + obj.name)
        if any(not math.isfinite(value) for vertex in mesh.vertices for value in vertex.co):
            raise ValueError("Non-finite FPS hand vertices: " + obj.name)
        for axis in range(3):
            lower = min(vertex.co[axis] for vertex in mesh.vertices)
            upper = max(vertex.co[axis] for vertex in mesh.vertices)
            if abs(lower + .5) > 1e-4 or abs(upper - .5) > 1e-4:
                raise ValueError("Noncanonical FPS hand bounds: " + obj.name)


def export_hand_parts(body, spec, role, output_path):
    """Export 44 UV/material-complete meshes; return a JSON-serializable report.

    ``spec`` supplies positive-X shoulder, elbow and wrist coordinates in the
    same normalized world space as ``body``. No original objects, geometry,
    materials, UVs or source files are changed. Temporary export meshes are
    removed and the caller's object selection is restored, including on error.
    """
    if body is None or body.type != "MESH":
        raise ValueError("export_hand_parts requires a static source mesh body.")
    if bpy.context.mode != "OBJECT":
        raise ValueError("Export hands from Object Mode to leave the source untouched.")
    if not body.data.uv_layers:
        raise ValueError("The textured source body requires an atlas UV layer.")
    uv_name = (body.data.uv_layers.active or body.data.uv_layers[0]).name
    existing_materials = list(body.data.materials)
    valid_material = next((material for material in existing_materials if material), None)
    if valid_material is None:
        raise ValueError("The source body has no assigned atlas material.")
    materials = [material or valid_material for material in existing_materials]
    tree, samples = _source_samples(body, uv_name)
    shoulder, elbow, wrist = (Vector(spec[key]) for key in ("shoulder", "elbow", "wrist"))
    if wrist.x <= elbow.x or elbow.x <= shoulder.x:
        raise ValueError("Source hand slices require outward positive-X T-pose joints.")
    output_path = Path(output_path).expanduser().resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    selected = tuple(bpy.context.selected_objects)
    active = bpy.context.view_layer.objects.active
    collection = bpy.data.collections.new("DeadAirHandsExport_" + str(role))
    bpy.context.scene.collection.children.link(collection)
    objects = []
    part_reports = []

    def make_object(name, mesh, report, sample):
        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        obj.matrix_world = Matrix.Identity(4)
        for material in materials:
            mesh.materials.append(material)
        objects.append(obj)
        report.update({
            "name": name, "vertices": len(mesh.vertices),
            "triangles": len(mesh.polygons),
            "atlas_uv": list(sample["uv"]),
            "atlas_sample_distance_m": sample["distance_m"],
            "sample_material": materials[sample["material_index"]].name,
            "canonical_blender_bounds": [[-.5, -.5, -.5], [.5, .5, .5]],
        })
        part_reports.append(report)

    try:
        source_points = [body.matrix_world @ vertex.co for vertex in body.data.vertices]
        for side, label in ((1, "Left"), (-1, "Right")):
            prefix = label + "_"
            source_tip = max(side * point.x for point in source_points)
            available_hand = max(0, source_tip - wrist.x)
            arm_length = (wrist - elbow).length
            cuff_length = min(.03, max(.012, arm_length * .09))
            palm_length = min(max(.035, arm_length * .28), available_hand * .58)
            source_wrist = Vector((side * wrist.x, wrist.y, wrist.z))
            bands = {
                "forearm": (elbow.x + arm_length * .025, wrist.x - cuff_length * .55),
                "cuff": (wrist.x - cuff_length * .6, wrist.x + cuff_length * .4),
                "palm": (wrist.x + cuff_length * .15, wrist.x + palm_length),
            }
            for part, (lower, upper) in bands.items():
                sample_point = source_wrist.copy()
                sample_point.x = side * ((lower + upper) * .5)
                sample_point.y -= .01
                sample = _sample(tree, samples, sample_point, len(materials))
                try:
                    if upper <= lower + 1e-5:
                        raise ValueError("Insufficient source hand extent for this band.")
                    mesh, report = _source_fragment(
                        body, prefix + part, side, lower, upper,
                        uv_name, sample, len(materials),
                    )
                except (RuntimeError, ValueError) as error:
                    mesh = _canonical_mesh(prefix + part, "palm" if part == "palm" else "tube",
                                           uv_name, sample, len(materials))
                    report = {"method": "atlas_sampled_anatomy", "fallback_reason": str(error)}
                make_object(prefix + part, mesh, report, sample)

            # Generated segments articulate cleanly around every runtime grip.
            # Sample the actual distal source hand, varying samples by finger.
            finger_length = max(.045, min(.095, available_hand - max(palm_length, .025)))
            for index in range(4):
                for segment_index, segment in enumerate(_PHALANGES):
                    along = max(palm_length, .025) + finger_length * (.12 + segment_index * .23)
                    point = source_wrist + Vector((side * along, -.012, (index - 1.5) * .017))
                    sample = _sample(tree, samples, point, len(materials))
                    nail_point = point + Vector((side * .008, -.008, 0))
                    nail_sample = _sample(tree, samples, nail_point, len(materials))
                    name = prefix + "finger%d_%s" % (index, segment)
                    mesh = _canonical_mesh(name, "tip" if segment == "tip" else "tube",
                                           uv_name, sample, len(materials), nail_sample)
                    make_object(name, mesh, {"method": "atlas_sampled_anatomy"}, sample)
            for index, part in enumerate(("thumb", "thumb_middle", "thumb_tip")):
                point = source_wrist + Vector((side * (.035 + index * .019), -.015, -.035))
                sample = _sample(tree, samples, point, len(materials))
                nail_sample = _sample(tree, samples, point + Vector((0, -.01, 0)), len(materials))
                mesh = _canonical_mesh(prefix + part, "tip" if part == "thumb_tip" else "tube",
                                       uv_name, sample, len(materials), nail_sample)
                make_object(prefix + part, mesh, {"method": "atlas_sampled_anatomy"}, sample)

        _validate_parts(objects)
        # Blender enforces global object-name uniqueness. Existing imported
        # hands with these names must be unlinked by the caller first; silently
        # exporting suffixes such as Left_palm.001 would break the adapter.
        for obj in bpy.context.selected_objects:
            obj.select_set(False)
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        bpy.ops.export_scene.fbx(
            filepath=str(output_path), use_selection=True, object_types={"MESH"},
            axis_forward="-Z", axis_up="Y", global_scale=1,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            use_space_transform=True, bake_space_transform=True,
            bake_anim=False, add_leaf_bones=False, use_mesh_modifiers=True,
            mesh_smooth_type="FACE", path_mode="AUTO",
        )
        return {
            "role": str(role), "output_path": str(output_path),
            "source_mesh": body.name, "part_count": len(objects),
            "source_fragments": sum(report["method"] == "source_fragment" for report in part_reports),
            "generated_parts": sum(report["method"] == "atlas_sampled_anatomy" for report in part_reports),
            "axis_contract": "Blender X width, Z along, Y=-Unity depth; FBX Y up, -Z forward; baked mesh axes",
            "parts": part_reports,
        }
    finally:
        for obj in objects:
            mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if mesh.users == 0:
                bpy.data.meshes.remove(mesh)
        bpy.data.collections.remove(collection)
        for obj in bpy.context.selected_objects:
            obj.select_set(False)
        for obj in selected:
            if obj.name in bpy.context.view_layer.objects:
                obj.select_set(True)
        if active is not None and active.name in bpy.context.view_layer.objects:
            bpy.context.view_layer.objects.active = active
