"""Render saved static cast models and export portable, separated GLB previews.

blender --background --python review_restored_models.py -- --roles operator,dispatcher
No animation is generated and runtime FBX/prefabs are never overwritten.
"""
import bpy
import json
import runpy
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Characters'))
import restore_authored_cast as restore

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
def option(name,default):return args[args.index(name)+1] if name in args else default
roles=option('--roles',','.join(restore.ROLES)).split(',')
views=option('--views','front,face,three_quarter').split(',')
base=runpy.run_path(str(ROOT/'Tools/Characters/build_indie_cast.py'))
records=json.loads((restore.SOURCE/'character_manifest.json').read_text())['characters']
for role in roles:
    bpy.ops.wm.open_mainfile(filepath=str(restore.ART/(role+'_model.blend')))
    parts=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('authored_part')]
    arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    integrity=restore.static_asset_audit(parts,arm)
    report_path=restore.ART/(role+'_model.json')
    report=json.loads(report_path.read_text(encoding='utf-8'))
    report['static_integrity']=integrity
    report['parts']={part.name:restore.source_stats(part) for part in parts}
    report['refined']={key:sum(stats[key] for stats in report['parts'].values())
                       for key in next(iter(report['parts'].values()))}
    report_path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    info=next(record for record in records if record['role']==role)
    spec,_=restore.spec_markers(arm,info,base)
    if '--skip-render' not in args:
        restore.preview_rest(parts,arm,role,spec,base,'after_refinement',views)
        if '--outfit' in args:
            for part in parts:part.hide_render=part.get('authored_part') in {'body','hair','beard'}
            restore.preview_rest(parts,arm,role,spec,base,'separate_outfit',['front'])
            for part in parts:part.hide_render=False
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for part in parts:part.select_set(True)
    bpy.context.view_layer.objects.active=arm
    target=restore.ART/(role+'_model.glb')
    bpy.ops.export_scene.gltf(filepath=str(target),export_format='GLB',use_selection=True,
        export_animations=False,export_skins=True,export_yup=True,export_extras=True)
    print('STATIC MODEL REVIEW '+role+' '+str(target),flush=True)

characters=[json.loads(path.read_text(encoding='utf-8')) for role in restore.ROLES
            if (path:=restore.ART/(role+'_model.json')).exists()]
(restore.ART/'static_manifest.json').write_text(json.dumps(dict(formatVersion=2,
    stage='static models',coordinateSystem='Blender metres Z up / -Y face; glTF metres Y up',
    characters=characters),indent=2),encoding='utf-8')
