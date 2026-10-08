# Audit Kepatuhan Kode UI BMachine terhadap Design System Sista

**Tanggal audit:** 2025
**Lingkup:** `apps/bmachine/src/BMachine.UI/Views/**` dan `Controls/**` (52 file `.axaml`)
**Sumber aturan (ground truth):**
- `Styles/BMachineDesignSystem.axaml` — token resmi (`RadiusSm/Md/Lg/Xl`, `SpaceXs..Xl`, tinggi Button 36, `IconXs..Xl`)
- `Themes/DarkTheme.axaml` & `Themes/LightTheme.axaml` — nama brush resmi (`SeparatorBrush`, `NavigationDividerBrush`, `BorderSubtleBrush`, `BorderDefaultBrush`, `BorderFocusBrush`, `AccentBlueBrush`, `CardBackgroundBrush`, `AppBackgroundBrush`, dst.)
- `Styles/TrelloNavigation.axaml` — class nav resmi (`TrelloBoardTab`, `CompactTab`, `LogPanelTab`, `PART_Indicator`)
- `Styles/TablerIcons.axaml` — katalog `PathIcon` resmi

---

## 1. Ringkasan Status Kepatuhan

| Kategori Aturan | Status | Bukti Kuantitatif |
|---|---|---|
| 1. Hardcoded warna di View/Controls | ❌ **Tidak patuh** | 204 kemunculan hex di 31 file |
| 2. Hardcoded `CornerRadius` angka | ❌ **Tidak patuh** | 412 kemunculan di 45 file; hanya **2** yang pakai token `{StaticResource Radius…}` |
| 3. `Width/Height` magic number | ⚠️ **Sebagian** | 1.420 atribut numerik; 227 dari 287 `PathIcon` hardcode ukuran (hanya 3 pakai `Classes=`) |
| 4. Unicode glyph pengganti `PathIcon` | ✅ **Mayoritas patuh** | Ikon UI sudah pakai `PathIcon`/`StaticResource Icon…`; sisa glyph tipografis minor |
| 5. Garis separator & brush tidak konsisten | ❌ **Tidak patuh** | `NavigationDividerBrush` dipakai sebagai border pill 1px & border panel; `SeparatorBrush` vs `BorderSubtleBrush` tumpang tindih |
| 6. Nav/tab button tidak konsisten | ❌ **Tidak patuh** | 6 sistem tab paralel (`Segmented`, `SegTab`, `Tab`) di luar `TrelloBoardTab/CompactTab` |
| 7. Search bar/TextBox tanpa border token | ❌ **Tidak patuh** | Hanya 2 tempat pakai `Classes="search"`; banyak search box `BorderThickness="0"` / tinggi di-`override` |

**Kesimpulan:** Design system **belum ditegakkan**. File aturan sudah lengkap, tetapi View masih banyak menulis warna, radius, ukuran, dan varian kontrol secara manual. Akibat paling terasa: (a) **tema terang tidak jalan** karena warna gelap di-hardcode, (b) **permukaan panel hilang** karena token `SurfaceElevatedBrush`/`PrimaryBrush` tidak pernah didefinisikan, (c) **tampilan nav/tab antar layar berbeda-beda**.

---

## 2. Temuan Berprioritas

### 🔴 KATEGORI 1 — Hardcoded Warna (`#RRGGBB`)

#### T1.1 [TINGGI] Token brush yang dipakai tapi TIDAK PERNAH didefinisikan → panel kehilangan background
`SurfaceElevatedBrush` dipakai **11 kali** untuk background panel detail, tetapi tidak ada di `DarkTheme.axaml`, `LightTheme.axaml`, `App.axaml`, maupun `ThemeService.cs`. `PrimaryBrush` juga dipakai 1 kali dan tidak terdefinisi. `DynamicResource` yang tidak resolve membuat `Background` kosong/transparan.

Bukti:
- `Views/EditingCardListView.axaml:211,414,465,701,819` — `Background="{DynamicResource SurfaceElevatedBrush}"`
- `Views/LateCardListView.axaml:196,399,450,686,804` — idem
- `Views/Controls/LoadingOverlay.axaml:6` — `Background="{DynamicResource SurfaceElevatedBrush}"`
- `Views/LogPanelSidebar.axaml:259` — `Foreground="{DynamicResource PrimaryBrush}"` (ProgressBar)

```xml
<!-- EditingCardListView.axaml:211 -->
<Border x:Name="Part_DetailPanel" MinWidth="400" MaxWidth="500" Background="{DynamicResource SurfaceElevatedBrush}" ...>
```

**Perbaikan:** tambahkan token resmi, mis. di `DarkTheme.axaml` `SurfaceElevatedBrush` = `#202226` (setara `BackgroundBrush`) dan `LightTheme.axaml` = `#F1F3F5`; untuk `PrimaryBrush` ganti dengan token yang ada (`AccentColorBrush`) atau definisikan alias. **Jangan** biarkan nama token "hantu".

#### T1.2 [TINGGI] Warna semantik status (error/sukses/online) di-hardcode, tidak ikut tema
Warna merah/hijau status ditulis mentah, sehingga tetap gelap saat mode terang dan menyimpang dari token `AccentRedBrush`/`AccentGreenBrush`/`LogErrorBrush`/`LogSuccessBrush`.

Bukti:
- `Views/UnifiedTrelloView.axaml:46` `Fill="#EF4444"` (offline) dan `:49` `Fill="#22C55E"` (online)
- `Views/EditingCardListView.axaml:390,391,521,612,743` `#EF4444`; `:629` `#22C55E`
- `Views/LateCardListView.axaml:375,376,506,597,728` `#EF4444`; `:614` `#22C55E`
- `Views/CommentWindow.axaml:62,159` `Foreground="#ef4444"`; `:176` `#22C55E`

```xml
<!-- UnifiedTrelloView.axaml:45-49 -->
<Style Selector="Ellipse">          <Setter Property="Fill" Value="#EF4444"/> </Style>
<Style Selector="Ellipse.online">   <Setter Property="Fill" Value="#22C55E"/> </Style>
```

**Perbaikan:** pakai `{DynamicResource AccentRedBrush}` / `{DynamicResource AccentGreenBrush}` (sudah ada di kedua tema) atau `LogErrorBrush`/`LogSuccessBrush`. Status online/offline sebaiknya lewat token semantic agar tema terang benar.

#### T1.3 [TINGGI] Warna "danger/delete" `#FF453A` di-hardcode
- `Views/DashboardView.axaml:350,377,621,675` `Foreground="#FF453A"` (menu Delete)
- `Views/OutputExplorerView.axaml:357,491,747,822` `#FF453A`
- `Views/DashboardView.axaml:639` `Foreground="Red" Background="#1AFF0000" BorderBrush="Red"` (named color + hex)

**Perbaikan:** `{DynamicResource ButtonDangerForegroundBrush}` (dark `#FF9FA5` / light `#991B1B`) untuk teks, atau tombol `Classes="danger"`. Hindari nama warna bawaan (`Red`) sama sekali.

#### T1.4 [SEDANG] Biru aksen lama `#0078D7` bertabrakan dengan `AccentBlue #3b82f6`
- `Views/EditingCardListView.axaml:159,161,373,374` `#260078D7` / `#0078D7`
- `Views/LateCardListView.axaml:358,359`; `Views/UnifiedTrelloView.axaml:232,233,245` `#180078D7/#400078D7/#280078D7/#0078D7`

**Perbaikan:** ganti seluruhnya ke `{DynamicResource AccentBlueBrush}` / `AccentSubtleBrush` (`#263B82F6`). Warna `#0078D7` adalah sisa palet lama.

#### T1.5 [SEDANG] Aksen & warna domain di-hardcode
- Amber folder: `Views/OutputExplorerView.axaml:229,368,496,574,667,752,827` `#E8A447`
- Gradien tombol Advanced: `Views/ToolboxWindow.axaml:382-399` `#3B82F6/#6366F1/#4F8FF7/…`
- Splash: `Views/SplashWindow.axaml:19,23,24,29,42-47,57,102` (`#111111`, `#18181B`, `#09090B`, `#3B82F6`, `#60A5FA`)
- Lightbox: `Views/ImageLightboxWindow.axaml:12` `#1A1A1A`
- Tombol master: `Views/DashboardView.axaml:914` `Background="#2A2A2A"`
- Tombol locker: `Views/FolderLockerView.axaml:162,167` `#1AFF0000/#FF4444/#1A00FF00/#00FF00`

**Perbaikan:** tambahkan token domain (mis. `FolderAccentBrush`) atau pakai `AccentOrangeBrush`/`AccentBlueBrush`. Untuk brand splash boleh dikecualikan tetapi tetap lebih baik lewat resource.

#### T1.6 [SEDANG] Scrim/overlay & `BoxShadow` pakai hex mentah
- Overlay: `#40000000`/`#80000000`/`#99000000`/`#D91E1E1E` tersebar (mis. `Views/OutputExplorerView.axaml:900,923,929,963,969,1002,1020`; `Views/DashboardView.axaml:189,217,222,1110,1138`; `Views/FolderLockerView.axaml:187`)
- 22 `BoxShadow` dengan warna hex (`Views/DashboardView.axaml:189` `#40000000`, dst.)

**Perbaikan:** definisikan token `OverlayScrimBrush` (`#80000000`) & `ShadowBrush`, lalu ganti semua. `BoxShadow` minimal pakai token warna agar konsisten tema.

---

### 🔴 KATEGORI 2 — Hardcoded `CornerRadius` Angka

#### T2.1 [TINGGI] Hampir semua radius ditulis manual, token `Radius*` nyaris tidak dipakai
Token resmi: `RadiusSm=6`, `RadiusMd=8`, `RadiusLg=10`, `RadiusXl=12`. Faktanya hanya **2** pemakaian `{StaticResource Radius…}` di seluruh Views/Controls, sedangkan **412** penulisan angka.

Distribusi nilai (`grep`):
```
218 × CornerRadius="6"   → RadiusSm
 65 × CornerRadius="8"   → RadiusMd
 30 × CornerRadius="4"   → DI LUAR SKALA
 25 × CornerRadius="10"  → RadiusLg
 21 × CornerRadius="12"  → RadiusXl
 10 × CornerRadius="5"   → DI LUAR SKALA
  5 × CornerRadius="2"   → DI LUAR SKALA
  4 × CornerRadius="20"  → DI LUAR SKALA
  3 × CornerRadius="7"   → DI LUAR SKALA
  2 × CornerRadius="9", 2 × "28", 2 × "22", 2 × "44", 2 × "100", 1 × "3", 1 × "24", 1 × "150", 1 × "1"
```

File terbanyak: `SettingsView.axaml` (38), `EditingCardListView.axaml` (36), `LateCardListView.axaml` (35), `CardDetailPanelHost.axaml` (33), `DashboardView.axaml` (29), `DocPanelView.axaml` (28).

**Perbaikan:** ganti bertahap sesuai peta: `6→{StaticResource RadiusSm}`, `8→{StaticResource RadiusMd}`, `10→{StaticResource RadiusLg}`, `12→{StaticResource RadiusXl}`. Nilai 4/5/7/9/18/20 dipetakan ke skala terdekat (4,5→`RadiusSm`; 7,9→`RadiusMd`; 18,20→`RadiusXl`) agar skala seragam. Untuk pill (999) sudah ada `RadiusPill`.

---

### 🟠 KATEGORI 3 — `Width/Height` Magic & Ukuran Ikon

#### T3.1 [TINGGI] `PathIcon` hardcode ukuran, mengabaikan token `IconXs..Xl`
227 dari 287 `PathIcon` menulis `Width/Height` literal; hanya 3 memakai `Classes="xs/sm/lg/xl"`. Nilai dominan `Width="14" Height="14"` (188×) dan `16` (35×) tepat sama dengan token `IconSm=14` / `IconMd=16`.

Bukti contoh:
- `Views/DashboardView.axaml:435` `<PathIcon Data="{StaticResource IconFolder}" Width="14" Height="14" .../>`
- `Views/PathSettingsView.axaml:52` `Width="16" Height="16"`
- `Views/SpreadsheetView.axaml:365` `Width="14" Height="14"`
- `Views/OutputExplorerView.axaml:915,932,972` `Width="14/24" Height="14/24"`

**Perbaikan:** hapus `Width/Height` literal dan pakai `Classes="sm"` (14) / `Classes="lg"` (18) / `Classes="xl"` (24); atau `{StaticResource IconSm}`. `PathIcon` default sudah 16 (`IconMd`).

#### T3.2 [SEDANG] Tinggi `TextBox` di-`override` keluar dari token 36
Konsistensi input 36px dirusak oleh override ad-hoc:
- `Views/DashboardView.axaml:358,648` `Height="22" MinHeight="0"`
- `Views/MantraDataView.axaml:255` `Height="24"` (search)
- `Views/Dialogs/MantraData/AiSettingsWindow.axaml:167,170,174` `Height="32"`
- `Views/Dialogs/MantraData/FindReplaceWindow.axaml:106,110` `Height="34"`; `SplitColumnWindow.axaml:111,115,119` `Height="34"`; `CustomMergeWindow.axaml:142` `Height="34"`

**Perbaikan:** hapus `Height` literal → ikuti `MinHeight=36` dari style `TextBox`; untuk field compact gunakan class varian, bukan angka bebas. `Height=36` boleh tetap (sesuai token) tetapi lebih baik tanpa override.

#### T3.3 [SEDANG] Ukuran window & panel fixed yang seharusnya adaptive/token
- `Views/ToolboxWindow.axaml:11` `Width="1024" Height="730"`; `:618,676` `Width="150" Height="200"`
- `Views/ImageLightboxWindow.axaml:8` `Width="800" Height="600"`
- Panel detail `MinWidth="400" MaxWidth="500"` diulang di `EditingCardListView`/`LateCardListView`/`CardDetailPanelHost`
- `Views/CardDetailPanelHost.axaml:528,543` & `UnifiedTrelloView.axaml:447,462` `MaxDropDownHeight="160"`

**Perbaikan:** pindahkan `150×200`, `400/500` ke `Styles.Resources` sebagai token (`ThumbW/ThumbH`, `PanelWidth`) atau `GridLength`; `MinWidth/MaxWidth` panel cukup didefinisikan sekali di style `Border.sidePanel`.

---

### 🟢 KATEGORI 4 — Unicode Glyph (bukan `PathIcon`)

**Status: mayoritas sudah patuh.** Ikon fungsional sudah pakai `PathIcon` + `{StaticResource Icon…}` (287 pemakaian). **Tidak ditemukan** `▾ ✕ ✓ ▲ ▼ ◀ ▶ × ☰ ⋮` sebagai teks tombol. Temuan sisa bersifat tipografis/legitim:

#### T4.1 [RENDAH] Glyph tipografis dalam teks UI
- `Views/MantraDataView.axaml:367,368` — `Header="L/P → Jenis Kelamin"`, `Header="dd-MM-yyyy → Tanggal Indonesia"` (panah `→` sebagai teks menu)
- `Views/OutputExplorerView.axaml:957,997` — `Text="↵ to create  ·  esc to cancel"` (`↵` dan `·`)
- `Views/DashboardView.axaml:915` — `·` pada hint
- `Views/FolderLockerView.axaml:37,38` — `PasswordChar="•"` → **ini BUKAN pelanggaran** (memang properti `PasswordChar`)

**Perbaikan (opsional):** untuk `→` pada menu transform, ganti dengan `PathIcon` `IconArrowRight` kecil atau biarkan (teks naratif masih wajar). `↵`/`·` pada hint keyboard dapat diganti `PathIcon IconCornerDownLeft` + separator. **Jangan** jadikan prioritas.

---

### 🔴 KATEGORI 5 — Garis Separator / Brush Tidak Konsisten

#### T5.1 [TINGGI] Border pill nav memakai `NavigationDividerBrush` di satu tempat, `SeparatorBrush` di tempat lain
Pola pill nav yang sama dipakai di 3 layar dengan brush border **berbeda**:
- `Views/DashboardView.axaml:173` → `BorderBrush="{DynamicResource NavigationDividerBrush}" BorderThickness="1"`
- `Views/LogPanelSidebar.axaml:168` → `BorderBrush="{DynamicResource NavigationDividerBrush}" BorderThickness="1"`
- `Views/ToolboxWindow.axaml:1136` → `BorderBrush="{DynamicResource SeparatorBrush}" BorderThickness="1"`

`NavigationDividerBrush` sengaja lebih terang (`#656A73` dark / `#737D8B` light) untuk **garis pemisah**, bukan border pill — sehingga pill di Dashboard/LogPanel terlihat lebih "tebal/nyala" daripada di Toolbox. **Perbaikan:** pill nav seragam pakai `SeparatorBrush` (atau `BorderSubtleBrush`), simpan `NavigationDividerBrush` hanya untuk `Border.navigationSeparator`.

#### T5.2 [SEDANG] `NavigationDividerBrush` dipakai sebagai border penuh 1px pada panel
- `Views/CardDetailPanelHost.axaml:11` `BorderBrush="{DynamicResource NavigationDividerBrush}" BorderThickness="1,0,0,0"`
- `Views/EditingCardListView.axaml:212`, `Views/LateCardListView.axaml:197` — idem

Garis pembatas panel idealnya `SeparatorBrush`/`BorderSubtleBrush`; `NavigationDividerBrush` (2px divider) terlalu kontras untuk 1px. **Perbaikan:** ganti ke `SeparatorBrush`.

#### T5.3 [SEDANG] Dua token tumpang-tindih untuk tugas yang sama + divider manual
- `SeparatorBrush` dipakai di 29 file; `BorderSubtleBrush` dipakai 23 kali untuk divider/garis (`EditingCardListView.axaml:51,418`, `LateCardListView.axaml:51,403`, dll).
- 49 border manual `BorderThickness="0,0,0,1"` / `"0,1,0,0"` alih-alih `Rectangle.divider`/`Separator` resmi.

**Perbaikan:** tetapkan aturan tunggal — divider daftar/konten → `SeparatorBrush` + `<Rectangle Classes="divider"/>` atau `<Separator/>` (sudah ada stylenya di `BMachineDesignSystem.axaml:633-642`); `BorderSubtleBrush` hanya untuk garis kartu. Hindari border manual berulang.

---

### 🔴 KATEGORI 6 — Nav/Tab Button Tidak Konsisten

#### T6.1 [TINGGI] Enam sistem tab paralel di luar `TrelloBoardTab/CompactTab/LogPanelTab`
Selain class resmi, ada varian lokal yang menduplikasi logika tab:
- `Views/SettingsView.axaml:44,74,80,84,87,90` → `RadioButton.Segmented`
- `Views/ExplorerSettingsWindow.axaml:49,71,75,80,96,100,104,108,113,129,133` → `Button.Tab` + `RadioButton.Segmented`
- `Views/MantraDataView.axaml:151,162,166` → `Button.SegTab` / `Button.SegTab.Active`
- `Views/ToolboxWindow.axaml:38,56,60,64` → `Button.Tab` (**didefinisikan tapi dipakai 0 kali = dead code**)

```xml
<!-- MantraDataView.axaml:166 — tab aktif tanpa indikator, pakai warna solid -->
<Style Selector="Button.SegTab.Active">
    <Setter Property="Background" Value="{DynamicResource AccentColorBrush}"/>
    <Setter Property="Foreground" Value="{DynamicResource ButtonPrimaryForegroundBrush}"/>
</Style>
```

Berbeda dengan `TrelloBoardTab` yang punya `PART_Indicator` (underline biru, `TrelloNavigation.axaml:174-181`), varian lokal ini **tidak punya indikator aktif** → bahasa visual tab antar layar tidak seragam.

**Perbaikan:** migrasikan `Segmented`/`SegTab`/`Button.Tab` ke `RadioButton Classes="TrelloBoardTab CompactTab"` (atau `LogPanelTab` untuk panel log) sehingga indikator `PART_Indicator` dan state hover/checked ikut otomatis. Hapus style `Button.Tab` mati di `ToolboxWindow.axaml:38-66`.

#### T6.2 [RENDAH] Override lokal `RadioButton.LogPanelTab`
- `Views/LogPanelSidebar.axaml:532` menimpa padding `LogPanelTab` yang sudah resmi di `TrelloNavigation.axaml:225-231`.

**Perbaikan:** jika padding memang perlu beda, tambahkan varian resmi di `TrelloNavigation.axaml`, jangan override di View.

*(Catatan positif: pemakaian `Classes="TrelloBoardTab CompactTab"` sudah konsisten di 23 titik — DashboardView, UnifiedTrelloView, ToolboxWindow, LeaderboardView, PointLeaderboardView, SettingsView, ExplorerSettingsWindow.)*

---

### 🔴 KATEGORI 7 — Search Bar / TextBox Tanpa Border Token

#### T7.1 [TINGGI] Search box menghapus border token (`BorderThickness="0"`)
- `Views/LogPanelSidebar.axaml:232` — `Watermark="Search..." Background="Transparent" BorderThickness="0"` (border dihilangkan, hanya border luar `Border`)
- `Views/OutputExplorerView.axaml:906-907` — `BorderThickness="0" Background="Transparent" Padding="0" MinHeight="0" Height="20"` (menghapus border **dan** tinggi 36)

```xml
<!-- OutputExplorerView.axaml:906 -->
<TextBox x:Name="SearchBox" ... BorderThickness="0" Background="Transparent" Padding="0" MinHeight="0" Height="20" ...>
```

**Perbaikan:** pakai `Classes="search"` (`BMachineDesignSystem.axaml:326-341`, sudah `RadiusMd`, border `BorderDefaultBrush`→`BorderHoverBrush`→`BorderFocusBrush`, tinggi 36). Jika butuh "borderless di dalam pill", tetap pertahankan border token pada elemen dalam atau pindahkan border ke kontainer secara konsisten.

#### T7.2 [SEDANG] Search box tidak memakai `Classes="search"` (hanya 2 tempat)
Hanya `Views/DashboardView.axaml:533,827` yang memakai `Classes="search"`. Search lain menulis style sendiri:
- `Views/MantraDataView.axaml:255` — `CornerRadius="4"` (**di luar skala**, harusnya `RadiusSm=6`) + `Height="24"`
- `Views/ToolboxWindow.axaml:584,642` — `Classes="Input"` + `Padding="10,7"` + tinggi bebas
- `Views/CardDetailPanelHost.axaml:528,543` & `UnifiedTrelloView.axaml:447,462` — `AutoCompleteBox ... BorderThickness="0"` di dalam `Border` `CornerRadius="12"` (12 = `RadiusXl`, tapi inner tanpa border)

**Perbaikan:** seragamkan semua search bar ke `Classes="search"`; perbaiki `CornerRadius="4"` → `{StaticResource RadiusSm}` di `MantraDataView.axaml:255`; pastikan `AutoCompleteBox` search memakai `BorderDefaultBrush` (sudah ada style-nya di `BMachineDesignSystem.axaml:380-390`).

---

## 3. Rekomendasi Perbaikan Konkret (Ringkas)

| # | Aksi | File contoh | Token/Class tujuan |
|---|---|---|---|
| 1 | Definisikan `SurfaceElevatedBrush` & `PrimaryBrush` (atau ganti pemakaiannya) | `DarkTheme.axaml`, `LightTheme.axaml`; `EditingCardListView.axaml:211`, `LogPanelSidebar.axaml:259` | `#202226` / `#F1F3F5`, `AccentColorBrush` |
| 2 | Ganti status merah/hijau hardcoded | `UnifiedTrelloView.axaml:46,49`, `EditingCardListView.axaml:390,629` | `AccentRedBrush`, `AccentGreenBrush` |
| 3 | Ganti danger `#FF453A` | `DashboardView.axaml:350`, `OutputExplorerView.axaml:357` | `ButtonDangerForegroundBrush` / `Classes="danger"` |
| 4 | Ganti biru lama `#0078D7` | `EditingCardListView.axaml:161,373`, `UnifiedTrelloView.axaml:233` | `AccentBlueBrush`, `AccentSubtleBrush` |
| 5 | Seragamkan radius ke skala | 45 file (lihat T2.1) | `RadiusSm/Md/Lg/Xl` |
| 6 | Ukuran ikon lewat token | `DashboardView.axaml:435`, `PathSettingsView.axaml:52` | `Classes="sm/lg/xl"` / `IconSm..IconXl` |
| 7 | Samakan border pill nav | `ToolboxWindow.axaml:1136` vs `DashboardView.axaml:173` | `SeparatorBrush` |
| 8 | Panel pakai separator, bukan divider | `CardDetailPanelHost.axaml:11`, `EditingCardListView.axaml:212` | `SeparatorBrush` |
| 9 | Migrasi tab lokal → class resmi | `SettingsView.axaml:44`, `ExplorerSettingsWindow.axaml:80`, `MantraDataView.axaml:151`, `ToolboxWindow.axaml:38` | `TrelloBoardTab CompactTab` / `LogPanelTab` |
| 10 | Search bar pakai class resmi | `LogPanelSidebar.axaml:232`, `OutputExplorerView.axaml:906`, `MantraDataView.axaml:255` | `Classes="search"` + `RadiusSm` |

**Prioritas eksekusi:** T1.1 (panel rusak) → T1.2/T1.3 (tema terang & semantic) → T6.1 (nav tidak konsisten) → T7.1 (search) → T2.1 (radius, massal) → T3.1 (ikon, massal) → sisanya.

**Catatan metodologi:** setiap temuan di atas diverifikasi dengan `grep -rn`/`grep -rnoP` pada `Views/**` dan `Controls/**`, lalu dicek definisi tokennya di `Styles/**` dan `Themes/**`. Glyph pada komentar (`═ ─ ═` banner) tidak dihitung sebagai pelanggaran. Temuan dibatasi pada isu berdampak visual/UX; nitpick yang tidak terverifikasi tidak dilaporkan.
