# BMachine Design System v1.0

Aturan design terpusat agar semua komponen selaras. File token & style: [`BMachineDesignSystem.axaml`](src/BMachine.UI/Styles/BMachineDesignSystem.axaml) + [`TrelloNavigation.axaml`](src/BMachine.UI/Styles/TrelloNavigation.axaml) (pill nav). Theme colors: [`DarkTheme.axaml`](src/BMachine.UI/Themes/DarkTheme.axaml) / [`LightTheme.axaml`](src/BMachine.UI/Themes/LightTheme.axaml). Global overrides: [`App.axaml`](src/BMachine.App/App.axaml).

> **Prinsip:** Jangan hardcode warna/radius/font di View. Pakai token & `Classes` di bawah. Jika butuh varian baru, tambah di DesignSystem, bukan di View.

---

## 1. Typography

| Token | Value |
|-------|-------|
| `FontPrimary` | `'Segoe UI Variable', 'SF Pro Text', 'Helvetica Neue', system-ui, -apple-system, sans-serif` |
| `FontMono` | `'Cascadia Code', 'JetBrains Mono', Consolas, monospace` |

**Classes TextBlock:**
- `h1` — 24 Bold, tracking 0.3
- `h2` — 18 SemiBold
- `h3` — 14 SemiBold
- `body` — 13, TextSecondary
- `caption` — 11, muted #71717A
- `eyebrow` — 10.5 SemiBold, mono, tracking 1.2, #52525B
- `mono` — mono 12

Default `TextBlock` = FontPrimary Regular, TextPrimary, line-height 20.

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

Jangan pakai hex random. Jika butuh warna baru, tambah sebagai token di `BMachineDesignSystem.axaml`.

---

## 3. Radius & Spacing

| Token | Value | Pakai untuk |
|-------|-------|-------------|
| `RadiusSm` | 6 | button, input, checkbox, tooltip |
| `RadiusMd` | 8 | card kecil, search box |
| `RadiusLg` | 10 | card/bento |
| `RadiusXl` | 12 | modal/dialog |
| `RadiusPill` | 999 | pill/avatar |

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

`ComboBoxItem`: padding 10,6, radius 6, margin 2, hover #22272B, selected AccentSubtle.

Varian: `TextBox.mono` (mono 12), `TextBox.search` (radius 8).

---

## 6. CheckBox

Box 16×16, radius 4, border #3F3F46, transparent bg. Hover border #52525B + bg #1A1A1D. Checked bg Accent + border Accent. Transition 0.15s. Font 12.5, TextSecondary.

---

## 7. Toggle Switch

`ToggleButton.Switch` — Track 44×24, radius 12, thumb 18, radius 9. Off: bg #1AFFFFFF, border #0AFFFFFF, thumb TextSecondary. On: bg Accent, thumb white, margin 23,0,0,0. Hover off #25FFFFFF, on opacity 0.85. Pressed thumb width 22. Transition 0.25s spline.

---

## 8. Icon (PathIcon)

Default 16×16, foreground TextSecondary, transition 0.15s. Token ukuran: `IconXs 12`, `IconSm 14`, `IconMd 16`, `IconLg 18`, `IconXl 24`.

Classes: `PathIcon.xs` / `.sm` / `.lg` / `.xl`. Icon di dalam Button otomatis ikut `Foreground` button.

**Aturan:** Semua icon pakai `PathIcon` + `StreamGeometry` dari `App.axaml` / `FeatherIcons.axaml` / `TablerIcons.axaml`. Jangan pakai emoji/unicode icon. Ukuran selaras, jangan hardcode Width/Height random.

---

## 9. Card / Bento

`Border.card` — Bg CardBackgroundBrush, border SeparatorBrush 1px, radius 10, padding 16, ClipToBounds True. `Border.cardSm` — radius 8, padding 12. Tanpa shadow/glow.

---

## 10. Navigation Pill (Trello style)

`RadioButton.TrelloBoardTab` — card bordered (CardBackground + Separator 1px, radius 8, padding 4) berisi pill. Pill: transparent, radius 6, padding 12,7, font 13 Medium, hover #22272B, active #263B82F6 + indicator 20×3 Accent, opacity 0→1 (reserve space). Hover inactive tetap abu #22272B, bukan Accent. Lihat [`TrelloNavigation.axaml`](src/BMachine.UI/Styles/TrelloNavigation.axaml).

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
