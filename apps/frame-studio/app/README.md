# Frame Studio

Frame Studio adalah ruang kerja desktop untuk template PSD. PSD/PSB master dibuka hanya-baca dan foto sumber tetap lokal. Nama layer seperti `XL` atau `S` tidak cukup untuk membuat slot foto; area gambar hanya dianggap siap bila memiliki batas positif yang beririsan dengan kanvas serta bukti mask, mask vektor, atau clipping.

## Prototype Bingkai Foto

Tombol **Bingkai Foto · preview** membuka alur eksperimen yang terpisah dari parser PSD dan kandidat `FrameSlot`. Prototype memakai artboard serta foto contoh sintetis, mendukung mask persegi panjang dan poligon sederhana dengan titik ternormalisasi, memilih salah satu dari dua posisi layer yang disimulasikan, menerima foto lokal lewat picker atau drop, lalu mengatur crop, zoom, dan rotasi. Mask dan titik sisip disimpan sebagai data bingkai pengguna; bingkai dapat disimpan dan dipakai ulang selama sesi prototype.

Preview dan ekspor PNG memakai hasil render yang sama, termasuk clip path mask. PNG merupakan raster pratinjau saja. PSD berlapis tetap nonaktif; prototype tidak membaca atau mengubah master PSD, dan mask/effect Photoshop lain belum didukung. Jalankan `FrameStudio.App --frame-tool-prototype` untuk membuka demo langsung.

## Alur saat ini

1. Pilih satu PSD/PSB master, folder foto sumber (atau beberapa file gambar), dan folder output.
2. Periksa komposit tersimpan dan area yang dikenali. Label ukuran saja—termasuk `XL` atau `S`—bukan slot. Kandidat foto/logo tanpa batas valid dan mask/clipping tidak ditampilkan sebagai target yang bisa dipakai.
3. Penempatan, kontrol crop, dan ekspor nonaktif sampai setidaknya satu slot foto lolos pemeriksaan batas dan mask/clipping. Jika belum, panel menampilkan “Slot foto belum disiapkan.” Kandidat teks tetap berupa overlay raster manual, bukan pengeditan objek teks PSD.
4. Setelah slot foto tervalidasi tersedia, ekspor membuat salinan PNG terpisah; PSD master tidak pernah menjadi jalur output. Validasi batas ini belum berarti perender setara Photoshop Frame Tool: komposit tetap dirender dengan kotak persegi dan mask/effect PSD belum diterapkan ulang.

Ekspor yang diaktifkan dimulai dari PNG komposit tersimpan, lalu menambahkan overlay lokal pada area yang tervalidasi. Setiap hasil ditulis dulu ke file sementara di folder output, kemudian dipindahkan ke nama akhirnya. File yang sudah ada tidak ditimpa; nomor urut ditambahkan bila perlu. PSD/PSB master hanya dibaca dan tidak pernah dijadikan jalur output. Aplikasi belum menghasilkan PSD berlapis.

## Batas kompatibilitas dan kesetiaan visual

- Penanda nama dan ukuran hanya membantu menemukan kandidat. Penempatan baru diaktifkan bila metadata menyediakan batas yang masuk akal beserta mask, mask vektor, atau clipping; pemeriksaan ini bukan jaminan kesetiaan Photoshop.
- Perender dapat menyesuaikan, memperbesar, menggeser, dan memutar gambar sumber di dalam kotak kandidat. Perender belum menerapkan ulang blend layer, mask, efek, atau layer tersembunyi PSD.
- Teks dirender sebagai overlay raster putih sederhana di dalam kotak kandidat. Teks asli dalam komposit tersimpan tidak dihapus dan mungkin tetap tampak di bawahnya. Font, gaya, perataan, dan isi objek teks PSD tidak diekstrak.
- Ekspor batch mengganti satu kandidat foto/logo pilihan untuk setiap foto sumber; assignment slot lain tetap. Aplikasi belum memetakan daftar nama/alamat ke beberapa slot atau membuat PSD yang dapat diedit.
- Mesin memakai PsdSharp 1.2.0 untuk mencoba membaca metadata dan komposit PSD/PSB. Fitur Photoshop bertag tertentu mungkin belum didukung. Jika parser gagal, aplikasi melaporkannya sebagai batas kompatibilitas atau interpretasi—bukan bukti bahwa file rusak. Penulis PSD berlapis tidak diaktifkan karena blok bertag yang belum dikenal dan kesetiaan komposit belum dapat dijamin.
- Status bawah menampilkan platform runtime, arsitektur proses, dan mesin PSD yang tersedia. `psd-tools` tidak diaktifkan sebagai fallback lintas platform; ekspor PSD berlapis tetap nonaktif. Jika slot foto belum lolos validasi, penempatan dan ekspor PNG juga tetap nonaktif.
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
