# profesi.py — PROFESI + SPORTY + OKE BASE (.lnk)
import os
import sys
import json
import shutil
import traceback
from collections import defaultdict
import re
import base64

ALLOWED_EXTS = (".jpg", ".jpeg")
# Regex diperluas: KELAS..., GROUP..., KELOMPOK..., atau folder berawalan angka (misal "1. FOTO")
CLASS_OR_GROUP_REGEX = re.compile(r'^(?:(?:KELAS|KLS|GROUP|KELOMPOK)|(?:\d+[\s._-]))', re.IGNORECASE)
PF_CODE_REGEX = re.compile(r'^(pf[mb])[-_\s]*([0-9]{1,3}[a-zA-Z]?)$', re.IGNORECASE)

def is_class_or_group(name: str) -> bool:
    return bool(CLASS_OR_GROUP_REGEX.match((name or "").strip()))

def contains_kw(text: str, kw: str) -> bool:
    return kw.lower() in (text or "").lower()

def _pad_digits_keep_suffix(code: str) -> str:
    m = re.match(r'^(\d+)([a-zA-Z]?)$', code)
    if not m:
        return code
    digits, suf = m.groups()
    return digits.zfill(3) + suf.lower()

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

def generate_pf_variants(label: str):
    """Hasilkan beberapa varian kunci untuk label PFM/PFB.
    Mencakup format dengan spasi, tanpa pemisah, '-' dan '_', serta zero-padding 3 digit.
    """
    s = (label or "").strip()
    m = PF_CODE_REGEX.match(s)
    if not m:
        return [s.lower()]
    pfx = m.group(1).lower()
    code_raw = m.group(2)
    code = _pad_digits_keep_suffix(code_raw)
    return [
        f"{pfx}-{code}",
        f"{pfx}_{code}",
        f"{pfx}{code}",
        f"{pfx} {code}",
    ]

def try_resolve_mapping(label: str, mapping: dict):
    for k in generate_pf_variants(label):
        if k in mapping:
            return mapping[k]
    # fallback: langsung pakai label bila tidak ada
    return None

def try_find_master_key(label: str, master_dict: dict):
    for k in generate_pf_variants(label):
        if k in master_dict:
            return k
    return None

# ---------- Project config ----------
def get_project_root():
    current_dir = os.path.dirname(os.path.abspath(__file__))
    while True:
        if any(fname.endswith('.sln') for fname in os.listdir(current_dir)):
            return current_dir
        parent_dir = os.path.dirname(current_dir)
        if parent_dir == current_dir:
            return os.path.dirname(os.path.abspath(__file__))
        current_dir = parent_dir

def load_config():
    """Muat config.json dari beberapa lokasi yang mungkin di lingkungan rilis.

    Urutan pencarian:
    - Folder yang sama dengan skrip Python ini
    - Parent folder dari skrip (root aplikasi rilis biasanya di sini)
    - Current working directory
    - Root proyek yang terdeteksi via .sln (saat pengembangan)
    """
    try:
        script_dir = os.path.dirname(os.path.abspath(__file__))
        candidates = [
            os.path.join(script_dir, 'config.json'),
            os.path.join(os.path.dirname(script_dir), 'config.json'),
            os.path.join(os.getcwd(), 'config.json'),
        ]

        # Tambahkan root proyek bila ada .sln (mode dev)
        try:
            project_root = get_project_root()
            candidates.append(os.path.join(project_root, 'config.json'))
        except Exception:
            pass

        for path in candidates:
            if os.path.exists(path):
                with open(path, 'r', encoding='utf-8') as f:
                    return json.load(f)
    except Exception:
        pass
    return {}

# ---------- Parse filename ----------
def get_profession_from_filename(filename):
    """
    Return (label, base) from filename.
    e.g. 'pilot 2(1).jpg' -> ('pilot','2'); 'tni 12 single.jpeg' -> ('tni','12SINGLE'); '15.jpg' -> (None,'15')
    """
    base_noext = os.path.splitext(filename)[0].strip()

    m = re.match(r'^(.*?)(\d+)\s*\(\d+\)\s*(.*?)$', base_noext, flags=re.IGNORECASE)
    if m:
        left, base, right = m.groups()
        label = (left.strip() or right.strip()) or None
        return (label, base)

    m = re.match(r'^(.*?)(\d+)\s*single\s*(.*?)$', base_noext, flags=re.IGNORECASE)
    if m:
        left, base, right = m.groups()
        label = (left.strip() or right.strip()) or None
        return (label, f"{base}SINGLE")

    m = re.match(r'^(.*?)(\d+)\s*(.*?)$', base_noext, flags=re.IGNORECASE)
    if m:
        left, base, right = m.groups()
        label = (left.strip() or right.strip()) or None
        return (label, base)

    return (None, None)

# ---------- Collect sources ----------
def get_files_to_process(pilihan_path, files_to_reprocess=None):
    if files_to_reprocess:
        return [f for f in files_to_reprocess if os.path.exists(f)]
    all_files = []
    for root, _, filenames in os.walk(pilihan_path):
        for filename in filenames:
            if filename.lower().endswith(ALLOWED_EXTS):
                all_files.append(os.path.join(root, filename))
    return all_files

# ---------- Category detect ----------
def detect_category_from_parts(parts_dir):
    idx_sporty = [i for i, c in enumerate(parts_dir) if 'sporty' in c.lower()]
    idx_profesi = [i for i, c in enumerate(parts_dir) if 'profesi' in c.lower()]
    last_sporty = idx_sporty[-1] if idx_sporty else None
    last_profesi = idx_profesi[-1] if idx_profesi else None
    if last_sporty is not None and last_profesi is not None:
        return 'sporty' if last_sporty > last_profesi else 'profesi'
    if last_sporty is not None:
        return 'sporty'
    if last_profesi is not None:
        return 'profesi'
    return None

# ---------- OKE BASE helpers ----------
def create_shortcut(link_path, target_path):
    """Buat file shortcut .lnk (Windows)."""
    import subprocess
    import tempfile

    vbs_script = f"""
    Set oWS = WScript.CreateObject("WScript.Shell")
    sLinkFile = "{link_path}"
    Set oLink = oWS.CreateShortcut(sLinkFile)
    oLink.TargetPath = "{target_path}"
    oLink.Save
    """
    try:
        with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.vbs') as f:
            f.write(vbs_script)
            temp_vbs_path = f.name
        subprocess.run(["cscript", "//Nologo", temp_vbs_path], check=True)
        os.remove(temp_vbs_path)
        return True
    except Exception:
        return False

def create_shortcuts_in_output_local(final_event_folder, pilihan_path, oke_base_path, user_name, output_path):
    """Membuat shortcut di output lokal ke folder input dan ke OKE BASE jika ada."""
    print("\n--- Membuat Shortcut di Output Lokal ---")
    try:
        pilihan_path_norm = os.path.normpath(pilihan_path)
        sumber_parent = os.path.dirname(pilihan_path_norm)

        # Shortcut ke folder event di root output base
        event_folder_name = os.path.basename(final_event_folder)
        event_shortcut_path = os.path.join(output_path, f"{event_folder_name}.lnk")
        if not os.path.exists(event_shortcut_path):
            print(f"  - Mencoba membuat shortcut ke event folder: {event_folder_name}...")
            ok = create_shortcut(event_shortcut_path, final_event_folder)
            print(f"    -> {'OK' if ok else 'GAGAL'}")
        else:
            print(f"  - Shortcut ke event folder '{event_folder_name}.lnk' sudah ada, dilewati.")

        # Shortcut ke folder di input
        for item in os.listdir(sumber_parent):
            src_item = os.path.join(sumber_parent, item)
            if os.path.isdir(src_item):
                link_item = os.path.join(final_event_folder, f"{item}.lnk")
                if not os.path.exists(link_item):
                    ok = create_shortcut(link_item, src_item)
                    print(f"  - Shortcut ke input {item}: {'OK' if ok else 'GAGAL'}")
                else:
                    print(f"  - Shortcut ke input {item}: sudah ada")

        # Shortcut ke OKE BASE jika ada
        if oke_base_path and os.path.exists(oke_base_path):
            parts = sumber_parent.split(os.sep)
            month_idx = -1
            for i, part in enumerate(parts):
                if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
                    month_idx = i
                    break
            if month_idx != -1:
                relative_structure = os.path.join(*parts[month_idx:])
                oke_dest = os.path.join(oke_base_path, relative_structure)
                oke_folder_name = f"#OKE {user_name.upper()}"
                oke_user_folder = os.path.join(oke_dest, oke_folder_name)
                balik = os.path.join(final_event_folder, f"{oke_folder_name}.lnk")
                if not os.path.exists(balik):
                    ok = create_shortcut(balik, oke_user_folder)
                    print(f"  - Shortcut ke OKE BASE: {'OK' if ok else 'GAGAL'}")
                else:
                    print("  - Shortcut ke OKE BASE sudah ada.")
    except Exception as e:
        print(f"[ERROR] Shortcut di output lokal: {e}", file=sys.stderr)


def create_oke_base_links(pilihan_path, oke_base_path, user_name):
    """Buat struktur OKE BASE + shortcut .lnk dari folder induk PILIHAN."""
    print("\n--- Membuat OKE BASE & Shortcut ---")
    if not oke_base_path or not os.path.exists(oke_base_path):
        print("[INFO] OKE BASE dilewati (path kosong / tidak ada).")
        return

    try:
        pilihan_path_norm = os.path.normpath(pilihan_path)
        sumber_parent = os.path.dirname(pilihan_path_norm)

        # Cari path mulai dari folder bulan (misal "02 AGUSTUS 2025")
        parts = sumber_parent.split(os.sep)
        month_idx = -1
        for i, part in enumerate(parts):
            if re.match(r'^\d{2}\s+\w+\s+\d{4}$', part, re.IGNORECASE):
                month_idx = i
                break
        if month_idx == -1:
            print("[WARNING] Folder bulan tidak ditemukan. OKE BASE dilewati.")
            return

        relative_structure = os.path.join(*parts[month_idx:])
        oke_dest = os.path.join(oke_base_path, relative_structure)
        os.makedirs(oke_dest, exist_ok=True)
        print(f"OKE BASE target: {oke_dest}")

        # Gunakan format: #OKE NAMA_USER (huruf besar)
        oke_folder_name = f"#OKE {user_name.upper()}"
        oke_user_folder = os.path.join(oke_dest, oke_folder_name)
        os.makedirs(oke_user_folder, exist_ok=True)

        # Shortcut setiap folder di parent
        for item in os.listdir(sumber_parent):
            src_item = os.path.join(sumber_parent, item)
            if os.path.isdir(src_item):
                link_item = os.path.join(oke_dest, f"{item}.lnk")
                if not os.path.exists(link_item):
                    ok = create_shortcut(link_item, src_item)
                    print(f"  - Shortcut {item}: {'OK' if ok else 'GAGAL'}")
                else:
                    print(f"  - Shortcut {item}: sudah ada")

        # Shortcut balik dari sumber ke #OKE
        balik = os.path.join(sumber_parent, f"{oke_folder_name}.lnk")
        if not os.path.exists(balik):
            ok = create_shortcut(balik, oke_user_folder)
            print(f"  - Shortcut balik: {'OK' if ok else 'GAGAL'}")
        else:
            print("  - Shortcut balik sudah ada.")

    except Exception as e:
        print(f"[ERROR] OKE BASE: {e}", file=sys.stderr)

# ---------- Core ----------
def process_images(master_path_profesi, master_path_sporty, pilihan_path, output_path, config_data,
                   files_to_reprocess=None, mappings_b64=None, oke_base_path=None):
    print("--- Memulai Jurus: PROFESI | SPORTY ---")

    if not all([master_path_profesi, pilihan_path, output_path]):
        print("[ERROR] Argumen tidak lengkap (butuh master_profesi, pilihan, output).", file=sys.stderr)
        return
    if not os.path.exists(master_path_profesi):
        print(f"[ERROR] Master PROFESI tidak ditemukan: {master_path_profesi}", file=sys.stderr); return
    if not os.path.exists(pilihan_path):
        print(f"[ERROR] PILIHAN tidak ditemukan: {pilihan_path}", file=sys.stderr); return
    if not os.path.exists(output_path):
        print(f"[ERROR] OUTPUT tidak ditemukan: {output_path}", file=sys.stderr); return
    master_sporty_exists = bool(master_path_sporty and os.path.exists(master_path_sporty))
    if not master_sporty_exists:
        print("[WARN] Master SPORTY tidak diberikan/ada. File 'sporty' akan di-skip bila perlu.", file=sys.stderr)

    # Mapping dari config (dua kunci) + override Base64
    profession_mappings = {}
    for key in ("ProfessionMappings", "professionMappings"):
        src_map = config_data.get(key) or {}
        if isinstance(src_map, dict):
            profession_mappings.update({k.lower(): v for k, v in src_map.items()})
    if mappings_b64:
        try:
            decoded = base64.b64decode(mappings_b64).decode('utf-8')
            src = json.loads(decoded)
            if isinstance(src, dict):
                profession_mappings.update({k.lower(): v for k, v in src.items()})
            print("[INFO] Mapping ditimpa dari argumen Base64.")
        except Exception as e:
            print(f"[WARNING] Gagal membaca mapping Base64: {e}", file=sys.stderr)

    # Event folder
    relative_structure = get_relative_path_from_month(pilihan_path)
    final_event_folder = os.path.join(output_path, relative_structure)
    os.makedirs(final_event_folder, exist_ok=True)

    # Mirror level-1
    try:
        for name in os.listdir(pilihan_path):
            p = os.path.join(pilihan_path, name)
            if os.path.isdir(p):
                os.makedirs(os.path.join(final_event_folder, name), exist_ok=True)
    except Exception:
        pass

    # Pre-create kelas/grup/kelompok di dalam folder yang mengandung 'profesi' atau 'sporty'
    def precreate_tag(tag: str):
        try:
            for name in os.listdir(pilihan_path):
                src_lvl1 = os.path.join(pilihan_path, name)
                if not (os.path.isdir(src_lvl1) and contains_kw(name, tag)):
                    continue
                out_lvl1 = os.path.join(final_event_folder, name)
                os.makedirs(out_lvl1, exist_ok=True)
                for child in os.scandir(src_lvl1):
                    if child.is_dir() and is_class_or_group(child.name):
                        os.makedirs(os.path.join(out_lvl1, child.name), exist_ok=True)
        except Exception as e:
            print(f"[WARNING] Precreate {tag} gagal: {e}", file=sys.stderr)
    precreate_tag('profesi')
    precreate_tag('sporty')

    # Index master
    master_files_profesi = {}
    for f in os.listdir(master_path_profesi):
        stem, ext = os.path.splitext(f)
        if ext:
            master_files_profesi[stem.lower()] = f

    master_files_sporty = {}
    if master_sporty_exists:
        for f in os.listdir(master_path_sporty):
            stem, ext = os.path.splitext(f)
            if ext:
                master_files_sporty[stem.lower()] = f

    files = get_files_to_process(pilihan_path, files_to_reprocess)
    if not files:
        print("[INFO] Tidak ada file JPG/JPEG ditemukan.")
        print(f"SUMMARY_JSON:{json.dumps({})}")
        # Tetap jalankan OKE BASE jika diminta
        if oke_base_path:
            user_name = os.environ.get('BMACHINE_USER_NAME', config_data.get('UserName', 'USER'))
            create_oke_base_links(pilihan_path, oke_base_path, user_name)
        return

    processed_bases = set()
    summary_counts = defaultdict(int)
    unmatched, errors = [], []

    # --- NEW LOGIC: Deep Walk with Smart Folder Detection ---
    for root, dirs, files in os.walk(pilihan_path):
        jpg_files = [f for f in files if f.lower().endswith(ALLOWED_EXTS)]
        if not jpg_files:
            continue

        # 1. Tentukan konteks folder
        rel_dir = os.path.relpath(root, pilihan_path)
        parts_dir = [] if rel_dir in (".", "") else rel_dir.split(os.sep)
        
        current_folder_name = os.path.basename(root)
        
        # 2. Cek apakah folder ini sendiri adalah NAMA PROFESI?
        #    Strategi: Cek apakah nama folder cocok dengan salah satu Master Key (fuzzy/exact).
        folder_master_key = None
        
        # Cek di Profesi
        k_prof = try_find_master_key(current_folder_name, master_files_profesi)
        if k_prof:
            folder_master_key = (k_prof, 'profesi')
        
        # Cek di Sporty (jika belum ketemu & ada master sporty)
        if not folder_master_key and master_sporty_exists:
            k_sport = try_find_master_key(current_folder_name, master_files_sporty)
            if k_sport:
                folder_master_key = (k_sport, 'sporty')

        # 3. Siapkan Folder Output (Mirroring Partial - AGGRESSIVE FLAT)
        #    Strategi:
        #    - Jika file ada di dalam subfolder (Depth >= 2), ratakan semua ke folder Level 1 (Group/Kelas).
        #    - Jika file ada di Level 1 (Depth == 1):
        #        - Jika nama folder cocok dengan format Group/Kelas (Regex), pertahankan (Level 1).
        #        - Jika folder biasa (misal folder profesi BRIMOB), ratakan ke Root.
        
        target_rel_dir = rel_dir
        
        if rel_dir != "." and rel_dir != "":
            parts = rel_dir.split(os.sep)
            if len(parts) >= 2:
                # Kasus: 1. FOTO.../BRIMOB -> output ke 1. FOTO...
                target_rel_dir = parts[0]
            elif len(parts) == 1:
                # Kasus: 1. FOTO... (Cocok Regex) -> Tetap 1. FOTO...
                # Kasus: BRIMOB (Tidak Cocok) -> Ratakan ke Root (".")
                if is_class_or_group(parts[0]):
                    target_rel_dir = parts[0]
                else:
                    target_rel_dir = "."
        
        if target_rel_dir == "." or target_rel_dir == "":
            current_output_dir = final_event_folder
        else:
            current_output_dir = os.path.join(final_event_folder, target_rel_dir)
            
        os.makedirs(current_output_dir, exist_ok=True)

        print(f"\n[FOLDER] {rel_dir} -> [OUTPUT] {target_rel_dir} (GroupMatch: {'YES' if len(parts)>0 and is_class_or_group(parts[0]) else 'NO'})")

        # 4. Proses File
        for filename in jpg_files:
            full_path = os.path.join(root, filename)
            try:
                # Ambil base name (nomor) & label dari filename (untuk fallback)
                label_from_name, base_name = get_profession_from_filename(filename)
                
                # Logic Penentuan Master:
                # Prioritas 1: Label dari Folder
                # Prioritas 2: Label dari Filename
                # Prioritas 3: Label dari Path Parent (legacy, e.g. parent folder name)
                
                final_master_key = None
                category_mode = 'profesi' # default
                
                # A. Cek Master dari Folder
                if folder_master_key:
                    final_master_key = folder_master_key[0]
                    category_mode = folder_master_key[1]
                
                # B. Cek Master dari Filename (Override/Fallback)
                #    Jika folder master TIDAK ketemu, KITA WAJIB cari di filename.
                #    TAPI, jika folder master KETEMU, apakah filename boleh override?
                #    Idealnya: Folder menang. Kecuali user taruh "dokter.jpg" di folder "PILOT"? 
                #    Assumption: Folder = Strongest Signal for bulk organization.
                
                if not final_master_key and label_from_name:
                    # Coba cari master dari nama file
                    mk = try_find_master_key(label_from_name, master_files_profesi)
                    if mk: 
                        final_master_key = mk
                        category_mode = 'profesi'
                    elif master_sporty_exists:
                        mk_sport = try_find_master_key(label_from_name, master_files_sporty)
                        if mk_sport:
                            final_master_key = mk_sport
                            category_mode = 'sporty'

                # C. Fallback Terakhir: Cek mapping / parent path (Existing Logic parts)
                if not final_master_key:
                     # Coba logika lama parsing path
                     # ... (Sederhanakan: kita anggap kalau sudah gagal A dan B, ya gagal)
                     pass

                if not final_master_key:
                     vars_hint = ", ".join(generate_pf_variants(label_from_name or current_folder_name))
                     print(f"  [SKIP] '{filename}' -> Master tidak ditemukan (Folder: '{current_folder_name}', File: '{label_from_name}')", file=sys.stderr)
                     unmatched.append(f"'{filename}' di '{rel_dir}'")
                     continue

                # Dapatkan file master
                master_dict = master_files_profesi if category_mode == 'profesi' else master_files_sporty
                master_root = master_path_profesi if category_mode == 'profesi' else master_path_sporty
                
                file_master_name = master_dict[final_master_key]
                file_master_path = os.path.join(master_root, file_master_name)
                master_ext = os.path.splitext(file_master_name)[1]

                # Tentukan Nama Output
                # Gunakan base_name original (1.jpg -> 1.psd, pilot 1.jpg -> 1.psd)
                # Jika base_name kosong (misal "pilot.jpg"), pakai nama file asli tanpa ext
                tgt_name = base_name if base_name else os.path.splitext(filename)[0]
                
                tujuan_path = os.path.join(current_output_dir, f"{tgt_name}{master_ext}")
                
                if os.path.exists(tujuan_path):
                     print(f"  [SKIP] '{tgt_name}' sudah ada.")
                     continue
                
                shutil.copy2(file_master_path, tujuan_path)
                summary_counts[os.path.splitext(file_master_name)[0]] += 1
                print(f"  [OK] '{filename}' -> '{tgt_name}{master_ext}' ({category_mode.upper()}: {file_master_name})")

            except Exception as e:
                errors.append(f"{full_path}: {e}")
                print(f"  [ERROR] {filename}: {e}", file=sys.stderr)

    print("\n--- RINGKASAN ---")
    if not summary_counts:
        print("Tidak ada file yang berhasil diproses.")
    for label, count in summary_counts.items():
        print(f"  - {label}: {count} file")
    if unmatched:
        print("\n--- MASTER TIDAK DITEMUKAN ---")
        for item in sorted(set(unmatched)):
            print(f"  - {item}")
    if errors:
        print("\n--- ERROR ---")
        for err in errors:
            print(err)
    print(f"SUMMARY_JSON:{json.dumps(summary_counts)}")

    # OKE BASE (opsional)
    if oke_base_path:
        user_name = os.environ.get('BMACHINE_USER_NAME', config_data.get('UserName', 'USER'))
        create_oke_base_links(pilihan_path, oke_base_path, user_name)
        # Tambahkan shortcut di output lokal
        create_shortcuts_in_output_local(final_event_folder, pilihan_path, oke_base_path, user_name, output_path)

# ---------- Main ----------
def main():
    try:
        # Argumen:
        # 1: master_profesi (wajib)
        # 2: master_sporty  (boleh kosong "")
        # 3: pilihan        (wajib)
        # 4: output         (wajib)
        # 5: oke_base_path  (opsional)
        # 6: mappings_base64 (opsional)
        # 7: files_to_reprocess (opsional, pisah koma)
        if len(sys.argv) < 5:
            print("[USAGE] python profesi.py <master_profesi> <master_sporty_or_empty> <pilihan> <output> [oke_base] [mappings_base64] [files_to_reprocess]", file=sys.stderr)
            sys.exit(1)

        master_profesi = sys.argv[1]
        master_sporty  = sys.argv[2]
        pilihan        = sys.argv[3]
        output         = sys.argv[4]
        oke_base_path  = sys.argv[5] if len(sys.argv) > 5 and sys.argv[5] else None
        mappings_b64   = sys.argv[6] if len(sys.argv) > 6 and sys.argv[6] else None
        files_reproc   = sys.argv[7].split(',') if len(sys.argv) > 7 and sys.argv[7] else []

        cfg = load_config()
        process_images(master_profesi, master_sporty, pilihan, output, cfg,
                       files_to_reprocess=files_reproc, mappings_b64=mappings_b64, oke_base_path=oke_base_path)
    except Exception as e:
        print(f"[FATAL] Terjadi error yang menyebabkan force-close: {e}", file=sys.stderr)
        traceback.print_exc()
        print(f"FORCECLOSE:{str(e)}", file=sys.stderr)
        sys.exit(2)

if __name__ == "__main__":
    main()
