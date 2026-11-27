# PERUBAHAN.md — Log perubahan & serah-terima agent

Setiap agent yang mengerjakan port ini **wajib** menulis entrinya di sini
sebelum commit + push (format lengkap di `AGENTS.md`). Entri terbaru di
paling atas. Dengan log ini, siapa pun yang menerima project — Dex, Hermes,
atau orang lain — langsung tahu apa saja yang diubah tanpa menebak.

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
