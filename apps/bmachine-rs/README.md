# BMachine — Port Rust/Slint

Port aplikasi BMachine (C#/.NET + Avalonia, di `apps/bmachine`) ke Rust + Slint.
Bukan replika piksel-per-piksel: tampilan boleh dirombak, tapi alur BATCH dan
Explorer harus sama persis dengan C#. Detail Trello harus sama persis C#
(tampilan + perilaku).

## Build & jalan

```sh
source ~/.cargo/env
cargo build
DISPLAY=:98 SLINT_BACKEND=winit-software ./target/debug/bmachine-slint-demo
```

Butuh Rust 1.100+ dan Slint 1.19.1 (lihat `Cargo.toml` / `Cargo.lock`).

## Struktur

- `src/main.rs` — logika Rust (state, handler Trello manual, timer mockup).
- `ui/` — komponen Slint (`main.slint`, `design.slint`, `modul.slint`,
  `trello-detail.slint`, `radial.slint`).
- `DESIGN_SYSTEM.md` — sumber kebenaran tampilan + status tiap fase.
- `ANALISIS_BMACHINE.md` — analisis mendalam C# (referensi, jangan diedit
  sembarangan — ini hasil baca source C#).
- `EXPLORER_SPEC.md` — spesifikasi Explorer.
- `LANJUTKAN.md` — **baca ini dulu sebelum lanjut kerja** (aturan lanjutan).
- `AGENTS.md` — instruksi untuk agent apa pun yang mengerjakan port ini
  (Dex, Hermes, atau lainnya) supaya serah-terima tidak membingungkan.
- `PERUBAHAN.md` — log perubahan & serah-terima: agent yang mengerjakan
  wajib menulis apa yang diubah di sini sebelum commit + push.
- `tools/` — alat bantu tes (`sendkey2.c`, `gen_explorer_data.py`).

## Status (26 Nov 2026)

Selesai: Fase 1 HOME, Fase 2 BATCH v3, Fase 3 Trello/Leaderboard/Spreadsheet,
Fase 4 Radial/Toolbox/Spreadsheet/Leaderboard, detail Trello "sama persis",
perbaikan judul ganda Settings.
Belum: Fase 5 (Settings/dialog/splash/log panel — sebagian sudah),
Fase 6 (koneksi Trello/Sheets/Drive asli, eksekusi Python/JSX, SQLite,
Photoshop). Semua data Trello/Sheets/Toolbox saat ini masih mockup.
