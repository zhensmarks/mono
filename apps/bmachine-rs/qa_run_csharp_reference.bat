@echo off
setlocal
for %%P in (bmachine-slint-demo.exe BMachine.App.exe) do taskkill /F /IM %%P >nul 2>&1
robocopy "Y:\BMachine" "C:\bm\BMachineReference" /E /NFL /NDL /NJH /NJS /NP >nul
start "" "C:\bm\BMachineReference\BMachine.App.exe"
timeout /t 10 >nul
