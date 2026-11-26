# LANJUTKAN.md — Aturan Lanjutan Port BMachine Rust/Slint

Dokumen ini supaya siapa pun (Dex, abeng, atau orang lain) bisa melanjutkan
port ini dari titik mana pun tanpa mengulang kesalahan yang sama. Kalau ada
update dari mana pun — C# berubah, ada yang edit port ini, atau abeng kirim
info baru — mulai dari sini.

## 1. Lokasi & alur Git

- Repo: `zhensmarks/mono`, branch **`main` — langsung ke main, tanpa branch
  baru** (aturan abeng).
- Port ini di: `apps/bmachine-rs/` (C# asli tetap di `apps/bmachine/`, jangan
  disentuh dari port ini).
- Sebelum mulai kerja: `git pull origin main` dulu. Sesudah selesai: commit +
  push ke main. Jangan pernah force-push.
- Kalau ada update dari pihak lain di `apps/bmachine-rs/`: pull dulu, baca
  `git log --oneline -10 -- apps/bmachine-rs/`, pahami dulu baru lanjut.
  Jangan timpa pekerjaan orang; konflik diselesaikan manual, jangan
  `checkout --ours` membabi buta.

## 2. Kalau C# berubah (`apps/bmachine/`)

- Baca ulang file C# yang berubah, update `ANALISIS_BMACHINE.md` dan bagian
  `DESIGN_SYSTEM.md` yang kena, baru ubah port.
- Prinsip abeng: tampilan boleh dirombak, **alur BATCH + Explorer harus sama
  persis ("jangan sampai salah sedikitpun")**, detail Trello harus sama
  persis (tampilan + perilaku).
- Jangan tambah tombol "← Kembali" di mana pun. Kembali/tutup hanya lewat
  klik kanan (+ Esc). Satu-satunya pengecualian: tombol "Tutup" di detail
  Trello dan × di title bar window.

## 3. Build, jalan, tes

```sh
source ~/.cargo/env
cargo build            # ~25–65 detik
DISPLAY=:98 SLINT_BACKEND=winit-software ./target/debug/bmachine-slint-demo
```

- Tes di Xvfb display `:98` (Dex) / `:99` (koordinator — jangan dibunuh
  instancenya, bunuh instance sendiri hanya via window PID, jangan
  `pkill -f bmachine`).
- `cargo build` harus exit 0 sebelum klaim beres. Klaim "selesai" hanya
  setelah dites klik sungguhan + screenshot — "belum terbukti jalan tidak
  boleh diklaim beres".
- Alat tes: `/tmp/sendkey2 :98 <mode>` (esc, return, ctrll, ctrlv, ... —
  JANGAN panggil tanpa argumen, itu kirim ctrll ke :99), clipboard via
  `DISPLAY=:98 xclip`, screenshot via
  `ffmpeg -y -f x11grab -video_size 1280x800 -i :98 -frames:v 1 file.png`
  (spasi setelah `-i` wajib).

## 4. Pelajaran yang jangan diulang

- **Koordinat klik HARUS diukur, jangan ditebak dari screenshot.**
  Ukur rect tombol dari border pixel-nya (border Btn `#3F3F46`) atau dari
  glyph label sebelum klik. Dex pernah salah ukur Y ~20px dan melapor tombol
  "mati" padahal tombolnya sehat (26 Nov).
- **Binding satu arah `text:` di TextInput PUTUS saat user mengetik.**
  Pakai `<=>` bila perubahan programatik (clear draft dsb.) harus tampil.
- `visible: false` tetap makan tempat di layout — pakai elemen `if` atau
  wrapper 0-size bila ruangnya harus hilang.
- **JANGAN `muse.write` overwrite file yang sudah ada** (pernah menghapus
  DESIGN_SYSTEM.md §§1–19). Pakai `muse.edit` dengan anchor unik, atau
  append. Splice baris hanya dengan backup + baca ulang + verifikasi build.
- Tinggi persen berantai / `vertical-stretch` di Card jatuh ke
  preferred-height di Slint — pakai tinggi eksplisit `max(...)`.
- KeyBindings di FocusScope mati saat scope kehilangan fokus (klik mouse
  tidak memfokuskan TouchArea) — refokus saat overlay/flyout berubah.
- `xdotool windowsize` TIDAK me-layout ulang (ukuran window terikat konstanta
  Design) — untuk tes 520×600 ubah sementara `window-width/height` di
  `design.slint`, build, tes, kembalikan + rebuild.

## 5. File kunci

- `DESIGN_SYSTEM.md` — sumber kebenaran tampilan + status tiap fase
  (§15 Trello, §19 Fase 4, §20 Explorer, §21 Dynamic Island). Update tiap
  kali ada perubahan tampilan/perilaku.
- `ANALISIS_BMACHINE.md` — analisis C# (626 baris). Read-only untuk
  referensi; update hanya bila C# berubah.
- `EXPLORER_SPEC.md` — spesifikasi Explorer.
- `~/workspace/user/files/BATCH_DASHBOARD.md` +
  `~/workspace/user/files/BMACHINE_ALUR_APLIKASI.md` — spek otoritatif dari
  abeng (menang atas analisis bila bertentangan).
- Screenshots verifikasi: `~/workspace/rust-gui-contoh/bmachine-slint-*.png`
  (tidak ikut commit — terlalu banyak).

## 6. Status saat aturan ini ditulis (26 Nov 2026)

- Selesai & terverifikasi klik sungguhan: Fase 1, BATCH v3, Fase 3, detail
  Trello "sama persis" (embedded + window, 4 sub-panel, Add Manual, Unlink),
  Settings judul ganda, Fase 4 (Radial, Toolbox 3 tab, Spreadsheet
  draft/simpan/guard, Leaderboard Harian/Bulanan).
- Belum: Fase 5 (Settings/dialog/splash/log panel — sebagian sudah),
  Fase 6 (Trello/Sheets/Drive asli, eksekusi Python/JSX, SQLite, Photoshop).
  Semua data Trello/Sheets/Toolbox masih mockup (hardcode).
- Diketahui belum beres: widget dashboard EDITING 24 / REVISION 7 vs daftar
  Trello 6 kartu (angka mana yang benar? tanya abeng); window Trello tidak
  menampilkan kartu manual (count tetap "6 kartu"); pertanyaan terbuka
  §19.9 di DESIGN_SYSTEM.md (radial 3 tab vs 5 file `_tb_*.jsx`, SEND-*
  placeholder, Shift+Middle, Sheets guard/draft, Leaderboard bronze
  literal, dst).
- Jangan kerjakan: Dynamic Island (nanti setelah semua beres), Akun
  BMachine (nanti saja), dockable workspace PixaCompact (jangan ditawarkan).

## 8. Serah-terima antar agent (Dex ↔ Hermes)

Abeng lanjutkan project ini kadang lewat Dex, kadang lewat Hermes-nya.
Supaya tidak ada yang bingung saat project pindah tangan:

- **Yang mengerjakan WAJIB tulis `PERUBAHAN.md`** sebelum commit + push
  (format di `AGENTS.md`): apa yang diubah, kenapa, apa yang terverifikasi,
  apa yang masih terbuka.
- **Yang menerima WAJIB baca** `PERUBAHAN.md` (entri terbaru) +
  `git log --oneline -10` sebelum mulai — jangan bertanya ulang hal yang
  sudah tertulis di log.
- Aturan kerja agent ada di `apps/bmachine-rs/AGENTS.md` — Hermes baca
  file itu dulu tiap kali disuruh kerjakan port ini.
- Satu pekerjaan = satu commit utuh + push ke main. Jangan serahkan
  project dalam keadaan setengah commit / belum push.

## 9. Yang dilarang

- Reset password dashboard Hermes (abeng: "jangan reset").
- Menyimpan password apa pun ke memory/file — kecuali login yang memang
  sudah diminta disimpan (IDLIX, IG nihao.abeng, GitHub zhensmarks di
  Secure Vault). Password Windows/SMB/RDP selalu sekali pakai.
- Upload/push tanpa izin abeng. Hapus file tanpa backup.
- Menjalankan script dari sumber tidak jelas / memasukkan kredensial ke
  script pihak ketiga (contoh: tool "Gemini Pro gratis via emulator" —
  jangan pernah dijalankan).
