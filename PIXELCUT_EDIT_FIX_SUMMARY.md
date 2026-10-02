# Perbaikan Mode Edit Pixelcut — Shortcut Masking, Save, & Pen Tool

Branch: `fix/edit-mode-shortcuts-save` (dari `main` @ `b6d4ca5`)
Tanggal: 2026-10-02
Status: build sukses, terverifikasi lewat uji langsung (Xvfb + screenshot).

## Ringkasan

1. **Shortcut keyboard Delete/Restore masking** (baru): `Delete`/`Backspace` = delete masking,
   `Shift+Delete`/`Shift+Backspace` = restore masking. Context-aware: saat selection tool
   sedang menggambar, Delete/Backspace tetap memangkas titik terakhir (perilaku lama).
2. **Ctrl+S langsung replace** tanpa membuat file `.bak`.
3. **Pen tool ala Photoshop**: drag knob handle Bézier (tahan Alt = patahkan simetri),
   Alt+klik anchor = convert smooth→corner, hover highlight + kursor kontekstual,
   hint & tooltip diperbarui.
4. **Bug yang ditemukan saat pengujian**: `_pendingSelMode` basi — Alt+klik (mode Subtract)
   lalu Enter memakai mode Subtract basi sehingga commit menghasilkan seleksi kosong.
   Diperbaiki: latch mode hanya untuk tool yang commit saat pointer release.
5. **Rapian UI/UX kecil**: tooltip dinamis untuk tombol Erase/Restore, hint bar sinkron
   dengan shortcut masking, teks catatan shortcut di panel pengaturan diperbarui.

## Perubahan per file

### `apps/pixacompact/Services/Editing/EditorShortcutMap.cs`
- Enum `EditorShortcutAction` += `MaskDelete`, `MaskRestore`.
- Definisi baru: `MaskDelete` ("Delete masking", `TxtEditMaskDeleteShortcut`, default `Delete`),
  `MaskRestore` ("Restore masking", `TxtEditMaskRestoreShortcut`, default `Shift+Delete`).
- Baru: `IsReservedKeyForAction(action, key)` — Delete/Backspace boleh di-bind ke aksi
  masking, tetap reserved untuk aksi lain (Space/Escape/Enter reserved untuk semua).
- `TryValidate` memakai `IsReservedKeyForAction` (konflik antar-aksi tetap terdeteksi).
- `TryGetAction` tidak lagi menolak mentah-mentah reserved key; tiap definisi dicek
  dengan `IsReservedKeyForAction` sehingga binding Delete/Backspace ke aksi masking
  bisa resolve.

### `apps/pixacompact/Views/PreviewWindow.Editor.cs`
- `HandleEditorKey`: blok Back/Delete dibuat context-aware —
  - tool sedang menggambar (`IsActive`/`CanCommit`, tanpa Shift) → `RemoveLastPoint()` (lama);
  - selain itu → resolve via `EditorShortcutMap.TryGetAction` + fallback pasangan
    Back↔Delete, lalu `ApplySelectionToMask(0, …)` / `(255, …)`.
  - Restore tanpa `_session.HasRestoreSource` → Toast peringatan (guard bawaan
    `ApplySelectionToMask`, tidak diubah).
- `switch` shortcut: `case MaskDelete/MaskRestore` (untuk binding non-Delete, mis. remap `D`).
- `SaveInPlace()`: blok backup `.bak` dihapus — Ctrl+S langsung me-replace file.
  Tidak ada referensi `.bak` lain di codebase.
- `UpdateEditorShortcutToolTips()`: tooltip dinamis untuk `BtnApplyErase`,
  `BtnApplyRestore` (+ varian `Panel`) menampilkan shortcut aktif.
- `UpdateToolHint()`: hint tool seleksi (Lasso/Wand/Marquee) mencantumkan shortcut
  masking; hint Pen ditulis ulang mendokumentasikan interaksi baru.
- `_pendingSelMode` kini hanya di-latch untuk tool yang commit saat pointer dilepas
  (lasso/marquee). **Bug fix**: sebelumnya Alt+klik pada pen me-latch mode Subtract,
  lalu Enter memakai mode basi itu → commit dari seleksi kosong.
- State drag/hover pen + reset saat ganti tool (field `_penDrag*`, `_penHover*`,
  `_lastPenClickMs`; reset di `SetActiveTool`).

### `apps/pixacompact/Views/PreviewWindow.EditorPen.cs` (baru)
- Hasil ekstraksi dari `PreviewWindow.Editor.cs`: `PenHoverKind`, `DrawPenOverlay`,
  `IsHotHandle`, `DrawHandleKnob`, `UpdatePenHover`. Partial class yang sama —
  tidak ada perubahan perilaku, hanya agar tiap file tetap di bawah batas ukuran
  argumen push (verifikasi: build Release 0 warning/error setelah ekstraksi).

### `apps/pixacompact/Services/Editing/SelectionTool.cs` (`PenTool`)
- `HitTestHandle(p, radius)` → `(hit, anchorIndex, isIn)`: hit-test knob handle absolut.
- `SetHandle(index, isIn, relative, mirror)`: atur satu handle; `mirror=true` menjaga
  simetri (smooth), `false` (tahan Alt) mematahkan — ala Photoshop.
- `ConvertToCorner(index)`: hapus kedua handle (setara Alt+klik Convert Point Tool).

### `apps/pixacompact/Views/PreviewWindow.axaml`
- Dua baris pengaturan shortcut baru setelah "Quick mask": `TxtEditMaskDeleteShortcut`
  ("Delete masking"), `TxtEditMaskRestoreShortcut` ("Restore masking").
- Teks catatan diperbarui: Backspace/Delete memangkas titik saat menggambar, selebihnya
  menjalankan shortcut masking di atas.

### `apps/pixacompact/Views/PreviewWindow.axaml.cs`
- `OnShortcutTextBoxKeyDown`: memakai `IsReservedKeyForAction` per definisi field,
  sehingga field masking bisa diisi Delete/Backspace; field lain tetap menolak.
  (`OnSettingsFlyoutOpened`/`OnSaveSettingsClick`/`OnRestoreShortcutDefaultsClick`
  sudah mengiterasi `EditorShortcutMap.Definitions` — otomatis ikut.)

## Keputusan desain shortcut
- Default Photoshop: Delete/Backspace → delete masking, Shift+Delete/Shift+Backspace → restore.
- Backspace & Delete diperlakukan sebagai pasangan: bila tombol yang ditekan tidak
  ter-bind, dipakai binding pasangannya (keduanya tidak bisa di-bind ke aksi lain,
  jadi tidak ada ambiguitas).
- Toggle brush erase/restore (`X`) tidak diubah; aksi masking baru terpisah
  (apply selection → mask), sesuai tombol Erase/Restore yang sudah ada.

## Yang sengaja tidak diubah
- Arsitektur editor (session, overlay, rasterizer) tidak disentuh.
- `ApplySelectionToMask` dan guard restore-source/toast tidak diubah (dipakai ulang).
- Tidak ada penambahan shortcut di luar pola `EditorShortcutMap` (aksi masking baru
  ikut katalog + panel pengaturan + validasi konflik).
- "Add anchor on path segment" belum diimplementasikan (butuh hit-test segmen Bézier
  + split kurva; risiko distorsi bentuk — ditunda).

## Verifikasi
- `dotnet build apps/pixacompact/PixelcutCompact.csproj -c Release`: **sukses, 0 warning, 0 error**.
- `dotnet test` (vstest): **tidak bisa jalan di VM ini** — testhost gagal konek (2x, timeout
  90 dtk & 300 dtk). Sebagai ganti, harness konsol (`~/workspace/logiccheck`, me-referensi
  PixelcutCompact.dll) menjalankan **42 assert → ALL PASS**, mencakup: default & resolve
  shortcut masking, reserved-key per aksi, deteksi konflik, round-trip parse/format,
  remap custom, `HitTestHandle`/`SetHandle` (mirror & break)/`ConvertToCorner`/`MoveAnchor`
  PenTool + kasus index invalid.
- Uji GUI langsung: aplikasi dijalankan di bawah `Xvfb :99` via harness
  (`~/workspace/verifyapp`, membuka PreviewWindow + gambar uji), input sintetis XTest
  (keyboard/mouse), screenshot via XGetImage. Hasil:
  - `01-preview-loaded.png`: jendela preview + gambar uji tampil normal.
  - `02-edit-mode.png`: tombol `E` membuka mode edit (tool rail, panel Properties).
  - `03-pen-path.png`: path pen 4 anchor + handle Bézier + rubber band tergambar.
  - `04-pen-handle-hover.png`: hover knob handle → highlight kuning.
  - `05-pen-handle-dragged.png`: drag knob handle → kurva berubah bentuk.
  - `06-pen-alt-convert.png`: Alt+klik anchor → diamond (smooth) jadi square (corner),
    direction line & knob hilang.
  - `10-kpenter-commit.png`: Enter → path jadi selection ("Ada selection · 0.03 Mpx",
    marching ants).
  - `13-fixed-enter-commit.png`: setelah fix `_pendingSelMode`, Alt+klik lalu Enter →
    selection 0.09 Mpx mode Replace (sebelumnya jadi 0 Mpx karena Subtract basi).
  - `14-fixed-delete-masking.png`: Delete → erase masking teraplikasi (area terhapus,
    "Undo 1 / Redo 0").
  - `15-ctrl-s-saved.png`: Ctrl+S menyimpan; **tidak ada file `.bak`** dibuat.
  - `20-del-while-drawing.png`: Delete saat menggambar → titik terakhir terhapus (legacy).
  - `22-shift-del-toast.png`: Shift+Delete tanpa restore source → toast
    "Piksel gambar asli belum siap; Pulihkan belum tersedia."
- Panel pengaturan shortcut (flyout gear) tidak berhasil dibuka via klik sintetis
  (keterbatasan otomasi XTest, bukan bug app); baris baru terverifikasi statis
  (x:Name cocok dengan ControlName) + code review alur load/save/reset.

## Status push ke GitHub
- Syarat verifikasi user terpenuhi (build sukses + aplikasi jalan + perilaku
  terverifikasi), dan user meminta push langsung ke `main`.
- **PUSH BERHASIL** ke `main` via `github push_files` dalam 3 commit
  (satu file >128KB harus dipecah karena batas argumen CLI):
  - `f163635` (1/3): summary + EditorShortcutMap + SelectionTool + EditorPen + axaml.cs
  - `680faf0` (2/3): PreviewWindow.axaml
  - `5076adf` (3/3): PreviewWindow.Editor.cs
- Verifikasi pasca-push: blob SHA ketujuh file di `origin/main` sama persis
  dengan hasil build lokal yang teruji.
- Screenshot bukti (`verify_shots/`, 12 file) sengaja tidak di-push; tersimpan
  lokal di `~/workspace/mono-work/verify_shots/`.

## Catatan
- Bug `_pendingSelMode` basi ditemukan murni dari pengujian langsung ("sambil mencoba"):
  tanpa fix, skenario Alt+klik → Enter diam-diam me-subtract dari seleksi kosong.
- Screenshot bukti ada di `verify_shots/` (12 file, ~836 KB).

---

# Tahap 1 — Pen tanpa auto-selection, Make Selection, workflow mask, & rebuild layout

Branch: `fix/pen-selection-revert` (dari `main` @ `35dc3f5`)
Tanggal: 2026-10-02
Status: build sukses (0 warning/error), harness logika ALL PASS, terverifikasi
langsung via Xvfb + screenshot. **Belum di-push** (menunggu verifikasi user).

## 1. Pen tool: hapus auto-selection, Ctrl+Enter = Make Selection

- Double-click / Enter kini HANYA menutup path pen — tidak lagi auto-commit menjadi
  selection (perilaku lama yang dikeluhkan user).
- Path tertutup dipertahankan di overlay sampai: trigger eksplisit, ganti tool,
  path baru dimulai (klik baru me-reset), atau Esc.
- **Ctrl+Enter = "Make Selection"** ala Photoshop: mengubah path pen tertutup
  (atau path committable tool lain) menjadi selection.
- Tidak ada auto-commit lain: klik anchor pertama dsb. tidak membuat selection.

## 2. "Make Selection" bisa di-customize

- Aksi baru `EditorShortcutAction.MakeSelection` ("Make selection",
  `TxtEditMakeSelectionShortcut`, default `Ctrl+Enter`).
- `EditorShortcutMap` diperluas dukung modifier **Ctrl** konsisten: `TryParse`
  ("Ctrl+X", "Ctrl+Shift+X"), `TryGetAction`, `TryValidate` (konflik vs chord
  hardcoded Ctrl+Z/Y/S/J/A/D/I, Ctrl+Shift+Z/J/I), `IsReservedKeyForAction`
  per-aksi (Enter hanya untuk MakeSelection), `IsSupportedModifiers`.
- `OnShortcutTextBoxKeyDown` menerima Ctrl → Ctrl+Enter bisa direkam di panel
  pengaturan; baris pengaturan ditambahkan di axaml.
- **Bug platform ditemukan saat uji**: di X11, tombol fisik `\` dilaporkan Avalonia
  sebagai `Key.OemPipe` (bukan `Key.OemBackslash` seperti di Win32). Diperbaiki
  dengan `KeysEquivalent` (OemBackslash ≡ OemPipe) di `TryGetAction` +
  `FormatKey` menampilkan keduanya sebagai `\`. Shortcut `\` kini bekerja di
  kedua platform.

## 3. Arsitektur masking: Original sebagai basis WAJIB

- Prinsip user: "mode edit harus SELALU masking dari gambar original".
- `PrepareEditorAsync` kini memuat Original **sinkron** (task lazy dihapus —
  tanpa race); bila file sama dengan result, buffer dibagi sebagai restore source.
- Bila original hilang/gagal/beda ukuran → `_restoreUnavailableReason` diisi dan
  restore **dinonaktifkan eksplisit** (toast + tooltip + tombol menyebut alasan,
  tidak diam-diam).
- `CompositeInto` tidak diubah: RGB matting (decontaminated) dipertahankan untuk
  area cutout; RGB original dipakai hanya di area yang di-restore
  (`maskAlpha > sourceAlpha`).
- **Fill hitam/putih**: `ApplySelectionToMask(..., fillAll)` — tanpa seleksi =
  seluruh mask (ala Photoshop). Delete = fill hitam (sembunyikan), Shift+Delete =
  fill putih (restore/kembalikan piksel asli).
- **Mask view hitam-putih**: toggle `\` (remappable) + tombol rail + menu View;
  saling lepas dengan Quick Mask (Q = overlay merah seleksi; `\` = mask itu sendiri).
- Tombol **Bandingkan** tetap peek original (tooltip diperjelas: bukan mask).
- **"Buang perubahan"**: tombol destructive (merah) di dialog `ConfirmDiscardChanges`
  + F12 (= Revert ala Photoshop, dengan konfirmasi bila kotor). `DiscardEditsAsync`
  me-recreate sesi dari disk; undo stack, overlay, quick mask, status bar ter-reset.

## 4. Menu Bar + rebuild presentation layer (mandat desain baru)

User memberi lisensi penuh untuk merombak presentation layer ala
Photoshop/Affinity/Compositor (referensi: `~/workspace/compositor-ref`).
Mesin (session/mask/composite/selection) TIDAK disentuh.

### Hierarki tegas (tetap, tidak bisa di-dock/pindah)
```
Menu bar (File/Edit/Select/View)
→ Options bar kontekstual per tool (judul tool + kontrol, tinggi tetap 42)
→ Rail tools KIRI vertikal 2 kolom (HANYA tools edit) | kanvas | panel kanan
→ Status bar bawah (info dokumen + hint kontekstual, tinggi tetap 30)
```

### Menu Bar
- **File**: Save (Ctrl+S), Save As…, —, Revert (F12), Buang perubahan…, —,
  Keluar Mode Edit.
- **Edit**: Undo (Ctrl+Z), Redo (Ctrl+Shift+Z), —, Fill Black (Delete),
  Fill White (Shift+Delete), —, Make Selection (Ctrl+Enter, ikut remap).
- **Select**: Select All (Ctrl+A), Deselect (Ctrl+D), Inverse (Ctrl+Shift+I), —,
  Grow (Ctrl+J), Shrink (Ctrl+Shift+J).
- **View**: Bandingkan (Original) ☑, Quick Mask (Q) ☑, Mask View (B/W) ☑.
- Setiap item menampilkan shortcut-nya (dari `EditorShortcutMap` / hardcoded) dan
  ter-disable tepat mengikuti state (Revert/Buang disable bila bersih; Fill White
  disable bila tak ada restore source; Deselect/Inverse/Grow/Shrink disable bila
  tak ada seleksi; Make Selection disable bila tak ada path committable).
- Tombol aksi dokumen di rail (Undo/Redo/Save/SaveAs/Revert/Exit) dan tombol
  seleksi (All/None/Invert) **dihapus dari rail** — satu aksi = satu tempat (menu).
  Quick Mask & Mask View tetap di rail (view-mode toggle, disengaja).

### Rail tools kiri (vertikal, 2 kolom ala Photoshop)
- Kolom seleksi: Move, RectMarquee, EllipseMarquee, Lasso, PolyLasso, Wand, Pen.
- Kolom paint/view: Brush, Eraser, RefineEdge, Pan — separator — QuickMask,
  MaskView, RefineHair. Semua 14 muat tanpa scroll di tinggi normal.
- Tombol 36×36, active state jelas, tooltip selalu menyebut shortcut.

### Design tokens (satu definisi)
- `Window.Resources`: `EditorBg/Surface/SurfaceAlt/Border/TextPrimary/TextSecondary/`
  `TextDisabled/Accent/Danger/Warning/Success` + metrik (`EditorRailWidth`,
  `EditorToolSize`, `EditorOptionsBarHeight` 42, `EditorStatusBarHeight` 30,
  `EditorMenuBarHeight` 34).
- Style: `editor-menubar/menuitem` (gelap + disabled jelas), `editor-danger`
  (hover/pressed/disabled/focus), `editor-section-title`, `editor-hint`.
- Status bar gaya Compositor: kiri info dokumen (tool, zoom, dimensi, seleksi,
  undo), kanan hint kontekstual tool aktif.

### Pecah file (batas 128KB/arg push)
- `PreviewWindow.Editor.cs` 139KB → `EditorMenu.cs` (menu bar), `EditorDialogs.cs`
  (save/revert/discard), `EditorOptions.cs` (options bar/tooltip/hint),
  `EditorWorkspace.cs` disederhanakan (docking dihapus → layout tetap).
- Semua file kini < 103KB.

### Bug ditemukan saat pengujian langsung
1. **OemPipe vs OemBackslash** (di atas) — shortcut `\` tidak jalan di X11.
2. **PanelOptionsBar tidak disembunyikan** saat keluar mode edit (bug lama) —
   teks "Pan" tersisa di atas kanvas. Diperbaiki di `SetEditorWorkspaceActive(false)`.
3. **Ekstraksi rail merusak XAML** (self-closing `<Border/>` merusak hitung
   kurung) — diperbaiki dengan rekonstruksi dari git HEAD + validasi balance tag.

## Verifikasi

- `dotnet build -c Release`: **0 warning, 0 error**.
- Harness `~/workspace/logiccheck`: **ALL PASS** (~50 assert) — parse/resolve/
  validasi Ctrl+Enter & `\`, remap custom, konflik Ctrl+S dkk., ekuivalensi
  OemBackslash/OemPipe, fill hitam/putih tanpa seleksi, restore mengembalikan
  RGB+alpha ORIGINAL di area ter-delete, RGB matting dipertahankan di area cutout.
- GUI langsung (Xvfb, `~/workspace/verifyapp`, `verify_shots/` lanjut 23→45):

| # | File | Bukti |
|---|------|-------|
| 23–24 | `23-app-start.png`, `24-edit-mode-menubar.png` | Layout baru: menu bar + rail vertikal + status bar |
| 25 | `25-rail-2col.png` | Rail 2 kolom: 14 tools, tanpa scroll |
| 26–29 | `26-menu-file.png`, `27-menu-edit.png`, `28-menu-select.png`, `29-menu-view.png` | Tiap menu terbuka; shortcut tampil; disable state tepat (Revert/Buang disable saat bersih, Deselect/Inverse/Grow/Shrink disable tanpa seleksi, Make Selection disable tanpa path) |
| 30 | `30-pen-closed-no-selection.png` | Path pen tertutup (double-click) TANPA menjadi selection; path retained di overlay |
| 31 | `31-ctrl-enter-selection.png` | Ctrl+Enter → marching ants; status "Selection: 0.02 Mpx" |
| 32/38/39 | `32-maskview-bw.png`, `38-maskview-via-menu.png`, `39-maskview-backslash-key.png` | Mask view B/W via menu & keyboard `\` (setelah fix OemPipe); toast penjelasan |
| 33–34 | `33-fill-black-selection.png`, `34-maskview-after-delete.png` | Delete = fill hitam pada seleksi (area jadi hitam); mask view tunjukkan area hitam |
| 35 | `35-fill-white-restore.png` | Shift+Delete = fill putih → piksel asli kembali (bukti "kembalikan gambar"); history "Restore Selection" |
| 40–43 | `40-before-revert.png`, `42-revert-dialog-f12.png`, `43-after-revert.png` | F12 → dialog "Buang semua perubahan?" (tombol merah destruktif) → gambar kembali ke asli, history reset |
| 44–45 | `44-hover-pen-button.png`, `45-exit-edit-mode.png` | Hover: highlight + tooltip berisi shortcut; Esc keluar bersih |

- Catatan: CPU ~100% saat idle di mode edit adalah **pre-existing** (terjadi juga di
  `main` tanpa perubahan ini) — bukan regresi; di luar cakupan Tahap 1.
- Remap Make Selection ke tombol lain terverifikasi di level harness
  (parse/validate/resolve); alur klik-perekam di panel pengaturan tidak diubah.

## Status
- Commit lokal di branch `fix/pen-selection-revert` (menyusul). **JANGAN push** —
  menunggu verifikasi user.
- Screenshot (`verify_shots/`, 23 file baru) tidak untuk di-push.

---

## TAHAP 2: Audit & Polish UI/UX Mendalam (2026-10-02)

Mandat user: "rombak ulang saja bila perlu, yang penting memanjakan user" —
target visual sekelas Photoshop/Affinity/Compositor.

### Temuan Audit (Bug Hunt)

| # | Temuan | Status |
|---|--------|--------|
| 1 | **Tombol Layers (+, −, Dup, Merge) + checkbox visibility MATI TOTAL**: tidak merespons klik mouse maupun keyboard (Space). Pointer events (tunnel) tidak sampai ke tombol meski `IsEnabled/IsVisible/IsHitTestVisible=True` dan handler ter-wire. Terjadi hanya di panel `PanelDocLayers`. | **DIPERBAIKI** (lihat keputusan desain) |
| 2 | **Hover state terlalu halus**: `#202630` → `#2B3440` nyaris tak terlihat; user mengeluh. | **DIPERBAIKI** |
| 3 | **Panel pengaturan shortcut tidak menampilkan shortcut hardcoded** (Ctrl+Z/Y/S/A/D, Ctrl+Shift+I/J, F12, Escape) — hanya 19 aksi remappable. | **DIPERBAIKI** |
| 4 | Tombol SELECTION/MASK panel kanan: **semua BEKERJA** (All/None/Invert, Grow/Shrink/Feather, Save/Load Sel, Erase/Restore/Fill, Expand/Contract/Feather, Defringe) — terverifikasi via history entries. | OK |
| 5 | Menu File/Edit/Select/View: **semua bekerja** dengan disabled-state tepat. | OK |
| 6 | Brush cursor: lingkaran sesuai ukuran + cincin hardness (dashed) — sudah ala Photoshop. | OK |
| 7 | Options bar kontekstual: Brush (Ukuran/Softness/Lanjutkan/Hapus), RefineEdge (Size/Feather/New/Add/Sub/Intersect) — rapi, pola Compositor (42px, semibold title). | OK |
| 8 | Status bar: tool aktif, zoom, ukuran, info seleksi, undo/redo, hint bar akurat per tool. | OK |
| 9 | Zoom via mouse wheel: ada (`HandleEditorWheelZoom`). | OK |

### Perbaikan

1. **Panel Layers disembunyikan dari mode masking** (`PreviewWindow.Editor.cs`):
   - Menghapus pemanggilan `EnterDocEditMode()` — panel V2 tidak relevan untuk workflow masking dan tombol-tombolnya mati (temuan #1).
   - `_editorV2Active` tetap false → `PanelDocLayers` hidden via `ApplyEditorDockVisibility()`.
   - Kode V2 (`EditorV2.cs`, `LayerPanel.cs`) tetap ada untuk penggunaan masa depan.
2. **Hover/pressed/disabled diperjelas** (`PreviewWindow.axaml`):
   - Token baru: `EditorBtnBg`, `EditorBtnBgHover` (#3A455C), `EditorBtnBgPressed`, `EditorBtnBorder`, `EditorBtnBorderHover` (#8BB8FF), `EditorBtnBgDisabled`, `EditorBtnBorderDisabled`.
   - `editor-compact` dan `editor-tool` kini pakai token; hover jauh lebih terasa (bg lebih terang + border biru aksen).
3. **Panel shortcut: bagian "Fixed shortcuts"** — daftar read-only: Undo (Ctrl+Z), Redo (Ctrl+Y), Save (Ctrl+S), Select all (Ctrl+A), Deselect (Ctrl+D), Invert (Ctrl+Shift+I), Grow (Ctrl+J), Shrink (Ctrl+Shift+J), Revert (F12), Cancel (Escape).

### Keputusan Desain

- **Layers vs Masking**: Ini editor masking pixelcut, bukan editor layer. Panel Layers (V2) adalah fitur eksperimental yang membingungkan di konteks masking + tombolnya mati. Disembunyikan, bukan dihapus — kode tetap ada.
- **Bahasa visual**: Mengikuti pola Compositor — options bar 42px fixed dengan judul semibold + kontrol kontekstual; panel non-modal tidak men-dimming editor; tooltip di setiap kontrol.
- **Hover harus TERASA**: Bukan sekadar grey-out samar. Background naik 2 tingkat + border aksen biru.

### Verifikasi (Xvfb + Screenshot)

| Screenshot | Isi |
|------------|-----|
| `120-no-layers-panel.png` | Panel Layers hilang; panel kanan hanya SELECTION/MASK/HISTORY |
| `121-hover-erase.png` | Hover tombol Erase: bg terang + border biru jelas |
| `123-menu-file.png`, `125-menu-select.png`, `126-menu-view.png` | Menu bekerja, disabled-state tepat |
| `127-opt-brush.png` | Options bar Brush rapi |
| `129-brush-cursor.png` | Brush cursor: lingkaran luar solid + cincin hardness dashed |
| `135-refine-opt.png` | Options bar RefineEdge: Size/Feather/New/Add/Sub/Intersect |

Build: Release 0 warning/error. Semua kode debug sementara sudah dihapus.

### Status
- Commit lokal di branch `fix/pen-selection-revert`. **JANGAN push** — menunggu verifikasi user.
- Screenshot Tahap 2 (`verify_shots/`, file 120–137) tidak untuk di-push.

---

# Tahap 3 — Rombak visual total ala Photoshop/Affinity (murni tampilan, tanpa ubah fungsi)

Mandat user: "rombak tampilan saja deh agar benar-benar seperti photoshop/affinity" + aturan keras kerapian: tidak boleh ada gap/spasi berlebihan di grup tombol/kontrol (gap dalam grup 2-4px, antar grup 8-12px, tidak ada gap >12px yang tidak disengaja; semua baris tombol full-width sejajar).

## 3A. Design tokens — satu palet dark netral (`a6cbccc`)
- `EditorBg` #1E1E1E (canvas surround, sebelumnya #242424 / #28283c tint biru-ungu), `EditorSurface` #2B2B2B, `EditorSurfaceAlt` #2F2F2F, `EditorBorder` #3A3A3A, teks #E8E8E8/#A6A6A6/#6E6E6E.
- Aksen tunggal biru Photoshop **#31A8FF** + turunan (hover #4DB8FF, pressed #1E8FDB, dark #1F3A52); semua #6366F1/#78A9F6/#263B55/#60A5FA/#1F6FEB diganti token.
- Corner radius konsisten 3px (editor-tool, editor-compact, editor-primary).

## 3B. Rail kiri — satu kolom 52px (`8fb373e`)
- Dari 2 kolom 84px → 1 kolom 52px, tombol 36x36 ikon 16px, Spacing 4.
- Grup: [Move, RectMarquee, EllipseMarquee, Lasso, PolyLasso, Wand, Pen] | [Brush, Eraser, RefineEdge] | [Pan] | [QuickMask, MaskView, RefineHair], separator 1px `EditorBorder`.
- Tombol Lasso dari TextBlock "L" → PathIcon.

## 3C. Options bar — ramping 32px (`c4bd907`)
- Height 42→32, Padding 18,0→12,0, judul 13→12px, Spacing grup 12→8, separator `#30FFFFFF`→token `EditorBorder`, slider 80→64 / 60→52. Isi kontrol per tool tidak diubah.

## 3D. Dock kanan — header netral konsisten (`7f88ccd`)
- Header SELECTION/MASK/HISTORY: 10px Bold `EditorTextSecondary`, tanpa ikon berwarna (sebelumnya: section tanpa header vs MASK oranye vs HISTORY biru); bg section = `EditorSurface`; Spacing 10→8; tombol Erase/Restore/Fill netral.
- Bug diperbaiki: `LstHistory` `AutoScrollToSelectedItem="False"` — auto-scroll ListBox menggeser dock ~50px sehingga header SELECTION/PROPERTIES tak terlihat.

## 3E. Menu & status bar (`2bb18f9`)
- MenuItem foreground #9fa0a1→#D6D6D6, dropdown item Padding 8,4, InputGesture rata kanan; status bar Padding 18,0→12,0. Teks konten tidak diubah.

## 3F. Ikon tool rail digambar ulang ala Photoshop (`d29e5dc`)
- 14 ikon ditulis ulang dari nol sebagai vector path original (viewBox 24x24, fill solid putih, even-odd; TIDAK menjiplak artwork Adobe): Move=panah 4-arah, Pan=tangan (keduanya sebelumnya tertukar intuisi — diperbaiki), Rect/EllipseMarquee=garis putus-putus, Lasso=loop tali, PolyLasso=polyline+anchor, MagicWand=tongkat+percikan, Brush=kuas, Eraser=penghapus, Pen=nib, RefineEdge=kuas+tepi gerigi, QuickMask/MaskView=ring setengah arsir/isi, RefineHair=kepala+bahu. Jelas di state normal/hover/selected.

## Verifikasi Tahap 3 (Xvfb 1600x900 + VerifyApp)
- Build Release **0 warning/error** tiap area; `PreviewWindow.axaml` 113KB (< 120KB).
- Regresi fungsi: 22 item PASS — 11 tool rail, QuickMask/MaskView toggle, panel All/None/Invert/Grow/Shrink/Feather/Save/Load/Erase/Restore/Fill/Expand/Contract/Defringe, menu File/Edit/Select/View, Ctrl+Z / Ctrl+Shift+Z / Ctrl+Enter (pen segitiga→selection) / Delete / Shift+Delete / Q / `\`, Esc keluar edit, settings gear. Tidak ada crash/tombol mati. (Save/Load Sel: alur file picker OS tidak bisa diuji di Xvfb headless — perlu uji manual di desktop.)
- Audit kerapatan: BERSIH — gap tombol rail 4px, antar grup options bar 6-10px + separator, dock 4px/8px; tidak ada gap >12px dalam grup kontrol.
- Screenshot bukti (`verify_shots/`, tidak di-push): audit `200-227`, implementasi `300-350`, regresi `400-476`/`501-559`/`907`, ikon `520-523` (rail normal/hover/selected + close-up kerapatan `4xx-dock-dense`, `4xx-optbar-dense`, `4xx-rail-dense`).

## Yang sengaja tidak dikerjakan
- **Background dropdown menu (#43444a) + border putih**: di-hardcode sebagai local value di ControlTemplate MenuItem milik Fluent theme; Style setter kalah precedence. Satu-satunya fix = ganti seluruh ControlTemplate (±200-300 baris XAML) — ditolak karena risiko regresi perilaku. Opsi: terima apa adanya, atau buatkan custom template di ResourceDictionary terpisah (perlu persetujuan user).
- Teks label campur ID/EN, isi options per tool (mis. Move), dialog RefineHair, chrome MODE EDITOR ganda: di luar scope visual murni / perilaku.

## Status
- 6 commit lokal di branch `fix/pen-selection-revert` (3A→3F). **JANGAN push** — menunggu perintah user.

---

# Tahap 4 — Shortcut Photoshop di mode edit (2026-10-02)

Delapan shortcut ala Photoshop ditambahkan; perilaku lama tidak diubah. Commit lokal di branch `fix/pen-selection-revert` (6 commit: `9b30115` 4A, `89cec4b` 4B, `02259f5` 4C, `674c1d3` 4D, `7aabe32` 4E, `0659129` 4F).

## Daftar shortcut baru (semua terverifikasi berfungsi)
| Shortcut | Fungsi |
|---|---|
| Alt + klik kanan + geser | Brush HUD: geser horizontal = ukuran brush, vertikal = hardness (atas = lebih keras). Lingkaran cursor live + label "128 px · 14%" |
| Shift+[ / Shift+] | Brush hardness down/up (±0.05) |
| 1..9, 0 | Brush opacity 10%..100% (saat brush/eraser aktif) |
| X | Toggle erase/restore brush (sudah ada sejak awal — diverifikasi: tombol "Hapus"→"Pulihkan") |
| Arrow keys / Shift+Arrows | Nudge selection 1px / 10px (undoable, hanya saat ada selection) |
| Tab | Sembunyikan/tampilkan rail + options bar + dock kanan |
| Ctrl++ / Ctrl+- / Ctrl+0 / Ctrl+1 | Zoom in / out / fit to screen / 100% |

## Implementasi
- `Services/Editing/BrushHudMath.cs` (baru): matematika HUD `(size, hardness) = f(dx, dy)` — testable, 6 assert logiccheck.
- `PreviewWindow.Editor.cs`: state `_brushHudActive` + hook di `EditorPointerPressed/Moved/Released` (klik kanan biasa tetap tidak ngapa-ngapain — tanpa konflik); `AdjustBrushHardness`, `TryOpacityDigit`, `NudgeSelection` (pakai `ShiftBuffer` + `BeginEdit`), `ToggleEditorPanels`, `EditorZoomStep/Fit` (pakai `_viewPort` yang sudah ada).
- `PreviewWindow.EditorWorkspace.cs`: flag `_tabPanelsHidden`, dihormati `ApplyEditorDockVisibility()`; direset saat masuk mode edit.
- `PreviewWindow.axaml.cs`: `OnPreviewKeyDownTab` via **tunnel** `KeyDownEvent` — Tab mentah ditangkap sebelum focus-navigation Avalonia mengonsumsinya (temuan saat verifikasi: handler biasa tidak dipanggil).
- `EditorShortcutMap.cs`: `HardcodedChords` +~40 chord baru (digits, arrows, Tab, zoom, Shift+[/]) sehingga tidak bisa di-remap; `TryParse` kini mengenal "0".."9" → `Key.D0..D9` (sebelumnya "5" ter-parse jadi `Key.Clear` yang menyesatkan).
- Panel pengaturan: 6 baris baru di "Fixed shortcuts"; hint Brush/Eraser/Move diperbarui.

## Verifikasi
- `dotnet build -c Release`: **0 warning, 0 error**.
- `logiccheck`: **ALL PASS** (+24 assert baru: 6 HUD math, 12 hardcoded chord, 3 penolakan remap, 3 regresi).
- GUI Xvfb (VerifyApp): `150-brush-hud.png` (label "128 px · 14%", cursor membesar), `161-tab-hidden.png` (panel hilang), `173-shortcut-panel.png` (marching-ants + Zoom 125 + Softness 25% + "Pulihkan" + hint baru), `175-shortcut-fixed-list.png` (6 baris baru di panel), opacity 0.5 & hardness 0.75 terkonfirmasi via settings JSON.
- Pelajaran: proses Xvfb/VerifyApp basi dari sesi lama bisa membuat tes menempel ke window app lama — sekarang skrip verifikasi selalu kill proses basi + assert Xvfb baru jalan.

## Catatan
- Konvensi persen hardness mengikuti label options bar yang sudah ada (`(1.0 - hardness) * 100`); label "Softness" yang terbalik vs Photoshop adalah quirk pre-existing, di luar scope.
- **JANGAN push** — menunggu perintah user.

---

# Tahap 5 — Ketebalan & warna garis path Pen tool (2026-10-02)

Ketebalan garis (stroke width) + warna garis path untuk Pen tool — fungsional penuh, bukan sekadar visual: tersimpan di settings (persist JSON), render ulang overlay secara live, path yang sudah digambar ikut berubah. Commit lokal di branch `fix/pen-selection-revert` (3 commit: `67fb898` 5A, `060cd50` 5B, `55bbb1e` 5C).

## Fitur
| Fitur | Detail |
|---|---|
| Slider "Tebal:" | 1–8 px (default 2), snap tick, label live ("4 px"); perubahan langsung me-render ulang path |
| 8 swatch preset ala Photoshop | putih, hitam, merah, hijau, biru, kuning, magenta, cyan; ring biru `#31A8FF` menandai yang aktif |
| Tombol "Custom" | Dialog manual: 16 warna tambahan + input hex (`#RRGGBB`/`#RGB`) + preview live + Pilih/Batal. (Avalonia `ColorPicker` bawaan dicoba dulu — me-render kotak gelap kosong di Linux/X11, jadi diganti dialog manual yang terbukti tampil) |
| Kontras otomatis | Garis path digambar di atas **underlay hitam** (tebal+2 px, opacity 0.7) → terbaca di canvas gelap maupun terang; anchor mengikuti warna path (outline hitam dipertahankan); handle/knob tetap biru + hover tetap kuning `#FFE24A` (keputusan keterbacaan) |
| Persist | `EditorPenPathThickness` (double), `EditorPenPathColor` (string `#RRGGBB`) di `PreviewWindowSettings` → `preview_settings.json` |

## Implementasi
- `Services/Editing/PenPathStyle.cs` (baru): `PenPathColors` (8 preset + nama), `ClampThickness(1–8)`, `NormalizeColor` (→ `#RRGGBB`, fallback default bila invalid), `ParseColor` (tidak pernah throw) — 13 assert logiccheck.
- `Models/EditorSettings.cs`: `DefaultPenPathThickness=2.0`, `Min/MaxPenPathThickness=1/8`, `DefaultPenPathColor="#FFFFFF"`.
- `Services/PreviewWindowSettings.cs`: properti `EditorPenPathThickness`/`EditorPenPathColor` + load/save JSON.
- `PreviewWindow.axaml`: section `OptPenGroup` (+13 baris; file tetap < 120KB) — hanya tampil saat Pen aktif.
- `PreviewWindow.EditorOptions.cs`: `BuildPenSwatches()` (dibangun di code; `ToolTip.SetTip` — `Button` buatan code tidak punya properti `.ToolTip` di Avalonia), `OnPenPathThicknessChanged`/`SetPenPathColor` (save + `RenderOverlay()` live), dialog custom color, hint bar Pen menyebut "tebal & warna garis: options bar".
- `PreviewWindow.Editor.cs`: `DrawPathOutline(..., penStyle:true)` — underlay hitam + garis warna user; `DrawLine` dapat param `width`; tool non-Pen tidak berubah (tetap `#FFE24A`).
- `PreviewWindow.EditorPen.cs`: fill anchor = warna path; hover kuning & handle biru dipertahankan.

## Verifikasi
- `dotnet build -c Release`: **0 warning, 0 error**.
- `logiccheck`: **ALL PASS** (+13 assert: default, clamp 1/8, normalisasi valid/invalid/null/kosong, 8 preset ter-parse).
- GUI Xvfb (VerifyApp), semua terkonfirmasi visual:
  - `720-pen-optbar-red6.png` — options bar Pen: slider di "4 px" + swatch merah terpilih (ring biru).
  - `721-pen-path-closed-red6.png` — segitiga path tertutup **merah tebal** + underlay hitam + anchor merah.
  - `740-pen-custom-picked2.png` / `761-pen-path-purple3.png` — dialog custom: klik swatch ungu → hex `#BF5AF2` + preview ungu → "Pilih" → dialog tutup, **path jadi ungu**.
  - `770-dlg-aligned.png` — dialog custom tampil penuh (16 swatch, hex input, preview, Batal/Pilih).
  - Regresi: gambar path → ganti warna/tebal → Enter tutup path → Ctrl+Enter → selection tetap terbentuk ("Ada selection · 0.03 Mpx").
- Pelajaran verifikasi: (1) koordinat klik sintetis harus diukur dari screenshot via analisis piksel — estimasi manual meleset (tombol "Pilih" ternyata di ~(665,302), bukan perkiraan awal); (2) Avalonia `ColorPicker` tidak me-render di X11 — dialog manual lebih andal; (3) `StackPanel` vertikal di Avalonia tidak me-stretch child ke lebar penuh (tombol dialog tetap rata kiri walau `HorizontalAlignment=Right`/Grid `*` — diterima sebagai layout final yang tetap rapi).

## Status
- 3 commit lokal Tahap 5 di branch `fix/pen-selection-revert`. **JANGAN push** — menunggu perintah user (push ke `main` setelah semua tahap + tahap bahasa Indonesia selesai).

## Tahap 6 — Rombak visual sisa mode edit (settings, Bandingkan, Result, status bar, dialog, slider)

Tujuan: samakan semua chrome mode edit yang belum tersentuh tahap 3 ke design tokens
(surround #1E1E1E, panel #2B2B2B, border #3A3A3A, teks #E8E8E8/#A6A6A6, aksen #31A8FF,
radius 3px, font 11px). Murni visual — nol perubahan perilaku.

### 1. Tombol "Bandingkan" pindah ke options bar
- Panel floating `PanelViewControls` (compare + zoom, top-left kanvas) diberi x:Name dan
  disembunyikan saat mode edit; toggle `BtnCompareOptions` ("Bandingkan") ditambahkan di
  options bar (Grid kolom 2, kanan, selalu terlihat, tidak tergantung tool).
- Keduanya terhubung ke state `_compareOriginal` yang sama; `UpdateCompareButtonState`
  sinkron ke `BtnCompare` + `BtnCompareOptions` + `MiCompare` (menu View).
- Zoom saat edit tetap via status bar + Ctrl++/-/0/1.

### 2. Label "Result" disembunyikan saat edit
- `TxtResultLabel` (Grid.Row=1, di bawah kanvas) diberi x:Name; `IsVisible=False` saat
  mode edit (Row Auto collapse sendiri), dikembalikan saat preview.

### 3. Panel Pengaturan (flyout gear) dirapikan
- Background `EditorSurface`, border `EditorBorder`, radius 3, padding 12; label →
  `EditorTextSecondary`, separator → `EditorBorder`, judul 12px primary.
- Daftar shortcut (fixed + remappable) sejajar rapi; tidak ada teks terpotong.

### 4. Status bar
- Separator vertikal antara nama tool dan grup info; padding & tipografi konsisten tokens.

### 5. Dialog disamakan ke tokens
- Dialog revert/discard (`EditorDialogs.cs`), dialog unduh model (`Editor.cs`), dialog
  custom color (`EditorOptions.cs`): bg #2B2B2B, radius 3, judul 12px #E8E8E8, deskripsi
  #A6A6A6, tombol netral #3A3A3A / danger #B91C1C / primer #31A8FF.
- Panel Refine Busy: radius 3, `EditorBtnBg`.

### 6. Keseragaman tombol (aturan keras user)
- Dalam satu grup/baris, semua tombol WAJIB lebar & tinggi sama persis.
- Dialog revert/discard jadi vertical stack full-width (AskDiscardConfirmAsync;
  ConfirmDiscardChanges width 540→420); grup mode selection fixed `Width="72"`
  (terukur 72,72,72,72); Grow/Shrink `MinWidth="64"`; OK/Cancel custom color `MinWidth=84`.
- Grup `OptSelGrowGroup` dihapus dari options bar (redundan: sudah ada di dock panel +
  menu Select; kontennya ~890px overflow dan menimpa toggle Bandingkan di 1000px).
  ScrollViewer pengaman tetap dipasang agar tidak ada overlap di jendela sempit.

### 7. Slider options bar diperbaiki (keluhan user: "terlihat aneh")
- Akar masalah: track terlalu pendek (52–64px) sehingga thumb terlihat kebesaran.
- Perbaikan: semua 10 slider mode edit (Feather, Grow, Brush size, Softness, Opacity,
  Flow, Tolerance, Refine size/feather, Pen thickness + Pixels di panel kanan)
  dilebarkan ke 100–110px dan `VerticalAlignment="Center"` (baseline sejajar).
- Sempat dibuat style custom (track 4px + thumb 14px) di `EditorSliderStyles.axaml`,
  tetapi template custom MERUSAK interaktivitas: Avalonia `Slider` mencari
  `PART_DecreaseButton`/`PART_IncreaseButton` di namescope template-nya sendiri dan
  menangani thumb-drag sendiri (`_track.IgnoreThumbDrag = true`) — drag & klik track
  tidak berfungsi. Demi aturan "fungsi tetap jalan", custom style DIBATALKAN (file
  dihapus) dan kembali ke template default Fluent yang proporsional di lebar baru.
- Hasil: slider terlihat normal & proporsional, drag terverifikasi (60 px → 2000 px).

### 8. Simetri & kesan modern options bar (penegasan user)
- Tinggi bar 32px; semua kontrol `VerticalAlignment=Center`; border 1px #3A3A3A flat,
  radius 3px, spacing 8px antar grup — ala options bar Photoshop CC.
- Judul tool dipadatkan agar muat tanpa ellipsis: "Rectangular Marquee"→"Rect Marquee",
  "Elliptical Marquee"→"Ellipse Marquee", "Polygonal Lasso"→"Poly Lasso"
  (berlaku juga di status bar via `ToolDisplayName`).

### Verifikasi Tahap 6
- `dotnet build -c Release`: **0 warning, 0 error** (PreviewWindow.axaml tetap < 120KB).
- GUI Xvfb (VerifyApp), window 1000x600:
  - `800` — mode edit bersih (tanpa floating compare, tanpa label Result).
  - `805/806`, `808` — toggle Bandingkan via options bar + checkbox menu View sinkron.
  - `820/824` — grup mode selection 72px seragam; `821` — dialog revert full-width identik.
  - `840-opt-{pan,lasso,wand,pen,brush,eraser,move,marquee,refine}.png` — options bar
    per tool: simetris kiri-kanan, tinggi kontrol seragam, slider proporsional.
  - `844-slider-drag2.png` — drag slider brush berfungsi (60→2000 px).
  - `845-dock-slider2.png` — slider Pixels panel kanan konsisten dengan options bar.
- Pelajaran: (1) binary VerifyApp WAJIB di-rebuild setelah edit kode; (2) Escape tidak
  menutup dialog modal Avalonia — klik "Batal"; (3) jangan retemplate `Slider` Avalonia
  tanpa meniru struktur template default (template parts di namescope Slider).

## Status
- 1 commit Tahap 6 di branch `fix/pen-selection-revert` (mencakup semua sub-bagian 1–8). **JANGAN push** —
  menunggu perintah user (push ke `main` setelah semua tahap + tahap bahasa Indonesia selesai).

## Tahap 7 — Lokalisasi Indonesia/Inggris + dialog Preferensi ala Photoshop

### 7.1. Sistem lokalisasi ID/EN (live, tanpa restart)
- `Resources/Strings.id.axaml` + `Strings.en.axaml` — **281 key**, sumber kebenaran
  di `/tmp/t7/strings.py` (dict `S` + `REMAP`, fungsi `gen_dict()`).
- Dictionary di-merge di **`App.axaml` (level Application)** agar semua window
  (termasuk dialog) ikut berganti bahasa; `ApplyEditorLanguage()` menukar
  `ResourceInclude` di `Application.Current.Resources` lalu `RefreshLocalizedTexts()`.
- axaml memakai `{DynamicResource Key}` (246 titik); code-behind (Toast/hint/tooltip/
  dialog/status/nama tool) lewat helper `T(key, args)` — satu sumber, tidak ada
  hardcode dua bahasa di call-site. `EditorShortcutMap.TryValidate` terima
  `Func<string,string>? t` opsional untuk pesan error lokal.
- Setting `PreviewWindowSettings.EditorLanguage` ("id"/"en", default `"id"`).
- Terjemahan natural: Undo=Batalkan, Redo=Ulangi, Deselect=Batalkan pilihan,
  Invert=Balikkan, Feather tetap "Feather", Quick Mask=Masker cepat, dsb.

### 7.2. Warna garis Pen pindah ke pengaturan
- 8 swatch + tombol Kustom pindah dari options bar Pen ke pengaturan;
  slider **Tebal tetap** di options bar (options bar rapi).
- Key baru: `Set_PenSection`, `Set_PenColor`, `Dlg_ExtraColors`; hint Pen diperbarui.

### 7.3. Flyout pengaturan → dialog Preferensi modal (Ctrl+K)
- `Views/PreferencesWindow.axaml` + `.axaml.cs` — gaya Photoshop Preferences:
  daftar kategori kiri (**Umum, Bahasa, Pen, Pintasan**), panel konten kanan,
  tombol **Batal / OK** seragam 96px di bawah. Token tahap 3 (#2B2B2B/#3A3A3A/
  radius 3/aksen #31A8FF).
- Dibuka via **ikon gear** (`OnPreferencesClick`) atau **Ctrl+K**
  (di `OnPreviewKeyDown`, sebelum cabang eksklusif edit-mode; tidak konflik).
- Isi per kategori: Umum = Mode Editor (Beta) + Latar (tipe/kotak/solid);
  Bahasa = combo Indonesia/English; Pen = warna garis; Pintasan = 5 shortcut
  preview + 19 aksi edit remappable + daftar fixed + "Kembalikan bawaan".
- Semantik OK/Batal jujur: **OK** = validasi + simpan semua + tutup;
  **Batal/Esc** = tutup tanpa menyimpan — perubahan live (bahasa, warna pen,
  beta, background) dikembalikan ke snapshot saat dialog dibuka.
- PreviewWindow mengekspos API publik minimal: `Settings`, `Tr()`, `ShowToast()`,
  `ApplyPreferencesLanguage()`, `SetPenPathColorPref()`, `RefreshEditorHints()`,
  `ApplyBackground()` (kini public).
- Key string baru: `Pref_Title/General/Language/Pen/Shortcuts`, `Btn_OK`,
  `Dlg_ExtraColors`.

### Pelajaran teknis (bug yang ditemukan & diperbaiki)
1. **Avalonia 11.3 `ResourceInclude(Uri)` TIDAK mengeset `Source`**
   (hanya `_baseUri`; `get_Loaded()` butuh `Source` → crash "Source must be set").
   Solusi: `new ResourceInclude(IServiceProvider)` + set `Source` via initializer,
   dengan `NullServiceProvider` minimal (ctor NRE bila provider null).
2. **`SelectionChanged` fire saat XAML populate** (`SelectedIndex="0"`) —
   `FindControl` di handler crash "Could not find parent name scope".
   Solusi: flag `_uiReady`, handler abaikan event sebelum init selesai.
3. **Esc tidak sampai ke `Window.OnKeyDown`** bila ComboBox fokus (ditandai handled).
   Solusi: `AddHandler(KeyDownEvent, ..., handledEventsToo: true)`.
4. Jangan `pkill -f`/grep -i untuk bersih-bersih proses: pola `verifyapp` (case-insensitive)
   cocok dengan path skrip sendiri → bunuh diri (SIGKILL). Pakai pola presisi
   (`verifyapp/bin.*VerifyApp`) atau PID spesifik.
5. `x11help` konek ke DISPLAY saat import — import SETELAH Xvfb siap.

### Verifikasi Tahap 7
- `dotnet build -c Release`: **0 warning, 0 error** (PreviewWindow.axaml 66KB < 120KB).
- `logiccheck`: **ALL PASS**.
- GUI Xvfb (VerifyApp), skrip `gui_verify_prefs.py` + `gui_verify_prefs2.py`:
  - `820` — dialog Preferensi kategori Umum (ID); `821` — Bahasa; `822` — Pen
    (8 swatch + Kustom, ring seleksi); `823` — Pintasan (semua baris ID).
  - `824/825` — ganti ke English dari dalam dialog: kategori + tombol +
    combo langsung EN; `826` — main window EN (menu File/Edit/Select/View,
    PROPERTIES, SELECTION, hint bar EN).
  - `GEAR-OPEN` ✓, `LANG-EN` ✓, `ESC-CLOSED` ✓, `ESC-RESTORE-ID` ✓
    (Esc kembalikan bahasa ID), `PEN-PERSIST=#FF3B30` ✓ (OK simpan),
    `CANCEL-RESTORE=#FF3B30` ✓ (Batal buang perubahan biru).
  - `829/830` — kembali ID penuh setelah Esc.

## Status
- Tahap 7 (lokalisasi + warna pen di pengaturan + dialog Preferensi) selesai di
  branch `fix/pen-selection-revert`, **belum di-commit**. **JANGAN push** —
  menunggu perintah user (push ke `main` setelah konfirmasi akhir).

---

## Putaran 2 (2026-10-02)

Branch: `fix/edit-ui-round2` (dari `main` @ `bd6a730`). 11 keluhan user dari screenshot.

### #1: Rail tool button terpotong
- **Perbaikan**: `Focusable="False"` di style `Button.editor-tool`; hapus perubahan `BorderThickness` pada `:focus`.
- **Bukti**: `r2-v1f-6x.png` — tombol penuh tidak kepotong.

### #2: Animasi tombol aneh
- **Perbaikan**: Hapus `scale(1.025)/scale(0.975)` di App.axaml + `TransformOperationsTransition`; hapus `scale(1)` no-op di EditorStyles.axaml. Tombol kini hanya fade warna 0.12s.
- **Bukti**: Dialog "Perubahan belum disimpan" — transisi halus.

### #3: Tombol panel PROPERTI tidak rapih
- **Perbaikan**: 5 grup tombol diubah ke Grid kolom `*` sama lebar, `ColumnSpacing="4"`, margin manual dihapus.
- **Bukti**: `r2-03-props-zoom.png` — tombol seragam.

### #4: Slider tebal garis Pen
- **Perbaikan**: Tambah slider "Tebal garis: 1-8 px" di Preferensi kategori Pen, terhubung ke `EditorPenPathThickness`. Live update + persist. Lokalisasi ID/EN.
- **Bukti**: `r2-04-pref-pen3-crop.png`.

### #5: Remap Ctrl+Enter → Shift+Space
- **Perbaikan**: `IsReservedKeyForAction` kini terima modifiers — Space polos tetap reserved untuk Pan, tapi Space+Shift/Ctrl boleh di-remap. Handler Space-pan hanya untuk Space polos. `OnShortcutTextBoxKeyDown` teruskan modifiers.
- **Bukti end-to-end (2026-10-02, Tugas B)**: alur penuh via GUI —
  1. `taskB-12-remapped.png`: textbox "Buat seleksi" menerima "Shift+Space" (remap tersimpan: `"MakeSelection":"Shift+Space"` di settings).
  2. `taskB-15-path.png`: path Pen 4 anchor digambar.
  3. `taskB-16-selection.png`: tekan Shift+Space → SELEKSI TERBENTUK (marching ants; panel SELEKSI "Ada seleksi · 0.13 Mpx"; status bar "Seleksi: 0.13 Mpx · Replace"; Batalkan 1).
- **Catatan automation**: dialog Preferensi adalah window X11 terpisah — keypress harus dikirim setelah `focus(dialog)`, jika tidak key masuk ke window utama.

### #6: Restorasi panel Layers (minimal)
- **Perbaikan**: `PanelDocLayers` IsVisible=True; hapus kondisi `_editorV2Active` yang menyembunyikan di runtime. Panel LAPISAN tampil dengan tombol +,-,Dup,Merge yang menerima klik.
- **Catatan**: Fungsi layer penuh butuh DocSession V2 (fase terpisah). Struktur tetap sebagai kontrol terpisah untuk docking mendatang.
- **Bukti**: `r2-06-layers3-crop.png`.

### #7: Bersihkan preview mode
- **Perbaikan**: Hapus 3 elemen melayang: `PanelViewControls` (Bandingkan+zoom), `BtnEnterEdit` (tengah-bawah), `BtnEnterEditHeader` (kanan-atas). Tambah `BtnCompareBottom` di bottom bar agar fungsi bandingkan tetap aksesibel.
- **Bukti**: `r2-07-preview.png` — preview bersih, bottom bar: < Bandingkan BUKA EDITOR >.

### #8: Undo/redo untuk path & seleksi
- **Perbaikan**: 
  - `MaskUndoStack`: Snapshot simpan selection coverage; `Push`/`Undo`/`Redo` overload untuk (mask, selection).
  - `MaskEditSession`: `UndoAction`/`RedoAction` pulihkan seleksi; `PushSelectionUndo()`.
  - Operasi seleksi (buat, semua, perluas, perkecil, feather, balikkan) push sebelum ubah.
  - Pen path: stack di UI; Ctrl+Z saat path aktif → path hilang; Ctrl+Shift+Z → kembali (`PenTool.RestoreAnchors`).
  - `RenderAnts()` dipanggil setelah undo/redo.
- **Bukti**: `r2-08-path.png` → `r2-08-undo.png` (path hilang) → `r2-08-redo.png` (kembali); `r2-08-sel3.png` → `r2-08-selundo3.png` (seleksi hilang).

### #9: Restore masking dimensi tertukar (rotasi 90°)
- **Investigasi**: `PrepareEditorAsync` menolak restore bila `obuf.Width != result.Width`. Kasus umum: hasil = rotasi 90° dari asli (6000×4000 vs 4000×6000).
- **Perbaikan**: Bila W/H tertukar, putar buffer asli 90° searah jarum jam (`Rotate90Clockwise`) agar cocok; restore tersedia. Dimensi benar-benar beda tetap ditolak dengan pesan jelas.
- **Bukti end-to-end via GUI dengan gambar rotasi aktual (2026-10-02, Tugas C)**:
  - File test: `/tmp/rot_orig.png` 600×400 (biru + kotak merah kiri-atas + hijau kanan-bawah); `/tmp/rot_hasil.png` 400×600 (abu-abu + lubang TRANSPARAN di tengah — simulasi background removal).
  - Alur: masuk edit mode (tombol "Pulihkan" AKTIF — tanpa #9 restore tidak tersedia) → Ctrl+A → Shift+Delete (pulihkan) → Ctrl+S.
  - `taskC-08-before.png`: lubang transparan terlihat. `taskC-09-restored.png`: lubang terisi BIRU (piksel asli).
  - **Verifikasi piksel**: file hasil simpan dibandingkan dengan ekspektasi (`original.transpose(ROTATE_270)` mengisi area transparan) via PIL `ImageChops.difference` → **total diff = 0, COCOK SEMPURNA**. Arah rotasi 90° searah jarum jam terbukti benar (kotak merah kiri-atas pindah ke kanan-atas).
  - Catatan: restore hanya memulihkan area transparan (komposit: `maskAlpha > sourceAlpha` → tampilkan piksel Original); area opaque tetap menampilkan Result. Ini by-design untuk alur background-removal.

### #10: Double-click tidak menutup path (ala Photoshop)
- **Perbaikan**: Hapus logika double-click menutup path. Double-click kini hanya menaruh 2 anchor biasa. Path ditutup via klik anchor pertama (`PenTool.AddAnchor` via `CloseHitRadius`). Tambah indikator lingkaran kecil di kursor saat hover anchor pertama.
- **Bukti**: `r2-10-dblclick.png` (path tidak tertutup); `r2-10-hover.png` (indikator); `r2-10-closed.png` (path tertutup via klik anchor pertama).

### #11: Percepat tampil preview
- **Investigasi**: `PrepareEditorAsync` melakukan decode Bitmap sinkron di UI thread (`await Task.CompletedTask` no-op); gambar besar memblokir jendela.
- **Perbaikan**: Decode result & original dipindah ke `Task.Run` (background); UI langsung tampil; session diisi saat selesai. Pemanggil tidak await; `WireEditorControls` via Dispatcher.
- **Angka sebelum/sesudah (2026-10-02, Tugas D)** — diukur jujur via worktree sementara di `001f344^` (sebelum) vs `001f344` (sesudah), instrumentasi Stopwatch sementara (tidak di-commit), gambar test 3000×2000 (original 1.6MB, file beda agar decode original ikut terukur):
  - SEBELUM: UI thread terblokir **1309 ms** selama penyiapan sesi (`session-done-ui-blocked-ms=1309`) — decode original + alokasi sesi sinkron di UI thread.
  - SESUDAH: kerja yang sama (**1270 ms**) berjalan di background thread (`session-done-bg-ms=1270`); UI thread bebas segera setelah gambar tampil — **0 ms freeze**.
  - Kesimpulan: total kerja sama (~1.3 dtk), tapi tidak lagi memblokir interaksi (klik/scroll) selama penyiapan.

### Status verifikasi
- Build Release: 0 warning, 0 error.
- Logiccheck: ALL PASS.
- GUI: **11 dari 11 item terverifikasi end-to-end** via Xvfb screenshot (2026-10-02): #5 (Shift+Space trigger seleksi), #9 (restore rotasi, diff piksel 0), #11 (angka 1309ms → 0ms freeze).

### Susulan: rapatkan tombol panel Properti (fd6d91f)
- **Perbaikan**: `ColumnSpacing` 4→2, margin grid 0,2→0,1, padding section 12,10→8,8 (SELECTION & MASK) agar tombol rapat mengisi penuh sesuai contoh user.
- **Bukti**: `taskE-properties-closeup.png` — tombol Semua/Kosongkan/Balikkan, Perluas/Perkecil/Feather, Hapus/Pulihkan/Isi, Defringe full-width: rapat, tanpa ruang kosong aneh.

### Tugas A (2026-10-02): hilangkan silent-fail masuk mode edit — commit `87f31d1`
- **Masalah**: Sejak #11, `_session` dibuat di background task. `EnterEditMode()` diam-diam return bila `_session == null` → tombol BUKA EDITOR / tombol "e" tidak bereaksi bila diklik sebelum session siap. Background task juga BISA gagal permanen (file hilang/corrupt → `_session` null selamanya).
- **Perbaikan** (`PreviewWindow.Editor.cs`, `PreviewWindow.axaml.cs`, `PreviewWindow.axaml`, `Strings.id/en.axaml`):
  - Field baru `_sessionPreparing` + `_sessionFatalError`; `PrepareEditorAsync` mencatat alasan gagal fatal.
  - `BeginPrepareEditorSessionAsync()`: wrapper terpusat — set status preparing, update UI ("Menyiapkan...", tombol disabled), Toast sekali bila gagal.
  - `EnterEditMode()`: tidak lagi silent — Toast "Menyiapkan editor, mohon tunggu sebentar..." (bila preparing), Toast alasan + retry otomatis (bila gagal), Toast bila Mode Editor (Beta) nonaktif.
  - Tombol footer "BUKA EDITOR" dinonaktifkan + berlabel "Menyiapkan..." selama preparing; teks footer kini localized (`Footer_OpenEditor`/`Footer_EditorMode`, default axaml "PHOTOSHOP" diganti resource).
  - Tombol "e" memanggil `EnterEditMode()` langsung (selalu ada feedback).
- **Bukti GUI**: `taskA-07-early-e.png` — tekan "e" segera setelah window tampil → Toast oranye "⚠ Sesi editor belum siap." (tidak lagi diam); `taskA-04-e-key.png` — setelah session siap, "e" masuk edit mode normal (layout Photoshop penuh).
- Build Release 0/0; logiccheck ALL PASS.

## Putaran 3 — WS2: Audit Total Tombol (2026-10-02)

Keluhan user: tombol-tombol masih belum rapih, belum grid/rapat, lebar beda-beda.

### Temuan (screenshot ulang semua options bar)
1. Tombol mode Baru/Tambah/Kurang/Iris pakai `Width=72` di StackPanel — kaku, tidak auto-fit bahasa.
2. Tombol brush "Lanjutan" vs "Hapus" content-sized, lebar beda.
3. Wand: "Kurang" terpotong ("Kuran") — bar overflow di 1000px.
4. Panel Properti: tombol "Isi" lebih sempit dari "Hapus"/"Pulihkan" (tidak Stretch).
5. Margin redundan `Margin="8,0,0,0"` (StackPanel sudah Spacing=8).

### Perbaikan
- `PreviewWindow.axaml`:
  - `OptSelectionModeGroup` pindah ke PALING KIRI (ala Photoshop) + Grid SharedSizeGroup (kolom auto sama lebar, fit ID/EN) + Padding 6,4.
  - `BrushModeButtonGrid` baru: Grid 2 kolom untuk Lanjutan/Hapus (sama lebar).
  - Hapus 5 margin redundan.
- `PreviewWindow.EditorOptions.cs`: `UpdateBrushModeButton` — saat Eraser (Hapus hidden), Lanjutan ColumnSpan=2.
- `EditorStyles.axaml`: `editor-compact` += `HorizontalAlignment=Stretch`, `HorizontalContentAlignment=Center`.

### Verifikasi GUI
- Brush: Lanjutan/Hapus sama lebar ✓ (`ws2-brush-adv.png`)
- Wand: 4 mode + Toleransi + 3 checkbox + 8-Terhubung penuh terlihat ✓ (`ws2-wand10.png`)
- Lasso/Marquee/Pen: mode di kiri, rapi ✓ (`ws2-lasso8.png`, `ws2-marquee10.png`, `ws2-pen10.png`)
- Properti: Hapus/Pulihkan/Isi sama lebar ✓ (`ws2-maskrow-final.png`)
- Build: 0 warning/error.

## Putaran 3 — WS3: Highlight Tombol Rail Terpotong (2026-10-02)

Keluhan user: highlight tombol rail masih terpotong (perbaikan lama Focusable=false tidak menyentuh akar).

### Akar masalah (terbukti via analisis piksel)
Rail width 52px - padding 16px = 36px content, tombol 36×36 = PAS 0px ruang napas.
Highlight aktif (border biru #31A8FF) sisi kanan terpotong oleh clip ScrollViewer:
- Sebelum: top=37, bottom=35, left=38, **right=6 piksel** (102 piksel biru total)
- Fokus/keyboard bukan penyebab (Focusable=false sudah benar, tapi bukan akar).

### Perbaikan
- `EditorTheme.axaml`: `EditorRailWidth` 52 → 56 (content 40px, tombol 36px dapat 2px napas tiap sisi).

### Verifikasi piksel
- Sesudah: top=38, bottom=36, left=38, **right=36 piksel** (134 piksel biru total) — keempat sisi lengkap.
- Screenshot: `ws3-blue-fixed-vis.png` (visualisasi border biru utuh).
- Build: 0 warning/error.
