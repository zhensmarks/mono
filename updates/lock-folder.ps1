# lock-folder.ps1 - kunci folder update: SEMUA user hanya bisa BACA.
#
#   powershell -ExecutionPolicy Bypass -File lock-folder.ps1              # lokal + NAS
#   powershell -ExecutionPolicy Bypass -File lock-folder.ps1 -Target local
#   powershell -ExecutionPolicy Bypass -File lock-folder.ps1 -DryRun
#
# Lokal: akun kamu tetap bisa tulis (biar bisa publish), user lain read-only.
# NAS  : semua read-only. Untuk publish ke NAS, unlock dulu (publish-update.ps1
#        melakukannya otomatis setelah password benar, lalu mengunci lagi).

param(
    [ValidateSet('local', 'nas', 'both')][string]$Target = 'both',
    [string]$NasPath = '\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine',
    [switch]$DryRun,
    [switch]$SkipAuth
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'admin-auth.ps1')

if (-not $DryRun -and -not $SkipAuth) {
    if (-not (Confirm-AdminPassword -Action 'mengunci folder update')) {
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

$Me = "$env:USERDOMAIN\$env:USERNAME"

if ($Target -in @('local', 'both')) {
    Write-Host ""
    Write-Host "== Kunci folder staging lokal ==" -ForegroundColor White
    Write-Host "  $PSScriptRoot"
    # warisan diputus dulu supaya izin "Modify" dari folder induk tidak bocor
    Invoke-Acl $PSScriptRoot @("/inheritance:r", "/grant:r", "${Me}:(OI)(CI)F", "/grant:r", "Everyone:(OI)(CI)RX")
}

if ($Target -in @('nas', 'both')) {
    Write-Host ""
    Write-Host "== Kunci folder NAS ==" -ForegroundColor White
    Write-Host "  $NasPath"
    if (-not [System.IO.Directory]::Exists($NasPath)) {
        Write-Host "  [!] Tidak bisa diakses, dilewati." -ForegroundColor Yellow
    } else {
        Invoke-Acl $NasPath @("/inheritance:r", "/grant:r", "Everyone:(OI)(CI)RX")
    }
}

Write-Host ""
Write-Host "Selesai. Folder sekarang read-only (kecuali admin lokal)." -ForegroundColor Green
Write-Host "Untuk publish ke NAS: publish-update.ps1 akan unlock -> copy -> lock otomatis." -ForegroundColor DarkGray
