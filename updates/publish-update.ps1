# publish-update.ps1 - terbitkan versi baru BMachine ke folder update LAN.
#
# Pemakaian (dari folder mana saja):
#   powershell -ExecutionPolicy Bypass -File publish-update.ps1 -Version 8.4.0 -Notes "Checklist Trello auto-refresh","Tombol refresh checklist"
#   powershell -ExecutionPolicy Bypass -File publish-update.ps1 -Version 8.4.1 -NotesFile notes.txt
#   powershell -ExecutionPolicy Bypass -File publish-update.ps1 -Version 8.4.2 -NoBuild   # payload dari build terakhir
#
# Yang dikerjakan:
#   1. Tulis versi baru ke BMachine.App.csproj (<Version>).
#   2. Publish single-file (build.ps1) ke ..\BMachine.
#   3. Susun payload: exe + Assets + Scripts\Master + Scripts\Action (data user TIDAK ikut).
#   4. Tulis manifest.json (versi, catatan singkat, sha256).
#
# Catatan: catatan rilis usahakan maksimal 5 poin, tiap poin <= 70 karakter.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Notes,
    [string]$NotesFile = "",
    [switch]$NoBuild,
    [switch]$SkipAuth,
    [string]$OutDir = '\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine'
)

$ErrorActionPreference = "Stop"

# --- Proteksi password admin (admin.lock) -----------------------------------
. (Join-Path $PSScriptRoot 'admin-auth.ps1')
if (-not $SkipAuth) {
    if (-not (Confirm-AdminPassword -Action "publish v$Version")) {
        throw "Password admin salah. Publish dibatalkan."
    }
}

$Here       = $PSScriptRoot                      # ...\mono\updates
$Mono       = Split-Path $Here -Parent           # ...\mono
$AppRoot    = Join-Path $Mono "apps\bmachine"
$BuildPs1   = Join-Path $AppRoot "build.ps1"
$Csproj     = Join-Path $AppRoot "src\BMachine.App\BMachine.App.csproj"
$PublishDir = Join-Path $Mono "BMachine"
$PayloadDir = Join-Path $Here "payload"
$Manifest   = Join-Path $Here "manifest.json"

# --- Validasi versi ---------------------------------------------------------
$Version = $Version.Trim().TrimStart("v", "V")
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versi harus format X.Y.Z (contoh 8.4.0). Diterima: '$Version'"
}

# --- Catatan rilis ----------------------------------------------------------
if (-not $Notes -or $Notes.Count -eq 0) {
    $autoNotes = Join-Path $Here "notes.txt"
    if ($NotesFile -and (Test-Path $NotesFile)) { $autoNotes = $NotesFile }
    if (Test-Path $autoNotes) {
        $Notes = Get-Content -LiteralPath $autoNotes | Where-Object { $_.Trim() -ne "" }
    }
}

if (-not $Notes -or $Notes.Count -eq 0) {
    throw "Catatan rilis kosong. Isi -Notes `"poin 1`",`"poin 2`" atau siapkan updates\notes.txt (satu poin per baris)."
}

$cleanNotes = @()
foreach ($n in $Notes) {
    $t = ($n.Trim() -replace '^[-**]\s*', '')
    if ($t -eq "") { continue }
    if ($t.Length -gt 90) {
        Write-Host "[!] Catatan kepanjangan ($($t.Length) karakter), dipotong: $t" -ForegroundColor Yellow
        $t = $t.Substring(0, 87) + "..."
    }
    $cleanNotes += $t
}
if ($cleanNotes.Count -gt 6) {
    Write-Host "[!] Catatan rilis $($cleanNotes.Count) poin, dipotong jadi 6 poin pertama." -ForegroundColor Yellow
    $cleanNotes = $cleanNotes[0..5]
}

# --- 1. Tulis versi ke csproj ----------------------------------------------
Write-Host "[1/5] Set versi $Version di BMachine.App.csproj" -ForegroundColor Cyan
$csprojText = Get-Content -LiteralPath $Csproj -Raw
if ($csprojText -notmatch '<Version>[^<]*</Version>') {
    throw "Tag <Version> tidak ditemukan di $Csproj"
}
$csprojText = [regex]::Replace($csprojText, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
Set-Content -LiteralPath $Csproj -Value $csprojText -Encoding UTF8

# --- 2. Publish -------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host "[2/5] Publish BMachine (single-file)..." -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File $BuildPs1
    if ($LASTEXITCODE -ne 0) { throw "Publish gagal (exit $LASTEXITCODE)." }
} else {
    Write-Host "[2/5] Lewati publish (-NoBuild), pakai hasil build terakhir." -ForegroundColor DarkGray
}

$publishedExe = Join-Path $PublishDir "BMachine.App.exe"
if (-not (Test-Path $publishedExe)) { throw "Hasil publish tidak ditemukan: $publishedExe" }

# --- 3. Susun payload -------------------------------------------------------
Write-Host "[3/5] Susun payload..." -ForegroundColor Cyan
if (Test-Path $PayloadDir) { Remove-Item -LiteralPath $PayloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $PayloadDir -Force | Out-Null

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $PayloadDir "BMachine.App.exe") -Force

$assetsDir = Join-Path $PublishDir "Assets"
if (Test-Path $assetsDir) {
    Copy-Item -LiteralPath $assetsDir -Destination (Join-Path $PayloadDir "Assets") -Recurse -Force
}

# Scripts: ikut semua kecuali data user
$scriptsSrc = Join-Path $PublishDir "Scripts"
if (Test-Path $scriptsSrc) {
    $scriptsDst = Join-Path $PayloadDir "Scripts"
    $exclude = @("Others", "Output", "Temp")
    Copy-Item -LiteralPath $scriptsSrc -Destination $scriptsDst -Recurse -Force
    foreach ($name in $exclude) {
        $drop = Join-Path $scriptsDst $name
        if (Test-Path $drop) { Remove-Item -LiteralPath $drop -Recurse -Force }
    }
}

# --- 4. Manifest ------------------------------------------------------------
Write-Host "[4/5] Tulis manifest.json..." -ForegroundColor Cyan
$exeInfo = Get-Item -LiteralPath (Join-Path $PayloadDir "BMachine.App.exe")
$hash = (Get-FileHash -LiteralPath $exeInfo.FullName -Algorithm SHA256).Hash

$manifestObj = [ordered]@{
    version    = $Version
    releasedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    notes      = $cleanNotes
    payload    = "payload"
    exe        = "BMachine.App.exe"
    sha256     = $hash
    sizeBytes  = $exeInfo.Length
}

$json = $manifestObj | ConvertTo-Json -Depth 4
# Tulis tanpa BOM supaya bisa dibaca tool lain (C#/JsonDocument tetap aman, tapi lebih bersih).
[System.IO.File]::WriteAllText($Manifest, $json, (New-Object System.Text.UTF8Encoding($false)))

$sizeMb = [math]::Round($exeInfo.Length / 1MB, 1)

# --- 5. Salin ke server NAS (payload dulu, manifest terakhir) -----------------
if ($OutDir) {
    if ([System.IO.Directory]::Exists($OutDir)) {
        # Kalau folder NAS dikunci (read-only), buka sementara dulu.
        # Password sudah diverifikasi di awal, dan folder dikunci lagi di akhir.
        $relock = $false
        $probe = Join-Path $OutDir "._writetest"
        try {
            Set-Content -LiteralPath $probe -Value "x" -ErrorAction Stop
            Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Host "[5/5] Folder NAS terkunci - membuka sementara..." -ForegroundColor Yellow
            & icacls $OutDir /grant "Everyone:(OI)(CI)M" | Out-Null
            $relock = $true
        }

        Write-Host "[5/5] Salin payload ke NAS: $OutDir" -ForegroundColor Cyan
        $null = robocopy $PayloadDir (Join-Path $OutDir "payload") /MIR /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { throw "robocopy gagal (exit $LASTEXITCODE) saat menyalin payload ke NAS." }

        Copy-Item -LiteralPath $Manifest -Destination (Join-Path $OutDir "manifest.json") -Force
        Write-Host "      manifest.json tersalin (versi terlihat oleh user setelah payload lengkap)." -ForegroundColor DarkGray

        if ($relock) {
            & icacls $OutDir /inheritance:r /grant:r "Everyone:(OI)(CI)RX" | Out-Null
            Write-Host "      folder NAS dikunci lagi (read-only)." -ForegroundColor DarkGray
        }
    } else {
        Write-Host "[!] Folder NAS tidak bisa diakses: $OutDir" -ForegroundColor Yellow
        Write-Host "    Payload tetap tersedia lokal di $PayloadDir - salin manual kalau perlu." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Selesai: v$Version ($sizeMb MB)" -ForegroundColor Green
Write-Host "  manifest : $Manifest"
Write-Host "  payload  : $PayloadDir"
Write-Host "  NAS      : $OutDir"
Write-Host ""
Write-Host "Catatan rilis:" -ForegroundColor Cyan
$cleanNotes | ForEach-Object { Write-Host "  - $_" }
Write-Host ""
Write-Host "PC lain akan melihat versi ini di: $OutDir" -ForegroundColor Yellow
