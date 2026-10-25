# Frame Studio PSD Preview Engine (read-only)

## Current capability

The engine is a UI-independent .NET 8 library. Its primary reader is [PsdSharp 1.2.0](https://github.com/kaelon141/PsdSharp), a managed MIT-licensed reader. It can attempt PSD/PSB metadata, layer-tree, and stored-composite reads; support is best-effort per file. A parser error is not a diagnosis that a file is corrupt.

An optional [psd-tools 1.26.0](https://github.com/psd-tools/psd-tools/tree/v1.26.0) desktop sidecar is available as a fallback when PsdSharp fails on a file path. The Avalonia app configures it only on Linux x86_64 and only when Python is found; set `FRAME_STUDIO_PSD_TOOLS_PYTHON` to choose a specific interpreter. Other platforms are gated off. The first successful local read must verify Python 3.11+ and `psd-tools==1.26.0` with its declared dependencies. The PSD and layer data stay local; the engine embeds only the small bridge script and does not bundle Python or its packages.

The fallback returns a best-effort layer tree plus the PSD's **stored merged composite** as an RGBA preview for RGB 8-bit files that fit the preview cap. It does not recomposite the layers, so it can show a stale preview if the PSD's stored composite is stale. The app displays anonymous, generic layer labels; source layer names and text values are not included in UI summaries or status. Layer kind, visibility, opacity, bounds, mask, vector-mask, clipping, and effects flags are descriptive only. It cannot edit pixels, text, masks, or effects and cannot save a layered PSD.

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

The psd-tools allocation budget is **not** a total process-memory cap. The sidecar refuses to read unless the OS process limit can be applied to the child Python process. The primary in-process PsdSharp reader has the input-size and preview limits but is not separately memory-sandboxed; the app exposes this limitation. For untrusted files, the host application should also use an OS-level job/container limit. The exercised configuration was Linux x86_64 only. Windows and macOS hosts are unverified; this is desktop-only and makes no mobile-support claim.

## Why layered PSD export remains disabled

A disposable copy of the local 2953×4488 RGB 8-bit sample was used for writer testing; the master itself remained read-only. In a metadata-only rename round-trip, psd-tools reopened the output and retained the observed structure counts: 30 layers, 5 groups, 3 text layers, 11 masks, 1 effect-bearing layer, and the `lfxs` block. The stored composite hash matched before and after.

That result was not sufficient for editing. A second test hid one visible pixel layer in the disposable copy. The visibility change persisted after save/reopen and the same structural counts remained, but psd-tools reported that its preview update failed because rendering the sample's gradient fills required the optional SciPy dependency. Its merged-composite hash therefore remained unchanged. ImageMagick 6.9.12 independently opened both PSDs as 2953×4488 sRGB 8-bit and found zero changed pixels in the merged composite, despite the saved visibility change. This is a confirmed stale-composite failure, so `CanWriteLayeredPsd`, photo/text/logo editing, and assignment-based export remain `false`.

The reader fallback uses the original stored composite and does not run the incomplete layer renderer. A future writer must update the pixel layer and merged composite, preserve unknown tagged blocks and text/mask/effect/group data, pass independent-reader checks, and open correctly in a Photoshop-compatible editor before any write capability is enabled.

## Engine-to-UI contract

`PsdTemplateEngineAdapter.Capabilities` reports the primary PsdSharp flags and, when configured, `OptionalFallback` diagnostics. The Avalonia app:

- enables the optional fallback only on the tested Linux x86_64 desktop runtime when Python is present, and shows the Python/psd-tools requirement;
- distinguishes configured-but-unverified from a successful read and reports whether the child-process memory cap was applied;
- treats `FramePsdLayer.Kind`, `HasMask`, `HasVectorMask`, `HasClipping`, and `HasEffects` as read-only metadata, not proof that a layer can be edited;
- treat `CompositePreview` as the stored composite, not a render from the current layer visibility/tree;
- shows only anonymous, generic layer labels and read-only structural summaries;
- requires positive in-canvas bounds plus mask, vector-mask, or clipping evidence before an image candidate is exposed as placement-ready; name-only `XL`/`S` hints are not photo slots;
- keeps placement and PNG export disabled until at least one validated photo target exists; fallback preview remains read-only; and
- gates the sidecar off on unverified desktop platforms and does not advertise mobile support.

`InspectAsync` returns `Success`, a fixed safe `Message`, anonymous text/image descriptors (image descriptors require validated bounds and mask/clipping evidence), a sanitized layer-tree summary, the stored-composite PNG, `EngineCapabilities`, and `FailureKind`. Source layer names and current text values are not returned to the UI. Failure categories distinguish missing/access-denied/unsupported/parser failures; none claims the source is corrupt. `IsAvailable` means the adapter exists, not that a particular PSD will parse.

`ExportCopyAsync` rejects non-empty `SlotAssignments` before reading or creating an output folder. It also refuses to write until a validated photo target exists. When allowed, it writes only a new flattened PNG from a supported stored composite; this is not a layered output, does not use `SourceFolder`, and does not overwrite existing output paths. The Avalonia app's separate raster overlay path additionally stays gated until its slot readiness checks pass; neither path claims Photoshop Frame Tool or mask-render fidelity.

The engine intentionally does not reference the Avalonia UI assembly. The app opens a local PSD from its file picker or `--open-psd <path>` startup argument and uses the same read-only inspection contract; no network upload is involved.

## Dependencies and licensing

The .NET runtime dependency remains PsdSharp 1.2.0 (MIT). The optional sidecar requires psd-tools 1.26.0 (MIT), whose declared runtime dependencies are attrs (MIT), Pillow (MIT-CMU), and NumPy (BSD-3-Clause). Python 3.11+ is an external runtime. The optional psd-tools `composite` extra adds `aggdraw` and SciPy for more layer-effect rendering; it is not required for reading the stored composite, and installing it would not resolve psd-tools' documented rendering limitations or make the writer safe. See [third-party notices](THIRD-PARTY-NOTICES.md).

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/engine/FrameStudio.Engine.csproj -c Release
dotnet test apps/frame-studio/app/FrameStudio.sln -c Release
```

The suite builds synthetic PSD fixtures at runtime. The local master integration test is opt-in and requires both `FRAME_STUDIO_PSD_TOOLS_PYTHON` and `FRAME_STUDIO_PSD_TOOLS_SAMPLE`; it reports aggregate structure only and verifies the source hash is unchanged. No customer or proprietary PSD is committed. The synthetic Python pixel check recomputes the composite from the layer tree with `force=True, ignore_preview=True`; it does not validate the PSD's stored merged composite.

The engine tests also include `engine.Tests/Fixtures/synthetic_frame_targets.psd`, generated by `engine.Tests/test_synthetic_frame_psd_fixture.py`. It contains two embedded Smart Object targets (XL/S) with positive in-canvas bounds, user masks and clipping layers, foreground frame/artwork above them, a background below, and a native Type layer with test text, two character-style runs, and paragraph formatting. The Python baseline test verifies structure and a no-op psd-tools save/re-open: parsed text/style data and the raw Type Tool tagged-block bytes stay unchanged, and the composite recomputed from the layer tree stays unchanged outside the target rectangles. Run it with `python3 apps/frame-studio/engine.Tests/test_synthetic_frame_psd_fixture.py` (psd-tools 1.26.0 and its dependencies required). This checks only the synthetic fixture and psd-tools serialization; it does not exercise a Frame Studio writer, establish Photoshop/UXP behavior, or demonstrate compatibility with any private PSD.
