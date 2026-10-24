# Frame Studio (UI prototype)

A desktop-first Avalonia shell for the proposed school-photo workflow. Current UI can choose a PSD/PSB path, browse a source folder or individual images, preview local image thumbnails, select example photo/text/logo slots, and exercise crop-preview controls.

## Run

```bash
dotnet run --project FrameStudio.App/FrameStudio.App.csproj
```

## Tests

```bash
dotnet test FrameStudio.Tests/FrameStudio.Tests.csproj
```

## Important prototype boundary

This branch contains UI and local photo-preview behavior only. It does **not** parse or modify PSD/PSB files, replace text or logos, create per-source PSD copies, run retouching, or export. Selecting an input PSD only records its path and reports that it has not been parsed. Export remains disabled. Source/master files are never written by this prototype.

The mobile layout is not implemented or tested in this branch. Avalonia mobile targets require a separate supported SDK/workload setup and device validation.
