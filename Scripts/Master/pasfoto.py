import os
import re
import sys
import json
import shutil
from pathlib import Path
from typing import Optional, Tuple, Any, Dict, List

# ============================================
# Konfigurasi & Util
# ============================================

ALLOWED_EXTS = (".jpg", ".jpeg")

def natural_sort_key(s: str):
    """Urutan natural: 1, 2, 10 (bukan 1, 10, 2)."""
    return [int(text) if text.isdigit() else text.lower() for text in re.split(r'([0-9]+)', s)]

def get_project_root() -> str:
    """
    Cari root proyek: naik ke atas sampai ketemu file .sln.
    Jika tidak ketemu, fallback ke folder skrip.
    """
    current_dir = os.path.dirname(os.path.abspath(__file__))
    while True:
        try:
            if any(fname.endswith('.sln') for fname in os.listdir(current_dir)):
                return current_dir
        except Exception:
            pass
        parent_dir = os.path.dirname(current_dir)
        if parent_dir == current_dir:
            return os.path.dirname(os.path.abspath(__file__))
        current_dir = parent_dir

def load_config():
    """Membaca file konfigurasi utama (config.json) dari root proyek."""
    project_root = get_project_root()
    config_path = os.path.join(project_root, 'config.json')
    if not os.path.exists(config_path):
        print(f"[ERROR] File konfigurasi utama 'config.json' tidak ditemukan di '{project_root}'.", file=sys.stderr)
        return {}
    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            return json.load(f)
    except Exception as e:
        print(f"[ERROR] Terjadi kesalahan saat memuat 'config.json': {e}", file=sys.stderr)
        return {}

# ============================================
# Loader config.json (struktur PathConfigs)
# ============================================

def load_paths_from_config():
    """
    Baca config.json (struktur PathConfigs: [ {Name, Paths[]} ]).
    Ambil:
      - master_pasfoto_path dari Name:
            prioritas: "MD PAS FOTO" | "MD PASFOTO" | "MD_PASFOTO" | "MD-PASFOTO"
            fallback : "PAS FOTO" | "PAS_FOTO" | "PAS-FOTO"
      - output_base_path dari Name:
            prioritas: "#PANCEN" | "PANCEN" | "OUTPUT_PANCEN"
    Hormati env:
      MASTER_PASFOTO_PATH, PANCEN_OUTPUT_BASE
    """
    # Env override
    env_master = os.environ.get("MASTER_PASFOTO_PATH")
    env_output = os.environ.get("PANCEN_OUTPUT_BASE")
    env_master = env_master if (env_master and os.path.isdir(env_master)) else None
    env_output = env_output if (env_output and os.path.isdir(env_output)) else None

    # Baca config.json
    cfg_path = os.path.join(get_project_root(), "config.json")
    cfg = None
    if os.path.isfile(cfg_path):
        try:
            with open(cfg_path, "r", encoding="utf-8") as f:
                cfg = json.load(f)
        except Exception:
            cfg = None

    if cfg is None and not (env_master or env_output):
        return None, None

    def _first_existing(paths):
        for p in paths or []:
            if isinstance(p, str) and os.path.isdir(p):
                return p
        return None

    found_master = None
    found_output = None

    if cfg is not None:
        path_configs = cfg.get("PathConfigs") or []
        # Normalisasi: Name lower -> list Paths
        name_to_paths: Dict[str, List[str]] = {}
        for entry in path_configs:
            name = (entry.get("Name") or "").strip().lower()
            paths = entry.get("Paths") or []
            name_to_paths.setdefault(name, []).extend(paths)

        # Kandidat master
        master_names_primary = {"md pas foto", "md pasfoto", "md_pasfoto", "md-pasfoto"}
        master_names_fallback = {"pas foto", "pas_foto", "pas-foto"}

        # Kandidat output
        output_names = {"#pancen", "pancen", "output_pancen"}

        # Master: prioritas primary → fallback
        for nm in master_names_primary:
            if nm in name_to_paths and not found_master:
                found_master = _first_existing(name_to_paths[nm])
        if not found_master:
            for nm in master_names_fallback:
                if nm in name_to_paths:
                    found_master = _first_existing(name_to_paths[nm])
                    if found_master:
                        break

        # Output
        for nm in output_names:
            if nm in name_to_paths and not found_output:
                found_output = _first_existing(name_to_paths[nm])

    # Env override terakhir
    if env_master:
        found_master = env_master
    if env_output:
        found_output = env_output

    return found_master, found_output

# ============================================
# PSD Template Finder
# ============================================

def find_psd_for_code(master_pasfoto_path: str, layer_code: str):
    """
    Cari PSD sesuai kode (mis. PFM-06, PFM 06, PFM06) di folder master_pasfoto_path.
    Normalisasi angka: 06 = 006 = 6 (integer comparison).
    File PSD: PFM-001.psd, PFM-002.psd, ..., PFM-010.psd
    """
    if not os.path.isdir(master_pasfoto_path):
        return None

    try:
        files = os.listdir(master_pasfoto_path)
    except Exception:
        return None

    # Ekstrak prefix dan angka dari kode: "PFM 06" → prefix="PFM", num_int=6
    code_clean = re.sub(r'[\s\-]+', '', layer_code).upper()
    code_match = re.match(r'(PF[MB])(\d+)', code_clean, re.IGNORECASE)
    if not code_match:
        return None
    
    target_prefix = code_match.group(1).upper()  # "PFM" atau "PFB"
    target_num = int(code_match.group(2))        # 6 (dari "06")

    candidates = []
    
    for f in files:
        if not f.lower().endswith(".psd"):
            continue
        
        fname_no_ext = os.path.splitext(f)[0]
        
        # Ekstrak prefix dan angka dari nama file: "PFM-006" → prefix="PFM", num=6
        fname_match = re.search(r'(PF[MB])[\s\-]*(\d+)', fname_no_ext, re.IGNORECASE)
        if not fname_match:
            continue
        
        file_prefix = fname_match.group(1).upper()
        file_num = int(fname_match.group(2))
        
        # Match: prefix sama DAN angka sama (integer comparison)
        if file_prefix == target_prefix and file_num == target_num:
            candidates.append(f)

    if candidates:
        # Jika ada multiple match, ambil yang nama terpendek (paling bersih)
        candidates.sort(key=len)
        return os.path.join(master_pasfoto_path, candidates[0])

    return None

# ============================================
# TXT Parser
# ============================================

def read_txt_get_code_and_flag(txt_path: str):
    """
    Baca file .txt (utf-8 fallback utf-16) → ambil:
      - layer_code (regex PFM-\\d+)
      - flag 'pakai nama sekolah' (boolean)
    """
    content = ""
    try:
        with open(txt_path, "r", encoding="utf-8", errors="ignore") as f:
            content = f.read()
    except UnicodeDecodeError:
        with open(txt_path, "r", encoding="utf-16", errors="ignore") as f:
            content = f.read()
    except Exception:
        content = ""

    match = re.search(r"PF[MB][\s-]*\d+", content, re.IGNORECASE) or re.search(
        r"PF[MB][\s-]*\d+", os.path.basename(txt_path), re.IGNORECASE
    )
    # Normalisasi: "PFM 06" / "PFM-06" → "PFM06"
    layer_code = re.sub(r'[\s\-]+', '', match.group(0)).upper() if match else None

    combined_text_source = (content + " " + os.path.basename(txt_path)).lower()
    show_ribbon = "pakai nama sekolah" in combined_text_source

    return layer_code, show_ribbon

# ============================================
# Penamaan File Output
# ============================================

def make_safe_name(name: str) -> str:
    # ganti karakter terlarang di Windows
    name = re.sub(r'[<>:"/\\|?*\r\n\t]', "_", name)
    # rapikan spasi
    name = re.sub(r"\s+", " ", name).strip()
    return name or "unnamed"

def compute_dest_filename(base_name: str, idx: int) -> str:
    # hilangkan suffix duplikat: "1(1)", "1 (2)", "_(3)", " (4)"
    base_clean = re.sub(r'[\s_\-]*\(\d+\)$', "", base_name).strip()
    if not base_clean:
        return f"{idx}.psd"
    if base_clean.isdigit():
        return f"{idx}.psd"
    return f"{make_safe_name(base_clean)}.psd"

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

# ============================================
# LOGIKA UTAMA
# ============================================

def main(master_pasfoto_path: str, pilihan_path: str, output_base_path: str):
    output_folder = None
    print("--- Memulai Proses Pas Foto (MODE TANPA PHOTOSHOP) ---")
    print(f"[RESOLVE] MASTER : {master_pasfoto_path}")
    print(f"[RESOLVE] PILIHAN: {pilihan_path}")
    print(f"[RESOLVE] OUTPUT : {output_base_path}")

    # Validasi input path
    if not os.path.isdir(master_pasfoto_path):
        print(f"[ERROR] Folder master '{master_pasfoto_path}' tidak ditemukan atau bukan direktori.", file=sys.stderr)
        return
    if not os.path.isdir(pilihan_path):
        print(f"[ERROR] Folder pilihan '{pilihan_path}' tidak ditemukan atau bukan direktori.", file=sys.stderr)
        return
    if not os.path.isdir(output_base_path):
        print(f"[ERROR] Folder output base '{output_base_path}' tidak ditemukan atau bukan direktori.", file=sys.stderr)
        return

    output_folder = None
    # Output global: <output_base>/<relative_structure_from_month>
    try:
        relative_structure = get_relative_path_from_month(pilihan_path)
        output_folder = os.path.join(output_base_path, relative_structure)
        os.makedirs(output_folder, exist_ok=True)

        # Mirror level-1 folders dari pilihan_path ke output_folder
        try:
            for name in os.listdir(pilihan_path):
                p = os.path.join(pilihan_path, name)
                if os.path.isdir(p):
                    os.makedirs(os.path.join(output_folder, name), exist_ok=True)
        except Exception as e:
            print(f"[WARNING] Gagal mirror folder level-1: {e}", file=sys.stderr)

        # Salin .txt dari semua subfolder
        try:
            for name in os.listdir(pilihan_path):
                p = os.path.join(pilihan_path, name)
                if os.path.isdir(p):
                    out_p = os.path.join(output_folder, name)
                    copy_txt_files_recursive(p, out_p)
        except Exception as e:
            print(f"[WARNING] Gagal salin .txt dari subfolder: {e}", file=sys.stderr)

        print(f"Output akan disimpan di: {output_folder}")
    except Exception as e:
        print(f"[ERROR] Gagal membuat folder output: {e}", file=sys.stderr)
        return None

    # --- LOGIKA BARU: Proses semua subfolder jika tidak ada yang spesifik ---
    try:
        all_subfolders = [d for d in os.listdir(pilihan_path) if os.path.isdir(os.path.join(pilihan_path, d))]
    except Exception as e:
        print(f"[ERROR] Terjadi kesalahan saat membaca folder pilihan '{pilihan_path}': {e}", file=sys.stderr)
        return

    # 1. Cari subfolder yang mengandung 'pas foto'
    pasfoto_folders = [d for d in all_subfolders if re.search(r"pas\s*(foto|photo)", d.lower())]

    # 2. Jika tidak ada, proses semua subfolder yang ada
    folders_to_process = pasfoto_folders
    if not pasfoto_folders:
        print("[INFO] Tidak ada folder spesifik 'pas foto' ditemukan. Memproses semua subfolder yang ada.")
        folders_to_process = all_subfolders

    if not folders_to_process:
        print("[INFO] Tidak ada subfolder yang ditemukan untuk diproses.")
        print("\n--- Proses Selesai ---")
        return

    processed_count = 0
    for item in sorted(folders_to_process, key=natural_sort_key):
        item_path = os.path.join(pilihan_path, item)

        print(f"\n--- Memproses: {item} ---")
        processed_count += 1

        # Folder output untuk item ini
        item_output_folder = os.path.join(output_folder, item)
        os.makedirs(item_output_folder, exist_ok=True)

        txt_files = [f for f in os.listdir(item_path) if f.lower().endswith(".txt")]
        if not txt_files:
            print(f"[WARNING] Tidak ada file .txt ditemukan di dalam '{item}'. Dilewati.")
            continue

        layer_code = None
        show_ribbon = False
        for txt_file in sorted(txt_files, key=natural_sort_key):
            code, ribbon = read_txt_get_code_and_flag(os.path.join(item_path, txt_file))
            if code:
                layer_code = code
                show_ribbon = ribbon
                print(f"  - Kode ditemukan: {layer_code} (dari file '{txt_file}')")
                break

        if not layer_code:
            print(f"[WARNING] Tidak ada kode format 'PFM-XXX' ditemukan pada .txt di '{item}'. Dilewati.")
            continue

        # Temukan PSD template
        psd_template_path = find_psd_for_code(master_pasfoto_path, layer_code)
        if not psd_template_path:
            print(f"[ERROR] PSD untuk kode '{layer_code}' tidak ditemukan di '{master_pasfoto_path}'. Dilewati.", file=sys.stderr)
            continue

        print(f"  - Template PSD: {os.path.basename(psd_template_path)}")

        # Kumpulkan JPG/JPEG sumber (urutan natural)
        source_images = [
            f for f in os.listdir(item_path)
            if f.lower().endswith(ALLOWED_EXTS)
            and not f.startswith(".")
            and not f.startswith("._")
        ]
        source_images.sort(key=natural_sort_key)

        if not source_images:
            print("  - [INFO] Tidak ada file JPG/JPEG sumber. Akan tetap menyalin satu PSD sebagai '1.psd'.")
            dest_path = os.path.join(item_output_folder, "1.psd")
            try:
                shutil.copy2(psd_template_path, dest_path)
                print("    - [SUCCESS] Membuat 1.psd")
            except Exception as e:
                print(f"    - [ERROR] Gagal menyalin PSD: {e}", file=sys.stderr)
            continue

        # Duplikasi PSD → penamaan cerdas
        print(f"  - Ditemukan {len(source_images)} file gambar. Memulai duplikasi & rename.")
        for idx, img_file in enumerate(source_images, start=1):
            base_name, _ = os.path.splitext(img_file)
            dest_filename = compute_dest_filename(base_name, idx)
            dest_path = os.path.join(item_output_folder, dest_filename)
            if os.path.exists(dest_path):
                print(f"    - SKIP_EXISTING: '{dest_filename}' sudah ada.")
                continue

            try:
                shutil.copy2(psd_template_path, dest_path)
                total = len(source_images)
                print(f"    - [OK] {idx}/{total} {os.path.basename(dest_path)}")
            except Exception as e:
                print(f"    - [ERROR] Gagal menyalin ke '{os.path.basename(dest_path)}': {e}", file=sys.stderr)

    if processed_count == 0:
        print("[INFO] Tidak ada folder yang cocok dengan kriteria yang ditemukan untuk diproses.")

    print("\n--- Proses Selesai ---")
    return output_folder

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

# ============================================
# CLI
# ============================================

def resolve_paths_from_cli(argv: List[str]) -> Tuple[Optional[str], Optional[str], Optional[str], Optional[str]]:
    """
    Mode argumen:
      1) Empat argumen: pasfoto.py "<MASTER>" "<PILIHAN>" "<OUTPUT>" "<OKE_BASE>"
    """
    if len(argv) >= 5:
        master, pilihan, output, oke_base = argv[1], argv[2], argv[3], argv[4]
        return master, pilihan, output, oke_base

    return None, None, None, None

if __name__ == "__main__":
    # Hindari error unicode panah di Windows console lama
    if os.name == "nt":
        try:
            sys.stdout.reconfigure(encoding="utf-8")
            sys.stderr.reconfigure(encoding="utf-8")
        except Exception:
            pass

    master_pasfoto_path, pilihan_path, output_base_path, oke_base_path = resolve_paths_from_cli(sys.argv)

    if not all([master_pasfoto_path, pilihan_path, output_base_path, oke_base_path]):
        print("[ERROR] Argumen tidak lengkap. Diperlukan: master_path, pilihan_path, output_path, oke_base_path", file=sys.stderr)
        sys.exit(1)

    output_folder = main(master_pasfoto_path, pilihan_path, output_base_path)

    # Panggil fungsi baru untuk membuat link di OKE BASE
    config_data = load_config()
    if not config_data: config_data = {}
    user_name = os.environ.get('BMACHINE_USER_NAME', config_data.get('UserName', 'USER'))
    create_oke_base_links(pilihan_path, oke_base_path, user_name)
    # Tambahkan shortcut di output lokal
    if output_folder is not None:
        create_shortcuts_in_output_local(output_folder, pilihan_path, oke_base_path, user_name, output_base_path)