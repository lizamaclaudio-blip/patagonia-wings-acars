# Patagonia Wings ACARS - Build and package script
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\build-release.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$slnFile = Join-Path $root "PatagoniaWings.Acars.sln"
$releaseDir = Join-Path $root "release"
$installerScript = Join-Path $PSScriptRoot "PatagoniaWingsACARSSetup.iss"
$appConfigPath = Join-Path $root "PatagoniaWings.Acars.Master\\App.config"

$innoSetupPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $innoSetupPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

$msbuildPaths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
)
$msbuild = $msbuildPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

$appVersion = "2.0.1"
if (Test-Path -LiteralPath $appConfigPath) {
    try {
        [xml]$appConfig = Get-Content -LiteralPath $appConfigPath
        $versionNode = $appConfig.configuration.appSettings.add | Where-Object { $_.key -eq "AppVersion" } | Select-Object -First 1
        if ($versionNode -and $versionNode.value) {
            $appVersion = [string]$versionNode.value
        }
    } catch {
    }
}

Write-Host ""
Write-Host "Patagonia Wings ACARS - Build and Package" -ForegroundColor Cyan
Write-Host "Version: $appVersion" -ForegroundColor Cyan
Write-Host ""

if (-not $msbuild) {
    Write-Host "MSBuild no encontrado. Instala Visual Studio 2022 o Build Tools." -ForegroundColor Red
    exit 1
}

Write-Host "MSBuild: $msbuild" -ForegroundColor Green

if (-not $iscc) {
    Write-Host "Inno Setup no encontrado. Se compilara Release, pero no se generara instalador." -ForegroundColor Yellow
} else {
    Write-Host "Inno Setup: $iscc" -ForegroundColor Green
}

Write-Host ""
Write-Host "Limpiando build anterior..." -ForegroundColor Cyan
& $msbuild $slnFile /t:Clean /p:Configuration=Release /p:Platform="Any CPU" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Clean fallo." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Compilando solucion en Release..." -ForegroundColor Cyan
& $msbuild $slnFile /t:Build /p:Configuration=Release /p:Platform="Any CPU" /v:minimal /nologo /m
if ($LASTEXITCODE -ne 0) {
    Write-Host "La compilacion fallo." -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $root "PatagoniaWings.Acars.Master\bin\Release\PatagoniaWings.Acars.Master.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "Ejecutable no encontrado: $exePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

if ($iscc) {
    Write-Host ""
    Write-Host "Generando instalador con Inno Setup..." -ForegroundColor Cyan
    & $iscc "/DMyAppVersion=$appVersion" $installerScript
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Inno Setup fallo. Revisa el script .iss." -ForegroundColor Red
        exit 1
    }

    $outputExe = Join-Path $releaseDir "PatagoniaWingsACARSSetup.exe"
    if (-not (Test-Path $outputExe)) {
        Write-Host "No se encontro el instalador generado en: $outputExe" -ForegroundColor Red
        exit 1
    }

    $sizeMB = [math]::Round((Get-Item $outputExe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Instalador generado exitosamente." -ForegroundColor Green
    Write-Host "Archivo: PatagoniaWingsACARSSetup.exe" -ForegroundColor Green
    Write-Host "Tamano : $sizeMB MB" -ForegroundColor Green
    Write-Host "Ruta   : $releaseDir" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Copiando binarios Release a la carpeta release..." -ForegroundColor Yellow
    $binDir = Join-Path $root "PatagoniaWings.Acars.Master\bin\Release"
    Copy-Item -Path "$binDir\*" -Destination $releaseDir -Recurse -Force
    Write-Host "Binarios copiados a: $releaseDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Proceso completado." -ForegroundColor Cyan
