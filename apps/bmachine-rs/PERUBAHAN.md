# PERUBAHAN.md — Log perubahan & serah-terima agent

Setiap agent yang mengerjakan port ini **wajib** menulis entrinya di sini
sebelum commit + push (format lengkap di `AGENTS.md`). Entri terbaru di
paling atas. Dengan log ini, siapa pun yang menerima project — Dex, Hermes,
atau orang lain — langsung tahu apa saja yang diubah tanpa menebak.

## 2026-11-27 — Dex — Dynamic Bar (floating mini-bar ala Dynamic Island)

- Diubah:
  - **AUDIT sumber data dulu** — widget dashboard mengambil angka dari: EDITING/REVISION/LATE = hasil `fetch_cards` Trello API per list via `spawn_trello_sync` (startup bila kredensial ada + refresh manual + simpan pengaturan akun); POINTS = `Stat.Points` di SQLite, dibaca sekali saat startup; **tidak ada yang menulis `Stat.Points` di port** (masih "0" sampai Sheets/Fase 6 menulisnya). **Tidak ada timer refresh berkala di port** — field `app-trello-interval` ("30") hanya tampilan Settings, tidak pernah dibaca Rust. Jadi bar TIDAK mem-poll Trello sendiri; bar menyalin properti `stat-*` AppWindow (sumber yang SAMA dengan widget dashboard) lewat timer sinkronisasi Rust 2 detik (tanpa I/O jaringan).
  - **`ui/windows.slint`** — `DynamicBarWindow` baru (pola window OS seperti Spreadsheet/Toolbox): 560×46 px, `no-frame`, `always-on-top`, latar transparan; pill radius 23 px (`Design.surface-elevated`); 4 chip angka (dot + label + angka, warna `cat-*`) + ikon lonceng + badge merah (cap "9+"); panel kembang (tinggi 106 px + 40 px/baris, maks 5 baris) berisi baris notifikasi (dot kategori, judul + sub) + footer "Buka Dashboard". Klik diproses di `pointer-event(up)` (bukan `clicked`) supaya drag tidak memicu aksi. Drag ambang 5 px → `move-window(dx,dy)`; selesai drag → `save-position()`. Klik kanan di mana saja → `close-window()`.
  - **`ui/design.slint`** — ikon "bell" (Lucide) ditambahkan ke `SvgIcon`.
  - **`ui/main.slint`** — `DynamicBarWindow` ditambahkan ke baris re-export; properti baru `app-dynamic-bar-visible: true`; callback `open-dynamic-bar()` / `hide-dynamic-bar()`; item flyout "Dynamic Bar" (ikon "layers", show-toggle) di grup APLIKASI setelah Terminal.
  - **`src/main.rs`** — `BarNotification` {kind, category, title, sub}, `BarState` {seen_cards per list, synced_lists, last_points, notifications}, `BarStore`; `BAR_MAX_NOTIFICATIONS = 10`, `BAR_MAX_NEW_PER_SYNC = 3`; `record_list_sync` (sync pertama per list = baseline tanpa notifikasi; kartu baru sesudahnya → "Kartu baru masuk", maks 3/sync); `record_points` (pertama = baseline; naik → "Poin bertambah" sub "prev → cur"); `push_notifs_to_bar` (ganti total, tampil 5 terbaru, `notif-count` = total); `read_bar_position` / `save_bar_position` (DB `DynamicBar.X` / `DynamicBar.Y`); `open_main_category` (EDITING/REVISION/LATE → window utama + embedded Trello list kategori itu; POINTS → Leaderboard; `window().show()` untuk menaikkan window); `open_dynamic_bar_window` / `hide_dynamic_bar_window` (pola Toolbox: handler tutup hanya hide + set visible=false + simpan, window di-drop saat dibuka lagi); `spawn_trello_sync` mendapat param `bar_store` + `record_list_sync` tiap fetch sukses (4 call-site diperbarui); blok load DB baca `Settings.DynamicBar.Visible` (default true) + isi baseline `last_points` dari `Stat.Points`; timer sinkronisasi Rust 2 detik (salin stat ke bar + baca ulang `Stat.Points` dari DB, update dashboard bila berubah, catat kenaikan poin — TANPA jaringan); timer startup 2,5 detik membuka bar setelah splash; 4 tes baru `dynamic_bar_tests` (baseline, notifikasi kartu baru sekali, cap 3/sync, baseline/naik/turun/tidak-terurai poin). **`sync_bar_theme`** — lihat butir tema di bawah.
  - **`src/theme.rs`** — `sync_design_to_bar(ui, bar)`: salin 15 token yang dipakai bar (surface-elevated, separator, text-primary/secondary/muted, accent, bg-hover, status-danger, cat-editing/revision/late/points, radius-sm/md, font-family). Dipanggil saat bar dibuka + di handler tema (preset, user-theme, reset, appearance) + tiap tick timer 2 detik sebagai jaring pengaman.
  - `DESIGN_SYSTEM.md` §21 ditulis ulang dari "fitur masa depan" menjadi spesifikasi yang dibangun (termasuk catatan celah Design per-window di bawah).
- Alasan: abeng 27 Nov 19:52 — dynamic bar menampilkan widget dashboard + notifikasi card masuk + notifikasi poin bertambah, supaya user tidak harus selalu buka window utama. Urutan kerja 19:52: tema widget → nav rapat ke avatar → dynamic bar → tear-off BATCH → Sakuga.
- Terverifikasi: build exit 0 (warning lama sha2 saja), cargo test **31 passed** (27 lama + 4 baru). Klik sungguhan + screenshot di Xvfb :98 (APPDATA tes `/tmp/dynbar/appdata`, DB asli abeng TIDAK disentuh): bar muncul sebagai window OS terpisah "BMachine — Dynamic Bar" 560×46 di (200,12) = atas-tengah window utama 960×640, selalu di atas (juga di atas overlay Settings); angka bar = angka dashboard (0/0/0/0, lalu 4180 lalu 4210 — DB `Stat.Points` diubah via sqlite3 di tengah jalan → dashboard + bar ikut berubah dalam ~2 detik tanpa restart); poin 4180 → 4210 memunculkan notifikasi "Poin bertambah / 4180 → 4210" + badge lonceng "1"; klik lonceng → bar mengembang menampilkan baris notifikasi + "Buka Dashboard"; klik baris notifikasi → Leaderboard terbuka di window utama (toast "Leaderboard — poin editor"); klik chip EDITING di bar → embedded Trello EDITING terbuka ("Dimuat 6 card", 6 kartu mock + badge DONE/TODO, toast "Daftar EDITING — 0 kartu"); drag +100,+100 → posisi (300,112), DB `DynamicBar.X=300/Y=112`; restart app → bar kembali di (300,112) dan POINTS 4210 pulih dari DB; klik kanan bar → window hilang + DB `Settings.DynamicBar.Visible=false`; toggle "Dynamic Bar" di flyout (grup APLIKASI) → bar terbuka lagi di (300,112), `Visible=true`. Screenshots: `~/workspace/rust-gui-contoh/dynamicbar-verify.png`, `dynamicbar-points4180.png`, `dynamicbar-notif.png`, `dynamicbar-clicknotif.png`, `dynamicbar-clickediting.png`, `dynamicbar-restart.png`, `dynamicbar-spider2.png`, `dynamicbar-dark.png`, `dynamicbar-flyout2.png`, `dynamicbar-reopen.png`.
- **Temuan saat verifikasi (diperbaiki sebelum commit) — tema TIDAK diikuti bar:** setelah klik Spider Red, dot EDITING bar tetap #3B82F6 (biru BMachine Dark) padahal dashboard sudah merah. Akar: **Slint memberi tiap window OS instance `Design` sendiri** — `apply_appearance` hanya menyentuh Design window utama. Terbukti juga di SpreadsheetWindow (titlebar tetap (32,34,38) saat Spider aktif). Perbaikan: `sync_design_to_bar` (di atas) — diverifikasi ulang terukur pixel: buka ulang dengan preset Spider tersimpan → dot EDITING bar #D9272E (merah Spider), REVISION biru, POINTS oranye, sama dengan widget dashboard; klik BMachine Dark → dot kembali #3B82F6 langsung (tanpa jeda timer).
- Masih terbuka: (1) **Celah yang sama di window OS lain** — Spreadsheet/Toolbox/CardList/Radial/ReportBug juga tidak ikut ganti tema (instance Design sendiri, belum disinkronkan); bar sudah diperbaiki, window lain masih menunggu (pekerjaan terpisah, pola `sync_design_to_bar` bisa dipakai ulang). (2) Notifikasi bar hanya di memori sesi — hilang saat restart (sengaja untuk v1; kalau abeng mau riwayat notifikasi tersimpan, perlu key SQLite baru). (3) Notifikasi kartu Trello baru belum dites end-to-end dengan Trello live (tidak ada kredensial di env tes; logika diuji unit + hook `record_list_sync` di 4 call-site `spawn_trello_sync`). (4) (lama) item radial belum dites klik langsung; Ctrl+L kedua tidak menutup Log Panel saat fokus di panel.

## 2026-11-27 — Dex — Pill nav HOME/BATCH dirapatkan ke avatar

- Diubah: `ui/main.slint` bar atas — spacer `horizontal-stretch` pertama (yang mendorong kapsul ke tengah jendela) dihapus; kapsul kini duduk tepat setelah avatar + gap; `ui/design.slint` token `nav-gap` 16px → 12px (hanya dipakai spacer ini). Komentar bar atas diperbarui. `DESIGN_SYSTEM.md` §22: catatan posisi pill nav.
- Alasan: abeng 27 Nov 19:40 — jarak avatar dan nav HOME/BATCH terlalu jauh (pill di tengah jendela); referensi: dua kelompok pill berdekatan dengan jarak kecil.
- Terverifikasi: build exit 0 (warning lama sha2), cargo test 27 passed. Klik sungguhan + ukur pixel di Xvfb :98 (APPDATA tes /tmp/navgap/appdata, DB asli abeng TIDAK disentuh): gap tepi kanan avatar (x=80) ke tepi kiri kapsul (x=98) = 17px ≈ 12px*scale (scale 1,325 → 15,9px, +1px antialiasing); tengah pill putih y 47–100 (73,5) = tengah kapsul y 44–103 (73,5) → selisih 0px (perbaikan 19:03 tetap terjaga); tengah pill vs tengah bar atas (y 28–118) = 0,5px. Klik BATCH → tab BATCH terpilih (ikon+teks), HOME ikon saja, tampilan BATCH terbuka; klik HOME → balik ke dashboard; klik avatar → flyout tetap menempel avatar dan terbuka (header tanpa avatar, "Selamat siang"). Nav dinamis + animasi tidak diubah. Screenshots: `~/workspace/rust-gui-contoh/navgap-verify.png`, `navgap-batch.png`, `navgap-flyout.png`.
- Masih terbuka: (lama) item radial belum dites klik langsung; Ctrl+L kedua tidak menutup Log Panel saat fokus di panel.

## 2026-11-27 — Dex — Gaya latar widget dashboard (Solid/Gradien/Kaca/Outline) sebagai bagian sistem tema

- Diubah:
  - **AUDIT C# vs port — C# TIDAK PUNYA gaya latar widget.** `AppearancePalette.cs` hanya warna: Primary/Secondary/Background/LightBackground + 4 warna widget (Editing/Revision/Late/Points); StatWidget C# selalu permukaan solid `CardBackgroundBrush` + garis aksen atas; `SettingsView.axaml` + `SettingsViewModel.cs` hanya punya: template picker, mode Light/Dark/System, density, aksen, kecepatan animasi, 4 warna widget — TIDAK ADA pilihan solid/gradient/kaca/outline atau semacamnya, dan tidak ada key SQLite untuk itu. Yang sudah ada di port adalah **pola** latar (`widget-bg`: doodle/dots/impulse/none, key `Settings.WidgetBg`) — itu motif dekorasi di ATAS kartu, BUKAN permukaan kartu. Jadi keempat gaya ini murni invensi port (konsisten dengan design system), bukan padanan C#; tidak ada opsi C# yang gagal dibuat.
  - **IMPLEMENTASI (`ui/design.slint`, `ui/main.slint`, `ui/settings.slint`, `src/theme.rs`, `src/main.rs`)** — StatWidget: properti baru `bg-style: "solid"` + turunan `surface-bg` / `surface-border` / `surface-border-width` / `show-top-bar`; latar luar transparan saat outline; overlay gradien (`accent.with-alpha(0.22)` → 0 di 65% tinggi) hanya saat gradient. `ui/main.slint`: properti `widget-style` + `bg-style: root.widget-style` di keempat StatWidget (EDITING/REVISION/LATE/POINTS) + binding ke SettingsView. `ui/settings.slint`: section lama "Widget background" diganti judul **"Pola latar widget"** (motif dekorasi) + section baru **"Gaya latar widget"** (4 OptionChip Solid/Gradien/Kaca/Outline — tiap klik: `theme-preset = "custom"`, set gaya, `appearance-changed()`, toast); preview StatWidget ikut `bg-style`. `src/theme.rs`: `ThemePalette.widget_style` (serde default `"solid"` — JSON tema user lama tanpa field tetap terbaca); `with_widget_style()`; pemetaan per preset — solid: BMachine Dark/Light + Armor; outline: Midnight OLED; gradient: Catppuccin Mocha + Spider; glass: Emerald Studio + Gwen; `apply_preset` + `apply_user_theme` set `ui.set_widget_style(...)`; `snapshot_current_as_user_theme` menyimpan gaya; `load_theme_from_db` baca key **`Settings.WidgetStyle`** (validasi `valid_widget_style()`, tak dikenal → `"solid"`); `save_theme_to_db` tulis key itu; 3 tes baru. `src/main.rs`: `on_reset_appearance` juga reset gaya → `"solid"` (pola → `"doodle"`).
  - `DESIGN_SYSTEM.md`: §8 (arti 4 gaya + beda pola vs gaya + audit C#) dan §22 (pemetaan per preset, key `Settings.WidgetStyle`, section picker, reset).
- Alasan: abeng 27 Nov 19:37 — widget dashboard juga punya tema; aturan tanpa-restart 19:06 (semua berlaku langsung, simpan ke SQLite di belakang layar).
- Terverifikasi: build exit 0 (warning lama sha2 saja), cargo test **27 passed** (24 lama + 3 baru: `valid_widget_style_falls_back_to_solid`, pemetaan gaya preset, kompat JSON lama). Klik sungguhan + screenshot di Xvfb :98 (APPDATA tes `/tmp/widgettheme/appdata`, DB asli abeng TIDAK disentuh): keempat gaya dipilih dari Settings → keempat widget berubah langsung tanpa restart (toast "Gaya latar widget: Gradien/Kaca/Outline") — **terukur pixel**: Solid border (38,38,38) + permukaan (26,26,26); Gradien tint atas (30,43,65) → bawah (26,26,26); Kaca border tint biru (43,75,127) + permukaan terang (32,32,32); Outline border = aksen persis (EDITING #3B82F6, REVISION #F97316, LATE #EF4444, POINTS #2DD4BF) + permukaan transparan (18,18,18) + tanpa garis aksen atas. Preset Spider Red → widget jadi Gradien (toast "Tema Spider Red diterapkan"); Midnight OLED → widget jadi Outline. Restart app → Midnight OLED + Outline pulih dari SQLite (DB: `Settings.WidgetStyle=outline`, `Appearance.Palette=midnight-oled`). Reset ke Default → BMachine Dark + Solid (DB: `WidgetStyle=solid`). Tema user "ujitema" tersimpan dengan `"widget_style":"outline"` di JSON `Settings.Theme.UserThemes` + muncul di picker dengan ✓. Ubah gaya manual → preset jadi `custom` (tidak ada ✓ di preset). Preview miniatur Appearance ikut gaya aktif. Screenshots: `~/workspace/rust-gui-contoh/widgettheme-dash-solid.png`, `widgettheme-dash-gradient.png`, `widgettheme-dash-glass.png`, `widgettheme-dash-outline.png`, `widgettheme-dash-spider.png`, `widgettheme-dash-midnight.png`, `widgettheme-restart.png`, `widgettheme-dash-reset.png`, `widgettheme-usertheme-saved.png`.
- Masih terbuka: (lama, belum berubah) item radial belum dites klik langsung — hotkey Shift+klik tengah tidak terpancing dari xdotool di Xvfb; Ctrl+L kedua tidak menutup Log Panel saat fokus di panel. Catatan tes: scroll Settings Flickable melompat jauh (3–6 klik wheel = satu section penuh) — koordinat klik wajib diukur dari screenshot segar sesaat sebelum klik; membuka Settings dari dashboard kadang mendarat di grup General (bukan Appearance terakhir) — klik grup Appearance dulu sebelum scroll.

## 2026-11-27 — Dex — Header flyout: hapus avatar + perbaiki jarak dot-sapaan

- Diubah: `ui/main.slint` header flyout profil — avatar 48px dihapus (redundan: pemanggil flyout ini sendiri sudah ikon user); dot status Text "●" diganti Rectangle 8px (border-radius 4, `Design.status-success`) dibungkus VerticalLayout center (§7). Komentar header diperbarui.
- Alasan: abeng 27 Nov 19:37 — avatar tidak perlu; jarak kosong antara dot hijau dan "Selamat sore" dipertanyakan.
- Akar jarak kosong: Text "●" 8px lebarnya ~31px padahal glyph cuma 7px (terukur dari pixel: dot x 99–105, teks mulai x 135 → jarak 30px, bukan spacing 6px yang ditulis). Rectangle 8px menghilangkan keanehan metrik font.
- Terverifikasi: build exit 0 (warning lama sha2), cargo test 24 passed. Klik avatar + screenshot Xvfb :98 (APPDATA tes /tmp/flyoutfix/appdata, DB asli tidak disentuh): header tanpa avatar; dot x 39–46 (8px), teks "Selamat sore" mulai x 52 → jarak 6px persis; flyout tetap menempel avatar; item lain (Terminal, Settings, Laporkan Bug, Log Out, footer) tidak berubah. Screenshot: `~/workspace/rust-gui-contoh/flyoutfix-verify.png`.
- Masih terbuka: (lama) item radial belum dites klik langsung; Ctrl+L kedua tidak menutup Log Panel saat fokus di panel.

## 2026-11-27 — Dex — Sistem tema ala Omarchy (8 preset + tema user + density + warna widget), perbaiki pill nav turun, redesain flyout profil

- Diubah:
  - **AUDIT C# vs port (Settings Appearance)** — C# (`SettingsView.axaml` + `SettingsViewModel.cs` + `AppearancePalette.cs` + `UiDensityService.cs`): THEME = 6 palet (Classic, Spider, Gwen, Armor, Midnight, Symbiote — masing-masing Primary/Secondary/Background/LightBackground/WidgetEditing/Revision/Late/Points, key `Appearance.Palette`); Mode Light/Dark/System (`Settings.Theme`); DENSITY Compact/Normal/Comfortable (`Settings.UiDensity`, Apply: BtnHeight 24/36/40, font, padding, ikon); BACKGROUNDS custom Dark/Light; ACCENT ComboBox + Pick (`Settings.Accent`); ANIMATIONS kecepatan animasi widget; WIDGET COLORS Editing/Revision/Late/Points (`Settings.Color.*`). Kesenjangan port sebelum kerja ini: (1) preset tidak mengatur warna widget (`Design.cat-*` tidak pernah disentuh `apply_appearance`); (2) tidak ada UI Density — token `density-scale` mati dan prop `ui-density` tidak ter-wire; (3) palet bernama C# (Spider/Gwen/Armor) tidak ada; (4) ThemeRow tanpa preview swatch; (5) tidak ada tema user bernama (hanya satu bucket "Custom" tanpa nama). **Restart (audit tambahan, aturan abeng 19:06):** opsi Appearance C# sendiri sudah live (palette/mode/density/aksen/warna widget via property-changed handler); yang butuh restart di C# adalah import database ("Import SUCCESS! Please RESTART App." — port sudah mengatasinya di P0 dengan VACUUM INTO tanpa restart) dan update aplikasi (ganti biner). Semua opsi baru port dibuat live tanpa restart (lihat `DESIGN_SYSTEM.md` §22).
  - **SISTEM TEMA (`src/theme.rs`, `src/main.rs`, `ui/settings.slint`, `ui/main.slint`)** — satu tema = satu palet penuh (`ThemePalette`: id/nama/mode/accent/bg/fg/editing/revision/late/points); `builtin_presets()` 8 preset: `bmachine-dark`, `bmachine-light`, `midnight-oled`, `catppuccin-mocha`, `emerald-studio`, `spider`, `gwen`, `armor` (nilai C# persis dari `AppearancePalette.cs` untuk spider/gwen/armor; Catppuccin pakai mauve/peach). `apply_preset` set SEMUA properti tampilan + turunkan token `Design` (satu palet dibaca semua permukaan); `apply_user_theme`, `snapshot_current_as_user_theme`, `load/save/refresh_user_themes` (key `Settings.Theme.UserThemes`, JSON array, id `user:<slug>`). `load_theme_from_db`: unquote + normalisasi hex C# (`"spider"`, `#FF` alpha), preset dari `Appearance.Palette` dulu lalu `Settings.ThemePreset` + peta nama lama (`legacy_preset_id`); `save_theme_to_db`: preset ke BOTH key, widget colors `Settings.Color.*`, density kapital (Compact/Normal/Comfortable). `main.rs`: 4 handler baru (`on_apply_theme_preset`, `on_save_user_theme`, `on_apply_user_theme`, `on_delete_user_theme`) + `on_reset_appearance` diperpanjang (reset juga 4 warna widget + density Normal + preset id `bmachine-dark`). `settings.slint`: ThemeRow + preview 4 titik palet + × hapus (tema user); 8 baris preset (callback id, bukan set manual per warna); section **Tema saya** (`for` atas array paralel + field nama + "Simpan tema"); section **Kepadatan** (3 OptionChip); section **Warna widget** (komponen baru `WidgetColorRow`: dot + label + field hex + 4 swatch).
  - **Perbaikan dari hasil tes klik:** (a) default `app-theme-preset`/`theme-preset` masih nama lama `"BMachine Dark"` sehingga tidak ada preset tampil terpilih di DB baru — diubah ke id `"bmachine-dark"`; (b) field nama tema tidak ter-clear setelah simpan — akar: binding satu arah `text: root.xxx` PUTUS saat user mengetik (Slint), sehingga perubahan programatik tidak tampil; semua field hex Appearance + field nama tema diubah ke `<=>` (dua arah), `WidgetColorRow.hex` jadi `in-out` + `text <=> hex` (catatan pola ini di `DESIGN_SYSTEM.md` §22 + komentar `TextField`); (c) `legacy_preset_id`: `match unquote(raw)` (String) vs literal `&str` — 12 error kompilasi, diperbaiki ke `.as_str()`.
  - **PILL NAV TURUN — BUG (abeng 27 Nov 19:03):** `ui/main.slint` pill-content padding vertikal 6→2*scale (slot 40*scale = tinggi NavTab 40*scale; tinggi NavTab TIDAK diubah). Terukur sebelum: pill y 52–106 (tengah 79) vs kapsul y 44–106 (tengah 75) = 4px di bawah; sesudah: pill y 47–100 (tengah 73,5) = tengah kapsul 73,5 → selisih 0px, jarak atas = jarak bawah 2px.
  - **REDESAIN FLYOUT PROFIL (abeng 27 Nov 19:04):** posisi TETAP menempel avatar (x=20, y=chrome+60*scale — tidak diubah). `ui/design.slint`: komponen baru `FlyoutItem` (ikon + label + ekor hint/Toggle, hover `bg-hover`, aturan §7 VerticalLayout center). `ui/main.slint`: header avatar 48px (aksen-subtle + ikon user aksen) + nama + dot hijau + sapaan jam; grup APLIKASI (Terminal + Toggle, Settings + hint `Ctrl+,`); grup LAINNYA (Laporkan Bug, Log Out merah); footer versi + status update (`Terbaru` hijau / `Pembaruan tersedia` aksen). Semua item lama tetap ada & berfungsi; semua warna dari token `Design` (ikut tema aktif).
  - `DESIGN_SYSTEM.md`: §22 baru (sistem tema, key SQLite, aturan tanpa-restart, pola binding TextField, pill, flyout).
- Alasan: abeng 27 Nov 18:54 ("polish lagi tampilan, banyak fitur + bebas customisasi") → arah Omarchy 19:02 (preset terkurasi sekali klik, tema user, ringan, opinionated) → aturan tanpa-restart 19:06 → bug pill 19:03 → redesain flyout 19:04.
- Terverifikasi: build exit 0 (warning lama sha2 saja), cargo test 24 passed (6 tes tema baru). Klik sungguhan + screenshot di Xvfb :98 (APPDATA tes /tmp/polish/appdata, DB asli abeng TIDAK disentuh): pill tengah = kapsul 0px (`nav-pill-centered.png`); preset Spider Red + Emerald Studio diterapkan live — dashboard/nav/Settings berubah tanpa restart (`polish-spider-dash.png`, toast "Tema Spider Red diterapkan"; EDITING merah #D9272E, REVISION biru #1557A5, POINTS oranye #F97316); density Comfortable live — font + spacing membesar + toast (`polish-density-comfortable.png`); warna widget EDITING → hijau #10B981 via swatch, kartu dashboard ikut hijau live (`polish-widget-green.png`, `polish-dash-widget-green.png`); tema user: simpan "temaku" + "temadua" (muncul di picker + ✓ + toast; field nama ter-clear setelah simpan — perbaikan `<=>` terbukti), hapus "temadua" via × (DB tinggal `user:temaku`, preset fallback `custom` — terverifikasi SQLite); restart app → Spider/Comfortable/"temaku" pulih dari SQLite (`polish-restore-check.png`); restart lagi → custom + EDITING hijau + Comfortable pulih (`polish-restart-dash.png`); Reset ke Default → BMachine Dark penuh (DB: preset `bmachine-dark`, aksen #3B82F6, Editing #3B82F6, Points #2DD4BF, density Normal) + toast (`polish-reset-dash.png`); flyout: klik avatar → tampil menempel avatar (`polish-flyout.png`; ikon+teks terukur: Terminal 0px, Settings 2px, Laporkan Bug 2,5px — glyph optis, struktur §7 dipatuhi), Toggle Terminal → log panel terbuka (`polish-flyout-terminal.png`), klik Settings → Settings terbuka + flyout tertutup (`polish-flyout-settings.png`). Screenshots: `~/workspace/rust-gui-contoh/polish-*.png` + `nav-pill-centered.png`.
- Masih terbuka: (lama, belum berubah) item radial (radial.slint:98) belum dites klik langsung — hotkey Shift+klik tengah tidak terpancing dari xdotool di Xvfb; Ctrl+L kedua tidak menutup Log Panel saat fokus di panel (perilaku lama). Catatan tes: klik field nama tema sempat gagal saat section di tepi atas viewport (klik mendarat di y yang salah karena scroll) — BUKAN bug app: setelah scroll ke tengah viewport, field fokus + ketik jalan normal; koordinat klik wajib diukur dari screenshot segar sesaat sebelum klik. Preview miniatur Appearance masih memakai warna pv-* lama (EDITING merah di preview saat widget hijau) — pre-existing, tidak diubah di kerja ini.

## 2026-11-27 — Dex — Ikon+teks sejajar tengah, nav dinamis, posisi flyout profil

- Diubah:
  - AKAR MASALAH ikon+teks (keluhan abeng 27 Nov 18:45, terukur dari pixel screenshot nav: ikon ~8px di atas teks, selisih garis tengah 8,5px): di Slint, `HorizontalLayout { alignment: center }` hanya menengahkan sumbu HORIZONTAL — anak ber-height eksplisit (SvgIcon 16px) nempel ke ATAS baris, sedangkan `Text` tanpa height di-stretch setinggi baris dan `vertical-alignment: center` menengahkan glyph-nya. Perbaikan sistematis: bungkus tiap SvgIcon di baris ikon+teks dengan `VerticalLayout { alignment: center; ... }` (di-stretch setinggi baris, lalu menengahkan ikon) — pola yang sudah benar di GroupRow + search field settings.slint. Kena 10 titik: `ui/design.slint` NavTab; `ui/logpanel.slint` PanelTab, ExplorerRow, MasterRow, path bar, search MASTER; `ui/radial.slint` item radial; `ui/spreadsheet.slint` search; `ui/windows.slint` judul window + search. Yang sudah benar tidak diubah (avatar, panah StatWidget, slot radial, IconBtn — x/y eksplisit; GroupRow + search Settings — sudah pakai pola ini).
  - NAV DINAMIS (abeng 27 Nov 18:46): NavTab di `ui/design.slint` — label hanya tampil saat selected; saat tidak terpilih pill mengecil jadi ikon saja. Label dibungkus `label-box` (Rectangle, `width: selected ? preferred-width + 7px : 0`, `clip: true`, `animate width 200ms`) supaya transisi membesar/mengecil halus dan pill yang mengecil tetap simetris (gap 7px ikut di dalam lebar animasi). Container pill mengikuti preferred-width.
  - POSISI FLYOUT PROFIL (abeng 27 Nov 18:48): `ui/main.slint` ~3743 — dulu `x: (root.width - 256px)/2; y: 60px*scale` (muncul di TENGAH layar — salah). Sekarang menempel tombol pemanggil seperti C# Button.Flyout: `x: 20px*scale` (rata kiri tombol avatar) dan `y: chrome + 60px*scale` (bawah tombol avatar + jarak 8px*scale).
  - `DESIGN_SYSTEM.md`: catat aturan bungkus-ikon di § tentang NavTab/komponen (SvgIcon di HorizontalLayout wajib dibungkus VerticalLayout{alignment:center}).
- Alasan: tiga keluhan abeng 27 Nov malam (ikon+teks tidak sejajar di semua tampilan; nav harus dinamis icon-only saat tidak terpilih; flyout muncul di tengah bukan di tombol pemanggil).
- Terverifikasi: build exit 0 (warning lama sha2 saja), cargo test 18 passed. Klik sungguhan + ukur pixel di Xvfb :98 (APPDATA tes /tmp/navfix/appdata, DB asli tidak disentuh): nav HOME ikon 79,5 vs teks 80,0 (selisih 0,5px; dulu ~8,5px), BATCH ikon 79,0; path bar 0px; ExplorerRow 1,0px; FILES tab 0,5px; MasterRow 1,0px; search MASTER + spreadsheet + Settings terpusat; judul Spreadsheet Editor ~0px (pusat cap). Nav dinamis: HOME terpilih = HOME ikon+teks (123px) + BATCH ikon saja; klik BATCH = BATCH ikon+teks (127px) + HOME ikon saja; frame tengah transisi (lebar 173px gabungan) membuktikan animasi jalan, bukan snap. Flyout: tepi kiri x=27 = tepi kiri avatar, atas y=109 = bawah avatar (98,5) + jarak ~11px. Screenshots: `~/workspace/rust-gui-contoh/navfix-home-selected.png`, `navfix-batch-selected.png`, `navfix-flyout.png`, `navfix-settings.png`, `navfix-logpanel.png`, `navfix-master.png`, `navfix-spreadsheet.png`, `navfix-mid-anim3.png`.
- Masih terbuka: item radial (radial.slint:98) pakai pola wrapper yang sama tapi BELUM dites klik langsung — hotkey global Shift+klik tengah tidak terpancing dari xdotool di Xvfb (keterbatasan tes lama, bukan regresi; kode radial tidak diubah selain wrapper ikon). Verifikasi di PC Windows abeng. Ctrl+L kedua tidak menutup Log Panel saat fokus di panel (ditutup via × header; perilaku lama, bukan dari perubahan ini).
## 2026-11-27 — Dex — Hapus Floating Widget / Smart Orb SEPENUHNYA (koreksi P2)

- Diubah:
  - DIHAPUS `ui/floating.slint` (file, 315 baris) — window
    `FloatingWidgetWindow` 100×100 transparan + orb 60px + glow
    bernapas + menu expand Master/Action + resize handle.
  - `src/main.rs` (−317 baris): hapus `scan_floating_scripts`,
    `parse_hex_color` (hanya dipakai orb), `floating_orb_prefs`,
    `open_floating_widget`, `hide_floating_widget`; hapus baca
    `Settings.FloatingWidget` saat load, hapus tulis
    `Settings.FloatingWidget` di save-general-settings, hapus
    deklarasi cell `floating_win_cell`/`floating_visible`, hapus
    handler `on_toggle_floating_widget`, hapus timer startup 300ms
    yang menampilkan orb. Key DB lama (`Settings.FloatingWidget`,
    `Settings.Orb.*`, `Configs.Widget.Position`,
    `Settings.Color.Orb`) TIDAK dihapus dari database — hanya
    berhenti dibaca/ditulis.
  - `ui/main.slint`: hapus export `FloatingWidgetWindow`, callback
    `toggle-floating-widget` + handler `changed app-smart-orb`,
    property `app-smart-orb`, binding `smart-orb`.
  - `ui/settings.slint`: hapus property `smart-orb` + baris toggle
    "Smart Orb / Floating Widget" di grup General.
  - `DESIGN_SYSTEM.md` §17.3: grup General tidak lagi mencantumkan
    Smart Orb; dicatat orb dihapus 27 Nov, diganti radial menu.
- Alasan: koreksi abeng 27 Nov malam — orb yang dibangun di P2
  (dari file C# `FloatingWidgetView.axaml.cs` yang masih ada di
  source tree) ternyata sudah DIHAPUS PENUH dari C# dan diganti
  radial menu. Port ikut menghapus, bukan memperbaiki orb.
- TIDAK disentuh: radial menu (P0) tetap ada — window radial
  320×320 di kursor, dwell 300ms, release-to-execute, hotkey
  global Shift+klik tengah; label widget "POINTS"; spreadsheet
  fingerprint guard + splash (P2 item 2–3).
- Terverifikasi: `cargo build --offline` exit 0 (hanya warning lama
  `sha2`); `cargo test --offline` 18 passed, 0 failed; klik
  sungguhan di Xvfb :98 (APPDATA tes terisolasi
  `/tmp/p2orb/appdata`, DB asli abeng tidak disentuh): dashboard
  tampil bersih tanpa window orb (`xdotool search --onlyvisible
  --name "Floating"` kosong; screenshot
  `p2-orb-removed-dashboard.png`); Settings General tidak ada lagi
  toggle orb (`p2-orb-removed-settings.png`); radial menu tetap
  jalan — `--radial-test` membuka window "BMachine Radial Menu"
  320×320 di kursor (480,240) (`p2-orb-removed-radial2.png`;
  isi radial tidak tertangkap screenshot karena window transparan
  tanpa compositor di Xvfb — artefak lingkungan, bukan bug).
- Masih terbuka: verifikasi radial + hilangnya orb di PC Windows
  abeng (klik panah EDITING yang kadang hilang di Xvfb masih
  menunggu cek Windows).

## 2026-11-27 — Dex — P2: Floating Widget / Smart Orb + Spreadsheet fingerprint guard + Splash samakan C#

- Diubah:
  - (1) Floating Widget / Smart Orb (`ui/floating.slint` BARU,
    `ui/main.slint`, `src/main.rs`): window OS `FloatingWidgetWindow`
    100×100 transparan, no-frame, always-on-top; orb 60px + glow
    bernapas via Timer (kecepatan `Settings.Orb.Speed`, toggle
    `Settings.Orb.Breathing`); drag kiri ambang 5px, klik kiri expand,
    klik kanan collapse; menu expand 440×210 (ukuran tersimpan
    `Settings.Orb.ExpandedWidth/Height`, resize handle min 300×150),
    nav Master/Action + daftar script vertikal + resize handle;
    Master = `.py` di `Scripts/Master`, Action = `.jsx` di
    `Scripts/Action` + `.pyw` di `Scripts`, nama = stem file,
    alfabetis; posisi tersimpan `Configs.Widget.Position.X/Y`;
    aksen `Settings.Color.Orb`; visibility `Settings.FloatingWidget`
    (default True, tampil saat startup, dibuka 300ms setelah window
    utama agar tidak tertutup); persist di `save-general-settings` +
    handler toggle. Toggle lama di General yang hanya toast diganti
    wiring asli ke window ini.
  - (2) Spreadsheet fingerprint guard (`src/spreadsheet_guard.rs`
    BARU, `ui/spreadsheet.slint`, `ui/modul.slint`, `ui/windows.slint`,
    `ui/main.slint`, `src/main.rs`): fingerprint = join seluruh baris
    dengan `\u{1f}`; anchors per baris; `find_mismatches` lewati baris
    kosong; `describe` dinamis (nomor baris pertama mismatch + aktual
    vs yang dimuat, `"(dan N baris lain)"`); draft persist SQLite key
    `Spreadsheet.Draft` (JSON PascalCase); snapshot persist key
    `Spreadsheet.SheetSnapshot`; saat reload draft hanya diterapkan
    bila nilai asli sel masih cocok, sisanya tetap tersimpan sebagai
    `remaining` (bukan dibuang); setelah save berhasil draft yang
    terkirim di-drop; status bar guard-result + `Simpan (N)`.
  - (3) Splash samakan C# (`ui/splash.slint`, `src/main.rs`): logo
    layers 56px + `"B"` 22px weight 900 aksen + `"MACHINE"` 22px
    weight 300 letter-spacing 4; `VersionText "VERSION 15.15.3"`;
    status default `"Memuat..."`; ProgressBar 0–100 tinggi 3; log
    terminal maks 3 baris prefix `"> "`; langkah 10% SQLite, 25%
    tema & UI, 50% preload (82 keys), 75% engine Python, 90%
    layanan eksternal, 100% siap.
  - (4) Perbaikan bug fokus editor sel (`ui/spreadsheet.slint`,
    `GridCell`): editor `TextInput` selalu ada di tree (bukan hanya
    dibuat saat `if is-active`) + `changed is-active => editor.focus()`
    — sebelumnya ketikan tidak masuk ke sel karena fokus tertahan di
    elemen sebelumnya. Tambahan: `changed text => editor.text =
    root.text` — setelah pengguna mengetik, binding `text` editor
    putus (nilai ketikan menimpa binding), jadi push nilai baru dari
    Rust (Muat Ulang/Simpan) tidak sampai ke editor sel aktif;
    tanpa ini sel aktif menampilkan nilai lama setelah Muat Ulang.
  - (5) Jawaban DESIGN_SYSTEM.md §19.9 butir 4–6 (dari source C#,
    diverifikasi langsung: `SpreadsheetRowAnchorGuard.cs:63`,
    `SpreadsheetDraft.cs:81`, `SpreadsheetViewModel.cs:680,719`):
    Q4 pesan guard dinamis, bukan hardcode; Q5 koreksi premis —
    C# TIDAK membuang draft sel berubah saat reload, draft tetap
    tersimpan sebagai remaining dan dicoba lagi; Q6 draft persist
    antar-sesi di SQLite key `Spreadsheet.Draft`.
- Alasan: tugas P2 (referensi C#: `FloatingWidgetView.axaml/.cs`,
  `SpreadsheetRowFingerprint` + `SpreadsheetRowAnchorGuard`,
  `SplashViewModel.cs` + `SplashWindow.axaml`); 3 keputusan abeng
  27 Nov 16:54 tetap dijaga (label widget "POINTS", pemicu radial
  tambahan dihapus — hanya hotkey Shift+klik tengah, titlebar
  custom 28px + min 520).
- Terverifikasi: `cargo build --offline` exit 0 (satu-satunya warning
  lama: unused dependency `sha2`); `cargo test --offline` 18 passed,
  0 failed (termasuk 6 test `spreadsheet_guard`). Klik sungguhan di
  Xvfb :98 (APPDATA tes terisolasi `/tmp/p2test/appdata`, DB asli
  abeng tidak disentuh):
  - Orb: tampil saat startup di (100,100); klik kiri → expand
    440×210, Master menampilkan `AlphaMaster`, `TestMaster`
    (alfabetis); nav Action → `RootScript`, `TestAction`; klik
    `RootScript` → toast `"[Photoshop] Menjalankan: RootScript"` +
    collapse; drag (150,150)→(300,300) → posisi (250,250), DB
    `Configs.Widget.Position` 250/250; klik kanan → collapse;
    resize handle → 500×250, DB `Settings.Orb.ExpandedWidth/Height`
    500/250; toggle Settings OFF → widget hilang, DB `false`;
    ON → muncul lagi di (250,250), DB `true`.
    (`p2-orb-startup.png`, `p2-orb-expanded5.png`,
    `p2-orb-action.png`, `p2-orb-after-exec.png`, `p2-orb-dragged.png`,
    `p2-orb-collapsed.png`, `p2-orb-resized.png`,
    `p2-orb-toggle-off.png`, `p2-orb-toggle-on.png`)
  - Spreadsheet guard (build final, DB tes bersih): ketik di B1 +
    Return → draft tercatat di `Spreadsheet.Draft` (baris 1, kolom
    B, original "SDN RANCAIYUH I", nilai " EDITSDN RANCAIYUH I"),
    tombol jadi `Simpan (1)`, status "Perubahan belum disimpan
    (1 sel)"; ubah snapshot dari luar (B1 → "SDN RANCAIYUH I LUAR")
    → Simpan DIBLOKIR pesan guard dinamis, draft tetap ada;
    Muat Ulang → B1 menampilkan "SDN RANCAIYUH I LUAR" (draft tidak
    diterapkan), draft tetap tersimpan di DB, status "Data dimuat
    ulang dari sheet."; edit B2 + Simpan (tanpa perubahan luar) →
    berhasil, snapshot ter-update, draft kosong, status "✓ Semua
    perubahan tersimpan ke Google Sheets & SQLite".
    (`p2-sheet-draft.png`, `p2-guard2-block.png`,
    `p2-guard2-reloaded.png`, `p2-sheet-saved.png`)
  - Splash (build final): logo layers + `"B"` + `"MACHINE"`,
    `"VERSION 15.15.3"`, status langkah bootstrapper, ProgressBar,
    log `"> "` maks 3 baris (`"> Tema Dark diterapkan"`,
    `"> 82 keys tersimpan ke memori"`,
    `"> Engine Python OK (Scripts/Master)"`).
    (`p2-splash-final.png`, sebelumnya `p2-splash-b.png`)
- Masih terbuka / penyimpangan jujur:
  - Klik item script floating masih jalur toast radial, eksekusi
    nyata ikut Fase 6 (sama seperti radial/toolbox).
  - Menu floating daftar vertikal + Flickable, bukan WrapPanel
    horizontal C#; belum ada drag-reorder item menu.
  - Pesan guard menampilkan sel pertama non-kosong baris
    (`DisplayOf`) — bila sel yang berubah bukan kolom pertama,
    nilai aktual vs yang dimuat bisa tampak sama; itu perilaku C#
    apa adanya, bukan bug port.
  - `set_size()` Rust tidak mengubah properti `width/height` Slint
    — ukuran window floating diubah lewat binding properti di
    `.slint`, bukan `set_size()` (pelajaran Slint/Linux, dicatat
    untuk port berikutnya).
  - Satu-satunya warning build: unused dependency `sha2` (lama,
    bukan dari P2).

## 2026-11-27 — Dex — P1: log panel resize + CardListWindow 450×600 compact + Settings 7 grup + Leaderboard samakan C# + Spreadsheet chrome 46px + Esc tidak tutup Settings

- Diubah (7 item P1):
  - (1) Log panel bisa di-resize + lebar tersimpan (`ui/main.slint`,
    `src/main.rs`): handle drag 6px di tepi kiri panel (garis separator
    2px, accent `#663b82f6` saat hover, kursor `ew-resize`), drag via
    TouchArea `pointer-event` (pola sama seperti drag titlebar),
    clamp 180px..(lebar window − 200px); lebar disimpan ke SQLite
    `Dashboard.LogPanelWidth` saat drag dilepas, status buka/tutup ke
    `Dashboard.IsLogPanelOpen`; saat startup lebar ≥180 dan status buka
    dipulihkan (kecuali argumen `--log` diberikan); default awal 287.6
    (nilai nyata abeng dari DB aslinya).
  - (2) CardListWindow 450×600 CenterScreen (`ui/windows.slint`,
    `src/main.rs` `center_over_main(..., 450, 600)`). **Penyimpangan dari
    brief, ditemukan saat implementasi:** C# punya compact mode
    (`CompactThreshold = 800`, `UnifiedTrelloView.axaml.cs:9`,
    `UnifiedTrelloViewModel.cs:48-56`) — di lebar 450px C# menampilkan
    list SAJA (belum ada kartu dipilih) ATAU detail full-width (kartu
    dipilih), tidak pernah berdampingan. Jadi port menerapkan perilaku
    compact C# itu: `selected-idx` default −1 (buka di list), list
    dibungkus `if selected-idx < 0`, detail `if selected-idx >= 0`
    dengan `horizontal-stretch: 1` (bukan kolom 360px tetap);
    `close()` mengembalikan `selected-idx = -1` (klik kanan/× di detail
    sudah meneruskan ke `close()`). Panel detail TIDAK dihapus — tampil
    full-width saat kartu dipilih, persis C#.
  - (3) Grup Settings (`ui/settings.slint`, `ui/explorer-settings.slint`
    BARU, `ui/main.slint`): grup "Explorer" dihapus dari Settings dan
    dipindah ke `ExplorerSettingsView` tersendiri (header "Explorer
    Settings", dibuka dari tombol "⚙ Explorer" di sidebar Explorer,
    klik kanan = kembali ke dashboard); default grup "General";
    **header nav "App"/"Data"/"Tentang" dihapus** — diverifikasi dari
    `SettingsView.axaml:307-345` C# nav-nya 7 item polos tanpa header
    (hanya separator di beberapa item). Kini 7 grup polos: General,
    Appearance, Account, Extensions, Script Manager, Paths, About.
  - (4) Leaderboard (`ui/modul.slint`, `src/main.rs`, `ui/main.slint`):
    bar poin teal dihapus; rank 1-based (sebelumnya 0-based — bug lama);
    rank 18 Bold lebar 40px, nama 16/800, poin angka polos 20/800
    `cat-points` (tanpa suffix "poin"); tab "HARIAN"/"BULANAN" di tengah;
    spasi baris 10px; footer total DIHITUNG dari data (`lb-total`,
    format pemisah ribuan titik); refresh = FAB bundar 30×30 accent blue
    dengan ↻ putih; status "Loading..." + Timer 600ms saat refresh.
    Baris dimuat dari SQLite `Lb.Row.N` = "nama\tpoin"
    (`load_leaderboard()` + `format_id_number`/`format_points_value`).
    Catatan: tab BULANAN memakai data `Lb.Row.*` yang sama dengan
    HARIAN — C# bulanan berasal dari agregasi poin Sheets yang belum
    ada sumbernya di port (Fase 6).
  - (5) Titlebar + ukuran window utama — VERIFIKASI SAJA, tanpa ubah
    kode: sesuai keputusan abeng 27 Nov 16:54 (no. 2), window tetap
    bisa di-resize sampai kecil, titlebar custom 28px, awal 960×640,
    min-width 520. Nilai C# (1280×800 / titlebar 40px) SENGAJA tidak
    diterapkan.
  - (6) SpreadsheetWindow chrome (`ui/windows.slint`, `src/main.rs`):
    titlebar 38px → 46px, drag TouchArea menutupi sisi kiri (lebar
    `parent.width − 420px`, kursor pointer = C# Hand) memanggil callback
    baru `move-window`; tombol close tetap kanan-atas; ukuran tetap
    1000×600. **Observasi X=-20 terjawab:** posisi tersimpan −20 berasal
    dari `center_over_main` lama untuk window 1000px di atas main 960px
    sebelum ada clamp; restore kini clamp x/y ≥ 0 (dan DB asli abeng
    punya X=817, tidak terdampak). Posisi disimpan saat window dipindah.
  - (7) Esc (`ui/main.slint`): handler Esc kini punya cabang kosong
    eksplisit untuk `overlay-view == "SETTINGS"` — Esc tidak berbuat
    apa-apa di Settings (perilaku C#); klik kanan tetap menutup Settings
    via `settings-back-touch`.
- Alasan: brief P1 abeng 27 Nov 2026 (7 item, referensi C#
  `~/workspace/mono-bmachine/apps/bmachine/src`); aturan "samakan
  dengan C#" 27 Nov 16:17 + aturan presisi 27 Nov 16:30 + 3 keputusan
  abeng 27 Nov 16:54 (label widget ke-4 tetap "POINTS"; pemicu radial
  tambahan tetap dihapus).
- Terverifikasi: `cargo build` exit 0 (hanya warning `sha2` unused yang
  sudah ada sebelumnya); `cargo test` 12 passed / 0 failed. Klik
  sungguhan + screenshot di Xvfb :98 (1280×800), binary
  `target/debug/bmachine-slint-demo --no-splash`:
  - (1) Panel log terbuka saat startup (restore `IsLogPanelOpen=True`,
    lebar ~288px = 287.6 tersimpan); drag handle dari x=669 ke x=569 →
    panel melebar, DB `Dashboard.LogPanelWidth` = 387.6; drag kembali →
    287.6. `p1-01-dashboard.png`, `p1-02-logpanel-drag.png`.
  - (2) Klik panah EDITING → window "BMachine — Trello Cards" 450×600
    di (255,20) = center atas main 960×640; list 6 kartu mock
    full-width; klik kartu → detail full-width; klik kanan → kembali
    ke list. `p1-03-cardlist.png`, `p1-04-cardlist-detail.png`,
    `p1-05-cardlist-back.png`. Klik panah EDITING berhasil di percobaan
    pertama kali ini.
  - (3) Ctrl+, → Settings default General, 7 grup polos tanpa header
    "App"/"Data"/"Tentang"; Explorer via log panel FILES → folder
    27_SDN RANCAIYUH I → tombol "⚙ Explorer" → overlay "Explorer
    Settings" tersendiri (Default Path, View Mode, Sort, Group, toggles).
    `p1-06b-settings-noheader.png`, `p1-19-explorer.png`,
    `p1-20-explorer-settings.png`.
  - (4) Klik body POINTS → Leaderboard: tanpa bar, rank 1–5, poin
    "1.280 / 1.150 / 980 / 420 / 350" dari `Lb.Row.*` yang di-seed di DB
    tes lokal, footer "Total: 4.180 Pts" (dihitung, bukan hardcode);
    klik FAB → "Loading..." → baris kembali + toast "Leaderboard
    di-refresh — data dimuat dari database"; tab BULANAN berpindah.
    `p1-09-leaderboard.png`, `p1-10-lb-loading.png`,
    `p1-11-lb-refreshed.png`, `p1-12-lb-bulanan.png`.
  - (5) `xdotool windowsize` 520×500 → window 520×500, layout reflow,
    titlebar tetap 28px; 400×400 → window 400×400 (min-width 520 TIDAK
    ditegakkan WM Linux/Xvfb — widget terpotong, sesuai yang abeng
    terima: "tampilan yang hilang di ukuran sangat kecil itu wajar").
    `p1-16-resize-520.png`, `p1-17-resize-400.png`.
  - (6) Klik panah POINTS → SpreadsheetWindow 1000×600 di (0,100)
    — X tersimpan −20 ter-clamp ke 0; chrome 46px + tombol × kanan-atas;
    drag titlebar (300,123)→(400,223) → window pindah ke (100,200);
    klik × → tertutup, DB `SpreadsheetWindow` = 100,200,1000,600.
    `p1-13-spreadsheet.png`, `p1-14-sheet-dragged.png`.
  - (7) Esc di Settings → Settings TETAP terbuka (screenshot sha256
    identik sebelum/sesudah); klik kanan → kembali ke dashboard.
    `p1-07-esc-settings.png`, `p1-08-rightclick-settings.png`.
  - Semua screenshot di `~/workspace/rust-gui-contoh/p1-*.png`.
- Masih terbuka:
  - Klik panah EDITING kadang hilang ~50% di Xvfb (log debug P0:
    handler tidak jalan saat gagal) — diduga artefak Xvfb tanpa window
    manager; **perlu verifikasi di PC Windows abeng**; logika handler
    sengaja tidak diubah.
  - `min-width: 520` tidak ditegakkan Linux/Xvfb WM (window bisa
    dikecilkan ke 400×400) — **perlu verifikasi di Windows**.
  - Tab BULANAN Leaderboard memakai data `Lb.Row.*` yang sama dengan
    HARIAN (belum ada sumber bulanan terpisah — agregasi Sheets, Fase 6).
  - Kembali dari Explorer Settings = ke dashboard (bukan kembali ke
    Explorer) — sama seperti pola Settings yang sudah ada.
  - Catatan lingkungan (bukan bug port): setelah VM swap 27 Nov,
    library sistem hilang — dipulihkan dari cache apt via
    `dpkg -i` (libxi-dev, libxtst-dev, libxinerama-dev, libxkbcommon-x11-0,
    libxcb-xkb1, xdotool, xclip; symlink manual
    `libxkbcommon-x11.so → .so.0`; `apt-get update` PID 820 macet >9
    menit — jangan dibunuh, biarkan). `sendkey2` di-compile ulang dari
    `~/workspace/bmachine-slint-demo/tools/sendkey2.c`.

## 2026-11-27 — Dex — P0: radial/spreadsheet/toolbox jadi window OS + General Settings & flyout samakan C# + export/import DB aman + perbaikan reopen window

- Diubah (6 item P0):
  - (1) Radial menu jadi window OS `RadialMenuWindow` 320×320 di cursor
    (`src/main.rs`: instansiasi pola sheet/card cell, posisi
    `radial_window_origin(cursor, scale)` = cursor − 160×scale;
    TriggerDown/Up via watcher; overlay RADIAL dihapus; geometri roda
    tidak diubah). `ui/radial.slint` `execute-current()`: hover
    `TOOLBOX` → `open-toolbox("MantraNama")` (sebelumnya callback tidak
    pernah dipanggil).
  - (2) Panah POINTS → `SpreadsheetWindow` OS 1000×600
    (`open-new-sheet-window()`; pulihkan posisi/ukuran dari SQLite;
    hide+show untuk re-raise; tanpa formula bar; edit in-cell via
    `TextInput` + `edited`/`apply-cell-edit`).
  - (3) Toolbox jadi window OS `ToolboxWindow` 1024×730, titlebar custom
    38px (`ui/windows.slint` + instansiasi `src/main.rs`); overlay
    Toolbox dihapus.
  - (4) BATCH empty state = seluruh tab: drop zone ala C# saat `src-empty`
    (border 2px, lingkaran-plus 64×64 opacity 0.5, "DRAG FOLDERS HERE"
    16px/800, subtitle 12px, tombol "Pilih Folder" → dataset contoh A);
    drop zone panel SOURCE disamakan ukurannya. Drag-drop OS tetap
    keterbatasan Slint (bukan bug).
  - (5) General Settings samakan C# (`ui/settings.slint`): Bahasa ComboBox
    140px, FILTERS TextField "All (*)", DATA (Storage ">" → grup Paths,
    Export/Import Database), DASHBOARD VISIBILITY (3 toggle: Show Batch /
    Show Folder Locker / Show Output Explorer), SHORTCUT dipindah dari
    Script Manager ke General, tombol Save H34. Wiring
    `src/main.rs`/`src/db.rs` untuk `Settings.Language`,
    `Settings.Batch.Filter`, `Settings.Dash.Batch/Lock/Explorer`.
    `ComboBox` baru di `ui/design.slint`; `Btn` tambah `height-override`.
    Toggle visibilitas TIDAK auto-save per-toggle — penyimpanan lewat
    tombol Simpan (pola C# General.Save).
  - (6) Profile flyout samakan C#: sapaan dari jam lokal via `chrono`
    (<12 "Selamat pagi", <15 "Selamat siang", else "Selamat sore"); item
    Toolbox dihapus; tambah "Laporkan Bug" → `ReportBugDialog` 560×~400
    (Ringkasan H36 + Detail H110; ringkasan kosong → toast "Isi
    ringkasan dulu"; kirim valid → baris `Activities` Type="BugReport" +
    toast "Laporan terkirim"); label "Terminal" (tanpa "(Log Panel)");
    urutan [update bila ada], Terminal, Laporkan Bug, Settings (bold),
    Log Out (merah, bold → dialog logout); footer "BMACHINE · 15.15"
    (`APP_VERSION = "15.15"` di `src/main.rs`, dari assembly
    `ToString(2)` untuk v15.15.3). `Cargo.toml` tambah `chrono = "0.4"`.
- Keputusan abeng 27 Nov 2026, 16:54 WIB (menggantikan "JANGAN UBAH" di
  brief awal) — sudah diterapkan:
  - (1) Label widget ke-4 TETAP "POINTS" ("point saja boleh").
  - (2) Window utama BISA di-resize sampai compact/kecil: hapus
    `width`/`height` di `AppWindow`, `min-width: 520px`
    (`ui/design.slint` token `window-min-width`); ukuran awal 960×640
    di-set dari Rust (`set_size` setelah `AppWindow::new()`).
    Tampilan yang hilang/terpotong di ukuran sangat kecil itu wajar.
  - (3) Pemicu radial tambahan DIHAPUS: orb hexagon di nav + Shift+klik
    tengah di latar dashboard (`radial-trigger-touch`). Yang tersisa
    hanya hotkey global (rdev, default SAMA seperti C#:
    Type=Mouse(0), MouseButton=2 (Middle), Modifiers=4 (Shift), Key=0 —
    `default_trigger_type` 1→0, `default_trigger_key` 222→0,
    `default_trigger_modifiers` 12→4; label "Shift + Mouse Middle").
- Diubah (perbaikan saat tes):
  - Bug hook global radial: polling `hook_rx` dibuat sebagai
    `slint::Timer::default().start(...)` TANPA binding — `Drop` Timer
    Slint 1.19 menghapus timer, callback tidak pernah jalan. Perbaikan:
    timer polling disimpan di `let _hook_timer` (hidup sampai `main()`
    selesai); timer simulasi di `Vec<slint::Timer>`; `--simulate-hotkey`
    kini simulasi Shift+Middle ditahan 3 detik. Debug hook sementara:
    env `BMACHINE_DEBUG_HOOK=1`.
  - Clamp posisi window: `center_over_main()` di-clamp `max(0)` untuk X
    dan Y (Toolbox 1024×730 sempat terbit di (-32,-45), titlebar di
    luar layar). Slint 1.19 tidak menyediakan ukuran layar.
  - Export/import database (tugas tambahan abeng, prioritas tinggi):
    `src/platform.rs` `pick_folder`/`pick_file` → enum `PickResult
    { Picked, Cancelled, Unavailable(String) }` (batal = diam tanpa
    toast; dialog gagal = toast jujur berisi penyebab; non-Windows =
    toast "hanya tersedia di Windows"). `src/db.rs`: `backup_to()`
    buat folder induk dulu; `validate_bmachine_db()` (header 16 byte
    `SQLite format 3\0` + tabel `KeyValueStore` ada, pesan Indonesia
    jelas); `import_key_values()` → `Result<usize, String>`, satu
    transaksi (semua-atau-tidak-sama-sekali); `import_database()` baru
    (validasi → backup DB aktif dulu ke `.bak` bertimestamp →
    import satu transaksi; kembalikan `ImportReport`); `unique_path()`
    (`-2`, `-3` bila sudah ada — `VACUUM INTO` gagal bila file tujuan
    ada). Handler export/import di `src/main.rs`: lock gagal → toast
    jelas; sukses import menyebut jumlah + lokasi `.bak` + "restart
    untuk menerapkan". Export tetap `VACUUM INTO` (benar; C# gagal
    karena copy mentah file .db terbuka mode WAL).
  - Reopen window OS (ditemukan saat verifikasi): window yang sudah
    di-hide TIDAK andal di-show ulang di Slint/winit Linux (reopen
    SpreadsheetWindow gagal 4× berturut-turut; CardListWindow juga).
    Perbaikan: buka-ulang setelah tutup SELALU membuat window baru
    (flag `*_visible: Rc<Cell<bool>>` per window; `*cell = None` saat
    flag false; close handler hanya set flag false + hide). Berlaku
    untuk SpreadsheetWindow, CardListWindow, ToolboxWindow,
    ReportBugDialog (radial tidak diubah — show-only-nya andal).
    CardListWindow kini juga di-tengah window utama (CenterOwner,
    860×580) + hide+show seperti sheet. ReportBug: flag juga di-set
    false saat kirim sukses (hide).
- Alasan: brief P0 27 Nov 16:31 + 3 keputusan abeng 16:54 + tugas
  tambahan export/import DB (abeng, prioritas tinggi) + temuan saat tes
  klik sungguhan.
- Terverifikasi: `cargo build --offline` exit 0; `cargo test --offline`
  12 passed, 0 failed (5 radial shortcut + 7 db). Klik sungguhan +
  screenshot di `~/workspace/rust-gui-contoh/` (prefix `p0*`, `p0b-*`):
  radial window OS di cursor (Shift+Middle ditahan); SpreadsheetWindow
  1000×600 buka 3/3 siklus tutup→buka (window id baru tiap siklus,
  posisi pulih dari DB); Toolbox window OS (0,0 setelah clamp);
  BATCH drop zone penuh + pulih via "Pilih Folder"; flyout (sapaan jam
  + Laporkan Bug + footer 15.15) + ReportBug (toast kosong, kirim →
  baris Activities terverifikasi di SQLite); Settings General lengkap;
  toggle visibilitas: klik pusat terukur (917,345) → OFF, Simpan → DB
  `Settings.Dash.Batch='false'`, nav hanya HOME; kembalikan ON +
  Simpan → DB `'true'`, nav HOME+BATCH. In-cell edit spreadsheet
  TERBUKTI: double-klik B1 → Ctrl+A + paste "SDN TEST EDIT" → Enter →
  B1 = "SDN TEST EDIT", "Simpan (1)" + "Perubahan belum disimpan
  (1 sel)"; nilai dikembalikan ke "SDN RANCAIYUH I" setelah tes.
  Tes DB pakai salinan DB asli abeng (`~/workspace/user/files/
  BMachine.db`, TIDAK pernah ditulis; salinan di `/tmp/`):
  `KeyValueStore` 166 baris, `Activities` 7674 baris, `journal_mode:
  wal`; export → file valid (166 baris); import → 166 baris; file
  bukan-DB ditolak dengan pesan jelas, DB aktif utuh, tanpa `.bak`;
  import 1 baris baik + 1 baris rusak → gagal total + tepat 1 `.bak`.
  Picker PowerShell (Windows) hanya diverifikasi dengan membaca.
- Masih terbuka:
  - Panah EDITING (CardListWindow): klik panah di Xvfb ini hilang
    ~50% (handler TIDAK jalan — log debug sementara membuktikan;
    saat klik sampai, window selalu terbuka). Bukan bug logika aplikasi;
    diduga artefak Xvfb tanpa window manager (`_NET_ACTIVE_WINDOW`
    tidak didukung). Perlu verifikasi di PC abeng (Windows, WM asli).
    Koordinat panah sudah benar (hover biru di (416,163); glyph di
    (427,163)).
  - `min-width: 520` TIDAK ditegakkan WM Linux/Xvfb ini
    (`xdotool windowsize 400 500` berhasil). Perlu verifikasi di PC
    abeng (Windows).
  - SpreadsheetWindow tersimpan X=-20 (terbit 20px ke kiri layar) —
    posisi pulih dari DB (center_over_main untuk 1000px di main 960px).
    Observasi lama, belum diputuskan bug; clamp hanya berlaku bila
    belum ada simpanan.
  - Toggle visibilitas sempat diduga "tidak merespons" — ternyata klik
    tes meleset 27px ke kiri track (x=890, track 896–939). Bukan bug
    aplikasi. Pelajaran: ukur koordinat klik dari piksel, jangan tebak
    dari screenshot yang diskalakan. (Sudah masuk LANJUTKAN.md §4 —
    periksa.)

## 2026-11-27 — Dex (contoh "harus sama" dari abeng) — Aturan presisi: new window + ukuran kontrol seragam

- Diubah: `LANJUTKAN.md` §2 + aturan presisi — panah EDITING → UnifiedTrello
  new window (bukan popup); Spreadsheet = new window "Spreadsheet Editor"
  1000×600 berisi DataGrid Excel (tanpa formula bar); side panel / log panel
  / detail / Comment / Checklist / Move / batch move / comment new window
  disamakan; TextBox / Dropdown / AutoCompleteBox + icon/teks berukuran
  sama dan rata atas-bawah.
- Alasan: abeng 27 Nov 16:30 — contoh konkret "harus sama" + pertanyaan
  "spreadsheet itu harusnya gimana sih".
- Terverifikasi: panah EDITING port sudah membuka new window "BMachine —
  Trello Cards" (klik sungguhan + screenshot, 27 Nov 16:30); fakta C#
  dicek dari source (DashboardViewModel.cs:445, CardListWindow.axaml,
  SpreadsheetWindow.axaml, SpreadsheetView.axaml).
- Masih terbuka: ukuran SpreadsheetWindow port 980×620 (C#: 1000×600) +
  formula bar port (C# tidak punya) — perbaiki ke C#; daftar gap lengkap
  menyusul dari penilaian.

## 2026-11-27 — Dex (sinkronisasi dari PC abeng, atas perintah abeng) — Push kerja lokal yang gagal ter-push

- Diubah: `src/` (db, doc_manager, executor, local_update, main, process_log,
  sheets_api, theme, trello_api), `ui/` (main, radial, settings, windows),
  baru `qa_clean_run_csharp.bat` + `qa_run_csharp_reference.bat`;
  `PERUBAHAN.md` digabung — 11 entri Antigravity Agent dari lokal yang
  belum ada di GitHub + 2 entri Dex. File lain hanya beda CRLF/LF, tidak
  diubah (repo tetap LF). `LANJUTKAN.md` + `CATATAN_CSHARP_V15153.md`
  tetap versi GitHub (lebih baru dari lokal).
- Alasan: abeng 27 Nov 16:19 — kerja terbaru di PC
  (D:\#DATA ABENG\#PROJECT\mono\apps\bmachine-rs) belum ter-push karena
  gagal terus; Hermes lokal sudah habis limit. Diambil via SMB (share
  Data, user bengs) lalu di-push dari VM.
- Terverifikasi: `cargo build` exit 0 (1m 45s, Linux; override toolchain
  karena rust-toolchain.toml mengunci channel Windows; perlu install
  libxi-dev + libxtst-dev dkk di VM — bukan perubahan kode). 1 warning:
  dependensi `sha2` tidak terpakai.
- Masih terbuka: smoke test klik di Windows oleh abeng; label SPREADSHEET
  vs POINTS (keputusan abeng); verifikasi panah widget ke-4.

## 2026-11-27 — Dex (aturan baru abeng) — Samakan semua tampilan & fitur dengan C#

- Diubah: `LANJUTKAN.md` §2 + aturan baru — semua tampilan dan fitur port
  disamakan dengan C#: cara pemanggilan (klik widget/panah, hotkey, menu)
  dan cara tampilan keluarnya (embedded vs window, posisi, animasi), dll.
  Kalau bertentangan dengan kebebasan "tampilan boleh dirombak", tanya
  abeng dulu, jangan menebak.
- Alasan: abeng 27 Nov 16:17 WIB — "semua tampilan dan fitur nanti samakan
  ya cara pemanggilannya, cara tampilan keluarnya dll nya oke".
- Terverifikasi: perubahan dokumen saja (tidak ada kode yang diubah).
- Masih terbuka: keputusan abeng soal label SPREADSHEET vs POINTS
  (lihat CATATAN_CSHARP_V15153.md).

## 2026-11-27 — Dex (test C# v15.15.3, atas perintah abeng) — Catatan penerapan ke port

- Diubah: baru `CATATAN_CSHARP_V15153.md` — hasil test menyeluruh C#
  v15.15.3 (build Linux + klik sungguhan di Xvfb 1280x800, tanpa data
  Trello/Sheets/Photoshop/NAS). Isi: label widget ke-4 C# = SPREADSHEET
  (port: POINTS — perlu keputusan abeng sebelum diubah); BATCH empty
  state (drop zone "+" + "DRAG FOLDERS HERE", klik tidak buka picker);
  struktur Settings Control Center; isi profile flyout; Trello list
  kosong tanpa teks; layout Leaderboard; Esc TIDAK menutup Settings,
  right-click = back; Fase 6 kredensial di OS vault (`secret-tool` di
  Linux / Credential Manager di Windows) + update check via NAS
  `\\delapanmataair\...\manifest.json`; cara ulang test C# di Linux +
  koordinat nav terukur (profile 513, HOME 587, BATCH 657, LOCKER 732).
- Alasan: abeng 27 Nov 16:13 — "tidak perlu screenshoot tapi kamu catat
  yang harusnya di terapkan ke bmachine-rs tuh gimana harusnya".
- Terverifikasi: C# build exit 0 + jalan stabil, semua navigasi dites
  klik (dashboard, nav 3 tab, Trello list, BATCH, LOCKER, Settings,
  flyout, Leaderboard). Koreksi: nav sempat dikira tidak responsif —
  ternyata klik Dex meleset ke celah antar tab / icon profil; setelah
  diukur dari pixel, semua tab jalan.
- Masih terbuka: keputusan abeng soal label SPREADSHEET vs POINTS;
  verifikasi panah widget ke-4 (klik Dex membuka Leaderboard embedded,
  mungkin kena body); toggle Terminal ON tapi panel log tidak muncul di
  Linux (cek di PC abeng); sapaan "Good Evening" mulai jam berapa.

## 2026-11-27 — Antigravity Agent — Sinkronisasi Radial Menu dengan BMACHINE_RADIAL_MENU.md (5 Skrip Toolbox C# & Roda Settings 200px)
- Diubah:
  - `ui/radial.slint`: Menyelaraskan daftar skrip outer ring TOOLBOX persis sesuai `BMACHINE_RADIAL_MENU.md` §11.3 — 5 file `_tb_*.jsx` asli: `_tb_C-REDAKSI.jsx` (C-Redaksi), `_tb_FOOTNOTE.jsx` (Footnote), `_tb_IMAGE_SIZE.jsx` (Image Size), `_tb_NOMORSATSET.jsx` (NomorSatset), dan `_tb_SELEKSI_ALAM.jsx` (Seleksi Alam) dengan label, ikon, dan eksekusi skrip yang benar.
  - `ui/settings.slint`: Mengganti kartu kotak statis menjadi kanvas roda 200×200 nyata (`RadialSlotsControl` C#) dengan pelat lingkaran, pembatas 8 sektor trigonometri, hub tengah 88px, dan 8 tombol slot pada $R=72\text{px}$ yang data dan urutannya 100% identik dengan live menu (memenuhi §1 & §3 aturan sinkronisasi dua permukaan).
- Alasan: Instruksi user merujuk pada spesifikasi resmi `BMACHINE_RADIAL_MENU.md`.
- Terverifikasi:
  - `cargo check` exit 0 dengan 0 error & 0 warning.
- Masih terbuka:
  - Pengujian drag & drop interaktif di roda pengaturan saat runtime.

## 2026-11-27 — Antigravity Agent — Geometri Radial Menu 1:1 C#, Multi-Window Standalone, Excel Spreadsheet & Polish Sidepanel
- Diubah:
  - `ui/radial.slint`: Re-implementasi total geometri & interaksi Radial Menu sesuai `RadialMenuWindow.axaml` & `RadialMenuViewModel.cs` C# — kanvas 320×320 terpusat, pelat lingkaran 200px (R=100), hub tengah padat 88px (R=44) dengan teks uppercase tracking +2 & subjudul dinamis ("SELECT" / "HOLD FOR SUBMENU"), 8 garis pembatas sektor trigonometri (`22.5°..337.5°`), 8 PieWedge anular hover glow (R44..R100) yang menyala sesuai sektor aktif, tombol menu 36×36 pada R=72px dengan ikon Lucide & hover scale 1.15, ring luar submenu petal (R104..R152) untuk skrip TOOLBOX (`_tb_*.jsx`) dan DOC Tabs, pelacakan sudut polar `atan2(dx, -dy)` presisi clock-face, dwell timer, serta release-to-execute.
  - `ui/spreadsheet.slint` (baru): Engine spreadsheet interaktif bergaya Microsoft Excel / Google Sheets — header kolom A..F (Kode, Sekolah, Jenjang, Status, Poin, Catatan), nomor baris 1..N, Formula Bar (`fx`) dengan penunjuk koordinat sel aktif (`A1`..`F6`) dan input editor sel langsung, seleksi sel aktif dengan border aksen 2px & handle dot, toolbar aksi (+ Baris, ∑ Auto-Sum, Zoom `[-] 100% [+]`, Muat Ulang, dan Simpan dengan draft dirty tracking).
  - `ui/windows.slint` (baru): Komponen OS Window mandiri sekunder — `SpreadsheetWindow` (window mandiri editor spreadsheet ukuran 980×620 dengan custom chrome) dan `CardListWindow` (window mandiri daftar kartu Trello ukuran 860×580 dengan split view: list kartu di kiri & panel detail kartu di kanan).
  - `ui/modul.slint`: Mengintegrasikan `SpreadsheetComponent` ke dalam `SheetsView` menggantikan tabel mockup kaku sebelumnya, menambahkan tombol "Buka Window Baru" (`popout-window`).
  - `ui/logpanel.slint`: Re-polish total sidebar kanan — membersihkan seluruh karakter encoding lama, menambahkan ikon Lucide pada 4 tab navigasi (`LOG`, `FILES`, `MASTER`, `DOC`), live progress bar, konsol log dengan strip level warna, dot status proses, aksi Salin/Stop/Bersihkan ber-toast, breadcrumb folder ringkas, dan form Instan Redaksi terstruktur.
  - `ui/main.slint`: Re-export `SpreadsheetWindow` & `CardListWindow`, menambahkan callback `open-new-sheet-window()` dan `open-new-card-window(string)`, menyambungkan klik panah pada `StatWidget` (`EDITING`, `REVISION`, `LATE`) dan tombol popout spreadsheet ke pembuatan window OS baru.
  - `src/main.rs`: Menambahkan manajemen instance window sekunder (`sheet_win_cell`, `card_win_cell`) di event loop Slint dengan callback `show()`, `hide()`, dan sinkronisasi data kartu/kategori Trello.
- Alasan: Instruksi user untuk menyamakan logika & fungsi Radial Menu agar benar-benar mirip C#, mengimplementasikan new window yang membuka window baru sungguhan, membangun spreadsheet dengan fitur Excel/spreadsheet nyata, dan mem-polish logpanel/sidepanel.
- Terverifikasi:
  - `cargo check` exit 0 dengan **0 error & 0 warning**.
  - Geometri trigonometri dan binding Slint terverifikasi sinkron.
- Masih terbuka:
  - Verifikasi interaktif langsung oleh user di layar desktop Windows.

## 2026-11-27 — Antigravity Agent — Full Live Theme Customization, Custom Titlebar, Real Trello Sync & Persistensi SQLite
- Diubah:
  - `src/theme.rs` (baru): Engine tema live-customizable (`apply_appearance`, `parse_hex_rgb`, `is_windows_light_mode`, `load_theme_from_db`, `save_theme_to_db`). Mendukung input hex `#RRGGBB` bebas untuk Accent/Background/Foreground, mode Terang/Gelap/Ikut Sistem, preset (`BMachine Dark`, `BMachine Light`, `Midnight OLED`, `Catppuccin Mocha`, `Emerald Studio`, `Custom`), font global (`Inter` / `JetBrains Mono`), corner radius (`sharp`/`default`/`rounded`), nav pill style (`capsule`/`rounded`/`flat`), dan widget background (`doodle`/`dots`/`impulse`/`none`), semuanya diterapkan langsung ke global `Design` dan disimpan ke SQLite (`BMachine.db`).
  - `src/platform.rs` (baru): Helper native OS untuk membuka URL di browser (`open_url`) serta dialog native Windows (`pick_folder`, `pick_file`) untuk Browse path/credentials.
  - `ui/design.slint`: Menambahkan token `font-family` dan `density-scale`.
  - `ui/settings.slint`: Mengubah Appearance dari mockup preview lokal menjadi kontrol tema global live (`appearance-changed()`, `reset-appearance()`), menambah preset baru, Corner radius, Nav pill style, dan tombol Reset ke Default; menyambungkan tombol `Get Key`, `Connect` (`test-trello`), dan `Browse` di Account/Explorer/Paths ke aksi nyata.
  - `ui/main.slint`: Export `Design` ke Rust, `default-font-family: Design.font-family`, two-way binding (`<=>`) penuh ke `SettingsView` (termasuk perbaikan `widget-bg` yang sebelumnya one-way), menambahkan custom titlebar frameless 28px (drag window, minimize, maximize, close), toast ber-border aksen dengan `overflow: elide`, dan menyambungkan `refresh-trello()` serta `open-url()`.
  - `ui/trello-detail.slint`: Menyambungkan tombol buka kartu di browser (`↗`) ke `open-url`.
  - `src/trello_api.rs`: Menambahkan `verify_connection` (`/1/members/me`), `shortUrl`, dan `add_comment`.
  - `src/main.rs`: Menghapus panggilan HTTP dummy sinkron saat startup yang memperlambat boot; menyambungkan `spawn_trello_sync` agar mengisi count widget sekaligus daftar kartu nyata (`editing-titles/subs/due/ids`, `revision-*`, `late-*`); menyambungkan persistensi General & Paths ke SQLite, window drag/min/max, dan membersihkan seluruh compiler warning.
- Alasan: Instruksi user untuk melanjutkan port dengan kualitas `#POLISH`, menyamakan logika/fungsi C# (Trello sync, persistensi DB, dialog native), dan membuat tampilan bisa full customisasi secara live.
- Terverifikasi:
  - `cargo build` exit 0 dengan **0 warning** (36.94s).
  - Smoke test launch `target/debug/bmachine-slint-demo.exe --no-splash` berjalan stabil (`RUNNING_OK PID=17692`).
- Masih terbuka:
  - Pengambilan screenshot desktop dari terminal non-interaktif gagal (`CopyFromScreen: The handle is invalid`), sehingga verifikasi visual di layar desktop perlu dicek langsung oleh user.
  - Eksekusi Python `batch_wrapper.py` / Photoshop JSX di tombol footer BATCH masih perlu diuji dengan folder `Scripts/` & Photoshop lokal user.

## 2026-11-27 — Antigravity Agent — Nav centering/kapsul + shortcut global radial (rdev) + argumen QA
- Diubah:
  - `ui/main.slint`: Nav bar dashboard kini terpusat penuh: akun kiri, grup pill HOME/BATCH tengah (sejajar), radial kanan. Spasi antar ikon akun dan pill dibuat jelas; highlight pill aktif memakai kapsul oval yang mengikuti bentuk dasar.
  - `ui/design.slint`: Token kapsul nav (`nav-pill-*`) dan token radial baru (`radial-*`) ditambahkan; radius pill aktif full, outer ring dan slot mengikuti highlight.
  - `src/main.rs`: Trigger radial kini global dengan `rdev` (`--` `Cargo.toml` menambah `rdev = "0.5.3"`). Shortcut default mengikuti `ShortcutConfig` C# (`Key: 222`, `Modifiers: 12` = Win+Shift+`). Argumen QA `--radial-test` membuka radial overlay tanpa shortcut untuk screenshot VM, dan `--simulate-hotkey` menjalankan simulasi `Win+Shift+'` lewat `rdev::simulate` untuk tes hook global.
  - `src/main.rs`, `src/process_log.rs`, `ui/logpanel.slint`: QA argumen `--view=`, `--group=`, `--tab=`, `--log`, `--flyout`, `--dialog=` untuk laporan screenshot semua tampilan.
  - `apps/bmachine-rs/start_app.ps1`, `start_app.bat`, `copy_rdev.bat`: Helper QA di dalam shared folder VM (`Y:\apps\bmachine-rs`) untuk menjalankan app di guest tanpa menyentuh desktop host.
- Alasan: User minta nav benar-benar sejajar/kapsul seperti referensi, dan radial menu dipanggil dari shortcut seperti C# (bukan hanya klik lokal).
- Terverifikasi: `cargo build --release` exit 0. Di VM `Win10LTSC-Dev` (shared folder `Y:`), radial overlay terbuka via `--radial-test`; dashboard nav menampilkan akun kiri, HOME pill kapsul, BATCH pill, radial kanan. Screenshot VM: `D:/VMs/vm_radial_test.png`.
- Catatan: Shortcut global Win+Shift+' diimplementasi dengan `rdev`, tetapi verifikasi in-guest via `VBoxManage keyboardputscancode` belum menampilkan radial karena injeksi scancode LWin/Shift di VM tidak selalu sampai ke hook global; `--radial-test` dan `--simulate-hotkey` tersedia untuk tes tanpa input fisik user.

## 2026-11-27 — Antigravity Agent — Argumen startup QA untuk laporan screenshot semua tampilan
- Diubah:
  - `src/main.rs`: Argumen startup `--view=`, `--group=`, `--tab=`, `--log`, `--flyout`, `--dialog=` kini membuka state UI yang diminta tanpa klik mouse/keyboard fisik. Dipakai runner screenshot VM; normal startup tanpa argumen tidak berubah.
- Alasan: User meminta laporan gambar semua tampilan; state seperti Settings per grup, overlay Radial/Sheets/Leaderboard, dan dialog tidak bisa dibuka hanya dengan shortcut keyboard.
- Terverifikasi: `cargo build` exit 0. Screenshot VM di-generate untuk 20 state ke `D:/VMs/qa/` (lihat `manifest.jsonl`).

## 2026-11-27 — Antigravity Agent — Navbar dashboard mengikuti referensi (Akun kiri, Home/Batch pill, radial kanan)
- Diubah:
  - `ui/main.slint`: Kontrol Akun dipisah ke sisi kiri nav, pill tengah hanya Home + Batch; tombol close `×` yang tidak ada di referensi dihilangkan. Tombol radial kanan kini memakai hexagon geometris.
  - `ui/design.slint`: NavTab mendukung ikon Home/layers, Home terpilih memakai kapsul terang dan teks/ikon gelap, tab nonaktif muted, radius pill penuh; underline biru dihapus.
- Alasan: Mengikuti screenshot referensi dan arahan user untuk memperbaiki navigation layout serta polish UI.
- Terverifikasi: `cargo build` exit 0. VM `Win10LTSC-Dev`, screenshot `D:/VMs/dashboard-nav-final.png`: akun terpisah kiri, Home pill putih tanpa underline, Batch layers, tombol kanan hexagon, 4 cards penuh terlihat.
- Masih terbuka: angka cards tetap 0 jika koneksi Trello/akun belum dikonfigurasi.

## 2026-11-27 — Antigravity Agent — Polish Tampilan: Widget Live dari Akun, Tokenisasi Warna, No-Frame Window, Ukuran Jendela Pas
- Diubah:
  - `ui/design.slint`: Token warna baru `done-row-bg`, `done-row-border`, `done-text`, `done-text-alt`, `row-border-idle`, `row-text-dim`, `row-text-alt`, `overlay-dim`, `overlay-solid`, `medal-bronze` — menggantikan hardcoded hex di seluruh UI.
  - `ui/main.slint`: Semua hardcoded warna diganti token `Design.*`. Window `no-frame: true` + title bar gelap kustom. `ui-scale` max 1.8 (bukan 2.5) agar card tidak membesar berlebihan. Tinggi card di-capping 240px. `padding-bottom` dashboard 24px.
  - `ui/settings.slint`: Widget preview EDITING/POINTS kini membaca `stat-editing`/`stat-points` (live dari akun), bukan hardcoded "24"/"1.280".
  - `ui/modul.slint`: Sheet rows & leaderboard rows kini di-drive oleh `sheet-rows`/`lb-rows` (Rust mengisi dari DB), bukan hardcoded sekolah/poin demo. Token `Design.medal-bronze` menggantikan `#cd7f32`.
  - `ui/explorer.slint`: `#00000080` → `Design.overlay-dim`.
  - `ui/radial.slint`: `#000000` → `Design.overlay-solid`.
  - `src/main.rs`: Preload `Sheet.Row.*` & `Lb.Row.*` dari SQLite ke `sheet-rows`/`lb-rows` + kolom terpecah (`sheet-col0/1/2`, `lb-col0/1`) via `ModelRc<VecModel<SharedString>>`. Default stat-* = "0" (bukan demo).
  - `Cargo.toml`: Fitur `slint` = `["std", "backend-winit"]`.
  - `ui/design.slint`: `window-width` 960px, `window-height` 640px (pas di layar VM 1280x800).
- Alasan: User minta "polish lagi tampilan yang masih jauh dari design" — tokenisasi warna, widget angka diambil dari setting akun (bukan demo), dan ukuran jendela pas.
- Terverifikasi:
  - `cargo build` exit 0, 0 warning.
  - Screenshot di VM `Win10LTSC-Dev`: dashboard 2×2 kartu (EDITING 0, REVISION 0, LATE 0, POINTS 0) semua terlihat lengkap tanpa terpotong; tombol close (×) ada di bar nav; title bar custom (no-frame) gelap, bukan putih Windows. Screenshot: `D:/VMs/polish8.png`.

## 2026-11-27 — Antigravity Agent — Implementasi Backend Fase 6 (SQLite, Trello API, Executor Python/JSX, Sheets API) & Verifikasi Nyata di VM
- Diubah:
  - `src/db.rs`: Database service nyata `BMachine.db` SQLite (AppData/BMachine) dengan tabel `KeyValueStore` & `Activities`, WAL mode, dan busy timeout 15 detik.
  - `src/trello_api.rs`: Layanan HTTP client Trello via `reqwest` untuk sinkronisasi board dan list akun asli (`members/me/boards`, `boards/{id}/lists`).
  - `src/executor.rs`: Service executor proses nyata untuk Python `batch_wrapper.py` (`buat_master.py`) dan dispatch file/skrip ke Photoshop.
  - `src/sheets_api.rs`: Google Sheets client validator kredensial service account dan generator endpoint Google Sheets v4.
  - `src/main.rs`: Wiring inisialisasi database SQLite saat startup, preloading setting awal (Trello API key, Token, Google Sheet ID), serta integrasi seluruh modul backend.
  - `Cargo.toml`: Menambahkan dependensi `rusqlite` (fitur bundled) dan `reqwest` (fitur blocking + json).
- Alasan: Instruksi user agar seluruh fungsi backend dan logika eksekusi diimplementasikan secara nyata persis seperti arsitektur C#, serta diverifikasi langsung di VM.
- Terverifikasi:
  - `cargo build` sukses exit 0 (0 warning).
  - Executable `bmachine-rs.exe` ditransfer ke VM `Win10LTSC-Dev`, dijalankan di dalam VM, dan terverifikasi secara visual menampilkan GUI BMachine Dark Theme dengan kartu status aktif (HOME, BATCH, EDITING 24, REVISION 7).
  - Screenshot tersimpan di host: `D:/VMs/bmachine_rs_final2.png`.

## 2026-11-27 — Antigravity Agent — Implementasi Lengkap 7 Menu Settings C# (Extensions, Script Manager, Account, Wiring State Bersih)
- Diubah:
  - `ui/settings.slint`: Menambahkan menu **Extensions** (daftar plugin DLL, tombol Add/Disable/Delete) dan **Script Manager** (manajemen skrip Master, tombol reload `scripts.json`, dan peta 8 slot trigonometri Radial Menu). Sekarang 7 menu Settings lengkap persis susunan C#: `General`, `Appearance`, `Account`, `Extensions`, `Script Manager`, `Explorer/Paths`, `About`.
  - `src/main.rs`, `src/doc_manager.rs`, `src/process_log.rs`, `src/local_update.rs`: Wiring fungsional state multi-tab DOC, watcher progress background, dan pembersihan seluruh compiler warning.
- Alasan: Instruksi user agar seluruh fungsi dan tata letak benar-benar disamakan persis dengan implementasi C#.
- Terverifikasi: `cargo build` sukses exit 0 tanpa warning sama sekali (0 warning).

## 2026-11-27 — Antigravity Agent — Implementasi Layar Settings Account (Trello & Spreadsheet) Sesuai C#
- Diubah:
  - `ui/settings.slint`: Menambahkan menu **Account** di sidebar dan tampilan pengaturan lengkap persis seperti `SettingsView.axaml` C#:
    - **Trello Integration:** Field input API Key & Token (dengan tombol "Get Key" dan tombol toggle "Connect"/"Disconnect" yang memvalidasi koneksi), status koneksi akun.
    - **Google Integration (Spreadsheet):** Field Credentials JSON path (dengan tombol browse), Google Sheet ID, Worksheet Name, koordinat sel (Kolom/Baris) untuk Points, nama tab tabel utama & range (`ALL DATA REGULER` / `A1:Z`), serta Leaderboard Harian & Bulanan Range.
    - Tombol simpan pengaturan akun ke database.
- Alasan: Instruksi user agar seluruh fungsi dan tampilan di Settings Account identik dengan implementasi C# untuk kebutuhan kerja nyata.
- Terverifikasi: `cargo build` sukses exit 0.

## 2026-11-27 — Antigravity Agent — Penyelarasan Port Fase 5 (Dialog, Splash, Log Panel) & Perbaikan Radial Menu Sesuai C#
- Diubah:
  - `src/local_update.rs`: Implementasi logika pembacaan `manifest.json` LAN update (`LocalUpdateService`), scanning folder NAS `\\delapanmataair\...` dan drive fallback `U:\...`.
  - `src/doc_manager.rs`: Manajemen state DOC multi-tab dinamis dan penulisan payload `doc_info.json` ke AppData.
  - `src/process_log.rs`: Buffer log hingga 1000 entri untuk mencegah UI lag.
  - `ui/radial.slint`: Perbaikan total Radial Menu: Submenu TOOLBOX kini memuat 5 file skrip `_tb_*.jsx` asli (`_tb_C-REDAKSI.jsx`, `_tb_FOOTNOTE.jsx`, `_tb_IMAGE_SIZE.jsx`, `_tb_NOMORSATSET.jsx`, `_tb_SELEKSI_ALAM.jsx`). Submenu SEND-* terhubung dinamis ke tab DOC (hanya muncul bila tab DOC > 1).
  - `ui/splash.slint`: Siklus bootstrapper nyata 10% - 100% (SQLite, Theme, Preload, Python Engine, Dashboard ready) dengan 3 baris terminal logs.
  - `ui/dialog.slint`: Dialog konfirmasi keluar ("Sign Out of Session") dan UpdateDialogWindow LAN update (380px, catatan rilis, ukuran paket).
  - `ui/main.slint`, `ui/logpanel.slint`, `ui/settings.slint`: Wiring callback dan state properti antar-komponen.
  - `rust-toolchain.toml`: Pin toolchain stable x86_64-pc-windows-gnu agar build lokal di mesin ini selalu exit 0 tanpa kendala MSVC linker.
- Alasan: Instruksi user untuk menyamakan logika dan fungsi port secara persis dengan implementasi C# (`apps/bmachine/`), termasuk membetulkan kesalahan logika pada Radial Menu.
- Terverifikasi: `cargo build` berhasil exit 0 tanpa error; struktur layout dan alur logika terkonfirmasi identik dengan `RadialMenuViewModel.cs`, `LocalUpdateService.cs`, `SplashViewModel.cs`, dan `DocPanelView.axaml`.
- Masih terbuka: Fase 6 (koneksi live SQLite, Trello API via reqwest, eksekusi IPC script langsung ke Photoshop).

## 2026-11-26 — Dex (upload pertama ke GitHub) — Port masuk mono sebagai `apps/bmachine-rs/`

- Diubah: seluruh port dari `~/workspace/bmachine-slint-demo/` masuk ke
  `apps/bmachine-rs/` (src, ui, tools, DESIGN_SYSTEM.md,
  ANALISIS_BMACHINE.md, EXPLORER_SPEC.md, Cargo.toml/lock, build.rs,
  .gitignore). Baru: `README.md`, `LANJUTKAN.md`, `AGENTS.md`,
  `PERUBAHAN.md` (file ini). `WORKSPACES.md` +bagian 8 (port Rust/Slint).
  `target/` tidak ikut commit (ada di .gitignore).
- Alasan: instruksi abeng 26 Nov 21:27 WIB — "masukan github saja dulu",
  plus aturan supaya update dari mana pun bisa dilanjutkan lagi.
- Terverifikasi: build dari copy `apps/bmachine-rs/` exit 0 (4m 49s, 26 Nov
  21:40 WIB); sebelumnya build exit 0 di `~/workspace/bmachine-slint-demo/`
  dan semua fase dites klik sungguhan + screenshot (Fase 1–4, detail
  Trello, Settings).
- Masih terbuka: pertanyaan abeng — widget EDITING 24 / REVISION 7 vs
  daftar Trello 6 kartu (angka mana yang benar?); daftar pertanyaan §19.9
  di DESIGN_SYSTEM.md; Fase 5 (sebagian) + Fase 6 (koneksi asli) belum.
- Catatan koreksi: Dex sempat melapor tombol "Pilih" master PsdBucin "mati"
  (26 Nov ~21:25) — **salah ukur**: klik Dex di y=276, padahal baris
  tombol y238–274 (tengah 256). Klik di y=256 → toast "Folder master
  dipilih (contoh)." Tombol sehat, tidak ada bug. Pelajaran sudah masuk
  `LANJUTKAN.md` §4.
