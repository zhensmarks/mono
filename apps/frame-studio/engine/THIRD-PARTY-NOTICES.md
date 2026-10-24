# Third-party notices

## .NET runtime dependency

### PsdSharp 1.2.0 — MIT

Used by `FrameStudio.Engine` to attempt PSD/PSB header, layer-record, and stored-composite reads. License: <https://github.com/kaelon141/PsdSharp/blob/main/LICENSE>. NuGet package: <https://www.nuget.org/packages/PsdSharp/1.2.0>.

PsdSharp is read-only; it is not used to edit or save layered PSD/PSB documents. PNG writing uses .NET's built-in `System.IO.Compression` APIs.

## Optional desktop-only Python sidecar

This fallback is disabled unless the host explicitly configures a local Python executable. The engine embeds its bridge script, but does not bundle Python or install Python packages. It requires Python 3.11+ and the exact `psd-tools==1.26.0` version, which is checked at runtime.

- [psd-tools 1.26.0 — MIT](https://github.com/psd-tools/psd-tools/blob/v1.26.0/LICENSE). Declared runtime dependencies: attrs, Pillow, and NumPy. The pinned package's dependency declarations are in its [v1.26.0 project metadata](https://github.com/psd-tools/psd-tools/blob/v1.26.0/pyproject.toml).
- attrs — MIT. <https://github.com/python-attrs/attrs/blob/main/LICENSE>
- Pillow — MIT-CMU. <https://pillow.readthedocs.io/en/stable/about.html#license>
- NumPy — BSD-3-Clause. <https://numpy.org/doc/stable/license.html>
- Python interpreter — external system/runtime installation; Python Software Foundation License. <https://docs.python.org/3/license.html>

The optional `psd-tools[composite]` extra also declares aggdraw and SciPy for additional rendering operations. The POC does not install that extra or recomposite layers; it reads the PSD's stored merged image. Layer-effect rendering remains limited even when those packages are present.

## Test-only .NET dependencies

- Microsoft.NET.Test.Sdk 17.12.0 — MIT.
- xunit 2.9.3 — Apache-2.0.
- xunit.runner.visualstudio 3.0.1 — Apache-2.0.

These packages are not referenced by the runtime engine assembly. Tests create small synthetic PSD fixtures at runtime; the local integration probe uses an environment-provided file and commits no customer or proprietary PSD.
