#!/usr/bin/env python3
"""Generate src/icons_data.rs from the upstream open-source icon packages.

Reads tools/icons-mapping.json, extracts the needed icons from the
downloaded package copies under ~/workspace/rust-gui-contoh/icon-packs-src/
(lucide-static 1.82.0, @tabler/icons 3.53.0, @phosphor-icons/core 2.1.1,
heroicons 2.2.0, feather-icons 4.29.2, @material-symbols/svg-400 0.49.0),
normalizes every icon to absolute SVG path commands on a 24x24 viewBox,
and emits a Rust lookup table.

Normalization: all shapes (path/polyline/polygon/line/circle/rect) become
absolute M/L/C/Q/A/Z commands; relative commands are converted; smooth
curves (S/T) are expanded by reflecting the previous control point;
coordinates are scaled/translated per pack (Phosphor 256 -> 24,
Material Symbols 960 -> 24 with y-offset) and rounded to 2 decimals.
Lucide icons listed in "keep_from_design_slint" are taken VERBATIM from
the existing ternary in ui/design.slint so the default look never changes.

Usage: python3 tools/gen_icons.py  (run from apps/bmachine-rs/)
Output: src/icons_data.rs  (committed; regenerate after any mapping change)
"""
import json
import math
import os
import re
import sys
from xml.etree import ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC_DIR = os.path.expanduser("~/workspace/rust-gui-contoh/icon-packs-src")

NUM_RE = re.compile(r"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")


def fmt(n: float) -> str:
    n = round(n, 2)
    if n == 0:
        n = 0.0  # avoid "-0"
    s = f"{n:.2f}".rstrip("0").rstrip(".")
    return s if s not in ("", "-0") else "0"


class Transformer:
    """Affine transform: x' = x*scale, y' = y*scale + ty*scale... (ty given in output units)."""

    def __init__(self, scale: float, ty: float):
        self.s = scale
        self.ty = ty

    def pt(self, x: float, y: float):
        return (x * self.s, y * self.s + self.ty)

    def num(self, v: float) -> float:
        return v * self.s


def normalize_path(d: str, tr: Transformer) -> str:
    """Convert path data to absolute M/L/C/Q/A/Z commands, transformed.

    Cursor-based parser: arc flags are single 0/1 characters and may be
    glued to the following number (e.g. Lucide "A2 2 0 0022 17" = flags
    0,0 then x=22), so flags are read as exactly one character each.
    """
    out = []
    n = len(d)
    i = [0]  # cursor (list so nested fns can mutate)

    def skip_sep():
        while i[0] < n and d[i[0]] in " ,\t\r\n":
            i[0] += 1

    def read_num():
        skip_sep()
        m = NUM_RE.match(d, i[0])
        if not m:
            raise ValueError(f"expected number at {d[max(0, i[0]-10):i[0]+10]!r} in {d[:60]!r}")
        i[0] = m.end()
        return float(m.group(0))

    def read_flag():
        skip_sep()
        if i[0] >= n or d[i[0]] not in "01":
            raise ValueError(f"expected arc flag at {d[max(0, i[0]-10):i[0]+10]!r}")
        v = int(d[i[0]])
        i[0] += 1
        return v

    def read_cmd():
        skip_sep()
        if i[0] < n and d[i[0]].isalpha():
            c = d[i[0]]
            i[0] += 1
            return c
        return None

    # Current point + subpath start + previous control points are kept in
    # ORIGINAL coordinates; the transform is applied only when emitting,
    # so relative commands add offsets in the source space.
    cx = cy = 0.0
    sx = sy = 0.0
    prev_cx = prev_cy = None
    prev_qx = prev_qy = None
    prev_cmd = ""
    cmd = None

    def emit_pt(kind, x, y):
        ex, ey = tr.pt(x, y)
        out.append((kind, ex, ey))

    while True:
        c = read_cmd()
        if c is not None:
            cmd = c
            if cmd in "Zz":
                out.append(("Z",))
                cx, cy = sx, sy
                prev_cx = prev_cy = prev_qx = prev_qy = None
                prev_cmd = "Z"
                continue
        if cmd is None:
            break
        # Implicit repetition exhausted (no more numbers) -> done.
        j = i[0]
        while j < n and d[j] in " ,\t\r\n":
            j += 1
        if j >= n:
            break
        up = cmd.upper()
        rel = cmd.islower()
        if up == "M":
            x, y = read_num(), read_num()
            if rel:
                x, y = cx + x, cy + y
            cx, cy = sx, sy = x, y
            emit_pt("M", x, y)
            prev_cx = prev_cy = prev_qx = prev_qy = None
            prev_cmd = "M"
            cmd = "l" if rel else "L"
            continue
        if up == "L":
            x, y = read_num(), read_num()
            if rel:
                x, y = cx + x, cy + y
            cx, cy = x, y
            emit_pt("L", x, y)
            prev_cx = prev_cy = prev_qx = prev_qy = None
            prev_cmd = "L"
            continue
        if up == "H":
            x = read_num()
            if rel:
                x = cx + x
            cx, cy = x, cy
            emit_pt("L", x, y)
            prev_cx = prev_cy = prev_qx = prev_qy = None
            prev_cmd = "L"
            continue
        if up == "V":
            y = read_num()
            if rel:
                y = cy + y
            cx, cy = cx, y
            emit_pt("L", x, y)
            prev_cx = prev_cy = prev_qx = prev_qy = None
            prev_cmd = "L"
            continue
        if up == "C":
            x1, y1 = read_num(), read_num()
            x2, y2 = read_num(), read_num()
            x, y = read_num(), read_num()
            if rel:
                x1, y1 = cx + x1, cy + y1
                x2, y2 = cx + x2, cy + y2
                x, y = cx + x, cy + y
            cx, cy = x, y
            x1, y1 = tr.pt(x1, y1)
            x2, y2 = tr.pt(x2, y2)
            ex, ey = tr.pt(x, y)
            out.append(("C", x1, y1, x2, y2, ex, ey))
            prev_cx, prev_cy = x2, y2
            prev_qx = prev_qy = None
            prev_cmd = "C"
            continue
        if up == "S":
            x2, y2 = read_num(), read_num()
            x, y = read_num(), read_num()
            if rel:
                x2, y2 = cx + x2, cy + y2
                x, y = cx + x, cy + y
            if prev_cmd in ("C", "S") and prev_cx is not None:
                x1, y1 = 2 * cx - prev_cx, 2 * cy - prev_cy
            else:
                x1, y1 = cx, cy
            cx, cy = x, y
            x1, y1 = tr.pt(x1, y1)
            x2, y2 = tr.pt(x2, y2)
            ex, ey = tr.pt(x, y)
            out.append(("C", x1, y1, x2, y2, ex, ey))
            prev_cx, prev_cy = x2, y2
            prev_qx = prev_qy = None
            prev_cmd = "S"
            continue
        if up == "Q":
            x1, y1 = read_num(), read_num()
            x, y = read_num(), read_num()
            if rel:
                x1, y1 = cx + x1, cy + y1
                x, y = cx + x, cy + y
            cx, cy = x, y
            x1, y1 = tr.pt(x1, y1)
            ex, ey = tr.pt(x, y)
            out.append(("Q", x1, y1, ex, ey))
            prev_qx, prev_qy = x1, y1
            prev_cx = prev_cy = None
            prev_cmd = "Q"
            continue
        if up == "T":
            x, y = read_num(), read_num()
            if rel:
                x, y = cx + x, cy + y
            if prev_cmd in ("Q", "T") and prev_qx is not None:
                x1, y1 = 2 * cx - prev_qx, 2 * cy - prev_qy
            else:
                x1, y1 = cx, cy
            cx, cy = x, y
            x1, y1 = tr.pt(x1, y1)
            ex, ey = tr.pt(x, y)
            out.append(("Q", x1, y1, ex, ey))
            prev_qx, prev_qy = x1, y1
            prev_cx = prev_cy = None
            prev_cmd = "T"
            continue
        if up == "A":
            rx, ry = read_num(), read_num()
            rot = read_num()
            laf, sf = read_flag(), read_flag()
            x, y = read_num(), read_num()
            if rel:
                x, y = cx + x, cy + y
            cx, cy = x, y
            rx, ry = tr.num(rx), tr.num(ry)
            ex, ey = tr.pt(x, y)
            out.append(("A", rx, ry, rot, laf, sf, ex, ey))
            prev_cx = prev_cy = prev_qx = prev_qy = None
            prev_cmd = "A"
            continue
        raise ValueError(f"unsupported command {cmd!r} in {d[:60]!r}")

    parts = []
    for seg in out:
        if seg[0] == "Z":
            parts.append("Z")
        elif seg[0] == "A":
            _, rx, ry, rot, laf, sf, x, y = seg
            parts.append(f"A {fmt(rx)} {fmt(ry)} {fmt(rot)} {laf} {sf} {fmt(x)} {fmt(y)}")
        else:
            parts.append(seg[0] + " " + " ".join(fmt(v) for v in seg[1:]))
    return " ".join(parts)


def shape_to_path(el, tr: Transformer) -> str:
    tag = el.tag.split("}")[-1]
    if tag == "path":
        return normalize_path(el.get("d", ""), tr)
    if tag in ("polyline", "polygon"):
        pts = [float(v) for v in re.split(r"[,\s]+", el.get("points", "").strip()) if v != ""]
        pairs = [(pts[k], pts[k + 1]) for k in range(0, len(pts) - 1, 2)]
        if not pairs:
            return ""
        segs = []
        for j, (x, y) in enumerate(pairs):
            x, y = tr.pt(x, y)
            segs.append(("M" if j == 0 else "L", x, y))
        if tag == "polygon":
            segs.append(("Z",))
        parts = []
        for s in segs:
            if s[0] == "Z":
                parts.append("Z")
            else:
                parts.append(s[0] + " " + " ".join(fmt(v) for v in s[1:]))
        return " ".join(parts)
    if tag == "line":
        x1, y1 = tr.pt(float(el.get("x1", 0)), float(el.get("y1", 0)))
        x2, y2 = tr.pt(float(el.get("x2", 0)), float(el.get("y2", 0)))
        return f"M {fmt(x1)} {fmt(y1)} L {fmt(x2)} {fmt(y2)}"
    if tag == "circle":
        cx, cy = tr.pt(float(el.get("cx", 0)), float(el.get("cy", 0)))
        r = tr.num(float(el.get("r", 0)))
        return (
            f"M {fmt(cx - r)} {fmt(cy)} "
            f"A {fmt(r)} {fmt(r)} 0 1 0 {fmt(cx + r)} {fmt(cy)} "
            f"A {fmt(r)} {fmt(r)} 0 1 0 {fmt(cx - r)} {fmt(cy)} Z"
        )
    if tag == "ellipse":
        cx, cy = tr.pt(float(el.get("cx", 0)), float(el.get("cy", 0)))
        rx, ry = tr.num(float(el.get("rx", 0))), tr.num(float(el.get("ry", 0)))
        return (
            f"M {fmt(cx - rx)} {fmt(cy)} "
            f"A {fmt(rx)} {fmt(ry)} 0 1 0 {fmt(cx + rx)} {fmt(cy)} "
            f"A {fmt(rx)} {fmt(ry)} 0 1 0 {fmt(cx - rx)} {fmt(cy)} Z"
        )
    if tag == "rect":
        x, y = tr.pt(float(el.get("x", 0)), float(el.get("y", 0)))
        w, h = tr.num(float(el.get("width", 0))), tr.num(float(el.get("height", 0)))
        rx = tr.num(float(el.get("rx", el.get("ry", 0) or 0)))
        ry = tr.num(float(el.get("ry", el.get("rx", 0) or 0)))
        if rx == 0 and ry == 0:
            return (
                f"M {fmt(x)} {fmt(y)} L {fmt(x + w)} {fmt(y)} "
                f"L {fmt(x + w)} {fmt(y + h)} L {fmt(x)} {fmt(y + h)} Z"
            )
        rx, ry = min(rx, w / 2), min(ry, h / 2)
        return (
            f"M {fmt(x + rx)} {fmt(y)} L {fmt(x + w - rx)} {fmt(y)} "
            f"A {fmt(rx)} {fmt(ry)} 0 0 1 {fmt(x + w)} {fmt(y + ry)} "
            f"L {fmt(x + w)} {fmt(y + h - ry)} "
            f"A {fmt(rx)} {fmt(ry)} 0 0 1 {fmt(x + w - rx)} {fmt(y + h)} "
            f"L {fmt(x + rx)} {fmt(y + h)} "
            f"A {fmt(rx)} {fmt(ry)} 0 0 1 {fmt(x)} {fmt(y + h - ry)} "
            f"L {fmt(x)} {fmt(y + ry)} "
            f"A {fmt(rx)} {fmt(ry)} 0 0 1 {fmt(x + rx)} {fmt(y)} Z"
        )
    return ""


def extract_svg(path: str, tr: Transformer) -> str:
    tree = ET.parse(path)
    root = tree.getroot()
    parts = []
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag in ("path", "polyline", "polygon", "line", "circle", "ellipse", "rect"):
            d = el.get("d", "") if tag == "path" else ""
            if tag == "path" and d.strip() in ("M0 0h24v24H0z", "M0 0h24v24H0V0z"):
                continue  # transparent bounding-box path, skip
            p = shape_to_path(el, tr)
            if p:
                parts.append(p)
    return " ".join(parts)


def parse_design_slint_lucide(path: str) -> dict:
    """Existing 30 Lucide paths, verbatim from the SvgIcon ternary."""
    text = open(path, encoding="utf-8").read()
    pairs = re.findall(r'root\.name == "([^"]+)" \? "([^"]*)"', text)
    out = {}
    for name, d in pairs:
        out[name] = d
    return out


def rust_escape(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def main():
    mapping = json.load(open(os.path.join(HERE, "icons-mapping.json"), encoding="utf-8"))
    packs = mapping["packs"]
    icon_map = mapping["map"]
    design_lucide = parse_design_slint_lucide(os.path.join(ROOT, "ui", "design.slint"))
    print(f"lucide keep-from-design.slint: {len(design_lucide)} icons")

    data = {}  # pack -> {name: d}
    fallbacks = []  # (pack, name)
    for pack, cfg in packs.items():
        data[pack] = {}
        tr = Transformer(cfg["scale"], cfg["ty"])
        keep = set(cfg.get("keep_from_design_slint", []))
        for name, per_pack in sorted(icon_map.items()):
            if pack == "lucide" and name in keep:
                data[pack][name] = design_lucide[name]
                continue
            src = per_pack.get(pack)
            if src is None:
                fallbacks.append((pack, name))
                continue  # resolved to Lucide at runtime
            fpath = os.path.join(SRC_DIR, cfg["dir"], src + cfg["ext"])
            if not os.path.exists(fpath):
                print(f"ERROR: missing file for {pack}/{name}: {fpath}")
                sys.exit(1)
            d = extract_svg(fpath, tr)
            if not d:
                print(f"ERROR: empty path data for {pack}/{name} ({fpath})")
                sys.exit(1)
            data[pack][name] = d

    # Emit Rust file
    lines = []
    lines.append("// GENERATED by tools/gen_icons.py — do not edit by hand.")
    lines.append("// Regenerate: python3 tools/gen_icons.py  (run from apps/bmachine-rs/)")
    lines.append("//")
    lines.append("// Sources (all open-source, path data normalized to absolute")
    lines.append("// SVG commands on a 24x24 viewBox; Phosphor scaled 256->24,")
    lines.append("// Material Symbols scaled 960->24 with y-offset):")
    lines.append("// - Lucide 1.82.0 (ISC): 30 paths kept verbatim from ui/design.slint,")
    lines.append("//   15 new from lucide-static.")
    lines.append("// - Tabler 3.53.0 (MIT), Phosphor 2.1.1 (MIT, fill-based),")
    lines.append("//   Heroicons 2.2.0 (MIT), Feather 4.29.2 (MIT),")
    lines.append("//   Material Symbols 0.49.0 (Apache-2.0, fill-based, outlined).")
    lines.append("// Icons a pack lacks fall back to Lucide at runtime (see icons.rs):")
    for pack, name in fallbacks:
        lines.append(f"// - {pack}: {name}")
    lines.append("")
    lines.append("/// Path data lookup: (pack, icon name) -> SVG path commands, or None.")
    lines.append("/// Returns None when the pack has no equivalent (caller falls back to Lucide).")
    lines.append("pub(crate) fn lookup(pack: &str, name: &str) -> Option<&'static str> {")
    lines.append("    match pack {")
    for pack in ["lucide", "tabler", "phosphor", "heroicons", "feather", "material"]:
        lines.append(f'        "{pack}" => match name {{')
        for name in sorted(data[pack]):
            lines.append(f'            "{name}" => Some("{rust_escape(data[pack][name])}"),')
        lines.append("            _ => None,")
        lines.append("        },")
    lines.append("        _ => None,")
    lines.append("    }")
    lines.append("}")
    out_path = os.path.join(ROOT, "src", "icons_data.rs")
    open(out_path, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    for pack in data:
        print(f"{pack}: {len(data[pack])} icons")
    print(f"fallbacks (pack -> Lucide): {len(fallbacks)}: " + ", ".join(f"{p}/{n}" for p, n in fallbacks))
    print(f"wrote {out_path}")


if __name__ == "__main__":
    main()
