@echo off
setlocal enabledelayedexpansion
title Monorepo Publish Tool

set ROOT=%~dp0
set BMACHINE_DIR=%ROOT%apps\bmachine
set PIXA_DIR=%ROOT%apps\pixacompact

if not "%~1"=="" (
    set TARGET=%~1
    goto RUN_PARAM
)

:MENU
cls
echo ========================================================
echo   MONOREPO - PUBLISH (Paket Rilis Mandiri / win-x64)
echo ========================================================
echo   [1] Publish BMachine     (win-x64, ~140 MB)
echo   [2] Publish PixaCompact  (win-x64 + Playwright/ONNX, ~2 GB)
echo   [3] Publish SEMUA Aplikasi
echo   [0] Keluar
echo ========================================================
echo   Catatan: Publish menghasilkan .exe mandiri yang siap
echo   dijalankan di komputer mana pun tanpa perlu install .NET.
echo ========================================================
set /p CHOICE="Pilih opsi [1-3, 0]: "

if "%CHOICE%"=="1" goto PUB_BMACHINE
if "%CHOICE%"=="2" goto PUB_PIXA
if "%CHOICE%"=="3" goto PUB_ALL
if "%CHOICE%"=="0" goto EXIT
echo.
echo Pilihan tidak valid. Silakan coba lagi.
pause
goto MENU

:RUN_PARAM
if /i "%TARGET%"=="bmachine" goto PUB_BMACHINE
if /i "%TARGET%"=="pixacompact" goto PUB_PIXA
if /i "%TARGET%"=="all" goto PUB_ALL
echo.
echo Argumen tidak dikenal: %TARGET%
echo Pilihan valid: bmachine, pixacompact, all
exit /b 1

:PUB_BMACHINE
echo.
echo ========================================================
echo   [1/1] Mempublikasikan BMachine (Standalone win-x64)...
echo ========================================================
cd /d "%BMACHINE_DIR%"
powershell -ExecutionPolicy Bypass -File "%BMACHINE_DIR%\build.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [FAIL] Publish BMachine GAGAL!
    cd /d "%ROOT%"
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)
cd /d "%ROOT%"
echo.
echo [OK] BMachine siap digunakan di:
echo      %ROOT%BMachine\BMachine.App.exe
if "%TARGET%"=="" if not "%CHOICE%"=="3" pause
if "%CHOICE%"=="1" goto MENU
if not "%CHOICE%"=="3" exit /b 0

if "%CHOICE%"=="3" goto PUB_PIXA_STEP

:PUB_PIXA
echo.
echo ========================================================
echo   [1/1] Mempublikasikan PixaCompact (Standalone win-x64)...
echo ========================================================
cd /d "%PIXA_DIR%"
powershell -ExecutionPolicy Bypass -File "%PIXA_DIR%\build.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [FAIL] Publish PixaCompact GAGAL!
    cd /d "%ROOT%"
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)
cd /d "%ROOT%"
echo.
echo [OK] PixaCompact siap digunakan di:
echo      %ROOT%PixaCompact\PixelcutCompact.exe
if "%TARGET%"=="" pause
if "%CHOICE%"=="2" goto MENU
exit /b 0

:PUB_PIXA_STEP
echo.
echo ========================================================
echo   [2/2] Mempublikasikan PixaCompact (Standalone win-x64)...
echo ========================================================
cd /d "%PIXA_DIR%"
powershell -ExecutionPolicy Bypass -File "%PIXA_DIR%\build.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [FAIL] Publish PixaCompact GAGAL!
    cd /d "%ROOT%"
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)
cd /d "%ROOT%"
echo.
echo ========================================================
echo   Semua aplikasi berhasil dipublikasikan!
echo   Output:
echo   - %ROOT%BMachine
echo   - %ROOT%PixaCompact
echo ========================================================
if "%TARGET%"=="" pause
if "%CHOICE%"=="3" goto MENU
exit /b 0

:PUB_ALL
goto PUB_BMACHINE

:EXIT
exit /b 0
