$ErrorActionPreference = "Stop"

Write-Host "================================"
Write-Host "  BMachine - Main App Build"
Write-Host "================================"

# Cleanup
Write-Host "[0/1] Cleaning up log files..."
Remove-Item -Path "build_log*.txt" -ErrorAction SilentlyContinue
Write-Host "[OK] Cleanup completed."

# Publish BMachine.App
Write-Host "[1/1] Publishing BMachine.App..."
$projectPath = "src\BMachine.App\BMachine.App.csproj"
$outputDir = "publish\win-x64\BMachine"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host "Build selesai. Output utama: publish\win-x64\BMachine"
