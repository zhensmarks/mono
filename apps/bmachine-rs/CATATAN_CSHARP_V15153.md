# Catatan test C# v15.15.3 untuk bmachine-rs

Ditulis 27 Nov 2026 oleh Dex, atas perintah abeng ("catat yang harusnya
diterapkan ke bmachine-rs tuh gimana harusnya").

**Sumber:** build Linux dari `main` (commit `679fe8b`, v15.15.3, net8.0,
Avalonia 11.3.2), dijalankan di Xvfb 1280x800, **tanpa** data Trello /
Sheets / Photoshop / NAS. Semua yang di bawah ini hasil klik sungguhan
+ screenshot (bukan baca source saja) — kecuali yang ditandai "dari source".

## Yang harus diterapkan / dicek di port

### 1. Label widget ke-4 = SPREADSHEET, bukan POINTS (perlu keputusan abeng)
- C# (`DashboardView.axaml:334`, dari source + terkonfirmasi di layar):
  `Label="SPREADSHEET"` (hardcode, bukan key bahasa),
  `Value="{Binding StatPoints}"`, `Suffix="POIN"`.
- Port saat ini menampilkan **"POINTS"**.
- Navigasi C# **sudah sama** dengan aturan port: body
  `Command=OpenLeaderboardWindowCommand`, panah
  `WindowCommand=OpenSpreadsheetWindowCommand` (= aturan abeng: body
  POINTS → Leaderboard, panah POINTS → Spreadsheet). **Hanya labelnya
  yang beda.**
- Keputusan ditangan abeng: samakan label ikut C# (SPREADSHEET) atau
  tetap POINTS. Jangan ubah sebelum abeng jawab.

### 2. BATCH empty state — samakan seperti C#
- C# hanya menampilkan drop zone besar: lingkaran "+" di tengah,
  "DRAG FOLDERS HERE", "Drop source folders to begin batch processing".
- Klik pada drop zone **tidak** membuka file picker (drag-drop saja).
- Port: saat belum ada folder, tampilkan empty state seperti ini,
  bukan panel SOURCE kosong.

### 3. LOCKER empty state (arsip saja)
- C#: "Locker Not Configured" + tombol "Start Configuration".
- Port menghapus LOCKER dari nav atas perintah abeng (26 Nov) — catatan
  ini hanya arsip, bukan perintah mengembalikan LOCKER.

### 4. Settings (Control Center) — samakan strukturnya
- Menu kiri: General, Appearance, Account, Extensions, Script Manager,
  Paths, About.
- Halaman General: LANGUAGE (dropdown, "English"), FILTERS (File
  Extensions Filter, "All (*)"), DATA (Storage `>`, Database Backup
  Export/Import), DASHBOARD VISIBILITY (Show Batch ON, Show Folder
  Locker ON, Show Output Explorer ON).
- Port: samakan struktur ini. Jangan tambah / hapus item tanpa
  konfirmasi abeng.

### 5. Profile flyout — samakan isinya
- "USER" + sapaan berbasis waktu (jam 16:xx WIB sudah "Good Evening"),
  Terminal (toggle), Laporkan Bug, Settings, Log Out, footer
  "BMACHINE · 15.15".
- Port: samakan isi + sapaan waktu.

### 6. Trello list kosong — jangan tambah teks sendiri
- C# menampilkan tab EDITING / REVISI / SUSULAN dan area kosong tanpa
  pesan "tidak ada kartu". Port: samakan, jangan tambah empty-state
  text buatan sendiri.

### 7. Leaderboard — samakan layout
- Tab HARIAN / BULANAN, "Total: 0 Pts" kiri bawah, tombol refresh
  bulat biru kanan bawah.

### 8. Navigasi kembali — pertahankan aturan port
- C# tidak punya tombol back. **Esc TIDAK menutup Settings.**
  Yang berfungsi: right-click → `NavigateBack()`.
- Port sudah memakai aturan ini (right-click / Esc = back, tanpa tombol
  "← Kembali"). Pertahankan, jangan tambah tombol back.

### 9. Fase 6 — kredensial WAJIB di OS vault (bukan file/config)
- C# di Linux memanggil `secret-tool` (libsecret) untuk membaca token
  Trello; di Windows memakai Credential Manager. Di VM ini `secret-tool`
  tidak ada → Trello kosong + toast error (itu keterbatasan lingkungan,
  bukan bug aplikasi).
- Port Fase 6: simpan kredensial Trello/Sheets di OS vault.

### 10. Fase 6 — update check via NAS, bukan GitHub
- C# cek `\\delapanmataair\...\BMachine\manifest.json` (Local vs
  Remote version). Di VM ini NAS tidak terjangkau → "manifest.json
  tidak ditemukan" (bukan bug).

### 11. Stats grid C# — TIDAK perlu diubah di port
- C#: `MaxWidth 600 / MaxHeight 400`, di tengah layar (bukan
  full-window). Port memakai fluid layout — itu pilihan desain abeng,
  biarkan.

## Yang TIDAK perlu diubah
- Navigasi widget body vs panah — sudah sama dengan aturan abeng.
- LOCKER tetap tidak ada di nav port (keputusan abeng 26 Nov).
- Fluid layout dashboard port.

## Terbuka / perlu verifikasi — JANGAN ubah port dulu
- **Panah widget ke-4 C#:** klik Dex membuka Leaderboard embedded,
  padahal `WindowCommand`-nya `OpenSpreadsheetWindowCommand` —
  kemungkinan klik Dex kena body, bukan panah. Perlu klik ulang dengan
  koordinat panah terukur sebelum mengubah apa pun.
- **Toggle Terminal ON tapi panel log tidak muncul** di Linux — belum
  jelas apakah panelnya jendela terpisah / hanya di dashboard / gagal
  di Linux. Perlu cek di PC abeng.
- Sapaan "Good Evening" mulai jam berapa (16:xx WIB sudah evening?).

## Cara mengulang test C# di Linux (untuk agent berikutnya)
- SDK: `~/.dotnet` (8.0.428). Install via `dotnet-install.sh`;
  `/tmp` cuma 512 MB tmpfs, jadi `TMPDIR` harus di home.
- Copy HARUS meniru layout repo: `apps/bmachine` +
  `shared/BMachine.Shared.BugReporter` (csproj mereferensi
  `../../../../shared` — copy tanpa `shared/` gagal CS0246).
- Build: `build.sh` → `dotnet publish -c Release -r linux-x64
  --self-contained`. Jalan di Xvfb `:97`.
- Koordinat nav terukur (1280x800): profile 513, HOME 587, BATCH 657,
  LOCKER 732. **JANGAN tebak koordinat dari screenshot** — ukur dari
  pixel (pelajaran yang sama seperti tombol "Pilih" 26 Nov).
