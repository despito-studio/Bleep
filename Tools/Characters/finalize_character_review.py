"""Apply reviewed surface and individual bind-rig repairs to the static cast.

The first input is retained in ReviewCandidates/BeforeFinal, so rerunning this
step starts from the same pre-review mesh instead of stacking geometry edits.
Animation export is a separate step using export_restored_cast.py.
"""
import json
import re
import shutil
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'Tools/Characters'))
import restore_authored_cast as restore
from refine_character_surfaces import refine_character_surfaces
from fit_individual_rigs import fit_individual_rig
from restored_weight_finish import finish_restored_weights


def canonical_materials(parts, role):
    folder = ROOT/'Assets/Characters/Textures/Restored'/role
    folder.mkdir(parents=True, exist_ok=True)
    used = {m for o in parts for m in o.data.materials if m}
    names = {}
    for material in used:
        name = re.sub(r'\.\d+$', '', material.name)
        name = name.replace('_surface_review', '')
        if not name.startswith(role+'_restored_'):
            raise ValueError('Unexpected source material: '+name)
        if name in names and names[name] != material:
            raise ValueError('Two different materials share '+name)
        names[name] = material
    # Discard unused originals before assigning canonical names, preventing
    # Blender's automatic .001 suffix from breaking Unity's texture contract.
    for name, material in names.items():
        old = bpy.data.materials.get(name)
        if old is not None and old != material and old not in used:
            bpy.data.materials.remove(old)
        material.name = name
        nodes = [n for n in material.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and n.outputs[0].is_linked]
        if len(nodes) != 1:
            raise ValueError(name+' must have one connected albedo')
        image = nodes[0].image
        path = folder/(name+'_albedo.png')
        image.filepath_raw = str(path)
        image.file_format = 'PNG'
        image.save()
        data = path.read_bytes()
        image.pack(data=data, data_len=len(data))


def main():
    args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    roles = args[args.index('--roles')+1].split(',') if '--roles' in args else restore.ROLES
    backup = ROOT/'Art/Characters/ReviewCandidates/BeforeFinal'
    backup.mkdir(parents=True, exist_ok=True)
    for role in roles:
        target = restore.ART/(role+'_model.blend')
        source = backup/target.name
        if not source.exists():
            shutil.copy2(target, source)
        bpy.ops.wm.open_mainfile(filepath=str(source))
        parts = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.get('authored_part')]
        arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
        parts, surfaces = refine_character_surfaces(parts, arm, role, ROOT/'Art/Characters/ReviewCandidates/Final')
        fit = fit_individual_rig(parts, arm, role)
        weights = finish_restored_weights(parts, arm, role)
        canonical_materials(parts, role)
        audit = restore.static_asset_audit(parts, arm)
        arm.animation_data_clear()
        for action in list(bpy.data.actions):
            bpy.data.actions.remove(action)
        bpy.ops.wm.save_as_mainfile(filepath=str(target))
        report_path = restore.ART/(role+'_model.json')
        report = json.loads(report_path.read_text(encoding='utf-8'))
        report.update(surface_refinement=surfaces, individual_rig=fit, weight_finish=weights, static_integrity=audit)
        report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
        print('REVIEW FINALIZED '+role+' '+json.dumps(audit), flush=True)


if __name__ == '__main__':
    main()
