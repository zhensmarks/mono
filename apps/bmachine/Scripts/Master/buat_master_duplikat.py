#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Tombol DUPLIKAT PSD — penggandaan MANUAL (tidak otomatis).

Alur pakai:
  1) Klik tombol master (BUAT MASTER / MANASIK / PAS FOTO / WISUDA / PROFESI).
     Fase 1 copy-edit menyalin 1 template edit ke folder output + mencatat
     daftar tujuan (1.psd, 2.psd, ...) ke manifest. Fase 1 TIDAK lagi
     menyalin .txt / membuat shortcut output lokal (dipindah ke fase 2 ini
     supaya "buka Photoshop" benar-benar tahap terakhir).
  2) Edit + SAVE PSD-nya di Photoshop sesuka hati — tanpa timer, tanpa dipaksa
     tutup.
  3) Klik tombol INI. Jalur fase 2:
       a) salin file .txt dari folder PILIHAN ke output
       b) buat shortcut output lokal
       c) gandakan tiap template edit ke semua tujuannya
     Log kelihatan per file (`- [OK] 2.psd`).

Tanpa Photoshop, tanpa watcher COM, tanpa menunggu tab ditutup.

Dipanggil app lewat: batch_wrapper.py --target buat_master_duplikat.py --pilihan <src> --output <base>
CLI manual: python buat_master_duplikat.py <pilihan_path> <output_base> [oke_base] [--manifest=<path>]
"""
import os
import sys
import re
import shutil


def _script_dir() -> str:
    return os.path.dirname(os.path.abspath(__file__))


def _resolve_output_base(output_base: str) -> str:
    """Output base yang dipakai buat_master: argumen dulu, lalu DB (OkeBase/LocalOutput).

    Kalau app mengirim --output kosong, fase 1 memakai path dari DB; tanpa fallback ini
    tombol tidak akan menemukan manifestnya (hash path beda).
    """
    if output_base and os.path.isdir(output_base):
        return output_base
    try:
        sys.path.insert(0, _script_dir())
        from buat_master import find_db_path, read_db_value

        db_path = find_db_path()
        for key in ("Configs.Master.OkeBase", "Configs.Master.LocalOutput"):
            val = read_db_value(db_path, key)
            if val:
                print(f"[INFO] Output base dari DB ({key}): {val}")
                return val
    except Exception as e:
        print(f"[WARN] Output base tidak bisa diambil dari DB: {e}", file=sys.stderr)
    return output_base


def _get_relative_path_from_month(pilihan_path: str) -> str:
    """Mirip pasfoto/manasik: struktur relatif mulai folder bulan -> parent PILIHAN."""
    pilihan_path_norm = os.path.normpath(pilihan_path)
    sumber_parent = os.path.dirname(pilihan_path_norm)
    parts = sumber_parent.split(os.sep)
    month_idx = -1
    for i, part in enumerate(parts):
        if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
            month_idx = i
            break
    if month_idx == -1:
        return os.path.basename(sumber_parent)
    return os.path.join(*parts[month_idx:])


def _copy_txt_files_recursive(source_folder: str, output_folder: str) -> int:
    """Salin semua .txt dari subfolder ke output. Idempoten (overwrite) — aman
    kalau tombol DUPLIKAT ditekan berulang."""
    count = 0
    for root, _dirs, files in os.walk(source_folder):
        for file in files:
            if file.lower().endswith('.txt'):
                src_path = os.path.join(root, file)
                rel_path = os.path.relpath(root, source_folder)
                dest_dir = os.path.join(output_folder, rel_path) if rel_path != '.' else output_folder
                os.makedirs(dest_dir, exist_ok=True)
                dest_path = os.path.join(dest_dir, file)
                try:
                    shutil.copy2(src_path, dest_path)
                    print(f"    - Salin .txt: {os.path.relpath(dest_path, output_folder) or file}")
                    count += 1
                except Exception as e:
                    print(f"    - [ERROR] Gagal salin .txt '{file}': {e}", file=sys.stderr)
    return count


MASTER_SUBDIR = "#MASTER"


def _load_entries(manifest_path: str) -> list:
    """Baca entry manifest (toleran: dict {"entries": [...]} maupun list lama)."""
    import json

    if not manifest_path or not os.path.isfile(manifest_path):
        return []
    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception:
        return []
    if isinstance(data, dict):
        return list(data.get("entries") or [])
    if isinstance(data, list):
        return data
    return []


def _save_entries(manifest_path: str, entries: list) -> bool:
    import json

    try:
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump({"entries": entries}, f, indent=2, ensure_ascii=False)
        return True
    except Exception as e:
        print(f"[ERROR] Gagal save manifest: {e}", file=sys.stderr)
        return False


def _norm_key(text: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", (text or "").upper())


def _scan_manual_masters(output_folder: str, registered: set) -> list:
    """Cari master PSD yang ditambahkan user sendiri (tidak ada di manifest).

    Dua lokasi yang didukung:
      1) <folder grup>/#MASTER/*.psd   (struktur baru — per grup/redaksi)
      2) <root event>/*.psd            (struktur lama, tetap didukung)

    Returns: list of (master_psd_path, label, target_folder)
             target_folder = folder tempat hasil gandaan harus keluar
                             (parent dari #MASTER, atau root event).
    """
    found = []
    if not os.path.isdir(output_folder):
        return found

    for root, _dirs, files in os.walk(output_folder):
        is_master_dir = os.path.basename(root).upper() == MASTER_SUBDIR.upper()
        # Root event hanya dipakai untuk kompatibilitas struktur lama
        is_legacy_root = os.path.normcase(os.path.abspath(root)) == os.path.normcase(os.path.abspath(output_folder))
        if not (is_master_dir or is_legacy_root):
            continue

        target_folder = os.path.dirname(root) if is_master_dir else output_folder
        for name in files:
            if not name.lower().endswith(".psd"):
                continue
            full_path = os.path.join(root, name)
            if os.path.normcase(os.path.abspath(full_path)) in registered:
                continue
            label = os.path.splitext(name)[0]
            found.append((full_path, label, target_folder))
            where = f"{MASTER_SUBDIR}\\" if is_master_dir else ""
            print(f"  [MANUAL] Ditemukan master tambahan: {where}{name}")
    return found


def _collect_known_job_names(entries: list) -> dict:
    """Peta folder -> set nama file job yang sudah dipakai manifest.

    Dipakai agar master manual memakai penomoran yang SAMA dengan grup itu
    (mis. 1.psd, 3.psd, 7.psd) tanpa perlu membaca ulang folder sumber.
    """
    mapping = {}
    for e in entries:
        for job in (e.get("jobs") or []):
            d = os.path.normcase(os.path.abspath(os.path.dirname(job)))
            mapping.setdefault(d, set()).add(os.path.basename(job))
    return mapping


def _photo_base_name(filename: str) -> str:
    """Nama dasar output dari nama file foto (sama dengan logika profesi_flat).

    '(15).JPG' -> '15' | 'pilot 2(1).jpg' -> '2' | 'tni 12 single.jpeg' -> '12SINGLE'
    """
    base_noext = os.path.splitext(filename)[0].strip()
    for pattern, single in (
        (r"^(.*?)(\d+)\s*\(\d+\)\s*(.*?)$", False),
        (r"^(.*?)(\d+)\s*single\s*(.*?)$", True),
        (r"^(.*?)(\d+)\s*(.*?)$", False),
    ):
        m = re.match(pattern, base_noext, flags=re.IGNORECASE)
        if m:
            _left, base, _right = m.groups()
            return f"{base}SINGLE" if single else base
    return os.path.splitext(filename)[0]


def _map_source_jobs(pilihan_path: str, output_folder: str, target_folder: str, label: str, ext: str) -> list:
    """Petakan foto di folder sumber yang cocok dengan label master -> daftar job.

    Contoh: master '#MASTER/DOKTER BEDAH.psd' di grup '.../KB'
    -> cari folder sumber '.../KB/DOKTER BEDAH' lalu ambil nomor fotonya saja
    (bukan seluruh nomor di grup), supaya tidak menimpa hasil profesi lain.
    """
    if not pilihan_path or not os.path.isdir(pilihan_path):
        return []

    try:
        rel_target = os.path.relpath(target_folder, output_folder)
    except Exception:
        rel_target = "."
    target_parts = [] if rel_target in (".", "") else [_norm_key(p) for p in rel_target.split(os.sep)]
    lab = _norm_key(label)

    exact, loose = None, None
    for root, _dirs, files in os.walk(pilihan_path):
        base = _norm_key(os.path.basename(root))
        if not base or not (base == lab or lab in base or base in lab):
            continue
        photos = [f for f in files if f.lower().endswith((".jpg", ".jpeg"))]
        if not photos:
            continue

        rel_src = os.path.relpath(root, pilihan_path)
        src_parts = [] if rel_src in (".", "") else [_norm_key(p) for p in rel_src.split(os.sep)]

        idx, ok = 0, True
        for tp in target_parts:
            if not tp:
                continue
            hit = False
            while idx < len(src_parts):
                if tp in src_parts[idx] or src_parts[idx] in tp:
                    hit = True
                    idx += 1
                    break
                idx += 1
            if not hit:
                ok = False
                break
        if not ok:
            continue

        if base == lab:
            exact = (root, photos)
            break
        if loose is None:
            loose = (root, photos)

    chosen = exact or loose
    if not chosen:
        return []

    _src_root, photos = chosen
    jobs, seen = [], set()
    for f in sorted(photos):
        name = _photo_base_name(f)
        if not name:
            continue
        job = os.path.join(target_folder, f"{name}{ext}")
        key = os.path.normcase(job)
        if key in seen:
            continue
        seen.add(key)
        jobs.append(job)
    return jobs


def _add_manual_masters_to_manifest(manifest_path: str, output_folder: str, pilihan_path: str) -> int:
    """Daftarkan master PSD buatan user ke manifest agar ikut digandakan.

    Aturan:
    - Master di <grup>/#MASTER/X.psd -> hasil gandaan keluar di <grup>/ (di luar #MASTER)
    - Job diambil dari foto di folder sumber yang namanya cocok dengan label master
      (jadi tidak menimpa hasil profesi lain di grup yang sama).
    """
    entries = _load_entries(manifest_path)
    if not entries:
        return 0

    registered = set()
    for e in entries:
        p = e.get("edit_psd") or ""
        if p:
            registered.add(os.path.normcase(os.path.abspath(p)))

    manual_masters = _scan_manual_masters(output_folder, registered)
    if not manual_masters:
        return 0

    print(f"\n[INFO] Ditemukan {len(manual_masters)} master PSD tambahan (manual).")

    known_jobs = _collect_known_job_names(entries)
    added = 0

    for master_path, label, target_folder in manual_masters:
        ext = os.path.splitext(master_path)[1] or ".psd"

        jobs = _map_source_jobs(pilihan_path, output_folder, target_folder, label, ext)
        if jobs:
            print(f"  [TAMBAH] {label}: {len(jobs)} tujuan (dari folder sumber '{label}')")
        else:
            key = os.path.normcase(os.path.abspath(target_folder))
            names = sorted(known_jobs.get(key) or [])
            if not names:
                print(f"  [SKIP] {label}: tidak ada foto/tujuan yang bisa dipetakan")
                continue
            jobs = [os.path.join(target_folder, n) for n in names]
            print(f"  [TAMBAH] {label}: {len(jobs)} tujuan (mengikuti penomoran grup)")

        entries.append({"edit_psd": master_path, "label": label, "jobs": jobs})
        registered.add(os.path.normcase(os.path.abspath(master_path)))
        added += 1

    if added > 0:
        if _save_entries(manifest_path, entries):
            print(f"\n[INFO] Manifest di-update: +{added} master manual")
        else:
            return 0

    return added


def main() -> int:
    if os.name == "nt":
        try:
            sys.stdout.reconfigure(encoding="utf-8")
            sys.stderr.reconfigure(encoding="utf-8")
        except Exception:
            pass

    print("PYTHON_SCRIPT_STARTED: buat_master_duplikat.py")
    sys.stdout.flush()

    argv = sys.argv[1:]
    manifest_flag = ""
    for a in argv:
        if a.startswith("--manifest="):
            manifest_flag = a.split("=", 1)[1].strip()
    args = [a for a in argv if not a.startswith("--")]

    # Flag: --overwrite-manual memaksa timpa file yang sudah ada (master input manual
    # yang tak terdeteksi di fase 1, lalu kamu salin sendiri ke output).
    overwrite_manual = "--overwrite-manual" in [a.lower() for a in argv]

    if len(args) < 2:
        print("[ERROR] Usage: buat_master_duplikat.py <pilihan_path> <output_base> [oke_base] [--manifest=...]",
              file=sys.stderr)
        return 2

    pilihan_path = args[0].strip()
    output_base = _resolve_output_base(args[1].strip())
    oke_base_path = args[2].strip() if len(args) > 2 else ""

    if _script_dir() not in sys.path:
        sys.path.insert(0, _script_dir())
    from _photoshop_utils import manifest_path_for, run_duplicate_only

    manifest = manifest_flag or manifest_path_for(pilihan_path, output_base)

    print(f"[INFO] PILIHAN : {pilihan_path}")
    print(f"[INFO] OUTPUT  : {output_base}")
    if oke_base_path:
        print(f"[INFO] OKE BASE: {oke_base_path}")
    print(f"[INFO] Manifest: {manifest}")

    if not os.path.isfile(manifest):
        print("[ERROR] Belum ada manifest untuk folder PILIHAN ini — jalankan dulu tombol master "
              "(mis. BUAT MASTER / MANASIK) supaya tujuan duplikat tercatat.", file=sys.stderr)
        return 1

    # ------------------------------------------------------------------
    # Langkah 1 & 2 dipindah dari fase 1 copy-edit ke fase 2 ini:
    #   (1) salin .txt dari folder PILIHAN ke output
    #   (2) buat shortcut output lokal
    # Shortcut OKE BASE TIDAK di sini — sudah dibuat di fase 1.
    # Jalankan SEBELUM duplikasi PSD, supaya kalau gagal masih ada
    # template + manifest untuk ditekan ulang.
    # ------------------------------------------------------------------
    rel_structure = _get_relative_path_from_month(pilihan_path)
    final_output_folder = os.path.join(output_base, rel_structure)
    os.makedirs(final_output_folder, exist_ok=True)

    print("\n--- Menyalin file .txt dari folder PILIHAN (fase 2) ---")
    try:
        for name in os.listdir(pilihan_path):
            p = os.path.join(pilihan_path, name)
            if os.path.isdir(p):
                out_p = os.path.join(final_output_folder, name)
                _copy_txt_files_recursive(p, out_p)
    except Exception as e:
        print(f"[WARNING] Gagal salin .txt: {e}", file=sys.stderr)

    if oke_base_path:
        print("\n--- Membuat shortcut output lokal (fase 2) ---")
        try:
            # Shortcut di OKE BASE (DOC/MENTAHAN/PILIHAN/#OKE) sudah dibuat di
            # fase 1 (pasfoto/manasik/wisuda/profesi_flat) — di sini cukup
            # shortcut output lokal.
            from pasfoto import create_shortcuts_in_output_local
            user_name = os.environ.get('BMACHINE_USER_NAME', 'USER')
            create_shortcuts_in_output_local(final_output_folder, pilihan_path, oke_base_path, user_name, output_base)
        except Exception as e:
            print(f"[WARNING] Gagal buat shortcut output lokal: {e}", file=sys.stderr)

    # ------------------------------------------------------------------
    # Langkah 3: gandakan tiap template edit (edit_psd) ke semua job.
    # SEBELUM duplikasi: scan master PSD manual yang user tambah sendiri.
    # ------------------------------------------------------------------
    print("\n--- Scanning master PSD tambahan (manual) ---")
    _add_manual_masters_to_manifest(manifest, final_output_folder, pilihan_path)
    
    print("\n--- Menjalankan DUPLIKAT PSD ---")
    if overwrite_manual:
        print("[INFO] Mode overwrite-manual: file yang sudah ada akan di-TIMPA.")
    ok = run_duplicate_only(manifest, skip_existing=not overwrite_manual, overwrite_manual=overwrite_manual)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())