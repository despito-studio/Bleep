"""Editable clothing pieces cut from the authored character surfaces.

Call ``tag_source_parts`` in the recovered source T pose, before refinement.
Call ``modularize`` after refinement.  The source UV charts and their painted
details are retained, packed into independent garment textures, and recorded in
SourceAtlasUV.  This module neither builds a replacement primitive character nor
changes the armature or animation.
"""
from collections import Counter, defaultdict
from pathlib import Path
import json
import math

import bpy
import bmesh
import numpy as np

PART_ATTRIBUTE = 'authored_part_id'
PARTS = {
    'operator': ('body', 'hair', 'dress', 'bra', 'stockings', 'shoes', 'garters', 'jewelry'),
    'engineer': ('body', 'hair', 'shirt', 'jacket', 'trousers', 'tie', 'shoes'),
    'journalist': ('body', 'hair', 'beard', 'jacket', 'shirt', 'tie', 'trousers', 'cap', 'badge', 'shoes', 'equipment'),
    'dispatcher': ('body', 'hair', 'cropped_top', 'sleeves', 'wrap_skirt', 'shoes', 'jewelry'),
}
NAMES = {'operator': 'Vivian', 'engineer': 'Victor', 'journalist': 'Edgar', 'dispatcher': 'Hazel'}
FABRIC = {'dress', 'bra', 'stockings', 'garters', 'shirt', 'jacket', 'trousers', 'tie', 'cropped_top', 'sleeves', 'wrap_skirt', 'cap'}


def _pixels(image):
    width, height = image.size
    pixels = np.empty(width*height*4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    return pixels.reshape(height, width, 4)


def _components(mesh):
    links = [set() for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        links[a].add(b); links[b].add(a)
    found = set(); ids = [0] * len(links); groups = {}
    for index in range(len(links)):
        if index in found:
            continue
        todo = [index]; part = []
        while todo:
            current = todo.pop()
            if current in found:
                continue
            found.add(current); part.append(current); ids[current] = index
            todo.extend(links[current] - found)
        groups[index] = part
    return ids, groups


def _face_colors(obj, image):
    """Sample inside each triangle, avoiding neighboring UV-island gutters."""
    width, height = image.size
    pixels = _pixels(image)
    uv = obj.data.uv_layers.active.data
    result = []
    for face in obj.data.polygons:
        coordinates = np.asarray([uv[i].uv[:] for i in face.loop_indices])
        center = coordinates.mean(axis=0)
        samples = np.concatenate((center[None, :], coordinates * .35 + center[None, :] * .65))
        xs = np.clip(np.rint(samples[:, 0] * (width - 1)), 0, width - 1).astype(int)
        ys = np.clip(np.rint(samples[:, 1] * (height - 1)), 0, height - 1).astype(int)
        result.append(np.median(pixels[ys, xs, :3], axis=0))
    return result


def _classify(role, center, rgb, component):
    x, y, z = center; r, g, b = rgb
    gray = (r + g + b) / 3
    dark = max(rgb) < .28
    if role == 'operator':
        if component in {1257, 2281}:
            return 'jewelry'
        if component in {1919, 2001, 2115, 2196}:
            return 'body'  # ear geometry is pale skin, not silver hair
        # The original bob has brown painted underside faces connected to its
        # white exterior. Their position, not their skin-like color, identifies
        # them as hair; fringe above the eyes also belongs to the same piece.
        hair_shell = z > 1.56 and (z > 1.745 or (y > .012 and z > 1.60) or (abs(x) > .085 and z > 1.575))
        pale_hair = z > 1.49 and gray > .47 and max(rgb)-min(rgb) < .19 and (abs(x) > .065 or y > .004 or z > 1.69)
        if hair_shell or pale_hair:
            return 'hair'
        if z < .175:
            return 'shoes'
        if .175 <= z < .81 and dark:
            return 'stockings'
        if .72 < z < .97 and dark:
            return 'garters'
        black_lace = dark and r < g*1.85 and r < b*1.8
        shoulder_strap = 1.405 < z < 1.535 and .073 < abs(x) < .143 and max(rgb) < .43 and y < .02
        if (1.23 < z < 1.515 and abs(x) < .20 and black_lace) or shoulder_strap:
            return 'bra'
        if z < 1.53 and r > g * 1.45 and r > b * 1.10 and g < .26:
            return 'dress'
        return 'body'
    if role == 'engineer':
        if component == 810:
            return 'tie'
        if component in {0, 116} or z < .155:
            return 'shoes'
        if z < .86:
            return 'trousers'
        # Victor's raised coat collar reaches above the neck. Keep those
        # surfaces with the coat instead of extending the head's height test
        # across his broad shoulders and behind the scalp.
        if z > 1.56 and (abs(x) > .14 or y > .14):
            return 'jacket'
        # The narrow inside return of that collar runs behind each ear, much
        # closer to the scalp. Its neutral charcoal distinguishes it from the
        # warm brown hair and exposed skin in the same height band.
        inner_collar = (1.56 < z < 1.71 and max(rgb) < .40 and max(rgb)-min(rgb) < .085
                        and (y > .085 or (abs(x) > .075 and y > .005)))
        if inner_collar:
            return 'jacket'
        if z > 1.65 and gray < .27 and (y > -.055 or z > 1.748):
            return 'hair'
        if component in {217, 507, 1348, 1420} or z > 1.568:
            return 'body'
        if max(rgb) < .37:
            return 'jacket'
        if r > g * 1.16 and g > b * 1.07 and max(rgb)-min(rgb) > .14:
            return 'body'
        return 'shirt'
    if role == 'journalist':
        if component == 641:
            return 'cap'
        if component == 1033:
            return 'badge'
        if component in {1056, 1094, 1124, 1141, 1211, 1276, 1306, 1344}:
            return 'equipment'
        if component in {0, 422}:
            return 'shoes' if z < .165 else 'trousers'
        if z < .165:
            return 'shoes'
        if z > 1.75 and gray < .20:
            return 'hair'
        if r > g * 1.13 and g > b * 1.3 and r > .15:
            return 'body'
        if z > 1.60:
            return 'body'
        if 1.18 < z < 1.605 and y < -.22 and gray > .29 and max(rgb)-min(rgb) < .24:
            return 'beard' if abs(x-.06) > .032 or z > 1.43 else 'shirt'
        if 1.07 < z < 1.44 and y < -.24 and abs(x-.06) < .062 and dark:
            return 'tie'
        if r > .25 and g > .16 and b < g * .67:
            return 'badge'
        return 'jacket'
    if role == 'dispatcher':
        if component in {1475, 1529, 3609, 3637, 811, 820}:
            return 'jewelry'
        if component in {0, 17, 93, 201, 2424, 2442, 2510, 2685}:
            return 'shoes'
        if component in {418, 739}:
            return 'wrap_skirt'
        if component in {522, 4695}:
            # The imported topology welds each bell sleeve to its bare hand.
            # Keep the gold skin in Body; the pink cuff remains its own garment.
            exposed_hand = g > b*1.6 and r < g*3.0
            return 'body' if exposed_hand else 'sleeves'
        if z > .87 and r < .40 and g < .20 and b < .16 and (y > -.11 or abs(x) > .12 or z > 1.87):
            return 'hair'
        red_cloth = r > .25 and r > g * 2.0 and b > g * .80
        if red_cloth and z < 1.74:
            if z < .4:
                return 'shoes'  # ankle straps belong to footwear, never skirt
            if abs(x) > .40 and z > 1.28:
                return 'sleeves'  # bell fabric beyond the source elbow
            return 'wrap_skirt' if z < 1.33 else 'cropped_top'
        return 'body'
    raise ValueError('Unknown authored role: ' + role)


def tag_source_parts(obj, role, image):
    """Tag material boundaries before topology refinement, without changing UVs.

    Inputs must still use the exact imported source vertex order and recovered
    T-pose coordinates. Component IDs are derived from the immutable source.
    """
    mesh = obj.data
    if not mesh.uv_layers.active:
        raise ValueError(obj.name + ' has no authored UV coordinates')
    component_ids, components = _components(mesh)
    colors = _face_colors(obj, image)
    labels = []
    for face, color in zip(mesh.polygons, colors):
        center = sum((mesh.vertices[i].co for i in face.vertices), mesh.vertices[face.vertices[0]].co * 0) / len(face.vertices)
        labels.append(_classify(role, center, color, component_ids[face.vertices[0]]))
    # Eliminate a lone misclassified texture sample inside a coherent garment.
    # Real narrow straps and original separate components are not affected.
    edges = defaultdict(list)
    for face in mesh.polygons:
        for edge in face.edge_keys:
            edges[tuple(sorted(edge))].append(face.index)
    original = labels[:]
    neighbors = defaultdict(set)
    for faces in edges.values():
        for a in faces:
            neighbors[a].update(b for b in faces if b != a)
    for i, links in neighbors.items():
        votes = Counter(original[j] for j in links)
        if len(links) >= 3 and len(votes) == 1 and votes.most_common(1)[0][1] == len(links):
            candidate = next(iter(votes))
            if original[i] not in {'bra', 'garters', 'jewelry', 'badge', 'tie'}:
                labels[i] = candidate
    if role == 'operator':
        # Texture-baked pale edges split a few original lingerie straps into
        # burgundy islands. Their geometric location identifies the actual item:
        # upper central remnants belong to the rebuilt bra; lower detached bands
        # are garters. Keep the continuous main dress and its asymmetrical hem.
        remaining = {i for i, label in enumerate(labels) if label == 'dress'}
        islands = []
        while remaining:
            todo = [min(remaining)]; island = set()
            while todo:
                current = todo.pop()
                if current not in remaining:
                    continue
                remaining.remove(current); island.add(current)
                todo.extend(j for j in neighbors[current] if j in remaining and labels[j] == 'dress')
            islands.append(island)
        if islands:
            largest = max(islands, key=lambda ids: sum(mesh.polygons[i].area for i in ids))
            for island in islands:
                if island is largest:
                    continue
                points = [mesh.vertices[v].co for i in island for v in mesh.polygons[i].vertices]
                zmin = min(p.z for p in points); zmax = max(p.z for p in points)
                xmin = min(p.x for p in points); xmax = max(p.x for p in points)
                label = 'garters' if zmax < 1.09 else 'bra' if zmin > 1.30 and zmax < 1.55 and xmin > -.15 and xmax < .15 else None
                if label:
                    for i in island:
                        labels[i] = label
    attribute = mesh.attributes.get(PART_ATTRIBUTE) or mesh.attributes.new(PART_ATTRIBUTE, 'INT', 'FACE')
    names = list(PARTS[role])
    for value, label in zip(attribute.data, labels):
        value.value = names.index(label)
    mesh_edge_map = {tuple(sorted(e.vertices)): e for e in mesh.edges}
    boundaries = 0
    for key, faces in edges.items():
        if len({labels[i] for i in faces}) > 1:
            edge = mesh_edge_map[key]
            edge.use_seam = True; edge.use_edge_sharp = True; boundaries += 1
    obj['authored_parts'] = json.dumps(names)
    obj['authored_role'] = role
    obj['authored_source_image'] = image.name
    obj['authored_source_path'] = image.filepath
    mesh.update()
    return {'source_faces': dict(Counter(labels)), 'protected_part_boundary_edges': boundaries}


def _uv_islands(mesh, layer):
    """Connected charts, treating a vertex split in UV as a chart boundary."""
    edges = defaultdict(list)
    for face in mesh.polygons:
        loops = list(face.loop_indices)
        for i, loop in enumerate(loops):
            nxt = loops[(i+1) % len(loops)]
            a = mesh.loops[loop].vertex_index; b = mesh.loops[nxt].vertex_index
            uva = tuple(round(float(v), 6) for v in layer.data[loop].uv)
            uvb = tuple(round(float(v), 6) for v in layer.data[nxt].uv)
            key = ((a, uva), (b, uvb)) if a < b else ((b, uvb), (a, uva))
            edges[key].append(face.index)
    adjacent = [set() for _ in mesh.polygons]
    for links in edges.values():
        for index in links:
            adjacent[index].update(j for j in links if j != index)
    seen = set(); islands = []
    for index in range(len(adjacent)):
        if index in seen:
            continue
        todo = [index]; faces = []
        while todo:
            current = todo.pop()
            if current in seen:
                continue
            seen.add(current); faces.append(current); todo.extend(adjacent[current]-seen)
        islands.append(faces)
    return islands


def _pow2(value):
    return 2 ** math.ceil(math.log2(max(32, value)))


def _pack_rectangles(rectangles, max_width=2048):
    """Deterministic shelf packing. Rectangles are not stretched or rotated."""
    order = sorted(range(len(rectangles)), key=lambda i: (-rectangles[i][1], -rectangles[i][0], i))
    min_width = _pow2(max(w for w, h in rectangles))
    choices = []
    for width in [2 ** p for p in range(5, 14) if min_width <= 2 ** p <= max(max_width, min_width)]:
        shelves = []; placements = {}; total_height = 0
        for i in order:
            w, h = rectangles[i]; suitable = [(s[1]-h, j) for j, s in enumerate(shelves) if s[0]+w <= width and s[1] >= h]
            if suitable:
                _, j = min(suitable); shelf = shelves[j]
                placements[i] = (shelf[0], shelf[2]); shelf[0] += w
            else:
                placements[i] = (0, total_height); shelves.append([w, h, total_height]); total_height += h
        height = _pow2(total_height)
        choices.append((width * height, max(width, height), width, height, placements))
    _, _, width, height, placements = min(choices, key=lambda x: (x[0], x[1]))
    return width, height, placements


def _charts(obj, source_image, density=1.0, gutter=4):
    width, height = source_image.size; uv = obj.data.uv_layers.active
    charts = []
    for faces in _uv_islands(obj.data, uv):
        loops = [i for f in faces for i in obj.data.polygons[f].loop_indices]
        points = np.asarray([uv.data[i].uv[:] for i in loops])
        lo = np.floor(points.min(axis=0) * (width, height)).astype(int) - 2
        hi = np.ceil(points.max(axis=0) * (width, height)).astype(int) + 2
        lo = np.maximum(lo, (0, 0)); hi = np.minimum(hi, (width, height))
        size = np.maximum(hi-lo, (1, 1)); output = np.maximum(np.ceil(size*density).astype(int), (2, 2))
        charts.append({'faces': faces, 'loops': loops, 'lo': lo, 'size': size, 'output': output, 'rect': tuple(output+2*gutter)})
    return charts


def _scaled_charts(charts, density, gutter=4):
    result = []
    for chart in charts:
        output = np.maximum(np.ceil(chart['size']*density).astype(int), (2, 2))
        result.append(dict(chart, output=output, rect=tuple(output+2*gutter)))
    return result


def _clean_garment_tile(tile, role, label):
    """Remove baked skin leakage while keeping the garment's authored shading.

    Cloth pixels propagate into foreign skin-colored pixels from the closest
    clean neighborhood. This affects the independent garment texture only;
    source atlases, body, face, and Hazel's intentional gold skirt bands survive.
    """
    rgb = tile[:, :, :3]; r, g, b = [rgb[:, :, i] for i in range(3)]
    if role == 'operator' and label == 'dress':
        contamination = ((r > .38) & (g > .24) & (b > .22) & (r < g*2.35)) | ((g > .35) & (b > .32))
        donors = (r > g*1.65) & (r > b*.95) & (r > .11) & ~contamination
        fallback = np.array((.34, .073, .132), dtype=np.float32)
    elif role == 'dispatcher' and label in {'cropped_top', 'sleeves'}:
        contamination = (g > b*1.6) & (r < g*3.0) & (g > .11)
        donors = (r > g*2.8) & (b > g*1.0) & (r > .25) & ~contamination
        fallback = np.array((.68, .082, .235), dtype=np.float32)
    elif role == 'engineer' and label == 'jacket':
        # The jacket is charcoal; the actual white shirt is a different object.
        # White/skin triangles on its collar were baked from nearby source parts.
        contamination = (rgb.max(axis=2) > .40) & (rgb.min(axis=2) > .23)
        donors = (rgb.max(axis=2) < .28) & ((rgb.max(axis=2)-rgb.min(axis=2)) < .095)
        fallback = np.array((.123, .117, .109), dtype=np.float32)
    else:
        return tile, 0
    count = int(contamination.sum())
    if not count:
        return tile, 0
    result = tile.copy(); colors = result[:, :, :3]
    known = donors.copy(); pending = contamination.copy()
    if known.any():
        fallback = np.median(colors[known], axis=0)
    h, w = pending.shape
    for _ in range(48):
        if not pending.any():
            break
        padded_colors = np.pad(colors*known[:, :, None], ((1, 1), (1, 1), (0, 0)), mode='constant')
        padded_known = np.pad(known.astype(np.float32), 1, mode='constant')
        totals = np.zeros_like(colors); weights = np.zeros((h, w), dtype=np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            totals += padded_colors[1+dy:1+dy+h, 1+dx:1+dx+w]
            weights += padded_known[1+dy:1+dy+h, 1+dx:1+dx+w]
        fill = pending & (weights > 0)
        if not fill.any():
            break
        colors[fill] = totals[fill]/weights[fill, None]
        known[fill] = True; pending[fill] = False
    colors[pending] = fallback
    return result, count


def _image_material(role, label, image):
    material = bpy.data.materials.new(role + '_restored_' + label)
    material.use_nodes = True; material.diffuse_color = (1, 1, 1, 1)
    bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        bsdf = material.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        output = next((n for n in material.node_tree.nodes if n.type == 'OUTPUT_MATERIAL'), None)
        if output is None:
            output = material.node_tree.nodes.new('ShaderNodeOutputMaterial')
        material.node_tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    bsdf.inputs['Roughness'].default_value = .52 if label == 'hair' else .32 if label == 'jewelry' else .78
    texture = material.node_tree.nodes.new('ShaderNodeTexImage'); texture.image = image; texture.interpolation = 'Linear'
    material.node_tree.links.new(texture.outputs['Color'], bsdf.inputs['Base Color'])
    return material


def _repack_texture(obj, source_image, role, label, target, density=1.0, charts=None):
    """Copy each source chart with gutters into this garment's own UV texture."""
    width, height = source_image.size; gutter = 4
    if charts is None:
        charts = _charts(obj, source_image, density, gutter)
    out_width, out_height, positions = _pack_rectangles([c['rect'] for c in charts])
    source_pixels = _pixels(source_image)
    pixels = np.zeros((out_height, out_width, 4), dtype=np.float32)
    pixels[:, :, 3] = 1
    original_uv = obj.data.uv_layers.active
    original_uv.name = 'SourceAtlasUV'
    packed = obj.data.uv_layers.new(name='GarmentUV', do_init=True)
    cleaned_pixels = 0
    for i, chart in enumerate(charts):
        x, y = positions[i]; ox, oy = chart['output']; lx, ly = chart['lo']; sx, sy = chart['size']
        # Bilinear resampling only when a source chart exceeds the character
        # texture budget. Mapping and texel centers use the same affine map.
        xs = lx + (np.arange(ox)+.5) * sx/ox - .5
        ys = ly + (np.arange(oy)+.5) * sy/oy - .5
        x0 = np.floor(xs).astype(int); y0 = np.floor(ys).astype(int)
        fx = (xs-x0)[None, :, None]; fy = (ys-y0)[:, None, None]
        x0 = np.clip(x0, 0, width-1); y0 = np.clip(y0, 0, height-1)
        x1 = np.minimum(x0+1, width-1); y1 = np.minimum(y0+1, height-1)
        rows0 = source_pixels[y0[:, None], x0[None, :]]*(1-fx) + source_pixels[y0[:, None], x1[None, :]]*fx
        rows1 = source_pixels[y1[:, None], x0[None, :]]*(1-fx) + source_pixels[y1[:, None], x1[None, :]]*fx
        tile = rows0*(1-fy)+rows1*fy
        tile, cleaned = _clean_garment_tile(tile, role, label); cleaned_pixels += cleaned
        padded = np.pad(tile, ((gutter, gutter), (gutter, gutter), (0, 0)), mode='edge')
        pixels[y:y+oy+2*gutter, x:x+ox+2*gutter] = padded
        for loop in chart['loops']:
            u, v = original_uv.data[loop].uv
            packed.data[loop].uv = ((x+gutter+(u*width-lx)*ox/sx)/out_width,
                                    (y+gutter+(v*height-ly)*oy/sy)/out_height)
    # Make garment coordinates UV0: Unity and Blender default to the first UV.
    saved_source = [tuple(x.uv) for x in original_uv.data]
    saved_packed = [tuple(x.uv) for x in packed.data]
    obj.data.uv_layers.remove(packed)
    original_uv.name = 'GarmentUV'
    for loop, coords in zip(original_uv.data, saved_packed):
        loop.uv = coords
    preserved = obj.data.uv_layers.new(name='SourceAtlasUV', do_init=False)
    for loop, coords in zip(preserved.data, saved_source):
        loop.uv = coords
    obj.data.uv_layers.active_index = 0; obj.data.uv_layers[0].active_render = True
    image = bpy.data.images.new(role + '_restored_' + label + '_albedo', width=out_width, height=out_height, alpha=True)
    image.pixels.foreach_set(pixels.ravel()); image.filepath_raw = str(target); image.file_format = 'PNG'
    image.save(); image.pack()
    material = _image_material(role, label, image)
    obj.data.materials.clear(); obj.data.materials.append(material)
    for face in obj.data.polygons:
        face.material_index = 0
    return {'texture': str(target), 'size': [out_width, out_height], 'uv_charts': len(charts),
            'source_density': density, 'gutter_pixels': gutter, 'material': material.name,
            'cloth_skin_leakage_pixels_repainted': cleaned_pixels}


def texture_roundtrip_error(obj, source_image):
    """Compare saved PNG against the source at face-center UV coordinates."""
    image = next(n.image for n in obj.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE')
    saved = bpy.data.images.load(image.filepath, check_existing=False)
    source_pixels = _pixels(source_image); target_pixels = _pixels(saved)
    source_uv = obj.data.uv_layers['SourceAtlasUV']; target_uv = obj.data.uv_layers['GarmentUV']
    def sample(pixels, coords):
        h, w, _ = pixels.shape
        x = np.clip(coords[:, 0]*w-.5, 0, w-1); y = np.clip(coords[:, 1]*h-.5, 0, h-1)
        x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
        x1 = np.minimum(x0+1, w-1); y1 = np.minimum(y0+1, h-1)
        fx = (x-x0)[:, None]; fy = (y-y0)[:, None]
        return (pixels[y0, x0, :3]*(1-fx)+pixels[y0, x1, :3]*fx)*(1-fy)+(pixels[y1, x0, :3]*(1-fx)+pixels[y1, x1, :3]*fx)*fy
    source_coords = []; target_coords = []
    for face in obj.data.polygons:
        source_coords.append(np.mean([source_uv.data[i].uv[:] for i in face.loop_indices], axis=0))
        target_coords.append(np.mean([target_uv.data[i].uv[:] for i in face.loop_indices], axis=0))
    error = np.abs(sample(source_pixels, np.asarray(source_coords))-sample(target_pixels, np.asarray(target_coords)))
    bpy.data.images.remove(saved)
    return {'mean_abs_rgb': float(error.mean()), 'p95_abs_rgb': float(np.percentile(error, 95)), 'max_abs_rgb': float(error.max())}


def _separate_faces(obj, face_indices, name):
    """Copy then delete unwanted faces so all deform/custom layers survive."""
    part = obj.copy(); part.data = obj.data.copy(); part.name = name
    obj.users_collection[0].objects.link(part)
    mesh = part.data
    bm = bmesh.new(); bm.from_mesh(mesh); bm.faces.ensure_lookup_table()
    keep = set(face_indices)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(mesh); bm.free(); mesh.update()
    return part


def _cloth_edge_returns(obj, amount=.0018):
    """Turn garment opening edges inward, keeping the authored outer surface.

    A narrow return is sufficient for a closed-looking hem and avoids doubling
    the entire garment mesh or filling deliberate sleeve/neck openings.
    """
    bm = bmesh.new(); bm.from_mesh(obj.data)
    boundaries = [e for e in bm.edges if e.is_boundary]
    if not boundaries:
        bm.free(); return {'edge_return_faces': 0}
    bm.normal_update()
    # Copy vertex deform data explicitly; Blender's bare edge-extrusion operator
    # otherwise leaves new rim loop UVs at (0,0), causing visible dark seams.
    inner = {}; uv_layers = list(bm.loops.layers.uv.values())
    deform = bm.verts.layers.deform.active
    vertex_layers = list(bm.verts.layers.float.values()) + list(bm.verts.layers.int.values())
    face_layers = list(bm.faces.layers.float.values()) + list(bm.faces.layers.int.values())
    for edge in boundaries:
        for vertex in edge.verts:
            if vertex not in inner:
                co = vertex.co-vertex.normal*amount
                new = bm.verts.new(co)
                if deform:
                    for group, weight in vertex[deform].items():
                        new[deform][group] = weight
                for layer in vertex_layers:
                    new[layer] = vertex[layer]
                inner[vertex] = new
    added = 0
    for edge in boundaries:
        source_face = edge.link_faces[0]
        source_loop = next(loop for loop in source_face.loops if loop.edge == edge)
        a = source_loop.vert; b = source_loop.link_loop_next.vert
        rim = bm.faces.new((b, a, inner[a], inner[b]))
        rim.material_index = source_face.material_index; rim.smooth = True
        for layer in face_layers:
            rim[layer] = source_face[layer]
        source_loops = [source_loop.link_loop_next, source_loop, source_loop, source_loop.link_loop_next]
        for output, origin in zip(rim.loops, source_loops):
            for layer in uv_layers:
                output[layer].uv = origin[layer].uv
        added += 1
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    return {'edge_return_faces': added, 'edge_return_depth_m': amount}


def finish_vivian_dress_caps(obj):
    """Round the two small sleeve-cap tips, then bind their outside edges.

    Call on the separated Vivian Dress after ``modularize``. Only vertices at
    |x| > 0.202 m and z 1.40–1.53 m can move. The neckline, chest, skirt slit,
    asymmetrical hem and body are outside this deliberately small work region.
    """
    from mathutils import Vector
    if obj.get('authored_role') != 'operator' or obj.get('authored_part') != 'dress':
        return {'skipped': True}
    if obj.get('vivian_cap_finish'):
        return json.loads(obj['vivian_cap_finish'])
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.verts.ensure_lookup_table()
    region = {v for v in bm.verts if abs(v.co.x) > .202 and 1.40 < v.co.z < 1.53}
    original = {v: v.co.copy() for v in bm.verts}
    max_motion = .014
    for _ in range(7):
        positions = {}
        for vertex in region:
            neighbors = [edge.other_vert(vertex) for edge in vertex.link_edges]
            if not neighbors:
                continue
            mean = sum((v.co for v in neighbors), Vector())/len(neighbors)
            strength = min(1, max(0, (abs(original[vertex].x)-.202)/.028))*.47
            proposal = vertex.co.lerp(mean, strength)
            displacement = proposal-original[vertex]
            if displacement.length > max_motion:
                displacement.normalize(); proposal = original[vertex]+displacement*max_motion
            positions[vertex] = proposal
        for vertex, co in positions.items():
            vertex.co = co
    for face in bm.faces:
        if all(v in region for v in face.verts):
            face.smooth = True
    for edge in bm.edges:
        if all(v in region for v in edge.verts):
            edge.smooth = True
    bm.normal_update()
    boundary = [e for e in bm.edges if e.is_boundary and all(v in region for v in e.verts)]
    adjacency = defaultdict(list)
    for edge in boundary:
        a, b = edge.verts; adjacency[a].append(b); adjacency[b].append(a)
    remaining = set(adjacency); paths = []
    while remaining:
        start = min((v for v in remaining if len(adjacency[v]) == 1), key=lambda v: v.index, default=min(remaining, key=lambda v: v.index))
        chain = []; previous = None; current = start
        while current in remaining:
            chain.append(current); remaining.remove(current)
            next_vertices = [v for v in adjacency[current] if v != previous and v in remaining]
            if not next_vertices:
                break
            previous, current = current, next_vertices[0]
        if len(chain) >= 3:
            paths.append(chain)
    # The original cap openings contain short saw-tooth segments. Smooth the
    # ordered opening curve itself (and its paired 1.8 mm outer return vertex),
    # rather than merely smoothing face normals over the old triangular tips.
    for path in paths:
        coordinates = [v.co.copy() for v in path]
        for _ in range(6):
            coordinates = [coordinates[0]] + [coordinates[i].lerp((coordinates[i-1]+coordinates[i+1])*.5, .55) for i in range(1, len(path)-1)] + [coordinates[-1]]
        offsets = {}
        for vertex, target in zip(path, coordinates):
            displacement = target-original[vertex]
            if displacement.length > .026:
                displacement.normalize(); target = original[vertex]+displacement*.026
            shift = target-vertex.co; offsets[vertex] = shift
            for edge in vertex.link_edges:
                neighbor = edge.other_vert(vertex)
                if neighbor in region and neighbor not in path and (neighbor.co-vertex.co).length < .006:
                    offsets[neighbor] = shift
        for vertex, shift in offsets.items():
            vertex.co += shift
    bm.normal_update()
    deform = bm.verts.layers.deform.active
    uv_layers = list(bm.loops.layers.uv.values())
    face_part = bm.faces.layers.int.get(PART_ATTRIBUTE)
    created_faces = []; radius = .0014
    for path in paths:
        rings = []; source_uvs = []
        for i, vertex in enumerate(path):
            tangent = path[min(i+1, len(path)-1)].co-path[max(0, i-1)].co
            if tangent.length < 1e-8:
                tangent = Vector((0, 1, 0))
            tangent.normalize()
            normal = vertex.normal-tangent*vertex.normal.dot(tangent)
            if normal.length < 1e-5:
                normal = tangent.cross(Vector((0, 0, 1)))
            normal.normalize(); across = tangent.cross(normal).normalized()
            source_loop = next(loop for face in vertex.link_faces for loop in face.loops if loop.vert == vertex)
            source_uvs.append({layer: source_loop[layer].uv.copy() for layer in uv_layers})
            ring = []
            for segment in range(6):
                angle = segment*math.tau/6
                point = vertex.co+(normal*math.cos(angle)+across*math.sin(angle))*radius
                new = bm.verts.new(point)
                if deform:
                    for group, weight in vertex[deform].items():
                        new[deform][group] = weight
                ring.append(new)
            rings.append(ring)
        for i in range(len(rings)-1):
            for segment in range(6):
                nxt = (segment+1) % 6
                face = bm.faces.new((rings[i][segment], rings[i][nxt], rings[i+1][nxt], rings[i+1][segment]))
                face.smooth = True
                if face_part:
                    face[face_part] = PARTS['operator'].index('dress')
                for loop, source in zip(face.loops, (source_uvs[i], source_uvs[i], source_uvs[i+1], source_uvs[i+1])):
                    for layer in uv_layers:
                        loop[layer].uv = source[layer]
                created_faces.append(face)
        for ring, source, reverse in ((rings[0], source_uvs[0], True), (rings[-1], source_uvs[-1], False)):
            face = bm.faces.new(tuple(reversed(ring)) if reverse else ring)
            face.smooth = True
            if face_part:
                face[face_part] = PARTS['operator'].index('dress')
            for loop in face.loops:
                for layer in uv_layers:
                    loop[layer].uv = source[layer]
            created_faces.append(face)
    if created_faces:
        bmesh.ops.recalc_face_normals(bm, faces=created_faces)
    moved = [(v.co-original[v]).length for v in region]
    untouched = max(((v.co-co).length for v, co in original.items() if v not in region), default=0)
    report = {'rounded_cap_vertices': sum(d>1e-7 for d in moved), 'maximum_cap_adjustment_m': max(moved, default=0),
              'outside_region_movement_m': untouched, 'binding_paths': len(paths), 'binding_faces': len(created_faces),
              'binding_radius_m': radius}
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    obj['vivian_cap_finish'] = json.dumps(report)
    return report


def modularize(obj, arm, role, texture_dir, output_dir=None, texture_budget=2048*2048, edge_returns=True):
    """Return actual editable mesh parts and an auditable export manifest.

    The input object is removed after successful partitioning and texture writes.
    Callers must export every returned part and validate their combined surface.
    Hair-shell finishing and Vivian's fitted bodice are separate finishing steps.
    """
    attribute = obj.data.attributes.get(PART_ATTRIBUTE)
    if attribute is None:
        raise ValueError('tag_source_parts must run before surface refinement')
    names = json.loads(obj['authored_parts'])
    source_image = bpy.data.images.get(obj['authored_source_image'])
    if source_image is None:
        source_image = bpy.data.images.load(obj['authored_source_path'])
    target_dir = Path(texture_dir) / role; target_dir.mkdir(parents=True, exist_ok=True)
    face_groups = defaultdict(list)
    for face, value in zip(obj.data.polygons, attribute.data):
        if value.value < 0 or value.value >= len(names):
            raise ValueError('Invalid refined part id on face %d' % face.index)
        face_groups[names[value.value]].append(face.index)
    parts = []
    for label in names:
        if label not in face_groups:
            continue
        suffix = ''.join(word.title() for word in label.split('_'))
        part = _separate_faces(obj, face_groups[label], 'SM_Char_' + NAMES[role] + '_' + suffix)
        part.parent = arm; part['authored_part'] = label; part['authored_role'] = role
        part['source_face_count'] = len(face_groups[label]); part.data.name = part.name + '_Mesh'
        parts.append(part)
    if sum(len(p.data.polygons) for p in parts) != len(obj.data.polygons):
        raise AssertionError('Garment split dropped or duplicated source faces')
    # Select one common density so small accessories cannot steal resolution from
    # faces. Every item's output is independent, with no full-atlas duplication.
    density = 1.0
    original_charts = [_charts(p, source_image) for p in parts]
    while True:
        scaled_charts = [_scaled_charts(charts, density) for charts in original_charts]
        sizes = [_pack_rectangles([c['rect'] for c in charts])[:2] for charts in scaled_charts]
        total_pixels = sum(w*h for w, h in sizes)
        if total_pixels <= texture_budget or density <= .125:
            break
        density *= .85
    report = {'role': role, 'source_mesh': obj.name, 'source_faces': len(obj.data.polygons),
              'texture_budget_pixels': texture_budget, 'texture_pixels': total_pixels,
              'source_texture': list(source_image.size), 'parts': []}
    for part, charts in zip(parts, scaled_charts):
        label = part['authored_part']; target = target_dir / (role + '_restored_' + label + '_albedo.png')
        texture = _repack_texture(part, source_image, role, label, target, density, charts)
        # Repack first: returning hem faces inherit their edge UVs and weights.
        hems = _cloth_edge_returns(part) if edge_returns and label in FABRIC and label != 'bra' else {}
        part.data.calc_loop_triangles()
        report['parts'].append(dict(part=label, object=part.name, vertices=len(part.data.vertices),
                                   triangles=len(part.data.loop_triangles), source_faces=part['source_face_count'],
                                   uv_layers=[uv.name for uv in part.data.uv_layers], **texture, **hems))
    obj_name = obj.name
    bpy.data.objects.remove(obj, do_unlink=True)
    if output_dir:
        path = Path(output_dir); path.mkdir(parents=True, exist_ok=True)
        (path / (role + '_modular_outfit.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('MODULAR OUTFIT ' + role + ' ' + json.dumps({'parts': len(parts), 'texture_pixels': total_pixels, 'source_mesh': obj_name}), flush=True)
    return parts, report


def render_outfit_preview(parts, role, destination, views=('front', 'angle', 'back')):
    """Isolate real garment meshes so body/hair cannot conceal split defects."""
    from mathutils import Vector
    wanted = {'dress'} if role == 'operator' else {'cropped_top', 'sleeves', 'wrap_skirt'}
    for part in parts:
        part.hide_render = part['authored_part'] not in wanted
    scene = bpy.context.scene
    for item in list(bpy.data.objects):
        if item.type in {'LIGHT', 'CAMERA'}:
            bpy.data.objects.remove(item, do_unlink=True)
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 4; scene.cycles.use_denoising = True
    scene.render.threads_mode = 'FIXED'; scene.render.threads = 2
    if scene.world is None:
        scene.world = bpy.data.worlds.new('Outfit inspection studio')
    scene.world.use_nodes = True
    background = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
    background.inputs[0].default_value = (.20, .24, .27, 1); background.inputs[1].default_value = .45
    scene.view_settings.view_transform = 'AgX'; scene.view_settings.look = 'AgX - Medium High Contrast'
    target = Vector((0, -.08 if role == 'operator' else -.16, 1.115))
    for location, power, size in [((-3, -4, 5), 450, 4), ((4, -2, 3.5), 220, 3), ((0, 3, 5), 400, 3)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        lamp = bpy.context.object; lamp.data.energy = power; lamp.data.shape = 'DISK'; lamp.data.size = size
        lamp.rotation_euler = (target-lamp.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.object.camera_add(); camera = bpy.context.object; scene.camera = camera; camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 1.14 if role == 'operator' else 1.72
    scene.render.resolution_x = 640; scene.render.resolution_y = 800; scene.render.resolution_percentage = 100
    for suffix, position in [('front', (0, -4.5, 1.16)), ('angle', (2.1, -4.5, 1.34)), ('back', (1, 4.5, 1.24))]:
        if suffix not in views:
            continue
        camera.location = position; camera.rotation_euler = (target-camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(Path(destination)/(role+'_outfit_'+suffix+'.png'))
        bpy.ops.render.render(write_still=True)
    for part in parts:
        part.hide_render = False


if __name__ == '__main__':
    import sys
    arguments = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    if '--audit' in arguments:
        root = Path(__file__).resolve().parents[2]
        sys.path.insert(0, str(root/'Tools/Characters'))
        import importlib.util
        spec = importlib.util.spec_from_file_location('restore_authored_cast', root/'Tools/Characters/restore_authored_cast.py')
        restore = importlib.util.module_from_spec(spec); spec.loader.exec_module(restore)
        records = json.loads((root/'Art/Characters/Imported/character_manifest.json').read_text())['characters']
        selected = arguments[arguments.index('--role')+1] if '--role' in arguments else None
        results = {}
        for info in records:
            role = info['role']
            if selected and selected != role:
                continue
            bpy.ops.wm.open_mainfile(filepath=str(root/'Art/Characters/Imported'/(role+'.blend')))
            obj = next(o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.vertices)>500)
            arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
            image = bpy.data.images.load(str(root/'Art/Characters/Imported'/(role+'_source_albedo.png')))
            if '--refine' in arguments:
                arm.animation_data_clear()
                for bone in arm.pose.bones:
                    bone.location = (0, 0, 0); bone.rotation_mode = 'QUATERNION'; bone.rotation_quaternion = (1, 0, 0, 0); bone.scale = (1, 1, 1)
                for item in list(bpy.data.objects):
                    if item != obj and item != arm:
                        bpy.data.objects.remove(item, do_unlink=True)
            obj.modifiers.clear(); restore.recover_source_t_pose(obj, arm, info)
            result = tag_source_parts(obj, role, image)
            result['uv_charts'] = len(_uv_islands(obj.data, obj.data.uv_layers.active))
            if '--faces' in arguments:
                colors = _face_colors(obj, image)
                table = [dict(face=f.index, center=list(f.center), rgb=[float(v) for v in c], part=PARTS[role][obj.data.attributes[PART_ATTRIBUTE].data[f.index].value]) for f, c in zip(obj.data.polygons, colors)]
                (root/'Logs'/(role+'_part_faces.json')).write_text(json.dumps(table))
            if '--pack' in arguments:
                destination = root/'Logs/modular-outfit-audit'
                if '--refine' in arguments:
                    import source_surface_finish
                    import build_indie_cast
                    colors = restore.sample_colors(obj, image)
                    groups = restore.semantic_groups(obj, role, colors, info)
                    restore.make_rig(obj, arm, info, colors); restore.rebuild_weights(obj, arm, info, groups)
                    restore.lower_new_bind(obj, arm); obj.parent = arm
                    restore.add_secondary(obj, arm, role, groups, vars(build_indie_cast))
                    source_surface_finish.refine_surface(obj, role)
                    skin = obj.modifiers.new('Authored skin', 'ARMATURE'); skin.object = arm
                parts, packed = modularize(obj, arm, role, destination, output_dir=destination, edge_returns='--hems' in arguments)
                for part in parts:
                    print('TEXTURE CHECK ' + part['authored_part'] + ' ' + json.dumps(texture_roundtrip_error(part, image)), flush=True)
                result['packed'] = packed
                bpy.ops.wm.save_as_mainfile(filepath=str(destination/(role+'.blend')))
                if '--render-outfit' in arguments:
                    render_outfit_preview(parts, role, destination)
            results[role] = result
            print('PART AUDIT ' + role + ' ' + json.dumps(result), flush=True)
        (root/'Logs/modular_parts_audit.json').write_text(json.dumps(results, indent=2))
