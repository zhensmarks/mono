#!/usr/bin/env python3
"""Generator dataset Explorer BMachine.

Satu sumber kebenaran untuk:
  - ui/explorer-data.slint  (komponen ExplorerData: tabel item per folder,
    permutasi sort, grup, split — Slint tidak bisa sort/filter array,
    jadi semua urutan dihitung di sini)
  - tools/explorer-data-rust.txt (tabel global untuk search/type-ahead Rust)

Aturan urutan (EXPLORER_SPEC.md §2.5):
  - folder dulu, lalu shortcut (.lnk), lalu file — di semua sort;
  - natural sort (angka: "2" sebelum "10"); Descending membalik tiap
    kelompok terpisah.
  - Group Date: Today -> Yesterday -> Earlier this week -> Last week ->
    Earlier this month -> Last month -> A long time ago.
  - Group Type: Folders -> Shortcut -> "XXX Files" per ekstensi (alfabetis).
  - Split: kiri = grup ekstensi terbanyak (folder = grup "Folder"),
    kanan = sisanya; bila 1 grup -> belah dua (ceil kiri).

Tanggal contoh relatif ke "hari ini" 26 Nov 2026 (bucket Date tetap).
"""
import json
import re

# (name, kind, ext, size, date-display, days-ago, bucket, preview)
FOLDERS = {
    # Dataset contoh DIKOSONGKAN (27 Nov 2026, keputusan abeng: default
    # publish harus bersih — tidak ada data pribadi). Kunci folder tetap
    # ada (root/a/b/pf/pfb/gen/master) supaya komponen ExplorerData
    # tetap punya properti tiap folder; isi + path diisi user sendiri
    # saat folder nyata sudah bisa dibaca (Fase 6 / master_creator).
    "root": {"path": "", "items": []},
    "a": {"path": "", "items": []},
    "b": {"path": "", "items": []},
    "pf": {"path": "", "items": []},
    "pfb": {"path": "", "items": []},
    "gen": {"path": "", "items": []},
    "master": {"path": "", "items": []},
}

DATE_BUCKETS = ["Today", "Yesterday", "Earlier this week", "Last week",
                "Earlier this month", "Last month", "A long time ago"]


def natkey(s):
    return [int(t) if t.isdigit() else t.lower()
            for t in re.split(r"(\d+)", s)]


def kind_rank(kind):
    return {"folder": 0, "link": 1, "file": 2}[kind]


def orders(items):
    """Kembalikan dict: name-asc/desc, date-asc/desc, type-asc/desc
    sebagai daftar indeks (ke items, urutan kanonis = name-asc)."""
    n = len(items)
    # kanonis = name-asc
    canon = sorted(range(n), key=lambda i: (kind_rank(items[i][1]), natkey(items[i][0])))
    by_kind = {}
    for i in canon:
        by_kind.setdefault(kind_rank(items[i][1]), []).append(i)

    def rev_within(order):
        out = []
        for k in sorted(by_kind):
            grp = [i for i in order if kind_rank(items[i][1]) == k]
            out.extend(reversed(grp))
        return out

    name_asc = canon
    name_desc = rev_within(name_asc)

    date_asc = []
    for k in sorted(by_kind):
        grp = by_kind[k]
        date_asc.extend(sorted(grp, key=lambda i: (-items[i][5], natkey(items[i][0]))))
    date_desc = rev_within(date_asc)

    type_asc = []
    for k in sorted(by_kind):
        grp = by_kind[k]
        type_asc.extend(sorted(grp, key=lambda i: (items[i][2].lower(), natkey(items[i][0]))))
    type_desc = rev_within(type_asc)

    return {"name-asc": name_asc, "name-desc": name_desc,
            "date-asc": date_asc, "date-desc": date_desc,
            "type-asc": type_asc, "type-desc": type_desc}


def group_orders(items):
    """Group Date & Type: (order, headers) — header label di atas item pertama
    tiap bucket, "" bila tidak ada header."""
    # Date: bucket kanonis, di dalam bucket (kind_rank, natural name)
    order, headers = [], []
    for b in DATE_BUCKETS:
        grp = [i for i, it in enumerate(items) if it[6] == b]  # it[6] = bucket
        grp = sorted(grp, key=lambda i: (kind_rank(items[i][1]), natkey(items[i][0])))
        for j, i in enumerate(grp):
            order.append(i)
            headers.append(b if j == 0 else "")
    date = (order, headers)
    # Type: Folders -> Shortcut -> "XXX Files" alfabetis
    order, headers = [], []
    folders = [i for i, it in enumerate(items) if it[1] == "folder"]
    folders = sorted(folders, key=lambda i: natkey(items[i][0]))
    links = [i for i, it in enumerate(items) if it[1] == "link"]
    links = sorted(links, key=lambda i: natkey(items[i][0]))
    exts = sorted({items[i][2].upper()[1:] for i, it in enumerate(items)
                   if it[1] == "file"})
    groups = []
    if folders:
        groups.append(("Folders", folders))
    if links:
        groups.append(("Shortcut", links))
    for e in exts:
        grp = sorted([i for i, it in enumerate(items)
                      if it[1] == "file" and it[2].upper() == "." + e],
                     key=lambda i: natkey(items[i][0]))
        groups.append((e + " Files", grp))
    for label, grp in groups:
        for j, i in enumerate(grp):
            order.append(i)
            headers.append(label if j == 0 else "")
    return {"date": date, "type": (order, headers)}


def split_orders(items):
    """Split: kiri = grup ekstensi terbanyak (folder = 'Folder'),
    kanan = sisanya; bila 1 grup -> belah dua (ceil kiri)."""
    canon = sorted(range(len(items)),
                   key=lambda i: (kind_rank(items[i][1]), natkey(items[i][0])))
    groups = {}
    for i in canon:
        g = "Folder" if items[i][1] == "folder" else items[i][2].lower()
        groups.setdefault(g, []).append(i)
    if len(groups) >= 2:
        # grup terbanyak; seri -> yang item pertamanya paling dulu di canon
        best = max(groups.items(), key=lambda kv: (len(kv[1]), -canon.index(kv[1][0])))
        left = best[1]
        right = [i for i in canon if i not in set(left)]
        return left, right
    half = (len(canon) + 1) // 2
    return canon[:half], canon[half:]


def sl_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


def sl_str_arr(vals):
    return "[" + ", ".join(sl_str(v) for v in vals) + "]"


def sl_int_arr(vals):
    return "[" + ", ".join(str(v) for v in vals) + "]"


def gen_slint():
    out = []
    out.append("// DATA EXPLORER — digenerate tools/gen_explorer_data.py.")
    out.append("// JANGAN edit manual: ubah data di generator lalu jalankan ulang.")
    out.append("// Slint tidak bisa sort/filter array di .slint, jadi semua urutan")
    out.append("// (sort Name/Date/Type x Asc/Desc, grup Date/Type, split) dihitung")
    out.append("// di sini sebagai permutasi indeks ke tabel kanonis (name-asc).")
    out.append("export component ExplorerData inherits Rectangle {")
    for key, f in FOLDERS.items():
        items = f["items"]
        names = [it[0] for it in items]
        kinds = [it[1] for it in items]
        exts = [it[2] for it in items]
        # base = nama tanpa ekstensi (untuk penamaan "x - Copy" saat paste);
        # folder = nama utuh.
        bases = [it[0][:-len(it[2])] if it[2] and it[0].endswith(it[2]) else it[0]
                 for it in items]
        sizes = [it[3] for it in items]
        dates = [it[4] for it in items]
        previews = [it[7] for it in items]
        o = orders(items)
        g = group_orders(items)
        sl, sr = split_orders(items)
        p = key + "-"
        out.append(f"    out property <[string]> {p}names: {sl_str_arr(names)};")
        out.append(f"    out property <[string]> {p}kinds: {sl_str_arr(kinds)};")
        out.append(f"    out property <[string]> {p}exts: {sl_str_arr(exts)};")
        out.append(f"    out property <[string]> {p}bases: {sl_str_arr(bases)};")
        out.append(f"    out property <[string]> {p}sizes: {sl_str_arr(sizes)};")
        out.append(f"    out property <[string]> {p}dates: {sl_str_arr(dates)};")
        out.append(f"    out property <[string]> {p}previews: {sl_str_arr(previews)};")
        for sk, perm in o.items():
            out.append(f"    out property <[int]> {p}sort-{sk}: {sl_int_arr(perm)};")
        for gk, (gorder, ghdr) in g.items():
            out.append(f"    out property <[int]> {p}grp-{gk}: {sl_int_arr(gorder)};")
            out.append(f"    out property <[string]> {p}grp-{gk}-hdr: {sl_str_arr(ghdr)};")
        out.append(f"    out property <[int]> {p}split-l: {sl_int_arr(sl)};")
        out.append(f"    out property <[int]> {p}split-r: {sl_int_arr(sr)};")
    out.append("}")
    return "\n".join(out) + "\n"


def gen_rust():
    out = []
    out.append("// TABEL GLOBAL EXPLORER — digenerate tools/gen_explorer_data.py.")
    out.append("// (folder-key, path, name, kind, ext, base, size, date, preview, loc)")
    out.append("// Dipakai search (rekursif, substring case-insensitive) & type-ahead.")
    out.append("static EXPLORER_ITEMS: &[(&str, &str, &str, &str, &str, &str, &str, &str, &str, &str)] = &[")
    # urutan kanonis per folder (name-asc) — harus sama dengan tabel Slint
    for key, f in FOLDERS.items():
        items = f["items"]
        canon = sorted(range(len(items)),
                       key=lambda i: (kind_rank(items[i][1]), natkey(items[i][0])))
        for i in canon:
            it = items[i]
            base = it[0][:-len(it[2])] if it[2] and it[0].endswith(it[2]) else it[0]
            loc = f["path"].split("\\")[-1] if f["path"] else ""
            out.append("    (" + ", ".join([
                sl_str(key), sl_str(f["path"]), sl_str(it[0]), sl_str(it[1]),
                sl_str(it[2]), sl_str(base), sl_str(it[3]), sl_str(it[4]),
                sl_str(it[7]), sl_str(loc)]) + "),")
    out.append("];")
    out.append("")
    out.append("// Subtree tiap folder (search rekursif = folder ini + turunan).")
    out.append("static EXPLORER_SUBTREE: &[(&str, &[&str])] = &[")
    subtree = {
        "root": ["root", "a", "b", "pf", "pfb", "gen"],
        "a": ["a", "pf", "gen"],
        "b": ["b", "pfb", "gen"],
        "pf": ["pf", "gen"],
        "pfb": ["pfb", "gen"],
        "gen": ["gen"],
        "master": ["master"],
    }
    for k, v in subtree.items():
        out.append(f'    ("{k}", &[{", ".join(sl_str(x) for x in v)}]),')
    out.append("];")
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    proj = os.path.dirname(here)
    with open(os.path.join(proj, "ui", "explorer-data.slint"), "w") as fh:
        fh.write(gen_slint())
    with open(os.path.join(here, "explorer-data-rust.txt"), "w") as fh:
        fh.write(gen_rust())
    print("generated ui/explorer-data.slint + tools/explorer-data-rust.txt")
