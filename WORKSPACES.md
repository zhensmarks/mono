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
- Script atasannya di root mono: `build.ps1` (build) & `publish.ps1` (publish).
- Share NAS: `\\DELAPANMATAAIR\Editor\#PROJECT ROBOT\BMachine` (target "UPLOAD SERVER").
- ProjectBot punya `lib/git.js` -> commit, push, tag, `gh release create` lewat Telegram.
- CI GitHub Actions (`.github/workflows/ci.yml`): push ke main -> build & publish
  otomatis **hanya app yang berubah** (path filter). Artifact bisa diunduh dari
  tab Actions. Sentuh `shared/**` -> kedua app di-build ulang.

Yang masih perlu dibangun (tahap selanjutnya):
- Updater di app: cek versi terbaru di GitHub Release, download hanya yang berubah,
  timpa, restart. Saat ini distribusi ke mesin pemakai masih manual (copy dari
  publish/ atau NAS).

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

Jembatan yang sudah dibangun (SUDAH JALAN, teruji end-to-end):

```
App (BMachine / PixaCompact)
  |  crash otomatis ATAU user klik "Lapork Bug" di SettingsView
  v
shared/BMachine.Shared.BugReporter (library bersama, std-lib only)
  |  POST http://127.0.0.1:21478/bug  (JSON: project, summary, text, appVersion, severity, source)
  v
ProjectBot/lib/httpbug.js  (endpoint POST /bug, port 21478)
  |  1. simpan ke data/inbox.json (type: BUG, urgency, tags:[source, vX.Y.Z], source:"app")
  |  2. kirim notifikasi ke topik Telegram "Project & Bug" (threadId 5)
  v
Kamu lihat notifikasi di Telegram
  -> baca, tenangkan, lalu: /kerjakan BMachine.v2 perbaiki <bug>
  -> ProjectBot jalankan agent di repo, lapor file berubah
  -> kamu commit/push lewat Telegram
```

Bagian yang sudah dibuat dan lokasinya:
- `shared/BMachine.Shared.BugReporter/BugReporter.cs` - client HTTP tahan banting.
  `SendAsync()` tidak pernah lempar (return false kalau gagal); `ReportCrash()`
  simpan lokal + POST fire-and-forget. Serialize camelCase, deserialize
  case-insensitive (tanpa ini server 400 / deserialize null).
- `apps/bmachine/src/BMachine.App/Program.cs` - 3 handler crash yang sudah ada
  tetap menulis `crash_report.txt`, sekarang juga kirim ke ProjectBot.
- `apps/bmachine/src/BMachine.UI/Views/Dialogs/MantraData/ReportBugDialog.axaml`
  - dialog "Lapork Bug" (Ringkasan + Detail), style MantraData, tanpa ikon.
- `apps/bmachine/src/BMachine.UI/Views/SettingsView.axaml` - tombol "Lapork Bug"
  di header sebelah version pill.
- `apps/pixacompact/Program.cs` - tambah global `UnhandledException` + kirim.
- `ProjectBot/lib/httpbug.js` + hook `startHttpBugServer()` di `index.js`.
  Validasi body (project & summary wajib), batas 2 MB, port dari
  `config.json -> bugBridge.port` (default 21478).

Mengapa HTTP lokal (127.0.0.1) dan bukan Telegram langsung dari app:
- App dan ProjectBot berjalan di mesin yang sama (setup sekarang). Paling sederhana.
- Tidak perlu menanam token Telegram di dalam aplikasi (tidak aman).
- Tidak perlu membuka port internet. ProjectBot yang jadi satu-satunya pintu.

Cara test jembatan (bot harus jalan):
```
curl -X POST http://127.0.0.1:21478/bug -H "Content-Type: application/json" \
  -d '{"project":"BMachine.v2","summary":"tes","text":"detail","appVersion":"8.3.0"}'
```

Keterbatasan yang diketahui:
- Kalau app dipasang di mesin lain (tanpa ProjectBot), bug tidak terkirim.
  `ReportCrash` tetap menyimpan ke file lokal sebagai cadangan, jadi tidak ada
  bug yang hilang sama sekali. Untuk skala mesin lain butuh endpoint publik
  (mis. lewat 9router atau webhook server). Itu tahap lanjutan.

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
