# AGENTS.md — BMachine.v2

BMachine.v2 is the primary project. It is **independent** — do NOT mirror or sync changes
to `BDater/`. BDater is no longer coupled to BMachine.

## Focus

Current work: `src/BMachine.UI/Models/MantraData` (MantraData feature area).

See `../AGENTS.md` for the workspace-level rules.

## Code style

- C# 12, .NET 8, Avalonia 11
- MVVM: CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`)
- Views: `.axaml` files; code-behind in `.axaml.cs`
- ViewModels: no code-behind logic; pure data + commands

## Design language (MantraDataView.axaml)

Hybrid Minimalist Dark (Bento). Token set:
- Window Background: `{DynamicResource AppBackgroundBrush}` (WAJIB mengikuti pengaturan Appearance di Settings, JANGAN hardcode `#09090B` pada Window baru), card `#111113`, border `#232326`, text `#E8E8E6`, muted `#71717A`
- `ToolBtn`: `Background="#18181B"`, `Foreground="#E4E4E7"`, `BorderBrush="#27272A"`,
  `BorderThickness="1"`, `CornerRadius="6"`, `Height="32"`
- `ActionBtn`: `Background="#EDEDED"`, `Foreground="#111111"`, `CornerRadius="6"`,
  `Height="32"`, `FontWeight="SemiBold"`
- No icons in toolbar buttons — plain `Content="Text"` only (typographic, no glyphs)
- Formula bar: monospace `'Cascadia Code', Consolas, monospace`; no glyph prefix in ViewModel

## Dialog buttons (MantraData dialogs)

- `Button.Action` — light, solid `#EDEDED`, dark text; the default primary action button.
- `Button.Muted` — dark card `#1C1C1F`, light text; secondary/cancel button.
- `Button.ChromeClose` — transparent titlebar close button.
- Avoid `Button.Primary` inside MantraData dialogs unless a blue accent is explicitly requested:
  `App.axaml` defines a global `Button.Primary` with a **custom template** whose
  `ContentPresenter` does not inherit a font family reliably, which caused the "block text /
  blok pada teks" rendering issue on the Ganti Semua button.