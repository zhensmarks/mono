@echo off
setlocal
cd /d "D:\#DATA ABENG\#PROJECT\mono"
if errorlevel 1 (
    echo Gagal masuk ke folder repository.
    pause
    exit /b 1
)

echo Memeriksa pembaruan dari GitHub...
git fetch origin
if errorlevel 1 (
    echo Gagal mengambil pembaruan. Periksa koneksi internet dan akses GitHub.
    pause
    exit /b 1
)

echo.
git status -sb

git merge-base --is-ancestor HEAD origin/main
if errorlevel 1 (
    echo.
    echo Update otomatis dihentikan: branch lokal berbeda dari origin/main.
    echo Tidak ada perubahan lokal yang dihapus. Minta bantuan untuk memeriksa kondisi Git.
    pause
    exit /b 2
)

for /f %%N in ('git rev-list --count HEAD..origin/main') do set "UPDATES=%%N"
if "%UPDATES%"=="0" (
    echo.
    echo Sudah versi terbaru dari GitHub.
    pause
    exit /b 0
)

echo.
echo Commit yang akan diterapkan:
git log --oneline HEAD..origin/main

echo.
echo Menerapkan pembaruan secara aman (fast-forward saja)...
git merge --ff-only origin/main
if errorlevel 1 (
    echo.
    echo Update gagal. Perubahan lokal tetap dipertahankan; tidak ada reset/stash otomatis.
    pause
    exit /b 1
)

echo.
echo Update selesai. Versi lokal:
git log -1 --oneline
pause
