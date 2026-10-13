"""Scoped weighting corrections for the restored modular character meshes.

Call finish_restored_weights(parts, arm, role) after garment separation and
before animation/export. It changes deform weights only, never bind geometry,
UVs, materials, skeleton rest transforms, or animation curves.
"""
import json
from mathutils.kdtree import KDTree


def _smooth(value):
    value = max(0.0, min(1.0, value))
    return value*value*(3.0-2.0*value)


def _finish_trouser_weights(parts, arm, role):
    if role not in {'engineer', 'journalist'}:
        return {'role': role, 'changed_vertices': 0}
    trousers = next(obj for obj in parts if obj.get('authored_part') == 'trousers')
    if trousers.get('restored_trouser_weights_v1'):
        return json.loads(trousers['restored_trouser_weights_v1'])
    names = {group.index: group.name for group in trousers.vertex_groups}
    changed = []
    transitions = {}
    max_change = 0.0
    for vertex in trousers.data.vertices:
        side = 'L' if vertex.co.x >= 0 else 'R'
        ankle = arm.data.bones['foot.'+side].head_local.z
        knee = arm.data.bones['shin.'+side].head_local.z
        bottom, top = ankle+.025, knee+(.14 if role == 'engineer' else .08)
        if vertex.co.z >= top:
            continue
        # Loose, deep trouser sections sit far in front of the shin.
        # A shin-only turn drives this fabric below the planted sole during a
        # deep bend. Carry its ankle-facing region with the foot, fading out
        # continuously into the existing shin/thigh knee bend.
        foot = 1.0-_smooth((vertex.co.z-bottom)/(top-bottom))
        thigh = _smooth((vertex.co.z-knee+.075)/.15)
        desired = {'foot.'+side: foot, 'shin.'+side: (1-foot)*(1-thigh),
                   'thigh.'+side: (1-foot)*thigh}
        desired = {name: weight for name, weight in desired.items() if weight > 1e-7}
        total = sum(desired.values())
        desired = {name: weight/total for name, weight in desired.items()}
        original = {names[value.group]: value.weight for value in vertex.groups}
        delta = max(abs(original.get(name, 0)-desired.get(name, 0)) for name in original.keys() | desired.keys())
        if delta < 1e-6:
            continue
        changed.append((vertex.index, desired))
        max_change = max(max_change, delta)
        transitions[side] = [bottom, top]
    indices = [index for index, _ in changed]
    if indices:
        for group in trousers.vertex_groups:
            group.remove(indices)
        for index, desired in changed:
            for name, weight in desired.items():
                trousers.vertex_groups[name].add([index], weight, 'REPLACE')
    report = dict(role=role, part='trousers', changed_vertices=len(changed), max_weight_change=max_change,
                  foot_to_shin_transition_z=transitions, max_influences=3,
                  geometry_uv_materials_unchanged=True,
                  method='smooth planted-foot support for loose lower trousers; existing knee blend retained')
    trousers['restored_trouser_weights_v1'] = json.dumps(report)
    return report


def _sync_cuff_seams(parts, trousers, arm):
    """Keep the original, split garment seam closed after changing its weights.

    Some lower cuff faces belong to the Shoes object. Their boundary vertices
    are exact copies of the Trousers boundary, so both must deform identically.
    Only coincident seam vertices are changed; the shoe's own weighting stays.
    """
    tree = KDTree(len(trousers.data.vertices))
    for vertex in trousers.data.vertices:
        tree.insert(trousers.matrix_world @ vertex.co, vertex.index)
    tree.balance()
    names = {group.index: group.name for group in trousers.vertex_groups}
    source = [{names[w.group]: w.weight for w in vertex.groups}
              for vertex in trousers.data.vertices]
    knee = max(arm.data.bones['shin.'+side].head_local.z for side in ('L', 'R'))
    matches = changed = 0
    for obj in parts:
        if obj.get('authored_part') != 'shoes':
            continue
        own_names = {group.index: group.name for group in obj.vertex_groups}
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            if (arm.matrix_world.inverted() @ point).z >= knee:
                continue
            _, index, distance = tree.find(point)
            if distance > .00001:
                continue
            matches += 1
            desired = source[index]
            original = {own_names[w.group]: w.weight for w in vertex.groups}
            if all(abs(original.get(name, 0)-desired.get(name, 0)) < 1e-7
                   for name in original.keys() | desired.keys()):
                continue
            for group in obj.vertex_groups:
                group.remove([vertex.index])
            for name, weight in desired.items():
                group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
                group.add([vertex.index], weight, 'REPLACE')
            changed += 1
    return dict(matched_vertices=matches, changed_vertices=changed,
                method='identical deform weights on coincident trouser/shoe cuff seams')


def finish_restored_weights(parts, arm, role):
    report = _finish_trouser_weights(parts, arm, role)
    if role not in {'engineer', 'journalist'}:
        return report
    trousers = next(obj for obj in parts if obj.get('authored_part') == 'trousers')
    # Run even when the main weight pass is already cached, so saved models
    # made before the seam correction can be upgraded without rebuilding them.
    seam_report = _sync_cuff_seams(parts, trousers, arm)
    if seam_report['changed_vertices'] or not trousers.get('restored_trouser_seams_v1'):
        trousers['restored_trouser_seams_v1'] = json.dumps(seam_report)
    report['cuff_seams'] = json.loads(trousers['restored_trouser_seams_v1'])
    return report
