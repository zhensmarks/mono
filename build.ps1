$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "  BMachine v2 - Build & Publish Script"
Write-Host "========================================"

# Cleanup
Write-Host "[0/4] Cleaning up log files..."
Remove-Item -Path "build_log*.txt" -ErrorAction SilentlyContinue
Write-Host "[OK] Cleanup completed."

# Publish
Write-Host "[1/4] Publishing application..."
$projectPath = "src\BMachine.App\BMachine.App.csproj"
$outputDir = "publish\win-x64"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host "🎉 Build Finished Successfully! Output in $outputDir"
