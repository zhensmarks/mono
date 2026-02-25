# 🚀 BMachine v2

<div align="center">

![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)
![Framework](https://img.shields.io/badge/.NET-8.0-purple.svg)

**Dashboard Automasi All-in-One untuk Manajemen Workflow Kreatif**

[Fitur](#-fitur) • [Instalasi](#-instalasi) • [Penggunaan](#-cara-penggunaan) • [Konfigurasi](#%EF%B8%8F-konfigurasi) • [Update](#-update)

</div>

---

## 📋 Deskripsi

BMachine adalah dashboard automasi komprehensif yang dirancang untuk mempermudah pengelolaan workflow kreatif. Aplikasi ini mengintegrasikan berbagai layanan seperti **Trello**, **Google Sheets**, **Google Drive**, dan **Pixelcut AI** dalam satu antarmuka yang modern dan mudah digunakan.

---

## ✨ Fitur

### 🏠 Dashboard Utama
| Fitur | Deskripsi |
|-------|-----------|
| **Statistik Real-time** | Lihat metrik Editing, Revision, Late, dan Points dalam sekali pandang |
| **Smart Orb** | Widget mini floating untuk akses cepat ke statistik dan kontrol utama |
| **Activity Log** | Panel log terintegrasi untuk memantau proses background dan error |
| **Panel Kustomisasi** | Atur tampilan, tema, dan warna sesuai preferensi |

---

### 📋 Integrasi Trello
Kelola kartu Trello langsung dari aplikasi dengan fitur-fitur canggih:

- **📊 Kanban Lists** - Tampilan list "Editing", "Revision", dan "Late" 
- **🔄 Batch Move** - Pindahkan banyak kartu sekaligus antar list
- **🔗 Unlink** - Hapus kartu dari list "Manual" dengan satu klik
- **🎨 Color Coding** - Penanda visual untuk status kartu (Merah untuk revisi, dll)
- **📝 Card Details** - Lihat detail kartu termasuk label, due date, dan attachment
- **⚡ Quick Actions** - Aksi cepat untuk operasi yang sering digunakan

---

### 🖼️ Pixelcut (AI Image Processing)
Pemrosesan gambar berbasis AI yang powerful:

| Fitur | Deskripsi |
|-------|-----------|
| **Background Removal** | Hapus background gambar secara otomatis dengan AI |
| **Upscaling** | Tingkatkan resolusi gambar menggunakan AI |
| **Smart Queue** | Antrian pemrosesan dengan auto-retry untuk item yang gagal |
| **Drag & Drop** | Cukup drag file untuk memulai pemrosesan |
| **Compact View** | Tampilan ringkas khusus untuk pemrosesan file |
| **Batch Processing** | Proses banyak gambar sekaligus |

---

### ☁️ Google Drive Sync
Sinkronisasi file dengan Google Drive:

- **📤 Upload Mudah** - Drag & drop file untuk upload ke folder Month/Date
- **📁 Folder Otomatis** - Folder dibuat otomatis berdasarkan tanggal
- **✅ Multi-Selection** - Dukungan Shift+Click dan multi-select standar
- **📊 Progress Tracking** - Progress bar real-time untuk setiap upload
- **🔄 Auto Sync** - Sinkronisasi otomatis dengan folder lokal

---

### 📊 Google Sheets Integration
Integrasi dengan Google Sheets untuk tracking data:

- **📈 Auto Sync** - Data otomatis tersinkron dengan spreadsheet
- **📋 Template System** - Sistem template untuk berbagai jenis laporan
- **⏰ Real-time Update** - Update data secara real-time

---

### 🎨 Kustomisasi & Tema

| Fitur | Deskripsi |
|-------|-----------|
| **Dark/Light Mode** | Pilih tema gelap atau terang sesuai preferensi |
| **Custom Accent Color** | Atur warna aksen aplikasi |
| **Smart Orb Customization** | Kustomisasi warna dan posisi Smart Orb |
| **Window State Memory** | Aplikasi mengingat ukuran dan posisi terakhir |

---

### 🔐 Keamanan

- **🔒 Credential Protection** - File kredensial tidak disertakan dalam build
- **🔑 Secure Token Storage** - Token disimpan dengan aman di database lokal
- **🌐 External Auth** - Autentikasi melalui browser untuk keamanan maksimal

---

### 🔄 Sistem Update

- **📥 Auto Check** - Cek update otomatis saat startup
- **🔔 Notifikasi** - Notifikasi di header jika ada update baru
- **📋 Release Notes** - Lihat catatan rilis sebelum mengupdate
- **⬇️ One-Click Download** - Download update langsung dari aplikasi

---

## 💾 Instalasi

### Prasyarat
- Windows 10/11
- .NET 8 Runtime (sudah termasuk jika menggunakan versi portable)

### Script Requirements (Opsional)
Beberapa fitur memerlukan software tambahan:

| Software | Versi | Diperlukan Untuk |
|----------|-------|------------------|
| **Python** | 3.10+ | Script automasi (`.py`, `.pyw`) |
| **Adobe Photoshop** | CC 2020+ | Script JSX untuk edit PSD |

#### Instalasi Python
1. Download Python dari [python.org](https://python.org)
2. Saat install, **centang** "Add Python to PATH"
3. Install dependencies:
   ```bash
   pip install requests pillow
   ```

#### Script JSX (Adobe Photoshop)
- Script `.jsx` akan otomatis berjalan jika **Adobe Photoshop** terpasang
- Pastikan Photoshop sudah diset sebagai default untuk file `.jsx`
- File JSX ada di folder `Scripts/Action/`

#### Lokasi Script
```
Scripts/
├── Master/          # Script utama (.py)
├── Action/          # Script Photoshop (.jsx, .pyw)
└── *.txt            # File template/instruksi
```

### Cara Install

#### Metode 1: Download Release (Recommended)
1. **Download** file `BMachine.App.exe` dari [Releases](https://github.com/zhensmarks/BMachine.v2/releases)
2. **Jalankan** file tersebut - tidak perlu instalasi!

#### Metode 2: Build dari Source
```bash
# Clone repository
git clone https://github.com/zhensmarks/BMachine.v2.git
cd BMachine.v2

# Restore dependencies
dotnet restore

# Jalankan aplikasi
dotnet run --project src/BMachine.App

# Atau build untuk produksi
.\build.ps1
```

---

## 📖 Cara Penggunaan

### Pertama Kali
1. **Jalankan** aplikasi `BMachine.App.exe`
2. **Buka Settings** (ikon gear di header)
3. **Konfigurasi** integrasi yang diperlukan:
   - Masukkan **Trello API Key** dan **Token** (gunakan tombol "Get Key" dan "Get Token")
   - Letakkan file `credentials.json` untuk Google Drive/Sheets
4. **Simpan** dan mulai menggunakan!

### Shortcut Keyboard
| Shortcut | Aksi |
|----------|------|
| `Ctrl + ,` | Buka Settings |
| `Ctrl + L` | Toggle Log Panel |
| `Escape` | Tutup popup/dialog |

---

## ⚙️ Konfigurasi

### Trello Integration
1. Buka **Settings** → **Trello Integration**
2. Klik **"Get Key"** untuk mendapatkan API Key dari Trello
3. Klik **"Get Token"** untuk mengotorisasi akses
4. Masukkan **Board ID** dan **Workspace ID**

### Google Drive/Sheets
1. Buat **Service Account** di [Google Cloud Console](https://console.cloud.google.com)
2. Download file JSON credentials
3. Rename menjadi `credentials.json`
4. Letakkan di folder yang sama dengan aplikasi
5. **Share** spreadsheet/folder ke email service account

### Pixelcut
Pixelcut **tidak memerlukan API key**. Fitur ini menggunakan Python script yang berkomunikasi langsung dengan layanan Pixelcut.

**Requirement:**
- Python 3.10+ terinstall
- **VPN aktif** (diperlukan untuk mengakses layanan Pixelcut dari Indonesia)
- Proxy address dapat dikonfigurasi di Settings jika diperlukan

---

## 🔄 Update

Aplikasi akan **otomatis mengecek update** saat startup. Jika ada update:

1. **Ikon hijau** akan muncul di header (sebelah Settings)
2. Atau buka **Settings** → **About** → **Check for Updates**
3. Klik **"Download Update"** untuk membuka halaman download
4. Download dan replace file exe lama dengan yang baru

---

## 🛠️ Tech Stack

| Komponen | Teknologi |
|----------|-----------|
| **Framework** | Avalonia UI (.NET 8) |
| **Bahasa** | C# |
| **Pattern** | MVVM (CommunityToolkit.Mvvm) |
| **Database** | LiteDB |
| **API** | REST (Trello, Google, Pixelcut) |

---

## 📁 Struktur Proyek

```
BMachine.v2/
├── src/
│   ├── BMachine.App/       # Main application
│   ├── BMachine.UI/        # UI components & ViewModels
│   ├── BMachine.Core/      # Core business logic
│   └── BMachine.SDK/       # SDK & interfaces
├── Scripts/                # Action scripts (JSX, Python)
├── publish/                # Build output
└── build.ps1               # Build script
```

---

## 📝 Changelog

### v2.0.0 (Latest)
- ✨ Sistem update otomatis dari GitHub Releases
- ✨ Tab "About" baru di Settings
- ✨ Tombol "Get Key" dan "Get Token" untuk Trello
- 🔒 Peningkatan keamanan kredensial
- 📦 Single-file executable publish

---

## 📄 Lisensi

Proyek ini bersifat private dan tidak untuk distribusi publik.

---

<div align="center">

**Made with ❤️ by Zhensmarks**

</div>
