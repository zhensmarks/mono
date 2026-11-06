# STUDI: `master_creator` (DMA App | Master Creator) — cara cepat bikin master & detect folder sekolah

Sumber: `D:\#DATA ABENG\#PROJECT\#BAHAN_UJI_COBA\master_creator`
(`master_creator.exe` 7.6 MB + `_internal/` PySide6 + `mc_config.dma`)

Cara dibaca: exe adalah PyInstaller (onedir, pyver 313). Isinya di-carve pakai
`pyinstaller_carve.py` (skill `reverse-engineer-anything`), lalu code object di-dis.
Satu file modul: **`dma_app_pyside_6 2.26.25.py`** — 869 code object, `MainWindow` 174 method
langsung, ±22.100 baris. Berkas analisa tersimpan di
`hermes/profiles/bmachine/cache/scratch/mc_extract/` (`mc_outline.txt`, `mc_strings.txt`,
`mc_docstrings.txt`) + tool `mc_dis.py` (dis satu fungsi / satu rentang baris).

`mc_config.dma` = config terenkripsi **AES-GCM** dengan kunci dari **PBKDF2 (password + salt)**
(`encrypt_config`/`decrypt_config`/`derive_key`); password diminta lewat `ask_password`/`Locker`.
Jadi isi config user tidak bisa dibaca tanpa password — tapi daftar *key*-nya terbaca dari kode.

---

## 1. Tiga konsep inti aplikasi

| Konsep | Isi | Key config |
|---|---|---|
| **Direktori kerja** | daftar folder sekolah yang sedang dikerjakan + mana yang aktif | `selected_dir_list`, `selected_dir`, `selected_dir_last`, `dir_management_status` |
| **Master (sumber)** | folder master per jenis produk (di server/LAN) | `default_path_pasfoto`, `default_path_manasik`, `default_path_wisuda`, `default_path_profesi`, `default_path_sporty`, `default_path_free_sekolah`, `default_path_sovenir_plakat/_mendali/_hanging/_box_pensil`, `default_path_bersama_*` (kpp/kpl/kml/bpp/bpl/bmp/bml/kwp/kwl/kmp/bwp/bwl/angkatan) |
| **Output (#OKE)** | folder hasil kerja per sekolah, diingat per folder | `dir_output_context` (dict path → `last_output_folder`), `last_output_folder`, `last_output_source_dir` |

Baris UI utama: `Direktori :` [combo folder] [Hapus] [Buka Folder Sekolah Aktif] [Check]
[Buat Folder] [Lihat Semua Note] lalu [Proses] [Reset] [ORI COPY] [Replace Foto (PS)].
Menu: Pengaturan (Default Path Reguler / Default Path Bersama / Pengaturan Utama / Folder Reguler /
Locker / Debug).

---

## 2. "Detect folder yang bisa dipilih" — inti kecepatan pindah sekolah

### 2.1 Drop = daftar kerja yang persisten
`FolderDropLineEdit.dropEvent` / `FolderDropComboBox.dropEvent` / `DirStatusItemDelegate.dropEvent`
menerima folder yang di-drop ke window, lalu ditambahkan ke `selected_dir_list`
(normalisasi `_dir_manager_norm`, dedupe pakai `os.path.normcase`). Daftar ini **bertahan antar sesi**.

`_dir_manager_display_name(path)`: kalau nama folder terakhir adalah `PILIHAN` (atau mengandung
`PILIHAN`) → yang ditampilkan **nama folder induknya**. Jadi user boleh drop folder sekolah
**atau** folder `PILIHAN` di dalamnya, tampilannya tetap nama sekolah.

Combo `cb_dir` = pindah sekolah cepat: tiap item menyimpan path asli di `Qt.UserRole`, tooltip =
path lengkap, dan warnanya mengikuti status (`_dm_status_color_hex`).

### 2.2 Status folder bekerja: PROSES (kuning) / ACC (hijau)
`_dm_status_map()` → config `dir_management_status` (dict path → status).
`_dm_get_status(path)`: `proses`/`process` → `'proses'`; `done`/`doneacc`/`done_acc`/`acc` → `'done'`;
selain itu kosong. Popup **DM (Direktori Managment)**: tabel direktori + checkbox PROSES/ACC yang
saling eksklusif; hanya satu folder aktif.
Gate: kalau folder aktif berstatus `proses`, `proses_pilihan` menolak jalan —
*"Folder ini statusnya PROSES. Ubah dulu ke ACC lewat DM."*
Ini yang membuat "kerja sekolah A" dan "sekolah B" tidak tercampur.

### 2.3 Menu **Folder Reguler** — scanner struktur bulan → marketing → sekolah
`open_folder_reguler_popup` (+ `_RegularScanWorker`, QThread) membaca root dari config
`folder_reguler_path`:

- Scan **3 level**: `BULAN > MARKETING > SEKOLAH` (`_RegularScanWorker.run`),
  subfolder diambil `_subdirs_sorted` (`os.scandir`, hanya `is_dir(follow_symlinks=False)`,
  diurut `name.lower()`), exception → list kosong (folder network yang error tidak meledak).
- `selected_month` diingat (`folder_reguler_selected_month`, opsi `SEMUA BULAN`), plus kotak `Search:`
  (cari bulan / marketing / sekolah).
- Tabel 4 kolom: **Bulan | Marketing | Sekolah | path**. Status bar: *"Total data: N folder sekolah"*
  dan saat difilter *"Tampil: x / y folder sekolah"*.
- Path UNC (`\\server\...`) discan sengaja di thread terpisah: *"UNC network sedang scanning bulan: X…
  UI tetap responsif."*
- Klik kanan: **Add Folder** (masukkan ke daftar kerja) dan **Open Folder** (buka di Explorer).
- **Add Folder hanya menerima folder yang punya subfolder `PILIHAN`**; yang tidak punya dilaporkan
  satu per satu ("Folder sekolah berikut tidak punya subfolder 'PILIHAN'"), sisanya → *"Berhasil tambah
  N folder ke daftar kerja."*

`_resolve_pilihan_dir(path_sekolah)` (aturan lengkap, dipakai banyak tempat):
1. `normpath().strip()`; kosong atau bukan folder → `''`
2. kalau basename == `PILIHAN` (case-insensitive) → pakai path itu sendiri
3. kalau `<path>/PILIHAN` ada → pakai itu
4. kalau tidak, cari nama anak folder yang `.upper() == 'PILIHAN'` (case-insensitive) dan folder → pakai
5. gagal semua → `''` (dianggap tidak punya PILIHAN)

### 2.4 Ingatan output per sekolah (yang bikin pindah-pindah terasa instan)
Saat combo `cb_dir` diganti, `_dir_manager_set_active` + `_dir_manager_restore_output_context`
dijalankan: status folder di-restore, `dir_output_context[key]` dibaca → `last_output_folder` untuk
sekolah itu dipasang kembali, dan `_refresh_copy_done_flags_from_oke` menyegarkan tanda
"sudah pernah copy". `_candidate_src_dirs` menebak kandidat sumber dari path (termasuk kasus `PILIHAN`).
`_dir_manager_get_buat_folder_status` menandai `SUDAH PERNAH BUAT FOLDER` /
`SUDAH PERNAH BUAT FOLDER (OUTPUT TIDAK DITEMUKAN)`.

### 2.5 Check / validasi isi folder
- `_check_folder_summary`, `_check_folder_pilihan` (root + subfolder langsung),
  `_check_folder_oke` (rekursif, hanya file langsung per folder) — hitung **JPG / PSD / PNG**.
- Pembedaan **group vs single** dari nama file (regex inti):
  - group: `(\d+)\s*\((\d+)\)(?:\s+(.*?))?\.(jpg|jpeg|psd|png)` → `001 (1).jpg`, `001 (2).jpg`
  - single: `(\d+)\.(jpg|jpeg)` ; label `TANPA LABEL` bila tidak ada teks setelah kurung
  - varian eksplisit: `(\d+)\s*\(SINGLE\)`, `(\d+)\s*SINGLE`
- `_check_folder_oke._export_quantity_checker_txt` export `EDITOR Quantity Checker.txt`
  (format `NAMA : JPG n  PSD n  PNG n`).

---

## 3. "Bikin master cepat" — otomatisasi

### 3.1 Deteksi jenis kerja dari NAMA FOLDER (`_automation_detect_kind`)
Dicocokkan pada nama folder huruf besar, **tahan typo** (sengaja, dari kebiasaan user):

| Kind | Pemicu |
|---|---|
| (SPORTY) | `SPORTY`, `SPOTRY` |
| (NON REG) | `NON REG`, `NONREG`, `NON-REG`, `NON_REG` |
| (BERSAMA) | `BERSAMA`, `ANGKATAN`, `POSTER`, `KALENDER` |
| WISUDA | `WISUDA`, `WISHUDA`, `WSD`, `MANASIK`, `MSK` |
| PROFESI | `PROFESI`, `OB PROFESI`, `OB` |
| FREE_SEKOLAH | `FREE` + (`SEKOLAH`,`SEKOALAH`,`SEKOLA`,`SEKOL`,`SEKO`) |
| PAS_FOTO | `PAS` + (`FOTO`,`PHOTO`,`POTO`) |
| PLAKAT | `PLAKAT`, `PLAKET`, `PLKAT`, `PLAKT`, regex `PL(A)?K(A\|E)?T` |
| MENDALI | `MEDALI`, `MENDALI` |
| BOX_PENSIL | `BOX` + (`PENSIL`,`PENCIL`,`PENSI`,`PENC`) |
| HANGING | `HANGING`, `HANGEN`, `HANGIN` |
| UNKNOWN | lainnya |

### 3.2 Target folder (`_automation_scan_targets` + `_automation_parse_prefix_number`)
- `_automation_parse_prefix_number(name)`: regex `^\s*(\d{1,4})\s*[\.,\)]\s*` → angka prefix
  (nama `1. KELAS A` → 1).
- Bila di root ada subfolder bernomor → dipakai urut angka; bila tidak ada → root itu sendiri
  dianggap satu target (dan root naik satu level). Hasil: `[{name, path}]`.

### 3.3 Deteksi dari NOTE (`_automation_collect_note_text`, `cari_master_otomatis_dari_note`)
- `_automation_collect_note_text(folder)`: baca semua berkas bernama
  `NOTE`/`NOTES`/`BACA`/`BACAA` dengan ekstensi `.txt/.text/.md/.rtf` **atau tanpa ekstensi**,
  gabung teksnya. Dipakai untuk mendeteksi kode PLAKAT/HANGING dan kata kunci MEDALI.
- `cari_master_otomatis_dari_note`: untuk MANASIK/WISUDA. Baca `.txt` di folder sekolah, ambil token
  `[A-Za-z0-9]+`, pilih subfolder master (konfirmasi "Pilih Subfolder Master" bila ambigu),
  lalu pilih PSD master: pola `^(\d+)([A-Z])$` (seri A/B/C), `^[A-Z]$`, `^\d+(R|RP)$`,
  deteksi "lettered" (`\b\d+\b\s+[A-Z]\b`), utamakan file polos (`prefer_plain`).
  Config: `last_manasik_folder`, `default_path_manasik`, `default_path_wisuda`.
- Contoh nyata di lapangan: folder sekolah berisi `MSK-002, TANPA TEMPEL UNTA.txt` → kode `MSK-002`
  (MSK = manasik) + catatan "tanpa tempel unta" menentukan varian master.

### 3.4 Pipeline per produk
`proses_pilihan` = dispatcher: lihat `auto_mode` atau jenis, lalu panggil salah satu:
`start_automation_auto`, `hitung_custom`, `start_counting`, `hitung_wisuda`, `lanjutkan_copy`,
`proses_free_sekolah`, `proses_non_reg`, `master_bersama`, `proses_plakat`, `proses_mendali`,
`proses_hanging`, `proses_box_pensil`, `proses_pas_foto`.

Keluarga aksi yang sama untuk tiap produk (pola konsisten):
- `COPY MASTER …` (+ `(AUTO)`, `(SINGLE)`) → salin PSD master ke folder `MASTER` di folder sekolah.
- `BUAT MASTER …` (+ `(AUTO)`, `(PERKELAS)`, `(Skip Copy)`) → buka PSD di Photoshop, replace foto,
  simpan; `_automation_wait_for_save` / `_automation_wait_for_all_saves` menunggu user Save,
  `_automation_prompt_save_and_close`, `_automation_auto_continue_after_save`.
- `_dialog_buat_master_*` = versi manual/dialog (plakat, mendali, hanging, box pensil, pas foto,
  pas foto perkelas); `_dialog_copy_master_*` = versi copy-nya.
- `_nonreg_*` = jalur NON REG (pilih master berdasarkan angkatan/kode, `_nonreg_extract_codes`).
- `master_bersama` (+ `_master_bersama_automation` / `_master_bersama_manual`) = BERSAMA/ANGKATAN.

### 3.5 Mode BATCH & AUTO
- `buat_folder_otomatis`: pilih jenis → **REGULER / REGULER BATCH / SUSULAN**.
  - REGULER = satu sekolah: pilih Direktori Output (mode LOKAL), Bulan, Marketing, #OKE;
    bisa "Jadikan Default Output" + "Pertahankan Bulan" (`bulan_terakhir`).
  - **REGULER BATCH** = ulang proses yang sama untuk **semua folder sekolah di daftar kerja**
    (`BUAT FOLDER REGULER (BATCH) (i/n)`, `selected_dir`), memakai konfigurasi default yang
    disimpan sekali dari run REGULER pertama; kalau default belum ada → diminta isi dulu.
  - Mode SERVER: output otomatis ke `folder_marketing_path` (tanpa Direktori Output).
  - Hasil: struktur `#OKE …` + shortcut `#OKE Pindah Kesini.lnk`, `DOC.lnk`
    (via WScript.Shell) + penanda `DOC LOCK (BAHAN EDITOR)` dan berkas `.dma`.
- SUSULAN: `#OKE SUSULAN <keterangan>` (mis. `23 OKTOBER` / `1.2.3`) + semua `.txt` dikopi ke struktur baru.
- `pengaturan_automation` (AUTOMATION): `automation_auto_continue_after_save` (ON/OFF),
  `automation_always_show_task` (YES/NO), `automation_always_make_master` (Yes/No),
  `automation_output_location` (**LOKAL/SERVER**), `automation_run_mode` (**SEKALIGUS/URUT**),
  `automation_replace_custom_input` (ON/OFF), `automation_max_per_part` (default 30).
- `start_automation_auto` (±1.000 baris) = orkestrator: scan target → deteksi kind → copy master →
  buat master → tunggu Save → follow-up queue (`_automation_enqueue_followup`,
  `_automation_process_followups`, `_automation_barrier_maybe_start`) → `_automation_check_complete`.
  Ada TaskManager + panel task (bisa dibatalkan: `_automation_cancel`, `_automation_force_stop`).

### 3.6 Protokol Photoshop (yang bisa dipakai ulang di BMachine)
`_internal/assets/`:
- `replace_auto_normal.jsx` — headless. Baca `replace_params.json` di folder script yang sama
  (JSON.parse, fallback `eval`), lalu:
  - scan master & input **rekursif**, dengan `excludeDirNames` (default `["BAHAN"]`; folder yang
    namanya *mengandung* "BAHAN" seperti "BAHAN PNG" juga dilewati),
  - urut **natural sort** (angka dianggap angka),
  - cocokkan input ke master lewat **angka depan** yang sama,
  - `maxPerPart` (30 normal, **70** untuk mode SINGLE/HANGING) → proses per part
    (`partIndex`, `startIdx`/`endIdx`),
  - replace XL & S (atau XL saja), progress bar + cancel, `skipProgress`,
  - tulis `replace_result.json`: `{cancelled, successNumbers[], successCount, failCount}`.
- `replace_params.json` (nyata): `{"masterPath": …, "inputPath": …, "maxPerPart": 30,
  "skipProgress": true, "mode": "YBPNG", "excludeDirNames": ["BAHAN"], "resultPath": …}`.
- `replace.jsx` = versi interaktif (UI pilih folder di Photoshop) untuk pemakaian manual.
- Cara app memanggil Photoshop: `_open_psd_in_photoshop`, `_photoshop_close_docs_by_paths`,
  `_popen_no_console` (tanpa jendela CMD), config path Photoshop di `pengaturan_path_photoshop`.

---

## 4. Yang belum saya buka (batas studi ini)

- Isi `mc_config.dma` (AES-GCM/PBKDF2, butuh password aplikasi) — hanya daftar key yang terbaca.
- Isi detail `_dialog_buat_master_plakat/_mendali/_hanging/_box_pensil/_pasfoto*` (masing-masing
  600–1.400 baris bytecode) — sudah diketahui masukannya (dialog + note), belum alur per baris.
- `start_automation_auto` (±1.000 baris) baru dibaca konstanta + urutan fasenya, bukan tiap cabang.

Kalau perlu lebih dalam: `py mc_dis.py "<nama fungsi>"` atau
`py mc_dis.py --r "<fungsi>:<baris-awal>-<baris-akhir>"`, dan
`py mc_strings_for.py "<kata kunci>"` untuk konstanta per fungsi.

---

## 5. Relevansi ke BMachine

Sudah ada di BMachine: `PathSettingsViewModel` (MasterPaths, banyak path, bisa diurutkan) dan
`OutputExplorerViewModel` (QuickAccess + folder eksternal + task file).

Belum ada di BMachine, dan inilah yang bikin `master_creator` terasa cepat:

1. **Daftar kerja sekolah persisten + combo pindah cepat** (`selected_dir_list` + `cb_dir`,
   nama tampil = nama sekolah walau yang di-drop folder `PILIHAN`).
2. **Status per folder** PROSES (kuning) / ACC (hijau), saling eksklusif, dan folder PROSES
   menolak diproses.
3. **Ingatan output per sekolah** (`dir_output_context`) — pindah folder langsung memulihkan
   folder output + tanda "sudah pernah copy" milik sekolah itu.
4. **Scanner Folder Reguler** (BULAN > MARKETING > SEKOLAH, filter bulan + search, Add/Open,
   syarat subfolder `PILIHAN`).
5. **Resolusi `PILIHAN` yang toleran** (sekolah atau subfolder `PILIHAN`-nya, case-insensitive).
6. **Deteksi jenis kerja dari nama folder** (tabel typo di §3.1) → langsung pilih pipeline master.
7. **Deteksi master dari NOTE.txt** folder sekolah (kode produk + varian).
8. **REGULER BATCH**: satu konfigurasi default → diterapkan ke semua sekolah di daftar.
9. **Quantity checker** JPG/PSD/PNG + pembedaan group (n (1)/(2)) vs single + export `EDITOR Quantity Checker.txt`.
