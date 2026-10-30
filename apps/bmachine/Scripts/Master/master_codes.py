# -*- coding: utf-8 -*-
"""
master_codes.py — SATU sumber aturan "kode di .txt  ->  folder master".

Dipakai bersama oleh:
  - buat_master.py   (dispatcher: lihat semua 'pintu', panggil script yang cocok)
  - manasik.py       (MSK)
  - wisuda.py        (WSD / KRA)
  - tambahan_flat.py (semua kode lain: FRS, BML, KMP, HGN, KPP, ...)

Aturan baca kode (semua bentuk ini dianggap sama):
    MSK-001.txt        MSK 001.txt        MSK_001.txt        MSK001.txt
    KODE= MSK-001.txt  KODE = MSK 10RP-004 .txt
    MSK-001 (DESAIN BARU).txt     MSK 002, DESAIN BARU.txt
    FRS-003.txt        BML 005.txt       KML-005, 33+1.txt
    isi file: "KODE= BML-001" / "KODE = MSK 10RP-004"

Kalau file ini diubah, semua script ikut berubah — jangan duplikasi logikanya.
"""

import os
import re
import json
import sqlite3

# ----------------------------------------------------------------------------
# 1. POLA KODE
# ----------------------------------------------------------------------------
# Token kode: PREFIX 2-4 huruf + pemisah opsional + 1-3 angka (MSK-001 / MSK001)
CODE_TOKEN_RE = re.compile(r'\b([A-Za-z]{2,4})[\s_\-=:.]{0,3}(\d{1,3})(?!\d)')

# Penanda ukuran cetak. WAJIB dibuang sebelum cari kode, kalau tidak
# "MSK 10RP-004" akan terbaca sebagai MSK-10 (salah).
# Batas: digit sebelumnya tidak boleh angka (supaya "18R" tidak ikut), tapi
# boleh nempel huruf — nyata di lapangan: "FOTO8R_TM", "FOTO10RP_MANASIK".
SIZE_TOKEN_RE = re.compile(
    r'(?i)(?<![0-9])(?:10|12|14|16|20)\s*RP(?![0-9A-Z])'
    r'|(?<![0-9])[2-9]\s*R(?![0-9A-Z])'
)

# Prefix yang bukan kode, tapi penanda nilai kode ("KODE= 001")
PSEUDO_PREFIX = {'KODE', 'KODENYA', 'CODE', 'KD'}

# Prefix yang SUDAH punya script khusus -> jangan diambil tambahan_flat.py
HANDLED_BY = {
    'MSK': 'manasik',
    'WSD': 'wisuda',
    'KRA': 'wisuda',
    'PFM': 'pasfoto',
    'PFB': 'pasfoto',
}

# Prefix yang hasilnya HANYA 1 file .psd per folder kerja — bukan 1 psd per
# foto. Ini desain GABUNGAN: semua foto di folder itu masuk ke SATU desain
# (kolase / lembaran / poster / hanging), jadi jumlah foto di PILIHAN TIDAK
# boleh menentukan jumlah psd. Nama file hasilnya: 1.psd
#   FRS = FREE SEKOLAH   HGN = HANGING   PLK = PLAKAT/POSTER
#   BML/BMP = BERSAMA MANASIK    BPL/BPP = BERSAMA PROFESI
#   BSL/BSP = BERSAMA SPORTY
# Kalau nanti ada jenis lain yang juga "1 pcs per sekolah" (mis. ATN, MWS,
# IDC), tinggal tambahkan prefix-nya di sini — tidak perlu ubah script lain.
SINGLE_FILE_PREFIXES = {
    'FRS', 'HGN', 'PLK',
    'BML', 'BMP', 'BPL', 'BPP', 'BSL', 'BSP',
}

# Petunjuk prefix dari NAMA folder master (dipakai kalau folder masih kosong,
# jadi begitu psd-nya dimasukkan otomatis kebaca tanpa ubah kode).
NAME_HINTS = {
    'FREE SEKOLAH': 'FRS',
    'HANGING': 'HGN',
    'ANGKATAN': 'ATN',
    'ID CARD': 'IDC',
    'KARTU PELAJAR': 'IDC',
    'PLAKAT': 'PLK',
    'MEDALI': 'MWS',
}


# ----------------------------------------------------------------------------
# 2. HELPER KODE
# ----------------------------------------------------------------------------
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
    except Exception:
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
    """Kumpulkan kandidat kode dari semua .txt di folder (bentuk apa pun).

    Kode berprefix didahulukan di atas angka polos. Angka polos sering cuma
    catatan ("-4 SUSULAN.txt") yang BUKAN kode master.
    Hasil: ['MSK-001'] atau ['001'].
    """
    high, low = [], []
    seen = set()

    def add(bucket, code):
        code = str(code or '').strip()
        if code and code not in seen:
            seen.add(code)
            bucket.append(code)

    for _filename, stem, content in read_txt_files(folder_path):
        text = SIZE_TOKEN_RE.sub(' ', f"{stem}\n{content}")

        got_explicit = False
        for pref, num in CODE_TOKEN_RE.findall(text):
            pref = pref.upper()
            if pref in PSEUDO_PREFIX:
                add(high, num)                  # "KODE= 001"
            else:
                add(high, f"{pref}-{num}")      # "KODE= MSK-001"
            got_explicit = True
        if got_explicit:
            continue

        for num in re.findall(r'(?<!\d)(\d{1,3})(?!\d)', text):
            add(low, num)
    return high + low


def find_candidate_codes_deep(folder_path, prefer_tag=None, max_depth=3):
    """Kode dari .txt folder ini; kalau kosong, telusuri subfolder.

    Kasus nyata: PILIHAN\\3. FOTO 8R_NONREG_GRADASI\\MANASIK\\MSK-004.txt
    — .txt tidak sejajar dengan subfolder level-1, jadi pembacaan
    "folder ini saja" tidak menemukan apa pun dan pekerjaan dianggap
    bukan manasik/wisuda. Fungsi ini menyelam sampai `max_depth` level.
    """
    codes = find_candidate_codes(folder_path, prefer_tag=prefer_tag)
    if codes:
        return codes
    root = os.path.abspath(folder_path)
    base_depth = root.rstrip(os.sep).count(os.sep)
    try:
        for cur, dirs, files in os.walk(root):
            if cur.rstrip(os.sep).count(os.sep) - base_depth >= max_depth:
                dirs[:] = []
            if not any(f.lower().endswith('.txt') for f in files):
                continue
            got = find_candidate_codes(cur, prefer_tag=prefer_tag)
            if got:
                return got
    except Exception:
        pass
    return []


def folder_text(folder_path):
    """Gabungan semua nama+isi .txt di folder (untuk deteksi kata kunci)."""
    out = []
    for _fn, stem, content in read_txt_files(folder_path):
        out.append(stem)
        out.append(content)
    return "\n".join(out)


# ----------------------------------------------------------------------------
# 3. CARI FILE MASTER UNTUK SEBUAH KODE
# ----------------------------------------------------------------------------
def psd_stems(folder_path):
    """Nama file .psd (tanpa ekstensi) di folder."""
    try:
        return sorted(os.path.splitext(f)[0] for f in os.listdir(folder_path)
                      if f.lower().endswith('.psd'))
    except Exception:
        return []


def find_master_in(folder_path, code, allow_generic=True):
    """Cari .psd di folder master yang cocok dengan `code`.

    Toleran terhadap cara penulisan: "MSK-001", "MSK 001", "MSK001",
    "FRS 003 SINGLE", "WSD-001 B" — yang penting prefix sama & nomornya sama.
    Kalau tidak ada yang cocok TAPI folder itu cuma punya 1 desain
    (mis. "MASTER KPL"), desain tunggal itu dipakai (allow_generic).
    """
    digits = code_digits(code)
    if not digits:
        return None
    target = int(digits)
    want_prefix = code_prefix(code)

    stems = psd_stems(folder_path)
    best = None
    for stem in stems:
        clean = SIZE_TOKEN_RE.sub(' ', stem).upper()
        if want_prefix and want_prefix not in clean:
            continue
        nums = re.findall(r'\d+', clean)
        if not any(int(n) == target for n in nums):
            continue
        # utamakan yang persis "PREFIX-NNN" / "PREFIX NNN" tanpa embel-embel
        if re.fullmatch(rf'\s*{re.escape(want_prefix)}[\s\-_]?0*{target}\s*', clean):
            return os.path.join(folder_path, stem + '.psd')
        if best is None:
            best = stem
    if best:
        return os.path.join(folder_path, best + '.psd')

    if allow_generic and len(stems) == 1:
        return os.path.join(folder_path, stems[0] + '.psd')
    return None


# ----------------------------------------------------------------------------
# 4. REGISTRY: prefix kode -> folder master (dari Settings / AdditionalPaths)
# ----------------------------------------------------------------------------
def _db_value(cur, key):
    try:
        cur.execute("SELECT Value FROM KeyValueStore WHERE Key=?", (key,))
        row = cur.fetchone()
    except Exception:
        return None
    if not row:
        return None
    try:
        return json.loads(row[0])
    except Exception:
        return row[0]


def _guess_prefixes_from_folder(path):
    """Tebak prefix dari isi folder (psd) dan/atau dari nama folder.

    Bisa lebih dari satu: folder PasFoto berisi PFB-001..010 DAN PFM-001..010.
    """
    stems = psd_stems(path)
    counts = {}
    for stem in stems:
        clean = SIZE_TOKEN_RE.sub(' ', stem).upper()
        m = re.match(r'^\s*([A-Z]{2,4})[\s\-_]?\d', clean)
        if m:
            counts[m.group(1)] = counts.get(m.group(1), 0) + 1
    found = sorted(counts, key=lambda k: (-counts[k], k))
    if found:
        return found

    # folder masih kosong -> pakai nama folder / tanda (XXX)
    base = os.path.basename(path.rstrip('\\/')).upper()
    m = re.search(r'\(([A-Z]{2,4})\)', base)          # "... (BML)"
    if m:
        return [m.group(1)]
    out = []
    for name, pref in NAME_HINTS.items():
        if name in base and pref not in out:
            out.append(pref)
    return out


def build_registry(db_path=None, cache_path=None, use_cache=True, force=False):
    """Bangun {'prefix': [{'name':..., 'path':...}]} dari Settings BMachine.

    Cache di %APPDATA%\\BMachine\\master_index.json — di-refresh kalau folder
    master berubah (mtime / jumlah psd), jadi menambah psd baru tetap kebaca.
    """
    if db_path is None:
        db_path = os.path.join(os.environ.get("APPDATA", ""), "BMachine", "BMachine.db")
    if cache_path is None:
        cache_path = os.path.join(os.environ.get("APPDATA", ""), "BMachine", "master_index.json")

    candidates = []   # (name, path)
    if os.path.exists(db_path):
        try:
            conn = sqlite3.connect(db_path)
            cur = conn.cursor()
            for key in ("Manasik10RP", "Manasik8R", "Wisuda10RP", "Wisuda8R", "PasFoto"):
                v = _db_value(cur, "Configs.Master." + key)
                if v and os.path.isdir(str(v)):
                    candidates.append((key, str(v)))
            add = _db_value(cur, "Configs.Master.AdditionalPaths") or []
            if isinstance(add, str):
                try:
                    add = json.loads(add)
                except Exception:
                    add = []
            for item in add:
                p = (item or {}).get("Path")
                n = (item or {}).get("Name") or os.path.basename(str(p or "").rstrip("\\/"))
                if p and os.path.isdir(str(p)):
                    candidates.append((n, str(p)))
            conn.close()
        except Exception as e:
            print(f"[WARN] Gagal baca DB master: {e}")

    cache = {}
    if use_cache and not force and os.path.isfile(cache_path):
        try:
            with open(cache_path, "r", encoding="utf-8") as f:
                cache = json.load(f)
        except Exception:
            cache = {}

    registry = {}
    new_cache = {}
    for name, path in candidates:
        try:
            sig = [int(os.path.getmtime(path)), len(os.listdir(path))]
        except Exception:
            sig = [0, 0]
        hit = cache.get(path)
        prefixes = None
        if isinstance(hit, dict) and hit.get("sig") == sig:
            prefixes = hit.get("prefixes")
        if not prefixes:
            prefixes = _guess_prefixes_from_folder(path)
        new_cache[path] = {"sig": sig, "prefixes": prefixes, "name": name}
        for prefix in prefixes or []:
            registry.setdefault(prefix, []).append({"name": name, "path": path})

    if use_cache:
        try:
            os.makedirs(os.path.dirname(cache_path) or ".", exist_ok=True)
            with open(cache_path, "w", encoding="utf-8") as f:
                json.dump(new_cache, f, ensure_ascii=False, indent=1)
        except Exception:
            pass
    return registry


def registry_for_codes(registry, codes):
    """Filter registry: hanya prefix yang benar-benar ada di pekerjaan."""
    wanted = {code_prefix(c) for c in codes if code_prefix(c)}
    return {p: registry[p] for p in wanted if p in registry}


def describe_registry(registry):
    lines = []
    for pref in sorted(registry):
        tag = " (script khusus)" if pref in HANDLED_BY else ""
        for item in registry[pref]:
            lines.append(f"  {pref}{tag} <- {item['name']}: {item['path']}")
    return "\n".join(lines)
