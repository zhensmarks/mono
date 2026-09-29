@echo off
setlocal enabledelayedexpansion
title Monorepo Build Tool

set ROOT=%~dp0
set BMACHINE_PROJ=%ROOT%apps\bmachine\src\BMachine.App\BMachine.App.csproj
set PIXA_PROJ=%ROOT%apps\pixacompact\PixelcutCompact.csproj

if not "%~1"=="" (
    set TARGET=%~1
    goto RUN_PARAM
)

:MENU
cls
echo ========================================================
echo   MONOREPO - BUILD CEPAT (Kompilasi & Validasi Error)
echo ========================================================
echo   [1] Build BMachine saja
echo   [2] Build PixaCompact saja
echo   [3] Build SEMUA Aplikasi (BMachine + PixaCompact)
echo   [0] Keluar
echo ========================================================
set /p CHOICE="Pilih opsi [1-3, 0]: "

if "%CHOICE%"=="1" goto BUILD_BMACHINE
if "%CHOICE%"=="2" goto BUILD_PIXA
if "%CHOICE%"=="3" goto BUILD_ALL
if "%CHOICE%"=="0" goto EXIT
echo.
echo Pilihan tidak valid. Silakan coba lagi.
pause
goto MENU

:RUN_PARAM
if /i "%TARGET%"=="bmachine" goto BUILD_BMACHINE
if /i "%TARGET%"=="pixacompact" goto BUILD_PIXA
if /i "%TARGET%"=="all" goto BUILD_ALL
echo.
echo Argumen tidak dikenal: %TARGET%
echo Pilihan valid: bmachine, pixacompact, all
exit /b 1

:BUILD_BMACHINE
echo.
echo ========================================================
echo   [1/1] Membangun BMachine (Release)...
echo ========================================================
dotnet build "%BMACHINE_PROJ%" -c Release --nologo -v:q -p:NuGetAudit=false
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Build BMachine GAGAL!
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)
echo [OK] BMachine berhasil dibangun (0 Error).
if "%TARGET%"=="" if not "%CHOICE%"=="3" pause
if "%CHOICE%"=="1" goto MENU
if not "%CHOICE%"=="3" exit /b 0

if "%CHOICE%"=="3" goto BUILD_PIXA_STEP

:BUILD_PIXA
echo.
echo ========================================================
echo   [1/1] Membangun PixaCompact (Release)...
echo ========================================================
dotnet build "%PIXA_PROJ%" -c Release --nologo -v:q -p:NuGetAudit=false
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Build PixaCompact GAGAL!
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)
echo [OK] PixaCompact berhasil dibangun (0 Error).
if "%TARGET%"=="" pause
if "%CHOICE%"=="2" goto MENU
exit /b 0

:BUILD_PIXA_STEP
echo.
echo ========================================================
echo   [2/2] Membangun PixaCompact (Release)...
echo ========================================================
dotnet build "%PIXA_PROJ%" -c Release --nologo -v:q -p:NuGetAudit=false
if %ERRORLEVEL% neq 0 (
    echo [FAIL] Build PixaCompact GAGAL!
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)
echo [OK] PixaCompact berhasil dibangun (0 Error).
echo.
echo ========================================================
echo   Semua aplikasi berhasil dibangun tanpa error!
echo ========================================================
if "%TARGET%"=="" pause
if "%CHOICE%"=="3" goto MENU
exit /b 0

:BUILD_ALL
goto BUILD_BMACHINE

:EXIT
exit /b 0
