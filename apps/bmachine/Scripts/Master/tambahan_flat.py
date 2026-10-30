# -*- coding: utf-8 -*-
"""
tambahan_flat.py — builder generik untuk "MASTER TAMBAHAN" berbasis KODE.

Semua folder pekerjaan yang punya .txt berisi kode (FRS-004, HGN-003, KMP-001,
KPP-005, KSP-002, KML-005, BML-002, ATN-007, MWS-01, ...) akan dibuatkan PSD
dengan master yang diambil dari folder master tambahan yang cocok.

Aturan kode + pencarian master ada di master_codes.py (satu sumber).

Pemakaian:
    tambahan_flat.py <pilihan_path> <output_base> <registry.json> [--mode=copy-edit]
                     [--phase=1] [--manifest=<path>]

registry.json dibuat oleh buat_master.py:
    {"FRS": [{"name": "FREE SEKOLAH", "path": "D:\\...\\FREE SEKOLAH"}], ...}
"""

import os
import re
import sys
import json
import shutil

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import master_codes as mc

MASTER_SUBDIR = "#MASTER"
COPY_EDIT = False
MANIFEST_PATH = None

# Master tambahan (FRS, HGN, KPP, KMP, KSP, KML, BML, ATN, MWS, ...) hampir
# selalu dipakai APA ADANYA: yang dibutuhkan cuma 1.psd / 2.psd per grup,
# tanpa diedit dulu. Jadi walaupun app jalan di mode copy-edit, script ini
# TIDAK membuat folder #MASTER dan tidak mencatat manifest - langsung salin.
# Kalau suatu saat perlu diedit dulu, ubah flag ini jadi False.
ALWAYS_DIRECT = True

try:
    from _fastio import batch_copy as _fast_batch_copy
except Exception:
    def _fast_batch_copy(jobs, workers=4, overwrite=True, on_done=None, on_error=None):
        done = skipped = failed = 0
        for s, d in jobs:
            if os.path.isfile(d) and not overwrite:
                skipped += 1
                continue
            try:
                os.makedirs(os.path.dirname(os.path.abspath(d)), exist_ok=True)
                shutil.copy2(s, d)
                done += 1
                if on_done:
                    try: on_done(d)
                    except Exception: pass
            except Exception as e:
                failed += 1
                if on_error:
                    try: on_error(d, str(e))
                    except Exception: pass
        return done, skipped, failed


# ---------------------------------------------------------------------------
# helper path
# ---------------------------------------------------------------------------
def normalize_pilihan_path(pilihan_path):
    """-> (folder_pilihan, [nama_subfolder] | None). Terima path .txt juga."""
    p = (pilihan_path or '').strip().strip('"').strip("'")
    if not p:
        return p, None
    p = os.path.normpath(p)
    if os.path.isfile(p):
        p = os.path.dirname(p)
    if not os.path.isdir(p):
        return p, None
    try:
        entries = os.listdir(p)
    except Exception:
        return p, None
    subdirs = [n for n in entries if os.path.isdir(os.path.join(p, n))]
    has_files = any(f.lower().endswith(('.txt', '.jpg', '.jpeg')) for f in entries)
    if not subdirs and has_files:
        return os.path.dirname(p), [os.path.basename(p)]
    return p, None


def get_relative_path_from_month(pilihan_path):
    parts = os.path.normpath(pilihan_path).split(os.sep)
    parent = os.path.dirname(os.path.normpath(pilihan_path))
    parts = parent.split(os.sep)
    for i, part in enumerate(parts):
        if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
            return os.path.join(*parts[i:])
    return os.path.basename(parent)


def get_leading_number(name_no_ext):
    m = re.match(r'\s*(\d+)', name_no_ext)
    return m.group(1) if m else None


def normalize_non_numeric(name_no_ext):
    return re.sub(r'\(\d+\)$', '', name_no_ext.strip()).strip()


ONLY_PAREN = re.compile(r"^\s*\(\s*(\d{1,3})\s*\)\s*$")


def load_registry(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def find_master(registry, code):
    """Cari .psd untuk `code` di semua folder master prefix tsb."""
    pref = mc.code_prefix(code)
    for item in registry.get(pref, []):
        got = mc.find_master_in(item.get("path", ""), code)
        if got:
            return got, item
    return None, None


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def main(pilihan_path, output_base, registry, oke_base=None):
    pilihan_path, only_events = normalize_pilihan_path(pilihan_path)
    if not os.path.isdir(pilihan_path):
        print(f"[ERROR] Folder PILIHAN tidak ditemukan: {pilihan_path}", file=sys.stderr)
        return 0

    final_output_folder = os.path.join(output_base, get_relative_path_from_month(pilihan_path))
    os.makedirs(final_output_folder, exist_ok=True)

    event_names = only_events
    if event_names is None:
        try:
            event_names = [n for n in sorted(os.listdir(pilihan_path))
                           if os.path.isdir(os.path.join(pilihan_path, n))]
        except Exception as e:
            print(f"[WARNING] Gagal membaca PILIHAN: {e}", file=sys.stderr)
            event_names = []

    print("--- Memulai Proses Master Tambahan (berbasis kode) ---")
    print(f"Pilihan: {pilihan_path}")
    print(f"Output : {final_output_folder}")
    print(f"Prefix terdaftar: {', '.join(sorted(registry)) or '(kosong)'}")
    # Master tambahan dipakai apa adanya -> tidak ada #MASTER / manifest.
    use_copy_edit = COPY_EDIT and not ALWAYS_DIRECT
    if COPY_EDIT and ALWAYS_DIRECT:
        print("[MODE] Master tambahan disalin apa adanya (tanpa #MASTER / edit Photoshop).")

    total_jobs = 0
    total_dirs = 0
    ce_jobs = {}

    for subfolder_name in event_names:
        subfolder_path = os.path.join(pilihan_path, subfolder_name)
        if not os.path.isdir(subfolder_path):
            continue

        def codes_for_dir(dir_path):
            """Kode .txt terdekat (folder itu sendiri, lalu naik ke induk)."""
            cur = os.path.abspath(dir_path)
            root = os.path.abspath(subfolder_path)
            while True:
                codes = mc.find_candidate_codes(cur)
                usable = [c for c in codes
                          if mc.code_prefix(c) in registry
                          and mc.code_prefix(c) not in mc.HANDLED_BY]
                if usable:
                    return usable, cur
                if os.path.normcase(cur) == os.path.normcase(root):
                    return [], None
                parent = os.path.dirname(cur)
                if os.path.normcase(parent) == os.path.normcase(cur):
                    return [], None
                if not os.path.normcase(parent).startswith(os.path.normcase(root)):
                    return [], None
                cur = parent

        def out_dir_for(dir_path):
            rel = os.path.relpath(dir_path, subfolder_path)
            if rel in (".", ""):
                return os.path.join(final_output_folder, subfolder_name)
            return os.path.join(final_output_folder, subfolder_name, rel)

        touched = False
        for root, dirs, files in os.walk(subfolder_path):
            jpg_files = [f for f in files if f.lower().endswith(('.jpg', '.jpeg'))]
            if not jpg_files:
                continue

            codes, group_dir = codes_for_dir(root)
            if not codes:
                continue

            master_path, info = find_master(registry, codes[0])
            if not master_path:
                # coba kode berikutnya
                for c in codes[1:]:
                    master_path, info = find_master(registry, c)
                    if master_path:
                        codes = [c] + [x for x in codes if x != c]
                        break
            rel_dir = os.path.relpath(root, subfolder_path)
            if not master_path:
                print(f"  [SKIP] '{rel_dir}': kode {codes} belum ada master-nya "
                      f"(folder '{registry.get(mc.code_prefix(codes[0]), [{}])[0].get('name', '?')}' kosong).")
                continue

            chosen = codes[0]
            current_output_dir = out_dir_for(root)
            os.makedirs(current_output_dir, exist_ok=True)
            group_out_dir = out_dir_for(group_dir) if group_dir else current_output_dir
            print(f"  [GRUP] '{rel_dir}' -> kode {chosen} ({os.path.basename(master_path)}) "
                  f"dari {info.get('name')}")

            gids, seen = [], set()
            for fn in jpg_files:
                name_no_ext = os.path.splitext(fn)[0]
                gid = get_leading_number(name_no_ext)
                if gid is None:
                    m = ONLY_PAREN.match(name_no_ext)
                    gid = m.group(1) if m else normalize_non_numeric(name_no_ext)
                if gid and gid not in seen:
                    seen.add(gid)
                    gids.append(gid)

            # ------------------------------------------------------------------
            # DESAIN GABUNGAN = 1 FILE SAJA.
            # FREE SEKOLAH / HANGING / PLAKAT / BERSAMA (BML, BMP, BPL, BPP,
            # BSL, BSP) itu semua fotonya digabung ke SATU desain. Jadi jumlah
            # foto di PILIHAN tidak boleh jadi jumlah psd: cukup 1.psd.
            # ------------------------------------------------------------------
            single_file = mc.code_prefix(chosen) in getattr(mc, 'SINGLE_FILE_PREFIXES', set())
            if single_file:
                gids = ['1']
                existing_psd = [f for f in os.listdir(current_output_dir)
                                if f.lower().endswith('.psd')]
                if existing_psd:
                    print(f"    - '{rel_dir if rel_dir != '.' else '[Root]'}': desain gabungan, "
                          f"sudah ada psd ({existing_psd[0]}) -> tidak ditambah.")
                    total_dirs += 1
                    touched = True
                    continue

            print(f"    - '{rel_dir if rel_dir != '.' else '[Root]'}': "
                  f"{'1 file (desain gabungan)' if single_file else str(len(gids)) + ' grup'}.")
            total_dirs += 1
            touched = True

            if use_copy_edit:
                new_jobs = []
                for gid in gids:
                    dest = os.path.join(current_output_dir, f"{gid}.psd")
                    if os.path.exists(dest):
                        continue
                    new_jobs.append(dest)
                if new_jobs:
                    ce_jobs.setdefault((master_path, group_out_dir), []).extend(new_jobs)
                    print(f"      -> {len(new_jobs)} tujuan dicatat.")
            else:
                jobs = [(master_path, os.path.join(current_output_dir, f"{gid}.psd"))
                        for gid in gids
                        if not os.path.exists(os.path.join(current_output_dir, f"{gid}.psd"))]
                if jobs:
                    done, _sk, _fl = _fast_batch_copy(
                        jobs, workers=4, overwrite=True,
                        on_error=lambda d, e: print(f"      [ERROR] {os.path.basename(d)}: {e}", file=sys.stderr))
                    total_jobs += done
                    if done:
                        print(f"      -> {done} file disalin.")

        if touched:
            print("")

    if use_copy_edit and ce_jobs:
        from _photoshop_utils import manifest_add
        for (m_path, group_out_dir), jobs in ce_jobs.items():
            master_dir = os.path.join(group_out_dir, MASTER_SUBDIR)
            try:
                os.makedirs(master_dir, exist_ok=True)
            except Exception as e:
                print(f"    [ERROR] Gagal buat folder {MASTER_SUBDIR}: {e}", file=sys.stderr)
                continue
            edit_psd = os.path.join(master_dir, os.path.basename(m_path))
            if not os.path.isfile(edit_psd):
                shutil.copy2(m_path, edit_psd)
                print(f"    [EDIT] {MASTER_SUBDIR}\\{os.path.basename(edit_psd)} dibuka untuk diedit.")
            else:
                print(f"    [EDIT] {MASTER_SUBDIR}\\{os.path.basename(edit_psd)} sudah ada, dipakai ulang.")
            label = os.path.splitext(os.path.basename(m_path))[0]
            manifest_add(MANIFEST_PATH, edit_psd, label, jobs)
            print(f"    [MANIFEST] {label}: {len(jobs)} tujuan duplikat tercatat.")

    print(f"\n--- Master Tambahan Selesai ({total_dirs} folder, {total_jobs} psd disalin) ---")
    return total_dirs


if __name__ == "__main__":
    COPY_EDIT = False
    MANIFEST_PATH = None
    opts = {"mode": "direct", "phase": None, "manifest": None}
    for a in sys.argv:
        if a.startswith("--mode="): opts["mode"] = a.split("=", 1)[1].lower()
        elif a.startswith("--phase="): opts["phase"] = a.split("=", 1)[1].strip()
        elif a.startswith("--manifest="): opts["manifest"] = a.split("=", 1)[1]
    if any(a.startswith("--") for a in sys.argv):
        sys.argv = [a for a in sys.argv if not a.startswith("--")]

    if len(sys.argv) < 4:
        print("[ERROR] Usage: tambahan_flat.py <pilihan> <output_base> <registry.json> "
              "[--mode=copy-edit --phase=1 --manifest=...]", file=sys.stderr)
        sys.exit(1)

    if opts["mode"] == "copy-edit":
        import tempfile
        COPY_EDIT = True
        MANIFEST_PATH = opts["manifest"] or os.path.join(
            tempfile.gettempdir(), f"bm_copy_edit_tambahan_{os.getpid()}.json")
        print(f"[MODE] copy-edit fase 1 (manifest: {MANIFEST_PATH})")

    pil = sys.argv[1].strip().strip('"')
    out = sys.argv[2].strip().strip('"')
    reg_path = sys.argv[3].strip().strip('"')
    reg = load_registry(reg_path)
    # hanya prefix yang bukan tanggung jawab script khusus
    reg = {p: v for p, v in reg.items() if p not in mc.HANDLED_BY}
    main(pil, out, reg)
