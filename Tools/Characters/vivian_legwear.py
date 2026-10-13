"""Clean, source-fitted stockings and real garter ribbons for Vivian.

Call after vivian_body_finish.finish_vivian_body.  Lower-leg sections are taken
from the union of the authored stocking and misclassified garter surfaces.
The completed thigh profile supplies the shared cuff/skin fit.  Neither shoes,
dress, torso, nor armature are changed.
"""
from pathlib import Path
from math import sin, cos, pi, sqrt
import json

import bpy
import bmesh
import numpy as np
from mathutils import Vector

from vivian_bodice import _Reference


def _section_segments(objects, height, side):
    segments = []
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        for triangle in mesh.loop_triangles:
            points = [mesh.vertices[i].co for i in triangle.vertices]
            if max(v.z for v in points) < height or min(v.z for v in points) > height:
                continue
            intersections = []
            for a, b in zip(points, points[1:] + points[:1]):
                if (a.z - height) * (b.z - height) < 0:
                    p = a + (b - a) * ((height - a.z) / (b.z - a.z))
                    intersections.append((float(p.x), float(p.y)))
            if len(intersections) == 2 and all(p[0] * side > 0 for p in intersections):
                segments.append(intersections)
    return segments


def _cross2(a, b):
    return a[0] * b[1] - a[1] * b[0]


def _source_ring(objects, height, side, count):
    segments = _section_segments(objects, height, side)
    if not segments:
        raise ValueError('Missing authored leg section at %.4f' % height)
    points = np.asarray([p for segment in segments for p in segment])
    low, high = points.min(axis=0), points.max(axis=0)
    center = (low + high) * .5
    extent = (high - low) * .5
    radii = []
    missing = 0
    for i in range(count):
        direction = np.array((cos(i * 2 * pi / count), sin(i * 2 * pi / count)))
        hits = []
        for a, b in segments:
            a, b = np.asarray(a), np.asarray(b)
            edge = b - a
            denominator = _cross2(direction, edge)
            if abs(denominator) < 1e-12:
                continue
            t = _cross2(a - center, edge) / denominator
            u = _cross2(a - center, direction) / denominator
            if t > 0 and -.00001 <= u <= 1.00001:
                hits.append(t)
        if hits:
            radii.append(max(hits))
        else:
            # The original is open at a few cloth partitions. Fill only that
            # missing arc between its two adjacent measured surface samples.
            radii.append(float('nan')); missing += 1
    radii = np.asarray(radii)
    good = np.where(np.isfinite(radii))[0]
    if len(good) < count // 2:
        raise ValueError('Authored leg section is too incomplete at %.4f' % height)
    radii = np.interp(np.arange(count), np.r_[good-count, good, good+count], np.tile(radii[good], 3))
    # Tiny local relaxation removes polygon corners while preserving the
    # measured calf, knee and ankle envelope to within a millimetre.
    smoothed = radii * .8 + (np.roll(radii, 1) + np.roll(radii, -1)) * .1
    smoothed = np.clip(smoothed, radii - .0008, radii + .0008)
    ring = [(float(center[0]+smoothed[i]*cos(i*2*pi/count)),
             float(center[1]+smoothed[i]*sin(i*2*pi/count)), height) for i in range(count)]
    return ring, missing


class _Mesh:
    def __init__(self, reference):
        self.reference = reference
        self.points = []
        self.weights = []
        self.faces = []
        self.face_uvs = []

    def vertex(self, point, weights=None):
        self.points.append(tuple(point))
        self.weights.append(weights if weights is not None else self.reference.sample(Vector(point))[1])
        return len(self.points)-1

    def face(self, vertices, uvs):
        self.faces.append(tuple(vertices)); self.face_uvs.append(tuple(uvs))

    def ring_surface(self, rings, side, v_values):
        count = len(rings[0]); offset = len(self.points)
        for ring in rings:
            for point in ring:
                self.vertex(point)
        for row in range(len(rings)-1):
            for j in range(count):
                k = (j+1) % count
                a, b = offset+row*count+j, offset+row*count+k
                c, d = offset+(row+1)*count+k, offset+(row+1)*count+j
                # Each leg has its own half of the editable textile UV.
                start = 0 if side < 0 else .5
                u0 = start + .5*j/count; u1 = start + .5*(j+1)/count
                self.face((a, b, c, d), ((u0, v_values[row]), (u1, v_values[row]),
                                        (u1, v_values[row+1]), (u0, v_values[row+1])))

    def finish(self, source, armature):
        mesh = bpy.data.meshes.new(source.data.name+'_Finished')
        mesh.from_pydata(self.points, [], self.faces); mesh.update()
        obj = bpy.data.objects.new(source.name+'_Finished', mesh)
        for collection in source.users_collection:
            collection.objects.link(obj)
        obj.matrix_world = source.matrix_world.copy(); obj.parent = armature
        for key in source.keys():
            obj[key] = source[key]
        uv = mesh.uv_layers.new(name='GarmentUV')
        for face, values in zip(mesh.polygons, self.face_uvs):
            face.use_smooth = True
            for index, value in zip(face.loop_indices, values):
                uv.data[index].uv = value
        groups = {}
        for index, weights in enumerate(self.weights):
            for name, amount in weights.items():
                if name not in groups:
                    groups[name] = obj.vertex_groups.new(name=name)
                groups[name].add([index], amount, 'REPLACE')
        modifier = obj.modifiers.new('Authored skin', 'ARMATURE'); modifier.object = armature
        bm = bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh); bm.free(); mesh.update()
        return obj


def _texture(obj, source, label, texture_dir=None):
    size = 512 if label == 'stockings' else 128
    yy, xx = np.mgrid[0:size, 0:size]
    u, v = xx/(size-1), yy/(size-1)
    weave = np.sin(xx*pi)*.001 + np.cos(yy*2.37+xx*.41)*.0015
    if label == 'stockings':
        # A restrained charcoal knit, without source anatomy/shadow triangles.
        base = .175 + .003*np.cos(u*4*pi) + weave
        band_start = (.862-.154)/(.892-.154)
        band = np.clip((v-band_start)*300, 0, 1)
        base = base*(1-band) + (.103+weave)*band
        stitch = (np.abs(v-(.888-.154)/(.892-.154)) < .0015)
        base = np.where(stitch, base+.012, base)
    else:
        border = (u < .10) | (u > .90)
        base = .087 + weave + border*.012
    pixels = np.ones((size, size, 4), dtype=np.float32)
    pixels[:, :, :3] = np.stack((base*1.025, base, base*1.045), axis=-1)
    if texture_dir is None:
        old = source.get('garment_texture') or source.get('texture_path')
        if not old:
            images = [n.image for m in source.data.materials if m and m.use_nodes
                      for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image]
            old = bpy.path.abspath(images[0].filepath) if images else None
        if old:
            destination = Path(old).parent / ('operator_restored_'+label+'_albedo.png')
        else:
            destination = Path(__file__).resolve().parents[2]/'Assets/Characters/Textures/Restored/operator'/('operator_restored_'+label+'_albedo.png')
    else:
        destination = Path(texture_dir)/'operator'/('operator_restored_'+label+'_albedo.png')
    destination.parent.mkdir(parents=True, exist_ok=True)
    image = bpy.data.images.new('operator_restored_'+label+'_clean_fabric', width=size, height=size, alpha=False)
    image.pixels.foreach_set(pixels.ravel()); image.filepath_raw = str(destination)
    image.file_format = 'PNG'; image.save(); image.pack()
    material = bpy.data.materials.get('operator_restored_'+label) or bpy.data.materials.new('operator_restored_'+label)
    material.use_nodes = True; material.node_tree.nodes.clear()
    nodes, links = material.node_tree.nodes, material.node_tree.links
    output = nodes.new('ShaderNodeOutputMaterial'); bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = .76 if label == 'stockings' else .70
    bsdf.inputs['Specular IOR Level'].default_value = .24
    texture = nodes.new('ShaderNodeTexImage'); texture.image = image
    links.new(texture.outputs['Color'], bsdf.inputs['Base Color']); links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    obj.data.materials.append(material); obj['garment_texture'] = str(destination)
    return str(destination)


def _fitted_ring(profile, height, side, count, ease=.0008):
    cx, cy, rx, ry = profile(height, side)
    return [(cx+(rx+ease)*cos(j*2*pi/count), cy+(ry+ease)*sin(j*2*pi/count), height) for j in range(count)]


def _stockings(builder, sources, profile):
    count = 40
    levels = sorted(set([.1543, .16, .175] + list(np.linspace(.19, .835, 32)) +
                        [.845, .852, .860, .862, .866, .874, .882, .888, .891, .892]))
    report = {'section_samples': 0, 'missing_source_rays': 0, 'max_lower_radial_relaxation_m': .0008}
    for side in (-1, 1):
        rings = []
        for z in levels:
            if z < .865:
                # The stocking/shoe split cuts through the lowest ankle arc.
                # Extend the first complete ankle section a short distance
                # inside the untouched shoe instead of fitting that open arc.
                measured, missing = _source_ring(sources, max(.18, z), side, count)
                if z < .18:
                    center_x = sum(p[0] for p in measured)/count
                    center_y = sum(p[1] for p in measured)/count
                    shrink = 1-.05*(.18-z)/(.18-.1543)
                    measured = [(center_x+(x-center_x)*shrink, center_y+(y-center_y)*shrink, z)
                                for x,y,_ in measured]
                report['missing_source_rays'] += missing; report['section_samples'] += count
            else:
                measured = _fitted_ring(profile, z, side, count)
            if z >= .825:
                fitted = _fitted_ring(profile, z, side, count)
                # Complete the shared profile below the first Body ring, so
                # even its bottom cap stays entirely inside the stocking.
                t = min(1.0, max(0.0, (z-.825)/.025)); t = t*t*(3-2*t)
                measured = [tuple((1-t)*a+t*b for a,b in zip(m,f)) for m,f in zip(measured,fitted)]
            if z >= .862:
                # Rounded, 1 mm raised elastic cuff. Its lower edge grows out
                # of the stocking surface; the top turns inward over the skin.
                raised = .0011*min(1, max(0, (z-.862)/.004))
                cx, cy, _, _ = profile(z, side)
                measured = [(x+raised*cos(j*2*pi/count), y+raised*sin(j*2*pi/count), z)
                            for j,(x,y,z) in enumerate(measured)]
            rings.append(measured)
        # A proper turned top edge, rather than an open razor-thin boundary.
        inner = _fitted_ring(profile, .8915, side, count, ease=.00025)
        rings.append(inner)
        rings.append(_fitted_ring(profile, .884, side, count, ease=.00025))
        values = [(z-.154)/(.892-.154) for z in levels] + [.998, .992]
        builder.ring_surface(rings, side, values)
    return report


def _straps(builder, profile):
    count = 0
    for side in (-1, 1):
        for angle in (-pi/2+side*.36, pi/2-side*.36):
            offset = len(builder.points); rows = 15; columns = 3
            for layer in (0, 1):
                for row in range(rows):
                    z = .884 + row/(rows-1)*.173
                    cx, cy, rx, ry = profile(z, side)
                    length = sqrt((rx*sin(angle))**2 + (ry*cos(angle))**2)
                    half_angle = .0045/max(.035, length)
                    for col in range(columns):
                        theta = angle+(col/(columns-1)*2-1)*half_angle
                        ease = .0024 if layer == 0 else .0011
                        point = (cx+(rx+ease)*cos(theta), cy+(ry+ease)*sin(theta), z)
                        # Shared thigh weighting follows the cleaned skin.
                        bone = 'thigh.L' if side > 0 else 'thigh.R'
                        pelvis = max(0, min(.20, (z-1.0)*3.5))
                        builder.vertex(point, {bone: 1-pelvis, 'pelvis': pelvis} if pelvis else {bone: 1})
            sheet = rows*columns
            for row in range(rows-1):
                for col in range(columns-1):
                    i = offset+row*columns+col
                    uv = ((col/(columns-1), row/(rows-1)), ((col+1)/(columns-1), row/(rows-1)),
                          ((col+1)/(columns-1), (row+1)/(rows-1)), (col/(columns-1), (row+1)/(rows-1)))
                    builder.face((i, i+1, i+columns+1, i+columns), uv)
                    builder.face((i+sheet, i+sheet+columns, i+sheet+columns+1, i+sheet+1), (uv[0],uv[3],uv[2],uv[1]))
            boundary = (list(range(columns)) + [row*columns+columns-1 for row in range(1, rows)] +
                        [(rows-1)*columns+col for col in range(columns-2, -1, -1)] +
                        [row*columns for row in range(rows-2, 0, -1)])
            for a, b in zip(boundary, boundary[1:]+boundary[:1]):
                builder.face((offset+a,offset+a+sheet,offset+b+sheet,offset+b),
                             ((.01,.01),(.01,.99),(.05,.99),(.05,.01)))
            count += 1
    return count


def finish_vivian_legwear(parts, arm, texture_dir=None):
    """Return ``(updated_parts, report)`` with only stockings/garters replaced.

    The optional texture_dir is for audit output isolation. Production defaults
    to the existing per-part texture locations. Call after the body finish.
    """
    from vivian_body_finish import thigh_profile
    by_part = {obj.get('authored_part'): obj for obj in parts}
    stockings, garters = by_part.get('stockings'), by_part.get('garters')
    if stockings is None or garters is None:
        return list(parts), {'rebuilt': False, 'reason': 'required authored legwear absent'}
    if stockings.get('vivian_legwear_finish'):
        return list(parts), json.loads(stockings['vivian_legwear_finish'])
    reference = _Reference(stockings)
    stocking_builder = _Mesh(reference)
    geometry = _stockings(stocking_builder, [stockings, garters], thigh_profile)
    strap_builder = _Mesh(_Reference(by_part['body']))
    strap_count = _straps(strap_builder, thigh_profile)
    new_stockings = stocking_builder.finish(stockings, arm)
    new_garters = strap_builder.finish(garters, arm)
    textures = {'stockings': _texture(new_stockings, stockings, 'stockings', texture_dir),
                'garters': _texture(new_garters, garters, 'garters', texture_dir)}
    replacements = {stockings: new_stockings, garters: new_garters}
    updated = [replacements.get(obj, obj) for obj in parts]
    for source, target in replacements.items():
        name = source.name; bpy.data.objects.remove(source, do_unlink=True); target.name = name
        target.data.name = name+'_Mesh'
    report = {'rebuilt': True, 'stocking_top_m': .892, 'cuff_height_m': .030,
              'cuff_relief_m': .0011, 'garter_straps': strap_count,
              'garter_width_m': .009, 'garter_thickness_m': .0013,
              'textures': textures, 'source_surface_fit': geometry,
              'vertices': {obj['authored_part']: len(obj.data.vertices) for obj in replacements.values()},
              'construction': 'source-section retopology, continuous elastic cuffs, closed fitted garter ribbons',
              'animation_changed': False}
    new_stockings['vivian_legwear_finish'] = json.dumps(report)
    new_garters['vivian_legwear_finish'] = json.dumps(report)
    return updated, report
