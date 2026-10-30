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
# POSTER = A3 (nama yang dipakai di lapangan cuma beda sebutan), jadi
# keduanya dianggap ukuran yang sama: 'A3'.
SIZE_TOKEN_RE = re.compile(
    r'(?i)(?<![0-9])(?:10|12|14|16|20)\s*RP(?![0-9A-Z])'
    r'|(?<![0-9])[2-9]\s*R(?![0-9A-Z])'
    r'|(?<![0-9A-Z])A3(?![0-9A-Z])'
    r'|(?<![A-Z])POSTER(?![A-Z])'
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
# (kolase / lembaran / poster / hanging / kalender / id card / medali), jadi
# jumlah foto di PILIHAN TIDAK menentukan jumlah psd.
#   FRS = FREE SEKOLAH   HGN = HANGING     PLK = PLAKAT/POSTER
#   ATN = ANGKATAN       IDC = ID CARD     MWS = MEDALI
#   BML/BMP = BERSAMA MANASIK    BPL/BPP = BERSAMA PROFESI
#   BSL/BSP = BERSAMA SPORTY
#   KPL/KPP = KALENDER PROFESI   KML/KMP = KALENDER MANASIK
#   KSL/KSP = KALENDER SPORTY
# Nama file hasilnya = nama file master (mis. "FRS 006.psd", "KMP 001.psd").
SINGLE_FILE_PREFIXES = {
    'FRS', 'HGN', 'PLK', 'ATN', 'IDC', 'MWS',
    'BML', 'BMP', 'BPL', 'BPP', 'BSL', 'BSP',
    'KPL', 'KPP', 'KML', 'KMP', 'KSL', 'KSP',
}


def is_single_file_code(code):
    """True kalau kode ini cuma butuh 1 psd (semua master TAMBAHAN).

    Master tambahan = prefix yang tidak punya script khusus (bukan MSK/WSD/
    KRA/PFM/PFB). Semuanya desain gabungan, jadi prefix BARU yang nanti
    ditambahkan otomatis ikut aturan ini tanpa perlu ubah daftar di atas.
    """
    pref = code_prefix(code)
    if not pref:
        return False
    return pref in SINGLE_FILE_PREFIXES or pref not in HANDLED_BY

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
    """Ambil penanda ukuran: '8R' / '10RP' / 'A3' (None kalau tidak ada).

    'POSTER' dianggap sama dengan 'A3' — di lapangan nama foldernya kadang
    cuma "POSTER", kadang "POSTER_A3", kadang "A3" saja.
    """
    t = (text or '').upper()
    if re.search(r'(?<![0-9])(?:10|12|14|16|20)\s*RP(?![0-9A-Z])', t):
        return '10RP'
    if re.search(r'(?<![0-9])[2-9]\s*R(?![0-9A-Z])', t):
        return '8R'
    if re.search(r'(?<![0-9A-Z])A3(?![0-9A-Z])', t) or re.search(r'(?<![A-Z])POSTER(?![A-Z])', t):
        return 'A3'
    return None


# Nama subfolder master yang dianggap mewakili tiap ukuran.
SIZE_ALIASES = {
    '10RP': ('10RP', '10 RP', '10R'),
    '8R': ('8R', '8 R'),
    'A3': ('A3', 'POSTER', 'PLAKAT', 'PLK'),
}


def size_matches(folder_name, size_tag):
    """True kalau nama subfolder master mewakili `size_tag` (POSTER == A3)."""
    if not size_tag:
        return False
    up = (folder_name or '').upper()
    for alias in SIZE_ALIASES.get(size_tag, (size_tag,)):
        if alias.upper() in up:
            return True
    return False


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


def _match_stem(stems, want_prefix, target):
    """Cari stem .psd yang prefix & nomornya cocok (toleran spasi/strip/nol)."""
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
            return stem
        if best is None:
            best = stem
    return best


def find_master_in(folder_path, code, allow_generic=True, size_tag=None):
    """Cari .psd di folder master yang cocok dengan `code`.

    Toleran terhadap cara penulisan: "MSK-001", "MSK 001", "MSK001",
    "FRS 003 SINGLE", "WSD-001 B" — yang penting prefix sama & nomornya sama.

    Folder master TAMBAHAN sering dibagi per ukuran cetak:
        FREE SEKOLAH\\          (10RP)      FREE SEKOLAH\\8R\\
        ANGKATAN\\10RP\\  ANGKATAN\\8R\\  ANGKATAN\\A3\\   (=POSTER)
    Jadi kalau `size_tag` diketahui, subfolder yang mewakili ukuran itu
    dicari LEBIH DULU, baru root, baru subfolder lain.

    Kalau tidak ada yang cocok TAPI folder itu cuma punya 1 desain
    (mis. "MASTER KPL"), desain tunggal itu dipakai (allow_generic).
    """
    digits = code_digits(code)
    if not digits:
        return None
    target = int(digits)
    want_prefix = code_prefix(code)

    size_dirs, other_dirs = [], []
    try:
        for name in sorted(os.listdir(folder_path)):
            p = os.path.join(folder_path, name)
            if not os.path.isdir(p) or name.startswith('#'):
                continue          # '#BG KATALOG', '#WISUDA (JANGAN DIPAKE)' = bukan desain
            if size_matches(name, size_tag):
                size_dirs.append(p)
            else:
                other_dirs.append(p)
    except Exception:
        pass

    # urutan: subfolder ukuran cocok -> root -> subfolder lain (tanpa generic)
    ordered = [(p, True) for p in size_dirs] + [(folder_path, True)]
    ordered += [(p, False) for p in other_dirs]

    for path, generic_ok in ordered:
        stems = psd_stems(path)
        if not stems:
            continue
        got = _match_stem(stems, want_prefix, target)
        if got:
            return os.path.join(path, got + '.psd')
        if generic_ok and allow_generic and len(stems) == 1:
            return os.path.join(path, stems[0] + '.psd')
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
    Folder master tambahan sering dibagi per ukuran (8R\\, 10RP\\, A3\\) dan
    kadang cuma di subfolder (mis. ID CARD\\LANDSCAPE, ID CARD\\PORTRAIT),
    jadi psd di subfolder satu level juga ikut dibaca.
    """
    stems = psd_stems(path)
    if not stems:
        try:
            for name in sorted(os.listdir(path)):
                sub = os.path.join(path, name)
                if os.path.isdir(sub) and not name.startswith('#'):
                    stems.extend(psd_stems(sub))
        except Exception:
            pass
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


# Dinaikkan kalau cara menebak prefix berubah, supaya cache lama (master_index.json)
# tidak dipakai lagi. v2: prefix ikut dibaca dari subfolder ukuran (8R/10RP/A3).
_CACHE_VERSION = 2


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
            sig = [int(os.path.getmtime(path)), len(os.listdir(path)), _CACHE_VERSION]
        except Exception:
            sig = [0, 0, _CACHE_VERSION]
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
