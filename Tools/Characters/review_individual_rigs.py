"""Side-by-side Blender renders of production rest vs fitted review candidate."""
import bpy,sys,math,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/Characters/ReviewCandidates/Rigs'
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
roles=args[args.index('--roles')+1].split(',') if '--roles' in args else ['operator','engineer','journalist','dispatcher']
def load(path,offset):
 with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.objects=src.objects
 objects=[o for o in dst.objects if o is not None]
 for o in objects:bpy.context.scene.collection.objects.link(o)
 arm=next(o for o in objects if o.type=='ARMATURE');arm.location.x+=offset
 return arm
def look(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
for role in roles:
 bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene
 arms=[load(ROOT/'Art/Characters/RestoredCast'/(role+'_model.blend'),-.57),load(OUT/(role+'_rig.blend'),.57)]
 scene.render.engine='CYCLES';scene.cycles.samples=6;scene.cycles.use_denoising=False;scene.render.threads_mode='FIXED';scene.render.threads=2
 scene.render.resolution_x=960;scene.render.resolution_y=800;scene.render.resolution_percentage=100
 scene.world=bpy.data.worlds.new('Rig review background');scene.world.use_nodes=True
 bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.06,.08,.11,1);bg.inputs[1].default_value=.55
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.014));floor=bpy.context.object
 mat=bpy.data.materials.new('Review floor');mat.diffuse_color=(.075,.095,.12,1);floor.data.materials.append(mat)
 for pos,power,size in [((-3,-4,5),650,4),((4,-2,3),340,3),((0,3,5),420,3)]:
  bpy.ops.object.light_add(type='AREA',location=pos);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;look(light,(0,0,1))
 bpy.ops.object.camera_add(location=(0,-6,1.06));cam=bpy.context.object;cam.data.type='ORTHO';cam.data.ortho_scale=2.55;scene.camera=cam;look(cam,(0,0,1.06))
 labelmat=bpy.data.materials.new('Review labels');labelmat.diffuse_color=(.85,.90,.94,1)
 labels=[]
 for x,text in [(-.90,'BEFORE'),(.23,'FITTED CANDIDATE')]:
  bpy.ops.object.text_add(location=(x,-.8,-.10),rotation=(math.pi/2,0,0));o=bpy.context.object;o.data.body=text;o.data.size=.058;o.data.materials.append(labelmat);labels.append(o)
 scene.render.filepath=str(OUT/(role+'_rig_comparison.png'));bpy.ops.render.render(write_still=True)
 bone_mat=bpy.data.materials.new('Fitted rig overlay');bone_mat.diffuse_color=(1,.28,.015,1);bone_mat.use_nodes=True
 node=next(n for n in bone_mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(1,.28,.015,1);node.inputs['Emission Color'].default_value=(1,.20,.01,1);node.inputs['Emission Strength'].default_value=.6
 overlays=[]
 for arm in arms:
  for bone in arm.data.bones:
   if bone.name.startswith(('secondary_','thumb.','index.','middle.','ring.','little.')) or bone.name=='root':continue
   a=arm.matrix_world@bone.head_local;b=arm.matrix_world@bone.tail_local;a.y=b.y=-1
   curve=bpy.data.curves.new('Joint axis','CURVE');curve.dimensions='3D';curve.bevel_depth=.0035;curve.bevel_resolution=2
   spline=curve.splines.new('POLY');spline.points.add(1);spline.points[0].co=(*a,1);spline.points[1].co=(*b,1)
   o=bpy.data.objects.new('Joint axis',curve);scene.collection.objects.link(o);o.data.materials.append(bone_mat);overlays.append(o)
 scene.render.filepath=str(OUT/(role+'_rig_overlay.png'));bpy.ops.render.render(write_still=True)
 if role=='journalist':
  for o in overlays+labels:o.hide_render=True
  for arm,y in zip(arms,[-.62,.62]):arm.location.x=0;arm.location.y=y
  cam.location=(4,0,.25);look(cam,(0,0,.25));cam.data.ortho_scale=1.95;scene.render.resolution_x=1400;scene.render.resolution_y=550
  scene.render.filepath=str(OUT/'journalist_feet_comparison.png');bpy.ops.render.render(write_still=True)
 print('RIG REVIEW',role,flush=True)
