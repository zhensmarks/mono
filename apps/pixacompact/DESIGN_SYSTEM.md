# Sistem Desain PixaCompact

Dokumen ini adalah aturan praktis untuk UI PixaCompact, bukan batasan estetika yang kaku. Tujuannya menjaga alur edit/foto tetap jelas, ringan dirender, dan nyaman digunakan dengan mouse maupun keyboard.

## Arah visual

- **Permukaan gelap netral** berlapis secukupnya; hindari menumpuk kartu, garis, glow, dan transparansi pada satu area.
- Gunakan **satu aksen utama** untuk aksi utama, fokus, dan pilihan aktif: biru sebagai default, tetapi hormati warna kustom yang dipilih pengguna. Warna lain hanya untuk makna status atau data yang memang berbeda.
- Pertahankan ruang kerja foto sebagai fokus: chrome editor tenang, ikon konsisten, dan label hanya ditampilkan saat membantu mengenali aksi.
- Jangan menambah paket ikon, font ikon, gambar raster dekoratif, atau efek blur global untuk kebutuhan chrome.

## Token warna

| Peran | Nilai | Pemakaian |
|---|---|---|
| Latar aplikasi | `#11161B` | Latar belakang jendela |
| Permukaan utama | `#171D23` | Panel dan jendela |
| Permukaan terangkat | `#202830` | Kontrol dan panel sekunder |
| Hover | `#29343F` | Isyarat hover yang terlihat |
| Aksen default | `#2D78C4` | Aksi utama dan pilihan aktif; gunakan `AccentBlueBrush` dinamis agar pilihan warna pengguna diterapkan konsisten |
| Teks utama | `#F1F4F7` | Judul, label, dan isi utama |
| Teks sekunder | `#AEB9C4` | Bantuan dan metadata |
| Garis pemisah | `#303A44` | Batas antarkelompok yang perlu |
| Fokus keyboard | `#B5D9FF` | Cincin fokus yang terlihat |
| Bahaya | `#CA4B55` | Hapus, hentikan, atau tindakan destruktif; pastikan teks putih tetap terbaca |

Pertahankan kontras teks terhadap permukaan. Status proses tidak boleh disampaikan lewat warna saja; sertakan label, ikon, atau indikator progres.

## Tombol dan kontrol

- Tombol teks umum: tinggi minimum **34 px**, radius **7 px**, padding seimbang, dan label tidak terpotong.
- Tombol ikon saja: area klik **36 × 36 px**; kontrol tutup/aksi title bar minimal **40 × 40 px** bila ruang memungkinkan.
- Tombol tool editor: **36 × 36 px**; kontrol ringkas boleh lebih pendek hanya ketika ada batas ruang nyata.
- Gunakan varian berdasarkan maksud: `primary` (satu aksi utama per area), `quiet` (aksi sekunder), `danger` (aksi destruktif), `icon` (ikon saja), `editor-tool`, `editor-compact`.
- Hover, tekan, nonaktif, terpilih, dan fokus harus punya state yang dapat dibedakan. Fokus keyboard tidak boleh hanya berupa hover.
- Jangan memberi warna primer kepada setiap tombol; pertahankan hierarki sehingga aksi utama paling mudah ditemukan.

## Ikon

- Pakai **PathIcon/StreamGeometry vektor** bawaan XAML; ukuran standar 16–18 px, ukuran besar hanya untuk ilustrasi keadaan kosong.
- Ikon dalam kelompok yang sama harus konsisten pada stroke/berat visual, bounding box, dan warna.
- Gunakan ikon yang sama untuk makna yang sama. Jangan gunakan emoji atau karakter font sebagai ikon kontrol.
- Tombol ikon saja wajib punya tooltip dan nama aksesibilitas (`AutomationProperties.Name`) yang menjelaskan tindakannya. Jangan mengandalkan tooltip sebagai satu-satunya label untuk aksi utama.

## Gerak dan performa

- Jendela aplikasi memakai permukaan solid secara default; hindari Acrylic/blur dan bayangan besar pada chrome.
- Hindari shimmer, glow pulse, atau animasi dekoratif yang berjalan tanpa henti. Gerak hanya untuk progres/loading yang sedang aktif atau transisi singkat yang membantu membaca perubahan state.
- Spinner proses boleh berjalan saat operasi benar-benar aktif dan harus berhenti pada selesai, gagal, batal, atau saat jendela ditutup.
- Gunakan brush/transisi singkat untuk hover; jangan mengubah skala atau posisi tombol saat pointer masuk.
- Utamakan geometri vektor dan resource bersama dibanding aset berulang atau dependensi baru.

## Aksesibilitas dan responsivitas

- Semua aksi penting dapat dicapai dengan Tab/Shift+Tab dan dijalankan dengan Enter/Space; urutan fokus mengikuti urutan tugas.
- Sediakan ring fokus yang kontras dan nama yang bermakna untuk kontrol ikon.
- Ukuran target desktop nyaman disentuh/klik dan label tetap terbaca pada bahasa Indonesia/English serta penskalaan tampilan.
- Dashboard menjaga lebar minimum **520 px** agar tombol proses dan kontrol footer tidak terpotong; galeri dan preferensi mempertahankan batas minimum jendelanya masing-masing.
- Scrollbar boleh tipis, tetapi tetap dapat ditemukan saat konten meluap.
- Uji keadaan normal, hover, fokus, terpilih, nonaktif, memuat, berhasil, gagal, dan kosong. Catat bila inspeksi pembaca layar atau pengukuran WCAG belum dilakukan; jangan mengklaim lulus tanpa pengukuran.

## Checklist perubahan UI

1. Apakah kontrol ini punya tujuan yang jelas dan respons state yang tepat?
2. Apakah warna, ikon, label, dan binding mewakili aksi sebenarnya?
3. Apakah ada efek, garis, badge, atau animasi yang bisa dihapus tanpa mengurangi pemahaman?
4. Apakah kontrol bekerja dengan mouse dan keyboard, serta tidak terpotong pada ukuran jendela yang didukung?
5. Apakah perubahan tetap di `apps/pixacompact/**` dan tidak menambah dependensi visual yang berat?
