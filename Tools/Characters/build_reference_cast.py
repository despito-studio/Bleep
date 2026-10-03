"""Dead Air reference cast. Run with Blender 4.5 --background --python this_file.

All geometry, weights, materials and actions are authored here, in metres.
Blender coordinates: Z up, -Y forward. FBX: Y up, +Z forward in Unity.
The .blend files are the editable art source; FBXs are the runtime exchange.
"""
import bpy, bmesh, math, json, os, random, sys
from mathutils import Vector, Matrix, Quaternion
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'Art' / 'Characters'
OUT = ROOT / 'Assets' / 'Resources' / 'Characters'
PREVIEW = ART / 'Previews'
for p in (ART, OUT, PREVIEW): p.mkdir(parents=True, exist_ok=True)
random.seed(73)
PARTS=[]
MATS={}

def material(name, rgb, roughness=.83, metallic=0):
    if name in MATS: return MATS[name]
    m=bpy.data.materials.new(name); m.diffuse_color=(*rgb,1)
    m.use_nodes=True
    # Factory startup and different UI languages must produce identical materials.
    m.node_tree.nodes.clear()
    bs=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output=m.node_tree.nodes.new('ShaderNodeOutputMaterial')
    m.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface'])
    bs.inputs['Base Color'].default_value=(*rgb,1)
    bs.inputs['Roughness'].default_value=roughness
    bs.inputs['Metallic'].default_value=metallic
    MATS[name]=m; return m

def palette():
    return {k:material(k,v) for k,v in {
      'Porcelain':(.72,.49,.42),'SkinLight':(.88,.66,.57),'SkinShadow':(.48,.27,.24),
      'OliveGold':(.63,.38,.10),'GoldSkinLight':(.85,.59,.21),'GoldSkinShadow':(.40,.23,.075),
      'Sallow':(.53,.39,.29),'SallowLight':(.67,.52,.39),'SallowDark':(.36,.25,.20),
      'Burgundy':(.20,.035,.065),'BurgundyLight':(.31,.065,.10),'BurgundyDark':(.095,.017,.029),
      'Stockings':(.024,.025,.031),'BootLeather':(.018,.019,.025),'BodiceBlack':(.045,.043,.052),
      'WhiteHair':(.89,.85,.82),'HairShade':(.55,.48,.53),'HairHighlight':(.98,.94,.85),
      'DarkHair':(.026,.013,.009),'DarkHairLight':(.075,.036,.015),'DarkHairGlow':(.14,.064,.023),
      'Suit':(.019,.029,.033),'SuitLight':(.034,.050,.054),'SuitShadow':(.009,.014,.018),
      'Shirt':(.69,.68,.58),'ShirtLight':(.83,.81,.69),'ShirtShade':(.44,.46,.41),
      'Tie':(.29,.12,.045),'TieGold':(.60,.36,.12),'TieGreen':(.13,.24,.14),
      'Teal':(.008,.12,.13),'TealLight':(.017,.18,.18),'TealDark':(.007,.059,.068),
      'Beard':(.86,.83,.67),'BeardShade':(.53,.48,.40),'BeardWarm':(.72,.62,.47),
      'Gold':(.77,.44,.09),'GoldLight':(.98,.70,.25),
      'DressRed':(.55,.034,.042),'DressPink':(.85,.17,.23),'DressCoral':(.77,.078,.10),
      'LipDark':(.085,.011,.023),'LipRed':(.42,.025,.049),'LipScarlet':(.67,.021,.035),
      'EyeWhite':(.84,.84,.69),'EyeBlack':(.009,.006,.007),'IrisGreen':(.014,.28,.15),
      'IrisHoney':(.42,.23,.027),'Highlight':(.99,.95,.86),'Silver':(.65,.66,.65),
      'PackCream':(.79,.74,.59),'PackRed':(.47,.022,.034),'Paper':(.85,.81,.68),
    }.items()}

def mesh(name, verts, faces, mat, bone=None, weights=None):
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob)
    ob.data.materials.append(mat)
    if bone:
        vg=ob.vertex_groups.new(name=bone); vg.add(list(range(len(verts))),1,'REPLACE')
    if weights:
        for group, entries in weights.items():
            vg=ob.vertex_groups.get(group) or ob.vertex_groups.new(name=group)
            for vi,w in entries: vg.add([vi],w,'REPLACE')
    PARTS.append(ob); return ob

def loft(name, rings, mat, bone=None, sides=8, phase=math.pi/8, caps=True, weights_fn=None):
    # ring: centre_x, centre_y, z, radius_x, radius_y
    v=[]
    for x,y,z,rx,ry in rings:
        for j in range(sides):
            a=phase+2*math.pi*j/sides; v.append((x+math.cos(a)*rx,y+math.sin(a)*ry,z))
    f=[]
    for r in range(len(rings)-1):
        for j in range(sides): f.append((r*sides+j,r*sides+(j+1)%sides,(r+1)*sides+(j+1)%sides,(r+1)*sides+j))
    if caps: f.extend([tuple(range(sides-1,-1,-1)),tuple((len(rings)-1)*sides+j for j in range(sides))])
    w={}
    if weights_fn:
        for i,pt in enumerate(v):
            for bn,val in weights_fn(pt).items(): w.setdefault(bn,[]).append((i,val))
    return mesh(name,v,f,mat,bone,w)

def tube(name, points, radii, mat, bone, sides=6):
    pts=[Vector(p) for p in points]; verts=[]
    for k,p in enumerate(pts):
        tangent=(pts[min(k+1,len(pts)-1)]-pts[max(0,k-1)]).normalized()
        a=tangent.cross(Vector((0,1,0)))
        if a.length<.01: a=tangent.cross(Vector((1,0,0)))
        a.normalize(); b=tangent.cross(a).normalized()
        rad=radii[k] if isinstance(radii,list) else radii
        ra,rb=rad if isinstance(rad,tuple) else (rad,rad)
        for j in range(sides):
            q=p+a*math.cos(2*math.pi*j/sides)*ra+b*math.sin(2*math.pi*j/sides)*rb
            verts.append(tuple(q))
    faces=[]
    for k in range(len(pts)-1):
        for j in range(sides): faces.append((k*sides+j,k*sides+(j+1)%sides,(k+1)*sides+(j+1)%sides,(k+1)*sides+j))
    faces.extend([tuple(range(sides-1,-1,-1)),tuple((len(pts)-1)*sides+j for j in range(sides))])
    return mesh(name,verts,faces,mat,bone)

def poly(name, points, mat, bone, thickness=.005):
    n=len(points); v=list(points)+[(x,y+thickness,z) for x,y,z in points]
    f=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]
    f += [(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)]
    return mesh(name,v,f,mat,bone)

def cube(name,loc,scale,mat,bone,bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); ob=bpy.context.object
    ob.name=name; ob.dimensions=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=ob.modifiers.new('Hand cut corners','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.data.materials.append(mat)
    vg=ob.vertex_groups.new(name=bone); vg.add(list(range(len(ob.data.vertices))),1,'REPLACE')
    PARTS.append(ob); return ob

def ellipsoid(name,loc,scale,mat,bone,segments=10,rings=5):
    rs=[]
    for j in range(rings+1):
        a=-math.pi/2+(math.pi*j/rings)
        rs.append((loc[0],loc[1],loc[2]+math.sin(a)*scale[2],max(.003,math.cos(a)*scale[0]),max(.003,math.cos(a)*scale[1])))
    return loft(name,rs,mat,bone,sides=segments,phase=math.pi/segments)

def stripe(name, pts, width, mat, bone):
    # polyline in front XZ plane, with flat ribbon, useful for seams and brows
    if len(pts)==2:
        p,q=Vector(pts[0]),Vector(pts[1]); vec=(q-p).normalized(); n=Vector((-vec.z,0,vec.x))*width/2
        return poly(name,[tuple(p+n),tuple(q+n),tuple(q-n),tuple(p-n)],mat,bone)
    return tube(name,pts,width/2,mat,bone,4)

def chain(name,points,mat,bone,rad=.006):
    return tube(name,points,rad,mat,bone,5)

def bone_weight(z):
    z=z[2] if isinstance(z,(tuple,list,Vector)) else z
    if z<1.00:return {'pelvis':1}
    if z<1.16:
        t=(z-1)/.16;return {'pelvis':1-t,'spine':t}
    if z<1.3:
        t=(z-1.16)/.14;return {'spine':1-t,'chest':t}
    return {'chest':1}

def face_female(P,center,skin,lip,iris='IrisGreen',style='vivian',width=1):
    x,y,z=center; sx=.145*width
    loft('Sculpted angular face',[(x,y,z-.165,.063*width,.066),(x,y,z-.115,.112*width,.096),
      (x,y,z-.035,.148*width,.12),(x,y,z+.065,.145*width,.128),(x,y+.004,z+.145,.118*width,.104),
      (x,y+.008,z+.185,.053*width,.053)],P[skin],'head',sides=10,phase=-math.pi/2)
    # Cheek planes and sharp little jaw.
    for s in [-1,1]:
        # The face loft supplies the cheek planes. Separate light triangles here
        # formed detached fins in oblique views, rather than an angular cheek.
        # almond eyes with the characteristic angled corners
        ey=z+.038; ex=x+s*.064*width; depth=y-.118
        outline=[(ex-s*.047*width,depth,ey+.005),(ex-s*.006*width,depth-.010,ey+.027),(ex+s*.049*width,depth+.007,ey+.020),
                 (ex+s*.035*width,depth+.002,ey-.010),(ex-s*.017*width,depth-.010,ey-.009)]
        poly('Kohl eyeliner',outline,P['EyeBlack'],'head')
        white=[(ex-s*.037*width,depth-.005,ey+.006),(ex,depth-.016,ey+.020),(ex+s*.037*width,depth-.002,ey+.015),
               (ex+s*.022*width,depth-.008,ey-.004),(ex-s*.014*width,depth-.015,ey-.005)]
        poly('Almond eye white',white,P['EyeWhite'],'head')
        ellipsoid('Iris', (ex,depth-.018,ey+.008),(.014,.003,.017),P[iris],'head',8,3)
        ellipsoid('Pupil',(ex,depth-.022,ey+.008),(.006,.002,.014),P['EyeBlack'],'head',6,3)
        ellipsoid('Eye glint',(ex-.004,depth-.025,ey+.016),(.003,.001,.004),P['Highlight'],'head',4,2)
        if style=='hazel':
            stripe('Expressive brow',[(ex-s*.042,depth+.005,ey+.051),(ex+s*.045,depth+.006,ey+.046)],.012,P['DarkHair'],'head')
        # Ears have an outer and inner plane.
        ellipsoid('Ear',(x+s*.145*width,y+.004,z-.018),(.023,.016,.043),P[skin],'head',6,4)
        ellipsoid('Ear fold',(x+s*.149*width,y-.011,z-.018),(.012,.004,.026),P['SkinShadow' if skin=='Porcelain' else 'GoldSkinShadow'],'head',5,3)
    mesh('Tapered nose',[(x-.016,y-.117,z+.025),(x+.016,y-.117,z+.025),(x+.025,y-.145,z-.040),
      (x,y-.164,z-.047),(x-.023,y-.145,z-.040),(x,y-.137,z-.063)],[(0,1,3),(1,2,3),(0,3,4),(4,3,5),(3,2,5)],P[skin],'head')
    mouth_y=y-.105
    if style=='hazel':
        poly('Smile dark opening',[(x-.064,mouth_y-.004,z-.083),(x-.026,mouth_y-.012,z-.072),(x+.061,mouth_y-.005,z-.081),
          (x+.033,mouth_y-.012,z-.111),(x-.008,mouth_y-.017,z-.12),(x-.044,mouth_y-.010,z-.102)],P['LipDark'],'head')
        poly('Smile teeth',[(x-.050,mouth_y-.019,z-.083),(x+.046,mouth_y-.019,z-.085),(x+.027,mouth_y-.020,z-.096),(x-.029,mouth_y-.02,z-.096)],P['Highlight'],'head')
        stripe('Smiling lower lip',[(x-.039,mouth_y-.024,z-.106),(x,mouth_y-.027,z-.118),(x+.035,mouth_y-.024,z-.104)],.012,P[lip],'head')
        poly('Upper lip',[(x-.062,mouth_y-.017,z-.083),(x-.020,mouth_y-.028,z-.070),(x,mouth_y-.026,z-.077),(x+.025,mouth_y-.022,z-.073),(x+.061,mouth_y-.012,z-.082),(x,mouth_y-.029,z-.085)],P[lip],'head')
    else:
        poly('Dark lipstick',[(x-.042,mouth_y-.011,z-.080),(x-.012,mouth_y-.021,z-.071),(x,mouth_y-.018,z-.079),(x+.018,mouth_y-.02,z-.073),
          (x+.043,mouth_y-.009,z-.084),(x+.011,mouth_y-.023,z-.108),(x-.019,mouth_y-.02,z-.103)],P[lip],'head')
        stripe('Mouth line',[(x-.03,mouth_y-.025,z-.086),(x+.029,mouth_y-.025,z-.088)],.004,P['EyeBlack'],'head')

def hands(P,side,wrist,mat='Porcelain',size=1):
    s=side; suffix='L' if s>0 else 'R'; x,y,z=wrist; bone='hand.'+suffix
    ellipsoid('Palm '+suffix,(x,y-.013,z-.041*size),(.033*size,.025*size,.060*size),P[mat],bone,6,4)
    # Four clearly readable fingers; not a mitten.
    for j in range(4):
        fx=x+(j-1.5)*.015*size; length=[.060,.073,.070,.051][j]*size
        tube('Finger %s %s'%(j,suffix),[(fx,y-.014,z-.068*size),(fx,y-.020,z-.090*size),(fx,y-.034,z-.068*size-length)],
          [.0095*size,.0085*size,.006*size],P[mat],bone,5)
    tube('Thumb '+suffix,[(x-s*.025*size,y-.010,z-.037*size),(x-s*.052*size,y-.027,z-.061*size),
      (x-s*.050*size,y-.043,z-.087*size)],[.014*size,.012*size,.008*size],P[mat],bone,5)

def shoes(P,side,x,z=.095,heavy=1,mat='BootLeather'):
    s=side; bone='foot.'+('L' if s>0 else 'R')
    loft('Angular shoe',[(x,-.060,z-.07,.062*heavy,.14*heavy),(x,-.075,z-.035,.066*heavy,.155*heavy),
        (x,-.065,z+.033,.058*heavy,.13*heavy),(x,-.008,z+.08,.046*heavy,.053*heavy)],P[mat],bone,8)
    loft('Sole welt',[(x,-.067,z-.077,.066*heavy,.151*heavy),(x,-.067,z-.056,.067*heavy,.155*heavy)],P['SuitShadow'],bone,8)

def rig(spec):
    bpy.ops.object.select_all(action='DESELECT')
    ar=bpy.data.armatures.new('ReferenceCharacterSkeleton'); ob=bpy.data.objects.new('Armature',ar)
    bpy.context.collection.objects.link(ob); bpy.context.view_layer.objects.active=ob; ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    defs={
      'root':((0,0,0),(0,0,.16),None),
      'pelvis':((0,0,spec['hip']),(0,0,spec['waist']),'root'),
      'spine':((0,0,spec['waist']),(0,0,spec['chest']),'pelvis'),
      'chest':((0,0,spec['chest']),(0,0,spec['shoulder'][2]),'spine'),
      'neck':((0,0,spec['shoulder'][2]),(0,0,spec['headbase']),'chest'),
      'head':((0,0,spec['headbase']),(0,0,spec['top']),'neck')}
    for side,suffix in [(1,'L'),(-1,'R')]:
        sh=spec['shoulder'];el=spec['elbow'];wr=spec['wrist'];hip=spec['hipx'];kne=spec['knee'];foot=spec['ankle']
        flip=lambda q:(q[0]*side,q[1],q[2])
        defs['upper_arm.'+suffix]=(flip(sh),flip(el),'chest')
        defs['forearm.'+suffix]=(flip(el),flip(wr),'upper_arm.'+suffix)
        defs['hand.'+suffix]=(flip(wr),(wr[0]*side,wr[1]-.02,wr[2]-.13),'forearm.'+suffix)
        defs['thigh.'+suffix]=((hip*side,0,spec['hip']),(kne[0]*side,kne[1],kne[2]),'pelvis')
        defs['shin.'+suffix]=((kne[0]*side,kne[1],kne[2]),(foot[0]*side,foot[1],foot[2]),'thigh.'+suffix)
        defs['foot.'+suffix]=((foot[0]*side,foot[1],foot[2]),(foot[0]*side,-.20,.045),'shin.'+suffix)
    for name,(head,tail,parent) in defs.items():
        b=ar.edit_bones.new(name);b.head=head;b.tail=tail
        if parent:b.parent=ar.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT');ar.display_type='STICK';ob.show_in_front=True
    # Explicit sockets are exported as empties, parented to hands.
    for suffix in ['L','R']:
        em=bpy.data.objects.new('ItemSocket.'+suffix,None);bpy.context.collection.objects.link(em)
        em.empty_display_type='ARROWS';em.empty_display_size=.06;em.parent=ob;em.parent_type='BONE';em.parent_bone='hand.'+suffix
        em.location=(0,-.08,0)
    return ob

def bind_join(arm,name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in PARTS: ob.select_set(True)
    bpy.context.view_layer.objects.active=PARTS[0];bpy.ops.object.join();ob=bpy.context.object;ob.name=name+'_Body'
    # Triangulate explicitly, retaining the authored planes and material regions.
    mod=ob.modifiers.new('Game triangulation','TRIANGULATE');mod.quad_method='BEAUTY';bpy.ops.object.modifier_apply(modifier=mod.name)
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    bm.to_mesh(ob.data);bm.free();ob.data.update()
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    mod=ob.modifiers.new('Armature deformation','ARMATURE');mod.object=arm;ob.parent=arm
    for polyface in ob.data.polygons:polyface.use_smooth=False
    return ob

def smoking_arm_rotations(arm):
    """Solve the authored right arm so fingers meet the operator's mouth."""
    for pb in arm.pose.bones:pb.rotation_mode='XYZ'
    upper=arm.pose.bones['upper_arm.R'];fore=arm.pose.bones['forearm.R'];hand=arm.pose.bones['hand.R']
    shoulder=upper.bone.head_local.copy();target=Vector((-.060,-.205,1.495))
    direction=target-shoulder;distance=direction.length;direction.normalize()
    length_a=upper.bone.length;length_b=fore.bone.length
    along=(length_a*length_a-length_b*length_b+distance*distance)/(2*distance)
    height=math.sqrt(max(0,length_a*length_a-along*along))
    outside=Vector((-1,0,-.4));outside-=direction*outside.dot(direction);outside.normalize()
    elbow=shoulder+direction*along+outside*height
    result={}
    for pb,start,end in [(upper,shoulder,elbow),(fore,elbow,target),
                        (hand,target,target+Vector((.063,.049,.105)))]:
        rest_direction=pb.bone.tail_local-pb.bone.head_local
        rotation=rest_direction.rotation_difference(end-start)@pb.bone.matrix_local.to_quaternion()
        pb.matrix=Matrix.LocRotScale(start,rotation,Vector((1,1,1)))
        bpy.context.view_layer.update()
        result[pb.name]=pb.rotation_euler.to_quaternion()
    for pb in arm.pose.bones:pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
    bpy.context.view_layer.update()
    return result

def actions(arm,classid):
    arm.animation_data_create();clips=[]
    smoke_rotations=smoking_arm_rotations(arm) if classid=='operator' else {}
    for clip,count in [('Idle',49),('Walk',33),('Run',25),('UseItem',49)]:
        act=bpy.data.actions.new(clip); arm.animation_data.action=act; act.use_fake_user=True
        for frame in range(1,count+1):
            t=(frame-1)/(count-1);wave=math.sin(t*math.tau);opp=math.sin(t*math.tau+math.pi)
            for pb in arm.pose.bones:pb.rotation_mode='XYZ';pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
            if clip=='Idle':
                arm.pose.bones['chest'].rotation_euler[0]=.018*wave
                arm.pose.bones['head'].rotation_euler[2]=.025*wave
                arm.pose.bones['pelvis'].location[1]=.005*wave
                for s in ['L','R']:arm.pose.bones['forearm.'+s].rotation_euler[0]=-.045+.015*wave
            if clip in ('Walk','Run'):
                a=.49 if clip=='Walk' else .79
                for s,v in [('L',wave),('R',opp)]:
                    arm.pose.bones['thigh.'+s].rotation_euler[0]=a*v
                    arm.pose.bones['shin.'+s].rotation_euler[0]=-.53*max(0,-v) if clip=='Walk' else -1.05*max(0,-v)
                    arm.pose.bones['foot.'+s].rotation_euler[0]=.13*v
                    arm.pose.bones['upper_arm.'+s].rotation_euler[0]=-a*.7*v
                    arm.pose.bones['forearm.'+s].rotation_euler[0]=-.16 if clip=='Walk' else -.60
                arm.pose.bones['pelvis'].location[1]=(.019 if clip=='Walk' else .040)*(1-math.cos(t*math.tau*2))
                arm.pose.bones['chest'].rotation_euler[0]=.03 if clip=='Walk' else .15
                arm.pose.bones['chest'].rotation_euler[2]=.07*wave
            if clip=='UseItem':
                ease=max(0,math.sin(math.pi*t))**.7
                arm.pose.bones['upper_arm.R'].rotation_euler[0]=-.75*ease
                arm.pose.bones['forearm.R'].rotation_euler[0]=-1.20*ease
                arm.pose.bones['hand.R'].rotation_euler[2]=-.27*ease
                arm.pose.bones['head'].rotation_euler[0]=.08*ease
                arm.pose.bones['upper_arm.L'].rotation_euler[0]=-.19*ease
                arm.pose.bones['forearm.L'].rotation_euler[0]=-.50*ease
                if smoke_rotations:
                    # The runtime stretches this two-second clip across the
                    # lighter / inhale / exhale sequence. Hold at the mouth.
                    rise=min(1,max(0,t/.48));fall=min(1,max(0,(1-t)/.32))
                    strength=min(rise*rise*(3-2*rise),fall*fall*(3-2*fall))
                    for bn,rotation in smoke_rotations.items():
                        arm.pose.bones[bn].rotation_euler=Quaternion().slerp(rotation,strength).to_euler('XYZ')
            for pb in arm.pose.bones:
                pb.keyframe_insert(data_path='rotation_euler',frame=frame,group=pb.name)
                pb.keyframe_insert(data_path='location',frame=frame,group=pb.name)
        clips.append({'name':clip,'frames':count,'fps':24})
    arm.animation_data.action=bpy.data.actions.get('Idle');bpy.context.scene.frame_set(1)
    return clips

def vivian(P):
    spec={'hip':.89,'waist':1.05,'chest':1.27,'headbase':1.52,'top':1.88,'hipx':.108,
      'shoulder':(.235,0,1.39),'elbow':(.297,.010,1.16),'wrist':(.322,-.009,.949),
      'knee':(.112,.007,.47),'ankle':(.106,0,.15)}
    # Feminine silhouette is a continuous, waisted polygon dress.
    dress=loft('Burgundy fitted dress',[(0,.008,.705,.235,.146),(0,.008,.79,.243,.155),(0,0,.91,.209,.134),
      (0,0,1.025,.163,.103),(0,0,1.12,.159,.102),(0,-.006,1.21,.181,.128),
      (0,-.005,1.31,.231,.142),(0,0,1.37,.215,.119)],P['Burgundy'],weights_fn=bone_weight,sides=12)
    # Sewn panels are colour regions on the continuous surface, so no floating
    # geometry separates from the fitted silhouette when the spine bends.
    dress.data.materials.append(P['BurgundyLight'])
    dress.data.materials.append(P['BurgundyDark'])
    for face in dress.data.polygons:
        if face.normal.y<-.2 and abs(face.center.x)>.115:face.material_index=1
        elif face.normal.y>.4:face.material_index=2
    loft('Exposed neckline',[(0,0,1.25,.12,.083),(0,0,1.37,.188,.105),(0,0,1.43,.105,.085)],P['Porcelain'],'chest',10)
    loft('Neck',[(0,0,1.38,.060,.053),(0,.003,1.56,.055,.053)],P['Porcelain'],'neck',8)
    poly('Black sweetheart inset',[(-.209,-.080,1.37),(-.153,-.127,1.321),(-.086,-.145,1.303),(0,-.139,1.321),
      (.086,-.145,1.303),(.153,-.127,1.321),(.209,-.080,1.37),(.194,-.091,1.255),(0,-.135,1.193),(-.194,-.091,1.255)],P['BodiceBlack'],'chest')
    for s in [-1,1]:
        stripe('Black dress strap',[(s*.131,-.091,1.43),(s*.170,-.120,1.32)],.025,P['BodiceBlack'],'chest')
        sf='L' if s>0 else 'R'
        tube('Cap sleeve',[(s*.207,0,1.382),(s*.230,.003,1.348),(s*.249,.005,1.300)],[.053,.059,.052],P['Burgundy'],'upper_arm.'+sf,8)
        tube('Bare upper arm',[(s*.242,0,1.32),(s*.288,.003,1.185),(s*.297,.01,1.15)],[.052,.044,.039],P['Porcelain'],'upper_arm.'+sf,8)
        tube('Slender forearm',[(s*.297,.01,1.17),(s*.316,-.002,1.06),(s*.322,-.009,.951)],[.041,.039,.024],P['Porcelain'],'forearm.'+sf,8)
        hands(P,s,(s*.322,-.009,.951))
        tube('Stocking thigh',[(s*.111,0,.871),(s*.119,0,.695),(s*.112,.007,.464)],[.089,.075,.049],P['Stockings'],'thigh.'+sf,8)
        tube('Stocking calf',[(s*.112,.007,.47),(s*.112,.024,.344),(s*.106,.004,.15)],[.051,.062,.031],P['Stockings'],'shin.'+sf,8)
        tube('Boot shaft',[(s*.106,.005,.14),(s*.108,.011,.29)],[.042,.043],P['BootLeather'],'shin.'+sf,6)
        shoes(P,s,s*.106,.086,.73)
    face_female(P,(0,-.002,1.698),'Porcelain','LipDark',iris='IrisHoney')
    # Bob cap, separated face opening, blunt fringe and tapered side sections.
    loft('White bob crown',[(0,.012,1.733,.157,.143),(0,.018,1.825,.142,.139),(0,.025,1.892,.09,.086),
      (0,.029,1.906,.037,.035)],P['WhiteHair'],'head',10,phase=-math.pi/2)
    for s in [-1,1]:
        loft('Pointed bob side',[(s*.133,.024,1.850,.023,.081),(s*.152,.010,1.757,.029,.109),
          (s*.157,.021,1.591,.026,.094),(s*.143,.027,1.480,.019,.067),(s*.112,.033,1.442,.004,.016)],P['WhiteHair'],'head',6)
        stripe('Bob fine strand',[(s*.159,-.088,1.744),(s*.162,-.074,1.59),(s*.129,-.028,1.466)],.003,P['HairHighlight'],'head')
    # Straight fringe is divided into hand cut panels with an imperfect edge.
    for j in range(7):
        l=-.145+j*.0415;r=l+.043
        front=lambda xx:.014-.153*math.sqrt(max(.04,1-(xx/.169)**2))
        top=lambda xx:1.831+.025*math.sqrt(max(.04,1-(xx/.169)**2))
        poly('Blunt fringe %02d'%j,[(l,front(l)+.018,top(l)),(r,front(r)+.018,top(r)),
          (r,front(r),1.753+(.002 if j%2 else 0)),(l,front(l),1.754)],P['WhiteHair' if j%3 else 'HairHighlight'],'head',.012)
    # Back of the bob is a single faceted curtain with a tapered lower edge.
    vv=[];ff=[];segments=8
    for z,rx,ry in [(1.822,.148,.145),(1.74,.167,.153),(1.55,.166,.143),(1.466,.143,.120)]:
        for j in range(segments+1):
            a=math.pi*j/segments;vv.append((math.cos(a)*rx,.022+math.sin(a)*ry,z))
    for level in range(3):
        for j in range(segments):
            a=level*(segments+1)+j;ff.append((a,a+1,a+segments+2,a+segments+1))
    mesh('Continuous bob back',vv,ff,P['WhiteHair'],'head')
    # Necklace follows clavicles; pendant remains visible above bodice.
    chain('Silver necklace',[(-.064,-.061,1.475),(-.10,-.092,1.418),(-.070,-.112,1.375),(0,-.121,1.349),(.070,-.112,1.375),(.10,-.092,1.418),(.064,-.061,1.475)],P['Silver'],'chest',.006)
    ellipsoid('Necklace pendant',(0,-.127,1.339),(.017,.008,.023),P['Silver'],'chest',6,3)
    for s in [-1,1]:
        pts=[]
        for j in range(13):
            a=j*math.tau/12;pts.append((s*.141+.016*math.cos(a),-.056,1.645+.028*math.sin(a)))
        chain('Gold hoop earring',pts,P['Gold'],'head',.004)
    # Packet in a small dress-side holder, modelled independently from the usable runtime prop.
    pack=cube('Vivian cigarette packet',(.210,-.050,.959),(.057,.027,.085),P['PackCream'],'pelvis',.004)
    poly('Packet red chevron',[(.182,-.067,.986),(.238,-.067,.986),(.238,-.067,.968),(.211,-.068,.948),(.182,-.067,.968)],P['PackRed'],'pelvis')
    stripe('Packet foil line',[(.185,-.069,.994),(.235,-.069,.994)],.004,P['Gold'],'pelvis')
    return spec

def victor(P):
    spec={'hip':.60,'waist':.89,'chest':1.19,'headbase':1.53,'top':1.81,'hipx':.217,
      'shoulder':(.48,.06,1.47),'elbow':(.596,.007,1.10),'wrist':(.543,-.066,.802),
      'knee':(.245,.015,.345),'ankle':(.266,0,.13)}
    weight=lambda pt:{'pelvis':1} if pt[2]<.84 else ({'spine':1} if pt[2]<1.18 else {'chest':1})
    loft('Massive hunched suit',[(0,.063,.61,.407,.231),(0,.079,.84,.470,.286),(0,.12,1.08,.492,.304),
      (0,.13,1.34,.595,.270),(0,.13,1.52,.620,.246),(0,.16,1.61,.463,.247),
      (0,.10,1.60,.178,.156)],P['Suit'],weights_fn=weight,sides=12)
    # Sallow shirt front bulges beyond open jacket.
    loft('Protruding shirt belly',[(0,-.105,.65,.280,.133),(0,-.146,.77,.346,.190),(0,-.152,.96,.364,.218),
      (0,-.124,1.17,.286,.199),(0,-.085,1.38,.235,.168),(0,-.047,1.50,.169,.117)],P['Shirt'],weights_fn=weight,sides=10,phase=-math.pi/2)
    for s in [-1,1]:
        poly('Wide jacket lapel',[(s*.159,-.105,1.534),(s*.427,-.115,1.485),(s*.290,-.177,1.240),
          (s*.404,-.127,1.128),(s*.306,-.275,.844),(s*.239,-.286,1.08)],P['SuitLight'],'chest',.026)
        stripe('Lapel stitch',[(s*.184,-.118,1.49),(s*.294,-.179,1.261)],.008,P['SuitShadow'],'chest')
        poly('Crumpled white collar',[(s*.035,-.125,1.532),(s*.13,-.131,1.548),(s*.198,-.166,1.407),
          (s*.105,-.207,1.293),(s*.057,-.186,1.432)],P['ShirtLight'],'chest',.016)
        sf='L' if s>0 else 'R'
        tube('Huge jacket shoulder',[(s*.44,.08,1.477),(s*.558,.084,1.343),(s*.596,.007,1.087)],[(.225,.220),(.224,.212),(.144,.150)],P['Suit'],'upper_arm.'+sf,8)
        tube('Jacket forearm',[(s*.596,.007,1.112),(s*.590,-.024,.958),(s*.543,-.066,.809)],[.149,.150,.099],P['Suit'],'forearm.'+sf,8)
        tube('White folded cuff',[(s*.549,-.060,.840),(s*.536,-.073,.785)],[.104,.093],P['ShirtLight'],'forearm.'+sf,8)
        hands(P,s,(s*.536,-.073,.786),'Sallow',1.50)
        tube('Heavy suit trouser',[(s*.216,.028,.684),(s*.23,.019,.479),(s*.245,.015,.326)],[(.187,.184),(.173,.168),(.141,.136)],P['Suit'],'thigh.'+sf,8)
        tube('Bunched trouser shin',[(s*.245,.015,.346),(s*.262,.019,.229),(s*.266,0,.101)],[.148,.149,.113],P['Suit'],'shin.'+sf,8)
        loft('Trouser hem fold',[(s*.266,0,.143,.146,.118),(s*.266,-.005,.111,.143,.123)],P['SuitLight'],'shin.'+sf,8)
        shoes(P,s,s*.266,.105,1.6,'BootLeather')
    # Deliberately tiny, bald head nestles inside the shoulders.
    loft('Bald bulbous head',[(0,-.058,1.505,.073,.071),(0,-.073,1.557,.116,.102),(0,-.083,1.64,.128,.115),
      (0,-.065,1.727,.111,.109),(0,-.039,1.788,.075,.073),(0,-.031,1.809,.03,.034)],P['Sallow'],'head',10,phase=-math.pi/2)
    for s in [-1,1]:
        ellipsoid('Large crumpled ear',(s*.126,-.069,1.641),(.037,.034,.058),P['SallowLight'],'head',7,4)
        ellipsoid('Ear hollow',(s*.133,-.101,1.644),(.020,.006,.033),P['SallowDark'],'head',6,3)
        poly('Sagging cheek',[(s*.019,-.197,1.636),(s*.099,-.164,1.65),(s*.119,-.144,1.593),(s*.055,-.191,1.569)],P['SallowLight'],'head')
        ey=1.672
        stripe('Deep eye socket',[(s*.019,-.189,ey),(s*.093,-.175,ey+.014)],.022,P['SallowDark'],'head')
        stripe('Narrow eye',[(s*.031,-.201,ey),(s*.078,-.190,ey+.006)],.007,P['EyeWhite'],'head')
        ellipsoid('Tiny dull iris',(s*.058,-.202,ey+.001),(.005,.003,.006),P['EyeBlack'],'head',4,3)
        stripe('Bushy brow',[(s*.018,-.190,ey+.022),(s*.10,-.164,ey+.032)],.009,P['SuitShadow'],'head')
        stripe('Sunken eye crease',[(s*.031,-.20,ey-.023),(s*.095,-.16,ey-.023)],.004,P['SallowDark'],'head')
    ellipsoid('Bulbous nose',(0,-.207,1.628),(.043,.048,.039),P['SallowLight'],'head',7,4)
    for s in [-1,1]:ellipsoid('Nostril',(s*.023,-.241,1.615),(.010,.005,.005),P['SallowDark'],'head',5,3)
    stripe('Downturned mouth',[(-.057,-.186,1.57),(0,-.203,1.584),(.057,-.186,1.57)],.012,P['SallowDark'],'head')
    stripe('Lower lip',[(-.038,-.197,1.559),(0,-.206,1.558),(.040,-.193,1.558)],.007,P['SallowLight'],'head')
    # Sparse swept-back horseshoe hair, visible at sides and crown.
    for s in [-1,1]:
        tube('Receding side hair',[(s*.080,-.008,1.781),(s*.113,.014,1.73),(s*.126,.009,1.655)],[(.012,.028),(.014,.034),(.009,.022)],P['DarkHair'],'head',5)
    tube('Receding crown fringe',[(-.084,.022,1.78),(0,.047,1.817),(.084,.022,1.78)],.014,P['DarkHair'],'head',5)
    # The patterned tie follows the actual convex shirt front in short sections;
    # a single non-planar polygon used to disappear through the belly.
    tie_rows=[(1.455,.012,-.211,.034),(1.38,.015,-.267,.045),(1.18,.010,-.329,.052),
      (.96,0,-.379,.060),(.77,-.001,-.347,.063),(.65,.010,-.250,.065),
      (.47,.020,-.267,.064),(.29,.023,-.252,.060),(.205,.025,-.238,.002)]
    tie_vertices=[];tie_weights={}
    for z,cx,yy,halfwidth in tie_rows:
        tie_vertices.extend([(cx-halfwidth,yy,z),(cx+halfwidth,yy,z)])
    for idx,pt in enumerate(tie_vertices):
        bone='pelvis' if pt[2]<.84 else 'spine' if pt[2]<1.18 else 'chest'
        tie_weights.setdefault(bone,[]).append((idx,1))
    mesh('Continuous loose patterned tie',tie_vertices,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(tie_rows)-1)],P['Tie'],weights=tie_weights)
    poly('Tie knot',[(-.039,-.235,1.459),(.045,-.235,1.463),(.067,-.274,1.409),(.019,-.281,1.360),(-.036,-.266,1.412)],P['TieGold'],'chest',.009)
    poly('Tie trailing end',[(-.109,-.219,1.47),(-.067,-.224,1.451),(-.142,-.306,1.27),
      (-.159,-.369,1.0),(-.169,-.337,.80),(-.173,-.268,.457),(-.208,-.257,.394),
      (-.203,-.327,.80),(-.194,-.359,1.0),(-.183,-.292,1.27)],P['Tie'],'spine',.006)
    def tie_surface(z):
        for row,next_row in zip(tie_rows,tie_rows[1:]):
            if next_row[0]<=z<=row[0]:
                t=(row[0]-z)/(row[0]-next_row[0])
                return row[2]*(1-t)+next_row[2]*t
        return tie_rows[-1][2]
    for j in range(44):
        z=.30+j*.024;xx=.012+.035*math.sin(j*2.47)
        pts=[(xx-.012,tie_surface(z)-.001,z),(xx,tie_surface(z+.012)-.001,z+.012),
             (xx+.013,tie_surface(z+.003)-.001,z+.003),(xx,tie_surface(z-.012)-.001,z-.012)]
        bone='pelvis' if z<.84 else 'spine' if z<1.18 else 'chest'
        poly('Tie printed flower %02d'%j,pts,P['TieGold' if j%3 else 'TieGreen'],bone,.0001)
    for j in range(5):
        zz=.83+j*.105; yy=-.374+(zz-.83)*.35
        stripe('Shirt strained fold',[(-.264,yy+.028,zz+.018),(-.063,yy,zz-.008)],.009,P['ShirtShade'],'spine')
    # A battered engineer's brass lighter peeks from an outside pocket.
    cube('Victor brass lighter',(.387,-.188,1.067),(.075,.033,.10),P['Gold'],'spine',.008)
    stripe('Jacket pocket welt',[(.333,-.191,1.025),(.439,-.183,1.035)],.021,P['SuitShadow'],'spine')
    return spec

def edgar(P):
    spec={'hip':.84,'waist':1.07,'chest':1.37,'headbase':1.65,'top':2.045,'hipx':.103,
      'shoulder':(.253,0,1.485),'elbow':(.380,-.012,1.255),'wrist':(.408,-.080,1.059),
      'knee':(.107,.008,.47),'ankle':(.12,0,.14)}
    weight=lambda p:{'pelvis':1} if p[2]<1.01 else ({'spine':1} if p[2]<1.3 else {'chest':1})
    loft('Long teal uniform coat',[(0,.03,.702,.294,.153),(0,.013,.931,.247,.147),(0,.012,1.118,.237,.133),
      (0,.010,1.322,.263,.137),(0,.035,1.478,.324,.151),(0,.014,1.538,.149,.100)],P['Teal'],weights_fn=weight,sides=10)
    for s in [-1,1]:
        sf='L' if s>0 else 'R'
        poly('Angular coat lapel',[(s*.12,-.060,1.55),(s*.28,-.098,1.47),(s*.20,-.157,1.236),(s*.065,-.157,1.18),(s*.092,-.162,1.398)],P['TealLight'],'chest',.021)
        poly('Lower split coat panel',[(s*.031,-.145,1.02),(s*.24,-.084,1.08),(s*.298,-.077,.726),(s*.026,-.141,.67)],P['Teal' if s>0 else 'TealDark'],'pelvis',.016)
        tube('Angular uniform shoulder',[(s*.25,.022,1.477),(s*.33,.008,1.373),(s*.38,-.012,1.25)],[.098,.088,.068],P['Teal'],'upper_arm.'+sf,6)
        tube('Exaggerated coat cuff',[(s*.38,-.012,1.273),(s*.422,-.052,1.113),(s*.408,-.080,1.061)],[.076,.112,.105],P['Teal'],'forearm.'+sf,6)
        tube('Cuff trim',[(s*.412,-.071,1.084),(s*.408,-.080,1.056)],[.108,.105],P['TealDark'],'forearm.'+sf,6)
        hands(P,s,(s*.408,-.080,1.054),'OliveGold',1.3)
        tube('Spindly long trousers',[(s*.100,0,.855),(s*.11,.01,.66),(s*.107,.008,.466)],[.069,.061,.042],P['TealDark'],'thigh.'+sf,6)
        tube('Spindly shin',[(s*.107,.008,.47),(s*.114,.020,.296),(s*.12,0,.113)],[.044,.048,.031],P['Teal'],'shin.'+sf,6)
        shoes(P,s,s*.12,.090,.95)
    for z in [.874,1.061,1.241]:
        ellipsoid('Large brass uniform button',(.02,-.155,z),(.040,.018,.044),P['Gold'],'spine',7,4)
        ellipsoid('Button glint',(.011,-.171,z+.010),(.013,.003,.016),P['GoldLight'],'spine',5,3)
    stripe('Coat pocket',[(-.22,-.116,1.061),(-.104,-.148,1.079)],.027,P['TealDark'],'spine')
    loft('Long golden neck',[(0,0,1.47,.076,.065),(0,0,1.76,.064,.063)],P['OliveGold'],'neck',6)
    # Tall narrow face, large alert eyes and a long bent nose.
    loft('Long angular golden face',[(0,-.017,1.637,.078,.063),(0,-.010,1.728,.091,.087),(0,-.005,1.839,.079,.078),
      (0,.006,1.933,.077,.072),(0,.008,1.962,.053,.047)],P['OliveGold'],'head',8,phase=-math.pi/2)
    for s in [-1,1]:
        ellipsoid('Golden ear',(s*.087,0,1.75),(.028,.023,.019),P['GoldSkinLight'],'head',6,3)
        ellipsoid('Popping eye',(s*.039,-.085,1.84),(.032,.024,.046),P['EyeWhite'],'head',8,5)
        ellipsoid('Eye iris',(s*.04,-.11,1.837),(.016,.004,.024),P['IrisHoney'],'head',7,4)
        ellipsoid('Eye pupil',(s*.04,-.114,1.838),(.008,.003,.018),P['EyeBlack'],'head',6,3)
        ellipsoid('Eye catchlight',(s*.04-.004,-.118,1.849),(.004,.002,.006),P['Highlight'],'head',4,2)
        stripe('Worried lifted brow',[(s*.015,-.088,1.907),(s*.067,-.075,1.899)],.012,P['DarkHair'],'head')
    mesh('Long crooked nose',[(-.012,-.078,1.893),(.011,-.078,1.892),(.018,-.135,1.786),(.031,-.165,1.756),
      (-.004,-.170,1.764),(-.015,-.108,1.780)],[(0,1,3,4),(1,2,3),(0,4,5),(5,4,3,2)],P['GoldSkinLight'],'head')
    # Beard is a broad, split sculpted sheet, sharp tips reaching halfway down the chest.
    for s in [-1,1]:
        poly('Forked long beard',[(s*.010,-.101,1.697),(s*.084,-.078,1.718),(s*.087,-.125,1.585),
          (s*.142,-.175,1.367),(s*.087,-.191,1.201),(s*.046,-.177,1.239),(s*.028,-.156,1.442)],P['Beard'],'head',.018)
        for j in range(3):
            xx=s*(.026+j*.025)
            stripe('Beard graphic strand',[(xx,-.126,1.67),(xx+s*.027,-.186,1.432),(xx+s*.037,-.2,1.297)],.009 if j==1 else .005,P['BeardShade' if j%2 else 'BeardWarm'],'head')
    poly('Square upper moustache',[(-.093,-.091,1.735),(.095,-.091,1.735),(.094,-.125,1.666),(-.092,-.125,1.666)],P['BeardWarm'],'head',.022)
    # A small spiral moustache curl, as in the reference.
    pts=[]
    for j in range(20):
        a=j/19*math.pi*3.5;r=.020*(1-j/24)
        pts.append((.020+math.cos(a)*r,-.139,1.703+math.sin(a)*r))
    chain('Spiral moustache',pts,P['GoldSkinShadow'],'head',.003)
    # Tall irregular peaked cap and contrasting band.
    loft('Tall peaked cap crown',[(0,.02,1.941,.104,.095),(0,.015,2.006,.154,.126),(0,.025,2.146,.132,.118),
      (.034,.025,2.185,.092,.077)],P['TealDark'],'head',5,phase=-math.pi/2)
    loft('Cap band',[(0,-.011,1.922,.101,.088),(0,-.011,1.963,.108,.096)],P['SuitShadow'],'head',8)
    chain('Cap light piping',[(-.097,-.064,1.959),(-.063,-.098,1.984),(0,-.114,1.985),(.064,-.098,1.984),(.100,-.06,1.959)],P['Beard'],'head',.007)
    poly('Cap brim',[(-.112,-.03,1.925),(-.092,-.169,1.916),(0,-.191,1.912),(.111,-.151,1.916),(.109,-.03,1.927)],P['SuitShadow'],'head',.013)
    poly('Cap diamond badge',[(-.016,-.116,2.074),(.025,-.119,2.109),(.068,-.111,2.073),(.027,-.125,2.033)],P['Gold'],'head',.008)
    poly('Badge highlight',[(.025,-.132,2.105),(.027,-.138,2.04),(-.009,-.128,2.073)],P['GoldLight'],'head')
    # Journalist's small notebook at the left coat hip.
    cube('Edgar pocket notebook',(-.235,-.09,.926),(.074,.029,.105),P['Tie'],'pelvis',.004)
    stripe('Notebook page edge',[(-.268,-.108,.961),(-.204,-.108,.961)],.009,P['Paper'],'pelvis')
    return spec

def hazel(P):
    spec={'hip':.96,'waist':1.14,'chest':1.36,'headbase':1.68,'top':2.03,'hipx':.094,
      'shoulder':(.208,0,1.46),'elbow':(.266,.016,1.218),'wrist':(.279,-.018,1.012),
      'knee':(.102,.013,.505),'ankle':(.103,0,.14)}
    weight=lambda p:{'pelvis':1} if p[2]<1.04 else ({'spine':1} if p[2]<1.26 else {'chest':1})
    # Long fitted dress, small waist, flared asymmetric hem.
    dress=loft('Pink fitted flared dress',[(0,.012,.204,.245,.153),(0,.012,.331,.211,.136),(0,.012,.550,.154,.111),
      (0,.012,.792,.166,.119),(0,.009,.950,.192,.135),(0,0,1.081,.131,.091),(0,0,1.174,.127,.084),
      (0,-.002,1.31,.161,.106),(0,.005,1.406,.205,.115)],P['DressRed'],weights_fn=weight,sides=10)
    # Printed fabric patches sit on the actual dress triangles, with no raised
    # tile extrusion. Barycentric points keep every patch within its fabric plane.
    for idx,face in enumerate(dress.data.polygons):
        if len(face.vertices)!=4 or idx%2:continue
        a,b,c=[dress.data.vertices[vi].co.copy() for vi in face.vertices[:3]]
        normal=(b-a).cross(c-a).normalized()
        pts=[]
        for wa,wb,wc in [(.20,.22,.58),(.20,.50,.30),(.36,.52,.12),(.60,.24,.16),(.46,.12,.42)]:
            pts.append(tuple(a*wa+b*wb+c*wc+normal*.0004))
        z=sum(p[2] for p in pts)/5
        poly('Dress printed coral motif %02d'%idx,pts,P['DressPink' if idx%4 else 'DressCoral'],
             'pelvis' if z<1.04 else 'spine' if z<1.26 else 'chest',.0001)
    loft('Golden shoulders',[(0,.01,1.37,.170,.094),(0,.011,1.449,.216,.105),(0,.013,1.505,.089,.071)],P['OliveGold'],'chest',10)
    loft('Exaggerated graceful neck',[(0,.006,1.427,.061,.053),(0,.002,1.58,.043,.042),(0,.008,1.726,.051,.051)],P['OliveGold'],'neck',8)
    # Sculpted dress neckline and short pointed sleeves.
    chain('Coral scoop neckline',[(-.177,-.064,1.454),(-.113,-.104,1.424),(0,-.115,1.402),(.113,-.104,1.424),(.177,-.064,1.454)],P['DressPink'],'chest',.014)
    for s in [-1,1]:
        sf='L' if s>0 else 'R'
        tube('Dress short sleeve',[(s*.183,.002,1.447),(s*.225,.004,1.348)],[.061,.064],P['DressRed'],'upper_arm.'+sf,7)
        tube('Bare golden arm',[(s*.222,.003,1.352),(s*.26,.010,1.242),(s*.266,.016,1.214)],[.041,.036,.032],P['OliveGold'],'upper_arm.'+sf,7)
        tube('Golden forearm',[(s*.266,.016,1.227),(s*.273,.003,1.12),(s*.279,-.018,1.014)],[.034,.032,.021],P['OliveGold'],'forearm.'+sf,7)
        hands(P,s,(s*.279,-.018,1.015),'OliveGold',.9)
        tube('Hidden upper leg',[(s*.092,0,.921),(s*.102,.01,.502)],[.065,.044],P['OliveGold'],'thigh.'+sf,6)
        tube('Slender calf',[(s*.102,.01,.507),(s*.103,.009,.344),(s*.103,0,.124)],[.043,.052,.026],P['OliveGold'],'shin.'+sf,6)
        shoes(P,s,s*.103,.087,.78)
    face_female(P,(0,0,1.85),'OliveGold','LipScarlet',style='hazel',width=1.17)
    # Dark hair cap, curtain silhouette and the characteristic sweeping winged fringe.
    loft('Dark hair crown',[(0,.03,1.886,.184,.152),(0,.04,2.006,.174,.147),(0,.038,2.052,.104,.103),
      (0,.035,2.064,.051,.054)],P['DarkHair'],'head',10,phase=-math.pi/2)
    for s in [-1,1]:
        # Three large ribbon volumes per side, sharply tapering toward the hip.
        for j in range(3):
            xx=s*(.143+j*.027);yy=.014+j*.044
            tube('Long swept hair curtain',[(xx,yy,1.989),(s*(.186+j*.015),yy+.018,1.743),(s*(.23+j*.020),yy+.021,1.397),
                (s*(.244+j*.016),yy-.032,1.148),(s*(.164+j*.024),yy-.070,1.008+j*.037)],
                [(.055,.039),(.063,.038),(.066,.035),(.055,.032),(.006,.010)],P['DarkHair' if j%2 else 'DarkHairLight'],'head',5)
        poly('Winged swept fringe',[(s*.006,-.109,2.052),(s*.104,-.123,2.037),(s*.18,-.092,1.986),(s*.278,-.029,2.008),
          (s*.214,-.096,1.943),(s*.123,-.157,1.931),(s*.049,-.16,1.968)],P['DarkHair'],'head',.030)
        stripe('Fringe warm edge',[(s*.055,-.162,1.979),(s*.118,-.161,1.944),(s*.210,-.10,1.953)],.008,P['DarkHairGlow'],'head')
        # Pale dangling comma earrings with lower enlarged angular drops.
        chain('Long ivory earring stem',[(s*.161,-.083,1.779),(s*.159,-.096,1.723),(s*.171,-.102,1.689)],P['Highlight'],'head',.006)
        poly('Ivory comma earring',[(s*.175,-.101,1.725),(s*.166,-.119,1.685),(s*.190,-.123,1.660),(s*.187,-.118,1.635),
          (s*.159,-.116,1.633),(s*.144,-.115,1.654),(s*.154,-.110,1.675)],P['Highlight'],'head',.012)
    # Rear hair ribbons stop clear of feet and provide a complete back view.
    for j in range(5):
        xx=(j-2)*.065
        tube('Long rear hair',[(xx,.158,1.954),(xx*1.25,.193,1.70),(xx*1.45,.191,1.39),(xx*1.21,.151,1.045+.025*(j%2))],
          [(.057,.032),(.066,.036),(.069,.034),(.012,.020)],P['DarkHair' if j%2 else 'DarkHairLight'],'head',5)
    # Character-specific pendant and a slim red radio pouch at the waist.
    chain('Gold bracelet',[(.281,-.045,1.046),(.3,-.021,1.045),(.278,.005,1.045),(.26,-.02,1.045),(.281,-.045,1.046)],P['Gold'],'forearm.L',.005)
    cube('Hazel radio pouch',(-.154,-.035,1.032),(.049,.035,.086),P['BurgundyDark'],'pelvis',.005)
    return spec

def setup_render():
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8
    scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED';scene.render.threads=2
    scene.render.resolution_x=700;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world.color=(.10,.10,.10)
    scene.world.use_nodes=True
    scene.world.node_tree.nodes.clear()
    background=scene.world.node_tree.nodes.new('ShaderNodeBackground')
    output=scene.world.node_tree.nodes.new('ShaderNodeOutputWorld')
    scene.world.node_tree.links.new(background.outputs['Background'],output.inputs['Surface'])
    background.inputs['Color'].default_value=(.12,.16,.19,1)
    background.inputs['Strength'].default_value=.5
    scene.view_settings.view_transform='AgX'
    # A studio presentation uses enough fill to make silhouettes reviewable.
    for name,loc,power,size,color in [('Key',(-3,-4,6),700,5,(1,.84,.73)),('Fill',(4,-2,3),420,4,(.64,.79,1)),('Rim',(1,3,5),900,3,(1,.88,.66))]:
        light=bpy.data.lights.new(name,'AREA');light.energy=power;light.shape='DISK';light.size=size;light.color=color
        ob=bpy.data.objects.new(name,light);bpy.context.collection.objects.link(ob);ob.location=loc;point_at(ob,(0,0,1))
    floor=material('StudioFloor',(.033,.047,.052))
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.007));ob=bpy.context.object;ob.name='Studio floor';ob.data.materials.append(floor)
    cam=bpy.data.cameras.new('Portrait camera');ob=bpy.data.objects.new('Portrait camera',cam);bpy.context.collection.objects.link(ob)
    ob.location=(3.3,-7,3.0);point_at(ob,(0,0,1.07));cam.type='ORTHO';cam.ortho_scale=2.45;cam.lens=55;scene.camera=ob
    return ob

def point_at(ob,point):ob.rotation_euler=(Vector(point)-ob.location).to_track_quat('-Z','Y').to_euler()

def render_portrait(classid,body,arm):
    cam=setup_render();scene=bpy.context.scene
    if classid=='engineer':cam.data.ortho_scale=2.18;point_at(cam,(0,0,.91))
    if classid=='operator':cam.data.ortho_scale=2.16;point_at(cam,(0,0,.97))
    scene.render.filepath=str(PREVIEW/(classid+'_threequarter.png'));bpy.ops.render.render(write_still=True)
    cam.location=(0,-8,2.0);point_at(cam,(0,0,1.07 if classid in ('dispatcher','journalist') else .94))
    scene.render.filepath=str(PREVIEW/(classid+'_front.png'));bpy.ops.render.render(write_still=True)
    # Save source with the body selected, studio rig available, posed on idle start.
    bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);bpy.context.view_layer.objects.active=arm
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/('character_'+classid+'.blend')))

def build(classid,name,fn):
    global PARTS,MATS
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    for d in list(bpy.data.actions):bpy.data.actions.remove(d)
    PARTS=[];MATS={};P=palette()
    spec=fn(P);arm=rig(spec);body=bind_join(arm,name)
    clips=actions(arm,classid);bpy.context.scene.render.fps=24
    bpy.context.scene.frame_end=49;bpy.context.scene.frame_set(1)
    # Runtime exports include only the character and sockets, no studio objects.
    bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);body.select_set(True)
    for ob in bpy.context.scene.objects:
        if ob.name.startswith('ItemSocket.'):ob.select_set(True)
    bpy.context.view_layer.objects.active=arm
    bpy.ops.export_scene.fbx(filepath=str(OUT/('character_'+classid+'.fbx')),use_selection=True,
        object_types={'MESH','ARMATURE','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=False,
        add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',use_armature_deform_only=False,
        bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,
        path_mode='AUTO',mesh_smooth_type='FACE',use_mesh_modifiers=True)
    bounds=[body.matrix_world@Vector(v) for v in body.bound_box]
    stats={'class':classid,'name':name,'triangles':len(body.data.polygons),'vertices':len(body.data.vertices),
      'bones':len(arm.data.bones),'materials':len(body.data.materials),'height_m':round(max(v.z for v in bounds)-min(v.z for v in bounds),3),
      'clips':clips,'source':'Art/Characters/character_'+classid+'.blend','runtime':'Assets/Resources/Characters/character_'+classid+'.fbx'}
    # Save every editable source before spending time on presentation renders.
    # This also makes interrupted builds safe to resume with --render-only.
    reference=ART/'References'/(classid+'_'+name.lower()+'_reference.png')
    if reference.exists():
        ref_image=bpy.data.images.load(str(reference),check_existing=True);ref_image.pack()
        guide=bpy.data.objects.new('Reference '+name,None);bpy.context.collection.objects.link(guide)
        guide.empty_display_type='IMAGE';guide.data=ref_image;guide.empty_display_size=1.7
        guide.rotation_euler=(math.pi/2,0,0);guide.location=(1.3,.25,1.05)
        guide.hide_render=True
    bpy.ops.object.select_all(action='DESELECT');arm.select_set(True)
    bpy.context.view_layer.objects.active=arm
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=3.2
                area.spaces.active.region_3d.view_location=(0,0,1)
                area.spaces.active.shading.type='SOLID'
                area.spaces.active.shading.color_type='MATERIAL'
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/('character_'+classid+'.blend')))
    print('CHARACTER_EXPORTED '+json.dumps(stats),flush=True)
    return stats

def lineup(stats):
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    poses=[(-2.0,0,0),(-.7,0,0),(.79,0,0),(1.84,0,0)]
    for st,loc in zip(stats,poses):
        path=ART/('character_'+st['class']+'.blend')
        with bpy.data.libraries.load(str(path),link=False) as (src,dst):
            dst.objects=[n for n in src.objects if n=='Armature' or n.endswith('_Body')]
        loaded=[ob for ob in dst.objects if ob]
        for ob in loaded:bpy.context.collection.objects.link(ob)
        for ob in loaded:
            if ob.type=='ARMATURE':
                ob.location=loc;ob.animation_data_clear()
                for pb in ob.pose.bones:pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
        curve=bpy.data.curves.new('Name','FONT');curve.body=st['name'].upper();curve.size=.095;curve.align_x='CENTER';curve.extrude=0
        text=bpy.data.objects.new('Label '+st['name'],curve);bpy.context.collection.objects.link(text);text.location=(loc[0],-.30,-.08)
        text.rotation_euler=(math.pi/2,0,0);text.data.materials.append(material('Label',(.57,.71,.69)))
    cam=setup_render();cam.location=(.10,-10,3.25);point_at(cam,(0,0,1.04));cam.data.ortho_scale=5.1
    sc=bpy.context.scene;sc.render.resolution_x=1600;sc.render.resolution_y=844;sc.render.filepath=str(PREVIEW/'reference_cast_lineup.png')
    bpy.ops.render.render(write_still=True);bpy.ops.wm.save_as_mainfile(filepath=str(ART/'reference_cast_lineup.blend'))

def validate_sources(stats):
    checks=[]
    for st in stats:
        bpy.ops.wm.open_mainfile(filepath=str(ART/('character_'+st['class']+'.blend')))
        arm=bpy.data.objects['Armature'];body=bpy.data.objects[st['name']+'_Body']
        assert len(arm.data.bones)==18
        assert body.parent==arm
        assert all(len(f.vertices)==3 for f in body.data.polygons)
        unweighted=sum(1 for v in body.data.vertices if sum(g.weight for g in v.groups)<.999)
        assert unweighted==0,(st['class'],'unweighted vertices',unweighted)
        actions_by_name={a.name:a for a in bpy.data.actions}
        positions={}
        for clip in st['clips']:
            action=actions_by_name[clip['name']]
            assert tuple(action.frame_range)==(1.0,float(clip['frames']))
            arm.animation_data.action=action;bpy.context.scene.frame_set((clip['frames']+1)//2)
            deps=bpy.context.evaluated_depsgraph_get();evaluated=body.evaluated_get(deps)
            mesh_evaluated=evaluated.to_mesh()
            coordinates=[evaluated.matrix_world@v.co for v in mesh_evaluated.vertices]
            assert all(math.isfinite(component) for pt in coordinates for component in pt)
            assert max(pt.length for pt in coordinates)<4
            evaluated.to_mesh_clear()
            positions[clip['name']]=[round(c,3) for c in arm.pose.bones['hand.R'].head]
        checks.append({'class':st['class'],'triangles':len(body.data.polygons),
          'weighted_vertices':len(body.data.vertices),'bones':len(arm.data.bones),
          'clips':list(actions_by_name),'right_hand_at_clip_midpoint':positions})
    (ART/'source_validation.json').write_text(json.dumps(checks,indent=2),encoding='utf8')
    print('SOURCE_VALIDATION_COMPLETE '+json.dumps(checks),flush=True)

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    if any(a in args for a in ['--render-only','--lineup-only','--validate-only']):
        stats=json.loads((ART/'character_manifest.json').read_text(encoding='utf8'))['characters']
    else:
        stats=[]
        for cid,name,fn in [('operator','Vivian',vivian),('engineer','Victor',victor),('journalist','Edgar',edgar),('dispatcher','Hazel',hazel)]:
            stats.append(build(cid,name,fn))
        (ART/'character_manifest.json').write_text(json.dumps({'coordinate_system':'Blender Z up -Y forward; FBX Y up -Z forward setting (Unity +Z facing)',
          'units':'metres','style':'Faceted PSX, shared flat palette, no subdivision','characters':stats},indent=2),encoding='utf8')
    if '--validate-only' in args:
        validate_sources(stats)
    elif '--lineup-only' in args:
        lineup(stats)
    elif '--models-only' not in args:
        for st in stats:
            bpy.ops.wm.open_mainfile(filepath=str(ART/('character_'+st['class']+'.blend')))
            MATS={}
            arm=bpy.data.objects.get('Armature');body=bpy.data.objects.get(st['name']+'_Body')
            render_portrait(st['class'],body,arm)
        MATS={}
        lineup(stats)
    print('REFERENCE_CAST_COMPLETE '+json.dumps(stats))
