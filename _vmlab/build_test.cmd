@echo off
setlocal
set PATH=C:\vmlab\dotnet;%PATH%
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
cd /d C:\vmlab\src\apps\bmachine
echo ==== SDK ====
dotnet --version
echo ==== RESTORE ====
dotnet restore BMachine.v2.sln
echo RESTORE_EXIT=%ERRORLEVEL%
echo ==== BUILD (Debug) ====
dotnet build BMachine.v2.sln -c Debug --no-restore
echo BUILD_EXIT=%ERRORLEVEL%
echo ==== TEST ====
dotnet test tests\BMachine.UI.RegressionTests\BMachine.UI.RegressionTests.csproj -c Debug --no-build --logger "console;verbosity=normal"
echo TEST_EXIT=%ERRORLEVEL%
echo ==== SELESAI ====
