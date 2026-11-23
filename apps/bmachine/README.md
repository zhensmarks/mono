# BMachine

<div align="center">

![Version](https://img.shields.io/badge/version-15.10.0-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey.svg)
![Framework](https://img.shields.io/badge/.NET-8.0-purple.svg)

**Dashboard automasi untuk workflow kreatif**

</div>

## Deskripsi

BMachine adalah aplikasi desktop untuk membantu editor mengelola pekerjaan kreatif dari satu tempat. Aplikasi ini menggabungkan pemantauan kartu Trello, pengolahan dan sinkronisasi data, pengelolaan file, serta otomasi melalui skrip Photoshop dan Python.

Versi aplikasi mengikuti `Version` di `src/BMachine.App/BMachine.App.csproj`; versi yang tercatat saat dokumentasi ini diperbarui adalah **15.10.0**.

## Fitur

### Dashboard dan Trello
- Statistik Editing, Revision, Late, dan poin.
- Daftar kartu Editing, Revision, dan Late, termasuk detail, komentar, lampiran, checklist, dan aksi pemindahan.
- Pemindahan kartu secara batch dan penautan kartu manual.
- Panel log dan pengaturan tampilan.

### Workflow kreatif
- MantraData untuk membaca dan mengolah data spreadsheet/dokumen, transformasi, pencocokan foto, dan tata letak yearbook.
- Toolbox untuk menjalankan otomasi Photoshop melalui JSX dan skrip Python.
- Pemrosesan gambar Pixelcut melalui skrip pendamping.
- Integrasi Google Drive dan Google Sheets untuk alur file dan pelacakan data.

Ketersediaan tiap fitur eksternal bergantung pada konfigurasi, koneksi, dan perangkat lunak yang dibutuhkan fitur tersebut.

## Persyaratan

- **Rilis utama:** Windows 10/11, x64.
- **Menjalankan dari source:** .NET 8 SDK.
- **Skrip Python:** Python 3.10 atau lebih baru; sebagian skrip memerlukan `requests` dan `pillow`.
- **Skrip Photoshop:** Adobe Photoshop CC 2020 atau lebih baru.

Skrip build juga menyediakan target macOS (ARM64/x64) dan Linux x64. Periksa dan uji hasil build pada sistem operasi tujuan sebelum mendistribusikannya.

## Menjalankan dari source

```bash
git clone https://github.com/zhensmarks/mono.git
cd mono/apps/bmachine
dotnet restore BMachine.v2.sln
dotnet run --project src/BMachine.App/BMachine.App.csproj
```

Untuk membuat rilis Windows x64 dari PowerShell di folder `apps/bmachine`:

```powershell
.\build.ps1
```

Skrip publish membuat aplikasi self-contained di folder `BMachine` pada root monorepo. Untuk build macOS atau Linux, gunakan `./build.sh` pada sistem yang sesuai.

## Konfigurasi awal

1. Jalankan aplikasi dan buka **Settings**.
2. Atur kredensial Trello dan informasi board yang diperlukan.
3. Jika memakai Google Drive/Sheets, siapkan kredensial Google dan berikan akses folder atau spreadsheet kepada akun layanan yang digunakan.
4. Pasang Python atau Photoshop hanya jika workflow yang dipakai memerlukannya.

**Penyimpanan data:** pengaturan dan data aplikasi disimpan secara lokal menggunakan SQLite di folder data aplikasi. Kunci API dan token Trello disimpan melalui credential vault sistem operasi, bukan sebagai nilai biasa di tabel SQLite. Kredensial Google berbentuk file tetap perlu dijaga sebagai rahasia dan tidak dimasukkan ke repository.

## Shortcut

| Shortcut | Aksi |
|---|---|
| `Ctrl + ,` | Buka Settings |
| `Ctrl + L` | Tampilkan/sembunyikan panel log |
| `Escape` | Tutup popup atau dialog |

## Teknologi

| Komponen | Teknologi |
|---|---|
| UI desktop | Avalonia UI 11 |
| Runtime | .NET 8, C# 12 |
| Pola aplikasi | MVVM, CommunityToolkit.Mvvm |
| Penyimpanan lokal | SQLite melalui Microsoft.Data.Sqlite |
| Integrasi | REST API Trello dan Google; skrip pendamping untuk workflow lain |

## Struktur folder

```text
apps/bmachine/
├── src/
│   ├── BMachine.App/       # Entry point dan aplikasi desktop
│   ├── BMachine.Core/      # Database, keamanan, platform, dan plugin
│   ├── BMachine.SDK/       # Kontrak layanan dan plugin
│   ├── BMachine.UI/        # Tampilan, ViewModel, dan layanan UI
│   └── Plugins/            # Plugin aplikasi
├── Scripts/                # Skrip Photoshop dan Python
├── tests/                  # Regression tests
├── tools/                  # Utilitas pengembangan
├── build.ps1               # Publish Windows x64
└── build.sh                # Publish macOS/Linux
```

## Distribusi

BMachine tidak dimaksudkan untuk didistribusikan ulang secara publik. Repository ini tidak menyediakan lisensi distribusi publik; tanyakan kepada pemilik proyek sebelum menyalin atau menerbitkan build.