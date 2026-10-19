"""Blender batch conversion: keep material parts, bake metre transforms, produce LODs."""
import bpy
import json
import pathlib
import shutil
import sys
from mathutils import Matrix, Vector

ROOT = pathlib.Path(__file__).resolve().parents[2]
manifest = json.loads((ROOT / "Art/ProductionSources/PolyHaven/manifest.json").read_text())
records = []
record_path = ROOT / "Assets/ArtProduction/model-manifest.json"
existing = {record["key"]: record for record in json.loads(record_path.read_text())["models"]} if record_path.exists() else {}
only = next((argument.split("=", 1)[1] for argument in sys.argv if argument.startswith("--only=")), None)


def image_path(doc, texture):
    if not texture:
        return ""
    return doc["images"][doc["textures"][texture["index"]]["source"]]["uri"]


for asset in manifest["assets"]:
    if asset["kind"] != "model":
        continue
    key = asset["key"]
    source = ROOT / asset["source"]
    folder = ROOT / "Assets/ArtProduction/SourceModels" / key
    folder.mkdir(parents=True, exist_ok=True)
    if key in existing and (folder / "detail.fbx").exists() and (folder / "distance.fbx").exists() and ("--force" not in sys.argv or (only is not None and key != only)):
        records.append(existing[key])
        print(f"PRODUCTION_MODEL_CACHED {key}", flush=True)
        continue
    doc = json.loads(source.read_text())
    materials = []
    material_names = {}
    renamed_materials = []
    for material_index, material in enumerate(doc.get("materials", [])):
        original_name = material["name"]
        duplicate_index = material_names.get(original_name, 0)
        material_names[original_name] = duplicate_index + 1
        imported_name = original_name if duplicate_index == 0 else original_name + f".{duplicate_index:03}"
        unique_name = original_name if duplicate_index == 0 else original_name + "_slot_" + str(material_index)
        renamed_materials.append((imported_name, unique_name))
        pbr = material.get("pbrMetallicRoughness", {})
        maps = {}
        for channel, texture in (("albedo", pbr.get("baseColorTexture")), ("normal", material.get("normalTexture")), ("mask", pbr.get("metallicRoughnessTexture")), ("emission", material.get("emissiveTexture"))):
            uri = image_path(doc, texture)
            if uri:
                target = folder / pathlib.Path(uri).name
                shutil.copy2(source.parent / uri, target)
                maps[channel] = target.relative_to(ROOT).as_posix()
        materials.append({"name": unique_name, "albedo": maps.get("albedo", ""), "normal": maps.get("normal", ""), "mask": maps.get("mask", ""), "emission": maps.get("emission", ""), "alphaMode": material.get("alphaMode", "OPAQUE"), "alphaCutoff": material.get("alphaCutoff", .5), "doubleSided": material.get("doubleSided", False), "metallicFactor": pbr.get("metallicFactor", 1), "roughnessFactor": pbr.get("roughnessFactor", 1), "baseColorFactor": pbr.get("baseColorFactor", [1, 1, 1, 1]), "transmission": material.get("extensions", {}).get("KHR_materials_transmission", {}).get("transmissionFactor", 0)})
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(source))
    for old_name, new_name in renamed_materials:
        if old_name != new_name:
            bpy.data.materials[old_name].name = new_name
    objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if key in ("military-crate", "trash-can"):
        selected = [obj for obj in objects if (obj.name.endswith("_a") if key == "military-crate" else obj.name.startswith("metal_trash_can_rust"))]
        if not selected:
            raise ValueError("Variant selection produced no assembly: " + key)
        for obj in objects:
            if obj not in selected:
                bpy.data.objects.remove(obj, do_unlink=True)
        objects = selected
    if key in ("pine-tree", "fir-tree", "shrub"):
        # These source files are side-by-side variant packs, not one assembled tree.
        chosen_name = next(node["name"] for node in doc["nodes"] if "mesh" in node)
        chosen = next((obj for obj in objects if obj.name == chosen_name), None)
        if chosen is None:
            raise ValueError(f"Cannot identify foliage variant {chosen_name} for {key}")
        for obj in objects:
            if obj != chosen:
                bpy.data.objects.remove(obj, do_unlink=True)
        objects = [chosen]
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    origin = Vector(((low.x + high.x) * .5, (low.y + high.y) * .5, low.z))
    for obj in objects:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = Matrix.Identity(4)
        obj.data.transform(Matrix.Translation(-origin) @ world)
    for obj in list(bpy.context.scene.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.object.select_all(action="SELECT")
    total_triangles = sum(sum(len(poly.vertices) - 2 for poly in obj.data.polygons) for obj in objects)
    budget = 30000 if key == "potted-plant" else 60000
    if key in ("pine-tree", "fir-tree", "shrub", "potted-plant") and total_triangles > budget:
        for obj in objects:
            bpy.context.view_layer.objects.active = obj
            modifier = obj.modifiers.new("Realtime foliage budget", "DECIMATE")
            modifier.ratio = budget / total_triangles
            modifier.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.ops.export_scene.fbx(filepath=str(folder / "detail.fbx"), use_selection=True, object_types={"MESH"}, add_leaf_bones=False, axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_mesh_modifiers=True)
    tri0 = sum(sum(len(poly.vertices) - 2 for poly in obj.data.polygons) for obj in objects)
    for obj in objects:
        if len(obj.data.polygons) > 150:
            modifier = obj.modifiers.new("Distance silhouette", "DECIMATE")
            modifier.ratio = .25 if key in ("pine-tree", "fir-tree", "shrub") else .40
            modifier.use_collapse_triangulate = True
    bpy.ops.export_scene.fbx(filepath=str(folder / "distance.fbx"), use_selection=True, object_types={"MESH"}, add_leaf_bones=False, axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_mesh_modifiers=True)
    tri1 = 0
    for obj in objects:
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        tri1 += sum(len(poly.vertices) - 2 for poly in mesh.polygons)
        evaluated.to_mesh_clear()
    records.append({"key": key, "asset": asset["asset"], "detail": (folder / "detail.fbx").relative_to(ROOT).as_posix(), "distance": (folder / "distance.fbx").relative_to(ROOT).as_posix(), "materials": materials, "triangles": tri0, "distanceTriangles": tri1, "sourceParts": len(objects)})
    print(f"PRODUCTION_MODEL {key}: {len(objects)} parts, {tri0}/{tri1} triangles", flush=True)

(ROOT / "Assets/ArtProduction/model-manifest.json").write_text(json.dumps({"models": records}, indent=2), encoding="utf-8")
print("PRODUCTION_CONVERSION_COMPLETE", flush=True)
