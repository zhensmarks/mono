@echo off
setlocal
title Monorepo - Build
cd /d "%~dp0"

set "BMACHINE=apps\bmachine\src\BMachine.App\BMachine.App.csproj"
set "PIXA=apps\pixacompact\PixelcutCompact.csproj"

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
echo   MONOREPO - BUILD  (kompilasi + validasi 0 error)
echo ========================================================
echo   [1] Build BMachine
echo   [2] Build PixaCompact
echo   [3] Build SEMUA
echo   [0] Keluar
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
echo   SEMUA BUILD SUKSES - 0 Error
echo ========================================================
if not defined INTERACTIVE exit /b 0
pause
goto :menu

:do_bm
echo.
echo [1] Build BMachine (Release)...
dotnet build "%BMACHINE%" -c Release --nologo -v:q -p:NuGetAudit=false
if errorlevel 1 (
    echo [FAIL] BMachine GAGAL
    exit /b 1
)
echo [OK] BMachine - 0 Error
exit /b 0

:do_px
echo.
echo [2] Build PixaCompact (Release)...
dotnet build "%PIXA%" -c Release --nologo -v:q -p:NuGetAudit=false
if errorlevel 1 (
    echo [FAIL] PixaCompact GAGAL
    exit /b 1
)
echo [OK] PixaCompact - 0 Error
exit /b 0

:badarg
echo Argumen tidak dikenal: %ARG%
echo Pilihan valid: bmachine ^| pixacompact ^| all
exit /b 1

:fail
echo.
echo ========================================================
echo   BUILD GAGAL - periksa pesan error di atas
echo ========================================================
pause
exit /b 1
