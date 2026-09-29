@echo off
setlocal
title Monorepo - Publish
cd /d "%~dp0"

set "BMACHINE_PS1=apps\bmachine\build.ps1"
set "PIXA_PS1=apps\pixacompact\build.ps1"

rem --- Mode argumen (non-interaktif) ---
set "ARG=%~1"
if /i "%ARG%"=="bmachine"    goto :bm
if /i "%ARG%"=="pixacompact" goto :px
if /i "%ARG%"=="all"         goto :all
if not "%ARG%"=="" goto :badarg

rem --- Mode menu (interaktif) ---
:menu
cls
echo ========================================================
echo   MONOREPO - PUBLISH (paket rilis mandiri / win-x64)
echo ========================================================
echo   [1] Publish BMachine     (~140 MB)
echo   [2] Publish PixaCompact  (~2 GB, Playwright + ONNX)
echo   [3] Publish SEMUA
echo   [0] Keluar
echo ========================================================
echo   Hasil .exe langsung di root: mono\BMachine, mono\PixaCompact
echo ========================================================
set "INTERACTIVE=1"
set "ARG="
set /p "ARG=Pilih [1-3, 0]: "
if "%ARG%"=="1" goto :bm
if "%ARG%"=="2" goto :px
if "%ARG%"=="3" goto :all
if "%ARG%"=="0" exit /b 0
echo Pilihan tidak valid.
timeout /t 2 >nul
goto :menu

:bm
call :do_bm
if errorlevel 1 goto :fail
if not defined INTERACTIVE exit /b 0
pause
goto :menu

:px
call :do_px
if errorlevel 1 goto :fail
if not defined INTERACTIVE exit /b 0
pause
goto :menu

:all
call :do_bm
if errorlevel 1 goto :fail
call :do_px
if errorlevel 1 goto :fail
echo.
echo ========================================================
echo   SEMUA PUBLISH SUKSES
echo   - %~dp0BMachine\BMachine.App.exe
echo   - %~dp0PixaCompact\PixelcutCompact.exe
echo ========================================================
if not defined INTERACTIVE exit /b 0
pause
goto :menu

:do_bm
echo.
echo [1] Publish BMachine (win-x64, self-contained)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%BMACHINE_PS1%"
if errorlevel 1 (
    echo [FAIL] Publish BMachine GAGAL
    exit /b 1
)
echo [OK] BMachine -^> %~dp0BMachine\BMachine.App.exe
exit /b 0

:do_px
echo.
echo [2] Publish PixaCompact (win-x64, self-contained)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%PIXA_PS1%"
if errorlevel 1 (
    echo [FAIL] Publish PixaCompact GAGAL
    exit /b 1
)
echo [OK] PixaCompact -^> %~dp0PixaCompact\PixelcutCompact.exe
exit /b 0

:badarg
echo Argumen tidak dikenal: %ARG%
echo Pilihan valid: bmachine ^| pixacompact ^| all
exit /b 1

:fail
echo.
echo ========================================================
echo   PUBLISH GAGAL - periksa pesan error di atas
echo ========================================================
pause
exit /b 1
