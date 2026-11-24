# admin-auth.ps1 - verifikasi password admin untuk folder update (dot-source, jangan dijalankan langsung).
#
# Dipakai oleh publish-update.ps1, lock-folder.ps1, unlock-folder.ps1.
# Password TIDAK disimpan di file: admin.lock hanya berisi hash PBKDF2-SHA256 + salt.
# Format admin.lock:  pbkdf2$<iterasi>$<saltBase64>$<hashBase64>

function Get-AdminLockPath {
    Join-Path $PSScriptRoot 'admin.lock'
}

function Test-AdminPassword {
    param([string]$Plain)

    $lockPath = Get-AdminLockPath
    if (-not (Test-Path -LiteralPath $lockPath)) { return $true }  # belum ada lock = tidak diproteksi

    $raw = (Get-Content -LiteralPath $lockPath -Raw).Trim()
    $parts = $raw -split '\$'
    if ($parts.Count -ne 4 -or $parts[0] -ne 'pbkdf2') {
        throw "Format admin.lock tidak dikenal. Hapus file itu untuk mematikan proteksi."
    }

    $iterations = [int]$parts[1]
    $salt = [Convert]::FromBase64String($parts[2])
    $expected = [Convert]::FromBase64String($parts[3])

    $derive = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        $Plain, $salt, $iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $actual = $derive.GetBytes($expected.Length)

    if ($actual.Length -ne $expected.Length) { return $false }
    for ($i = 0; $i -lt $actual.Length; $i++) {
        if ($actual[$i] -ne $expected[$i]) { return $false }
    }
    return $true
}

function Read-PlainPassword {
    param([string]$Prompt = 'Password admin')

    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

# Minta password sampai benar (maksimal 3 kali). Mengembalikan $true kalau lolos.
function Confirm-AdminPassword {
    param([string]$Action = 'melanjutkan')

    if (-not (Test-Path -LiteralPath (Get-AdminLockPath))) {
        Write-Host "[i] admin.lock belum ada - proteksi password mati." -ForegroundColor DarkGray
        return $true
    }

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $plain = Read-PlainPassword "Password admin untuk $Action"
        if (Test-AdminPassword -Plain $plain) { return $true }
        Write-Host "[!] Password salah ($attempt/3)." -ForegroundColor Red
    }
    return $false
}
