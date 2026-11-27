$ErrorActionPreference='Stop'
$exe = "Y:\apps\bmachine-rs\bmachine-slint-demo.exe"
$existing = Get-Process -Name "bmachine-slint-demo" -ErrorAction SilentlyContinue
if ($existing) { $existing | Stop-Process -Force; Start-Sleep -Seconds 2 }
$p = Start-Process -FilePath $exe -ArgumentList "--no-splash", "--simulate-hotkey" -PassThru
Start-Sleep -Seconds 3
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -ne 0) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.Application]::SetForegroundWindow([IntPtr]$h) | Out-Null
    Start-Sleep -Milliseconds 500
}
"pid=$($p.Id) handle=$h"
