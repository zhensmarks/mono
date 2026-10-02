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
