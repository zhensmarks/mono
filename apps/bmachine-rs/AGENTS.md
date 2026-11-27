# AGENTS.md — Instruksi untuk agent yang mengerjakan port ini

File ini dibaca oleh agent apa pun yang disuruh mengerjakan port BMachine
Rust/Slint — Dex, Hermes, atau agent lain. Tujuannya satu: **siapa pun yang
pegang project ini, orang berikutnya (atau Dex saat diserahkan lagi) harus
langsung tahu apa yang diubah, tanpa menebak.**

## Sebelum mengubah apa pun

1. `git pull origin main` dulu — jangan kerja di atas kode basi.
2. Baca berurutan: `LANJUTKAN.md` (aturan + status) →
   `DESIGN_SYSTEM.md` (sumber kebenaran tampilan) → `PERUBAHAN.md`
   (apa yang diubah agent sebelumnya) → `git log --oneline -10`.
3. Pahami dulu, baru ubah. Kalau instruksi user bertentangan dengan file
   ini, instruksi user yang menang — tapi catat penyimpangannya di
   `PERUBAHAN.md`.

## Saat mengerjakan

- Ikuti semua aturan di `LANJUTKAN.md` (jangan tambah tombol "← Kembali",
  alur BATCH + Explorer sama persis C#, detail Trello sama persis,
  jangan `muse.write` overwrite file yang sudah ada, ukur koordinat klik
  dari pixel jangan ditebak, dsb).
- Jangan sentuh `apps/bmachine/` (C# asli) dari port ini.
- Jangan ubah history git (no rebase, no force-push, no amend commit yang
  sudah di-push).
- Jangan hapus `PERUBAHAN.md` atau `LANJUTKAN.md`.

## Sesudah mengerjakan — WAJIB sebelum dianggap selesai

1. **Verifikasi sungguhan:** `cargo build` harus exit 0, lalu tes dengan
   klik sungguhan + screenshot. "Belum terbukti jalan tidak boleh diklaim
   beres." Tulis apa yang dites dan hasilnya di entri log.

### Alat tes GUI (Xvfb :98)

- Kirim tombol global: `/tmp/sendkey2 :98 <mode>` — display adalah argumen
  PERTAMA, mode kedua (mis. `ctrlcomma`, `esc`, `ctrll`). Bentuk
  `/tmp/sendkey2:98 ...` (titik dua menempel nama file) SALAH — file itu
  tidak ada; bentuk `/tmp/sendkey2 ctrlcomma:98` juga salah ("GAGAL buka
  ctrlcomma"). Scroll: `xdotool click 5` = ke bawah, `click 4` = ke atas.
- Screenshot: `ffmpeg -y -f x11grab -video_size 1280x800 -i :98
  -frames:v 1 <file>.png` (spasi setelah `-i` wajib).
- Matikan instance tes berdasarkan window ID saja
  (`xdotool windowkill <id>`), JANGAN `pkill -f bmachine`.
2. **Tulis entri di `PERUBAHAN.md`** (format di bawah) — apa yang diubah,
   kenapa, apa yang terverifikasi, apa yang masih terbuka. Ini yang bikin
   Dex tidak bingung saat project diserahkan lagi.
3. **Commit dengan pesan yang jelas** (`area: deskripsi singkat`, contoh:
   `toolbox: perbaiki posisi tombol Pilih master`) dan **push ke `main`**.
   Satu pekerjaan = satu commit yang utuh, jangan commit setengah jalan.

## Format entri `PERUBAHAN.md`

```md
## 2026-11-27 — <nama agent / siapa yang menyuruh> — <judul singkat>
- Diubah: <file + apa yang diubah>
- Alasan: <kenapa diubah / instruksi siapa>
- Terverifikasi: <build exit 0 / klik + screenshot apa / belum dites>
- Masih terbuka: <yang belum beres / butuh jawaban abeng>
```

## Serah-terima Dex ↔ Hermes

- Hermes → Dex: pastikan semua sudah di-commit + push ke main, entri
  `PERUBAHAN.md` sudah ditulis. Dex akan pull, baca log + `git log`,
  lalu lanjut tanpa bertanya ulang.
- Dex → Hermes: sama — Dex commit + push + tulis log. Hermes baca
  `PERUBAHAN.md` terbaru sebelum mulai.
- Kalau ada yang tidak jelas di log sebelumnya, tanya abeng — jangan
  menebak dan jangan menimpa pekerjaan agent sebelumnya.
