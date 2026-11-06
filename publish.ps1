# Builds a portable, self-contained copy of DentalX and zips it — no .NET runtime, no
# installer, no admin rights needed on the target PC. Extract the zip anywhere and run
# DentalX.exe. Each PC gets its own local database/captures (Desktop\DentalX.db), so
# parallel test machines never interfere with each other or with the dev machine.
#
# Usage:  .\publish.ps1
# Output: publish\DentalX_<version>.zip

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = Get-Date -Format "yyyyMMdd_HHmm"
$outDir  = "publish\DentalX_$version"
$zipPath = "publish\DentalX_$version.zip"

Write-Host "Publishing self-contained build ($version)..." -ForegroundColor Cyan
dotnet publish DentalX.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $outDir

if (Test-Path "Driver") {
    Copy-Item "Driver" -Destination "$outDir\Driver" -Recurse -Force
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "Zipping..." -ForegroundColor Cyan
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $zipPath ($sizeMB MB)" -ForegroundColor Green
Write-Host "Send this zip to any PC, extract it fully, and run DentalX.exe - nothing else needs installing."
