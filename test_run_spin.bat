@echo off
setlocal EnableDelayedExpansion
for /F %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a"

set "SPINNER=%TEMP%\snap_spinner.bat"
setlocal DisableDelayedExpansion
(
echo @echo off
echo setlocal EnableDelayedExpansion
echo set "ESC=%%ESC%%"
echo set "s1=.   "
echo set "s2=..  "
echo set "s3=... "
echo set "s4=...."
echo set /a i=0
echo :loop
echo set /a i=^(i+1^)%%%%4
echo if !i! EQU 0 set "c=%%s1%%"
echo if !i! EQU 1 set "c=%%s2%%"
echo if !i! EQU 2 set "c=%%s3%%"
echo if !i! EQU 3 set "c=%%s4%%"
echo ^<nul set /p "=%%ESC%%[2K%%ESC%%[3GStatus : Sedang memindahkan !c!"
echo ping 127.0.0.1 -n 2 ^>nul
echo if exist "%%TEMP%%\stop_spin" exit
echo goto loop
) > "%SPINNER%"
endlocal

if exist "%TEMP%\stop_spin" del "%TEMP%\stop_spin"
start /b cmd /c "%SPINNER%"

ping 127.0.0.1 -n 4 >nul
echo. > "%TEMP%\stop_spin"
ping 127.0.0.1 -n 2 >nul

echo %ESC%[2K%ESC%[3G%ESC%[92mStatus : Selesai 100%%%ESC%[0m
echo.
