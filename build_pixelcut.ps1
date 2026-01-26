$ErrorActionPreference = "Stop"

$solutionDir = $PSScriptRoot
$projectDir = Join-Path $solutionDir "src\PixelcutCompact"
$outputDir = $projectDir 
$exeName = "PixelcutCompact.exe"

Write-Host "Building PixelcutCompact..." -ForegroundColor Cyan

# Publish single file
dotnet publish "$projectDir\PixelcutCompact.csproj" -c Release -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true --self-contained -o "$projectDir\bin\PublishTemp"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build Failed!" -ForegroundColor Red
    exit 1
}

# Move/Copy to destination
$sourceExe = Join-Path "$projectDir\bin\PublishTemp" $exeName
$destExe = Join-Path $outputDir $exeName

if (Test-Path $destExe) {
    Remove-Item $destExe -Force
}

Move-Item $sourceExe $destExe -Force

# Clean up temp
Remove-Item "$projectDir\bin\PublishTemp" -Recurse -Force

Write-Host "Build Success!" -ForegroundColor Green
Write-Host "Output: $destExe" -ForegroundColor Yellow
