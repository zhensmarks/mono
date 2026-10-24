# Frame Studio PSD Engine (safe read-only POC)

## Current capability

The engine is a UI-independent .NET 8 library. Its runtime reader is [PsdSharp 1.2.0](https://github.com/kaelon141/PsdSharp), a cross-platform managed C# library under the MIT license. It can attempt PSD/PSB metadata, layer-tree, and stored-composite reads. Support is best-effort per file; a parser error is not a file-corruption diagnosis.

The engine does **not** edit photo pixels, text, or logos and cannot save a layered PSD. An empty-assignment export can only write a new flattened PNG from the composite already stored in the source, when the document is RGB with 3 or 4 channels, 8/16/32-bit depth, and the RGBA preview fits the configured memory cap. This does not apply per-source assignments or regenerate layer compositing. Existing output paths are never overwritten, and the input is opened read-only.

Slot candidates are name-based hints only. They do not prove that a layer is an editable aperture, mask, text object, or logo. `CurrentValue` remains null because editable text values are not read by this engine.

## Why this POC does not write PSD files

The bundled PsdSharp package is read-only. We evaluated other open-source candidates locally, but none passed the safety requirements for rewriting a real user's layered file:

- [psd-tools 1.26.0](https://github.com/psd-tools/psd-tools) is MIT-licensed. Its Python reader successfully read the local RGB 8-bit sample's 2953×4488 document, exposed 30 layer nodes, and produced a composite. Its documented write/edit support is limited, and it does not edit Type-layer text. It is not a runtime dependency of this C# engine.
- [PhotoshopAPI 0.9.1](https://github.com/EmilDohne/PhotoshopAPI) is BSD-3-Clause-licensed and exposes PSD/PSB layer editing, including text-layer types. On the local sample it exposed 30 layer nodes, but logged that it skipped an unrecognized `lfxs` tagged block. Its documentation also says files it writes do not contain a valid merged image. Dropping an unknown block or writing a stale/missing composite is not acceptable for a customer master.
- [ag-psd 31.0.3](https://www.npmjs.com/package/ag-psd) is MIT-licensed and supports PSD read/write, but the local probe stopped because a Canvas backend was not initialized. Its documented limits include no PSB or 16-bit writing and stale composite/layer bitmaps after visual or text-layer changes unless those are regenerated. It is not used by this C# engine.

The local sample fails in PsdSharp with `TargetInvocationException` wrapping `NotSupportedException: osType key 'ObAr' is not recognized`. The same input was readable by psd-tools and PhotoshopAPI, so that failure is specific to the tested parser path; it is not evidence that the PSD is corrupt. The master was read-only throughout evaluation.

A future writer must preserve unknown tagged blocks, update pixel-layer data and text-layer raster data, regenerate the merged composite and preview metadata, write only to a new path, and pass independent-reader plus Photopea/Photoshop compatibility checks. Until then, assignments are rejected and no layered output is claimed.

## Engine-to-UI contract

`PsdTemplateEngineAdapter.Capabilities` reports the build's honest feature flags:

- `CanAttemptPsdRead` / `CanAttemptPsbRead`: the engine can attempt these formats; they are not per-file guarantees.
- `CanInspectLayerTreeBestEffort` and `CanExportSupportedStoredCompositePng`: best-effort, subject to the format and memory limits above.
- `CanEditPhotoLayers`, `CanEditTextLayers`, `CanReplaceLogoLayers`, and `CanWriteLayeredPsd`: currently `false`.

`InspectAsync` returns the existing `Success`, `Message`, and name-based `Slots`, plus `EngineCapabilities` and `FailureKind`. `FailureKind` distinguishes `SourceNotFound`, `AccessDenied`, `UnsupportedFeature`, `ReaderCouldNotInterpretInput`, and `ReaderFailure`; none is a declaration that the source is corrupt. `IsAvailable` means the adapter is present, not that a given file will parse.

`ExportCopyAsync` rejects any non-empty `SlotAssignments` before reading or creating an output folder. With no assignments it may write `<master>-flattened.png` from the stored composite; it does not use `SourceFolder` yet. New names are selected and written with `FileMode.CreateNew`.

The engine intentionally does not reference the Avalonia UI assembly. The UI can continue mapping the existing DTO properties and read `EngineCapabilities` / `FailureKind` to disable unsupported actions and display parser-specific errors.

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/engine/FrameStudio.Engine.csproj
dotnet test apps/frame-studio/engine.Tests/FrameStudio.Engine.Tests.csproj
```

Tests construct tiny PSD fixtures at runtime; no customer or proprietary PSD is committed.
