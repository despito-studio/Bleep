"""Original Dead Air cast: clean palette geometry and grounded, editable FK clips.

Blender 4.5: --background --factory-startup --python Tools/Characters/build_indie_cast.py
Optional arguments after --: --role operator --skip-render
Nothing from the supplied source meshes or atlases is modified or sampled.
"""
import bpy, bmesh, math, json, sys, shutil, hashlib
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'Art/Characters/IndieCast'
MODELS = ROOT / 'Assets/Resources/Characters'
TEXTURES = ROOT / 'Assets/Characters/Textures'
FPS = 60
TAU = math.tau

CAST = {
    'operator': dict(name='Vivian',height=1.82,hip=.87,knee=.48,ankle=.11,waist=1.00,chest=1.25,
        shoulder=1.39,shoulder_x=.205,upper=.29,lower=.255,head=1.635,head_width=.132,
        head_depth=.108,head_height=.175,neck=1.475,hip_x=.092,leg_width=.069,arm_width=.055,
        dress=True,skin=(.77,.57,.48),primary=(.31,.075,.13),secondary=(.42,.11,.19),hair=(.69,.72,.69)),
    'engineer': dict(name='Victor',height=1.76,hip=.76,knee=.415,ankle=.105,waist=.985,chest=1.23,
        shoulder=1.35,shoulder_x=.315,upper=.275,lower=.25,head=1.588,head_width=.142,
        head_depth=.122,head_height=.168,neck=1.425,hip_x=.145,leg_width=.113,arm_width=.094,
        dress=False,skin=(.70,.53,.43),primary=(.075,.09,.10),secondary=(.13,.155,.165),hair=(.13,.10,.085)),
    'journalist': dict(name='Edgar',height=1.95,hip=.94,knee=.515,ankle=.115,waist=1.105,chest=1.355,
        shoulder=1.49,shoulder_x=.22,upper=.29,lower=.27,head=1.735,head_width=.128,
        head_depth=.11,head_height=.17,neck=1.565,hip_x=.095,leg_width=.064,arm_width=.057,
        dress=False,skin=(.72,.60,.45),primary=(.065,.285,.29),secondary=(.10,.40,.39),hair=(.76,.74,.65)),
    'dispatcher': dict(name='Hazel',height=1.90,hip=.91,knee=.50,ankle=.11,waist=1.055,chest=1.335,
        shoulder=1.45,shoulder_x=.205,upper=.29,lower=.265,head=1.715,head_width=.129,
        head_depth=.11,head_height=.174,neck=1.515,hip_x=.095,leg_width=.068,arm_width=.053,
        dress=True,skin=(.75,.54,.41),primary=(.57,.16,.22),secondary=(.70,.26,.32),hair=(.075,.055,.075)),
}

def clamp(x,a=0,b=1): return max(a,min(b,x))
def smooth(x): x=clamp(x); return x*x*(3-2*x)
def v3(p): return Vector(p)
def blend(a,b,t):
    t=clamp(t)
    return {a:1-t,b:t} if 0<t<1 else {b if t>=1 else a:1}

class Sculpt:
    def __init__(self): self.vertices=[]; self.faces=[]; self.weights=[]; self.colors=[]
    def mesh(self,verts,faces,color,weight):
        offset=len(self.vertices)
        self.vertices.extend(tuple(p) for p in verts)
        self.weights.extend(weight(p) if callable(weight) else dict(weight) for p in verts)
        self.faces.extend(tuple(offset+i for i in face) for face in faces)
        self.colors.extend([color]*len(faces))
    def loft(self,rings,color,weight,sides=10,phase=0,caps=True):
        verts=[]
        for x,y,z,rx,ry in rings:
            for i in range(sides):
                a=phase+i*TAU/sides; verts.append((x+rx*math.cos(a),y+ry*math.sin(a),z))
        faces=[]
        for row in range(len(rings)-1):
            for i in range(sides): faces.append((row*sides+i,row*sides+(i+1)%sides,(row+1)*sides+(i+1)%sides,(row+1)*sides+i))
        if caps: faces.extend([tuple(range(sides-1,-1,-1)),tuple((len(rings)-1)*sides+i for i in range(sides))])
        self.mesh(verts,faces,color,weight)
    def tube(self,points,radii,color,weight,sides=8):
        pts=list(map(v3,points));verts=[]
        direction=(pts[-1]-pts[0]).normalized();a=direction.cross(Vector((0,1,0)))
        if a.length<.01:a=direction.cross(Vector((1,0,0)))
        a.normalize();b=direction.cross(a).normalized()
        for p,rad in zip(pts,radii):
            ra,rb=(rad,rad) if isinstance(rad,(float,int)) else rad
            for i in range(sides):verts.append(p+a*math.cos(i*TAU/sides)*ra+b*math.sin(i*TAU/sides)*rb)
        faces=[]
        for row in range(len(pts)-1):
            for i in range(sides):faces.append((row*sides+i,row*sides+(i+1)%sides,(row+1)*sides+(i+1)%sides,(row+1)*sides+i))
        faces.extend([tuple(range(sides-1,-1,-1)),tuple((len(pts)-1)*sides+i for i in range(sides))])
        self.mesh(verts,faces,color,weight)
    def orb(self,center,radii,color,weight,sides=10,rings=6):
        x,y,z=center;rx,ry,rz=radii
        profile=[]
        for i in range(rings+1):
            a=-math.pi/2+math.pi*i/rings
            profile.append((x,y,z+math.sin(a)*rz,max(.002,math.cos(a)*rx),max(.002,math.cos(a)*ry)))
        self.loft(profile,color,weight,sides,math.pi/sides)
    def panel(self,points,color,weight,thickness=.003):
        # Front surface points follow an XZ perimeter; face -Y.
        n=len(points);verts=list(points)+[(x,y+thickness,z) for x,y,z in points]
        faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]
        faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
        self.mesh(verts,faces,color,weight)

def make_palette(role,spec):
    def mul(rgb,f):return tuple(clamp(c*f) for c in rgb)
    colors=[spec['skin'],mul(spec['skin'],.82),mul(spec['skin'],1.12),spec['primary'],spec['secondary'],
        mul(spec['primary'],.65),spec['hair'],mul(spec['hair'],.75),mul(spec['hair'],1.14),
        (.045,.052,.065),(.10,.115,.135),(.75,.74,.63),(.87,.85,.73),(.46,.47,.43),
        (.65,.45,.19),(.83,.65,.28),(.16,.09,.105),(.44,.115,.17),(.045,.25,.15),(.49,.54,.48),
        (.90,.87,.77),(.64,.66,.63),(.37,.40,.39),(.40,.24,.12),(.31,.33,.32),(.19,.22,.23),
        (.21,.36,.35),(.36,.20,.245),(.48,.49,.48),(.59,.46,.37),(.065,.065,.06),(.93,.91,.83)]
    path=TEXTURES/(role+'_indie_albedo.png');image=bpy.data.images.new(role+'_indie_albedo',width=256,height=128,alpha=False)
    pixels=[]
    for y in range(128):
        for x in range(256):pixels.extend((*colors[(y//32)*8+x//32],1))
    image.pixels=pixels;image.filepath_raw=str(path);image.file_format='PNG';image.save()
    shutil.copy2(path,ART/(role+'_indie_albedo.png'))
    material=bpy.data.materials.new(role+'_indie_palette');material.use_nodes=True;material.diffuse_color=(1,1,1,1)
    bs=material.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.88
    tex=material.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image;tex.interpolation='Closest'
    material.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color']);image.pack()
    return material,colors,path

def skeleton(spec):
    bpy.ops.object.select_all(action='DESELECT')
    data=bpy.data.armatures.new('DeadAirIndieSkeleton');arm=bpy.data.objects.new('Armature',data);bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active=arm;arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
    defs={
        'root':((0,0,0),(0,0,.16),None),
        'pelvis':((0,0,spec['hip']),(0,0,spec['waist']),'root'),
        'spine':((0,0,spec['waist']),(0,0,spec['chest']),'pelvis'),
        'chest':((0,0,spec['chest']),(0,0,spec['shoulder']),'spine'),
        'neck':((0,0,spec['shoulder']),(0,0,spec['neck']),'chest'),
        'head':((0,0,spec['neck']),(0,0,spec['height']),'neck'),
    }
    markers={}
    for side,suffix in [(1,'L'),(-1,'R')]:
        shoulder=Vector((side*spec['shoulder_x'],0,spec['shoulder']))
        elbow=shoulder+Vector((side*math.sin(.20)*spec['upper'],-.018,-math.cos(.20)*spec['upper']))
        wrist=elbow+Vector((side*.055,-.016,-spec['lower']))
        palmend=wrist+Vector((side*.004,-.008,-.115))
        hip=Vector((side*spec['hip_x'],0,spec['hip']));knee=Vector((side*spec['hip_x'],-.016,spec['knee']))
        ankle=Vector((side*spec['hip_x'],0,spec['ankle']));toebase=Vector((side*spec['hip_x'],-.11,.055));toetip=Vector((side*spec['hip_x'],-.205,.045))
        defs.update({'clavicle.'+suffix:((0,0,spec['shoulder']),shoulder,'chest'),
            'upper_arm.'+suffix:(shoulder,elbow,'clavicle.'+suffix),'forearm.'+suffix:(elbow,wrist,'upper_arm.'+suffix),
            'hand.'+suffix:(wrist,palmend,'forearm.'+suffix),'thigh.'+suffix:(hip,knee,'pelvis'),
            'shin.'+suffix:(knee,ankle,'thigh.'+suffix),'foot.'+suffix:(ankle,toebase,'shin.'+suffix),
            'toe.'+suffix:(toebase,toetip,'foot.'+suffix)})
        fingers={}
        for index,name in enumerate(['index','middle','ring','little']):
            width=(index-1.5)*.018
            length=[.073,.084,.077,.063][index]
            start=palmend+Vector((side*width,-.001,.010))
            points=[start,start+Vector((side*.002,-.007,-length*.38)),start+Vector((side*.003,-.014,-length*.73)),start+Vector((side*.004,-.020,-length))]
            for segment in range(3):defs[name+'.%02d.'%(segment+1)+suffix]=(points[segment],points[segment+1],('hand.'+suffix) if segment==0 else name+'.%02d.'%segment+suffix)
            fingers[name]=points
        start=wrist+Vector((-side*.036,-.006,-.036));points=[start,start+Vector((-side*.025,-.018,-.018)),start+Vector((-side*.039,-.030,-.037)),start+Vector((-side*.041,-.042,-.057))]
        for segment in range(3):defs['thumb.%02d.'%(segment+1)+suffix]=(points[segment],points[segment+1],('hand.'+suffix) if segment==0 else 'thumb.%02d.'%segment+suffix)
        fingers['thumb']=points
        markers[suffix]=dict(shoulder=shoulder,elbow=elbow,wrist=wrist,palmend=palmend,hip=hip,knee=knee,ankle=ankle,fingers=fingers)
    for name,(a,b,parent) in defs.items():
        bone=data.edit_bones.new(name);bone.head=a;bone.tail=b
        if parent:bone.parent=data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT');arm.show_in_front=True;arm.data.display_type='STICK'
    for bone in arm.pose.bones:bone.rotation_mode='QUATERNION'
    arm['DeadAirIndieRig']=True;arm['source_coordinate_system']='metres; Z up; -Y face; relaxed A pose'
    return arm,markers

def torso_weights(spec,p):
    z=p[2]
    if z<spec['waist']-.08:return {'pelvis':1}
    if z<spec['waist']+.08:return blend('pelvis','spine',(z-spec['waist']+.08)/.16)
    if z<spec['chest']+.08:return blend('spine','chest',(z-spec['chest']+.08)/.16)
    return {'chest':1}

def joined_torso_sleeves(s,spec,markers,rings):
    """Sewn shoulder openings share their vertices with the sleeve rings.

    The upper torso has ten-edge deltoid openings, not capped cylinders or
    hidden ball-joint patches. Its collar slopes into the actual shoulder.
    """
    sides=12;verts=[];weights=[]
    for row,(x,y,z,rx,ry) in enumerate(rings):
        for j in range(sides):
            angle=math.pi/12+j*TAU/sides;p=(x+rx*math.cos(angle),y+ry*math.sin(angle),z)
            verts.append(p);weights.append(torso_weights(spec,p))
    cut={'L':{10,11,0},'R':{4,5,6}}
    faces=[]
    for row in range(len(rings)-1):
        for j in range(sides):
            if row in (4,5) and any(j in region for region in cut.values()):continue
            faces.append((row*sides+j,row*sides+(j+1)%sides,(row+1)*sides+(j+1)%sides,(row+1)*sides+j))
    faces.extend([tuple(range(sides-1,-1,-1)),tuple((len(rings)-1)*sides+j for j in range(sides))])
    for side,suffix,start in [(1,'L',10),(-1,'R',4)]:
        columns=[(start+i)%sides for i in range(4)]
        border=([4*sides+j for j in columns]+[5*sides+columns[-1],6*sides+columns[-1]]+
            [6*sides+j for j in reversed(columns[:-1])]+[5*sides+columns[0]])
        # Explicit corner ordering follows the cut perimeter exactly.
        for index in border:weights[index]=blend('chest','clavicle.'+suffix,.44)
        m=markers[suffix];a,b,c=m['shoulder'],m['elbow'],m['wrist'];radius=spec['arm_width']
        direction=(b-a).normalized();u=direction.cross(Vector((0,1,0))).normalized();v=direction.cross(u).normalized()
        points=[a.lerp(b,.12),a.lerp(b,.25),a.lerp(b,.38)] if spec['dress'] else [a.lerp(b,.12),a.lerp(b,.25),a.lerp(b,.78),b,b.lerp(c,.22),b.lerp(c,.86),c]
        radii=[radius*1.20,radius*1.16,radius*1.10] if spec['dress'] else [radius*1.10,radius*1.06,radius*.92,radius*.87,radius*.83,radius*.74,radius*.70]
        # Match the ring's winding and seam to the anatomical hole by sorting
        # angular correspondence in its transverse plane.
        center=sum((v3(verts[index]) for index in border),Vector())/len(border)
        angles=[math.atan2((v3(verts[index])-center).dot(v),(v3(verts[index])-center).dot(u)) for index in border]
        unwrapped=[angles[0]]
        for angle in angles[1:]:
            while angle-unwrapped[-1]>math.pi:angle-=TAU
            while angle-unwrapped[-1]<-math.pi:angle+=TAU
            unwrapped.append(angle)
        winding=1 if unwrapped[-1]>unwrapped[0] else -1
        first=unwrapped[0]
        prev=border
        for point,rad in zip(points,radii):
            current=[]
            for j in range(10):
                angle=first+winding*j*TAU/10;p=point+u*math.cos(angle)*rad+v*math.sin(angle)*rad
                current.append(len(verts));verts.append(tuple(p))
                t=(p-a).dot((c-a).normalized())/(c-a).length;el=(b-a).length/(c-a).length
                weights.append(blend('clavicle.'+suffix,'upper_arm.'+suffix,clamp(t/.16)*.85+.15) if t<.16 else blend('upper_arm.'+suffix,'forearm.'+suffix,(t-el+.11)/.22))
            for j in range(10):faces.append((prev[j],prev[(j+1)%10],current[(j+1)%10],current[j]))
            prev=current
        faces.append(tuple(reversed(prev)))
    offset=len(s.vertices);s.vertices.extend(verts);s.weights.extend(weights);s.faces.extend(tuple(offset+i for i in face) for face in faces);s.colors.extend([3]*len(faces))

def sculpt_body(role,spec,markers):
    s=Sculpt();hip=spec['hip'];waist=spec['waist'];chest=spec['chest'];shoulder=spec['shoulder'];male=not spec['dress'];stocky=role=='engineer'
    width=.29 if stocky else .175 if male else .157
    depth=.17 if stocky else .105
    torso=[(0,.012,hip-.07,width*.84,depth*.96),(0,0,hip+.035,width*.95,depth),
        (0,0,waist,width*(.93 if male else .79),depth*.96),(0,0,(waist+chest)*.5,width*(1.0 if male else .87),depth),
        (0,0,chest,width*(1.0 if male else 1.03),depth*1.12),(0,0,shoulder-.045,width*(1.08 if male else 1.24),depth),
        (0,0,shoulder-.008,width*(1.00 if male else 1.06),depth*.81),(0,0,shoulder+.055,width*.41,depth*.59)]
    joined_torso_sleeves(s,spec,markers,torso)
    # Clean continuous clothing surfaces; small graphic panels are deliberate.
    if spec['dress']:
        hem=hip-.19 if role=='operator' else hip-.33
        skirtwidth=.216 if role=='operator' else .258
        def skirtweight(p):
            # The lower dress follows the actual thigh envelope. Limiting it
            # to a small pelvis/thigh mix lets crouched legs emerge through its
            # front. The central panel blends both legs instead of a hard seam.
            t=clamp((hip+.035-p[2])/(hip+.035-hem))**.65
            left=clamp(.5+p[0]/.13)
            return {'pelvis':1-t,'thigh.L':t*left,'thigh.R':t*(1-left)}
        s.loft([(0,0,hem,skirtwidth,.137),(0,0,hem+.035,skirtwidth,.138),(0,0,hip-.025,.18,.116),(0,0,waist-.06,.14,.106)],3,skirtweight,12,math.pi/12)
        # Waist band and modest square neckline.
        s.loft([(0,0,waist-.025,.148,.113),(0,0,waist+.012,.149,.113)],5,{'spine':1},12,math.pi/12)
        s.panel([(-.064,-depth*.83-.006,shoulder+.010),(.064,-depth*.83-.006,shoulder+.010),
            (0,-depth*1.10-.006,shoulder-.075)],9,{'chest':1})
    else:
        # Shirt bib and sharply cut lapels. They are attached to the chest,
        # never classified by position or texture colour during skin binding.
        front=-depth*1.12-.004
        s.panel([(-.088,front,shoulder-.008),(.088,front,shoulder-.008),(.055,front,chest-.16),(-.055,front,chest-.16)],12,{'chest':1})
        for side in [-1,1]:
            s.panel([(side*.075,front-.004,shoulder-.005),(side*.16,front-.002,shoulder-.08),(side*.055,front-.006,chest-.15),(side*.036,front-.009,chest+.012)],4,{'chest':1})
        s.loft([(0,0,hip+.02,width*.94,depth*1.045),(0,0,hip+.055,width*.94,depth*1.045)],9,{'pelvis':1},12,math.pi/12)
        s.panel([(-.020,-depth-.018,hip+.06),(.020,-depth-.018,hip+.06),(.020,-depth-.018,hip+.025),(-.020,-depth-.018,hip+.025)],14,{'pelvis':1})
        if stocky:
            s.panel([(-.024,front-.015,shoulder-.057),(.024,front-.015,shoulder-.057),(.019,front-.018,shoulder-.095),(-.019,front-.018,shoulder-.095)],23,{'chest':1})
            s.panel([(-.014,front-.018,shoulder-.09),(.014,front-.018,shoulder-.09),(.028,front-.020,chest-.205),(0,front-.022,chest-.247),(-.028,front-.020,chest-.205)],23,{'chest':1})
        else:
            s.panel([(.07,front-.014,chest+.028),(.12,front-.014,chest+.028),(.12,front-.014,chest-.025),(.07,front-.014,chest-.025)],15,{'chest':1})
        for z in [chest-.07,chest-.18,waist-.025]:s.orb((0,front-.011,z),(.009,.004,.009),14,{'chest':1} if z>waist else {'spine':1},6,3)
    # Neck is a connected taper with a transition ring to the head.
    s.loft([(0,0,shoulder-.012,.060,.054),(0,0,spec['neck']-.02,.049,.048),(0,0,spec['neck']+.025,.058,.054)],0,lambda p:blend('neck','head',clamp((p[2]-spec['neck']+.035)/.07)),8)
    for side,suffix in [(1,'L'),(-1,'R')]:
        m=markers[suffix];a,b,c=m['shoulder'],m['elbow'],m['wrist'];r=spec['arm_width']
        # Seamless sleeve around the elbow with several support loops.
        points=[a,a.lerp(b,.16),a.lerp(b,.78),b,b.lerp(c,.22),b.lerp(c,.86),c]
        radii=[r*1.10,r*1.10,r*.92,r*.87,r*.83,r*.74,r*.70]
        def armweight(p):
            p=v3(p);t=(p-a).dot((c-a).normalized())/(c-a).length;el=(b-a).length/(c-a).length
            if t<.16:return blend('clavicle.'+suffix,'upper_arm.'+suffix,t/.16*.85+.15)
            return blend('upper_arm.'+suffix,'forearm.'+suffix,(t-el+.11)/.22)
        if spec['dress']:
            # Skin arms with a short, distinct sleeve rather than painted skin.
            s.tube([a.lerp(b,.34),a.lerp(b,.78),b,b.lerp(c,.22),b.lerp(c,.86),c],
                [r*1.04,r*.92,r*.87,r*.83,r*.74,r*.70],0,armweight,8)
        s.tube([b.lerp(c,.87),c],[r*.77,r*.76],12 if stocky else 4,{'forearm.'+suffix:1},8)
        palmcenter=c.lerp(m['palmend'],.55)
        s.orb(palmcenter,(.045*(1.15 if stocky else 1),.029,.072),0,{'hand.'+suffix:1},8,4)
        for name,points in m['fingers'].items():
            radius=.014 if name=='thumb' else .0105
            for segment in range(3):
                start,end=points[segment],points[segment+1];bone=name+'.%02d.'%(segment+1)+suffix
                s.tube([start,end],[radius*(1-segment*.13),radius*(.91-segment*.13)],0,{bone:1},6)
        hippt,knee,ankle=m['hip'],m['knee'],m['ankle'];r=spec['leg_width']
        legpoints=[hippt,hippt.lerp(knee,.14),hippt.lerp(knee,.79),knee,knee.lerp(ankle,.20),knee.lerp(ankle,.84),ankle]
        def legweight(p):return blend('thigh.'+suffix,'shin.'+suffix,(spec['knee']+.055-p[2])/.11)
        legradii=[r*1.10,r*1.12,r*.84,r*.80,r*.79,r*.66,r*.65]
        if spec['dress']:
            hem=hip-.19 if role=='operator' else hip-.33
            start=clamp((hip-hem-.025)/(hip-spec['knee']))
            legpoints=[hippt.lerp(knee,start),hippt.lerp(knee,(start+.79)*.5),hippt.lerp(knee,.79),knee,knee.lerp(ankle,.20),knee.lerp(ankle,.84),ankle]
            legradii=[r*.98,r*.90,r*.84,r*.80,r*.79,r*.66,r*.65]
        s.tube(legpoints,legradii,9 if spec['dress'] else 3,legweight,8)
        if spec['dress']:
            # Upper legs are separately skinned skin, lower legs dark stockings.
            hem=hip-.19 if role=='operator' else hip-.33
            start=clamp((hip-hem-.008)/(hip-spec['knee']))
            end=min(.99,start+.20)
            s.tube([hippt.lerp(knee,start),hippt.lerp(knee,(start+end)*.5),hippt.lerp(knee,end)],
                [r*.96,r*.92,r*.88],0,{'thigh.'+suffix:1},8)
        x=ankle.x;shoe_w=.073*(1.24 if stocky else 1);shoe_d=.146*(1.15 if stocky else 1)
        def shoeweight(p):return blend('foot.'+suffix,'toe.'+suffix,clamp((-p[1]-.095)/.10)*.78)
        s.loft([(x,-.062,.004,shoe_w,shoe_d),(x,-.062,.025,shoe_w,shoe_d),
            (x,-.064,.057,shoe_w*.93,shoe_d*.97),(x,-.045,.109,shoe_w*.77,shoe_d*.79),(x,0,.158,shoe_w*.66,.053)],9,shoeweight,10,math.pi/10)
        s.loft([(x,-.062,0,shoe_w*1.015,shoe_d*1.015),(x,-.062,.023,shoe_w*1.02,shoe_d*1.02)],10,shoeweight,10,math.pi/10)
        s.tube([ankle+Vector((0,0,-.013)),ankle+Vector((0,0,.075))],[r*.72,r*.75],9,{'foot.'+suffix:1},8)
    face(s,role,spec)
    return s

def face(s,role,spec):
    z=spec['head'];rx=spec['head_width'];ry=spec['head_depth'];rz=spec['head_height'];head={'head':1}
    # A compact six-ring head with readable cheeks, jaw and brow planes.
    s.loft([(0,.008,z-rz,.052,.060),(0,0,z-rz*.69,rx*.73,ry*.80),
        (0,0,z-rz*.24,rx*.97,ry),(0,0,z+rz*.34,rx,ry*1.035),
        (0,.003,z+rz*.76,rx*.82,ry*.91),(0,.004,z+rz,.048,.045)],0,head,12,math.pi/12)
    eyez=z+.038;front=-ry*.99
    for side in [-1,1]:
        x=side*rx*.43
        # Crisp inset graphic eye panels; these are deliberate simple shapes.
        s.panel([(x-.038,front-.005,eyez+.017),(x+.038,front-.005,eyez+.017),
            (x+.033,front-.005,eyez-.016),(x-.033,front-.005,eyez-.016)],9,head,.002)
        s.panel([(x-.029,front-.009,eyez+.010),(x+.029,front-.009,eyez+.010),
            (x+.025,front-.009,eyez-.009),(x-.025,front-.009,eyez-.009)],20,head,.002)
        s.orb((x,front-.013,eyez),(.012,.0025,.014),18 if role=='dispatcher' else 19,head,8,3)
        s.orb((x,front-.017,eyez),(.0055,.002,.010),9,head,6,3)
        # Slightly different eyebrows preserve character rather than template faces.
        lift=.012 if role=='dispatcher' else -.006 if role=='engineer' else .002
        s.panel([(x-.035,front-.014,eyez+.035+lift),(x+.034,front-.014,eyez+.030),
            (x+.034,front-.014,eyez+.040),(x-.035,front-.014,eyez+.045+lift)],7,head)
        s.orb((side*rx*.99,.009,z-.010),(.024,.018,.038),0,head,6,4)
        if spec['dress']:s.orb((side*rx*1.01,-.004,z-.05),(.011,.012,.014),21,head,6,3)
    nose=[(-.013,front+.004,z+.022),(.013,front+.004,z+.022),(.020,front-.019,z-.028),
        (0,front-.037,z-.033),(-.020,front-.019,z-.028),(0,front-.020,z-.046)]
    s.mesh(nose,[(0,1,3),(1,2,3),(0,3,4),(4,3,5),(3,2,5),(0,4,5,2,1)],2,head)
    mouthz=z-.070;mouthfront=-ry*.88-.008
    s.panel([(-.027,mouthfront,mouthz+.004),(.027,mouthfront,mouthz+.002),(.023,mouthfront,mouthz-.009),(-.024,mouthfront,mouthz-.008)],17 if spec['dress'] else 16,head,.002)
    # Hair cap leaves the forehead and all eye geometry exposed.
    s.loft([(0,.007,z+rz*.48,rx*1.055,ry*1.075),(0,.008,z+rz*.86,rx*.88,ry*.94),
        (0,.006,z+rz+.013,rx*.46,ry*.50),(0,.006,z+rz+.019,.013,.013)],6,head,12,math.pi/12)
    if role=='operator':
        for side in [-1,1]:
            s.tube([(side*rx*.91,.015,z+rz*.66),(side*rx*1.11,.013,z+.005),(side*rx*.95,.018,z-rz*.67)],
                [(.043,.068),(.041,.060),(.028,.050)],7,head,8)
        s.panel([(-rx*.92,-ry*.82,z+rz*.53),(rx*.92,-ry*.82,z+rz*.53),
            (rx*.80,-ry*.87,z+.066),(rx*.33,-ry*.89,z+.086),(0,-ry*.91,z+.060),(-rx*.81,-ry*.87,z+.076)],6,head,.008)
        s.loft([(0,.078,z-rz*.64,rx*.92,.071),(0,.074,z+.04,rx*1.08,.079),(0,.062,z+rz*.56,rx,.072)],6,head,10)
    elif role=='dispatcher':
        for side in [-1,1]:
            s.tube([(side*rx*.88,.055,z+rz*.63),(side*rx*1.10,.073,z-.075),
                (side*.15,.085,spec['shoulder']-.02),(side*.125,.092,spec['chest']-.13)],
                [(.054,.062),(.058,.063),(.068,.071),(.045,.056)],6,head,8)
        s.loft([(0,.095,spec['chest']-.16,.145,.060),(0,.108,spec['shoulder']+.02,.162,.071),
            (0,.076,z+.06,rx*1.06,.072)],6,head,10)
        s.panel([(-rx*.85,-ry*.78,z+rz*.57),(rx*.85,-ry*.78,z+rz*.57),
            (rx*.73,-ry*.88,z+.10),(.01,-ry*.90,z+.076),(-rx*.71,-ry*.84,z+.11)],6,head,.008)
    elif role=='engineer':
        # Victor's receding hairline and small moustache remain recognizable.
        s.panel([(-.053,-ry*.95,z-.047),(.052,-ry*.95,z-.047),(.044,-ry*.98,z-.060),(-.046,-ry*.98,z-.060)],7,head)
    else:
        for side in [-1,1]:
            s.tube([(side*.067,-ry*.78,z-.058),(side*.064,-ry*.91,z-.145),
                (side*.051,-ry*.86,z-.285),(side*.043,-ry*.80,z-.345)],
                [(.050,.029),(.048,.038),(.032,.033),(.009,.012)],6,head,8)
        s.panel([(-.052,-ry-.009,z-.039),(.052,-ry-.009,z-.039),(.041,-ry-.012,z-.061),(-.041,-ry-.012,z-.061)],6,head)
        capz=z+rz*.87
        s.loft([(0,.008,capz,rx*1.085,ry*1.10),(0,.009,capz+.046,rx*1.10,ry*1.12),
            (0,.010,capz+.086,rx*.85,ry*.93),(0,.010,capz+.099,.035,.035)],3,head,12,math.pi/12)
        s.panel([(-rx*.86,-ry*.77,capz+.013),(rx*.86,-ry*.77,capz+.013),
            (rx*.88,-ry-.068,capz+.004),(0,-ry-.100,capz-.003),(-rx*.88,-ry-.068,capz+.004)],5,head,.018)
        s.panel([(-.014,-ry-.011,capz+.057),(.014,-ry-.011,capz+.057),(.014,-ry-.011,capz+.031),(-.014,-ry-.011,capz+.031)],15,head)

def create_mesh(sculpt,arm,material,name):
    me=bpy.data.meshes.new(name);me.from_pydata(sculpt.vertices,[],sculpt.faces);me.update()
    obj=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(obj);me.materials.append(material)
    uv=me.uv_layers.new(name='PaletteUV')
    for face,color in zip(me.polygons,sculpt.colors):
        center=((color%8+.5)/8,(color//8+.5)/4)
        for loop in face.loop_indices:uv.data[loop].uv=center
    for bone in arm.data.bones:obj.vertex_groups.new(name=bone.name)
    for index,weights in enumerate(sculpt.weights):
        total=sum(weights.values())
        for name,value in weights.items():
            if value>1e-7:obj.vertex_groups[name].add([index],value/total,'REPLACE')
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    # Keep support loops editable as quads in the Blender source. The modifier
    # evaluates deliberate triangles for render/export without destroying them.
    obj.modifiers.new('Purposeful triangle surfaces','TRIANGULATE')
    skin=obj.modifiers.new('Indie character skin','ARMATURE');skin.object=arm;obj.parent=arm
    return obj

def marker(arm,name,parent,point):
    empty=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(empty)
    empty.empty_display_type='PLAIN_AXES';empty.empty_display_size=.025
    empty.parent=arm;empty.parent_type='BONE';empty.parent_bone=parent
    bpy.context.view_layer.update()
    empty.matrix_world=Matrix.Translation(point)
    bpy.context.view_layer.update()
    if (empty.matrix_world.translation-v3(point)).length>.00001:
        raise RuntimeError(name+' bone-parent marker failed to retain its authored position')
    empty['authored_world_position']=list(point)
    return empty

def reset_pose(arm):
    for p in arm.pose.bones:p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
def turn_world(arm,name,axis,angle):
    p=arm.pose.bones[name];m=p.matrix.copy();q=Quaternion(axis,angle)
    rotation=q@m.to_quaternion();p.matrix=Matrix.LocRotScale(m.translation,rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()
def aim(arm,name,target):
    p=arm.pose.bones[name];m=p.matrix.copy();direction=p.tail-p.head
    q=direction.rotation_difference(v3(target)-p.head)
    p.matrix=Matrix.LocRotScale(p.head,q@m.to_quaternion(),Vector((1,1,1)))
    bpy.context.view_layer.update()
def solve_chain(arm,a,b,target,pole):
    pa,pb=arm.pose.bones[a],arm.pose.bones[b];origin=pa.head.copy();target=v3(target);offset=target-origin
    la,lb=arm.data.bones[a].length,arm.data.bones[b].length
    distance=clamp(offset.length,abs(la-lb)+.0001,la+lb-.0001);direction=offset.normalized()
    along=(la*la-lb*lb+distance*distance)/(2*distance);height=math.sqrt(max(0,la*la-along*along))
    bend=v3(pole)-origin;bend-=direction*bend.dot(direction)
    if bend.length<1e-6:bend=Vector((0,-1,0))
    knee=origin+direction*along+bend.normalized()*height;goal=origin+direction*distance
    aim(arm,a,knee);aim(arm,b,goal);return goal
def preserve_foot(arm,suffix,goal):
    # Keep each whole sole horizontal throughout stance and swing.
    rest=arm.data.bones['foot.'+suffix].matrix_local.to_quaternion()
    arm.pose.bones['foot.'+suffix].matrix=Matrix.LocRotScale(goal,rest,Vector((1,1,1)))
    bpy.context.view_layer.update()

def foot_cycle(phase,stride,lift,stance):
    phase%=1
    if phase<stance:return stride*.5-stride*phase/stance,0
    t=(phase-stance)/(1-stance)
    return -stride*.5+stride*smooth(t),lift*math.sin(math.pi*t)**1.5

CLIPS=[('Idle',2,True,0),('Walk',.75,True,1.422222),('Run',.5,True,3.2),('UseItem',2,False,0),
    ('WalkBack',.75,True,1.422222),('StrafeLeft',.75,True,1.422222),('StrafeRight',.75,True,1.422222),
    ('CrouchIdle',2,True,0),('CrouchWalk',22/24,True,.604196)]

def animate(arm,spec,markers):
    clips=[];arm.animation_data_create()
    for name,duration,loop,speed in CLIPS:
        count=round(duration*FPS)+1;action=bpy.data.actions.new(name);action.use_fake_user=True;arm.animation_data.action=action
        for frame in range(1,count+1):
            t=(frame-1)/(count-1);bpy.context.scene.frame_set(frame);reset_pose(arm);bpy.context.view_layer.update()
            moving=name in ['Walk','WalkBack','StrafeLeft','StrafeRight','Run','CrouchWalk'];crouch=name.startswith('Crouch')
            drop=.055 if moving else 0
            if name=='Run':drop=.135
            if crouch:drop=.28 if name=='CrouchIdle' else .30
            pelvis=arm.pose.bones['pelvis'];mat=pelvis.matrix.copy();mat.translation.z-=drop;pelvis.matrix=mat;bpy.context.view_layer.update()
            if crouch:turn_world(arm,'spine',(1,0,0),math.radians(21))
            if moving:
                stride=.80 if name=='Run' else .36 if crouch else .64
                # Short stocky legs use a proportional stride instead of being stretched.
                stride*=min(1,(spec['hip']-spec['ankle'])/.76)
                lift=.16 if name=='Run' else .055 if crouch else .075;stance=.5 if name=='Run' else .65 if crouch else .60
                for side,suffix,phase in [(1,'L',t),(-1,'R',t+.5)]:
                    fore,height=foot_cycle(phase,stride,lift,stance);target=markers[suffix]['ankle'].copy()
                    if name=='WalkBack':target.y+=fore
                    elif name.startswith('Strafe'):target.x+=fore*(-1 if name=='StrafeLeft' else 1)
                    else:target.y-=fore
                    target.z+=height
                    goal=solve_chain(arm,'thigh.'+suffix,'shin.'+suffix,target,(side*.15,-.7,spec['knee']))
                    preserve_foot(arm,suffix,goal)
                    armphase=math.sin(phase*TAU);turn_world(arm,'upper_arm.'+suffix,(1,0,0),armphase*(.25 if name=='Run' else .13))
                    turn_world(arm,'forearm.'+suffix,(1,0,0),-.10 if name!='Run' else -.34)
                turn_world(arm,'chest',(0,0,1),math.sin(t*TAU)*.025)
            else:
                if crouch:
                    for side,suffix in [(1,'L'),(-1,'R')]:
                        target=markers[suffix]['ankle'];goal=solve_chain(arm,'thigh.'+suffix,'shin.'+suffix,target,(side*.15,-.7,spec['knee']))
                        preserve_foot(arm,suffix,goal)
                turn_world(arm,'chest',(1,0,0),math.sin(t*TAU)*.009)
            turn_world(arm,'head',(0,0,1),math.sin(t*TAU)*.018)
            if name=='UseItem':
                amount=math.sin(math.pi*t)**2
                for suffix,side in [('R',-1),('L',1)]:
                    old=arm.pose.bones['hand.'+suffix].head.copy();target=Vector((side*.14,-.29,spec['chest']+.05))
                    goal=old.lerp(target,amount if suffix=='R' else amount*.62)
                    solve_chain(arm,'upper_arm.'+suffix,'forearm.'+suffix,goal,(side*.40,-.08,spec['waist']))
                turn_world(arm,'head',(1,0,0),amount*.10)
            for p in arm.pose.bones:
                p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name);p.keyframe_insert('location',frame=frame,group=p.name)
        action['loop']=loop;action['authored_speed_mps']=speed
        # Linear bake keeps a deterministic stance with no Bezier overshoot.
        for slot in action.slots:
            for layer in action.layers:
                for strip in layer.strips:
                    bag=strip.channelbag(slot)
                    if bag:
                        for curve in bag.fcurves:
                            for key in curve.keyframe_points:key.interpolation='LINEAR'
        clips.append(dict(name=name,duration=(count-1)/FPS,fps=FPS,frames=count,loop=loop,
            authored_speed_mps=speed*min(1,(spec['hip']-spec['ankle'])/.76)))
    arm.animation_data.action=bpy.data.actions['Idle'];bpy.context.scene.frame_set(1)
    return clips

def validate(obj,arm,clips):
    report=dict(unweighted_vertices=0,max_weight_sum_error=0,max_influences=0,nonfinite_vertices=0,min_floor_z=1e9,clip_floor={},loop_error={})
    bm=bmesh.new();bm.from_mesh(obj.data)
    report['boundary_edges']=sum(edge.is_boundary for edge in bm.edges)
    report['nonmanifold_edges']=sum(not edge.is_manifold for edge in bm.edges)
    report['degenerate_faces']=sum(face.calc_area()<1e-10 for face in bm.faces)
    report['quad_support_faces']=sum(len(face.verts)==4 for face in bm.faces)
    bm.free()
    for v in obj.data.vertices:
        report['unweighted_vertices']+=not bool(v.groups);report['max_weight_sum_error']=max(report['max_weight_sum_error'],abs(sum(g.weight for g in v.groups)-1));report['max_influences']=max(report['max_influences'],len(v.groups))
    deps=bpy.context.evaluated_depsgraph_get()
    for info in clips:
        arm.animation_data.action=bpy.data.actions[info['name']];floor=1e9;first=None;last=None
        for frame in range(1,info['frames']+1):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update();ev=obj.evaluated_get(deps);me=ev.to_mesh();points=[ev.matrix_world@v.co for v in me.vertices];ev.to_mesh_clear()
            report['nonfinite_vertices']+=sum(not all(math.isfinite(x) for x in p) for p in points)
            floor=min(floor,min(p.z for p in points));first=points if first is None else first;last=points
        report['clip_floor'][info['name']]=floor;report['min_floor_z']=min(report['min_floor_z'],floor)
        if info['loop']:report['loop_error'][info['name']]=max((a-b).length for a,b in zip(first,last))
    report['passed']=report['unweighted_vertices']==0 and report['max_weight_sum_error']<1e-5 and report['nonfinite_vertices']==0 and report['min_floor_z']>-.006 and max(report['loop_error'].values())<1e-4 and report['nonmanifold_edges']==0 and report['degenerate_faces']==0
    arm.animation_data.action=bpy.data.actions['Idle'];bpy.context.scene.frame_set(1)
    return report

def preview(obj,arm,role,spec,front_only=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=12;scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED';scene.render.threads=4
    scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100
    if scene.world is None:scene.world=bpy.data.worlds.new('Indie preview world')
    scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.105,.135,.155,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
    scene.view_settings.view_transform='Standard';scene.view_settings.look='Medium High Contrast';scene.view_settings.exposure=0
    staging=[]
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.009));floor=bpy.context.object;staging.append(floor)
    mat=bpy.data.materials.new('Preview floor');mat.diffuse_color=(.17,.205,.22,1);floor.data.materials.append(mat)
    for location,power,size in [((-3,-4,5),430,4),((4,-2,3.5),300,3),((0,3,5),580,3)]:
        bpy.ops.object.light_add(type='AREA',location=location);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size
        light.rotation_euler=(Vector((0,0,spec['height']*.5))-light.location).to_track_quat('-Z','Y').to_euler();staging.append(light)
    bpy.ops.object.camera_add(location=(2.5,-5.5,2.6));cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=spec['height']*1.18
    target=Vector((0,0,spec['height']*.51));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();scene.camera=cam;staging.append(cam)
    fixtures=[('Idle',0,'front')] if front_only else [('Idle',0,'front'),('Walk',.18,'walk'),('CrouchIdle',0,'crouch')]
    for name,phase,view in fixtures:
        arm.animation_data.action=bpy.data.actions[name];scene.frame_set(1+round((bpy.data.actions[name].frame_range[1]-1)*phase))
        scene.render.filepath=str(ART/'Previews'/(role+'_'+view+'.png'));bpy.ops.render.render(write_still=True)
    for item in staging:bpy.data.objects.remove(item,do_unlink=True)
    arm.animation_data.action=bpy.data.actions['Idle'];scene.frame_set(1)

def build(role,skip_render=False,no_export=False,front_only=False):
    bpy.ops.wm.read_factory_settings(use_empty=True);spec=CAST[role];bpy.context.scene.render.fps=FPS
    mat,colors,atlas=make_palette(role,spec);arm,markers=skeleton(spec);sculpt=sculpt_body(role,spec,markers);obj=create_mesh(sculpt,arm,mat,spec['name']+'_IndieBody')
    eye=Vector((0,-spec['head_depth']-.017,spec['head']+.038));mouth=Vector((0,-spec['head_depth']*.88-.010,spec['head']-.075))
    marker(arm,'MouthSocket','head',mouth);marker(arm,'EyeSocket','head',eye)
    marker(arm,'IndieCast','root',Vector((0,0,0)))
    torsofront=max(-p[1] for p in sculpt.vertices if abs(p[0])<.15 and spec['waist']<p[2]<spec['shoulder'])
    marker(arm,'TorsoFront','chest',Vector((0,-torsofront,spec['chest'])))
    for suffix,m in markers.items():
        grip=m['wrist']+Vector((0,-.032,-.120))
        marker(arm,'ItemSocket.'+suffix,'hand.'+suffix,grip);marker(arm,'GripContact.'+suffix,'hand.'+suffix,grip)
        marker(arm,'FootContact.'+suffix,'foot.'+suffix,Vector((m['ankle'].x,-.061,0)))
        marker(arm,'FootToeContact.'+suffix,'toe.'+suffix,Vector((m['ankle'].x,-.194,0)))
    clips=animate(arm,spec,markers);validation=validate(obj,arm,clips)
    if not validation['passed']:raise RuntimeError(role+' invalid rig: '+json.dumps(validation))
    if not skip_render:preview(obj,arm,role,spec,front_only)
    source=ART/(role+'.blend');bpy.ops.wm.save_as_mainfile(filepath=str(source))
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);arm.select_set(True)
    for item in bpy.data.objects:
        if item.type=='EMPTY' and item.parent==arm:item.select_set(True)
    bpy.context.view_layer.objects.active=arm
    export=MODELS/('character_'+role+'.fbx')
    if not no_export:bpy.ops.export_scene.fbx(filepath=str(export),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',use_space_transform=True,bake_space_transform=False,
        add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',use_armature_deform_only=True,
        bake_anim=True,bake_anim_use_all_bones=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,path_mode='AUTO',embed_textures=False)
    unity=lambda p:[round(p[0],6),round(p[2],6),round(-p[1],6)]
    obj.data.calc_loop_triangles()
    result=dict(role=role,name=spec['name'],vertices=len(obj.data.vertices),triangles=len(obj.data.loop_triangles),source_polygons=len(obj.data.polygons),bones=len(arm.data.bones),
        bone_names=[b.name for b in arm.data.bones],height_m=spec['height'],source_eye=unity(eye),mouth=unity(mouth),
        torso_front_m=torsofront,atlas=str(atlas.relative_to(ROOT)).replace('\\','/'),material=mat.name,
        source=str(source.relative_to(ROOT)).replace('\\','/'),runtime=str(export.relative_to(ROOT)).replace('\\','/'),
        palette_colors=colors,palette=dict(skin=colors[0],skin_shadow=colors[1],nail=colors[2],cloth=colors[3],cream=colors[12],metal=colors[21]),
        skin_color=colors[0],sleeve_color=colors[3],cuff_color=colors[12 if role=='engineer' else 4],
        clips=clips,validation=validation,rest_pose='relaxed lowered arms; Blender world-authored quaternion FK; Z up / -Y face',
        foot_contacts={suffix:unity(Vector((m['ankle'].x,-.061,0))) for suffix,m in markers.items()},
        shoulder_positions={suffix:unity(m['shoulder']) for suffix,m in markers.items()})
    (ART/(role+'_manifest.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('INDIE CAST READY '+json.dumps({k:result[k] for k in ['role','vertices','triangles','bones','source_eye','validation']}),flush=True)
    return result

def main():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    roles=[args[args.index('--role')+1]] if '--role' in args else list(CAST)
    if '--roles' in args:roles=args[args.index('--roles')+1].split(',')
    for directory in [ART,ART/'Previews',MODELS,TEXTURES]:directory.mkdir(parents=True,exist_ok=True)
    for role in roles:build(role,'--skip-render' in args,'--no-export' in args,'--front-only' in args)
    characters=[json.loads((ART/(role+'_manifest.json')).read_text()) for role in CAST if (ART/(role+'_manifest.json')).exists()]
    manifest=dict(formatVersion=1,coordinateSystem='source Blender metres Z up / -Y face; Unity X right Y up +Z face',characters=characters)
    (ART/'cast_manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')

if __name__=='__main__':main()
