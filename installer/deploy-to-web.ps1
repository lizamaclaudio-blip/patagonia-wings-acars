Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$appConfigPath = Join-Path $root "PatagoniaWings.Acars.Master\App.config"
$releaseDir = Join-Path $root "release"
$officialWebRoot = "C:\Users\lizam\Desktop\PROYECTO PATAGONIA WINGS\PatagoniaWingsACARS\PATAGONIA WINGS WEB 2.0\patagonia-wings-site"
$webPublic = Join-Path $officialWebRoot "public\downloads"
$publishSecretsPath = Join-Path $PSScriptRoot ".publish-secrets.local"
$releaseNotesPath = Join-Path $PSScriptRoot "release-notes.txt"

function Read-KeyFromEnvFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $escapedKey = [regex]::Escape($Key)

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line.TrimStart().StartsWith("#")) { continue }

        if ($line -match ('^\s*{0}\s*=\s*(.*)\s*$' -f $escapedKey)) {
            return $Matches[1].Trim().Trim('"').Trim("'")
        }
    }

    return $null
}

if (-not (Test-Path -LiteralPath $appConfigPath)) {
    throw "App.config no encontrado: $appConfigPath"
}

if (-not (Test-Path -LiteralPath $officialWebRoot)) {
    throw "No existe la web oficial esperada: $officialWebRoot"
}

[xml]$appConfig = Get-Content -LiteralPath $appConfigPath
$settings = @{}
foreach ($node in $appConfig.configuration.appSettings.add) {
    $settings[[string]$node.key] = [string]$node.value
}

$appVersion = $settings["AppVersion"]
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "No se pudo leer AppVersion desde App.config"
}

$supabaseUrl = $settings["SupabaseUrl"]
if ([string]::IsNullOrWhiteSpace($supabaseUrl)) {
    throw "No se pudo leer SupabaseUrl desde App.config"
}

$storagePublicBase = "$supabaseUrl/storage/v1/object/public/acars-releases"
$supabaseStorageWriteKey = $env:SUPABASE_SERVICE_ROLE_KEY

if ([string]::IsNullOrWhiteSpace($supabaseStorageWriteKey)) {
    $supabaseStorageWriteKey = Read-KeyFromEnvFile -Path $publishSecretsPath -Key "SUPABASE_SERVICE_ROLE_KEY"
}

if ([string]::IsNullOrWhiteSpace($supabaseStorageWriteKey)) {
    throw "Falta SUPABASE_SERVICE_ROLE_KEY para escribir releases en Supabase storage. Crea installer\.publish-secrets.local o exporta la variable de entorno."
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
}
else {
    "- Version $appVersion con autoupdate por feed generico Supabase`n- Runtime y scripts de publicacion alineados"
}

$manifestObject = [ordered]@{
    version         = $appVersion
    webVersion      = "2.0"
    downloadUrl     = $genericDownloadUrl
    mandatory       = $false
    notes           = $releaseNotes
    releaseDate     = [DateTime]::UtcNow.ToString("yyyy-MM-dd")
    minVersion      = "2.0.5"
    fileSize        = "$sizeMB MB"
    storageProvider = "supabase"
}

$manifestJson = $manifestObject | ConvertTo-Json -Depth 4

$escapedChangelog = [System.Security.SecurityElement]::Escape($releaseNotes)
$xmlContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$appVersion.0</version>
  <url>$genericDownloadUrl</url>
  <changelog>$escapedChangelog</changelog>
  <mandatory>false</mandatory>
</item>
"@

Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $versionedManifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $xmlPath -Value $xmlContent -Encoding UTF8
Set-Content -LiteralPath $versionedXmlPath -Value $xmlContent -Encoding UTF8

$uploadEntries = @(
    @{ objectName = $storageGenericInstallerName; sourcePath = $genericInstallerPath;  preferredContentType = "application/octet-stream" }
    @{ objectName = $storageGenericManifestName; sourcePath = $manifestPath;           preferredContentType = "application/json" }
    @{ objectName = $storageGenericXmlName;      sourcePath = $xmlPath;                preferredContentType = "application/xml" }
    @{ objectName = $storageInstallerName;       sourcePath = $versionedInstallerPath; preferredContentType = "application/octet-stream" }
    @{ objectName = $storageManifestName;        sourcePath = $versionedManifestPath;  preferredContentType = "application/json" }
    @{ objectName = $storageXmlName;             sourcePath = $versionedXmlPath;       preferredContentType = "application/xml" }
)

$uploadScriptPath = Join-Path $env:TEMP "patagonia-acars-upload.js"
$uploadScript = @'
const fs = require("fs");
const path = require("path");
const { createClient } = require(path.join(process.cwd(), "node_modules", "@supabase", "supabase-js"));

const supabase = createClient(process.env.SUPABASE_URL, process.env.SUPABASE_KEY);
const entries = JSON.parse(process.env.UPLOAD_ENTRIES_JSON);

async function uploadWithFallback(bucket, entry) {
  const payload = fs.readFileSync(entry.sourcePath);

  const attempts = [
    { upsert: true, cacheControl: "no-cache", contentType: entry.preferredContentType },
    { upsert: true, cacheControl: "no-cache" },
    { upsert: true, cacheControl: "no-cache", contentType: "application/octet-stream" },
    { upsert: true, cacheControl: "no-cache", contentType: "text/plain" }
  ];

  let lastError = null;

  for (const attempt of attempts) {
    const options = { ...attempt };
    if (!options.contentType) {
      delete options.contentType;
    }

    const { error } = await bucket.upload(entry.objectName, payload, options);
    if (!error) {
      return;
    }

    lastError = error;

    const status = Number(error.status || 0);
    const statusCode = String(error.statusCode || "");
    const message = String(error.message || "").toLowerCase();

    const isMimeProblem =
      status === 400 ||
      status === 415 ||
      statusCode === "400" ||
      statusCode === "415" ||
      message.includes("mime type") ||
      message.includes("not supported");

    if (!isMimeProblem) {
      throw error;
    }
  }

  throw lastError;
}

(async () => {
  const bucket = supabase.storage.from("acars-releases");

  for (const entry of entries) {
    await uploadWithFallback(bucket, entry);
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
Write-Host "Instalador generico:   $storagePublicBase/$storageGenericInstallerName" -ForegroundColor Cyan
Write-Host "Manifest generico:     $storagePublicBase/$storageGenericManifestName" -ForegroundColor Cyan
Write-Host "XML generico:          $storagePublicBase/$storageGenericXmlName" -ForegroundColor Cyan
Write-Host "Instalador versionado: $downloadUrl" -ForegroundColor Cyan
Write-Host "Manifest versionado:   $storagePublicBase/$storageManifestName" -ForegroundColor Cyan
Write-Host "XML versionado:        $storagePublicBase/$storageXmlName" -ForegroundColor Cyan