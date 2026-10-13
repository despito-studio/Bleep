"""Conservative surface finishing for the restored, authored character meshes.

Refinement keeps silhouette cuts, lace, lashes, jewelry and UV boundaries fixed.
Hair lining is a separate, opt-in operation on an already separated Hair mesh;
it never thickens the complete character or doubles closed hair volumes.
"""
import json
import math

import bpy
import bmesh


def _components(bm):
    remaining = set(bm.verts)
    result = []
    while remaining:
        seed = next(iter(remaining))
        todo, vertices = [seed], set()
        while todo:
            vertex = todo.pop()
            if vertex not in remaining:
                continue
            remaining.remove(vertex)
            vertices.add(vertex)
            todo.extend(edge.other_vert(vertex) for edge in vertex.link_edges)
        result.append(vertices)
    return result


def _activate(obj):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def _restore_neckline_seam(bm, role, part_names):
    """Make Hazel's right neckline follow the intact left neckline arc.

    Work on the shared skin/cloth seam before either mesh is extracted. This
    removes the authored saw teeth without opening a gap between the two parts
    or changing the neck, face, sleeves or the rest of the torso.
    """
    if role != 'dispatcher':
        return dict(vertices=0, max_adjustment_m=0.0)
    layer = bm.faces.layers.int.get('authored_part_id')
    if not layer:
        return dict(vertices=0, max_adjustment_m=0.0)
    edges = [edge for edge in bm.edges if len(edge.link_faces) == 2
             and {part_names[face[layer]] for face in edge.link_faces} == {'body', 'cropped_top'}
             and all(1.49 < v.co.z < 1.65 and abs(v.co.x) < .17 and v.co.y < -.139
                     for v in edge.verts)]
    adjacent = {}
    for edge in edges:
        a, b = edge.verts
        adjacent.setdefault(a, []).append(b)
        adjacent.setdefault(b, []).append(a)
    ends = [v for v, neighbors in adjacent.items() if len(neighbors) == 1]
    if len(ends) != 2 or any(len(neighbors) > 2 for neighbors in adjacent.values()):
        raise ValueError('Hazel neckline is not a single shared front seam')
    path = [min(ends, key=lambda v: v.co.x)]
    while True:
        choices = [v for v in adjacent[path[-1]] if len(path) < 2 or v != path[-2]]
        if not choices:
            break
        path.append(choices[0])
    middle = min(range(len(path)), key=lambda i: path[i].co.y)
    reference = [v.co.copy() for v in reversed(path[:middle+1])]
    right = path[middle:]
    center_x = reference[0].x
    for point in reference:
        point.x = 2*center_x-point.x
    def distances(points):
        result = [0.0]
        for a, b in zip(points, points[1:]):
            result.append(result[-1]+(b-a).length)
        return result
    source = [v.co.copy() for v in right]
    original_arc, target_arc = distances(source), distances(reference)
    endpoint_delta = source[-1]-reference[-1]
    moved = []
    for vertex, original, length in zip(right, source, original_arc):
        t = length/original_arc[-1]
        target_distance = t*target_arc[-1]
        segment = next((i for i in range(len(target_arc)-1) if target_arc[i+1] >= target_distance), len(target_arc)-2)
        u = (target_distance-target_arc[segment])/(target_arc[segment+1]-target_arc[segment])
        vertex.co = reference[segment].lerp(reference[segment+1], u) + endpoint_delta*t
        moved.append((vertex.co-original).length)
    return dict(vertices=sum(value > 1e-7 for value in moved), max_adjustment_m=max(moved, default=0.0),
                method='right front seam follows mirrored intact left arc; skin/cloth vertices remain shared')


def _restore_ear_rim(bm, role):
    """Remove scan-like needle tips on Victor's left helix, in source metres.

    Only the tiny outer rim region can move, at most eight millimetres. The
    lobule, ear attachment, UVs and surrounding facial geometry are unchanged.
    """
    if role != 'engineer':
        return 0, 0.0, 0
    region = [v for v in bm.verts
              if -.125 < v.co.x < -.085 and -.078 < v.co.y < .005
              and 1.637 < v.co.z < 1.718]
    part_layer = bm.faces.layers.int.get('authored_part_id')
    needles = []
    for vertex in region:
        neighbors = [edge.other_vert(vertex) for edge in vertex.link_edges]
        same_part = not part_layer or len({face[part_layer] for face in vertex.link_faces}) == 1
        if (vertex.is_manifold and 3 <= len(neighbors) <= 4 and same_part
                and all(len(face.verts) == 3 for face in vertex.link_faces)
                and min(v.co.x for v in neighbors) - vertex.co.x > .001):
            needles.append(vertex)
    if needles:
        # Remove only closed, outward cone caps. Dissolving their centre leaves
        # the surrounding loop filled, replacing each needle with its rim face.
        bmesh.ops.dissolve_verts(bm, verts=needles, use_face_split=False, use_boundary_tear=False)
        region = [vertex for vertex in region if vertex.is_valid]
    originals = {v: v.co.copy() for v in region}
    def smoothstep(value):
        value = min(1.0, max(0.0, value))
        return value * value * (3 - 2 * value)
    for _ in range(10):
        positions = {}
        for vertex in region:
            neighbors = [edge.other_vert(vertex) for edge in vertex.link_edges]
            if not neighbors:
                continue
            mean = sum((v.co for v in neighbors), vertex.co * 0) / len(neighbors)
            original = originals[vertex]
            taper = (smoothstep((original.z - 1.637) / .013)
                     * smoothstep((1.718 - original.z) / .013)
                     * smoothstep((-.085 - original.x) / .014))
            # The damaged rim alternates in depth as well as width. Smooth its
            # entire local contour, with a fixed attachment/lobule transition.
            displacement = vertex.co.lerp(mean, .65 * taper) - original
            if displacement.length > .008:
                displacement = displacement.normalized() * .008
            positions[vertex] = original + displacement
        for vertex, co in positions.items():
            vertex.co = co
    distances = [(v.co - co).length for v, co in originals.items()]
    return sum(distance > 1e-7 for distance in distances), max(distances, default=0.0), len(needles)


def _protect_invalid_quad_joins(bm):
    """Do not turn overlapping/oppositely wound source triangles into bow ties."""
    count = 0
    for edge in bm.edges:
        if len(edge.link_faces) != 2 or any(len(f.verts) != 3 for f in edge.link_faces):
            continue
        first, second = edge.link_faces
        loop = next(loop for loop in first.loops if loop.edge == edge)
        other_loop = next(loop for loop in second.loops if loop.edge == edge)
        a, b = loop.vert, loop.link_loop_next.vert
        c = next(v for v in first.verts if v not in edge.verts)
        d = next(v for v in second.verts if v not in edge.verts)
        normal = first.normal + second.normal
        quad = [c, a, d, b]
        turns = [(quad[(i+1) % 4].co - quad[i].co).cross(
                  quad[(i+2) % 4].co - quad[(i+1) % 4].co).dot(normal)
                 for i in range(4)]
        if other_loop.vert != b or normal.length < .1 or min(turns) <= 1e-12:
            edge.seam = True
            count += 1
    return count


def refine_surface(obj, role=None):
    """Replace unrestricted smoothing with pinned boundaries and detail masks.

    Call before splitting the authored outfit. The FACE-domain authored_part_id
    attribute is retained; joins cannot cross its boundaries. Existing vertex
    groups, loop UVs and custom face data survive Blender's subdivision.
    """
    _activate(obj)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    before = len(bm.faces)
    neckline = _restore_neckline_seam(bm, role, json.loads(obj.get('authored_parts', '[]')))
    ear_vertices, ear_distance, ear_needles = _restore_ear_rim(bm, role)
    # Some authored hair locks weld their front, underside and scalp at one
    # non-manifold edge (three/four faces). Smoothing their shared normal makes
    # a black triangular dent at the bangs. Separate those coincident sheets
    # without moving the authored surface or rewriting any texture coordinates.
    multi_sheet_edges = [edge for edge in bm.edges if len(edge.link_faces) > 2]
    separated_edges = len(multi_sheet_edges)
    if multi_sheet_edges:
        bmesh.ops.split_edges(bm, edges=multi_sheet_edges)
        bm.verts.index_update()
    bm.normal_update()
    invalid_joins = _protect_invalid_quad_joins(bm)
    protected = set()
    part_layer = bm.faces.layers.int.get('authored_part_id')
    for edge in bm.edges:
        part_boundary = (part_layer is not None and len(edge.link_faces) == 2
                         and edge.link_faces[0][part_layer] != edge.link_faces[1][part_layer])
        if part_boundary:
            edge.seam = True
        if not edge.is_manifold or part_boundary:
            protected.update(edge.verts)
        elif edge.calc_face_angle() > math.radians(62):
            edge.smooth = False
            protected.update(edge.verts)
    # Dense necklace links can have hundreds of vertices, whereas eyelashes
    # can be a handful. Protect narrow details by size as well as part count.
    for vertices in _components(bm):
        extent = [max(v.co[a] for v in vertices) - min(v.co[a] for v in vertices)
                  for a in range(3)]
        if len(vertices) < 96 or min(extent) < .004:
            protected.update(vertices)
    for vertex in bm.verts:
        if vertex.link_edges and min(edge.calc_length() for edge in vertex.link_edges) < .002:
            protected.add(vertex)
    protected_indices = [v.index for v in protected]
    # Explicitly pin one source-vertex ring around boundaries. A disconnected
    # hair corner otherwise moves independently and reveals a triangular hole.
    transition = {edge.other_vert(v) for v in protected for edge in v.link_edges} - protected
    transition_indices = [v.index for v in transition]
    bmesh.ops.join_triangles(
        bm, faces=list(bm.faces), angle_face_threshold=.52, angle_shape_threshold=.70,
        cmp_seam=True, cmp_sharp=True, cmp_uvs=True, cmp_vcols=True, cmp_materials=True)
    quads = sum(len(face.verts) == 4 for face in bm.faces)
    bm.to_mesh(obj.data)
    bm.free()

    mask = obj.vertex_groups.new(name='_source_surface_smoothing')
    # Modifier application can invalidate the RNA wrapper. Keep its name as a
    # Python string; reading mask.name afterward can refer to another group.
    mask_name = mask.name
    mask.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    if protected_indices:
        mask.add(protected_indices, 0.0, 'REPLACE')
    if transition_indices:
        mask.add(transition_indices, .25, 'REPLACE')
    sub = obj.modifiers.new('Authored surface support', 'SUBSURF')
    sub.subdivision_type = 'SIMPLE'
    sub.levels = sub.render_levels = 1
    sub.uv_smooth = 'PRESERVE_BOUNDARIES'
    bpy.ops.object.modifier_apply(modifier=sub.name)
    smoothed = obj.modifiers.new('Detail-preserving surface finish', 'SMOOTH')
    smoothed.factor = .18
    smoothed.iterations = 2
    smoothed.vertex_group = mask_name
    bpy.ops.object.modifier_apply(modifier=smoothed.name)
    obj.vertex_groups.remove(obj.vertex_groups[mask_name])
    # A few source hair spike triangles are nearly collinear. Subdivision can
    # collapse their centre to an existing point; discard only zero-area faces
    # (below a 0.01 mm square), never a visible garment or facial surface.
    cleanup = bmesh.new()
    cleanup.from_mesh(obj.data)
    collapsed = [face for face in cleanup.faces if face.calc_area() < 1e-10]
    removed_collapsed_faces = len(collapsed)
    if collapsed:
        bmesh.ops.delete(cleanup, geom=collapsed, context='FACES_ONLY')
        wire_edges = [edge for edge in cleanup.edges if not edge.link_faces]
        if wire_edges:
            bmesh.ops.delete(cleanup, geom=wire_edges, context='EDGES')
        isolated = [vertex for vertex in cleanup.verts if not vertex.link_edges]
        if isolated:
            bmesh.ops.delete(cleanup, geom=isolated, context='VERTS')
        cleanup.to_mesh(obj.data)
    cleanup.free()
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    obj.data.set_sharp_from_angle(angle=math.radians(68))
    obj.data.update()
    result = dict(
        recovered_quad_regions=quads, source_faces=before,
        separated_nonmanifold_sheet_edges=separated_edges,
        overlapping_triangle_joins_prevented=invalid_joins,
        removed_collapsed_support_faces=removed_collapsed_faces,
        local_ear_vertices_rounded=ear_vertices, max_ear_adjustment_m=ear_distance,
        removed_ear_needle_caps=ear_needles,
        neckline_seam=neckline,
        protected_source_vertices=len(protected_indices),
        transition_source_vertices=len(transition_indices),
        support_subdivision='one simple subdivision; authored UV interpolation',
        surface_finish='two 0.18 passes; open boundaries, part seams, thin details and sharp corners pinned')
    obj['authored_surface_finish'] = json.dumps(result)
    return result


def finish_hair_shell(obj, role=None, thickness=.0015):
    """Give open hair patches an inward lining and a modeled edge thickness.

    Use ONLY on a separated Hair mesh. Closed components are untouched. Outer
    positions/UVs and original material assignments stay exact. Inner vertices
    copy every deform weight from their paired outer vertex; rim UVs remain in
    the adjacent authored hair island instead of sampling a generic color.
    """
    part = str(obj.get('authored_part', '')).lower()
    if part != 'hair':
        return dict(skipped='not a separated Hair mesh')
    if obj.get('authored_hair_lining'):
        return json.loads(obj['authored_hair_lining'])
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    # Part extraction can expose branching hair seams that were shared with a
    # different material in the combined source. Line each sheet independently.
    branching = [edge for edge in bm.edges if len(edge.link_faces) > 2]
    if branching:
        bmesh.ops.split_edges(bm, edges=branching)
    # Two otherwise valid open fans can touch at one vertex. A shared inward
    # duplicate would make four rim faces meet along one vertical edge. Split
    # those fans at their boundary before creating the lining.
    pinched_vertices = [vertex for vertex in bm.verts
                        if sum(edge.is_boundary for edge in vertex.link_edges) > 2]
    separated_fans = 0
    for vertex in pinched_vertices:
        boundary_edges = [edge for edge in vertex.link_edges if edge.is_boundary]
        separated_fans += len(bmesh.utils.vert_separate(vertex, boundary_edges)) - 1
    bm.normal_update()
    components = _components(bm)
    open_components = [vertices for vertices in components
                       if any(edge.is_boundary for v in vertices for edge in v.link_edges)]
    added_inner = added_rims = 0
    uv_layers = list(bm.loops.layers.uv.values())
    deform = bm.verts.layers.deform.active
    vertex_layers = list(bm.verts.layers.float.values()) + list(bm.verts.layers.int.values())
    face_layers = list(bm.faces.layers.float.values()) + list(bm.faces.layers.int.values())
    color_layers = list(bm.loops.layers.color.values()) + list(bm.loops.layers.float_color.values())
    def copy_loop(target, source):
        for layer in uv_layers:
            target[layer].uv = source[layer].uv
            target[layer].pin_uv = source[layer].pin_uv
        for layer in color_layers:
            target[layer] = source[layer]
    def copy_face(target, source):
        target.material_index = source.material_index
        target.smooth = source.smooth
        for layer in face_layers:
            target[layer] = source[layer]
    for vertices in open_components:
        faces = {face for v in vertices for face in v.link_faces}
        boundary = {edge for v in vertices for edge in v.link_edges if edge.is_boundary}
        pairs = {}
        for vertex in vertices:
            local_width = min((edge.calc_length() for edge in vertex.link_edges), default=.01)
            amount = min(thickness, max(.0001, local_width * .15))
            inner = bm.verts.new(vertex.co - vertex.normal * amount)
            if deform:
                for group, weight in vertex[deform].items():
                    inner[deform][group] = weight
            for layer in vertex_layers:
                inner[layer] = vertex[layer]
            pairs[vertex] = inner
        for face in faces:
            original_loops = list(face.loops)
            reverse = list(reversed(original_loops))
            inner = bm.faces.new([pairs[loop.vert] for loop in reverse])
            copy_face(inner, face)
            for target_loop, original_loop in zip(inner.loops, reverse):
                copy_loop(target_loop, original_loop)
            added_inner += 1
        for edge in boundary:
            source_face = edge.link_faces[0]
            source_loop = next(loop for loop in source_face.loops if loop.edge == edge)
            a, b = source_loop.vert, source_loop.link_loop_next.vert
            rim = bm.faces.new([b, a, pairs[a], pairs[b]])
            copy_face(rim, source_face)
            for target, source in zip(rim.loops,
                                      [source_loop.link_loop_next, source_loop,
                                       source_loop, source_loop.link_loop_next]):
                copy_loop(target, source)
            for layer in uv_layers:
                center = sum((loop[layer].uv for loop in source_face.loops),
                             source_loop[layer].uv * 0) / len(source_face.loops)
                for loop in list(rim.loops)[2:]:
                    loop[layer].uv += (center - loop[layer].uv) * .025
            rim.smooth = True
            edge.smooth = False
            added_rims += 1
    bm.normal_update()
    boundary_after = sum(edge.is_boundary for edge in bm.edges)
    nonmanifold_after = sum(not edge.is_manifold for edge in bm.edges)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    result = dict(open_hair_components=len(open_components), closed_components_preserved=len(components)-len(open_components),
                  inner_faces=added_inner, rim_faces=added_rims, thickness_m=thickness,
                  separated_hair_sheet_edges=len(branching),
                  separated_pinched_hair_fans=separated_fans,
                  boundary_edges_after=boundary_after, nonmanifold_edges_after=nonmanifold_after)
    obj['authored_hair_lining'] = json.dumps(result)
    return result
