#!/usr/bin/env python3
"""Local-only read bridge for psd-tools; writes JSON metadata and raw RGBA to caller-owned temp paths."""

from __future__ import annotations

import json
import logging
import os
import sys
import warnings
from pathlib import Path


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
        }
        layers = _layer_tree(document, "layer", 1, state)
        warnings_out = [
            "Layer tree and stored merged composite are best-effort reads; no layer, text, mask, effect, or PSD editing is supported."
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
