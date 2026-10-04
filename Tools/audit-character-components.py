import pathlib, bpy, json
scope={}
exec(pathlib.Path(r'D:\DeadAirUnityProject\Tools\audit-input-characters.py').read_text().split('reports=[]')[0],scope)
rows=[]
for path in scope['FILES']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.suffix=='.glb':bpy.ops.import_scene.gltf(filepath=str(path))
    else:bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    for o in meshes:
        cs=scope['components'](o)
        rows.append({'file':path.name,'object':o.name,'components':[{k:v for k,v in c.items() if not k.startswith('_')} for c in cs]})
        print('WELDED_COMPONENTS',path.name,o.name,[(c['vertices'],c['triangles'],round(c['height'],5)) for c in cs[:12]])
scope['OUT'].joinpath('welded-components.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
