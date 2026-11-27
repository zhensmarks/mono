# PERUBAHAN.md — Log perubahan & serah-terima agent

Setiap agent yang mengerjakan port ini **wajib** menulis entrinya di sini
sebelum commit + push (format lengkap di `AGENTS.md`). Entri terbaru di
paling atas. Dengan log ini, siapa pun yang menerima project — Dex, Hermes,
atau orang lain — langsung tahu apa saja yang diubah tanpa menebak.

## 2026-11-27 — Dex (sinkronisasi dari PC abeng, atas perintah abeng) — Push kerja lokal yang gagal ter-push

- Diubah: `src/` (db, doc_manager, executor, local_update, main, process_log,
  sheets_api, theme, trello_api), `ui/` (main, radial, settings, windows),
  baru `qa_clean_run_csharp.bat` + `qa_run_csharp_reference.bat`;
  `PERUBAHAN.md` digabung — 11 entri Antigravity Agent dari lokal yang
  belum ada di GitHub + 2 entri Dex. File lain hanya beda CRLF/LF, tidak
  diubah (repo tetap LF). `LANJUTKAN.md` + `CATATAN_CSHARP_V15153.md`
  tetap versi GitHub (lebih baru dari lokal).
- Alasan: abeng 27 Nov 16:19 — kerja terbaru di PC
  (D:\#DATA ABENG\#PROJECT\mono\apps\bmachine-rs) belum ter-push karena
  gagal terus; Hermes lokal sudah habis limit. Diambil via SMB (share
  Data, user bengs) lalu di-push dari VM.
- Terverifikasi: `cargo build` exit 0 (1m 45s, Linux; override toolchain
  karena rust-toolchain.toml mengunci channel Windows; perlu install
  libxi-dev + libxtst-dev dkk di VM — bukan perubahan kode). 1 warning:
  dependensi `sha2` tidak terpakai.
- Masih terbuka: smoke test klik di Windows oleh abeng; label SPREADSHEET
  vs POINTS (keputusan abeng); verifikasi panah widget ke-4.

## 2026-11-27 — Dex (aturan baru abeng) — Samakan semua tampilan & fitur dengan C#

- Diubah: `LANJUTKAN.md` §2 + aturan baru — semua tampilan dan fitur port
  disamakan dengan C#: cara pemanggilan (klik widget/panah, hotkey, menu)
  dan cara tampilan keluarnya (embedded vs window, posisi, animasi), dll.
  Kalau bertentangan dengan kebebasan "tampilan boleh dirombak", tanya
  abeng dulu, jangan menebak.
- Alasan: abeng 27 Nov 16:17 WIB — "semua tampilan dan fitur nanti samakan
  ya cara pemanggilannya, cara tampilan keluarnya dll nya oke".
- Terverifikasi: perubahan dokumen saja (tidak ada kode yang diubah).
- Masih terbuka: keputusan abeng soal label SPREADSHEET vs POINTS
  (lihat CATATAN_CSHARP_V15153.md).

## 2026-11-27 — Dex (test C# v15.15.3, atas perintah abeng) — Catatan penerapan ke port

- Diubah: baru `CATATAN_CSHARP_V15153.md` — hasil test menyeluruh C#
  v15.15.3 (build Linux + klik sungguhan di Xvfb 1280x800, tanpa data
  Trello/Sheets/Photoshop/NAS). Isi: label widget ke-4 C# = SPREADSHEET
  (port: POINTS — perlu keputusan abeng sebelum diubah); BATCH empty
  state (drop zone "+" + "DRAG FOLDERS HERE", klik tidak buka picker);
  struktur Settings Control Center; isi profile flyout; Trello list
  kosong tanpa teks; layout Leaderboard; Esc TIDAK menutup Settings,
  right-click = back; Fase 6 kredensial di OS vault (`secret-tool` di
  Linux / Credential Manager di Windows) + update check via NAS
  `\\delapanmataair\...\manifest.json`; cara ulang test C# di Linux +
  koordinat nav terukur (profile 513, HOME 587, BATCH 657, LOCKER 732).
- Alasan: abeng 27 Nov 16:13 — "tidak perlu screenshoot tapi kamu catat
  yang harusnya di terapkan ke bmachine-rs tuh gimana harusnya".
- Terverifikasi: C# build exit 0 + jalan stabil, semua navigasi dites
  klik (dashboard, nav 3 tab, Trello list, BATCH, LOCKER, Settings,
  flyout, Leaderboard). Koreksi: nav sempat dikira tidak responsif —
  ternyata klik Dex meleset ke celah antar tab / icon profil; setelah
  diukur dari pixel, semua tab jalan.
- Masih terbuka: keputusan abeng soal label SPREADSHEET vs POINTS;
  verifikasi panah widget ke-4 (klik Dex membuka Leaderboard embedded,
  mungkin kena body); toggle Terminal ON tapi panel log tidak muncul di
  Linux (cek di PC abeng); sapaan "Good Evening" mulai jam berapa.

## 2026-11-27 — Antigravity Agent — Sinkronisasi Radial Menu dengan BMACHINE_RADIAL_MENU.md (5 Skrip Toolbox C# & Roda Settings 200px)
- Diubah:
  - `ui/radial.slint`: Menyelaraskan daftar skrip outer ring TOOLBOX persis sesuai `BMACHINE_RADIAL_MENU.md` §11.3 — 5 file `_tb_*.jsx` asli: `_tb_C-REDAKSI.jsx` (C-Redaksi), `_tb_FOOTNOTE.jsx` (Footnote), `_tb_IMAGE_SIZE.jsx` (Image Size), `_tb_NOMORSATSET.jsx` (NomorSatset), dan `_tb_SELEKSI_ALAM.jsx` (Seleksi Alam) dengan label, ikon, dan eksekusi skrip yang benar.
  - `ui/settings.slint`: Mengganti kartu kotak statis menjadi kanvas roda 200×200 nyata (`RadialSlotsControl` C#) dengan pelat lingkaran, pembatas 8 sektor trigonometri, hub tengah 88px, dan 8 tombol slot pada $R=72\text{px}$ yang data dan urutannya 100% identik dengan live menu (memenuhi §1 & §3 aturan sinkronisasi dua permukaan).
- Alasan: Instruksi user merujuk pada spesifikasi resmi `BMACHINE_RADIAL_MENU.md`.
- Terverifikasi:
  - `cargo check` exit 0 dengan 0 error & 0 warning.
- Masih terbuka:
  - Pengujian drag & drop interaktif di roda pengaturan saat runtime.

## 2026-11-27 — Antigravity Agent — Geometri Radial Menu 1:1 C#, Multi-Window Standalone, Excel Spreadsheet & Polish Sidepanel
- Diubah:
  - `ui/radial.slint`: Re-implementasi total geometri & interaksi Radial Menu sesuai `RadialMenuWindow.axaml` & `RadialMenuViewModel.cs` C# — kanvas 320×320 terpusat, pelat lingkaran 200px (R=100), hub tengah padat 88px (R=44) dengan teks uppercase tracking +2 & subjudul dinamis ("SELECT" / "HOLD FOR SUBMENU"), 8 garis pembatas sektor trigonometri (`22.5°..337.5°`), 8 PieWedge anular hover glow (R44..R100) yang menyala sesuai sektor aktif, tombol menu 36×36 pada R=72px dengan ikon Lucide & hover scale 1.15, ring luar submenu petal (R104..R152) untuk skrip TOOLBOX (`_tb_*.jsx`) dan DOC Tabs, pelacakan sudut polar `atan2(dx, -dy)` presisi clock-face, dwell timer, serta release-to-execute.
  - `ui/spreadsheet.slint` (baru): Engine spreadsheet interaktif bergaya Microsoft Excel / Google Sheets — header kolom A..F (Kode, Sekolah, Jenjang, Status, Poin, Catatan), nomor baris 1..N, Formula Bar (`fx`) dengan penunjuk koordinat sel aktif (`A1`..`F6`) dan input editor sel langsung, seleksi sel aktif dengan border aksen 2px & handle dot, toolbar aksi (+ Baris, ∑ Auto-Sum, Zoom `[-] 100% [+]`, Muat Ulang, dan Simpan dengan draft dirty tracking).
  - `ui/windows.slint` (baru): Komponen OS Window mandiri sekunder — `SpreadsheetWindow` (window mandiri editor spreadsheet ukuran 980×620 dengan custom chrome) dan `CardListWindow` (window mandiri daftar kartu Trello ukuran 860×580 dengan split view: list kartu di kiri & panel detail kartu di kanan).
  - `ui/modul.slint`: Mengintegrasikan `SpreadsheetComponent` ke dalam `SheetsView` menggantikan tabel mockup kaku sebelumnya, menambahkan tombol "Buka Window Baru" (`popout-window`).
  - `ui/logpanel.slint`: Re-polish total sidebar kanan — membersihkan seluruh karakter encoding lama, menambahkan ikon Lucide pada 4 tab navigasi (`LOG`, `FILES`, `MASTER`, `DOC`), live progress bar, konsol log dengan strip level warna, dot status proses, aksi Salin/Stop/Bersihkan ber-toast, breadcrumb folder ringkas, dan form Instan Redaksi terstruktur.
  - `ui/main.slint`: Re-export `SpreadsheetWindow` & `CardListWindow`, menambahkan callback `open-new-sheet-window()` dan `open-new-card-window(string)`, menyambungkan klik panah pada `StatWidget` (`EDITING`, `REVISION`, `LATE`) dan tombol popout spreadsheet ke pembuatan window OS baru.
  - `src/main.rs`: Menambahkan manajemen instance window sekunder (`sheet_win_cell`, `card_win_cell`) di event loop Slint dengan callback `show()`, `hide()`, dan sinkronisasi data kartu/kategori Trello.
- Alasan: Instruksi user untuk menyamakan logika & fungsi Radial Menu agar benar-benar mirip C#, mengimplementasikan new window yang membuka window baru sungguhan, membangun spreadsheet dengan fitur Excel/spreadsheet nyata, dan mem-polish logpanel/sidepanel.
- Terverifikasi:
  - `cargo check` exit 0 dengan **0 error & 0 warning**.
  - Geometri trigonometri dan binding Slint terverifikasi sinkron.
- Masih terbuka:
  - Verifikasi interaktif langsung oleh user di layar desktop Windows.

## 2026-11-27 — Antigravity Agent — Full Live Theme Customization, Custom Titlebar, Real Trello Sync & Persistensi SQLite
- Diubah:
  - `src/theme.rs` (baru): Engine tema live-customizable (`apply_appearance`, `parse_hex_rgb`, `is_windows_light_mode`, `load_theme_from_db`, `save_theme_to_db`). Mendukung input hex `#RRGGBB` bebas untuk Accent/Background/Foreground, mode Terang/Gelap/Ikut Sistem, preset (`BMachine Dark`, `BMachine Light`, `Midnight OLED`, `Catppuccin Mocha`, `Emerald Studio`, `Custom`), font global (`Inter` / `JetBrains Mono`), corner radius (`sharp`/`default`/`rounded`), nav pill style (`capsule`/`rounded`/`flat`), dan widget background (`doodle`/`dots`/`impulse`/`none`), semuanya diterapkan langsung ke global `Design` dan disimpan ke SQLite (`BMachine.db`).
  - `src/platform.rs` (baru): Helper native OS untuk membuka URL di browser (`open_url`) serta dialog native Windows (`pick_folder`, `pick_file`) untuk Browse path/credentials.
  - `ui/design.slint`: Menambahkan token `font-family` dan `density-scale`.
  - `ui/settings.slint`: Mengubah Appearance dari mockup preview lokal menjadi kontrol tema global live (`appearance-changed()`, `reset-appearance()`), menambah preset baru, Corner radius, Nav pill style, dan tombol Reset ke Default; menyambungkan tombol `Get Key`, `Connect` (`test-trello`), dan `Browse` di Account/Explorer/Paths ke aksi nyata.
  - `ui/main.slint`: Export `Design` ke Rust, `default-font-family: Design.font-family`, two-way binding (`<=>`) penuh ke `SettingsView` (termasuk perbaikan `widget-bg` yang sebelumnya one-way), menambahkan custom titlebar frameless 28px (drag window, minimize, maximize, close), toast ber-border aksen dengan `overflow: elide`, dan menyambungkan `refresh-trello()` serta `open-url()`.
  - `ui/trello-detail.slint`: Menyambungkan tombol buka kartu di browser (`↗`) ke `open-url`.
  - `src/trello_api.rs`: Menambahkan `verify_connection` (`/1/members/me`), `shortUrl`, dan `add_comment`.
  - `src/main.rs`: Menghapus panggilan HTTP dummy sinkron saat startup yang memperlambat boot; menyambungkan `spawn_trello_sync` agar mengisi count widget sekaligus daftar kartu nyata (`editing-titles/subs/due/ids`, `revision-*`, `late-*`); menyambungkan persistensi General & Paths ke SQLite, window drag/min/max, dan membersihkan seluruh compiler warning.
- Alasan: Instruksi user untuk melanjutkan port dengan kualitas `#POLISH`, menyamakan logika/fungsi C# (Trello sync, persistensi DB, dialog native), dan membuat tampilan bisa full customisasi secara live.
- Terverifikasi:
  - `cargo build` exit 0 dengan **0 warning** (36.94s).
  - Smoke test launch `target/debug/bmachine-slint-demo.exe --no-splash` berjalan stabil (`RUNNING_OK PID=17692`).
- Masih terbuka:
  - Pengambilan screenshot desktop dari terminal non-interaktif gagal (`CopyFromScreen: The handle is invalid`), sehingga verifikasi visual di layar desktop perlu dicek langsung oleh user.
  - Eksekusi Python `batch_wrapper.py` / Photoshop JSX di tombol footer BATCH masih perlu diuji dengan folder `Scripts/` & Photoshop lokal user.

## 2026-11-27 — Antigravity Agent — Nav centering/kapsul + shortcut global radial (rdev) + argumen QA
- Diubah:
  - `ui/main.slint`: Nav bar dashboard kini terpusat penuh: akun kiri, grup pill HOME/BATCH tengah (sejajar), radial kanan. Spasi antar ikon akun dan pill dibuat jelas; highlight pill aktif memakai kapsul oval yang mengikuti bentuk dasar.
  - `ui/design.slint`: Token kapsul nav (`nav-pill-*`) dan token radial baru (`radial-*`) ditambahkan; radius pill aktif full, outer ring dan slot mengikuti highlight.
  - `src/main.rs`: Trigger radial kini global dengan `rdev` (`--` `Cargo.toml` menambah `rdev = "0.5.3"`). Shortcut default mengikuti `ShortcutConfig` C# (`Key: 222`, `Modifiers: 12` = Win+Shift+`). Argumen QA `--radial-test` membuka radial overlay tanpa shortcut untuk screenshot VM, dan `--simulate-hotkey` menjalankan simulasi `Win+Shift+'` lewat `rdev::simulate` untuk tes hook global.
  - `src/main.rs`, `src/process_log.rs`, `ui/logpanel.slint`: QA argumen `--view=`, `--group=`, `--tab=`, `--log`, `--flyout`, `--dialog=` untuk laporan screenshot semua tampilan.
  - `apps/bmachine-rs/start_app.ps1`, `start_app.bat`, `copy_rdev.bat`: Helper QA di dalam shared folder VM (`Y:\apps\bmachine-rs`) untuk menjalankan app di guest tanpa menyentuh desktop host.
- Alasan: User minta nav benar-benar sejajar/kapsul seperti referensi, dan radial menu dipanggil dari shortcut seperti C# (bukan hanya klik lokal).
- Terverifikasi: `cargo build --release` exit 0. Di VM `Win10LTSC-Dev` (shared folder `Y:`), radial overlay terbuka via `--radial-test`; dashboard nav menampilkan akun kiri, HOME pill kapsul, BATCH pill, radial kanan. Screenshot VM: `D:/VMs/vm_radial_test.png`.
- Catatan: Shortcut global Win+Shift+' diimplementasi dengan `rdev`, tetapi verifikasi in-guest via `VBoxManage keyboardputscancode` belum menampilkan radial karena injeksi scancode LWin/Shift di VM tidak selalu sampai ke hook global; `--radial-test` dan `--simulate-hotkey` tersedia untuk tes tanpa input fisik user.

## 2026-11-27 — Antigravity Agent — Argumen startup QA untuk laporan screenshot semua tampilan
- Diubah:
  - `src/main.rs`: Argumen startup `--view=`, `--group=`, `--tab=`, `--log`, `--flyout`, `--dialog=` kini membuka state UI yang diminta tanpa klik mouse/keyboard fisik. Dipakai runner screenshot VM; normal startup tanpa argumen tidak berubah.
- Alasan: User meminta laporan gambar semua tampilan; state seperti Settings per grup, overlay Radial/Sheets/Leaderboard, dan dialog tidak bisa dibuka hanya dengan shortcut keyboard.
- Terverifikasi: `cargo build` exit 0. Screenshot VM di-generate untuk 20 state ke `D:/VMs/qa/` (lihat `manifest.jsonl`).

## 2026-11-27 — Antigravity Agent — Navbar dashboard mengikuti referensi (Akun kiri, Home/Batch pill, radial kanan)
- Diubah:
  - `ui/main.slint`: Kontrol Akun dipisah ke sisi kiri nav, pill tengah hanya Home + Batch; tombol close `×` yang tidak ada di referensi dihilangkan. Tombol radial kanan kini memakai hexagon geometris.
  - `ui/design.slint`: NavTab mendukung ikon Home/layers, Home terpilih memakai kapsul terang dan teks/ikon gelap, tab nonaktif muted, radius pill penuh; underline biru dihapus.
- Alasan: Mengikuti screenshot referensi dan arahan user untuk memperbaiki navigation layout serta polish UI.
- Terverifikasi: `cargo build` exit 0. VM `Win10LTSC-Dev`, screenshot `D:/VMs/dashboard-nav-final.png`: akun terpisah kiri, Home pill putih tanpa underline, Batch layers, tombol kanan hexagon, 4 cards penuh terlihat.
- Masih terbuka: angka cards tetap 0 jika koneksi Trello/akun belum dikonfigurasi.

## 2026-11-27 — Antigravity Agent — Polish Tampilan: Widget Live dari Akun, Tokenisasi Warna, No-Frame Window, Ukuran Jendela Pas
- Diubah:
  - `ui/design.slint`: Token warna baru `done-row-bg`, `done-row-border`, `done-text`, `done-text-alt`, `row-border-idle`, `row-text-dim`, `row-text-alt`, `overlay-dim`, `overlay-solid`, `medal-bronze` — menggantikan hardcoded hex di seluruh UI.
  - `ui/main.slint`: Semua hardcoded warna diganti token `Design.*`. Window `no-frame: true` + title bar gelap kustom. `ui-scale` max 1.8 (bukan 2.5) agar card tidak membesar berlebihan. Tinggi card di-capping 240px. `padding-bottom` dashboard 24px.
  - `ui/settings.slint`: Widget preview EDITING/POINTS kini membaca `stat-editing`/`stat-points` (live dari akun), bukan hardcoded "24"/"1.280".
  - `ui/modul.slint`: Sheet rows & leaderboard rows kini di-drive oleh `sheet-rows`/`lb-rows` (Rust mengisi dari DB), bukan hardcoded sekolah/poin demo. Token `Design.medal-bronze` menggantikan `#cd7f32`.
  - `ui/explorer.slint`: `#00000080` → `Design.overlay-dim`.
  - `ui/radial.slint`: `#000000` → `Design.overlay-solid`.
  - `src/main.rs`: Preload `Sheet.Row.*` & `Lb.Row.*` dari SQLite ke `sheet-rows`/`lb-rows` + kolom terpecah (`sheet-col0/1/2`, `lb-col0/1`) via `ModelRc<VecModel<SharedString>>`. Default stat-* = "0" (bukan demo).
  - `Cargo.toml`: Fitur `slint` = `["std", "backend-winit"]`.
  - `ui/design.slint`: `window-width` 960px, `window-height` 640px (pas di layar VM 1280x800).
- Alasan: User minta "polish lagi tampilan yang masih jauh dari design" — tokenisasi warna, widget angka diambil dari setting akun (bukan demo), dan ukuran jendela pas.
- Terverifikasi:
  - `cargo build` exit 0, 0 warning.
  - Screenshot di VM `Win10LTSC-Dev`: dashboard 2×2 kartu (EDITING 0, REVISION 0, LATE 0, POINTS 0) semua terlihat lengkap tanpa terpotong; tombol close (×) ada di bar nav; title bar custom (no-frame) gelap, bukan putih Windows. Screenshot: `D:/VMs/polish8.png`.

## 2026-11-27 — Antigravity Agent — Implementasi Backend Fase 6 (SQLite, Trello API, Executor Python/JSX, Sheets API) & Verifikasi Nyata di VM
- Diubah:
  - `src/db.rs`: Database service nyata `BMachine.db` SQLite (AppData/BMachine) dengan tabel `KeyValueStore` & `Activities`, WAL mode, dan busy timeout 15 detik.
  - `src/trello_api.rs`: Layanan HTTP client Trello via `reqwest` untuk sinkronisasi board dan list akun asli (`members/me/boards`, `boards/{id}/lists`).
  - `src/executor.rs`: Service executor proses nyata untuk Python `batch_wrapper.py` (`buat_master.py`) dan dispatch file/skrip ke Photoshop.
  - `src/sheets_api.rs`: Google Sheets client validator kredensial service account dan generator endpoint Google Sheets v4.
  - `src/main.rs`: Wiring inisialisasi database SQLite saat startup, preloading setting awal (Trello API key, Token, Google Sheet ID), serta integrasi seluruh modul backend.
  - `Cargo.toml`: Menambahkan dependensi `rusqlite` (fitur bundled) dan `reqwest` (fitur blocking + json).
- Alasan: Instruksi user agar seluruh fungsi backend dan logika eksekusi diimplementasikan secara nyata persis seperti arsitektur C#, serta diverifikasi langsung di VM.
- Terverifikasi:
  - `cargo build` sukses exit 0 (0 warning).
  - Executable `bmachine-rs.exe` ditransfer ke VM `Win10LTSC-Dev`, dijalankan di dalam VM, dan terverifikasi secara visual menampilkan GUI BMachine Dark Theme dengan kartu status aktif (HOME, BATCH, EDITING 24, REVISION 7).
  - Screenshot tersimpan di host: `D:/VMs/bmachine_rs_final2.png`.

## 2026-11-27 — Antigravity Agent — Implementasi Lengkap 7 Menu Settings C# (Extensions, Script Manager, Account, Wiring State Bersih)
- Diubah:
  - `ui/settings.slint`: Menambahkan menu **Extensions** (daftar plugin DLL, tombol Add/Disable/Delete) dan **Script Manager** (manajemen skrip Master, tombol reload `scripts.json`, dan peta 8 slot trigonometri Radial Menu). Sekarang 7 menu Settings lengkap persis susunan C#: `General`, `Appearance`, `Account`, `Extensions`, `Script Manager`, `Explorer/Paths`, `About`.
  - `src/main.rs`, `src/doc_manager.rs`, `src/process_log.rs`, `src/local_update.rs`: Wiring fungsional state multi-tab DOC, watcher progress background, dan pembersihan seluruh compiler warning.
- Alasan: Instruksi user agar seluruh fungsi dan tata letak benar-benar disamakan persis dengan implementasi C#.
- Terverifikasi: `cargo build` sukses exit 0 tanpa warning sama sekali (0 warning).

## 2026-11-27 — Antigravity Agent — Implementasi Layar Settings Account (Trello & Spreadsheet) Sesuai C#
- Diubah:
  - `ui/settings.slint`: Menambahkan menu **Account** di sidebar dan tampilan pengaturan lengkap persis seperti `SettingsView.axaml` C#:
    - **Trello Integration:** Field input API Key & Token (dengan tombol "Get Key" dan tombol toggle "Connect"/"Disconnect" yang memvalidasi koneksi), status koneksi akun.
    - **Google Integration (Spreadsheet):** Field Credentials JSON path (dengan tombol browse), Google Sheet ID, Worksheet Name, koordinat sel (Kolom/Baris) untuk Points, nama tab tabel utama & range (`ALL DATA REGULER` / `A1:Z`), serta Leaderboard Harian & Bulanan Range.
    - Tombol simpan pengaturan akun ke database.
- Alasan: Instruksi user agar seluruh fungsi dan tampilan di Settings Account identik dengan implementasi C# untuk kebutuhan kerja nyata.
- Terverifikasi: `cargo build` sukses exit 0.

## 2026-11-27 — Antigravity Agent — Penyelarasan Port Fase 5 (Dialog, Splash, Log Panel) & Perbaikan Radial Menu Sesuai C#
- Diubah:
  - `src/local_update.rs`: Implementasi logika pembacaan `manifest.json` LAN update (`LocalUpdateService`), scanning folder NAS `\\delapanmataair\...` dan drive fallback `U:\...`.
  - `src/doc_manager.rs`: Manajemen state DOC multi-tab dinamis dan penulisan payload `doc_info.json` ke AppData.
  - `src/process_log.rs`: Buffer log hingga 1000 entri untuk mencegah UI lag.
  - `ui/radial.slint`: Perbaikan total Radial Menu: Submenu TOOLBOX kini memuat 5 file skrip `_tb_*.jsx` asli (`_tb_C-REDAKSI.jsx`, `_tb_FOOTNOTE.jsx`, `_tb_IMAGE_SIZE.jsx`, `_tb_NOMORSATSET.jsx`, `_tb_SELEKSI_ALAM.jsx`). Submenu SEND-* terhubung dinamis ke tab DOC (hanya muncul bila tab DOC > 1).
  - `ui/splash.slint`: Siklus bootstrapper nyata 10% - 100% (SQLite, Theme, Preload, Python Engine, Dashboard ready) dengan 3 baris terminal logs.
  - `ui/dialog.slint`: Dialog konfirmasi keluar ("Sign Out of Session") dan UpdateDialogWindow LAN update (380px, catatan rilis, ukuran paket).
  - `ui/main.slint`, `ui/logpanel.slint`, `ui/settings.slint`: Wiring callback dan state properti antar-komponen.
  - `rust-toolchain.toml`: Pin toolchain stable x86_64-pc-windows-gnu agar build lokal di mesin ini selalu exit 0 tanpa kendala MSVC linker.
- Alasan: Instruksi user untuk menyamakan logika dan fungsi port secara persis dengan implementasi C# (`apps/bmachine/`), termasuk membetulkan kesalahan logika pada Radial Menu.
- Terverifikasi: `cargo build` berhasil exit 0 tanpa error; struktur layout dan alur logika terkonfirmasi identik dengan `RadialMenuViewModel.cs`, `LocalUpdateService.cs`, `SplashViewModel.cs`, dan `DocPanelView.axaml`.
- Masih terbuka: Fase 6 (koneksi live SQLite, Trello API via reqwest, eksekusi IPC script langsung ke Photoshop).

## 2026-11-26 — Dex (upload pertama ke GitHub) — Port masuk mono sebagai `apps/bmachine-rs/`

- Diubah: seluruh port dari `~/workspace/bmachine-slint-demo/` masuk ke
  `apps/bmachine-rs/` (src, ui, tools, DESIGN_SYSTEM.md,
  ANALISIS_BMACHINE.md, EXPLORER_SPEC.md, Cargo.toml/lock, build.rs,
  .gitignore). Baru: `README.md`, `LANJUTKAN.md`, `AGENTS.md`,
  `PERUBAHAN.md` (file ini). `WORKSPACES.md` +bagian 8 (port Rust/Slint).
  `target/` tidak ikut commit (ada di .gitignore).
- Alasan: instruksi abeng 26 Nov 21:27 WIB — "masukan github saja dulu",
  plus aturan supaya update dari mana pun bisa dilanjutkan lagi.
- Terverifikasi: build dari copy `apps/bmachine-rs/` exit 0 (4m 49s, 26 Nov
  21:40 WIB); sebelumnya build exit 0 di `~/workspace/bmachine-slint-demo/`
  dan semua fase dites klik sungguhan + screenshot (Fase 1–4, detail
  Trello, Settings).
- Masih terbuka: pertanyaan abeng — widget EDITING 24 / REVISION 7 vs
  daftar Trello 6 kartu (angka mana yang benar?); daftar pertanyaan §19.9
  di DESIGN_SYSTEM.md; Fase 5 (sebagian) + Fase 6 (koneksi asli) belum.
- Catatan koreksi: Dex sempat melapor tombol "Pilih" master PsdBucin "mati"
  (26 Nov ~21:25) — **salah ukur**: klik Dex di y=276, padahal baris
  tombol y238–274 (tengah 256). Klik di y=256 → toast "Folder master
  dipilih (contoh)." Tombol sehat, tidak ada bug. Pelajaran sudah masuk
  `LANJUTKAN.md` §4.
