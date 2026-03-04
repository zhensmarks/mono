$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "  BMachine v2 - Build & Publish Script"
Write-Host "========================================"

# Cleanup
Write-Host "[0/4] Cleaning up log files..."
Remove-Item -Path "build_log*.txt" -ErrorAction SilentlyContinue
Write-Host "[OK] Cleanup completed."

# Publish BMachine.App
Write-Host "[1/4] Publishing BMachine.App..."
$projectPath = "src\BMachine.App\BMachine.App.csproj"
$outputDir = "publish\win-x64\BMachine"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

# Publish TrelloCompact
Write-Host "[2/4] Publishing TrelloCompact..."
$trelloPath = "src\TrelloCompact\TrelloCompact.csproj"
$trelloOut = "publish\win-x64\TrelloCompact"

if (Test-Path $trelloPath) {
    dotnet publish $trelloPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $trelloOut
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host "🎉 Build Finished Successfully! Output in publish\win-x64"
