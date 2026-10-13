"""Render the four saved static character models together, without animation."""
import bpy
import math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Art/Characters/RestoredCast'
bpy.ops.wm.read_factory_settings(use_empty=True)
for role,label,x in [('operator','VIVIAN',-2.4),('engineer','VICTOR',-.8),
                     ('journalist','EDGAR',.8),('dispatcher','HAZEL',2.4)]:
    with bpy.data.libraries.load(str(ART/(role+'_model.blend')),link=False) as (source,target):
        target.objects=source.objects
    objects=[obj for obj in target.objects if obj is not None]
    arm=next(obj for obj in objects if obj.type=='ARMATURE')
    for obj in objects:
        if obj.type=='ARMATURE' or (obj.type=='MESH' and obj.get('authored_part')):
            bpy.context.scene.collection.objects.link(obj)
    arm.animation_data_clear();arm.location.x+=x
    for bone in arm.pose.bones:
        bone.location=(0,0,0);bone.rotation_mode='QUATERNION';bone.rotation_quaternion=(1,0,0,0);bone.scale=(1,1,1)
    font=bpy.data.curves.new(label,'FONT');font.body=label;font.align_x='CENTER';font.size=.115
    text=bpy.data.objects.new(label,font);bpy.context.scene.collection.objects.link(text)
    text.location=(x,-.12,2.17);text.rotation_euler=(math.pi/2,0,0)

scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU'
scene.cycles.samples=12;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.resolution_x=1600;scene.render.resolution_y=640;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Neutral model studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.13,.17,.20,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012))
floor=bpy.context.object;material=bpy.data.materials.new('Studio floor');material.diffuse_color=(.08,.11,.13,1)
floor.data.materials.append(material)
for location,power,size in [((-4,-5,6),950,6),((4,-3,4),600,5),((0,4,6),800,5)]:
    bpy.ops.object.light_add(type='AREA',location=location);light=bpy.context.object
    light.data.energy=power;light.data.shape='DISK';light.data.size=size
    light.rotation_euler=(Vector((0,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(0,-10,1.15));camera=bpy.context.object
camera.data.type='ORTHO';camera.data.ortho_scale=6.4
camera.rotation_euler=(Vector((0,0,1.06))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
scene.render.filepath=str(ART/'Previews/lineup-static.png')
bpy.ops.render.render(write_still=True)
print('RESTORED STATIC LINEUP '+scene.render.filepath,flush=True)
