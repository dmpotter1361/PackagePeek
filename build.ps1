<#
    Reproducible build for Package Peek.
    Publishes the self-contained app and builds the MSI installer.
    Paths are computed from this script's location, so it works from any clone.

    Usage:  pwsh ./build.ps1 [-Version 0.1.2]
    Requires: .NET SDK, and WiX v5  (dotnet tool install --global wix --version 5.0.2)
#>
param([string]$Version = "0.1.3")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pub  = Join-Path $root "publish"
$proj = Join-Path $root "AmazonTracker.csproj"
$wxs  = Join-Path $root "installer\Package.wxs"
$msi  = Join-Path $root "installer\PackagePeek-$Version-x64.msi"

Write-Host "Publishing self-contained build..." -ForegroundColor Cyan
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pub

Write-Host "Building MSI ($Version)..." -ForegroundColor Cyan
wix build $wxs -d "PublishDir=$pub" -d "Version=$Version" -arch x64 -o $msi

Write-Host "Done: $msi" -ForegroundColor Green
