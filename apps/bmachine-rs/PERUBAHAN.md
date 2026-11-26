# PERUBAHAN.md — Log perubahan & serah-terima agent

Setiap agent yang mengerjakan port ini **wajib** menulis entrinya di sini
sebelum commit + push (format lengkap di `AGENTS.md`). Entri terbaru di
paling atas. Dengan log ini, siapa pun yang menerima project — Dex, Hermes,
atau orang lain — langsung tahu apa saja yang diubah tanpa menebak.

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
