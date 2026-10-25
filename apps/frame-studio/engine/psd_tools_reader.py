#!/usr/bin/env python3
"""Local-only psd-tools bridge for sanitized metadata, stored RGBA, and safe aperture maps."""

from __future__ import annotations

import base64
import json
import logging
import os
import sys
import warnings
from pathlib import Path

MAX_APERTURE_MASK_BYTES = 4 * 1024 * 1024
IMAGE_MARKERS = {"photo", "foto", "portrait", "slot", "logo", "crest", "emblem", "badge", "lambang", "lencana"}


def _silence_library_output() -> None:
    logging.disable(logging.CRITICAL)
    warnings.filterwarnings("ignore")
    # Never forward parser diagnostics that could include source names or descriptors.
    sys.stderr = open(os.devnull, "w", encoding="utf-8")


def _apply_process_memory_limit(limit_bytes: int) -> bool:
    try:
        import resource

        resource.setrlimit(resource.RLIMIT_AS, (limit_bytes, limit_bytes))
        return True
    except (ImportError, AttributeError, OSError, ValueError):
        return False


def _has_image_marker(name: str) -> bool:
    normalized = name.lower()
    for separator in ("-", "_", "[", "]", "(", ")", ":", "/", "\\", "."):
        normalized = normalized.replace(separator, " ")
    return bool(set(normalized.split()) & IMAGE_MARKERS)


def _simple_user_mask(layer):
    """Return a layer-sized binary user mask only for a narrowly supported layer profile."""
    if layer.is_group() or layer.kind not in ("pixel", "smartobject"):
        return None
    if not layer.visible or layer.opacity != 255 or layer.clipping:
        return None
    if layer.blend_mode.name != "NORMAL" or layer.vector_mask is not None or bool(layer.effects):
        return None

    mask = layer.mask
    if mask is None or mask.disabled or mask.has_real():
        return None
    mask_data = getattr(mask, "_data", None)
    if mask_data is None:
        return None
    flags = getattr(mask_data, "flags", None)
    if bool(getattr(flags, "invert", False)) or bool(getattr(flags, "disabled", False)):
        return None
    if getattr(mask_data, "user_mask_density", None) not in (None, 255):
        return None
    if getattr(mask_data, "user_mask_feather", None) not in (None, 0, 0.0):
        return None

    width, height = int(layer.width), int(layer.height)
    if width <= 0 or height <= 0 or width * height > MAX_APERTURE_MASK_BYTES:
        return None
    image = mask.topil(real=False, layer_sized=True)
    if image is None or image.size != (width, height):
        return None

    import numpy as np

    samples = np.asarray(image.convert("L"), dtype=np.uint8)
    if samples.shape != (height, width) or np.any((samples != 0) & (samples != 255)):
        return None
    return samples


def _aperture_maps(document, state: dict) -> dict[str, str]:
    """Return binary visible apertures for simple root-level image slots only."""
    import numpy as np

    layers = list(document)
    maps: dict[str, str] = {}
    for index, target in enumerate(layers):
        if not _has_image_marker(str(target.name or "")):
            continue
        target_mask = _simple_user_mask(target)
        if target_mask is None:
            continue

        width, height = target_mask.shape[1], target_mask.shape[0]
        left, top = int(target.left), int(target.top)
        covered = np.zeros((height, width), dtype=np.bool_)
        unsupported = False
        for foreground in layers[index + 1 :]:
            if not foreground.visible:
                continue
            if (
                foreground.is_group()
                or foreground.clipping
                or foreground.blend_mode.name != "NORMAL"
                or foreground.opacity != 255
                or foreground.vector_mask is not None
                or bool(foreground.effects)
            ):
                unsupported = True
                break

            foreground_left, foreground_top = int(foreground.left), int(foreground.top)
            x0 = max(left, foreground_left)
            y0 = max(top, foreground_top)
            x1 = min(left + width, foreground_left + int(foreground.width))
            y1 = min(top + height, foreground_top + int(foreground.height))
            if x1 <= x0 or y1 <= y0:
                continue

            rendered = foreground.composite(force=True, apply_icc=False)
            if rendered is None or rendered.size != (int(foreground.width), int(foreground.height)):
                unsupported = True
                break
            alpha = np.asarray(rendered.convert("RGBA"), dtype=np.uint8)[
                y0 - foreground_top : y1 - foreground_top,
                x0 - foreground_left : x1 - foreground_left,
                3,
            ]
            target_region = target_mask[y0 - top : y1 - top, x0 - left : x1 - left]
            if np.any(((alpha > 0) & (alpha < 255)) & (target_region != 0)):
                unsupported = True
                break
            covered[y0 - top : y1 - top, x0 - left : x1 - left] |= alpha > 0

        if unsupported:
            continue

        aperture = np.where((target_mask == 255) & ~covered, 255, 0).astype(np.uint8)
        if not np.any(aperture):
            continue
        if state["aperture_mask_bytes"] + aperture.nbytes > MAX_APERTURE_MASK_BYTES:
            break
        state["aperture_mask_bytes"] += aperture.nbytes
        maps[f"layer/{index}"] = base64.b64encode(aperture.tobytes()).decode("ascii")
    return maps


def _layer_tree(nodes, prefix: str, depth: int, state: dict):
    if depth > state["max_depth"]:
        raise ValueError("layer tree depth limit exceeded")

    result = []
    for index, layer in enumerate(nodes):
        state["count"] += 1
        if state["count"] > state["max_layers"]:
            raise ValueError("layer count limit exceeded")

        layer_id = f"{prefix}/{index}"
        name = str(layer.name or "")
        if len(name) > state["max_name_length"]:
            name = name[: state["max_name_length"]]
            state["names_truncated"] = True

        is_group = bool(layer.is_group())
        children = _layer_tree(layer, layer_id, depth + 1, state) if is_group else []
        result.append(
            {
                "id": layer_id,
                "name": name,
                "kind": str(layer.kind),
                "isGroup": is_group,
                "visible": bool(layer.visible),
                "opacity": max(0, min(255, int(round(layer.opacity)))),
                "x": int(layer.left),
                "y": int(layer.top),
                "width": max(0, int(layer.width)),
                "height": max(0, int(layer.height)),
                "hasPixelData": bool(layer.has_pixels()),
                "hasMask": layer.mask is not None,
                "hasVectorMask": layer.vector_mask is not None,
                "hasClipping": bool(layer.clipping),
                "hasEffects": bool(layer.effects),
                "children": children,
            }
        )
    return result


def _write_manifest(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")


def main() -> int:
    if len(sys.argv) != 8:
        return 64

    source_path = Path(sys.argv[1])
    manifest_path = Path(sys.argv[2])
    rgba_path = Path(sys.argv[3])
    max_alloc_bytes = int(sys.argv[4])
    max_preview_bytes = int(sys.argv[5])
    process_memory_bytes = int(sys.argv[6])
    max_input_bytes = int(sys.argv[7])

    _silence_library_output()
    process_memory_limit_applied = _apply_process_memory_limit(process_memory_bytes)

    try:
        if source_path.stat().st_size > max_input_bytes:
            raise ValueError("input file size limit exceeded")

        import psd_tools
        from psd_tools import PSDImage, ParseLimits

        if psd_tools.__version__ != "1.26.0":
            _write_manifest(
                manifest_path,
                {"success": False, "errorType": "UnsupportedVersion"},
            )
            return 2

        document = PSDImage.open(
            source_path,
            max_alloc_bytes=max_alloc_bytes,
            parse_limits=ParseLimits(
                max_read_bytes=max_input_bytes,
                max_total_bytes=max_input_bytes * 4,
                max_objects=100_000,
                max_nesting_depth=32,
            ),
        )

        state = {
            "count": 0,
            "max_layers": 20_000,
            "max_depth": 32,
            "max_name_length": 512,
            "names_truncated": False,
            "aperture_mask_bytes": 0,
        }
        layers = _layer_tree(document, "layer", 1, state)
        aperture_maps = _aperture_maps(document, state)
        for layer in layers:
            aperture = aperture_maps.get(layer["id"])
            if aperture is not None:
                layer["apertureMaskBase64"] = aperture

        warnings_out = [
            "Layer tree and stored merged composite are best-effort reads; no PSD layer, text, mask, effect, or layered-file editing is supported. Exact binary visible-aperture maps are emitted only for simple user masks and supported foreground layers."
        ]
        if state["names_truncated"]:
            warnings_out.append("One or more layer names were truncated to the configured safety limit.")

        preview_bytes = 0
        expected_preview_bytes = int(document.width) * int(document.height) * 4
        mode = document.color_mode.name
        if (
            mode == "RGB"
            and int(document.depth) == 8
            and int(document.channels) == 3
            and 0 < expected_preview_bytes <= max_preview_bytes
        ):
            image = document.topil(apply_icc=True)
            if image is not None:
                image = image.convert("RGBA")
                if image.size != (int(document.width), int(document.height)):
                    raise ValueError("stored composite dimensions do not match the document")
                pixels = image.tobytes()
                if len(pixels) != expected_preview_bytes:
                    raise ValueError("stored composite byte length does not match the document")
                rgba_path.write_bytes(pixels)
                preview_bytes = len(pixels)
        else:
            warnings_out.append(
                "No preview was emitted: only RGB 8-bit documents within the configured RGBA memory limit are supported."
            )

        _write_manifest(
            manifest_path,
            {
                "success": True,
                "psdToolsVersion": psd_tools.__version__,
                "pythonVersion": ".".join(str(part) for part in sys.version_info[:3]),
                "width": int(document.width),
                "height": int(document.height),
                "channelCount": int(document.channels),
                "bitDepth": int(document.depth),
                "colorMode": mode,
                "previewBytes": preview_bytes,
                "processMemoryLimitApplied": process_memory_limit_applied,
                "layers": layers,
                "warnings": warnings_out,
            },
        )
        return 0
    except BaseException as exc:
        # Keep failure output deliberately generic: paths, layer names, and text stay local.
        try:
            _write_manifest(
                manifest_path,
                {"success": False, "errorType": type(exc).__name__},
            )
        except BaseException:
            pass
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
