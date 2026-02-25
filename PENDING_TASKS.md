# PENDING TASKS (Round 6+)

## 📌 TODO LIST

### 1. Fix Bugs & Issues
- [x] **Trello Comment Copy Bug**: `Ctrl + C` sering gagal mengcopy teks yang di-select pada textbox komen Trello. Harus pakai context menu baru bisa.
    - Kemungkinan ada shortcut conflict atau handler `KeyDown` yang "menelan" event copy.


### 1. File Sorting & Grouping Logic
- [x] **Shortcut (.lnk) Sorting**: Perbaiki logika pengurutan agar file `.lnk` tidak muncul di atas folder.
    - Urutan yang diharapkan: **Folders** → **Shortcuts (.lnk)** → **Files**.
    - Masalah: Saat ini `.lnk` muncul di atas folder atau tercampur.

- [x] **View Layout Shortcuts** 🆕: Tambahkan keyboard shortcuts untuk mengubah view layout secara cepat (seperti Windows Explorer).
    - 1: Extra Large Icons
    - 2: Large Icons
    - 3: Medium Icons
    - 4: Small Icons
    - 5: List
    - 6: Details
    - 7: Tiles
    - 8: Content
*(Note: Sesuaikan dengan view yang tersedia di BMachine)*

### 3. UI & Shortcuts
- [x] **Drag to block-select**: Implementasi rubber-band selection (drag di area kosong untuk memilih beberapa item sekaligus).

- [x] **Trello Sync**: Memastikan urutan kartu yang diubah di BMachine sinkron dengan benar di Trello.com. <!-- id: 37 -->
- [x] **Terminal Responsiveness**: Memastikan panel log (terminal) menutup otomatis saat window dikecilkan dan tidak menabrak konten dashboard. <!-- id: 38 -->
- [ ] **Peningkatan UI & Fitur Explorer:**
    - [ ] Aktifkan *copy-paste* di panel *preview* (txt/docx)
    - [ ] Perbaiki pintasan *copy* saat ganti nama file (F2)
    - [x] Fitur **Copy Path** (Ctrl+Shift+C) dan **Paste Path** (Ctrl+Shift+V) untuk navigasi cepat <!-- id: 39 -->
    - [ ] Fitur **Reorder Tab** (Geser posisi tab kiri/kanan)
- [ ] **Aksi Otomatis & Script Photoshop:**
    - [ ] Fitur **Auto REPLACE** (Inline Action Folder): Mencocokkan folder Output dengan Input secara otomatis dan menjalankan replace.
    - [ ] Buat script `_auto_replace.jsx` (Tersembunyi dari Radial Menu).
    - [x] **Penyempurnaan Context Menu:**
        - [x] Sesuaikan lebar tombol "COPY/MOVE to #OKE" (UniformGrid).
        - [x] Flatten Menu Batch (Tampilkan langsung di menu utama dengan separator).
    - [x] **Auto & Manual Logic Script:**
        - [x] `replace.jsx`: Handle "Nama (1)" copy filename logic. (Fixed)
        - [x] `replace.jsx`: UI Refinement (Compact, Queue Button position). (Fixed)
        - [x] `save_master.jsx`: UI Refinement (Compact 2 Col). (Fixed)
        - [x] `save_master.jsx`: Feature "SAVE ORIGINAL" (Auto detect existing JPG/PNG). (New)
- [x] **Peningkatan UI Designer & Trello:**
    - [x] Latar belakang header Trello yang halus dari cover kartu
    - [x] Reposisi tombol aksi (Comment, Checklist, Move) di bawah judul kartu
    - [x] Ubah warna tombol aksi menjadi warna aksen aplikasi
- [ ] **Dashboard UI:**
    - [x] Tambahkan jarak pada tombol script Python (.py) di footer agar tidak rapat <!-- id: 36 -->
- [ ] **Parallel Operation Queue**: Allows multiple file operations (Copy/Move) to be queued or run concurrently without blocking the UI.
    - Target: Background queue manager that accepts new tasks while others are running.
- [ ] **Grouped Operation UI (Windows-like)**:
    - Show: "Moving X items from [Source] to [Destination]".
    - Progress bar reflects the *entire* batch operation.
    - If a folder is moved, it should be treated as one high-level operation in the UI.
### 5. Trello Quick Reply / Preset Comments [x]
- [x] **Preset Comments UI**:
    - Tambahkan icon chevron-up di sebelah comment textbox Trello.
    - Saat diklik, muncul dropdown/popup list berisi preset kata-kata.
    - Opsi "TAMBAH KATA KATA" (+ icon) untuk membuat preset baru.
    - **Logic Limit**: Tombol "TAMBAH KATA KATA" akan **hilang** jika sudah ada 5 preset. Tombol muncul kembali hanya jika salah satu preset dihapus.
    - List menampilkan teks preset, icon Pensil (Edit), dan icon Delete (Hapus).
    - Maksimal tampilan awal 5 baris (scrollable jika lebih).
- [x] **Functional Logic**:
    - Klik pada teks preset langsung mengirim (send) comment ke Trello tanpa perlu tekan tombol send lagi.
    - Edit: Membuka dialog/textbox untuk mengubah kata-kata yang sudah ada.
    - Delete: Menghapus preset dari list.
- [x] **Data Persistence**:
    - Simpan preset kata-kata secara permanen di database agar tidak hilang saat aplikasi ditutup.
- [x] **Compatibility**:
    - Textbox comment standar tetap harus bisa digunakan secara manual seperti biasa.

### 6. Search Functionality (File Search) [x]
- [x] **Ctrl + F: Search Bar Logic**:
    - Implementasi shortcut `Ctrl + F` untuk memunculkan search bar.
    - Search bar harus muncul di bagian **StatusBar** (bawah explorer) pada tab yang sedang aktif.
    - **UI Style**: Dibuat dengan tinggi yang pendek/kompak (Compact Height) agar tidak makan tempat.
    - Filter item di explorer secara real-time berdasarkan input teks di search bar.
    - Fokus otomatis ke textbox search saat shortcut ditekan.
    - Tombol "X" atau ESC untuk menutup search bar dan mengembalikan view normal.
    - *Technical Note*: Pastikan tidak ada "layout jump" saat search bar muncul/hilang.
