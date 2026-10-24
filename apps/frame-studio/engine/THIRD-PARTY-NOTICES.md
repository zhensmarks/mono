# Third-party notices

## Runtime dependency

### PsdSharp 1.2.0 — MIT

Used by `FrameStudio.Engine` to read PSD/PSB headers, layer records, and stored composite image data. License: <https://github.com/kaelon141/PsdSharp/blob/main/LICENSE>. NuGet package: <https://www.nuget.org/packages/PsdSharp/1.2.0>.

PsdSharp is read-only; it is not used to edit or save layered PSD/PSB documents. The runtime project has no other NuGet dependency. PNG writing uses .NET's built-in `System.IO.Compression` APIs.

### SkiaSharp 2.88.9 — MIT

Used by `FrameStudio.App` to compose raster photo/logo/text overlays over the stored composite and encode new PNG output files. License: <https://github.com/mono/SkiaSharp/blob/main/LICENSE.md>. NuGet package: <https://www.nuget.org/packages/SkiaSharp/2.88.9>.

## Test-only dependencies

- Microsoft.NET.Test.Sdk 17.12.0 — MIT.
- xunit 2.9.3 — Apache-2.0.
- xunit.runner.visualstudio 3.0.1 — Apache-2.0.

These packages are not referenced by the runtime engine assembly. Tests generate tiny PSD fixture bytes at runtime; no third-party or customer PSD fixture is committed.
