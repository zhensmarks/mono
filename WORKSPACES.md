# WORKSPACES.md — Monorepo BMachine + PixaCompact

Satu repo, banyak aplikasi. Setiap app tetap punya solution/csproj/logikanya sendiri.
File ini adalah sumber kebenaran untuk susunan folder, area kerja tim, dan alur update.

---

## 1. Susunan folder

```
mono/
  apps/
    bmachine/        <- aplikasi BMachine (utuh: sln, src, Scripts, publish)
    pixacompact/     <- aplikasi PixaCompact (utuh: csproj, Views, Services, publish)
  shared/            <- kode bersama (KOSONG dulu, isi belakangan saat dibutuhkan)
  WORKSPACES.md      <- file ini
  build.ps1          <- build salah satu / semua app
  publish.ps1        <- publish (single-file) salah satu / semua app
```

Aturan baku:
- Isi folder app TIDAK diubah sama sekali saat pindah. Tidak ada rename namespace,
  tidak ada ubah logika, tidak ada ubah tampilan. Yang berubah hanya lokasi foldernya.
- Path di dalam csproj/build script semua relatif terhadap folder app-nya masing-masing,
  sehingga pindah folder tidak merusak build. Contoh: `apps\bmachine\src\BMachine.App`
  tetap merujuk `..\..\Scripts\` ke `apps\bmachine\Scripts\` (sama seperti dulu).

---

## 2. Area kerja tim (supaya tidak saling tabrakan)

| Anggota / tim | Area utama | Folder | Boleh sentuh | JANGAN sentuh |
|---|---|---|---|---|
| Tim Dashboard | Plugin dashboard | `apps/bmachine/src/Plugins/BMachine.Plugin.Dashboard/` | folder plugin itu + asset plugin | Core, SDK, UI inti, App |
| Tim UI / UX | Tampilan & interaction | `apps/bmachine/src/BMachine.UI/` (Views, ViewModels, Styles, Themes, Controls) | semua di dalam BMachine.UI | Core, SDK, App, plugin |
| Tim Core | Logika & data | `apps/bmachine/src/BMachine.Core/` , `BMachine.SDK/` | Core + SDK | UI, App, plugin |
| Tim PixaCompact | App background remover | `apps/pixacompact/` (semua) | seluruh app PixaCompact | `apps/bmachine/` |
| Tim ProjectBot | Bot & otomasi | `ProjectBot/` (di luar mono, lokal) | bot, config, lib | kode app |

Aturan tambahan:
- `BMachine.Core` dan `BMachine.SDK` adalah **core**. Tim UI/Dashboard boleh membaca,
  tapi perubahan harus lewat review (branch terpisah + PR), karena semua orang bergantung.
- Setiap pekerjaan di branch sendiri: `feature/ui-mantra`, `feature/dashboard-export`,
  `fix/pixa-crash-gallery`, dst. Tidak ada commit langsung ke `main` untuk perubahan besar.
- Pesan commit: `area: deskripsi singkat`. Contoh: `ui: perbaiki blok teks tombol Ganti Semua`.

Mengapa dibagi begini: plugin dashboard sudah punya csproj sendiri dan diload terpisah
oleh `PluginManager` (lihat bagian 3). Tim UI tidak perlu membongkar Core untuk mengubah
tampilan. Tiap tim fokus di foldernya, build cepat, konflik kecil.

---

## 3. Update parsial (kirim hanya bagian yang berubah)

Kunci: BMachine.App me-load plugin dari folder plugin via `PluginManager`
(`Assembly.LoadFrom` terhadap `*.dll` di folder plugins). Jadi update bisa per-DLL.

Skenario & ukuran aktual (diukur dari publish dir sekarang):
- Tim Dashboard ganti sesuatu
  -> build `BMachine.Plugin.Dashboard.csproj` saja -> kirim 1 file dll (kecil)
  -> timpa dll lama di mesin pemakai -> restart app. Selesai.
- Tim UI ganti tampilan
  -> build `BMachine.UI.csproj` -> kirim BMachine.UI.dll (+ asset axaml)
  -> timpa -> restart.
- Perubahan Core / SDK / App -> rebuild BMachine.App (publish ~143 MB, self-contained).
- PixaCompact -> publish penuh (~2.2 GB karena Playwright browser + model ONNX).
  Karena itu PixaCompact dirilis jarang-dengan-paket-besar; BMachine bisa sering dengan
  patch kecil.

Catatan: fitur "timpa dll + restart" butuh app memuat plugin dari folder luar (sudah ada).
Bagian yang belum ada: cara mendistribusikan dll itu ke mesin pemakai. Lihat bagian 5.

---

## 4. Versioning

Setiap app melacak versi di csproj utamanya:
- `apps/bmachine/src/BMachine.App/BMachine.App.csproj` -> `<Version>` (sudah ada: 8.3.0)
- `apps/pixacompact/PixelcutCompact.csproj` -> `<Version>` (BELUM ADA, harus ditambahkan)

Format: Semantic Versioning `MAJOR.MINOR.PATCH`.
- PATCH: perbaikan bug, tidak ada fitur baru.
- MINOR: fitur baru, tetap kompatibel.
- MAJOR: perubahan besar / breaking.

Setiap rilis wajib bump versi. Bug report (bagian 6) menyertakan versi ini supaya
ketahuan di versi mana bug itu terjadi.

---

## 5. Distribusi update

Yang sudah ada:
- Build script per app: `apps/bmachine/build.ps1` dan `apps/pixacompact/build.ps1`.
- Share NAS: `\\DELAPANMATAAIR\Editor\#PROJECT ROBOT\BMachine` (target "UPLOAD SERVER").
- ProjectBot punya `lib/git.js` -> commit, push, tag, `gh release create` lewat Telegram.

Yang perlu dibangun (tahap selanjutnya, setelah monorepo ini jalan):
- CI GitHub Actions per app: saat push ke `main`, build otomatis dan pasang hasilnya
  ke Release GitHub. Dengan begitu "publish" hanya = push kode.
- Updater di app: cek versi terbaru di GitHub Release, download hanya yang berubah,
  timpa, restart.

---

## 6. Bug report otomatis: dikirim ke mana?

Jawabannya: **ke ProjectBot**, bot Telegram yang sudah ada dan sudah jalan.

Yang sudah ada di ProjectBot:
- Grup forum Telegram dengan topik "Project & Bug" (threadId 5) dan "Inbox" (threadId 8).
- `data/inbox.json`: penyimpanan catatan bertipe `BUG` dengan field `project`, `urgency`,
  `summary`, `text`, `status`. Sudah dipakai, sudah ada isinya.
- Command `/kerjakan BMachine.v2 <instruksi>` -> menjalankan Codex di repo lokal,
  lalu melaporkan file yang berubah.
- `lib/git.js` -> commit / push / rilis GitHub, semua lewat Telegram.

Yang BELUM ada (yang menyebabkan bug user belum sampai ke mana-mana):
- Aplikasi (BMachine / PixaCompact) tidak mengirim apa pun saat crash atau saat user
  mau melapor. Sekarang crash hanya tertulis ke file lokal:
  - BMachine -> `%AppData%\BMachine\crash_report.txt` (sudah ada handler-nya)
  - PixaCompact -> `crash.log` di folder app
  File itu tidak terbaca siapa pun kecuali user buka sendiri.

Jembatan yang akan dibangun:

```
App (BMachine / PixaCompact)
  |  crash otomatis ATAU user klik "Lapork Bug" di UI
  v
HTTP POST ke ProjectBot lokal -> http://127.0.0.1:<port>/bug
  body: { project, appVersion, summary, text, severity, source: "crash"|"manual" }
  v
ProjectBot:
  1. simpan ke data/inbox.json (struktur yang sama, type: "BUG")
  2. kirim notifikasi ke topik Telegram "Project & Bug"
  3. balas ke app: { ok: true, id: <id inbox> }
  v
Kamu lihat notifikasi di Telegram
  -> baca, tenangkan, lalu: /kerjakan BMachine.v2 perbaiki <bug>
  -> ProjectBot jalankan agent di repo, lapor file berubah
  -> kamu commit/push lewat Telegram
```

Mengapa HTTP lokal (127.0.0.1) dan bukan Telegram langsung dari app:
- App dan ProjectBot berjalan di mesin yang sama (setup sekarang). Paling sederhana.
- Tidak perlu menanam token Telegram di dalam aplikasi (tidak aman).
- Tidak perlu membuka port internet. ProjectBot yang jadi satu-satunya pintu.

Keterbatasan yang diketahui:
- Kalau app dipasang di mesin lain (tanpa ProjectBot), bug tidak terkirim.
  Untuk skala itu butuh endpoint publik (mis. lewat 9router atau webhook server).
  Itu tahap lanjutan, dicatat di sini supaya tidak lupa.

---

## 7. Cara memakai repo ini

```bash
# clone sekali
git clone https://github.com/zhensmarks/mono.git
cd mono

# build satu app
powershell -File apps/bmachine/build.ps1
powershell -File apps/pixacompact/build.ps1

# atau pakai script atasannya
powershell -File build.ps1 -App bmachine
```

Untuk anggota tim, selalu bekerja di branch dan di folder area sendiri.
Lihat bagian 2 untuk batasan folder per tim.
