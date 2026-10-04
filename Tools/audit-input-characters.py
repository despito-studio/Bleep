import bpy, json, pathlib, hashlib, math, struct, collections
from mathutils import Vector

OUT = pathlib.Path(r'D:\DeadAirUnityProject\Verification-Characters\Source')
OUT.mkdir(parents=True, exist_ok=True)
FILES = [pathlib.Path(r'D:\1') / n for n in ('1.glb', '2.glb', '3.fbx', '4.fbx')]

def vec(v): return [round(float(x), 6) for x in v]
def box(points):
    return {'min': [min(p[i] for p in points) for i in range(3)],
            'max': [max(p[i] for p in points) for i in range(3)]}
def bounds(o): return box([o.matrix_world @ Vector(p) for p in o.bound_box])

def components(o):
    me=o.data; parent=list(range(len(me.vertices)))
    def find(i):
        while parent[i]!=i: parent[i]=parent[parent[i]]; i=parent[i]
        return i
    # GLB flat normals/UV seams duplicate vertices; weld coincident positions
    # only in this diagnostic graph so the report identifies full characters.
    at_position={}
    for v in me.vertices:
        key=tuple(round(x,6) for x in v.co)
        if key in at_position:parent[find(v.index)]=find(at_position[key])
        else:at_position[key]=v.index
    for e in me.edges:
        a,b=(find(i) for i in e.vertices)
        if a!=b: parent[b]=a
    groups=collections.defaultdict(list)
    for v in me.vertices: groups[find(v.index)].append(v.index)
    face_groups=collections.defaultdict(list)
    for p in me.polygons: face_groups[find(p.vertices[0])].append(p.index)
    rows=[]
    for key,ids in groups.items():
        points=[o.matrix_world @ me.vertices[i].co for i in ids]; b=box(points)
        polys=face_groups[key]
        rows.append({'vertices':len(ids),'polygons':len(polys),'triangles':sum(len(me.polygons[p].vertices)-2 for p in polys),
            'bounds':b,'height':b['max'][2]-b['min'][2], '_ids':ids,'_faces':polys})
    return sorted(rows,key=lambda c:c['vertices'],reverse=True)

def mesh_hash(me):
    h=hashlib.sha256()
    for v in me.vertices: h.update(struct.pack('<3f',*v.co))
    for p in me.polygons: h.update(struct.pack('<I',len(p.vertices)));h.update(struct.pack('<'+'I'*len(p.vertices),*p.vertices))
    return h.hexdigest()

def glb_metadata(path):
    if path.suffix!='.glb':return None
    raw=path.read_bytes();pos=12
    while pos<len(raw):
        size,kind=struct.unpack_from('<II',raw,pos);pos+=8
        if kind==0x4e4f534a:
            d=json.loads(raw[pos:pos+size]);return {'generator':d.get('asset',{}).get('generator'),
                'nodes':[{'name':n.get('name'), 'mesh':n.get('mesh'),'skin':n.get('skin'),'children':n.get('children',[])} for n in d.get('nodes',[])],
                'mesh_count':len(d.get('meshes',[])), 'skins':d.get('skins',[]),
                'animation_names':[a.get('name') for a in d.get('animations',[])],
                'images':[{'name':i.get('name'),'mimeType':i.get('mimeType'),'bufferView':i.get('bufferView'),'uri':i.get('uri')} for i in d.get('images',[])]}
        pos+=size

def setup_render(meshes, path):
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE_NEXT';scene.render.resolution_x=1100;scene.render.resolution_y=850
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Audit neutral world');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.045,.053,.061,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
    scene.view_settings.view_transform='AgX'
    pts=[o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
    b=box(pts);lo=Vector(b['min']);hi=Vector(b['max']);center=(lo+hi)/2;size=hi-lo
    span=max(size.x,size.z,size.y*.7,.1)
    data=bpy.data.cameras.new('Audit camera');cam=bpy.data.objects.new('Audit camera',data);scene.collection.objects.link(cam)
    cam.location=center+Vector((span*2,span*.15,span*.12));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO';data.ortho_scale=max(size.z*1.3,size.y*1.05, .2);data.clip_end=max(100,span*10);scene.camera=cam
    for name,pos,energy in [('Key',(-1,-2,3),1300),('Fill',(2,-1,1),700),('Rim',(1,2,2),1100)]:
        ld=bpy.data.lights.new(name,'AREA');ld.energy=energy;ld.shape='DISK';ld.size=span*1.5
        light=bpy.data.objects.new(name,ld);scene.collection.objects.link(light);light.location=center+Vector(pos)*span
        light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

reports=[]
for path in FILES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    before=hashlib.sha256(path.read_bytes()).hexdigest()
    if path.suffix=='.glb':bpy.ops.import_scene.gltf(filepath=str(path))
    else:bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
    source_objects=list(bpy.context.scene.objects);meshes=[o for o in source_objects if o.type=='MESH']
    report={'file':str(path),'bytes':path.stat().st_size,'sha256':before,'glb':glb_metadata(path),
        'objects':[{'name':o.name,'type':o.type,'parent':o.parent.name if o.parent else None,'location':vec(o.location),'scale':vec(o.scale)} for o in source_objects],
        'meshes':[],'armatures':[],'materials':[],'images':[],'actions':[],'previews':[]}
    hashes=collections.defaultdict(list)
    variant_components=[]
    for o in meshes:
        me=o.data;me.calc_loop_triangles();cs=components(o);mh=mesh_hash(me);hashes[mh].append(o.name)
        report['meshes'].append({'object':o.name,'mesh':me.name,'vertices':len(me.vertices),'polygons':len(me.polygons),
            'triangles':len(me.loop_triangles),'edges':len(me.edges),'geometry_hash':mh,'bounds':bounds(o),'dimensions':vec(o.dimensions),
            'materials':[m.name if m else None for m in me.materials],'uv_layers':[u.name for u in me.uv_layers],
            'vertex_groups':[g.name for g in o.vertex_groups],'modifiers':[{'type':m.type,'name':m.name,'object':m.object.name if m.type=='ARMATURE' and m.object else None} for m in o.modifiers],
            'shape_keys':[k.name for k in me.shape_keys.key_blocks] if me.shape_keys else [],
            'connected_components':[{k:v for k,v in c.items() if not k.startswith('_')} for c in cs]})
        total_height=o.dimensions.z
        if len(cs)>1:
            for i,c in enumerate(cs):
                if c['height']>=total_height*.72 and c['vertices']>500:variant_components.append((o,i,c))
    report['exact_geometry_duplicates']=[names for names in hashes.values() if len(names)>1]
    for o in source_objects:
        if o.type=='ARMATURE':report['armatures'].append({'object':o.name,'bones':[b.name for b in o.data.bones], 'bone_count':len(o.data.bones)})
    for a in bpy.data.actions:report['actions'].append({'name':a.name,'frame_range':vec(a.frame_range)})
    for m in bpy.data.materials:
        ps=m.node_tree.nodes.get('Principled BSDF') if m.use_nodes else None
        report['materials'].append({'name':m.name,'base_color':vec(ps.inputs['Base Color'].default_value) if ps else vec(m.diffuse_color),
            'roughness':float(ps.inputs['Roughness'].default_value) if ps else None,
            'textures':[{'node':n.name,'image':n.image.name if n.image else None,'file':n.image.filepath if n.image else None} for n in m.node_tree.nodes if n.type=='TEX_IMAGE'] if m.use_nodes else []})
    for i in bpy.data.images:report['images'].append({'name':i.name,'size':list(i.size),'packed':bool(i.packed_file),'file':i.filepath,'has_data':i.has_data})
    overview=OUT/(path.stem+'-overview.png');setup_render(meshes,overview);report['previews'].append(str(overview))
    # Separate complete mesh objects, and large disconnected full-body islands,
    # give a reviewable alternative selection without writing back to the source.
    full_meshes=[o for o in meshes if o.dimensions.z>=max(m.dimensions.z for m in meshes)*.72 and len(o.data.vertices)>500]
    if 1<len(full_meshes)<=4:
        for n,selected in enumerate(full_meshes):
            for o in meshes:o.hide_render=o!=selected
            preview=OUT/(path.stem+'-object-'+str(n+1)+'.png');setup_render([selected],preview)
            report['previews'].append({'file':str(preview),'object':selected.name})
        for o in meshes:o.hide_render=False
    if 1<len(variant_components)<=4 and len(meshes)==1:
        for n,(o,i,c) in enumerate(variant_components):
            remap={old:new for new,old in enumerate(c['_ids'])};me=bpy.data.meshes.new('Read-only component preview')
            me.from_pydata([o.data.vertices[k].co for k in c['_ids']],[],[[remap[v] for v in o.data.polygons[f].vertices] for f in c['_faces']])
            for mat in o.data.materials:me.materials.append(mat)
            for p,f in zip(me.polygons,c['_faces']):p.material_index=o.data.polygons[f].material_index;p.use_smooth=o.data.polygons[f].use_smooth
            # Preserve loop UVs on the isolated component for inspection.
            for source_uv in o.data.uv_layers:
                uv=me.uv_layers.new(name=source_uv.name)
                for p,f in zip(me.polygons,c['_faces']):
                    for to_loop,from_loop in zip(p.loop_indices,o.data.polygons[f].loop_indices):uv.data[to_loop].uv=source_uv.data[from_loop].uv
            selected=bpy.data.objects.new('Component '+str(i),me);bpy.context.scene.collection.objects.link(selected);selected.matrix_world=o.matrix_world
            o.hide_render=True;preview=OUT/(path.stem+'-component-'+str(n+1)+'.png');setup_render([selected],preview)
            report['previews'].append({'file':str(preview),'object':o.name,'component':i,'vertices':c['vertices']})
            bpy.data.objects.remove(selected,do_unlink=True);o.hide_render=False
    report['source_unchanged']=hashlib.sha256(path.read_bytes()).hexdigest()==before
    (OUT/(path.stem+'-audit.json')).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');reports.append(report)
    print('CHARACTER_AUDIT',path.name,'meshes',len(meshes),'vertices',sum(m['vertices'] for m in report['meshes']),
        'triangles',sum(m['triangles'] for m in report['meshes']),'armatures',len(report['armatures']),'actions',len(report['actions']))
(OUT/'all-audit.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding='utf-8')
print('CHARACTER_AUDIT_DONE',OUT)
