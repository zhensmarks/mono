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
    # ------------------------------------------------------------------
    print("\n--- Menjalankan DUPLIKAT PSD ---")
    if overwrite_manual:
        print("[INFO] Mode overwrite-manual: file yang sudah ada akan di-TIMPA.")
    ok = run_duplicate_only(manifest, skip_existing=not overwrite_manual, overwrite_manual=overwrite_manual)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())