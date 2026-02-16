# PENDING TASKS (Round 6+)

## 📌 TODO LIST

### 1. View & Rendering Bugs
- [ ] **View submenu radio bug**: Checklist di View > menu tidak switch dengan benar (tetap di Grid saat pindah ke List/Thumbnail). binding tidak berfungsi sebagai radio toggle — perlu diubah ke command-based switching.
- [ ] **Thumbnails view bug**: Thumbnail view tidak bisa dipakai, mungkin XAML rendering issue atau layout conflict.

### 2. UI & Shortcuts
- [ ] **Drag to block-select**: Implementasi rubber-band selection (drag di area kosong untuk memilih beberapa item sekaligus).

### 3. Advanced File Operations (Parallel & Batch UI) 🆕
- [ ] **Parallel Operation Queue**: Allows multiple file operations (Copy/Move) to be queued or run concurrently without blocking the UI.
    - Currently: UI blocks or only handles one operation at a time.
    - Target: Background queue manager that accepts new tasks while others are running.
- [ ] **Grouped Operation UI (Windows-like)**:
    - Instead of showing "Moving File 1.jpg", "Moving File 2.jpg"...
    - Show: "Moving X items from [Source] to [Destination]".
    - Progress bar should reflect the *entire* batch operation (Total Size / Total Items).
    - If a folder is moved, it should be treated as one high-level operation in the UI.
