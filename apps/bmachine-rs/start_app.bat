@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "Y:\apps\bmachine-rs\start_app.ps1" > "Y:\apps\bmachine-rs\app_front.log" 2>&1
timeout /t 3 >nul
