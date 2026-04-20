Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$appConfigPath = Join-Path $root "PatagoniaWings.Acars.Master\App.config"
$releaseDir = Join-Path $root "release"
$officialWebRoot = "C:\Users\lizam\Desktop\PROYECTO PATAGONIA WINGS\PatagoniaWingsACARS\PATAGONIA WINGS WEB 2.0\patagonia-wings-site"
$webPublic = Join-Path $officialWebRoot "public\downloads"
$publishSecretsPath = Join-Path $PSScriptRoot ".publish-secrets.local"
$releaseNotesPath = Join-Path $PSScriptRoot "release-notes.txt"

# Este script deja alineados los dos carriles del updater:
# 1) los archivos public/downloads que consumen clientes legacy
# 2) los objetos versionados en Supabase que consume App.config

if (-not (Test-Path -LiteralPath $appConfigPath)) {
    throw "App.config no encontrado: $appConfigPath"
}

[xml]$appConfig = Get-Content -LiteralPath $appConfigPath
$settings = @{}
foreach ($node in $appConfig.configuration.appSettings.add) {
    $settings[[string]$node.key] = [string]$node.value
}

$appVersion = $settings["AppVersion"]
$supabaseUrl = $settings["SupabaseUrl"]
$storagePublicBase = "$supabaseUrl/storage/v1/object/public/acars-releases"
$webEnvPath = Join-Path $officialWebRoot ".env.local"
$supabasePublishableKey = $null
$supabaseStorageWriteKey = $env:SUPABASE_SERVICE_ROLE_KEY

function Read-KeyFromEnvFile {
    param(
        [string]$Path,
        [string]$Key
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match "^\Q$Key\E=(.+)$") {
            return $matches[1].Trim()
        }
    }

    return $null
}

if (Test-Path -LiteralPath $webEnvPath) {
    $supabasePublishableKey = Read-KeyFromEnvFile -Path $webEnvPath -Key "NEXT_PUBLIC_SUPABASE_ANON_KEY"
}

if ([string]::IsNullOrWhiteSpace($supabaseStorageWriteKey)) {
    $supabaseStorageWriteKey = Read-KeyFromEnvFile -Path $webEnvPath -Key "SUPABASE_SERVICE_ROLE_KEY"
}

if ([string]::IsNullOrWhiteSpace($supabasePublishableKey)) {
    $supabasePublishableKey = $settings["SupabaseAnonKey"]
}

if ([string]::IsNullOrWhiteSpace($supabaseStorageWriteKey)) {
    $supabaseStorageWriteKey = Read-KeyFromEnvFile -Path $publishSecretsPath -Key "SUPABASE_SERVICE_ROLE_KEY"
}

if ([string]::IsNullOrWhiteSpace($supabaseStorageWriteKey)) {
    throw "Falta SUPABASE_SERVICE_ROLE_KEY para escribir releases en Supabase storage. Los archivos public/downloads si quedan listos, pero la subida versionada requiere esa clave."
}

$releaseVersioned = Join-Path $releaseDir "PatagoniaWingsACARSSetup-$appVersion.exe"
$releaseGeneric = Join-Path $releaseDir "PatagoniaWingsACARSSetup.exe"
$installerSrc = if (Test-Path -LiteralPath $releaseVersioned) { $releaseVersioned } else { $releaseGeneric }

if (-not (Test-Path -LiteralPath $installerSrc)) {
    throw "Installer no encontrado. Ejecuta build-release.ps1 antes. Ruta esperada: $releaseVersioned"
}

if (-not (Test-Path -LiteralPath $webPublic)) {
    New-Item -ItemType Directory -Path $webPublic -Force | Out-Null
}

$genericInstallerName = "PatagoniaWingsACARSSetup.exe"
$versionedInstallerName = "PatagoniaWingsACARSSetup-$appVersion.exe"
$storageReleaseSuffix = "-r2"
$storageGenericInstallerName = "PatagoniaWingsACARSSetup.exe"
$storageGenericManifestName = "acars-update.json"
$storageGenericXmlName = "autoupdater.xml"
$storageInstallerName = "PatagoniaWingsACARSSetup-$appVersion$storageReleaseSuffix.exe"
$storageManifestName = "acars-update-$appVersion$storageReleaseSuffix.json"
$storageXmlName = "autoupdater-$appVersion$storageReleaseSuffix.xml"
$genericInstallerPath = Join-Path $webPublic $genericInstallerName
$versionedInstallerPath = Join-Path $webPublic $versionedInstallerName
$manifestPath = Join-Path $webPublic "acars-update.json"
$versionedManifestPath = Join-Path $webPublic "acars-update-$appVersion.json"
$xmlPath = Join-Path $webPublic "autoupdater.xml"
$versionedXmlPath = Join-Path $webPublic "autoupdater-$appVersion.xml"

Copy-Item -LiteralPath $installerSrc -Destination $genericInstallerPath -Force
Copy-Item -LiteralPath $installerSrc -Destination $versionedInstallerPath -Force

$sizeMB = [math]::Round((Get-Item -LiteralPath $installerSrc).Length / 1MB, 1)
$downloadUrl = "$storagePublicBase/$storageInstallerName"
$genericDownloadUrl = "$storagePublicBase/$storageGenericInstallerName"
$releaseNotes = if (Test-Path -LiteralPath $releaseNotesPath) {
    (Get-Content -LiteralPath $releaseNotesPath -Raw).Trim()
} else {
    "- Version $appVersion con autoupdate por feed genérico Supabase`n- Ruta en vivo mejorada en desktop`n- Runtime y scripts de publicación alineados"
}

$manifestObject = [ordered]@{
    version = $appVersion
    webVersion = "2.0"
    downloadUrl = $genericDownloadUrl
    mandatory = $false
    notes = $releaseNotes
    releaseDate = [DateTime]::UtcNow.ToString("yyyy-MM-dd")
    minVersion = "2.0.5"
    fileSize = "$sizeMB MB"
    storageProvider = "supabase"
}

$manifestJson = $manifestObject | ConvertTo-Json -Depth 4
$xmlContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$appVersion.0</version>
  <url>$genericDownloadUrl</url>
  <changelog>$([System.Security.SecurityElement]::Escape($releaseNotes))</changelog>
  <mandatory>false</mandatory>
</item>
"@

Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $versionedManifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $xmlPath -Value $xmlContent -Encoding UTF8
Set-Content -LiteralPath $versionedXmlPath -Value $xmlContent -Encoding UTF8

$uploadEntries = @(
    @{ objectName = $storageGenericInstallerName; sourcePath = $genericInstallerPath; contentType = "application/octet-stream" }
    @{ objectName = $storageGenericManifestName; sourcePath = $manifestPath; contentType = "application/json" }
    @{ objectName = $storageGenericXmlName; sourcePath = $xmlPath; contentType = "application/xml" }
    @{ objectName = $storageInstallerName; sourcePath = $versionedInstallerPath; contentType = "application/octet-stream" }
    @{ objectName = $storageManifestName; sourcePath = $manifestPath; contentType = "application/json" }
    @{ objectName = $storageXmlName; sourcePath = $xmlPath; contentType = "application/xml" }
)

$uploadScriptPath = Join-Path $env:TEMP "patagonia-acars-upload.js"
$uploadScript = @'
const fs = require("fs");
const { createClient } = require(process.cwd() + "/node_modules/@supabase/supabase-js");

const supabase = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_KEY);
const entries = JSON.parse(process.env.UPLOAD_ENTRIES_JSON);

(async () => {
  for (const entry of entries) {
    const payload = fs.readFileSync(entry.sourcePath);
    const { error } = await supabase.storage
      .from("acars-releases")
      .upload(entry.objectName, payload, {
        // Upsert=true hace el publish idempotente: misma version, mismo nombre, sin 409.
        upsert: true,
        contentType: entry.contentType,
        cacheControl: "no-cache",
      });

    if (error) {
      throw error;
    }
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
'@

Set-Content -LiteralPath $uploadScriptPath -Value $uploadScript -Encoding UTF8

$uploadJson = $uploadEntries | ConvertTo-Json -Depth 5 -Compress
Push-Location $officialWebRoot
try {
    $env:SUPABASE_URL = $supabaseUrl
    $env:SUPABASE_KEY = $supabaseStorageWriteKey
    $env:UPLOAD_ENTRIES_JSON = $uploadJson
    node $uploadScriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Fallo la subida a Supabase storage."
    }
}
finally {
    Remove-Item Env:SUPABASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:SUPABASE_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:UPLOAD_ENTRIES_JSON -ErrorAction SilentlyContinue
    Pop-Location
    Remove-Item -LiteralPath $uploadScriptPath -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Deploy ACARS completado." -ForegroundColor Green
Write-Host "Version: $appVersion" -ForegroundColor Green
Write-Host "Instalador: $downloadUrl" -ForegroundColor Cyan
Write-Host "Manifest versionado: $storagePublicBase/$storageManifestName" -ForegroundColor Cyan
Write-Host "AutoUpdater versionado: $storagePublicBase/$storageXmlName" -ForegroundColor Cyan
