"""Prepare smaller Unity textures while retaining byte-exact provider originals."""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import shutil
from datetime import datetime, timezone

import numpy as np
from PIL import Image


ROOT = pathlib.Path(__file__).resolve().parents[2]
PROVIDER = pathlib.Path("Art/ProductionSources/PolyHaven/manifest.json")
REPORT = pathlib.Path("Art/ProductionSources/texture-optimization.json")
RAW = pathlib.Path("Art/ProductionSources/PolyHaven/OriginalTextures")
GENERATED_RAW = pathlib.Path("Art/ProductionSources/GeneratedGraphics/OriginalTextures")
SURFACE_LARGE = {
    "plaster", "painted-wall", "parquet", "concrete", "asphalt", "brick",
    "wood-panel", "snow-asphalt", "snow", "roof-tile", "floor-tile",
}
SMALL_MODELS = {"wall-clock", "kettle", "fire-alarm", "desk-lamp", "security-camera", "wall-lamp"}
LARGE_MODELS = {
    "sofa", "armchair", "bookshelf", "steel-shelves", "wooden-cabinet", "barrel",
    "military-crate", "wooden-crate", "generator", "pine-tree", "fir-tree", "shrub",
}
EXTENSIONS = {".png", ".jpg", ".jpeg"}
CHANNELS = ("albedo", "normal", "mask", "emission")


def read_json(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + ".preparing")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(target)


def file_info(path):
    data = (ROOT / path).read_bytes()
    return {"path": path.as_posix(), "bytes": len(data), "md5": hashlib.md5(data).hexdigest(),
            "sha256": hashlib.sha256(data).hexdigest()}


def channel_from_name(name):
    name = name.lower()
    if "_nor_" in name or "_normal" in name:
        return "normal"
    if "_diff" in name or "_color" in name or "_albedo" in name:
        return "albedo"
    if "_emiss" in name:
        return "emission"
    return "mask"


def limit_for(kind, key, channel):
    if kind == "surface":
        return 1024 if key in SURFACE_LARGE and channel == "albedo" else 512
    if kind == "generated":
        return 1024 if key == "broadcast-network" else 512
    if key in SMALL_MODELS:
        return 256
    if key in LARGE_MODELS:
        return 512
    return 512 if channel == "albedo" else 256


def build_jobs(provider):
    jobs = {}

    def add(path, kind, key, channel, alpha_mode="OPAQUE", cutoff=.5):
        path = pathlib.Path(path)
        if not (ROOT / path).exists():
            raise FileNotFoundError(path)
        job = jobs.setdefault(path.as_posix(), {
            "runtimePath": path.as_posix(), "kind": kind, "key": key, "channel": channel,
            "maximumDimension": limit_for(kind, key, channel), "alphaMode": alpha_mode,
            "alphaCutoff": float(cutoff), "providerCopies": [],
        })
        if alpha_mode == "MASK":
            job["alphaMode"] = alpha_mode
            job["alphaCutoff"] = float(cutoff)
        return job

    surfaces = read_json(pathlib.Path("Assets/ArtProduction/surface-manifest.json"))
    for asset in surfaces["assets"]:
        for channel, path in asset["maps"].items():
            add(path, "surface", asset["key"], channel, "MASK" if asset["key"] == "foliage" else "OPAQUE")
    models = read_json(pathlib.Path("Assets/ArtProduction/model-manifest.json"))
    for model in models["models"]:
        for material in model["materials"]:
            for channel in CHANNELS:
                if material.get(channel):
                    add(material[channel], "model", model["key"], channel,
                        material.get("alphaMode", "OPAQUE"), material.get("alphaCutoff", .5))
    # Retained source variants also need smaller physical files inside Assets.
    for folder, kind in (("Assets/ArtProduction/Textures", "surface"), ("Assets/ArtProduction/SourceModels", "model")):
        for path in sorted((ROOT / folder).rglob("*")):
            if path.suffix.lower() in EXTENSIONS:
                relative = path.relative_to(ROOT)
                add(relative, kind, path.parent.name, channel_from_name(path.name))
    graphics = read_json(pathlib.Path("Art/ProductionSources/generated-graphics.json"))
    add(graphics["asset"], "generated", "broadcast-network", "albedo")
    add(graphics["windowGrime"]["asset"], "generated", "window-grime", "mask")
    for record in provider["files"]:
        original = pathlib.Path(record.get("originalDownloadPath", record["path"]))
        if original.suffix.lower() not in EXTENSIONS:
            continue
        if original.as_posix().startswith("Assets/"):
            runtime = original
        else:
            # glTF retains its relative image references to this smaller copy.
            relative = original.relative_to("Art/ProductionSources/PolyHaven/Models")
            runtime = pathlib.Path("Assets/ArtProduction/SourceModels") / relative.parts[0] / original.name
            if not (ROOT / runtime).exists():
                runtime = original
        if runtime.as_posix() not in jobs:
            key = original.parts[4] if not original.as_posix().startswith("Assets/") else original.parent.name
            add(runtime, "model" if not original.as_posix().startswith("Assets/") else "surface",
                key, channel_from_name(original.name))
        job = jobs[runtime.as_posix()]
        job["providerRecord"] = record
        job["originalDownloadPath"] = original.as_posix()
        if original != runtime:
            job["providerCopies"].append(original.as_posix())
    return sorted(jobs.values(), key=lambda job: job["runtimePath"]), graphics


def raw_path(job):
    path = pathlib.Path(job["runtimePath"])
    if job["kind"] == "generated":
        return GENERATED_RAW / path.name
    if job["kind"] == "surface":
        return RAW / "Surfaces" / job["key"] / path.name
    return RAW / "Models" / job["key"] / "textures" / path.name


def resize_float(channel, size):
    return np.asarray(Image.fromarray(np.asarray(channel, dtype=np.float32), mode="F")
                      .resize(size, Image.Resampling.LANCZOS), dtype=np.float32)


def preserve_coverage(alpha, original, cutoff):
    target = float(np.count_nonzero(original >= cutoff)) / original.size
    if target <= 0 or target >= 1:
        return alpha
    low, high = 0., 8.
    for _ in range(24):
        middle = (low + high) * .5
        coverage = float(np.count_nonzero(alpha * middle >= cutoff)) / alpha.size
        if coverage < target:
            low = middle
        else:
            high = middle
    candidates = (np.clip(alpha * low, 0, 1), np.clip(alpha * high, 0, 1))
    return min(candidates, key=lambda value: abs(float(np.count_nonzero(value >= cutoff)) / value.size - target))


def downsample(source, job):
    image = Image.open(ROOT / source)
    image.load()
    original_format = image.format
    width, height = image.size
    maximum = job["maximumDimension"]
    scale = min(1., maximum / max(width, height))
    size = (max(1, round(width * scale)), max(1, round(height * scale)))
    has_alpha = "A" in image.getbands() or "transparency" in image.info
    rgba = np.asarray(image.convert("RGBA"), dtype=np.float32) / np.float32(255.)
    alpha = rgba[:, :, 3]
    resized_alpha = np.clip(resize_float(alpha, size), 0, 1)
    if has_alpha and job["alphaMode"] == "MASK":
        resized_alpha = preserve_coverage(resized_alpha, alpha, job["alphaCutoff"])
    colors = []
    if job["channel"] == "normal":
        for index in range(3):
            colors.append(resize_float(rgba[:, :, index] * 2 - 1, size))
        vectors = np.stack(colors, axis=-1)
        length = np.linalg.norm(vectors, axis=-1, keepdims=True)
        vectors = np.divide(vectors, np.maximum(length, np.float32(1e-8)))
        vectors[length[:, :, 0] <= 1e-8] = (0, 0, 1)
        rgb = vectors * .5 + .5
        method = "linear tangent-vector Lanczos downsample; renormalized XYZ"
    elif job["channel"] in ("albedo", "emission"):
        for index in range(3):
            encoded = rgba[:, :, index]
            linear = np.where(encoded <= .04045, encoded / 12.92, ((encoded + .055) / 1.055) ** 2.4)
            if has_alpha:
                filtered = resize_float(linear * alpha, size)
                denominator = np.maximum(np.clip(resize_float(alpha, size), 0, 1), np.float32(1e-6))
                filtered = filtered / denominator
            else:
                filtered = resize_float(linear, size)
            filtered = np.clip(filtered, 0, 1)
            colors.append(np.where(filtered <= .0031308, filtered * 12.92, 1.055 * filtered ** (1 / 2.4) - .055))
        rgb = np.stack(colors, axis=-1)
        method = "sRGB decode; linear-light Lanczos downsample; sRGB encode"
        if has_alpha:
            method += "; premultiplied alpha filtering"
    else:
        rgb = np.stack([resize_float(rgba[:, :, index], size) for index in range(3)], axis=-1)
        method = "linear data-channel Lanczos downsample"
    encoded = np.uint8(np.rint(np.clip(rgb, 0, 1) * 255))
    if has_alpha:
        encoded = np.concatenate((encoded, np.uint8(np.rint(resized_alpha * 255))[:, :, None]), axis=-1)
    result = Image.fromarray(encoded, "RGBA" if has_alpha else "RGB")
    alpha_stats = {"present": has_alpha, "mode": job["alphaMode"]}
    if has_alpha:
        cutoff = job["alphaCutoff"]
        alpha_stats.update({"cutoff": cutoff, "sourceCoverage": float(np.count_nonzero(alpha >= cutoff)) / alpha.size,
                            "derivedCoverage": float(np.count_nonzero(resized_alpha >= cutoff)) / resized_alpha.size,
                            "coveragePreserved": job["alphaMode"] == "MASK"})
    image.close()
    return result, original_format, [width, height], list(size), method, alpha_stats


def save_image(image, target, source_format, channel):
    path = ROOT / target
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".preparing")
    if source_format == "JPEG" and image.mode != "RGBA":
        image.save(temporary, format="JPEG", quality=90 if channel in ("albedo", "emission") else 95,
                   subsampling=0, optimize=True)
    else:
        image.save(temporary, format="PNG", optimize=True, compress_level=9)
    temporary.replace(path)


def prepare():
    provider = read_json(PROVIDER)
    jobs, graphics = build_jobs(provider)
    records = []
    print(f"Preparing {len(jobs)} physical textures; raw originals remain outside Assets", flush=True)
    for index, job in enumerate(jobs, 1):
        target = pathlib.Path(job["runtimePath"])
        raw = raw_path(job)
        cached = ROOT / raw
        cached.parent.mkdir(parents=True, exist_ok=True)
        provider_record = job.get("providerRecord")
        if not cached.exists():
            current_source = pathlib.Path(provider_record["path"]) if provider_record else target
            if not (ROOT / current_source).exists():
                current_source = target
            if provider_record:
                incoming = file_info(current_source)
                if incoming["sha256"] != provider_record["sha256"] or incoming["md5"] != provider_record["md5"]:
                    raise FileNotFoundError("Restore the unchanged provider original to the raw cache before preparing: "
                                            + str(raw) + " from " + provider_record["url"])
            shutil.copy2(ROOT / current_source, cached)
        original = file_info(raw)
        if provider_record:
            if original["sha256"] != provider_record["sha256"] or original["md5"] != provider_record["md5"]:
                raise ValueError("Provider original differs from its download attestation: " + str(raw))
        before = file_info(target)
        with Image.open(ROOT / target) as previous:
            before["dimensions"] = list(previous.size)
        image, source_format, old_size, new_size, method, alpha = downsample(raw, job)
        save_image(image, target, source_format, job["channel"])
        image.close()
        derived = file_info(target)
        derived.update({"dimensions": new_size, "channel": job["channel"], "maximumDimension": job["maximumDimension"],
                        "method": method, "alpha": alpha})
        copies = set(job["providerCopies"])
        # Older retained scan variants may no longer occur in the provider index.
        if job["kind"] == "model":
            candidate = pathlib.Path("Art/ProductionSources/PolyHaven/Models") / job["key"] / "textures" / target.name
            if (ROOT / candidate).exists() and candidate != target:
                copies.add(candidate.as_posix())
        for path in sorted(copies):
            shutil.copy2(ROOT / target, ROOT / path)
        record = {"kind": job["kind"], "key": job["key"], "channel": job["channel"],
                  "original": dict(original, dimensions=old_size), "before": before, "derived": derived,
                  "additionalDerivedCopies": sorted(copies), "sourceProvider": "image_gen" if job["kind"] == "generated" else "Poly Haven",
                  "sourceUrl": provider_record.get("url") if provider_record else None}
        records.append(record)
        if provider_record:
            provider_record["originalDownloadPath"] = job["originalDownloadPath"]
            provider_record["path"] = raw.as_posix()
            provider_record["runtimePath"] = target.as_posix()
            provider_record["derived"] = derived
            provider_record["additionalDerivedCopies"] = sorted(copies)
            provider_record["originalCacheExcludedFromGit"] = True
        print(f"{index}/{len(jobs)} {target.as_posix()} {old_size[0]}x{old_size[1]} -> "
              f"{new_size[0]}x{new_size[1]} {before['bytes']} -> {derived['bytes']} bytes", flush=True)
    provider["texturePreparation"] = {
        "manifest": REPORT.as_posix(), "originalTextureCache": RAW.as_posix(),
        "originalTextureCacheExcludedFromGit": True,
        "note": "files[].md5/sha256/bytes attest unchanged provider downloads in the local raw cache. "
                "runtimePath and derived describe resized files shipped in the repository; source URLs allow original restoration.",
    }
    generated = {record["key"]: record for record in records if record["kind"] == "generated"}
    graphics["texturePreparation"] = generated["broadcast-network"]
    graphics["windowGrime"]["texturePreparation"] = generated["window-grime"]
    summary = {
        "schemaVersion": 1, "preparedUtc": datetime.now(timezone.utc).isoformat(),
        "tool": "Tools/ArtProduction/optimize_textures.py", "purpose": "physical asset preparation; not a Unity runtime validation",
        "sourcePolicy": "Exact originals cached outside Assets and excluded from Git; runtime paths and Unity .meta GUIDs retained.",
        "textureCount": len(records), "beforeBytes": sum(record["before"]["bytes"] for record in records),
        "afterBytes": sum(record["derived"]["bytes"] for record in records),
        "originalBytes": sum(record["original"]["bytes"] for record in records),
        "policy": {"largeSurfaceAlbedo": 1024, "largeSurfaceNormalAndMask": 512, "otherSurfaceMaps": 512,
                   "defaultModelAlbedo": 512, "defaultModelData": 256,
                   "smallFixtureMaps": 256, "smallFixtureKeys": sorted(SMALL_MODELS),
                   "largeFurnitureAndFoliageMaps": 512, "largeModelKeys": sorted(LARGE_MODELS),
                   "networkGraphic": 1024, "windowGrime": 512},
        "runtimeValidation": "Not rerun after texture optimization: the user explicitly requested no tests. Earlier GPU/native results describe the previous texture payload.",
        "textures": records,
    }
    write_json(PROVIDER, provider)
    write_json(pathlib.Path("Art/ProductionSources/generated-graphics.json"), graphics)
    write_json(REPORT, summary)
    print(json.dumps({key: summary[key] for key in ("textureCount", "beforeBytes", "afterBytes", "originalBytes")}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepare", action="store_true", required=True, help="Write smaller production textures and provenance records")
    parser.parse_args()
    prepare()
