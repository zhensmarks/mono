# Frame Studio PSD Engine (proof of concept)

This .NET 8 class library reads PSD/PSB document metadata and layer structure, returns conservative photo/text/logo slot candidates, and exposes the file's stored merged composite as RGBA pixels. It is UI-framework independent and does not reference Avalonia or the UI assembly.

## Safe output decision

The runtime dependency, PsdSharp 1.2.0, is a reader and does not write or edit PSD files. This engine therefore does **not** claim to alter text layers, insert source photos into editable layers, or save a layered PSD. It exports only the existing merged composite to a new, flattened PNG. The output API uses `FileMode.CreateNew`, so an existing file is not overwritten; the input PSD/PSB is left unchanged.

The composite is the merged image already stored in the PSD, not a re-render of the layer tree. It cannot reflect any text/photo/position/rotation/crop edits. `CanSaveLayeredPsd` is always `false` in this POC.

## Reader and slot candidates

`PsdDocumentReader.Read(path)` or `Read(stream, sourceName)` returns document dimensions, channel count, bit depth, color mode, a nested layer tree with names/visibility/opacity/document-space bounds, warnings, and photo-slot candidates. Candidate detection is only a naming hint: standalone `XL` or `S`, or names containing `Photo`, `Foto`, `Portrait`, or `Slot` (optionally with `XL` or `S`). The layer reader does not inspect Photoshop text objects or prove that a layer is a photo mask. A reported rectangle is the layer rectangle, not a validated crop aperture.

For Avalonia, `CompositePreview.RgbaPixels` is row-major 8-bit RGBA data; the UI can use it to create its own bitmap. `SaveCompositePreviewPng` writes a flattened PNG copy if a preview is available.

## Async adapter matching the UI workflow

The engine exposes a framework-independent bridge with the UI contract's operations:

```csharp
public bool IsAvailable { get; }
Task<FramePsdTemplateInspection> InspectAsync(
    string masterPath,
    CancellationToken cancellationToken = default);
Task<FramePsdExportResult> ExportCopyAsync(
    FramePsdExportRequest request,
    CancellationToken cancellationToken = default);
```

Its DTOs mirror the UI's `PsdTemplateInspection`, `PsdSlotDescriptor`, `PsdExportRequest`, and `PsdExportResult` shapes under the engine namespace: `FramePsdTemplateInspection`, `FramePsdSlotDescriptor`, `FramePsdExportRequest`, and `FramePsdExportResult`. `FramePsdSlotType` has `Photo`, `Text`, and `Logo` values. `CurrentValue` is null because editable PSD text values are not read. Photo/text/logo types are inferred from layer-name tokens only.

Because `IPsdTemplateAdapter` and its DTOs live in `FrameStudio.App.Services`, the engine deliberately does not reference that UI assembly (which would create a dependency cycle). The UI can implement its contract with this thin mapping:

```csharp
using FrameStudio.Engine;
using FrameStudio.App.Services;

public sealed class PsdTemplateAdapter : IPsdTemplateAdapter
{
    private readonly PsdTemplateEngineAdapter _engine = new();
    public bool IsAvailable => _engine.IsAvailable;

    public async Task<PsdTemplateInspection> InspectAsync(
        string masterPath, CancellationToken cancellationToken = default)
    {
        var result = await _engine.InspectAsync(masterPath, cancellationToken);
        var slots = result.Slots.Select(slot => new PsdSlotDescriptor(
            slot.Id,
            slot.DisplayName,
            slot.Type switch
            {
                FramePsdSlotType.Photo => PsdSlotType.Photo,
                FramePsdSlotType.Text => PsdSlotType.Text,
                FramePsdSlotType.Logo => PsdSlotType.Logo,
                _ => throw new ArgumentOutOfRangeException(nameof(slot.Type))
            },
            slot.LayerName,
            slot.CurrentValue)).ToArray();
        return new PsdTemplateInspection(result.Success, result.Message, slots);
    }

    public async Task<PsdExportResult> ExportCopyAsync(
        PsdExportRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _engine.ExportCopyAsync(new FramePsdExportRequest(
            request.MasterPath, request.SourceFolder, request.OutputFolder,
            request.SlotAssignments), cancellationToken);
        return new PsdExportResult(result.Success, result.Message, result.OutputPath);
    }
}
```

`ExportCopyAsync` rejects any non-empty `SlotAssignments` rather than silently discarding edits. With an empty dictionary, it writes a new flattened PNG named `<master>-flattened.png` in `OutputFolder` (adding a numeric suffix when necessary). `SourceFolder` is carried through the request but unused until photo placement is implemented. The success message explicitly says assignments were not applied and no layered PSD was written.

## Format and fidelity boundaries

- PSD and PSB are read through PsdSharp.
- Preview output is deliberately limited to RGB documents with three channels (RGB) or four (RGBA) and 8-, 16-, or 32-bit source channels. Sixteen-bit samples are reduced to their high byte and 32-bit samples follow PsdSharp's conversion. RGBA allocation is capped at 256 MiB by default; callers can pass a different cap to `PsdDocumentReader`.
- Other color modes can still yield document/layer metadata, but this engine emits no preview for them. This avoids presenting PsdSharp 1.2.0's generic CMYK plane conversion as color-accurate RGB.
- PsdSharp supplies the stored composite; blend modes, masks, effects, and layer visibility are not recomposited by this engine. Parsing support and fidelity can vary for newer Photoshop features and malformed files.
- Streams must be readable and seekable. A path-based overload opens the file read-only and closes it after extraction.

## Dependency and license

The runtime dependency is [PsdSharp 1.2.0](https://www.nuget.org/packages/PsdSharp/1.2.0), a cross-platform managed C# PSD/PSB reader under the [MIT License](https://github.com/kaelon141/PsdSharp/blob/main/LICENSE). No commercial PSD dependency or image-processing package is added. PNG writing uses .NET's built-in `System.IO.Compression` APIs. Test-only package licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Tests construct their own tiny layered PSD at runtime, with no customer or proprietary PSD fixture committed.

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/engine/FrameStudio.Engine.csproj
dotnet test apps/frame-studio/engine.Tests/FrameStudio.Engine.Tests.csproj
```
