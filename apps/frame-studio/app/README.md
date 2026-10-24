# Frame Studio

Frame Studio adalah ruang kerja desktop untuk template PSD. PSD/PSB master dibuka hanya-baca, foto sumber dimuat dari perangkat, kandidat slot ditampilkan sebagai perkiraan, dan hasil baru disimpan sebagai PNG di folder terpisah.

## Alur saat ini

1. Pilih satu PSD/PSB master, folder foto sumber (atau beberapa file gambar), dan folder output.
2. Periksa komposit tersimpan serta kandidat foto, teks, dan logo yang dikenali dari nama layer. Kotak kandidat berasal dari batas layer; ini hanya perkiraan, bukan mask Photoshop atau objek teks yang sudah diverifikasi.
3. Pasangkan gambar sumber ke kandidat foto/logo. Pangkas, zoom, geser, rotasi, serta mode isi/penuh disimpan per slot gambar. Posisi foto/logo bisa diatur di kanvas atau panel pratinjau. Setiap kandidat teks memiliki kolom manual yang dirender sebagai overlay raster.
4. Ekspor satu PNG baru untuk setiap foto dalam antrean. Slot pengganti adalah kandidat foto/logo yang dipilih, atau kandidat gambar pertama. Jika ada foto antrean tetapi tidak ada kandidat gambar dengan batas yang tersedia, ekspor ditolak agar foto tidak diam-diam diabaikan. Jika antrean kosong, satu salinan dari kondisi kanvas saat ini dapat dibuat.

Ekspor dimulai dari PNG komposit tersimpan, lalu menambahkan gambar dan teks pada kotak layer perkiraan. Setiap hasil ditulis dulu ke file sementara di folder output, kemudian dipindahkan ke nama akhirnya. File yang sudah ada tidak ditimpa; nomor urut ditambahkan bila perlu. PSD/PSB master hanya dibaca dan tidak pernah dijadikan jalur output. Aplikasi belum menghasilkan PSD berlapis.

## Batas kompatibilitas dan kesetiaan visual

- Kotak foto/logo ditebak dari nama dan batas layer PSD; kotak itu belum diverifikasi sebagai mask, aperture, smart object, atau clipping group. Penempatan persegi dapat menutupi bingkai atau dekorasi yang seharusnya dipertahankan mask Photoshop. Periksa hasil sebelum dipakai untuk produksi.
- Perender dapat menyesuaikan, memperbesar, menggeser, dan memutar gambar sumber di dalam kotak kandidat. Perender belum menerapkan ulang blend layer, mask, efek, atau layer tersembunyi PSD.
- Teks dirender sebagai overlay raster putih sederhana di dalam kotak kandidat. Teks asli dalam komposit tersimpan tidak dihapus dan mungkin tetap tampak di bawahnya. Font, gaya, perataan, dan isi objek teks PSD tidak diekstrak.
- Ekspor batch mengganti satu kandidat foto/logo pilihan untuk setiap foto sumber; assignment slot lain tetap. Aplikasi belum memetakan daftar nama/alamat ke beberapa slot atau membuat PSD yang dapat diedit.
- Mesin memakai PsdSharp 1.2.0 untuk mencoba membaca metadata dan komposit PSD/PSB. Fitur Photoshop bertag tertentu mungkin belum didukung. Jika parser gagal, aplikasi melaporkannya sebagai batas kompatibilitas atau interpretasi—bukan bukti bahwa file rusak. Penulis PSD berlapis tidak diaktifkan karena blok bertag yang belum dikenal dan kesetiaan komposit belum dapat dijamin.
- Render output dibatasi hingga 30 juta piksel per gambar. Ukuran komposit diperiksa sebelum alokasi raster.

## Build dan tes

Dari direktori utama repository:

```sh
dotnet build apps/frame-studio/app/FrameStudio.sln --configuration Release
dotnet test apps/frame-studio/app/FrameStudio.sln --configuration Release
```

Solusi mencakup aplikasi Avalonia, mesin PSD, dan dua proyek tes. Tes mesin memakai fixture PSD sintetis yang dibuat khusus. Tidak ada PSD atau foto milik pelanggan yang disertakan dalam repository.

## Mobile

Target mobile belum dibangun atau diuji. Proyek saat ini menargetkan `net8.0` dan memakai `Avalonia.Desktop`; belum ada target mobile atau validasi perangkat.
