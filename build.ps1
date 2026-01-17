# build.ps1 - BMachine Build & Publish Script
# Usage: .\build.ps1

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BMachine v2 - Build & Publish Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 0. Cleanup Log Files
Write-Host "[0/4] Cleaning up log files..." -ForegroundColor Yellow
$logsRemoved = 0

# Remove log files from project root
Get-ChildItem -Path "." -Include "*.log", "*.txt" -File -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
    $logsRemoved++
}

# Remove BMachine.db (development database)
if (Test-Path "BMachine.db") {
    Remove-Item "BMachine.db" -Force -ErrorAction SilentlyContinue
    $logsRemoved++
}

# Clean bin/obj folders (optional deep clean)
# Uncomment if you want full clean:
# Remove-Item -Path "src\*\bin", "src\*\obj" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  - Removed $logsRemoved temporary files" -ForegroundColor DarkGray
Write-Host "[OK] Cleanup completed." -ForegroundColor Green
Write-Host ""

# 1. Publish Application
Write-Host "[1/4] Publishing application..." -ForegroundColor Yellow
dotnet publish src/BMachine.App/BMachine.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish failed!" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Publish completed." -ForegroundColor Green

# 2. Sync Scripts Folder
Write-Host ""
Write-Host "[2/4] Syncing Scripts folder..." -ForegroundColor Yellow

# Remove old Scripts folder in publish
if (Test-Path "publish\Scripts") {
    Remove-Item -Path "publish\Scripts" -Recurse -Force
    Write-Host "  - Removed old Scripts folder" -ForegroundColor DarkGray
}

# Copy fresh Scripts folder
Copy-Item -Path "Scripts" -Destination "publish\Scripts" -Recurse -Force
Write-Host "  - Copied Scripts folder" -ForegroundColor DarkGray
Write-Host "[OK] Scripts synced." -ForegroundColor Green

# 3. Build Summary
Write-Host ""
Write-Host "[3/3] Build Summary" -ForegroundColor Yellow
Write-Host "  - Executable: BMachine.App.exe" -ForegroundColor White

$scriptCount = (Get-ChildItem -Path "publish\Scripts\Master" -Filter "*.py" -ErrorAction SilentlyContinue).Count
$actionCount = (Get-ChildItem -Path "publish\Scripts\Action" -Filter "*.jsx" -ErrorAction SilentlyContinue).Count
$pywCount = (Get-ChildItem -Path "publish\Scripts" -Filter "*.pyw" -ErrorAction SilentlyContinue).Count

Write-Host "  - Master Scripts: $scriptCount" -ForegroundColor White
Write-Host "  - Action Scripts (JSX): $actionCount" -ForegroundColor White
Write-Host "  - Action Scripts (PYW): $pywCount" -ForegroundColor White

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
