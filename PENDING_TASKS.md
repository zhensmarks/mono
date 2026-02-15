# PENDING TASKS & IMPLEMENTATION PLAN

Project: BMachine.v2 - Explorer & BatchView Refinements

---

## 📋 Task List (Round 5+)

### 1. File Preview Side Panel
- [ ] Tambahkan `SplitView` di `OutputExplorerView.axaml`.
- [ ] Implementasi logic preview:
    - Klik file `.txt` -> Tampilkan konten di panel kanan.
    - Klik file `.docx` -> Tampilkan preview teks/metadata.
- [ ] Layout side panel: Right-to-Left (panel muncul dari kanan).

### 2. BatchView UI Fixes
- [ ] Hapus label statis "PILIHAN (TREE)" dan "OUTPUT LOKAL" yang menyebabkan header ganda.
- [ ] Gunakan root node dari TreeView sebagai identitas utama folder.

### 3. File Opening & Deleting Logic
- [ ] **Opening**: Pastikan `.png` dibuka dengan Image Viewer default, kecuali user memilih Photoshop secara manual.
- [ ] **Deleting**: Ubah logic hapus agar menggunakan Shell API agar file masuk ke **Recycle Bin**, bukan dihapus permanen.

### 4. Keyboard Shortcuts (Custom & Standard)
- [ ] Pastikan shortcut custom dari settings ter-load di window Explorer baru.
- [ ] `Ctrl + W`: Hanya menutup tab aktif (jika ada > 1 tab), bukan seluruh window.
- [ ] `Alt + Up`: Navigasi kembali (Go to Parent Folder).
- [ ] `Ctrl + Tab`: Berpindah antar tab explorer.
- [ ] `F5`: Refresh folder aktif.

### 5. Explorer UI Polishing
- [ ] **Fit-to-Content**: Sesuaikan lebar kolom folder agar tidak terpotong (ellipsis) secara prematur.
- [ ] **Sidebar**: Hapus icon "+" di sidebar bawah; pindahkan fungsinya ke context menu (Add Folder).
- [ ] **Sorting**: Pastikan file Shortcut (`.lnk`) dipisahkan dari folder fisik dalam sorting.

---

## 🛠️ Technical Plan

### Side Panel Preview
Menggunakan `SplitView` dengan `DisplayMode="Overlay"` atau `Inline`. Data preview diikat ke `SelectedItems.FirstOrDefault()` pada `OutputExplorerViewModel`.

### Fit-to-Content Width
Mengubah `Grid.ColumnDefinitions` pada `ListBox.ItemTemplate` menjadi `Auto, Auto` dan menghapus `TextTrimming` pada `TextBlock`.

### Recycle Bin
Menggunakan interop ke `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` dengan `UIOption.OnlyErrorDialogs` dan `RecycleOption.SendToRecycleBin` untuk kompatibilitas Windows yang mudah.

---

*Dokumen ini dibuat secara otomatis untuk memudahkan kelanjutan pengerjaan di platform lain.*
