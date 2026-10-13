"""Review-only surface repairs on the saved, recognizable authored cast.

Apply before fit_individual_rigs. No production path is written by this module.
``refine_character_surfaces(parts, arm, role, output_dir)`` returns parts/audit.
Hazel's garment assignment uses connected arm geometry and the wrist plane,
never skin-colour tests: the ochre bands are intentional fabric decoration.
"""
from pathlib import Path
from collections import defaultdict
import math
import json

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]


def _activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def _image(obj):
    return next(n.image for n in obj.data.materials[0].node_tree.nodes
                if n.type == 'TEX_IMAGE' and n.image and n.outputs[0].is_linked)


def _save_pixels(name, pixels, folder):
    folder = Path(folder); folder.mkdir(parents=True, exist_ok=True)
    height, width = pixels.shape[:2]
    image = bpy.data.images.new(name, width=width, height=height, alpha=True)
    image.pixels.foreach_set(np.asarray(pixels, dtype=np.float32).ravel())
    image.filepath_raw = str(folder/(name+'.png')); image.file_format = 'PNG'; image.save()
    data = Path(image.filepath_raw).read_bytes(); image.pack(data=data, data_len=len(data))
    return image


def _isolate_materials(parts, folder):
    """Every candidate's editable images live beside it, including unchanged maps."""
    images = {}; materials = {}
    for obj in parts:
        for i, original in enumerate(list(obj.data.materials)):
            if original not in materials:
                mat = original.copy(); mat.name = original.name+'_surface_review'
                for node in mat.node_tree.nodes:
                    if node.type != 'TEX_IMAGE' or not node.image:
                        continue
                    source = node.image
                    if source not in images:
                        data = np.empty(source.size[0]*source.size[1]*4, dtype=np.float32)
                        source.pixels.foreach_get(data)
                        images[source] = _save_pixels(source.name.split('.')[0]+'_review',
                            data.reshape(source.size[1], source.size[0], 4), folder)
                    node.image = images[source]
                materials[original] = mat
            obj.data.materials[i] = materials[original]


def _drop_faces(obj, ids):
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in ids], context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(obj.data); bm.free(); obj.data.update()


def _join(target, others):
    if not others:
        return target
    _activate(target)
    for obj in others:
        obj.select_set(True)
    bpy.ops.object.join()
    return target


def _transfer(source, destination, ids):
    if not ids:
        return 0
    import modular_outfits
    copy = modular_outfits._separate_faces(source, ids, source.name+'_recovered_fabric')
    _drop_faces(source, ids); _join(destination, [copy])
    return len(ids)


def _strip_returns(obj):
    """Remove the earlier artificial UV-collapsed hems before joining real cloth."""
    uv = obj.data.uv_layers.get('SourceAtlasUV')
    if uv is None:
        return 0
    ids = [face.index for face in obj.data.polygons
           if len({tuple(round(x, 7) for x in uv.data[i].uv) for i in face.loop_indices}) <= 2]
    _drop_faces(obj, ids)
    return len(ids)


def _repack_original(obj, role, label, folder):
    import modular_outfits as outfits
    source = bpy.data.images.load(str(ROOT/'Art/Characters/Imported'/(role+'_source_albedo.png')), check_existing=True)
    mesh = obj.data; original = mesh.uv_layers.get('SourceAtlasUV')
    if original is None:
        raise ValueError('Recovering fabric requires preserved original UVs: '+obj.name)
    coordinates = [tuple(loop.uv) for loop in original.data]
    while len(mesh.uv_layers) > 1:
        mesh.uv_layers.remove(mesh.uv_layers[-1])
    mesh.uv_layers[0].name = 'RecoveredSourceUV'
    for loop, uv in zip(mesh.uv_layers[0].data, coordinates):
        loop.uv = uv
    mesh.uv_layers.active_index = 0
    # Deliberately use the semantic review label: no colour-based cleanup runs.
    density=.85; limit=1024*1024 if 'sleeves' in label else 512*512
    while True:
        charts=outfits._charts(obj,source,density)
        width,height,_=outfits._pack_rectangles([c['rect'] for c in charts])
        if width*height<=limit or density<.10:break
        density*=.80
    return outfits._repack_texture(obj, source, role, label,
        Path(folder)/(role+'_'+label+'_albedo.png'), density=density, charts=charts)


def _new_mesh(name, vertices, faces, uvs, weights, arm, material):
    mesh = bpy.data.meshes.new(name+'_Mesh'); mesh.from_pydata(vertices, [], faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); bpy.context.collection.objects.link(obj); obj.parent = arm
    uv = mesh.uv_layers.new(name='GarmentUV')
    for face in mesh.polygons:
        for index in face.loop_indices:
            uv.data[index].uv = uvs[mesh.loops[index].vertex_index]
        face.use_smooth = True
    for bone in arm.data.bones:
        obj.vertex_groups.new(name=bone.name)
    for i, values in enumerate(weights):
        for name, weight in values.items():
            obj.vertex_groups[name].add([i], weight, 'REPLACE')
    mod = obj.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
    obj.data.materials.append(material)
    return obj


def _hazel_inner_arms(body, arm, folder):
    """An actual continuous, hidden skin surface beneath each continuous sleeve."""
    import modular_outfits as outfits
    pixels = np.ones((128, 64, 4), dtype=np.float32)
    for y in range(128):
        shade = .98 + .02*math.sin(math.pi*y/127)
        pixels[y, :, :3] = np.array((.79, .45, .052))*shade
    image = _save_pixels('dispatcher_review_arm_skin', pixels, folder)
    material = outfits._image_material('dispatcher', 'inner_arm_skin', image)
    vertices = []; faces = []; uvs = []; weights = []
    for side in ('L', 'R'):
        upper = arm.data.bones['upper_arm.'+side]; lower = arm.data.bones['forearm.'+side]
        shoulder, elbow, wrist = upper.head_local, upper.tail_local, lower.tail_local
        axis = (wrist-shoulder).normalized(); transverse = Vector((0, 1, 0)); tangent = axis.cross(transverse).normalized()
        start = len(vertices); segments = 16; rings = 19
        for row in range(rings):
            t = row/(rings-1)
            point = shoulder.lerp(elbow, t*2) if t <= .5 else elbow.lerp(wrist, (t-.5)*2)
            radius = .024*(1-t)+.013*t
            blend = min(1, max(0, (t-.41)/.18))
            binding = {'upper_arm.'+side: 1-blend, 'forearm.'+side: blend}
            binding = {key: value for key, value in binding.items() if value > 1e-6}
            for column in range(segments):
                angle = 2*math.pi*column/segments
                vertices.append(tuple(point+radius*(math.cos(angle)*transverse+math.sin(angle)*tangent)))
                uvs.append((column/(segments-1), t)); weights.append(binding)
        for row in range(rings-1):
            for column in range(segments):
                a=start+row*segments+column; b=start+row*segments+(column+1)%segments
                faces.append((a,b,b+segments,a+segments))
        faces.append(tuple(start+i for i in reversed(range(segments))))
        faces.append(tuple(start+(rings-1)*segments+i for i in range(segments)))
    patch = _new_mesh('Hazel continuous skin under sleeves',vertices,faces,uvs,weights,arm,material)
    _join(body,[patch])
    return {'vertices':len(vertices),'quads':len(faces)-4,'skin_exists_beneath_fabric':True}


def _hazel(parts, arm, folder):
    import modular_outfits as outfits
    body = next(p for p in parts if p.get('authored_part') == 'body')
    top = next(p for p in parts if p.get('authored_part') == 'cropped_top')
    sleeves = next(p for p in parts if p.get('authored_part') == 'sleeves')
    stripped = _strip_returns(top)+_strip_returns(sleeves)
    # Connected narrow arm strips are unequivocal upper-arm fabric; their
    # alternating gold/pink paint must not create garment/skin boundaries.
    import restore_authored_cast as restore
    components = restore.components(body)
    fabric_vertices = set()
    for component in components:
        positions = [body.data.vertices[i].co for i in component]
        if (min(abs(p.x) for p in positions) > .175 and max(abs(p.x) for p in positions) < .335
                and min(p.z for p in positions) > 1.16 and max(p.z for p in positions) > 1.48):
            fabric_vertices.update(component)
    fabric = [face.index for face in body.data.polygons if set(face.vertices) <= fabric_vertices]
    restored = _transfer(body, sleeves, fabric)
    top_ids = [face.index for face in top.data.polygons
               if abs(face.center.x) > .17 and face.center.z > 1.24]
    restored += _transfer(top, sleeves, top_ids)
    # The gold strip reaching into the bell cuff is fabric too. Its wrist-plane
    # location distinguishes it from the separate long fingers below the cuff.
    ids = []
    for face in body.data.polygons:
        point = face.center; side = 'L' if point.x > 0 else 'R'
        bone = arm.data.bones['forearm.'+side]; axis = (bone.tail_local-bone.head_local).normalized()
        proximal = (point-bone.tail_local).dot(axis)
        if abs(point.x) > .335 and 1.20 < point.z < 1.28 and proximal < -.020:
            ids.append(face.index)
    restored += _transfer(body,sleeves,ids)
    bm = bmesh.new(); bm.from_mesh(sleeves.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00003)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces)); bm.to_mesh(sleeves.data); bm.free()
    for face in sleeves.data.polygons:
        face.use_smooth = True
    texture = _repack_original(sleeves,'dispatcher','continuous_patterned_sleeves',folder)
    top_texture = _repack_original(top,'dispatcher','original_patterned_top',folder)
    inner = _hazel_inner_arms(body, arm, folder)
    hems = outfits._cloth_edge_returns(sleeves, .0012)
    top_hems = outfits._cloth_edge_returns(top,.0012)
    return {'faces_recovered_as_fabric':restored,'colour_split_hems_removed':stripped,
            'sleeve_texture':texture,'top_texture':top_texture,'sleeve_hem':hems,
            'top_hem':top_hems,'inner_arms':inner,'colour_used_for_segmentation':False}


def _smooth_region(obj, predicate, iterations=3, factor=.28, maximum=.002, lock_boundary=False):
    bm=bmesh.new();bm.from_mesh(obj.data)
    region=[v for v in bm.verts if predicate(v.co) and not (lock_boundary and v.is_boundary)]; original={v:v.co.copy() for v in region}
    for _ in range(iterations):
        targets={}
        for vertex in region:
            neighbors=[edge.other_vert(vertex) for edge in vertex.link_edges]
            if neighbors:
                mean=sum((v.co for v in neighbors),Vector())/len(neighbors)
                target=vertex.co.lerp(mean,factor); delta=target-original[vertex]
                targets[vertex]=original[vertex]+delta.normalized()*min(maximum,delta.length) if delta.length else target
        for vertex,target in targets.items():vertex.co=target
    bm.normal_update();bm.to_mesh(obj.data);bm.free()
    return len(region)


def _finish_garment_edges(parts, role):
    """Round millimetre-scale saw teeth, preserving paired body/cloth seams."""
    import modular_outfits as outfits
    cloth={'dress','shirt','jacket','trousers','tie','cap','cropped_top','sleeves','wrap_skirt'}
    copies=defaultdict(list)
    def key(co):return tuple(round(value,6) for value in co)
    # This coordinate map also keeps the adjoining skin vertex on its fabric
    # seam; a smooth edge must not create a new gap in the adjacent surface.
    for obj in parts:
        for vertex in obj.data.vertices:copies[key(vertex.co)].append((obj,vertex.index))
    report={}
    for obj in parts:
        label=obj.get('authored_part')
        if label not in cloth or (role=='dispatcher' and label in {'cropped_top','sleeves'}):continue
        stripped=_strip_returns(obj)
        # Rebuild the map for this mesh after removing old return vertices.
        bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
        adjacency=defaultdict(list)
        for edge in bm.edges:
            if edge.is_boundary:
                a,b=edge.verts;adjacency[a].append(b);adjacency[b].append(a)
        moves=[]
        for vertex,neighbors in adjacency.items():
            if len(neighbors)!=2:continue
            a,b=neighbors
            if min((a.co-vertex.co).length,(b.co-vertex.co).length)>.022:continue
            delta=(a.co+b.co)*.5-vertex.co
            # Only small edge irregularities: retain large purposeful lapel,
            # asymmetrical skirt and pointed cuff silhouettes.
            if .0014<delta.length<.012:
                move=delta.normalized()*min(.0013,delta.length*.25)
                moves.append((vertex.index,key(vertex.co),vertex.co+move))
        for index,previous,target in moves:bm.verts[index].co=target
        bm.normal_update();bm.to_mesh(obj.data);bm.free()
        for _,previous,target in moves:
            for other,index in copies.get(previous,[]):
                if other != obj and index<len(other.data.vertices) and key(other.data.vertices[index].co)==previous:
                    other.data.vertices[index].co=target
        hems=outfits._cloth_edge_returns(obj,.0012)
        report[label]={'old_return_faces_removed':stripped,'edge_vertices_rounded':len(moves),
                       'max_adjustment_m':.0013 if moves else 0,**hems}
    return report


def _surface_tree(body):
    return BVHTree.FromPolygons([v.co for v in body.data.vertices], [tuple(f.vertices) for f in body.data.polygons])


def _front(tree,x,z,fallback):
    hit=tree.ray_cast(Vector((x,-2,z)),Vector((0,1,0)),3)[0]
    return hit.y if hit is not None else fallback


def _face_palette(role,folder):
    skin = np.array((.79,.56,.49) if role=='operator' else (.76,.55,.46))
    image=np.ones((256,256,4),dtype=np.float32);image[:,:,:3]=skin
    image[:128,:128,:3]=(.13,.074,.066) if role=='operator' else (.16,.105,.081)
    image[:128,128:,:3]=skin
    image[128:,128:,:3]=(.29,.071,.105) if role=='operator' else (.57,.30,.24)
    width,height=(.019,.0044) if role=='operator' else (.014,.0028)
    iris_radius=.0058 if role=='operator' else .0042
    iris_color=np.array((.20,.34,.34) if role=='operator' else (.28,.33,.27))
    for row in range(128):
        for col in range(128):
            x=(col/127-.5)*2*width;z=(row/127-.5)*2*height
            radius=math.hypot(x,z)/iris_radius;angle=math.atan2(z,x)
            colour=np.array((.62,.63,.59))
            if radius < 1:
                colour=iris_color*(.70+.28*radius+.10*math.sin(angle*31+radius*19))
                if radius>.85:colour*=.45
                if radius<.40:colour=np.array((.018,.022,.021))
                if ((x+iris_radius*.27)**2+(z-iris_radius*.30)**2)<(iris_radius*.15)**2:colour=np.array((.84,.88,.86))
            image[128+row,col,:3]=colour
    import modular_outfits as outfits
    tex=_save_pixels(role+'_modeled_face_details',image,folder)
    return outfits._image_material(role,'modeled_face_details',tex)


def _modeled_eyes(body,arm,role,folder):
    """Conformal almond lenses, continuous lid loops and brow strips."""
    tree=_surface_tree(body); vertices=[];faces=[];uvs=[];weights=[]
    eye_z=1.722 if role=='operator' else 1.655
    half_separation=.038 if role=='operator' else .030
    width,height=(.019,.0044) if role=='operator' else (.014,.0028)
    fallback=-.085 if role=='operator' else -.14
    def add(co,uv):
        vertices.append(tuple(co));uvs.append(uv);weights.append({'head':1.0});return len(vertices)-1
    for side in (-1,1):
        cx=side*half_separation;segments=32;rows=[]
        for radius in (.08,.32,.62,.86,1.0,1.12,1.25):
            ring=[]
            for col in range(segments):
                a=2*math.pi*col/segments
                x=cx+width*radius*math.cos(a)
                z=eye_z+height*radius*math.sin(a)+side*(x-cx)*.085
                y=_front(tree,x,z,fallback)-(.0020*(1-min(radius,1)**2)+.0006)
                if radius>1:y-=.0004*math.sin((radius-1)*math.pi/.25)
                uv=((x-cx)/(2*width)*.496+.25,(z-eye_z-side*(x-cx)*.085)/(2*height)*.496+.75) if radius<=1 else (.52+col/segments*.46,.06+(radius-1)*1.6)
                ring.append(add((x,y,z),uv))
            rows.append(ring)
        faces.append(tuple(rows[0]))
        for a,b in zip(rows,rows[1:]):
            for col in range(segments):
                nxt=(col+1)%segments;faces.append((a[col],b[col],b[nxt],a[nxt]))
        # Upper lash margin: a small modeled rim, separate from the skin loop.
        strips=[]
        for row in range(3):
            strip=[]
            for col in range(21):
                t=col/20;x=cx+width*(2*t-1);z=eye_z+height*math.sin(math.pi*t)+side*(x-cx)*.085+row*.0006
                y=_front(tree,x,z,fallback)-.0012
                strip.append(add((x,y,z),(.02+t*.45,.03+row*.06)))
            strips.append(strip)
        for a,b in zip(strips,strips[1:]):
            for i in range(20):faces.append((a[i],a[i+1],b[i+1],b[i]))
        if role=='operator':
            brow=[]
            for row in range(3):
                ring=[]
                for col in range(17):
                    t=col/16;x=cx+width*1.02*(2*t-1);z=eye_z+.015+.004*math.sin(math.pi*t)+row*.0011
                    y=_front(tree,x,z,fallback)-.0010
                    ring.append(add((x,y,z),(.02+t*.45,.22+row*.07)))
                brow.append(ring)
            for a,b in zip(brow,brow[1:]):
                for i in range(16):faces.append((a[i],a[i+1],b[i+1],b[i]))
    material=_face_palette(role,folder)
    obj=_new_mesh(role+' modeled eyes and lids',vertices,faces,uvs,weights,arm,material)
    _join(body,[obj])
    return {'eye_centers_z':eye_z,'eye_centers_x':[-half_separation,half_separation],
            'vertices':len(vertices),'faces':len(faces),'eyelid_support_loops':3,'new_texture':[256,256]}


def _vivian_bob(parts,hair,arm,folder):
    """A continuous rounded bob, with the authored eye-covering silhouette.

    The imported hair object also contained two ear surfaces. Keep these on
    the body before replacing the faceted sheets; hair colour is never used
    to decide what belongs to the ears.
    """
    import restore_authored_cast as restore
    body=next(p for p in parts if p.get('authored_part')=='body')
    old_earring_vertices=set()
    for component in restore.components(body):
        points=[body.data.vertices[i].co for i in component]
        if (20<=len(component)<=400
                and min(abs(p.x) for p in points)>.058 and max(abs(p.x) for p in points)<.098
                and min(p.z for p in points)>1.595 and max(p.z for p in points)<1.75
                and max(p.z for p in points)-min(p.z for p in points)>.015
                and min(p.y for p in points)>-.08 and max(p.y for p in points)<.035):
            old_earring_vertices.update(component)
    old_earring_faces=[face.index for face in body.data.polygons if set(face.vertices)<=old_earring_vertices]
    _drop_faces(body,old_earring_faces)
    ear_vertices=set()
    for component in restore.components(hair):
        pts=[hair.data.vertices[i].co for i in component]
        if (80<=len(component)<=110 and min(p.z for p in pts)>1.62
                and max(p.z for p in pts)<1.75 and min(p.y for p in pts)>-.025):
            ear_vertices.update(component)
    ear_faces=[f.index for f in hair.data.polygons if set(f.vertices)<=ear_vertices]
    retained_ears=_transfer(hair,body,ear_faces)
    old_cards=[]
    for face in body.data.polygons:
        points=[body.data.vertices[i].co for i in face.vertices]
        if (min(abs(p.x) for p in points)>.06 and max(abs(p.x) for p in points)<.12
                and min(p.z for p in points)>1.535 and max(p.z for p in points)<1.84
                and max(p.z for p in points)-min(p.z for p in points)>.072
                and max(p.x for p in points)-min(p.x for p in points)<.036):
            old_cards.append(face.index)
    _drop_faces(body,old_cards)
    removed=len(hair.data.polygons)
    vertices=[];faces=[];uvs=[];weights=[];columns=65;rows=20
    for row in range(rows):
        t=row/(rows-1)
        for col in range(columns):
            u=col/(columns-1);angle=(u*2-1)*math.pi;side=abs(angle)
            transition=min(1,max(0,(side-.73)/.40))
            transition=transition*transition*(3-2*transition)
            lower=1.713*(1-transition)+1.556*transition
            lower+=.0012*math.cos(angle*18)*(1-transition)+.008*(math.sin(angle)**2)*transition
            z=lower*(1-t)+1.879*t
            radius=.124
            if z>1.755:radius*=math.sqrt(max(.0007,1-((z-1.755)/.125)**2))
            else:radius*=.96+.04*math.sin(math.pi*min(1,(z-1.55)/.205)*.5)
            x=radius*math.sin(angle)
            y=.006-radius*1.015*math.cos(angle)
            vertices.append((x,y,z));uvs.append((u,t));weights.append({'head':1})
    for row in range(rows-1):
        for col in range(columns-1):
            a=row*columns+col;faces.append((a,a+1,a+columns+1,a+columns))
    faces.append(tuple((rows-1)*columns+i for i in range(columns-1)))
    pixels=np.ones((256,256,4),dtype=np.float32)
    for x in range(256):
        u=x/255;strand=.027*math.sin(u*math.pi*47)+.012*math.sin(u*math.pi*93)
        for y in range(256):
            shade=.86+.06*(y/255)+strand
            pixels[y,x,:3]=np.array((.88,.84,.84))*shade
    import modular_outfits as outfits
    material=outfits._image_material('operator','shaped_bob_hair',_save_pixels('operator_shaped_bob_hair',pixels,folder))
    fringe=_new_mesh('Vivian continuous rounded bob',vertices,faces,uvs,weights,arm,material)
    _activate(fringe);solid=fringe.modifiers.new('Finished fringe edge','SOLIDIFY');solid.thickness=.0015;solid.offset=0
    bpy.ops.object.modifier_apply(modifier=solid.name)
    old_mesh=hair.data;new_mesh=fringe.data.copy()
    for group in list(hair.vertex_groups):hair.vertex_groups.remove(group)
    hair.data=new_mesh
    for group in fringe.vertex_groups:hair.vertex_groups.new(name=group.name)
    bpy.data.objects.remove(fringe,do_unlink=True)
    if old_mesh.users==0:bpy.data.meshes.remove(old_mesh)
    # The authored large gold earrings were painted on flat hair/skin sheets.
    # Give the same earrings an actual circular cross section and open centre.
    jewelry=next(p for p in parts if p.get('authored_part')=='jewelry')
    previous=[f.index for f in jewelry.data.polygons if f.center.z>1.5]
    _drop_faces(jewelry,previous)
    gold=np.ones((32,32,4),dtype=np.float32);gold[:,:,:3]=(.72,.43,.085)
    mat=outfits._image_material('operator','gold_earrings',_save_pixels('operator_gold_earrings',gold,folder))
    verts=[];polys=[];coords=[];bindings=[]
    for side in (-1,1):
        start=len(verts)
        for i in range(48):
            theta=i/48*2*math.pi
            for j in range(8):
                a=j/8*2*math.pi;tube=.0017
                verts.append((side*.083+(.012+tube*math.cos(a))*math.cos(theta),-.044+tube*math.sin(a),1.642+(.027+tube*math.cos(a))*math.sin(theta)))
                coords.append((i/47,j/7));bindings.append({'head':1})
        for i in range(48):
            for j in range(8):
                a=start+i*8+j;b=start+((i+1)%48)*8+j
                polys.append((a,b,start+((i+1)%48)*8+(j+1)%8,start+i*8+(j+1)%8))
    hoops=_new_mesh('Vivian gold hoop earrings',verts,polys,coords,bindings,arm,mat)
    _join(jewelry,[hoops])
    return {'removed_card_faces':removed,'old_flat_earring_card_faces_removed':len(old_cards),
            'old_body_earring_faces_removed':len(old_earring_faces),'ear_faces_preserved':retained_ears,
            'quad_bob_faces':len(faces)-1,'eye_covering_fringe_z':1.713,
            'thickness_m':.0015,'modeled_gold_earring_quads':len(polys)}


def _face_finish(parts,arm,role,folder):
    body=next(p for p in parts if p.get('authored_part')=='body')
    hair=next(p for p in parts if p.get('authored_part')=='hair')
    if role=='operator':
        moved=_smooth_region(body,lambda p:abs(p.x)<.085 and p.y<-.015 and 1.608<p.z<1.78,5,.26,.003)
        fringe=_vivian_bob(parts,hair,arm,folder)
    else:
        moved=_smooth_region(body,lambda p:abs(p.x)<.13 and p.y<.02 and 1.49<p.z<1.77,5,.28,.0035,lock_boundary=True)
        # This closed hair shell shares the scalp outline with the body. Its
        # silhouette is authored; independent smoothing opens their seam even
        # when BMesh reports no boundary because the shell has return faces.
        fringe={'original_scalp_outline_preserved':True}
    features=_modeled_eyes(body,arm,role,folder)
    for face in body.data.polygons:face.use_smooth=True
    for face in hair.data.polygons:face.use_smooth=True
    return {'facial_surface_vertices_refined':moved,'hair':fringe,'modeled_features':features}


def refine_character_surfaces(parts,arm,role,output_dir):
    """Return updated parts and audit; source and production assets are untouched."""
    out=Path(output_dir);out.mkdir(parents=True,exist_ok=True);texture_dir=out/'Textures'/role
    _isolate_materials(parts,texture_dir)
    report={'role':role,'original_likeness_retained':True,'production_written':False}
    if role=='dispatcher':report['garments']=_hazel(parts,arm,texture_dir)
    report['garment_edges']=_finish_garment_edges(parts,role)
    if role in {'operator','engineer'}:report['head']=_face_finish(parts,arm,role,texture_dir)
    images={node.image for obj in parts for mat in obj.data.materials if mat and mat.use_nodes
            for node in mat.node_tree.nodes if node.type=='TEX_IMAGE' and node.image and node.outputs[0].is_linked}
    resized=[]
    while sum(image.size[0]*image.size[1] for image in images)>2048*2048:
        image=max(images,key=lambda image:image.size[0]*image.size[1])
        before=list(image.size);image.scale(max(32,before[0]//2),max(32,before[1]//2))
        image.filepath_raw=str(texture_dir/(image.name.split('.')[0]+'.png'))
        image.file_format='PNG';image.save()
        data=Path(image.filepath_raw).read_bytes();image.pack(data=data,data_len=len(data))
        resized.append({'image':image.name,'before':before,'after':list(image.size)})
    bad_weights=0;max_influences=0;nonfinite=0
    for obj in parts:
        obj['surface_review_revision']=2
        for vertex in obj.data.vertices:
            values=[group.weight for group in vertex.groups if group.weight>1e-6]
            bad_weights+=int(not values or abs(sum(values)-1)>.002)
            max_influences=max(max_influences,len(values))
            nonfinite+=int(not all(math.isfinite(x) for x in vertex.co))
    report['audit']={'used_texture_pixels':sum(image.size[0]*image.size[1] for image in images),
                     'texture_budget_pixels':2048*2048,'resized_maps':resized,
                     'bad_weights':bad_weights,'max_influences':max_influences,
                     'nonfinite_vertices':nonfinite,
                     'triangles':sum(sum(len(face.vertices)-2 for face in obj.data.polygons) for obj in parts)}
    if bad_weights or max_influences>4 or nonfinite:raise RuntimeError(str(report['audit']))
    (out/(role+'_surface_changes.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    return parts,report
