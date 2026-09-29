$ErrorActionPreference = "Stop"

Write-Host "========================================"
Write-Host "  PixaCompact - Publish (win-x64)"
Write-Host "========================================"

# Root monorepo = dua level di atas folder app ini (mono\apps\pixacompact -> mono)
$MONO        = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$projectPath = Join-Path $PSScriptRoot "PixelcutCompact.csproj"
$outputDir   = Join-Path $MONO "PixaCompact"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

# Bersihkan output lama agar tidak menumpuk file usang
if (Test-Path $outputDir) {
    Write-Host "[0/2] Membersihkan output lama..."
    Remove-Item -Path $outputDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "[1/2] Publishing PixaCompact..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host "[2/2] Copying Playwright drivers..."
$playwrightSrc = Join-Path $PSScriptRoot "bin\Release\net8.0\win-x64\.playwright"
if (Test-Path $playwrightSrc) {
    Copy-Item -Path "$playwrightSrc\*" -Destination "$outputDir\.playwright" -Recurse -Force
} else {
    Write-Host "[WARN] Driver Playwright tidak ditemukan di: $playwrightSrc" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Publish selesai. Output utama:" -ForegroundColor Green
Write-Host "  $outputDir\PixelcutCompact.exe" -ForegroundColor Green
