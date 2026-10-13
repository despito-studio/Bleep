"""Local cloth-pose corrections for the restored cast's existing clips.

Victor's long tie inherits the crouched chest transform down through the floor
when all secondary rotations stay at identity. Bend only its terminal link
forward; retain the authored cloth, bind matrices, weights and humanoid motion.
The correction is baked into the existing two crouch actions for FBX export.
"""
import math

import bpy
from mathutils import Matrix, Vector


TIE_TIP = 'secondary_cloth.tie.03'


def _minimum_height(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    height = min((evaluated.matrix_world @ vertex.co).z for vertex in mesh.vertices)
    evaluated.to_mesh_clear()
    return height


def _bend_tie_tip(arm, angle_degrees=80.0):
    """Set only the terminal link's local quaternion, keeping its joint fixed."""
    pose = arm.pose.bones[TIE_TIP]
    angle = math.radians(angle_degrees)
    direction = Vector((0, -math.sin(angle), -math.cos(angle)))
    matrix = pose.matrix.copy()
    delta = (pose.tail-pose.head).rotation_difference(direction)
    target = Matrix.LocRotScale(matrix.translation, delta @ matrix.to_quaternion(), matrix.to_scale())
    basis = pose.bone.convert_local_to_pose(target, pose.bone.matrix_local,
        parent_matrix=pose.parent.matrix, parent_matrix_local=pose.parent.bone.matrix_local,
        invert=True)
    pose.rotation_mode = 'QUATERNION'
    pose.rotation_quaternion = basis.to_quaternion().normalized()
    bpy.context.view_layer.update()


def bake_restored_cloth_pose(parts, arm, clips):
    """Return an audit; call immediately after ``base['animate']``.

    Only Victor's terminal tie quaternion is keyed, and only in CrouchIdle and
    CrouchWalk. The upper two tie links stay attached to the existing chest
    motion. No geometry, UV, weights, bind bones or humanoid keys are modified.
    Runtime spring motion must be additive to the freshly sampled clip pose.
    """
    tie = next((obj for obj in parts if obj.get('authored_role') == 'engineer'
                and obj.get('authored_part') == 'tie'), None)
    if tie is None or TIE_TIP not in arm.pose.bones:
        return {'corrected': False, 'reason': 'no restored engineer tie'}
    scene = bpy.context.scene
    prior_action = arm.animation_data.action
    prior_frame = scene.frame_current
    report = {'corrected': True, 'bone': TIE_TIP, 'world_forward_bend_degrees': 80.0,
              'changed_channels': [TIE_TIP+'.rotation_quaternion'], 'clips': {},
              'geometry_changed': False, 'weights_changed': False, 'bind_changed': False,
              'humanoid_animation_changed': False}
    for clip in clips:
        if clip['name'] not in {'CrouchIdle', 'CrouchWalk'}:
            continue
        action = bpy.data.actions[clip['name']]
        arm.animation_data.action = action
        original_floor = float('inf'); corrected_floor = float('inf')
        previous = None
        for frame in range(1, clip['frames']+1):
            scene.frame_set(frame); bpy.context.view_layer.update()
            original_floor = min(original_floor, _minimum_height(tie))
            _bend_tie_tip(arm)
            pose = arm.pose.bones[TIE_TIP]
            if previous is not None and pose.rotation_quaternion.dot(previous) < 0:
                pose.rotation_quaternion.negate()
            previous = pose.rotation_quaternion.copy()
            pose.keyframe_insert('rotation_quaternion', frame=frame, group=TIE_TIP)
            corrected_floor = min(corrected_floor, _minimum_height(tie))
        path = arm.pose.bones[TIE_TIP].path_from_id('rotation_quaternion')
        for slot in action.slots:
            for layer in action.layers:
                for strip in layer.strips:
                    bag = strip.channelbag(slot)
                    if bag:
                        for curve in bag.fcurves:
                            if curve.data_path == path:
                                for key in curve.keyframe_points:
                                    key.interpolation = 'BEZIER'
                                    key.handle_left_type = 'AUTO_CLAMPED'
                                    key.handle_right_type = 'AUTO_CLAMPED'
        action['restored_tie_crouch_clearance'] = True
        report['clips'][clip['name']] = {'frames': clip['frames'],
            'original_floor_m': original_floor, 'corrected_floor_m': corrected_floor}
        if corrected_floor < .015:
            raise RuntimeError('Restored tie bend failed its 15 mm floor clearance: '+clip['name'])
    arm.animation_data.action = prior_action
    scene.frame_set(prior_frame); bpy.context.view_layer.update()
    return report
