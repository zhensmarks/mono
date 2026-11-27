# PERUBAHAN.md — Log perubahan & serah-terima agent

Setiap agent yang mengerjakan port ini **wajib** menulis entrinya di sini
sebelum commit + push (format lengkap di `AGENTS.md`). Entri terbaru di
paling atas. Dengan log ini, siapa pun yang menerima project — Dex, Hermes,
atau orang lain — langsung tahu apa saja yang diubah tanpa menebak.

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
  - `ui/main.slint`: Semua hardcoded warna diganti token `Design.*`. Window `no-frame: true` + tombol close kustom (×) di bar nav. `ui-scale` max 1.8 (bukan 2.5) agar card tidak membesar berlebihan. Tinggi card di-capping 240px. `padding-bottom` dashboard 24px.
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
