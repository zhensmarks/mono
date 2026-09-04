#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
buat_master.py — One-For-All Master Orchestrator
=================================================
Sekali jalan, deteksi semua jenis folder di dalam PILIHAN dan
jalankan master script yang sesuai secara berurutan.

  PROFESI / SPORTY -> profesi_flat.py logic
  PAS FOTO         -> pasfoto.py logic
  MANASIK          -> manasik.py logic
  WISUDA           -> wisuda.py logic

Usage (via batch_wrapper.py):
  python buat_master.py <pilihan_path> <output_base_path> <oke_base_path>

Or directly via context menu (standalone):
  python buat_master.py <pilihan_path>

Paths are read from BMachine SQLite database automatically.
"""

import os
import sys
import sqlite3
import subprocess
import json
import traceback


def find_db_path():
    appdata = os.environ.get("APPDATA", "")
    db_path = os.path.join(appdata, "BMachine", "BMachine.db")
    if os.path.exists(db_path):
        return db_path
    local = os.environ.get("LOCALAPPDATA", "")
    db_path2 = os.path.join(local, "BMachine", "BMachine.db")
    if os.path.exists(db_path2):
        return db_path2
    return None


def read_db_value(db_path, key):
    if not db_path or not os.path.exists(db_path):
        return ""
    try:
        conn = sqlite3.connect(db_path)
        cur = conn.cursor()
        cur.execute("SELECT Value FROM KeyValueStore WHERE Key = ?", (key,))
        row = cur.fetchone()
        conn.close()
        if row:
            raw = row[0]
            try:
                parsed = json.loads(raw)
                return str(parsed) if parsed else ""
            except Exception:
                return str(raw)
    except Exception as e:
        print(f"[WARN] Gagal membaca DB key '{key}': {e}", file=sys.stderr)
    return ""


def get_script_dir():
    return os.path.dirname(os.path.abspath(__file__))


_PASFOTO_KEYWORDS   = ["PAS FOTO", "PAS_FOTO", "PASFOTO", "FREE PAS FOTO", "FREE PAS_FOTO", "PAS FOTO FREE", "PFM", "PFB"]
_PROFESI_KEYWORDS   = ["PROFESI", "SPORTY", "FOTO PROFESI"]
_MANASIK_KEYWORDS   = ["MANASIK", "MSK"]
_WISUDA_KEYWORDS    = ["WISUDA", "WSD"]


def detect_folder_types(pilihan_path):
    found = set()
    if not os.path.isdir(pilihan_path):
        return found
    try:
        entries = os.listdir(pilihan_path)
    except Exception:
        return found
        
    def check_txt(txt_path):
        try:
            filename = os.path.basename(txt_path).upper()
            content = ""
            try:
                with open(txt_path, "r", encoding="utf-8", errors="ignore") as f:
                    content = f.read().upper()
            except Exception:
                pass
                
            combined = filename + " " + content
            
            for kw in _PASFOTO_KEYWORDS:
                if kw in combined: found.add('pasfoto')
            for kw in _MANASIK_KEYWORDS:
                if kw in combined: found.add('manasik')
            for kw in _WISUDA_KEYWORDS:
                if kw in combined: found.add('wisuda')
        except Exception:
            pass

    for entry in entries:
        full = os.path.join(pilihan_path, entry)
        
        if os.path.isdir(full):
            # Cek nama folder HANYA untuk PROFESI
            entry_upper = entry.upper()
            for kw in _PROFESI_KEYWORDS:
                if kw in entry_upper:
                    found.add('profesi')
                    break
                    
            # Cari file .txt di dalam subfolder untuk Pas Foto, Manasik, Wisuda
            try:
                sub_entries = os.listdir(full)
                for sub in sub_entries:
                    if sub.lower().endswith('.txt'):
                        check_txt(os.path.join(full, sub))
            except Exception:
                pass
                
        elif entry.lower().endswith('.txt'):
            # Jika ada .txt langsung di folder pilihan
            check_txt(full)
            
    return found


def run_script(cmd_args, description):
    print(f"\n{'='*60}")
    print(f"[BUAT MASTER] Memulai: {description}")
    print(f"{'='*60}")
    sys.stdout.flush()
    env = os.environ.copy()
    env["PYTHONIOENCODING"] = "utf-8"
    env["PYTHONUNBUFFERED"] = "1"
    try:
        proc = subprocess.Popen(cmd_args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
        while True:
            line = proc.stdout.readline()
            if not line:
                if proc.poll() is not None:
                    break
                continue
            try:
                sys.stdout.write(line.decode("utf-8", errors="replace"))
                sys.stdout.flush()
            except Exception:
                pass
        proc.wait()
        print(f"[BUAT MASTER] Selesai: {description} (exit {proc.returncode})")
        sys.stdout.flush()
        return proc.returncode
    except Exception as e:
        print(f"[BUAT MASTER] ERROR saat menjalankan {description}: {e}", file=sys.stderr)
        return -1


def main():
    print("PYTHON_SCRIPT_STARTED: buat_master.py")
    sys.stdout.flush()

    if len(sys.argv) < 2:
        print("[ERROR] Usage: buat_master.py <pilihan_path> [output_base] [oke_base]", file=sys.stderr)
        sys.exit(1)

    pilihan_path = sys.argv[1].strip()
    output_base  = sys.argv[2].strip() if len(sys.argv) > 2 else ""
    oke_base     = sys.argv[3].strip() if len(sys.argv) > 3 else ""

    print(f"[INFO] PILIHAN: {pilihan_path}")

    if not os.path.isdir(pilihan_path):
        print(f"[ERROR] Folder PILIHAN tidak ditemukan: {pilihan_path}", file=sys.stderr)
        sys.exit(1)

    db_path = find_db_path()

    def db(key, fallback=""):
        val = read_db_value(db_path, key)
        return val if val else fallback

    if not output_base:
        output_base = db("Configs.Master.OkeBase") or db("Configs.Master.LocalOutput") or ""
    if not oke_base:
        oke_base = db("Configs.Master.OkeBase") or ""

    master_profesi      = db("Configs.Master.Profesi")
    master_profesi_8r   = db("Configs.Master.Profesi8R")
    master_sporty       = db("Configs.Master.Sporty")
    master_pasfoto      = db("Configs.Master.PasFoto")
    master_manasik_10rp = db("Configs.Master.Manasik10RP")
    master_manasik_8r   = db("Configs.Master.Manasik8R")
    master_wisuda_10rp  = db("Configs.Master.Wisuda10RP")
    master_wisuda_8r    = db("Configs.Master.Wisuda8R")
    user_name           = db("User.Name") or "USER"

    print(f"[INFO] Output Base: {output_base}")
    print(f"[INFO] Oke Base: {oke_base}")

    detected = detect_folder_types(pilihan_path)
    print(f"[INFO] Terdeteksi jenis master: {', '.join(sorted(detected)) or 'tidak ada'}")

    if not detected:
        print("[WARN] Tidak ada sub-folder yang cocok dengan jenis master apapun.")
        print("[WARN] Pastikan folder berisi sub-folder bernama PROFESI, PAS FOTO, MANASIK, atau WISUDA.")
        sys.exit(0)

    if not output_base:
        print("[ERROR] Output base path tidak ada. Set di Settings > Paths > OKE BASE.", file=sys.stderr)
        sys.exit(1)

    script_dir  = get_script_dir()
    python_exe  = sys.executable
    total = 0
    errors = 0

    os.environ["BMACHINE_USER_NAME"] = user_name

    if 'profesi' in detected:
        if not master_profesi:
            print("[SKIP] Master Profesi tidak diset di Settings.", file=sys.stderr)
        else:
            rc = run_script([
                python_exe, os.path.join(script_dir, "profesi_flat.py"),
                master_profesi, master_sporty or "", pilihan_path, output_base,
                oke_base or "", "", "", master_profesi_8r or "",
            ], "PROFESI / SPORTY")
            total += 1
            if rc != 0: errors += 1

    if 'pasfoto' in detected:
        if not master_pasfoto:
            print("[SKIP] Master PasFoto tidak diset di Settings.", file=sys.stderr)
        else:
            rc = run_script([
                python_exe, os.path.join(script_dir, "pasfoto.py"),
                master_pasfoto, pilihan_path, output_base, oke_base or "NONE",
            ], "PAS FOTO")
            total += 1
            if rc != 0: errors += 1

    if 'manasik' in detected:
        master_manasik = master_manasik_10rp or master_manasik_8r
        if not master_manasik:
            print("[SKIP] Master Manasik tidak diset di Settings.", file=sys.stderr)
        else:
            rc = run_script([
                python_exe, os.path.join(script_dir, "manasik.py"),
                master_manasik, pilihan_path, output_base,
                master_manasik_8r or "", oke_base or "",
            ], "MANASIK")
            total += 1
            if rc != 0: errors += 1

    if 'wisuda' in detected:
        master_wisuda = master_wisuda_10rp or master_wisuda_8r
        if not master_wisuda:
            print("[SKIP] Master Wisuda tidak diset di Settings.", file=sys.stderr)
        else:
            rc = run_script([
                python_exe, os.path.join(script_dir, "wisuda.py"),
                master_wisuda, pilihan_path, output_base,
                master_wisuda_8r or "", oke_base or "",
            ], "WISUDA")
            total += 1
            if rc != 0: errors += 1

    print(f"\n{'='*60}")
    print(f"[BUAT MASTER] SELESAI — {total} script dijalankan, {errors} error.")
    print(f"{'='*60}")
    print(f"SUMMARY_JSON:{json.dumps({'total': total, 'errors': errors, 'detected': sorted(list(detected))})}")
    sys.stdout.flush()
    sys.exit(0 if errors == 0 else 1)


if __name__ == "__main__":
    if os.name == "nt":
        try:
            sys.stdout.reconfigure(encoding="utf-8")
            sys.stderr.reconfigure(encoding="utf-8")
        except Exception:
            pass
    main()
