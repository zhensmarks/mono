# 🚀 BMachine v5.0.0 — Cross-Platform Edition

**Release Date:** February 12, 2026

## ✨ What's New

### 🌍 Full Cross-Platform Support
BMachine sekarang berjalan di **Windows, macOS, dan Linux** dari satu codebase!

- **macOS:** Native `.app` bundle (ARM64 + Intel x64)
- **Linux:** Native binary (x64)
- **Windows:** `.exe` (x64) — seperti biasa

### 🏗️ Platform Abstraction Layer
- Semua operasi platform-specific (file explorer, Python scripts, Photoshop JSX, system settings) sekarang menggunakan interface `IPlatformService`
- Otomatis mendeteksi OS dan menggunakan implementasi yang sesuai

### 🖱️ Cross-Platform Input Hooks
- Migrasi dari Win32 API (`user32.dll`) ke **SharpHook** (libuiohook)
- Global mouse/keyboard hooks berjalan di semua platform

### 💾 Data Persistence (Penting!)
- Database dan log sekarang disimpan di **lokasi yang aman**, tidak lagi di dalam folder aplikasi
- **Tidak akan hilang** saat update/replace aplikasi!

| Platform | Lokasi Data |
|----------|-------------|
| 🪟 Windows | `%AppData%\BMachine\` |
| 🍎 macOS | `~/Library/Application Support/BMachine/` |
| 🐧 Linux | `~/.config/BMachine/` |

## 🐛 Bug Fixes
- **Fix crash macOS** saat double-click dari Finder (path permission error)
- **Fix SkiaSharp version mismatch** — pin ke v2.88.8 untuk stabilitas
- **Fix hardcoded `win-x64`** di PixelcutCompact.csproj

## 📦 Downloads

| Platform | File |
|----------|------|
| 🪟 Windows x64 | `BMachine.App.exe` |
| 🍎 macOS ARM (M1/M2/M3) | `BMachine-osx-arm64.app` |
| 🍎 macOS Intel | `BMachine-osx-x64.app` |
| 🐧 Linux x64 | `BMachine.App` (binary) |

## ⚠️ Breaking Changes
- Lokasi database berpindah dari folder aplikasi ke AppData. Settings yang sudah ada perlu di-set ulang pada first launch v5.0.0.
