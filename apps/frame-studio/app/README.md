# Frame Studio

Frame Studio is a desktop workspace for PSD templates. PSD/PSB masters are opened read-only; layered PSD writing remains disabled. Photo and logo placement is enabled only when the reader supplies an exact, bounds-matched binary aperture map that the renderer can apply.

## Photo-frame prototype

The **Demo frame · sintetis** button opens a prototype separate from PSD inspection. Its synthetic artboard supports rectangular and polygon masks, local photo selection by picker or native drag-and-drop, crop/pan/zoom/rotation, and simulated layer insertion order. The artboard preview and PNG export use the same rendered PNG bytes. The output is a flattened preview copy, not an editable PSD, and the prototype does not inspect or modify a selected master.

The PSD artboard also has a separate **Frame foto** tool. A user-drawn rectangle becomes a fully opaque binary mask and uses the shared compositor for preview and a new PNG copy. This is a raster overlay only; it does not become a PSD layer. Nonrectangular, soft-edged, feathered, or antialiased manual frames remain unsupported.

## PSD inspection and masked PNG copies

PsdSharp reads PSD structure and the stored composite. On Linux x64, an optional local `psd-tools==1.26.0` sidecar can provide a narrow class of exact binary user-mask maps. A map is accepted only when dimensions match the layer bounds, the supported mask profile is binary and unfeathered, the visible foreground stack is supported, and the primary and sidecar stored composites match. Missing maps, soft edges, vector masks, clipping, effects, unsupported occlusion, and metadata-only flags fail closed.

The PSD canvas preview and separate PNG-copy export now use the same raster compositor, which applies those exact mask pixels rather than the layer rectangle. Photo transforms and logo fitting are therefore clipped to the verified aperture. Text candidates remain manual raster overlays; the original text in the stored composite is not removed or edited. Existing PSD masters are never an output path and existing PNG output files are not overwritten.

Workflow grouping heuristics describe a primary photo group, optional smaller photo placements, and a supporting background group. The sanitized heuristic exposes only counts/role hints—not source layer names—and never changes visibility, unlocks placement, or saves a PSD. The current aperture sidecar supports only simple root-level masked image layers; nested Smart Object masks, group masks, transforms, and combined stack/effect behavior are not yet verified. Group activation remains gated until mode-specific renders can be compared against trusted output and mask/effect round-trip checks.

Automatic multi-layer replacement workflows remain disabled until their supported structure, mask coverage, and rendered output are verified. Manual rectangles create overlays only in separate PNG copies; they do not edit the PSD.

## PSD writing status

The app has no layered PSD/PSB writer. A separate raw PhotoCraft spike succeeded only on a small synthetic raster-layer fixture; it did not verify variable-length text, Smart Objects, group masks, effects, or complete workflow fidelity. PSD saving must stay disabled until disposable-copy save/reopen testing verifies layer records, masks, effects, previews, and unchanged pixels outside the intended aperture for a supported workflow.

## Compatibility and safety

- The optional reader is bounded by input, allocation, preview, layer/depth, process-memory, and timeout limits. Opt-in reader tests use synthetic fixtures only.
- The app treats PSD/PSB inputs as read-only and writes rendered results only as separate PNG copies. Synthetic test fixtures contain no user-provided master or photo.
- Rendering is capped at 30 million pixels per document. Unsupported Photoshop structures remain locked rather than being guessed from layer names or bounds.
- The app targets `net8.0`; compiling with Avalonia 12.1 analyzers requires the .NET 10 SDK. The app and test host use the .NET 8 runtime.

## Build and test

From the repository root:

```sh
dotnet build apps/frame-studio/app/FrameStudio.sln --configuration Release
dotnet test apps/frame-studio/app/FrameStudio.sln --configuration Release
```

On Linux x64 with Python 3.11+ and `psd-tools==1.26.0`, opt in to the synthetic aperture integration tests:

```sh
FRAME_STUDIO_PSD_TOOLS_MASK_TESTS=1 dotnet test apps/frame-studio/app/FrameStudio.sln --configuration Release
```

## Mobile

Mobile targets have not been built or tested. The current application uses `Avalonia.Desktop`.
