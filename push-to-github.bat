@echo off
setlocal EnableDelayedExpansion
title Push ke GitHub - mono
cd /d "%~dp0"

rem ============================================================
rem  push-to-github.bat  -  commit + push perubahan ke GitHub
rem  Pemakaian:
rem    (klik 2x)                     -> menu interaktif
rem    push-to-github.bat bmachine   -> commit+push hanya apps\bmachine
rem    push-to-github.bat tracked    -> commit+push file yang sudah di-track
rem    push-to-github.bat all        -> commit+push SEMUA perubahan
rem ============================================================

set "ARG=%~1"

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Git tidak ditemukan di PATH.
    pause
    exit /b 1
)

echo Memeriksa status dari GitHub...
git fetch origin 2>nul

if /i "%ARG%"=="bmachine" goto :do_bm
if /i "%ARG%"=="tracked"  goto :do_tracked
if /i "%ARG%"=="all"      goto :do_all
if not "%ARG%"=="" goto :badarg

:menu
cls
echo ========================================================
echo   PUSH KE GITHUB  (commit + push)
echo ========================================================
git status -sb
echo --------------------------------------------------------
echo   [1] Commit + Push hanya apps\bmachine     (disarankan)
echo   [2] Commit + Push file yang sudah di-track saja
echo   [3] Commit + Push SEMUA perubahan (termasuk file baru)
echo   [0] Keluar
echo ========================================================
set "PIL="
set /p "PIL=Pilih [1-3, 0]: "
if "%PIL%"=="1" goto :do_bm
if "%PIL%"=="2" goto :do_tracked
if "%PIL%"=="3" goto :do_all
if "%PIL%"=="0" exit /b 0
echo Pilihan tidak valid.
timeout /t 2 >nul
goto :menu

:do_bm
set "LABEL=hanya apps\bmachine"
set "STAGE=git add -A -- apps/bmachine"
goto :commit

:do_tracked
set "LABEL=file yang sudah di-track"
set "STAGE=git add -u"
goto :commit

:do_all
set "LABEL=SEMUA perubahan"
set "STAGE=git add -A"
goto :commit

:commit
echo.
echo === Stage: %LABEL% ===
%STAGE%

echo.
echo === Yang akan di-commit ===
git status -s
git diff --cached --stat

git diff --cached --quiet
if not errorlevel 1 (
    echo.
    echo [INFO] Tidak ada perubahan baru untuk di-commit.
    goto :trypush
)

echo.
set "MSG="
set /p "MSG=Pesan commit (kosongkan = otomatis): "
if "%MSG%"=="" set "MSG=update: %LABEL%"

git commit -m "%MSG%"
if errorlevel 1 (
    echo [ERROR] Commit gagal.
    pause
    exit /b 1
)

:trypush
echo.
echo === Push ke origin/main ===
git push origin main
if errorlevel 1 (
    echo.
    echo [ERROR] Push gagal. Kemungkinan perlu login, atau ada konflik.
    echo         Jika perlu, jalankan:  git pull --rebase origin main
    pause
    exit /b 1
)

echo.
echo ========================================================
echo   SELESAI - perubahan sudah ter-push ke GitHub
echo ========================================================
git log --oneline -1
echo.
pause
exit /b 0

:badarg
echo Argumen tidak dikenal: %ARG%
echo Pilihan valid: bmachine ^| tracked ^| all
pause
exit /b 1
