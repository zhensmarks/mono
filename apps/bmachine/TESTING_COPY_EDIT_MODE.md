# Test Copy-Edit-Sync (Buat Master mode baru)

Status: **LULUS di VM `Win10LTSC-Dev` dengan Photoshop 2020 nyata** — 2026-10-22.

## Alur (goal)

`buat_master.py` default `--mode=copy-edit`:

1. **Fase 1** — tiap script jenis (`pasfoto/manasik/wisuda/profesi_flat`) dipanggil dengan
   `--mode=copy-edit --phase=1 --manifest=<file.json>`: salin 1 template per item ke output
   (untuk diedit) + catat seluruh tujuan duplikat ke manifest. **Tidak ada duplikasi, tidak ada Photoshop.**
2. **Fase 2** — `python _photoshop_utils.py <manifest> <timeout>`: buka SEMUA template serentak di
   Photoshop (COM; pywin32 atau fallback PowerShell), tunggu tiap dokumen ditutup user
   (watcher COM `Documents` — bukan file lock), lalu copy jobs-nya: nama file = nama input,
   struktur folder cermin input (SKIP_EXISTING).

`--mode=direct` = perilaku lama (langsung duplikasi, tanpa Photoshop).

## Hasil test VM (4 jenis campur dalam 1 PILIHAN)

```
[SYNC] Manifest: 5 master edit, 10 tujuan duplikat
  → ASTRONOT.psd, TNI.psd, PFM-005.psd, MSK-001.psd, WSD-001.psd terbuka SERENTAK
  → ditutup 1 per 1: [DITUTUP] per tab + sinkron masing-masing
[SELESAI-SYNC] 5/5 master, 9 file tersalin
  (1 SKIP_EXISTING = tabrakan flatten 001.psd profesi — first-wins, sama dengan mode direct)
SUMMARY_JSON: errors 0, detected [manasik, pasfoto, profesi, wisuda]
```

Verifikasi filesystem: 14 PSD = 5 template edit + 10 tujuan (1 overlap):
`KELAS 1 B\001/002.psd`, `KELAS 4 A\003.psd`, `2. MANASIK 10RP\1/2.psd`,
`3. WISUDA 10RP\1/2.psd`, `4. PROFESI\001/002.psd`.

## Jebakan yang ditemukan & sudah ditangani (jangan diulangi)

1. **Photoshop TIDAK mengunci file PSD** saat dokumen terbuka → deteksi file-lock =
   duplikasi prematur sebelum user edit (kelihatan "LULUS" palsu, 0.0 detik).
   Solusi: watcher COM `Photoshop.Documents` (`watch_documents_closed`).
2. **Dialog modal (save prompt) membuat COM balas `Count=0`** walau dokumen terbuka →
   semua target dianggap closed palsu. Guard: `Count=0` + `ActiveDocument` masih terakses
   = modal (skip poll); `Count=0` + `ActiveDocument` throw = semua benar-benar tertutup;
   `Count>0` tapi tak bisa baca nama = query tak sehat (skip poll).
3. Fixture master harus **PSD asli** — header dummy `8BPS` ditolak Photoshop
   ("file is not compatible").
4. Nama folder fixture manasik/wisuda wajib mengandung `manasik`/`wisuda` + `10rp`/`8r`
   (seleksi lama script) — contoh: `2. MANASIK 10RP`, `3. WISUDA 10RP`.
5. Scancode Ctrl+W di VM: `1d 11 91 9d` — `11`=W; `17`=I (Ctrl+I = Invert, pernah salah
   dan merusak fixture).

## Cara test ulang di VM

- Deploy 6 script (`buat_master, pasfoto, manasik, wisuda, profesi_flat, _photoshop_utils`)
  ke guest `C:\bm\Scripts\Master\` + `setup_t4.py` (fixture 4 jenis + DB config; backup otomatis
  nilai lama ke `Z:\t4_db_backup.json`) — pola ada di `D:\VMs\_share\run_t4.bat`.
- Jalankan detached (Win32_Process.Create) log ke `Z:\t4.log`; screenshot antar langkah;
  Alt+Tab dulu supaya PS di foreground; tutup tab via Ctrl+W; baca log + `dir /s /b` output.
- **Wajib restore DB VM setelah test**: `restore_t4_db.py` (config BMachine kembali semula).
- Baca bukti akhir: buka log di Notepad guest
  (`cmd /c "start notepad C:\bm\t4.log"` via background guestcontrol).
