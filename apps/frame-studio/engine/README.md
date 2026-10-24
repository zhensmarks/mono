# Frame Studio PSD Engine (proof of concept)

This .NET 8 class library reads PSD/PSB document metadata and layer structure, returns conservative photo-slot candidates, and exposes the file's stored merged composite as RGBA pixels. It is UI-framework independent and can be consumed by Avalonia without taking a dependency on Avalonia.

## Safe output decision

The current dependency, PsdSharp 1.2.0, is a reader and does not write or edit PSD files. This engine therefore does **not** claim to alter text layers, insert source photos into editable layers, or save a layered PSD. It exports only the existing merged composite to a new, flattened PNG. The output API uses `FileMode.CreateNew`, so an existing file is not overwritten; use a distinct `.png` path in the output folder. The input PSD/PSB is left unchanged.

The composite is the merged image already stored in the PSD, not a re-render of the layer tree. It is useful for inspection and as a flattened copy, but it cannot reflect any text/photo/position/rotation/crop edits. `CanSaveLayeredPsd` is always `false` in this POC.

## What the reader returns

`PsdDocumentReader.Read(path)` or `Read(stream, sourceName)` returns document dimensions, channel count, bit depth, color mode, a nested layer tree with names/visibility/opacity/document-space bounds, warnings, and any photo-slot candidates. Candidate detection is intentionally only a naming hint: standalone `XL` or `S`, or names containing `Photo`, `Foto`, `Portrait`, or `Slot` (optionally with `XL` or `S`). It does not inspect Photoshop text objects or prove that a layer is a photo mask. The reported rectangle is the layer rectangle, not a validated crop aperture.

For Avalonia, `CompositePreview.RgbaPixels` is row-major 8-bit RGBA data; the UI can use it to create its own bitmap. `SaveCompositePreviewPng` writes a PNG copy if a preview is available.

## Format and fidelity boundaries

- PSD and PSB are read through PsdSharp.
- The preview path is deliberately limited to RGB documents with three channels (RGB) or four (RGBA) and 8-, 16-, or 32-bit source channels. Sixteen-bit samples are reduced to their high byte and 32-bit samples follow PsdSharp's conversion. RGBA allocation is capped at 256 MiB by default; callers can pass a smaller or larger cap to `PsdDocumentReader`.
- Other color modes can still yield document/layer metadata, but this engine emits no preview for them. This avoids presenting PsdSharp 1.2.0's generic CMYK plane conversion as color-accurate RGB.
- PsdSharp supplies the stored composite; blend modes, masks, effects, and layer visibility are not recomposited by this engine. Parsing support and fidelity can vary for newer Photoshop features and malformed files.
- Streams must be readable and seekable. A path-based overload opens the file read-only and closes it after extraction.

## Dependency and license

The runtime dependency is [PsdSharp 1.2.0](https://www.nuget.org/packages/PsdSharp/1.2.0), a cross-platform managed C# PSD/PSB reader under the [MIT License](https://github.com/kaelon141/PsdSharp/blob/main/LICENSE). No commercial PSD dependency or image-processing package is added. PNG writing uses .NET's built-in `System.IO.Compression` APIs.

The test project uses Microsoft.NET.Test.Sdk, xUnit, and xunit.runner.visualstudio; tests build their own tiny layered PSD bytes at runtime, so no customer or proprietary PSD fixture is committed.

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/engine/FrameStudio.Engine.csproj
dotnet test apps/frame-studio/engine.Tests/FrameStudio.Engine.Tests.csproj
```

The tests exercise a synthetic 2×2 RGB PSD with a real layer record, name-based slot hints, exact RGBA extraction, PNG compression/pixels, and non-overwrite behavior.
