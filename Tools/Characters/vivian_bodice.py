"""Vivian's fitted innerwear, rebuilt as a separate, skinned garment.

The authored mesh is the fitting reference, not a painted replacement for
volume. Cups are retopologized into regular panels; binding and shoulder bands
have real thickness. Original proportions and neckline are retained, with a
clean independently baked textile. Run after ``modular_outfits.modularize``.
"""

from math import pi, sin, cos, exp, sqrt
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform


def _clamp(value, low=0.0, high=1.0):
    return max(low, min(high, value))


def _normal_weights(weights):
    result = {k: v for k, v in sorted(weights.items(), key=lambda p: -p[1])[:4] if v > 1e-6}
    total = sum(result.values())
    return {k: v / total for k, v in result.items()} if total else {'chest': 1.0}


class _Reference:
    def __init__(self, obj):
        self.obj = obj
        mesh = obj.data
        mesh.calc_loop_triangles()
        self.points = [v.co.copy() for v in mesh.vertices]
        self.triangles = list(mesh.loop_triangles)
        self.bvh = BVHTree.FromPolygons(self.points, [tuple(t.vertices) for t in self.triangles], all_triangles=True)
        self.uv = mesh.uv_layers.active
        names = {g.index: g.name for g in obj.vertex_groups}
        self.weights = [{names[g.group]: g.weight for g in v.groups} for v in mesh.vertices]

    def sample(self, point):
        nearest = self.bvh.find_nearest(point)
        if nearest[2] is None:
            return Vector((.5, .5)), {'chest': 1.0}
        p, normal, index, distance = nearest
        triangle = self.triangles[index]
        a, b, c = [self.points[i] for i in triangle.vertices]
        bary = barycentric_transform(p, a, b, c, Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
        weights = {}
        uv = Vector((0.0, 0.0))
        for amount, vi, li in zip(bary, triangle.vertices, triangle.loops):
            for name, value in self.weights[vi].items():
                weights[name] = weights.get(name, 0.0) + max(0.0, amount) * value
            if self.uv:
                uv += self.uv.data[li].uv * amount
        return uv, _normal_weights(weights)


class _Builder:
    def __init__(self, reference):
        self.reference = reference
        self.vertices = []
        self.faces = []
        self.uvs = []
        self.weights = []
        self.regions = {}

    def vertex(self, point, sample=None):
        point = Vector(point)
        uv, weights = self.reference.sample(Vector(sample) if sample is not None else point)
        index = len(self.vertices)
        self.vertices.append(point)
        self.uvs.append(uv)
        self.weights.append(weights)
        return index

    def panel(self, grid, region):
        rows, columns = len(grid), len(grid[0])
        offset = len(self.vertices)
        for row in grid:
            for point in row:
                self.vertex(point)
        for row in range(rows - 1):
            for col in range(columns - 1):
                i = offset + row * columns + col
                self.faces.append((i, i + 1, i + columns + 1, i + columns))
        lining = len(self.vertices)
        for row in range(rows):
            for col in range(columns):
                point = Vector(grid[row][col])
                across = Vector(grid[row][min(col + 1, columns - 1)]) - Vector(grid[row][max(0, col - 1)])
                along = Vector(grid[min(row + 1, rows - 1)][col]) - Vector(grid[max(0, row - 1)][col])
                normal = across.cross(along).normalized()
                reference_normal = self.reference.bvh.find_nearest(point)[1]
                if reference_normal is not None and normal.dot(reference_normal) < 0:
                    normal.negate()
                self.vertex(point - normal * .0014, sample=point)
        for row in range(rows - 1):
            for col in range(columns - 1):
                i = lining + row * columns + col
                self.faces.append((i, i + columns, i + columns + 1, i + 1))
        boundary = (list(range(columns)) +
                    [row * columns + columns - 1 for row in range(1, rows)] +
                    [(rows - 1) * columns + col for col in range(columns - 2, -1, -1)] +
                    [row * columns for row in range(rows - 2, 0, -1)])
        for a, b in zip(boundary, boundary[1:] + boundary[:1]):
            self.faces.append((offset + a, lining + a, lining + b, offset + b))
        self.regions[region] = list(range(offset, len(self.vertices)))

    def piping(self, path, radius=.0011, sides=6, region='binding'):
        """A small closed fabric binding; not a line painted on skin."""
        offset = len(self.vertices)
        for i, point in enumerate(path):
            point = Vector(point)
            tangent = Vector(path[min(i + 1, len(path) - 1)]) - Vector(path[max(0, i - 1)])
            tangent.normalize()
            a = tangent.cross(Vector((0, -1, 0))).normalized()
            if a.length < .01:
                a = tangent.cross(Vector((1, 0, 0))).normalized()
            b = tangent.cross(a).normalized()
            for j in range(sides):
                angle = 2 * pi * j / sides
                self.vertex(point + radius * (cos(angle) * a + sin(angle) * b), sample=point)
        for i in range(len(path) - 1):
            for j in range(sides):
                a = offset + i * sides + j
                b = offset + i * sides + (j + 1) % sides
                self.faces.append((a, b, b + sides, a + sides))
        self.faces.append(tuple(offset + j for j in reversed(range(sides))))
        end = offset + (len(path) - 1) * sides
        self.faces.append(tuple(end + j for j in range(sides)))
        self.regions.setdefault(region, []).extend(range(offset, len(self.vertices)))

    def finish(self, source, armature):
        mesh = bpy.data.meshes.new('Vivian_FittedBra_Mesh')
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        uv = mesh.uv_layers.new(name='BraUV')
        for loop in mesh.loops:
            uv.data[loop.index].uv = self.uvs[loop.vertex_index]
        obj = bpy.data.objects.new('SM_Char_Vivian_Bra', mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = armature
        for key, value in source.items():
            obj[key] = value
        obj['authored_part'] = 'bra'
        obj['separate_volumetric_garment'] = True
        obj['shell_thickness_m'] = .0014
        obj['construction'] = 'fitted quad cups, fabric shell, bound neckline and shoulder bands'
        for material in source.data.materials:
            obj.data.materials.append(material)
        for bone in armature.data.bones:
            obj.vertex_groups.new(name=bone.name)
        for i, weights in enumerate(self.weights):
            for name, value in weights.items():
                if name in obj.vertex_groups:
                    obj.vertex_groups[name].add([i], value, 'REPLACE')
        region_attribute = mesh.attributes.new('garment_region', 'INT', 'POINT')
        for region_id, indices in enumerate(self.regions.values(), 1):
            for index in indices:
                region_attribute.data[index].value = region_id
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        for polygon in mesh.polygons:
            polygon.use_smooth = True
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        deform = obj.modifiers.new('Authored garment skin', 'ARMATURE')
        deform.object = armature
        return obj


def _cup_reference(reference, sign):
    """Use the actual original silhouette and depth, excluding shoulder straps."""
    pts = np.array([tuple(p) for p in reference.points
                    if p.x * sign >= -.001 and p.y < -.060 and 1.31 < p.z < 1.444], dtype=float)
    if len(pts) < 20:
        raise ValueError('Vivian bra needs the authored cup surface before reconstruction')
    pts[:, 0] *= sign
    return pts


def _curve(values, count, passes=3):
    result = np.interp(np.linspace(0, 1, count), np.linspace(0, 1, len(values)), values)
    for _ in range(passes):
        saved = result.copy()
        result[1:-1] = .25 * saved[:-2] + .5 * saved[1:-1] + .25 * saved[2:]
    return result


def _torso_depth(x, z):
    """Continuous chest with paired blended anatomical forms, in source metres.

    The original frontmost breast depth is about 0.134 m. These smooth forms
    retain that extent, while replacing its flat triangular chest plates.
    """
    base = float(np.interp(z, [1.30, 1.335, 1.36, 1.395, 1.42, 1.44, 1.47, 1.50, 1.525, 1.55],
                          [-.097, -.098, -.087, -.083, -.072, -.06125, -.0425, -.024, -.0128, -.0105]))
    base *= sqrt(max(.08, 1.0 - (abs(x) / .205) ** 2))
    forms = []
    for center in [-.066, .066]:
        height_radius = .079 if z < 1.398 else .068
        radius = ((x - center) / .063) ** 2 + ((z - 1.398) / height_radius) ** 2
        t = _clamp(1.0 - radius)
        forms.append(.059 * t * t * (3 - 2 * t))
    # Smooth maximum preserves the cleavage valley without a seam at x=0.
    volume = (forms[0] ** 4 + forms[1] ** 4) ** .25
    return base - volume


def _fit_depth(points):
    return _torso_depth


def _envelope(points, xs):
    tops, bottoms = [], []
    for x in xs:
        distance = np.abs(points[:, 0] - x)
        nearby = points[distance < .0055]
        if len(nearby) < 4:
            nearby = points[np.argsort(distance)[:8]]
        tops.append(float(np.quantile(nearby[:, 2], .97)))
        bottoms.append(float(np.quantile(nearby[:, 2], .03)))
    top = _curve(tops, len(xs), 5)
    bottom = _curve(bottoms, len(xs), 5)
    return top, bottom


def _build_cups(builder, reference):
    anchors = {}
    medial = []
    for side, sign in [('L', 1), ('R', -1)]:
        points = _cup_reference(reference, sign)
        width = min(.111, float(np.quantile(points[:, 0], .985)))
        xs = np.linspace(.0005, width, 25)
        upper, lower = _envelope(points, xs)
        depth = _fit_depth(points)
        grid = []
        # Crosswise rows follow the cup's breast form; the top edge retains
        # a small scallop, rather than the old saw-toothed triangle shards.
        for j in range(10):
            t = j / 9
            row = []
            for i, x in enumerate(xs):
                scallop = .0014 * sin(pi * i / (len(xs) - 1)) * cos(i / (len(xs) - 1) * pi * 10)
                z = lower[i] - .0035 + (upper[i] - lower[i] + .0035 + scallop) * t
                fitted_x = x * (1 - .11 * t * (x / width) ** 4)
                y = depth(fitted_x, z) - .0018
                row.append(Vector((sign * fitted_x, y, z)))
            grid.append(row)
        builder.panel(grid, 'cup_' + side)
        builder.piping(grid[-1], radius=.00085, region='neckline_' + side)
        builder.piping(grid[0], radius=.0010, region='underbust_binding_' + side)
        medial.append((grid[0][0].z, grid[-1][0].z))
        # The strap meets the highest lateral cup region, not its pointed end.
        at = max(range(int(len(xs) * .65), len(xs)), key=lambda i: grid[-1][i].z)
        anchors[side] = grid[-1][at]
    low = min(p[0] for p in medial) - .001
    high = min(p[1] for p in medial)
    gore = []
    for z in np.linspace(low, high, 12):
        gore.append([Vector((x, _torso_depth(x, z) - .0031, z)) for x in [-.0018, 0, .0018]])
    builder.panel(gore, 'center_seam')
    return anchors


def _strap_path(reference, anchor, sign):
    """Fit the original shoulder-band route by its narrow source cross-sections."""
    points = [p for p in reference.points if p.x * sign > .045 and p.z > 1.425]
    if not points:
        return []
    top = max(p.z for p in points)
    front = [p for p in points if p.y < 0]
    path = [anchor.copy()]
    for z in np.linspace(anchor.z + .004, top, 18):
        nearby = sorted(front, key=lambda p: abs(p.z - z))[:max(3, len(front) // 20)]
        p = sum(nearby, Vector()) / len(nearby)
        p.z = float(z)
        p.y = min(p.y - .0018, _torso_depth(p.x, p.z) - .003)
        path.append(p)
    back = [p for p in points if p.y >= 0]
    if back:
        # Original back straps are retained only where present in the source.
        for z in np.linspace(top - .003, max(1.425, min(p.z for p in back)), 16):
            nearby = sorted(back, key=lambda p: abs(p.z - z))[:max(3, len(back) // 20)]
            p = sum(nearby, Vector()) / len(nearby)
            p.z = float(z)
            p.y += .0018
            path.append(p)
    for _ in range(3):
        original = [p.copy() for p in path]
        for i in range(1, len(path) - 1):
            path[i] = .25 * original[i - 1] + .5 * original[i] + .25 * original[i + 1]
    return path


def _build_straps(builder, reference, anchors):
    for side, sign in [('L', 1), ('R', -1)]:
        path = _strap_path(reference, anchors[side], sign)
        if len(path) < 3:
            continue
        grid = []
        for i, p in enumerate(path):
            tangent = path[min(i + 1, len(path) - 1)] - path[max(0, i - 1)]
            # The ribbon width follows local lateral X and stays narrow.
            across = Vector((1, 0, 0)) - tangent.normalized() * tangent.normalized().x
            across.normalize()
            grid.append([p + across * width for width in [-.0036, 0, .0036]])
        builder.panel(grid, 'strap_' + side)
        for edge in [0, -1]:
            builder.piping([row[edge] for row in grid], radius=.00055, region='strap_binding_' + side)


def _restore_body_underlay(source, body, reference, dress):
    """Complete the body beneath the separate clothing opening.

    The old asset fused clothing into skin, so removing its bra also removed
    the torso surface there. A fitted underlay closes that missing body region;
    it does not add another renderer or alter the original bust proportions.
    """
    body_reference = _Reference(body)
    dress_reference = _Reference(dress) if dress is not None else None
    def ray(ref, x, z):
        hit = ref.bvh.ray_cast(Vector((x, -.7, z)), Vector((0, 1, 0)))[0]
        return hit if hit is not None and hit.y < .045 else None
    def surface(x, z):
        hits = [(ray(ref, x, z), name) for ref, name in
                [(body_reference, 'body'), (reference, 'bra')] +
                ([(dress_reference, 'dress')] if dress_reference else [])]
        hits = [(p, n) for p, n in hits if p is not None]
        return min(hits, key=lambda pair: pair[0].y) if hits else (None, None)
    skin_hit = body_reference.bvh.ray_cast(Vector((0, -.7, 1.477)), Vector((0, 1, 0)))
    skin_triangle = body_reference.triangles[skin_hit[2]]
    skin_chart = [body_reference.uv.data[index].uv.copy() for index in skin_triangle.loops]
    def clean_skin_uv(p):
        # All vertices stay in ONE verified bare-chest chart. Nearest-triangle
        # sampling per vertex would cross unrelated packed atlas islands.
        a = .40 + .08 * _clamp((p.z - 1.38) / .15)
        b = .27 + .10 * _clamp((p.x + .13) / .26)
        return skin_chart[0] * a + skin_chart[1] * b + skin_chart[2] * (1 - a - b)
    vertices, faces, weights, uvs = [], [], [], []
    columns, rows = 57, 31
    for z in np.linspace(1.329, 1.455, rows):
        for x in np.linspace(-.126, .126, columns):
            hit, owner = surface(x, z)
            y = _torso_depth(x, z)
            edge = max(_clamp((abs(x) - .108) / .018), _clamp((z - 1.440) / .015))
            if owner == 'dress':
                # The underlay is physically behind the actual dress border.
                y = max(y, hit.y + .002)
            elif hit is not None:
                y = y * (1 - edge) + (hit.y - .00025) * edge
                if owner == 'bra':
                    y += .0025
            p = Vector((x, y, z)); vertices.append(p)
            weights.append(reference.sample(p)[1]); uvs.append(clean_skin_uv(p))
    for j in range(rows - 1):
        for i in range(columns - 1):
            a = j * columns + i
            indices = (a, a + 1, a + columns + 1, a + columns)
            center = sum((vertices[index] for index in indices), Vector()) / 4
            hit, owner = surface(center.x, center.z)
            if owner in {'body', 'bra'}:
                faces.append(indices)
    # Fill only the old strap footprint with skin, retaining the unchanged
    # shoulder/neck silhouette and leaving every necklace vertex untouched.
    strap_indices = {}
    for face in source.data.polygons:
        center = face.center
        if center.z <= 1.443:
            continue
        indices = []
        for vi in face.vertices:
            if vi not in strap_indices:
                p = source.data.vertices[vi].co.copy()
                samples = [ray(body_reference, p.x + delta, p.z) for delta in [-.012, .012]]
                samples = [q for q in samples if q is not None]
                if samples:
                    p.y = sum(q.y for q in samples) / len(samples) + .0002
                strap_indices[vi] = len(vertices)
                vertices.append(p); uvs.append(clean_skin_uv(p)); weights.append(reference.sample(p)[1])
            indices.append(strap_indices[vi])
        faces.append(tuple(indices))
    mesh = bpy.data.meshes.new('Vivian_ContinuousBust_Mesh')
    mesh.from_pydata(vertices, [], faces); mesh.update()
    uv = mesh.uv_layers.new(name=body.data.uv_layers.active.name)
    for loop in mesh.loops:
        uv.data[loop.index].uv = uvs[loop.vertex_index]
    for material in body.data.materials:
        mesh.materials.append(material)
    patch = bpy.data.objects.new('Vivian_ContinuousBust', mesh)
    bpy.context.collection.objects.link(patch)
    patch.parent = body.parent
    for group in body.vertex_groups:
        patch.vertex_groups.new(name=group.name)
    for index, values in enumerate(weights):
        for name, value in values.items():
            if name in patch.vertex_groups:
                patch.vertex_groups[name].add([index], value, 'REPLACE')
    for face in mesh.polygons:
        face.use_smooth = True
    # Remove the mixed skin/painted-lace triangles only within the fully covered
    # front patch. Unchanged face, neck, shoulders and dress remain source data.
    bm = bmesh.new(); bm.from_mesh(body.data)
    remove = []
    for face in bm.faces:
        center = face.calc_center_median()
        if (min(v.co.z for v in face.verts) > 1.337 and
                max(v.co.z for v in face.verts) < 1.451 and
                max(abs(v.co.x) for v in face.verts) < .117 and center.y < -.035):
            remove.append(face)
    bmesh.ops.delete(bm, geom=remove, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(body.data); bm.free()
    # Old body triangles on the cut boundary contained half skin / half painted
    # lace. Keep their geometry at the transition, but clean those UV samples;
    # no dark saw-tooth paint survives behind the new bound neckline.
    body_uv = body.data.uv_layers.active
    for face in body.data.polygons:
        center = face.center
        if .0 <= abs(center.x) < .12 and 1.374 < center.z < 1.468 and center.y < -.022:
            for loop in face.loop_indices:
                body_uv.data[loop].uv = clean_skin_uv(body.data.vertices[body.data.loops[loop].vertex_index].co)
    for vertex in body.data.vertices:
        p = vertex.co
        if abs(p.x) < .116 and 1.337 < p.z < 1.451 and p.y < -.035:
            p.y = max(p.y, _torso_depth(p.x, p.z) + .002)
    bpy.ops.object.select_all(action='DESELECT')
    patch.select_set(True); body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    materials = list(body.data.materials)
    unique = []
    remap = {}
    for index, material in enumerate(materials):
        if material not in unique:
            unique.append(material)
        remap[index] = unique.index(material)
    assignments = [remap[f.material_index] for f in body.data.polygons]
    body.data.materials.clear()
    for material in unique:
        body.data.materials.append(material)
    for face, index in zip(body.data.polygons, assignments):
        face.material_index = index
    bm = bmesh.new(); bm.from_mesh(body.data)
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(body.data); bm.free(); body.data.update()
    body['complete_body_beneath_bra'] = True
    return len(vertices)


def _fit_neckline(parts):
    dress = next((o for o in parts if o.get('authored_part') == 'dress'), None)
    if dress is None:
        return
    for vertex in dress.data.vertices:
        p = vertex.co
        if 1.30 < p.z < 1.442 and abs(p.x) < .145 and p.y < -.05:
            p.y = min(p.y, _torso_depth(p.x, p.z) - .0038)
    dress.data.update()


def _bake_clean_fabric(obj, texture_dir):
    """Bake a subtle, editable charcoal textile onto the new garment UV.

    Blender's material bake keeps texture coordinates coherent across new quad
    panels. Transferring the old split-triangle atlas vertex-by-vertex would
    interpolate across unrelated islands and create colored scratches.
    """
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    for layer in list(obj.data.uv_layers):
        obj.data.uv_layers.remove(layer)
    obj.data.uv_layers.new(name='GarmentUV')
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.1519, island_margin=.018, area_weight=.1)
    bpy.ops.object.mode_set(mode='OBJECT')
    material = obj.data.materials[0]
    material.use_nodes = True
    nodes = material.node_tree.nodes; links = material.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    emission = nodes.new('ShaderNodeEmission')
    coords = nodes.new('ShaderNodeTexCoord')
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 185
    noise.inputs['Detail'].default_value = 2
    ramp = nodes.new('ShaderNodeValToRGB')
    # Subdued woven fabric. Small modeled scallops carry the lace silhouette;
    # the texture never paints a breast or adds hard anatomical shading.
    ramp.color_ramp.elements[0].position = .20
    ramp.color_ramp.elements[0].color = (.018, .015, .018, 1)
    ramp.color_ramp.elements[1].position = .80
    ramp.color_ramp.elements[1].color = (.036, .028, .032, 1)
    links.new(coords.outputs['UV'], noise.inputs['Vector'])
    links.new(noise.outputs['Fac'], ramp.inputs['Fac'])
    links.new(ramp.outputs['Color'], emission.inputs['Color'])
    links.new(emission.outputs[0], output.inputs['Surface'])
    image = bpy.data.images.new('operator_restored_bra_fabric', width=256, height=256, alpha=False)
    target = nodes.new('ShaderNodeTexImage'); target.image = image; nodes.active = target
    scene = bpy.context.scene
    prior_engine = scene.render.engine
    prior_samples = scene.cycles.samples
    prior_threads = (scene.render.threads_mode, scene.render.threads)
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 1
    scene.render.threads_mode = 'FIXED'; scene.render.threads = 2
    scene.render.bake.use_selected_to_active = False
    bpy.ops.object.bake(type='EMIT', margin=8)
    destination = Path(texture_dir) / 'operator' / 'operator_restored_bra_albedo.png'
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.filepath_raw = str(destination); image.file_format = 'PNG'; image.save(); image.pack()
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = .72
    bsdf.inputs['Specular IOR Level'].default_value = .27
    texture = nodes.new('ShaderNodeTexImage'); texture.image = image; texture.interpolation = 'Linear'
    links.new(texture.outputs['Color'], bsdf.inputs['Base Color'])
    links.new(bsdf.outputs[0], output.inputs['Surface'])
    scene.render.engine = prior_engine
    scene.cycles.samples = prior_samples
    scene.render.threads_mode, scene.render.threads = prior_threads
    obj['garment_texture'] = str(destination)
    return str(destination)


def clean_bodice_degenerate_faces(parts, area_epsilon=1e-10):
    """Resolve collapsed local support faces without moving or reweighting skin.

    Source strap sheets can collapse when fitted to the continuous skin below
    them. Split only ambiguous tiny polygons first, preserving any real-area
    triangles, then discard the truly collapsed skin triangles. Closed garment
    piping is triangulated in place, so its shell keeps every boundary edge.
    BMesh preserves the surviving loop UVs and per-vertex deform weights.
    """
    report = {}
    for obj in parts:
        part = obj.get('authored_part')
        if part not in {'body', 'bra'}:
            continue
        bm = bmesh.new(); bm.from_mesh(obj.data)
        bad = [face for face in bm.faces if face.calc_area() < area_epsilon]
        before = len(bad)
        closed_before = all(edge.is_manifold for edge in bm.edges)
        ambiguous = [face for face in bad if len(face.verts) > 3]
        if ambiguous:
            bmesh.ops.triangulate(bm, faces=ambiguous, quad_method='BEAUTY', ngon_method='BEAUTY')
        collapsed = [face for face in bm.faces if face.calc_area() < area_epsilon]
        removed = len(collapsed)
        if collapsed:
            if part == 'bra':
                bm.free()
                raise RuntimeError('Vivian closed bra still contains collapsed triangles')
            bmesh.ops.delete(bm, geom=collapsed, context='FACES')
        loose = [vertex for vertex in bm.verts if not vertex.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        remaining = sum(face.calc_area() < area_epsilon for face in bm.faces)
        if remaining or (part == 'bra' and closed_before and any(not edge.is_manifold for edge in bm.edges)):
            bm.free()
            raise RuntimeError('Vivian local face cleanup failed integrity check')
        bm.to_mesh(obj.data); bm.free(); obj.data.update()
        report[part] = dict(degenerate_faces_before=before,
                            triangulated_polygons=len(ambiguous),
                            removed_collapsed_triangles=removed,
                            degenerate_faces_after=remaining)
    return report


def add_vivian_bodice(parts, armature, texture_dir):
    """Return ``(updated_parts, report)``; replace only the extracted bra mesh.

    The face, hair and jewelry are untouched. Body completion stays within the
    original neckline/strap footprint, and the dress front fits the improved
    breast form. Source bone weights and independently editable materials stay
    compatible with the character's existing armature.
    """
    source = next((o for o in parts if o.get('authored_part') == 'bra'), None)
    if source is None:
        return list(parts), {'rebuilt': False, 'reason': 'no separate authored bra mesh'}
    reference = _Reference(source)
    builder = _Builder(reference)
    anchors = _build_cups(builder, reference)
    _build_straps(builder, reference, anchors)
    obj = builder.finish(source, armature)
    body = next((o for o in parts if o.get('authored_part') == 'body'), None)
    dress = next((o for o in parts if o.get('authored_part') == 'dress'), None)
    restored_body_vertices = _restore_body_underlay(source, body, reference, dress) if body is not None else 0
    _fit_neckline(parts)
    cleanup = clean_bodice_degenerate_faces([o for o in (body, obj) if o is not None])
    texture = _bake_clean_fabric(obj, texture_dir)
    before = len(source.data.vertices)
    name = source.name
    updated = [obj if part == source else part for part in parts]
    bpy.data.objects.remove(source, do_unlink=True)
    obj.name = name
    obj.data.calc_loop_triangles()
    report = {
        'rebuilt': True,
        'source_vertices': before,
        'vertices': len(obj.data.vertices),
        'triangles': len(obj.data.loop_triangles),
        'object': obj.name,
        'material': [m.name for m in obj.data.materials],
        'shell_thickness_m': .0014,
        'construction': 'source-fitted quad cup panels with modeled neckline binding and shoulder ribbons',
        'uv': 'independent garment UV with baked charcoal textile; no crossed source charts',
        'texture': texture,
        'body_underlay_vertices': restored_body_vertices,
        'anatomy': 'original authored cup envelope/depth retained; no primitive spherical breast replacement',
        'animation_changed': False,
        'local_geometry_cleanup': cleanup,
    }
    return updated, report
