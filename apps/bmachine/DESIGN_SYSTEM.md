# BMachine Design System v1.0

Aturan design terpusat agar semua komponen selaras. File token & style: [`BMachineDesignSystem.axaml`](src/BMachine.UI/Styles/BMachineDesignSystem.axaml) + [`TrelloNavigation.axaml`](src/BMachine.UI/Styles/TrelloNavigation.axaml) (pill nav). Theme colors: [`DarkTheme.axaml`](src/BMachine.UI/Themes/DarkTheme.axaml) / [`LightTheme.axaml`](src/BMachine.UI/Themes/LightTheme.axaml). Global overrides: [`App.axaml`](src/BMachine.App/App.axaml).

> **Prinsip:** Jangan hardcode warna/radius/font di View. Pakai token & `Classes` di bawah. Jika butuh varian baru, tambah di DesignSystem, bukan di View.

---

## 1. Typography

**Font hanya dari bundle aplikasi.** Tidak boleh memakai font sistem OS (Segoe UI, Arial, Consolas, dll).

| Token | Value | Sumber |
|-------|-------|--------|
| `FontPrimary` | `avares://BMachine.UI/Assets/Fonts#Inter` | Inter (`Avalonia.Fonts.Inter`, `WithInterFont()`) |
| `FontMono` | `avares://BMachine.UI/Assets/Fonts#JetBrains Mono` | `Assets/Fonts/JetBrainsMono-*.ttf` |

Font yang dipakai (wajib tersedia sebagai file):
- Inter: Regular, Medium, SemiBold, Bold, ExtraBold (ExtraBold hanya untuk `navLabel`, file `Inter-ExtraBold.ttf`, family `Inter ExtraBold`) (Italic jika dibutuhkan)
- JetBrains Mono: Regular, Medium, SemiBold, Bold

**Skala weight (satu-satunya yang boleh dipakai):**

| Weight | Pakai untuk |
|--------|-------------|
| `Light` | Subjudul branding (`MACHINE` di splash) |
| `Normal` | Body, isi teks panjang, input |
| `Medium` | Label menu/nav, caption yang perlu dibaca |
| `SemiBold` | Button, judul kecil (h2/h3), header kolom, label aktif |
| `Bold` | Judul utama (h1), nilai penting, eyebrow |
| `Black` | Dilarang di UI. Hanya untuk logo |
| `ExtraBold` | Hanya untuk label navigasi (`navLabel`, family `Inter ExtraBold`). Lain dilarang |

Aturan:
- Jangan pakai `Black` untuk teks UI; ganti `Bold`.
- `ExtraBold` hanya untuk `navLabel`. Dilarang di teks lain.
- Jangan pakai angka weight ad hoc. Gunakan salah satu di tabel.
- `Italic` hanya untuk teks placeholder/catatan/kutipan. Jangan dipakai untuk penekanan judul atau label.

**Skala ukuran:** `11` caption, `12` body kecil/button/menu, `13` body, `14` h3, `18` h2, `24` h1. Angka lain (9, 9.5, 10.5, 11.5, 12.5, 13.5, 15, 20, 22, 26, 28) jangan dipakai baru, kecuali icon/badge yang diatur ukuran khusus.

**Classes TextBlock:**
- `h1` — 24 Bold, tracking 0.3
- `h2` — 18 SemiBold
- `h3` — 14 SemiBold
- `body` — 13 Normal, TextSecondary
- `caption` — 11 Medium, muted #71717A
- `eyebrow` — 10.5 SemiBold, mono, tracking 1.2, #52525B
- `mono` — mono 12 Normal

Default `TextBlock` = FontPrimary Normal, TextPrimary, line-height 20.

---
## 2. Warna (semantic)

| Token | Dark | Light | Pakai untuk |
|-------|------|-------|-------------|
| `AppBackgroundBrush` | #121212 | #F5F5F5 | window bg |
| `CardBackgroundBrush` | #1A1C20 | #FFFFFF | card/bento |
| `SeparatorBrush` | #26282C | #E5E7EB | border/divider |
| `TextPrimaryBrush` | #FFFFFF | #121212 | teks utama |
| `TextSecondaryBrush` | #99FFFFFF | #666666 | teks sekunder |
| `TextMutedBrush` | #71717A | #71717A | caption |
| `AccentColorBrush` / `AccentBlueBrush` | #3b82f6 | #3b82f6 | primary accent |
| `BgHoverBrush` | #22272B | #22272B | hover pill/nav |
| `BgActiveBrush` / `AccentSubtleBrush` | #263B82F6 (15% Accent) | sama | active pill |
| `BgInputBrush` | #0C0C0E | #0C0C0E | input bg |
| `BorderDefaultBrush` | #232326 | #232326 | input border |
| `BorderHoverBrush` | #3F3F46 | #3F3F46 | hover border |
| `BorderFocusBrush` | #4B4B52 | #4B4B52 | focus border |
| `OverlayBrush` | #CC000000 | #99000000 | scrim/backdrop |
| `ScrimStrongBrush` | #E6000000 | #E6000000 | scrim kuat (lightbox, loading) |
| `ScrimSoftBrush` | #99000000 | #99000000 | scrim lembut |
| `TintHoverBrush` | #0AFFFFFF | #0AFFFFFF | hover tint putih |
| `StatusDangerBrush` / `StatusSuccessBrush` | merah / hijau status | sama | dot/indikator status |
| `SurfaceElevatedBrush` | #202226 | #F1F3F5 | panel detail, comment, checklist |
| `PrimaryBrush` | #3b82f6 | #2563EB | accent primer (sidebar log) |
| `AccentEditingColor` / `AccentLateColor` / `AccentRevisionColor` | #3b82f6 / #ef4444 / #f97316 | sama | warna kategori kartu list |

Jangan pakai hex random. Jika butuh warna baru, tambah sebagai token di `BMachineDesignSystem.axaml`.

---

## 3. Radius & Spacing

| Token | Value | Pakai untuk |
|-------|-------|-------------|
| `RadiusXxs` | 2 | garis/indikator kecil |
| `RadiusXs` | 4 | badge, chip kecil, tombol inline |
| `RadiusSm` | 6 | button, input, checkbox, tooltip |
| `RadiusMd` | 8 | card kecil, search box |
| `RadiusLg` | 10 | card/bento |
| `RadiusXl` | 12 | modal/dialog |
| `RadiusPill` | 999 | pill/avatar. Lingkaran murni (Width = Height = 2×radius) pakai `RadiusPill` |

Spacing: `SpaceXs 4`, `SpaceSm 8`, `SpaceMd 12`, `SpaceLg 16`, `SpaceXl 20`.

---

## 4. Button — 4 varian selaras

Semua: `Radius 6`, `Height 36`, `Font 12.5 SemiBold`, `Padding 16,8`, `transition 0.15s`.

| Classes | Background | Foreground | Border | Hover | Pressed |
|---------|------------|------------|--------|-------|---------|
| `primary` / `Action` | #EDEDED | #111111 | 0 | #FFFFFF | #D4D4D4 |
| `secondary` / `Muted` | #1C1C1F | #E4E4E7 | 1px #3F3F46 | #27272B / #52525B | #222226 |
| `ghost` / `Clear` | Transparent | inherit | 0 | #10FFFFFF | #1AFFFFFF |
| `danger` / `Warn` | #1C0F0F | #A05252 | 1px #5A2020 | #2A1010 | — |
| `accent` / `Accent` | Transparent | Accent | 1px Accent | AccentSubtle #263B82F6 | — |

> Alias `Action`/`Muted`/`Clear`/`Warn` tetap didukung untuk kompatibilitas. Prefer `primary`/`secondary`/`ghost`/`danger` untuk kode baru.

---

## 5. TextBox / ComboBox / AutoCompleteBox / NumericUpDown

Selaras: `Bg #0C0C0E`, `Border #232326`, `Radius 6`, `MinHeight 36`, `Padding 10,0`, `Font 13`.

- **Hover:** Border #3F3F46, Bg #111113
- **Focus:** Border #4B4B52
- **Disabled:** opacity 0.4

`ComboBoxItem`: padding 10,6, radius 6, margin 2, **MinHeight 32**, hover #22272B, selected AccentSubtle.

**Ukuran daftar pilihan (aturan wajib):** `MaxDropDownHeight 240` dan lebar minimum daftar `160` — HANYA di `BMachineDesignSystem.axaml` (style `ComboBox` + `ComboBox /template/ Border#PART_PopupBorder`; AutoCompleteBox juga 240).
- Dilarang menulis `MaxDropDownHeight` (atau lebar daftar) langsung di view/code-behind.
- Alasannya nyata: dulu nilainya campur — 160 di panel kartu & list, 220 di explorer dan sel spreadsheet, sisanya default tema — sehingga daftar yang isinya sama tinggi/lebarnya berubah begitu pindah tampilan atau kolom. Ukuran daftar harus sama di mana pun.

**Perilaku buka daftar (aturan wajib):** satu klik di MANA PUN pada ComboBox/AutoCompleteBox harus membuka daftarnya, bukan cuma klik chevron kecil. Untuk ComboBox otomatis; untuk AutoCompleteBox dijaga handler global di `BMachine.App/App.axaml.cs` (`InputElement.PointerPressedEvent.AddClassHandler<AutoCompleteBox>` → `IsDropDownOpen = true`). Item daftar saran distyle lewat selector terbatas `AutoCompleteBox ListBoxItem` (hover/selected/padding) supaya item ListBox lain tidak ikut berubah.

Varian: `TextBox.mono` (mono 12), `TextBox.search` (radius 8).

---

## 6. CheckBox

Box 16×16, radius 4, border #3F3F46, transparent bg. Hover border #52525B + bg #1A1A1D. Checked bg Accent + border Accent. Transition 0.15s. Font 12.5, TextSecondary.

---

## 7. Toggle Switch

`ToggleButton.Switch` — Track 44×24, radius 12, thumb 18, radius 9. Off: bg #1AFFFFFF, border #0AFFFFFF, thumb TextSecondary. On: bg Accent, thumb white, margin 23,0,0,0. Hover off #25FFFFFF, on opacity 0.85. Pressed thumb width 22. Transition 0.25s spline.

---

## 8. Icon (PathIcon)

Semua icon UI memakai outline Tabler melalui `PathIcon` + `StreamGeometry` dari `TablerIcons.axaml`. Jangan pakai emoji/unicode glyph sebagai icon UI.

**Ukuran (token, wajib):** `IconXs 12`, `IconSm 14`, `IconMd 16` (default), `IconLg 18`, `IconXl 24`. Classes: `PathIcon.xs` / `.sm` / `.lg` / `.xl`.

**Aturan agar icon tidak terpotong:**
- Icon WAJIB berada di kotak persegi (`Width` = `Height`) dan memakai `Stretch="Uniform"`.
- Ukuran kontainer (button/grid) minimal `IconSize + 8` (contoh icon 16 → kontainer minimal 24). Jangan set `Width`/`Height` kontainer lebih kecil dari itu.
- Jangan set `ClipToBounds="True"` pada button/grid yang membungkus icon kecuali kontainernya sudah cukup besar.
- Stroke `1.8` di `PathIcon` default. Jika icon dipakai di ukuran `xs` (12), boleh turun ke `1.5` agar tidak penuh.
- Icon di dalam Button otomatis ikut `Foreground` button.
- **Teks di dalam Button juga WAJIB ikut `Foreground` button** — jangan mengandalkan
  warisan. Style global `TextBlock` (`Foreground=TextPrimaryBrush`) punya precedence
  lebih tinggi dari nilai warisan, jadi label tombol tanpa style ini mengambil warna
  teks tema: di tema gelap + palet Classic itu = putih di atas bg `#EDEDED` sehingga
  label HILANG (terukur: 0 piksel teks; icon selamat karena sudah punya binding).
  Sudah ditangani style `Button.<variant> TextBlock` → `$parent[Button].Foreground`
  di `BMachineDesignSystem.axaml` (varian primary/secondary/danger/accent).
  Jangan menambah `Foreground` lokal pada label/icon tombol — itu mematikan
  adaptivitas palet (`AppearancePaletteService` meng-override
  `ButtonPrimaryForegroundBrush` + `TextOnPrimaryBrush` bersamaan).
- Icon tanpa teks: beri `ToolTip.Tip` agar label tetap terbaca saat hover.

---
## 9. Card / Bento

`Border.card` — Bg CardBackgroundBrush, border SeparatorBrush 1px, radius 10, padding 16, ClipToBounds True. `Border.cardSm` — radius 8, padding 12. Tanpa shadow/glow.

---

## 10. Navigation Pill (Trello style)

`RadioButton.TrelloBoardTab` — card bordered (CardBackground + Separator 1px, radius 8, padding 4) berisi pill. Pill: transparent, radius 6, padding 12,7, label `TextBlock.navLabel` (family `Inter ExtraBold`, 12), hover #22272B, active #263B82F6 + indicator 20×3 Accent, opacity 0→1 (reserve space). Hover inactive tetap abu #22272B, bukan Accent. Lihat [`TrelloNavigation.axaml`](src/BMachine.UI/Styles/TrelloNavigation.axaml).

---

## 11. Hover / Active / Focus — aturan umum

- **Hover:** selalu `Background` atau `BorderBrush` transition 0.12–0.15s, jangan pakai scale/rotate kecuali button press (scale 0.97).
- **Active/Checked:** pakai AccentSubtle (#263B82F6) + indicator pendek, bukan full background solid.
- **Focus:** border #4B4B52, jangan pakai outline tebal.
- **Disabled:** opacity 0.4, bukan warna abu terpisah.

---

## 12. Cara pakai

```xml
<!-- Button -->
<Button Classes="primary" Content="Simpan"/>
<Button Classes="secondary" Content="Batal"/>
<Button Classes="ghost" Content="X"/>
<Button Classes="danger" Content="Hapus"/>

<!-- Input -->
<TextBox Watermark="Cari..."/>
<TextBox Classes="mono" Watermark="0x..."/>
<ComboBox SelectedItem="{Binding Foo}"/>
<AutoCompleteBox Watermark="Pilih..."/>
<NumericUpDown Value="{Binding Count}"/>

<!-- Check -->
<CheckBox Content="Ingat saya" IsChecked="{Binding Remember}"/>

<!-- Toggle -->
<ToggleButton Classes="Switch" IsChecked="{Binding Enabled}"/>

<!-- Icon -->
<PathIcon Data="{StaticResource IconSettings}" Classes="sm"/>
<PathIcon Data="{StaticResource IconTrash}" Classes="lg"/>

<!-- Card -->
<Border Classes="card">
  <TextBlock Classes="h3" Text="Judul"/>
</Border>

<!-- Nav pill -->
<Border Background="{DynamicResource CardBackgroundBrush}" BorderBrush="{DynamicResource SeparatorBrush}" BorderThickness="1" CornerRadius="8" Padding="4">
  <StackPanel Orientation="Horizontal" Spacing="2">
    <RadioButton Classes="TrelloBoardTab" Content="TAB A" IsChecked="{Binding IsA}"/>
    <RadioButton Classes="TrelloBoardTab" Content="TAB B" IsChecked="{Binding IsB}"/>
  </StackPanel>
</Border>
```

---

## 13. Yang tidak boleh

- Hardcode `Background="#1A1A1D"` / `CornerRadius="7"` / `FontFamily="..."` di View — pakai token.
- Beda radius untuk komponen sejenis (misal button 6 vs 7 vs 10).
- Icon beda ukuran tanpa alasan (12 vs 14 vs 18 random).
- Hover beda warna per View (harus #22272B untuk pill, #27272B untuk secondary button, #10FFFFFF untuk ghost).
- Set ukuran daftar pilihan (`MaxDropDownHeight`, lebar minimum daftar) di view/code-behind — ukuran daftar ComboBox/AutoCompleteBox hanya dari style global (§5).

## 14. Scrollbar

- Scrollbar di semua ScrollViewer disembunyikan permanen lewat ControlTheme global (`BMachineDesignSystem.axaml`, ScrollBar `IsVisible="False"`).
- Scroll tetap bisa lewat mouse wheel / gesture.
- Jangan tambah scrollbar per-view.

## 16. Border, Separator & Divider

Tiga jenis garis, masing-masing punya peran dan token sendiri. Jangan campur.

| Jenis | Peran | Token | Cara tulis |
|-------|-------|-------|------------|
| **Border** | Batas kontainer/komponen interaktif (card, input, dropdown, dialog) | `BorderDefaultBrush` (input), `SeparatorBrush` (card) | `BorderBrush="{DynamicResource ...}"` `BorderThickness="1"` |
| **Separator** | Garis pemisah antar item dalam satu kelompok (list, menu, form) | `SeparatorBrush` | `<Separator/>` (style global, tinggi 1) |
| **PanelDivider** | Pemisah antar panel yang tampil berdampingan | `PanelDividerBrush` | `Classes="panel-divider"` |

**Aturan:**
- **Satu garis per batas area.** Kalau dua garis berdempetan (jarak < 6px), hapus garis KANAN; garis kiri tetap satu-satunya. Terjadi saat panel punya border sendiri padahal di sebelahnya sudah ada pemisah (mis. `LogPanelResizeHandle` 2px) — border kiri panel (`BorderThickness="1,0,0,0"`) lalu dihilangkan. Berlaku untuk semua tema (light/dark/palette).
- Card/bento: `Border` + `SeparatorBrush` 1px, radius 10 (`Classes="card"`). Tanpa shadow.
- Input (TextBox, ComboBox, dropdown): `BorderDefaultBrush` 1px, berubah ke `BorderHoverBrush` saat hover dan `BorderFocusBrush` saat focus.
- Separator antar baris/item: selalu `<Separator/>`. Jangan buat `Border Height="1"` atau `Rectangle Height="1"` manual.
- Panel berdampingan: `Classes="panel-divider"`. Panel tunggal yang mengisi window tidak boleh diberi divider.
- Dilarang: `BorderBrush="#hex"`, `Stroke="#hex"`, `Fill="#hex"` untuk garis. Pakai token tema agar light mode ikut berubah.
- Warna garis wajib **opaque**: hex AARRGGBB dengan alpha `00` (mis. `#00333333`) = transparan → semua separator/border hilang di tema Classic. `ThemeService` menormalisasi alpha `00`→`FF` saat load & saat simpan; picker border jangan pernah mengirim A=0.
- Dilarang: `Background="#1A1A1D"` atau warna card hardcoded. Pakai `CardBackgroundBrush`.
- Ketebalan standar: 1px. Ketebalan 2px hanya untuk indikator aktif (mis. tab underline).
- Garis tebal/glow (DropShadow) pada border dilarang, kecuali accent pada splash.

---
**Status penerapan:** garis manual `Border Height="1"` pada list/detail sudah diganti `<Separator/>`. Sisa `Rectangle Height="1"` sebagai footer divider pada dialog (Grid, `ColumnSpan`) dibiarkan dan dianggap pola sah.
**Button tanpa class dilarang.** Setiap `<Button>` wajib punya `Classes`:
- `primary` / `Action`: aksi utama.
- `secondary` / `Muted`: aksi sekunder/batal.
- `ghost` / `Clear`: tanpa border, background transparan (inline, toolbar teks).
- `ghostIcon`: tombol ikon borderless.
- `danger` / `Warn`: aksi hapus/destruktif.
- `accent` / `Accent`: aksi outline aksen.
- `small`: varian kompak (tinggi 24, font 11, padding 8,2) untuk aksi inline di baris/list.

Button tanpa `Classes` hanya boleh bila memang tampil sebagai tombol default dari style global (tinggi 36, font 12.5 SemiBold, radius 6).
## 15. Tombol ikon borderless (ghostIcon)

- Tombol ikon tanpa border/background (ikon toolbar: +, refresh, copy, trash, close X, send, dll) WAJIB pakai `Classes="ghostIcon"`.
- `ghostIcon` (di `BMachineDesignSystem.axaml`) memakai template minimal + setter Transparent eksplisit untuk state `:pointerover`/`:pressed`, sehingga tema Semi.Avalonia tidak menyuntikkan hover background/border.
- JANGAN sekadar menghapus blok `<Button.Styles>` hover dari satu view: style lokal itu juga yang menahan tema; setelah dihapus, tema default malah menampilkan hover lagi. Override eksplisit (template minimal) yang benar.
- Tombol ber-label/ber-border (Action, primary, Muted, statusBtn, badge) TIDAK pakai ghostIcon.

## 17. Density (Compact / Normal / Comfortable)

- Ukuran tombol, ikon, dan teks diambil dari token `Density*` (`UiDensityService`), bukan angka lokal di View.
- Mode: Compact (tombol 24, font 11), Normal (36, 12.5), Comfortable (40, 13.5).
- Pilihan disimpan di `Settings.UiDensity` dan diterapkan sebelum splash.
- View tidak boleh memakai `Height`/`FontSize` tetap untuk tombol atau ikon.

## 18. Aturan Font

- Hanya `Inter` (teks) dan `JetBrains Mono` (kode/log), dimuat dari `avares://BMachine.UI/Assets/Fonts`.
- Dilarang memakai font sistem. Explorer hanya menerima dua font ini.
- Bold untuk navigasi header dan judul; SemiBold untuk label tombol; Regular untuk isi.
- Label navigasi (HOME, BATCH, LOCKER, EXPLORER, LOG, MASTER, DOC, nama profil) memakai style `TextBlock.navLabel` (family `Inter ExtraBold`, 12). Isi popup profil (Settings, Log Out) memakai `FontPrimary` weight Bold, bukan `navLabel`. Jangan set FontSize/FontWeight lokal pada teks navigasi.

## 19. Tooltip & Splash

- Tooltip memakai token tema (`CardBackgroundBrush`, `SeparatorBrush`, `TextPrimaryBrush`) pada `ToolTip` dan `ToolTip /template/ Border`.
- Splash memakai `AppBackgroundBrush` solid, tanpa gradient, mengikuti tema tersimpan.

## 20. Shadow Pembatas

- Pembatas panel memakai `Border.panel-divider` dengan `BoxShadow` tipis (`0 0 6 0 #14000000`), bukan garis keras saja.
- Hanya dipakai pada panel yang berdampingan dengan konten lain.

## 21. Wrap — mana yang boleh, mana yang dilarang

Prinsip: **wrap hanya untuk konten yang panjangnya tak terbatas dan tidak dipakai mengukur kontrol lain.** Yang menentukan ukuran (daftar pilihan, baris tombol) tidak boleh wrap, karena tinggi/lebar jadi berubah-ubah dan terlihat "tiba-tiba membesar".

WAJIB wrap (kalau tidak, terpotong di panel sempit):
- Judul/nama kartu: `TextWrapping="Wrap"` di TextBlock judul.
- Badge & label (ID, Attachments, Due, Labels): `WrapPanel`.
- Baris tombol aksi panel detail (Comment/Checklist/Move + 2 tombol ikon): `WrapPanel` — boleh turun baris, tapi ukuran tombolnya tetap 32 px.

DILARANG wrap:
- Item daftar pilihan ComboBox/AutoCompleteBox: satu baris, `TextTrimming="CharacterEllipsis"`, `MinWidth` 150 (lihat §5). Teks panjang jadi elipsis, BUKAN item dua baris.
- Teks di dalam tombol: `NoWrap` supaya tombol tidak menggemuk dan mengubah tinggi baris.
- Angka/nilai di kartu statistik dan kolom tabel: `NoWrap`.

Kalau sebuah elemen butuh ruang lebih: beri ruangnya dari awal (`MinWidth`, lebar kolom, atau ukuran tetap) — jangan biarkan dia "kecil dulu lalu membesar" setelah render.


## 22. Context Menu — wajib seragam di seluruh aplikasi

SEMUA permukaan teks wajib memakai context menu aplikasi (tema ContextMenu/MenuItem
sendiri), BUKAN bawaan Semi.Avalonia:

- `TextBox` (kotak komentar, field, AutoCompleteBox di dalamnya)
- `SelectableTextBlock` (deskripsi kartu, log) — di sini menu bawaan muncul saat teks DIBLOK
- Kontrol lain yang menempelkan `ContextMenu` di ControlTheme-nya

Kenapa: Semi.Avalonia menempelkan menu bawaan berbahasa Mandarin (`剪切`/`复制`/`粘贴`) dengan
tema Semi di setiap ControlTheme TextBox/SelectableTextBlock. Override resource
(`TopLevelMenuItem`, `ItemContainerTheme`) TIDAK berpengaruh karena Semi mendefinisikan
resource itu di dictionary-nya sendiri. Satu-satunya cara andal: ganti `ContextMenu`-nya.

Cara pasang (sudah ada di `src/BMachine.App/App.axaml.cs`):
```csharp
Control.ContextRequestedEvent.AddClassHandler<TextBox>((c, e) => { ... menu.Open(c); e.Handled = true; },
                                                        RoutingStrategies.Tunnel);
```
- Tunnel class handler: jalan lebih dulu dari handler ContextMenu bawaan sehingga bisa
  dibatalkan dengan `e.Handled = true`.
- Item TextBox: Cut / Copy / Paste / Select All (Inggris, pakai `InputGesture` Ctrl+X/C/V/A;
  Cut & Paste di-disable saat `IsReadOnly`).
- Item SelectableTextBlock: Copy / Select All (Copy disabled kalau tidak ada `SelectedText`).
- Muncul permukaan teks baru (mis. kontrol teks pihak ketiga): tambahkan handler tipe itu
  di file yang sama, JANGAN biarkan terpasang menu bawaan.

## 23. Panel Edit Slot Radial (Settings → Script Manager → RADIAL MENU)

Dua mode, satu panel:

| Mode | Layout |
|------|--------|
| Awal (belum ada slot dipilih) | Roda radial di **tengah** area konten, panel SLOT DETAILS **tersembunyi** |
| Edit (slot dipilih / slot baru ditambah) | Panel SLOT DETAILS di kolom kanan, roda bergeser ke kiri (layout kiri-kanan) |

Keluar dari mode edit: tombol tutup (X) di header panel atau tombol **Esc**
(`KeyBinding Gesture="Escape"` → `CloseSlotDetailsCommand`) → roda kembali ke tengah.

Implementasi layout: satu `Grid x:Name="RadialRowGrid"` yang **kolomnya diatur code-behind**
(`UpdateRadialRowLayout()` di `SettingsView.axaml.cs`, dipanggil saat resize + saat
`IsSlotDetailsOpen` berubah):

| Kondisi | Kolom | Hasil |
|---------|-------|-------|
| panel tertutup | `*, Auto, *` (roda di kolom 1) | roda **center** |
| panel buka, lebar cukup | `Auto, *` (roda kiri, panel kanan) | **kiri-kanan**, panel fleksibel |
| panel buka, terlalu sempit | `*` + baris `Auto, Auto` | panel turun baris (fallback, tidak terpotong) |

Kunci "tidak terpotong": kolom panel memakai `*` (bukan `Auto`, bukan lebar tetap) sehingga panel
ikut menyusut mengisi sisa lebar. `MinWidth` panel dipakai sebagai ambang keputusan di code-behind
(roda 234 + gap 16 + panel 280 + 8), bukan sebagai pemaksa lebar.

Chevron `<` `>` di kiri/kanan roda **tidak dipakai lagi** — pindah halaman pakai slot navigasi
`MORE`/`BACK` di roda itu sendiri (`IsNavigationReserved`).

Kenapa tidak boleh `ColumnDefinitions="*, Auto, *"` dengan panel lebar tetap: di jendela kecil
isi panel KELUAR dari tepi kanan jendela (field "Short Code" hilang separuh, tombol Hapus
terpangkas) karena kolom Auto tidak bisa menyusut.

Aturan tambahan untuk panel yang bisa muncul di samping konten lain:
- Panel detail wajib punya `MinWidth` (mis. 348) + `ClipToBounds="True"`.
- Baris tombol di dalam panel pakai `WrapPanel` (lebar alami tombol, turun baris kalau sempit) —
  JANGAN Grid `*, Auto, Auto`; kolom Auto tidak bisa menyusut sehingga label tombol saling menimpa.
- Beri `Margin` kanan/bawah pada anak WrapPanel supaya jarak antar-item tetap ada saat turun baris.

Struktur panel (urutan tetap):
1. Header: judul (eyebrow, `TextPrimaryBrush`) + subjudul 11 px + tombol X 28x28 (`ghost`).
2. Kartu file: permukaan `BgHoverBrush` + border 1 px; ikon 44x44 (bisa diklik ganti ikon);
   nama file 13 px SemiBold; path 11 px `TextSecondaryBrush` + `TextTrimming="CharacterEllipsis"`
   + tooltip berisi path penuh (path TIDAK boleh di-wrap sampai panel melar).
3. Field: dua kolom **sama lebar** (`*, *`), label 11 px Medium di atas input.
4. Baris aksi: tinggi tombol **seragam 32 px** — hanya `danger` (Hapus) + `primary` (Simpan).
   Tombol Mundur/Maju **dihapus**: memindahkan posisi slot dilakukan dengan **drag** slot di roda,
   bukan tombol. Jangan tambahkan tombol geser posisi lagi di panel.

DILARANG di panel ini:
- Baris spacer `*` di `RowDefinitions` (bikin jarak mati besar antar-elemen).
- Kolom field lebar tetap kecil (mis. 90 px) untuk input pendek — pakai `*, *`.
- Tombol aksi beda tinggi / beda padding antar tombol.

## 24. Baris tombol Master (tab BATCH) & isi tombol sekunder

Baris tombol master di bawah panel SOURCE/OUTPUT dibangun dari isi `Scripts/Master/*.py`
(`BatchViewModel.LoadScriptsAsync`), jadi nama file = nama tombol.

- **Hanya skrip PUBLIK yang jadi tombol.** Skrip helper yang di-import skrip lain memakai prefix
  `_` (mis. `_photoshop_utils.py`) dan WAJIB disaring dari baris tombol (`if (fname.StartsWith("_"))
  continue;`) — jangan tampilkan library sebagai aksi yang bisa diklik user. Skrip yang ingin
  disembunyikan sementara (mis. `_buat_master_buka_edit.py`) cukup diberi prefix `_`, bukan dihapus.
- **Label, ikon, dan URUTAN tombol datang dari `Scripts/scripts.json`** (`Name` / `IconKey` /
  `Order`), bukan dari XAML. Skrip tanpa entri memakai nama file apa adanya + ikon fallback
  `IconScript`. Tombol aksi manual yang menyertai alur master (mis. `DUPLIKAT PSD`) diletakkan di
  UJUNG baris — tepat setelah tombol `BUAT MASTER` — supaya urutannya tetap: jenis master
  (PROFESI, MANASIK, PAS FOTO, WISUDA) lalu aksi manual di ujung.
- **Skrip baru muncul setelah app dijalankan ulang**; daftar tombol dibangun sekali saat start.
- **Isi tombol sekunder WAJIB beda dari permukaan di belakangnya.** Token
  `ButtonSecondaryBackgroundBrush` tidak boleh dipetakan sama dengan `CardBackgroundBrush`/surface
  (di tema terang keduanya menjadi putih — terukur isi tombol 255,255,255 dengan latar 255,255,255):
  hasilnya tombol terbaca transparan dan hanya border 1 px yang terlihat ("background transparan,
  kelihatan ada garisnya"). Aturan palet: nilai tombol sekunder = surface digeser sedikit
  (lebih gelap di tema terang, lebih terang di tema gelap, `AppearancePaletteService`).
  DILARANG: menyatukan key tombol dengan key card/surface di `Brushes(...)` mana pun.

## 25. Aksi Image Preview (UnifiedTrello)

Image preview attachment memakai `ImageLightboxWindow`. Toolbar kanan-atas harus tetap satu grup
berisi ikon 16 px dalam tombol ikon 36x36 dengan tooltip: **Download Image**, **Copy Image**, lalu
**Close Viewer**. Tombol Copy memakai `IconCopy`, menulis `Bitmap` langsung ke clipboard Avalonia,
dan tidak menyimpan file sementara. Bitmap itu harus kompatibel dengan tombol **Paste Logo** pada tab
DOC; user bisa membuka preview Trello → Copy Image → tab DOC → Paste Logo tanpa download lebih dulu.

DILARANG menambah tombol teks, warna lokal, atau varian tombol berbeda di toolbar preview.

---

## 26. Pembaruan (dialog startup + baris di flyout profil)

**Kapan dipakai:** cek update jalan SEKALI saat startup (`CheckForUpdatesBackground`), hasilnya
disimpan di memori (`UpdateInfo`). Hasil itu dipakai dua tempat, tidak pernah dicek ulang:

1. **Dialog startup** — `Views/Dialogs/UpdateDialogWindow` (menggantikan strip banner lama di
   `MainWindow.axaml`). Muncul HANYA kalau ada update dan versinya belum dipilih **Nanti**.
   Ditunda sampai `MainWindow.Show()` selesai (`MarkMainWindowReady()`) lalu dibuka sebagai
   *owned window* + `ClampToVisibleArea`, supaya selalu di depan dan tidak pernah keluar layar.
2. **Baris pembaruan di flyout profil** (atas menu Terminal/Laporkan Bug/Settings/Log Out,
   `DashboardView.axaml`) — muncul selama update tersedia, termasuk setelah user memilih
   **Nanti**. Ini penanda non-intrusif: `IsVisible="{Binding HasPendingUpdate}"` membaca flag
   **in-memory**, jadi membuka flyout TIDAK membaca folder share (UNC) → tanpa lag.

**Struktur dialog (atas ke bawah):** Border card `RadiusMd` + border `SeparatorBrush` → header
(ikon unduh 42x42 `RadiusPill` di atas `AccentColorBrush`, label "PEMBARUAN TERSEDIA" 11 bold +
badge versi `RadiusPill`, baris "Versi saat ini: 11") → badan scroll (teks keterangan 12.5 +
`ItemsControl` butir catatan `UpdateNoteItems` maks 4) → baris aksi.

**Angka baris aksi (§4):** kedua tombol `MinHeight="36"` `Padding="16,8"` `Radius 6` — pakai
`MinHeight`, BUKAN `Height`. Tombol primary memakai `BorderThickness="1"` dengan
`BorderBrush="{DynamicResource ButtonPrimaryBackgroundBrush}"` (warna sama dengan background)
agar luas isi kedua tombol IDENTIK (dulu 32 vs 34 px karena `BorderThickness` 0 vs 1 — diukur
user dengan penggaris).

**Alur state:** `CheckForUpdatesBackground` → kirim `UpdateStateMessage(info)` SEBELUM cek
"Nanti" (badge tetap tampil walau sudah didismiss) → `DashboardViewModel.Receive` set
`HasPendingUpdate` + `PendingUpdateVersion` → klik baris kirim `UpdateAvailableMessage(info)`
balik ke `MainWindowViewModel.ShowUpdatePopup`. Karena `messenger tidak mengantre`, state di-push
ULANG saat `DashboardViewModel` dibuat (`MarkInitialLoadComplete`).

DILARANG:
- Membaca `LocalUpdateService.CheckAsync` (folder UNC) saat flyout dibuka — cek hanya saat startup.
- `Height="..."` pada tombol dialog; pakai `MinHeight="36"` (§4).
- Warna hardcode di dialog (`Foreground="White"`, `Background="#..."`, `CornerRadius="<angka>"`);
  pakai `TextOnPrimaryBrush`, token brush, `RadiusPill`/`RadiusXxs`.
- `Foreground` lokal pada label/tombol aksi dialog — biarkan style `Button.<variant> TextBlock`
  (§8) yang menurunkan warna dari tombolnya; foreground lokal mematikan adaptivitas palet.
- `WindowStartupLocation="CenterOwner"` tanpa clamp: owner kecil bisa melempar dialog ke luar layar.
