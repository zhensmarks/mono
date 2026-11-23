# AGENTS.md — BMachine.v2

BMachine.v2 is the primary project. It is independent; do not mirror or sync changes to `BDater/`.

## Focus

Current feature focus: `src/BMachine.UI/Models/MantraData`. Check the workspace-level instructions in `../AGENTS.md` before making changes.

## Code style

- C# 12, .NET 8, Avalonia 11.
- Use MVVM and CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`).
- Keep ViewModels focused on state and commands. Put visual structure in `.axaml` views and keep code-behind limited to view-specific interaction.

## Design system

Use the shared design system as the source of truth; do not copy old one-off values into new views.

- Shared control styles and sizing tokens: `src/BMachine.UI/Styles/BMachineDesignSystem.axaml`.
- Theme colors and semantic brushes: `src/BMachine.UI/Themes/DarkTheme.axaml` and `LightTheme.axaml`.
- Navigation and tab styles: `src/BMachine.UI/Styles/TrelloNavigation.axaml`.
- Icon geometry: `src/BMachine.UI/Styles/TablerIcons.axaml`.
- Use dynamic theme resources such as `AppBackgroundBrush`, `CardBackgroundBrush`, `TextPrimaryBrush`, and the appropriate status or border brush. Do not hardcode colors in views. When a new semantic color is needed, define its light and dark values in both theme dictionaries and use the shared resource key.
- `SurfaceElevatedBrush` and `PrimaryBrush` are already declared in both theme dictionaries. Search the theme resources before adding or renaming a brush.
- Use the existing radius, spacing, and icon-size resources or shared control classes instead of arbitrary values where a matching token exists.
- Keep both light and dark themes usable. A color change in one theme must have an intentional counterpart in the other.
- MantraData toolbar buttons are text-only unless a feature explicitly requires an icon. Keep the formula bar monospace and do not add decorative glyphs to ViewModel text.
- In MantraData dialogs, prefer `Button.Action` for the main action, `Button.Muted` for secondary/cancel, and `Button.ChromeClose` for title-bar close. Avoid `Button.Primary` there unless a blue accent is explicitly requested; its custom template has previously caused font rendering issues.

## Build and verification

- After code changes, build and run the app from this directory:
  `dotnet run --no-restore --project src/BMachine.App/BMachine.App.csproj`
- The development run uses the normal daily database under the user's app-data folder. Do not set `BMACHINE_APPDATA_ROOT` for this check.
- Run regression tests when relevant: `dotnet test tests/BMachine.UI.RegressionTests`.
- Windows publishing is handled by `build.ps1`; `build.sh` contains macOS and Linux publishing paths. Report clearly when a build or platform-specific check could not be run.