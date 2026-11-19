# Rust migration plan: pixacompact (C#)

2 packages, 81 source files (19984 lines), 13 test files, 0 UI files, grouped into 11 units.

| Ready | Caution | Blocked | Keep in source language |
| ---: | ---: | ---: | ---: |
| 5 | 6 | 0 | 0 |

> 1 files had parse errors; their units are marked partial.

## Start here

Best first ports: safe to port (readiness) and worth porting (loops and string work, tests, size).

| # | Unit | Lines | Priority | Readiness | Tests | Bridge | Main risks |
| ---: | --- | ---: | ---: | ---: | ---: | --- | --- |
| 1 | `Models/` | 332 | 61 | 66 | 1 | P/Invoke (C ABI) | String offsets and slicing (1); Callback-shaped APIs (3); Null references (3) |
| 2 | `Helpers/` | 129 | 51 | 75 | 0 | P/Invoke (C ABI) | Hash iteration order (1); Integer width and overflow (10); Async and promises (3) |
| 3 | `Services/Editing/` | 4089 | 27 | 0 | 8 | P/Invoke (C ABI) | Pointers and manual memory (11); Hash iteration order (5); String offsets and slicing (1) |
| 4 | `Services/` | 3975 | 23 | 0 | 5 | P/Invoke (C ABI) | String offsets and slicing (11); Hash iteration order (6); Async and promises (303) |

### `Models/`

332 lines in 5 files, wave 0, priority 61, readiness 66, bridge **P/Invoke (C ABI)**.

- 1 NuGet dependencies not in the crate table: check them by hand.
- Leaf: imports no other unit.
- 1 test files import it directly.
- String offsets and slicing x1: `Models/PixaAccount.cs:45`
- Callback-shaped APIs x3: `Models/PixaAccount.cs:55`, `Models/PixelcutFileItem.cs:90`, `Models/PixelcutFileItem.cs:97`
- Null references x3: `Models/PixelcutFileItem.cs:72`, `Models/PixelcutFileItem.cs:82`, `Models/PixelcutFileItem.cs:113`
- Async and promises x2: `Models/PixelcutFileItem.cs:80`, `Models/PixelcutFileItem.cs:90`
- Class inheritance x2: `Models/PixaAccount.cs:7`, `Models/PixelcutFileItem.cs:6`
- Division and modulo x2: `Models/PixelcutFileItem.cs:59`, `Models/UpdateInfo.cs:53`
- Dynamic typing x1: `Models/PixaAccount.cs:55`
- Exceptions x3: `Models/PixaAccount.cs:61`, `Models/PixelcutFileItem.cs:88`, `Models/PixelcutFileItem.cs:104`

### `Helpers/`

129 lines in 2 files, wave 0, priority 51, readiness 75, bridge **P/Invoke (C ABI)**.

- Leaf: imports no other unit.
- No tests found: write characterization tests against the original code first.
- Hash iteration order x1: `Helpers/PlaywrightStealthHelper.cs:11`
- Integer width and overflow x10: `Helpers/ExifHelper.cs:30`, `Helpers/ExifHelper.cs:41`, `Helpers/ExifHelper.cs:78`
- Async and promises x3: `Helpers/PlaywrightStealthHelper.cs:9`, `Helpers/PlaywrightStealthHelper.cs:11`, `Helpers/PlaywrightStealthHelper.cs:16`
- Dynamic typing x2: `Helpers/ExifHelper.cs:78`, `Helpers/ExifHelper.cs:78`
- Concurrency x1: `Helpers/PlaywrightStealthHelper.cs:2`
- Exceptions x2: `Helpers/ExifHelper.cs:10`, `Helpers/ExifHelper.cs:34`

### `Services/Editing/`

4089 lines in 22 files, wave 1, priority 27, readiness 0, bridge **P/Invoke (C ABI)**.

- 4 NuGet dependencies not in the crate table: check them by hand.
- 8 test files import it directly.
- Pointers and manual memory x11: `Services/Editing/EditorViewPort.cs:87`, `Services/Editing/MaskOperations.cs:301`, `Services/Editing/MaskRegion.cs:16`
- Hash iteration order x5: `Services/Editing/EditorShortcutMap.cs:112`, `Services/Editing/SelectionContour.cs:65`, `Services/Editing/SelectionContour.cs:66`
- String offsets and slicing x1: `Services/Editing/MaskEditSession.cs:437`
- Integer width and overflow x302: `Services/Editing/BrushHudMath.cs:18`, `Services/Editing/BrushStrokeAccumulator.cs:20`, `Services/Editing/BrushStrokeAccumulator.cs:20`
- Dynamic typing x178: `Services/Editing/BrushHudMath.cs:18`, `Services/Editing/BrushStrokeAccumulator.cs:20`, `Services/Editing/BrushStrokeAccumulator.cs:28`
- Null references x83: `Services/Editing/BrushTool.cs:74`, `Services/Editing/BrushTool.cs:146`, `Services/Editing/BrushTool.cs:158`
- Division and modulo x79: `Services/Editing/BrushHudMath.cs:19`, `Services/Editing/BrushStrokeAccumulator.cs:28`, `Services/Editing/BrushStrokeAccumulator.cs:29`
- Callback-shaped APIs x8: `Services/Editing/EditorShortcutMap.cs:71`, `Services/Editing/EditorShortcutMap.cs:76`, `Services/Editing/EditorShortcutMap.cs:77`
- Class inheritance x5: `Services/Editing/MaskEditSession.cs:33`, `Services/Editing/SelectionTool.cs:99`, `Services/Editing/SelectionTool.cs:144`
- Async and promises x4: `Services/Editing/SelectionIo.cs:16`, `Services/Editing/SelectionIo.cs:36`, `Services/Editing/SelectionIo.cs:43`
- Reflection and prototypes x4: `Services/Editing/EditorShortcutMap.cs:113`, `Services/Editing/EditorShortcutMap.cs:114`, `Services/Editing/SelectionState.cs:80`
- Concurrency x2: `Services/Editing/MaskEditSession.cs:2`, `Services/Editing/SelectionIo.cs:3`
- Exceptions x28: `Services/Editing/BrushStrokeAccumulator.cs:24`, `Services/Editing/BrushStrokeAccumulator.cs:25`, `Services/Editing/BrushStrokeAccumulator.cs:35`

### `Services/`

3975 lines in 16 files, wave 2, priority 23, readiness 0, bridge **P/Invoke (C ABI)**.

- 5 test files import it directly.
- String offsets and slicing x11: `Services/ExternalEditorService.cs:170`, `Services/PixelcutService.cs:139`, `Services/PixelcutService.cs:435`
- Hash iteration order x6: `Services/OutputProvenance.cs:37`, `Services/PixaAccountBackupService.cs:162`, `Services/PixaAccountBackupService.cs:190`
- Async and promises x303: `Services/BgEraserWebAutomationService.cs:29`, `Services/BgEraserWebAutomationService.cs:34`, `Services/BgEraserWebAutomationService.cs:39`
- Null references x156: `Services/BgEraserWebAutomationService.cs:22`, `Services/BgEraserWebAutomationService.cs:22`, `Services/BgEraserWebAutomationService.cs:24`
- Callback-shaped APIs x37: `Services/LocalUpdateService.cs:79`, `Services/LocalUpdateService.cs:115`, `Services/LocalUpdateService.cs:291`
- Concurrency x22: `Services/BgEraserWebAutomationService.cs:3`, `Services/BgEraserWebAutomationService.cs:4`, `Services/LocalUpdateService.cs:9`
- Dynamic typing x20: `Services/BgEraserWebAutomationService.cs:337`, `Services/BgEraserWebAutomationService.cs:337`, `Services/BgEraserWebAutomationService.cs:337`
- Reflection and prototypes x17: `Services/LocalUpdateService.cs:6`, `Services/PhotopeaLocalServer.cs:214`, `Services/PhotopeaLocalServer.cs:363`
- Integer width and overflow x9: `Services/BgEraserWebAutomationService.cs:329`, `Services/BgEraserWebAutomationService.cs:329`, `Services/BgEraserWebAutomationService.cs:329`
- Class inheritance x7: `Services/BgEraserWebAutomationService.cs:10`, `Services/NobgSpaceWebAutomationService.cs:10`, `Services/PhotopeaLocalServer.cs:14`
- Division and modulo x4: `Services/PixaAccountRotator.cs:60`, `Services/RembgResourceManager.cs:152`, `Services/RembgResourceManager.cs:153`
- Exceptions x302: `Services/BgEraserWebAutomationService.cs:35`, `Services/BgEraserWebAutomationService.cs:41`, `Services/BgEraserWebAutomationService.cs:45`

## Order by package

Packages come after the workspace packages they use. Inside a package, port by wave: a unit's imports are in earlier waves.

### 1. PixelcutCompact (`.`, portable, 0 test files)

| Wave | Unit | Status | Lines | Readiness | Bridge | Main risks |
| ---: | --- | --- | ---: | ---: | --- | --- |
| 0 | `Program.cs` | ready | 57 | 79 | P/Invoke (C ABI) | Null references (2); Dynamic typing (1) |
| 0 | `Helpers/` | ready | 129 | 75 | P/Invoke (C ABI) | Hash iteration order (1); Integer width and overflow (10) |
| 0 | `Models/` | ready | 332 | 66 | P/Invoke (C ABI) | String offsets and slicing (1); Callback-shaped APIs (3) |
| 0 | `Converters/` | ready | 130 | 57 | P/Invoke (C ABI) | Dynamic typing (9); Class inheritance (7) |
| 0 | `Services/Editing/Document/` | caution | 406 | 18 | P/Invoke (C ABI) | String offsets and slicing (2); Integer width and overflow (48) |
| 1 | `Services/Editing/` | caution | 4089 | 0 | P/Invoke (C ABI) | Pointers and manual memory (11); Hash iteration order (5) |
| 2 | `Services/` | caution | 3975 | 0 | P/Invoke (C ABI) | String offsets and slicing (11); Hash iteration order (6) |
| 3 | `Services/Ai/` | caution | 995 | 7 | P/Invoke (C ABI) | Pointers and manual memory (3); Integer width and overflow (63) |
| 4 | `ViewModels/` | caution | 2746 | 0 | P/Invoke (C ABI) | String offsets and slicing (17); Hash iteration order (4) |
| 4 | `Views/` | caution | 7100 | 0 | P/Invoke (C ABI) | String offsets and slicing (15); Pointers and manual memory (3) |
| 5 | `App.axaml.cs` | ready | 25 | 83 | P/Invoke (C ABI) | Dynamic typing (1); Class inheritance (1) |

## NuGet dependencies

| Package | Units | Rust | Fit | Note |
| --- | ---: | --- | --- | --- |
| `Avalonia` | 7 | ? | unknown |  |
| `Avalonia.Media` | 5 | ? | unknown |  |
| `Avalonia.Platform` | 3 | ? | unknown |  |
| `Avalonia.Controls` | 2 | ? | unknown |  |
| `Avalonia.Input` | 2 | ? | unknown |  |
| `Avalonia.Markup` | 2 | ? | unknown |  |
| `Avalonia.Threading` | 2 | ? | unknown |  |
| `CommunityToolkit.Mvvm` | 2 | ? | unknown |  |
| `Abeng.BugReporter` | 1 | ? | unknown |  |
| `Avalonia.Collections` | 1 | ? | unknown |  |
| `Avalonia.Data` | 1 | ? | unknown |  |
| `Avalonia.Interactivity` | 1 | ? | unknown |  |
| `Avalonia.Layout` | 1 | ? | unknown |  |
| `Avalonia.Styling` | 1 | ? | unknown |  |
| `Avalonia.VisualTree` | 1 | ? | unknown |  |

## Risk guide

- **String offsets and slicing** (High): JS indexes strings in UTF-16 code units, Rust in UTF-8 bytes. Offsets that cross the boundary must be converted, or non-ASCII text gets wrong ranges silently.
- **Dynamic typing** (Medium): Values whose shape is checked at runtime need an explicit Rust enum or `serde_json::Value`.
- **Exceptions** (Low): Thrown errors become `Result`. Errors crossing the bridge must map back to the same JS error shape.
- **Async and promises** (Medium): Promises become futures. Bridge with napi-rs `async fn` or `wasm-bindgen-futures`, or keep the async shell in JS and port the sync core.
- **Generators** (Medium): Lazy generators become iterators, or return a collected Vec across the bridge.
- **Class inheritance** (Medium): Class hierarchies become traits plus composition; `instanceof` checks become enums.
- **Reflection and prototypes** (Medium): Proxy, Reflect, Symbol and prototype tricks need a redesign, not a translation.
- **Callback-shaped APIs** (Medium): Function-typed parameters and methods cross the bridge on every call. Keep boundaries coarse: pass data in, get data out.
- **Number semantics** (Low): JS numbers are f64; bitwise ops truncate to i32; parseInt is lenient. Pick Rust integer types deliberately.
- **Integer width and overflow** (Medium): Python ints never overflow; C, Go and Java ints wrap or are undefined on overflow. Pick i64, i128 or a bignum crate deliberately, and use wrapping_* where the source wraps.
- **Division and modulo** (Medium): Python `//` and `%` floor toward negative infinity; Rust `/` and `%` truncate: use `div_euclid`/`rem_euclid` or explicit floor math. C division by zero is undefined.
- **Hash iteration order** (High): Set and map iteration order comes from the source language's hash function. A port cannot reproduce it: sort before output, or make the order part of the contract.
- **Pointers and manual memory** (High): Pointer arithmetic and manual memory become slices, indices and owned types. Code that exposes pointers across the boundary needs `unsafe`, isolated and explained.
- **Concurrency** (Medium): Threads, goroutines and locks need a design in Rust (channels, Send/Sync), not a line-by-line translation; keep concurrency in the caller where possible.
- **Null references** (Medium): Any reference may be null in the source: decide per parameter whether it is `Option` and what null means, instead of mapping every value to `Option`.

## Whole app

Shape: **server**.

| Entry point | Kind | Found from |
| --- | --- | --- |
| `Program.cs` | cli | `static void Main(` at Program.cs:17 |
| `Services/PhotopeaLocalServer.cs` | server | `HttpListener(` at Services/PhotopeaLocalServer.cs:65 |

### Every dependency

| Dependency | Declared | Used by | Rust | Fit | Note |
| --- | --- | --- | --- | --- | --- |
| `Avalonia` | yes | 7 | ? | unknown |  |
| `Avalonia.Media` | no | 5 | ? | unknown |  |
| `Avalonia.Platform` | no | 3 | ? | unknown |  |
| `Avalonia.Controls` | no | 2 | ? | unknown |  |
| `Avalonia.Input` | no | 2 | ? | unknown |  |
| `Avalonia.Markup` | no | 2 | ? | unknown |  |
| `Avalonia.Threading` | no | 2 | ? | unknown |  |
| `CommunityToolkit.Mvvm` | yes | 2 | ? | unknown |  |
| `Abeng.BugReporter` | no | 1 | ? | unknown |  |
| `Avalonia.Collections` | no | 1 | ? | unknown |  |
| `Avalonia.Data` | no | 1 | ? | unknown |  |
| `Avalonia.Interactivity` | no | 1 | ? | unknown |  |
| `Avalonia.Layout` | no | 1 | ? | unknown |  |
| `Avalonia.Styling` | no | 1 | ? | unknown |  |
| `Avalonia.VisualTree` | no | 1 | ? | unknown |  |
| `Avalonia.Controls.WebView` | yes | 0 | ? | unknown |  |
| `Avalonia.Desktop` | yes | 0 | ? | unknown |  |
| `Avalonia.Fonts.Inter` | yes | 0 | ? | unknown |  |
| `Avalonia.Themes.Fluent` | yes | 0 | ? | unknown |  |
| `Microsoft.ML.OnnxRuntime` | yes | 0 | ? | unknown |  |
| `Microsoft.Playwright` | yes | 0 | ? | unknown |  |

### Pieces, in port order

- wave 0: `Converters/` (130 lines)
- wave 0: `Helpers/` (129 lines)
- wave 0: `Models/` (332 lines)
- wave 0: `Program.cs` (57 lines)
- wave 0: `Services/Editing/Document/` (406 lines)
- wave 1: `Services/Editing/` (4089 lines)
- wave 2: `Services/` (3975 lines)
- wave 3: `Services/Ai/` (995 lines)
- wave 4: `ViewModels/`, `Views/`, tangled: one piece (9846 lines)
- wave 5: `App.axaml.cs` (25 lines)

### Decisions for the user

**D1** (shape): The app is a server, entered through Program.cs, Services/PhotopeaLocalServer.cs. Port every entry point, in that shape?

> Yes: an axum front door (`app front-door`) takes the server over first

**D2** (dependency Avalonia): Which crate replaces Avalonia (used by 7 units)?

> Research it against primary sources into rust/research/Avalonia.md before porting what uses it

**D3** (dependency Avalonia.Media): Which crate replaces Avalonia.Media (used by 5 units)?

> Research it against primary sources into rust/research/Avalonia.Media.md before porting what uses it

**D4** (dependency Avalonia.Platform): Which crate replaces Avalonia.Platform (used by 3 units)?

> Research it against primary sources into rust/research/Avalonia.Platform.md before porting what uses it

**D5** (dependency Avalonia.Controls): Which crate replaces Avalonia.Controls (used by 2 units)?

> Research it against primary sources into rust/research/Avalonia.Controls.md before porting what uses it

**D6** (dependency Avalonia.Input): Which crate replaces Avalonia.Input (used by 2 units)?

> Research it against primary sources into rust/research/Avalonia.Input.md before porting what uses it

**D7** (dependency Avalonia.Markup): Which crate replaces Avalonia.Markup (used by 2 units)?

> Research it against primary sources into rust/research/Avalonia.Markup.md before porting what uses it

**D8** (dependency Avalonia.Threading): Which crate replaces Avalonia.Threading (used by 2 units)?

> Research it against primary sources into rust/research/Avalonia.Threading.md before porting what uses it

**D9** (dependency CommunityToolkit.Mvvm): Which crate replaces CommunityToolkit.Mvvm (used by 2 units)?

> Research it against primary sources into rust/research/CommunityToolkit.Mvvm.md before porting what uses it

**D10** (dependency Abeng.BugReporter): Which crate replaces Abeng.BugReporter (used by 1 unit)?

> Research it against primary sources into rust/research/Abeng.BugReporter.md before porting what uses it

**D11** (dependency Avalonia.Collections): Which crate replaces Avalonia.Collections (used by 1 unit)?

> Research it against primary sources into rust/research/Avalonia.Collections.md before porting what uses it

**D12** (dependency Avalonia.Data): Which crate replaces Avalonia.Data (used by 1 unit)?

> Research it against primary sources into rust/research/Avalonia.Data.md before porting what uses it

**D13** (dependency Avalonia.Interactivity): Which crate replaces Avalonia.Interactivity (used by 1 unit)?

> Research it against primary sources into rust/research/Avalonia.Interactivity.md before porting what uses it

**D14** (dependency Avalonia.Layout): Which crate replaces Avalonia.Layout (used by 1 unit)?

> Research it against primary sources into rust/research/Avalonia.Layout.md before porting what uses it

**D15** (dependency Avalonia.Styling): Which crate replaces Avalonia.Styling (used by 1 unit)?

> Research it against primary sources into rust/research/Avalonia.Styling.md before porting what uses it

**D16** (dependency Avalonia.VisualTree): Which crate replaces Avalonia.VisualTree (used by 1 unit)?

> Research it against primary sources into rust/research/Avalonia.VisualTree.md before porting what uses it

**D17** (dependency Avalonia.Controls.WebView): Which crate replaces Avalonia.Controls.WebView (declared, no unit imports it)?

> Research it against primary sources into rust/research/Avalonia.Controls.WebView.md before porting what uses it

**D18** (dependency Avalonia.Desktop): Which crate replaces Avalonia.Desktop (declared, no unit imports it)?

> Research it against primary sources into rust/research/Avalonia.Desktop.md before porting what uses it

**D19** (dependency Avalonia.Fonts.Inter): Which crate replaces Avalonia.Fonts.Inter (declared, no unit imports it)?

> Research it against primary sources into rust/research/Avalonia.Fonts.Inter.md before porting what uses it

**D20** (dependency Avalonia.Themes.Fluent): Which crate replaces Avalonia.Themes.Fluent (declared, no unit imports it)?

> Research it against primary sources into rust/research/Avalonia.Themes.Fluent.md before porting what uses it

**D21** (dependency Microsoft.ML.OnnxRuntime): Which crate replaces Microsoft.ML.OnnxRuntime (declared, no unit imports it)?

> Research it against primary sources into rust/research/Microsoft.ML.OnnxRuntime.md before porting what uses it

**D22** (dependency Microsoft.Playwright): Which crate replaces Microsoft.Playwright (declared, no unit imports it)?

> Research it against primary sources into rust/research/Microsoft.Playwright.md before porting what uses it

**D23** (tangled units): ViewModels/, Views/ import each other (9846 lines). Port them as one piece?

> Yes: one piece in wave 4, no glue between its parts

