@echo off
setlocal
taskkill /F /IM BMachine.App.exe >nul 2>&1
taskkill /F /IM bmachine-slint-demo.exe >nul 2>&1
rmdir /S /Q "C:\bm\BMachineReference" >nul 2>&1
robocopy "Y:\BMachine" "C:\bm\BMachineReference" /E /NFL /NDL /NJH /NJS /NP >nul
start "" "C:\bm\BMachineReference\BMachine.App.exe"
exit /b 0
