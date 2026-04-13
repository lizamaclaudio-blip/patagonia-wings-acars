#requires -Version 5.1
<#
.SYNOPSIS
    Script para compilar el instalador MSI de Patagonia Wings ACARS
.DESCRIPTION
    Este script automatiza la compilación del instalador incluyendo:
    - Publicación de la aplicación
    - Copia del WASM Module
    - Compilación con WiX
.NOTES
    Requiere WiX Toolset 3.11+ instalado
    Requiere .NET Framework 4.8.1+ SDK
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

# Verificar WiX
$wixPath = "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin"
if (-not (Test-Path $wixPath)) {
    Write-Error "WiX Toolset no encontrado. Instalar desde: https://wixtoolset.org/releases/"
    exit 1
}

$env:PATH += ";$wixPath"

# Rutas
$rootDir = Split-Path -Parent $PSScriptRoot
$masterProject = Join-Path $rootDir "PatagoniaWings.Acars.Master"
$publishDir = Join-Path $PSScriptRoot "Publish"
$wasmDir = Join-Path $PSScriptRoot "WASM"
$outputDir = Join-Path $PSScriptRoot $OutputPath

Write-Host "Rutas:" -ForegroundColor Yellow
Write-Host "  Root: $rootDir"
Write-Host "  Master: $masterProject"
Write-Host "  Publish: $publishDir"
Write-Host "  WASM: $wasmDir"
Write-Host "  Output: $outputDir"
Write-Host ""

# Limpieza
Write-Host "Limpiando directorios anteriores..." -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# 1. Publicar aplicación
if (-not $SkipPublish) {
    Write-Host "Publicando aplicación ACARS..." -ForegroundColor Green
    
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
        Write-Error "Error publicando la aplicación"
        exit 1
    }
    
    Write-Host "✓ Aplicación publicada" -ForegroundColor Green
} else {
    Write-Host "⚠ Saltando publicación (SkipPublish)" -ForegroundColor Yellow
}

# 2. Verificar WASM
Write-Host ""
Write-Host "Verificando WASM Module..." -ForegroundColor Green

$wasmRequired = @(
    "manifest.json",
    "module.config",
    "patagonia-acars-wasm.wasm"
)

$wasmMissing = $false
foreach ($file in $wasmRequired) {
    $filePath = Join-Path $wasmDir $file
    if (-not (Test-Path $filePath)) {
        Write-Warning "Archivo WASM no encontrado: $file"
        $wasmMissing = $true
    }
}

if ($wasmMissing) {
    Write-Host ""
    Write-Host "⚠ ATENCIÓN: Faltan archivos del WASM Module" -ForegroundColor Yellow
    Write-Host "Para incluir soporte completo de aviones (A319, Fenix, PMDG):" -ForegroundColor Yellow
    Write-Host "  1. Descargar MobiFlight desde https://www.mobiflight.com/" -ForegroundColor Yellow
    Write-Host "  2. Copiar el archivo .wasm a $wasmDir" -ForegroundColor Yellow
    Write-Host "  3. O compilar el módulo WASM desde fuentes (GPL v3)" -ForegroundColor Yellow
    Write-Host ""
    
    if (-not $SkipWASM) {
        $continue = Read-Host "¿Continuar sin WASM Module? (S/N)"
        if ($continue -ne 'S' -and $continue -ne 's') {
            exit 1
        }
    }
}

# 3. Compilar con WiX
Write-Host ""
Write-Host "Compilando instalador MSI..." -ForegroundColor Green

$wxsFile = Join-Path $PSScriptRoot "PatagoniaWings.ACARS.wxs"
$wixobjFile = Join-Path $outputDir "PatagoniaWings.ACARS.wixobj"
$msiFile = Join-Path $outputDir "PatagoniaWings.ACARS.msi"

# Candle
$candleArgs = @(
    $wxsFile
    "-dPublishDir=$publishDir"
    "-dWasmDir=$wasmDir"
    "-o", $wixobjFile
    "-arch", "x64"
)

Write-Host "Ejecutando: candle $candleArgs" -ForegroundColor Gray
& candle @candleArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error en candle (compilación WiX)"
    exit 1
}

Write-Host "✓ Archivos .wixobj generados" -ForegroundColor Green

# Light
$lightArgs = @(
    $wixobjFile
    "-ext", "WixUIExtension"
    "-ext", "WixUtilExtension"
    "-o", $msiFile
    "-cultures:es-ES"
)

Write-Host "Ejecutando: light $lightArgs" -ForegroundColor Gray
& light @lightArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error en light (generación MSI)"
    exit 1
}

Write-Host "✓ Instalador MSI generado" -ForegroundColor Green

# 4. Resumen
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Instalador creado exitosamente!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Archivo: $msiFile" -ForegroundColor Yellow
Write-Host ""
Write-Host "Tamaño: $([math]::Round((Get-Item $msiFile).Length / 1MB, 2)) MB" -ForegroundColor Gray
Write-Host ""
Write-Host "Incluye:" -ForegroundColor White
Write-Host "  ✓ Aplicación ACARS" -ForegroundColor Green
if (-not $wasmMissing) {
    Write-Host "  ✓ WASM Module para aviones complejos" -ForegroundColor Green
} else {
    Write-Host "  ⚠ Sin WASM Module (instalación básica)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Para instalar, ejecutar: $msiFile" -ForegroundColor Cyan
Write-Host ""
