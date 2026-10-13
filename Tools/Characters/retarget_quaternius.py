"""Retarget licensed Quaternius motion onto the cast's own fitted skeletons.

Blender Z-up, -Y forward. Bone lengths, joint locations, garment weights and
bind matrices belong to the target character. Only animation keys are written.
The game retains in-place motion; the runtime contact solver handles terrain.
"""
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT/'Art/AnimationSources/QuaterniusUAL/source.blend'
FPS = 30
CLIPS = [
    ('Idle', 'Idle_Loop', True, 0),
    ('Walk', 'Walk_Loop', True, 2.4),
    ('WalkBack', 'Walk_Loop', True, 2.4),
    ('StrafeLeft', 'Walk_Loop', True, 2.4),
    ('StrafeRight', 'Walk_Loop', True, 2.4),
    ('Run', 'Sprint_Loop', True, 5.0),
    ('CrouchIdle', 'Crouch_Idle_Loop', True, 0),
    ('CrouchWalk', 'Crouch_Fwd_Loop', True, 1.4),
    ('UseItem', 'Interact', False, 0),
]
MAP = {'pelvis':'pelvis', 'spine':'spine_01', 'chest':'spine_03',
       'neck':'neck_01', 'head':'Head'}
for suffix, side in [('L','l'),('R','r')]:
    for target, source in [('clavicle','clavicle'),('upper_arm','upperarm'),
                           ('forearm','lowerarm'),('hand','hand'),
                           ('thigh','thigh'),('shin','calf'),('foot','foot'),('toe','ball')]:
        MAP[target+'.'+suffix] = source+'_'+side
    for finger, source in [('thumb','thumb'),('index','index'),('middle','middle'),
                           ('ring','ring'),('little','pinky')]:
        for segment in range(1,4):
            MAP[f'{finger}.{segment:02d}.{suffix}'] = f'{source}_{segment:02d}_{side}'


def _reset(arm):
    for p in arm.pose.bones:
        p.rotation_mode='QUATERNION'
        p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)


def _basis(arm, name, rotation, position=None, cache=None):
    pose=arm.pose.bones[name]
    parent=pose.parent
    parent_matrix=(cache.get(parent.name,parent.matrix) if cache is not None else parent.matrix) if parent else Matrix.Identity(4)
    if position is None:
        position=(parent_matrix @ parent.bone.matrix_local.inverted() @ pose.bone.matrix_local).translation if parent else pose.bone.head_local
    matrix=Matrix.LocRotScale(position,rotation,Vector((1,1,1)))
    parent=pose.parent
    basis=pose.bone.convert_local_to_pose(matrix,pose.bone.matrix_local,
        parent_matrix=parent_matrix,
        parent_matrix_local=parent.bone.matrix_local if parent else Matrix.Identity(4),invert=True)
    pose.matrix_basis=basis
    if cache is not None:
        cache[name]=matrix
    else:
        bpy.context.view_layer.update()


def _load_source():
    names={x[1] for x in CLIPS}
    with bpy.data.libraries.load(str(SOURCE),link=False) as (available,loaded):
        loaded.objects=['Armature']
        loaded.actions=[a for a in available.actions if a in names]
    source=loaded.objects[0]
    bpy.context.scene.collection.objects.link(source)
    source.name='Retarget source (not exported)'
    source.hide_render=True
    source.animation_data_create();source.animation_data.use_nla=False
    actions={original:action for original,action in zip([a for a in available.actions if a in names],loaded.actions)}
    # The source GLB was imported at Blender's 24fps default. Its times are
    # seconds, and the target is independently resampled at 30fps below.
    return source,actions,24.0


def _sample_source(source,actions,source_fps):
    scene=bpy.context.scene
    sampled={}
    for source_name,action in actions.items():
        source.animation_data.action=action
        start,end=action.frame_range
        duration=(end-start)/source_fps
        count=round(duration*FPS)+1
        frames=[]
        for index in range(count):
            frame=start+(end-start)*index/(count-1)
            scene.frame_set(math.floor(frame),subframe=frame%1);bpy.context.view_layer.update()
            frames.append({b.name:(b.head.copy(),b.tail.copy(),b.matrix.to_quaternion().normalized()) for b in source.pose.bones})
        sampled[source_name]=(duration,frames)
    return sampled


def _solve_leg(arm, side, target, foot_rotation, base):
    thigh='thigh.'+side;shin='shin.'+side;foot='foot.'+side
    hip=arm.pose.bones[thigh].head.copy()
    # A pole in front of each *individual* hip guarantees forward knees even
    # for Edgar's offset hips, short calves and much longer slippers.
    pole=hip+Vector((0,-1,-.1))
    base['solve_chain'](arm,thigh,shin,target,pole)
    _basis(arm,foot,foot_rotation,arm.pose.bones[foot].head.copy())


def _shoe_floor(parts,side,shoe_sides):
    minimum=float('inf')
    deps=bpy.context.evaluated_depsgraph_get()
    for obj in parts:
        if obj.get('authored_part')!='shoes':continue
        evaluated=obj.evaluated_get(deps);mesh=evaluated.to_mesh()
        for vertex,vertex_side in zip(mesh.vertices,shoe_sides[obj.name]):
            if vertex_side!=side:continue
            minimum=min(minimum,(evaluated.matrix_world@vertex.co).z)
        evaluated.to_mesh_clear()
    return minimum


def animate_retargeted(parts,arm,role,base):
    """Write the nine game clips; return export manifest clip records."""
    source,actions,source_fps=_load_source()
    source_rest={b.name:b.matrix_local.to_quaternion().normalized() for b in source.data.bones}
    source_heads={b.name:b.head_local.copy() for b in source.data.bones}
    samples=_sample_source(source,actions,source_fps)
    source_leg=sum(source.data.bones[n].length for n in ['thigh_l','calf_l'])
    target_leg=sum(arm.data.bones[n].length for n in ['thigh.L','shin.L'])
    ratio=target_leg/source_leg
    rest={b.name:b.matrix_local.copy() for b in arm.data.bones}
    ordered=[b.name for b in sorted(arm.data.bones,key=lambda b:len(b.parent_recursive)) if b.name in MAP]
    # Target rest is an anatomically fitted relaxed stance. Calibrate only the
    # sideways arm spread out of the source idle; retain its sagittal motion.
    idle=samples['Idle_Loop'][1][0]
    arm_neutral={name:(idle[MAP[name]][1]-idle[MAP[name]][0]).normalized() for name in ordered if name.startswith(('upper_arm.','forearm.'))}
    arm.animation_data_create();arm.animation_data.use_nla=False
    bpy.context.scene.render.fps=FPS
    result=[]
    shoe_sides={}
    for obj in parts:
        if obj.get('authored_part')!='shoes':continue
        names={g.index:g.name for g in obj.vertex_groups}
        shoe_sides[obj.name]=[max(['L','R'],key=lambda side:sum(g.weight for g in v.groups if names[g.group].endswith('.'+side))) for v in obj.data.vertices]
    try:
        for name,source_name,loop,speed in CLIPS:
            duration,frames=samples[source_name]
            upper_length=min(arm.data.bones['thigh.'+s].length for s in ['L','R'])
            maximum_drop=max(0.,max(source_heads['pelvis'].z-pose['pelvis'][0].z for pose in frames)*ratio)
            # The cast has deliberately different thigh/calf ratios. A deep
            # mannequin crouch must not put a short thigh's knee above its hip.
            pelvis_scale=min(1.,upper_length*.78/max(.001,maximum_drop)) if name.startswith('Crouch') else 1.
            lift_limit=upper_length*(.65 if name=='Run' else .22 if name.startswith('Crouch') else .36)
            source_lift=max(max(pose['foot_'+s.lower()][0].z-source_heads['foot_'+s.lower()].z for s in ['L','R']) for pose in frames)*ratio
            lift_scale=min(1.,lift_limit/max(.001,source_lift))
            action=bpy.data.actions.new(name);action.use_fake_user=True
            action['source']='Quaternius Universal Animation Library Standard / '+source_name
            action['target_role']=role;action['retargeted_individual_proportions']=True
            arm.animation_data.action=action
            previous={};first_keys=None;lowest=float('inf')
            for index,source_pose in enumerate(frames):
                number=index+1;bpy.context.scene.frame_set(number);_reset(arm);bpy.context.view_layer.update()
                # Horizontal root displacement stays in-place. Vertical pelvis
                # motion is scaled by leg length, never by the character's head.
                shift=source_pose['pelvis'][0]-source_heads['pelvis']
                target_pelvis=rest['pelvis'].translation+Vector((shift.x*ratio,shift.y*ratio,shift.z*ratio*pelvis_scale))
                pose_matrices={}
                for bone_name in ordered:
                    if bone_name.startswith(('thigh.','shin.','foot.','toe.')):continue
                    source_name_bone=MAP[bone_name]
                    if source_name_bone not in source_pose:continue
                    head,tail,rotation=source_pose[source_name_bone]
                    target_rest=rest[bone_name].to_quaternion()
                    target_rotation=rotation@source_rest[source_name_bone].inverted()@target_rest
                    if bone_name.startswith(('upper_arm.','forearm.')):
                        direction=(tail-head).normalized()
                        bone=arm.data.bones[bone_name]
                        target_direction=(bone.tail_local-bone.head_local).normalized()
                        direction=arm_neutral[bone_name].rotation_difference(direction)@target_direction
                        target_rotation=target_direction.rotation_difference(direction)@target_rest
                    elif bone_name.startswith(('hand.','thumb.','index.','middle.','ring.','little.')):
                        # Preserve target palm roll while inheriting source wrist
                        # articulation around its forearm, without resizing hands.
                        target_parent_name=arm.data.bones[bone_name].parent.name
                        src=MAP[bone_name];parent=MAP[target_parent_name]
                        local_now=source_pose[parent][2].inverted()@rotation
                        local_rest=source_rest[parent].inverted()@source_rest[src]
                        target_parent=pose_matrices[target_parent_name].to_quaternion()
                        parent_rest=rest[target_parent_name].to_quaternion()
                        target_local=parent_rest.inverted()@target_rest
                        # Local animation axes belong to the source parent's
                        # rest frame. Re-express them in the fitted parent's
                        # frame before applying wrist/finger articulation.
                        delta=local_now@local_rest.inverted()
                        world_delta=source_rest[parent]@delta@source_rest[parent].inverted()
                        target_delta=parent_rest.inverted()@world_delta@parent_rest
                        target_rotation=target_parent@target_delta@target_local
                    _basis(arm,bone_name,target_rotation,target_pelvis if bone_name=='pelvis' else None,pose_matrices)
                bpy.context.view_layer.update()
                # Foot trajectories are retargeted to each hero's ankle pivots;
                # limbs are solved at their actual lengths, never scaled bones.
                for side in ['L','R']:
                    source_foot='foot_'+side.lower();target_foot='foot.'+side
                    displacement=(source_pose[source_foot][0]-source_heads[source_foot])*ratio
                    if displacement.z>0:displacement.z*=lift_scale
                    if name=='WalkBack':displacement.y*=-1
                    elif name.startswith('Strafe'):
                        direction=-1 if name=='StrafeLeft' else 1
                        displacement.x+=direction*(-displacement.y);displacement.y=0
                    goal=rest[target_foot].translation+displacement
                    foot_delta=source_pose[source_foot][2]@source_rest[source_foot].inverted()
                    if foot_delta.w<0:foot_delta.negate()
                    # Flat source boots can point much farther than the cast's
                    # heels/slippers. Limit ankle articulation, keeping the
                    # character's own sole orientation and foot dimensions.
                    limit=math.radians(40 if name=='Run' else 24)
                    foot_delta=Quaternion().slerp(foot_delta,min(1.,limit/max(.001,foot_delta.angle)))
                    rotation=foot_delta@rest[target_foot].to_quaternion()
                    _solve_leg(arm,side,goal,rotation,base)
                    # Fit the real shoe's sole to the source contact, including
                    # high heels / wide slippers. This is baked once, not a
                    # generic ankle-height offset shared by all four characters.
                    floor=_shoe_floor(parts,side,shoe_sides)
                    source_ball='ball_'+side.lower()
                    grounded=source_pose[source_ball][0].z <= source_heads[source_ball].z+.012
                    for correction in range(5):
                        if not (floor < -.0001 or (grounded and floor>.0001)):break
                        goal.z-=floor
                        _solve_leg(arm,side,goal,rotation,base)
                        floor=_shoe_floor(parts,side,shoe_sides)
                    lowest=min(lowest,_shoe_floor(parts,side,shoe_sides))
                bpy.context.view_layer.update()
                if index==0:
                    first_keys={p.name:(p.location.copy(),p.rotation_quaternion.copy()) for p in arm.pose.bones}
                for p in arm.pose.bones:
                    if loop and index==len(frames)-1:
                        p.location,p.rotation_quaternion=first_keys[p.name]
                    q=p.rotation_quaternion
                    if p.name in previous and q.dot(previous[p.name])<0:q.negate()
                    previous[p.name]=q.copy()
                    p.keyframe_insert('location',frame=number,group=p.name)
                    p.keyframe_insert('rotation_quaternion',frame=number,group=p.name)
            # Monotone automatic handles keep dense samples smooth without
            # oscillating beyond authored extrema or using linear organic curves.
            for layer in action.layers:
                for strip in layer.strips:
                    for slot in action.slots:
                        bag=strip.channelbag(slot)
                        if bag:
                            for curve in bag.fcurves:
                                for key in curve.keyframe_points:
                                    key.interpolation='BEZIER';key.handle_left_type='AUTO_CLAMPED';key.handle_right_type='AUTO_CLAMPED'
            result.append(dict(name=name,duration=(len(frames)-1)/FPS,fps=FPS,frames=len(frames),loop=loop,
                authored_speed_mps=speed,source_animation=source_name,source_author='Quaternius',
                source_license='CC0-1.0',retarget_leg_scale=ratio,min_shoe_floor=lowest,
                crouch_depth_scale=pelvis_scale,swing_lift_scale=lift_scale))
            print('RETARGET '+role+' '+name+' '+str(len(frames))+' frames floor '+str(round(lowest,5)),flush=True)
        arm.animation_data.action=bpy.data.actions['Idle'];bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
        return result
    finally:
        bpy.data.objects.remove(source,do_unlink=True)
        for action in actions.values():
            if action is not None:bpy.data.actions.remove(action)
