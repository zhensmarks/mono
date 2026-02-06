# build-macos.ps1 - BMachine macOS Build Script
# Usage: .\build-macos.ps1
# Note: This creates cross-compiled binaries for macOS.
#       To create .dmg/.pkg, transfer the output folder to a Mac.

param(
    [ValidateSet("x64", "arm64", "both")]
    [string]$Architecture = "both"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BMachine v2 - macOS Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$projectPath = "src/BMachine.App/BMachine.App.csproj"
$publishBase = "publish-macos"

# Cleanup previous builds
Write-Host "[1/4] Cleaning previous macOS builds..." -ForegroundColor Yellow
if (Test-Path $publishBase) {
    Remove-Item $publishBase -Recurse -Force
}
New-Item -ItemType Directory -Path $publishBase | Out-Null
Write-Host "[OK] Cleanup completed." -ForegroundColor Green
Write-Host ""

# Build function
function Build-MacOS {
    param([string]$Arch)
    
    $rid = "osx-$Arch"
    $outDir = "$publishBase/$rid"
    
    Write-Host "  - Building for $rid..." -ForegroundColor White
    dotnet publish $projectPath -c Release -r $rid --self-contained -o $outDir 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    [ERROR] Build failed for $rid!" -ForegroundColor Red
        return $false
    }
    
    # Remove PDB files
    Get-ChildItem -Path $outDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
    
    Write-Host "    [OK] $rid build completed." -ForegroundColor Green
    return $true
}

# 2. Build for selected architecture(s)
Write-Host "[2/4] Publishing for macOS..." -ForegroundColor Yellow

$buildSuccess = $true

if ($Architecture -eq "x64" -or $Architecture -eq "both") {
    if (-not (Build-MacOS -Arch "x64")) { $buildSuccess = $false }
}

if ($Architecture -eq "arm64" -or $Architecture -eq "both") {
    if (-not (Build-MacOS -Arch "arm64")) { $buildSuccess = $false }
}

if (-not $buildSuccess) {
    Write-Host "[ERROR] One or more builds failed!" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] macOS builds completed." -ForegroundColor Green
Write-Host ""

# 3. Sync Scripts & Plugins
Write-Host "[3/4] Syncing content..." -ForegroundColor Yellow

$targets = Get-ChildItem -Path $publishBase -Directory

foreach ($target in $targets) {
    $targetPath = $target.FullName
    
    # Scripts
    if (Test-Path "$targetPath\Scripts") { Remove-Item "$targetPath\Scripts" -Recurse -Force }
    Copy-Item "Scripts" -Destination $targetPath -Recurse -Force
    
    # Plugins
    if (Test-Path "plugins") {
        if (!(Test-Path "$targetPath\plugins")) { New-Item -ItemType Directory "$targetPath\plugins" | Out-Null }
        Copy-Item "plugins\*" -Destination "$targetPath\plugins" -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "  - Scripts and Plugins synced to all targets." -ForegroundColor DarkGray
Write-Host "[OK] Content synced." -ForegroundColor Green
Write-Host ""

# 4. Create Info.plist template for .app bundle
Write-Host "[4/4] Creating macOS app bundle templates..." -ForegroundColor Yellow

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>BMachine</string>
    <key>CFBundleDisplayName</key>
    <string>BMachine</string>
    <key>CFBundleIdentifier</key>
    <string>com.bmachine.app</string>
    <key>CFBundleVersion</key>
    <string>2.0.1</string>
    <key>CFBundleShortVersionString</key>
    <string>2.0.1</string>
    <key>CFBundleExecutable</key>
    <string>BMachine.App</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>appicon.icns</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
"@

foreach ($target in $targets) {
    $infoPlist | Out-File -FilePath "$($target.FullName)\Info.plist" -Encoding UTF8
}

Write-Host "  - Info.plist template created for each target." -ForegroundColor DarkGray
Write-Host "[OK] Templates created." -ForegroundColor Green
Write-Host ""

# Build Summary
Write-Host "========================================" -ForegroundColor Green
Write-Host "  macOS BUILD COMPLETE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output folders:" -ForegroundColor White

foreach ($target in $targets) {
    $size = [math]::Round((Get-ChildItem -Path $target.FullName -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
    Write-Host "  - $($target.Name): ${size}MB" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Next steps (on macOS):" -ForegroundColor Yellow
Write-Host "  1. Transfer the publish-macos folder to your Mac" -ForegroundColor White
Write-Host "  2. Create .app bundle structure:" -ForegroundColor White
Write-Host "     mkdir -p BMachine.app/Contents/MacOS" -ForegroundColor DarkGray
Write-Host "     mkdir -p BMachine.app/Contents/Resources" -ForegroundColor DarkGray
Write-Host "     cp -r publish-macos/osx-x64/* BMachine.app/Contents/MacOS/" -ForegroundColor DarkGray
Write-Host "     cp Info.plist BMachine.app/Contents/" -ForegroundColor DarkGray
Write-Host "  3. (Optional) Code sign: codesign --deep --force --sign - BMachine.app" -ForegroundColor White
Write-Host "  4. Create DMG: create-dmg BMachine.dmg BMachine.app" -ForegroundColor White
Write-Host ""
