"""Cache licensed production sources and the exact files used by the Unity importer."""
from __future__ import annotations

import concurrent.futures
import hashlib
import json
import pathlib
import shutil
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / "Art/ProductionSources/PolyHaven"
DEST = ROOT / "Assets/ArtProduction"
HEADERS = {"User-Agent": "DeadAir-ArtPipeline/1.0 (offline production asset preparation)"}
SURFACES = {
    "plaster": "plastered_wall_04",
    "painted-wall": "peeling_painted_wall",
    "wood": "wood_table_worn",
    "parquet": "herringbone_parquet",
    "steel": "metal_plate_02",
    "painted-metal": "green_metal_rust",
    "concrete": "concrete_floor_worn_02",
    "asphalt": "asphalt_02",
    "fabric": "denim_fabric",
    "brick": "brown_brick_02",
    "foliage": "leafy_grass",
    "soil": "forest_ground_04",
    "leather": "fabric_leather_01",
    "wood-panel": "wood_cabinet_worn_long",
    "snow-asphalt": "asphalt_snow",
    "snow": "snow_02",
    "bark": "japanese_cedar_bark",
    "roof-tile": "grey_roof_tiles_02",
    "floor-tile": "terrazzo_tiles",
}
MODELS = {
    "school-desk": "SchoolDesk_01",
    "school-chair": "SchoolChair_01",
    "radio-transceiver": "vintage_radio_transceiver",
    "cassette-player": "portable_cassette_player",
    "television": "Television_01",
    "video-camera": "vintage_video_camera",
    "desk-lamp": "desk_lamp_arm_01",
    "kettle": "vintage_electric_kettle",
    "wall-clock": "wall_clock",
    "military-crate": "old_military_crate",
    "wooden-crate": "wooden_crate_01",
    "steel-shelves": "steel_frame_shelves_01",
    "bookshelf": "wooden_bookshelf_worn",
    "wooden-bench": "painted_wooden_bench",
    "wooden-cabinet": "painted_wooden_cabinet",
    "sofa": "sofa_02",
    "armchair": "mid_century_lounge_chair",
    "coffee-table": "CoffeeTable_01",
    "potted-plant": "potted_plant_01",
    "trash-can": "metal_trash_can",
    "storage-cart": "industrial_storage_cart",
    "generator": "portable_generator",
    "searchlight": "portable_searchlight",
    "security-camera": "security_camera_01",
    "fire-alarm": "fire_alarm",
    "wall-lamp": "industrial_wall_lamp",
    "cardboard-box": "cardboard_box_01",
    "microscope": "vintage_microscope",
    "barrel": "Barrel_01",
    "wooden-chair": "WoodenChair_01",
    "pine-tree": "pine_sapling_small",
    "fir-tree": "fir_sapling_medium",
    "shrub": "shrub_02",
}


def fetch(url, path, expected=None):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and (not expected or hashlib.md5(path.read_bytes()).hexdigest() == expected):
        return path
    for attempt in range(5):
        try:
            request = urllib.request.Request(url, headers=HEADERS)
            with urllib.request.urlopen(request, timeout=120) as response:
                data = response.read()
            if expected and hashlib.md5(data).hexdigest() != expected:
                raise ValueError("Source MD5 mismatch: " + url)
            temporary = path.with_suffix(path.suffix + ".download")
            temporary.write_bytes(data)
            temporary.replace(path)
            return path
        except Exception:
            if attempt == 4:
                raise
            time.sleep(2 ** attempt)


def metadata(asset):
    path = fetch("https://api.polyhaven.com/files/" + asset, CACHE / (asset + "-files.json"))
    return json.loads(path.read_text(encoding="utf-8"))


def map_file(meta, key):
    variants = meta.get(key, {})
    for resolution in ("2k", "1k", "4k"):
        formats = variants.get(resolution, {})
        for extension in ("png", "jpg"):
            if extension in formats:
                return formats[extension]
    return None


def prepare():
    jobs, entries = [], []
    for key, asset in SURFACES.items():
        meta = metadata(asset)
        entry = {"key": key, "asset": asset, "kind": "surface", "maps": {}}
        for channel, api_key in (("albedo", "Diffuse"), ("normal", "nor_gl"), ("mask", "arm")):
            source = map_file(meta, api_key)
            if source is None:
                raise ValueError(f"Missing {channel}: {asset}")
            path = DEST / "Textures" / key / pathlib.Path(source["url"]).name
            entry["maps"][channel] = path.relative_to(ROOT).as_posix()
            jobs.append((source, path))
        entries.append(entry)
    for key, asset in MODELS.items():
        meta = metadata(asset)
        formats = meta.get("gltf", {})
        variant = formats.get("2k", formats.get("1k", {}))
        source = variant.get("gltf")
        if source is None:
            raise ValueError("No glTF source: " + asset)
        folder = CACHE / "Models" / key
        path = folder / pathlib.Path(source["url"]).name
        entry = {"key": key, "asset": asset, "kind": "model", "source": path.relative_to(ROOT).as_posix(), "files": []}
        jobs.append((source, path))
        for relative, included in source.get("include", {}).items():
            target = folder / relative
            jobs.append((included, target))
            entry["files"].append(target.relative_to(ROOT).as_posix())
        entries.append(entry)
    return jobs, entries


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    previous_path = CACHE / "manifest.json"
    previous = json.loads(previous_path.read_text(encoding="utf-8")) if previous_path.exists() else {}
    prior_files = {item.get("originalDownloadPath", item["path"]): item for item in previous.get("files", [])}
    jobs, entries = prepare()
    print(f"Downloading {len(jobs)} verified files for {len(entries)} production assets", flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        futures = {}
        for source, original_path in jobs:
            prior = prior_files.get(original_path.relative_to(ROOT).as_posix(), {})
            path = ROOT / prior["path"] if prior.get("originalCacheExcludedFromGit") else original_path
            futures[pool.submit(fetch, source["url"], path, source.get("md5"))] = (source, path, original_path, prior)
        files = []
        for count, future in enumerate(concurrent.futures.as_completed(futures), 1):
            source, path, original_path, prior = futures[future]
            future.result()
            record = dict(prior)
            record.update({"path": path.relative_to(ROOT).as_posix(), "url": source["url"], "md5": source.get("md5"), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "bytes": path.stat().st_size})
            if path != original_path and not original_path.exists():
                original_path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, original_path)
            files.append(record)
            print(f"{count}/{len(jobs)} {path.name}", flush=True)
    manifest = {"provider": "Poly Haven", "license": "CC0-1.0", "licenseUrl": "https://polyhaven.com/license", "apiCredit": "Powered by Poly Haven", "assets": entries, "files": sorted(files, key=lambda row: row["path"])}
    if "texturePreparation" in previous:
        manifest["texturePreparation"] = previous["texturePreparation"]
    (CACHE / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    (DEST / "surface-manifest.json").write_text(json.dumps({"assets": [entry for entry in entries if entry["kind"] == "surface"]}, indent=2), encoding="utf-8")
    print("Production manifest saved", flush=True)


if __name__ == "__main__":
    main()
