# Folder Update BMachine (NAS kantor)

Distribusi versi baru BMachine ke PC lain di kantor **tanpa internet/GitHub**.
App membaca `manifest.json` dari server NAS, membandingkan versi, lalu menampilkan strip
"Update tersedia" di bagian atas jendela.

**Lokasi share (dipakai app):**

```
\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine
U:\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine      (kalau drive U: dimapping)
```

**Folder kerja (staging) di repo:**

```
mono\updates\
  manifest.json        <- versi terbaru + catatan rilis singkat (dibuat otomatis, gitignore)
  payload\             <- isi yang disalin ke folder instalasi (gitignore)
  admin.lock           <- hash password admin (PBKDF2-SHA256, bukan password asli)
  admin-auth.ps1       <- helper verifikasi password
  publish-update.ps1   <- terbitkan versi baru + kirim ke NAS (minta password)
  lock-folder.ps1      <- kunci folder: user hanya baca, admin yang bisa tulis
  unlock-folder.ps1    <- buka kunci sementara (minta password)
  share-folder.ps1     <- (opsional) bagikan folder lewat PC ini kalau tidak pakai NAS
  notes.txt            <- catatan rilis berikutnya (satu poin per baris)
  notes.example.txt
  README.md
```

## 1. Terbitkan versi baru

Jalankan di **console PowerShell biasa** (bukan dari skrip otomatis, karena akan diminta password):

```powershell
cd "D:\#DATA ABENG\#PROJECT\mono\updates"
powershell -ExecutionPolicy Bypass -File publish-update.ps1 -Version 15.4.4
```

Script akan:

1. Minta **password admin** (dicek ke `admin.lock`).
2. Menulis `<Version>` baru ke `BMachine.App.csproj`.
3. Publish single-file ke `mono\BMachine`.
4. Menyusun `payload\` (exe + Assets + Scripts\Master + Scripts\Action). Data user
   (`Scripts\Others`, `Scripts\Output`, `Scripts\Temp`, `plugins`) **tidak** ikut.
5. Menulis `manifest.json` + sha256.
6. Menyalin payload ke NAS, **manifest paling akhir** supaya user tidak melihat versi baru
   sebelum payload-nya lengkap.

Catatan rilis diambil dari `notes.txt` (satu poin per baris), atau langsung:

```powershell
publish-update.ps1 -Version 15.4.4 -Notes "Poin satu","Poin dua"
```

Aturan catatan: maksimal 6 poin, tiap poin <= 90 karakter (otomatis dipotong + peringatan).

## 2. Kunci folder (hanya admin yang bisa publish)

```powershell
# jalankan sebagai Administrator, akan minta password admin
powershell -ExecutionPolicy Bypass -File lock-folder.ps1                  # lokal + NAS
powershell -ExecutionPolicy Bypass -File lock-folder.ps1 -Target local     # hanya staging
powershell -ExecutionPolicy Bypass -File lock-folder.ps1 -DryRun           # lihat perintah icacls
```

Efeknya: `Everyone` hanya boleh baca, akun admin yang diberi izin tulis.

- **Lokal** (`mono\updates`) langsung dikunci.
- **NAS**: ACL hanya diubah kalau `-NasAdminAccount "DOMAIN\admin-nas"` diisi, karena akun
  admin di server NAS berbeda dengan akun Windows lokal. Tanpa parameter itu script hanya
  menampilkan perintah `icacls` untuk dijalankan manual - jadi tidak akan merusak izin NAS.

Buka kunci sementara (mis. mau publish tanpa Administrator):

```powershell
powershell -ExecutionPolicy Bypass -File unlock-folder.ps1          # user boleh tulis
powershell -ExecutionPolicy Bypass -File unlock-folder.ps1 -Reset   # balik ke izin warisan
```

Password: `admin.lock` menyimpan hash PBKDF2-SHA256 (120.000 iterasi + salt), bukan password.

## 3. Proteksi isi payload

Selain kunci folder, app **memverifikasi SHA256** payload sebelum menerapkan update:

- Manifest berisi `sha256` exe hasil publish.
- Sebelum menyalin, app hitung hash `payload\BMachine.App.exe`.
- Kalau beda (mis. ada yang menaruh exe lain di folder update) -> update **dibatalkan** dengan
  pesan "Payload tidak cocok dengan manifest (SHA256 beda)".

Jadi walau seseorang bisa menulis ke folder, exe asing tidak akan pernah dipasang.

## 4. Yang dilihat user

- Saat app dibuka, app cek manifest di NAS (fallback ke `U:\` kalau UNC tidak resolve).
- Kalau versi di manifest lebih baru -> **strip biru di atas jendela**:
  `Update tersedia vX.Y.Z` + poin catatan singkat + tombol **Update sekarang** / **Nanti** / **X**.
- Strip ini berada di barisnya sendiri (tidak menutupi tombol lain di panel log).
- **Update sekarang**: app menutup diri, menyalin payload ke folder instalasi, lalu membuka app lagi.
- **Nanti**: strip hilang dan versi itu tidak diganggu lagi sampai ada versi lebih baru.
- Cek manual: **Settings -> About -> Check for updates**.

## 5. Kalau update gagal

- Log: `%TEMP%\bmachine-update.log`
- Exe lama dibackup otomatis jadi `BMachine.App.exe.bak` di folder instalasi.
- Kalau penyalinan gagal (mis. exe sedang dipakai), script memulihkan exe lama dan tetap
  membuka app; pesan error tampil di strip/Settings.

## 6. Path di sisi user

Default NAS di atas. Bisa diubah per PC di **Settings -> About -> Folder update**
(disimpan di DB sebagai `Update.LocalPath`). Override sementara: env var `BMACHINE_UPDATE_DIR`.

## 7. Distribusi pertama tetap manual

App versi lama (sebelum fitur ini) belum bisa membaca `manifest.json`, jadi **sekali** harus
dipasang manual di tiap PC: ambil `payload\BMachine.App.exe` (versi terbaru) dan timpa
`BMachine.App.exe` di folder instalasi masing-masing. Setelah itu semua update lewat strip.

## 8. Versi

Versi dibaca dari `<Version>` di `apps\bmachine\src\BMachine.App\BMachine.App.csproj` dan
otomatis diperbarui oleh `publish-update.ps1`. Rilis terakhir: **15.4.3**.
