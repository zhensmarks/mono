@echo off
REM Uji koneksi internet dari dalam VM
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r = Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -TimeoutSec 25; Write-Host ('NET_OK HTTP ' + $r.StatusCode) } catch { Write-Host ('NET_ERR ' + $_.Exception.Message) }"
