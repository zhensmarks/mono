# EXPLORER — Spesifikasi Cara Kerja (C#)

Dokumen ini menjelaskan cara kerja **Explorer** di BMachine (Avalonia/C#,
repo `mono`, app `apps/bmachine`), disusun dari pembacaan read-only kode
aktual. Tujuannya sama seperti `BATCH_DASHBOARD.md` untuk BATCH: supaya
port Rust/Slint-nya benar secara alur, bukan cuma tampilan.

Sumber: `Views/ExplorerWindow.axaml(.cs)`,
`Views/OutputExplorerView.axaml(.cs)`,
`ViewModels/ExplorerWindowViewModel.cs`,
`ViewModels/OutputExplorerViewModel.cs` (~3658 baris),
`Views/LogPanelSidebar.axaml(.cs)`, `Views/PanelNavigationLayout.cs`,
`Views/ExplorerSettingsView.axaml(.cs)`,
`Views/ExplorerSettingsWindow.axaml`,
`ViewModels/ExplorerSettingsViewModel.cs`,
`ViewModels/FileOperationsMonitorViewModel.cs`,
`Services/FileOperationManager.cs`, `Views/DocPanelView.axaml`,
`Views/PathSettingsView.axaml`, `Views/TaskMonitorView.axaml(.cs)`,
`Views/TaskMonitorWindow.axaml(.cs)`, `Services/ProcessLogService.cs`.
Lihat juga `ANALISIS_BMACHINE.md` §6.3 dan `BMACHINE_ALUR_APLIKASI.md` §4.

> **Koreksi REA — binari v6.5.0 (26 Nov 2026).** Laporan REA
> (`~/workspace/rea-analysis/LAPORAN_REA_BMACHINE.md`) membandingkan
> binari rilis v6.5.0 (17 Agu 2026) dengan source kini (csproj 15.15.1).
> Sebagian besar selisih adalah **DRIFT VERSI, bukan kesalahan dokumen**
> — spesifikasi ini ditulis dari source kini, jadi sudah benar; JANGAN
> "memperbaiki" spesifikasi ini ke arah v6.5.0 (K11). Yang REA tandai
> KOREKSI dan relevan untuk Explorer:
> - **K2 — Undo + Cut/paste: TERKONFIRMASI ada di source kini**
>   (`OutputExplorerViewModel`: `Undo`, `PushUndo`, `IsCutPayload`,
>   `MarkCutItems`). Sudah tercakup di §2.7 + §8.1 (Undo Ctrl+Z maks 50,
>   cut payload terpisah via `CutPaths` statis, no-op, blok subtree).
> - **K3 — Multi-tab + pinned tab berwarna: TERKONFIRMASI**
>   (`ExplorerWindowViewModel` 3 → 14 anggota: `TogglePin`,
>   `TogglePinTabAsync`, `CyclePinnedColor`, `RenamePinned`,
>   `LoadPinnedTabsAsync`, `SavePinnedTabsAsync`,
>   `SavePinnedTabsPublicAsync`). Sudah tercakup di §1.2 (pin, badge
>   30px, palet 8 warna, cycle warna, rename, persistensi
>   `Explorer_PinnedTabs`). Fitur ini BARU setelah v6.5.0 — port wajib
>   menyertakannya sejak awal, bukan sebagai tambahan belakangan.
> - **K4 — Ikon sistem + thumbnail + address bar ketik + zoom ikon:
>   TERKONFIRMASI** (`LoadThumbnailAsync`, `LoadSystemIconAsync`,
>   `ApplySystemIconMode`, `PushSystemIconsSetting`,
>   `PushCustomIconPathSetting`, `ActivateAddressBar`,
>   `CommitAddressBar`, `CancelAddressBar`, `ZoomIcons`,
>   `StrCmpLogicalW` natural sort). Sudah tercakup di §2.3, §2.5, §8.1.
>   Catatan: `CycleViewMode` (ganti mode tampilan via siklus) HILANG di
>   source kini — jangan asumsikan ada tombol cycle-view-mode (§8.2).

---

## 0. TL;DR — tiga wajah Explorer

"Explorer" di BMachine bukan satu layar, melainkan **satu kontrol
(`OutputExplorerView`) yang dipakai di tiga tempat**:

1. **ExplorerWindow** — window terpisah (frameless, 1000×700) dengan
   **tab**; tiap tab membungkus satu `OutputExplorerView`.
2. **Tab EXPLORER di Log Panel** — `OutputExplorerView` yang sama,
   disematkan di sidebar kanan dalam mode ringkas (`IsCompactHost`).
3. **OutputExplorerView itu sendiri** — file explorer lokal murni
   (TIDAK ada upload Google Drive di kode — klaim README belum
   terverifikasi; yang ada hanya explorer lokal + logika folder
   tanggal lokal).

Di sekelilingnya: **Explorer Settings** (window terpisah: General +
Appearance & Shortcuts), **Task Monitor** (window 420px untuk operasi
file background), dan **Log Panel** (sidebar kanan: tab EXPLORER / LOG /
MASTER / DOC + PS tersembunyi).

Alur inti sehari-hari dalam 1 baris:

```
buka Explorer → navigasi ke folder output (D:\#GAWENA) → cari/lihat file
→ klik kanan folder → "BUAT MASTER" → folder terkirim ke BATCH + langsung jalan
```

---

## 1. ExplorerWindow — window multi-tab

`Views/ExplorerWindow.axaml` (+`.cs`),
`ViewModels/ExplorerWindowViewModel.cs`.

### 1.1 Apa itu

Window top-level terpisah, **bukan bagian dashboard**. Kelas
`ExplorerWindow : Window`, ukuran default **1000×700**,
`WindowStartupLocation="CenterScreen"`, chrome kustom
(`SystemDecorations="Full"` + `ExtendClientAreaToDecorationsHint="True"`
+ `ExtendClientAreaChromeHints="NoChrome"`, transparansi Mica).
Ukuran window disimpan/dipulihkan dari DB kunci
`ExplorerWindow_Width` / `ExplorerWindow_Height` (hanya diterapkan
bila > 100).

**Cara membuka: TIDAK ditemukan di file-file ini.** Perintah pembuka
(`NewExplorerWindowCommand`) hidup di `OutputExplorerViewModel`;
pemicu pastinya (tombol/shortcut di MainWindow atau di
OutputExplorerView) perlu dicek di file lain. Konstruktor ViewModel
bisa menerima satu `OutputExplorerViewModel` awal — dipakai saat
sebuah explorer yang sedang berjalan "dipindah" ke window baru.

### 1.2 Struktur tab

- Koleksi `ObservableCollection<ExplorerTabItemViewModel> Tabs` +
  `SelectedTab`; tiap tab membungkus satu `OutputExplorerViewModel`
  (properti `ExplorerViewModel`).
- **Buat tab:** `AddTabCommand` — tab baru (explorer kosong) ditambah
  di akhir dan langsung dipilih. `OpenTabWithPath(path)` — tab baru
  disisipkan **tepat setelah tab aktif**, lalu dipilih; dipakai saat
  `RequestOpenNewTab` dari explorer aktif (mis. buka folder di tab
  baru).
- **Tutup tab:** tombol × di tab (hanya muncul saat hover), klik-tengah
  pada header tab, atau Ctrl+W. Setelah tutup, tab terpilih pindah ke
  tetangga (`Math.Min(idx, Count-1)`). **Bila tab terakhir ditutup
  (via × atau klik-tengah), window ikut tertutup.** Ctrl+W pada
  satu-satunya tab: tidak berbuat apa-apa (window tetap terbuka).
- **Pindah tab:** klik kiri header tab; `SwitchExplorerTabMessage` →
  pindah ke tab berikutnya (modulo, melingkar). **Drag-reorder tab
  TIDAK ada.**
- **Pin tab:** context menu "Pin Tab"/"Unpin Tab". Saat di-pin/unpin,
  koleksi diurut ulang: semua pinned di depan, lalu unpinned;
  `SavePinnedTabsAsync` langsung menyimpan.
- **Tampilan pinned tab:** lebar tetap **30px**, berisi badge 20×20
  (rounded 5) dengan label maksimal **2 karakter** huruf besar
  (`DisplayInitials`: label kustom bila ada, jika tidak huruf pertama
  judul folder). Tooltip menampilkan judul lengkap. Garis bawah aksen
  muncul saat tab aktif. Ada separator vertikal 1px antara zona pinned
  dan tab normal, hanya bila keduanya ada (`HasPinnedTabs`).
- **Palet warna badge (8 warna, `PinColorPalette`):** oranye `#F97316`
  (default), violet `#8B5CF6`, emerald `#10B981`, biru `#3B82F6`, rose
  `#F43F5E`, amber `#FBBF24`, cyan `#06B6D4`, pink `#EC4899`.
  Context menu pinned: "Change Color" (siklus ke warna berikutnya,
  langsung tersimpan) dan "Rename…" (dialog kecil: TextBox
  `MaxLength=2`, watermark "e.g. DK, HM ...", tombol OK/Cancel,
  Enter = konfirmasi; label disimpan uppercase).
- **Persistensi:** `PinnedTabsState` (`PinnedPaths`, `CustomLabels`,
  `CustomColors`) disimpan di DB kunci **`Explorer_PinnedTabs`** via
  `SavePinnedTabsPublicAsync`. Saat window dibuat,
  `LoadPinnedTabsAsync` memulihkannya: tab default yang kosong dihapus
  bila belum dinavigasi, lalu pinned tab disisipkan **di depan** sesuai
  urutan tersimpan, lengkap dengan label dan warna kustom.
- **Tiga mode header** (diatur `NotifyHeaderStateChanged`): bila tab
  aktif sedang search → header diganti kotak search; bila >1 tab atau
  ada pinned → tab bar tampil (`ShowTabBar`); bila **satu tab unpinned**
  → tab bar disembunyikan dan nama folder aktif ditampilkan rata tengah
  sebagai judul (`ShowSingleTabTitle`, margin kiri-kanan 60).

### 1.3 Layout window

Grid 2 baris: header (`Auto`) + konten (`*`).

- **Header:** Grid 2 kolom. Kiri: area tab (judul tunggal / kotak
  search / tab bar dalam `ScrollViewer` horizontal). Kanan: **hanya
  satu tombol Close (×)** — tidak ada tombol minimize/maximize di
  XAML ini. Seluruh header adalah drag zone (`OnDragZonePointerPressed`
  → `BeginMoveDrag`). Garis separator 1px di bawah header.
- **Konten:** `ItemsControl` berisi semua tab ditumpuk dalam satu
  `Grid`; hanya tab terpilih yang terlihat
  (`IsVisible="{Binding IsSelected}"`). Tiap tab menampilkan
  `views:OutputExplorerView` dengan `DataContext` =
  `ExplorerViewModel` tab tersebut.
- **Floating selection statusbar:** pil mengambang di bawah-tengah
  (margin bawah 14, radius 20, bayangan), muncul hanya bila tab aktif
  punya **>1 item terpilih**, berisi ikon trash + teks "N items
  selected" (`SelectionInfo`/`HasSelectionInfo` di
  `ExplorerTabItemViewModel`). `IsHitTestVisible="False"` — murni
  informatif, tidak bisa diklik.
- **Yang TIDAK ada di level window ini:** sidebar, address bar,
  breadcrumb, dan status bar — semuanya hidup di dalam
  `OutputExplorerView` per tab, bukan di `ExplorerWindow`.

### 1.4 Navigasi & shortcut (level window)

Tidak ada tombol Back/Forward/Up, address bar, atau breadcrumb di
window ini — semua itu milik `OutputExplorerView` di dalam tiap tab.
Yang dilakukan window hanya **meneruskan shortcut keyboard ke VM tab
aktif** (`ApplyWindowExplorerShortcuts`, diterapkan ulang tiap ganti
tab dan saat `ExplorerShortcutsReadyMessage`): Back, Forward, Up,
Refresh, fokus address bar, fokus kotak search. Pengecualian: gesture
Back **tidak** didaftarkan di level window bila gesturnya adalah tombol
"Back" itu sendiri, karena bentrok dengan editing di TextBox.
Ctrl+F ditangani khusus (`OnWindowKeyDown` tunnel → `FocusSearchBoxCommand`
tab aktif). Saat search aktif di tab (`IsSearchVisible`), header berubah
menjadi kotak search terpusat 320×24, watermark "Cari file...", terikat
dua arah ke `SearchText` tab aktif; Escape menutup search
(`CloseSearchCommand`), Enter membuka hasil pertama
(`OpenFirstSearchResultCommand`).

Semua shortcut dibungkus `FocusAwareCommand` +
`ExplorerShortcutGate.ShouldBlock`: bila fokus sedang di TextBox,
gesture pengeditan teks diblokir (navigasi/refresh tetap jalan).
Saat window dibuka atau tab diganti, `FocusActiveExplorerView` memberi
fokus ke `OutputExplorerView` yang terlihat agar shortcut langsung
bekerja.

---

## 2. OutputExplorerView — explorer lokal utama

`Views/OutputExplorerView.axaml` (1042 baris),
`Views/OutputExplorerView.axaml.cs`,
`ViewModels/OutputExplorerViewModel.cs` (~3658 baris).

`UserControl` Avalonia (bukan `Window`), dibungkus
`LayoutTransformControl` (`ZoomHost`) untuk zoom konten.

### 2.1 Root path

`LoadRootPath()`: root = `Configs.Explorer.DefaultPath`, fallback
`Configs.Master.LocalOutput` (biasanya `D:\#GAWENA`). Jika kosong atau
folder tidak ada → `IsRootPathMissing = true`, `CurrentPath = ""`,
daftar kosong, empty state "Local Output belum disetting. Silahkan
atur di menu Settings > Paths". `LoadRootPath` dipanggil ulang saat
`MasterPathsChangedMessage` diterima. `CurrentPath` awal = `RootPath`.

### 2.2 Layout lengkap

- **Header / path bar** (satu baris penuh, bisa disembunyikan via
  `IsShowPathBar` / `ToggleShowPathCommand`, persisten di
  `Configs.Explorer.ShowPathBar`): tombol Back / Forward / Up /
  Refresh (kiri), address bar dual-mode (tengah), `StatusSummary` +
  tombol Search (kanan). Tidak ada status bar bawah — `StatusSummary`
  ("N items", atau "N items, M selected") tampil di header ini.
- **Sidebar Quick Access** (kiri, lebar default 120, `MinWidth` 32,
  `GridSplitter`, persisten `Configs.Explorer.SidebarWidth`):
  `ListBox` `QuickAccessItems` (baris 28px, ikon sistem/folder/file +
  nama, klik → `NavigateToQuickAccessCommand`, context menu "Remove
  Pin"). Di bawahnya tombol Settings (gear) yang membuka
  `ExplorerSettingsWindow` sebagai dialog modal. Tidak ada item default
  yang di-seed di kode — hanya yang tersimpan di
  `Configs.Explorer.QuickAccess`.
- **Area daftar file** (kanan): 5 `ListBox` bergantian sesuai layout
  (lihat §2.5) + `SplitScrollViewer`, overlay rubber-band
  (`RubberBandCanvas`/`SelectionRect`), empty state (ikon +
  `EmptyStateText`, atau pesan "Local Output belum disetting" bila
  root hilang).
- **Preview panel** (kolom kanan area file, lebar 280 bila terlihat,
  0 bila tidak): judul + isi teks read-only + tombol close
  (`ClosePreviewPanelCommand`). Hanya untuk **satu file terpilih**:
  `.txt` dibaca penuh secara async, `.docx` hanya placeholder
  "(Preview for .docx: open with default app to view.)". File lain /
  folder / multi-select → panel tertutup (`UpdatePreviewAsync()`).
- **Overlay spotlight:** dialog New Folder (`IsNewFolderVisible`,
  default "New Folder"), New File (`IsNewFileVisible`, default
  "New File.txt"), Duplicate (`IsDuplicateVisible`, `NumericUpDown`
  1–100), dan pil type-ahead di bawah-tengah (lihat §2.4).
- **Tidak ada di file ini:** kotak input pencarian (`HeaderSearchBox`
  dicari di `TopLevel`/host lewat `FocusSearchBoxMessage`) dan toolbar
  tombol Batch/Duplicate/Copy-Master — command-command itu ada di
  ViewModel tapi binding-nya tidak ada di ketiga file ini (kemungkinan
  di host `ExplorerWindow`).

### 2.3 Navigasi

- **Stack:** `_backStack` / `_forwardStack` (`Stack<string>`).
  `NavigateTo(path)`: abaikan bila kosong / tidak ada / sama dengan
  current; push current ke back, clear forward, lalu
  `ExecuteNavigation()`. `GoBack()` / `GoForward()` memindah antar
  stack. `NavigateUp()`: hanya bila `CanNavigateUp`, yaitu
  `IsSubfolderOf(CurrentPath, RootPath)` — **Up tidak bisa naik di
  atas RootPath**. `ExecuteNavigation()` selalu mengosongkan
  `SearchText` (keluar dari mode search), lalu `UpdateBreadcrumbs()`
  + `LoadViewSettingsForPath()` + update flag `Can*`.
- **Breadcrumb (`UpdateBreadcrumbs()`):**
  `ObservableCollection<BreadcrumbItem>` (Name + Path). Lokal: drive
  sebagai crumb pertama, lalu tiap folder. **UNC:** `\\Server\Share`
  digabung jadi satu crumb pertama ("Server\Share"), lalu subfolder.
  Penting: di XAML, `ItemsControl` breadcrumb
  `IsHitTestVisible="False"` dan seluruh permukaan address bar adalah
  tap-target untuk masuk mode edit — **segmen breadcrumb tidak bisa
  diklik satu-satu untuk navigasi**; navigasi path dilakukan lewat
  address bar.
- **Address bar bisa diedit:** klik permukaan address bar →
  `ActivateAddressBarCommand` (`AddressBarText = CurrentPath`,
  `IsAddressBarEditing = true`, textbox fokus + SelectAll). Enter →
  `CommitAddressBarCommand` (`ExpandEnvironmentVariables` +
  `GetFullPath`; harus folder yang ada, kalau tidak → error + batal).
  Esc → `CancelAddressBarCommand`. Shortcut: `Alt+D` dan `Ctrl+L`
  (`FocusPathBarCommand`).
- **Input lain:** tombol mouse XButton1/XButton2 = Back/Forward;
  tombol `Back` ditangani manual di `OnRootKeyDown` (bukan KeyBinding,
  agar tidak bentrok dengan TextBox); Enter membuka item terpilih;
  shortcut default bisa diubah lewat `Configs.Explorer.Shortcut*`
  (Back `Alt+Left`, Forward `Alt+Right`, Up `Alt+Up`, Refresh `F5`).

### 2.4 Search & type-ahead (dua mekanisme berbeda)

- **Search (`SearchText`, `ToggleSearchCommand` / `Ctrl+F`):**
  rekursif penuh ke subfolder via `SearchStream()` —
  `EnumerationOptions`: `RecurseSubdirectories = true`,
  `IgnoreInaccessible = true`, case-insensitive. Pola `*teks*`; bila
  teks mengandung `*`/`?`, keduanya diperlakukan sebagai **karakter
  literal** (pola `*` + filter substring literal), bukan wildcard.
  Hasil di-streaming dalam **batch 64 item atau tiap 120 ms**
  (`onBatch` → `Items.Add` di UI thread) sehingga folder besar
  langsung menampilkan hit pertama; **cap 5000** (`SearchResultCap`,
  flag `SearchTruncated`, ringkasan "N hasil (dibatasi)").
  **Debounce 300 ms** (`SearchDebounceMs`, `Task.Delay` +
  `CancellationTokenSource`); ketikan baru membatalkan walk sebelumnya.
  Status: `IsSearching` → "Mencari...", 0 hit → "Tidak ada hasil" /
  empty state "Tidak ada hasil untuk … di folder ini atau
  subfoldernya". Enter di search → `OpenFirstSearchResultCommand`
  (buka hit pertama). Hasil search menampilkan kolom lokasi
  (`ShowLocation = true`, `LocationDisplay` = folder induk + tooltip
  nama + path). Urutan enumerasi: direktori dulu, lalu file; hasil
  akhir tetap melewati sort/group `PopulateItems()`. Menutup search
  mengosongkan `SearchText`; pindah folder juga mengosongkannya.
- **Type-ahead (terpisah dari search, di `axaml.cs`
  `OnRootKeyDown`):** ketik huruf/angka tanpa modifier → buffer
  lokal, reset bila jeda **> 800 ms**, Esc mengosongkan buffer;
  dicocokkan `Name.StartsWith(buffer)` (case-insensitive) pada
  `vm.Items`, lalu select + `ScrollIntoView` + fokus ke container
  item. Ada juga pil overlay type-ahead di XAML yang terikat ke
  properti VM `TypeAheadText` / `IsTypeAheadVisible` — tetapi code-behind
  memakai buffer lokal `_typeAheadBuffer` dan **tidak ditemukan
  penugasan ke properti VM tersebut** (meragukan: pil mungkin tidak
  pernah terisi, atau diisi dari tempat lain).

### 2.5 Tampilan: layout, sort, group, setting per-folder vs global

- **Enum `ExplorerLayoutMode`: `Vertical, Horizontal, Thumbnail,
  Tiles, Split`.** `SetViewLayoutCommand(int)` memetakan 8 shortcut
  `Ctrl+Shift+1..8`: 1–3 = Thumbnail ukuran 256/128/96; 4 = Split;
  5 = Horizontal ("List"); 6 = Vertical ("Details"); 7 = Tiles
  (ikon 48); 8 = Vertical ("Content", duplikat Vertical). Default
  `Vertical`, `ThumbnailSize` default 140
  (`ThumbnailHeight = ThumbnailSize + 40`).
  - **Vertical:** baris detail 26px — ikon, nama (rename inline),
    ukuran (`DisplaySize`), tanggal `dd MMM yyyy`, plus kolom lokasi
    saat search. Kolom ukuran/tanggal hilang bila
    `ShowDetailsColumns = false` (host compact < 400px).
  - **Horizontal:** `WrapPanel` vertikal, kartu ringkas (ikon + nama,
    maks 150px), scroll horizontal via roda mouse yang dialihkan.
  - **Thumbnail:** kartu `ThumbnailSize × ThumbnailHeight`, thumbnail
    gambar asli (decode 150px, `ThumbnailImage`, lazy) untuk file
    gambar, ikon sistem/fallback untuk lainnya, nama di bawah.
  - **Tiles:** kartu 240×56, ikon/thumbnail + nama berdampingan.
  - **Split: dua kolom kiri/kanan dalam satu `ScrollViewer`
    (`SplitScrollViewer`) dengan satu scroll bersama.**
    `UpdateSplitView()`: bila ada **≥ 2 ekstensi file berbeda**,
    kiri = grup ekstensi terbanyak (folder dihitung sebagai grup
    "Folder"), kanan = sisanya; bila hanya 1 ekstensi / hanya folder
    → daftar dibelah dua (ceil setengah kiri). Kedua pane berbagi
    **satu** `SelectedItems` (klik tanpa modifier di Split
    mengosongkan seleksi kedua pane dulu; header group tidak dipakai
    di Split).
- **Sort (`ExplorerSortOption`: Name/Date/Type, default Name;
  `IsSortDescending`):** `PopulateItems()` memakai **natural sort**
  (`NaturalSortComparer` → `StrCmpLogicalW` di Windows, fallback
  `OrdinalIgnoreCase`) untuk Name/Type. Urutan: **folder dulu, lalu
  shortcut (`.lnk`/`.desktop`/`.webloc`), lalu file** — berlaku untuk
  ketiga sort, dan Descending membalik tiap kelompok terpisah (folder
  tetap di atas). Sort Type memakai kunci ekstensi
  (`GetSortTypeKey()`).
- **Group (`ExplorerGroupBy`: None/Date/Type, default Date):** header
  `ExplorerGroupHeaderViewModel` (tidak bisa dipilih/fokus). Date:
  urutan tetap Today → Yesterday → Earlier this week → Last week →
  Earlier this month → Last month → A long time ago
  (`GetDateGroupHeader()`, minggu mulai Minggu). Type: Folders →
  Shortcut → grup "XXX Files" per ekstensi (alfabetis).
- **Per-folder vs global:** `SaveViewSettings()` /
  `LoadViewSettingsForPath(path)`; kunci lokal
  `Configs.Explorer.FolderSettings.<Base64(path-lowercase-tanpa-trailing-slash)>`
  berisi DTO SortBy/IsSortDescending/LayoutMode/GroupBy, berlaku
  **hanya untuk folder persis itu** (subfolder tidak mewarisi).
  Checkbox "This Folder Only" (`IsLocalSettings`) di background menu:
  ON → simpan sebagai lokal seketika; OFF → hapus override lokal,
  kembali ke global. Global: `Configs.Explorer.SortBy`,
  `IsSortDescending`, `GroupBy`, dan mode view. **Kejanggalan kunci:**
  `LoadRootPath()` membaca mode global dari
  `Configs.Explorer.ViewMode`, sedangkan `LoadViewSettingsForPath()`
  (jalur global) membaca `Configs.Explorer.DefaultViewMode` dan
  `SaveViewSettings()` menulis `ViewMode` bila `CurrentPath` kosong
  tapi `DefaultViewMode` bila ada path — dua kunci berbeda untuk
  setting yang sama (jangan ditiru mentah-mentah).
- **Lainnya:** `ContentScale` 50–250% (`Configs.Explorer.ContentZoom`,
  `LayoutTransformControl`); `Ctrl+roda mouse` → `ZoomIconsCommand`
  (bila bukan Thumbnail/Tiles, pindah dulu ke Thumbnail 96, lalu ±16,
  jepit 48–512); font via `Configs.Explorer.FontFamily`; ikon sistem
  Windows/7tsp opsional (`UseSystemIcons`, `CustomIconPath`, cache
  per ekstensi, ikon `.lnk` per path) dengan fallback glyph folder
  kuning/file abu-abu.

### 2.6 FileSystemWatcher

`SetupFolderWatcher(path)`: **satu watcher per folder**,
`NotifyFilter = FileName | DirectoryName | LastWrite | Size`,
`IncludeSubdirectories = false`. Event yang di-subscribe:
**Created, Deleted, Renamed saja** (Changed tidak di-subscribe meski
`LastWrite` ada di filter). Watcher dipertahankan bila path sama
(tidak dibuat ulang tiap `LoadItems`); di-dispose hanya saat pindah
folder / path tidak ada. **Debounce restart-timer 400 ms**
(`WatcherDebounceMs`): tiap event membatalkan timer sebelumnya dan
menjadwalkan ulang satu reload. Reload hanya jalan bila path watcher
masih sama dengan `CurrentPath` **dan** tidak sedang search (search
tidak boleh di-reset watcher).

### 2.7 Operasi file

- **Copy/Cut/Paste** (`IExplorerClipboard`): Copy mengosongkan state
  cut; Cut mengisi **`CutPaths` statis** (`HashSet<string>`,
  process-wide — sengaja agar cut di satu tab/window bisa di-paste
  sebagai MOVE di tab/window lain) dan `MarkCutItems()` meredupkan
  item ter-cut (`RowOpacity = 0,45` via `IsCut`, dipertahankan melewati
  refresh karena objek item baru). Paste: `IsCutPayload()` (kesamaan
  himpunan persis) menentukan move vs copy; cut + paste ke folder yang
  sama = **no-op** (dilewati, tidak jadi "- Copy"); paste folder ke
  subtree-nya sendiri diblok; nama bentrok memakai satu aturan bersama
  `FileNameDeduplicator.BuildCopyPath` — **"x - Copy", "x - Copy (2)",
  …** (dengan `reserved` agar paste multi-item tidak berebut nama yang
  sama); copy/move berjalan background via
  `FileOperationManager.CopyFileBackground / MoveFileBackground`;
  setelah move-paste berhasil, `CutPaths` dikosongkan dan clipboard
  file di-clear. Item terakhir yang di-paste di-select via
  `_pendingSelectName` (dicoba ulang maks 3 reload — reload watcher
  bisa datang sebelum file selesai disalin).
- **Undo (`UndoCommand`, `Ctrl+Z`): maks 50 entri** (`PushUndo()`,
  entri tertua dibuang), **per-instance ViewModel** (bukan statis).
  Bisa di-undo: `new` (hapus folder/file yang dibuat), `rename`
  (pindah kembali), `batchrename` (balikkan berurutan terbalik),
  `copy` (hapus salinan yang dibuat), `move` (pindah kembali).
  **Delete ke Recycle Bin TIDAK bisa di-undo** — entrinya berkind
  `recycle` dan saat di-undo hanya menampilkan info "Deleted to the
  Recycle Bin — restore it from there." Permanent delete tidak membuat
  entri undo sama sekali.
- **Duplicate:** popup `DuplicateCount` 1–100
  (`OpenDuplicatePopupCommand` / `ConfirmDuplicateCommand`);
  `DuplicateItem(count)` memakai aturan nama yang sama ("x - Copy",
  …) + `CopyFileBackground` + entri undo kind `copy`. Binding pemicu
  popup tidak ada di ketiga file ini (kemungkinan di host).
- **Batch rename (`BatchRenameFromClipboardCommand`):** baca teks
  clipboard (`MainWindow.Clipboard`), pecah per baris; item terpilih
  diurutkan **berdasarkan Name** (bukan urutan seleksi/tampilan);
  `count = Min(jumlah nama, jumlah item)`; file: ekstensi asli
  dipertahankan bila nama baru belum punya ekstensi; rename sinkron
  `File/Directory.Move`; entri undo `batchrename`. Catatan: toast
  sukses memakai `count` (Min) meski sebagian rename gagal.
- **New folder/file:** overlay spotlight, Enter = buat, Esc = batal.
  `CreateNewFolder` → `Directory.CreateDirectory` + undo `new`;
  `CreateNewFile` → nama tanpa "." otomatis ditambah ".txt", file
  kosong, + undo `new`. `CreateFolderShortcutCommand`: nama otomatis
  "New Folder (2)…" bila bentrok, lalu `_pendingEdit = true` sehingga
  langsung masuk mode rename inline.
- **Delete:** `DeleteItemCommand` → Recycle Bin via
  `_platformService.MoveToRecycleBin` (sinkron, **tanpa konfirmasi**
  di kode yang dibaca), + entri undo `recycle`. `PermanentDeleteItemCommand`
  (`Shift+Delete`) → **selalu konfirmasi dulu** (`ShowConfirmAsync`,
  "Hapus permanen … tidak bisa dibatalkan"), lalu
  `DeleteFileBackground` (tanpa undo). Pengaman: folder yang sedang
  dibuka dan ancestor-nya tidak bisa dihapus (`IsPathInside` yang
  sadar separator — "C:\Out2" bukan di dalam "C:\Out"); hit search di
  subfolder **bisa** dihapus.
- **Rename inline:** `IsEditing` di tiap template (Enter commit, Esc
  cancel, LostFocus commit); saat fokus, hanya bagian nama yang
  di-select (ekstensi file tidak ikut, `FocusAndSelectNamePart()`).
  `CommitRenameCommand`: `File/Directory.Move`, entri undo `rename`,
  item tetap ter-select setelah reload. `CancelRenameCommand` me-reload
  daftar (mengembalikan nama). `LoadItems()` dilewati total bila ada
  item sedang di-rename.
- **Lainnya:** `CopyPathCommand` (multi-path dipisah newline; tanpa
  seleksi → salin `CurrentPath`), `CopyNameCommand`,
  `PastePathCommand` (navigasi ke path teks di clipboard; bila file →
  ke folder induknya), `SelectAllCommand` (`Ctrl+A`), drag-out ke
  aplikasi eksternal (mis. Photoshop, threshold 5px, Shift = move),
  drag-in dari luar (`OnDrop` → `HandleDroppedFiles()`, Shift = move,
  default copy, guard subtree sendiri, **tanpa entri undo**),
  rubber-band selection (threshold 5px, auto-scroll tepi 30px/12px,
  sadar virtualisasi — item yang ter-virtualisasi tetap ter-select),
  dan klik item yang sudah ter-select menunda deselect sampai mouse-up
  agar drag multi-select tidak kehilangan seleksi.

### 2.8 Buka file

`OpenItemCommand` (double-click / Enter): folder → `NavigateTo()`
(bila `.lnk` ke folder, pakai `TargetPath` hasil resolve shortcut via
`cscript` + VBScript `WScript.Shell` sementara).
`OpenItemInNewTabCommand` hanya untuk folder (melempar event
`RequestOpenNewTab`). File: shortcut (`.lnk`/`.desktop`/`.webloc`) →
aplikasi default; **gambar bitmap `.jpg`/`.jpeg`/`.png` → aplikasi
default**; **`.psd`/`.tif`/`.tiff`/`.webp` →
`OpenInPhotoshopSmart()`**; sisanya → aplikasi default.
`OpenWithCommand` → dialog "Open with…" OS
(`_platformService.OpenWithDialog`).

**Photoshop smart-open:** tulis file JSX sementara
`bmachine_smart_open_<guid>.jsx` di temp, jalankan via `Process.Start`
(`UseShellExecute = true`, mengandalkan asosiasi `.jsx` ke Photoshop):
bila `app.documents.length > 0` → `app.activeDocument.placeEmbedded(fileRef)`
(fallback `app.open` bila place gagal), bila tidak ada dokumen →
`app.open(fileRef)`; file temp dihapus setelah 5 detik; bila gagal total
→ error toast + fallback aplikasi default. Catatan: `.bmp`/`.gif` bukan
di daftar Photoshop maupun bitmap — jatuh ke "aplikasi default".

### 2.9 Hubungan dengan BATCH

- **`BatchActionCommand`:** folder terpilih saja → kirim
  `BatchAddFoldersMessage(folders)` +
  `NavigateToPageMessage("Batch")`. Binding-nya tidak ada di ketiga
  file ini (kemungkinan tombol di host/toolbar).
- **`BatchScriptCommand(BatchScriptOption)`:** sama, tapi
  `BatchAddFoldersMessage(paths, scriptFileName)` dengan
  `scriptFileName = Path.GetFileName(script.Path)`. **Dalam file ini
  tidak ada pemanggilan `ExecuteMaster` langsung** — eksekusi
  diserahkan ke penerima message (Batch) berdasarkan nama skrip yang
  dikirim.
- **Context menu "BUAT MASTER" (`axaml.cs` `OnContextRequested`):**
  saat menu konteks dibuka, cari `Separator` ber-`Tag="BatchMarker"`,
  hapus baris batch lama yang pernah di-inject, lalu cari
  `ScriptOptions.FirstOrDefault(s => s.OriginalName == "buat_master.py")`;
  bila ketemu, sisipkan **satu** `MenuItem` "BUAT MASTER" (bold, ikon
  skrip/`IconScript`, `Tag="BatchButtonRow"`, `Command =
  BatchScriptCommand`, parameter skrip itu) tepat setelah marker +
  separator `Tag="BatchSeparator"`. Bila `ScriptOptions` kosong atau
  `buat_master.py` tidak ada → tidak ada yang di-inject. Marker hanya
  ada di menu item tampilan **Vertical, Horizontal, dan Split**
  (kiri/kanan) — **menu background (`BackgroundMenu`) dan tampilan
  Thumbnail/Tiles tidak punya marker, jadi "BUAT MASTER" tidak muncul
  di sana**. `ScriptOptions` dimuat `LoadScriptsAsync()` dari
  `Scripts/Master/*.py` (+ `ScriptsPath` kustom dari
  `Configs.System.ScriptsPath`), diurutkan pakai `scripts.json`
  (`ScriptConfig`: Name/Order/IconKey; file `_`/tanpa alias memakai
  nama file, order 9999).
- **`CopyMasterBrowserHereCommand`:** folder dari parameter (bila
  direktori), atau folder terpilih pertama, atau `CurrentPath` →
  kirim `MasterBrowserSyncFromExplorerMessage(folderPath)`. Binding-nya
  tidak ditemukan di ketiga file ini.
- **`ExecuteFolderScript(BatchScriptOption)` — privat, bukan
  `[RelayCommand]`, dan tidak ditemukan pemanggilnya dalam ketiga file
  ini.** Bila dipanggil, ia menjalankan skrip langsung dari Explorer
  (tanpa pindah ke Batch): pilih master dari `Configs.Master.*`
  berdasarkan nama skrip (`wisuda` → Wisuda10RP/Wisuda8R; `manasik` →
  Manasik10RP/Manasik8R; `profesi` → Profesi/Sporty; `pasfoto`/`pas_foto`
  → PasFoto; lainnya → LastTemplatePath), lalu per folder secara
  sekuensial memanggil `batch_wrapper.py --target <nama-skrip>
  --pilihan <folder> --master … --master2 … --output <RootPath>
  --okebase <OkeBase>` via `_platformService.RunPythonScriptAsync`
  (env `BMACHINE_USER_NAME`), fire-and-forget. Statusnya (dipakai dari
  mana / dead code) **tidak bisa dipastikan dari ketiga file ini**.

### 2.10 Task Monitor (dari sisi Explorer)

`OnActiveTasksChanged` mendengarkan
`FileOperationManager.ActiveTasks.CollectionChanged`: **item ditambah
→ `ShowTaskMonitor()`** (+ subscribe `PropertyChanged` tiap task);
item dihapus → unsubscribe + `LoadItems()`; task selesai/gagal →
`LoadItems()`. `ShowTaskMonitorCommand`: **satu window bersama statis**
(`s_sharedTaskMonitorWindow`) untuk seluruh aplikasi — gaya TeraCopy,
**muncul tanpa mencuri fokus** (Show tanpa `Activate()`), `DataContext`
di-rebind ke `FileOperationsMonitorViewModel(_fileManager)` tiap kali
agar mengikuti manager yang benar-benar menjalankan task. Posisi: baca
`Configs.FileOperations.WindowPosition`; bila tersimpan dan masih di
layar → pakai itu, bila tidak → **pojok kanan-bawah layar utama**
(margin 16, dihitung ulang sekali setelah layout pertama karena
`SizeToContent="Height"`), lalu posisi tersimpan tiap pindah. **Window
tidak menutup sendiri saat task habis** — hanya tombol X yang menutup;
"Clear All Completed" hanya membersihkan riwayat.

---

## 3. Log Panel — sidebar kanan

`Views/LogPanelSidebar.axaml` (+`.cs`), `Views/PanelNavigationLayout.cs`,
`Views/DashboardView.axaml` (+`.cs`).

`UserControl`, `DataContext` = `DashboardViewModel`. Dihosting di
`DashboardView.axaml` sebagai kolom kanan grid
(`ColumnDefinitions="*, Auto, Auto"`): kolom 1 = resize handle 6px
(`LogPanelResizeHandle`, garis 2px), kolom 2 = `LogPanelSidebar` dengan
`Width={Binding LogPanelWidth}`, `MinWidth="180"`,
`IsVisible={Binding IsLogPanelOpen}`.

### 3.1 Buka/tutup & lebar

**Buka/tutup: BUKAN Ctrl+L** — tidak ada binding Ctrl+L untuk panel
ini di file yang dibaca. Caranya: (a) saklar "Terminal" di flyout
profil (`DashboardView.axaml.cs` `OnTerminalToggleClick` →
`IsLogPanelOpen`), (b) klik kanan di area panel
(`LogPanelSidebar.axaml.cs` `OnRootPointerPressed` →
`ToggleLogPanelCommand`). Ctrl+L di codebase ini adalah shortcut
Explorer ("Focus Address Bar", lihat §4.3).

**Lebar: 180–600px.** `DashboardView.axaml.cs`
`UpdateLogPanelMaxWidth`: `MaxWidth = Min(600, Max(180, lebarHost −
340))` (menyisakan ≥340px untuk dashboard); drag handle di
`OnResizeHandleMoved`: `Clamp(lebarAwal + delta, 180, maxWidth)`.
Lebar default tidak terverifikasi (ada di `DashboardViewModel`, di
luar daftar baca); fallback code-behind saat resize adalah 280.

### 3.2 Tab-tab panel & lebar kolom

Tab di `PanelTabsGrid` (`ColumnDefinitions="Auto,Auto,*,*,*"`): kolom 0
= **EXPLORER** (ikon folder, hanya tampil bila
`IsOutputExplorerVisible`), kolom 1 = separator, kolom 2 = **LOG**,
kolom 3 = **MASTER**, kolom 4 = **DOC** (disembunyikan bila
`BatchVM.IsDocFloating`). Ada juga RadioButton **PS/Photoshop** kelima
tapi `IsVisible="False"` — tab tersembunyi (lihat §3.6).

Pemilihan tab via `BatchVM.SelectedActivityMode`: 0 = Console/LOG,
1 = MASTER, 2 = Photoshop, 3 = DOC (`OnConsoleTabClick`,
`OnMasterTabClick`, `OnPhotoshopTabClick`, `OnDocTabClick`); EXPLORER
memakai flag terpisah `DashboardViewModel.IsExplorerPanelSelected`.
Search bar hanya tampil bila `BatchVM.IsSearchVisible &&
!IsExplorerPanelSelected`; footer hanya bila `BatchVM.IsStatusVisible
&& !IsExplorerPanelSelected`.

**Lebar kolom** — `Views/PanelNavigationLayout.cs`,
`GetColumnWidths(explorerVisible, docVisible)`, `ColumnCount = 5`:
kolom Explorer + separator = `Auto` bila Explorer tampil, `0` bila
tidak; LOG & MASTER selalu `*` (1 Star, sama besar); DOC = `*` bila
tidak floating, `0` bila floating. `UpdatePanelNavigationColumns`
menerapkannya tiap kali `IsOutputExplorerVisible` / `IsDocFloating`
berubah — jadi tidak ada kolom kosong yang tertinggal.

### 3.3 Tab EXPLORER di Log Panel

**Ini `OutputExplorerView` yang disematkan** — bukan tampilan lain:
`<views:OutputExplorerView DataContext="{Binding OutputExplorerVM}"
IsCompactHost="True" IsVisible="{Binding IsExplorerPanelSelected…}">`.
Jadi kontrol explorer yang sama dipakai dalam mode ringkas di side
panel. Mode compact: sidebar dijepit `lebar × 0,25` (40–160) dan kolom
detail disembunyikan bila lebar < 400 (`UpdateCompactLayout()` /
`ShowDetailsColumns`).

### 3.4 Tab LOG — konsol output subprocess

- **Isi:** header tab navigasi + search bar terintegrasi + area konten
  + footer status bar. Tab LOG: `ProgressBar` 2px di atas
  (`IsVisible={Binding IsProcessing}`, `Value=ProgressValue`,
  `Maximum=ProgressMax`, `IsIndeterminate={!IsDeterminateProgress}`)
  lalu `ScrollViewer` + `ItemsControl` berisi `LogItems` — tiap baris
  `SelectableTextBlock` font mono 13/18 dengan level warna. Auto-scroll
  ke bawah tiap ada item baru (`LogItems_CollectionChanged` →
  `ScrollToEnd`). Footer: dot status menyala (`StatusColor`, tooltip
  `ProcessStatusText`) + teks status uppercase mono + tombol aksi
  kontekstual: Stop (saat processing), Copy All Logs, Clear Log (tab
  LOG), Refresh Master (tab MASTER); saat tab MASTER aktif footer
  menampilkan `TARGET: {MasterBrowserTargetName}` dan indikator busy
  (`BusyMessage` + progress indeterminate).
- **Data tampil:** `DashboardViewModel.LogItems` (koleksi `LogItem`:
  `Message` + `Level` + `Color`). Level: Info / System / Success /
  Warning / Error / Debug / Accent — masing-masing memakai
  `Terminal*Brush` + kartu log dengan border kiri 3px dan latar tinted
  halus (mis. error `#10ef4444`, success `#104ade80`).
- **`Services/ProcessLogService.cs` (throttle):** `AddLog()` dari
  background thread masuk `ConcurrentQueue<string>`; timer flush tiap
  **100ms** (`FlushIntervalMs = 100`, maks 10x/detik) via satu
  `Dispatcher.UIThread.Post`; buffer di-drain per batch;
  `MaxLogs = 1000` (dipotong dari atas). Punya `Logs`
  (`ObservableCollection<string>`), `Clear()`, `Flush()`.
- **Progress & hasil script** TIDAK datang dari `ProcessLogService`,
  melainkan dari dua `FileSystemWatcher` di `%TEMP%`:
  `bmachine_progress.json` (`{"current","total","file"}` — debounce
  50ms, parse regex, update `ProgressValue/ProgressMax/
  IsDeterminateProgress` + `ProcessStatusText` "Running… N%" +
  memperbarui baris log terakhir `[Photoshop] Processing n/total:
  file` dengan cara hapus-tambah) dan `bmachine_result.json`
  (`{"title","lines"}` — debounce 100ms, menambah `=== title ===` +
  tiap baris, warna OK = LimeGreen, GAGAL/LEWATI = Orange).
- **Belum terverifikasi:** wiring antara `ProcessLogService.Logs` dan
  `DashboardViewModel.LogItems` (siapa yang subscribe / memetakan
  string → `LogItem` ber-level).
- Bonus: drop file teks ke panel
  (`.txt/.docx/.log/.md/.json/.xml/.csv/.ini/.cfg`) →
  `HandleDroppedLogFile` + pindah ke tab LOG (`OnDrop`).

### 3.5 Tab MASTER — BUKAN settings

Koreksi terhadap asumsi umum: tab MASTER di Log Panel **bukan halaman
setting path** — isinya **Master Browser**: `TreeView` dari
`BatchVM.MasterNodes` dengan `TreeDataTemplate` untuk `MasterNode`
(folder = baris nama tebal + ikon root/warna via `NodeIconKey`/
`NodeColorHex`; file `.psd/.psb` yang punya JPG = kartu thumbnail 2
kolom via `ThumbnailWrapPanel`, thumbnail di-decode lazy lewat
`LoadThumbnailCommand` saat baris muncul; file tanpa preview = baris
nama + ikon). Double-click / Enter / Space pada node →
`BatchVM.CopyMasterFileCommand` (salin file master ke target). Search
di atasnya terikat `BatchVM.MasterSearchText`.

Setting path master & Photoshop justru ada di
`Views/PathSettingsView.axaml` ("Explorer & Paths Configuration",
`PathSettingsViewModel`) — lihat §4.2.

### 3.6 Tab DOC — data untuk SEND-TEXT

`Views/DocPanelView.axaml` (DataContext Dashboard → `BatchVM`):
judul "INSTAN REDAKSI" + 4 tombol: Pengaturan Teks DOC
(`OnOpenDocSettingsClick` → `DocSettingsWindow`), Pop Out / floating
(`ToggleDocFloatingCommand`), tambah/hapus logo alternatif
(`ToggleSecondLogoCommand`), Reset semua (`ResetDocCommand`).

**Field utama:** `SchoolName` (TextBox "Nama Kelompok / Sekolah"),
`SchoolAddress` (TextBox "Alamat Lengkap", multi-baris), slot **Logo
Utama** (`SchoolLogoImage`) + opsional **Logo Alternatif**
(`SchoolLogoImage2`, mode 2 kolom bila aktif). Tiap field punya tombol
hover: ambil teks dari layer Photoshop (`PasteTextFromPSCommand`,
parameter `name`/`address`), Copy
(`CopySchoolNameCommand`/`CopySchoolAddressCommand`/`CopySchoolLogoCommand`),
logo bisa drag-drop file, Paste (`PasteLogoCommand` "1"/"2" — juga via
Ctrl+V global di `LogPanelSidebar`), dan Kirim ke Photoshop
(`SendLogoToPhotoshopCommand` "1"/"2").

**Multi-tab redaksi:** strip chip bernomor di bawah (`BatchVM.DocTabs`,
`NumberText` + `TabTooltip`) + chip "+" tambah tab baru kosong
(`OnDocAddChipPressed`) — jadi beberapa set nama/alamat/logo bisa
disimpan bergantian.

**Pengaturan teks sumber** (`Views/Dialogs/DocSettingsWindow.axaml`,
"Pengaturan DOC"): dua TextBox multi-baris — "Teks Nama (placeholder)"
(contoh `TK DELAPAN MATA AIR`) dan "Teks Alamat (placeholder)" (contoh
`JL. SARI ENDAH NO. 7A …`, satu variasi per baris); saat kirim ke
Photoshop hanya layer yang teksnya cocok dengan daftar ini
(case-insensitive, spasi berlebih diabaikan) yang diganti; daftar
kosong = field itu tidak diganti; tombol Default / Batal / Simpan.

**Belum terverifikasi:** isi persis `doc_info.json` (key JSON &
penulisnya) — penulisnya diduga di `BatchViewModel` (SEND-TEXT).

### 3.7 Tab PHOTOSHOP (tersembunyi)

Ada di XAML (`LogPanelSidebar.axaml`): `TreeView` dari
`BatchVM.PhotoshopNodes`, `IsVisible="False"` — baik TreeView-nya
maupun RadioButton tab "PS"-nya (`IsVisible="False"`, terikat
`BatchVM.IsPhotoshopVisible`). Jadi tab ini **tidak bisa dibuka dari
UI** saat ini. Isinya: tree folder gambar yang sama polanya dengan
MASTER, tapi tanpa template thumbnail; double-click node →
`BatchVM.SendToPhotoshopCommand` (`OnPhotoshopNodeDoubleTapped`).
Sumber foldernya dari "TAMBAHAN PHOTOSHOP" di `PathSettingsView`:
"Folder khusus gambar (jpg/png) yang akan muncul di tab PHOTOSHOP".

---

## 4. Explorer Settings

`Views/ExplorerSettingsView.axaml` (+`.cs`),
`Views/ExplorerSettingsWindow.axaml`,
`ViewModels/ExplorerSettingsViewModel.cs` (353 baris),
`Views/PathSettingsView.axaml` (+ ViewModel).

### 4.1 Pembagian window (koreksi asumsi)

Tab **General** UI-nya ada di `Views/ExplorerSettingsWindow.axaml`
(window 480×540, "Explorer Settings", tab segmented "General" vs
"Appearance & Shortcuts"): Default Explorer Path
(`DefaultExplorerPath` + Browse), View Mode List/Grid/Thumbnails
(terikat `OutputExplorerViewModel.IsVerticalLayout/
IsHorizontalLayout/IsThumbnailLayout`), Show Path Bar
(`IsShowPathBar`). Sedangkan `ExplorerSettingsView.axaml` adalah isi
tab kedua ("Appearance & Shortcuts") — berisi Appearance + daftar
shortcut.

### 4.2 General (ViewModel)

`PathLocalOutput` (`Configs.Path.LocalOutput`),
`DefaultExplorerPath` (`Configs.Explorer.DefaultPath`),
`ShowHiddenFiles` (default false), `ShowFileExtensions` (default
true), `DefaultViewMode` (default "Vertical"), `DefaultSortBy`
(default "Name"), `DefaultSortDescending`, `DefaultGroupBy` (default
"None") — semuanya tersimpan di DB kunci `Configs.Explorer.*` dan tiap
perubahan memanggil `SaveSettingAsync` → `_database.SetAsync` + siaran
`ExplorerSettingsChangedMessage` (kecuali perubahan path yang juga
mengirim pesan yang sama). Catatan: untuk
ShowHiddenFiles/ShowFileExtensions/Sort/Group **tidak ada kontrol UI**
di kedua axaml yang dibaca — propertinya ada dan tersimpan, tapi tidak
diekspos di jendela settings ini.

### 4.3 Appearance (di `ExplorerSettingsView.axaml`)

Content Zoom slider 75–200% (tombol −/+ 10%, `AdjustZoomCommand`
clamp 75–200; saat load, nilai <50 direset ke 100), Explorer Font
(AutoCompleteBox dari `AppFonts.Families`, default "Inter", preview
"Aa Bb Cc 0123", input tidak dikenal ditolak), Use 7tsp / OS System
Icons (toggle — ikon shell Windows dari shell32/imageres agar sinkron
tema 7tsp; bila mati memakai glyph minimal built-in), Custom 7tsp /
Icon Pack Path (TextBox + tombol Folder / File — file `*.7z`,
`*.dll/.res/.ico/.png/.exe/.icl` — / Reset).

### 4.4 Shortcuts (21 properti, semua bisa direkam ulang)

New Folder `Ctrl+Shift+N`, New File `Ctrl+Shift+T`, Focus Search
`Ctrl+L`, Delete `Ctrl+D`, New Window `Ctrl+N`, New Tab `Ctrl+T`,
Close Tab/Window `Ctrl+W`, Navigate Up `Alt+Up`, Back `Alt+Left`,
Forward `Alt+Right`, Rename `F2`, Permanent Delete `Shift+Delete`,
Focus Search Box `Ctrl+F`, Address Bar `Alt+D`, Address Bar 2
`Ctrl+L`, Switch Tab `Ctrl+Tab`, Copy `Ctrl+C`, Cut `Ctrl+X`, Paste
`Ctrl+V`, Copy Path `Ctrl+Shift+C`, Paste Path `Ctrl+Shift+V`.

Mekanisme: tombol Record → `StartRecordingCommand` (kirim
`ExplorerSettingsFocusMessage` agar view fokus) →
`ExplorerSettingsView.axaml.cs` `OnKeyDown` menangkap gesture (Esc =
batal, tombol modifier saja diabaikan) → `ApplyRecordedShortcut` →
simpan ke `Configs.Explorer.<Nama>` + siaran
`ExplorerShortcutsChangedMessage`; tiap perubahan properti juga
auto-save via `On<Prop>Changed`. Anomali kecil: `ShortcutAddressBar2`
tidak punya baris di axaml (grid hanya menampilkan
`ShortcutFocusSearch` untuk label "Focus Address Bar (Ctrl+L)"), jadi
ada dua shortcut default `Ctrl+L` yang berbeda key-nya.

**Catatan penting:** Ctrl+L di Explorer = Focus Address Bar — BUKAN
toggle Log Panel (lihat §3.1).

### 4.5 Path Settings (Paths Configuration)

`Views/PathSettingsView.axaml` ("Explorer & Paths Configuration",
`PathSettingsViewModel`): `PathLocalOutput` (Default Local Output,
opsional), `PathOkeBase` (Master OKE BASE, opsional), `PathPhotoshop`
(executable Photoshop, wajib untuk JSX + tombol
`FixPhotoshopWarningCommand` yang menulis `PSUserConfig.txt`),
`OfflineStoragePath` (Attachment Download Path Trello), grup Profesi
(`PathProfesi` 10RP, `PathProfesi8R`, `PathSporty`), Manasik
(`PathManasik10RP`, `PathManasik8R`), Wisuda (`PathWisuda10RP`,
`PathWisuda8R`), Pas Foto (`PathPasFoto`), plus dua expander:
**TAMBAHAN MASTER** (daftar `AdditionalMasterPaths` — tiap item bisa
rename, edit path manual, browse, hapus, drag-reorder, atur ikon +
warna root) dan **TAMBAHAN PHOTOSHOP** (daftar
`AdditionalPhotoshopPaths` + tombol naik/turun).

---

## 5. Task Monitor — operasi file background

`ViewModels/FileOperationsMonitorViewModel.cs` (45 baris),
`Services/FileOperationManager.cs`, `Views/TaskMonitorView.axaml`
(+`.cs`), `Views/TaskMonitorWindow.axaml` (+`.cs`).

- `FileOperationsMonitorViewModel`: wrapper tipis di atas
  `FileOperationManager` process-wide (sengaja tidak meminjam VM
  explorer mana pun agar tidak terikat tab yang sudah tertutup).
  Mengekspos `ActiveTasks` + 4 command: `CancelTaskCommand` →
  `CancelTask`, `RemoveTaskCommand` → `RemoveTask`,
  `RetryTaskCommand` → `RetryTask`, `ClearCompletedCommand` →
  `ClearCompletedTasks`.
- `FileOperationManager`: antrean serial (satu tugas jalan dalam satu
  waktu, `ProcessQueue` + `ConcurrentQueue`). Jenis tugas
  (`FileTaskType`): **Copy / Move / Delete** — file maupun folder
  (direktori disalin rekursif). Tiap `FileTaskItem`: nama,
  source/destination path, progress 0–100, status (Pending/Running…/
  Completed/Failed/Cancelled/Merging…), kecepatan MB/s, ETA `hh:mm:ss`,
  info ukuran ("12.4 MB / 180.0 MB"), `ErrorMessage`,
  `CancellationTokenSource`. Copy memakai stream async 64KB dengan
  throttle update UI ~8x/detik (125ms); Move satu drive =
  `Directory.Move` (atau copy+delete bila tujuan sudah ada = merge),
  beda drive = copy+delete; Delete rekursif per file. Cancel =
  `Cts.Cancel()` + status "Cancelling…"; Retry = membuat tugas baru
  dari source/destination lama; ClearCompleted menghapus yang
  Completed/Cancelled/Failed; daftar dibatasi 50 item (terlama dibuang).
- `TaskMonitorWindow`: window 420px lebar, `SizeToContent="Height"`,
  `WindowStartupLocation="Manual"`, `Topmost="False"`,
  `ShowInTaskbar="False"`, `SystemDecorations="BorderOnly"`. Header
  bisa di-drag (`BeginMoveDrag`), tombol collapse (`IsExpanded`,
  default true, daftar max 340px) dan Close. Tiap kartu tugas: badge
  COPY (biru) / MOVE (oranye) / DELETE (merah), nama, pill status
  (Berjalan/Selesai/Gagal/Dibatalkan), baris "dari … → ke …" (atau
  pesan error merah bila gagal), progress bar + % / ukuran / kecepatan
  / ETA. Tombol per tugas: Batalkan (saat berjalan), Coba lagi (saat
  gagal), Hapus dari daftar (saat tidak berjalan). Footer: tombol
  "Bersihkan yang selesai".
- **Perilaku window:** posisi tersimpan (`LoadPositionAsync` /
  `SavePositionAsync`, dipanggil saat window dibuat/dipindah, hanya
  saat `WindowState == Normal`). Tidak ada logika auto-close — window
  hanya tertutup via tombol Close, tugas tetap hidup di manager.
  "Tidak mencuri fokus" didukung sebagian (`Topmost=False`,
  `ShowInTaskbar=False`). **Tidak ditemukan di file yang dibaca:**
  siapa yang membuat/menampilkan window ini (kode pemanggil tidak
  termasuk daftar baca), apakah satu instance bersama, apakah muncul
  otomatis saat tugas pertama di-queue, dan apakah posisinya pojok
  kanan-bawah gaya TeraCopy — `WindowStartupLocation="Manual"` +
  delegate posisi menunjukkan posisi ditentukan pemanggil, tapi
  pemanggilnya belum terverifikasi. (Dari sisi Explorer,
  `OnActiveTasksChanged` menunjukkan task baru → `ShowTaskMonitor()`
  — satu window bersama statis, muncul tanpa `Activate()`, posisi dari
  `Configs.FileOperations.WindowPosition` atau pojok kanan-bawah layar
  utama margin 16.)

---

## 6. Master File Browser — lokasi menurut kode (PERTANYAAN TERBUKA)

**Ditemukan, dan lokasinya BUKAN di Log Panel.** Master File Browser
adalah panel overlay di dalam **tab BATCH**
(`Views/DashboardView.axaml`): dua panel — "MASTER FILE BROWSER PANEL
(LEFT)" di dalam panel SOURCE (`IsVisible={Binding
BatchVM.IsMasterBrowserOpenLeft}`) dan "MASTER FILE BROWSER PANEL
(RIGHT)" di dalam panel OUTPUT (`IsMasterBrowserOpenRight`). Keduanya
menampilkan tree `BatchVM.MasterNodes` yang sama + search
`MasterSearchText` ("Cari Master…") + header "COPY TO
{MasterBrowserTargetName}" + tombol tutup (`CloseMasterBrowserCommand`);
double-click node → `CopyMasterFileCommand` (salin master ke folder
target yang dipilih).

**Catatan penting:** tombol pembukanya (`ShowMasterBrowserCommand`,
parameter "Left"/"Right", tooltip "Buka Master File Browser") ada di
inline action bar tiap baris tree SOURCE/OUTPUT — tetapi panel action
bar tersebut saat ini `IsVisible="False"` di kedua tree, dan di
sampingnya ada komentar `<!-- MASTER FILE BROWSER (Removed) -->`.
Jadi panel browser-nya masih ada di kode, tapi pemicu UI-nya sedang
disembunyikan/dinonaktifkan.

**Jangan dikacaukan dengan tab MASTER di Log Panel** (§3.5): itu
permukaan kedua untuk data yang sama (`MasterNodes`) dengan tampilan
thumbnail 2 kolom — bukan "Master File Browser" yang dimaksud. Sumber
folder untuk keduanya dikonfigurasi di `PathSettingsView` (§4.5), dan
teks di sana menegaskan: folder "TAMBAHAN MASTER … akan muncul di
Master File Browser".

**Beda dengan keyakinan abeng (26 Nov 15:57):** abeng bilang Master File
Browser "ada di side panel / log panel navigasi button". Menurut kode,
browser itu overlay BATCH (trigger-nya disembunyikan), sementara yang
ada di side panel adalah tab MASTER (thumbnail browser, data sama).
Kemungkinan abeng mengingat tab MASTER itu sebagai "Master File
Browser". **Keputusan ditunda** (arahan abeng: bertahap, jangan dibangun
sekarang) — saat tiba waktunya, tanyakan ke abeng: mau dihidupkan lagi
trigger BATCH-nya, atau membangun browser di side panel (tab MASTER)
sebagai lokasi resminya?

---

## 7. Peta file & kunci DB

| Topik | File |
|---|---|
| Window multi-tab | `Views/ExplorerWindow.axaml(.cs)`, `ViewModels/ExplorerWindowViewModel.cs` |
| Explorer lokal | `Views/OutputExplorerView.axaml(.cs)`, `ViewModels/OutputExplorerViewModel.cs` |
| Log Panel sidebar | `Views/LogPanelSidebar.axaml(.cs)`, `Views/PanelNavigationLayout.cs` |
| Tab DOC | `Views/DocPanelView.axaml`, `Views/Dialogs/DocSettingsWindow.axaml` |
| Explorer Settings | `Views/ExplorerSettingsView.axaml(.cs)`, `Views/ExplorerSettingsWindow.axaml`, `ViewModels/ExplorerSettingsViewModel.cs` |
| Path Settings | `Views/PathSettingsView.axaml`, `ViewModels/PathSettingsViewModel.cs` |
| Task Monitor | `ViewModels/FileOperationsMonitorViewModel.cs`, `Services/FileOperationManager.cs`, `Views/TaskMonitorView.axaml(.cs)`, `Views/TaskMonitorWindow.axaml(.cs)` |
| Master nodes (data) | `Models/MasterNode.cs` (dirujuk `BatchVM.MasterNodes`) |
| Log throttle | `Services/ProcessLogService.cs` |

Kunci DB (SQLite `KeyValueStore`) yang dipakai Explorer:

| Kunci | Isi |
|---|---|
| `Explorer_PinnedTabs` | Tab yang di-pin (PinnedPaths, CustomLabels, CustomColors) |
| `ExplorerWindow_Width` / `ExplorerWindow_Height` | Ukuran window Explorer |
| `Configs.Explorer.DefaultPath` | Root default Explorer (fallback `Configs.Master.LocalOutput`) |
| `Configs.Explorer.QuickAccess` | Item Quick Access |
| `Configs.Explorer.SidebarWidth` | Lebar sidebar |
| `Configs.Explorer.ShowPathBar` | Path bar tampil/sembunyi |
| `Configs.Explorer.FolderSettings.<Base64(path)>` | Setting tampilan per-folder |
| `Configs.Explorer.SortBy` / `IsSortDescending` / `GroupBy` | Sort/group global |
| `Configs.Explorer.ViewMode` / `DefaultViewMode` | Mode view global (dua kunci berbeda — jangan ditiru) |
| `Configs.Explorer.ContentZoom` | Zoom konten 50–250% |
| `Configs.Explorer.FontFamily` | Font Explorer |
| `Configs.Explorer.Shortcut*` | 21 shortcut Explorer (lihat §4.4) |
| `Configs.FileOperations.WindowPosition` | Posisi window Task Monitor |
| `Configs.Master.LocalOutput` | Base folder OUTPUT (`D:\#GAWENA`) |
| `Configs.Master.AdditionalPaths` / `PhotoshopPaths` | Folder tambahan Master Browser / tab Photoshop |
| `Configs.System.ScriptsPath` | Override folder Scripts |
| `Settings.Dash.Explorer` | Visibilitas tab EXPLORER di Log Panel |
| `Dashboard.LogPanelWidth` | Lebar Log Panel |

---

## 8. Aturan untuk port Rust/Slint

### 8.1 Yang wajib ditiru (alur — jangan sampai salah)

- Root/fallback path (`DefaultPath` → `LocalOutput`); empty state
  "Local Output belum disetting" bila root hilang.
- Breadcrumb **display-only** + address bar edit (klik → edit, Enter
  commit, Esc batal); Up tidak bisa naik di atas RootPath; stack
  Back/Forward; pindah folder mengosongkan search.
- Search streaming cap 5000 + debounce 300 ms + batch 64/120 ms;
  `*`/`?` diperlakukan literal; Enter = buka hit pertama; hasil
  menampilkan kolom lokasi.
- Type-ahead 800 ms (buffer reset bila jeda > 800 ms, Esc kosongkan).
- 5 layout termasuk Split berbasis grup ekstensi terbanyak (folder =
  grup "Folder"); satu `SelectedItems` bersama di Split.
- Folder dulu, lalu shortcut, lalu file — di semua sort; natural sort;
  Descending membalik tiap kelompok terpisah.
- Group Date default (Today → Yesterday → … → A long time ago);
  header group tidak bisa dipilih.
- Setting per-folder (Base64 path, hanya folder persis itu) vs global;
  checkbox "This Folder Only".
- Watcher debounce 400 ms, event Created/Deleted/Renamed saja
  (tanpa Changed); reload tidak jalan saat search.
- Cut statis lintas tab + item ter-cut diredupkan (opacity 0,45);
  cut + paste ke folder sama = no-op; paste folder ke subtree-nya
  sendiri diblok; penamaan bentrok "x - Copy", "x - Copy (2)".
- Undo maks 50; Recycle Bin tidak bisa di-undo (hanya info);
  permanent delete tanpa entri undo; permanent delete SELALU
  konfirmasi dulu; delete ke Recycle Bin tanpa konfirmasi; folder yang
  sedang dibuka + ancestor-nya tidak bisa dihapus.
- Rename inline: hanya bagian nama yang di-select (ekstensi tidak
  ikut); Enter commit, Esc cancel, LostFocus commit.
- Photoshop smart-open: place ke dokumen aktif bila ada, else open;
  `.jpg/.jpeg/.png` → aplikasi default.
- Injeksi "BUAT MASTER" hanya dari `buat_master.py` setelah
  `BatchMarker`; hanya di menu item Vertical/Horizontal/Split — TIDAK
  di menu background dan TIDAK di Thumbnail/Tiles.
- Task Monitor: satu window bersama, muncul tanpa mencuri fokus di
  kanan-bawah (margin 16), posisi persisten, tidak menutup otomatis.
- Log Panel: buka/tutup via saklar "Terminal" di flyout profil (BUKAN
  Ctrl+L — itu Focus Address Bar Explorer); lebar 180–600px, resize
  handle 6px; tab EXPLORER/LOG/MASTER/DOC; tab EXPLORER =
  OutputExplorerView mode compact.
- Tab MASTER Log Panel = Master Browser (thumbnail 2 kolom,
  double-click = copy ke target) — BUKAN halaman settings.
- Tab DOC = INSTAN REDAKSI (nama/alamat/logo + multi-tab redaksi).
- Tab PHOTOSHOP tersembunyi (tidak bisa dibuka dari UI).

### 8.2 Yang jangan ditiru mentah-mentah

- Inkonsistensi kunci `ViewMode` vs `DefaultViewMode` (dua kunci
  untuk setting yang sama).
- `ItemsCount` folder yang selalu 0 (folder tampil "0 items").
- `ExecuteFolderScript` yang status pemakaiannya belum jelas
  (privat, tanpa pemanggil yang ditemukan).
- Pil type-ahead VM (`TypeAheadText`/`IsTypeAheadVisible`) yang tidak
  pernah diisi dari code-behind (kemungkinan mati).
- Toast batch rename yang memakai `Min(nama, item)` meski sebagian
  rename gagal.
- `CommitRename` pada nama kosong yang keluar dari mode edit tanpa
  reload (nama di UI berpotensi tetap kosong sampai reload).

### 8.3 Pertanyaan terbuka (jangan ditebak)

1. **Lokasi resmi Master File Browser** — kode bilang overlay BATCH
   (trigger disembunyikan); abeng bilang side panel / log panel.
   Tab MASTER di Log Panel adalah permukaan kedua untuk data yang
   sama. Tanyakan ke abeng saat tiba waktunya (lihat §6).
2. **Cara membuka ExplorerWindow** — pemicu `NewExplorerWindowCommand`
   tidak ditemukan di file yang dibaca.
3. **Wiring `ProcessLogService.Logs` → `DashboardViewModel.LogItems`**
   — siapa yang subscribe / memetakan string → `LogItem` ber-level.
4. **Isi persis `doc_info.json`** — key JSON & penulisnya (diduga di
   `BatchViewModel`).
5. **Pemanggil TaskMonitorWindow** — siapa yang membuat/menampilkan
   window ini; apakah satu instance bersama & auto-show sudah pasti
   (dari sisi Explorer: ya, via `OnActiveTasksChanged`).
6. **Lebar default Log Panel** — hanya fallback 280 di code-behind.
7. **Apakah beberapa ExplorerWindow bisa dibuka bersamaan** — kode
   `RenamePinned` mengisyaratkan mungkin, tapi tidak terverifikasi.
8. **Kombinasi tombol literal shortcut Explorer** — gesture diambil
   dari properti settings, nilai pastinya ada di §4.4 (default) tapi
   bisa diubah user.
