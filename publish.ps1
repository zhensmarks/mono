$ErrorActionPreference = "Stop"

# publish.ps1 — publish (single-file, self-contained, win-x64) salah satu atau semua app.
# Pemakaian:
#   powershell -File publish.ps1 -App bmachine
#   powershell -File publish.ps1 -App pixacompact
#   powershell -File publish.ps1                # semua app
#
# Output:
#   apps\bmachine\publish\win-x64\BMachine     (~143 MB)
#   apps\pixacompact\publish\win-x64\PixaCompact (~2.2 GB: Playwright + ONNX)
#
# CATATAN: publish PixaCompact lama (menyalin driver Playwright). Script ini
# memanggil build.ps1 milik app tersebut yang sudah menangani hal itu.

param(
    [string]$App = "all"
)

$ROOT = $PSScriptRoot

$Apps = @{
    "bmachine" = @{
        Label  = "BMachine"
        Script = "$ROOT\apps\bmachine\build.ps1"
    }
    "pixacompact" = @{
        Label  = "PixaCompact"
        Script = "$ROOT\apps\pixacompact\build.ps1"
    }
}

function Publish-One($key) {
    $info = $Apps[$key]
    Write-Host ""
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "  Publish $($info.Label)" -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan

    if (-not (Test-Path $info.Script)) {
        Write-Error "Build script tidak ditemukan: $($info.Script)"
    }

    # Script build.ps1 milik masing-masing app sudah memakai path relatif
    # terhadap foldernya sendiri, jadi langsung jalan dari sini.
    & powershell -ExecutionPolicy Bypass -File $info.Script
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish $($info.Label) gagal!"
    }
    Write-Host "[OK] Publish $($info.Label) sukses." -ForegroundColor Green
}

if ($App -eq "all") {
    foreach ($key in @("bmachine", "pixacompact")) { Publish-One $key }
} elseif ($Apps.ContainsKey($App)) {
    Publish-One $App
} else {
    Write-Error "App tidak dikenal: $App`nPilihan: all, $($Apps.Keys -join ', ')"
}

Write-Host ""
Write-Host "Selesai." -ForegroundColor Green
