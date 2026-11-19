# PixaCompact — Rust rewrite

Rewrite Rust dari app Avalonia (`apps/pixacompact`, C# -> `PixelcutCompact.exe`
v1.1.2). App C# tetap di tempatnya sebagai referensi perilaku; kode Rust di
folder ini adalah app baru dengan bentuk yang sama: **dashboard + grid gambar +
preview window dengan panel editor**, dan hapus background.

## Jalankan

```bash
cd apps/pixacompact/rust
cargo run -p pxa-app --release                    # dashboard (toolchain GNU)
cargo run -p pxa-app -- gambar1.png gambar2.png   # langsung masuk daftar
cargo run -p pxa-app -- --editor hasil.png        # langsung buka preview editor
cargo run -p pxa-app --bin pxa-matte -- in.png out.png [model.onnx]  # CLI engine
cargo test -p pxa-core                            # 26 test engine
```

Shortcut dashboard: **F5** = Remove BG, **F2** = preview item pertama,
**Esc** = tutup overlay. Drag-drop file/folder ke jendela juga didukung.

Toolchain dipin ke `stable-x86_64-pc-windows-gnu` karena mesin ini tidak punya
MSVC linker (PATH `link` ketemu coreutils -> `link: extra operand`).

### Engine ONNX (hapus background lokal)

- Model: `Resources/AiModels/modnet.onnx` (MODNet, sudah ada di repo C#).
  Dicari otomatis di `exe_dir/Resources/AiModels`, `../...`, atau env `PIXA_MODEL`.
- ONNX Runtime di-**dlopen** (fitur `load-dynamic` crate `ort`), jadi tidak butuh
  linker MSVC. DLL dicari: env `ORT_DYLIB_PATH` -> `onnxruntime.dll` di folder exe.
  `ort 2.0.0-rc.15` butuh ORT **>= 1.31**; DLL 1.20 bawaan C# ditolak. Taruh
  `onnxruntime.dll` 1.31+ di folder exe (sudah disalin ke `target/debug` &
  `target/release` saat verifikasi; sumber: NuGet `Microsoft.ML.OnnxRuntime` 1.31.1).
- Preprocess/postprocess port 1:1 dari `MattingOnnxService.cs` (shortest-edge 512,
  kelipatan 32, mean/std 0.5, bilinear, output sigmoid sudah 0..1 -> tanpa min-max).

## Sudah port & terverifikasi jalan

- Engine editor 1:1 (`PixelBuffer`, `MaskOperations`, `SelectionState`+kontur
  marching-squares, `MaskRasterizer` supersample 4x, `BrushTool`, `CompositeRenderer`,
  magic wand) -- **unit test pass**.
- **Hapus background lokal (MODNet ONNX)**: batch multi-gambar, status per item,
  cancel, tulis `<nama>-cutout.png`. Preprocess/postprocess port 1:1 dari
  `MattingOnnxService.cs`.
- **Dashboard** mirip app asli + **preview editor** (kanvas, tool, brush, seleksi,
  mask, undo/redo, ekspor). ID konflik egui sudah dibereskan.
- **Model dokumen & layer** (`Document/*`): 16 blend mode (Multiply/Screen/Overlay/
  SoftLight/...), compositor offset+visibility, `DocumentHistory` undo/redo,
  `ImageLayer` (opacity/mask/transform). Port `Services/Editing/Document/*.cs`.
- **Refine Hair** (`RefineHairService` + `TrimapBuilder`): trimap 0/128/255 dari
  mask, inferensi ONNX, blend alpha HANYA di band unknown + feather transisi.
  Tombol "Refine Hair" di editor.
- **BiRefNet spec** (`MattingSpec::birefnet`) -- tinggal tambah file model 1024x1024.
- **Output provenance** (`OutputProvenance`): `output-index.json`, skip file yang
  masih valid (input tak berubah), catat setelah proses. Terpasang di batch loop.
- **Engine web** (PIXA/BG Eraser/NOBG) -- orkestrasi diport ke Rust
  (`webengine`: rotasi akun round-robin, kuota, sub-batch, proxy, deteksi limit,
  PNG-alpha picking). Driver browser = Node+Playwright di `webdriver/driver.js`
  (engine yang sama dengan C#). Terbukti jalan end-to-end: buka pixelcut.ai,
  upload, klik Remove Background, download hasil.

## Struktur

| Crate / modul | Isi | Asal C# |
| --- | --- | --- |
| `pxa-core` | buffer RGBA, mask, seleksi+kontur, rasterizer, brush, komposit, magic wand, **dokumen/layer** | `Services/Editing/*.cs`, `Document/*.cs` |
| `pxa-app::matting` | engine ONNX MODNet + BiRefNet (preprocess -> inferensi -> alpha) | `Services/Ai/MattingOnnxService.cs`, `OnnxModelManager.cs` |
| `pxa-app::trimap` + `refine_hair` | trimap + orkestrasi refine hair | `Services/Ai/TrimapBuilder.cs`, `RefineHairService.cs` |
| `pxa-app::provenance` | indeks output + skip | `Services/OutputProvenance.cs` |
| `pxa-app::webengine` | orkestrasi engine web (rotor akun, kuota, proxy, limit) | `Services/PixelcutService.cs`, `PixaAccountRotator.cs` |
| `webdriver/driver.js` | driver browser Node+Playwright (upload/klik/download) | `PixaWebAutomationService.cs` dkk |
| `pxa-app::dashboard` | dashboard + batch + engine selector (lokal & web) | `Views/MainWindow.axaml`, `MainWindowViewModel.cs` |
| `pxa-app::editor` | preview window: kanvas, tool, brush/seleksi/mask, undo/redo, Refine Hair, ekspor | `Views/PreviewWindow*.cs` |

## Engine web (Node+Playwright)

```bash
# sekali saja: staging node_modules + chromium
cd webdriver && npm install playwright && npx playwright install chromium
# atau manual: letakkan folder `target/debug/pixa-webdriver` (driver.js + node_modules)
```

Dashboard mencari driver di `exe_dir/pixa-webdriver/driver.js`, fallback ke
`rust/webdriver/driver.js`. Node harus ada di PATH. Hasil engine web ditulis
ke `Output/<nama>-cutout.png` seperti engine lokal.

> Catatan: engine web butuh akun pixelcut dengan kredit; untuk foto nyata hasil
> alpha benar. Gambar sintetis uji sering kembali RGB (tanpa potong) di tier gratis.

## Sudah diport — parity hampir penuh

- **Panel Layers** di editor: daftar layer (Latar/Cutout), visibility, opacity,
  blend mode (16), tambah/hapus layer solid, composite live ke preview & ekspor.
  Dipakai `pxa-core::document`. Toggle `use_layers` / flag CLI `--layers`.
- **Akun + proxy UI** (Pengaturan → Akun & Browser): daftar akun round-robin
  (nama + limit/sesi) + daftar proxy Mix. Diteruskan ke driver web via env
  (`PX_WEB_ACCOUNTS`, `PX_WEB_PROXY`); profil browser per akun. Terbukti: profil
  bernama `.pwprofile-pixa-<akun>` dibuat sesuai akun terpilih.

- **Login interaktif per akun**: tombol `login` per akun → driver mode `login`
  (browser tampil, user login manual, sesi tersimpan di profil) → dipakai
  engine web headless berikutnya. Rotasi akun fungsional.
- **Layer: Duplikat + Gabung (Merge Down)** di panel Layers; `duplicate_active`
  + `merge_down` di `pxa-core::document` (composite penuh → layer bawah).
- **Update LAN** (`updater` port `LocalUpdateService`+`UpdateInfo`): baca
  `manifest.json`/`changelog.json`, bandingkan versi, terapkan via helper
  PowerShell. Tab Pembaruan di Pengaturan. E2E: manifest v9.9.9 terdeteksi.
- **Docking panel** (`PreviewWindow.Docking`): menu `Window ▾` di editor —
  per-panel pilih sisi dock (Atas/Kiri/Kanan/Bawah; Tools hanya kiri/kanan),
  tampil/sembunyi, reset layout.
- **Laporan bug** (`bugreport` port `Abeng.BugReporter`): kirim ke ProjectBot
  bug bridge `http://127.0.0.1:21478/bug`, fallback file JSON
  `%LOCALAPPDATA%\BMachine\bug-reports` kalau bridge mati; handler crash di
  `main` → `report_crash` (log `crash.log` + kirim). Tombol "Laporkan bug" di
  footer dashboard.
- **Floating panel** (`FloatPanel`): panel bisa dicabut jadi jendela OS melayang
  via `show_viewport_immediate` (jendela native terpisah, bisa digeser/resize/
  tutup). Menu `Window ▾` → centang "melayang" per panel; flag CLI
  `--float-tools`. Terbukti: jendela native `PX Tools (melayang)` 256x399
  muncul terpisah. Catatan: isi jendela = snapshot read-only; pada beberapa
  build eframe konten child-viewport belum ter-composit (jendela + judul +
  drag jalan, isi bisa tampak kosong sampai backend eframe multi-viewport
  penuh). Edit panel tetap di panel dock.

- **Properties + History panel** (`PanelProperties`/`PanelHistory`): panel
  Properties (seksi Selection: All/None/Invert, Grow/Shrink/Feather; seksi Mask:
  Erase/Restore/Fill, Expand/Contract/Feather mask, Defringe) + panel History
  (Undo/Redo stack). Reuse operasi seleksi/mask yang sudah ada di `Doc`.
  Terintegrasi dock + floating (menu `Window ▾`).

## Sisa (hampir beres)

1. **Konten floating-panel** — jendela melayang sudah spawn (terbukti),
   tapi isi snapshot read-only belum ter-composit oleh backend eframe
   multi-viewport di build ini; perbaiki dengan `set_immediate_viewport_renderer`
   (eframe) atau pindah ke `winit` multi-window eksplisit.
2. **Konten Properties/History di jendela melayang** — panel sudah ada &
   ter-dock; jika di-floating, isi jendela OS masih bergantung composit
   child-viewport (lihat item 1).

## Catatan desain

- Falloff brush & feather memakai math identik C# (`+0.5` lalu clamp, truncation
  integer pada box blur) supaya hasil byte-identik.
- Format internal RGBA8 non-premultiplied; konversi channel hanya di batas I/O.
- Token warna mengikuti `DESIGN_SYSTEM.md` (bg `#121212`, card `#1E1E1E`,
  accent `#3B82F6`, state green/red/blue).
- Verifikasi UI pakai capture layar (PrintWindow), tanpa menggerakkan mouse/keyboard
  user; state dibuka lewat flag CLI / shortcut.
- rust-anything tak dipakai untuk verifikasi runtime C# (belum ada runtime C#);
  `rust/MIGRATION-PLAN.md` = output `rust-anything analyze --lang csharp --whole-app`.
