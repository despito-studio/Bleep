"""Fit the restored cast's skeleton to its own geometry, on review copies.

Apply surface/garment corrections first, then call fit_individual_rig(parts,
arm, role). Names remain the game contract; bind joints and digit lengths do
not come from a shared humanoid template. No production file is saved here.
"""
import bpy
import math
import json
import hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Art/Characters/ReviewCandidates/Rigs'
PROFILE = {
    # Heights are anatomical transitions measured in the authored silhouettes:
    # the women's narrow forward knee is above the broad calf, not inside it.
    'operator': dict(hip=1.015, knee=.690, ankle=.155, abduction=6, elbow_flex=9),
    # Victor's wide coat needs clearance all the way to the wrist. A narrow
    # human-template arm angle puts both hands inside his side panels.
    'engineer': dict(hip=.610, knee=.350, ankle=.135, abduction=18, elbow_flex=8, forearm_abduction=24),
    'journalist': dict(hip=.815, knee=.415, ankle=.155, abduction=7, elbow_flex=12),
    'dispatcher': dict(hip=1.005, knee=.750, ankle=.160, abduction=5, elbow_flex=10),
}
FINGERS = ('index', 'middle', 'ring', 'little')


def _smooth(x):
    x=max(0.,min(1.,x));return x*x*(3-2*x)


def _weights(obj):
    names={g.index:g.name for g in obj.vertex_groups}
    return [{names[g.group]:g.weight for g in v.groups} for v in obj.data.vertices]


def _write(obj, index, values):
    values={n:w for n,w in sorted(values.items(),key=lambda item:-item[1])[:4] if w>1e-7}
    total=sum(values.values())
    for group in obj.vertex_groups:group.remove([index])
    for name,weight in values.items():
        group=obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        group.add([index],weight/total,'REPLACE')


def _middle(points):
    p=np.asarray(points,dtype=float)
    return Vector((np.quantile(p,.08,axis=0)+np.quantile(p,.92,axis=0))*.5)


def _section(points, origin, axis, width=.025):
    """Cross-section centre in the plane, with no along-axis joint drift."""
    origin=Vector(origin);axis=Vector(axis).normalized()
    ranked=sorted(((abs((Vector(p)-origin).dot(axis)),Vector(p)) for p in points),key=lambda item:item[0])
    chosen=[p for d,p in ranked if d<width]
    if len(chosen)<8:chosen=[p for _,p in ranked[:min(16,len(ranked))]]
    if not chosen:return origin.copy()
    center=_middle(chosen);return center-axis*(center-origin).dot(axis)


def _points(parts, weights, suffix, prefixes, labels=None, threshold=.45):
    result=[]
    for obj in parts:
        if labels and obj.get('authored_part') not in labels:continue
        for v,w in zip(obj.data.vertices,weights[obj.name]):
            total=sum(value for name,value in w.items() if name.endswith('.'+suffix) and any(name.startswith(p) for p in prefixes))
            if total>=threshold:result.append(v.co.copy())
    return result


def _snapshot(arm):
    return {b.name:dict(head=list(b.head_local),tail=list(b.tail_local),length=b.length,
                        rest_quaternion=list(b.matrix_local.to_quaternion())) for b in arm.data.bones}


def _topology(parts):
    rows=[dict(name=o.name,faces=[list(p.vertices) for p in o.data.polygons],
               uv=[[list(v.uv) for v in layer.data] for layer in o.data.uv_layers],
               materials=[m.name for m in o.data.materials]) for o in sorted(parts,key=lambda o:o.name)]
    return hashlib.sha256(json.dumps(rows,separators=(',',':')).encode()).hexdigest()


def _fit_digits(edit, suffix, hand_points, wrist, old_axis):
    axis=old_axis.normalized();width=Vector((0,1,0));width-=axis*width.dot(axis);width.normalize()
    normal=axis.cross(width).normalized()
    local=np.asarray([[(p-wrist).dot(axis),(p-wrist).dot(width),(p-wrist).dot(normal)] for p in hand_points])
    total=float(np.quantile(local[:,0],.99));palm=max(.025,total*.44)
    # Find the four long fingers beyond the thumb tip. Seeding clusters at
    # the palm also captures Hazel's thumb as a fifth, very short index.
    seeds=local[local[:,0]>total*.60]
    centers=np.quantile(seeds[:,1],[.10,.35,.65,.90])
    for _ in range(12):
        assigned=np.argmin(abs(seeds[:,1,None]-centers[None,:]),axis=1)
        centers=np.array([np.mean(seeds[assigned==i,1]) if np.any(assigned==i) else centers[i] for i in range(4)])
        centers.sort()
    thumb_boundary=centers[0]-(centers[1]-centers[0])*.55
    distal=local[(local[:,0]>palm*.90)&(local[:,1]>thumb_boundary)]
    def world(t,w,n):return wrist+axis*float(t)+width*float(w)+normal*float(n)
    paths={};knuckles=[]
    for i,finger in enumerate(FINGERS):
        owned=distal[np.argmin(abs(distal[:,1,None]-centers[None,:]),axis=1)==i]
        if len(owned)<3:owned=distal
        end=max(palm+.015,float(np.quantile(owned[:,0],.98)))
        path=[]
        for t in [palm,palm+(end-palm)*.38,palm+(end-palm)*.73,end]:
            band=owned[np.argsort(abs(owned[:,0]-t))[:max(6,len(owned)//7)]]
            path.append(world(t,float(np.median(band[:,1])),float((band[:,2].min()+band[:,2].max())*.5)))
        paths[finger]=path;knuckles.append(path[0])
    thumb=local[(local[:,1]<np.quantile(local[:,1],.18))&(local[:,0]<total*.83)]
    if len(thumb)<4:thumb=local[np.argsort(local[:,1])[:max(4,len(local)//5)]]
    tip=thumb[np.argmin(thumb[:,1])]
    root=world(palm*.30,float(np.quantile(local[:,1],.20))*.5,float(np.median(local[:,2])))
    end=world(*tip);paths['thumb']=[root,root.lerp(end,.36),root.lerp(end,.72),end]
    hand=edit['hand.'+suffix];hand.head=wrist;hand.tail=sum(knuckles,Vector())/4
    hand.align_roll(normal)
    for finger,path in paths.items():
        for k in range(3):
            bone=edit[finger+'.%02d.'%(k+1)+suffix];bone.head=path[k];bone.tail=path[k+1];bone.align_roll(normal)
    return dict(total_surface_length=total,palm_length=hand.length,
                digit_lengths={f:sum((b-a).length for a,b in zip(p,p[1:])) for f,p in paths.items()},
                paths=paths,axis=axis,wrist=wrist,palm=palm)


def _turn_edgar_shoes(parts, weights, ankles):
    """Turn the backward-facing source slippers without rotating the leg.

    Source cuff rings are preserved as a set by cyclically matching their
    rotated vertices. This keeps the separate trousers closed at the ankle.
    """
    shoes=next(o for o in parts if o.get('authored_part')=='shoes')
    trousers=next(o for o in parts if o.get('authored_part')=='trousers')
    coords={tuple(round(c,5) for c in v.co):v.index for v in trousers.data.vertices}
    changed=0;seams=[]
    for side,suffix in [(1,'L'),(-1,'R')]:
        indices=[v.index for v,w in zip(shoes.data.vertices,weights[shoes.name]) if sum(value for name,value in w.items() if name.endswith('.'+suffix))>.5]
        seam=[i for i in indices if tuple(round(c,5) for c in shoes.data.vertices[i].co) in coords]
        center=ankles[suffix];before={i:shoes.data.vertices[i].co.copy() for i in indices}
        for i in indices:
            p=before[i];shoes.data.vertices[i].co=(2*center.x-p.x,2*center.y-p.y,p.z);changed+=1
        # The upper seam consists of separate horizontal source rings.
        rings={}
        for i in seam:rings.setdefault(round(before[i].z,4),[]).append(i)
        for ring in rings.values():
            if len(ring)<3:
                for i in ring:shoes.data.vertices[i].co=before[i]
                continue
            ordered=sorted(ring,key=lambda i:math.atan2(before[i].y-center.y,before[i].x-center.x))
            k=min(range(len(ordered)),key=lambda shift:sum((shoes.data.vertices[i].co-before[ordered[(j+shift)%len(ordered)]]).length_squared for j,i in enumerate(ordered)))
            for j,i in enumerate(ordered):shoes.data.vertices[i].co=before[ordered[(j+k)%len(ordered)]]
        seams+=seam
    shoes.data.update()
    return dict(rotated_vertices=changed,closed_cuff_vertices=len(seams),yaw_degrees=180)


def fit_individual_rig(parts, arm, role):
    if arm.get('individual_rig_fit_v1'):return json.loads(arm['individual_rig_fit_v1'])
    cfg=PROFILE[role];before=_snapshot(arm);topology=_topology(parts)
    weights={o.name:_weights(o) for o in parts}
    markers={o.name:Vector(o.get('authored_world_position',o.matrix_world.translation)) for o in bpy.context.scene.objects if o.type=='EMPTY' and o.parent==arm}
    arm.animation_data_clear()
    for p in arm.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    limb={};digits={};leg_points={};arm_points={};hand_points={}
    for suffix in ('L','R'):
        leg_points[suffix]=_points(parts,weights,suffix,('thigh.','shin.','foot.'),{'body','trousers','stockings'},.6)
        arm_points[suffix]=_points(parts,weights,suffix,('upper_arm.','forearm.'),None,.40)
        hand_points[suffix]=_points(parts,weights,suffix,('hand.','index.','middle.','ring.','little.','thumb.'),{'body'},.55)
        old=arm.data.bones;points=leg_points[suffix]
        joints={}
        for key,z in [('hip',cfg['hip']),('knee',cfg['knee']),('ankle',cfg['ankle'])]:
            center=_section(points,Vector((0,0,z)),Vector((0,0,1)),.028 if key!='hip' else .07)
            # The last hidden upper-thigh cross-section is below the hip joint.
            center.z=z;joints[key]=center
        ratio=(cfg['hip']-cfg['knee'])/(cfg['hip']-cfg['ankle'])
        straight=joints['hip'].lerp(joints['ankle'],ratio)
        joints['knee'].y=min(joints['knee'].y,straight.y-.015)
        for key,bone in [('shoulder','upper_arm.'),('elbow','forearm.'),('wrist','hand.')]:
            b=old[bone+suffix];axis=b.tail_local-b.head_local
            cloud=hand_points[suffix] if key=='wrist' else arm_points[suffix]
            sample=b.head_local+axis.normalized()*.025 if key=='shoulder' else b.head_local
            center=_section(cloud,sample,axis,.025)
            if key=='shoulder':center-=axis.normalized()*.025
            delta=center-b.head_local
            if delta.length>.04:center=b.head_local+delta.normalized()*.04
            joints[key]=center
        limb[suffix]=joints
    shoes_turn=_turn_edgar_shoes(parts,weights,{s:limb[s]['ankle'] for s in limb}) if role=='journalist' else None
    bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='EDIT');edit=arm.data.edit_bones
    for suffix in ('L','R'):
        j=limb[suffix]
        for name,a,b in [('thigh','hip','knee'),('shin','knee','ankle'),('upper_arm','shoulder','elbow'),('forearm','elbow','wrist')]:
            bone=edit[name+'.'+suffix];bone.head=j[a];bone.tail=j[b];bone.align_roll(Vector((0,-1,0)))
        edit['clavicle.'+suffix].tail=j['shoulder']
        digits[suffix]=_fit_digits(edit,suffix,hand_points[suffix],j['wrist'],Vector(before['hand.'+suffix]['tail'])-Vector(before['hand.'+suffix]['head']))
        shoe=_points(parts,weights,suffix,('foot.','toe.'),{'shoes'},.55)
        front=sorted(shoe,key=lambda p:p.y)[:max(6,len(shoe)//7)]
        tip=_middle(front);sole=min(p.z for p in shoe);tip.z=sole+.018
        ball=j['ankle'].lerp(tip,.70);ball.z=sole+.040
        edit['foot.'+suffix].head=j['ankle'];edit['foot.'+suffix].tail=ball;edit['foot.'+suffix].align_roll(Vector((0,0,1)))
        edit['toe.'+suffix].head=ball;edit['toe.'+suffix].tail=tip;edit['toe.'+suffix].align_roll(Vector((0,0,1)))
        j['ball']=ball;j['toe']=tip
    hip=(limb['L']['hip']+limb['R']['hip'])*.5
    edit['pelvis'].head=hip
    # Fit neck/head depth independently from shoulder/leg templates. Hair,
    # hats and beard never determine the skull pivot or skull length.
    body=next(o for o in parts if o.get('authored_part')=='body')
    center_x=(limb['L']['shoulder'].x+limb['R']['shoulder'].x)*.5
    head_z=edit['head'].head.z
    head_cloud=[v.co.copy() for v in body.data.vertices if abs(v.co.x-center_x)<.13 and v.co.z>head_z-.07]
    if len(head_cloud)>12:
        base=_section(head_cloud,edit['head'].head,Vector((0,0,1)),.025)
        top_z=float(np.quantile([p.z for p in head_cloud],.985))
        top=_section(head_cloud,Vector((base.x,base.y,top_z)),Vector((0,0,1)),.02)
        edit['neck'].tail=base;edit['head'].head=base;edit['head'].tail=top
        neckbase=edit['neck'].head.copy();neckbase.x=base.x
        neckbase.y=(limb['L']['shoulder'].y+limb['R']['shoulder'].y)*.5
        edit['neck'].head=neckbase
    bpy.ops.object.mode_set(mode='OBJECT');bpy.context.view_layer.update()
    # Move knee/ankle skin blends with their actual new joints. Coincident
    # cloth/shoe vertices use exactly the same position-based rule.
    weight_changes=0
    for obj in parts:
        for v,old_weights in zip(obj.data.vertices,weights[obj.name]):
            leg_sum=sum(w for n,w in old_weights.items() if n.startswith(('thigh.','shin.','foot.','toe.')))
            # Vivian's reconstructed stockings inherited pelvis-heavy weights
            # from the old shorter leg template. A weight threshold splits a
            # single ring into old pelvis skinning and new thigh skinning,
            # stretching neighbouring 8mm edges beyond 40mm in Idle. Cover the
            # semantic leg garments completely, and include the inner thigh
            # skin so its coincident cuff follows the same continuous rule.
            leg_garment=(role=='operator' and obj.get('authored_part') in {'stockings','garters'})
            upper_skin=(obj.get('authored_part')=='body' and cfg['hip']*.84<v.co.z<cfg['hip']
                        and (role=='operator' or .025<abs(v.co.x-hip.x))
                        and abs(v.co.x-hip.x)<(.2 if role in {'operator','dispatcher'} else .26))
            if leg_sum<.6 and not upper_skin and not leg_garment:continue
            suffix=max(('L','R'),key=lambda s:sum(w for n,w in old_weights.items() if n.endswith('.'+s))) if leg_sum>=.6 else ('L' if v.co.x>hip.x else 'R')
            z=v.co.z;ankle=cfg['ankle'];knee=cfg['knee']
            top=knee+(.14 if role=='engineer' else .08) if role in {'engineer','journalist'} else ankle+.11
            foot=1-_smooth((z-ankle-.025)/max(.025,top-ankle-.025))
            thigh=_smooth((z-knee+.065)/.13)
            desired={'foot.'+suffix:foot,'shin.'+suffix:(1-foot)*(1-thigh),'thigh.'+suffix:(1-foot)*thigh}
            if obj.get('authored_part')=='shoes':
                ball=limb[suffix]['ball'];tip=limb[suffix]['toe']
                direction=(tip-ball).normalized()
                # The final foot joint is a genuine ball-of-foot bend. Keep
                # a short blend across it instead of making every toe rigid.
                toe=_smooth(((v.co-ball).dot(direction)+.012)/.045)
                desired['toe.'+suffix]=foot*toe
                desired['foot.'+suffix]=foot*(1-toe)
            else:
                # The hip transition must still belong partly to the pelvis.
                # Replacing an existing mixed hip with 100% thigh pinches the
                # groin when the shorter fitted femur swings or crouches.
                pelvic=_smooth((z-cfg['hip']+.08)/.16)
                desired={name:value*(1-pelvic) for name,value in desired.items()}
                desired['pelvis']=pelvic
            _write(obj,v.index,desired);weight_changes+=1
    # Fitted digit bones receive only their own anatomical finger region.
    for obj in parts:
        if obj.get('authored_part')!='body':continue
        for v,w in zip(obj.data.vertices,weights[obj.name]):
            for suffix in ('L','R'):
                influence=sum(x for n,x in w.items() if n.endswith('.'+suffix) and n.startswith(('hand.','thumb.','index.','middle.','ring.','little.')))
                if influence<.7:continue
                info=digits[suffix];along=(v.co-info['wrist']).dot(info['axis'])
                if along<info['palm']*.60:_write(obj,v.index,{'hand.'+suffix:1});continue
                distances=[]
                for family,path in info['paths'].items():
                    for k,(a,b) in enumerate(zip(path,path[1:])):
                        d=b-a;t=max(0,min(1,(v.co-a).dot(d)/max(1e-10,d.length_squared)))
                        distances.append(((v.co-a-d*t).length,family,k))
                distances.sort();family=distances[0][1]
                selected=[entry for entry in distances if entry[1]==family][:2]
                desired={family+'.%02d.'%(k+1)+suffix:1/max(.004,distance)**2 for distance,_,k in selected}
                amount=_smooth((along-info['palm']*.6)/(info['palm']*.45))
                total=sum(desired.values());desired={n:x/total*amount for n,x in desired.items()};desired['hand.'+suffix]=1-amount
                _write(obj,v.index,desired)
    # Bake each hero's own relaxed shoulder/elbow silhouette into the bind.
    for side,suffix in [(1,'L'),(-1,'R')]:
        upper=arm.pose.bones['upper_arm.'+suffix]
        angle=math.radians(cfg['abduction']);direction=Vector((side*math.sin(angle),.025,-math.cos(angle))).normalized()
        q=(upper.tail-upper.head).rotation_difference(direction)
        upper.matrix=Matrix.LocRotScale(upper.head,q@upper.matrix.to_quaternion(),Vector((1,1,1)))
        bpy.context.view_layer.update()
        lower=arm.pose.bones['forearm.'+suffix];flex=math.radians(cfg['elbow_flex'])
        lateral=math.radians(cfg.get('forearm_abduction',.86))
        direction=Vector((side*math.sin(lateral),-math.sin(flex),-math.cos(lateral)*math.cos(flex))).normalized()
        q=(lower.tail-lower.head).rotation_difference(direction)
        lower.matrix=Matrix.LocRotScale(lower.head,q@lower.matrix.to_quaternion(),Vector((1,1,1)))
        bpy.context.view_layer.update()
    transforms={b.name:arm.pose.bones[b.name].matrix@b.matrix_local.inverted() for b in arm.data.bones}
    max_move=0;changed_vertices=0
    for obj in parts:
        for v,w in zip(obj.data.vertices,_weights(obj)):
            if not any(n.startswith(('upper_arm.','forearm.','hand.','index.','middle.','ring.','little.','thumb.')) for n in w):continue
            p=sum(((transforms[n]@v.co)*weight for n,weight in w.items()),Vector())
            delta=(p-v.co).length
            if delta>1e-7:v.co=p;max_move=max(max_move,delta);changed_vertices+=1
        obj.data.update()
    bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='POSE');bpy.ops.pose.armature_apply(selected=False);bpy.ops.object.mode_set(mode='OBJECT')
    for p in arm.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    # Re-anchor explicit sockets after editing rest matrices. Eye/mouth data
    # comes from the surface artist; the fitted hands and soles own theirs.
    for obj in bpy.context.scene.objects:
        if obj.type!='EMPTY' or obj.parent!=arm:continue
        point=markers.get(obj.name,obj.matrix_world.translation.copy())
        for suffix in ('L','R'):
            if obj.name=='HandGrip.'+suffix:point=arm.data.bones['hand.'+suffix].head_local.lerp(arm.data.bones['hand.'+suffix].tail_local,.75)
            if obj.name in {'FootContact.'+suffix,'FootToeContact.'+suffix}:
                shoe=_points(parts,{o.name:_weights(o) for o in parts},suffix,('foot.','toe.'),{'shoes'},.55)
                low=min(p.z for p in shoe);floor=[p for p in shoe if p.z<low+.008]
                point=_middle(floor)
                if obj.name.startswith('FootToe'):point=min(floor,key=lambda p:p.y).copy()
                point.z=low
        obj.matrix_world=Matrix.Translation(point);obj['authored_world_position']=list(point)
    after=_snapshot(arm)
    assert _topology(parts)==topology,'Rig fitting must preserve face topology, UVs and material slots'
    report=dict(role=role,profile=cfg,bones_before=before,bones_after=after,
                digit_fit={s:{k:v for k,v in d.items() if k in {'total_surface_length','palm_length','digit_lengths'}} for s,d in digits.items()},
                leg_weight_vertices=weight_changes,bind_moved_vertices=changed_vertices,max_bind_shift_m=max_move,
                shoes_orientation=shoes_turn,topology_uv_material_sha256=topology,
                retarget=dict(space='Blender Z up, -Y forward',reference='fitted rest',
                  arm_rule='source sagittal motion relative to fitted neutral; do not copy source Idle abduction',
                  knee_poles={s:list(Vector(after['shin.'+s]['head'])+Vector((0,-.45,0))) for s in ('L','R')}))
    arm['individual_rig_fit_v1']=json.dumps(report)
    return report


def main():
    import sys
    sys.path.insert(0,str(ROOT/'Tools/Characters'))
    import restore_authored_cast as restore
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    roles=args[args.index('--roles')+1].split(',') if '--roles' in args else list(PROFILE)
    OUT.mkdir(parents=True,exist_ok=True)
    manifest=OUT/'rig_fit_manifest.json'
    reports_by_role={r['role']:r for r in json.loads(manifest.read_text(encoding='utf-8'))} if manifest.exists() else {}
    for role in roles:
        bpy.ops.wm.open_mainfile(filepath=str(restore.ART/(role+'_model.blend')))
        parts=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('authored_part')]
        arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
        report=fit_individual_rig(parts,arm,role)
        report['static_audit']=restore.static_asset_audit(parts,arm)
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(role+'_rig.blend')))
        (OUT/(role+'_rig.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
        print('FITTED RIG',role,'moved',report['bind_moved_vertices'],'leg weights',report['leg_weight_vertices'],flush=True)
        reports_by_role[role]=report
    manifest.write_text(json.dumps([reports_by_role[r] for r in PROFILE if r in reports_by_role],indent=2),encoding='utf-8')


if __name__=='__main__':main()
