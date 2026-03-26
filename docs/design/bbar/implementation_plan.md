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

## Strategi Jendela Modular

Setiap item di launcher di atas (kecuali Dashboard) akan memiliki opsi untuk dibuka sebagai **Jendela Modular Terpisah**:
*   Jika diklik biasa: Membuka Jendela Utama ke tab tersebut.
*   Jika klik kanan/pilih opsi "Float": Membuka jendela kecil khusus untuk fitur tersebut agar tetap ringan (Deep Sleep Mode).

**Hasil**: Jendela bar tetap kecil dan hemat RAM, tapi Anda punya akses instan ke seluruh "kekuatan" BMachine.

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
