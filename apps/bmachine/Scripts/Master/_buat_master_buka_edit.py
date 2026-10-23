#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Tombol BUKA EDIT PSD — buka template edit di Photoshop atas permintaan (opsional).

Dipakai kalau tab template sudah ditutup tapi mau diedit lagi: membaca manifest yang
sama seperti tombol DUPLIKAT PSD dan membuka tiap `edit_psd` di Photoshop. TIDAK
menggandakan apa pun dan TIDAK menunggu tab ditutup.

Dipanggil app lewat: batch_wrapper.py --target buat_master_buka_edit.py --pilihan <src> --output <base>
CLI manual: python buat_master_buka_edit.py <pilihan_path> <output_base> [oke_base] [--manifest=<path>]
"""
import os
import sys


def _script_dir() -> str:
    return os.path.dirname(os.path.abspath(__file__))


def _resolve_output_base(output_base: str) -> str:
    """Output base yang dipakai buat_master: argumen dulu, lalu DB (OkeBase/LocalOutput)."""
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


def main() -> int:
    if os.name == "nt":
        try:
            sys.stdout.reconfigure(encoding="utf-8")
            sys.stderr.reconfigure(encoding="utf-8")
        except Exception:
            pass

    print("PYTHON_SCRIPT_STARTED: buat_master_buka_edit.py")
    sys.stdout.flush()

    argv = sys.argv[1:]
    manifest_flag = ""
    for a in argv:
        if a.startswith("--manifest="):
            manifest_flag = a.split("=", 1)[1].strip()
    args = [a for a in argv if not a.startswith("--")]

    if len(args) < 2:
        print("[ERROR] Usage: buat_master_buka_edit.py <pilihan_path> <output_base> [oke_base] [--manifest=...]",
              file=sys.stderr)
        return 2

    pilihan_path = args[0].strip()
    output_base = _resolve_output_base(args[1].strip())

    if _script_dir() not in sys.path:
        sys.path.insert(0, _script_dir())
    from _photoshop_utils import load_manifest, manifest_path_for, open_edit_psds

    manifest = manifest_flag or manifest_path_for(pilihan_path, output_base)
    entries = load_manifest(manifest)

    print(f"[INFO] PILIHAN : {pilihan_path}")
    print(f"[INFO] Manifest: {manifest}")

    if not entries:
        print("[ERROR] Belum ada manifest template edit untuk folder PILIHAN ini — jalankan dulu "
              "tombol master (mis. BUAT MASTER / MANASIK).", file=sys.stderr)
        return 1

    for e in entries:
        print(f"  - {e.get('label') or ''}: {e['edit_psd']}")
    return 0 if open_edit_psds(manifest) else 1


if __name__ == "__main__":
    sys.exit(main())