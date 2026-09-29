$ErrorActionPreference = "Stop"

# build.ps1 — build salah satu atau semua aplikasi di monorepo.
# Pemakaian:
#   powershell -File build.ps1                # build semua app
#   powershell -File build.ps1 -App bmachine  # build BMachine saja
#   powershell -File build.ps1 -App pixacompact
#   powershell -File build.ps1 -Config Debug  # default: Release

param(
    [string]$App = "all",
    [string]$Config = "Release"
)

$ROOT = $PSScriptRoot

$Apps = @{
    "bmachine" = @{
        Label = "BMachine"
        Path  = "$ROOT\apps\bmachine\src\BMachine.App\BMachine.App.csproj"
    }
    "pixacompact" = @{
        Label = "PixaCompact"
        Path  = "$ROOT\apps\pixacompact\PixelcutCompact.csproj"
    }
}

function Build-One($key) {
    $info = $Apps[$key]
    Write-Host ""
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "  Build $($info.Label) ($Config)" -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan

    if (-not (Test-Path $info.Path)) {
        Write-Error "Project tidak ditemukan: $($info.Path)"
    }

    dotnet build $info.Path -c $Config --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build $($info.Label) gagal!"
    }
    Write-Host "[OK] Build $($info.Label) sukses." -ForegroundColor Green
}

if ($App -eq "all") {
    foreach ($key in @("bmachine", "pixacompact")) { Build-One $key }
} elseif ($Apps.ContainsKey($App)) {
    Build-One $App
} else {
    Write-Error "App tidak dikenal: $App`nPilihan: all, $($Apps.Keys -join ', ')"
}

Write-Host ""
Write-Host "Selesai." -ForegroundColor Green
