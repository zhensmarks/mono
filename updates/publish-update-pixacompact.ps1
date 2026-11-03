# publish-update-pixacompact.ps1 - terbitkan versi baru PixaCompact ke folder update LAN.
#
# Pemakaian (dari folder mana saja):
#   powershell -ExecutionPolicy Bypass -File publish-update-pixacompact.ps1 -Version 1.1.0 -Notes "Fix input beda output","Kanal update LAN"
#   powershell -ExecutionPolicy Bypass -File publish-update-pixacompact.ps1 -Version 1.1.1 -NotesFile notes-pixacompact.txt
#   powershell -ExecutionPolicy Bypass -File publish-update-pixacompact.ps1 -Version 1.1.2 -NoBuild   # payload dari build terakhir
#
# Yang dikerjakan:
#   1. Tulis versi baru ke PixelcutCompact.csproj (<Version>).
#   2. Publish single-file (build.ps1) ke ..\PixaCompact.
#   3. Susun payload: PixelcutCompact.exe + .playwright + Resources\AiModels (data user TIDAK ikut).
#   4. Tulis manifest.json (versi, catatan singkat, sha256) + changelog.json.
#   5. robocopy payload + manifest ke server NAS.
#
# Catatan: catatan rilis usahakan maksimal 5 poin, tiap poin <= 90 karakter.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Notes,
    [string]$NotesFile = "",
    [switch]$NoBuild,
    [switch]$SkipAuth,
    [string]$OutDir = '\\delapanmataair\Editor\#MASTER DESIGN\SOFTWARE\#PROJECT ROBOT\BMachine\PixaCompact'
)

$ErrorActionPreference = "Stop"

# --- Proteksi password admin (admin.lock) -----------------------------------
. (Join-Path $PSScriptRoot 'admin-auth.ps1')
if (-not $SkipAuth) {
    if (-not (Confirm-AdminPassword -Action "publish pixa v$Version")) {
        throw "Password admin salah. Publish dibatalkan."
    }
}

$Here       = $PSScriptRoot                      # ...\mono\updates
$Mono       = Split-Path $Here -Parent           # ...\mono
$AppRoot    = Join-Path $Mono "apps\pixacompact"
$BuildPs1   = Join-Path $AppRoot "build.ps1"
$Csproj     = Join-Path $AppRoot "PixelcutCompact.csproj"
$PublishDir = Join-Path $Mono "PixaCompact"
$PayloadDir = Join-Path $Here "payload-pixacompact"
$Manifest   = Join-Path $Here "manifest-pixacompact.json"
$Changelog  = Join-Path $Here "changelog-pixacompact.json"

# --- Validasi versi ---------------------------------------------------------
$Version = $Version.Trim().TrimStart("v", "V")
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versi harus format X.Y.Z (contoh 1.1.0). Diterima: '$Version'"
}

# --- Catatan rilis ----------------------------------------------------------
if (-not $Notes -or $Notes.Count -eq 0) {
    $autoNotes = Join-Path $Here "notes-pixacompact.txt"
    if ($NotesFile -and (Test-Path $NotesFile)) { $autoNotes = $NotesFile }
    if (Test-Path $autoNotes) {
        $Notes = Get-Content -LiteralPath $autoNotes | Where-Object { $_.Trim() -ne "" }
    }
}

if (-not $Notes -or $Notes.Count -eq 0) {
    throw "Catatan rilis kosong. Isi -Notes `"poin 1`",`"poin 2`" atau siapkan updates\notes-pixacompact.txt (satu poin per baris)."
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
Write-Host "[1/5] Set versi $Version di PixelcutCompact.csproj" -ForegroundColor Cyan
$csprojText = Get-Content -LiteralPath $Csproj -Raw
if ($csprojText -notmatch '<Version>[^<]*</Version>') {
    throw "Tag <Version> tidak ditemukan di $Csproj"
}
$csprojText = [regex]::Replace($csprojText, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
Set-Content -LiteralPath $Csproj -Value $csprojText -Encoding UTF8

# --- 2. Publish -------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host "[2/5] Publish PixaCompact (single-file)..." -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File $BuildPs1
    if ($LASTEXITCODE -ne 0) { throw "Publish gagal (exit $LASTEXITCODE)." }
} else {
    Write-Host "[2/5] Lewati publish (-NoBuild), pakai hasil build terakhir." -ForegroundColor DarkGray
}

$publishedExe = Join-Path $PublishDir "PixelcutCompact.exe"
if (-not (Test-Path $publishedExe)) { throw "Hasil publish tidak ditemukan: $publishedExe" }

# --- 3. Susun payload -------------------------------------------------------
Write-Host "[3/5] Susun payload..." -ForegroundColor Cyan
if (Test-Path $PayloadDir) { Remove-Item -LiteralPath $PayloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $PayloadDir -Force | Out-Null

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $PayloadDir "PixelcutCompact.exe") -Force

# Driver Playwright (dibutuhkan runtime untuk browser automation)
$pwSrc = Join-Path $PublishDir ".playwright"
if (Test-Path $pwSrc) {
    Copy-Item -LiteralPath $pwSrc -Destination (Join-Path $PayloadDir ".playwright") -Recurse -Force
} else {
    Write-Host "[!] Driver Playwright tidak ada di publish output (app akan install ulang saat start)." -ForegroundColor Yellow
}

# Model AI matting (dibutuhkan RefineHair)
$modelsSrc = Join-Path $PublishDir "Resources\AiModels"
if (Test-Path $modelsSrc) {
    Copy-Item -LiteralPath $modelsSrc -Destination (Join-Path $PayloadDir "Resources\AiModels") -Recurse -Force
}

# playwright.ps1 kalau ada
$pwPs1 = Join-Path $PublishDir "playwright.ps1"
if (Test-Path $pwPs1) {
    Copy-Item -LiteralPath $pwPs1 -Destination $PayloadDir -Force
}

# Data user TIDAK ikut payload: settings.json, preview_settings.json, BrowserProfile*,
# Resources\Rembg (Python embedded), crash.log — sudah dikecualikan karena kita copy
# per-item di atas, bukan robocopy seluruh folder.

# Daftar file yang harus DIHAPUS dari install user saat update.
$removeSrc = Join-Path $Here "remove-on-update-pixacompact.txt"
if (Test-Path $removeSrc) {
    $removeEntries = @(Get-Content -LiteralPath $removeSrc | Where-Object {
        $_.Trim() -ne "" -and -not $_.TrimStart().StartsWith("#")
    })
    $removeEntries | Set-Content -LiteralPath (Join-Path $PayloadDir "remove-on-update.txt") -Encoding UTF8
    Write-Host "      remove-on-update.txt: $($removeEntries.Count) file terdaftar dihapus saat update." -ForegroundColor DarkGray
}

# --- 4. Manifest ------------------------------------------------------------
Write-Host "[4/5] Tulis manifest-pixacompact.json..." -ForegroundColor Cyan
$exeInfo = Get-Item -LiteralPath (Join-Path $PayloadDir "PixelcutCompact.exe")
$hash = (Get-FileHash -LiteralPath $exeInfo.FullName -Algorithm SHA256).Hash

$manifestObj = [ordered]@{
    version    = $Version
    releasedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    notes      = $cleanNotes
    payload    = "payload"
    exe        = "PixelcutCompact.exe"
    sha256     = $hash
    sizeBytes  = $exeInfo.Length
}

$json = $manifestObj | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($Manifest, $json, (New-Object System.Text.UTF8Encoding($false)))

# --- 4b. Riwayat changelog --------------------------------------------------
$history = New-Object System.Collections.ArrayList
if (Test-Path -LiteralPath $Changelog) {
    try {
        $parsed = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $Changelog -Raw)
        foreach ($entry in @($parsed)) {
            if (-not $entry) { continue }
            $versionText = "$($entry.version)"
            if ($versionText -eq "" -or $versionText -eq $Version) { continue }
            [void]$history.Add($entry)
        }
    } catch {
        Write-Host "[!] changelog lama tidak terbaca, ditulis ulang dari rilis ini." -ForegroundColor Yellow
        $history = New-Object System.Collections.ArrayList
    }
}
$history.Add([pscustomobject][ordered]@{
    version    = $Version
    releasedAt = $manifestObj.releasedAt
    notes      = $cleanNotes
}) | Out-Null

$history = @($history | Sort-Object -Property @{ Expression = { [version]([string]$_.version) } } -Descending)
if ($history.Count -gt 60) { $history = @($history[0..59]) }
$changelogJson = ConvertTo-Json -InputObject @($history) -Depth 5
[System.IO.File]::WriteAllText($Changelog, $changelogJson, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "      changelog: $($history.Count) rilis tercatat." -ForegroundColor DarkGray

$sizeMb = [math]::Round($exeInfo.Length / 1MB, 1)

# --- 5. Salin ke server NAS (payload dulu, manifest terakhir) ---------------
if ($OutDir) {
    if ([System.IO.Directory]::Exists($OutDir)) {
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
        Copy-Item -LiteralPath $Changelog -Destination (Join-Path $OutDir "changelog.json") -Force
        Write-Host "      manifest.json + changelog.json tersalin." -ForegroundColor DarkGray

        if ($relock) {
            & icacls $OutDir /inheritance:r /grant:r "Everyone:(OI)(CI)RX" | Out-Null
            Write-Host "      folder NAS dikunci lagi (read-only)." -ForegroundColor DarkGray
        }
    } else {
        Write-Host "[!] Folder NAS tidak bisa diakses: $OutDir" -ForegroundColor Yellow
        Write-Host "    Payload tetap tersedia lokal di $PayloadDir - salin manual kalau perlu." -ForegroundColor Yellow
    }
}

# Bersihkan artefak newline ekstra di csproj (pola Get-Content -Raw + Set-Content
# selalu menambah SATU blank line EOF; trim supaya working tree bersih pasca-publish).
$raw = [System.IO.File]::ReadAllText($Csproj)
$trimmed = $raw -replace "(\s*)$", ""
if ($trimmed -ne $raw) { [System.IO.File]::WriteAllText($Csproj, $trimmed, (New-Object System.Text.UTF8Encoding($false))) }

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
