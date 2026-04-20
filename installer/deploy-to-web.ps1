Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$appConfigPath = Join-Path $root "PatagoniaWings.Acars.Master\App.config"
$releaseDir = Join-Path $root "release"
$officialWebRoot = "C:\Users\lizam\Desktop\PROYECTO PATAGONIA WINGS\PatagoniaWingsACARS\PATAGONIA WINGS WEB 2.0\patagonia-wings-site"
$webPublic = Join-Path $officialWebRoot "public\downloads"

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

if (Test-Path -LiteralPath $webEnvPath) {
    foreach ($line in Get-Content -LiteralPath $webEnvPath) {
        if ($line -match "^NEXT_PUBLIC_SUPABASE_ANON_KEY=(.+)$") {
            $supabasePublishableKey = $matches[1].Trim()
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($supabasePublishableKey)) {
    $supabasePublishableKey = $settings["SupabaseAnonKey"]
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

$manifestObject = [ordered]@{
    version = $appVersion
    webVersion = "2.0"
    downloadUrl = $downloadUrl
    mandatory = $false
    notes = "- Version 4.0.0 con sistema hibrido por perfil de aeronave`n- Maddog habilitado en pipeline LVAR/FSUIPC/MobiFlight sin romper C208 ni PMDG`n- Metadatos web y autoupdater alineados a la release real"
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
  <url>$downloadUrl</url>
  <changelog>v${appVersion}: perfiles hibridos por aeronave, release 4.0.0 y autoupdater alineado.</changelog>
  <mandatory>false</mandatory>
</item>
"@

Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $versionedManifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $xmlPath -Value $xmlContent -Encoding UTF8
Set-Content -LiteralPath $versionedXmlPath -Value $xmlContent -Encoding UTF8

$uploadEntries = @(
    @{ objectName = $storageInstallerName; sourcePath = $versionedInstallerPath; deleteFirst = $true }
    @{ objectName = $storageManifestName; sourcePath = $manifestPath; deleteFirst = $true }
    @{ objectName = $storageXmlName; sourcePath = $xmlPath; deleteFirst = $true }
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
    if (entry.deleteFirst) {
      const removeResult = await supabase.storage.from("acars-releases").remove([entry.objectName]);
      if (removeResult.error && !String(removeResult.error.message || "").includes("not found")) {
        throw removeResult.error;
      }
    }

    const { error } = await supabase.storage
      .from("acars-releases")
      .upload(entry.objectName, payload, {
        upsert: !entry.deleteFirst,
        contentType: "application/octet-stream",
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
    $env:SUPABASE_KEY = $supabasePublishableKey
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
