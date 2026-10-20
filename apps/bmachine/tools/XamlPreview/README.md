# XamlPreview — previewer XAML headless (tanpa menjalankan app)

Render view/snipet XAML BMachine ke PNG pakai resource styles `App.axaml` **asli**
(Semi.Avalonia, `BMachineDesignSystem.axaml`, tema Dark/Light). Alternatif ringan
untuk "Live XAML preview" — tanpa build app, tanpa buka VM.

## Pakai

```bash
# dari apps/bmachine
dotnet run --project tools/XamlPreview -- --type RadialMenuWindow --out preview.png --w 400 --h 400

# nama pendek juga boleh
dotnet run --project tools/XamlPreview -- --type LogPanelSidebar --out preview.png --theme light

# file .axaml: kalau ada x:Class -> instantiate view; kalau snipet biasa -> parse jadi kontrol
dotnet run --project tools/XamlPreview -- --file src/BMachine.UI/Views/SomeView.axaml --out preview.png
```

| arg | arti |
|---|---|
| `--type <T>` | instantiate type di `BMachine.UI` (full name atau nama pendek) |
| `--file <path>` | render file axaml (x:Class → view; tanpa x:Class → snipet) |
| `--out <path>` | PNG keluaran (default `preview.png`) |
| `--w` / `--h` | ukuran window DIP (default 1000×700; diabaikan kalau akar = Window yang sudah fixed size) |
| `--theme dark\light` | `RequestedThemeVariant` (default dark) |
| `--app-axaml <path>` | override lokasi `App.axaml` (default: cari naik dari cwd) |

## Cara kerja

1. Baca `src/BMachine.App/App.axaml`, buang `x:Class`, `AvaloniaRuntimeXamlLoader.Parse<Application>` → semua styles + `Application.Resources` (theme Light/Dark, TablerIcons) ikut termuat.
2. `AppBuilder.UseHeadless(UseHeadlessDrawing=false).UseSkia().WithInterFont()` → render nyata tanpa layar.
3. `window.Show()` → `RenderTargetBitmap.Render(win)` → `Save(png)`.

## Batasan (ketauan saat dipakai)

- **Tanpa DataContext.** Binding dinamis kosong: konten dari ViewModel bisa tidak muncul
  (mis. ikon radial yang datang dari `RadialMenuViewModel`). Struktur/layout/border tetap valid.
- Bukan live-watch seperti ekstensi VS Code: jalankan ulang tiap kali XAML berubah.
- Layout "aneh" yang muncul di preview wajib dikonfirmasi dulu di app/VM (font metrics/clip bisa beda).
- Debug build; untuk kecepatan `--no-build` setelah build sekali:
  `dotnet build tools/XamlPreview && dotnet run --project tools/XamlPreview --no-build -- --type X --out p.png`