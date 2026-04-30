#requires -Version 5.1
<#
.SYNOPSIS
    Build MSI installer for Patagonia Wings ACARS.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\Output",
    [switch]$SkipPublish,
    [switch]$SkipWASM
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Patagonia Wings ACARS Installer Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$wixPath = "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin"
if (-not (Test-Path $wixPath)) {
    Write-Error "WiX Toolset not found. Install from https://wixtoolset.org/releases/"
    exit 1
}
$env:PATH += ";$wixPath"

$rootDir = Split-Path -Parent $PSScriptRoot
$masterProject = Join-Path $rootDir "PatagoniaWings.Acars.Master"
$publishDir = Join-Path $PSScriptRoot "Publish"
$wasmDir = Join-Path $PSScriptRoot "WASM"
$outputDir = Join-Path $PSScriptRoot $OutputPath

Write-Host "Paths:" -ForegroundColor Yellow
Write-Host "  Root: $rootDir"
Write-Host "  Master: $masterProject"
Write-Host "  Publish: $publishDir"
Write-Host "  WASM: $wasmDir"
Write-Host "  Output: $outputDir"
Write-Host ""

Write-Host "Cleaning previous directories..." -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

if (-not $SkipPublish) {
    Write-Host "Publishing ACARS app..." -ForegroundColor Green
    $publishArgs = @(
        "publish"
        "$masterProject\PatagoniaWings.Acars.Master.csproj"
        "-c", $Configuration
        "-o", $publishDir
        "--self-contained", "false"
        "-r", "win-x64"
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed."
        exit 1
    }
    Write-Host "OK: app published." -ForegroundColor Green
}
else {
    Write-Host "Skipping publish (SkipPublish)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Checking WASM package..." -ForegroundColor Green
$wasmRequired = @("manifest.json", "module.config", "patagonia-acars-wasm.wasm")
$wasmMissing = $false
foreach ($file in $wasmRequired) {
    $filePath = Join-Path $wasmDir $file
    if (-not (Test-Path $filePath)) {
        Write-Warning "Missing WASM file: $file"
        $wasmMissing = $true
    }
}

if ($wasmMissing -and -not $SkipWASM) {
    $continue = Read-Host "Continue without WASM? (S/N)"
    if ($continue -ne "S" -and $continue -ne "s") {
        exit 1
    }
}

Write-Host ""
Write-Host "Building MSI with WiX..." -ForegroundColor Green

$wxsFile = Join-Path $PSScriptRoot "PatagoniaWings.ACARS.wxs"
$wixobjFile = Join-Path $outputDir "PatagoniaWings.ACARS.wixobj"
$msiFile = Join-Path $outputDir "PatagoniaWings.ACARS.msi"

$candleArgs = @(
    $wxsFile
    "-dPublishDir=$publishDir"
    "-dWasmDir=$wasmDir"
    "-o", $wixobjFile
    "-arch", "x64"
)
& candle @candleArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "candle failed."
    exit 1
}

$lightArgs = @(
    $wixobjFile
    "-ext", "WixUIExtension"
    "-ext", "WixUtilExtension"
    "-o", $msiFile
    "-cultures:es-ES"
)
& light @lightArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "light failed."
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Installer built successfully." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "File: $msiFile" -ForegroundColor Yellow
Write-Host "Size: $([math]::Round((Get-Item $msiFile).Length / 1MB, 2)) MB" -ForegroundColor Gray
Write-Host "Includes:" -ForegroundColor White
Write-Host "  - ACARS app" -ForegroundColor Green
if ($wasmMissing) {
    Write-Host "  - No WASM module (basic install)" -ForegroundColor Yellow
}
else {
    Write-Host "  - WASM module for complex aircraft" -ForegroundColor Green
}
Write-Host ""
Write-Host "Run installer: $msiFile" -ForegroundColor Cyan
