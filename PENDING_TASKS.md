# PENDING TASKS & IMPLEMENTATION PLAN

Project: BMachine.v2 - Explorer & BatchView Refinements

---

## 📋 Task List (Round 5+) — DONE

### 1. File Preview Side Panel ✅
- [x] Tambahkan panel preview di `OutputExplorerView.axaml` (Grid dua kolom: list + panel kanan).
- [x] Implementasi logic preview:
    - Klik file `.txt` → Tampilkan konten di panel kanan.
    - Klik file `.docx` → Tampilkan placeholder (buka dengan default app untuk view).
- [x] Layout side panel: Right-to-Left (panel muncul dari kanan, lebar 280px).

### 2. BatchView UI Fixes ✅
- [x] Hapus label statis "PILIHAN (TREE)" dan "OUTPUT LOKAL" yang menyebabkan header ganda.
- [ ] Gunakan root node dari TreeView sebagai identitas utama folder (opsional/lanjutan).

### 3. File Opening & Deleting Logic ✅ (Cross-Platform)
- [x] **Opening**: File bitmap `.jpg`, `.jpeg`, `.png` dibuka dengan **default app** (Image Viewer); shortcut (`.lnk`, `.desktop`, `.webloc`) juga dibuka dengan default app.
- [x] **Deleting**: Hapus (Delete) memindahkan ke **Recycle Bin/Trash**; Shift+Delete tetap hapus permanen.

### 4. Keyboard Shortcuts (Custom & Standard) ✅
- [x] Shortcut custom dari Settings ter-load di window Explorer (via `ExplorerShortcutsChangedMessage`).
- [x] `Ctrl + W`: Hanya menutup tab aktif jika ada > 1 tab; satu tab tetap tidak menutup window.
- [x] `Alt + Up`: Navigasi ke parent folder (sudah: `ShortcutNavigateUpGesture`).
- [x] `Ctrl + Tab`: Berpindah antar tab explorer (sudah: `ShortcutSwitchTabGesture`).
- [x] `F5`: Refresh folder aktif (`ShortcutRefreshGesture`).

### 5. Explorer UI Polishing ✅
- [x] **Fit-to-Content**: MinWidth pada kolom nama dan grid item agar tidak terpotong prematur.
- [x] **Sidebar**: Icon "+" di sidebar bawah dihapus; fungsi "Add Folder" (Pin external folder) dipindah ke context menu.
- [x] **Sorting**: File shortcut (`.lnk`, `.desktop`) dipisahkan dari folder fisik (urutan: Folders → Shortcuts → Files; GroupBy Type: grup "Shortcut" terpisah).

---

## 🌍 Cross-Platform (Universal)

Implementasi disesuaikan agar berjalan di **Windows, macOS, dan Linux**:

| Fitur | Windows | macOS | Linux |
|-------|---------|--------|--------|
| **Buka file default** | `IPlatformService.OpenWithDefaultApp()` → ShellExecute | `open` | `xdg-open` |
| **Recycle Bin / Trash** | `IPlatformService.MoveToRecycleBin()` → Shell API (SHFileOperation) | `~/.Trash` | `~/.local/share/Trash/files` + .trashinfo (XDG) |
| **Shortcut file** | `.lnk` | (opsional `.webloc`) | `.desktop` |
| **Sorting shortcut** | `.lnk` sebagai grup "Shortcut" | idem | `.desktop` sebagai "Shortcut" |

- **Recycle Bin**: Tidak lagi memakai `Microsoft.VisualBasic.FileIO` (Windows-only). Semua platform memakai `BMachine.Core.Platform.IPlatformService.MoveToRecycleBin(string path)`.
- **Preview panel**: UI murni Avalonia; tidak ada ketergantungan platform.
- **Shortcuts**: Load/save dari Settings; diterapkan di `ExplorerWindow.ApplyWindowExplorerShortcuts()` dan saat buka window baru.

---

## 🛠️ Technical Notes

### Preview Panel
- `OutputExplorerViewModel`: properti `IsPreviewPanelVisible`, `PreviewPanelTitle`, `PreviewPanelContent`, `PreviewPanelWidth` (0 atau 280).
- Selection change → `UpdatePreviewAsync()`: baca `.txt`; `.docx` tampil placeholder.

### Recycle Bin (Cross-Platform)
- **Windows**: P/Invoke `SHFileOperation` (shell32) dengan `FO_DELETE` + `FOF_ALLOWUNDO`.
- **macOS**: Pindah file/folder ke `~/.Trash` (dengan rename jika nama bentrok).
- **Linux**: Pindah ke `~/.local/share/Trash/files` dan tulis `.trashinfo` di `~/.local/share/Trash/info` (XDG Trash).

---

*Terakhir diperbarui setelah implementasi universal (cross-platform).*

---

## 📌 PENDING TASKS (Round 6+) — TODO

### Bugs to Fix
- [ ] **View submenu radio bug**: Checklist di View > menu tidak switch dengan benar (tetap di Grid saat pindah ke List/Thumbnail). Kemungkinan `IsChecked="{Binding IsVerticalLayout, Mode=TwoWay}"` binding tidak berfungsi sebagai radio toggle — perlu diubah ke command-based switching.
- [ ] **Thumbnails view bug**: Thumbnail view tidak bisa dipakai, mungkin XAML rendering issue atau layout conflict.
- [ ] **Rename shortcut (F2)**: Belum bisa dipakai melalui keyboard shortcut.
- [ ] **Item context menu masih ada New Folder/New File**: Hapus dari item context menu (sudah dipindah ke background menu > New submenu, tapi di horizontal/grid view masih ada).

### UI Improvements
- [ ] **Copy to #OKE / Move to #OKE buttons**: Perbesar seukuran context menu agar tidak terlihat aneh. Ubah dari button kecil menjadi full-width menu item atau sesuaikan padding/sizing.
- [ ] **Deselect on empty area click**: Klik di area kosong (background) harus menghapus seleksi item. Tambahkan handler `PointerPressed` di background area → `SelectedItems.Clear()`.
- [ ] **Copy/Cut/Paste customizable di Settings**: Tambahkan `ShortcutCopyGesture`, `ShortcutCutGesture`, `ShortcutPasteGesture` ke shortcut settings agar bisa di-customize user.

### New Features
- [ ] **Drag to block-select**: Implementasi rubber-band selection (drag di area kosong untuk memilih beberapa item sekaligus).
- [ ] **Drag & Drop (In/Out)**: Implementasi drag-in dan drag-out file support:
    - Drag file/folder dari Explorer ke luar (desktop, folder lain, app lain)
    - Drag file/folder dari luar ke Explorer untuk copy/move
    - Visual feedback saat drag (ghost preview, drop target highlight)
