# BMachine v2

**BMachine** adalah aplikasi desktop produktivitas untuk manajemen workflow berbasis Trello, dikembangkan dengan **.NET 8** dan **Avalonia UI**.

## ✨ Fitur Utama

- 🗂️ **Trello Integration** - Kelola card Trello langsung dari aplikasi (Editing, Revision, Done, Late Lists)
- 📝 **Manual Card Linking** - Tambahkan card Trello secara manual dengan paste link (batch support)
- 💬 **Comments & Checklists** - Lihat dan kelola komentar serta checklist card
- 📎 **Attachments** - Preview dan download attachment card
- 🔄 **Move Cards** - Pindahkan card antar list dengan cepat
- 🖼️ **Pixelcut Integration** - Batch image processing dengan Pixelcut AI
- 📁 **Folder Locker** - Kunci folder dengan keamanan TOTP
- ☁️ **Google Drive Upload** - Upload file langsung ke Google Drive
- 🎨 **Modern Dark UI** - Tampilan modern dengan tema gelap
- 🔮 **Smart Orb Widget** - Floating widget untuk akses cepat

## 🛠️ Tech Stack

- **Framework**: .NET 8
- **UI**: Avalonia UI 11.x
- **Pattern**: MVVM (CommunityToolkit.Mvvm)
- **Storage**: LiteDB (Local Database)
- **API**: Trello REST API, Google Drive API

## 📦 Project Structure

```
BMachine.v2/
├── src/
│   ├── BMachine.App/      # Main application entry point
│   ├── BMachine.UI/       # Views, ViewModels, Controls
│   ├── BMachine.Core/     # Core services & security
│   └── BMachine.SDK/      # Interfaces & abstractions
├── Scripts/               # Python & JSX automation scripts
└── Data/                  # Runtime data storage
```

## 🚀 Getting Started

### Prerequisites
- .NET 8 SDK
- (Optional) Python 3.x untuk scripts automation

### Build & Run
```bash
# Clone repository
git clone https://github.com/YOUR_USERNAME/BMachine.v2.git
cd BMachine.v2

# Run application
dotnet run --project src/BMachine.App
```

### Configuration
Aplikasi memerlukan konfigurasi berikut (diatur via Settings):
- **Trello API Key & Token** - Untuk integrasi Trello
- **Google API Credentials** - Untuk upload Google Drive

## 📝 License

Private project - All rights reserved.

---

> Developed with ❤️ using .NET & Avalonia UI
