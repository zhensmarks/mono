# unlock-folder.ps1 - buka kunci folder update (butuh password admin).
#
#   powershell -ExecutionPolicy Bypass -File unlock-folder.ps1            # semua user boleh tulis
#   powershell -ExecutionPolicy Bypass -File unlock-folder.ps1 -Reset     # balik ke izin warisan
#   powershell -ExecutionPolicy Bypass -File unlock-folder.ps1 -Target nas
#
# Setelah publish, jalankan lock-folder.ps1 lagi (atau biarkan publish-update.ps1
# yang mengunci otomatis).

param(
    [ValidateSet('local', 'nas', 'both')][string]$Target = 'both',
    [string]$NasPath = '\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine',
    [switch]$Reset,
    [switch]$DryRun,
    [switch]$SkipAuth
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'admin-auth.ps1')

if (-not $DryRun -and -not $SkipAuth) {
    if (-not (Confirm-AdminPassword -Action 'membuka kunci folder update')) {
        throw "Password admin salah. Folder tidak diubah."
    }
}

function Invoke-Acl([string]$Path, [string[]]$AclArgs) {
    $cmd = "icacls `"$Path`" " + ($AclArgs -join ' ')
    if ($DryRun) { Write-Host "  [dry-run] $cmd" -ForegroundColor DarkGray; return }
    Write-Host "  $cmd" -ForegroundColor DarkGray
    & icacls $Path @AclArgs | Out-Null
    if ($LASTEXITCODE -ge 3) { Write-Host "  [!] icacls gagal (exit $LASTEXITCODE)" -ForegroundColor Yellow }
}

$targets = @()
if ($Target -in @('local', 'both')) { $targets += $PSScriptRoot }
if ($Target -in @('nas', 'both')) { $targets += $NasPath }

foreach ($path in $targets) {
    Write-Host ""
    Write-Host "== $path ==" -ForegroundColor White
    if (-not [System.IO.Directory]::Exists($path)) {
        Write-Host "  [!] Tidak bisa diakses, dilewati." -ForegroundColor Yellow
        continue
    }

    if ($Reset) {
        Invoke-Acl $path @("/reset", "/T", "/C")
    } else {
        Invoke-Acl $path @("/grant", "Everyone:(OI)(CI)M")
    }
}

Write-Host ""
Write-Host "Selesai. Jangan lupa lock-folder.ps1 setelah publish." -ForegroundColor Green
