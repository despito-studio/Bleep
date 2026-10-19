"""Refresh scalar glTF properties without re-exporting unchanged model geometry."""
import json
import pathlib

root = pathlib.Path(__file__).resolve().parents[2]
source_manifest = json.loads((root / "Art/ProductionSources/PolyHaven/manifest.json").read_text())
model_path = root / "Assets/ArtProduction/model-manifest.json"
models = json.loads(model_path.read_text())
sources = {asset["key"]: asset for asset in source_manifest["assets"] if asset["kind"] == "model"}
for model in models["models"]:
    doc = json.loads((root / sources[model["key"]]["source"]).read_text())
    for record, material in zip(model["materials"], doc["materials"]):
        pbr = material.get("pbrMetallicRoughness", {})
        record["metallicFactor"] = pbr.get("metallicFactor", 1)
        record["roughnessFactor"] = pbr.get("roughnessFactor", 1)
        record["baseColorFactor"] = pbr.get("baseColorFactor", [1, 1, 1, 1])
        record["transmission"] = material.get("extensions", {}).get("KHR_materials_transmission", {}).get("transmissionFactor", 0)
model_path.write_text(json.dumps(models, indent=2), encoding="utf-8")
print("Original glTF scalar factors preserved for all material slots.")
