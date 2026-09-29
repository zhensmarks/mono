$ErrorActionPreference = "Stop"

Write-Host "================================"
Write-Host "  BMachine - Publish (win-x64)"
Write-Host "================================"

# Root monorepo = dua level di atas folder app ini (mono\apps\bmachine -> mono)
$MONO        = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$projectPath = Join-Path $PSScriptRoot "src\BMachine.App\BMachine.App.csproj"
$outputDir   = Join-Path $MONO "BMachine"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at $projectPath"
}

# Bersihkan output lama agar tidak menumpuk file usang
if (Test-Path $outputDir) {
    Write-Host "[0/1] Membersihkan output lama..."
    Remove-Item -Path $outputDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "[1/1] Publishing BMachine.App..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

Write-Host ""
Write-Host "Publish selesai. Output utama:" -ForegroundColor Green
Write-Host "  $outputDir\BMachine.App.exe" -ForegroundColor Green
