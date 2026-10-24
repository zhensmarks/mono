# Frame Studio PSD Engine (read-only POC)

## Current capability

The engine is a UI-independent .NET 8 library. Its primary reader is [PsdSharp 1.2.0](https://github.com/kaelon141/PsdSharp), a managed MIT-licensed reader. It can attempt PSD/PSB metadata, layer-tree, and stored-composite reads; support is best-effort per file. A parser error is not a diagnosis that a file is corrupt.

An optional [psd-tools 1.26.0](https://github.com/psd-tools/psd-tools/tree/v1.26.0) desktop sidecar is available as an explicitly configured fallback when PsdSharp fails on a file path. It is **not enabled by default**. The fallback runs a local Python process, requires Python 3.11+ and `psd-tools==1.26.0` with its declared dependencies, and does not upload the PSD or layer data. The engine assembly embeds only the small bridge script; it does not bundle Python or the Python packages.

The fallback returns a best-effort layer tree plus the PSD's **stored merged composite** as an RGBA preview for RGB 8-bit files that fit the preview cap. It does not recomposite the layers, so it can show a stale preview if the PSD's stored composite is stale. Layer kind, visibility, opacity, bounds, mask, vector-mask, and effects flags are descriptive only. Text values are deliberately omitted. It cannot edit pixels, text, masks, or effects and cannot save a layered PSD.

To opt in from a desktop host:

```csharp
var fallback = new PsdToolsDesktopFallbackReader(
    new PsdToolsDesktopFallbackOptions(pythonExecutablePath: "python3"));
var reader = new PsdDocumentReader(fallbackReader: fallback);
var adapter = new PsdTemplateEngineAdapter(reader);
```

The executable can be an absolute path or a command resolved through `PATH`. A successful sidecar read verifies the exact psd-tools version; `EngineCapabilities.OptionalFallback` reports whether the backend is configured and runtime-verified, its MIT license, runtime requirement, platform scope, and limitations. The input must be a file path; the stream-based API does not invoke this process.

## Memory and process limits

The engine rejects source files larger than 512 MiB before either reader is called by default. The optional reader also uses finite defaults: a 1 GiB psd-tools per-allocation budget; a 256 MiB maximum RGBA preview; 100,000 parsed objects and at most 32 nested containers; a 3 GiB process address-space limit where the operating system supports it; and a 120-second process timeout. The sidecar writes its manifest and raw preview only into a unique temporary directory and deletes it after the result is copied into the document model. Oversized manifests, layer trees, dimensions, and preview buffers are rejected.

The psd-tools allocation budget is **not** a total process-memory cap. The OS process limit applies only to the child Python sidecar; the primary in-process PsdSharp reader has the input-size and preview limits but is not separately memory-sandboxed. For untrusted files, the host application should also use an OS-level job/container limit. The sidecar requests an OS process limit where `resource.RLIMIT_AS` is available, and its capability warnings disclose when the limit was not applied. The exercised configuration was Linux x86_64 only. Windows and macOS hosts are unverified; this is desktop-only and makes no mobile-support claim.

## Why layered PSD export remains disabled

A disposable copy of the local 2953×4488 RGB 8-bit sample was used for writer testing; the master itself remained read-only. In a metadata-only rename round-trip, psd-tools reopened the output and retained the observed structure counts: 30 layers, 5 groups, 3 text layers, 11 masks, 1 effect-bearing layer, and the `lfxs` block. The stored composite hash matched before and after.

That result was not sufficient for editing. A second test hid one visible pixel layer in the disposable copy. The visibility change persisted after save/reopen and the same structural counts remained, but psd-tools reported that its preview update failed because rendering the sample's gradient fills required the optional SciPy dependency. Its merged-composite hash therefore remained unchanged. ImageMagick 6.9.12 independently opened both PSDs as 2953×4488 sRGB 8-bit and found zero changed pixels in the merged composite, despite the saved visibility change. This is a confirmed stale-composite failure, so `CanWriteLayeredPsd`, photo/text/logo editing, and assignment-based export remain `false`.

The reader fallback uses the original stored composite and does not run the incomplete layer renderer. A future writer must update the pixel layer and merged composite, preserve unknown tagged blocks and text/mask/effect/group data, pass independent-reader checks, and open correctly in a Photoshop-compatible editor before any write capability is enabled.

## Engine-to-UI contract

`PsdTemplateEngineAdapter.Capabilities` reports the primary PsdSharp flags and, when configured, `OptionalFallback` diagnostics. The UI should:

- leave the fallback disabled unless the desktop installation has the explicitly configured Python runtime and pinned psd-tools dependency;
- show configured-but-unverified separately from a successful read, then consult `RuntimeVerified` after use;
- treat `FramePsdLayer.Kind`, `HasMask`, `HasVectorMask`, and `HasEffects` as read-only metadata, not proof that a layer can be edited;
- treat `CompositePreview` as the stored composite, not a render from the current layer visibility/tree;
- keep layered export, photo/text/logo editing, and non-empty slot assignments disabled; and
- avoid enabling or advertising this sidecar on mobile.

`InspectAsync` returns the existing `Success`, `Message`, and name-based `Slots`, plus `EngineCapabilities` and `FailureKind`. Failure categories distinguish missing/access-denied/unsupported/parser failures; none claims the source is corrupt. `IsAvailable` means the adapter exists, not that a particular PSD will parse.

`ExportCopyAsync` rejects non-empty `SlotAssignments` before reading or creating an output folder. With no assignments it may write a new flattened PNG from a supported stored composite; this is not a layered output, does not use `SourceFolder`, and does not overwrite existing output paths.

The engine intentionally does not reference the Avalonia UI assembly. These DTO and capability additions are the integration contract; no UI menu or toolbar changes are part of this POC.

## Dependencies and licensing

The .NET runtime dependency remains PsdSharp 1.2.0 (MIT). The optional sidecar requires psd-tools 1.26.0 (MIT), whose declared runtime dependencies are attrs (MIT), Pillow (MIT-CMU), and NumPy (BSD-3-Clause). Python 3.11+ is an external runtime. The optional psd-tools `composite` extra adds `aggdraw` and SciPy for more layer-effect rendering; it is not required for reading the stored composite, and installing it would not resolve psd-tools' documented rendering limitations or make the writer safe. See [third-party notices](THIRD-PARTY-NOTICES.md).

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/engine/FrameStudio.Engine.csproj -c Release
dotnet test apps/frame-studio/engine.Tests/FrameStudio.Engine.Tests.csproj -c Release
```

The suite builds synthetic PSD fixtures at runtime. The local master integration test is opt-in and requires both `FRAME_STUDIO_PSD_TOOLS_PYTHON` and `FRAME_STUDIO_PSD_TOOLS_SAMPLE`; no customer or proprietary PSD is committed.
