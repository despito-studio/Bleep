"""Local skin continuity repairs for Vivian's restored static model.

Replace the detached thigh-shaped patches with continuous fitted skin, and
round only the skin tips under her sleeve caps. The original face, torso,
costume, atlas charts and vertices outside those regions are preserved.
"""
import json
import math
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree


def thigh_profile(z, side=1):
    """Shared leg/stocking fitting profile: center x/y and ellipse radii, metres."""
    samples = [
        (.855, .0844, .0104, .0715, .0775),
        (.870, .0844, .0104, .0735, .0800),
        (.920, .0840, .0110, .0770, .0860),
        (.980, .0840, .0200, .0790, .0960),
        # Above the visible thigh, the skin returns underneath the fitted
        # skirt. Keep these hidden attachment rings inside its tapered volume.
        (1.040, .0780, .0200, .0660, .0800),
        (1.100, .0710, .0180, .0600, .0700),
        (1.135, .0680, .0150, .0550, .0650),
    ]
    low, high = samples[0], samples[-1]
    for a, b in zip(samples, samples[1:]):
        if a[0] <= z <= b[0]:
            low, high = a, b
            break
    t = max(0.0, min(1.0, (z-low[0])/(high[0]-low[0])))
    values = [a+(b-a)*t for a, b in zip(low[1:], high[1:])]
    return (values[0]*(1 if side > 0 else -1), *values[1:])


def _thigh_region(co):
    return abs(co.x) < .215 and .70 < co.z < 1.135


def _shoulder_region(co):
    return .21 < abs(co.x) < .29 and 1.435 < co.z < 1.51 and -.04 < co.y < .085


def _skin_tile(obj, source_uvs):
    """Use unused space in the existing Body atlas, with no extra material."""
    image = next(node.image for node in obj.data.materials[0].node_tree.nodes
                 if node.type == 'TEX_IMAGE' and node.image)
    width, height = image.size
    pixels = np.empty(width*height*4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(height, width, 4)
    occupied = np.any(pixels[:, :, :3] != 0, axis=2)
    uv = obj.data.uv_layers.active
    for face in obj.data.polygons:
        points = np.asarray([uv.data[i].uv[:] for i in face.loop_indices])
        low = np.floor(points.min(axis=0)*(width, height)).astype(int)-5
        high = np.ceil(points.max(axis=0)*(width, height)).astype(int)+6
        x0, y0 = np.maximum(low, (0, 0)); x1, y1 = np.minimum(high, (width, height))
        occupied[y0:y1, x0:x1] = True
    integral = np.pad(occupied.astype(np.int32), ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    slot = None
    for size in (64, 32, 16):
        for y in range(height-size, -1, -4):
            for x in range(width-size, -1, -4):
                total = integral[y+size, x+size]-integral[y, x+size]-integral[y+size, x]+integral[y, x]
                if not total:
                    slot = (x, y, size)
                    break
            if slot:
                break
        if slot:
            break
    if not slot:
        raise ValueError('Body atlas has no unoccupied patch for repaired thigh UVs')
    samples = []
    for u, v in source_uvs:
        rgb = pixels[min(height-1, max(0, round(v*(height-1)))), min(width-1, max(0, round(u*(width-1)))), :3]
        r, g, b = rgb
        if .35 < g < .9 and r > g*1.07 and r < g*1.8 and b > g*.66:
            samples.append(rgb)
    if not samples:
        raise ValueError('No exposed authored skin samples found for thigh texture')
    color = np.median(samples, axis=0)
    x, y, size = slot
    # A small smooth tonal transition avoids reusing the old painted creases
    # that made skin look like separate folded panels. Lighting supplies form.
    u = np.linspace(0, 1, size)[None, :]
    v = np.linspace(0, 1, size)[:, None]
    tone = 1.0 + .025*(v-.5) + .008*np.cos(u*math.tau)
    pixels[y:y+size, x:x+size, :3] = np.clip(color[None, None, :]*tone[:, :, None], 0, 1)
    pixels[y:y+size, x:x+size, 3] = 1
    image.pixels.foreach_set(pixels.ravel())
    image.save()
    # Repacking an already packed image without explicit data can retain its
    # old embedded PNG even though the live pixels and external PNG changed.
    # Embed the exact newly saved bytes so save/reopen uses the repaired skin.
    encoded = Path(bpy.path.abspath(image.filepath_raw)).read_bytes()
    image.pack(data=encoded, data_len=len(encoded))
    return (x+2, y+2, size-4, width, height), dict(atlas=image.filepath, atlas_size=[width, height],
        unused_patch_pixels=[x, y, size, size], sampled_skin_rgb=[float(c) for c in color], extra_materials=0)


def finish_vivian_body(parts, arm):
    """Repair Body only, before the separate legwear and bodice finishers."""
    body = next(obj for obj in parts if obj.get('authored_part') == 'body')
    if body.get('vivian_body_finish'):
        return json.loads(body['vivian_body_finish'])
    original_mesh = body.data
    original_points = [vertex.co.copy() for vertex in original_mesh.vertices]
    source_uvs = [original_mesh.uv_layers.active.data[loop.index].uv.copy()
                  for loop in original_mesh.loops if _thigh_region(original_points[loop.vertex_index])]
    tile, texture_report = _skin_tile(body, source_uvs)

    # Nearest authored skin/leg points transfer the same normalized rig weights.
    reference = []
    for obj in parts:
        if obj.get('authored_part') not in {'body', 'stockings'}:
            continue
        names = {group.index: group.name for group in obj.vertex_groups}
        for vertex in obj.data.vertices:
            if abs(vertex.co.x) < .22 and .70 < vertex.co.z < 1.20:
                reference.append((vertex.co.copy(), {names[g.group]: g.weight for g in vertex.groups
                                                    if names[g.group] in arm.data.bones}))
    tree = KDTree(len(reference))
    for i, (point, _) in enumerate(reference):
        tree.insert(point, i)
    tree.balance()
    bm = bmesh.new(); bm.from_mesh(original_mesh); bm.verts.ensure_lookup_table()
    initial = {vertex: vertex.co.copy() for vertex in bm.verts}
    old_thigh_vertices = [v for v in bm.verts if _thigh_region(v.co)]
    removed_faces = len({face for vertex in old_thigh_vertices for face in vertex.link_faces})
    bmesh.ops.delete(bm, geom=old_thigh_vertices, context='VERTS')

    # The little pale triangles above the arms are source skin tips left outside
    # the now-rounded cap. Fit them to the existing upper-arm axis locally.
    moved_shoulders = []
    for vertex in list(bm.verts):
        if not _shoulder_region(vertex.co):
            continue
        side = 'L' if vertex.co.x > 0 else 'R'
        bone = arm.data.bones['upper_arm.'+side]
        axis = (bone.tail_local-bone.head_local).normalized()
        offset = vertex.co-bone.head_local
        along = offset.dot(axis)
        center = bone.head_local+axis*along
        radial = vertex.co-center
        radius = .026
        if radial.length > radius:
            original = vertex.co.copy()
            vertex.co = center+radial.normalized()*radius
            moved_shoulders.append((vertex.co-original).length)
    bm.normal_update()
    deform = bm.verts.layers.deform.verify()
    uv_layers = list(bm.loops.layers.uv.values())
    garment_uv = bm.loops.layers.uv.get('GarmentUV') or bm.loops.layers.uv.active
    group_indices = {group.name: group.index for group in body.vertex_groups}
    part_layer = bm.faces.layers.int.get('authored_part_id')
    x, y, size, width, height = tile
    segments = 32
    heights = [ .855+i*(1.135-.855)/28 for i in range(29) ]
    faces_created = []
    for side in (-1, 1):
        rings = []
        for z in heights:
            cx, cy, rx, ry = thigh_profile(z, side)
            ring = []
            for i in range(segments):
                angle = i*math.tau/segments
                point = Vector((cx+rx*math.cos(angle), cy+ry*math.sin(angle), z))
                vertex = bm.verts.new(point)
                _, index, _ = tree.find(point)
                weights = sorted(reference[index][1].items(), key=lambda value: -value[1])[:4]
                total = sum(weight for _, weight in weights)
                for name, weight in weights:
                    vertex[deform][group_indices[name]] = weight/total
                ring.append(vertex)
            rings.append(ring)
        for row in range(len(rings)-1):
            for i in range(segments):
                face = bm.faces.new((rings[row][i], rings[row][(i+1)%segments],
                                     rings[row+1][(i+1)%segments], rings[row+1][i]))
                face.smooth = True
                if part_layer:
                    face[part_layer] = 0
                for loop, u, v in zip(face.loops, (i/segments, (i+1)/segments, (i+1)/segments, i/segments),
                                      (row/28, row/28, (row+1)/28, (row+1)/28)):
                    for layer in uv_layers:
                        loop[layer].uv = ((x+size*u)/width, (y+size*v)/height)
                faces_created.append(face)
        for ring, reverse in ((rings[0], True), (rings[-1], False)):
            face = bm.faces.new(tuple(reversed(ring)) if reverse else ring)
            face.smooth = False
            for loop in face.loops:
                for layer in uv_layers:
                    loop[layer].uv = ((x+size*.5)/width, (y+size*.5)/height)
            faces_created.append(face)
    bmesh.ops.recalc_face_normals(bm, faces=faces_created)
    outside_movement = max(((vertex.co-point).length for vertex, point in initial.items()
                            if vertex.is_valid and not _thigh_region(point) and not _shoulder_region(point)), default=0)
    degenerate = sum(face.calc_area() < 1e-10 for face in bm.faces)
    if degenerate:
        raise ValueError('Body continuity repair produced degenerate faces: '+str(degenerate))
    bm.to_mesh(original_mesh); bm.free(); original_mesh.update()
    report = dict(removed_thigh_vertices=len(old_thigh_vertices), removed_thigh_faces=removed_faces,
        new_thigh_faces=len(faces_created), cross_sections_per_leg=len(heights), segments_per_section=segments,
        thigh_range_m=[heights[0], heights[-1]], shoulder_vertices_adjusted=len(moved_shoulders),
        max_shoulder_adjustment_m=max(moved_shoulders, default=0),
        outside_repair_regions_movement_m=outside_movement, texture=texture_report)
    body['vivian_body_finish'] = json.dumps(report)
    return report
