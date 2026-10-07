# Rangkuman Project BMachine

> Dokumen ini disusun **langsung dari file aktual di repo** (`D:/#DATA ABENG/#PROJECT/mono/apps/bmachine`).
> Setiap nilai token, nama brush, dan path folder di bawah ini diverifikasi dengan membaca file sumbernya.
> Sumber utama: `README.md`, `AGENTS.md`, `DESIGN_SYSTEM.md`, `src/BMachine.*/**`, `build.ps1`, `publish.bat`.

---

## 1. Apa itu BMachine + Tujuan

**BMachine** adalah *dashboard automasi all-in-one* berbasis desktop untuk mempercepat **workflow kreatif (editor Photoshop)**. Deskripsi asli di `IDEA.md`:

> "aplikasi untuk mempermudah editor photoshop yang memakai photoshop, trello, spreadsheet, custom"

Aplikasi menyatukan beberapa layanan pihak ketiga dalam satu antarmuka modern:

- **Trello** — manajemen kartu (list Editing / Revision / Late, batch move, unlink, color coding, detail kartu).
- **Google Sheets & Google Drive** — tracking data dan sinkronisasi file (upload drag & drop, folder otomatis per tanggal).
- **Pixelcut AI** — pemrosesan gambar (hapus background, upscaling) via script Python.
- **Adobe Photoshop** — menjalankan script `.jsx`/`.pyw` untuk otomasi edit PSD.
- **Spreadsheet / MantraData** — pengolahan data (Excel/Word parsing, transformasi, merge foto, yearbook layout).

**Tujuan utama:** mempermudah dan mempercepat pekerjaan editor dengan otomasi, pengelolaan data, dan integrasi layanan dalam satu aplikasi; ditambah keamanan kredensial, tema yang bisa dikustom, sistem update otomatis, dan arsitektur plugin.

- **Versi aktual (dari `src/BMachine.App/BMachine.App.csproj`): `<Version>8.3.0</Version>`** (README masih menampilkan badge 6.5.0 — sudah usang).
- Platform target: **Windows 10/11** (publish `win-x64`), tetapi kode sudah punya lapisan Platform untuk macOS/Linux.
- Proyek bersifat **private**, tidak untuk distribusi publik (`README.md`).

---

## 2. Tech Stack & Arsitektur

### 2.1 Tech Stack

| Komponen | Teknologi | Sumber |
|----------|-----------|--------|
| Framework UI | **Avalonia UI 11.3.22** | `BMachine.App.csproj` |
| Bahasa | **C# 12** | `AGENTS.md` |
| Runtime | **.NET 8** (`net8.0`) | semua `.csproj` |
| Pola | **MVVM** — `CommunityToolkit.Mvvm 8.2.2` (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`) | `BMachine.UI.csproj` |
| Database | **SQLite** via `Microsoft.Data.Sqlite 10.0.12` (KeyValueStore + Activities) | `BMachine.Core.csproj` |
| Tema kontrol | `Semi.Avalonia 11.2.0` + Fluent | `App.axaml` |
| Render grafis | `SkiaSharp 2.88.9` | `BMachine.App.csproj` |
| Excel/Word | `ClosedXML 0.105.1`, `MiniExcel 1.46.0`, `DocumentFormat.OpenXml 3.5.1` | `BMachine.UI.csproj` |
| Google API | `Google.Apis.Drive.v3`, `Google.Apis.Sheets.v4` | `BMachine.UI.csproj` |
| Gambar | `Magick.NET-Q8-AnyCPU`, `SkiaSharp` | `BMachine.UI.csproj` |
| Input global | `SharpHook 5.3.7` | `BMachine.UI.csproj` |
| Keamanan | `Otp.NET 1.4.1` (TOTP), `QRCoder 1.6.0` | `BMachine.Core.csproj` |
| Arsitektur | MVVM + Plugin system (load DLL runtime) + Event Bus (WeakReferenceMessenger) | `BMachine.Core/PluginSystem` |
| Test | **xUnit** (`xunit 2.9.3`) — `BMachine.UI.RegressionTests` | `tests/**` |
| Ikon | **Tabler Icons** (outline, MIT) sebagai `StreamGeometry` | `Styles/TablerIcons.axaml` |

### 2.2 Layer / Project

Solution: **`BMachine.v2.sln`** berisi 5 project + 1 test project.

```
src/
├── BMachine.App        -> aplikasi WinExe (entry point, MainWindow, App, Program)
├── BMachine.UI         -> UI (Views, ViewModels, Controls, Services, Styles, Themes, Models, Messages)
├── BMachine.Core       -> logika inti (Database, Platform, PluginSystem, Security)
├── BMachine.SDK        -> kontrak/interface (IPlugin, IServices, IThemeService, Attributes, Events)
├── Plugins/
│   └── BMachine.Plugin.Dashboard  -> contoh plugin dashboard
└── (shared, di luar src/)
    shared/BMachine.Shared.BugReporter  -> library bersama pelapor bug (dipakai App & UI)
tests/
└── BMachine.UI.RegressionTests      -> xUnit regression test (AXAML + VM)
```

**Arah dependensi (dari ProjectReference di `.csproj`):**

```
BMachine.SDK      (paling dasar, tanpa dependensi proyek lain)
   ▲
BMachine.Core ─────┘            (ref: SDK)
   ▲
BMachine.UI  (ref: SDK, Core, shared/BMachine.Shared.BugReporter)
   ▲
BMachine.App (ref: SDK, Core, UI, shared/BMachine.Shared.BugReporter)  <- WinExe
   ▲
BMachine.Plugin.Dashboard (ref: SDK, UI)
```

Peran tiap layer:

- **BMachine.SDK** — kontrak stabil: `IPlugin`, `IPluginContext`, `IWidget`, `IMenuEntry`, `IEventBus`, `IDatabase`, `INavigationService`, `INotificationService`, `IActivityService`, `ILogger`, `IThemeService`, `ILanguageService`; atribut `PluginInfo`, `MenuEntry`, `Widget`; base class `BasePlugin`; event `ContextMenuExecuteEvent`. Hanya mendefinisikan interface/POCO (tanpa paket eksternal).
- **BMachine.Core** — implementasi inti lintas-platform:
  - `Database/DatabaseService.cs` — implementasi `IDatabase` + `IActivityService` di atas SQLite (`KeyValueStore`, `Activities`). **Kredensial Trello (`Trello.ApiKey`, `Trello.Token`) tidak disimpan di DB**, melainkan di OS credential vault.
  - `Platform/` — `IPlatformService` + `WindowsPlatformService`, `MacPlatformService`, `LinuxPlatformService`, `PlatformServiceFactory` (reveal file, open folder, jalankan Python/JSX, AppData dir, recycle bin, dll).
  - `PluginSystem/` — `PluginManager`, `PluginContext`, `PluginManifest` (`plugin.json`), memuat `*.dll` via `Assembly.LoadFrom`.
  - `Security/` — `AesGcmCryptor` (AES-256-GCM, format kompatibel Python DMA Locker: HEADER `DMA2` + SALT 16 + NONCE 12 + CIPHERTEXT, PBKDF2 200.000 iterasi), `FolderLockerService`, `FolderLockerConfig`, `TotpService`, `PlatformCredentialStore` (Windows Credential / macOS Keychain / Linux Secret Service).
- **BMachine.UI** — seluruh tampilan & logika UI. Berisi `Views/` (33 `.axaml`), `ViewModels/` (32 `.cs`), `Controls/`, `Services/`, `Styles/`, `Themes/`, `Models/`, `Messages/` (39 file message), `Converters/`, `ViewLocator.cs`.
- **BMachine.App** — entry point `Program.cs` (global crash handler → `%AppData%\BMachine\crash_report.txt` + laporan ke BugReporter/ProjectBot), `App.axaml`/`App.axaml.cs` (bootstrap: Splash → Bootstrapper → MainWindow), `Views/MainWindow.axaml`, `ViewModels/MainWindowViewModel.cs`.
- **shared/BMachine.Shared.BugReporter** — `BugReporter.cs`: klien HTTP tahan-banting, `POST http://127.0.0.1:21478/bug` (fire-and-forget, tidak pernah throw) untuk lapor crash/bug ke ProjectBot.

**Alur startup** (`App.axaml.cs`): tampilkan `SplashWindow` → `Bootstrapper.InitializeAsync()` (DB → Theme → SettingsPreload → verifikasi script → layanan → `PluginManager.LoadAllPluginsAsync()` dari folder `plugins/`) → buat `MainWindow` + `MainWindowViewModel` → pulihkan posisi/ukuran window → swap Splash → MainWindow. Setelah itu hook input global (mouse wheel, radial menu, shortcut recording) diinisialisasi.

---

## 3. Fitur / Modul Utama & Struktur Folder

### 3.1 Struktur folder `src/BMachine.UI` (inti UI)

```
BMachine.UI/
├── Assets/Avatars/          # aset avatar
├── Controls/                # ActionCard, ActivityFeed, FormattedTextBlock, StatWidget (+ PieWedge, ThumbnailWrapPanel, AutoCompleteBoxHelper)
├── Converters/              # BoolToColor, BoolToFolderIcon, DoubleToGridLength, Generic, Log
├── Messages/                # 39 pesan messenger (navigasi, toast, refresh, dll)
├── Models/                  # TrelloCard, TrelloAttachment, TrelloChecklist, TrelloComment, TrelloMember,
│                            #   MasterNode, MasterFileItem/Group, ScriptConfig, TriggerConfig, UpdateInfo, LogItem,
│                            #   BatchScriptOption, RadialMenuItem, RangeObservableCollection, PendingAttachmentItem
│   └── MantraData/          # DataJobKind, MantraDataSettings, ProcessReport, TableDataRow, TransformModels
├── Services/                # Bootstrapper, ThemeService, LanguageService, EventBus, ProcessLogService,
│                            #   ToastNotificationService, NotificationService, UpdateService, FileOperationManager,
│                            #   GlobalMouseHookService, SettingsPreloadService, SystemIconService, ThumbnailCacheService,
│                            #   TrelloRequestSecurity, PluginAdapters, BmachineContextService
│   ├── Explorer/            # SevenThemePackageService
│   └── MantraData/          # ExcelParserService, WordParserService, DataCleanerService, FormulaEngine,
│                            #   QuickTransformService, TransformService, PhotoMatcherService, PhotoshopBridgeService,
│                            #   YearbookLayoutService, LocalAIService
├── Styles/                  # BMachineDesignSystem.axaml, TrelloNavigation.axaml, TablerIcons.axaml
├── Themes/                  # DarkTheme.axaml, LightTheme.axaml
├── ViewModels/              # 32 VM (Dashboard, Batch, UnifiedTrello, MantraData, FolderLocker, Settings, Toolbox, dll)
├── Views/                   # 33 view
│   ├── Controls/            # InAppToast, LoadingOverlay
│   └── Dialogs/             # DocSettingsWindow, MantraData/* (AiSettings, Cleaner, CustomMerge, FindReplace,
│                            #   Alert/Confirm/Input, ContextMenuSettings, ProcessPsd, ReportBug, SplitColumn, Transform)
└── ViewLocator.cs           # konvensi nama ViewModel->View
```

### 3.2 Modul / Fitur utama (dari Views + ViewModels)

| Modul | View utama | VM | Ringkas |
|-------|-----------|----|---------|
| **Dashboard** | `DashboardView.axaml` | `DashboardViewModel` | Statistik (Editing/Revision/Late/Points), kartu bento, header nav, Smart Orb, InAppToast |
| **Batch** | (di DashboardView, tab BATCH) | `BatchViewModel` | Batch move, Batch Auto Replace, tree folder, context menu |
| **Trello Unified** | `UnifiedTrelloView.axaml` | `UnifiedTrelloViewModel` | Kanban Editing/Revision/Late, detail kartu, komentar, attachment |
| **Trello Card Lists** | `EditingCardListView`, `RevisionCardListView`, `LateCardListView`, `CardListWindow` | `*CardListViewModel`, `BaseTrelloListViewModel` | List kartu per status |
| **Folder Locker** | `FolderLockerView.axaml` | `FolderLockerViewModel` | Kunci folder AES-256-GCM + verifikasi TOTP |
| **MantraData** | `MantraDataView.axaml` + banyak dialog | `MantraDataViewModel` | Pengolahan data: Excel/Word, cleaner, formula engine, transform, merge, yearbook, AI lokal |
| **MantraNama / MantraGanda / PsdBucin** | (di `ToolboxWindow`) | `MantraNamaViewModel`, `MantraGandaViewModel`, `PsdBucinViewModel` | Toolbox otomasi Photoshop |
| **Spreadsheet** | `SpreadsheetView`/`SpreadsheetWindow` | `SpreadsheetViewModel` | Editor spreadsheet internal |
| **Explorer** | `ExplorerWindow`, `OutputExplorerView`, `ExplorerSettingsView/Window` | `ExplorerWindowViewModel`, `OutputExplorerViewModel`, `ExplorerSettingsViewModel` | File explorer + output explorer |
| **Leaderboard** | `LeaderboardView`, `PointLeaderboardView/Window` | `PointLeaderboardViewModel` | Peringkat poin (Harian/Bulanan) |
| **Settings** | `SettingsView.axaml` | `SettingsViewModel` | General, Appearance, Account, Extensions, Script Manager, Paths, About |
| **Radial Menu** | `RadialMenuWindow.axaml` | `RadialMenuViewModel` | Menu radial dipanggil via shortcut global |
| **Task Monitor** | `TaskMonitorView/Window` | — | Monitoring proses |
| **Log Panel** | `LogPanelSidebar.axaml` | (di Dashboard) | Tab EXPLORER / LOG / MASTER / DOC (+ PS tersembunyi) |
| **Doc Panel** | `DocPanelView.axaml`, `DocFloatingWindow.axaml` | `DocPanelStatePolicy` | Panel dokumen, bisa di-float |
| **Splash** | `SplashWindow.axaml` | `SplashViewModel` | Splash saat bootstrap |

### 3.3 Script pendukung (`Scripts/`, 21 file)

```
Scripts/
├── Master/     buat_master.py, manasik.py, pasfoto.py, profesi_flat.py, wisuda.py
├── Action/     C-REDAKSI.jsx, FOOTNOTE.jsx, NOMORSATSET.jsx, "OPEN PSD.jsx", replace.jsx,
│               save_master.jsx, seleksi_alam.jsx, SEND-LOGO.jsx, SEND-TEXT.jsx,
│               _kembalikan_seleksi.jsx, _place_on_layer.jsx
├── batch_wrapper.py, bdater_process.jsx, folder_locker_v2.py, psdbucin_v3.pyw
└── scripts.json   # metadata script (Name, Code, IconKey, Order)
```
`scripts.json` memetakan script → `IconKey` Tabler (mis. `replace.jsx` → `IconSkull`, `save_master.jsx` → `IconSave`). Script `.py/.pyw/.jsx` + `scripts.json` disalin ke output oleh `BMachine.App.csproj` (`LinkBase="Scripts\"`).

---

## 4. Cara Build / Publish / Run

### 4.1 Prasyarat
- **.NET 8 SDK**.
- Opsional: **Python 3.10+** (script automasi), **Adobe Photoshop CC 2020+** (script `.jsx`).
- `nuget.config` mengarah ke `https://api.nuget.org/v3/index.json`.

### 4.2 Run (development)
```bash
dotnet restore
dotnet run --project src/BMachine.App
```
(`Program.cs` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.)

### 4.3 Build (kompilasi)
- Per app: `apps/bmachine/build.ps1` (lihat 4.4 — ini sebenarnya **publish**).
- Monorepo root `mono/build.ps1`:
  ```powershell
  powershell -File build.ps1                 # build semua app
  powershell -File build.ps1 -App bmachine   # BMachine saja
  powershell -File build.ps1 -Config Debug   # default Release
  ```
  Menjalankan `dotnet build <BMachine.App.csproj> -c <Config> --nologo`.
- `mono/build.bat` — pembungkus klik-jalan (menu interaktif atau argumen `bmachine|pixacompact|all`), memanggil `dotnet build -c Release --nologo -v:q -p:NuGetAudit=false`.

### 4.4 Publish (single-file, self-contained, win-x64) — `apps/bmachine/build.ps1`
- Output dir: `$MONO/BMachine` (dua level di atas folder app → root monorepo `mono/BMachine`).
- Perintah inti:
  ```powershell
  dotnet publish src\BMachine.App\BMachine.App.csproj -c Release -r win-x64 `
      --self-contained true -p:PublishSingleFile=true -o <mono>\BMachine
  ```
- **Preserve data pengguna**: sebelum publish, script memindahkan `crash.log`, `plugins/`, `Plugins/`, `Scripts\Others`, `Scripts\Output`, `Scripts\Temp` ke `.publish-preserve\bmachine`, lalu memulihkannya setelah publish (termasuk bila publish gagal) — agar plugin/skrip buatan user tidak terhapus.
- Output akhir: **`mono\BMachine\BMachine.App.exe`**.
- Properti publish di csproj: `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, `DebugType=none`, `DebugSymbols=false`, `ApplicationIcon=appicon.ico`.

### 4.5 Publish semua app — `mono/publish.bat` (+ `mono/publish.ps1`)
- `publish.bat` (interaktif/argumen) memanggil `apps\bmachine\build.ps1` dan/atau `apps\pixacompact\build.ps1`:
  ```bat
  publish.bat bmachine      :: atau pixacompact / all, tanpa argumen => menu
  ```
- Output: `mono\BMachine\BMachine.App.exe` (~140 MB) dan `mono\PixaCompact\PixelcutCompact.exe` (~2 GB).

### 4.6 Cross-platform build — `apps/bmachine/build.sh`
- macOS: publish `osx-arm64`/`osx-x64` (`PublishSingleFile=false`), membuat bundle `.app`, memperbaiki `libSkiaSharp.dylib` v2.88.8, `xattr -cr` (hapus quarantine).
- Linux: `dotnet publish -r linux-x64 --self-contained true -p:PublishSingleFile=true`.
- Windows: gunakan `build.ps1`.

### 4.7 Test
```bash
dotnet test tests/BMachine.UI.RegressionTests
```
`BMachine.UI.RegressionTests` memverifikasi aturan desain langsung dari file AXAML: nav **tanpa ikon** (text-only), tab LogPanel (`EXPLORER/LOG/MASTER/DOC`, font 11), `PanelNavigationLayout` (`*,Auto,*,*,*`), style tema radial hover, dll.

### 4.8 Versi & distribusi (dari `WORKSPACES.md`)
- Versi dilacak di `BMachine.App.csproj` (`<Version>` = **8.3.0**), format SemVer.
- Update parsial: `PluginManager` memuat `*.dll` dari folder `plugins/` → cukup kirim ulang DLL (mis. `BMachine.Plugin.Dashboard.dll`).
- Bug report otomatis → ProjectBot (Telegram) via `shared/BMachine.Shared.BugReporter` → `POST 127.0.0.1:21478/bug`.

---

## 5. ATURAN DESIGN SYSTEM (detail)

> **Sumber kebenaran token:** `src/BMachine.UI/Styles/BMachineDesignSystem.axaml` (token & style),
> `src/BMachine.UI/Themes/DarkTheme.axaml` & `LightTheme.axaml` (brush warna),
> `src/BMachine.UI/Styles/TrelloNavigation.axaml` (nav/pill),
> `src/BMachine.UI/Styles/TablerIcons.axaml` (ikon),
> `src/BMachine.App/App.axaml` (override global: Switch, ContextMenu, MenuItem).
>
> **Prinsip:** jangan hardcode warna/radius/font di View — pakai token & `Classes`. Jika butuh varian baru, tambah di DesignSystem, bukan di View.

### 5.1 Typography

Token font (`BMachineDesignSystem.axaml`):

| Token | Nilai |
|-------|-------|
| `FontPrimary` | `Inter, Segoe UI Variable, Segoe UI, SF Pro Text, Helvetica Neue, Arial, sans-serif` |
| `FontMono` | `Cascadia Code, JetBrains Mono, Consolas, monospace` |

`TextBlock` default: `FontFamily=FontPrimary`, `FontWeight=Regular`, `Foreground=TextPrimaryBrush`.

Classes `TextBlock`:

| Class | Setter |
|-------|--------|
| `h1` | FontSize 24, Bold, LetterSpacing 0.3 |
| `h2` | FontSize 18, SemiBold |
| `h3` | FontSize 14, SemiBold |
| `body` | FontSize 13, `TextSecondaryBrush` |
| `caption` | FontSize 11, `TextMutedBrush` |
| `eyebrow` | FontSize 10.5, SemiBold, `TextFaintBrush`, LetterSpacing 1.2, `FontMono` |
| `mono` | `FontMono`, FontSize 12 |

> Catatan: `AGENTS.md` menyebut "line-height 20" untuk default TextBlock, tetapi `BMachineDesignSystem.axaml` tidak menyetel line-height default. `DESIGN_SYSTEM.md` menulis `caption` = muted `#71717A`; nilai aktual `TextMutedBrush` = `#A1A1AA` (dark) / `#52525B` (light).

### 5.2 Token Radius, Spacing, Icon (`BMachineDesignSystem.axaml`)

| Token | Nilai | Dipakai untuk (dari style) |
|-------|-------|-----------------------------|
| `RadiusSm` | **6** | Button, TextBox/ComboBox/input, CheckBox, Tooltip, MenuItem |
| `RadiusMd` | **8** | `Border.cardSm`, `TextBox.search`, InAppToast (`RadiusMd`) |
| `RadiusLg` | **10** | `Border.card` / `Border.Card` (bento) |
| `RadiusXl` | **12** | (modal/dialog) |
| `RadiusPill` | **999** | pill/avatar |
| `SpaceXs` | 4 | |
| `SpaceSm` | 8 | |
| `SpaceMd` | 12 | |
| `SpaceLg` | 16 | `Border.Card` padding |
| `SpaceXl` | 20 | |
| `IconXs` | **12** | class `PathIcon.xs` |
| `IconSm` | **14** | class `PathIcon.sm` |
| `IconMd` | **16** | default `PathIcon` |
| `IconLg` | **18** | class `PathIcon.lg` |
| `IconXl` | **24** | class `PathIcon.xl` |

### 5.3 Warna (semantic brush) — nilai hex aktual per tema

| Token brush | **Dark** (`DarkTheme.axaml`) | **Light** (`LightTheme.axaml`) |
|-------------|------------------------------|-------------------------------|
| `AppBackgroundBrush` | `#121212` | `#F5F5F5` |
| `CardBackgroundBrush` | `#1A1C20` | `#FFFFFF` |
| `BackgroundBrush` (surface elevated) | `#202226` | `#F1F3F5` |
| `SeparatorBrush` | `#26282C` | `#E5E7EB` |
| `NavigationDividerBrush` | **`#656A73`** | **`#737D8B`** |
| `BorderSubtleBrush` | `#3A3D42` | `#E1E5EA` |
| `BorderDefaultBrush` | `#303036` | `#CBD5E1` |
| `BorderHoverBrush` | `#484850` | `#94A3B8` |
| `BorderFocusBrush` | `#5B8DEF` | `#2563EB` |
| `BgHoverBrush` | `#25282D` | `#F2F4F7` |
| `BgActiveBrush` | `#333B82F6` | `#1A3B82F6` |
| `BgInputBrush` | `#0C0C0E` | `#FFFFFF` |
| `BgInputHoverBrush` | `#111113` | `#F9FAFB` |
| `TextPrimaryBrush` | `#FFFFFF` | `#121212` |
| `TextSecondaryBrush` | `#99FFFFFF` | `#666666` |
| `TextMutedBrush` | `#A1A1AA` | `#52525B` |
| `TextFaintBrush` | `#8B8B92` | `#52525B` |
| `TextOnPrimaryBrush` | `White` | `White` |
| `AccentBlueBrush` / `AccentColorBrush` | `#3b82f6` | `#3b82f6` |
| `AccentOrangeBrush` | `#f97316` | `#f97316` |
| `AccentPointsBrush` | `#f97316` | `#f97316` |
| `AccentSubtleBrush` | `#263B82F6` (15% accent) | `#263B82F6` |
| `AccentSubtleHoverBrush` | `#333B82F6` | `#333B82F6` |
| `AccentLowOpacityBrush` | `#263B82F6` | `#1A3B82F6` |
| `ButtonPrimaryBackgroundBrush` | `#EDEDED` | `#EDEDED` |
| `ButtonPrimaryForegroundBrush` | `#111111` | `#111111` |
| `ButtonPrimaryHoverBrush` | `#FFFFFF` | `#FFFFFF` |
| `ButtonPrimaryPressedBrush` | `#D4D4D4` | `#D4D4D4` |
| `ButtonSecondaryBackgroundBrush` | `#1C1C1F` | `#F3F4F6` |
| `ButtonSecondaryForegroundBrush` | `#E4E4E7` | `#1F2937` |
| `ButtonSecondaryBorderBrush` | `#3F3F46` | `#CBD5E1` |
| `ButtonSecondaryHoverBrush` | `#27272B` | `#E5E7EB` |
| `ButtonSecondaryPressedBrush` | `#222226` | `#D1D5DB` |
| `ButtonDangerBackgroundBrush` | `#2A1114` | `#FEF2F2` |
| `ButtonDangerForegroundBrush` | `#FF9FA5` | `#991B1B` |
| `ButtonDangerBorderBrush` | `#7A353C` | `#FECACA` |
| `ButtonDangerHoverBrush` | `#37171C` | `#FEE2E2` |
| `ButtonDangerPressedBrush` | `#451A21` | `#FECACA` |
| `SwitchOffBackgroundBrush` | `#1AFFFFFF` | `#E5E7EB` |
| `SwitchOffBorderBrush` | `#26FFFFFF` | `#CBD5E1` |
| `SwitchHoverBackgroundBrush` | `#25FFFFFF` | `#D1D5DB` |
| `MenuHoverBrush` | `#10FFFFFF` | `#10000000` |
| `MenuSelectedBrush` | `#1E1E1E` | `#C9C9C9` |
| `AccentRedBrush` / `AccentGreenBrush` | `#ef4444` / `#22c55e` | `#dc2626` / `#16a34a` |
| `AccentEditingBrush`/`Revision`/`Late` | `AccentBlue`/`AccentOrange`/`#ef4444` | idem |
| `LogInfo/System/Success/Warning/Error/DebugBrush` | `#60a5fa`/`#2dd4bf`/`#4ade80`/`#fb923c`/`#f87171`/`#9ca3af` | `#004d99`/`#006666`/`#15803d`/`#c2410c`/`#b91c1c`/`#525252` |
| `TerminalBackgroundBrush` | `#1E1E1E` | `#F8F9FA` |
| `TerminalTextBrush` | `#E5E7EB` | `#1F2937` |

> ⚠️ **Catatan akurasi:** `DESIGN_SYSTEM.md` memuat beberapa nilai yang **sudah tidak sinkron** dengan file tema aktual (mis. `BorderDefaultBrush` ditulis `#232326` padahal aktual `#303036` dark / `#CBD5E1` light; `BorderFocusBrush` ditulis `#4B4B52` padahal aktual `#5B8DEF`/`#2563EB`; `TextMutedBrush` ditulis `#71717A` padahal aktual `#A1A1AA`/`#52525B`). **Gunakan nilai di tabel ini (dari file `.axaml`)** sebagai acuan.

### 5.4 Button — 4 varian selaras

Style dasar `Button` (`BMachineDesignSystem.axaml`):
`FontFamily=FontPrimary`, `FontSize=12.5`, `FontWeight=SemiBold`, `CornerRadius=RadiusSm (6)`, `Padding=16,8`, **`Height=36`**, `Cursor=Hand`, transition Background/BorderBrush `0.15s` + Opacity `0.1s`.

| Classes | Background (dark) | Foreground (dark) | Border | Hover | Pressed |
|---------|-------------------|-------------------|--------|-------|---------|
| `primary` / `Action` | `#EDEDED` | `#111111` | 0 | `#FFFFFF` | `#D4D4D4` |
| `secondary` / `Muted` | `#1C1C1F` | `#E4E4E7` | 1px `#3F3F46` | `#27272B` | `#222226` |
| `ghost` / `Clear` | Transparent | inherit | 0 | `BgHoverBrush` | `BgActiveBrush` |
| `danger` / `Warn` | `#2A1114` | `#FF9FA5` | 1px `#7A353C` | `#37171C` | `#451A21` |
| `accent` / `Accent` | Transparent | `AccentColorBrush` | 1px Accent | `AccentSubtleBrush` | — |

Semua button disabled: `Opacity=0.4`, `Cursor=Arrow`.

**Varian khusus (dengan tinggi/ukuran khusus):**

| Class | Ukuran | Catatan |
|-------|--------|---------|
| `Button.StatBtn` | `Height=NaN`, `MinHeight=110`, Padding 16, stretch | pengecualian stat widget dashboard |
| `Button.docTool` | **32×32**, MinWidth/MinHeight 32, Padding 6, radius 6, bg `ButtonSecondaryBackgroundBrush`, border `BorderDefaultBrush` | toolbar kecil Doc; hover border `BorderHoverBrush`; focus border `BorderFocusBrush`; disabled opacity 0.4 |
| `Button.IconBtn` / `Button.iconBtn` | **32×32**, MinWidth/MinHeight 32, Padding 6, bg/border Transparent, fg `TextSecondaryBrush`, radius 6 | hover bg `BgHoverBrush` + border `BorderHoverBrush` + fg `TextPrimaryBrush`; pressed `ButtonSecondaryPressedBrush`; focus `BorderFocusBrush`; disabled opacity 0.45; varian `.primary` bg `AccentBlueBrush`, fg `TextOnPrimaryBrush` |
| `Button.WindowClose` | **32×32**, border 0, radius 6, fg `TextMutedBrush`, ikon `IconSm` (14) | hover fg `TextPrimaryBrush` + bg `BgHoverBrush`; pressed `ButtonSecondaryPressedBrush` |
| `Button.PanelClose` | **32×32**, radius **5**, fg `TextMutedBrush`, ikon `IconSm` (14) | hover fg `ButtonDangerForegroundBrush` + bg `ButtonDangerHoverBrush`; pressed `ButtonDangerPressedBrush` |

> `AGENTS.md` mencatat: `Button.Primary` global punya template kustom (di `App.axaml`) yang `ContentPresenter`-nya tidak selalu mewarisi font family → hindari di dialog MantraData kecuali memang butuh aksen biru. Untuk dialog MantraData pakai `Button.Action` (light solid) / `Button.Muted` (dark card).

### 5.5 TextBox / ComboBox / AutoCompleteBox / NumericUpDown

`TextBox` default: `Bg=BgInputBrush`, `Border=BorderDefaultBrush`, `BorderThickness=1`, `CornerRadius=6`, `Padding=10,0`, `MinHeight=36`, `FontSize=13`, `VerticalContentAlignment=Center`, `SelectionBrush/CaretBrush=AccentColorBrush`; transition Border/Bg 0.15s.
- **Hover:** `BorderHoverBrush`, `Bg=BgInputHoverBrush`
- **Focus:** `BorderFocusBrush`, `Bg=BgInputBrush`
- **Disabled:** opacity 0.4

`ComboBox`: `Bg=BgInputBrush`, `Border=BorderDefaultBrush`, `Radius=6`, `Padding=10,0,8,0`, `MinHeight=36`, `FontSize=13`, `Cursor=Hand`; hover `BorderHoverBrush`; focus `BorderFocusBrush`.
`ComboBoxItem`: `Padding=10,6`, `Radius=6`, `Margin=2`; hover `BgHoverBrush`; selected `AccentSubtleBrush`.

`AutoCompleteBox`: `Bg=BgInputBrush`, `Border=BorderDefaultBrush`, `Radius=6`, `Padding=10,0`, `MinHeight=36`, `FontSize=13`. Di `App.axaml`: `MinimumPrefixLength=0`, `FilterMode=Contains`.
`NumericUpDown`: `Bg=BgInputBrush`, `Border=BorderDefaultBrush`, `Radius=6`, `MinHeight=36`, `Padding=8,0`, `FontSize=13`.

**Aturan Search Bar** (`TextBox.search`):
- `CornerRadius = RadiusMd (8)` (beda dari TextBox biasa yang 6)
- `Padding = 12,0,10,0`, **`MinHeight=36`, `Height=36`**
- `Background = BgInputBrush`, `Border = BorderDefaultBrush` 1px, `VerticalContentAlignment=Center`
- Hover → `BorderHoverBrush`; Focus → `BorderFocusBrush`
- Contoh pemakaian: `<TextBox Classes="search" Watermark="Cari Master..." .../>` (`DashboardView.axaml` baris 533 & 827).

### 5.6 CheckBox
`Box 16×16`, `CornerRadius=4`, `BorderThickness=1`, `BorderBrush=BorderDefaultBrush`, `Background=Transparent`; `FontSize=12.5`, `Foreground=TextSecondaryBrush`, `Padding=6,0,0,0`, `Cursor=Hand`. Transition Border/Bg 0.15s.
- **Hover:** border `BorderHoverBrush` + bg `BgHoverBrush`
- **Checked:** bg `AccentColorBrush` + border `AccentColorBrush`
- **Checked hover:** opacity 0.85
- **Disabled:** opacity 0.4

### 5.7 Toggle Switch (`ToggleButton.Switch`) — di `App.axaml`
- Track **44×24**, `CornerRadius=12`, border 1px (`SwitchOffBorderBrush`), bg off `SwitchOffBackgroundBrush`
- Thumb **18×18**, `CornerRadius=9`, bg `TextSecondaryBrush`, `Margin=3,0,0,0`, BoxShadow `0 2 4 0 #40000000`
- **On:** track bg/border `AccentColorBrush`; thumb `#FFFFFF`, `Margin=23,0,0,0`, BoxShadow `0 1 3 0 #60000000`
- **Hover:** off `SwitchHoverBackgroundBrush`; on opacity 0.85
- **Focus:** border `BorderFocusBrush`, `BorderThickness=2`
- **Pressed:** thumb `Width=22`; checked+pressed `Margin=19,0,0,0`
- **Disabled:** track opacity 0.5, thumb opacity 0.65
- Transition 0.25s (Margin pakai `SplineEasing 0.2,0,0,1`; Width 0.15s)

### 5.8 Icon (PathIcon) & Tabler
Style `PathIcon`: default **16×16** (`IconMd`), `Foreground=TextSecondaryBrush`, transition Foreground 0.15s. Template menggambar `Path` dengan `Fill=Transparent`, `Stroke={Foreground}`, **`StrokeThickness=1.8`**, `StrokeLineCap=Round`, `StrokeJoin=Round`, `Stretch=Uniform`.
Classes: `PathIcon.xs` (12), `.sm` (14), `.lg` (18), `.xl` (24).
Aturan: `Button PathIcon` mewarisi `Foreground` dari Button (via `RelativeSource AncestorType=Button`). `Button.WindowClose PathIcon` & `Button.PanelClose PathIcon` diset `IconSm` (14).

**Aturan ikon:** semua ikon UI memakai **outline Tabler** melalui `PathIcon` + `StreamGeometry` dari `Styles/TablerIcons.axaml` (**187 key `StreamGeometry`**, dari commit `74929e5`, lisensi MIT). Key legacy dipertahankan (mis. `IconLogIn`/`IconLogin`, `IconLogOut`/`IconLogout`) untuk kompatibilitas config script tersimpan. **Jangan pakai emoji/unicode sebagai glyph UI**, dan ukuran harus mengikuti token (bukan angka ad hoc). Kategori ikon di file: Essentials & Navigation, Files & Documents, Editing & Media, Development & Integrations, Communication & Status, Content & Miscellaneous. Contoh key: `IconHome2`, `IconSettings2`, `IconSearch2`, `IconCheck`, `IconAlertCircle`, `IconAlertTriangle`, `IconInfoCircle`, `IconTrash`, `IconPower`, `IconSkull`, `IconSave`, dll.

### 5.9 Card / Bento
- `Border.card` — bg `CardBackgroundBrush`, border `SeparatorBrush` 1px, `CornerRadius=RadiusLg (10)`, `Padding=16`, `ClipToBounds=True`.
- `Border.cardSm` — sama tetapi `CornerRadius=RadiusMd (8)`, `Padding=12`.
- `Border.Card` — seperti `card` tetapi `Padding=SpaceLg (16)`.
- **Tanpa shadow/glow** (kecuali tooltip/toast/dialog yang punya BoxShadow sendiri).

### 5.10 Separator / Divider
- `Separator` (DesignSystem): bg `SeparatorBrush`, `Height=1`, `Margin=0,12`.
  Di `App.axaml` ada style `Separator` yang menyetel `Margin=4,4` + template border — karena berada setelah include DesignSystem, nilai efektif margin separator menu = **4,4**.
- `Rectangle.divider`: `Height=1`, `Fill=SeparatorBrush`, `Margin=0,12`.
- `Border.navigationSeparator` (pemisah navigasi): bg `NavigationDividerBrush`, `Width=2`, `Height=20`, `Margin=8,0`, alignment Center; varian `.horizontal` → `Width=20`, `Height=2`, `Margin=0,8`.

### 5.11 Navigation / Tab (Trello style) — `TrelloNavigation.axaml`

**Palet Trello:** `TrelloBlue=#0079BF`, `TrelloDarkBlue=#026AA7`, `TrelloHeaderBg=#026AA7`, `TrelloBoardBg=#0079BF`; tombol header translucent: `TrelloBtnBg=#33FFFFFF` (20% white), `TrelloBtnHoverBg=#4DFFFFFF` (30%), `TrelloBtnActiveBg=#66FFFFFF` (40%), `TrelloBtnPressedBg=#80FFFFFF` (50%); `TrelloTabHoverBg=#14FFFFFF`, `TrelloTabActiveBg=#FFFFFF`.

**`RadioButton.TrelloBoardTab`** (tab utama, trello.com style):
- bg Transparent, `Foreground=TextSecondaryBrush`, border 0, **`CornerRadius=6`**, **`Padding=12,7`**, `Margin=2,0`, `FontSize=13`, `FontWeight=Medium`, `Cursor=Hand`.
- Template: `Border#PART_Root` + `Grid` berisi `ContentPresenter` + **`Border#PART_Indicator`**: `Width=20`, `Height=2`, `CornerRadius=1`, `Background=AccentBlueBrush`, `HorizontalAlignment=Center`, `VerticalAlignment=Bottom`, `Margin=0,0,0,1`, `Opacity=0`.
- **Hover:** bg `BgHoverBrush`, fg `TextPrimaryBrush` (tetap abu, bukan accent).
- **Checked:** bg `AccentSubtleBrush`, fg `TextPrimaryBrush`, `FontWeight=SemiBold`; indicator `Opacity=1`.
- **Checked hover:** bg `AccentSubtleHoverBrush`.
- **Focus:** border `BorderFocusBrush` 1px pada `PART_Root`; checked+focus juga menampilkan border focus.
- `RadioButton.TrelloBoardTab PathIcon` mewarisi Foreground dari RadioButton.

**`RadioButton.LogPanelTab` / `RadioButton.CompactTab`** (tab compact):
- Base: `Height=36`, `MinHeight=36`, `MinWidth=0`, `FontFamily=FontPrimary`, `FontSize=10`, `FontWeight=SemiBold`, stretch + center.
- `LogPanelTab`: `Padding=0,5`, **`FontSize=11`**, `CornerRadius=6`, border Transparent 1px. Checked → bg `AccentSubtleBrush` + border `BorderDefaultBrush` 1px; hover `BgHoverBrush`; checked hover `AccentSubtleHoverBrush`; focus `BorderFocusBrush`; pressed `ButtonSecondaryPressedBrush`; disabled opacity 0.45.
- `CompactTab`: `Padding=12,5`; `ContentPresenter` `TextWrapping=NoWrap`, `TextTrimming=CharacterEllipsis`.
- (Di `LogPanelSidebar.axaml` ada override lokal `RadioButton.LogPanelTab { Padding=2,6; MinWidth=0; ... }`.)

**`RadioButton.TrelloNavButton`** (header pill): bg `TrelloBtnBgBrush`, fg White, border 0, `CornerRadius=3`, `Padding=12,6`, `Margin=2,0`, `Height=32`, `FontSize=14`, `FontWeight=Medium`; hover `TrelloBtnHoverBrush`; **checked** bg `TrelloTabActiveBg` (#FFFFFF) + fg `TrelloBlueBrush` + SemiBold + BoxShadow `0 1 2 0 #1A000000`; pressed `TrelloBtnPressedBg` + `scale(0.97)`. Varian `.dark` untuk background gelap.

**`Button.TrelloHeaderButton`**: mirip nav button (CornerRadius 3, Padding 12,6, Height 32, Font 14 Medium); varian `.primary` solid white + fg TrelloBlue.

**`Border.TrelloPillContainer`** (segmented control wrapper): bg `#0F1A2E`, `CornerRadius=6`, `Padding=4`, border `#1E2D4A`; varian `.light` bg `#F1F2F4` + border `#DFE1E6`.

**Aturan nav/tab yang ditegakkan test (`NavigationNoIconsRegressionTests`):**
- Tab nav **text-only, tanpa ikon** (`PathIcon`/`Image`/dll) dan tanpa glyph unicode (`← → ☰ ▦ ✨`).
- Nav dashboard: label `HOME`, `BATCH`, `LOCKER` (`GroupName="NavTabs"`), pakai `Classes="TrelloBoardTab CompactTab"`.
- Tab Log Panel: label `EXPLORER`, `LOG`, `MASTER`, `DOC` (font 11), tiap tab punya `StackPanel Spacing="2"` + `TextBlock TextWrapping="NoWrap"`.
- Grid tab panel: `ColumnDefinitions="*,Auto,*,*,*"` (kolom ke-2 = separator `Auto`).
- Container nav header: `Border` bg `CardBackgroundBrush`, `BorderBrush=NavigationDividerBrush`, `BorderThickness=1`, `CornerRadius=8`, `Padding=4`, isi `StackPanel Spacing=2`.

### 5.12 Toast / Notifikasi

**In-app toast** (`Views/Controls/InAppToast.axaml` + `Services/ToastNotificationService.cs`):
- `Border.ToastContainer`: bg `CardBackgroundBrush`, border `SeparatorBrush` 1px, **`CornerRadius=RadiusMd (8)`**, `Padding=16,12`, `MinWidth=280`, `MaxWidth=420`, `BoxShadow=0 8 24 0 #60000000`.
- Varian (border 2px): `Success` → `AccentGreenBrush`; `Error` → `ButtonDangerBackgroundBrush`; `Warning` → `AccentOrangeBrush`; `Info` → `AccentBlueBrush`.
- Isi: `PathIcon` 18×18 (`IconCheck` / `IconAlertCircle` / `IconAlertTriangle` / `IconInfoCircle`) + `TextBlock` font 13 `TextPrimaryBrush`, wrap.
- **Penempatan** (`DashboardView.axaml`): `InAppToast x:Name="AppToast"`, `VerticalAlignment=Bottom`, `HorizontalAlignment=Right`, `Margin="0,0,20,20"`, `ZIndex=1001` (kanan-bawah, non-blocking).
- Perilaku service: singleton `ToastNotificationService.Instance`, antrian (queue) dengan durasi default **3000 ms** dan jeda **500 ms** antar toast. `ShowConfirmAsync` belum diimplementasi (mengembalikan `true`).
- Ada juga `NotificationService.cs` yang memicu notifikasi balon Windows via PowerShell `NotifyIcon.ShowBalloonTip` (Info/Warning/Error).

### 5.13 Tooltip
`ToolTip`: bg `#0A0A0A`, fg `#E4E4E7`, border `#232326` 1px, `CornerRadius=6`, `Padding=8,6`, `FontSize=11`, `FontFamily=FontPrimary`. Delay tooltip global: `ToolTip.ShowDelay=800` (`App.axaml`).

### 5.14 ContextMenu & MenuItem (override di `App.axaml`)
- `ContextMenu`: bg `AppBackgroundBrush`, border `SeparatorBrush` 1px, `CornerRadius=10`, `Padding=5`, BoxShadow `0 6 16 0 #80000000`.
- `MenuItem`: bg Transparent, fg `TextPrimaryBrush`, `Padding=12,7`, `FontSize=12`, `CornerRadius=6`, `Margin=0,1`; hover `MenuHoverBrush`; pressed `MenuSelectedBrush`; disabled opacity 0.4; chevron submenu `IconChevronRight` (14), check `IconCheck2` (14).
- `Button.ContextMenuAction`: bg `MenuHoverBrush`, fg `TextPrimaryBrush`, `CornerRadius=6`, `Padding=10,6`, `FontSize=11`, border `SeparatorBrush` 1px.

### 5.15 Hover / Active / Focus / Disabled — aturan umum
- **Hover:** selalu transisi `Background`/`BorderBrush` 0.12–0.15s; jangan scale/rotate kecuali button press (`scale(0.97)` pada Trello nav button).
- **Active/Checked:** pakai `AccentSubtleBrush` (#263B82F6) + indikator pendek, bukan full solid.
- **Focus:** border `BorderFocusBrush` (dark `#5B8DEF`, light `#2563EB`), jangan outline tebal.
- **Disabled:** `Opacity=0.4` (beberapa kontrol 0.45), bukan warna abu terpisah.
- Warna hover baku per komponen: pill/nav = `BgHoverBrush` (#25282D dark), secondary button = `ButtonSecondaryHoverBrush` (#27272B), ghost = `BgHoverBrush`.

### 5.16 Yang tidak boleh (dari DESIGN_SYSTEM.md)
- Hardcode `Background="#1A1A1D"` / `CornerRadius="7"` / `FontFamily="..."` di View — pakai token.
- Radius berbeda untuk komponen sejenis (button 6 vs 7 vs 10).
- Ukuran ikon berbeda tanpa alasan (12 vs 14 vs 18 random).
- Hover berbeda warna per View (harus konsisten: pill `#25282D`, secondary button `#27272B`, ghost `#10FFFFFF`/`BgHoverBrush`).
- Window baru **wajib** pakai `{DynamicResource AppBackgroundBrush}` (ikut Appearance Settings), jangan hardcode `#09090B`.

---

## Lampiran — Ringkasan Angka Kunci (verifikasi cepat)

| Item | Nilai | Sumber |
|------|-------|--------|
| Versi app | **8.3.0** | `BMachine.App.csproj` |
| Target framework | net8.0 | semua `.csproj` |
| Avalonia | 11.3.22 | `BMachine.App.csproj` |
| `RadiusSm/Md/Lg/Xl/Pill` | **6 / 8 / 10 / 12 / 999** | `BMachineDesignSystem.axaml` |
| `SpaceXs/Sm/Md/Lg/Xl` | **4 / 8 / 12 / 16 / 20** | `BMachineDesignSystem.axaml` |
| `IconXs/Sm/Md/Lg/Xl` | **12 / 14 / 16 / 18 / 24** | `BMachineDesignSystem.axaml` |
| Tinggi Button default | **36** | `BMachineDesignSystem.axaml` |
| Radius TextBox / Search | **6 / 8** | `BMachineDesignSystem.axaml` |
| PathIcon stroke | **1.8 px**, round | `BMachineDesignSystem.axaml` |
| `NavigationDividerBrush` | **dark #656A73 / light #737D8B** | `DarkTheme`/`LightTheme` |
| `SeparatorBrush` | **dark #26282C / light #E5E7EB** | `DarkTheme`/`LightTheme` |
| Indicator nav tab | **20×2**, radius 1, `AccentBlueBrush` | `TrelloNavigation.axaml` |
| Toast | radius **8**, 280–420 px, durasi **3000 ms** | `InAppToast.axaml` / `ToastNotificationService.cs` |
| Ikon Tabler | **187** `StreamGeometry` | `TablerIcons.axaml` |
| ViewModels / Views | **32 / 33** | `src/BMachine.UI` |
| Output publish | `mono\BMachine\BMachine.App.exe` | `build.ps1` |
