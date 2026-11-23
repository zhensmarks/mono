# PixaCompact Rust v2

Implementasi clean-room aplikasi desktop Windows untuk PixaCompact. Kode di folder ini tidak memakai ulang crate Rust lama; aplikasi C# dan port Rust terdahulu hanya menjadi referensi perilaku dan daftar fitur.

## Tujuan

Membuat alur kerja penghapusan latar yang lebih mudah dipahami tanpa menghilangkan kemampuan penting:

1. Tambahkan gambar atau folder lewat tombol maupun drag-and-drop.
2. Tinjau antrean, pilih engine, lokasi hasil, dan jalankan batch.
3. Pantau status tiap gambar; batalkan pekerjaan yang sedang berjalan dan buka hasil.
4. Buka editor untuk memperbaiki mask, refine hair, mengelola layer, undo/redo, dan mengekspor PNG.
5. Kelola akun/proxy untuk engine web, preferensi, pembaruan LAN, dan laporan bug.

## Arah pengalaman pengguna

- Dashboard berfokus pada antrean kerja: sumber gambar, engine yang dipilih, progres, dan tindakan utama terlihat tanpa mencari-cari.
- Status ditulis dengan label dan progres yang jelas; warna tidak menjadi satu-satunya penanda.
- Editor memisahkan kanvas, tool, properti, layer, dan riwayat; opsi hanya tampil saat relevan.
- Empty, loading, error, disabled, cancel, dan hasil selesai memiliki keadaan yang jelas.
- Bahasa awal: Indonesia dan Inggris. Teks UI tidak ditanam tersebar di komponen.
- Pilihan engine web selalu menjelaskan bahwa gambar dikirim ke layanan pihak ketiga. Engine lokal tetap jalur yang menjaga pemrosesan gambar di perangkat.

## Cakupan fitur yang wajib dipertahankan

- Penghapusan latar lokal berbasis ONNX, batch, pembatalan, hasil PNG, dan pengelolaan model/runtime.
- Engine web Pixelcut, BG Eraser, dan NOBG; pemilihan engine, rotasi akun, kuota, proxy, sesi login browser, dan pemilihan hasil ber-alpha.
- Editor: kanvas preview, brush, seleksi, magic wand, mask, refine hair, undo/redo, layer visibility/opacity/transform, 16 blend mode, duplikat, merge, serta ekspor.
- Riwayat/output provenance agar hasil valid tidak diproses ulang.
- Pengaturan, pembaruan LAN, pelaporan bug, dan penanganan crash.

## Cara memakai rust-anything

`rust-anything` bukan penerjemah kode dan tidak menguji tampilan desktop sebagai satu aplikasi. Gunakan `analyze` untuk memetakan unit C# yang dapat dipindah; untuk fungsi publik statis yang cocok, gunakan siklus `bridge` → `record` → implementasi Rust → `diff` → `gate`. Rekam fixture deterministik pada salinan proyek dengan data uji, tanpa kredensial atau database produksi.

Untuk alur GUI, jangan mengklaim `app record/replay` telah membuktikan interaksi pengguna. Tambahkan tes unit Rust untuk operasi gambar dan tes UI/Windows yang terpisah; uji manual alur drag-drop, batch, batal, editor, ekspor, pengaturan, dan error sebelum menyatakan siap.

## Kriteria selesai

- Semua fitur wajib di atas punya implementasi atau status yang dinyatakan jelas; tidak ada fitur lama yang diam-diam hilang.
- Fungsi matematika/transformasi deterministik yang dipindahkan cocok dengan kasus rekaman; seluruh gate dan tes Rust lulus.
- Build rilis Windows berhasil pada runner Windows, termasuk pemeriksaan aset ONNX/runtime dan paket webdriver.
- Uji UI menunjukkan antrean, progres, batal, editor, layer, ekspor, pengaturan, dan jalur error dapat digunakan dengan keyboard dan mouse.
- Perilaku upload ke layanan eksternal hanya terjadi setelah pengguna memilih engine web dan memulai proses.

## Urutan kerja

1. Kerangka aplikasi, tema, navigasi, antrean dan empty state.
2. Akses gambar dan pemrosesan batch lokal; rekam perilaku engine terkait.
3. Editor dan operasi mask/layer; rekam unit logika yang kompatibel.
4. Engine web, akun/proxy, login, kuota, dan penanganan kegagalan.
5. Pengaturan, update, bug report, aksesibilitas, packaging, dan verifikasi Windows.

Setiap tahap harus bisa ditinjau sebelum klaim fitur penuh dibuat.