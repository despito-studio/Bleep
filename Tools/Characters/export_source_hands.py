"""Regenerate only source-derived FPS hands, without body/texture writes.

Blender --background --factory-startup --python this_file -- [--roles ...]
Importing this module has no scene, filesystem or export side effects.
"""

from pathlib import Path
import argparse
import hashlib
import json
import runpy
import sys


ROOT = Path(__file__).resolve().parents[2]
_DEFINITIONS_END = "\nselected=CAST\n"


def _preparation_definitions():
    path = ROOT / "Tools" / "Characters" / "prepare_user_cast.py"
    source = path.read_text(encoding="utf-8")
    definitions, separator, _ = source.partition(_DEFINITIONS_END)
    if not separator:
        raise RuntimeError("Cannot isolate the preparation definitions safely.")
    scope = {"__file__": str(path), "__name__": "hand_preparation_definitions"}
    exec(compile(definitions, str(path), "exec"), scope)
    return scope


def _use_atlas_without_writing(body, role, texture_folder):
    """Replace image references while preserving the source shader factors."""
    import bpy

    source_images = []
    material_factors = []
    for material in body.data.materials:
        if material is None:
            raise RuntimeError("Unassigned source material for " + role)
        base_factors = []
        if material.use_nodes:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image and node.image not in source_images:
                    source_images.append(node.image)
                if node.type == "BSDF_PRINCIPLED":
                    base_factors.append(list(node.inputs["Base Color"].default_value))
        material_factors.append({
            "source_material": material.name,
            "diffuse_color": list(material.diffuse_color),
            "principled_base_color_factors": base_factors,
        })
    if len(source_images) != 1:
        raise RuntimeError("Expected one source albedo atlas for " + role)

    atlas_path = texture_folder / (role + "_albedo.png")
    if atlas_path.is_file():
        atlas = bpy.data.images.load(str(atlas_path), check_existing=True)
        atlas_source = "existing_runtime_atlas"
    else:
        # Work only on an in-memory copy. The exporter never saves this image
        # or overwrites an atlas; the report makes the absent disk atlas clear.
        atlas = source_images[0].copy()
        atlas.scale(1024, 1024)
        atlas.filepath_raw = str(atlas_path)
        atlas_source = "source_atlas_scaled_in_memory"
    atlas.name = role + "_albedo"
    for material in body.data.materials:
        if material.use_nodes:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image in source_images:
                    node.image = atlas
        material.name = role + "_source_atlas"
    return {
        "method": atlas_source,
        "path": str(atlas_path),
        "exists_on_disk": atlas_path.is_file(),
        "size": list(atlas.size),
        "material_factors_preserved": material_factors,
    }


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--roles", nargs="+",
                        choices=("operator", "journalist", "engineer", "dispatcher"))
    parser.add_argument("--role",
                        choices=("operator", "journalist", "engineer", "dispatcher"))
    options = parser.parse_args(argv)
    if options.roles and options.role:
        parser.error("Use either --role or --roles.")
    scope = _preparation_definitions()
    selected_roles = set(options.roles or ([options.role] if options.role else []))
    cast = [item for item in scope["CAST"]
            if not selected_roles or item["role"] in selected_roles]
    if not cast:
        raise RuntimeError("No matching source roles were selected.")
    helper = runpy.run_path(str(ROOT / "Tools" / "Characters" / "source_hand_segments.py"),
                           run_name="hand_export_helpers")
    report_path = ROOT / "Verification-Characters" / "hand-final-report.json"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report = {"complete": False, "characters": [],
              "writes": "Hand FBXs and this verification report only; source/body/atlas files preserved"}

    def save_report():
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    try:
        for info in cast:
            role = info["role"]
            report["active_role"] = role
            body, source_stats = scope["import_selected"](info)
            spec = scope["fitted_spec"](body, info)
            atlas = _use_atlas_without_writing(body, role, scope["TEXTURES"])
            hands = helper["export_hand_parts"](
                body, spec, role, scope["HANDS"] / ("hand_" + role + ".fbx"),
            )
            source_path = Path(source_stats["source"])
            unchanged = hashlib.sha256(source_path.read_bytes()).hexdigest() == source_stats["sha256"]
            if not unchanged:
                raise RuntimeError("The original character source changed: " + str(source_path))
            result = {"role": role, "source": str(source_path),
                      "source_unchanged": unchanged, "T_pose_markers": spec,
                      "atlas": atlas, "hands": hands}
            report["characters"].append(result)
            save_report()
            print("SOURCE_HANDS_EXPORTED " + json.dumps({
                "role": role, "part_count": hands["part_count"],
                "source_fragments": hands["source_fragments"],
                "generated_parts": hands["generated_parts"],
                "output_path": hands["output_path"],
            }), flush=True)
        report.pop("active_role", None)
        report["complete"] = True
        save_report()
        print("SOURCE_HAND_EXPORT_COMPLETE " + str(report_path), flush=True)
        return report
    except Exception as error:
        report["error"] = str(error)
        save_report()
        raise


if __name__ == "__main__":
    main()
