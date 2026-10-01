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

$preserveDir = Join-Path $MONO ".publish-preserve\pixacompact"

# ---------------------------------------------------------------------------
# Data pengguna yang TIDAK boleh hilang saat publish.
# Publish membersihkan folder output lebih dulu, jadi tanpa daftar ini
# settings, profil browser (login) dan Python embedded rembg akan terhapus.
# Path relatif terhadap folder output; wildcard pada nama terakhir didukung.
# ---------------------------------------------------------------------------
$PreservePaths = @(
    "settings.json",          # konfigurasi + API key
    "preview_settings.json",  # pengaturan jendela editor
    "crash.log",              # log crash terakhir
    "Resources\Rembg",        # Python embedded + rembg (diinstal saat runtime)
    "*Profile*"               # profil browser: BrowserProfile, NobgProfile, dst (berisi login)
)

function Get-RelativePath([string]$Base, [string]$Full) {
    $b = $Base.TrimEnd('\') + '\'
    if ($Full.StartsWith($b, [StringComparison]::OrdinalIgnoreCase)) {
        return $Full.Substring($b.Length)
    }
    return $Full
}

function Save-UserData {
    if (-not (Test-Path $outputDir)) { return }
    if (Test-Path $preserveDir) { Remove-Item $preserveDir -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Path $preserveDir -Force | Out-Null

    $found = 0
    foreach ($pattern in $PreservePaths) {
        $parentRel = Split-Path $pattern -Parent
        $leaf      = Split-Path $pattern -Leaf
        $parentAbs = if ($parentRel) { Join-Path $outputDir $parentRel } else { $outputDir }
        if (-not (Test-Path $parentAbs)) { continue }

        $items = Get-ChildItem -Path $parentAbs -Filter $leaf -Force -ErrorAction SilentlyContinue
        foreach ($item in $items) {
            $rel  = Get-RelativePath $outputDir $item.FullName
            $dest = Join-Path $preserveDir $rel
            New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
            Write-Host "    [simpan] $rel" -ForegroundColor DarkGray
            Move-Item -LiteralPath $item.FullName -Destination $dest -Force
            $found++
        }
    }
    if ($found -eq 0) { Write-Host "    (belum ada data pengguna untuk disimpan)" -ForegroundColor DarkGray }
}

function Restore-UserData {
    if (-not (Test-Path $preserveDir)) { return }
    # Salin-balik secara rekursif (merge), jangan hapus output hasil build.
    $items = Get-ChildItem -Path $preserveDir -Recurse -Force -ErrorAction SilentlyContinue |
             Sort-Object { $_.FullName.Length }
    foreach ($item in $items) {
        $rel  = Get-RelativePath $preserveDir $item.FullName
        $dest = Join-Path $outputDir $rel
        if ($item.PSIsContainer) {
            if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        } else {
            $parent = Split-Path $dest -Parent
            if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            Write-Host "    [pulih] $rel" -ForegroundColor DarkGray
            Copy-Item -LiteralPath $item.FullName -Destination $dest -Force
        }
    }
    Remove-Item $preserveDir -Recurse -Force -ErrorAction SilentlyContinue
    # Bersihkan folder induk kalau sudah kosong
    $parent = Split-Path $preserveDir -Parent
    if ((Test-Path $parent) -and -not (Get-ChildItem -Path $parent -Force -ErrorAction SilentlyContinue)) {
        Remove-Item $parent -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "[0/3] Menyimpan data pengguna..."
Save-UserData

try {
    # Bersihkan output lama agar tidak menumpuk file usang
    if (Test-Path $outputDir) {
        Write-Host "[1/3] Membersihkan output lama..."
        Remove-Item -Path $outputDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "[2/3] Publishing PixaCompact..."
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outputDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish gagal (exit code $LASTEXITCODE)"
    }

    Write-Host "[3/3] Copying Playwright drivers..."
    $playwrightSrc = Join-Path $PSScriptRoot "bin\Release\net8.0\win-x64\.playwright"
    if (Test-Path $playwrightSrc) {
        Copy-Item -Path "$playwrightSrc\*" -Destination "$outputDir\.playwright" -Recurse -Force
    } else {
        Write-Host "[WARN] Driver Playwright tidak ditemukan di: $playwrightSrc" -ForegroundColor Yellow
    }
}
finally {
    # Selalu pulihkan data pengguna, termasuk kalau publish gagal di tengah jalan
    if (Test-Path $preserveDir) {
        Write-Host "Memulihkan data pengguna..."
        Restore-UserData
    }
}

Write-Host ""
Write-Host "Publish selesai. Output utama:" -ForegroundColor Green
Write-Host "  $outputDir\PixelcutCompact.exe" -ForegroundColor Green
