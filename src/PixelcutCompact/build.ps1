$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "  PixelcutCompact - Build Script"
Write-Host "========================================"

$projectPath = "PixelcutCompact.csproj"
$outputDir = "..\..\publish\win-x64\PixelcutCompact"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

Write-Host "Publishing PixelcutCompact..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host "🎉 Build Finished Successfully! Output in $outputDir"
