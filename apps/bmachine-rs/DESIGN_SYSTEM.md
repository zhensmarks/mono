# BMachine — Design System (Port Rust/Slint)

> **Catatan restorasi (26 Nov 2026).** File ini sempat tertimpa Build B
> (Explorer) sehingga hanya berisi §20 (237 baris); isi asli §§1–19
> (1378 baris) hilang dari disk tanpa backup. §§1–19 di bawah ini
> **direkonstruksi** dari kode (`ui/*.slint`, `src/main.rs`), ringkasan
> sesi, `ANALISIS_BMACHINE.md`, `EXPLORER_SPEC.md`, dan dua dokumen
> abeng (`BATCH_DASHBOARD.md`, `BMACHINE_ALUR_APLIKASI.md`). Bagian
> yang sumbernya tidak verbatim ditandai **[rekonstruksi]** — jangan
> anggap kata-katanya sama persis dengan aslinya; aturannya sendiri
> diverifikasi dari kode (kode mengalahkan ingatan). §20 di bawah
> utuh dari Build B. 4 edit Build B (§14, §14.1, §18.2) sudah
> diterapkan ulang di tempatnya.
>
> Prinsip: SATU sumber kebenaran visual = `ui/design.slint`
> (token + komponen bersama). Jangan hardcode warna/radius/font
> di view — pakai token. Aturan abeng yang MENGIKAT ditandai.



## 1. Tipografi

Satu-satunya font UI adalah **Inter** (Regular / Medium / SemiBold /
Bold / ExtraBold — dibundel di aplikasi C#; di port Slint dipakai
font sistem sampai font dibundel, lihat §14). **JetBrains Mono**
untuk angka/mono (nomor baris, kode, path). Black (900) dilarang di
UI — hanya untuk logo. Token ukuran di `Design` (`ui/design.slint`):

| Token | Ukuran | Peran |
|---|---|---|
| `font-caption` | 11px | caption, teks kecil, footer |
| `font-button` | 12px | teks tombol, menu, item daftar |
| `font-body` | 13px | isi / body |
| `font-h3` | 14px | judul kecil (h3) |
| `font-h2` | 18px | judul sedang (h2) |
| `font-h1` | 24px | judul besar (h1) |

Kelas teks (dari C#, dipertahankan di port):

- **h1** — 24px, Bold, tracking 0.3.
- **h2** — 18px, SemiBold.
- **h3** — 14px, SemiBold.
- **body** — 13px, Normal, warna sekunder.
- **caption** — 11px, Medium, `#71717A`.
- **eyebrow** — 10.5px, SemiBold, mono, tracking 1.2, `#52525B`.
- **mono** — 12px, JetBrains Mono.
- **navLabel** — 12px, ExtraBold, tracking 1 — hanya untuk label
  tab navigasi. ExtraBold dipakai di navLabel dan label StatWidget.
- Label StatWidget — 13px, weight 800 (ExtraBold, sama seperti navLabel),
  letter-spacing 1px, warna sekunder (lihat §8).

### 1.1 Sistem tipografi — SATU sistem di semua layar [MENGIKAT — abeng, 26 Nov 16:13]

Di aplikasi C# font-nya "ngacak, tidak simetris" — tombol di layar
ini tampilannya beda dengan tombol di layar itu; kadang informasi
PENTING justru pakai font aneh dengan ukuran kecil. Aturan port
ini MENGIKAT dan tidak boleh dilanggar:

1. **Satu peran = satu gaya.** Setiap peran teks (tombol, isi,
   judul, info penting, caption) punya SATU gaya tetap yang cocok
   dengan fungsinya — dipakai sama di semua layar.
2. **Tombol yang sama = tipografi yang sama.** Tombol yang sama
   di layar mana pun harus punya font / ukuran / weight yang sama.
3. **Informasi penting HARUS menonjol.** Ukuran memadai, weight
   jelas — tidak boleh kecil atau pakai font aneh.
4. Inkonsistensi C# JANGAN dibawa ke port. Kalau menemukan layar
   yang tipografinya menyimpang, perbaiki ke sistem ini — jangan
   meniru penyimpangannya.

### 1.2 Aturan pemilih font (Settings) [MENGIKAT — abeng, 26 Nov 16:11]

Font itu tricky: ada yang tebal, ada yang tidak, ada yang extra
tebal — kalau bebas, tampilannya jadi aneh. Karena itu:

1. Pemilih font HANYA menawarkan font dengan **keluarga weight
   LENGKAP** (Regular / Medium / SemiBold / Bold / ExtraBold),
   seperti Inter dan JetBrains Mono.
2. Mengganti font hanya mengganti **FAMILY** — weight desain
   (400 / 600 / 700 / 800) tetap dipetakan ke weight yang sama
   di font pilihan, tidak pernah diubah bebas.
3. Tampilkan preview tiap font dengan weight-nya (regular +
   bold + extrabold) di Settings.
4. Jangan pernah biarkan renderer mensintesis weight yang hilang.

[rekonstruksi — §1.1/§1.2 diringkas dari arahan abeng 26 Nov 16:11
dan 16:13; angka kelas teks dari `DESIGN_SYSTEM.md` C# dan token
`ui/design.slint`.]

## 2. Warna

Tema gelap = tema utama (sama seperti `DarkTheme.axaml` C#).
Semua token ada di `Design` (`ui/design.slint`) — baca kode untuk
nilai pastinya. Tema terang ada di C# tapi **belum diimplementasi
di port** (TODO, lihat §14).

### 2.1 Dasar (gelap)

| Token | Nilai | Peran |
|---|---|---|
| `app-bg` | `#121212` | latar aplikasi |
| `card-bg` | `#1A1C20` | latar kartu |
| `surface-elevated` | `#202226` | permukaan terangkat (dialog, panel) |
| `separator` | `#26282C` | garis pemisah / border kartu |
| `text-primary` | `#FFFFFF` | teks utama |
| `text-secondary` | `#999999` | teks sekunder |
| `text-muted` | `#71717A` | teks redup / caption |
| `accent` | `#3b82f6` | aksen / biru utama |
| `accent-subtle` | `#1F2B40` | latar aksen samar (tab aktif, chip terpilih) |
| `bg-hover` | `#22272B` | latar saat hover |
| `bg-input` | `#0C0C0E` | latar input |
| `border-default` | `#232326` | border default |
| `border-hover` | `#3F3F46` | border saat hover |
| `border-focus` | `#4B4B52` | border saat fokus |
| `ghost-hover` | `#28282D` | latar ghost saat hover |
| `ghost-pressed` | `#313136` | latar ghost saat ditekan |
| `toggle-off-bg` | `#2A2A2A` | latar toggle mati |

### 2.2 Status

| Token | Nilai |
|---|---|
| `status-danger` | `#ef4444` |
| `status-success` | `#4ade80` |
| `status-warning` | `#f59e0b` |

### 2.3 Kategori kartu Trello

| Token | Nilai | Kategori |
|---|---|---|
| `cat-editing` | `#3b82f6` | EDITING |
| `cat-revision` | `#f97316` | REVISION |
| `cat-late` | `#ef4444` | LATE |
| `cat-points` | `#2dd4bf` | POINTS |

### 2.4 Tombol

| Token | Nilai |
|---|---|
| `btn-primary-bg` / `-hover` / `-pressed` | `#EDEDED` / `#FFFFFF` / `#D4D4D4` |
| `btn-primary-fg` | `#111111` |
| `btn-secondary-bg` / `-hover` / `-pressed` | `#1C1C1F` / `#27272B` / `#222226` |
| `btn-secondary-fg` | `#E4E4E7` |
| `btn-secondary-border` | `#3F3F46` |
| `btn-danger-bg` / `-hover` | `#1C0F0F` / `#2A1010` |
| `btn-danger-fg` | `#A05252` |
| `btn-danger-border` | `#5A2020` |

### 2.5 Warna modul (C#)

ModuleBlue bg `#1B2B45` fg `#7FB4FF`; ModuleTeal bg `#13302F`
fg `#5EEAD4`; ModuleViolet bg `#221F38` fg `#C3AEFF`. Dipakai
untuk aksen modul (lihat §19).

### 2.6 Tema terang (C# — belum di port)

Latar `#F5F5F5`, kartu `#FFFFFF`, separator `#E5E7EB`, teks
`#121212` / `#666666`. Token terang BELUM ada di `Design` —
bila mengimplementasikan tema terang, tambahkan token terang
di `design.slint` (bukan hardcode di view).

## 3. Radius & Spacing

| Token | Nilai |
|---|---|
| `radius-xxs` | 2px |
| `radius-xs` | 4px |
| `radius-sm` | 6px |
| `radius-md` | 8px |
| `radius-lg` | 10px |
| `radius-xl` | 12px |
| `radius-pill` | 999px |

| Token | Nilai |
|---|---|
| `space-xs` | 4px |
| `space-sm` | 8px |
| `space-md` | 12px |
| `space-lg` | 16px |
| `space-xl` | 20px |

Aturan pakai:

- Tombol: radius 6 (`radius-sm`).
- Kartu: radius 10 (`radius-lg`); kartu kecil radius 8.
- StatWidget: radius 12 (`radius-xl`).
- Nav pill: radius 8; tab di dalamnya radius 6.
- Toggle: track radius 12, thumb radius 9.
- Chip: radius 4 (`radius-xs`).
- Padding isi kartu: 16px; kartu kecil 12px.
- Jarak antar kartu dashboard: 12px (margin 6 tiap sisi).

Mode densitas C# (Compact 24/11, Normal 36/12.5, Comfortable
40/13.5, dari token Density, tersimpan di `Settings.UiDensity`)
belum diimplementasikan di port — bila ditambahkan, ikuti pola
yang sama (satu token, bukan angka tersebar). [rekonstruksi]

## 4. Btn — Tombol

Komponen `Btn` (`ui/design.slint`). 5 varian — `variant`:
`"primary" | "secondary" | "ghost" | "danger" | "accent"`.
Tinggi 36px, radius 6, font 12 SemiBold, padding horizontal 16px.
Varian kecil (`small: true`): font 11px, padding 10px — untuk
tombol script bernama di footer BATCH (5 tombol harus muat di
lebar 520px). Skala fluid lewat properti `scale` (1 = ukuran
desain, diberi pemakai via `ui-scale`).

| Varian | Latar | Teks | Border |
|---|---|---|---|
| primary | `#EDEDED` (hover `#FFFFFF`, tekan `#D4D4D4`) | `#111111` | tanpa border |
| secondary | `#1C1C1F` (hover `#27272B`, tekan `#222226`) | `#E4E4E7` | 1px `#3F3F46` |
| ghost | transparan (hover `#28282D`, tekan `#313136`) | `#E4E4E7` | tanpa border |
| danger | `#1C0F0F` (hover `#2A1010`) | `#A05252` | 1px `#5A2020` |
| accent | latar kartu | warna aksen | 1px aksen |

Aturan:

- **Setiap Button WAJIB punya class (varian).** Tidak ada tombol
  tanpa varian.
- WAJIB punya hover + pressed + focus. **Tidak ada tombol mati**
  — tombol yang belum berfungsi jangan ditampilkan, atau tampilkan
  sebagai toast yang menjelaskan (lihat §10.1).
- Teks tombol tidak wrap (`NoWrap`); label mewarisi warna
  Foreground tombol.
- Disabled = opacity 0.4.
- Transisi background/border 150ms (`anim-normal`).
- Tekan (pressed) boleh scale 0.97 — satu-satunya transform yang
  diizinkan (lihat §10.3).

## 5. TextField & Toggle

### 5.1 TextField

Komponen `TextField` (`ui/design.slint`). Latar `bg-input`
(`#0C0C0E`), border default `#232326`, radius 6, tinggi 36px,
padding kiri 10px, font 13. Border berubah saat fokus
(`border-focus` `#4B4B52`); hover border `#3F3F46`, latar
`#111113`. Placeholder warna muted, hilang saat ada teks atau
fokus. Seleksi teks: foreground putih, background aksen.
Callback: `accepted(string)` (Enter) dan `edited(string)`.

### 5.2 Toggle

Komponen `Toggle` (`ui/design.slint`) — SATU komponen untuk semua
saklar on/off di aplikasi (Settings, flyout profil via `FlyoutItem`,
Explorer Settings, Toolbox Auto-pair). Gaya dipilih di Settings →
Appearance → "Gaya toggle", DB `Settings.ToggleStyle`
(`"classic"` | `"pill"` | `"outline"`, tak dikenal → `"classic"`),
berlaku ke semua Toggle app-wide, live tanpa restart (Rust:
`valid_toggle_style()` di `src/theme.rs`, load di startup +
`on_toggle_settings_changed` simpan DB + dorong ke ToolboxWindow
— satu-satunya window sekunder yang berisi Toggle; Toggle di window
utama ikut via binding `app-toggle-style`).

- **Klasik** (bawaan): track 44×24, radius 12; thumb 18px, radius 9,
  putih. Hidup = latar aksen; mati = latar `toggle-off-bg`
  (`#2A2A2A`).
- **Pill geser** (ref `~/workspace/pet-refs/toggle-ref.gif`): pill
  hitam lebar 64×24 (`#111111`), knob putih 30×18 berisi teks ON/OFF
  (hitam, 9px, ExtraBold) yang ikut geser; sisi track yang kosong
  menampilkan label satunya (`#666666`). Knob ON di kiri, OFF di
  kanan.
- **Outline**: track 44×24 transparan + border 2px (aksen saat hidup,
  `#555555` saat mati); thumb lingkaran solid 16px (aksen saat hidup,
  `#888888` saat mati).

Animasi latar + posisi thumb 250ms (`anim-slow`, ease-in-out) di
semua gaya. Callback `toggled(bool)`.

### 5.3 CheckBox [rekonstruksi]

CheckBox C#: 16×16, radius 4. Belum ada komponen CheckBox di
port — bila dibutuhkan, buat komponen baru di `design.slint`
dengan ukuran yang sama (bukan Toggle yang dikecilkan).

## 6. Card — Kartu / Bento

Komponen `Card` (`ui/design.slint`). Latar `card-bg`, border
separator 1px, radius 10, **tanpa shadow/glow**. Isi diberi
padding oleh pemakai (biasanya `VerticalLayout` padding 16).
Kartu kecil (`cardSm` di C#): radius 8, padding 12.

Aturan:

- Satu garis per batas: bila dua garis berjarak < 6px, hapus
  yang kanan.
- Separator selalu lewat komponen `HSeparator`, jangan gambar
  persegi 1px manual.
- Warna garis harus opaque (alpha 00 merusak tema Classic).
- Judul kartu boleh wrap; isi kartu tidak boleh meluber.

## 7. NavTab — Tab Navigasi

Komponen `NavTab` (`ui/design.slint`). **Lebar mengikuti isi**
(label + padding horizontal 14px × scale) — jangan pernah
set lebar eksplisit di tab. Tinggi 32px × scale. Label
ExtraBold 12px, letter-spacing 1px, selalu di tengah (horizontal
+ vertikal).

Gaya aktif (gaya Trello, contoh abeng 26 Nov 14:18):

- Latar `accent-subtle` (biru gelap samar) + teks warna aksen.
- Garis indikator aksen 20×3px, diposisikan **ABSOLUT** di bawah
  tengah tab (`x = (parent.width − 20px×scale) / 2`,
  `y = parent.height − 5px×scale`) — di luar alur layout teks,
  supaya label tetap di tengah sempurna.
- Hover (tidak aktif) = latar `bg-hover`, teks tetap biasa
  (tidak pernah aksen saat hover).

Nav pill (wadah tab): kartu radius 8, padding 4; pill **mengikuti
isi** — lebar pill = `preferred-width` layout dalamnya (tombol
profil + separator + tab + padding 6×scale). Pill ikut membesar
dengan `ui-scale`. Nav berisi **HOME + BATCH saja** (LOCKER
dihapus 26 Nov 13:50 atas permintaan abeng; Settings, Explorer,
dan Modul BUKAN tab nav — lihat §18).

**Aturan ikon+teks (27 Nov 2026, perbaikan keluhan abeng):**
`HorizontalLayout { alignment: center }` hanya menengahkan sumbu
horizontal — SvgIcon ber-height eksplisit nempel ke ATAS baris,
sedangkan Text di-stretch dan menengah sendiri, jadi ikon selalu
~8px di atas teks. WAJIB: bungkus tiap SvgIcon di baris ikon+teks
dengan `VerticalLayout { alignment: center; ... }`. Berlaku di
semua tampilan (nav, tombol, search field, baris Settings/panel,
judul window, item radial).

**Paket ikon (28 Nov 2026):** `SvgIcon` tidak lagi menyimpan path
di `.slint` — path tiap ikon (45 nama, urutan `icons::ALL_ICON_NAMES`)
diambil dari `Design.icon-paths`, diisi Rust per paket aktif
(`theme::push_icons`, DB `Settings.IconPack`, default `"lucide"`).
Paket: Lucide (bawaan) + Tabler, Phosphor, Heroicons, Feather,
Material Symbols (semua open-source, path dinormalisasi ke viewBox
24×24; Phosphor + Material fill-based → `Design.icon-fill`, sisanya
stroke). Paket yang tidak punya padanan satu ikon fallback ke Lucide
(9 kasus, dicatat di header `src/icons_data.rs`). Pilih di Settings →
Appearance ("Paket ikon", + pratinjau 4 ikon), berlaku live tanpa
restart ke SEMUA window (sync Design menyalin ketiga properti ikon;
window sekunder ikut saat dibuka + saat ganti paket).

**Nav dinamis (abeng 27 Nov 18:46):** tab terpilih tampil ikon +
teks; tab tidak terpilih hanya ikon saja (lebar label dianimasikan
200ms, pill mengikuti preferred-width). **Flyout profil (abeng
27 Nov 18:48):** menempel tombol avatar — `x: 20px*scale`,
`y: chrome + 60px*scale` (bawah tombol + jarak 8px*scale), bukan
di tengah layar.

## 8. StatWidget — Kartu Statistik Dashboard

Komponen `StatWidget` (`ui/design.slint`). Gaya C# (pilihan abeng
26 Nov 14:24, opsi C: tanpa sub-teks, tanpa ikon huruf).

**Struktur dua lapis** (supaya garis aksen atas mengikuti
lengkung sudut kartu):

- Lapisan luar = warna aksen, radius 12.
- Lapisan dalam = permukaan kartu, digeser ke bawah 2px × scale,
  tinggi = `parent.height − 2px × scale`, radius 12, border
  separator (jadi aksen saat hover). Aksen hanya terlihat
  sebagai garis 2px di tepi atas; **semua sudut tetap rounded**.

**Isi** (di dalam lapisan dalam, padding 20px × scale):

- Atas: dot warna aksen (glyph "●", 8px × scale) + label
  (13px × scale, weight 600, warna sekunder, letter-spacing
  1px × scale) + spacer + panah "→" (16px × scale, muted).
- Bawah: nilai BESAR + suffix satuan (warna aksen).
  **Rata bawah** — bawah suffix sejajar dengan bawah angka
  (bukan rata tengah). Keduanya diposisikan absolut di dalam
  Rectangle setinggi `val-size × 1.25`: nilai di x 0, y 0;
  suffix di `x = val.width + 8px × scale`,
  `y = val-size − suffix-size`.
- **Ukuran font nilai SAMA untuk semua kartu**: `val-size =
  root.width × 0.18` (proporsional lebar kartu, bukan per
  kartu). Suffix = 25% dari font nilai. Jadi "1.280 POIN"
  tetap muat di kartu 234px (520×600) dan tetap besar di
  kartu 614px (1280×800); tidak ada angka yang ukurannya beda.

**Latar widget (bisa dipilih user — setting `widget-bg`)**:
`"doodle" | "dots" | "impulse" | "none"`. Default `"doodle"`.
Semua StatWidget membaca `bg: root.widget-bg` dari AppWindow,
jadi ganti satu tempat = ganti semua kartu. Tambah varian =
tambah satu wadah + satu nilai.

- `"doodle"`: plus kiri atas & kiri bawah, lingkaran outline
  kanan atas (56px) & kanan bawah (28px), 3 titik kecil —
  semua warna aksen, opacity 0.15–0.22.
- `"dots"`: dua baris titik 3px (opacity 0.14) di tepi atas
  (y 14px) dan bawah (y `parent.height − 17px`).
- `"impulse"`: 14 titik 4px (opacity 0.25) membentuk gelombang
  pulsa di paruh bawah kartu (`y = parent.height × 0.72 −
  wave[i] × 52px`). Dekorasi, bukan grafik data.
- `"none"`: polos.

**Gaya latar widget (permukaan kartu — setting `widget-style`,
bagian dari tema, 27 Nov 2026):** `"solid" | "gradient" |
"glass" | "outline" | "gauge"`. Default `"solid"`. BEDA dengan pola latar
di atas: pola = motif dekorasi di ATAS kartu; gaya = permukaan
kartunya sendiri. Semua StatWidget membaca `bg-style:
root.widget-style` dari AppWindow, jadi ganti satu tempat =
ganti semua kartu. Per preset (lihat §22): BMachine Dark/Light +
Armor = solid, Midnight OLED = outline, Catppuccin Mocha +
Spider = gradient, Emerald Studio + Gwen = glass. Pilih manual
di Settings → Appearance → "Gaya latar widget" (override →
preset jadi `custom`); tersimpan di SQLite key
`Settings.WidgetStyle`. AUDIT C#: C# TIDAK punya gaya latar
widget — `AppearancePalette.cs` hanya warna (Primary/Secondary/
Background/LightBackground + 4 warna widget), StatWidget C#
selalu permukaan `CardBackgroundBrush` + garis aksen; keempat
gaya ini murni invensi port, konsisten dengan design system.

- `"solid"`: permukaan `card-bg`, border separator, garis aksen
  2px di tepi atas (tampilan C# asli).
- `"gradient"`: permukaan `card-bg` + overlay gradien vertikal
  tint aksen (alpha 0,22 di atas → 0 di 65% tinggi), garis aksen
  atas tetap ada.
- `"glass"`: permukaan `surface-elevated` (lebih terang) +
  border tint aksen (alpha 0,45), garis aksen atas tetap ada.
- `"outline"`: transparan (latar app) + border aksen penuh
  1,5px, TANPA garis aksen atas (lapisan luar dibuat
  transparan, lapisan dalam y=0 setinggi penuh).
- `"gauge"` (template tambahan, abeng 27 Nov 21:15, ref
  `widget-template-ref.jpg`): layout kartu BERUBAH — bukan
  cuma permukaan. Atas: dot + label + panah (sama). Tengah:
  arc elips bercahaya (viewbox 100×42, dari (6,40) ke (94,40)
  lewat puncak (50,12)) + dot di ujung busur terisi. Bawah:
  caption ("Jumlah card" / "Total poin") + angka besar di
  TENGAH (62% dari ukuran angka biasa) + badge pill status.
  Arc = nilai widget / nilai TERBESAR keempat widget (0..1,
  `gauge-ratio`, dihitung Rust `update_gauge_ratios` di
  `main.rs` tiap 2 detik lewat timer sinkronisasi bar — sama
  seperti salinan stat ke bar). Badge: ≥0,75 "Tinggi", ≥0,35
  "Sedang", else "Rendah". Pola latar (doodle/dots/impulse)
  disembunyikan di mode gauge (arc adalah dekorasinya);
  garis aksen atas juga hilang (border aksen alpha 0,35,
  hover → aksen penuh). Warna mengikuti aksen widget +
  token tema (`card-bg` / `text-*`) — gelap/terang ikut tema
  aktif, BUKAN kartu gelap tetap seperti ref. Glow = 3 lapis
  stroke (9px alpha 0,12 + 5px alpha 0,22 + 2,5px aksen)
  + wash `@radial-gradient` aksen alpha 0,16. Busur terisi =
  busur penuh yang di-clip selebar titik ujung (x monoton
  sepanjang busur). GOTCHA Slint: `Path` default `fit:
  contain` (letterbox — busur menyusut ke tengah, dot/clip
  tidak sejajar); keempat Path gauge WAJIB `fit: fill`.
  Pratinjau di Settings memakai `gauge-ratio: 0,72` tetap.
  Pilih manual di Settings → Appearance → "Gaya latar
  widget" (chip ke-5, "Gauge"); validator
  `valid_widget_style` di `theme.rs` menerima "gauge".

**Hover**: border dalam jadi warna aksen. **Klik**: callback
`clicked()` — membuka daftar kartu Fase 3 (lihat §15).
Skala fluid lewat properti `scale` (diberi AppWindow via
`card-scale`, lihat §11).

Kartu dashboard: EDITING 24 "CARD" (biru) · REVISION 7 "CARD"
(oranye) · LATE 3 "CARD" (merah) · POINTS 1.280 "POIN" (teal).
[Catatan: angka hero (24/7) vs daftar Fase 3 (6/5) belum
diselaraskan — pertanyaan terbuka untuk abeng, lihat §19.]

## 9. IconBtn, Chip, HSeparator

### 9.1 IconBtn

Komponen `IconBtn` (`ui/design.slint`). Tombol ikon persegi
untuk toolbar & baris aksi BATCH. Varian `"secondary"` (latar
gelap + border), `"ghost"` (transparan, hover saja), `"danger"`
(latar merah gelap + teks merah, untuk hapus). Glyph = satu
karakter teks (bukan emoji) — mis. "＋", "↻", "×". Ukuran sisi
lewat properti `size` (default 32px); samakan dengan tinggi
baris (36px) bila bersebelahan dengan Btn. Font glyph 16px
× scale, weight 700.

### 9.2 Chip

Komponen `Chip` (`ui/design.slint`). Label kecil: tinggi 18px,
latar `bg-hover`, border warna label (properti `accent`), radius
4, font 10px weight 600 warna aksen. Lebar diatur pemakai.
Dipakai untuk chip "(pending)" di header BATCH (oranye) dan
indikator lain.

### 9.3 HSeparator

Komponen `HSeparator` (`ui/design.slint`). Garis horizontal
1px, warna `separator`. Satu-satunya cara membuat garis
pemisah — jangan gambar persegi 1px manual.

## 10. Interaksi

Aturan interaksi ini MENGIKAT — perbaikan atas C# (di C# banyak
interaksi yang kurang; port ini harus benar-benar interaktif).
[rekonstruksi — urutan/nomor sub-bagian direkonstruksi dari kode
dan arahan abeng; §10.7 dan §10.11 sesuai acuan silang di kode
dan §20.]

### 10.1 Tidak ada tombol mati

Setiap tombol / kartu / baris yang tampil HARUS berbuat sesuatu
saat diklik. Tombol yang fungsinya belum dibangun jangan
ditampilkan sebagai tombol aktif — tampilkan toast yang
menjelaskan ("akan hadir …") atau sembunyikan. Jangan pernah
tampilkan kontrol yang tidak merespons.

### 10.2 Hover / aktif / fokus / disabled

- Hover = perubahan latar / border (transisi 120–150ms), tanpa
  scale / rotate.
- Aktif (terpilih) = latar `accent-subtle` + indikator aksen
  singkat, bukan blok solid.
- Fokus = border `#4B4B52` (border-focus).
- Disabled = opacity 0.4.
- Hover pada tab nonaktif tetap abu-abu — tidak pernah aksen.

### 10.3 Transisi

Durasi token: `anim-fast` 120ms, `anim-normal` 150ms,
`anim-slow` 250ms. Hover / aktif / fokus = transisi latar atau
border 0.12–0.15 detik. Satu-satunya transform yang diizinkan:
tombol saat ditekan scale 0.97. Tidak ada scale / rotate lain.

### 10.4 Toast — umpan balik setiap aksi

Setiap aksi menampilkan toast (teks singkat di bawah layar).
Toast hilang sendiri setelah 2,5 detik (Timer di AppWindow).
Toast dipakai untuk: konfirmasi aksi, error (mis. "Manifest
belum ada — jalankan Fase 1 dulu"), dan penjelasan tombol yang
belum dibangun. Jangan diam saat user mengklik sesuatu.

### 10.5 Menu konteks

Semua permukaan teks memakai menu konteks aplikasi sendiri
(bukan menu default yang berisi item bahasa asing). Menu
konteks BATCH: New Subfolder…, Replace (Auto), Copy Path,
Delete (lihat §16). Menu konteks Explorer: lihat §20.

### 10.6 Scrollbar tersembunyi

Scrollbar disembunyikan secara global (seperti C#). Konten
yang panjang di-scroll dengan roda / geser, tanpa bilah
scroll yang terlihat.

### 10.7 Shortcut keyboard

- **Ctrl+,** = buka Settings (overlay).
- **Ctrl+L** = buka Log Panel dengan tab EXPLORER
  (arahan abeng 26 Nov 16:22 — MENGALAHKAN bacaan kode lama
  yang bilang Ctrl+L = Focus Address Bar Explorer).
- **Esc** = tutup popup / overlay, dengan prioritas (lihat
  §10.10 dan §20.6 untuk Explorer).
- **Ctrl+C / V / X / A** di menu konteks (copy / paste /
  cut / select-all).
- Shortcut Explorer selengkapnya: lihat §20.6.
- Shortcut bisa direkam ulang di C# (Explorer Settings);
  di port, nilai default yang dipakai adalah yang di §20.6.

### 10.8 Satu garis per batas

Bila dua garis berjarak < 6px, hapus yang kanan. Separator
selalu lewat komponen `HSeparator` (§9.3), bukan persegi
manual. Warna garis harus opaque.

### 10.9 Wrap & ellipsis

- Judul kartu boleh wrap.
- Item dropdown tidak pernah wrap (ellipsis).
- Teks tombol tidak wrap.
- Angka statistik tidak wrap.

### 10.10 Dialog & Esc

Dialog konfirmasi (mis. hapus permanen, duplikasi) memakai
kartu dengan tombol yang tingginya MinHeight 36 (bukan Height
tetap) supaya kedua tombol punya area isi yang sama. Esc
menutup dialog dulu, baru overlay. Urutan prioritas Esc di
Explorer: dialog → tab-ctx → ctx → rename → addr-edit →
search → buffer type-ahead → tutup overlay (§20.6).

### 10.11 Klik kanan = kembali / tutup [MENGIKAT — abeng, 26 Nov 16:58]

Abeng menanamkan gesture klik kanan untuk kembali / menutup
di aplikasi C#, dan gesture ini HARUS ada di port:

1. **DashboardView (embedded view)** — klik kanan = kembali
   (`NavigateBack`). Di port: klik kanan di daftar / detail
   Fase 3 kembali ke dashboard (detail → daftar, daftar →
   grid). Implementasi: `TouchArea` latar + `PointerEventButton.right`
   (contoh: `list-back-touch` di `ui/main.slint`, `modul-back-touch`
   / `toolbox-back-touch` di `ui/modul.slint`).
2. **CardListWindow** — klik kanan GANDA (2× dalam 500ms) =
   tutup window. Di port: berlaku di daftar kartu Fase 3
   (klik kanan ganda = kembali ke dashboard).
3. **CardDetailPanelHost** — klik kanan di panel detail =
   tutup detail + batal pilih kartu (kembali ke daftar);
   klik kanan di sub-panel (komentar / checklist / pindah /
   lampiran) = tutup sub-panel, kembali ke panel detail.
   Di port: klik kanan di detail Fase 3 menutup detail ke
   daftar; klik kanan di panel komentar menutup panel itu
   (`show-comments` di AppWindow, direset true tiap kartu
   dibuka).
4. **ImageLightboxWindow** — klik kanan di latar / gambar =
   tutup. Di port: semua tampilan gambar layar penuh di
   masa depan menutup dengan klik kanan.
5. **Radial Menu (Fase 4)** — pemicu global: tombol mouse
   yang bisa dikonfigurasi (default Shift + Middle Mouse;
   indeks tombol 0=Kiri, 1=Kanan, 2=Tengah, 3=X1, 4=X2) +
   modifier, dengan mode rekam; roda mouse juga di-hook.
   Ini spesifikasi pemicu Radial Menu — JANGAN bangun hook
   system-wide sekarang (Fase 6 / butuh izin).
6. **Explorer overlay** — klik kanan di latar overlay =
   tutup overlay (`explorer-back-touch`, TouchArea anak
   pertama root). BATCH dikecualikan (klik kanan di BATCH
   membuka menu konteks, bukan menutup).

**TIDAK ADA tombol kembali (abeng, 26 Nov 19:48 — MENGIKAT):**
karena gesture klik kanan ini sudah ada, tombol "← Kembali ke
Dashboard" / "← Kembali" / "← Kembali ke Daftar" TIDAK BOLEH
ada di layar mana pun. Ketujuh tombol itu dihapus 26 Nov 19:48
(Toolbox, Spreadsheet, Leaderboard, Settings, daftar + detail
Trello embedded, detail window Trello) — build bersih, grep
"← Kembali" = 0, terverifikasi hilang lewat screenshot klik
sungguhan, dan klik kanan detail → daftar → dashboard tetap
jalan. Kembali / tutup HANYA via klik kanan (+ Esc). Yang tetap
ada: tombol "Tutup" di detail Trello (penutup panel, bukan
tombol kembali) dan tombol × di title bar window Trello.
Jangan tambah lagi tombol kembali di layar baru apa pun
(Fase 4+). Catatan: klik kanan SUDAH terbukti terdeteksi di
Slint (semua handler di atas terverifikasi), jadi tidak ada
lagi alasan memakai tombol kembali sebagai fallback.

## 11. Layout & Responsif

- **Grid dashboard mengisi window.** Grid 2×2 StatWidget
  mengisi seluruh window (margin samping 20px, bawah 30px) —
  bukan kotak 600×400 di tengah seperti C#.
- **Sel HARUS sama besar** — lewat ukuran eksplisit, jangan
  andalkan stretch saja (anak layout dengan preferred size
  berbeda + stretch sama = hasil akhir tidak sama). Lebar
  sel = `(root.width − 52px) / 2`; tinggi baris =
  `(root.height − 68px × ui-scale − 42px) / 2`.
- **Font fluid via `ui-scale`** (di AppWindow):
  `scale-w = ((width − 40px) / 1px) / 600`,
  `scale-h = ((height − 98px) / 1px) / 400`,
  `ui-scale = max(1, min(min(scale-w, scale-h), 2.5))` —
  1 pada / di bawah ukuran desain, ≈1.755 pada 1280×800,
  cap 2.5. Tidak pernah di bawah 1 supaya tetap terbaca.
- **`card-scale`** (teks kartu): `max(ui-scale,
  ((root.height − 68px × ui-scale − 42px) / 1px) / 400)` —
  yang terbesar dari ui-scale dan rasio tinggi baris grid
  terhadap baris desain (200px), supaya di window kecil
  yang tinggi teks tidak ketinggalan kecil.
- **Nav pill ikut membesar** dengan `ui-scale` (tinggi area
  nav 68px × ui-scale; padding 12px × ui-scale).
- **Tab bertumpuk** di dalam satu wadah Rectangle (anak-anak
  di `x: 0; y: 0; width: 100%; height: 100%`, toggle
  `visible`) — jangan pakai GridLayout untuk konten tab
  yang tumpang-tindih (kolom GridLayout mengikuti isi).
- **Tree BATCH padat**: baris folder 36px, baris anak 26px,
  font 12px — TIDAK ikut ui-scale (seperti tree C# yang padat).
- **Satu garis per batas** (§10.8); **scrollbar tersembunyi**
  (§10.6); aturan wrap §10.9.
- **Uji di dua ukuran**: 1280×800 DAN 520×600, sebelum
  mengklaim layar sudah cocok. Ukuran window desain di
  `Design.window-width/height` (1280×800); lebar minimum
  `window-min-width` 520px.

### 11.1 Pelajaran Slint (dari build 26 Nov)

- Mengatur `x`/`y` pada elemen di dalam layout = error
  compile — pakai padding / spacer / stretch.
- Rectangle TIDAK auto-size ke anaknya — tapi lebar Rectangle
  bisa diikat ke `preferred-width` layout anak (cara pill nav
  mengikuti isi).
- `opacity` pada Rectangle meredupkan anaknya juga — pakai
  Rectangle SAUDARA (bukan induk) bila hanya latar yang
  perlu transparan.
- `visible: false` TETAP memakan ruang layout di Slint.
- Aritmetika persen − panjang = error compile
  (`width: 100% - 2px` tidak bisa) — pakai `parent.height − …`.
- Array TIDAK bisa di-append dari kode .slint — hanya bisa
  DIGANTI seluruhnya (`root.x = [ … ]`, termasuk `= []`).
  Data dinamis memakai **dua array sejajar** (nama `[string]`
  + hitung `[int]`); struct array tidak bisa di-append.
- Deklarasi callback HANYA tipe
  (`callback edited(string);` benar, `callback edited(string text);`
  salah sintaks).
- Properti kustom bernama `color` pada turunan Rectangle gagal
  ("Cannot override property 'color'") — pakai nama lain
  (mis. `accent` di Chip).
- `for` di dalam `for` langsung = error sintaks — hindari
  loop bersarang; pakai loop tunggal terpisah.
- Mengindeks literal array inline (`[0.0, …][i]`) = error
  sintaks — definisikan array sebagai properti komponen dulu,
  baru indeks propertinya.
- `clip: true` TIDAK memotong anak ke rounded rect di software
  renderer — pakai struktur dua lapis (lihat §8).
- `string.length` TIDAK bisa diakses — ukuran font per nilai
  harus diatur eksplisit.
- Mengacu `width` Text saudara dari dalam layout menghasilkan
  ~0 — jangan posisikan suffix via `val.width` dari dalam
  layout (berfungsi bila kedua Text diposisikan absolut
  sebagai saudara di Rectangle biasa — itu implementasi
  suffix saat ini).
- `xdotool type` MERUSAK teks (paste via xclip + Ctrl+V;
  kirim KP_Enter terpisah dengan jeda).
- Jangan rantai `cargo build && run &` dalam satu perintah
  background — build dulu di foreground, lalu jalankan di
  perintah terpisah (pernah menghasilkan frame hitam kecil
  karena race).

## 12. Cara Menambah Layar Baru [rekonstruksi]

1. Buat komponen baru di file `.slint` yang sesuai (atau file
   baru bila layarnya besar — mis. `ui/explorer.slint`), import
   token/komponen dari `design.slint`. Jangan hardcode warna /
   radius / font.
2. Bila layar tampil sebagai overlay layar penuh, daftarkan
   nilainya di `overlay-view` AppWindow (`ui/main.slint`) dan
   sembunyikan pill nav saat overlay terbuka (lihat §18).
3. Sediakan tombol kembali yang jelas (ghost Btn "← Kembali…")
   DAN gesture klik kanan = kembali (§10.11) bila layarnya
   turunan dari embedded view.
4. Ikuti sistem tipografi §1.1 — peran teks yang sama =
   gaya yang sama; info penting menonjol.
5. Verifikasi screenshot di 1280×800 DAN 520×600; baca kembali
   screenshot dan perbaiki yang salah sebelum mengklaim selesai.
6. Catat layar baru di §14 (status penerapan) dan, bila ada
   aturan baru, di bagian yang sesuai di file ini.

## 13. Yang Tidak Boleh

- Jangan hardcode warna / radius / font di view — pakai token
  `Design`.
- Jangan pakai emoji sebagai ikon UI. Ikon = glyph teks /
  bentuk (Tabler outline di C#; monogram 2 huruf di port
  untuk kartu modul).
- Jangan buat Button tanpa varian (class).
- Jangan atur `MaxDropDownHeight` per view (aturan global
  C#: 240, lebar list min 160) — bila ada dropdown di port,
  satu aturan global.
- Jangan gambar separator dengan persegi 1px manual — pakai
  `HSeparator`.
- Jangan biarkan item dropdown wrap — pakai ellipsis.
- Jangan pakai weight Black di UI (hanya logo).
- Jangan pakai `visible: false` untuk menyembunyikan elemen
  dari layout — elemen itu tetap memakan ruang (lihat §11.1).
- Jangan append array dari kode .slint — ganti seluruh array
  (lihat §11.1).
- Jangan meniru inkonsistensi C# (font ngacak, tombol beda
  gaya per layar) — ikuti §1.1.
- Jangan mengklaim layar selesai tanpa screenshot yang dibaca
  kembali di 1280×800 dan 520×600.
- Jangan mengubah alur kerja BATCH / Explorer (yang harus
  teliti, tidak boleh salah sedikit pun — arahan abeng
  26 Nov 16:07). Tampilan boleh dirombak; alur kerja tidak.
- Jangan membangun Master File Browser di BATCH (ditunda
  bertahap — abeng 26 Nov 15:57; lokasinya di side panel /
  Log Panel, lihat §16 & §20.5).
- Jangan memakai `rembg` lokal untuk hapus latar — pakai
  Pixelcut (aturan workflow, dicatat di sini supaya tidak
  terulang).
- Jangan mengotak-atik file hasil build / `target/` — itu
  artefak, bukan sumber.

### 13.1 Pelajaran proses — JANGAN PERNAH timpa file dengan `muse.write` default (26 Nov 2026)

Insiden: Build B (Explorer) menambahkan §20 ke file ini dengan
`muse.write` mode default (overwrite) — akibatnya seluruh isi
§§1–19 (1378 baris) hilang dan harus direkonstruksi. Aturan
ini MENGIKAT untuk semua pekerjaan berikutnya:

1. **Jangan pernah pakai `muse.write` default-overwrite pada
   file yang sudah ada.** Untuk menambah / mengganti isi,
   pakai `muse.edit` dengan anchor unik yang pendek, atau
   `muse.write` mode `append`.
2. **Sebelum menulis, baca dulu file target.** Pastikan tahu
   isinya dan anchor yang dipakai benar-benar unik.
3. **Setelah selesai, baca kembali file hasil dan hitung
   barisnya** — pastikan tidak ada bagian yang hilang.
4. Bila perlu menulis ulang file besar, bangun dulu di file
   baru, verifikasi, baru gabungkan dengan `muse.edit`.

## 14. Status Penerapan

- [x] Token + komponen (`ui/design.slint`): Design (semua token),
  Btn, Card, HSeparator, Chip, TextField, Toggle, NavTab,
  StatWidget, IconBtn.
- [x] HOME — grid 2×2 di atas token; fluid grid + `ui-scale`;
  sel sama besar (ukuran eksplisit); StatWidget mengikuti
  aturan §8 (garis aksen atas dua lapis; latar bisa dipilih
  via `widget-bg` — 4 varian; suffix rata bawah — posisi
  absolut, `y = val-size − suffix-size`; font nilai seragam —
  `val-size = root.width × 0.18`, suffix = 25%).
- [x] NavTab mengikuti isi (pill `preferred-width`); gaya
  aktif Trello (highlight + garis indikator absolut).
- [x] LOCKER dihapus dari nav (abeng, 26 Nov 13:50) — nav
  hanya HOME + BATCH.
- [x] Fase 3 — daftar kartu + detail (lihat §15).
- [x] BATCH v3 — sesuai `BATCH_DASHBOARD.md` (lihat §16).
- [x] Settings — overlay layar penuh, panel samping berkelompok lengkap 7 menu C#: General, Appearance, Account (Trello & Sheets), Extensions (Plugins), Script Manager (Radial 8 slot & Master scripts), Explorer/Paths, About (lihat §17).
- [x] Toolbox / Spreadsheet / Leaderboard (lihat §19) — launcher Modul DIHAPUS 26 Nov (tidak ada di C#).
- [x] Log Panel (sidebar kanan, Ctrl+L — lihat §18): tab EXPLORER / LOG / MASTER / DOC; tab EXPLORER = compact (path D:\#GAWENA + 5 baris, klik → buka folder itu di overlay Explorer) + tombol "⧉ Buka Window Baru" → overlay "EXPLORER" (disambung Build B, 26 Nov — lihat §20).
- [x] Explorer (Build B / LANGKAH 3, 26 Nov — ui/explorer.slint + ui/explorer-data.slint + src/main.rs, overlay "EXPLORER", lihat §20): tab (maks 4, pin badge 30px palet 8 warna), 5 layout, sort/group via dataset terurut, search rekursif (Rust) + kolom Lokasi, type-ahead, preview .txt/.docx, operasi file + undo maks 50 + Task Monitor, ctx menu + "BUAT MASTER" / "Kirim ke BATCH", Quick Access, klik-kanan latar = tutup overlay, Esc prioritas. Terverifikasi 1280×800 (Vertical, Thumbnail, search, ctx menu, Task Monitor) dan 520×600 (sidebar/preview sembunyi, toolbar scroll horizontal).
- [x] Fase 5 — Dialog (Exit "Sign Out of Session", LAN Update manifest reader & dialog 380px, Splash screen bootstrapper 10-100% dengan 3 baris log terminal C#).
- [x] Radial Menu — Dibenahi total sesuai C#: Submenu TOOLBOX memuat 5 file skrip `_tb_*.jsx` asli (`_tb_C-REDAKSI.jsx`, `_tb_FOOTNOTE.jsx`, `_tb_IMAGE_SIZE.jsx`, `_tb_NOMORSATSET.jsx`, `_tb_SELEKSI_ALAM.jsx`), submenu SEND-* terhubung dinamis dengan tab DOC.
- [x] Log Panel & DOC — Tab redaksi DOC multi-tab (1, 2, tambah +, hapus x) + sinkronisasi state `doc_info.json`. Buffer log proses maks 1000 entri.
- [ ] Font Inter / JetBrains Mono dibundel — TODO (saat ini font sistem).
- [ ] Tema terang — TODO (token terang belum ada di `Design`).

Riwayat yang dibatalkan (jangan dibangun ulang tanpa arahan
abeng): grafik batang 7 hari ("aneh banget"), daftar 3 item
("masa dibuat detail gitu"), hero number + blob aksen ("aneh").
Subtitle delta di StatWidget dihapus (pilihan abeng: opsi C,
gaya C# — suffix satuan, lihat §8).

### 14.1 Koreksi REA (laporan `~/workspace/rea-analysis/LAPORAN_REA_BMACHINE.md`)

Koreksi K1–K11 dari analisis REA atas binari v6.5.0 (sebagian
besar selisih awal adalah drift versi — binari v6.5.0 berumur
~3 bulan dari source kini). Status di port:

- **K1** — BATCH panel OUTPUT mendukung New Subfolder… /
  Delete folder (hapus dari disk) + input inline nama folder
  baru. **DITERAPKAN** (sebelum Build B): menu konteks OUTPUT
  = New Subfolder… / Delete / Copy Path / Copy Output Path
  (tanpa Replace); input inline `newfolder-*` di kedua panel.
- **K2** — Explorer harus punya Undo stack + cut payload
  terpisah. **DITERAPKAN 26 Nov (Build B)**: undo maks 50 + cut terpisah.
- **K3** — Explorer tab + pinned tab berwarna (persist). **DITERAPKAN 26 Nov (Build B)**: pin 30px/palet 8/Change Color/Rename, persistensi `Explorer_PinnedTabs` TIDAK ada (pin sesi saja).
- **K4** — Explorer ikon sistem + thumbnail + address bar
  editable + zoom ikon. **DITERAPKAN 26 Nov (Build B)**:
  thumbnail/address-bar/natural-sort ✓, ikon sistem + zoom Ctrl+roda TIDAK (lihat §20).
- **K5** — Settings berisi editor grup Radial Menu lengkap
  (grup / anggota incl. Toolbox, urutan + preview +
  snapshot-rollback / ikon) + Apply / Reset palet Appearance.
  TODO (Fase 5).
- **K6** — Radial Menu punya submenu luar + submenu Toolbox +
  buka-via-dwell. TODO (Fase 4).
- **K7** — Panel DOC multi-tab. TODO (Fase 5).
- **K8** — Folder AppData `BMachine.v2` → `BMachine`.
  Catatan (port belum memakai AppData — Fase 6).
- **K9–K10** — Trello Vault (kredensial di OS Credential
  Store) + LAN update adalah fitur baru C#. Bukan bagian
  mockup — Fase 6.
- **K11** — Jangan pernah menyamakan port dengan v6.5.0.
  Pengingat permanen.

## 15. Fase 3 — Daftar Kartu + Detail (Trello)

Navigasi widget — CARA KERJA ASLI C# [MENGIKAT — abeng, 26 Nov 18:47]:

- **Klik BADAN widget EDITING / REVISION / LATE** → tampilan
  Trello EMBEDDED (UnifiedTrelloView C#): daftar kategori itu
  dengan 3 tab **EDITING / REVISI / SUSULAN** di atasnya; tab
  sesuai widget yang diklik dalam keadaan aktif. Pindah tab =
  ganti daftar. Back → dashboard.
- **Klik TANDA PANAH (→) di widget EDITING / REVISION / LATE**
  → daftar kategori itu sebagai WINDOW terpisah (CardListWindow
  C#): panel mengambang di tengah + title bar + tombol tutup;
  klik baris → detail seperti Fase 3. Klik kanan di window /
  Esc = tutup window (kembali ke grid, bukan ke daftar).
- **Klik BADAN widget POINTS** → tampilan Leaderboard (overlay
  `"LEADERBOARD"`, Harian/Bulanan + 5 peringkat).
- **Klik TANDA PANAH (→) di widget POINTS** → window
  Spreadsheet BMachine (overlay `"SHEETS"`).
- Panah adalah TouchArea sendiri di `StatWidget`
  (`arrow-clicked()`); klik panah TIDAK memicu klik badan.
  Tampilan widget tidak berubah (dot + label + panah + angka +
  suffix, accent line, background doodle tetap).
- TIDAK ADA launcher "Modul" di port ini (dihapus 26 Nov —
  tidak ada layar Modul di C#; lihat §19): Toolbox dibuka
  langsung dari flyout profil; Spreadsheet dari panah POINTS;
  Leaderboard dari badan POINTS.

State di AppWindow (`ui/main.slint`):

- `list-category`: `""` = grid dashboard; `"EDITING"` /
  `"REVISION"` / `"LATE"` = daftar kategori itu (POINTS tidak
  lagi membuka daftar — badan POINTS → Leaderboard).
- `trello-window-cat`: `""` = tidak ada window Trello;
  `"EDITING"` / `"REVISION"` / `"LATE"` = window kategori itu
  terbuka (`overlay-view` `"TRELLO-WINDOW"`). Window TIDAK
  memakai `list-category` (supaya menutup window kembali ke
  grid). Judul/aksen window dari properti ini (`tw-accent`).
- `editing-titles/subs/due`, `revision-titles/subs/due`,
  `late-titles/subs/due`: data daftar — SATU sumber (jangan
  duplikasi literal di tiap handler; pola sama seperti
  `kids-b-*` BATCH). Handler klik badan, tab, dan panah
  window semuanya mengisi `list-titles/subs/due` dari sini.
- `selected-card`: `""` = tidak ada detail; berisi judul kartu.
- `selected-index`: indeks kartu yang dipilih (untuk ambil
  sub / due di detail).
- `list-titles` / `list-subs` / `list-due`: tiga array sejajar
  (Slint tidak bisa append — ganti seluruh array sekaligus
  saat kategori dipilih).
- `list-accent`: warna aksen kategori yang sedang dibuka
  (EDITING biru, REVISION oranye, LATE merah, POINTS teal).
- `show-comments`: panel komentar di detail kartu (klik kanan
  di panel komentar menutup panel — C# CardDetailPanelHost;
  direset true tiap kartu dibuka).

### 15.0 Provider board — Trello asli / Trello kantor (27 Nov 2026)

Semua panggilan board terkumpul di SATU file `src/trello_api.rs`
(`TrelloApiService`, 5 endpoint: boards, lists, members/me, cards,
add comment — spesifikasi untuk tim kantor:
`~/workspace/your_files/spesifikasi-api-trello-kantor-untuk-bmachine.md`).
Pindah provider cukup ganti pengaturan di **Settings > Account >
Trello Integration**, tanpa rombak dan tanpa rebuild:

- **Base URL** — key DB baru `Trello.BaseUrl`, default
  `https://api.trello.com/1` (kosong = default; garis miring di
  akhir dibuang otomatis). API Key / Token TIDAK di-rename
  (`Trello.ApiKey` / `Trello.Token` — DB lama tetap terbaca).
- **Tes koneksi** — tombol Connect yang sama memanggil
  `verify_connection` ke base URL yang dikonfigurasi; sukses →
  Base URL + Key + Token disimpan, sync langsung jalan ke server
  baru. Simpan pengaturan akun juga menyimpan Base URL.
  Berlaku langsung tanpa restart (aturan port).
- **Link web kartu** — diambil dari `shortUrl` API
  (`editing/revision/late-urls` → `list-urls` → detail
  `card-url-override`); bila server tidak mengembalikan shortUrl,
  tombol buka-link di detail disembunyikan. Link kartu manual yang
  ditempel apa adanya dipertahankan domainnya; bila yang ditempel
  hanya ID polos, link dibangun dari origin base URL
  (`trello_api::web_url_for_card` — Trello asli → `trello.com`).
- Tombol **Get Key** hanya tampil bila base URL = Trello asli
  (Trello kantor key-nya dari server kantor sendiri).
- Checklist / lampiran / label / webhook BELUM dipakai API live
  (masih mockup di UI) — Trello kantor versi pertama cukup
  menyediakan 5 endpoint di atas.

### 15.1 Daftar kartu

Header: "← Kembali" + judul kategori + jumlah ("N kartu").
Judul header memakai label tab (EDITING / REVISI / SUSULAN —
bukan nilai internal REVISION / LATE). Di bawah header ada
bar tab NavTab **EDITING / REVISI / SUSULAN** (tab aktif =
kategori kini; klik tab = ganti daftar). Baris 48px (C#
EditingCardListView, diubah 26 Nov 19:20): checkbox seleksi
16px di kiri + NAMA kartu (13px, weight 700, elide — prioritas
utama) + badge checklist TODO/DONE di kanan (pill 62×22px, dot
+ label 9px Bold; warna persis BoolToColorConverter C# — DONE:
bg #173F2D, border #3FAF78, dot #45C98A, teks #9DE8C1; TODO:
bg transparan, border #46505C, dot #7B8794, teks #AAB4BF).
Badge = penanda apakah checklist format nama user sudah
diceklis (DONE) atau belum (TODO) — JANGAN dihilangkan.
Sub + due TIDAK tampil di baris (tetap ada di detail). Klik
checkbox = toggle seleksi (biru + ✓, tidak membuka detail);
klik baris → buka detail + toast. Klik kanan di daftar (area
kosong maupun baris) = kembali ke dashboard (§10.11).
Berlaku sama di daftar embedded dan window Trello (§15.3).
Bar status (C# UnifiedTrelloView Row 0/Row 3, ditambahkan 26 Nov
19:25 — abeng menandai yang hilang di screenshot): di ATAS tab,
dot status hijau (online) + teks "Dimuat N card" (12px Medium,
secondary; N = kartu tab aktif + kartu manual di EDITING) +
tombol + (ghost, HANYA di EDITING — Add Manual Cards) + tombol
↻ (ghost, RefreshActiveListCommand — memuat ulang daftar aktif,
tombol redup 0.4 saat memuat, toast "Trello di-refresh — N card
dimuat"). Di C# embedded bar ini aslinya ada di BAWAH (Row 3);
di port ditaruh di atas sesuai screenshot abeng. Bar ini hanya
ada di daftar embedded — CardListWindow C# tidak punya bar ini.
Daftar TETAP tampil (menyempit) saat detail maupun Add Manual
terbuka di kolom kanan — jangan sembunyikan daftar (bug 26 Nov:
`visible` daftar sempat mengecualikan `add-manual-open`).
Panel Add Manual Cards (C#): dibuka dari tombol + (EDITING
saja), header "Add Manual Cards" + subjudul + TextBox multi-
baris 200px (watermark https://trello.com/c/...) + tombol
"Import Cards" (primary, full width). Import: parse link per
baris (ID setelah /c/, nama dari slug — nama asli dari API
Trello di Fase 6), link duplikat dilewati, slot maks 5 di
mockup; toast "Ditambahkan N card manual[, M gagal.]"; panel
menutup otomatis. Kartu manual: baris 64px (nama + Url di bawah
nama — C# Url IsVisible=IsManual), badge TODO/DONE, detailnya
lihat §15.2 (chip tanggal "—", tanpa badge ID/Due, tombol
Unlink ganti Move — klik Unlink = hapus nyata via remove-manual,
toast "Kartu manual dihapus dari daftar."). Daftar
di-scroll via Flickable (C#: ScrollViewer) — 6 kartu + 1 manual
tidak muat di Card 1280×800.

Isi contoh (data nyata DMA dari Trello):

- **EDITING — 6 kartu**: "47276395_SDN RANCAIYUH 1_GUSFIARI"
  ("KELAS 2 · Pas Foto", due 28 Nov 2026),
  "48328499_SDN BANTAR KAMBING 03_YOGA" ("9 anak · 1 belum
  dikirim", 30 Nov 2026), "30816883_TK ISLAM ARAFAH 1024_NISSA"
  ("QC · Revisi", 1 Des 2026),
  "29332964_PAUD CAHAYA UMMAT 0724_NISSA" ("Revisi Alfan",
  2 Des 2026), "49109072_TK ISLAM AL GHIFARI_NISSA"
  ("Revisi Alfan", 3 Des 2026), "TK ASSALAM_FENTI"
  ("QC Alif", 4 Des 2026).
- **REVISION — 5 kartu**: "TK ASSALAM_FENTI",
  "PAUD HAUR WANGUN_FENTI", "PAUD AL FURQON_SURYA",
  "PAUD TUNAS BANGSA_SURYA", "RA ATS-TSABIT_SURYA"
  (subs "Revisi Alif / Alif / Surya / Riyan / Rahmah").
- **LATE — 3 kartu**: "PAUD TUNAS BANGSA GUNUNG PUTRI_SURYA"
  ("Telat 2 hari"), "RA ATS-TSABIT_SURYA" ("Telat 3 hari"),
  "SDN BANTAR KAMBING 03_YOGA" ("Telat 1 hari").
- **POINTS — tidak lagi membuka daftar Fase 3.** Badan widget
  POINTS → Leaderboard; panah widget POINTS → Spreadsheet
  (§15 awal). Daftar leaderboard 4 editor yang dulu di sini
  ("1. abeng" 1.280, "2. nissa" 980, "3. fenti" 760,
  "4. surya" 640) kini tampil di overlay Leaderboard (5
  peringkat: + yoga 520).

### 15.2 Detail kartu — TrelloDetailPanel (sama persis C#)

Komponen mandiri `ui/trello-detail.slint` (TrelloDetailPanel) —
satu komponen dipakai embedded (kolom kanan 400px, margin 20,
judul 24px) dan window (varian B: margin 24, judul 20px, bg
surface-elevated, lebar `tw-detail-w`). Ukuran C# tetap (tanpa
ui-scale): chip 11px SemiBold, badge meta 30px, tombol aksi 32px,
header sub-panel 18px SemiBold, label Description 13px SemiBold,
isi 12px.

Isi detail (C# CardDetailPanelHost): chip kategori + tanggal event
→ judul + × → meta WrapPanel (badge ID "ID: <DisplayId>" klik →
toast spreadsheet; badge Attachments "Attachments: 0"; badge Due;
ikon copy-link ⎘ + open-in-browser ↗ 30×30) → separator → baris
aksi: Comment (split button C# — segmen kiri "Comment" buka panel Comments INLINE di panel samping, segmen kanan segitiga solid 9×10 lebar 16px buka CommentWindow 400×600 window OS terpisah; state komentar dibagi dengan detail pembuka, Rust isi saat dibuka + tulis balik saat ditutup) + Checklist + Move (sembunyi bila manual)
+ Unlink (HANYA bila manual) → separator → label "Description" +
isi ("Edit pas foto sekolah — crop, centering kepala di guide,
logo terlihat."). Kartu manual: chip tanggal "—", tanpa badge ID /
Due, Unlink ganti Move. Kartu tanpa prefix (REVISI/LATE, TK
ASSALAM): tanpa badge ID. Badge Attachments selalu tampil ("0")
supaya panelnya terjangkau — C# menyembunyikannya bila 0 (Fase 6:
sembunyikan bila API mengembalikan 0).

Sub-panel menggantikan detail di kolom yang sama (header 18px
SemiBold + nama kartu 12px + ×; klik kanan sub-panel = kembali ke
detail): Comments (eye toggle ◉/○, 2 komentar mockup Alif 2 jam
lalu + Surya 5 jam lalu, tambah maks 3 komentar "abeng / baru
saja" via Enter / tombol →, quick-reply "Siap, diperbaiki." /
"Sudah dicek.", attach + → toast), Checklists ("EDITING abeng",
item Crop foto / Centering kepala di guide / Logo terlihat, centang
awal dari `done`, klik toggle + toast, ProgressText statis per kartu
"3/3" bila DONE / "1/3" bila TODO — mockup, "Duplicate Checklist"
→ toast), Move Card (Destination Board + Destination List, Confirm
Move validasi keduanya terisi → toast + kembali ke detail),
Attachments ("No attachments."). Semua logika mockup; API Trello
asli di Fase 6.

Navigasi (tanpa tombol kembali — §10.11): klik kanan latar
detail = tutup ke daftar; klik kanan sub-panel = kembali ke
detail; × = tutup; Esc = sub-panel → detail → daftar (window:
sub-panel → detail → tutup window). Klik tab HOME / BATCH atau
StatWidget mereset `selected-card` + `detail-panel`. Detail
POINTS = rincian poin ("Minggu ini +120" / "Bulan ini +480" /
"Total 1.280"), bukan detail kartu.

Posisi baris aksi BERGANTUNG pada judul + meta row 2 (judul 2
baris / meta row 2 ada → baris aksi lebih rendah) — saat menguji
klik, ukur dulu posisi render tombolnya (jangan pakai satu
koordinat tetap untuk semua kartu). Pelajaran 26 Nov: tombol
sempat dikira rusak pada kartu berjudul pendek (TK ASSALAM, kartu
manual, REVISI) — ternyata koordinat klik pengujinya yang salah
(baris aksi kartu 1 baris ada di y~295, bukan y~360).

### 15.3 Window Trello (dari panah widget)

`overlay-view` `"TRELLO-WINDOW"` + `trello-window-cat`
(EDITING / REVISION / LATE). Panel mengambang di tengah
(760×600, responsif: menyusut sampai 40px dari tepi di
window kecil) di atas dim hitam 60%. Title bar: "Trello —
EDITING / REVISI / SUSULAN" + jumlah kartu + tombol tutup ×.
Isi = daftar kategori itu (baris 48px, sama seperti §15.1,
tanpa `ui-scale` — ukuran window tetap); klik baris → detail
§15.2 varian B (TrelloDetailPanel yang sama, sub-panel
berfungsi). Catatan: daftar window BELUM menampilkan kartu
manual (hitung "N kartu" hanya kartu biasa) — kartu manual
hanya ada di daftar embedded; selaraskan di Fase 6 (atau saat
merapikan window).
Klik kanan di window (dim, latar daftar, maupun baris) /
tombol × / Esc = tutup window → kembali ke grid dashboard
(bukan ke daftar embedded — window tidak memakai
`list-category`). Klik kiri di dim TIDAK menutup.

### 15.4 Auto-refresh interval (28 Nov 2026 — ala C# DashboardViewModel)

- **Bukan realtime** (keputusan abeng 28 Nov 00:30 — realtime
  bikin PC lag; bottleneck = network + rate limit API + redraw
  UI, bukan bahasa). Tidak ada webhook / push / server penerima
  publik. Yang ada: timer interval seperti C#.
- **Mekanisme (C#):** `DispatcherTimer` tick 1 detik,
  `SyncTrelloStats()` cek elapsed per list terhadap intervalnya
  (`Settings.Interval.Editing/Revision/Late`, default 60 detik);
  list yang jatuh tempo di-refresh, tick di-skip bila refresh
  list itu masih berjalan (`if (!IsRefreshing)`).
  **Port:** timer Slint 1 detik (`_trello_auto_timer`,
  `src/main.rs`) + state per list `TrelloSyncState`
  (`src/trello_api.rs`: `in_flight` per list +
  `last_sync` yang di-set saat fetch DIMULAI — jadi refresh
  manual ikut me-reset timer list itu). Hanya list yang jatuh
  tempo yang di-sync (yang lain dilewat dengan string kosong).
- **Interval:** satu nilai untuk ketiga list — field Settings >
  General "Interval refresh Trello", key DB baru
  `Trello.RefreshInterval` (Int), **default 60 detik**,
  **minimum 30 detik** (di bawah itu dijepit ke 30; tidak valid /
  kosong → 60). Field itu sebelumnya mati (default "30"
  hardcode di `.slint`, tidak tersimpan, toast "Disimpan"-nya
  bohong) — mulai run ini field hidup: tampilkan nilai DB,
  ketikan mengubah properti langsung (timer membaca properti
  tiap detik — ganti interval berlaku tanpa restart), simpan
  lewat tombol Simpan General (nilai clamp ditulis balik ke
  field). Override env `BMACHINE_TRELLO_INTERVAL_SECS` HANYA
  untuk tes (tanpa clamp) — di produksi tidak diset.
- **Tick diam-diam:** tidak ada toast tiap tick, tidak
  menyentuh `trello-refreshing` (timer `.slint` 600ms akan
  toast bila flag itu disentuh — hanya refresh manual yang
  menutup flag + toast "Trello di-refresh — N card dimuat").
  Sync hanya mengganti array model (sama seperti refresh
  manual): detail kartu yang terbuka + `selected-card` +
  seleksi tidak disentuh; status bar tetap "Dimuat N card".
- **Refresh manual (tombol ↻):** tetap jalan; list yang sedang
  di-fetch auto di-skip oleh manual (fetch yang berjalan
  mengirim data segar juga), dan list yang di-claim manual
  me-reset `last_sync`-nya — tidak ada double-fire tepat
  setelah refresh manual.

## 16. BATCH — Batch Master (v3)

Sesuai `BATCH_DASHBOARD.md` (dokumen abeng, 26 Nov 15:52 —
otoritatif) dan `BMACHINE_ALUR_APLIKASI.md` §7/§10. BATCH =
Batch Master: pasangan folder SOURCE (hasil foto / PILIHAN)
→ OUTPUT (ekspor siap cetak). **Alur kerja BATCH harus
benar-benar teliti — jangan sampai salah sedikit pun**
(arahan abeng 26 Nov 16:07). Tampilan boleh dirombak; alur
kerja tidak.

### 16.1 Struktur layar

Header: judul "BATCH" + deskripsi "Pilih folder sumber, lalu
jalankan master." + tombol "+ Tambah Folder". Di bawahnya dua
panel: SOURCE (kiri) dan OUTPUT (kanan). Di bawah panel: footer
tombol script + Refresh + Clear. Tidak ada folder → drop zone
"DRAG FOLDERS HERE" (kartu ber-border, ikon + besar, "DRAG
FOLDERS HERE" 16 Bold, "Drop source folders to begin batch
processing" 12).

### 16.2 Panel SOURCE — tree isi folder sekolah

SOURCE menampilkan **TREE isi folder sekolah yang dipilih**,
bukan daftar master. Level-1 = subfolder bernomor di dalam
folder sekolah ("1. PAS FOTO" / "2. FOTO10RP_FREE SEKOLAH_GRAIS" /
"3. SOUVENIR_FREE ID CARD" / "4. SOUVENIR_FREE KARTU PELAJA" —
dataset A, folder "27_SDN RANCAIYUH I"). Header: "SOURCE
27_SDN RANCAIYUH I" + chip "(pending)" oranye (tampil selama
belum ada output yang cocok; hilang setelah BUAT MASTER
berhasil) + counter "N dipilih".

- Baris folder 36px, baris anak 26px, font 12px — TIDAK ikut
  ui-scale (tree padat seperti C#).
- Chevron ▸/▾ (klik = expand/collapse, menampilkan 3 file
  contoh dari array `kids-N`, indent dengan ikon ▤).
- Klik nama = toggle multi-select (maks 3) + highlight
  accent-subtle + toast "Path disalin: …".
- Tombol ⋯ = menu konteks: New Subfolder… / Replace (Auto) /
  Copy Path / **Delete — HAPUS DARI DISK** (toast "Dihapus
  dari disk: <nama>"; menghapus folder menomori ulang sisanya).
  Delete dari disk — ini koreksi atas analisis lama yang
  salah bilang delete hanya menghapus dari daftar; Clear
  yang hanya mengosongkan daftar.
- **"+ Tambah Folder" MENGGANTI seluruh SOURCE** (per
  `AddFolders()`): beralih antara dua dataset contoh — A
  "27_SDN RANCAIYUH I" ↔ B "28_SDN BANTAR KAMBING 03"
  (isi sendiri, mis. "1. PAS FOTO", "2. WISUDA",
  "3. ID CARD") — + toast "SOURCE diganti: …".

State (AppWindow): `src-name`, `src-folders` [string],
`kids-0..3` [string] (3 nama file per folder, "" = kosong),
`expanded-node` (string, "" = tidak ada), `selected-nodes`
[string] (multi-select, maks 3), `selected-source`,
`ctx-node` / `ctx-folder-idx` / `ctx-child-idx` /
`ctx-is-child` / `ctx-panel` ("SOURCE" | "OUTPUT").

### 16.3 Panel OUTPUT — tree sendiri, bukan cermin tampilan

OUTPUT punya **tree sendiri** (`out-folders` + `out-kids-0..3`,
expand state `expanded-out` — TIDAK dibagi dengan SOURCE).
Header: "OUTPUT 27_SDN RANCAIYUH I" + chip "(pending)" oranye
(tampil selama folder output belum ada di disk; hilang setelah
BUAT MASTER berhasil). Isi: teks "Output akan muncul di sini."
selama output belum siap; setelah BUAT MASTER berhasil, tree
OUTPUT terisi (disalin dari struktur SOURCE saat itu).
Dikosongkan kembali saat Clear. Klik panel = pilih folder
output.

Kenapa OUTPUT sama dengan SOURCE: BATCH bekerja berpasangan
SOURCE→OUTPUT — script membaca SOURCE, memakai template PSD
master, dan menulis hasilnya ke OUTPUT file demi file, sehingga
struktur OUTPUT berakhir identik dengan SOURCE (bukan karena
tampilannya disalin). "(pending)" berarti folder output belum
ada di disk; FileSystemWatcher mengisi tree begitu proses
membuatnya.

Menu konteks OUTPUT (K1 REA): New Subfolder… / Delete /
Copy Path / Copy Output Path (tanpa Replace). Input inline
nama subfolder baru (`newfolder-*`) tersedia di kedua panel.

### 16.4 Dua fase master

Indikator manifest (● merah "Manifest: belum ada" / ● hijau
"Manifest: ada") + tombol ghost "▶ Fase 1" (buat_master.py →
`has-manifest = true`, toast: template dibuka di Photoshop,
edit + SAVE) + tombol **"⧉ BUAT MASTER" — DI DALAM panel
OUTPUT** (baris aksi di bawah tree OUTPUT, di samping tombol
▤ placeholder Master File Browser yang ditunda).

- Bila `has-manifest` false → toast error "Manifest belum ada
  — jalankan Fase 1 (buat_master.py) dulu." dan tidak lanjut.
- Bila true → isi tree OUTPUT + set gate output-ready.
- Fase 2 sengaja manual ("agar tidak otomatis jadi kita tau
  kapan diproses") — jangan pernah mengotomasi ulang.

### 16.5 Replace

Tombol "Replace" (secondary): 0 terpilih → toast "Pilih folder
dulu"; 1 → toast Replace (replace.jsx) tunggal; >1 → toast
"Multiple Replace: N folder dipasangkan & dikirim sekaligus
ke Photoshop". Menu konteks "Replace (Auto)" juga memicu
replace untuk node itu. Multi-select via klik-toggle
(Shift-click tidak diimplementasi — Slint tidak mengekspos
modifier key di TouchArea; toggle-click mencapai hasil yang
sama untuk mockup).

### 16.6 Footer — tombol script bernama

Footer = 5 tombol kecil Btn (`small`, font 11px) bernama dari
`scripts.json`: **buat_master, manasik, wisuda, pasfoto,
profesi**. File berawalan `_` (helper) dan `enabled: false`
tidak muncul; `buat_master_duplikat.py` TIDAK di footer
(duplikat) — tombolnya sendiri ada di panel OUTPUT (§16.4).
+ tombol Refresh ↻ IconBtn (scan ulang SOURCE & OUTPUT, toast)
+ tombol Clear × merah IconBtn (kosongkan SEMUA folder →
kembali ke drop zone; file tetap ada di disk).

Klik tombol script = ExecuteMaster: toast menyatakan proses
**SEMUA** folder SOURCE (bukan hanya yang terpilih), lalu
OUTPUT terisi + toast "[SUCCESS] Batch Selesai.".

**Kesenjangan vs ALUR §10 (terbuka):** urutan scripts.json
lengkap punya 11 entri (PROFESI, C-REDAKSI, MANASIK, PAS FOTO,
WISUDA, FOOTNOTE, BUCIN, OPEN PSD, BUKA EDIT PSD, BUAT MASTER,
DUPLIKAT PSD) — footer port saat ini hanya 5 script master
Python; C-REDAKSI / FOOTNOTE / BUCIN / OPEN PSD / BUKA EDIT
PSD belum ada di footer (ditanyakan ke abeng 26 Nov 16:04,
belum dijawab — jangan tanya ulang).

### 16.7 Master File Browser — DITUNDA

"Master file browser bertahap saja karena master file browser
itu ada di side panel log panel navigasi button" (abeng,
26 Nov 15:57). TIDAK dibangun di BATCH. Ikon ▤ di panel OUTPUT
hanya placeholder toast ("Master File Browser akan hadir di
side panel (bertahap)."). Lokasinya di side panel / Log Panel
(lihat §20.5).

### 16.8 Folder Reguler (tahap 4, 28 Nov 2026)

Sumber: studi master_creator (`~/workspace/master-creator-study/`,
`open_folder_reguler_popup` + `_RegularScanWorker` + `_dir_manager_*`).

- **Kelola…** (header BATCH) membuka popup "Folder Reguler" (bukan
  halaman baru). Isi: daftar direktori tersimpan (nama + status
  berwarna + konteks output `dir → last_output` + ikon kunci + ×),
  tombol "+ Tambah…", tabel sekolah (kolom BULAN / MARKETING /
  SEKOLAH), kolom filter ketik ("Cari nama sekolah atau marketing…",
  filter-as-you-type, substring case-insensitive atas nama sekolah
  atau marketing), dropdown bulan, tombol "Jadikan Source" + "Tutup".
- **Status direktori** (`RegulerStatus`, prioritas tertinggi dulu):
  Hilang (path tidak ada) > Dipakai (root SOURCE sama/dibawahnya saat
  run aktif) > Terkunci > Siap / Kosong. Warna mengikuti token status
  (§22); dot hijau = Siap.
- **Kunci (work lock):** direktori terkunci tidak bisa dihapus (klik ×
  ditolak + toast); toggle lewat ikon gembok di baris; `locked`
  tersimpan di `Batch.FolderReguler` (JSON `[{path, locked,
  last_output}]`, migrasi otomatis dari format lama `Vec<String>`).
- **Konteks output per direktori:** memilih direktori sebagai SOURCE
  memulihkan `last_output`-nya menjadi `LocalOutput` (bila non-empty
  dan berbeda) + persist ke `Paths.LocalOutput`; toast menyebutkannya.
- **Scan latar:** pilih direktori → scan di thread latar (channel +
  timer 250ms, pola `_sheets_inbox_timer`); UI tidak lag. Hasil scan
  lama diabaikan bila user sudah pindah direktori. Heuristik scan:
  anak folder bulan yang isinya HANYA subfolder (tanpa file) = kategori
  (mis. "1. PAS FOTO") dan turun satu level; yang berisi file = sekolah.
  Marketing dibaca dari `NOTE.txt` di folder sekolah (baris
  `MARKETING:`). Bulan = nama folder bulan apa adanya ("07 Juli 2026").
- **Filter bulan:** dropdown berisi "Semua bulan" + bulan-bulan unik
  dari hasil scan (urut natural); pilihan tersimpan di
  `Batch.FolderReguler.Month` dan dipulihkan saat popup dibuka.
  Perbaikan 28 Nov 2026 (defect ComboBox — lihat PERUBAHAN.md):
  daftar bulan kini mendorong tabel ke bawah saat terbuka (bukan
  menumpuknya), jadi item bisa diklik. Akar masalah: layout Slint
  TIDAK memakai binding `height` ComboBox untuk mengukur tinggi
  baris induk — baris tetap setinggi field, daftar hanya meluber
  visual dan klik item ditelan elemen di bawahnya (z-order).
  Perbaikan: `preferred-height` di komponen `ComboBox`
  (`ui/design.slint`) + hapus height tetap pada baris induk di
  header BATCH dan baris filter popup ini. Efek samping yang sama:
  dropdown Folder Reguler di header BATCH kini juga bisa memilih
  item kedua (sebelumnya tertutup kartu SOURCE/OUTPUT).
- **Dropdown header** ("Folder Reguler" di header BATCH) memakai daftar
  yang sama; memilih direktori = memuatnya sebagai SOURCE (sama seperti
  Pilih Folder). Klik ganda baris sekolah = jadikan SOURCE + tutup
  popup; klik tunggal = tandai baris; "Jadikan Source" memakai baris
  yang ditandai.
- **Tutup popup:** × / "Tutup" / klik di luar / klik kanan / Esc
  (Esc menutup popup Kelola dulu sebelum yang lain).

### 16.9 Task Manager — panel Tugas (tahap 5, 28 Nov 2026)

Sumber: studi master_creator (`_position_task_panel`: panel Tasks
menempel di samping main window, mode floating).

- **Bentuk:** window OS TERPISAH ("Tugas — BATCH", 340×600), BUKAN
  dock dalam layout dan BUKAN log panel. Alasan: panel dalam-layout
  gagal total — header BATCH punya min-width efektif ~1080px sehingga
  panel dock kanan terdorong keluar layar (terbukti 28 Nov 2026: kartu
  SOURCE/OUTPUT menyusut saat auto-open tapi panel tetap tak terlihat).
- **Posisi (dock):** menempel di KANAN window utama + gap 8px logis,
  y sejajar atas window utama (`dock_task_panel_pos`, hasil physical
  px untuk `set_position`; clamp >= 0). Posisi dihitung ulang tiap
  kali panel dibuka / run baru dimulai. Di layar 1280px dengan window
  utama 1280px, panel jatuh di x=1288 (di luar layar) — itu benar
  secara rumus; untuk verifikasi panel digeser manual dengan
  `xdotool windowmove`.
- **Buka/tutup:** otomatis terbuka saat run dimulai; tombol "☰ Tugas"
  di header BATCH toggle buka/tutup; × di panel menutup (hide +
  bersihkan slot — buka-ulang selalu buat window BARU, karena reuse
  hide→show tidak andal; JANGAN tahan `borrow_mut()` melewati
  `push_tasks_to_panel` — panic "RefCell already mutably borrowed",
  ditemukan 28 Nov 2026).
- **Tema:** tiap window OS punya instance `Design` sendiri — token
  disalin eksplisit via `theme::sync_design_to_task` saat panel dibuka
  dan saat tema diterapkan/di-reset (pola `sync_design_to_bar`).
- **Satu tugas per run** (judul `script — <nama folder>` atau
  `script — N folder`, cap 8, terbaru dulu). Fase = tiap root SOURCE
  (✓ Done / ▶ Running / ✗ Failed / … Waiting / ○ Pending).
  Detail kartu: judul + label state, progress bar, teks fase, 3 baris
  log terakhir (live dari `batch_exec` via drain timer 200ms),
  follow-up ("Lanjutan: …" + tombol "Lanjutkan"), tombol "Batalkan"
  bila state Antre/Berjalan/Menunggu save.
- **State tugas:** Antre / Berjalan / Menunggu save / Selesai / Gagal /
  Dibatalkan. Fase 1 (`buat_master.py`) sukses → "Menunggu save"
  (barrier: menunggu semua save Photoshop sebelum lanjut) + follow-up
  "BUAT MASTER (duplikat)". "Lanjutkan" (atau tombol BUAT MASTER di
  panel OUTPUT) menjalankan Fase 2 (`buat_master_duplikat.py`, gate
  manifest sama). Fase 2 sukses → tugas Fase 1 ikut "Selesai"
  (fase "BUAT MASTER (duplikat)" ikut ✓); Fase 2 dibatalkan → tugas
  Fase 1 kembali "Menunggu save" + follow-up utuh (bisa diulang);
  Fase 2 gagal → tugas Fase 1 "Gagal". "Batalkan" pada tugas yang
  sedang berjalan = `handle.stop()` (bunuh tree proses, sama seperti
  tombol Stop); pada tugas antre/menunggu save = langsung "Dibatalkan"
  + follow-up dibersihkan (tugas batal tidak bisa dilanjutkan lagi).
- **Batasan stop (warisan tahap 3):** `stop_requested` hanya dicek
  ANTAR root — run satu root tidak bisa dihentikan di tengah root
  (selesai dulu baru berhenti). Batalkan tetap tercatat; efek
  penghentian terasa pada run multi-root.
- **Log panel TIDAK dihapus di tahap ini.** Cek dependensi 28 Nov 2026:
  log panel menampung 4 alat yang hanya bisa dijangkau lewatnya — LOG
  (process log + log run BATCH), EXPLORER (mini explorer), MASTER
  (daftar template PSD), DOC (tab redaksi). Entry point: flyout switch
  "Terminal", Ctrl+L (buka tab EXPLORER), arg `--log`, klik ganda file
  teks di BATCH (tab LOG), tombol nav Explorer (tab EXPLORER).
  Penghapusan diputuskan terpisah setelah panel Tugas terbukti.

## 17. Settings

Settings BUKAN tab nav — dibuka via flyout profil (item
"Settings") dan/atau tombol gear / Ctrl+, (lihat §18).
Layar Settings sendiri dinilai bagus oleh abeng (panel samping
+ grup) — yang dikoreksi hanya cara membukanya.

### 17.1 Panel samping berkelompok

Pola panel samping (contoh yang dikirim abeng 26 Nov 16:54):
judul "Settings" + **search bar** di atas + **grup dengan
header kecil** + item ber-**ikon + label**, item terpilih
di-highlight. Grup di port (`ui/settings.slint`):
**General, Appearance, Account, Extensions, Script Manager,
Paths, About** — item-item BMachine sendiri di dalamnya (hanya
polanya yang diambil dari contoh, bukan item aplikasi lain).

**Panel konten rounded + sidebar ciut (abeng, 27 Nov 21:21):**
konten tiap grup duduk dalam panel rounded (`card-bg`, border
`separator` 1px, radius `radius-lg`, padding dalam 16px) — ref
`settings-rounded-content-ref.jpg`. Sidebar bisa diciutkan jadi
rail ikon 56px (dari 180px) via tombol "← Ciutkan" / "→" di bawah
sidebar (ref `settings-collapse-ref.jpg`); judul + search
disembunyikan saat ciut, label grup hilang (`if`, bukan
`visible:false`), ikon terpusat, grup tetap bisa dipilih dari rail;
nama grup tetap terlihat dari heading konten ("General" / "Paths"
ditambahkan — grup lain sudah punya heading). Pilihan tersimpan di
DB `Settings.SidebarCollapsed` (Bool, default false), live tanpa
restart, dimuat saat startup. Saat chrome `wrapped`, panel konten
dilewati (konten = panel window itu sendiri — anti double-wrap
§23) dan panel window mulai x=208 (terbuka) / x=84 (ciut).

**Kategori + jarak sidebar (abeng, 28 Nov 07:05) + sidebar polos
(abeng, 28 Nov 07:09, dikonfirmasi 07:11):** 7 grup dikelompokkan
ke 4 kategori — APLIKASI (General, Appearance), AKUN (Account,
Extensions), SKRIP & FILE (Script Manager, Paths), INFO (About) —
label kategori kecil (11px/700, `text-muted`, huruf kapital) +
garis separator tipis antar kategori (ruang 17px: 8px jarak + 1px
garis + 8px jarak); label + separator ikut sembunyi bila kategori
kosong (filter search). Di rail ciut label hilang, garis separator
tetap tampil sebagai pemisah ikon. Sidebar POLOS tanpa panel
sendiri di semua mode chrome — HANYA konten yang dapat panel
rounded (build 4f0696f salah memberi sidebar kartu rounded sendiri
sehingga tampil dua panel berdampingan; diperbaiki 28 Nov).

### 17.2 Appearance — kustomisasi penuh [MENGIKAT — abeng, 26 Nov 16:09]

Isi Settings bukan hanya yang "berguna", tapi **yang memang
memungkinkan user customize** (abeng, 26 Nov 16:11). Hapus
HANYA kata / label yang benar-benar tidak berguna DAN tidak
memberi kontrol. Bila ragu apakah sebuah opsi memberi kontrol
kustomisasi, masukkan saja.

Grup Appearance (referensi layout: halaman "Visual style"
aplikasi lain — diambil POLA layout-nya, bukan nilainya):

- **Color mode** ("Use light or dark colors, or follow your
  system setting") — 3 kartu preview visual (terang / gelap /
  ikut sistem); yang terpilih ber-border aksen.
- **Theme** ("Start with a preset, then adjust its colors and
  fonts") — dropdown preset (preset BMachine sendiri, bukan
  "ChatGPT").
- **Accent** ("Set the color for highlights and selected
  controls") — dropdown Custom + swatch warna + hex.
- **Background** ("Set the color behind app content") —
  swatch + hex.
- **Foreground** ("Choose the main text color") — swatch + hex.
- **Font** ("Set the font for menus and controls") — dropdown
  (aturan §1.2: hanya font keluarga weight lengkap; preview
  weight per font).
- **Latar widget** — pemilih gaya latar StatWidget
  (`widget-bg`: doodle / dots / impulse / none) — lihat §8.
- **Window chrome** — pilihan chrome window (`use-native-chrome`:
  boolean). Title bar kustom + tombol close/minimize/maximize
  terikat `visible: !use-native-chrome` (tampil saat chrome
  OFF / frameless, sembunyi saat ON); konten menyesuaikan
  title bar kustom ~40px. Mengubah chrome OS asli saat runtime
  butuh `set_decorations` via winit di `main.rs`, atau setting
  berlaku setelah restart. Settings bisa menawarkan beberapa
  gaya title bar (Native OS, Custom BMachine, varian).

State di `SettingsView` (`ui/settings.slint`): `color-mode`
("dark"), `theme-preset` ("BMachine Dark"), `accent-color`
(`#339CFF`) + `accent-hex`, `bg-color` (`#191919`) + `bg-hex`,
`fg-color` (`#FFFFFF`) + `fg-hex`, `font-choice` ("Inter"),
`chrome` ("custom"), `widget-bg` ("doodle").

**Polish alignment (26 Nov, permintaan abeng "rapihkan ... agar tidak
terlihat menclok-menclok")** — aturan yang diterapkan di semua layar:
SATU tinggi kontrol (Btn / TextField / ThemeRow / OptionChip = 36px;
NavTab / PanelTab = 32px); baris input sejajar kiri-kanan (field Nama
General 260px, penuh saat menumpuk; field hex 120px sama di Accent /
Background / Foreground (swatch 36px + dot 28px, baris 36px — garis
atas/bawah lurus, 26 Nov; saat menumpuk, konten <560px, field jadi
80px + jarak 2px + dot compact 24px supaya baris hex tetap muat di
konten 256px pada 520px — tinggi tetap 36px, garis tetap lurus); baris
hex tinggi eksplisit 36px); dot swatch terpilih ring
aksen (kelihatan di semua warna) + dot tak terpilih outline
`border-hover` (dot gelap tetap kelihatan); bar preview kartu "Ikut
sistem" dibatasi separuh terang supaya tidak menyeberang garis belah;
kartu Font tinggi responsif (140px di layar sempit) + contoh font
di-wrap; kotak preview responsif (220px, mengecil di 520px); konten
Flickable tiap grup punya `padding-bottom` (item terakhir tidak
menempel tepi bawah); grid launcher Modul dipusatkan vertikal
(launcher sudah DIHAPUS 26 Nov — lihat §19.1); path bar
Explorer seragam 36px (field search 140px + status disembunyikan saat
search di <700px — search terjangkau di 520px); baris file Explorer
ikon lebar tetap 14px (sejajar header Nama); Task Monitor / kartu tugas
diperbaiki dari meluber (TaskCard 100px, baris bawah 36px); context
menu Explorer tidak lagi meluber (item tersembunyi tinggi 0); flyout
profil dipusatkan + tinggi hug content; context menu BATCH hug content.
**Border-width selalu konstan** (hanya warna yang berubah saat
selected) — tidak ada elemen yang berubah ukuran saat diklik.
Terverifikasi visual 1280x800 + 520x600 (screenshot
`bmachine-slint-align-*.png` di `~/workspace/rust-gui-contoh/`).
Sisa (belum diperbaiki, perlu keputusan): baris `MasterRow` yang
terfilter di Log Panel meninggalkan celah 36px (`visible: false`
tetap memakan ruang) — perbaikannya butuh `height: 0` kondisional.

### 17.3 Grup lain

- **General**: Nama user ("abeng"), Bahasa ("Indonesia"),
  Animasi startup, Kecepatan animasi statistik, Interval
  refresh Trello ("30"). (Smart Orb / Floating Widget DIHAPUS
  27 Nov 2026 — abeng: orb sudah dihapus penuh dari C# dan
  diganti radial menu, bukan orb lagi. Key DB lama
  `Settings.FloatingWidget` / `Settings.Orb.*` /
  `Configs.Widget.Position` tidak lagi dibaca/ditulis port,
  tapi TIDAK dihapus dari database.)
- **Account**: Trello Integration — **Base URL** (default
  `https://api.trello.com/1`, key DB `Trello.BaseUrl`),
  API Key, Token, tombol Connect (= Tes koneksi ke base URL
  yang dikonfigurasi); Google Integration; Board
  Synchronization (board/list ID). Lihat §15.0.
- **Explorer**: Default Explorer Path (`D:\#GAWENA`), View
  Mode ("Details"), Sort ("Name"), Group ("None"), Show
  hidden files, Show file extensions, Content zoom (75–200%).
- **Paths**: Local Output (`D:\#GAWENA`), OKE BASE, Photoshop
  Path, Master Pas Foto / Manasik / Wisuda / Profesi
  (`D:\#DATA ABENG\MASTER\…`).
- **About**: versi + info aplikasi.

Setiap perubahan field menampilkan toast "Disimpan: …"
(mockup — belum tersimpan ke DB).

### 17.4 Baris setting horizontal + wrap responsif (abeng, 28 Nov 09:57/09:59/10:01)

Keluhan abeng: daftar preset Theme 8 baris full-width boros;
baris setting label-di-atas + kontrol full-width di bawahnya aneh
(Nama User cuma nama pendek tapi textbox full-width, Bahasa
dropdown di bawah label). Aturan baru, mengikat di semua grup:

- **Komponen `SettingRow`** (`ui/settings.slint`, sesudah
  `FieldLabel`): label + subtext di KIRI, kontrol di KANAN
  secukupnya lebarnya. Kontrol ditulis SEKALI sebagai `@children`;
  lebarnya diatur di tiap pemakaian. Saat `stacked` — lebar konten
  `< 560px` (`property <bool> stacked: content-rect.width < 560px`)
  — kontrol pindah ke BAWAH label (wrap, memang diharapkan abeng).
  Di window 520 konten 288px (sidebar terbuka) / 412px (rail ciut)
  → menumpuk; di 800 konten 568px → horizontal; di 1280 konten
  1048px → horizontal.
- **Lebar kontrol saat lebar:** TextField Nama 260px, Bahasa 140px,
  filter ekstensi 140px, field path 420px, field Trello/Google
  420px, field hex 120px (80px saat menumpuk), chip secukupnya
  (lebar natural dari label), Toggle natural. Saat menumpuk semua
  field/dropdown jadi penuh lebar baris.
- **Theme jadi dropdown** (in-flow, pola sama seperti ComboBox
  `ui/design.slint` — BUKAN popup overlay): tertutup menampilkan
  nama preset aktif + 4 dot warna (accent, revision, bg, fg) +
  chevron-down; terbuka menampilkan daftar in-flow berisi 8 preset
  + Custom + tema user (tetap bisa dihapus ×). Pilih → handler
  `apply-theme-preset` / `apply-user-theme` yang sudah ada (live +
  tersimpan DB, tanpa perubahan Rust). Tinggi eksplisit **dan**
  `preferred-height` keduanya = `36px + (terbuka ? 4px + jumlah
  baris × 36px : 0px)` supaya layout induk mengukur benar (pelajaran
  defect dropdown bulan, commit c84d621).
- **Perbaikan 520px:** baris hex — field 80px, spacing 2px,
  `SwatchDot compact` 24px (total 248px, muat di konten 256px);
  `WidgetColorRow` saat menumpuk jadi 2 baris (dot+label+field di
  atas, 4 swatch di bawah), tinggi 76px; roda radial saat menumpuk
  — roda di atas (tengah) + 8 chip rincian penuh lebar di bawah
  (komponen `RadialWheelPreview` + `SlotChip`, dipakai di kedua
  layout supaya tidak duplikasi ~90 baris); Shortcut menu saat
  menumpuk — kotak + tombol menumpuk (kotak penuh, tombol penuh);
  Extensions saat menumpuk — header judul wrap + tombol di bawah,
  baris plugin nama di atas + tombol di bawah (tinggi baris 100px);
  judul card Account/Script Manager + 3 teks deskripsi Script
  Manager diberi `wrap: word-wrap` (judul panjang terpotong di
  520px); deret 3 tombol Script Manager menumpuk jadi 3 tombol
  penuh lebar; baris Worksheet/Kolom/Baris Account menumpuk jadi
  Worksheet penuh + Kolom/Baris berdampingan di bawah.
- **Yang dipertahankan sebagai blok penuh** (bukan baris setting):
  kartu preview Color mode (Terang/Gelap/Ikut sistem), kartu Font,
  blok Preview, roda radial, daftar Pet, baris multi-kolom Account
  (Worksheet/Kolom/Baris, Board sync 2-kolom, Refresh intervals
  4-kolom — sudah ringkas horizontal), Visibilitas dashboard (3
  baris label-kiri + toggle-kanan, sudah horizontal).
- **Catatan Slint:** ternary `kondisi ? 100% : Npx` TIDAK bisa
  dikompilasi ("Cannot convert length to percent") — pakai
  `kondisi ? parent.width : Npx` (keduanya length). `parent.width`
  aman di sini: saat menumpuk lebar wrapper ditentukan layout luar
  (stretch 1), bukan oleh kontrol.
- **Penyerap ruang ekstra (28 Nov, dua tempat):** layout Slint
  membagi rata ruang ekstra ke SEMUA anak bila total stretch <= 0
  (tiap anak dianggap stretch 1 — `i-slint-core/layout.rs`),
  dan cross-axis default adalah `stretch`, dan `alignment` default
  layout adalah `Stretch`. Akibatnya (a) label `FieldLabel` yang
  barisnya lebih tinggi dari label (dropdown Theme terbuka, Paket
  ikon 2 baris chip + preview) membuat deskripsi melayang ke tengah
  baris; (b) grup yang kontennya lebih pendek dari viewport (Paths,
  Extensions, About) ikut direnggangkan ke tinggi penuh sehingga
  baris tersebar. Perbaikan: (a) `FieldLabel` diakhiri `Rectangle
  { vertical-stretch: 1; }` tak terlihat (tinggi natural 0) +
  `vertical-stretch: 0` di kedua `Text`-nya — penyerap mengambil
  semua slack di dalam label; (b) tiap layout konten grup memakai
  `alignment: start` supaya anak-anaknya tetap pada tinggi natural
  dan ruang ekstra dibiarkan kosong di bawah (penyerap di akhir
  konten grup sempat dicoba — TIDAK cukup untuk Extensions karena
  Card yang berisi `Rectangle` melaporkan stretch 1 sehingga ikut
  menyerap separuh slack; `alignment: start` mematikan distribusi
  itu di akar). (Percobaan `vertical-stretch: 0` saja TIDAK mempan
  karena aturan total <= 0 di atas; `vertical-alignment: top` saja
  juga tidak — yang meregang adalah kotaknya, bukan posisi teks di
  dalamnya. Mekanisme dibuktikan di scratch crate
  `~/tmp-childtest`.)
- **Prinsip umum (mengikat, abeng 28 Nov 10:01):** SEMUA view/window
  harus matang tampilannya dari ukuran kecil (520) sampai besar —
  verifikasi di lebar 1280 DAN 520.

## 18. Arsitektur Overlay & Navigasi

### 18.1 Overlay layar penuh

AppWindow memakai `overlay-view` (string): `""` = dashboard
tampil (pill nav terlihat); selain itu overlay tampil + pill
nav disembunyikan (seperti C#: MainWindow ContentControl
beralih dari DashboardView). Nilai: `""` | `"SETTINGS"` |
`"TOOLBOX"` | `"SHEETS"` | `"LEADERBOARD"` |
`"EXPLORER"` | `"TRELLO-WINDOW"`.

- **Settings** dibuka via flyout profil ("Settings") atau
  Ctrl+, — BUKAN tab nav.
- **Toolbox / Spreadsheet / Leaderboard** adalah overlay
  masing-masing (`"TOOLBOX"` / `"SHEETS"` / `"LEADERBOARD"`),
  kembali ke dashboard via tombol "← Kembali ke Dashboard"
  atau klik kanan (§10.11). TIDAK ADA launcher Modul
  (dihapus 26 Nov — tidak ada di C#; lihat §19).
- **Window Trello** overlay `"TRELLO-WINDOW"` (§15.3).
- **Explorer** overlay `"EXPLORER"` — lihat §18.2 dan §20.
- **Esc**: bila overlay Explorer → `explorer-view.esc-pressed()`
  (prioritas §20.6); bila overlay lain → tutup overlay; bila
  tidak ada overlay tapi flyout terbuka → tutup flyout.
- **Log Panel** (sidebar kanan, lebar 300px): konten utama
  menyusut (`content-width = root.width − 300px`) supaya tidak
  tertutup panel. Dibuka via saklar "Terminal" di flyout
  profil; ditutup via × di header panel. Ctrl+L membuka Log
  Panel dengan tab EXPLORER (§10.7). Log Panel disembunyikan
  saat overlay layar penuh terbuka.

### 18.2 Membuka Explorer

(a) Tab EXPLORER di Log Panel = mode compact (§3.3
`EXPLORER_SPEC.md`): path `D:\#GAWENA` + 5 baris, klik baris
membuka folder itu di overlay Explorer.

(b) window Explorer terpisah = overlay "EXPLORER" (Build B, selesai 26 Nov — lihat §20). Tombol "⧉ Buka Window Baru" dan klik baris di tab EXPLORER membuka folder itu di overlay Explorer; Log Panel disembunyikan saat overlay terbuka (overlay layar penuh seperti Settings).

### 18.3 Flyout profil

Tombol profil (ikon user-circle, tanpa label) di pill nav
membuka flyout 256px: username "abeng" (14 SemiBold), dot
hijau + greeting "Selamat datang kembali" (11), separator,
menu — item update (hanya bila ada pembaruan, dot aksen +
"Ada pembaruan v{0}"), "Toolbox", saklar Terminal (Log
Panel), "Laporkan Bug", "Settings" (Bold), "Log Out" (Bold,
merah), separator, footer "BMACHINE · v6.5.0" (9px).
Flyout TIDAK ikut `ui-scale` (hanya pill nav yang ikut).
Klik item → toast (mockup) kecuali Settings (buka overlay)
dan Terminal (buka Log Panel).

## 19. Fase 4 — Toolbox, Spreadsheet, Leaderboard (BUKAN "Modul")

[rekonstruksi — dari kode `ui/modul.slint` + `BMACHINE_ALUR_APLIKASI.md`
§4.5 + `ANALISIS_BMACHINE.md` §6.2/§6.4/§6.5. Detail yang belum
terverifikasi ditandai.]

### 19.1 TIDAK ADA launcher Modul — DIHAPUS 26 Nov

Dulu ada `ModulView` (launcher 4 kartu: Toolbox /
Spreadsheet / Leaderboard / Log Panel) di port ini. Dihapus
26 Nov 2026 karena TIDAK ADA layar "Modul" di C# (abeng,
26 Nov 18:45 — "perasaan di bmachine saya yang dibangun C#
tidak ada module"). Yang ada di C# adalah `ToolboxWindow`
(berisi 4 modul Mantra — tipe `ToolboxModule`),
`SpreadsheetWindow`, dan `LeaderboardView` /
`PointLeaderboardWindow` — masing-masing dibuka lewat
jalurnya sendiri. Jangan bangun ulang launcher tanpa arahan
abeng.

| Layar | Buka dari (port) |
|---|---|
| Toolbox | item "Toolbox" di flyout profil → overlay `"TOOLBOX"` |
| Spreadsheet | panah (→) widget POINTS → overlay `"SHEETS"` |
| Leaderboard | badan widget POINTS → overlay `"LEADERBOARD"` |
| Log Panel | saklar Terminal di flyout / Ctrl+L (tab EXPLORER) |

### 19.2 Toolbox — TAMPILAN DIROMBAK [abeng, 26 Nov 17:19]

"Kurang lebih update-an tampilan toolbox itu" — tampilan
Toolbox di port HARUS di-update / dirombak (bukan ditiru
persis dari C#). Alur kerja tetap persis (lihat di bawah);
yang dirombak adalah tampilannya, mengikuti design system
port ini.

`ToolboxView` (`ui/modul.slint`): 3 tab NavTab —
**MantraNama / MantraGanda / PsdBucin**
(`BMACHINE_ALUR_APLIKASI.md` §4.5). Klik kanan = kembali
ke dashboard (§10.11). **TIDAK ADA tombol kembali** —
dihapus dari semua layar 26 Nov 19:48 (abeng): klik kanan
(+ Esc) sudah menjadi cara kembali/tutup; jangan tambah
lagi tombol kembali di layar mana pun (§10.11).
Isi tiap tab bisa di-scroll (Flickable, lihat §19.8) supaya
tombol bawah tetap terjangkau di 520×600.

**Yang tetap persis (alur kerja):**

- **MantraNama** — rapikan nama file. Kartu SUMBER (DropZone
  + TextField contoh "47276395_SDN RANCAIYUH 1_GUSFIARI" +
  Toggle Auto-pair + "Proses Nama" / "Bersihkan") + kartu
  PREVIEW PASANGAN (3 baris referensi → target + ✓ setelah
  proses) + toast mockup.
- **MantraGanda** — gandakan file. Kartu FILE/FOLDER MASTER
  (DropZone + Pilih) + KONFIGURASI (Jumlah "2", Mulai dari
  "1", Pisah) + PROGRESS (Timer mockup ~3 dtk, Gandakan /
  Berhenti / Bersihkan) + toast mockup.
- **PsdBucin** — duplicator PSD via `psdbucin_v3.pyw`.
  Kartu MASTER & OUTPUT (DropZone + path + Pilih) + INPUT
  FOTO (DropZone + path + Pilih) + STATUS ("Menunggu
  Folder..." → progress → "Selesai — PSD dibuat...") +
  MULAI / Clear + validasi toast bila folder belum dipilih.
  Chip "MANTRA DATA · LOCKED" di strip bawah — MantraData
  tetap LOCKED, tidak ada tab ke-4.

**Yang dirombak (tampilan):** layout tab + drop zone +
field + tombol mengikuti design system port (bukan window
Toolbox C#); kartu / panel mengikuti Card + Btn port.

**Yang TIDAK dibangun:** eksekusi nyata (dialog pilih file,
menjalankan `psdbucin_v3.pyw` / script Mantra) — semua aksi
saat ini toast mockup; eksekusi nyata Fase 6.

### 19.3 Spreadsheet — spreadsheet beneran (27 Nov 2026)

`SpreadsheetComponent` (`ui/spreadsheet.slint`, dipakai `SheetsView`
embedded + `SpreadsheetWindow` 1000×600): grid SATU Google Sheet.
**Sejak 28 Nov 2026 kolom DINAMIS dari sheet live** (bukan 7 kolom
tetap): header terdeteksi dari isi (baris pertama dalam 10 baris
pertama yang punya ≥2 sel tak-kosong dan ≥1 huruf — ALL DATA REGULER
= baris 3, EDITOR HARIAN = baris 1; jangan hardcode baris 3), data
mulai baris setelahnya. ALL DATA REGULER tampil apa adanya: 20
kolom A–T (NO FAKTUR | ORDER TIME | PIC | MARKETING | ID PELANGGAN
/ ID TRELLO | CUSTOMER NAME | ALAMAT | ITEM NAME | QTY | H+1 ADMIN
| ADM OPS | SELEKTOR | TGL DONE | EDITOR | TGL DONE | CETAK PACKING
SLIP | PRINTING | QC AKHIR | NAMA DRIVER | TGL PENGIRIMAN).
Bila tidak terhubung (tanpa JSON/ID), grid memakai model lokal
7 kolom di bawah + status bar jujur ("Belum terhubung ke Google
Sheets" / "Data lokal (N baris) · Belum terhubung…").

Model lokal (offline) — 7 kolom tetap A–G:

| Kolom | Header | Jenis | Kunci? | Opsi |
|---|---|---|---|---|
| A | KODE | teks | ya (ID, "Copy Row ID") | – |
| B | SEKOLAH | teks | tidak | – |
| C | JENJANG | dropdown | tidak | TK, PAUD, SD, SMP, SMA, SMK |
| D | STATUS | dropdown | tidak | SELESAI, PROSES, REVISI, ANTRIAN |
| E | POIN | angka | tidak | – |
| F | CATATAN | teks | tidak | – |
| G | TANGGAL | tanggal | tidak | – |

Definisi kolom lokal di `src/sheet_model.rs` (`SHEET_COLUMNS`);
kolom live di `src/sheets_api.rs` (`LiveSheet` + `to_dyn_columns`):
jenis ditebak dari kata kunci header seperti C# (TGL/DATE/TIME/
DEADLINE → tanggal; EDITOR/STATUS/SELEKSI/HKS/NR → dropdown,
opsi = nilai unik kolom maks 30; QTY/POIN/JUMLAH → angka; sisanya
teks). Kolom NO FAKTUR terkunci (kecuali sel kosong baris baru —
order baru harus bisa diisi NO FAKTUR-nya); ORDER TIME terkunci
sebagai teks (jangan timpa timestamp order). Header duplikat
(dua "TGL DONE") diberi sufiks supaya unik. Lebar kolom dari
`Configs.Spreadsheet.ColumnWidths` bila ada, kalau tidak heuristik
dari isi (70–260px). Paging 100 baris/halaman; gutter menampilkan
nomor baris sheet asli; footer "Menampilkan X–Y dari Z · Kolom:
N (A–T) · Sel Aktif: <huruf><baris>".

**Koneksi live — Google Sheets API (28 Nov 2026, "jantung
kerjaan"):** `src/sheets_api.rs` — baca JSON service account
(`client_email` + `private_key` PEM), JWT RS256 (ring 0.17, tanpa
crate baru di luar yang sudah ada) → tukar ke access token
(`Google.TokenUrl`, default oauth2.googleapis.com) → `GET
spreadsheets.values.get` (`Google.ApiBase`, default
sheets.googleapis.com) untuk `{tab}!{range}`. Kredensial dari
Settings (`Google.CredsPath`, `Google.SheetId`,
`Spreadsheet.SheetName`, `Spreadsheet.Range` default "A1:Z") —
dibaca SEGAR dari DB tiap sync, jadi perubahan Settings berlaku
tanpa restart. Fetch jalan di thread latar (tidak memblokir splash)
+ hasil diterapkan timer inbox 250 ms di thread UI; timeout operasi
45 detik (draft dipertahankan). **POINTS widget live dari sheet**
seperti C#: baca sel `{Google.SheetName}!{Google.SheetColumn}
{Google.SheetRow}` (EDITOR HARIAN!J14) tiap fetch, cache ke
`Stat.Points` + `Cache.GSheet.Points` — sebelumnya `Stat.Points`
mati (tidak ada yang menulisnya di port).

**Anti-menclok (MENGIKAT — abeng, 28 Nov 2026 00:25):** (a) SELALU
ambil data live terbaru sebelum Simpan DAN saat buka/Muat Ulang —
snapshot lokal saja tidak cukup; (b) baris dikenali kunci tetap
**NO FAKTUR** (kolom A, unik per order), BUKAN nomor baris —
`DraftEntry.RowKey` + `RowAnchor.row_key`, `apply_drafts` mencari
baris dari kunci (draft tanpa kunci TIDAK PERNAH diterapkan dari
posisi); sisipan baris di atas TIDAK memblokir simpan, perubahan isi
/ penghapusan baris berkunci MEMBLOKIR; (c) bila sheet berubah dari
luar, Simpan DIBLOKIR + pesan menyebut NO FAKTUR + nama kolom +
nilai lama vs baru + "klik Muat Ulang" (draft tetap tersimpan dan
dipulihkan setelah muat ulang). Pesan guard ditulis Rust ke status
bar JUGA (bukan hanya `guard-result` — `guard-result` hanya dibaca
`commit-save()` secara sinkron, jadi blokir async tidak terlihat
tanpa status bar; footer status wrap 2–3 baris, bukan elide).
Selama simpan live berjalan, Simpan/Muat Ulang ganda diblokir;
edit tetap bisa masuk dan draft yang tiba selama flight
dipertahankan. Simpan tanpa perubahan menerapkan data live segar
yang sudah di tangan (bukan commit grid basi).
"harus mengikuti sheet, jangan egois"):** aturan alignment grid
mengikuti format sheet kantor per kolom — dan karena sheet kantor
("ORDER PT DMA - NSJ 26-27", tab ALL DATA REGULER) menengahkan
SEMUA sel (header + isi, termasuk teks ALAMAT yang panjang dan
angka QTY), grid port pun rata tengah semua: header + semua sel
data, semua jenis kolom (teks, angka, tanggal, dropdown), baik
tampilan (`GridCell` Text) maupun editor (`TextInput` saat mengetik).
Aturan lama (teks kiri / angka kanan / tanggal tengah) sudah dihapus.
Ikon sel (▾ dropdown, gembok KODE, kalender tanggal) digambar DI ATAS
teks, jadi teks dipusatkan di lebar sel penuh. Daftar opsi dropdown
(popup pemilih, bukan sel) tetap rata kiri seperti ComboBox C#.
Saat kolom dinamis dari Sheets API dibangun, alignment tetap mengikuti
format per kolom dari sheet — bukan aturan keras port.

**Seleksi (model C#):** klik = satu sel; Shift+klik / seret =
rentang dari anchor; Ctrl+klik = tambah/hapus baris dari seleksi.
Sorotan = baris terpilih × kolom fokus saja (bukan seluruh blok);
sel aktif = border aksen 2px + titik fill-handle. Nomor baris di
gutter kiri = nomor baris sheet (bukan indeks grid).

**Edit:** klik tunggal hanya memilih; edit mulai dari klik-ganda /
F2 / Enter / mengetik langsung. Sel dropdown & tanggal membuka
editornya saat klik tunggal (C# — "dulu harus klik ganda, jadi
terasa tidak bisa diklik"). Sel terkunci → toast, tidak jadi edit.
Enter = terapkan, Esc = batal, klik sel lain = terapkan (teks/
angka) atau batal (dropdown/tanggal). State edit eksplisit
(`editing`), bukan toggling `IsReadOnly` seperti C# (§19.3a).

**Dropdown:** daftar opsi muncul di bawah/atas sel (dibalik bila
tidak muat), ✓ pada nilai saat ini, sorotan keyboard naik/turun +
Enter memilih; dijepit horizontal supaya tidak keluar window.

**Tanggal:** kalender 240px (Senin dulu, 6×7), bulan digeser ‹ ›,
hari terpilih = nilai sel, hari ini ditandai; tombol TODAY menulis
tanggal hari ini (bukan hari yang sama di bulan tampil); pilih
hari menulis `yyyy-MM-dd`.

**Ctrl+D (Fill Down, row-based, seperti C#):** nilai baris teratas
seleksi disalin ke baris lain di kolom fokus saja; butuh ≥2 baris
terpilih (kalau tidak, toast — bukan diam seperti C#).
Delete/Backspace: mengosongkan sel kolom fokus di baris terpilih
(sel terkunci dilewati).

**Keyboard:** panah pindah sel, Shift+panah perluas seleksi,
F2/Enter mulai edit, Esc batal, Delete/Backspace hapus,
Ctrl+D fill down, ketik langsung = type-to-edit. Fokus keyboard
kembali ke grid setelah edit selesai (commit/batal/pilih) —
sebelumnya fokus hilang bersama editor yang tersembunyi sehingga
panah/Ctrl+D tidak jalan setelah Enter.

**Toolbar:** Search (belum memfilter — dekoratif), + Baris (tambah
baris kosong di akhir data, tanpa batas 6 baris lagi), ∑ SUM (total
kolom angka pertama — POIN untuk model lokal, QTY untuk sheet
kantor — dihitung Rust), zoom 0.8–1.4 (belum mengubah skala grid —
dekoratif), Muat Ulang (ambil ulang data live bila terhubung,
kalau tidak snapshot lokal), Simpan (N) = jumlah draft hidup.

**Guard + draft (jalur simpan sama, kini keyed NO FAKTUR):**
fingerprint guard + anchor per baris memblokir Simpan bila sheet
berubah di luar (lihat "Anti-menclok" di atas); draft dipersist ke
SQLite `Spreadsheet.Draft` tiap edit dan dipulihkan saat app dibuka
/ Muat Ulang — draft yang berhasil diterapkan TETAP jadi edit belum
tersimpan sampai Simpan (perbaikan 27 Nov 2026: sebelumnya draft
diterapkan lalu dibuang, sehingga SpreadsheetWindow yang dibuka
belakangan menampilkan nilai asli dan guard kehilangan baris yang
berubah). "Muat Ulang" memuat ulang data live + menerapkan lagi
draft yang masih cocok (dicocokkan dari NO FAKTUR + nilai asli).

**Yang belum dibangun:** pencarian & zoom masih dekoratif.
Koneksi live sudah jalan (terverifikasi melawan mock server lokal
dengan data sheet kantor asli — lihat PERUBAHAN.md 28 Nov 2026);
verifikasi dengan JSON service account asli dilakukan nanti di PC
abeng (JANGAN minta JSON-nya untuk tes di sini). Setelah Simpan
live berhasil, grid belum me-refresh otomatis untuk menampilkan
baris yang disisipkan dari luar — tekan Muat Ulang (terbuka).

### 19.3a Yang sengaja TIDAK ditiru dari C# ("aneh" — abeng, 27 Nov 2026)

Dibaca dari `SpreadsheetView.axaml.cs` + `SpreadsheetViewModel.cs`,
perilaku di bawah ini TIDAK disalin — port membuat yang benar:

1. **Edit gating rapuh:** C# mengutak-atik `IsReadOnly` di beberapa
   tempat + flag `_openDropdownOnNextEdit` + dispatcher post untuk
   membuka editor. Port: state `editing` eksplisit.
2. **Tebak jenis kolom dari header:** C# menebak dropdown/tanggal
   dari kata kunci header (EDITOR/STATUS/TGL/...) atau isi sel.
   Port (27 Nov 2026): jenis kolom tetap di `SHEET_COLUMNS` untuk
   model lokal. **Koreksi 28 Nov 2026:** untuk kolom LIVE dari
   Sheets API, port menebak jenis dari kata kunci header PERSIS
   seperti C# (itu yang benar — header sheet kantor yang menentukan
   jenisnya); model lokal 7 kolom tetap fixed.
3. **Tidak ada lock per-sel:** C# `IsReadOnly = false` di grid;
   proteksi sheet kantor hanya ditegakkan Google Sheets. Port:
   kolom KODE terkunci dan ditolak di aplikasi (toast).
4. **FillDown diam:** C# diam saja bila <2 baris terpilih +
   menulis teks debug ke status. Port: toast jelas.
5. **`FixHeaderContentCentering` (+16px margin hack):** workaround
   tema Avalonia — tidak diperlukan di Slint.
6. **Format tanggal mendua:** C# bimbang antara "6 Februari 2026"
   vs ISO. Port: `yyyy-MM-dd` di mana-mana (seperti yang ditulis
   C# saat commit).
7. **Pesan guard C#** (`Describe`) menampilkan nilai pengenal baris
   (sel pertama tak-kosong) di kedua sisi pesan — bila hanya kolom
   lain yang berubah, pesannya tampak "berisi 'X', sedangkan yang
   dimuat 'X'" (nilai sama). Perilaku blokirnya benar; wording ini
   **sudah diperbaiki 28 Nov 2026**: `describe_mismatches` menyebut
   NO FAKTUR + nama kolom yang berubah + nilai baru vs nilai yang
   dimuat, tidak pernah pengenal yang sama di kedua sisi.

### 19.4 Leaderboard

`LeaderboardView` (`ui/modul.slint`): poin harian & bulanan.
Chip pilihan Harian / Bulanan (`OptionChip` — font Btn
12/600, terpilih = `accent-subtle` + border aksen).
`LeaderRow`: tinggi 56px, peringkat (18px, weight 800,
warna peringkat), nama (13px, 700) + bar poin (tinggi 8px,
proporsional terhadap yang teratas, warna teal), poin
("N poin", 13px, 700, rata kanan). Peringkat 1–3 pakai
warna teks (emas / perak / perunggu), tanpa emoji.
Contoh: 1. abeng 1.280 · 2. nissa 980 · 3. fenti 760 ·
4. surya 640 · 5. yoga 520 (harian); bulanan 4.860 /
4.210 / 3.980 / 3.120 / 2.740. Klik baris → toast rincian.
Klik kanan = kembali ke dashboard (§10.11).

Catatan: layar ini dibuka dari klik BADAN widget POINTS
(26 Nov — cara kerja asli C#); panah widget POINTS membuka
Spreadsheet (§15).

### 19.5 Pertanyaan terbuka [rekonstruksi — perlu konfirmasi abeng]

1. Data contoh hero (EDITING 24 / REVISION 7) vs daftar
   Fase 3 (EDITING 6 / REVISION 5) — belum diselaraskan;
   angka mana yang benar?
2. Lokasi Master File Browser — abeng: side panel / Log
   Panel (bertahap); tab MASTER Log Panel tetap daftar +
   toast sampai diputuskan.
3. Spreadsheet: detail draft persisten + guard fingerprint —
   perilaku persisnya saat sheet berubah di luar (blokir
   save saja, atau tawarkan gabung?).
4. Leaderboard vs Trello: apakah klik StatWidget POINTS
   harus membuka UnifiedTrelloView 3 tab (EDITING / REVISI /
   SUSULAN) seperti C#, atau daftar Fase 3 yang
   disederhanakan sudah cukup?

### 19.6 Verifikasi Fase 4 (26 Nov 2026 — klik sungguhan + screenshot dibaca)

- Build bersih (`cargo build` exit 0, Slint 1.19.1, Rust 1.100).
- Semua layar terverifikasi klik sungguhan (xdotool XTest) +
  screenshot dibaca koordinator, 1280×800 DAN 520×600,
  prefix `bmachine-slint-fase4-` di `~/workspace/rust-gui-contoh/`:
  dashboard 65/76/79; radial 02/03/07/08/41–46/53–57/66;
  Toolbox 09–18/20–25/58–61/67–71/82; Sheets 32/35–40/63/
  74/81; Leaderboard 26–31/62/72/73.
- Radial: orb + Shift+Klik Tengah (41) → submenu TOOLBOX
  (08) & SEND-LOGO Tab 1 → toast (42/43) → Esc tutup (54,
  setelah perbaikan fokus §19.8); REPLACE (55), SAVE MASTER
  (56), OPEN PSD (57) → toast; BUCIN → Toolbox PsdBucin (58).
- Toolbox: Proses Nama → ✓ (10); Gandakan tanpa master →
  toast, dengan master → progress → selesai (12–15);
  PsdBucin validasi (17) + full run Pilih master/foto →
  MULAI → 10/20 → selesai (59–61); klik kanan di DropZone
  → dashboard (21/25, setelah perbaikan §19.8).
- Sheets: edit → indikator draft (36); Simpan → commit (37);
  Simulasi → status merah (38); Simpan terblokir, draft
  dipertahankan (39); Muat ulang → hijau + draft tetap (40);
  draft bertahan pindah overlay (63).
- Leaderboard: Harian 4.180 / Bulanan 18.910 (26/27); klik
  baris → toast (29); Refresh → toast (30); klik kanan di
  baris → dashboard (31); kartu mengisi layar (62).
- 520×600: dashboard (65), radial (66), Toolbox PsdBucin +
  scroll → MULAI terjangkau (67–71), Leaderboard (72/73),
  Sheets (81). Catatan: toast sementara menutupi footer
  di 520×600 (hilang setelah 2,5 dtk, layout-nya sendiri muat).

### 19.7 Radial Menu (Fase 4, 26 Nov)

`RadialMenuView` (`ui/radial.slint`, 467 baris) + wiring di
`ui/main.slint` (import, `overlay-view` `"RADIAL"`,
`toolbox-tab`). Wheel 400×400 di tengah, dim hitam 60%.
Ring dalam 7 slot (sudut grid 8-slot C#, `MaxVisibleItems`=7,
tanpa More/Back): TOOLBOX 0° atas, REPLACE 45°, SAVE MASTER
315°, BUCIN 90°, OPEN PSD 270°, SEND-LOGO 135°, SEND-TEXT
225°, Selatan 180° kosong. Lingkaran 56px + kode + label
lengkap di luar ring (radius label 171px — perbaikan dari
140px: label sempat terpotong lingkaran, "EPLACE"/"BUC3").
Submenu luar + dwell 300ms (Timer): hover 300ms atau klik
grup membukanya; tetap terbuka saat pointer ke ring luar;
tutup saat hover slot lain / radial ditutup. Submenu TOOLBOX:
3 pill Mantra (klik → Toolbox langsung di tab itu, tanpa
`back()`); SEND-LOGO/SEND-TEXT: 2 tab contoh "Tab 1"/"Tab 2"
→ toast mockup (port belum punya DOC). Semua slot berfungsi,
tanpa dead button (§10.1): BUCIN → Toolbox PsdBucin;
REPLACE/SAVE MASTER/OPEN PSD → toast mockup. Hub tengah
108px: judul slot yang di-hover + subtitle "Pilih" /
"klik / tahan — submenu". Tutup: klik kanan di mana saja di
radial, klik kiri backdrop, Esc, × kanan-atas wheel.
Caption: "Shift + Klik Tengah di dashboard • Klik kanan /
Esc untuk menutup". `reset()` membersihkan hover/submenu/
dwell di semua jalur buka/tutup.
Entry point: **orb** di bar nav (ujung kanan, di luar pill,
48px, ring aksen + dot, tanpa emoji, ikut `ui-scale`) dan
**Shift+Klik Tengah** di latar dashboard (TouchArea latar —
hanya di celah: padding 20px, gutter 12px, strip bawah 30px,
BATCH kosong; klik tengah DI ATAS widget/baris tidak sampai
ke TouchArea latar). **Tidak ada** system-wide hook
(`GlobalInputHookService`) — itu Fase 6, butuh izin.
Penyimpangan dari C#: submenu TOOLBOX 3 tab Mantra (bukan
5 file `_tb_*.jsx`); tanpa PieWedge — highlight = bg
accent-subtle + border aksen; label horizontal, bukan
tangensial; tipografi §1.1; tidak ada slot yang membuka
Spreadsheet/Leaderboard.

### 19.7a Radial Menu — tema + aturan pintah (27 Nov 2026)

Menggantikan sebagian §19.7 (roda overlay 400×400 + orb sudah
tidak berlaku sejak P0: radial kini OS window 320×320 di posisi
kursor, `RadialMenuWindow` di `ui/windows.slint`, pemicu SATU —
hotkey global Shift+klik tengah).

**Tema (abeng 27 Nov 20:03, ref `~/workspace/radial-refs/`).**
Segmen radial mengikuti warna aksen preset aktif — Spider =
merah `#D9272E`, Gwen = pink, dst. — live tanpa restart.
Mekanisme: tiap OS window punya instance `Design` sendiri, jadi
aksen disalin ke window radial lewat `theme::sync_design_to_radial`
(`src/theme.rs`) — dipanggil saat radial dibuka, tiap handler
tema (preset / reset / tema user), dan tiap perubahan pengaturan
radial. Gaya radial dipilih di Settings → Script Manager →
"GAYA & SEGMEN RADIAL" (DB `Settings.Radial.Style`, default
`"glass"`; nilai tak dikenal → `"glass"`):
- **Kaca** (default): pelat kaca gelap (`surface-elevated`
  alpha 0.88), segmen hover = aksen 0.35 + busur glow aksen
  2.5px di tepi luar, divider putih 0.18.
- **HUD**: pelat transparan, outline aksen 0.45, segmen hover
  = aksen 0.15 + busur glow, divider aksen 0.3, border hub
  aksen (ala ref 3a/2).
- **Flat**: pelat `card-bg` datar, segmen hover =
  `accent-subtle` penuh, tanpa busur glow, border hub
  `separator`.
Label tengah = nama item yang di-hover/dwell.

**Aturan pintah / split rule (ref 8-layout.jpg).** Ring dibagi
N segmen SAMA RATA sesuai jumlah item (2–12); sudut tiap segmen
= 360/N dari Utara searah jarum jam, bukan hardcode 8. Mesin
geometri murni di `src/radial_geom.rs` (`sector_angle_deg`,
`sector_index`, `wedge_path`, `wedge_arc_path`,
`divider_commands`, `button_top_left`, `radial_geometry`) —
didorong Rust ke Slint saat radial dibuka / pengaturan berubah
(`push_radial_geometry` di `src/main.rs`): `sector-count`,
`wedge-cmds`, `arc-cmds`, `divider-cmds`, `btn-x`, `btn-y`.
Unit test: N ∈ {2,3,4,5,6,8,9,10,12}; regresi N=8 sama persis
dengan layout hardcode lama (tombol (142,70)…(91,91), 8 wedge,
divider); posisi tombol dibulatkan ke piksel utuh (konvensi
hardcode lama: 72·sin45° = 50,91 → 51).
**Batas roda: 2–8.** Mesin mendukung 2–12, tapi satu halaman
roda hanya memuat 8 slot (paritas C#) — Settings menawarkan
2/3/4/5/6/8 (DB `Settings.Radial.Segments`, default 8; di luar
2–8 dijepit). 9–12 baru terbuka bila slot radial bisa diedit
user. Bila segmen < 8, segmen terakhir SELALU navigasi
More/Back (slot 4) — `vis-idx(i)` di `ui/radial.slint` memetakan
segmen ke slot halaman (N=4: Utara/Timur/Selatan = slot 0/1/2,
Barat = More); pointer (`update-pointer`) dan klik tombol
memakai `resolve-sector` yang sama. Submenu luar (TOOLBOX 5
skrip, SEND 2 tab DOC) tetap busur sama rata seperti sebelumnya.

**Perbaikan startup terkait (27 Nov 2026).** `load_theme_from_db`
dulu hanya mengisi prop `app-theme-preset` — warna preset (aksen
dst.) tidak pernah masuk ke prop hex, jadi `apply_appearance`
di akhir memakai aksen default biru `#3B82F6` meski DB berisi
`Appearance.Palette = "spider"` (ditemukan saat verifikasi
radial Spider). Sekarang warna preset disalin ke prop dulu
(`set_hex_pair`); override eksplisit (`Settings.Accent`,
`Appearance.Background.*`, `Settings.Color.*`) tetap menang
karena ditulis setelahnya. DB asli abeng: aksen tetap
`#10B981` (override `Settings.Accent` menang atas merah Spider).

**Terverifikasi 27 Nov 2026 (Xvfb :98, APPDATA terisolasi).**
Build exit 0, 64 test lolos (8 baru: 6 geometri + 2 validasi
pengaturan). Piksel: N=8 divider di 22,0/67,4/112,2/157,8/
202,6/248,0/292,6/337,4°; N=4 di 44,5/135,0/225,5/315,0°;
wedge N=8 membentang ±22,5°. Spider: wedge HUD (32,5,6) =
0,15×`#D9272E`, busur Kaca tepat `#D9272E` (217,39,46);
pelat Kaca (32,32,32), HUD (0,0,0) transparan, Flat
`card-bg` (31,31,31). Kartu Settings tampil + chip
Kaca/HUD/Flat & 2/3/4/5/6/8; klik Kaca + 8 tersimpan di DB
(`Settings.Radial.Style=glass`, `Segments=8`) dan radial
yang dibuka ulang menampilkan Kaca + 8 segmen. **Hotkey
global TERBUKTI bisa dites di Xvfb** (membatalkan catatan
lama "tidak bisa dipicu via xdotool"): Shift+klik tengah
via xdotool (`keydown shift` + `mousedown 2`) membuka radial
di kursor, lepas menutupnya (release-to-execute).
**Masih terbuka:** (1) hover saat tombol tengah DITAHAN tidak
ter-update di X11 (implicit grab X11: move pergi ke window
tempat press terjadi; di Windows tanpa implicit capture
seharusnya jalan — belum terverifikasi di PC abeng); (2)
klik/eksekusi item radial via release-to-execute belum dites
langsung (hanya buka + hover + tutup); (3) radial sekali
ditemukan unmapped di tengah sesi uji Settings — pemicu
hide-nya tidak terekan ulang; buka-ulang via hotkey jalan,
kemungkinan artefak operasi X eksternal (windowraise/
windowmove xdotool), bukan alur user normal; (4) 9–12
segmen menunggu slot radial bisa diedit user.

### 19.8 Pelajaran teknis Fase 4 (26 Nov 2026)

- **Rantai persentase jatuh ke preferred-height.** `height:
  100%` / `vertical-stretch: 1` langsung di `Card` TIDAK
  mengembang (Sheets/Leaderboard terbukti live 2×: kartu
  hanya setinggi isi, void hitam di bawah). Membungkus Card
  dalam `Rectangle { vertical-stretch: 1; }` juga TIDAK
  mengembang. Pelajaran yang sama sudah ada di `main.slint`
  ~536 (konten dashboard pakai tinggi eksplisit). Perbaikan
  yang jalan: tinggi EKSPLISIT `max(300px, root.height -
  232px)` (Sheets) dan `max(330px, root.height - 195px)`
  (Leaderboard) — `max()` menjaga isi tidak terpotong bila
  `root.height` ternyata setinggi isi. `root.height` sendiri
  = tinggi window (800) — aritmetik eksplisit jalan,
  rantai persentase yang gagal.
- **Toolbox scroll tanpa merusak fill.** Tiap tab dibungkus
  `Flickable` (100%×100%) + layout dalam `height:
  max(parent.height, self.preferred-height)` — di layar
  tinggi kartu stretch mengisi area seperti sebelumnya
  (PREVIEW tetap ke y=707 di 1280×800, terverifikasi
  regresi 82); di layar pendek isi melebihi viewport dan
  bisa di-scroll (MULAI PsdBucin terjangkau di 520×600,
  69–71). `self.preferred-height` bisa dibaca di binding
  Slint (pola yang sama sudah dipakai `nama-card-layout.
  preferred-height`).
- **Esc mati saat FocusScope kehilangan fokus.** `KeyBinding`
  Esc di `keys-scope` hanya aktif bila scope memegang fokus;
  klik mouse tidak memberi fokus ke elemen mana pun (Btn =
  TouchArea), jadi begitu dashboard tersembunyi (overlay
  dibuka) / flyout dibuka, fokus hilang dan Esc + Ctrl+,
  mati — terverifikasi live: Esc tidak menutup radial maupun
  flyout, tapi Esc menutup Sheets saat TextInput sedang
  fokus. Perbaikan (`ui/main.slint`): `changed overlay-view
  => { keys-scope.focus(); }` + `changed show-flyout =>
  { if (root.show-flyout) { keys-scope.focus(); } }`.
  Terverifikasi: Esc menutup radial (54) dan mengembalikan
  dashboard dari Leaderboard (79). (Explorer aman:
  `open-explorer` memanggil `focus-list()` SETELAH
  overlay-view diset.)
- **Klik kanan ditelan TouchArea anak.** `toolbox-back-touch`
  / `sheets-back-touch` / `leaderboard-back-touch` adalah
  TouchArea latar, tapi TouchArea anak di atasnya (DropZone,
  LeaderRow) menelan klik kanan — klik kanan DI ATAS DropZone
  / baris tidak kembali ke dashboard (hanya celah kosong
  yang bisa). Perbaikan: `callback right-clicked()` +
  forwarding `pointer-event` right-down di `DropZone` dan
  `LeaderRow`, di-wire ke `root.back()` di 4 pemakaian
  DropZone + 5 LeaderRow. Terverifikasi live (21/25/31).
- **Uji 520×600 butuh ubah konstanta window.** `xdotool
  windowsize` TIDAK me-re-layout: ukuran AppWindow terikat
  ke `Design.window-width/height` (1280×800), jadi window
  X mengecil tapi isi tetap layout 1280 (terpotong). Cara
  uji: ubah sementara `window-width/height` di
  `ui/design.slint` → 520/600, build, uji, KEMBALIKAN ke
  1280/800 + build ulang (sudah dilakukan 26 Nov; pastikan
  tidak tertinggal 520 di commit).
- **Toast menutupi footer sementara di 520×600.** Toast
  diposisikan bottom-anchored; di tinggi 600px ia menutupi
  baris footer Leaderboard/Sheets selama 2,5 dtk. Layout
  di bawahnya sendiri muat (footer terbaca penuh setelah
  toast habis, 73). Kosmetik, bukan overflow layout.
- **sendkey2 `type`:** tanpa Shift (huruf jadi kecil) dan
  melewati spasi ("SDN RANCAIYUH I 2" jadi "sdnrancaiyuhi2")
  — artefak alat uji, bukan bug TextInput. Verifikasi edit
  tetap sah (indikator draft menyala).
- **Klik xdotool kadang gagal:** pakai `mousedown` +
  `sleep 0,4–0,5` + `mouseup` (bukan `click` polos);
  klik kanan kadang flaky — ulangi dengan hold 0,5 dtk
  atau pakai Esc (sesudah perbaikan fokus) untuk kembali.

### 19.9 Pertanyaan terbuka Fase 4 (untuk abeng — jangan ditanya ulang tanpa konteks)

1. Radial submenu TOOLBOX: 3 tab Mantra sudah benar, atau
   5 file `_tb_*.jsx` (klik = toast "dijalankan di
   Photoshop")?
2. Radial SEND-*: pertahankan 2 tab contoh ("Tab 1"/"Tab 2")
   sebagai placeholder, atau slot SEND langsung toast sampai
   DOC dibangun (Fase 5)?
3. Shift+Klik Tengah hanya jalan di celah latar dashboard —
   cukup sebagai sekunder, atau perlu shortcut keyboard
   tambahan?
4. Spreadsheet: pesan guard memakai nilai tersimpan yang
   di-hardcode ("dimuat: 'SDN RANCAIYUH I'") — sesudah
   Simpan, pesannya basi. Perlu dinamis?
   **TERJAWAB 27 Nov 2026 (dari source C#):** tidak ada
   hardcode — `SpreadsheetRowAnchorGuard.Describe`
   (SpreadsheetRowAnchorGuard.cs:63) sepenuhnya dinamis:
   nomor baris pertama yang mismatch + `ActualDisplay`
   (nilai baris di sheet saat ini) vs `ExpectedDisplay`
   (nilai saat dimuat), plus `"(dan N baris lain)"` bila
   lebih dari satu baris bergeser. Port sudah sama
   (`src/spreadsheet_guard.rs: describe`, terverifikasi
   klik 27 Nov: "baris 1 di sheet berisi '47276395',
   sedangkan yang dimuat '47276395'"). Catatan: yang
   ditampilkan adalah sel pertama non-kosong baris itu
   (`DisplayOf`), jadi bila sel yang berubah bukan kolom
   pertama, kedua nilainya bisa tampak sama — itu perilaku
   C# apa adanya, bukan bug port.
5. Spreadsheet: draft bertahan pindah overlay (mockup)
   vs C# yang membuang draft sel yang berubah saat
   reload — perilaku mana yang dipakai?
   **TERJAWAB 27 Nov 2026 (dari source C#):** premis
   pertanyaan SALAH — C# TIDAK membuang draft itu.
   `SpreadsheetDraftStore.Apply` (SpreadsheetDraft.cs:81):
   draft yang nilai aslinya sudah tidak cocok dengan sheet
   ("Sheet sudah berubah di sel itu: jangan timpa, simpan
   sebagai sisa") masuk `remaining`, dan
   `RestoreDraftsAsync` (SpreadsheetViewModel.cs:719)
   menulis ulang `remaining` ke penyimpanan. Draft hanya
   tidak DITERAPKAN ke grid saat reload, tapi tetap
   tersimpan dan dicoba lagi di reload berikutnya. Port
   sudah sama (`reload` → `remaining`, terverifikasi klik
   27 Nov: setelah Muat Ulang B1 menampilkan nilai baru
   sheet, draft tetap ada di `Spreadsheet.Draft`).
6. Spreadsheet: draft hanya di memori sesi — persistensi
   antar-sesi (SQLite?) atau cukup sesi?
   **TERJAWAB 27 Nov 2026 (dari source C#):** persist
   antar-sesi di SQLite. `PersistDraftsAsync`
   (SpreadsheetViewModel.cs:680) menulis JSON draft ke
   key `Spreadsheet.Draft` (`SpreadsheetDraftStore
   .StorageKey`); `RestoreDraftsAsync` membacanya kembali
   saat load. Port sudah sama (key `Spreadsheet.Draft`,
   persist tiap edit + tiap save/refresh, restore saat
   startup; terverifikasi: draft bertahan di DB setelah
   Muat Ulang dan setelah restart aplikasi).
7. Leaderboard: perunggu `#cd7f32` masih literal, belum
   token Design — tokenisasi nanti.
8. Data contoh: EDITING 24 / REVISION 7 (widget, diubah
   saat kerja Trello bersamaan) vs toast daftar "5 kartu" —
   angka mana yang benar? (lihat §19.5 butir 1).

## 20. Explorer (Build B / LANGKAH 3, 26 Nov 2026)

Overlay layar penuh `"EXPLORER"` (`ui/explorer.slint` + `ui/explorer-data.slint`
+ `src/main.rs`; wiring di `ui/main.slint` + `ui/logpanel.slint`).
Dibuka dari tab EXPLORER Log Panel (tombol "⧉ Buka Window Baru" → root
`D:\#GAWENA`; klik baris folder → folder itu; klik baris file → root).
Log Panel disembunyikan saat overlay terbuka. Ditutup via tombol × di
header, Esc (prioritas §20.6), atau klik kanan di latar overlay
(`explorer-back-touch`, TouchArea anak pertama root — arahan abeng
26 Nov 16:58, §10.11; BATCH dikecualikan).

### 20.1 Yang dibangun

- **Tab bar (maks 4):** tab = path folder; "+" tambah tab; × tutup tab
  (tab terakhir ditutup → overlay ikut tertutup; Ctrl+W pada
  satu-satunya tab tidak berbuat apa-apa). Satu tab unpinned →
  tampilkan nama folder aktif rata tengah sebagai judul (bukan tab bar).
  Pin via menu tab → badge 30px berisi inisial maks 2 huruf + warna
  palet 8 (`#F97316` oranye default, `#8B5CF6`, `#10B981`, `#3B82F6`,
  `#F43F5E`, `#FBBF24`, `#06B6D4`, `#EC4899`); pin memindah tab ke
  posisi 0. Menu tab (klik kanan badge / tombol ⋯): Pin Tab, Unpin Tab,
  Change Color (siklus 8 warna), Rename… (label 2 huruf), Close Tab.
  Back/Forward satu stack bersama antar tab (maks 10), dibersihkan
  saat ganti tab.
- **Path bar:** Back / Forward / Up / Refresh, address bar dual-mode
  (breadcrumb display-only — segmen TIDAK bisa diklik; klik permukaan
  → mode edit TextField + SelectAll, Enter commit hanya untuk folder
  yang dikenal — kalau tidak, error toast + batal — Esc batal),
  StatusSummary ("N items" / "N items, M selected"), tombol Cari
  (Ctrl+F). Up tidak bisa naik di atas RootPath (`D:\#GAWENA`); Up
  untuk folder generik = folder sebelumnya.
- **Sidebar Quick Access (120px, sembunyi bila lebar < 700px):**
  `D:\#GAWENA`, `27_SDN RANCAIYUH I`, `28_SDN BANTAR KAMBING 03`,
  `D:\#DATA ABENG\MASTER` — klik → navigasi; klik kanan item →
  "Remove Pin" (hapus + toast). Gear di bawahnya → Settings grup
  Explorer (`settings-group = "Explorer"`).
- **5 layout** (tombol teks Detail/Daftar/Thumb/Tiles/Split + shortcut
  Ctrl+Shift+1..5, disebut di toast saat ganti layout): Vertical
  (baris detail 26px: ikon + nama + ukuran + tanggal `dd MMM yyyy` +
  kolom Lokasi saat search), Horizontal (kartu ringkas, COLS 5),
  Thumbnail (grid kartu 140px, COLS 6; thumbnail = kotak warna +
  glyph), Tiles (kartu 240×56, COLS 3), Split (dua kolom satu scroll:
  kiri = grup ekstensi terbanyak — folder = grup "Folder" — kanan =
  sisanya; 1 grup → belah dua). Default Vertical. **TIDAK ADA tombol
  cycle-view-mode** (CycleViewMode sudah hilang di source kini —
  koreksi parent 26 Nov 17:13).
- **Sort (Name/Date/Type + Asc/Desc) + Group (None/Date/Type,
  default Date):** folder dulu, lalu shortcut (.lnk), lalu file — di
  semua sort; natural sort ("2" sebelum "10"); Desc membalik tiap
  kelompok terpisah. Group Date: Today → Yesterday → Earlier this
  week → Last week → Earlier this month → Last month → A long time
  ago (header tidak bisa dipilih).
- **Search:** TextField di header (Ctrl+F fokus); debounce 300ms
  (Timer, ketikan baru reset timer); rekursif (folder ini + subfolder);
  `*`/`?` literal; Enter = buka hit pertama; hasil menampilkan kolom
  Lokasi (nama folder induk); 0 hit → "Tidak ada hasil untuk …";
  status "Mencari…" saat debounce; cap 5000 + catatan "(dibatasi)"
  bila melebihi cap. Menutup search / pindah folder mengosongkan search.
- **Type-ahead:** ketik huruf/angka tanpa modifier saat daftar fokus →
  lompat ke item pertama yang namanya diawali buffer (case-insensitive,
  diputuskan di Rust); buffer reset bila jeda > 800ms; Esc mengosongkan.
  Implementasi: `FocusScope list-scope` + `key-pressed` + callback Rust
  `ex-typeahead` → `ta-index` → `select-index`. **Status: BELUM
  terverifikasi runtime** — Ctrl+F via injeksi key tidak membuka search
  saat startup (list-scope belum fokus; alur nyata via Log Panel
  memanggil `focus-list()`), sehingga type-ahead belum terbukti jalan;
  jangan klaim jalan sampai diuji dengan klik-daftar-dulu + ketik
  (lihat §20.5).
- **Preview (kanan, 280px; sembunyi bila lebar < 900px):** hanya SATU
  file terpilih — `.txt` isi penuh, `.docx` placeholder "(Preview for
  .docx: open with default app to view.)", file lain/folder/multi-select
  → panel tertutup.
- **Operasi file** (state lokal + toast + Task Monitor): New Folder /
  New File (dialog: TextField nama, Enter = buat, Esc = batal; file
  baru tanpa "." otomatis +.txt; folder baru langsung mode rename
  inline), Rename inline (F2 / ctx: hanya bagian nama di-select,
  ekstensi tidak ikut; Enter commit, Esc cancel), Delete (Recycle Bin,
  TANPA konfirmasi + toast), Permanent Delete (Shift+Delete — SELALU
  dialog konfirmasi), Duplicate (dialog stepper 1–100 → "x - Copy",
  "x - Copy (2)" …), Copy/Cut/Paste (cut: opacity 0.45 + payload cut
  terpisah lintas tab; cut+paste ke folder sama = no-op toast "Sudah
  di folder ini"; paste folder ke subtree-nya sendiri diblok; bentrok
  nama → "x - Copy"), Copy Path (multi-path newline; tanpa seleksi →
  CurrentPath), Select All (Ctrl+A), Undo (Ctrl+Z, maks 50 — §20.4).
  Pengaman: folder yang dibuka di tab lain + ancestor-nya tidak bisa
  dihapus (toast bila dicoba).
- **Buka file:** double-click / Enter — folder → navigasi; `.lnk` ke
  folder → navigasi ke target; `.jpg/.jpeg/.png` → toast aplikasi
  default; `.psd/.tif/.webp` → toast Photoshop smart-open; lain →
  toast aplikasi default; "Open with…" di ctx → toast.
- **Context menu item** (klik ⋯ / klik kanan): Open, Open in New Tab
  (folder saja), Copy, Cut, Paste (bila clipboard ada isi), Rename,
  Duplicate, Delete, Permanent Delete, Copy Path, "BUAT MASTER" (bold,
  di bawah separator) — **"BUAT MASTER" HANYA di Vertical/Horizontal/
  Split, TIDAK di menu background dan TIDAK di Thumbnail/Tiles**
  (spec §2.9). Menu background (klik area kosong): New Folder, New
  File, Paste, Select All, Refresh, "This Folder Only" (checkbox).
- **"Kirim ke BATCH" (toolbar) + "BUAT MASTER" (ctx):** kirim folder
  ke BATCH (set `src-name` + `src-folders` sesuai folder itu +
  `has-manifest = true` + pindah ke tab BATCH + `overlay-view = ""`).
  Toast toolbar: "Dikirim ke BATCH: …"; toast ctx: "BUAT MASTER:
  \<folder\> → Batch (fase 1: manifest dibuat, template dibuka di
  Photoshop)". Folder sekolah A/B → 4 subfolder BATCH-nya; "1. PAS
  FOTO" → KELAS 1 B; folder generik → isi generiknya.
- **Task Monitor:** kartu mengambang pojok kanan-bawah (420px), muncul
  otomatis saat operasi background dimulai, tanpa mencuri fokus; badge
  COPY (biru) / MOVE (oranye) / DELETE (merah), nama file, progress
  bar + % + ukuran + kecepatan + ETA (Timer 120ms, +4/tick), tombol
  Batalkan (berjalan) / Coba lagi (gagal) / Hapus dari daftar; footer
  "Bersihkan yang selesai"; TIDAK menutup otomatis — hanya tombol ×.
- **Multi-select:** klik toggle item (tanpa batas, beda dengan BATCH
  maks 3); pil bawah-tengah "N items selected" bila > 1 terpilih.

### 20.2 Pendekatan sort/group (Slint tidak bisa sort array)

Dataset per folder SUDAH terurut per sort — `ExplorerData`
(`ui/explorer-data.slint`, digenerate `tools/gen_explorer_data.py`,
satu sumber kebenaran) berisi tabel kanonis per folder (urutan
name-asc) + permutasi indeks per sort (Name/Date/Type × Asc/Desc),
grup (Date/Type + array header), dan split. `ExplorerView` memilih
`disp-order` lewat properti perantara `ord-*` per varian, baru
diseleksi per sort/group. Natural sort dihitung di generator
(`natkey`: angka sebagai angka). Tanggal contoh relatif ke 26 Nov 2026.

### 20.3 Search + type-ahead di Rust

String Slint tidak punya `contains`/substring/index-of — operasi
string lewat callback Rust (`src/main.rs`): `ex-search` (substring
case-insensitive, scope subtree, urutan folder → link → file
name-asc, cap 5000 + `truncated`, hasil via properti in-out `sr-*`
termasuk `sr-paths` + `sr-half` untuk Split), `ex-typeahead` (prefix
case-insensitive di folder itu, urutan kanonis → `ta-index`),
`ex-add-txt-ext`, `ex-initials`, `ex-split-ext`, `ex-name-base`,
`ex-is-typeahead-char`. Tabel `EXPLORER_ITEMS` + `EXPLORER_SUBTREE`
ditempel dari `tools/explorer-data-rust.txt` (digenerate).

### 20.4 Undo maks 50 + cut terpisah (K2)

Stack undo 50 entri (`undo-kinds`/`undo-names`/`undo-extra`, array
52 elemen pola map-per-slot, `undo-count = min(count+1, 50)`).
Kind: `new`/`rename`/`copy`/`move`/`recycle` — recycle = info toast
saja ("Sudah di Recycle Bin — restore dari sana"); permanent delete
tanpa entri undo. Cut memakai payload TERPISAH dari copy
(`cut-names`/`cut-folder` vs `cb-names`/`cb-folder`/`cb-mode`),
item ter-cut opacity 0.45 dan bertahan lintas tab.

### 20.5 Yang disederhanakan / belum terverifikasi

- **Mutasi murni Slint, satu snapshot:** operasi file mengubah array
  tampil via pola "map per slot" + `live-mode`; setelah mutasi, urutan
  tampil = urutan array. Hanya SATU snapshot (`snap-*`, folder terakhir
  yang dimutasi) yang bertahan — pindah folder lalu kembali memuat ulang
  data kanonis dan mutasi di folder lain hilang. Keterbatasan mockup.
- **Type-ahead belum terverifikasi runtime** (lihat §20.1) — butuh uji
  klik-daftar-dulu + ketik; bila tidak jalan di Slint, catat sebagai
  pertanyaan terbuka, jangan pura-pura jalan.
- **Pin tab tidak persisten antar sesi** (tidak ada kunci
  `Explorer_PinnedTabs`; Slint tanpa I/O file — pin hidup di memori).
- **Unpin tidak mengurut ulang tab;** tab pin selalu di posisi 0.
- **Ikon bukan ikon sistem Windows asli** (glyph/emoji + kotak warna);
  Slint tidak bisa mengekstrak ikon shell.
- **Zoom ikon Ctrl+roda mouse TIDAK ada** — Slint 1.19 tidak punya
  event roda mouse di `TouchArea`.
- **Menu konteks posisi tetap** (x = lebar−280, y = 160, lebar 240),
  bukan di posisi kursor — supaya tidak menabrak header kolom
  (y awal 140 menabrak header, digeser ke 160 setelah screenshot).
- **Toolbar scroll horizontal di layar sempit** (Flickable): di 520px
  semua tombol tetap ada + label utuh, digeser — "Kirim ke BATCH"
  tercapai dengan scroll, bukan terpotong hilang.
- **Breadcrumb display-only** (segmen tidak bisa diklik).
- **Guard hapus** hanya mode browse (cek subtree dalam di mode search
  tidak diimplementasi).
- **Data contoh hero vs daftar Fase 3** (EDITING 24 vs 6, REVISION 7
  vs 5) — TIDAK diubah, tetap pertanyaan terbuka untuk abeng.
- **Master File Browser DITUNDA** (abeng 26 Nov 15:57) — tidak
  dibangun; tab MASTER Log Panel tetap daftar + toast.

### 20.6 Shortcut Explorer

| Aksi | Shortcut |
|---|---|
| Buka Log Panel tab EXPLORER | Ctrl+L (global, `keys-scope`) |
| Fokus address bar | Alt+D (`@keys(Alt + D)`; Ctrl+L dipakai Log Panel) |
| Fokus search | Ctrl+F |
| Rename | F2 |
| Undo | Ctrl+Z (maks 50) |
| Select all | Ctrl+A |
| Copy / Cut / Paste | Ctrl+C / Ctrl+X / Ctrl+V |
| Delete (Recycle Bin) | Delete |
| Permanent delete (konfirmasi) | Shift+Delete |
| Layout Vertical/Horizontal/Thumbnail/Tiles/Split | Ctrl+Shift+1..5 |
| New Tab / Close Tab | Ctrl+T / Ctrl+W |
| Back / Forward / Up | Alt+Left / Alt+Right / Alt+Up |
| Esc (prioritas) | dialog → tab-ctx → ctx → rename → addr-edit → search → buffer type-ahead → tutup overlay |

Esc global di `main.slint`: bila `overlay-view == "EXPLORER"` →
`explorer-view.esc-pressed()` (bukan langsung tutup overlay).

### 20.7 Responsif

- Lebar < 700px: sidebar Quick Access disembunyikan; 5 tombol layout
  tetap tampil (tanpa tombol siklus — CycleViewMode tidak dibangun).
- Lebar < 900px: preview disembunyikan.
- Toolbar: Flickable horizontal (lihat §20.5).
- Log Panel disembunyikan saat overlay terbuka (overlay layar penuh
  seperti Settings) — keputusan Build B untuk 520px + Log Panel.

### 20.8 Verifikasi (26 Nov 2026)

- Build bersih (`cargo build` exit 0, Slint 1.19.1 + Rust 1.100).
- Screenshot (semua dibaca kembali + diperbaiki yang salah):
  `~/workspace/rust-gui-contoh/bmachine-slint-explorer.png` (Vertical
  1280×800), `bmachine-slint-explorer-thumb.png` (Thumbnail 1280×800),
  `bmachine-slint-explorer-search.png` (search "template" → 3 hasil +
  kolom Lokasi 1280×800), `bmachine-slint-explorer-ctx.png` (ctx menu
  + "BUAT MASTER" 1280×800), `bmachine-slint-explorer-taskmon.png`
  (Task Monitor COPY 40% 1280×800),
  `bmachine-slint-explorer-small.png` (520×600).
- Ctrl+L terverifikasi live via injeksi key X11 (`tools/sendkey2.c`,
  `XSendEvent` + `XSetInputFocus`): Log Panel tab EXPLORER terbuka.
- Keyboard search via injeksi key TIDAK membuka search saat startup
  (list-scope belum fokus) — search terverifikasi lewat state awal
  sementara + debounce timer; alur nyata (buka via Log Panel →
  `focus-list()`) memanggil fokus secara eksplisit.
- Temuan saat verifikasi + perbaikan: (a) `loc` hasil search folder
  generik sempat `D:\#GAWENA` — diperbaiki: path representatif gen =
  `D:\#GAWENA\08 AGUSTUS 2026`, loc = "08 AGUSTUS 2026"; (b) menu
  konteks y=140 menabrak header kolom → y=160; (c) toolbar meluber
  di 520px → Flickable horizontal; (d) window selalu 1280×800
  (`Design.window-width/height`) — screenshot 520×600 butuh override
  sementara ukuran window.
- Catatan lingkungan: service restart sempat mematikan Xvfb + menghapus
  `libxkbcommon-x11` (app panic) + isi `/tmp` — Xvfb dinyalakan ulang,
  `libxkbcommon-x11-0` di-install ulang dari apt cache (menunggu lock
  dpkg lepas dulu); alat uji keyboard kini disimpan persisten di
  `tools/sendkey2.c` (jangan hanya di `/tmp`).

## 21. Notch Hidup (Dynamic Bar rework — notch ala Nusa + Dynamic Island) — DIREWORK 27 Nov 2026 (abeng, 27 Nov 20:18–20:21)

Pill 560×46 yang SELALU menampilkan angka (`fd4ade3`) DITOLAK abeng 27 Nov 20:18 — maunya notch beneran. Referensi utama: **Nusa** (rama2555.github.io/nusa) — "a living notch for Windows", native Rust: pill hitam di atas-tengah, hover untuk membuka, maskot sepasang mata putih bercahaya yang muncul saat idle, tertidur, dan menyapa saat user kembali. TIDAK ada voice di Nusa — voice adalah tambahan kita sendiri.

### Perilaku

- **Notch minimal (mode Notch, default):** diam = pill hitam 150×32 di TENGAH ATAS LAYAR (monitor utama, y=0), isinya maskot — sepasang mata putih bercahaya. TIDAK ada angka yang terlihat langsung. Border pill mengikuti warna aksen tema aktif (di Spider: merah). **Pin layar (27 Nov 2026, abeng 22:44 + 22:57):** notch NEMPEL di tengah atas monitor ala Nusa/macOS Dynamic Island — window utama dipindah/diresize, notch TIDAK ikut bergerak; berlaku untuk mode Notch DAN mode Pet, keduanya melayang di atas semua window (window-nya sendiri sudah `always-on-top: true`). Panel mengembang (560px) juga tetap terpusat di tengah layar. Implementasi: ukuran layar dari `rdev::display_size()` (sudah dependensi, tanpa crate baru) — Windows = monitor utama (`GetSystemMetrics` SM_CXSCREEN/SM_CYSCREEN), Linux = ukuran display X; bila display tak terbaca, fallback ke tengah window utama (perilaku lama). Posisi dihitung lewat `centered_x(lebar_wadah, lebar_bar)` di `src/main.rs` + `recenter_notch` (dipakai saat mengembang/mengecil). Multi-monitor Linux: `display_size()` mengembalikan bounding box virtual, jadi "tengah layar" = tengah bounding box — per-monitor (RandR) belum dipakai.
- **Hover mengembang:** mouse di-hover → window membesar 560×(pill + 8 + panel) dan menampilkan 4 chip angka widget dashboard (EDITING/REVISION/LATE/POINTS — sumber data SAMA dengan dashboard, disalin tiap 2 detik oleh timer sinkronisasi Rust, tidak ada angka baru) + baris kontrol volume OS + daftar notifikasi (maks 5 terbaru) + footer "Buka Dashboard". Mouse menjauh → mengecil lagi (tutup tertunda 400 ms; hover-hold counter supaya panel tidak berkedip saat kursor pindah antar elemen).
- **Maskot hidup:** mata berkedip (timer 3,4 detik, tutup 140 ms); pupil mengikuti kursor saat di-hover; idle 45 detik → mata tertutup (tidur); hover kembali setelah tidur → mata membesar menyapa 1,6 detik. Semua transisi mata/mulut dianimasikan 250 ms ease-in-out (Slint `animate`).
- **Gaya mata (27 Nov 2026, abeng 21:08 — ref `~/workspace/pet-refs/eyes-ref.gif`):** bentuk mata maskot bisa dipilih di Settings → Appearance → Notch ("Gaya mata", OptionChips): **Nusa** (oval putih bercahaya + pupil hitam, bawaan), **Baymax** (dua titik putih yang dihubungkan garis horizontal — di GIF referensi titik + garisnya HITAM di atas latar terang, tapi di atas pill hitam BMachine warnanya DIBALIK jadi putih supaya terbaca), **Bulat** (lingkaran putih besar + pupil, tanpa glow). Gaya berlaku ke mata notch (pill minimal + panel mengembang) DAN mata blob pet bawaan — BUKAN sprite PetDex (sprite adalah wajahnya sendiri, gaya mata tidak memengaruhinya). Ukuran mata (eye-w/eye-h) tetap dihitung dari mood (idle/happy/surprised/tidur/kedip) sehingga SEMUA animasi yang sudah ada tetap jalan di semua gaya: kedip (mata menciut jadi garis 2px), pupil ikut kursor (gaya Nusa/Bulat: pupil bergeser ±3px; gaya Baymax: titiknya sendiri yang bergeser ±3px karena tidak ada pupil), tidur (garis 2px — di Baymax menyatu dengan garis penghubung jadi satu garis panjang), sapaan (mata membesar). Simpan di DB key `Settings.Notch.EyeStyle` (nilai tak dikenal → "nusa" via `valid_eye_style()`); berlaku live tanpa restart (bar yang sedang tampil di-set ulang lewat `open_dynamic_bar_window`, sama seperti mode notch/pet). Implementasi: komponen `MascotEyes` (sepasang mata + garis Baymax di belakang mata) di `ui/windows.slint`, menggantikan dua instans `MascotEye` yang terpisah; `MascotEye` menerima prop `eye-style` (glow hanya Nusa, pupil hanya bukan-Baymax, "bulat" memaksa lingkaran diameter = sisi terpanjang).
- **Mode Avatar Pet (Settings → Appearance → Notch):** maskot jadi avatar bulat 84×84 yang bisa di-drag ke mana saja; bergerak sendiri (wander) dengan glide halus — timer 120 ms, tiap tick bergerak 8% dari sisa jarak, dibatasi 1–5 px/tick (ease-out, maks ~41 px/detik, BUKAN lompat); target baru tiap 4–8 detik / saat sampai, min 60 px dari posisi sekarang, selalu di dalam LAYAR (bukan window utama lagi — diubah 27 Nov 2026, pin layar); tidak wander saat di-hover, saat panel mengembang, atau 5 detik setelah di-drag. Posisi default pet (belum pernah di-drag) = tengah atas layar, y=60. Posisi pet tersimpan di SQLite (`DynamicBar.X`/`DynamicBar.Y`) dan dipulihkan saat dibuka lagi. Mode notch selalu di tengah atas layar (posisi tidak disimpan).
- **Ekspresi pet (blob bulat, ref `~/workspace/pet-refs/`):** wajah = mata + mulut putih saja (bukan pixel-art). idle: mata bulat + mulut garis kecil; happy (sapaan / poin bertambah): mata sipit + mulut senyum; surprised (kartu baru masuk): mata besar; sleepy: mata garis + mulut titik. Mood dari event bertahan 3–4 detik lalu kembali idle; tidur/sapaan dihitung di Slint. **SYARAT TEGAS abeng: gerakan HARUS smooth** — ekspresi di-interpolasi (animate 250 ms), posisi di-glide (timer 120 ms), jangan patah-patah.
- **Voice (Windows SAPI, offline, gratis):** maskot mengucapkan kalimat pendek saat event — kartu baru masuk ("Ada kartu baru masuk di EDITING.") dan poin bertambah ("Poin bertambah menjadi 4300.", angka polos tanpa pemisah ribuan). Throttle 1,5 detik antar ucapan. Pengaturan di Settings → Appearance → Notch: on/off, pilih suara (ComboBox, "Otomatis (Indonesia bila ada)" = voice id-ID bila ada, kalau tidak ada voice pertama sistem), volume 0–100, tombol "Tes suara". SAPI hanya ada di Windows — semua kode voice di-gate `cfg(target_os = "windows")` dengan stub di OS lain supaya build Linux lolos. **Voice HANYA bisa dites di PC Windows abeng, bukan di Linux — belum terbukti bunyi.**
- **Kontrol volume OS di panel mengembang** (abeng 27 Nov 20:41 — shortcut radial volume DIBATALKAN, diganti ini): slider + ikon mute + teks persen. Windows: Core Audio `IAudioEndpointVolume` via PowerShell inline C#; Linux: `pactl` (PulseAudio) lalu `wpctl` (PipeWire); bila tidak ada server audio → slider dinonaktifkan (tampil "—"), bukan error. Set volume di-debounce 150 ms (hanya nilai terakhir yang diterapkan ke OS); semua pemanggilan subprocess ber-timeout supaya UI tidak blokir.
- **Yang dipertahankan dari `fd4ade3`:** logika notifikasi (`record_list_sync` / `record_points`, maks 3/sync, maks 10 disimpan, 5 terbaru ditampilkan, hanya di memori sesi); klik chip → tampilan terkait di window utama; klik baris notifikasi → tampilan terkait; klik kanan → tutup (`Settings.DynamicBar.Visible=false`); toggle buka lagi dari flyout profil; tema ikut preset aktif via `sync_design_to_bar` (15 token, saat dibuka + saat tema diterapkan + tiap tick 2 detik); klik diproses di `pointer-event(up)` supaya drag tidak memicu aksi; buka ulang = window baru (reuse hide→show tidak andal di Slint).
- **Panel mengembang saat di-drag** (mode pet): posisi panel ditahan di dalam LAYAR (clamp x ≤ lebar_layar−560, y ≤ tinggi_layar−420 — dulu: window utama, diubah 27 Nov 2026, pin layar); drag saat panel terbuka = posisi baru pet (tidak dikembalikan ke posisi sebelum mengembang).

### PetDex — loader (SELESAI 27 Nov 2026, commit `pet:`)

Pet bisa dari PetDex (petdex.dev) — model seperti skill di Hermes (install/tambah/hapus paket): galeri publik 5340+ pet, gratis, kode MIT, API manifest terbuka tanpa login (`https://petdex.dev/api/manifest` → redirect ke `https://assets.petdex.dev/manifests/petdex-v1.json`, tiap entri: slug, displayName, submittedBy, spritesheetUrl, petJsonUrl, zipUrl, spriteVersionNumber). Satu pet = folder berisi pet.json + spritesheet (WebP/PNG); grid atlas 8 kolom × 9 baris (v1) atau 8×11 (v2), frame 192×208; 9 state per baris, urutan: idle, running-right, running-left, waving, jumping, failed, waiting, running, review. pet.json minimal (id, displayName, description, spriteVersionNumber, spritesheetPath) — frame count/timing adalah konvensi format, diambil dari renderer open-source PetDex (`petdex-core/src/animation.rs`, MIT): Idle 6 frame [280,110,110,140,140,320] ms, RunningRight/Left 8 [120×7,220], Waving 4 [140×3,280], Jumping 5 [140×4,280], Failed 8 [140×7,240], Waiting 6 [150×5,260], Running 6 [120×5,220], Review 6 [150×5,280]. Install = tambah folder ke `<APPDATA>/BMachine/Pets/<slug>/` (unduh pet.json + spritesheet, BUKAN zip — tidak ada crate `zip` offline); hapus = hapus foldernya; tanpa akun/sinkronisasi PetDex. Tampilkan kredit pembuat (submittedBy) + link ke halaman pet (`https://petdex.dev/pets/<slug>`, tombol "Buka" di daftar); kebanyakan pet fan art, jangan diklaim sebagai milik BMachine. **Perangkap nyata:** galeri mengirim `submittedBy: null` di sebagian entri — `#[serde(default)]` tidak menerima null eksplisit, jadi semua field String/u32 manifest + pet.json di-parse dengan deserializer toleran-null (`de_null_string`/`de_null_u32` di `src/petdex.rs`); tanpa ini parse seluruh manifest gagal ("error decoding response body"). **Geometri sheet TIDAK diambil dari manifest** — field `spriteVersionNumber` manifest bisa basi/salah (Boba: manifest bilang v1, sheet aslinya v2 8×11); geometri selalu diturunkan dari sheet yang sudah di-decode (lebar % 8 == 0, tinggi % 208 == 0, baris ≥ 9).

- **UI:** Settings → Appearance → Notch → "Pet": ComboBox pet aktif ("Blob bawaan" + pet ter-install), field slug/nama + tombol Install (busy "Mengunduh…", unduhan di thread sendiri), daftar `PetRow` (nama + "oleh pembuat" + Buka + Hapus; klik baris = jadikan aktif). Pet aktif tersimpan di DB key `Settings.Notch.Pet` ("" = blob), berlaku langsung tanpa restart.
- **Renderer:** sheet di-decode sekali saat pet dipilih (crate `image`, WebP/PNG → RGBA8 → `slint::Image::from_rgba8`), tampil sebagai satu `Image` di dalam `clip: true` 84×84, digeser per frame (kolom × lebar-frame, baris × tinggi-frame); satu frame tampil setinggi pill pet (84px), lebar ikut rasio frame (192×208 → ~77px). Sprite menggantikan wajah blob hanya di mode pet; pill notch tetap bermata blob.
- **Animasi + mapping state (TANPA sistem mood baru):** timer Rust 50 ms memajukan `PetAnimationEngine` (port `petdex-core`: `set_state` reset ke frame 0, `tick(dt)` maju per durasi frame, looping); bergerak (dx window ≠ 0) → RunningRight/RunningLeft sesuai arah; happy → Waving; surprised → Jumping; selebihnya Idle (tidur juga Idle — baris idle sudah berisi frame mata terpejam). Failed/Waiting/Running/Review ada di engine untuk nanti. **TIDAK ada fade opacity antar state** — percobaan awal (opacity 0 → 1, 200 ms) membuat sprite tidak pernah terlihat: state berbalik Idle ↔ Running tiap langkah wander (~100 ms) sehingga `fade_until` selalu di-reset; yang membuat animasi smooth adalah durasi per-frame engine (tidak ada frame yang dilewat/dilompati) + wander yang sudah di-ease.
- **File:** `src/petdex.rs` (manifest, install/hapus, decode, engine + 8 unit test + 1 test live `#[ignore]`), `src/main.rs` (`PetRuntime`, `apply_pet_to_bar`, `refresh_pet_lists`, timer frame 50 ms, 4 handler Settings), `ui/windows.slint` (props `pet-sprite*` + render sprite di `DynamicBarWindow`), `ui/settings.slint` (`PetRow` + grup Pet), `ui/main.slint` (props `app-pet-*` + callback). Dep `image 0.25` (`default-features = false`, fitur `webp` + `png`).

### File

- `src/petdex.rs` — loader + renderer PetDex (manifest, install/hapus, decode sheet, `PetAnimationEngine`); lihat § PetDex di atas.
- `src/voice.rs` — SAPI via PowerShell `System.Speech`; `VoiceConfig`, throttle 1,5 detik, `pick_default_voice` (id-* dulu), `list_voices`, `speak_event`/`speak_test`.
- `src/volume.rs` — Core Audio (Windows) / pactl / wpctl (Linux); `get_volume` / `set_volume` (debounce 150 ms) / `set_muted`; parser output + unit test.
- `ui/windows.slint` — `MascotEye` (mata + glow + pupil, blink 3,4 detik, tidur 45 detik, sapaan 1,6 detik), `VolumeSlider`, `DynamicBarWindow` (mode notch 150×32 / pet 84×84; panel mengembang: 4 chip + volume + notifikasi + footer).
- `ui/settings.slint` + `ui/main.slint` — seksi "Notch" di grup Appearance (mode, suara on/off, pilih suara, volume, tes suara); properti `app-notch-*`.
- `src/main.rs` — `open_dynamic_bar_window` (mode-aware), `recenter_notch`, `refresh_bar_volume`, timer wander halus 120 ms, mood event (`pet_mood_until`), handler `on_notch_settings_changed` / `on_test_notch_voice`, pemuatan `Settings.Notch.*` saat startup.

Referensi (dari catatan awal):
- **Nusa** (rama2555.github.io/nusa) — notch hidup di atas layar Windows 10/11: musik + lirik sinkron, timer Pomodoro, file shelf, todo, jadwal sholat, clipboard, widget; native Rust, ~30 MB, 0% CPU saat idle.
- **Oyenza Studio** (studio.oyenza.com, via Threads @lucacadl 25 Okt 2026) — "A living notch", Windows + Mac; contohnya panel Prayer (Subuh/Dzuhur/Ashar/Maghrib/Isya + Qibla).
- Referensi Threads kedua (share/Hn2U2xEMY, dikirim 26 Nov 19:19) — **Dynamic Edge** (Threads @eyuel_eng 10 Nov 2026, "Dynamic Edge - The Dynamic Island for Windows"): WinUI 3 + Windows App SDK, media controls, clipboard history, app/folder launcher, timer, volume, lirik sinkron (bisa pop-out jendela sendiri), gratis di Microsoft Store.

## 22. Sistem Tema — preset terkurasi ala Omarchy (abeng, 27 Nov 19:02–19:06)

Customisasi BMachine mengikuti model Omarchy OS (DHH — Arch +
Hyprland, "omakase"): default opinionated yang langsung jadi,
ringan, sekali klik ubah seluruh aplikasi secara konsisten.

- **Satu tema = satu palet penuh.** `src/theme.rs::ThemePalette`
  (id, nama, mode, accent, bg, fg, editing, revision, late,
  points, widget-style) + `builtin_presets()` — 8 preset
  terkurasi: `bmachine-dark`, `bmachine-light`, `midnight-oled`,
  `catppuccin-mocha`, `emerald-studio`, `spider`, `gwen`,
  `armor`. Palet C# (`AppearancePalette.cs`) dipakai apa adanya
  untuk spider/gwen/armor/midnight; sisanya palet port.
  Definisi palet SATU tempat di Rust — Slint hanya memanggil id.
  **Gaya latar widget per preset** (bagian dari palet, 27 Nov
  2026 — C# tidak punya padanan, invensi port): solid =
  `bmachine-dark`, `bmachine-light`, `armor`; outline =
  `midnight-oled`; gradient = `catppuccin-mocha`, `spider`;
  glass = `emerald-studio`, `gwen`. Lihat §8 untuk arti
  keempat gaya (solid/gradient/glass/outline) dan bedanya
  dengan pola latar widget (`widget-bg`: doodle/dots/impulse/
  none — motif dekorasi, BUKAN permukaan).
- **Satu palet dibaca semua permukaan.** `apply_appearance()`
  menurunkan SEMUA token global `Design` (warna, `cat-*`, font,
  spacing, radius) dari properti AppWindow; dashboard, nav,
  Settings, Trello, spreadsheet, dialog, radial, log panel, splash
  dan flyout membaca token yang sama — tidak ada warna hardcode
  per permukaan. Preset mengatur juga 4 warna widget
  (`Design.cat-editing/revision/late/points`).
- **Picker di grup Appearance** (`ui/settings.slint`): tiap preset
  satu `ThemeRow` + preview 4 titik (aksen, revisi, bg, fg) +
  ✓ saat aktif. Di bawahnya: **Tema saya** (tema buatan user),
  **Kepadatan** (Compact/Normal/Comfortable — C#
  `Settings.UiDensity`), **Warna widget** (C# `Settings.Color.*`),
  **Pola latar widget** (`widget-bg`: doodle/dots/impulse/none —
  motif dekorasi di atas kartu), **Gaya latar widget**
  (`widget-style`: Solid/Gradien/Kaca/Outline — permukaan
  kartu; pilih manual = override, preset jadi `custom`). Preview
  miniatur di bawah ikut gaya widget aktif.
- **Tema user = override berlapis.** Simpan snapshot tampilan
  saat ini dengan nama → masuk picker (id `user:<slug>`).
  Ubah warna manual saat tema aktif → preset jadi `custom`
  (tema tersimpan tidak berubah sampai disimpan ulang dengan nama
  yang sama). Hapus via × di barisnya; bila tema yang dihapus
  sedang aktif → kembali ke `custom`.
- **Persistensi (SQLite, `KeyValueStore`).** Preset bawaan:
  `Appearance.Palette` (kompatibel C# — DB asli abeng menyimpan
  `"spider"` di sana, dikutip JSON) + `Settings.ThemePreset`;
  mode `Settings.Theme`; aksen `Settings.Accent`; bg
  `Appearance.Background.Dark/Light`; fg `Settings.Foreground`;
  widget `Settings.Color.Editing/Revision/Late/Points`; density
  `Settings.UiDensity` (Compact/Normal/Comfortable); **gaya
  latar widget `Settings.WidgetStyle`** (`"solid" | "gradient" |
  "glass" | "outline"`, default `"solid"`, nilai tak dikenal →
  `"solid"` via `valid_widget_style()`); tema user
  `Settings.Theme.UserThemes` (satu key, JSON array — tiap tema
  menyimpan juga `widget_style`). Nilai C#
  dikutip JSON dan berawalan alpha `#FF`/`#AARRGGBB` — loader
  mengupas keduanya (`unquote`, `normalize_hex`).
- **RINGAN:** tema hanya palet + token — ganti instan, tanpa
  aset, tanpa restart.

**ATURAN TANPA-RESTART [MENGIKAT — abeng, 27 Nov 19:06]:**
apapun yang diganti user di Settings HARUS langsung berlaku TANPA
restart aplikasi. Di C# masih ada pengaturan yang baru berefek
setelah restart — port harus lebih baik. Mekanisme: setiap
perubahan memanggil `appearance-changed()` → `apply_appearance()`
→ token `Design` → seluruh UI ikut berubah; simpan ke SQLite di
belakang layar. JANGAN pernah meminta/menunggu restart.
Untuk audit: opsi Appearance C# (palet, mode, density, aksen,
warna widget) di C# sendiri sudah live via property-changed
handler; yang butuh restart di C# adalah **import database**
("Import SUCCESS! Please RESTART App." — port sudah
mengatasinya di P0 dengan VACUUM INTO tanpa restart) dan
**update aplikasi** (ganti biner — wajar butuh restart).
`Reset ke Default` mengembalikan SEMUA ke BMachine Dark
(termasuk 4 warna widget + density Normal + gaya latar widget
Solid + pola doodle).

**Pola binding TextField (jebakan Slint):** binding satu arah
`text: root.xxx` PUTUS saat user mengetik (assignment ke properti
ter-binding memutuskan binding-nya) sehingga perubahan
programatik sesudahnya (preset / normalisasi / pengosongan field)
tidak tampil lagi. Semua field hex Appearance + field nama tema
WAJIB pakai `<=>` (dua arah) — lihat komentar di `TextField`
(`ui/design.slint`) dan `WidgetColorRow`.

**Pill nav terpusat (bug 27 Nov 19:03, diperbaiki):** NavTab
40*scale harus sama tinggi dengan slotnya di pill-content —
padding vertikal pill-content 2*scale (slot 40*scale). Jangan
ubah tinggi NavTab. Terukur: tengah pill = tengah kapsul (0px).

**Posisi pill nav (27 Nov 20:04, abeng — koreksi 19:54):** kelompok avatar + gap `nav-gap` 12px*scale + kapsul HOME/BATCH TETAP berdekatan, tapi KESELURUHAN kelompok dipusatkan di bar atas (spacer stretch di depan DAN di belakang kelompok) — BUKAN nempel kiri ("aneh", 20:04) dan bukan kapsul saja di tengah. **Sejajar vertikal (aturan abeng 20:19 — satu garis tengah):** avatar 44px*scale, SAMA tinggi dengan kapsul (dulu 40px — top-aligned di slot 44*scale sehingga tengahnya naik ~3px, terukur pixel). Flyout profil menempel avatar lewat `avatar-btn.absolute-position` (x dan y — bukan hardcode lagi, avatar pindah ke tengah dan tingginya berubah).

**Flyout profil (redesain 27 Nov 19:04):** posisi TETAP menempel avatar (`x` dan `y` dari `avatar-btn.absolute-position` + jarak 8px*scale di bawah avatar). Isi:
header (avatar 48px + nama + sapaan jam), grup APLIKASI
(Terminal + Toggle, Settings + shortcut `Ctrl+,`), grup LAINNYA
(Laporkan Bug, Log Out merah), footer (versi + status update
`Terbaru` / `Pembaruan tersedia`). Item = komponen `FlyoutItem`
(`ui/design.slint`): ikon + label + ekor (hint/Toggle), hover
`bg-hover`, ikut aturan ikon+teks §7.

## 23. Window Chrome — gaya chrome + tombol kontrol + "wrapped" (abeng, 27 Nov 20:28, 20:34, 21:08, 22:03)

Chrome BMachine headless tapi window tetap bisa dipindah + resize + ikut
OS snap. Semua gaya bisa dipilih di Settings → Appearance → "Window
chrome", tersimpan di DB, berlaku live tanpa restart (aturan no-restart
19:06) ke SEMUA window ber-chrome: utama, Spreadsheet, Toolbox,
CardList/Trello. Radial + notch TETAP borderless (popup, tanpa chrome).

**4 gaya (`Settings.Chrome`, validator `valid_chrome_style()`,
nilai tak dikenal → `"custom"`):**

| Gaya | Titlebar | `no-frame` | Konten |
|---|---|---|---|
| `custom` (bawaan) | custom 28px | `true` | `content-y` 28, `overlay-y` 32 |
| `headless` | tanpa titlebar | `true` | `content-y` 0, `overlay-y` 8; drag via strip 28px (lihat bawah) |
| `native` | OS (Windows) | `false` | `content-y` 0, `overlay-y` 8; custom titlebar disembunyikan |
| `wrapped` | custom 28px + panel | `true` | `content-y` 36, `overlay-y` 44, `content-bottom` 8 |

`no-frame: root.app-chrome != "native"` — binding langsung di
`ui/main.slint` dan `ui/windows.slint`. Backend winit Slint 1.19.1
menerapkan perubahan `no-frame` secara live via `set_decorations`
(`update_window_properties` di `winitwindowadapter.rs`), jadi ganti
gaya tidak butuh restart. Catatan: sebelum tugas ini, "native" hanya
menyembunyikan titlebar custom tanpa menampilkan chrome OS (bug laten
— `no-frame: true` statis); sekarang diperbaiki.

**Tombol kontrol** — `Settings.Chrome.ButtonStyle`
(`valid_chrome_button_style()`: `"icons"` | `"dots"`, default
`"icons"`) dan `Settings.Chrome.ButtonPosition`
(`valid_chrome_button_position()`: `"right"` | `"left"`, default
`"right"`). Kedua chip hanya tampil untuk custom/wrapped (di native /
headless tidak ada tombol). Komponen `WindowControls`
(`ui/design.slint`): gaya icons = tiga tombol 32×24 (─ □ ×); gaya
dots = pill putih berisi tiga dot pastel 12px (ref
`~/workspace/pet-refs/chrome-buttons-ref.jpg`) — merah bg `#F6C6C6` /
× `#A61B1B`, kuning bg `#F5DFA8` / − `#8A5A00`, hijau bg `#BFE8C0` /
maximize dua segitiga `#166534`; ada varian hover. Lebar dihitung dari
`btn-count`. Area drag titlebar menghindar dari tombol (108px kanan /
130px kiri). Di window sekunder, tinggi titlebar mengikuti window-nya
(Spreadsheet 46px ala C#, sisanya 28px).

**Wrapped** — konten duduk dalam SATU panel rounded inset
(`surface-elevated`, border 1px `separator`, radius 12px): panel
`x=8`, `y=chrome-content-y` (36 di main), lebar `width-16`, bawah
`content-bottom` 8. Titlebar + tombol tetap di frame. Aturan anti
double-wrap (abeng 22:03, "masa bungkus didalam bungkus ya wkwk"):
saat wrapped aktif dan Settings dibuka, sidebar Settings pindah ke area
frame (`x=20..200`, Rectangle sidebar transparan tanpa border) dan
konten Settings = panel window itu sendiri (panel mulai `x=208`) —
BUKAN panel kedua. Saat wrapped tidak aktif, Settings pakai panel
rounded-nya sendiri (lihat §17). **Panel rounded + collapse
sidebar (28 Nov 2026):** sudah diimplementasikan sesuai batasan di
atas — konten Settings pakai panel rounded-nya sendiri kecuali saat
wrapped (dilewati, konten = panel window; panel window mulai x=208
terbuka / x=84 ciut); sidebar ciut jadi rail ikon 56px, DB
`Settings.SidebarCollapsed`. Lihat §17.1. Terverifikasi GUI
(screenshot `settings-*.png`): panel konten x 212–940 (default
chrome), rail 56px + grup bisa dipilih dari rail, persistensi
restart, wrapped tanpa double-wrap (panel x=208/84), tema terang.

**Headless — drag strip:** strip `TouchArea` 28px teratas, double-click
= maximize, `win-move` delta → `set_position` (Rust). Strip
dideklarasikan PALING ATAS (setelah titlebar, sebelum dialog) —
percobaan pertama menaruhnya di bawah overlay membuat window TIDAK
bisa dipindah saat Settings dibuka (`settings-back-touch` full-window
+ Flickable isi menelan drag). Konsekuensi: 28px teratas jadi gagang
drag — klik di pita itu tidak sampai ke konten (ujung atas avatar /
kapsul nav ±8px; klik tengah avatar/kapsul tetap jalan). Jalur tutup
di headless: flyout profil → "Log Out" → dialog EXIT → konfirmasi.
Minimize di headless: tidak ada tombol (taskbar/dock OS saja).

**Sinkronisasi live:** `src/main.rs` — `push_chrome_to_sheet /
toolbox / cardlist` + `sync_chrome_to_windows()`, dipanggil dari
`on_apply_appearance` dan `on_reset_appearance` (reset juga
mengembalikan tombol ke icons + kanan). Window sekunder menerima gaya
saat dibuat DAN saat dibuka-ulang (`push_chrome_to_*` di handler
`on_open_*`); perubahan di Settings saat window sekunder sedang
terbuka langsung diterapkan tanpa restart. Setiap window OS punya
instans `Design` sendiri — sinkronisasi per-window WAJIB (pelajaran
yang sama seperti dynamic bar / radial, lihat §21/§19.7a).

**Resize + OS snap:** native = perilaku OS penuh (resize + snap
bawaan). Gaya frameless (custom/headless/wrapped) = perilaku hari ini:
drag manual untuk pindah; resize via hit-test tepi winit (Windows) —
drag manual TIDAK memicu Aero snap (keterbatasan bawaan, sama seperti
titlebar custom sebelumnya — jangan klaim snap jalan di mode frameless).

**Terverifikasi (27 Nov 2026, Xvfb :98, APPDATA terisolasi
`appdata-chrome-verify/`, DB asli abeng TIDAK disentuh):** build exit
0 (warning lama saja), cargo test **66 passed** (64 lama + 2 baru
validator chrome), 0 failed, 1 ignored. Main window: custom/icons/
kanan (bawaan); dots kanan (pill putih x 882–953, dot merah
`(246,198,198)`, kuning, hijau `(191,232,192)` + ikon ×/−/panah);
dots kiri (pill x 14–85); headless (titlebar hilang, konten dari y=8,
drag strip: dashboard (0,0)→(80,40), dari Settings — sidebar +
konten — (0,0)→(80,40)→(140,70), setelah perbaikan strip-topmost);
wrapped (panel tunggal x 8–952, y 36–632; Settings: sidebar di frame
x 20–200, konten = panel x 208–952, tanpa panel kedua); native
(`Settings.Chrome=native`, titlebar custom hilang, preview "Title bar
OS"). Persistensi: restart → headless tetap, DB
`Settings.Chrome=headless`, `ButtonStyle=dots`, `ButtonPosition=left`
tersimpan. Window sekunder: Spreadsheet ("BMachine — Spreadsheet
Editor" 1000×600) dots kiri → live-sync ke icons kiri saat gaya tombol
diubah di Settings selagi window terbuka (tanpa restart); CardList
("BMachine — Trello Cards" 450×600) icons kiri; Toolbox ("BMachine
Toolbox" 1024×730) icons kiri — dibuka via radial Shift+klik tengah →
segmen TOOLBOX (Timur) → release-to-execute. Tombol close dot merah
terbukti menutup Spreadsheet (klik di pusat dot (149,112) → window
hilang dari daftar visible). Screenshots di
`~/workspace/rust-gui-contoh/`: `chrome-*.png`.

**Masih terbuka:** (1) maximize/minimize: tombol ter-wire (komponen
`WindowControls` yang sama dengan close yang terbukti jalan;
`set_minimized` / `set_maximized` di Rust) tapi efek resize/sembunyi
TIDAK teramati di Xvfb — tanpa window manager, winit tidak bisa
menjalankan permintaan min/max; HARUS dites di PC Windows abeng.
(2) Native: chrome OS sendiri tidak tergambar di Xvfb (tanpa WM);
yang terbukti baru DB + titlebar custom hilang + binding `no-frame`
di sumber Slint — tampilan chrome OS asli HARUS dites di Windows.
(3) OS snap di mode frameless tidak jalan (drag manual saja) —
keterbatasan bawaan, lihat atas. (4) Perilaku Xvfb yang dicatat saat
verifikasi (bukan bug aplikasi): klik panah widget kadang meleset
(handler tidak jalan saat meleset — diduga artefak Xvfb; klik badan
widget tetap jalan); setelah menutup window sekunder, main window
kehilangan fokus (tanpa WM) sehingga klik tidak bereaksi sampai
window di-raise/diklik dulu; `xdotool windowkill` membunuh SELURUH
proses aplikasi (XKillClient) — jangan pakai untuk menutup SATU window
sekunder, pakai tombol close-nya; `xprop` tidak terpasang di VM ini.
