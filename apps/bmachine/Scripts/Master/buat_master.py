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
import re
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


# Aturan kode .txt -> folder master ada di master_codes.py (satu sumber,
# dipakai juga oleh manasik.py / wisuda.py / tambahan_flat.py).
try:
    sys.path.insert(0, get_script_dir())
    import master_codes as mc
except Exception as _e:      # pragma: no cover
    mc = None
    print(f"[WARN] master_codes.py tidak bisa dimuat: {_e}", file=sys.stderr)


def scan_codes(pilihan_path):
    """Semua kode .txt di PILIHAN (rekursif) -> list kode unik, urut kemunculan."""
    if mc is None or not os.path.isdir(pilihan_path):
        return []
    out, seen = [], set()
    for root, dirs, files in os.walk(pilihan_path):
        has_txt = any(f.lower().endswith('.txt') for f in files)
        if not has_txt:
            continue
        try:
            codes = mc.find_candidate_codes(root)
        except Exception:
            continue
        for c in codes:
            if mc.code_prefix(c) and c not in seen:
                seen.add(c)
                out.append(c)
    return out


_PASFOTO_KEYWORDS   = ["PAS FOTO", "PAS_FOTO", "PASFOTO", "FREE PAS FOTO", "FREE PAS_FOTO", "PAS FOTO FREE", "PFM", "PFB"]
# Kode pas foto eksplisit (termasuk pemisah underscore): PFM-005 / PFM_005 / PFB 005 / PFM005
_PASFOTO_CODE_RE    = re.compile(r"PF[MB][\s_\-]*\d+", re.IGNORECASE)
_PROFESI_KEYWORDS   = ["PROFESI", "SPORTY", "FOTO PROFESI"]
# MANASIK: hanya kode MSK yang ditangani manasik.py. BML/BMP/KML/KMP
# (bersama & kalender manasik) ditangani tambahan_flat.py lewat registry.
_MANASIK_KEYWORDS   = ["MANASIK", "MSK", "NONREG"]
_WISUDA_KEYWORDS    = ["WISUDA", "WSD"]

# Kode di nama/isi .txt: "MSK-001" / "KODE= MSK-001" / "MSK 10RP-004" / "BML 005"
_CODE_TOKEN_RE      = re.compile(r"\b([A-Z]{2,4})[\s_\-=:.]{0,3}(\d{1,3})(?!\d)")
# Penanda ukuran dibuang dulu supaya "MSK 10RP-004" tidak terbaca MSK-10
_SIZE_TOKEN_RE      = re.compile(r"(?i)(?<![0-9])(?:10|12|14|16|20)\s*RP(?![0-9A-Z])|(?<![0-9])[2-9]\s*R(?![0-9A-Z])")
_PSEUDO_PREFIX      = {"KODE", "KODENYA", "CODE", "KD"}

# prefix kode -> jenis master
_CODE_PREFIX_KIND = {
    "MSK": "manasik",
    "BML": "manasik", "BMP": "manasik", "KML": "manasik", "KMP": "manasik",
    "WSD": "wisuda", "KRA": "wisuda",
    "PFM": "pasfoto", "PFB": "pasfoto",
}


def codes_in_text(text):
    """Ambil semua prefix kode dari teks: ['MSK', 'FRS', ...]."""
    clean = _SIZE_TOKEN_RE.sub(" ", text or "").upper()
    out = []
    for pref, _num in _CODE_TOKEN_RE.findall(clean):
        if pref not in _PSEUDO_PREFIX:
            out.append(pref)
    return out


def iter_txt_files(root, max_depth=4):
    r"""Semua file .txt di dalam root sampai kedalaman max_depth.

    NOTE.txt tidak selalu ada langsung di bawah PILIHAN. Contoh nyata:
      ...\05_SDIT DARUL QURAN\PILIHAN\1. PAS FOTO\NOTE.txt
    Isinya bisa "PFM_005" / "PAS FOTO" / "2X3 = 6 LEMBAR" yang menandakan pas foto.
    Karena itu pencarian .txt harus rekursif (perilaku sama seperti pasfoto.py/manasik.py
    yang memakai os.walk), bukan cuma 1 level.
    """
    root = os.path.abspath(root)
    base = root.rstrip(os.sep).count(os.sep)
    try:
        for cur, dirs, files in os.walk(root):
            depth = cur.rstrip(os.sep).count(os.sep) - base
            if depth >= max_depth:
                dirs[:] = []
            for f in files:
                if f.lower().endswith(".txt"):
                    yield os.path.join(cur, f)
    except Exception:
        return


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

            # Deteksi kode pas foto eksplisit (termasuk pemisah underscore: PFM-005 / PFM_005)
            if _PASFOTO_CODE_RE.search(combined):
                found.add('pasfoto')

            for kw in _PASFOTO_KEYWORDS:
                if kw in combined: found.add('pasfoto')
            for kw in _MANASIK_KEYWORDS:
                if kw in combined: found.add('manasik')
            for kw in _WISUDA_KEYWORDS:
                if kw in combined: found.add('wisuda')

            # Deteksi dari KODE di nama/isi .txt (MSK-001 / KODE= MSK-001 / BML-005)
            for pref in codes_in_text(combined):
                kind = _CODE_PREFIX_KIND.get(pref)
                if kind:
                    found.add(kind)
        except Exception:
            pass

    # 1) Nama folder level-1 di dalam PILIHAN.
    #    PROFESI ditentukan dari dua hal:
    #    a) Keyword profesi (PROFESI, SPORTY, FOTO PROFESI) di nama folder, ATAU
    #    b) Penanda 10RP / 8R di nama folder. Tapi 10RP/8R juga dipakai manasik &
    #       wisuda, jadi untuk membedakan: jika ada sub-folder profesi khas
    #       (ASTRONOT, BRIMOB, CHEF, DAMKAR, DOKTER, MASINIS, PERAWAT, PILOT,
    #       TENTARA, TNI, POLISI, NURSING, OB, dll), maka folder 10RP/8R itu
    #       dianggap profesi. Jika sub-foldernya PAS FOTO / MANASIK / WISUDA
    #       atau kosong, tidak dianggap profesi.
    #    Contoh nyata: "1. FOTO 10RP OB PROPFESI GRADASI" + sub ASTRONOT, BRIMOB -> profesi.
    #    Contoh nyata: "1. FOTO 10RP MANASIK" + sub MSK-001 -> manasik (bukan profesi).
    #
    #    Daftar sub-folder profesi khas (case-insensitive, partial match):
    _PROFESI_SUBFOLDER_HINTS = [
        "ASTRONOT", "BRIMOB", "CHEF", "DAMKAR", "DOKTER", "MASINIS", "PERAWAT",
        "PILOT", "TENTARA", "TNI", "POLISI", "OB", "BASKET", "ESPORT",
        "PEMBALAP", "SEPAK", "TAEKWONDO", "TENNIS", "UFC", "SUKU",
    ]

    for entry in entries:
        full = os.path.join(pilihan_path, entry)
        if not os.path.isdir(full):
            continue
        entry_upper = entry.upper()

        # Keyword profesi langsung di nama folder
        has_profesi_kw = any(kw in entry_upper for kw in _PROFESI_KEYWORDS)
        has_10rp_8r = ("10RP" in entry_upper or "8R" in entry_upper)

        # Cek sub-folder untuk hint profesi
        sub_dirs = []
        try:
            sub_dirs = [d for d in os.listdir(full) if os.path.isdir(os.path.join(full, d))]
        except Exception:
            pass
        sub_dirs_upper = [d.upper() for d in sub_dirs]
        has_profesi_subhint = any(
            hint in s for s in sub_dirs_upper for hint in _PROFESI_SUBFOLDER_HINTS
        )

        if has_profesi_kw or (has_10rp_8r and has_profesi_subhint):
            found.add('profesi')

        # Pas foto / manasik / wisuda tetap dari keyword di nama folder level-1.
        for kw in _PASFOTO_KEYWORDS:
            if kw in entry_upper:
                found.add('pasfoto')
        for kw in _MANASIK_KEYWORDS:
            if kw in entry_upper:
                found.add('manasik')
        for kw in _WISUDA_KEYWORDS:
            if kw in entry_upper:
                found.add('wisuda')

    # 2) Semua file .txt (NOTE.txt) sampai beberapa level di dalam PILIHAN,
    #    karena NOTE.txt bisa berada di dalam sub-folder (mis. PILIHAN\1. PAS FOTO\NOTE.txt).
    for txt_path in iter_txt_files(pilihan_path, max_depth=4):
        check_txt(txt_path)

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

    # Mode: --mode=copy-edit (default) = salin master untuk diedit dulu di Photoshop.
    # --mode=direct = perilaku lama (langsung duplikasi tanpa Photoshop).
    #
    # MANUAL vs OTOMATIS: default = fase 1 saja (salin template + catat tujuan), lalu
    # user edit PS sesuka hati dan menekan tombol DUPLIKAT PSD. --auto mengembalikan
    # perilaku lama (fase 2 jalan sendiri: buka + tunggu tab ditutup + langsung
    # gandakan). --no-open melewati auto-buka template di Photoshop.
    mode = "copy-edit"
    auto_duplikat = False
    auto_open = True
    for _a in sys.argv:
        if _a.startswith("--mode="):
            mode = _a.split("=", 1)[1].lower()
        elif _a.lower() == "--auto":
            auto_duplikat = True
        elif _a.lower() == "--no-open":
            auto_open = False
    if any(a.startswith("--") for a in sys.argv):
        sys.argv = [a for a in sys.argv if not a.startswith("--")]
    if mode not in ("copy-edit", "direct"):
        print(f"[WARNING] Mode tidak dikenal: '{mode}'. Dipakai 'copy-edit'.", file=sys.stderr)
        mode = "copy-edit"
    if len(sys.argv) < 2:
        print("[ERROR] Usage: buat_master.py <pilihan_path> [output_base] [oke_base] "
              "[--mode=copy-edit|direct] [--auto] [--no-open]", file=sys.stderr)
        sys.exit(1)
    print(f"[INFO] Mode buat master: {mode}"
          + (" (auto-duplikat)" if auto_duplikat else " (MANUAL: tekan tombol DUPLIKAT PSD)"))

    pilihan_path = sys.argv[1].strip().strip('"').strip("'")
    output_base  = sys.argv[2].strip().strip('"') if len(sys.argv) > 2 else ""
    oke_base     = sys.argv[3].strip().strip('"') if len(sys.argv) > 3 else ""

    # Terima juga path ke file .txt (mis. "...\3. FOTO TM 8R GRADASI\KODE= MSK-001.txt")
    if os.path.isfile(pilihan_path):
        pilihan_path = os.path.dirname(pilihan_path)
    pilihan_path = os.path.normpath(pilihan_path)

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

    # ------------------------------------------------------------------
    # REGISTRY KODE -> FOLDER MASTER (Settings + Master Tambahan)
    # Satu kali baca, dipakai untuk semua jenis: MSK, WSD, FRS, BML, KMP, ...
    # ------------------------------------------------------------------
    registry = {}
    codes_found = []
    extra_prefixes = []
    if mc is not None:
        try:
            registry = mc.build_registry()
        except Exception as e:
            print(f"[WARN] Gagal membangun registry master: {e}", file=sys.stderr)
            registry = {}
        codes_found = scan_codes(pilihan_path)
        prefixes = {mc.code_prefix(c) for c in codes_found if mc.code_prefix(c)}
        extra_prefixes = sorted(p for p in prefixes
                                if p not in mc.HANDLED_BY and p in registry)
        missing_prefixes = sorted(p for p in prefixes
                                  if p not in mc.HANDLED_BY and p not in registry)
        print(f"[INFO] Kode .txt terdeteksi: {', '.join(codes_found) or '∅'}")
        if registry:
            print("[INFO] Folder master yang dikenal:")
            for line in mc.describe_registry(registry).splitlines():
                print(line)
        if missing_prefixes:
            print(f"[WARN] Prefix belum punya folder master: {', '.join(missing_prefixes)}")

    detected = detect_folder_types(pilihan_path)
    if extra_prefixes:
        detected.add('tambahan')
    print(f"[INFO] Terdeteksi jenis master: {', '.join(sorted(detected)) or 'tidak ada'}")

    if not detected:
        print("[WARN] Tidak ada sub-folder yang cocok dengan jenis master apapun.")
        print("[WARN] Pastikan folder berisi sub-folder PROFESI / PAS FOTO / MANASIK / WISUDA,")
        print("[WARN] atau .txt berisi kode (MSK-001, WSD-001, FRS-004, KMP-002, ...).")
        sys.exit(0)

    if not output_base:
        print("[ERROR] Output base path tidak ada. Set di Settings > Paths > OKE BASE.", file=sys.stderr)
        sys.exit(1)

    # Pastikan folder output sudah ada (dulu script anak menolak jalan kalau
    # output base belum dibuat — kasus bulan/tahun baru).
    try:
        os.makedirs(output_base, exist_ok=True)
    except Exception as e:
        print(f"[WARN] Gagal membuat output base '{output_base}': {e}", file=sys.stderr)

    script_dir  = get_script_dir()
    python_exe  = sys.executable
    total = 0
    errors = 0

    # Fase copy-edit: tiap script jenis SALIN 1 template + catat tujuan duplikat ke
    # manifest. Manifest disimpan di path STABIL (bukan temp ber-pid) supaya tombol
    # DUPLIKAT PSD bisa menemukannya lagi kapan saja setelah fase 1 selesai.
    script_flags = []
    manifest_path = None
    if mode == "copy-edit":
        if script_dir not in sys.path:
            sys.path.insert(0, script_dir)
        from _photoshop_utils import manifest_path_for
        manifest_path = manifest_path_for(pilihan_path, output_base)
        if os.path.isfile(manifest_path):
            try:
                os.remove(manifest_path)
            except OSError:
                pass
        script_flags = ["--mode=copy-edit", "--phase=1", f"--manifest={manifest_path}"]
        print(f"[INFO] Manifest copy-edit: {manifest_path}")

    os.environ["BMACHINE_USER_NAME"] = user_name

    if 'profesi' in detected:
        if not master_profesi:
            print("[SKIP] Master Profesi tidak diset di Settings.", file=sys.stderr)
        else:
            rc = run_script([
                python_exe, os.path.join(script_dir, "profesi_flat.py"),
                master_profesi, master_sporty or "", pilihan_path, output_base,
                oke_base or "", "", "", master_profesi_8r or "",
            ] + script_flags, "PROFESI / SPORTY")
            total += 1
            if rc != 0: errors += 1

    if 'pasfoto' in detected:
        if not master_pasfoto:
            print("[SKIP] Master PasFoto tidak diset di Settings.", file=sys.stderr)
        else:
            rc = run_script([
                python_exe, os.path.join(script_dir, "pasfoto.py"),
                master_pasfoto, pilihan_path, output_base, oke_base or "NONE",
            ] + script_flags, "PAS FOTO")
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
            ] + script_flags, "MANASIK")
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
            ] + script_flags, "WISUDA")
            total += 1
            if rc != 0: errors += 1

    # ------------------------------------------------------------------
    # MASTER TAMBAHAN: kode yang tidak punya script khusus
    # (FRS, BML, BMP, KML, KMP, BSL, BSP, KSL, KSP, KPL, KPP, HGN, ATN,
    #  MWS, IDC, PLK, ...) -> master diambil dari folder tambahan di Settings.
    # ------------------------------------------------------------------
    if 'tambahan' in detected and extra_prefixes:
        try:
            import tempfile
            reg_path = os.path.join(tempfile.gettempdir(), f"bm_registry_{os.getpid()}.json")
            with open(reg_path, "w", encoding="utf-8") as f:
                json.dump({p: registry[p] for p in extra_prefixes}, f, ensure_ascii=False, indent=1)
            rc = run_script([
                python_exe, os.path.join(script_dir, "tambahan_flat.py"),
                pilihan_path, output_base, reg_path,
            ] + script_flags, "MASTER TAMBAHAN (" + ", ".join(extra_prefixes) + ")")
            total += 1
            if rc != 0: errors += 1
        except Exception as e:
            print(f"[ERROR] Master tambahan gagal dijalankan: {e}", file=sys.stderr)
            errors += 1

    # PENGGANDAAN: default MANUAL — penggandaan hanya jalan saat user klik tombol
    # DUPLIKAT PSD. --auto = perilaku lama (fase 2 jalan sendiri: buka semua master,
    # tunggu tiap tab ditutup, lalu gandakan).
    if mode == "copy-edit":
        entries, jobs_planned = [], 0
        if not (manifest_path and os.path.isfile(manifest_path)):
            print("[WARN] Manifest kosong (tidak ada master disalin) — fase edit dilewati.")
        elif auto_duplikat:
            rc = run_script([
                python_exe, os.path.join(script_dir, "_photoshop_utils.py"),
                manifest_path, "1800",
            ], "SINKRONISASI FOTO OTOMATIS (buka master -> tunggu tab ditutup -> gandakan)")
            total += 1
            if rc != 0:
                errors += 1
        else:
            if script_dir not in sys.path:
                sys.path.insert(0, script_dir)
            try:
                from _photoshop_utils import load_manifest, open_edit_psds
                entries = load_manifest(manifest_path)
                jobs_planned = sum(len(e.get("jobs") or []) for e in entries)
                if auto_open and entries:
                    open_edit_psds(manifest_path)
            except Exception as e:
                print(f"[WARN] Auto-buka template dilewati: {e}", file=sys.stderr)
            print(f"\n{'='*60}")
            print(f"[MANUAL] {len(entries)} template edit siap, {jobs_planned} file tujuan menunggu.")
            print("[MANUAL] 1) Edit + SAVE PSD-nya di Photoshop (sesuka hati, tanpa timer).")
            print("[MANUAL] 2) Setelah selesai: klik tombol 'DUPLIKAT PSD'.")
            print(f"[MANUAL] Manifest: {manifest_path}")
            print(f"{'='*60}\n")

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
