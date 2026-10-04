"""Clean, fit and animate the four user models without altering D:/1.

Run Blender --background --factory-startup --python this_file -- [--audit-only].
Blender source/rest space: metres, Z up, -Y forward. Runtime FBX: Unity Y up.
"""
import bpy, bmesh, math, json, pathlib, hashlib, runpy, sys
from mathutils import Vector, Matrix, Quaternion

ROOT=pathlib.Path(__file__).resolve().parents[2]
PREVIEW=ROOT/'Verification-Characters'/'Prepared'
ART=ROOT/'Art'/'Characters'/'Imported'
OUT=ROOT/'Assets'/'Resources'/'Characters'
TEXTURES=ROOT/'Assets'/'Characters'/'Textures'
HANDS=ROOT/'Assets'/'Characters'/'Imported'
AUDIT=ROOT/'Verification-Characters'/'Source'
PREVIEW.mkdir(parents=True,exist_ok=True)
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
AUDIT_ONLY='--audit-only' in args
BODY_ONLY='--body-only' in args
REF=runpy.run_path(str(ROOT/'Tools'/'Characters'/'build_reference_cast.py'),run_name='reference_helpers')
audit_scope={}
exec((ROOT/'Tools'/'audit-input-characters.py').read_text().split('reports=[]')[0],audit_scope)

CAST=[
 dict(role='operator',name='Vivian',source='1.glb',variant='whole',height=1.88,
      hip=.89,waist=1.05,chest=1.27,headbase=1.52,hipx=.108,
      shoulder=(.235,0,1.39),arm_fractions=(.32,.59,.84),knee=(.112,.007,.47),ankle=(.106,0,.15)),
 dict(role='journalist',name='Edgar',source='2.glb',variant='negative-source-Y',height=2.045,
      hip=.84,waist=1.07,chest=1.37,headbase=1.65,hipx=.103,
      shoulder=(.29,0,1.485),arm_fractions=(.34,.59,.80),knee=(.107,.008,.47),ankle=(.12,0,.14)),
 dict(role='engineer',name='Victor',source='3.fbx',variant='positive-source-Y',height=1.81,forward_source='-X',
      hip=.60,waist=.89,chest=1.19,headbase=1.53,hipx=.217,
      shoulder=(.30,.09,1.44),arm_fractions=(.55,.70,.82),
      arm_markers={'shoulder':(.30,.09,1.44),'elbow':(.48,.156,1.43),'wrist':(.644,.151,1.425)},
      knee=(.245,.015,.345),ankle=(.266,0,.13)),
 dict(role='dispatcher',name='Hazel',source='4.fbx',variant='whole',height=2.03,
      hip=.96,waist=1.14,chest=1.36,headbase=1.68,hipx=.094,
      shoulder=(.208,0,1.53),arm_fractions=(.31,.58,.84),knee=(.102,.013,.505),ankle=(.103,0,.14)),
]

def bounds(body):
    pts=[v.co for v in body.data.vertices]
    lo=Vector(tuple(min(p[a] for p in pts) for a in range(3)));hi=Vector(tuple(max(p[a] for p in pts) for a in range(3)))
    return lo,hi

def import_selected(info):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path=pathlib.Path('D:/1')/info['source'];digest=hashlib.sha256(path.read_bytes()).hexdigest()
    if path.suffix=='.glb':bpy.ops.import_scene.gltf(filepath=str(path))
    else:bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
    sources=[o for o in bpy.context.scene.objects if o.type=='MESH']
    if len(sources)!=1:raise RuntimeError('Expected one source mesh: '+str(path))
    original=sources[0];original.data.calc_loop_triangles()
    original_stats={'vertices':len(original.data.vertices),'triangles':len(original.data.loop_triangles)}
    source_components=audit_scope['components'](original)
    keep_faces=None;selection={}
    if info['variant']!='whole':
        main=sorted(source_components,key=lambda c:c['vertices'],reverse=True)[:2]
        centers=sorted((c['bounds']['min'][1]+c['bounds']['max'][1])*.5 for c in main)
        split=sum(centers)*.5;positive=info['variant'].startswith('positive')
        keep_faces=set()
        for comp in source_components:
            center=(comp['bounds']['min'][1]+comp['bounds']['max'][1])*.5
            if (center>=split)==positive:keep_faces.update(comp['_faces'])
        selection={'split_source_Y':split,'centers_source_Y':centers,'selected_side':'positive' if positive else 'negative'}
    me=original.data.copy();body=bpy.data.objects.new(info['name']+'_Body',me);bpy.context.scene.collection.objects.link(body)
    if keep_faces is not None:
        bm=bmesh.new();bm.from_mesh(me);bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.index not in keep_faces],context='FACES')
        bm.to_mesh(me);bm.free()
    # Bake all source transforms and normalize each selected variant's own
    # face direction. The two source alternatives do not all face the same way.
    facing=-1 if info.get('forward_source')=='-X' else 1
    for v in me.vertices:
        p=original.matrix_world@v.co;v.co=Vector((p.y*facing,-p.x*facing,p.z))
    lo,hi=bounds(body);scale=info['height']/(hi.z-lo.z)
    center=Vector(((lo.x+hi.x)*.5,(lo.y+hi.y)*.5,lo.z))
    for v in me.vertices:v.co=(v.co-center)*scale
    bpy.data.objects.remove(original,do_unlink=True)
    # UVs are loop data: welding exact positional duplicates retains every UV island.
    bm=bmesh.new();bm.from_mesh(me)
    before=len(bm.verts);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
    degenerates=[f for f in bm.faces if f.calc_area()<1e-10]
    if degenerates:bmesh.ops.delete(bm,geom=degenerates,context='FACES')
    seen=set();duplicates=[]
    bm.verts.index_update()
    for f in bm.faces:
        key=tuple(sorted(v.index for v in f.verts))
        if key in seen:duplicates.append(f)
        else:seen.add(key)
    if duplicates:bmesh.ops.delete(bm,geom=duplicates,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free();me.update()
    bpy.context.view_layer.objects.active=body;body.select_set(True)
    triangulate=body.modifiers.new('Triangulated source topology','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=triangulate.name)
    for face in me.polygons:face.use_smooth=False
    me.calc_loop_triangles();lo,hi=bounds(body)
    stats={'role':info['role'],'name':info['name'],'source':str(path),'sha256':digest,'variant':info['variant'],
        'selection':selection,'forward_source':info.get('forward_source','+X'),'rotation_Z_degrees':90 if facing<0 else -90,
        'original':original_stats,'cleaned':{'vertices':len(me.vertices),'triangles':len(me.loop_triangles),
            'exact_position_vertices_merged':before-len(me.vertices),'degenerate_faces_removed':len(degenerates),
            'duplicate_faces_removed':len(duplicates)},'bounds_min':list(lo),'bounds_max':list(hi),'height_m':info['height']}
    if len(me.loop_triangles)>20000:raise RuntimeError('Triangle budget exceeded for '+info['role'])
    return body,stats

def fitted_spec(body,info):
    lo,hi=bounds(body);span=max(abs(lo.x),abs(hi.x));shoulder_x,elbow_x,wrist_x=[span*f for f in info['arm_fractions']]
    # Source arms are horizontal. Recover their actual row from the distal arm
    # vertices rather than assuming a marker from the old procedural character.
    arm_points=[v.co for v in body.data.vertices if abs(v.co.x)>span*.72 and v.co.z>info['height']*.52]
    zs=sorted(p.z for p in arm_points);arm_z=zs[len(zs)//2] if zs else info['shoulder'][2]
    ys=sorted(p.y for p in arm_points);arm_y=ys[len(ys)//2] if ys else 0
    spec={k:info[k] for k in ('hip','waist','chest','headbase','hipx','knee','ankle')}
    spec.update(top=info['height'],shoulder=(shoulder_x,arm_y,arm_z),
        elbow=(elbow_x,arm_y,arm_z),wrist=(wrist_x,arm_y,arm_z))
    # Bulky coats hide the shoulder joint. Engineer's actual sleeve root and
    # three-dimensional arm row were measured on normalized source geometry;
    # distal hand-span fractions put the shoulder too far into that sleeve.
    if 'arm_markers' in info:
        spec.update(info['arm_markers'])
        arm_z=spec['shoulder'][2]
    # Respect the small real neck space underneath cap/hair.
    spec['chest']=min(spec['chest'],arm_z-.08)
    return spec

def render(body,role,label,back=False,pose=False):
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE_NEXT'
    if hasattr(scene,'eevee') and hasattr(scene.eevee,'taa_render_samples'):scene.eevee.taa_render_samples=16
    scene.render.resolution_x=760;scene.render.resolution_y=920;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.world=bpy.data.worlds.new('Neutral character audit');scene.world.use_nodes=True
    bg=scene.world.node_tree.nodes['Background'];bg.inputs[0].default_value=(.055,.062,.07,1);bg.inputs[1].default_value=.5
    scene.view_settings.view_transform='AgX'
    lo,hi=bounds(body);h=hi.z-lo.z;center=Vector((0,0,h*.5));span=max(h,hi.x-lo.x)
    camdata=bpy.data.cameras.new('Prepared audit camera');cam=bpy.data.objects.new('Prepared audit camera',camdata);scene.collection.objects.link(cam)
    cam.location=center+Vector((span*.07,span*2 if back else -span*2,span*.02));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    camdata.type='ORTHO';camdata.ortho_scale=max(h*1.28*(760/920),(hi.x-lo.x)*1.13);scene.camera=cam
    light_objects=[]
    for name,xyz,power in [('Key',(-2,-3,4),850),('Fill',(3,-2,2),600),('Rim',(2,3,3),950)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.size=span*2
        light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=Vector(xyz)
        light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler();light_objects.append(light)
    target=PREVIEW/(role+'_'+label+'.png');scene.render.filepath=str(target);bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam,do_unlink=True)
    for ob in light_objects:bpy.data.objects.remove(ob,do_unlink=True)
    return str(target)

def export_atlas(body,role,stats):
    TEXTURES.mkdir(parents=True,exist_ok=True);ART.mkdir(parents=True,exist_ok=True)
    originals=[]
    for mat in body.data.materials:
        images=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image] if mat.use_nodes else []
        for image in images:
            if image not in originals:originals.append(image)
    if len(originals)!=1:raise RuntimeError('Expected a single source atlas for '+role)
    image=originals[0];original_size=list(image.size)
    image.filepath_raw=str(ART/(role+'_source_albedo.png'));image.file_format='PNG';image.save()
    image.scale(1024,1024);image.filepath_raw=str(TEXTURES/(role+'_albedo.png'));image.save()
    image.name=role+'_albedo';image.pack()
    for mat in body.data.materials:
        mat.name=role+'_source_atlas'
    stats['textures']=[{'source_size':original_size,'runtime_size':[1024,1024],
        'runtime':'Assets/Characters/Textures/'+role+'_albedo.png','original_copy':'Art/Characters/Imported/'+role+'_source_albedo.png'}]

def weights(body,arm,spec,role):
    for b in arm.data.bones:body.vertex_groups.new(name=b.name)
    sh=Vector(spec['shoulder']);el=Vector(spec['elbow']);wr=Vector(spec['wrist']);h=spec['top']
    # Keep detached accessories attached to their real support, even where a
    # long tie or coat passes in front of the thighs. Head/hair components are
    # likewise identified before bending the source T-pose sleeves.
    accessory_weights={}
    for comp in audit_scope['components'](body):
        lo=Vector(comp['bounds']['min']);hi=Vector(comp['bounds']['max'])
        supported=None
        if role=='engineer' and hi.z-lo.z>h*.5 and hi.x-lo.x<h*.15 and hi.z<h*.9 and hi.z>h*.7 and lo.z<h*.3:
            supported='chest'
        elif role=='dispatcher' and hi.z-lo.z>h*.45 and hi.x-lo.x<h*.45 and hi.z>h*.93 and lo.z<h*.6:
            supported='head'
        for face_index in comp['_faces']:
            for index in body.data.polygons[face_index].vertices:
                if supported:accessory_weights[index]=supported
                elif role=='journalist' and comp['vertices']>400 and body.data.vertices[index].co.z<spec['hip']*.95:
                    accessory_weights[index]='pelvis'
    def blend(a,b,t):return {a:1-t,b:t} if 0<t<1 else {b if t>=1 else a:1}
    def assign(vertex,values):
        total=sum(values.values())
        for name,value in values.items():
            if value>.0001:body.vertex_groups[name].add([vertex.index],value/total,'REPLACE')
    for v in body.data.vertices:
        x,y,z=v.co;ax=abs(x);side='L' if x>=0 else 'R'
        # Arms/fingers follow the actual horizontal source row, preserving the
        # complete sleeve. At least .8 hand weight supports runtime grip finding.
        if ax<=el.x:
            center=sh.lerp(el,max(0,min(1,(ax-sh.x)/max(.001,el.x-sh.x))))
        else:
            center=el.lerp(wr,max(0,min(1,(ax-el.x)/max(.001,wr.x-el.x))))
        sleeve_height=.19 if role=='dispatcher' else (.13 if role=='engineer' else .105)
        in_arm=ax>sh.x*.88 and abs(z-center.z)<h*sleeve_height and abs(y-center.y)<h*.11
        long_hair=role=='dispatcher' and z>spec['hip'] and y>sh.y+.08 and ax<.48
        if v.index in accessory_weights:values={accessory_weights[v.index]:1}
        elif long_hair:values={'head':1}
        elif in_arm:
            if ax>=wr.x-.025:values={'hand.'+side:1}
            elif ax>el.x-.04:values=blend('upper_arm.'+side,'forearm.'+side,(ax-(el.x-.04))/.08)
            else:values=blend('chest','upper_arm.'+side,(ax-sh.x*.78)/(sh.x*.35))
        elif z>spec['headbase']-.035:values={'head':1}
        elif z>sh.z-.035:values=blend('chest','neck',(z-(sh.z-.035))/max(.03,spec['headbase']-sh.z+.035))
        elif z<spec['hip']*.90:
            knee=spec['knee'][2];ankle=spec['ankle'][2]
            if z<ankle+.055:values={'foot.'+side:1}
            elif z<knee+.055:values=blend('shin.'+side,'thigh.'+side,(z-(knee-.055))/.11)
            else:values={'thigh.'+side:1}
        elif z<spec['waist']-.055:values={'pelvis':1}
        elif z<spec['waist']+.055:values=blend('pelvis','spine',(z-(spec['waist']-.055))/.11)
        elif z<spec['chest']+.065:values=blend('spine','chest',(z-(spec['chest']-.065))/.13)
        else:values={'chest':1}
        assign(v,values)
    mod=body.modifiers.new('Source character skin','ARMATURE');mod.object=arm;body.parent=arm

def lower_rest_arms(body,arm,spec):
    for side,suffix in [(1,'L'),(-1,'R')]:
        pb=arm.pose.bones['upper_arm.'+suffix];basis=pb.bone.matrix_local.to_quaternion()
        turn=Quaternion(Vector((0,1,0)),math.radians(65)*side)
        pb.rotation_mode='QUATERNION';pb.rotation_quaternion=basis.inverted()@turn@basis
    bpy.context.view_layer.update()
    # Capture transformed joint markers before changing bind matrices.
    rest={b.name:(b.head.copy(),b.tail.copy()) for b in arm.pose.bones}
    bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
    mod=next(m for m in body.modifiers if m.type=='ARMATURE');bpy.ops.object.modifier_apply(modifier=mod.name)
    body.parent=None
    arm.select_set(True);body.select_set(False);bpy.context.view_layer.objects.active=arm
    bpy.ops.object.mode_set(mode='POSE');bpy.ops.pose.armature_apply(selected=False);bpy.ops.object.mode_set(mode='OBJECT')
    body.parent=arm;mod=body.modifiers.new('Clean A pose source skin','ARMATURE');mod.object=arm
    return {name:{'head':list(a),'tail':list(b)} for name,(a,b) in rest.items()}

def mouth_position(body,height):
    import numpy as np
    mat=body.data.materials[0];images=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
    candidates=[]
    if images and body.data.uv_layers.active:
        image=images[0];w,h=image.size;pixels=np.empty(w*h*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
        uv=body.data.uv_layers.active.data
        for loop in body.data.loops:
            p=body.data.vertices[loop.vertex_index].co
            if abs(p.x)>.09 or p.z<height*.78 or p.z>height*.90:continue
            t=uv[loop.index].uv;rgb=pixels[min(h-1,max(0,int(t.y*h))),min(w-1,max(0,int(t.x*w))),:3]
            if rgb[0]>.38 and rgb[0]>rgb[1]*1.3 and rgb[0]>rgb[2]*1.15 and rgb[1]<.68:candidates.append(p.copy())
    if candidates:
        p=sum(candidates,Vector())/len(candidates);p.y=min(q.y for q in candidates)-.005
        return p,'source lip-colored UV vertices'
    near=[v.co for v in body.data.vertices if abs(v.co.x)<.07 and abs(v.co.z-height*.835)<height*.04]
    return Vector((0,min(p.y for p in near) if near else -.12,height*.835)),'source face front at normalized lip height'

def mouth_socket(arm,point):
    socket=bpy.data.objects.new('MouthSocket',None);bpy.context.collection.objects.link(socket)
    socket.empty_display_type='PLAIN_AXES';socket.empty_display_size=.025
    # Bone-parenting matrices include the head bone tail offset. Derive the
    # inverse from Blender itself so the exported socket lands at the lips.
    socket.parent=arm;socket.parent_type='BONE';socket.parent_bone='head';socket.matrix_world=Matrix.Translation(point)
    bpy.context.view_layer.update();socket.matrix_world=Matrix.Translation(point)
    return socket

def source_actions(arm,role,mouth):
    # Reuse the established clip authoring with the actual operator mouth target.
    if role=='operator':
        def smoke(a):
            for p in a.pose.bones:p.rotation_mode='XYZ'
            upper=a.pose.bones['upper_arm.R'];fore=a.pose.bones['forearm.R'];hand=a.pose.bones['hand.R']
            shoulder=upper.bone.head_local.copy();target=mouth+Vector((-.024,-.038,-.012))
            direction=target-shoulder;distance=direction.length;direction.normalize();la=upper.bone.length;lb=fore.bone.length
            along=(la*la-lb*lb+distance*distance)/(2*distance);high=math.sqrt(max(0,la*la-along*along))
            out=Vector((-1,0,-.4));out-=direction*out.dot(direction);out.normalize();elbow=shoulder+direction*along+out*high;result={}
            for pb,start,end in [(upper,shoulder,elbow),(fore,elbow,target),(hand,target,target+Vector((.045,.025,.080)))]:
                rest=pb.bone.tail_local-pb.bone.head_local;rotation=rest.rotation_difference(end-start)@pb.bone.matrix_local.to_quaternion()
                pb.matrix=Matrix.LocRotScale(start,rotation,Vector((1,1,1)));bpy.context.view_layer.update();result[pb.name]=pb.rotation_euler.to_quaternion()
            for p in a.pose.bones:p.rotation_euler=(0,0,0);p.location=(0,0,0)
            bpy.context.view_layer.update();return result
        REF['actions'].__globals__['smoking_arm_rotations']=smoke
    return REF['actions'](arm,role)

def export_character(body,arm,role):
    OUT.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT');body.select_set(True);arm.select_set(True)
    for o in bpy.context.scene.objects:
        if o.name.startswith('ItemSocket.') or o.name=='MouthSocket':o.select_set(True)
    bpy.context.view_layer.objects.active=arm
    path=OUT/('character_'+role+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH','ARMATURE','EMPTY'},
        axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=False,add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',
        use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,
        path_mode='AUTO',mesh_smooth_type='FACE',use_mesh_modifiers=True)
    return str(path.relative_to(ROOT))

def prepare(info):
    body,stats=import_selected(info);spec=fitted_spec(body,info);stats['T_pose_markers']=spec
    stats['previews']=[render(body,info['role'],'selected_front'),render(body,info['role'],'selected_back',True)]
    (PREVIEW/(info['role']+'_selection.json')).write_text(json.dumps(stats,indent=2),encoding='utf-8')
    if AUDIT_ONLY:return stats
    if BODY_ONLY:
        prior=json.loads((ART/'character_manifest.json').read_text(encoding='utf-8'))
        prior=next(item for item in prior['characters'] if item['role']==info['role'])
        binding=runpy.run_path(str(ROOT/'Tools'/'Characters'/'export_source_hands.py'),run_name='atlas_binding_helpers')
        stats['atlas_binding']=binding['_use_atlas_without_writing'](body,info['role'],TEXTURES)
        stats['textures']=prior['textures'];stats['hands']=prior['hands']
    else:
        export_atlas(body,info['role'],stats)
        # Export canonical source hand parts before reposing the body, preserving
        # their original forearm, cuff and palm atlas coordinates.
        HANDS.mkdir(parents=True,exist_ok=True)
        helper=runpy.run_path(str(ROOT/'Tools'/'Characters'/'source_hand_segments.py'),run_name='hand_export_helpers')
        stats['hands']=helper['export_hand_parts'](body,spec,info['role'],HANDS/('hand_'+info['role']+'.fbx'))
    mouth,method=mouth_position(body,spec['top']);stats['mouth']={'position':list(mouth),'method':method}
    arm=REF['rig'](spec)
    bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='EDIT')
    for side,suffix in [(1,'L'),(-1,'R')]:
        bone=arm.data.edit_bones['hand.'+suffix];bone.tail=bone.head+Vector((side*.11,0,0))
    bpy.ops.object.mode_set(mode='OBJECT');weights(body,arm,spec,info['role'])
    stats['rest_markers']=lower_rest_arms(body,arm,spec);mouth_socket(arm,mouth)
    stats['clips']=source_actions(arm,info['role'],mouth);stats['bones']=len(arm.data.bones)
    bpy.context.scene.render.fps=24;bpy.context.scene.frame_set(1)
    stats['runtime']=export_character(body,arm,info['role'])
    ART.mkdir(parents=True,exist_ok=True);stats['editable_source']='Art/Characters/Imported/'+info['role']+'.blend'
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/(info['role']+'.blend')))
    stats['previews'].append(render(body,info['role'],'idle_front',pose=True))
    walk=bpy.data.actions.get('Walk');arm.animation_data.action=walk;bpy.context.scene.frame_set(9)
    stats['previews'].append(render(body,info['role'],'walk_front',pose=True))
    run=bpy.data.actions.get('Run');arm.animation_data.action=run;bpy.context.scene.frame_set(7)
    stats['previews'].append(render(body,info['role'],'run_front',pose=True))
    use=bpy.data.actions.get('UseItem');arm.animation_data.action=use;bpy.context.scene.frame_set(25)
    stats['previews'].append(render(body,info['role'],'use_item_front',pose=True))
    stats['source_unchanged']=hashlib.sha256(pathlib.Path(stats['source']).read_bytes()).hexdigest()==stats['sha256']
    if not stats['source_unchanged']:raise RuntimeError('Source file changed unexpectedly.')
    print('PREPARED_USER_CHARACTER',json.dumps(stats))
    return stats

selected=CAST
if '--role' in args:selected=[i for i in CAST if i['role']==args[args.index('--role')+1]]
if '--roles' in args:
    selected_roles=set(args[args.index('--roles')+1:])
    selected=[i for i in CAST if i['role'] in selected_roles]
if not selected:raise RuntimeError('No matching role was selected.')
reports=[prepare(info) for info in selected]
target=PREVIEW/'selection_manifest.json' if AUDIT_ONLY else ART/'character_manifest.json'
if len(selected)!=len(CAST) and target.exists():
    previous=json.loads(target.read_text(encoding='utf-8')).get('characters',[])
    by_role={item['role']:item for item in previous}
    by_role.update({item['role']:item for item in reports})
    reports=[by_role[info['role']] for info in CAST if info['role'] in by_role]
target.write_text(json.dumps({'coordinate_system':'Blender metres Z up -Y front; each source variant is rotated around Z according to its own face direction',
    'source_files_preserved':True,'characters':reports},ensure_ascii=False,indent=2),encoding='utf-8')
print('USER_CAST_PREPARATION_COMPLETE',target)
