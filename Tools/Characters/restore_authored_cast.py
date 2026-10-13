"""Restore the authored cast onto the editable IndieCast animation contract.

The imported .blend files are immutable source references. Their UV islands,
facial features and costume silhouettes survive refinement. Skinning is rebuilt
in the recovered source T pose before the new lowered-arm bind is applied.
"""
import bpy, bmesh, math, json, hashlib, shutil, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Art/Characters/RestoredCast'
TEXTURES=ROOT/'Assets/Characters/Textures'
SOURCE=ROOT/'Art/Characters/Imported'
ROLES=['operator','engineer','journalist','dispatcher']
EYES={'operator':(0,-.067,1.665),'engineer':(.005,-.145,1.655),
      'journalist':(.066,-.263,1.752),'dispatcher':(.005,-.258,1.884)}

def clamp(x,a=0,b=1):return max(a,min(b,x))
def smooth(x):x=clamp(x);return x*x*(3-2*x)
def blend(a,b,t):
    t=clamp(t)
    return {a:1-t,b:t} if 0<t<1 else {b if t>=1 else a:1}
def normalized(values):
    values={k:v for k,v in sorted(values.items(),key=lambda kv:-kv[1])[:4] if v>1e-5}
    total=sum(values.values())
    return {k:v/total for k,v in values.items()}

def source_vertex_parts(obj):
    """Use authored garment boundaries, not dark fabric mistaken for hair."""
    from collections import Counter
    names=json.loads(obj['authored_parts'])
    attribute=obj.data.attributes['authored_part_id']
    votes=[Counter() for _ in obj.data.vertices]
    for face,value in zip(obj.data.polygons,attribute.data):
        for index in face.vertices:votes[index][names[value.value]]+=1
    return [vote.most_common(1)[0][0] if vote else 'body' for vote in votes]

def skin_weights(obj):
    names={g.index:g.name for g in obj.vertex_groups}
    return [{names[g.group]:g.weight for g in v.groups} for v in obj.data.vertices]

def write_weights(obj,weights,arm):
    obj.vertex_groups.clear()
    for b in arm.data.bones:obj.vertex_groups.new(name=b.name)
    for index,values in enumerate(weights):
        for name,value in normalized(values).items():obj.vertex_groups[name].add([index],value,'REPLACE')

def components(obj):
    adjacency=[set() for _ in obj.data.vertices]
    for edge in obj.data.edges:
        a,b=edge.vertices;adjacency[a].add(b);adjacency[b].add(a)
    visited=set();result=[]
    for index in range(len(adjacency)):
        if index in visited:continue
        todo=[index];part=[]
        while todo:
            i=todo.pop()
            if i in visited:continue
            visited.add(i);part.append(i);todo.extend(adjacency[i]-visited)
        result.append(part)
    return result

def sample_colors(obj,image):
    import numpy as np
    pixels=np.asarray(image.pixels[:],dtype=np.float32).reshape(image.size[1],image.size[0],4)
    cols=np.zeros((len(obj.data.vertices),3),dtype=np.float32);counts=np.zeros(len(cols),dtype=np.float32)
    uv=obj.data.uv_layers.active
    for loop in obj.data.loops:
        u,v=uv.data[loop.index].uv
        x=round(clamp(u)* (image.size[0]-1));y=round(clamp(v)*(image.size[1]-1))
        cols[loop.vertex_index]+=pixels[y,x,:3];counts[loop.vertex_index]+=1
    counts[counts==0]=1
    return cols/counts[:,None]

def recover_source_t_pose(obj,arm,info):
    """Undo the old baked arm lowering exactly; never distort UVs to hide it."""
    original_weights=skin_weights(obj)
    old={b.name:b.matrix_local.copy() for b in arm.data.bones}
    bpy.context.view_layer.objects.active=arm
    bpy.ops.object.mode_set(mode='EDIT')
    spec=info['T_pose_markers']
    for side,suffix in [(1,'L'),(-1,'R')]:
        flip=lambda p:Vector((p[0]*side,p[1],p[2]))
        sh,el,wr=[flip(spec[k]) for k in ['shoulder','elbow','wrist']]
        # prepare_user_cast.py overrides the reference rig hand to horizontal
        # BEFORE its old bind lowering. Invert that exact bind, otherwise the
        # original fingers rotate 90 degrees and pinch the wrist on recovery.
        for name,a,b in [('upper_arm',sh,el),('forearm',el,wr),('hand',wr,wr+Vector((side*.11,0,0)))]:
            bone=arm.data.edit_bones[name+'.'+suffix];bone.head=a;bone.tail=b;bone.roll=0
    bpy.ops.object.mode_set(mode='OBJECT')
    transforms={b.name:old[b.name]@b.matrix_local.inverted() for b in arm.data.bones}
    for vertex,weights in zip(obj.data.vertices,original_weights):
        matrix=Matrix(((0,0,0,0),)*4)
        for name,weight in weights.items():matrix+=transforms[name]*weight
        vertex.co=matrix.inverted_safe()@vertex.co
    obj.data.update()
    return original_weights

def source_stats(obj):
    obj.data.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(obj.data)
    data=dict(vertices=len(obj.data.vertices),triangles=len(obj.data.loop_triangles),
              boundary_edges=sum(e.is_boundary for e in bm.edges),
              nonmanifold_edges=sum(not e.is_manifold for e in bm.edges),
              degenerate_faces=sum(f.calc_area()<1e-10 for f in bm.faces))
    bm.free();return data

def static_asset_audit(parts,arm):
    textures={};reports={}
    for part in parts:
        mesh=part.data;mesh.calc_loop_triangles()
        weights=skin_weights(part)
        bad_weights=sum(not w or abs(sum(w.values())-1)>1e-5 or len(w)>4 or any(n not in arm.data.bones for n in w) for w in weights)
        uv=mesh.uv_layers.active
        bad_uv=sum(not all(math.isfinite(v) and -.0001<=v<=1.0001 for v in loop.uv) for loop in uv.data) if uv else len(mesh.loops)
        degenerate=source_stats(part)['degenerate_faces']
        nonfinite=sum(not all(math.isfinite(value) for value in vertex.co) for vertex in mesh.vertices)
        reports[part.name]=dict(part=part.get('authored_part'),triangles=len(mesh.loop_triangles),
            invalid_skin_vertices=bad_weights,invalid_uv_loops=bad_uv,degenerate_faces=degenerate,nonfinite_vertices=nonfinite,
            materials=[m.name for m in mesh.materials])
        for material in mesh.materials:
            for node in material.node_tree.nodes:
                if node.type!='TEX_IMAGE' or not node.image:continue
                image=node.image;path=Path(bpy.path.abspath(image.filepath))
                try:relative=path.relative_to(ROOT).as_posix()
                except ValueError:relative=str(path)
                textures[relative]=dict(width=image.size[0],height=image.size[1])
    pixels=sum(item['width']*item['height'] for item in textures.values())
    result=dict(parts=reports,textures=textures,texture_pixels=pixels,texture_budget_pixels=2048*2048,
        triangles=sum(r['triangles'] for r in reports.values()))
    result['passed']=all(r['invalid_skin_vertices']==0 and r['invalid_uv_loops']==0 and r['degenerate_faces']==0 and r['nonfinite_vertices']==0 for r in reports.values()) and pixels<=2048*2048 and result['triangles']<=60000
    if not result['passed']:raise RuntimeError('Static garment integrity failed: '+json.dumps(result))
    return result

def validate_parts(parts,arm,clips,base):
    # Evaluate each animation frame once for the entire shared skeleton. Bulk
    # mesh reads avoid repeating Python vertex transforms for every garment.
    import numpy as np
    reports={}
    for part in parts:
        weights=skin_weights(part);geometry=source_stats(part)
        reports[part.name]=dict(unweighted_vertices=sum(not w for w in weights),
            max_weight_sum_error=max(abs(sum(w.values())-1) for w in weights),
            max_influences=max(len(w) for w in weights),nonfinite_vertices=0,min_floor_z=1e9,
            clip_floor={},loop_error={},**{k:geometry[k] for k in ['boundary_edges','nonmanifold_edges','degenerate_faces']})
    deps=bpy.context.evaluated_depsgraph_get()
    for info in clips:
        arm.animation_data.action=bpy.data.actions[info['name']]
        first={};floors={part.name:float('inf') for part in parts}
        for frame in range(1,info['frames']+1):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
            for part in parts:
                evaluated=part.evaluated_get(deps);mesh=evaluated.to_mesh()
                points=np.empty(len(mesh.vertices)*3,dtype=np.float32)
                mesh.vertices.foreach_get('co',points);evaluated.to_mesh_clear()
                transform=np.asarray(evaluated.matrix_world,dtype=np.float64)
                world=points.reshape(-1,3)@transform[:3,:3].T+transform[:3,3]
                report=reports[part.name]
                report['nonfinite_vertices']+=int(np.count_nonzero(~np.isfinite(world).all(axis=1)))
                floors[part.name]=min(floors[part.name],float(world[:,2].min()))
                if frame==1:first[part.name]=world
                elif frame==info['frames'] and info['loop']:
                    report['loop_error'][info['name']]=float(np.linalg.norm(world-first[part.name],axis=1).max())
        for part in parts:
            report=reports[part.name];report['clip_floor'][info['name']]=floors[part.name]
            report['min_floor_z']=min(report['min_floor_z'],floors[part.name])
    arm.animation_data.action=bpy.data.actions['Idle'];bpy.context.scene.frame_set(1)
    for report in reports.values():
        report['passed']=(report['unweighted_vertices']==0 and report['max_weight_sum_error']<1e-5
            and report['max_influences']<=4 and report['nonfinite_vertices']==0
            and report['min_floor_z']>-.008 and max(report['loop_error'].values())<1e-4
            and report['degenerate_faces']==0)
    return dict(parts=reports,
        unweighted_vertices=sum(r['unweighted_vertices'] for r in reports.values()),
        max_weight_sum_error=max(r['max_weight_sum_error'] for r in reports.values()),
        max_influences=max(r['max_influences'] for r in reports.values()),
        nonfinite_vertices=sum(r['nonfinite_vertices'] for r in reports.values()),
        min_floor_z=min(r['min_floor_z'] for r in reports.values()),
        degenerate_faces=sum(r['degenerate_faces'] for r in reports.values()),
        loop_error={clip:max(r['loop_error'].get(clip,0) for r in reports.values()) for clip in reports[next(iter(reports))]['loop_error']})

def make_rig(obj,old,info,colors):
    role=info['role'];spec=info['T_pose_markers'];height=info['height_m']
    bpy.context.view_layer.objects.active=old
    bpy.ops.object.mode_set(mode='EDIT')
    # Fix old hip/foot pivots that were behind the actual body (most evident on
    # Hazel's long hair-centred bounds). Recover each sole center from the mesh.
    origin_y={'operator':0,'engineer':.03,'journalist':.025,'dispatcher':-.20}[role]
    origin_x={'operator':0,'engineer':0,'journalist':.060,'dispatcher':0}[role]
    for name in ['root','pelvis','spine','chest','neck','head']:
        b=old.data.edit_bones[name]
        b.head.x+=origin_x;b.tail.x+=origin_x
        b.head.y+=origin_y;b.tail.y+=origin_y
    for side,suffix in [(1,'L'),(-1,'R')]:
        for name in ['thigh','shin','foot']:
            b=old.data.edit_bones[name+'.'+suffix]
            b.head.x+=origin_x;b.tail.x+=origin_x;b.head.y+=origin_y;b.tail.y+=origin_y
        sh=old.data.edit_bones['upper_arm.'+suffix].head.copy()
        clav=old.data.edit_bones.new('clavicle.'+suffix);clav.head=(origin_x,origin_y,sh.z);clav.tail=sh;clav.parent=old.data.edit_bones['chest']
        old.data.edit_bones['upper_arm.'+suffix].parent=clav
        ankle=old.data.edit_bones['foot.'+suffix].head.copy()
        foot=old.data.edit_bones['foot.'+suffix]
        foot.tail=(ankle.x,origin_y-.08,.055)
        toe=old.data.edit_bones.new('toe.'+suffix);toe.head=foot.tail;toe.tail=(ankle.x,origin_y-.17,.04);toe.parent=foot
        hand=old.data.edit_bones['hand.'+suffix];wrist=hand.head.copy()
        # The source hands are horizontal in T pose; the old down-facing hand
        # axes caused palm twists. New longitudinal axes follow the fingers.
        hand.tail=wrist+Vector((side*.075,0,0))
        palmend=hand.tail.copy()
        for j,finger in enumerate(['index','middle','ring','little']):
            length=[.064,.073,.068,.052][j]*(1.25 if role=='engineer' else 1)
            start=palmend+Vector((0,(j-1.5)*.015,0))
            prev=hand
            for k,t in enumerate([.38,.73,1]):
                b=old.data.edit_bones.new(finger+'.%02d.'%(k+1)+suffix)
                b.head=start+Vector((side*length*([0,.38,.73][k]),0,0));b.tail=start+Vector((side*length*t,0,0));b.parent=prev;prev=b
        start=wrist+Vector((side*.023,-.034,0));prev=hand
        for k in range(3):
            b=old.data.edit_bones.new('thumb.%02d.'%(k+1)+suffix);b.head=start+Vector((side*.014*k,-.011*k,0));b.tail=b.head+Vector((side*.014,-.011,0));b.parent=prev;prev=b
    bpy.ops.object.mode_set(mode='OBJECT')
    old.name='Armature';old.data.name='RestoredAuthoredSkeleton'
    return origin_x,origin_y

def semantic_groups(obj,role,colors,info):
    """Classify on immutable source UV/material evidence before any deformation."""
    groups={k:set() for k in ['hair','cloth','tie','breast.L','breast.R']}
    partitions=components(obj)
    for part in partitions:
        pts=[obj.data.vertices[i].co for i in part]
        lo=Vector(tuple(min(p[a] for p in pts) for a in range(3)));hi=Vector(tuple(max(p[a] for p in pts) for a in range(3)))
        if role=='engineer' and hi.z-lo.z>.85 and hi.x-lo.x<.23 and hi.z>1.4 and lo.z<.5:groups['tie'].update(part)
        if role=='dispatcher' and .4<lo.z<.75 and hi.z<1.4 and hi.x-lo.x>.2:groups['cloth'].update(part)
    for v,rgb in zip(obj.data.vertices,colors):
        x,y,z=v.co;r,g,b=rgb;gray=(r+g+b)/3
        if role=='operator':
            if z>1.49 and gray>.47 and abs(r-b)<.18 and (abs(x)>.065 or y>.005 or z>1.735):groups['hair'].add(v.index)
            if .57<z<1.08 and r>g*1.4 and r>b*.9 and r>.07:groups['cloth'].add(v.index)
        elif role=='dispatcher':
            if .88<z<2.0 and gray<.26 and r>g*1.05 and (y>-.08 or abs(x)>.11):groups['hair'].add(v.index)
        elif role=='journalist':
            if 1.08<z<1.63 and y<-.22 and -.14<x<.28 and gray>.4:groups['hair'].add(v.index)
            if .59<z<1.10 and (g>r*1.07 or b>r*1.07) and -.34<y<.4:groups['cloth'].add(v.index)
        if role in ['operator','dispatcher']:
            centerz=1.34 if role=='operator' else 1.45
            cy=-.095 if role=='operator' else -.245
            if abs(z-centerz)<.115 and y<cy and .018<abs(x)<.18:
                groups['breast.L' if x>=0 else 'breast.R'].add(v.index)
    # Vivian's three dense silver necklaces are separate components around the
    # neck. Their pale highlights are jewelry, never bob-hair deformation.
    if role=='operator':
        for part in partitions:
            pts=[obj.data.vertices[i].co for i in part]
            if len(part)>100 and max(p.z for p in pts)<1.57 and min(p.z for p in pts)>1.45:
                groups['hair'].difference_update(part)
    labels=source_vertex_parts(obj)
    groups['hair']={i for i,label in enumerate(labels) if label in {'hair','beard'}}
    if role=='dispatcher':groups['cloth']={i for i,label in enumerate(labels) if label=='wrap_skirt'}
    if role=='engineer':groups['tie']={i for i,label in enumerate(labels) if label=='tie'}
    # Preserve the dress/coat opening while excluding painted skin, buttons,
    # and trim from cloth bones at shared source surface boundaries.
    if role in {'operator','journalist'}:
        groups['cloth']={i for i in groups['cloth'] if labels[i] in {'dress','jacket'}}
    return groups

def rebuild_weights(obj,arm,info,groups):
    spec=info['T_pose_markers'];role=info['role'];values=[]
    sh=Vector(spec['shoulder']);el=Vector(spec['elbow']);wr=Vector(spec['wrist'])
    labels=source_vertex_parts(obj)
    # Source color/part classifications explicitly exclude long hair, hems and
    # a dangling tie from the arm proximity rule.
    for v in obj.data.vertices:
        x,y,z=v.co;side='L' if x>=0 else 'R';ax=abs(x)
        center=sh.lerp(el,clamp((ax-sh.x)/(el.x-sh.x))) if ax<el.x else el.lerp(wr,clamp((ax-el.x)/(wr.x-el.x)))
        thickness=info['height_m']*(.19 if role=='dispatcher' else .13 if role=='engineer' else .105)
        in_arm=ax>sh.x*.80 and abs(z-center.z)<thickness and abs(y-center.y)<.23
        # Hazel's flared sleeves extend far below the forearm in T pose. Their
        # full bell must lower with the arm, including its wide hanging edge.
        if labels[v.index]=='sleeves':in_arm=True
        if v.index in groups['hair']:weight={'head':1}
        elif v.index in groups['tie']:weight={'chest':1}
        elif v.index in groups['cloth']:weight={'pelvis':1}
        elif in_arm:
            if ax>wr.x-.02:
                weight={'hand.'+side:1}
                if ax>wr.x+.055:
                    names=[n for n in arm.data.bones.keys() if n.endswith('.'+side) and any(n.startswith(f) for f in ['thumb.','index.','middle.','ring.','little.'])]
                    def distance(name):
                        b=arm.data.bones[name];a=b.head_local;d=b.tail_local-a;t=clamp((v.co-a).dot(d)/d.length_squared);return (v.co-a-d*t).length
                    closest=sorted(names,key=distance)[:2];dist=[distance(n) for n in closest]
                    if dist[0]<.031:weight={n:1/max(d,.003)**3 for n,d in zip(closest,dist)}
            elif ax>el.x-.055:weight=blend('upper_arm.'+side,'forearm.'+side,smooth((ax-el.x+.055)/.11))
            else:weight=blend('chest','upper_arm.'+side,smooth((ax-sh.x*.80)/(sh.x*.4)))
        elif z>spec['headbase']-.025:weight={'head':1}
        elif z>sh.z-.025:weight=blend('chest','neck',smooth((z-sh.z+.025)/max(.04,spec['headbase']-sh.z)))
        elif z<spec['hip']*.95:
            ankle=spec['ankle'][2];knee=spec['knee'][2]
            if z<ankle+.015:weight={'foot.'+side:1}
            elif z<ankle+.075:weight=blend('foot.'+side,'shin.'+side,smooth((z-ankle-.015)/.06))
            elif z<knee+.075:weight=blend('shin.'+side,'thigh.'+side,smooth((z-knee+.075)/.15))
            else:weight={'thigh.'+side:1}
        elif z<spec['waist']-.07:weight={'pelvis':1}
        elif z<spec['waist']+.07:weight=blend('pelvis','spine',smooth((z-spec['waist']+.07)/.14))
        elif z<spec['chest']+.08:weight=blend('spine','chest',smooth((z-spec['chest']+.08)/.16))
        else:weight={'chest':1}
        values.append(normalized(weight))
    write_weights(obj,values,arm)

def lower_new_bind(obj,arm):
    for side,suffix in [(1,'L'),(-1,'R')]:
        p=arm.pose.bones['upper_arm.'+suffix];basis=p.bone.matrix_local.to_quaternion()
        p.rotation_mode='QUATERNION';p.rotation_quaternion=basis.inverted()@Quaternion((0,1,0),math.radians(68)*side)@basis
    bpy.context.view_layer.update()
    bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('Rebuilt source bind deformation','ARMATURE');mod.object=arm
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='POSE');bpy.ops.pose.armature_apply(selected=False);bpy.ops.object.mode_set(mode='OBJECT')
    for p in arm.pose.bones:p.rotation_mode='QUATERNION';p.rotation_quaternion=(1,0,0,0)

def add_secondary(obj,arm,role,groups,base):
    weights=skin_weights(obj);chains={};tips=[]
    bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='EDIT')
    def chain(label,points,parent):
        names=[]
        for i,(a,b) in enumerate(zip(points,points[1:])):
            name=label+'.%02d'%(i+1);bone=arm.data.edit_bones.new(name);bone.head=a;bone.tail=b;bone.parent=arm.data.edit_bones[parent if i==0 else names[-1]];names.append(name)
        chains[label]=(names,[Vector(p) for p in points]);tips.append((names[-1],Vector(points[-1])))
        return names
    if groups['hair'] and role!='engineer':
        pts=[obj.data.vertices[i].co for i in groups['hair']];zmin=min(p.z for p in pts);zmax=max(p.z for p in pts)
        for suffix,side in [('L',1),('R',-1),('back',0)]:
            if suffix=='back' and role=='journalist':continue
            subset=[p for p in pts if (p.x*side>.035 if side else p.y>0)]
            if not subset:continue
            x=sum(p.x for p in subset)/len(subset);y=sum(p.y for p in subset)/len(subset)
            top=zmax-.055;bottom=zmin+.025
            n=2 if role=='operator' else 3
            chain('secondary_hair.'+suffix,[(x,y,top+(bottom-top)*i/n) for i in range(n+1)],'head')
    if groups['cloth']:
        for side,suffix in [(1,'L'),(-1,'R')]:
            subset=[obj.data.vertices[i].co for i in groups['cloth'] if obj.data.vertices[i].co.x*side>=0]
            if not subset:continue
            x=sum(p.x for p in subset)/len(subset);y=sum(p.y for p in subset)/len(subset);top=max(p.z for p in subset);bottom=min(p.z for p in subset)
            chain('secondary_cloth.'+suffix,[(x,y,top),(x,y,(top+bottom)*.5),(x,y,bottom)],'thigh.'+suffix)
    if groups['tie']:
        pts=[obj.data.vertices[i].co for i in groups['tie']];top=max(p.z for p in pts);bottom=min(p.z for p in pts)
        points=[]
        for i in range(4):
            z=top+(bottom-top)*i/3;near=sorted(pts,key=lambda p:abs(p.z-z))[:max(2,len(pts)//8)]
            points.append((sum(p.x for p in near)/len(near),sum(p.y for p in near)/len(near),z))
        chain('secondary_cloth.tie',points,'chest')
    for suffix in ['L','R']:
        indices=groups['breast.'+suffix]
        if not indices:continue
        pts=[obj.data.vertices[i].co for i in indices];center=sum(pts,Vector())/len(pts)
        name='secondary_breast.'+suffix;bone=arm.data.edit_bones.new(name);bone.head=center+Vector((0,.018,0));bone.tail=center+Vector((0,-.055,0));bone.parent=arm.data.edit_bones['chest'];tips.append((name,bone.tail.copy()))
    bpy.ops.object.mode_set(mode='OBJECT')
    def chain_weight(label,p,root):
        names,points=chains[label];top=points[0].z;bottom=points[-1].z;t=clamp((top-p.z)/max(.001,top-bottom))*len(names)
        segment=min(len(names)-1,int(t));fraction=t-segment
        return blend(root if segment==0 else names[segment-1],names[segment],smooth(fraction))
    for v in obj.data.vertices:
        side='L' if v.co.x>=0 else 'R'
        if v.index in groups['hair']:
            label='secondary_hair.'+('back' if v.co.y>0.035 and role!='journalist' else side)
            if label not in chains:label='secondary_hair.'+side
            if label in chains:weights[v.index]=chain_weight(label,v.co,'head')
        if v.index in groups['cloth']:
            label='secondary_cloth.'+side
            if label in chains:weights[v.index]=chain_weight(label,v.co,'pelvis')
        if v.index in groups['tie']:weights[v.index]=chain_weight('secondary_cloth.tie',v.co,'chest')
        if v.index in groups['breast.'+side]:
            name='secondary_breast.'+side
            center=arm.data.bones[name].head_local
            factor=.8*math.exp(-((v.co.x-center.x)/.065)**2-((v.co.z-center.z)/.075)**2)
            weights[v.index]=blend('chest',name,factor)
    write_weights(obj,weights,arm)
    for bone,point in tips:base['marker'](arm,bone+'_tip',bone,point)
    return {b.name:dict(head=list(b.head_local),tail=list(b.tail_local),parent=b.parent.name) for b in arm.data.bones if b.name.startswith('secondary_')}

def refine_surface(obj):
    bpy.context.view_layer.objects.active=obj
    bm=bmesh.new();bm.from_mesh(obj.data)
    before=len(bm.faces)
    bmesh.ops.join_triangles(bm,faces=list(bm.faces),angle_face_threshold=.52,angle_shape_threshold=.70,
                           cmp_seam=True,cmp_sharp=True,cmp_uvs=True,cmp_vcols=True,cmp_materials=True)
    quad_count=sum(len(f.verts)==4 for f in bm.faces)
    # Mark original sharp creases. UV seams are retained as loop data and never
    # collapsed or replaced with palette samples.
    for e in bm.edges:
        if e.is_manifold and e.calc_face_angle()>math.radians(62):e.smooth=False
    bm.to_mesh(obj.data);bm.free()
    sub=obj.modifiers.new('Deformation support tessellation','SUBSURF');sub.subdivision_type='SIMPLE';sub.levels=1;sub.render_levels=1;sub.uv_smooth='PRESERVE_BOUNDARIES'
    bpy.ops.object.modifier_apply(modifier=sub.name)
    # A low-amplitude Laplacian pass rounds long planar joins while preserving
    # source proportions. Face/eye/lip UVs remain their original affine maps.
    smoothmod=obj.modifiers.new('Controlled source surface finish','SMOOTH');smoothmod.factor=.18;smoothmod.iterations=2
    bpy.ops.object.modifier_apply(modifier=smoothmod.name)
    for f in obj.data.polygons:f.use_smooth=True
    obj.data.set_sharp_from_angle(angle=math.radians(68))
    weights=skin_weights(obj);write_weights(obj,weights,obj.parent)
    return dict(recovered_quad_regions=quad_count,source_faces=before,
                support_subdivision='one simple subdivision; exact interpolated UV islands',
                surface_finish='two restrained 0.18 Laplacian passes; smooth normals with 68-degree sharp creases')

def spec_markers(arm,info,base):
    role=info['role'];s=dict(base['CAST'][role]);b=arm.data.bones
    s.update(height=info['height_m'],hip=b['pelvis'].head_local.z,waist=b['spine'].head_local.z,
             chest=b['chest'].head_local.z,shoulder=b['upper_arm.L'].head_local.z,
             neck=b['head'].head_local.z,knee=b['shin.L'].head_local.z,
             ankle=b['foot.L'].head_local.z,upper=b['upper_arm.L'].length,lower=b['forearm.L'].length)
    markers={}
    for suffix in ['L','R']:
        markers[suffix]={k:b[n+'.'+suffix].head_local.copy() for k,n in [('shoulder','upper_arm'),('elbow','forearm'),('wrist','hand'),('hip','thigh'),('knee','shin'),('ankle','foot')]}
        markers[suffix]['palmend']=b['hand.'+suffix].tail_local.copy()
    return s,markers

def preview_rest(obj,arm,role,spec,base,suffix,views_to_render=None):
    for a in list(bpy.data.actions):bpy.data.actions.remove(a)
    arm.animation_data_create();arm.animation_data.action=bpy.data.actions.new('Idle')
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8;scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED';scene.render.threads=4
    scene.render.resolution_percentage=100
    if scene.world is None:scene.world=bpy.data.worlds.new('Restored cast studio')
    scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.15,.19,.22,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=0
    staging=[]
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012));floor=bpy.context.object;staging.append(floor)
    mat=bpy.data.materials.new('Neutral studio floor');mat.diffuse_color=(.105,.135,.15,1);floor.data.materials.append(mat)
    for location,power,size in [((-3,-4,5),450,4),((4,-2,3.5),220,3),((0,3,5),480,3)]:
        bpy.ops.object.light_add(type='AREA',location=location);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size
        light.rotation_euler=(Vector((0,0,spec['height']*.5))-light.location).to_track_quat('-Z','Y').to_euler();staging.append(light)
    bpy.ops.object.camera_add();camera=bpy.context.object;camera.data.type='ORTHO';scene.camera=camera;staging.append(camera)
    eye=Vector(EYES[role]);target=Vector((eye.x,0,spec['height']*.50))
    views=[('front',Vector((eye.x,-5,spec['height']*.5+.05)),target,spec['height']*1.10,640,800)]
    if suffix=='after_refinement':
        views.append(('face',eye+Vector((.38,-1.3,.07)),eye+Vector((0,.03,-.025)),.52 if role!='journalist' else .74,640,640))
        views.append(('back',Vector((-.4,5,spec['height']*.5+.12)),target,spec['height']*1.10,640,800))
        views.append(('three_quarter',Vector((2,-5,spec['height']*.5+.13)),target,spec['height']*1.10,640,800))
        if role=='operator':
            views.append(('bodice',Vector((.28,-3,1.44)),Vector((0,-.02,1.385)),.54,800,800))
    for view,location,target,scale,w,h in views:
        if views_to_render is not None and view not in views_to_render:continue
        camera.location=location;camera.rotation_euler=(target-location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
        scene.render.resolution_x=w;scene.render.resolution_y=h
        scene.render.filepath=str(ART/'Previews'/(role+'_'+suffix+'_'+view+'.png'));bpy.ops.render.render(write_still=True)
    for item in staging:bpy.data.objects.remove(item,do_unlink=True)

def build(base,role,skip_render=False,no_export=False,front_only=False,preview_only=False):
    sys.path.insert(0,str(ROOT/'Tools/Characters'))
    import modular_outfits
    import source_surface_finish
    import vivian_bodice
    import vivian_body_finish
    import vivian_legwear
    import restored_weight_finish
    import restored_cloth_pose
    ART.mkdir(parents=True,exist_ok=True)
    (ART/'Previews').mkdir(parents=True,exist_ok=True)
    info=next(c for c in json.loads((SOURCE/'character_manifest.json').read_text())['characters'] if c['role']==role)
    source=SOURCE/(role+'.blend');digest=hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(source))
    obj=next(o for o in bpy.data.objects if o.type=='MESH' and len(o.data.vertices)>500)
    arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
    arm.animation_data_clear()
    for p in arm.pose.bones:p.location=(0,0,0);p.rotation_mode='QUATERNION';p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
    for o in list(bpy.data.objects):
        if o!=obj and o!=arm:bpy.data.objects.remove(o,do_unlink=True)
    obj.modifiers.clear();obj.parent=None;obj.matrix_world=Matrix.Identity(4)
    atlas=TEXTURES/(role+'_restored_albedo.png')
    shutil.copy2(SOURCE/(role+'_source_albedo.png'),atlas)
    image=bpy.data.images.load(str(atlas),check_existing=False)
    for node in obj.data.materials[0].node_tree.nodes:
        if node.type=='TEX_IMAGE':node.image=image
    # Copy the actual authored atlas. No positional material guesses, baked
    # palette dots, color tint multipliers or UV reconstruction at import time.
    image.name=role+'_restored_albedo';image.pack()
    material=obj.data.materials[0];material.name=role+'_restored_atlas';material.diffuse_color=(1,1,1,1)
    bs=material.node_tree.nodes.get('Principled BSDF')
    if bs:bs.inputs['Roughness'].default_value=.78
    for n in material.node_tree.nodes:
        if n.type=='TEX_IMAGE':n.interpolation='Linear'
    original=source_stats(obj);colors=sample_colors(obj,image)
    recover_source_t_pose(obj,arm,info)
    part_tags=modular_outfits.tag_source_parts(obj,role,image)
    groups=semantic_groups(obj,role,colors,info)
    make_rig(obj,arm,info,colors)
    rebuild_weights(obj,arm,info,groups)
    lower_new_bind(obj,arm)
    obj.parent=arm
    secondary=add_secondary(obj,arm,role,groups,base)
    s,markers=spec_markers(arm,info,base)
    if not skip_render:preview_rest(obj,arm,role,s,base,'before_refinement')
    topology=source_surface_finish.refine_surface(obj,role)
    write_weights(obj,skin_weights(obj),arm)
    obj.name=info['name']+'_RestoredBody';skin=obj.modifiers.new('Restored authored skin','ARMATURE');skin.object=arm
    bpy.context.scene.render.fps=base['FPS']
    eye=Vector(EYES[role]);mouth=Vector(info['mouth']['position'])
    base['marker'](arm,'EyeSocket','head',eye);base['marker'](arm,'MouthSocket','head',mouth)
    base['marker'](arm,'IndieCast','root',Vector((0,0,0)));base['marker'](arm,'RestoredCast','root',Vector((0,0,0)))
    front=-min(v.co.y for v in obj.data.vertices if abs(v.co.x)<.16 and s['waist']<v.co.z<s['shoulder'])
    base['marker'](arm,'TorsoFront','chest',Vector((0,-front,s['chest'])))
    contacts={}
    for suffix,m in markers.items():
        grip=arm.data.bones['hand.'+suffix].tail_local.copy()+Vector((0,-.02,-.015))
        for name in ['ItemSocket.','GripContact.']:base['marker'](arm,name+suffix,'hand.'+suffix,grip)
        foot_indices=[v.index for v,w in zip(obj.data.vertices,skin_weights(obj)) if w.get('foot.'+suffix,0)+w.get('toe.'+suffix,0)>.75]
        pts=[obj.data.vertices[i].co for i in foot_indices];bottom=min(p.z for p in pts)
        sole=[p for p in pts if p.z<bottom+.018]
        x=sum(p.x for p in sole)/len(sole);y=sum(p.y for p in sole)/len(sole)
        contact=Vector((x,y,bottom));toe=Vector((x,min(p.y for p in sole),bottom))
        base['marker'](arm,'FootContact.'+suffix,'foot.'+suffix,contact);base['marker'](arm,'FootToeContact.'+suffix,'toe.'+suffix,toe);contacts[suffix]=list(contact)
    parts,modular_report=modular_outfits.modularize(obj,arm,role,TEXTURES/'Restored',ART)
    garment_finish={part.name:modular_outfits.finish_vivian_dress_caps(part) for part in parts if role=='operator' and part.get('authored_part')=='dress'}
    body_finish={};legwear_finish={}
    if role=='operator':
        body_finish=vivian_body_finish.finish_vivian_body(parts,arm)
        parts,legwear_finish=vivian_legwear.finish_vivian_legwear(parts,arm)
    hair_finish={part.name:source_surface_finish.finish_hair_shell(part,role) for part in parts if part.get('authored_part')=='hair'}
    bodice_report={}
    if role=='operator':parts,bodice_report=vivian_bodice.add_vivian_bodice(parts,arm,TEXTURES/'Restored')
    weight_finish=restored_weight_finish.finish_restored_weights(parts,arm,role)
    part_stats={part.name:source_stats(part) for part in parts}
    stats={key:sum(value[key] for value in part_stats.values()) for key in next(iter(part_stats.values()))}
    static_audit=static_asset_audit(parts,arm)
    static_report=dict(role=role,name=info['name'],source=str(source.relative_to(ROOT)),source_sha256=digest,
        original=original,refined=stats,parts=part_stats,topology=topology,modular=modular_report,
        hair_finish=hair_finish,garment_finish=garment_finish,body_finish=body_finish,legwear_finish=legwear_finish,
        bodice=bodice_report,weight_finish=weight_finish,secondary_bones=secondary,static_integrity=static_audit)
    (ART/(role+'_model.json')).write_text(json.dumps(static_report,indent=2),encoding='utf-8')
    if preview_only:
        if not skip_render:preview_rest(parts,arm,role,s,base,'after_refinement',['front','face','back'])
        bpy.ops.wm.save_as_mainfile(filepath=str(ART/(role+'_model.blend')))
        print('RESTORED STATIC MODEL '+role+' '+json.dumps(static_report),flush=True)
        return static_report
    clips=base['animate'](arm,s,markers)
    cloth_pose=restored_cloth_pose.bake_restored_cloth_pose(parts,arm,clips)
    validation=validate_parts(parts,arm,clips,base)
    validation['source_surface_boundaries']=original['boundary_edges']
    validation['topology_note']='Authored clothing openings/eyelashes are intentional open surfaces; weight, finite, floor and loop gates are independent.'
    validation['passed']=validation['unweighted_vertices']==0 and validation['max_weight_sum_error']<1e-5 and validation['max_influences']<=4 and validation['nonfinite_vertices']==0 and validation['min_floor_z']>-.008 and max(validation['loop_error'].values())<1e-4 and validation['degenerate_faces']==0
    if not validation['passed']:raise RuntimeError(role+' restored deformation failed '+json.dumps(validation))
    if not skip_render:base['preview'](parts[0],arm,role,s,front_only)
    out=ART/(role+'.blend');bpy.ops.wm.save_as_mainfile(filepath=str(out))
    bpy.ops.object.select_all(action='DESELECT');arm.select_set(True)
    for part in parts:part.select_set(True)
    for item in bpy.data.objects:
        if item.type=='EMPTY' and item.parent==arm:item.select_set(True)
    bpy.context.view_layer.objects.active=arm
    export=ROOT/'Assets/Resources/Characters'/('character_'+role+'.fbx')
    if not no_export:bpy.ops.export_scene.fbx(filepath=str(export),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=False,add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',use_armature_deform_only=True,bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,path_mode='AUTO',embed_textures=False)
    unity=lambda p:[round(p[0],6),round(p[2],6),round(-p[1],6)]
    result=dict(role=role,name=info['name'],**stats,bones=len(arm.data.bones),bone_names=[b.name for b in arm.data.bones],height_m=s['height'],source_eye=unity(eye),mouth=unity(mouth),torso_front_m=front,atlas=str(atlas.relative_to(ROOT)).replace('\\','/'),material=material.name,source=str(out.relative_to(ROOT)).replace('\\','/'),runtime=str(export.relative_to(ROOT)).replace('\\','/'),restored_source=str(source.relative_to(ROOT)).replace('\\','/'),restored_source_sha256=digest,original_geometry=original,refinement=topology,secondary_bones=secondary,semantic_source_vertices={k:len(v) for k,v in groups.items()},clips=clips,validation=validation,foot_contacts={k:unity(v) for k,v in contacts.items()},shoulder_positions={k:unity(v['shoulder']) for k,v in markers.items()},skin_color=base['CAST'][role]['skin'],sleeve_color=base['CAST'][role]['primary'],cuff_color=base['CAST'][role]['secondary'])
    result.pop('atlas',None);result.pop('material',None)
    result.update(parts=static_audit['parts'],textures=static_audit['textures'],texture_pixels=static_audit['texture_pixels'],
        body_finish=body_finish,legwear_finish=legwear_finish,bodice=bodice_report,weight_finish=weight_finish,cloth_pose=cloth_pose)
    (ART/(role+'_manifest.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('RESTORED CAST READY '+json.dumps({k:result[k] for k in ['role','vertices','triangles','bones','validation']}),flush=True)
    return result
