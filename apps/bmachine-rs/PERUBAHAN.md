# PERUBAHAN.md — Log perubahan & serah-terima agent

Setiap agent yang mengerjakan port ini **wajib** menulis entrinya di sini
sebelum commit + push (format lengkap di `AGENTS.md`). Entri terbaru di
paling atas. Dengan log ini, siapa pun yang menerima project — Dex, Hermes,
atau orang lain — langsung tahu apa saja yang diubah tanpa menebak.

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
