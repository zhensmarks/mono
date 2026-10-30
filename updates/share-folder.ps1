# share-folder.ps1 - bagikan folder update ke LAN supaya PC lain bisa cek update.
#
# Jalankan sebagai Administrator:
#   powershell -ExecutionPolicy Bypass -File share-folder.ps1
#
# Hasil: share bernama "mono" -> <repo>\mono  (read-only untuk Everyone)
# Path yang dipakai app:  \\<NAMA-PC>\mono\updates
#
# Kalau tidak mau membagikan seluruh isi repo (source code), pakai:
#   powershell -ExecutionPolicy Bypass -File share-folder.ps1 -UpdatesOnly
# -> share "bmachine-updates" -> <repo>\mono\updates
#    lalu isi kolom "Folder update" di Settings app dengan \\<NAMA-PC>\bmachine-updates

param(
    [string]$ShareName = "",
    [switch]$UpdatesOnly
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Script ini harus dijalankan sebagai Administrator (klik kanan PowerShell > Run as administrator)."
}

$Mono = Split-Path $PSScriptRoot -Parent

if ($UpdatesOnly) {
    $path = $PSScriptRoot
    if (-not $ShareName) { $ShareName = "bmachine-updates" }
} else {
    $path = $Mono
    if (-not $ShareName) { $ShareName = "mono" }
}

if (-not (Test-Path -LiteralPath $path)) { throw "Folder tidak ditemukan: $path" }

$existing = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[i] Share '$ShareName' sudah ada -> $($existing.Path)" -ForegroundColor Yellow
    if ($existing.Path -ne $path) {
        Write-Host "[!] Path share berbeda dengan yang diharapkan: $path" -ForegroundColor Yellow
    }
} else {
    New-SmbShare -Name $ShareName -Path $path -ReadAccess "Everyone" | Out-Null
    Write-Host "[OK] Share dibuat: '$ShareName' -> $path (read-only)" -ForegroundColor Green
}

# Firewall: izinkan SMB dari jaringan lokal
$ruleName = "BMachine Update Share (SMB-In)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort 445 `
        -Action Allow -Profile Domain,Private | Out-Null
    Write-Host "[OK] Firewall rule ditambahkan (TCP 445, Domain/Private)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Path untuk kolom 'Folder update' di Settings app:" -ForegroundColor Cyan
if ($UpdatesOnly) {
    Write-Host "  \\$env:COMPUTERNAME\$ShareName" -ForegroundColor White
} else {
    Write-Host "  \\$env:COMPUTERNAME\$ShareName\updates" -ForegroundColor White
}
Write-Host ""
Write-Host "Cek dari PC lain:  dir \\$env:COMPUTERNAME\$ShareName" -ForegroundColor DarkGray
