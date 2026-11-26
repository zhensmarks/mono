# BATCH Dashboard — Cara Kerja

Dokumen ini menjelaskan cara kerja tampilan **BATCH** di Dashboard BMachine
(Avalonia/C#, repo `mono`, app `apps/bmachine`). Tujuannya supaya orang lain
bisa langsung paham: apa yang terlihat di layar, data apa yang dibentuk di
belakangnya, dan bagaimana satu klik tombol berubah jadi proses Python +
Photoshop.

---

## 1. Gambaran singkat

BATCH adalah fitur **Batch Master**: memproses banyak folder foto/sekolah
sekaligus. Pola kerjanya selalu **pasangan folder**:

```
SOURCE / INPUT                          OUTPUT / MASTER
(folder hasil foto / PILIHAN)  ──────►  (folder hasil export siap cetak)
```

User men-drop satu atau beberapa folder SOURCE, aplikasi menebak folder
OUTPUT-nya, lalu user menjalankan salah satu *Master Script* (Python). Skrip
tersebut membaca SOURCE, memakai template master PSD, dan menulis hasil ke
OUTPUT. Setelah selesai, tree OUTPUT otomatis ter-refresh.

---

## 2. Dari mana tab BATCH muncul

- Navbar dashboard punya tiga tab radio: **HOME / BATCH / LOCKER**
  (`DashboardView.axaml`, sekitar baris 303–311).
- Tab BATCH hanya tampil kalau setting `Settings.Dash.Batch` bernilai `true`
  (dibaca di `DashboardViewModel` saat load visibility, default `"True"`).
- `IsBatchTabSelected` = `SelectedTabIndex == 1`. Tab HOME = 0, LOCKER = 2.
- Isi tab BATCH di-bind ke `BatchVM` (`BatchViewModel`), yang dibuat sekali di
  `DashboardViewModel` saat inisialisasi.

---

## 3. Layout tampilan

```
┌──────────────────────────── DASHBOARD ──────────────────────────────┐
│   [HOME]  [BATCH]  [LOCKER]                                         │
├─────────────────────────────────────────────────────────────────────┤
│  ┌───────────── SOURCE ─────────────┐ ┌──────── OUTPUT ───────────┐ │
│  │ SOURCE  <nama folder>   (pending)│ │        OUTPUT <nama>      │ │
│  │  ├ 01 POSE                       │ │  ├ 10RP                  │ │
│  │  ├ 02 POSE                       │ │  ├ 8R                    │ │
│  │  └ foto_001.jpg ...              │ │  └ ... (hasil export)     │ │
│  └──────────────────────────────────┘ └───────────────────────────┘ │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │ [icon script] [icon script] [icon script] ...   [refresh][x] │   │
│  └──────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.1 Drop zone (kondisi kosong)

Kalau belum ada folder (`BatchVM.ShowDropZone`), yang tampil adalah kotak
besar bertuliskan **"DRAG FOLDERS HERE"**. Drop zone menerima:

- folder biasa (drag dari Explorer),
- file shortcut `.lnk` (target-nya di-resolve lewat VBScript/cscript),
- drop diabaikan kalau tab LOCKER sedang aktif.

### 3.2 Panel SOURCE (kiri)

- Header: label kecil `SOURCE` + nama folder aktif (`HeaderSourceName`).
  Kalau belum ada output yang cocok, muncul chip kecil **"(pending)"**.
- TreeView (`SourceTreeItems`):
  - menampilkan isi folder SOURCE secara **lazy load** — anak folder baru
    dibaca saat node di-expand (lihat `BatchNodeItem.LoadChildren`),
  - urutan **natural sort** ("2" sebelum "10"),
  - file `.lnk` dan `desktop.ini` disembunyikan,
  - hierarki di-flatten secara visual (indentasi dihilangkan) supaya rapat,
  - **multi-select** (Ctrl/Shift) didukung.

### 3.3 Panel OUTPUT (kanan)

- Header: `OUTPUT` + nama folder output (`HeaderOutputName`).
- TreeView (`OutputTreeItems`) dengan isi yang sama seperti SOURCE.
- Kalau folder output belum ada di disk, tree kosong dan header menampilkan
  **"(pending)"**.
- **FileSystemWatcher** per root memantau folder output (termasuk
  subfolder). Setiap file dibuat/dihapus/rename → `RefreshOutput()` →
  tree langsung ikut update tanpa klik refresh manual.

### 3.4 Master File Browser (panel kanan OUTPUT)

Tombol folder di action bar node membuka **MASTER BROWSER** menggantikan
sementara tree OUTPUT:

- Menampilkan daftar file `.psd` / `.psb` (plus gambar `.jpg/.png/...` untuk
  mode Photoshop) dari path master yang dikonfigurasi.
- Ada search box (debounce 350 ms).
- **Double-click file = copy file master ke folder target** (nama otomatis
  di-rename `(1)`, `(2)`, ... kalau sudah ada). Setelah copy, tree target dan
  Explorer ikut di-refresh.

### 3.5 Footer

- **Tombol script master**: satu tombol per file `.py` di folder
  `Scripts/Master/` (atau custom scripts path dari Settings).
  - Nama, urutan, ikon, dan status tampil diatur oleh `scripts.json`
    (`Scripts/scripts.json` → disalin ke AppData sebagai `scripts.json`).
  - File berawalan `_` (helper/library) **tidak** jadi tombol.
  - File yang di-`enabled: false` di `scripts.json` juga tidak tampil.
  - `buat_master_duplikat.py` punya tombol sendiri di panel OUTPUT
    ("BUAT MASTER"), jadi hanya muncul di footer kalau user menyalakan
    ikon matanya di setting Master Script.
- **Refresh** (`RefreshCommand`): re-scan SourceRoot & OutputRoot semua root.
- **Clear** (`ClearCommand`): kosongkan semua folder, kembali ke drop zone.

### 3.6 Menu klik kanan (context menu)

Tree SOURCE, tree OUTPUT, dan header masing-masing punya context menu:

| Menu | Fungsi |
|---|---|
| New Subfolder... | Buat subfolder langsung di folder yang diklik |
| Replace (Auto) | Jalankan `replace.jsx` memakai pasangan folder otomatis (SOURCE↔OUTPUT yang bersesuaian) |
| Copy Path / Copy Output Path | Salin path ke clipboard |
| Delete / Delete Source Folder | Hapus folder dari disk (hati-hati) |

`Replace (Auto)` mendukung **multi-select**: semua folder yang dipilih
diproses sekaligus dalam satu invocation Photoshop (lihat bagian 6).

---

## 4. Model data di belakang layar

```
BatchViewModel
 ├─ SourceFolders : ObservableCollection<BatchFolderRoot>
 │    ├─ SourcePath / FolderName / DisplayName
 │    ├─ OutputPath / OutputHeader
 │    ├─ SourceRoot : BatchNodeItem   (tree SOURCE)
 │    └─ OutputRoot : BatchNodeItem   (tree OUTPUT, null = pending)
 ├─ MasterNodes / PhotoshopNodes      (isi Master Browser)
 ├─ MasterScriptOptions / ActionScriptOptions
 └─ SelectedMasterScript, OutputBasePath, IsProcessing, dst.
```

### 4.1 `BatchFolderRoot` — satu pasangan folder

- `RefreshSource()` membuat ulang node akar tree SOURCE.
- `SetupOutputWatcher()` membuat `FileSystemWatcher` di `OutputPath`
  (IncludeSubdirectories = true) → refresh tree tiap ada perubahan.
- `RefreshOutput()`: kalau folder output belum ada → `OutputRoot = null`
  (inilah sumber chip "(pending)").

### 4.2 `BatchNodeItem` — satu node tree

- Menyimpan `FullPath`, `Name`, `IsDirectory`.
- Folder berisi **dummy child** supaya chevron expand muncul; anak asli baru
  dibaca saat `IsExpanded` jadi `true` (lazy load, jalan di thread pool lalu
  di-push ke UI thread).
- Command per node: CopyPath, OpenText (file teks dibuka di log panel),
  Expand, Delete, CreateSubFolder.

### 4.3 Prediksi OUTPUT path ("Smart Path Prediction")

Saat folder di-drop (`AddFolders`), aplikasi menebak output:

1. Cari folder bulan di atas path dengan regex
   `^\d{2}\s+\w+\s+\d{4}$` (contoh `02 AGUSTUS 2025`), maksimal 5 level ke
   atas.
2. Kalau ketemu: `relativePath` = path relatif dari folder bulan tersebut.
3. Kalau **tidak** ketemu: fallback = nama folder saja.
4. `OutputPath = Configs.Master.LocalOutput + relativePath`.

**Kasus khusus `PILIHAN`:**

- Kalau nama folder = `PILIHAN`, output diarahkan ke **parent folder**
  (folder proyeknya), bukan ke folder `PILIHAN` itu sendiri.
- Label source & output memakai nama parent yang sama supaya sejajar.

### 4.4 Satu vs banyak folder

- **1 folder**: baris root tidak ditampilkan; isi folder langsung jadi baris
  teratas dan namanya tampil di header.
- **>1 folder**: tiap folder tampil sebagai root tree; nama root di header
  mengikuti root yang sedang dipilih (`SelectedBatchItem`).

---

## 5. Alur eksekusi Batch Master

Klik salah satu tombol script di footer → `ExecuteMaster()`:

```
1. Tentukan master path berdasarkan nama script (dari database):
     - *wisuda*   → Configs.Master.Wisuda10RP  + Wisuda8R
     - *manasik*  → Configs.Master.Manasik10RP + Manasik8R
     - *profesi*  → Configs.Master.Profesi + Sporty + Profesi8R
     - *pasfoto*  → Configs.Master.PasFoto
     - lainnya    → MasterTemplatePath (path yang di-browse manual)
     (buat_master* = auto-detect oleh orkestrator)

2. Untuk SETIAP root di SourceFolders (semua folder diproses, bukan cuma
   yang dipilih), jalankan berurutan:

   python Scripts/batch_wrapper.py
          --target  <nama_script.py>
          --pilihan <SourcePath>
          --master  <master1>
          --master2 <master2>      (kalau ada)
          --master3 <master3>      (kalau ada)
          --output  <OutputBasePath>
          --okebase <OkeBase>

   + environment variable BMACHINE_USER_NAME

3. stdout/stderr script dialirkan ke Log Panel (tab Console).
4. Setelah tiap folder selesai → RefreshOutput() untuk root itu.
5. Selesai → log "[SUCCESS] Batch Selesai."
```

`batch_wrapper.py` sendiri cuma perantara: dia mencari target script di
`Scripts/Master/`, lalu memanggilnya dengan urutan argumen yang benar per
script (mis. `pasfoto.py <master> <pilihan> <output> <oke_base>`,
`manasik.py <master> <pilihan> <output> <master2> <okebase>`, dst), dan
meneruskan output ke konsol host.

**Stop:** pesan `StopProcessMessage` akan `Kill(true)` proses tree
(wrapper + python child), jadi batch bisa dihentikan paksa.

---

## 6. Replace (Auto) — jalur Photoshop

Bukan batch export, tapi bagian dari tampilan BATCH yang sama:

1. `ReplaceSourceAuto` / `ReplaceOutputAuto` menentukan pasangan
   (OutputPath, SourcePath) dari folder yang diklik — atau dari **semua item
   multi-select**.
2. Aplikasi menulis file `.jsx` sementara berisi:

   ```jsx
   $.global.BMachineBatchContext = { SourceFolders: [...], OutputBasePath: ..., ... };
   $.evalFile(new File("<Scripts/Action/replace.jsx>"));
   delete $.global.BMachineBatchContext;
   ```

3. Wrapper itu dijalankan di Photoshop (path dari
   `Configs.Master.PhotoshopPath`).
4. `replace.jsx` memproses semua pasangan secara berurutan dalam satu
   invocation, lalu menampilkan laporan gabungan.

Varian lain:

- `ReplaceSourceManual` / `ReplaceOutputManual` — user memilih folder
  pasangannya lewat dialog.
- `ReplaceAllAuto` — semua root di SourceFolders diproses sekaligus.

---

## 7. Setting / key database yang dipakai

| Key | Fungsi |
|---|---|
| `Settings.Dash.Batch` | Tampilkan/sembunyikan tab BATCH |
| `Configs.Master.LocalOutput` | Base path OUTPUT (`OutputBasePath`) |
| `Configs.Master.LastTemplatePath` | Master template terakhir yang di-browse |
| `Configs.Master.Wisuda10RP`, `Wisuda8R` | Master untuk script wisuda |
| `Configs.Master.Manasik10RP`, `Manasik8R` | Master untuk script manasik |
| `Configs.Master.Profesi`, `Sporty`, `Profesi8R` | Master untuk script profesi |
| `Configs.Master.PasFoto` | Master untuk script pas foto |
| `Configs.Master.OkeBase` | Path OKE BASE (argumen `--okebase`) |
| `Configs.Master.PhotoshopPath` | Executable Photoshop untuk JSX |
| `Configs.Master.AdditionalPaths` | Path tambahan Master Browser |
| `Configs.Master.PhotoshopPaths` | Path tambahan mode Photoshop |
| `Configs.System.ScriptsPath` | Override folder Scripts (custom) |
| `Batch.FolderTemplates` | Template nama folder cepat (REVISI, FINAL, ...) |
| `User.Name` | Dikirim ke script sebagai `BMACHINE_USER_NAME` |

---

## 8. Peta file (kalau mau baca kodenya)

| File | Isi |
|---|---|
| `src/BMachine.UI/Views/DashboardView.axaml` | Seluruh layout tab BATCH: drop zone, dua TreeView, Master Browser, footer |
| `src/BMachine.UI/Views/DashboardView.axaml.cs` | Handler drag-drop (`OnBatchDrop`), sinkronisasi multi-select tree |
| `src/BMachine.UI/ViewModels/DashboardViewModel.cs` | `BatchVM` dibuat di sini, `IsBatchTabSelected`, visibility tab |
| `src/BMachine.UI/ViewModels/BatchViewModel.cs` | Otak fitur: AddFolders, prediksi output, daftar script, ExecuteMaster, Replace |
| `src/BMachine.UI/ViewModels/BatchFolderRoot.cs` | Pasangan SOURCE↔OUTPUT + FileSystemWatcher |
| `src/BMachine.UI/ViewModels/BatchNodeItem.cs` | Node tree + lazy load + command CRUD |
| `Scripts/batch_wrapper.py` | Launcher yang memanggil script Python target |
| `Scripts/Master/*.py` | Script pemrosesan (buat_master, manasik, wisuda, pasfoto, profesi_flat, ...) |
| `Scripts/Action/replace.jsx` | Logika Replace di Photoshop |
| `Scripts/scripts.json` | Alias/nama/urutan/ikon/enabled tiap script |

---

## 9. Catatan penting / pitfall

- **Semua folder diproses.** `ExecuteMaster` memproses *seluruh*
  `SourceFolders`, bukan hanya folder yang sedang diklik.
- **Output = base + relatif bulan.** Kalau struktur folder tidak mengandung
  pola `DD BULAN YYYY`, output jatuh ke `<LocalOutput>\<nama folder>`.
- **PILIHAN** selalu dipetakan ke parent folder-nya.
- Folder output yang belum ada = "(pending)"; watcher baru aktif setelah
  folder itu dibuat (oleh script atau manual).
- `buat_master_duplikat.py` hanya muncul di footer kalau `enabled: true` di
  `scripts.json`; default-nya lewat tombol "BUAT MASTER" di panel OUTPUT.
- Script helper berawalan `_` tidak pernah jadi tombol.
- Custom scripts path (Settings) meng-override folder `Scripts/` bawaan.
- Drop tidak berfungsi saat tab LOCKER aktif.
