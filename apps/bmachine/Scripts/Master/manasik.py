#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
manasik.py (fixed clean)
- Hapus escape karakter \" yang menyebabkan SyntaxError
- Tetap: baca JPG recursive, 1(1)/1(2) -> 1.psd, config.json opsional
"""

import os
import re
import shutil
import sys
import json

# Nama subfolder tempat template edit disimpan (per grup).
# Contoh: "...\1. FOTO 10RP_MANASIK_GRADASI\NONREG\#MASTER\MSK-002.psd"
MASTER_SUBDIR = "#MASTER"

# Util I/O cepat (batch paralel + cache + robocopy). Dipakai manasik/wisuda/
# pasfoto/profesi_flat untuk salin PSD besar tanpa bikin sistem lag.
try:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from _fastio import batch_copy as _fast_batch_copy, cached_listdir as _fast_cached_listdir, fast_copy as _fast_copy
    HAS_FASTIO = True
except Exception:
    HAS_FASTIO = False
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
    def _fast_cached_listdir(p):
        return os.listdir(p)
    def _fast_copy(s, d, overwrite=True):
        if os.path.isfile(d) and not overwrite:
            return True
        try:
            os.makedirs(os.path.dirname(os.path.abspath(d)), exist_ok=True)
            shutil.copy2(s, d)
            return True
        except Exception:
            return False

# Copy-Edit-Sync: mode fase (flag CLI --mode=copy-edit).
# Aktif = 1 template disalin untuk diedit, tujuan duplikat dicatat ke manifest.
COPY_EDIT = False
MANIFEST_PATH = None


def get_project_root():
    current_dir = os.path.dirname(os.path.abspath(__file__))
    while True:
        try:
            if any(fname.endswith('.sln') for fname in os.listdir(current_dir)):
                return current_dir
        except Exception:
            pass
        parent_dir = os.path.dirname(current_dir)
        if parent_dir == current_dir:
            print("[INFO] Tidak menemukan .sln; pakai direktori skrip sebagai basis.", file=sys.stderr)
            return os.path.dirname(os.path.abspath(__file__))
        current_dir = parent_dir


def load_config():
    project_root = get_project_root()
    for cfg_dir in [project_root, os.path.dirname(project_root), os.path.dirname(os.path.dirname(project_root))]:
        cfg = os.path.join(cfg_dir, 'config.json')
        if os.path.exists(cfg):
            try:
                with open(cfg, 'r', encoding='utf-8') as f:
                    return json.load(f)
            except Exception as e:
                print(f"[WARNING] Gagal memuat '{cfg}': {e}", file=sys.stderr)
                return {}
    print("[INFO] 'config.json' tidak ditemukan; lanjut default.", file=sys.stderr)
    return {}


def create_shortcuts_in_output_local(final_output_folder, pilihan_path, oke_base_path, user_name, output_base_path):
    """Membuat shortcut di output lokal ke folder input dan ke OKE BASE jika ada."""
    print("\n--- Membuat Shortcut di Output Lokal ---")
    try:
        pilihan_path_norm = os.path.normpath(pilihan_path)
        sumber_parent_folder = os.path.dirname(pilihan_path_norm)

        # Shortcut ke folder event di root output base
        event_folder_name = os.path.basename(final_output_folder)
        event_shortcut_path = os.path.join(output_base_path, f"{event_folder_name}.lnk")
        if not os.path.exists(event_shortcut_path):
            print(f"  - Mencoba membuat shortcut ke event folder: {event_folder_name}...")
            try:
                import subprocess, tempfile
                vbs_script = f"""
Set oWS = WScript.CreateObject(\"WScript.Shell\")
sLinkFile = \"{event_shortcut_path}\"
Set oLink = oWS.CreateShortcut(sLinkFile)
oLink.TargetPath = \"{final_output_folder}\"
oLink.Save
"""
                with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.vbs') as f:
                    f.write(vbs_script)
                    temp_vbs_path = f.name
                subprocess.run(["cscript", "//Nologo", temp_vbs_path], check=True)
                os.remove(temp_vbs_path)
                print(f"    -> Berhasil dibuat: {event_shortcut_path}")
            except Exception:
                print(f"    -> [ERROR] Gagal membuat shortcut ke event folder.")
        else:
            print(f"  - Shortcut ke event folder '{event_folder_name}.lnk' sudah ada, dilewati.")

        # Shortcut ke folder di input
        for item in os.listdir(sumber_parent_folder):
            source_item_path = os.path.join(sumber_parent_folder, item)
            link_item_path = os.path.join(final_output_folder, f"{item}.lnk")
            if os.path.isdir(source_item_path):
                if not os.path.exists(link_item_path):
                    print(f"  - Mencoba membuat shortcut ke input: {item}...")
                    try:
                        import subprocess, tempfile
                        vbs_script = f"""
Set oWS = WScript.CreateObject(\"WScript.Shell\")
sLinkFile = \"{link_item_path}\"
Set oLink = oWS.CreateShortcut(sLinkFile)
oLink.TargetPath = \"{source_item_path}\"
oLink.Save
"""
                        with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.vbs') as f:
                            f.write(vbs_script)
                            temp_vbs_path = f.name
                        subprocess.run(["cscript", "//Nologo", temp_vbs_path], check=True)
                        os.remove(temp_vbs_path)
                        print(f"    -> Berhasil dibuat: {link_item_path}")
                    except Exception:
                        print(f"    -> [ERROR] Gagal membuat shortcut ke '{item}'.")
                else:
                    print(f"  - Shortcut '{item}.lnk' sudah ada, dilewati.")

        # Shortcut ke OKE BASE jika ada
        if oke_base_path and os.path.exists(oke_base_path):
            path_parts = sumber_parent_folder.split(os.sep)
            month_folder_index = -1
            for i, part in enumerate(path_parts):
                if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
                    month_folder_index = i
                    break
            if month_folder_index != -1:
                relative_structure = os.path.join(*path_parts[month_folder_index:])
                oke_dest_path = os.path.join(oke_base_path, relative_structure)
                oke_user_folder = os.path.join(oke_dest_path, f"#OKE {user_name.upper()}")
                oke_shortcut_path = os.path.join(final_output_folder, f"#OKE {user_name.upper()}.lnk")
                if not os.path.exists(oke_shortcut_path):
                    print(f"  - Mencoba membuat shortcut ke OKE BASE...")
                    try:
                        import subprocess, tempfile
                        vbs_script = f"""
Set oWS = WScript.CreateObject(\"WScript.Shell\")
sLinkFile = \"{oke_shortcut_path}\"
Set oLink = oWS.CreateShortcut(sLinkFile)
oLink.TargetPath = \"{oke_user_folder}\"
oLink.Save
"""
                        with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.vbs') as f:
                            f.write(vbs_script)
                            temp_vbs_path = f.name
                        subprocess.run(["cscript", "//Nologo", temp_vbs_path], check=True)
                        os.remove(temp_vbs_path)
                        print(f"    -> Berhasil dibuat: {oke_shortcut_path}")
                    except Exception:
                        print(f"    -> [ERROR] Gagal membuat shortcut ke OKE BASE.")
                else:
                    print(f"  - Shortcut ke OKE BASE sudah ada, dilewati.")

                bat_file_path = os.path.join(final_output_folder, "snap.bat")
                if not os.path.exists(bat_file_path):
                    bat_content = f"""@echo off
setlocal EnableDelayedExpansion
for /F %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a"
chcp 65001 >nul
title SNAP PORTAL
color 0F

echo.
for %%I in ("%~dp0.") do set "PARENT_DIR_NAME=%%~nxI"
echo !ESC![96m  === SNAP PORTAL - MENGIRIM: !PARENT_DIR_NAME! ===!ESC![0m
echo.

if not exist "{oke_user_folder}\\" (
    echo !ESC![91m  [!] KONEKSI GAGAL: Jaringan/Server OKE BASE tidak ditemukan.!ESC![0m
    echo       Tujuan: "{oke_user_folder}"
    pause
    exit /b
)

set /a count=0
for /d %%D in (*) do ( set /a count+=1 )

if !count! EQU 0 (
    echo !ESC![93m  [!] INFO: Tidak ada folder yang perlu dikirim.!ESC![0m
    pause
    del "%~f0"
    exit /b
)

:: Buat script spinner
set "SPINNER=%TEMP%\\snap_spinner.bat"
setlocal DisableDelayedExpansion
(
echo @echo off
echo setlocal EnableDelayedExpansion
echo set "ESC=%%ESC%%"
echo set "s1=.   "
echo set "s2=..  "
echo set "s3=... "
echo set "s4=...."
echo set /a i=0
echo :loop
echo set /a i=^(i+1^)%%%%4
echo if !i! EQU 0 set "c=%%s1%%"
echo if !i! EQU 1 set "c=%%s2%%"
echo if !i! EQU 2 set "c=%%s3%%"
echo if !i! EQU 3 set "c=%%s4%%"
echo ^<nul set /p "=%%ESC%%[2K%%ESC%%[3GStatus : Sedang memindahkan !c!"
echo ping 127.0.0.1 -n 2 ^>nul
echo if exist "%%TEMP%%\\stop_spin" exit
echo goto loop
) > "%SPINNER%"
endlocal

set /a current=0
set "error_found=0"

for /d %%D in (*) do (
    set /a current+=1
    echo !ESC![93m  [ PENGIRIMAN !current! / !count! ]!ESC![0m
    echo   Folder : "%%~nxD"
    <nul set /p "=!ESC![3GStatus : Sedang memindahkan "
    
    if exist "%TEMP%\\stop_spin" del "%TEMP%\\stop_spin"
    start /b cmd /c "%SPINNER%"
    
    pushd "%%D"
    if !ERRORLEVEL! EQU 0 (
        robocopy . "{oke_user_folder}\\%%~nxD" /E /MOVE /IS /R:3 /W:2 /NJH /NJS /NDL /NFL /NC /NS /NP >nul
        set ROBO_ERR=!ERRORLEVEL!
        popd
        
        echo. > "%TEMP%\\stop_spin"
        ping 127.0.0.1 -n 2 >nul
        
        if !ROBO_ERR! GEQ 8 (
            set "error_found=1"
            echo.
            echo !ESC![2K!ESC![3G!ESC![91m[!] GAGAL MEMINDAHKAN FOLDER!ESC![0m
            echo       Pastikan server tidak penuh dan koneksi lancar.
            echo.
            pause
            exit /b
        )
        
        rmdir "%%D" 2>nul
    ) else (
        echo. > "%TEMP%\\stop_spin"
        ping 127.0.0.1 -n 2 >nul
        echo !ESC![2K!ESC![3G!ESC![91m[!] ERROR: Tidak dapat mengakses folder "%%D"!ESC![0m
    )
    
    echo !ESC![2K!ESC![3G!ESC![92mStatus : Selesai 100%%!ESC![0m
    echo.
)

if exist "%SPINNER%" del "%SPINNER%"
if exist "%TEMP%\\stop_spin" del "%TEMP%\\stop_spin"

echo !ESC![96m  === SEMUA PENGIRIMAN SUKSES ===!ESC![0m
timeout /t 2 >nul
del "%~f0"
"""
                    try:
                        with open(bat_file_path, 'w', encoding='utf-8') as f:
                            f.write(bat_content)
                        print(f"  - Berhasil membuat file batch: {os.path.basename(bat_file_path)}")
                    except Exception as e:
                        print(f"  - [ERROR] Gagal membuat file batch: {e}")
                else:
                    print(f"  - File batch '{os.path.basename(bat_file_path)}' sudah ada, dilewati.")

    except Exception as e:
        print(f"[ERROR] Terjadi kesalahan saat membuat shortcut di output lokal: {e}", file=sys.stderr)


def create_oke_base_links(pilihan_path, oke_base_path, user_name):
    """Membuat struktur folder dan shortcut .lnk di OKE BASE dengan format #OKE NAMA_USER (huruf besar)."""
    print("\n--- Memulai Proses OKE BASE ---")
    if not oke_base_path or not os.path.exists(oke_base_path):
        print("[ERROR] Path OKE BASE tidak valid atau tidak ditemukan.", file=sys.stderr)
        return

    try:
        pilihan_path_norm = os.path.normpath(pilihan_path)
        sumber_parent_folder = os.path.dirname(pilihan_path_norm)

        path_parts = sumber_parent_folder.split(os.sep)
        month_folder_index = -1
        for i, part in enumerate(path_parts):
            if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
                month_folder_index = i
                break
        if month_folder_index == -1:
            print("[ERROR] Tidak dapat menemukan folder bulan (contoh: '02 AGUSTUS 2025') di path sumber.", file=sys.stderr)
            return
        relative_structure = os.path.join(*path_parts[month_folder_index:])
        oke_dest_path = os.path.join(oke_base_path, relative_structure)
        os.makedirs(oke_dest_path, exist_ok=True)
        print(f"Folder tujuan OKE BASE: {oke_dest_path}")

        oke_user_folder = os.path.join(oke_dest_path, f"#OKE {user_name.upper()}")
        os.makedirs(oke_user_folder, exist_ok=True)

        for item in os.listdir(sumber_parent_folder):
            source_item_path = os.path.join(sumber_parent_folder, item)
            link_item_path = os.path.join(oke_dest_path, f"{item}.lnk")
            if os.path.isdir(source_item_path):
                if not os.path.exists(link_item_path):
                    print(f"  - Mencoba membuat shortcut untuk: {item}...")
                    # Shortcut hanya untuk Windows
                    try:
                        import subprocess, tempfile
                        vbs_script = f"""
Set oWS = WScript.CreateObject(\"WScript.Shell\")
sLinkFile = \"{link_item_path}\"
Set oLink = oWS.CreateShortcut(sLinkFile)
oLink.TargetPath = \"{source_item_path}\"
oLink.Save
"""
                        with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.vbs') as f:
                            f.write(vbs_script)
                            temp_vbs_path = f.name
                        subprocess.run(["cscript", "//Nologo", temp_vbs_path], check=True)
                        os.remove(temp_vbs_path)
                        print(f"    -> Berhasil dibuat: {link_item_path}")
                    except Exception:
                        print(f"    -> [ERROR] Gagal membuat shortcut untuk '{item}'.")
                else:
                    print(f"  - Shortcut '{item}.lnk' sudah ada, dilewati.")

        oke_shortcut_path = os.path.join(sumber_parent_folder, f"#OKE {user_name.upper()}.lnk")
        if not os.path.exists(oke_shortcut_path):
            print(f"  - Mencoba membuat shortcut kembali ke OKE BASE...")
            try:
                import subprocess, tempfile
                vbs_script = f"""
Set oWS = WScript.CreateObject(\"WScript.Shell\")
sLinkFile = \"{oke_shortcut_path}\"
Set oLink = oWS.CreateShortcut(sLinkFile)
oLink.TargetPath = \"{oke_user_folder}\"
oLink.Save
"""
                with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.vbs') as f:
                    f.write(vbs_script)
                    temp_vbs_path = f.name
                subprocess.run(["cscript", "//Nologo", temp_vbs_path], check=True)
                os.remove(temp_vbs_path)
                print(f"    -> Berhasil dibuat: {oke_shortcut_path}")
            except Exception:
                print(f"    -> [ERROR] Gagal membuat shortcut kembali.")
        else:
            print(f"  - Shortcut kembali '#OKE...' sudah ada, dilewati.")

    except Exception as e:
        print(f"[ERROR] Terjadi kesalahan saat membuat shortcut di OKE BASE: {e}", file=sys.stderr)


# ============================================================================
# DETEKSI KODE DARI FILE .txt
# ----------------------------------------------------------------------------
# Format nama/isi .txt yang didukung (semua dianggap sama):
#   MSK-001.txt              MSK 001.txt            MSK_001.txt
#   KODE= MSK-001.txt        KODE = MSK 10RP-004 .txt
#   MSK-001 (DESAIN BARU).txt            MSK 002, DESAIN BARU.txt
#   FRS-003.txt   BML-005.txt   KML-005, 33+1.txt   PFM-006, TANPA REDAKSI (-1).txt
#   isi file: "KODE= BML-001" / "KODE = MSK 10RP-004"
# ============================================================================

# Token kode: PREFIX 2-4 huruf + pemisah opsional + 1-3 angka (MSK-001 / MSK001)
_CODE_TOKEN_RE = re.compile(r'\b([A-Za-z]{2,4})[\s_\-=:.]{0,3}(\d{1,3})(?!\d)')

# Penanda ukuran cetak. WAJIB dibuang sebelum cari kode, kalau tidak
# "MSK 10RP-004" akan terbaca sebagai MSK-10 (salah).
# Batas: digit sebelumnya tidak boleh angka (supaya "18R" tidak ikut), tapi
# boleh nempel huruf — nyata di lapangan: "FOTO8R_TM", "FOTO10RP_MANASIK".
_SIZE_TOKEN_RE = re.compile(
    r'(?i)(?<![0-9])(?:10|12|14|16|20)\s*RP(?![0-9A-Z])'
    r'|(?<![0-9])[2-9]\s*R(?![0-9A-Z])'
)

# Prefix yang bukan kode, tapi penanda nilai kode ("KODE= 001")
_PSEUDO_PREFIX = {'KODE', 'KODENYA', 'CODE', 'KD'}


def detect_size_tag(text):
    """Ambil penanda ukuran: '8R' / '10RP' (None kalau tidak ada)."""
    t = (text or '').upper()
    if re.search(r'(?<![0-9])(?:10|12|14|16|20)\s*RP(?![0-9A-Z])', t):
        return '10RP'
    if re.search(r'(?<![0-9])[2-9]\s*R(?![0-9A-Z])', t):
        return '8R'
    return None


def code_prefix(code):
    """'MSK-001' -> 'MSK' ; '001' -> ''."""
    m = re.match(r'^\s*([A-Za-z]+)', str(code or ''))
    return m.group(1).upper() if m else ''


def code_digits(code):
    return re.sub(r'\D', '', str(code or ''))


def read_txt_files(folder_path):
    """Hasilkan (filename, stem, content) untuk semua .txt langsung di folder."""
    try:
        names = sorted(os.listdir(folder_path))
    except Exception as e:
        print(f"[ERROR] Gagal membaca folder '{folder_path}': {e}", file=sys.stderr)
        return
    for name in names:
        if not name.lower().endswith('.txt'):
            continue
        content = ''
        path = os.path.join(folder_path, name)
        for enc in ('utf-8', 'utf-8-sig', 'latin-1'):
            try:
                with open(path, 'r', encoding=enc, errors='ignore') as f:
                    content = f.read()
                break
            except Exception:
                content = ''
        yield name, os.path.splitext(name)[0], content


def find_candidate_codes(folder_path, prefer_tag=None):
    """Kumpulkan kandidat kode dari semua .txt di folder.

    PRIORITAS: kode berprefix (MSK-001, BML-005, FRS-003) didahulukan di atas
    angka polos. Angka polos sering cuma catatan ("-4 SUSULAN.txt") yang BUKAN
    kode master, jadi tidak boleh menang atas kode eksplisit.

    Mengembalikan list kode siap pakai, mis. ['MSK-001'] atau ['001'].
    """
    high, low = [], []
    seen = set()

    def add(bucket, code):
        code = str(code or '').strip()
        if code and code not in seen:
            seen.add(code)
            bucket.append(code)

    for _filename, stem, content in read_txt_files(folder_path):
        raw = f"{stem}\n{content}"
        # buang penanda ukuran dulu ("8R" / "10RP")
        text = _SIZE_TOKEN_RE.sub(' ', raw)

        got_explicit = False
        for pref, num in _CODE_TOKEN_RE.findall(text):
            pref = pref.upper()
            if pref in _PSEUDO_PREFIX:
                add(high, num)                  # "KODE= 001"
            else:
                add(high, f"{pref}-{num}")      # "KODE= MSK-001"
            got_explicit = True

        if got_explicit:
            continue

        # fallback: angka polos (prioritas rendah)
        for num in re.findall(r'(?<!\d)(\d{1,3})(?!\d)', text):
            add(low, num)

    return high + low


def master_prefixes(master_folder):
    """Daftar prefix kode yang tersedia di folder master (MSK, BML, ...)."""
    out = set()
    try:
        for fname in os.listdir(master_folder):
            if fname.lower().endswith('.psd'):
                m = re.match(r'^\s*([A-Za-z]{2,4})', fname)
                if m:
                    out.add(m.group(1).upper())
    except Exception:
        pass
    return out


def find_master_file(master_folder, code, prefix=None):
    """Cari .psd di master_folder yang nomornya COCOK EKSAK dengan `code`.

    - 001 == 1 (nol di depan diabaikan)
    - penanda ukuran di nama file (8R / 10RP) diabaikan, supaya kode 8
      tidak salah cocok dengan "MSK-001 8R.psd"
    - kalau `code` punya prefix (MSK-001), nama file WAJIB punya prefix sama.
      Ini mencegah master MSK dipakai untuk kode BML (desainnya beda).
    """
    digits = code_digits(code)
    if not digits:
        return None
    target = int(digits)
    want_prefix = (prefix if prefix is not None else code_prefix(code)).upper()

    try:
        names = sorted(os.listdir(master_folder))
    except Exception as e:
        print(f"[ERROR] Gagal membaca folder master '{master_folder}': {e}", file=sys.stderr)
        return None

    for fname in names:
        if not fname.lower().endswith('.psd'):
            continue
        stem = os.path.splitext(fname)[0]
        clean = _SIZE_TOKEN_RE.sub(' ', stem)
        nums = re.findall(r'\d+', clean)
        if not any(int(n) == target for n in nums):
            continue
        if want_prefix and want_prefix not in clean.upper():
            continue
        return os.path.join(master_folder, fname)
    return None


ONLY_PAREN = re.compile(r"^\s*\(\s*(\d{1,3})\s*\)\s*$")


def get_leading_number(name_without_ext):
    m = re.match(r'\s*(\d+)', name_without_ext)
    if m:
        return m.group(1)
    return None


def normalize_non_numeric(name_without_ext):
    s = name_without_ext.strip()
    s = re.sub(r'\(\d+\)$', '', s).strip()
    return s


def discover_jpg_recursive(folder):
    out = []
    for root, _, files in os.walk(folder):
        for fn in files:
            if fn.lower().endswith(('.jpg', '.jpeg')):
                out.append(os.path.join(root, fn))
    return out


def copy_txt_files_recursive(source_folder, output_folder):
    """Salin semua file .txt dari subfolder ke output folder, mempertahankan struktur folder."""
    for root, dirs, files in os.walk(source_folder):
        for file in files:
            if file.lower().endswith('.txt'):
                src_path = os.path.join(root, file)
                rel_path = os.path.relpath(root, source_folder)
                dest_dir = os.path.join(output_folder, rel_path) if rel_path != '.' else output_folder
                os.makedirs(dest_dir, exist_ok=True)
                dest_path = os.path.join(dest_dir, file)
                try:
                    shutil.copy2(src_path, dest_path)
                    print(f"    - Salin .txt: {os.path.relpath(dest_path, output_folder)}")
                except Exception as e:
                    print(f"    - [ERROR] Gagal salin .txt '{file}': {e}", file=sys.stderr)


def get_relative_path_from_month(pilihan_path):
    """Dapatkan path relatif mulai dari folder bulan hingga parent pilihan."""
    pilihan_path_norm = os.path.normpath(pilihan_path)
    sumber_parent = os.path.dirname(pilihan_path_norm)
    parts = sumber_parent.split(os.sep)
    month_idx = -1
    for i, part in enumerate(parts):
        if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
            month_idx = i
            break
    if month_idx == -1:
        return os.path.basename(sumber_parent)  # fallback
    relative_structure = os.path.join(*parts[month_idx:])
    return relative_structure



def normalize_pilihan_path(pilihan_path):
    """Rapikan argumen path. -> (folder_pilihan, daftar_nama_subfolder | None)

    Menerima juga:
      - path ke file .txt, mis. "...\\3. FOTO TM 8R GRADASI\\KODE= MSK-001.txt"
      - folder pekerjaan langsung, mis. "...\\PILIHAN\\3. FOTO TM 8R GRADASI"
        (tanpa subfolder di dalamnya) -> otomatis dianggap 1 pekerjaan.
    """
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
    has_job_files = any(f.lower().endswith(('.txt', '.jpg', '.jpeg')) for f in entries)

    if not subdirs and has_job_files:
        # p sendiri adalah folder pekerjaan
        return os.path.dirname(p), [os.path.basename(p)]
    return p, None


def main(master_path_primary, pilihan_path, output_base_path, master_path_secondary=None):
    _ = load_config()
    pilihan_path, only_events = normalize_pilihan_path(pilihan_path)
    relative_path = get_relative_path_from_month(pilihan_path)
    final_output_folder = os.path.join(output_base_path, relative_path)
    os.makedirs(final_output_folder, exist_ok=True)

    # Daftar subfolder pekerjaan (PILIHAN level-1)
    event_names = only_events
    if event_names is None:
        try:
            event_names = [n for n in sorted(os.listdir(pilihan_path))
                           if os.path.isdir(os.path.join(pilihan_path, n))]
        except Exception as e:
            print(f"[WARNING] Gagal membaca folder PILIHAN: {e}", file=sys.stderr)
            event_names = []

    # Mirror level-1 folders dari pilihan_path ke output
    try:
        for name in event_names:
            os.makedirs(os.path.join(final_output_folder, name), exist_ok=True)
    except Exception as e:
        print(f"[WARNING] Gagal mirror folder level-1: {e}", file=sys.stderr)

    # Gate COPY_EDIT: fase 1 copy-edit SKIP salin .txt + OKE BASE + shortcut lokal
    # (pindah ke fase 2 / tombol DUPLIKAT PSD). Hanya siapkan template edit + manifest.
    if not COPY_EDIT:
        # Salin .txt dari semua subfolder
        try:
            for name in event_names:
                p = os.path.join(pilihan_path, name)
                if os.path.isdir(p):
                    out_p = os.path.join(final_output_folder, name)
                    copy_txt_files_recursive(p, out_p)
        except Exception as e:
            print(f"[WARNING] Gagal salin .txt dari subfolder: {e}", file=sys.stderr)

    print(f"--- Memulai Proses Manasik ---")
    print(f"Master 1 (10RP): {master_path_primary}")
    print(f"Master 2 (8R): {master_path_secondary}")
    print(f"Pilihan: {pilihan_path}")
    print(f"Output akan disimpan di: {final_output_folder}")

    # PENTING: Gunakan path eksplisit dari argumen
    md_10rp_path = master_path_primary
    md_8r_path = master_path_secondary
    
    # Validasi path dasar
    if not md_10rp_path or not os.path.exists(md_10rp_path):
         print(f"[ERROR] Master Path 10RP tidak valid: {md_10rp_path}", file=sys.stderr)
    
    if not md_8r_path or not os.path.exists(md_8r_path):
         print(f"[ERROR] Master Path 8R tidak valid: {md_8r_path}", file=sys.stderr)

    # ------------------------------------------------------------------
    # Helper: kode .txt terdekat untuk sebuah folder.
    # Folder seperti NONREG / KOLASE yang punya .txt sendiri otomatis jadi
    # "grup" tersendiri dan memakai master sendiri, bukan menumpang induk.
    # ------------------------------------------------------------------
    def _codes_for_dir(dir_path):
        cur = os.path.abspath(dir_path)
        root = os.path.abspath(subfolder_path)
        while True:
            codes = find_candidate_codes(cur, prefer_tag=prefer_tag)
            if codes:
                return codes, cur
            if os.path.normcase(cur) == os.path.normcase(root):
                return [], None
            parent = os.path.dirname(cur)
            if os.path.normcase(parent) == os.path.normcase(cur):
                return [], None
            if not os.path.normcase(parent).startswith(os.path.normcase(root)):
                return [], None
            cur = parent

    def _pick_master(codes):
        for code in codes:
            found = find_master_file(master_folder_to_use, code)
            if found:
                return found, code
        return None, None

    def _out_dir_for(dir_path):
        rel = os.path.relpath(dir_path, subfolder_path)
        if rel in (".", ""):
            return os.path.join(final_output_folder, subfolder_name)
        return os.path.join(final_output_folder, subfolder_name, rel)

    processed = 0
    for subfolder_name in event_names:
        subfolder_path = os.path.join(pilihan_path, subfolder_name)
        if not os.path.isdir(subfolder_path):
            continue
        subfolder_lower = subfolder_name.lower()

        # ---- 1. Baca kode dari .txt (bentuk apa pun: MSK-001 / KODE= MSK-001) ----
        codes = find_candidate_codes(subfolder_path)
        msk_codes = [c for c in codes if code_prefix(c) == 'MSK']
        txt_mentions_manasik = any('MANASIK' in (stem + ' ' + content).upper()
                                   for _fn, stem, content in read_txt_files(subfolder_path))

        # ---- 2. Apakah folder ini pekerjaan MANASIK? ----
        #    Tidak lagi wajib ada kata "manasik" di nama folder:
        #    kode MSK di .txt sudah cukup (mis. "3. FOTO TM 8R GRADASI").
        is_manasik_job = (
            'manasik' in subfolder_lower
            or 'msk' in subfolder_lower
            or bool(msk_codes)
            or txt_mentions_manasik
        )
        if not is_manasik_job:
            souvenir = [c for c in codes if code_prefix(c) in ('BML', 'BMP', 'KML', 'KMP')]
            if souvenir:
                print(f"[SKIP] '{subfolder_name}': kode {', '.join(souvenir)} = desain BERSAMA/KALENDER "
                      f"manasik (bukan master MSK). Butuh master souvenir sendiri.")
            else:
                print(f"[INFO] Lewati '{subfolder_name}': bukan pekerjaan manasik "
                      f"(kode: {codes if codes else '∅'}).")
            continue

        # ---- 3. Tentukan ukuran -> folder master (nama folder, lalu .txt) ----
        size_tag = detect_size_tag(subfolder_name)
        if not size_tag:
            for _fn, stem, content in read_txt_files(subfolder_path):
                size_tag = detect_size_tag(f"{stem} {content}")
                if size_tag:
                    break

        if size_tag == '8R':
            master_folder_to_use, prefer_tag = md_8r_path, '8R'
        else:
            master_folder_to_use = md_10rp_path
            prefer_tag = size_tag or '10RP'
            if size_tag and size_tag != '10RP':
                print(f"[INFO] Ukuran '{size_tag}' tidak punya master sendiri; pakai master 10RP.")

        if not master_folder_to_use or not os.path.exists(master_folder_to_use):
            alt_path = md_8r_path if master_folder_to_use == md_10rp_path else md_10rp_path
            alt_tag = '8R' if master_folder_to_use == md_10rp_path else '10RP'
            if alt_path and os.path.exists(alt_path):
                print(f"[WARNING] Master {prefer_tag} tidak ada/valid; pakai master {alt_tag}.")
                master_folder_to_use, prefer_tag = alt_path, alt_tag
            else:
                print(f"[SCRIPT_ERROR] [ERROR] Folder master untuk {prefer_tag} tidak ditemukan/valid!", file=sys.stderr)
                continue

        print(f"\n--- Memproses: {subfolder_name} (master {prefer_tag}) ---")
        print(f"  - Kandidat kode dari .txt: {codes if codes else '∅'}")
        processed += 1

        ce_jobs = {}          # (master_path, grup_output_dir) -> [tujuan]
        _skipped_total = 0

        for root, dirs, files in os.walk(subfolder_path):
            jpg_files = [f for f in files if f.lower().endswith(('.jpg', '.jpeg'))]
            if not jpg_files:
                continue

            rel_dir = os.path.relpath(root, subfolder_path)
            current_output_dir = _out_dir_for(root)
            os.makedirs(current_output_dir, exist_ok=True)

            codes, group_dir = _codes_for_dir(root)
            if not codes:
                print(f"  [SKIP] '{rel_dir}' - tidak ada .txt kode di folder ini/induknya.")
                continue

            master_file_path, chosen_code = _pick_master(codes)
            if not master_file_path:
                print(f"  [SKIP] '{rel_dir}' - master tidak cocok untuk kode {codes}.")
                continue

            group_out_dir = _out_dir_for(group_dir) if group_dir else current_output_dir
            print(f"  [GRUP] '{rel_dir}' -> kode {chosen_code} ({os.path.basename(master_file_path)})")

            group_ids, seen = [], set()
            for fn in jpg_files:
                name_no_ext = os.path.splitext(fn)[0]
                num = get_leading_number(name_no_ext)
                if num is not None:
                    gid = num
                else:
                    m = ONLY_PAREN.match(name_no_ext)
                    if m:
                        gid = m.group(1)
                    else:
                        gid = normalize_non_numeric(name_no_ext)

                if gid and gid not in seen:
                    seen.add(gid)
                    group_ids.append(gid)

            print(f"    - Folder '{rel_dir if rel_dir != '.' else '[Root]'}': {len(group_ids)} grup.")

            _dest_jobs = []
            _new_jobs = []
            for gid in group_ids:
                out_name = f"{gid}.psd"
                destination_path = os.path.join(current_output_dir, out_name)
                if os.path.exists(destination_path):
                    _skipped_total += 1
                    continue
                if COPY_EDIT:
                    _new_jobs.append(destination_path)
                else:
                    _dest_jobs.append((master_file_path, destination_path))

            if COPY_EDIT:
                if _new_jobs:
                    ce_jobs.setdefault((master_file_path, group_out_dir), []).extend(_new_jobs)
                    print(f"      -> {len(_new_jobs)} tujuan dicatat.")
            else:
                copied, _sk, _fl = _fast_batch_copy(_dest_jobs, workers=4, overwrite=True,
                                                     on_done=lambda d: print(f"      - [OK] {os.path.basename(d)}"),
                                                     on_error=lambda d, e: print(f"      [ERROR] Gagal salin '{os.path.basename(d)}': {e}", file=sys.stderr))
                _skipped_total += _sk
                if copied > 0:
                    print(f"      -> {copied} file disalin.")

        if COPY_EDIT and ce_jobs:
            # Template edit disimpan di <grup>/#MASTER supaya tiap grup
            # (mis. NONREG vs reguler) punya template sendiri.
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

    print(f"\n--- Proses Selesai ({processed} pekerjaan manasik diproses) ---")
    return pilihan_path


if __name__ == "__main__":
    # Flag copy-edit dipisah dari argumen posisional (supaya argv[4]/argv[5] aman)
    _opts = {"mode": "direct", "phase": None, "manifest": None}
    for _a in sys.argv:
        if _a.startswith("--mode="): _opts["mode"] = _a.split("=", 1)[1].lower()
        elif _a.startswith("--phase="): _opts["phase"] = _a.split("=", 1)[1].strip()
        elif _a.startswith("--manifest="): _opts["manifest"] = _a.split("=", 1)[1]
    if any(a.startswith("--") for a in sys.argv):
        sys.argv = [a for a in sys.argv if not a.startswith("--")]
    _ce = _opts["mode"] == "copy-edit"
    if _ce:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import tempfile as _tempfile
        COPY_EDIT = True
        MANIFEST_PATH = _opts["manifest"] or os.path.join(
            _tempfile.gettempdir(), f"bm_copy_edit_{os.getpid()}.json")
        print(f"[MODE] copy-edit fase 1 (manifest: {MANIFEST_PATH})")

    if len(sys.argv) < 3:
        print("[ERROR] Argumen tidak lengkap. Diperlukan: master_path, pilihan_path, output_path, [master_path2], [oke_base_path]", file=sys.stderr)
        sys.exit(1)
    
    # args: script.py master1 pilihan output [master2] [oke]
    master_path = sys.argv[1].strip().strip('"')
    pilihan_path = sys.argv[2].strip().strip('"')
    output_base_path = sys.argv[3].strip().strip('"') if len(sys.argv) > 3 else ""
    master_path_2 = sys.argv[4].strip().strip('"') if len(sys.argv) > 4 else None
    oke_base_path = sys.argv[5].strip().strip('"') if len(sys.argv) > 5 else None

    # BUG FIX: main() dulu dipanggil 2x di sini -> semua langkah (salin .txt, scan,
    # manifest) diulang 2x = proses copy-edit tampak lama. Sekali panggil cukup.
    pilihan_container = main(master_path, pilihan_path, output_base_path, master_path_secondary=master_path_2)

    # Shortcut OKE BASE: SELALU jalan (fase 1 copy-edit tetap buat).
    if oke_base_path and oke_base_path.upper() != "NONE":
        user_name = os.environ.get('BMACHINE_USER_NAME', 'USER')
        container = pilihan_container or pilihan_path
        create_oke_base_links(container, oke_base_path, user_name)
        # Shortcut output lokal: SELALU dibuat (termasuk fase 1).
        final_output_folder = os.path.join(output_base_path, get_relative_path_from_month(container))
        create_shortcuts_in_output_local(final_output_folder, container, oke_base_path, user_name, output_base_path)

    # Copy-Edit standalone (tanpa --phase=1): fase 1 selesai -> langsung fase 2.
    if _ce and _opts["phase"] != "1":
        from _photoshop_utils import run_copy_edit_phase2
        ok = run_copy_edit_phase2(MANIFEST_PATH)
        sys.exit(0 if ok else 1)

