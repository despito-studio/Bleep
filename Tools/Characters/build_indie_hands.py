"""Build the clean, articulated FPS hands used by the Indie Cast.

Run after build_indie_cast.py, with Blender --background --python this_file.
The 44 canonical parts retain the game's tested item-contact transforms.
All geometry is authored here; no slices or UVs from the old models survive.
"""
from pathlib import Path
import json
import math
import sys

import bpy
import bmesh

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from source_hand_segments import _canonical_mesh, _validate_parts

ROLES = ("operator", "engineer", "journalist", "dispatcher")
DEFAULTS = {
    "operator": {"skin": (.77, .57, .48), "cloth": (.31, .075, .13)},
    "engineer": {"skin": (.70, .53, .43), "cloth": (.075, .09, .10)},
    "journalist": {"skin": (.72, .60, .45), "cloth": (.065, .285, .29)},
    "dispatcher": {"skin": (.75, .54, .41), "cloth": (.57, .16, .22)},
}


def material(role, name, color):
    mat = bpy.data.materials.new(role + "_indie_hand_" + name)
    mat.diffuse_color = (*color[:3], 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color[:3], 1)
    bsdf.inputs["Roughness"].default_value = .82
    return mat


def ring_mesh(name, rings, sides=10):
    """Canonical Z-longitudinal rings, with deliberate wrist/knuckle taper."""
    vertices = [(math.cos(i * math.tau / sides) * x,
                 math.sin(i * math.tau / sides) * y, z)
                for z, x, y in rings for i in range(sides)]
    faces = [tuple(range(sides - 1, -1, -1))]
    for row in range(len(rings) - 1):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((row * sides + i, row * sides + j,
                          (row + 1) * sides + j, (row + 1) * sides + i))
    faces.append(tuple((len(rings) - 1) * sides + i for i in range(sides)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    return mesh


def palm_mesh(name, left):
    # Bevelled palm with a broad knuckle ridge, narrow wrist and thumb root.
    outline = ((-.29, -.5), (.28, -.5), (.46, -.05), (.48, .29),
               (.29, .49), (-.36, .46), (-.50, .11), (-.42, -.18))
    vertices = []
    for depth, inset in ((-.5, .88), (-.35, 1), (.32, 1), (.5, .84)):
        for x, z in outline:
            vertices.append(((-x if left else x) * inset, depth, z))
    n = len(outline)
    faces = [tuple(range(n - 1, -1, -1))]
    for row in range(3):
        for i in range(n):
            j = (i + 1) % n
            faces.append((row * n + i, row * n + j, (row + 1) * n + j, (row + 1) * n + i))
    faces.append(tuple(3 * n + i for i in range(n)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    return mesh


def finish(mesh, mats, material_index=0):
    for mat in mats:
        mesh.materials.append(mat)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    lower = [min(v.co[axis] for v in bm.verts) for axis in range(3)]
    upper = [max(v.co[axis] for v in bm.verts) for axis in range(3)]
    for vertex in bm.verts:
        for axis in range(3):
            vertex.co[axis] = (vertex.co[axis] - (lower[axis] + upper[axis]) * .5) / (upper[axis] - lower[axis])
    layer = bm.loops.layers.uv.get("CleanPalette") or bm.loops.layers.uv.new("CleanPalette")
    for face in bm.faces:
        if material_index:
            face.material_index = material_index
        face.smooth = False
        for loop in face.loops:
            loop[layer].uv = (.5, .5)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def build(role, colors):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bare = role in ("operator", "dispatcher")
    skin = colors["skin"]
    mats = [material(role, "skin", skin),
            material(role, "nails", colors.get("nail", tuple(min(1, c * 1.12) for c in skin))),
            material(role, "sleeve", skin if bare else colors["cloth"]),
            material(role, "cuff", colors.get("metal", (.64, .66, .63)) if bare else colors.get("cream", (.87, .85, .73)))]
    objects = []
    for side in ("Right", "Left"):
        names = ["forearm", "cuff", "palm", "thumb", "thumb_middle", "thumb_tip"]
        names += [f"finger{i}_{part}" for i in range(4) for part in ("proximal", "middle", "distal", "tip")]
        for part in names:
            name = side + "_" + part
            mi = 0
            if part == "forearm":
                mesh = ring_mesh(name, ((-.5, .48, .45), (-.36, .50, .50),
                    (.10, .42, .43), (.41, .33, .34), (.5, .32, .33)))
                mi = 2
            elif part == "cuff":
                mesh = ring_mesh(name, ((-.5, .44, .44), (-.33, .5, .5), (.33, .5, .5), (.5, .44, .44)))
                mi = 3
            elif part == "palm":
                mesh = palm_mesh(name, side == "Left")
            else:
                mesh = _canonical_mesh(name, "tip" if part.endswith("tip") else "tube",
                    "CleanPalette", {"uv": (.5, .5), "material_index": 0}, 4,
                    {"uv": (.5, .5), "material_index": 1})
            finish(mesh, mats, mi)
            obj = bpy.data.objects.new(name, mesh)
            bpy.context.collection.objects.link(obj)
            objects.append(obj)
    _validate_parts(objects)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    out = ROOT / "Assets/Characters/Imported" / ("hand_" + role + ".fbx")
    bpy.ops.export_scene.fbx(filepath=str(out), use_selection=True,
        object_types={"MESH"}, axis_forward="-Z", axis_up="Y", global_scale=1,
        bake_anim=False, add_leaf_bones=False, path_mode="STRIP", mesh_smooth_type="FACE")
    return {"role": role, "parts": len(objects), "triangles": sum(len(o.data.polygons) for o in objects),
            "materials": {m.name: list(m.diffuse_color) for m in mats}, "file": str(out.relative_to(ROOT))}


def main():
    manifest_path = ROOT / "Art/Characters/IndieCast/cast_manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
    roles = {entry["role"]: entry for entry in manifest.get("characters", [])}
    reports = []
    for role in ROLES:
        colors = dict(DEFAULTS[role])
        if isinstance(roles, dict) and isinstance(roles.get(role), dict):
            colors.update(roles[role].get("palette", {}))
        reports.append(build(role, colors))
    output = ROOT / "Art/Characters/IndieCast/hands_manifest.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(reports, indent=2) + "\n", encoding="utf-8")
    print("INDIE_HANDS_OK " + json.dumps(reports))


if __name__ == "__main__":
    main()
