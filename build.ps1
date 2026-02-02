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
dotnet publish src/BMachine.App/BMachine.App.csproj -c Release -r win-x64 --self-contained -o publish

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish failed!" -ForegroundColor Red
    exit
}

# Cleanup unwanted files (PDBs, etc if any)
Get-ChildItem -Path publish -Filter "*.pdb" -Recurse | Remove-Item -Force

Write-Host "[OK] Publish completed." -ForegroundColor Green

# 1b. Build Plugins
Write-Host ""
Write-Host "[1b/4] Building Plugins..." -ForegroundColor Yellow

# Helper (BMachineContext)
# Write-Host "  - Building Helper (BMachineContext)..."
# dotnet publish src/BMachineContext/BMachineContext.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o src/BMachine.Plugins.ContextMenu/assets

# Plugin (ContextMenu)
# Write-Host "  - Building Plugin (ContextMenu)..."
# $pluginOut = "publish/plugins/BMachine.Plugins.ContextMenu"
# dotnet publish src/BMachine.Plugins.ContextMenu/BMachine.Plugins.ContextMenu.csproj -c Release -o $pluginOut

# Copy Manifest & Asset
# Copy-Item "src/BMachine.Plugins.ContextMenu/plugin.json" -Destination $pluginOut -Force
# if (Test-Path "src/BMachine.Plugins.ContextMenu/assets") {
#    Copy-Item "src/BMachine.Plugins.ContextMenu/assets" -Destination $pluginOut -Recurse -Force
# }

Write-Host "[OK] Plugins built." -ForegroundColor Green

# 2. Sync Scripts & Plugins
Write-Host ""
Write-Host "[2/4] Syncing content..." -ForegroundColor Yellow

# Scripts
if (Test-Path "publish\Scripts") { Remove-Item "publish\Scripts" -Recurse -Force }
Copy-Item "Scripts" -Destination "publish" -Recurse -Force
Write-Host "  - Scripts synced." -ForegroundColor DarkGray

# Plugins - MERGE existing publish with source plugins folder
# Don't delete publish/plugins because it contains ContextMenu plugin from publish step
if (Test-Path "plugins") {
    # Copy individual plugin files from source plugins folder
    if (!(Test-Path "publish\plugins")) { New-Item -ItemType Directory "publish\plugins" | Out-Null }
    Copy-Item "plugins\*" -Destination "publish\plugins" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  - Plugins synced." -ForegroundColor DarkGray
} else {
    Write-Host "  [WARN] 'plugins' folder not found in source!" -ForegroundColor Magenta
}

Write-Host "[OK] Content synced." -ForegroundColor Green

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
