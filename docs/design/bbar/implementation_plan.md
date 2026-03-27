# Plan for RAM Optimization (Tray Icon)

The current application consumes ~300MB of RAM. By implementing a "Tray Icon" mode where the main window is actually destroyed/closed instead of just hidden, we can significantly reduce the memory footprint when the app is "idle".

## Analisis Penggunaan RAM & Strategi "Deep Sleep"

Aplikasi saat ini memakan ~300MB karena semua modul (Trello, GDrive, Pixelcut, Batch, dll) dimuat di awal dan disimpan di memori oleh `DashboardViewModel`. 

Untuk mencapai target RAM rendah (<100MB), kita akan menerapkan sistem **"Deep Sleep"**:
*   Saat jendela ditutup ke Menu Bar, `MainWindow` dan `DashboardViewModel` ditutup sepenuhnya (Dispose) untuk mengosongkan RAM sebesar mungkin.
*   **Persistent Services**: `GlobalInputHookService`, `DatabaseService`, dan `RadialMenu` tetap aktif di latar belakang (Managed by `App.axaml.cs`).
*   Service background (Input Hook, Database) tetap berjalan.
*   Aset UI akan dibersihkan oleh Garbage Collector.

## Mekanisme Aktivasi (Triggering "Deep Sleep")

Karena aplikasi utama bersifat frameless, kita akan menyediakan 3 cara untuk berpindah ke mode B-Bar:

1.  **Sidebar "Idle" Button (User Choice)**: 
    *   Sebuah tombol baru dengan ikon `🌙` (Sleep) akan diletakkan di sidebar utama, tepat di atas tombol Logout.
    *   Klik tombol ini akan langsung menutup `MainWindow` dan memunculkan `BBarWindow`.
2.  **Minimize logic**: Jika user melakukan minimize manual (lewat taskbar), aplikasi bisa dikonfigurasi untuk otomatis pindah ke mode B-Bar.
3.  **Auto-Idle (Optional/Settings)**: Jika tidak ada aktivitas input selama X menit.

## Modalitas Jendela (Smart Wake-Up)

Untuk menghindari "Bangun Paksa" yang tidak diinginkan seperti kekhawatiran Anda, kita akan membagi kondisi aplikasi menjadi 3 mode:

1.  **Mode Latar Belakang (Deep Sleep)**: 
    *   Hanya **B-Bar** yang terlihat.
    *   **Radial Menu & JSX Execution**: Tetap bisa dijalankan 100% tanpa memunculkan jendela utama. Menjalankan script tidak akan men-trigger "Wake Up".
2.  **Mode Modular (Lite View)**:
    *   B-Bar aktif + satu jendela kecil khusus (misal: `BatchWindow` atau `TerminalWindow`).
    *   Aplikasi utama (Dashboard) tetap tertutup/Dispose. RAM tetap rendah (<100MB).
3.  **Mode Aktif (Full UI)**:
    *   `MainWindow` diciptakan kembali hanya jika user mengeklik:
        *   Tombol "Maximize" di B-Bar.
        *   Menu "Dashboard" di App Launcher.
        *   Navigasi spesifik ke UI Dashboard dari Radial Menu.

## Hasil Audit Trigger "Bangun" (Wake-Up Audit)

Saya telah mengecek kodingan untuk memastikan tidak ada fitur ringan yang "menarik" Dashboard berat secara tidak sengaja:

1.  **Image Lightbox Window** (`@[ImageLightboxWindow.axaml.cs](file:///Users/abeng/BMachine.v2/src/BMachine.UI/Views/ImageLightboxWindow.axaml.cs)`): 
    *   ✅ **Aman**. Jendela ini bersifat mandiri (Standalone). Membuka preview gambar tidak akan memicu pembuatan ulang Dashboard.
2.  **Modular Windows (Batch & Terminal)**:
    *   ✅ **Aman**. Kita akan memisahkan kodenya agar bisa dibuka tanpa `MainWindow`.
3.  **Pesan Navigasi (The Wakers)**:
    *   ⚠️ **Butuh Penyesuaian**: Pesan seperti `NavigateToPageMessage` (misal: klik menu di Radial Menu untuk buka Trello) harus didaftarkan di `App.axaml.cs`.
    *   **Logika baru**: `App` akan mendengarkan pesan ini. Jika `MainWindow` sedang `null` (Tidur), `App` akan otomatis memanggil `InitializeDashboardAsync()` dan membangunkan aplikasi.

**Daftar Trigger yang MEMBANGUNKAN aplikasi utama:**
*   Klik "Maximize" di B-Bar.
*   Klik menu "Dashboard/Trello" di B-Bar Launcher.
*   Klik menu navigasi Dashboard di Radial Menu.
*   Shortcut global khusus untuk Toggle Full UI.

**Daftar yang TETAP HEMAT RAM (Tidak bangun):**
*   Menjalankan script JSX (Photoshop/Illustrator).
*   Membuka preview gambar (Lightbox).
*   Membuka jendela Log/Terminal mandiri.
*   Proses background GDrive/Sync.

## Desain Jendela Modular (Terminal vs Batch)

Berdasarkan diskusi, kita akan memisahkan keduanya namun tetap terintegrasi:

1.  **TerminalWindow.axaml (The Log Hub)**:
    *   **Tujuan**: Menjadi pusat monitoring untuk seluruh aktivitas (termasuk script JSX dari Radial Menu).
    *   **Tab System**: Memiliki 4 tab internal: `Console`, `Master`, `Photoshop`, dan `Doc`.
    *   **UI**: Desain ala "Developer Console" yang clean dengan font Monospace.
2.  **BatchWindow.axaml (The Action Hub)**:
    *   **Tujuan**: Fokus pada antrean file dan eksekusi batch.
    *   **Integrasi**: Memiliki tombol "Open Logs" yang akan memunculkan `TerminalWindow` jika belum terbuka.
3.  **Standalone Benefit**:
    *   Anda bisa membuka **hanya** Terminal untuk memantau script Photoshop tanpa harus membuka UI Batch yang lebih kompleks.
    *   Jika Jendela Utama "Bangun", jendela-jendela modular ini akan otomatis menutup dan "kembali" menjadi tab di dalam Dashboard agar rapi.

## Premium Polish & Fitur Lanjutan (Saran Tambahan)

Untuk membuat B-Bar terasa sangat "Premium" dan bukan sekadar bar biasa:

1.  **Micro-Animations**:
    *   **Slide-Down Entry**: B-Bar muncul dengan animasi slide halus dari pinggir layar.
    *   **Glow Hover**: Angka statistik (`12 | 5...`) memiliki efek glow cyan tipis saat kursor mendekat.
2.  **Dynamic Task Monitor**:
    *   Jika ada proses Batch berjalan, angka statistik bisa berganti sementara menjadi progress bar kecil atau teks: `🔄 Batch: 45%`.
3.  **Status Connectivity**:
    *   Indikator visual (titik kecil) jika Trello atau GDrive sedang offline atau sedang melakukan sinkronisasi aktif.
4.  **Multi-Monitor "Follow Mouse"**:
    *   Opsi agar B-Bar muncul di monitor mana pun kursor Anda berada (sangat berguna untuk setup 2-3 monitor).
5.  **Smart Shortcuts (Action Hub)**:
    *   Menambahkan tombol cepat untuk **Screenshot** atau **Color Picker** langsung di B-Bar tanpa harus membuka Dashboard.

**Hasil**: B-Bar bukan lagi sekadar alat hemat RAM, tapi menjadi "Pusat Komando" produktivitas Anda.

## Potensi Penghalang (Obstacles) & Solusi

| Penghalang | Detail | Solusi |
|------------|--------|--------|
| **Multi-Monitor** | Bar harus tahu di monitor mana ia harus memanjang. | Gunakan API `Screens` di Avalonia untuk mendeteksi `PrimaryScreen` dan menyesuaikan lebar secara dinamis. |
| **Always on Top** | Bar idle harus tetap terlihat di atas aplikasi lain (seperti taskbar). | Set `Topmost="True"` dan `SystemDecorations="None"` pada jendela bar. |
| **RAM usage** | Jendela yang memanjang tetaplah sebuah "Window". | Kita akan tetap menggunakan strategi **Dispose UI Utama** saat bar aktif, sehingga bar tersebut hanya memuat aset yang sangat minimal. |

## Proposed Changes

### Proyek `BMachine.App`
Add `TrayIcon` to `App.axaml` and handle window state.

#### [MODIFY] [App.axaml](file:///Users/abeng/BMachine.v2/src/BMachine.App/App.axaml)
Add `<TrayIcon>` definition with Menu items (Open, Exit).

#### [MODIFY] [MainWindow.axaml.cs](file:///Users/abeng/BMachine.v2/src/BMachine.App/Views/MainWindow.axaml.cs)
Handle the `Closing` event to either exit or minimize to tray.

## Verification Plan

### Manual Verification
1.  Check Task Manager RAM usage when window is open (~300MB).
2.  Close the window to Tray.
3.  Observe RAM reduction.
4.  Reopen from Tray and ensure everything reloads correctly.
