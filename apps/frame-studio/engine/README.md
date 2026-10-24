# Frame Studio PSD Engine (proof of concept)

This .NET 8 class library reads PSD/PSB document metadata and layer structure, returns conservative photo/text/logo slot candidates with estimated layer rectangles, and exposes the file's stored merged composite. It is UI-framework independent and does not reference Avalonia or the UI assembly.

## Safe output decision

The runtime dependency, PsdSharp 1.2.0, is a reader and does not write or edit PSD files. The engine does **not** claim to alter text layers, insert source photos into editable layers, or save a layered PSD. It can export only the existing merged composite to a new, flattened PNG. The output API uses `FileMode.CreateNew`, so an existing file is not overwritten; the input PSD/PSB is left unchanged.

The composite is the merged image already stored in the PSD, not a re-render of the layer tree. It cannot reflect text/photo/position/rotation/crop edits. `CanSaveLayeredPsd` is always `false` in this POC.

## Reader and slot candidates

`PsdDocumentReader.Read(path)` or `Read(stream, sourceName)` returns document dimensions, channel count, bit depth, color mode, a nested layer tree with names/visibility/opacity/document-space bounds, warnings, and conservative photo-slot candidates. Candidate detection is only a naming hint: standalone `XL` or `S`, or names containing `Photo`, `Foto`, `Portrait`, or `Slot` (optionally with `XL` or `S`). The engine adapter additionally guesses photo/text/logo types from layer-name tokens. The layer reader does not inspect Photoshop text objects or prove that a layer is a photo mask. A reported rectangle is the layer rectangle, not a validated crop aperture.

The stored composite is exposed as RGBA pixels by `FrameCompositePreview.RgbaPixels`. During adapter inspection it is encoded as PNG bytes in `FramePsdTemplateInspection.FlattenedPreviewPng`, along with `DocumentWidth` and `DocumentHeight`, so the UI can display the actual merged composite without recompositing layers.

## Async adapter matching the UI workflow

`PsdTemplateEngineAdapter` exposes:

```csharp
public bool IsAvailable { get; }
Task<FramePsdTemplateInspection> InspectAsync(
    string masterPath,
    CancellationToken cancellationToken = default);
Task<FramePsdExportResult> ExportCopyAsync(
    FramePsdExportRequest request,
    CancellationToken cancellationToken = default);
```

The UI's `IPsdTemplateAdapter` is implemented by `FrameStudio.App.Services.PsdTemplateEngineUiAdapter`. That UI-side bridge maps `FramePsdTemplateInspection` and slot enums to UI DTOs while leaving the engine framework-independent. `CurrentValue` remains null because editable PSD text values are not read. Candidate types are inferred from layer names only.

`ExportCopyAsync` rejects every non-empty `SlotAssignments` dictionary rather than silently discarding edits. With an empty dictionary, it writes a new flattened PNG named `<master>-flattened.png` in `OutputFolder` (adding a numeric suffix when necessary). `SourceFolder` is carried through the request but unused by this engine method. The Avalonia UI has a separate raster canvas renderer that can draw local assets into estimated layer rectangles; those placements are not PSD-layer edits, and they do not use verified Photoshop masks. No layered PSD is written.

## Format and fidelity boundaries

- PSD and PSB are read through PsdSharp.
- Preview output is deliberately limited to RGB documents with three channels (RGB) or four (RGBA) and 8-, 16-, or 32-bit source channels. Sixteen-bit samples are reduced to their high byte and 32-bit samples follow PsdSharp's conversion. RGBA allocation is capped at 256 MiB by default; callers can pass a different cap to `PsdDocumentReader`.
- Other color modes can still yield document/layer metadata, but this engine emits no preview for them. This avoids presenting PsdSharp 1.2.0's generic CMYK plane conversion as color-accurate RGB.
- PsdSharp supplies the stored composite; blend modes, masks, effects, and layer visibility are not recomposited by this engine. Parsing support and fidelity can vary for newer Photoshop features and malformed files.
- Streams must be readable and seekable. A path-based overload opens the file read-only and closes it after extraction.

## Dependency and license

The runtime dependency is [PsdSharp 1.2.0](https://www.nuget.org/packages/PsdSharp/1.2.0), a cross-platform managed C# PSD/PSB reader under the [MIT License](https://github.com/kaelon141/PsdSharp/blob/main/LICENSE). No commercial PSD dependency or image-processing package is added. PNG writing uses .NET's built-in `System.IO.Compression` APIs. Test-only package licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Tests construct a tiny layered PSD at runtime, with no customer or proprietary PSD fixture committed or available in the workspace. They verify parser and adapter mechanics, not fidelity against a genuine production PSD.

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/engine/FrameStudio.Engine.csproj --configuration Release
dotnet test apps/frame-studio/engine.Tests/FrameStudio.Engine.Tests.csproj --configuration Release
```
