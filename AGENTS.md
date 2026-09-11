# AGENTS.md — BMachine.v2

BMachine is the **source of truth** for the entire project's feature set and UI language.
All changes start here, then are mirrored to BDater.

See `../AGENTS.md` for the full sync contract and file-pair table.

## Code style

- C# 12, .NET 8, Avalonia 11
- MVVM: CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`)
- Views: `.axaml` files; code-behind in `.axaml.cs`
- ViewModels: no code-behind logic; pure data + commands

## Design language (MantraDataView.axaml)

Hybrid Minimalist Dark (Bento). Token set from `ToolboxWindow.axaml`:
- Background `#09090B`, card `#111113`, border `#232326`, text `#E8E8E6`, muted `#71717A`
- `ToolBtn`: `Background="#18181B"`, `Foreground="#E4E4E7"`, `BorderBrush="#27272A"`,
  `BorderThickness="1"`, `CornerRadius="6"`, `Height="32"`
- `ActionBtn`: `Background="#EDEDED"`, `Foreground="#111111"`, `CornerRadius="6"`,
  `Height="32"`, `FontWeight="SemiBold"`
- No icons in toolbar buttons — plain `Content="Text"` only (typographic, no glyphs)
- Formula bar: monospace `'Cascadia Code', Consolas, monospace`; no glyph prefix in ViewModel
