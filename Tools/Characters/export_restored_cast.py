"""Export the reviewed modular models with the existing nine-clip rig contract.

Run in Blender after static model review. Geometry is loaded from *_model.blend
and never regenerated here. Static source files remain untouched.
"""
import hashlib
import json
import runpy
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Characters'))
import restore_authored_cast as restore
from restored_weight_finish import finish_restored_weights
from restored_cloth_pose import bake_restored_cloth_pose
from retarget_quaternius import animate_retargeted


def export_role(role, base):
    print('RESTORED EXPORT START '+role,flush=True)
    source = restore.ART / (role + '_model.blend')
    source_digest = hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(source))
    parts = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.get('authored_part')]
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    weight_finish = finish_restored_weights(parts, arm, role)
    audit = restore.static_asset_audit(parts, arm)
    info = next(c for c in json.loads((restore.SOURCE / 'character_manifest.json').read_text())['characters'] if c['role'] == role)
    spec, markers = restore.spec_markers(arm, info, base)
    sockets = {o.name: list(o['authored_world_position']) for o in bpy.context.scene.objects
               if o.type == 'EMPTY' and o.parent == arm and 'authored_world_position' in o}
    arm.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    base['reset_pose'](arm)
    bpy.context.scene.render.fps = base['FPS']
    if not arm.get('individual_rig_fit_v1'):
        raise RuntimeError(role+' needs its individually fitted bind rig before motion retargeting')
    clips = animate_retargeted(parts, arm, role, base)
    cloth_pose = bake_restored_cloth_pose(parts, arm, clips)
    validation = restore.validate_parts(parts, arm, clips, base)
    validation['passed'] = (validation['unweighted_vertices'] == 0
        and validation['max_weight_sum_error'] < 1e-5 and validation['max_influences'] <= 4
        and validation['nonfinite_vertices'] == 0 and validation['min_floor_z'] > -.008
        and max(validation['loop_error'].values()) < 1e-4 and validation['degenerate_faces'] == 0)
    (ROOT/'Logs'/('restored-'+role+'-deformation.json')).write_text(json.dumps(validation,indent=2),encoding='utf-8')
    if not validation['passed']:
        raise RuntimeError(role + ' deformation failed: ' + json.dumps(validation))
    animated_source = restore.ART / (role + '.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(animated_source))
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for part in parts:
        part.select_set(True)
    for item in bpy.context.scene.objects:
        if item.type == 'EMPTY' and item.parent == arm:
            item.select_set(True)
    bpy.context.view_layer.objects.active = arm
    target = ROOT / 'Assets/Resources/Characters' / ('character_' + role + '.fbx')
    bpy.ops.export_scene.fbx(filepath=str(target), use_selection=True,
        object_types={'ARMATURE', 'MESH', 'EMPTY'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        use_space_transform=True, bake_space_transform=False, add_leaf_bones=False,
        primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=True,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
        bake_anim_step=1, bake_anim_simplify_factor=0, path_mode='AUTO', embed_textures=False)
    unity = lambda p: [round(p[0], 6), round(p[2], 6), round(-p[1], 6)]
    report = dict(role=role, name=info['name'], source=animated_source.relative_to(ROOT).as_posix(),
        runtime=target.relative_to(ROOT).as_posix(), static_source=source.relative_to(ROOT).as_posix(),
        static_source_sha256=source_digest, vertices=sum(len(p.data.vertices) for p in parts),
        triangles=audit['triangles'], parts=audit['parts'], textures=audit['textures'],
        texture_pixels=audit['texture_pixels'], bones=len(arm.data.bones),
        bone_names=[b.name for b in arm.data.bones], height_m=spec['height'],
        source_eye=unity(sockets['EyeSocket']), mouth=unity(sockets['MouthSocket']),
        torso_front_m=-sockets['TorsoFront'][1],
        foot_contacts={s: unity(sockets['FootContact.'+s]) for s in ['L', 'R']},
        shoulder_positions={s: unity(m['shoulder']) for s, m in markers.items()},
        skin_color=base['CAST'][role]['skin'], sleeve_color=base['CAST'][role]['primary'],
        cuff_color=base['CAST'][role]['secondary'], clips=clips, weight_finish=weight_finish,
        cloth_pose=cloth_pose, validation=validation,
        animation_source='Art/AnimationSources/QuaterniusUAL/provenance.json',
        individual_rig=json.loads(arm['individual_rig_fit_v1']))
    (restore.ART / (role + '_manifest.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('RESTORED EXPORT ' + json.dumps(dict(role=role, triangles=audit['triangles'],
        parts=len(parts), clips=len(clips), validation=validation['passed'])), flush=True)
    return report


def main():
    args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    roles = args[args.index('--roles')+1].split(',') if '--roles' in args else restore.ROLES
    base = runpy.run_path(str(ROOT / 'Tools/Characters/build_indie_cast.py'))
    errors=[]
    for role in roles:
        try:export_role(role, base)
        except Exception as error:
            errors.append(role+': '+str(error))
            print('RESTORED EXPORT FAILED '+role+': '+str(error),flush=True)
    reports = [json.loads(p.read_text(encoding='utf-8')) for role in restore.ROLES
               if (p := restore.ART / (role+'_manifest.json')).exists()]
    manifest = dict(formatVersion=2, stage='animated runtime cast',
        coordinateSystem='Blender metres Z up / -Y face; Unity X right Y up +Z face', characters=reports)
    (restore.ART / 'cast_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    if errors:raise RuntimeError('Export failed for '+str(len(errors))+' role(s); see Logs/restored-<role>-deformation.json')


if __name__ == '__main__':
    main()
