@echo off
REM Install .NET 8 SDK di dalam VM ke C:\vmlab\dotnet
set D=C:\vmlab
if not exist %D% mkdir %D%
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile C:\vmlab\dotnet-install.ps1; & C:\vmlab\dotnet-install.ps1 -Channel 8.0 -InstallDir C:\vmlab\dotnet -NoPath"
echo EXIT=%ERRORLEVEL%
C:\vmlab\dotnet\dotnet.exe --version
C:\vmlab\dotnet\dotnet.exe --list-sdks
