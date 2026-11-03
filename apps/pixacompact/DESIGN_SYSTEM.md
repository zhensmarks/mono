# PixaCompact Design System v1.0

Aturan design & layout terpusat untuk `apps/pixacompact` (Avalonia 11.3, net8.0, `PixelcutCompact.exe`).
Sumber nyata yang dibaca: [`App.axaml`](App.axaml), [`Resources/EditorTheme.axaml`](Resources/EditorTheme.axaml), [`Resources/EditorStyles.axaml`](Resources/EditorStyles.axaml), [`Resources/Strings.id.axaml`](Resources/Strings.id.axaml) / [`Strings.en.axaml`](Resources/Strings.en.axaml), [`Views/MainWindow.axaml`](Views/MainWindow.axaml), [`Views/PreviewWindow.axaml`](Views/PreviewWindow.axaml).

> **Prinsip:** token & class dulu, angka/hex lokal hanya kalau memang tidak ada tokennya. Aturan di dokumen ini berlaku untuk **kode baru**; sisa hex hardcoded lama tidak wajib dibersihkan (lihat §10).

---

## 1. Typography

**Tidak ada token font di PixaCompact.** `App.axaml` tidak menyetel `FontFamily` apa pun — semua teks memakai font bawaan `FluentTheme`. Jangan menambah `FontFamily` lokal di View (berbeda dengan BMachine yang mewajibkan Inter/JetBrains Mono).

**Skala ukuran yang benar-benar dipakai di kode** (hasil hitung seluruh `.axaml`, 257 pemakaian `FontSize` eksplisit):

| Ukuran | Jumlah | Dipakai untuk | Contoh |
|--------|--------|---------------|--------|
| `9` | 5 | mikro: label ✕ tutup tab, status text counter footer | `TextBlock.close-icon`, `StatusText` footer |
| `10` | 33 | label status huruf besar/kapital kecil, subjudul flyout, badge OUTPUT/INPUT, error text | `Text="Status" FontWeight="Bold"`, `OUTPUT` |
| `11` | 91 | caption/meta: ukuran file, hint editor, label options bar, tooltip, chip editor | `OriginalSizeDisplay`, `editor-hint`, `ToolTip` |
| `12` | 127 | **default**: body, button (global `Button FontSize=12`), label engine, `NumericUpDown` | style global `App.axaml` |
| `12.5` | 2 | judul item segmented tab (`tab-title`) | `TextBlock Classes="tab-title"` |
| `13` | 4 | nama file pada file-card, judul window preview | `FileName`, `TxtTitle` |
| `14` | 11 | angka counter proses, judul kecil | `ProcessedCount / FilesCount` |
| `16` | 4 | judul terbesar: nama file di preview pane, pesan dialog alert | preview header, alert |

- `15` muncul **1 kali** — anomali, jangan dipakai baru.
- Angka baru di luar tabel di atas **dilarang**.
- Hierarki aturan: `9/10` untuk label kecil (state, badge), `11` untuk meta/hint, `12` untuk body & tombol, `12.5` hanya judul tab, `13` untuk nama file/judul panel, `14` untuk angka penting, `16` hanya untuk judul satu-satunya di sebuah pane.
- Weight yang dipakai: `Normal`, `SemiBold`, `Bold`. `Bold` untuk nama file & judul tab aktif, `SemiBold` untuk label & judul kecil, `Normal` untuk isi.
- Teks panjang: `TextTrimming="CharacterEllipsis"` untuk nama file/ukuran (jangan wrap di baris), `TextWrapping="Wrap"` hanya untuk judul pane dan pesan kosong.

---

## 2. Warna

### Token global (`App.axaml` → `Application.Resources`)

| Token Color | Brush | Nilai | Pakai untuk |
|-------------|-------|-------|-------------|
| `AppBackground` | `AppBackgroundBrush` | `#121212` | latar aplikasi |
| `CardBackground` | `CardBackgroundBrush` | `#1E1E1E` | background window/chrome, kartu, pane |
| `SurfaceElevated` | `SurfaceElevatedBrush` | `#262626` | permukaan terangkat |
| `AccentBlue` | `AccentBlueBrush` | `#3b82f6` | aksen utama: tab aktif, progress, hasil |
| `TextPrimary` | `TextPrimaryBrush` | `#FFFFFF` | teks utama |
| `TextSecondary` | `TextSecondaryBrush` | `#B0B0B0` | teks sekunder/meta |
| `Divider` | `DividerBrush` | `#2A2A2A` | garis pemisah |
| — | `CardBorderBrush` | `#2A2A2A` (alias `Divider`) | border kartu/tab/footer (backward-compat; dipakai hampir semua view) |

Pemakaian di View: selalu `{DynamicResource ...Brush}` — jangan hardcode hex baru.

### Warna state (hardcoded di `MainWindow.axaml`, dipakai konsisten — jangan diganti)

| Warna | Arti |
|-------|------|
| `#3B82F6` | processing (blue) |
| `#10B981` | done / sukses (green) |
| `#EF4444` | failed / error (red) |
| `#6366F1` `#8B5CF6` `#F97316` `#15xxxxxx` | aksen per-engine (REMBG / REMBG Online / BG Eraser) |
| `#10FFFFFF` / `#15FFFFFF` / `#1AFFFFFF` | tint hover putih (card, tab, tombol ikon) |

### Token mode edit (`EditorTheme.axaml` — khusus `PreviewWindow`, `PreferencesWindow`)

| Token | Nilai | Pakai |
|-------|-------|-------|
| `EditorBg` | `#1E1E1E` | surround editor |
| `EditorSurface` / `EditorSurfaceAlt` | `#2B2B2B` / `#2F2F2F` | panel / options bar |
| `EditorBorder` | `#3A3A3A` | garis panel |
| `EditorTextPrimary` / `Secondary` / `Disabled` | `#E8E8E8` / `#A6A6A6` / `#6E6E6E` | teks |
| `EditorAccent` (+ `Hover`/`Pressed`/`Dark`) | `#31A8FF` (#4DB8FF / #1E8FDB / #1F3A52) | aksen editor (biru Photoshop, BUKAN `AccentBlue`) |
| `EditorDanger` / `EditorWarning` / `EditorSuccess` | `#EF4444` / `#F59E0B` / `#22C55E` | status |
| `EditorBtnBg` + state | `#333333` (hover `#404040`, pressed `#2A2A2A`, border `#4A4A4A`) | tombol editor |
| Metrik (x:Double) | `EditorRailWidth 56`, `EditorToolSize 36`, `EditorOptionsBarHeight 32`, `EditorStatusBarHeight 30`, `EditorMenuBarHeight 34` | ukuran chrome editor — pakai `{StaticResource ...}` |

Di area editor **aksennya `EditorAccent` (#31A8FF)**, di luar area editor `AccentBlueBrush` (#3b82f6). Jangan mencampurnya.

---

## 3. Radius & Spacing

**Radius yang dipakai** (hitung nyata di seluruh `.axaml`):

| Nilai | Jumlah | Pakai untuk |
|-------|--------|-------------|
| `3` | 37 | tombol & kontrol editor (`editor-tool`, `editor-primary`, `editor-compact`, `editor-danger`, chip), tab editor |
| `4` | 31 | swatch warna, tooltip, tombol ikon kecil (close preview) |
| `6` | 23 | button global, `file-card`, `engine-card`, segmented container, gambar preview |
| `8` | 11 | flyout `ModeFlyout`, kartu kecil |
| `12` | 5 | window `PreviewWindow`, dialog alert & log |
| `16` | 1 | kartu empty-state (satu-satunya; jangan tambah radius > 12 untuk yang lain) |

Anomali yang sudah ada, **jangan ditiru**: `1.5` (bar aksen engine card — memang 3/2), `5` (3×), `9` (lingkaran tutup tab, setengah dari 18), `32` (lingkaran ikon empty-state, setengah dari 64), `0` (chrome window).

Aturan: komponen sejenis wajib radius sama — tombol global `6`, tombol editor `3`, kartu `6`, modal `12`.

**Spacing** (dari angka yang dipakai, bukan token):

- Padding kartu: `12,10` (`file-card`, `engine-card`), `10,8` (isi file-card di ItemTemplate), `15,10` (footer window & preview pane), `12` (flyout).
- Jarak antar kartu: `Margin 0,0,0,6` (`file-card`, `engine-card`), `0,0,0,4` (baris file).
- Margin luar area: `40,50` (kartu empty-state), `0,10` (ListBox file).
- Spacing StackPanel: `6` (daftar opsi), `8`/`12` (baris kontrol), `15` (gambar preview), `24` (konten kosong).
- Kontrol tinggi global: `32` (Button/ComboBox/NumericUpDown `MinHeight`), `40` (segmented tab `MinHeight`), `45` (header tombol window preview), `56` (title bar MainWindow), `55` (`PanelFooter MinHeight`).

---

## 4. Button & state classes

### Global (`App.axaml` → `Application.Styles`)

Semua `Button`: `CornerRadius=6`, `FontSize=12`, konten center H+V, tooltip `Placement=Bottom`, `VerticalOffset=8`.
`Button.icon`: kotak `32×32`, `Padding=0` — untuk tombol ikon.
`ComboBox` / `TextBox` / `NumericUpDown`: `CornerRadius=6`, `MinHeight=32` (Numeric juga `MinWidth=100`, spinner `22`).

### Class editor (`EditorStyles.axaml`, radius `3`, transition `0.12s`)

| Class | Peran | Catatan |
|-------|-------|---------|
| `editor-tool` | tombol tool | border `EditorBtnBorder`, hover border `EditorAccent` |
| `editor-primary` | aksi utama | bg `EditorAccent` |
| `editor-compact` | tombol/toggle padat di options bar | `MinHeight=26`, padding `8,4` |
| `editor-danger` | destruktif (Buang perubahan) | bg `#3A1D1D`, border `#7F2D2D` → hover `#EF4444` |
| `editor-dock-tabs` / `editor-dock-tab` | tab dock kanan | selected bg `EditorAccentDark` |
| `editor-menubar` / `editor-menuitem` | menu mode edit | font 12, padding `10,6` |
| `editor-dock-selector` | ComboBox dock | — |
| `shimmer` | animasi kilau `Border.shimmer` | opacity 0.1↔0.5, 1.5s |
| `spinning` | origin rotasi spinner | rotasi dijalankan dari code (`DispatcherTimer`), bukan style |
| `compare-float` | tombol Bandingkan melayang | **jangan dibuat transparan** saat hover/pressed |

Semua state editor (`:pointerover`/`:pressed`/`:disabled`/`:focus`) memakai token `Editor*`. Focus border `#C6DDFF` (atau `#FCA5A5` untuk danger). Disabled = `Opacity 0.55`.

### State class `file-card` (`MainWindow.axaml`)

`Border Classes="file-card"` + state via binding:

| State | Background | BorderBrush | Hover |
|-------|-----------|-------------|-------|
| (default) | `#0DFFFFFF` | `CardBorderBrush` | bg `#15FFFFFF`, border `#40FFFFFF` |
| `.processing` | `#103B82F6` | `#3B82F6` | bg `#1A3B82F6`, border `#603B82F6` + **pulse animation 1.8s** pada BorderBrush |
| `.done` | `#1010B981` | `#2010B981` | bg `#1A10B981`, border `#10B981` |
| `.failed` | `#10EF4444` | `#50EF4444` | bg `#1AEF4444`, border `#EF4444` |

Pemakaian:

```xml
<Border Classes="file-card"
        Classes.processing="{Binding IsProcessing}"
        Classes.done="{Binding IsDone}"
        Classes.failed="{Binding IsFailed}">
```

**Hover/transition (aturan wajib):**
- Hover kartu/tombol/tab: `BrushTransition` **`0:0:0.15`** pada `Background` + `BorderBrush` (editor `0:0:0.12` — dua nilai ini saja).
- **DILARANG animasi scale/rotate saat hover pada list item, kartu, atau tab.** Yang ada di kode sekarang pun hanya `ScaleTransform ScaleX=1 ScaleY=1` (identitas, untuk menghindari layout shift) — jangan diubah jadi scale ≠ 1.
- Animasi pulse hanya untuk state `.processing` (dan spinner), jangan ditambah efek baru ke `.done`/`.failed`.

---

## 5. Aturan layout MainWindow

Struktur window (`Grid RowDefinitions="Auto, *, Auto"`, border `CardBackgroundBrush`, chrome custom NoChrome):

1. **Title bar `Height="56"`** — drag area full width, kiri: `Button.title-model-selector` (judul engine, font 14 Bold, transparan — tidak boleh menampilkan hover tombol biasa) + flyout engine `FlyoutPresenterClasses="ModeFlyout"`; kanan: tombol `Classes="icon"` gallery & close (close hover `#FF4444`).
2. **Row konten** — `Border` atas `BorderThickness="0,1,0,0"` `CardBorderBrush`:
   - **Segmented tabs**: container `CornerRadius="6"` border `CardBorderBrush`, isi `ListBox` `UniformGrid Rows="1"`; `ListBoxItem MinHeight="40"`, hover `#0AFFFFFF`, **selected `AccentBlueBrush`** + teks putih Bold; pembatas antar tab `Border.separator` (`0,0,1,0`); judul `tab-title` font `12.5`, `MaxLines="2"` rata tengah; tombol tambah tab lebar `40`.
   - **Grid kolom `* , Auto , Auto(MaxWidth=500)`**: daftar file → splitter (`Width=4`) → **preview pane `Width="300"`** dengan `BorderThickness="1,0,0,0"` `CardBorderBrush`. Keduanya hanya tampil saat `IsPreviewPaneVisible`.
   - **Kartu daftar**: `Border.file-card` (radius 6), isi: checkbox · nama file 13 Bold + status 10 Bold + `ProgressBar Height="4"` indeterminate · kanan meta 11 + PathIcon. Empty state: `Border.dashboard-card` `CornerRadius="16"` `MaxWidth="500"`.
   - **Scrollbar**: hanya `ListBox` daftar file yang `VerticalScrollBarVisibility="Visible"`; ScrollViewer lain `Hidden`/`Disabled`.
3. **Footer `Padding="15,10"`** border atas `0,1,0,0` — kiri: tombol ikon log/settings + dot VPN + kartu counter (`CornerRadius="6"`, ring progres + angka 14 + status 9); kanan: tombol aksi rata kanan (`StackPanel HorizontalAlignment="Right"`), tombol primer `AccentBlueBrush`.

Aturan:
- Jangan mengubah lebar preview pane (300) atau tinggi title bar (56) secara lokal.
- Overlay (alert `CornerRadius="12"`, log `CornerRadius="12"`, ZIndex 110/300) selalu memakai `CardBackgroundBrush` + `CardBorderBrush`, dimmer `#AA000000`.
- `GridSplitter` hanya untuk daftar↔preview; jangan menambah splitter baru.

---

## 6. Aturan editor PreviewWindow

`PreviewWindow.axaml`: `ResourceInclude EditorTheme.axaml` (resources) + `StyleInclude EditorStyles.axaml` (styles) — dua-duanya wajib, chrome window `CornerRadius="12"`.

**Hierarki panel tetap** (`Grid RowDefinitions="Auto, Auto, Auto, Auto, *, Auto"`):

| Row | Panel | Ketinggian |
|-----|-------|------------|
| 0 | Header draggable (`MinHeight=45`) + `ChipEditMode` + `BtnExitEditHeader` + tombol tutup | auto |
| 1 | `PanelMenuBar` (`EditorMenuBarHeight` = 34) | 34 |
| 2 | slot docking atas (saat ini kosong — komentar `"Parked here until the editor applies the saved dock position."`) | auto |
| 3 | `PanelOptionsBar` (`EditorOptionsBarHeight` = 32) | 32 |
| 4 | `EditorWorkspaceGrid` (`RowDefinitions="Auto,*,Auto,Auto"` × `Auto,*,Auto,Auto`): `PanelToolRail` (kiri, `EditorRailWidth` 56, tool `EditorToolSize` 36) · `PanelNormalPreview` (`*, 2, *` → `PanelOriginalColumn` / `SplitterPreview` / kolom hasil + `EditOverlay` + `PanelRefineBusy`) · `RightDockSplitter` · `PanelRightEditor` (lebar 240: `PanelProperties`, `PanelHistory`, `PanelDocLayers`) · `PanelEditorStatus` (bawah, `EditorStatusBarHeight` 30) | isi |
| 5 | `PanelFooter` (`MinHeight=55`, `Padding="15,10"`, berisi `PanelNavigation`) | auto |

**Aturan visibility — wajib lewat `SetVisible`:**

```csharp
// PreviewWindow.EditorSession.cs
private void SetVisible(string name, bool visible)
{
    var c = this.FindControl<Control>(name);
    if (c != null) c.IsVisible = visible;
}
```

- Semua peralihan mode (masuk/keluar mode edit, tampil/grup options per tool, busy refine, compare) memanggil `SetVisible("PanelXxx", bool)` dari code-behind — **panel yang disorot wajib punya `x:Name`**.
- **Hierarki panel tetap**: mode edit tidak menambah/menghapus anak panel, hanya mengubah `IsVisible`. `PanelMenuBar`/`PanelOptionsBar`/`PanelToolRail`/`PanelRightEditor`/`PanelEditorStatus` default `IsVisible="False"` dan di-show saat sesi editor.
- Grup opsi options bar diatur per tool: `OptBrushGroup`, `OptWandGroup`, `OptPenGroup`, `OptSelectionGroup`, `OptSelectionModeGroup`, `OptRefineEdgeGroup` (`PreviewWindow.EditorOptions.cs`).
- Konten teks editor memakai `{DynamicResource ...}` dari `Strings.*` — jangan mengganti `Header`/`Content` dari code dengan literal.
- Kontrol editor wajib memakai class `editor-*` (§4), metrik dari `EditorTheme` (`{StaticResource EditorMenuBarHeight}`, dll).

---

## 7. Aturan lokalikasi

- Dua file, **wajib berdua dengan daftar key identik**: [`Resources/Strings.id.axaml`](Resources/Strings.id.axaml) + [`Resources/Strings.en.axaml`](Resources/Strings.en.axaml) — masing-masing 332 key (`Menu_*`, `Btn_*`, `Tip_*`, `Toast_*`, `Hint_*`, `Err_*`, ...).
- `App.axaml` hanya me-merge `Strings.id.axaml` sebagai default. Ganti bahasa: `ApplyEditorLanguage(lang)` di `PreviewWindow.EditorTools.cs` menukar `ResourceInclude` `Strings.*` di `Application.Resources` → simpan `Settings.EditorLanguage` → teks `DynamicResource` ikut ter-update **tanpa restart**.
- Karena itu: teks UI yang bisa berubah bahasa **wajib `DynamicResource`** di axaml (bukan `StaticResource`, bukan literal).
- Key baru: tambahkan ke **kedua file dalam satu commit**. Key hilang di satu bahasa = teks kosong di UI itu.
- Placeholder `{0}`/`{1}` diisi via `string.Format` — format string id/en harus pakai placeholder yang sama.
- Yang sudah ada tapi jangan ditiru: sebagian teks masih literal di axaml (mis. `"Pilih file untuk preview"`, header `"Pilih engine pemrosesan"`) dan `ToolTip.Tip` hardcode. Untuk kode baru, pakai key `Strings.*`.

---

## 8. Icon

- Icon UI memakai `PathIcon` + `StreamGeometry`. Key global di `App.axaml`: `IconSettings`, `IconEraser`, `IconProcess`, `IconDelete`, `IconPlay`, `IconPause`. Key window di `MainWindow.Resources`: `IconCheck`, `IconRetry`, `IconPause`, `IconPlay`, `IconSettings`, `IconProcess`, `IconChevronDown`, `IconLightning`, `IconMonitor`, `IconCloud` — plus geometry inline untuk ikon kecil (grid/list, X, plus, mata, panah).
- Ukuran ikon yang dipakai: `10` (centang/panah status), `12` (ikon tab/tool kecil), `14` (ikon toolbar/footer), `16` (ikon judul/jendela), `32` (ikon empty state/alert). Jangan memakai ukuran lain untuk ikon baru.
- Icon = kotak persegi (`Width` = `Height`) + ikut `Foreground` induknya.
- **Emoji/unicode glyph sebagai icon UI dilarang untuk kode baru.** Yang sudah ada (`TextBlock Text="✕"` pada tutup tab, `Content="✕"` tombol tutup preview, `Content="⟲"` reset warna) adalah pola lama — jangan ditambah, ganti dengan `PathIcon` bila file-nya disentuh.
- Icon tanpa label wajib `ToolTip.Tip`.

---

## 9. Yang tidak boleh

- **Hex baru di View** untuk warna yang sudah ada token (`CardBackgroundBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `AccentBlueBrush`, `CardBorderBrush`, `DividerBrush`; di editor `Editor*`). Pakai `{DynamicResource ...}`.
- **`FontSize` di luar skala §1** (terutama `15` dan nilai lain yang belum pernah dipakai).
- **Radius komponen sejenis berbeda-beda** (tombol global 6, tombol editor 3 — jangan campur; kartu 6, modal 12).
- **Animasi scale/rotate hover** pada list item, kartu, tab. Transition hanya `BrushTransition` 0.15s (global) / 0.12s (editor).
- **Emoji sebagai icon**; pakai `PathIcon`.
- **Literal teks UI baru** di axaml; tambah key ke `Strings.id` + `Strings.en`.
- **Menambah/menghapus anak panel** di PreviewWindow untuk ganti mode; hanya `SetVisible(..., bool)`.
- **Menyalin `AccentBlue` (#3b82f6) ke area editor** atau sebaliknya — area editor pakai `EditorAccent` (#31A8FF).
- **Mengubah lebar tetap layout**: title bar 56, preview pane 300, right dock 240, tinggi chrome editor dari `EditorTheme`.
- **Scrollbar di ScrollViewer selain daftar file** (§5).

---

## 10. Status penerapan (jujur)

- **Token utama ada**: 7 Color + 7 Brush (+ alias `CardBorderBrush`) di `App.axaml`, plus 30+ brush/metrik `Editor*` di `EditorTheme.axaml`. Icon `StreamGeometry` global ada 6, window-level banyak.
- **Gaya tersebar**: style global ringan di `Application.Styles` (`App.axaml`); sebagian besar gaya kartu/tab/footer di `Window.Styles` `MainWindow.axaml`; gaya editor di `Resources/EditorStyles.axaml` (di-`StyleInclude` oleh `PreviewWindow`). Belum ada satu file design system terpusat untuk view dashboard — dokumen ini adalah referensi aturannya.
- **Banyak hex hardcoded lama yang dibiarkan**: hover tint `#0AFFFFFF`/`#15FFFFFF`, warna state `#3B82F6`/`#10B981`/`#EF4444`, warna engine, tombol dialog `#4B5563`, `Foreground="White"` pada tombol aksi. Angka-angka ini **dipakai konsisten dan dianggap pola sah** — jangan di-refactor massal, tapi **kode baru harus memakai token** (§2) atau nilai yang persis sama.
- **Radius `3` (37×) dominan di editor** meski aturan global menyebut 6 — itu memang dua sistem berbeda (dashboard vs chrome editor); jangan diseragamkan.
- **Sisa kecil**: `15` (1×), `5` (3×), `9`/`32`/`1.5` (geometri khusus), literal teks UI di axaml, icon unicode lama. Dibiarkan; aturan berlaku untuk kode baru.
- Dokumen ini ditulis dari pembacaan langsung file axaml (bukan asumsi); bila token/style berubah, perbarui tabel di sini.