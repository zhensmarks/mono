# Frame Studio

Frame Studio is an Avalonia photo-template workspace. The current desktop app lets you choose one PSD/PSB master, select a source folder or add several photos through the file picker, inspect the master, and choose an output folder.

## What works now

Opening a PSD calls the read-only `FrameStudio.Engine.PsdTemplateEngineAdapter` through the UI's `IPsdTemplateAdapter`. When the file provides a supported stored RGB composite, the app displays that flattened image. It also lists candidate photo, text, and logo layers inferred from layer-name tokens. Those labels are hints only: they do not prove that a layer is an editable text object, a photo mask, or a valid crop aperture.

The selected source photo can be mapped to a candidate and previewed separately in a 2:3 crop frame. Zoom, position, and rotation affect only that local preview. Text values, logo/photo assignments, and crop adjustments are not applied to the PSD or composited over the stored image.

## Export limits

The only enabled output action is **Export flattened preview PNG**. It exports the composite already stored in the PSD to a new PNG file in the chosen output folder. Slot assignments are deliberately empty and are not applied; the PSD master is not modified. This is not one output per input photo, and it is not a layered PSD export. Print sizing such as 2×3 cm or 4×6 cm is not implemented; the 2:3 photo preview ratio is a separate local crop view.

The parser exposes stored composite pixels; it does not re-render Photoshop layers, masks, blend modes, or effects. Preview is limited to supported RGB documents with 3 or 4 channels and 8-, 16-, or 32-bit channels. Other files may expose metadata without a displayable composite.

## Build and tests

From the repository root:

```sh
dotnet clean apps/frame-studio/app/FrameStudio.sln --configuration Release
dotnet build apps/frame-studio/app/FrameStudio.sln --configuration Release
dotnet test apps/frame-studio/app/FrameStudio.sln --configuration Release
```

The solution includes the UI, engine, and both test projects. The engine test project can also be run by itself with `dotnet test apps/frame-studio/engine.Tests/FrameStudio.Engine.Tests.csproj --configuration Release`.

The engine tests generate a small synthetic PSD at runtime. No genuine PSD/PSB test fixture is included in the workspace, so those tests do not establish compatibility with customer documents.

## Mobile

Mobile targets have not been built or tested. The current project targets `net8.0` and references `Avalonia.Desktop`; no mobile target or device validation is configured. A multi-file photo picker is available in the UI, but mobile file-picker behavior still needs platform implementation and testing.
