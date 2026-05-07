$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "  PixaCompact - Build Script"
Write-Host "========================================"

$projectPath = "PixelcutCompact.csproj"
$outputDir = "..\..\publish\win-x64\PixaCompact"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

Write-Host "Publishing PixaCompact..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

Write-Host "Copying Playwright drivers..."
if (Test-Path "bin\Release\net8.0\win-x64\.playwright") {
    Copy-Item -Path "bin\Release\net8.0\win-x64\.playwright\*" -Destination "$outputDir\.playwright" -Recurse -Force
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host "🎉 Build Finished Successfully! Output in $outputDir"
