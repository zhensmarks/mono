# PENDING TASKS (Round 6+)

## 📌 TODO LIST

### 1. File Sorting & Grouping Logic
- [ ] **Shortcut (.lnk) Sorting**: Perbaiki logika pengurutan agar file `.lnk` tidak muncul di atas folder.
    - Urutan yang diharapkan: **Folders** → **Shortcuts (.lnk)** → **Files**.
    - Masalah: Saat ini `.lnk` muncul di atas folder atau tercampur.

### 2. View Layout Shortcuts 🆕
- [ ] **Ctrl + Shift + [1-8]**: Tambahkan keyboard shortcuts untuk mengubah view layout secara cepat (seperti Windows Explorer).
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
- [ ] **Drag to block-select**: Implementasi rubber-band selection (drag di area kosong untuk memilih beberapa item sekaligus).

### 4. Advanced File Operations (Parallel & Batch UI) 🆕
- [ ] **Parallel Operation Queue**: Allows multiple file operations (Copy/Move) to be queued or run concurrently without blocking the UI.
    - Target: Background queue manager that accepts new tasks while others are running.
- [ ] **Grouped Operation UI (Windows-like)**:
    - Show: "Moving X items from [Source] to [Destination]".
    - Progress bar reflects the *entire* batch operation.
    - If a folder is moved, it should be treated as one high-level operation in the UI.
