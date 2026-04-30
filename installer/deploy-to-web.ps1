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

# ── Auto-incremento de revision (YYYY.MM.DD.N) ───────────────────────────────
# Cada ejecucion de deploy-to-web genera una revision unica para ese dia.
# Esto permite que ACARS detecte hotfixes aunque la version visible no cambie.
function Get-NextRevision {
    param([string]$current)
    $today = [DateTime]::UtcNow.ToString("yyyy.M.d")
    if ($current -match "^(\d{4})\.(\d{1,2})\.(\d{1,2})\.(\d+)$") {
        $y,$m,$d,$n = $Matches[1],$Matches[2],$Matches[3],[int]$Matches[4]
        $currentDay = "$y.$m.$d"
        if ($currentDay -eq $today) { return "$today.$($n + 1)" }
    }
    return "$today.1"
}

$currentRevision = $settings["AppRevision"]
if ([string]::IsNullOrWhiteSpace($currentRevision)) { $currentRevision = "2026.1.1.0" }
$newRevision = Get-NextRevision -current $currentRevision

# Actualizar App.config con la nueva revision
try {
    $revNode = $appConfig.configuration.appSettings.add | Where-Object { $_.key -eq "AppRevision" } | Select-Object -First 1
    if ($revNode) {
        $revNode.value = $newRevision
        $appConfig.Save($appConfigPath)
        Write-Host "Revision actualizada: $currentRevision -> $newRevision" -ForegroundColor Green
    }
} catch {
    Write-Host "AVISO: No se pudo actualizar AppRevision en App.config: $_" -ForegroundColor Yellow
    $newRevision = $currentRevision
}
$settings["AppRevision"] = $newRevision

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
$storageGenericChannelName = "channel.json"

$storageInstallerName = "PatagoniaWingsACARSSetup-$appVersion$storageReleaseSuffix.exe"
$storageManifestName = "acars-update-$appVersion$storageReleaseSuffix.json"
$storageXmlName = "autoupdater-$appVersion$storageReleaseSuffix.xml"
$storageChannelName = "channel-$appVersion$storageReleaseSuffix.json"

$genericInstallerPath = Join-Path $webPublic $genericInstallerName
$versionedInstallerPath = Join-Path $webPublic $versionedInstallerName
$manifestPath = Join-Path $webPublic "acars-update.json"
$versionedManifestPath = Join-Path $webPublic "acars-update-$appVersion.json"
$xmlPath = Join-Path $webPublic "autoupdater.xml"
$versionedXmlPath = Join-Path $webPublic "autoupdater-$appVersion.xml"
$channelPath = Join-Path $webPublic "channel.json"
$versionedChannelPath = Join-Path $webPublic "channel-$appVersion.json"

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
    latestVersion   = $appVersion
    currentVersion  = $appVersion
    minSupportedVersion = "1.0.0"
    forceUpdateBelow = $appVersion
    webVersion      = "2.0"
    downloadUrl     = $genericDownloadUrl
    url             = $genericDownloadUrl
    installerUrl    = $genericDownloadUrl
    packageUrl      = $genericDownloadUrl
    directDownloadUrl = $genericDownloadUrl
    mandatory       = $true
    required        = $true
    revision        = $settings["AppRevision"]
    notes           = $releaseNotes
    releaseNotes    = $releaseNotes
    releaseDate     = [DateTime]::UtcNow.ToString("yyyy-MM-dd")
    minVersion      = "1.0.0"
    fileName        = $storageGenericInstallerName
    size            = [int64](Get-Item -LiteralPath $installerSrc).Length
    fileSize        = "$sizeMB MB"
    checksumSha256  = (Get-FileHash -LiteralPath $installerSrc -Algorithm SHA256).Hash
    storageProvider = "supabase"
}

$manifestJson = $manifestObject | ConvertTo-Json -Depth 4

# ── Packages index: SHA256 de cada binario del build ─────────────────────────
$appRevision = $settings["AppRevision"]
$channel     = $settings["UpdateChannel"]

$binSearchRoots = @(
    (Join-Path $root "PatagoniaWings.Acars.Master\bin\x64\Release"),
    (Join-Path $root "release")
)
$binRoot = $binSearchRoots | Where-Object { Test-Path $_ } | Select-Object -First 1

$packageEntries = [System.Collections.Generic.List[object]]::new()
$packageUploadEntries = [System.Collections.Generic.List[object]]::new()

if ($binRoot) {
    $binFiles = Get-ChildItem -LiteralPath $binRoot -Recurse -File |
        Where-Object { $_.Extension -match "\.(exe|dll|config|xml|json|png|svg|html|css|js)$" -and
                       $_.Name -notmatch "^vshost" }

    $storageFilesBase = "packages/$channel/$appVersion/$appRevision/files"

    foreach ($f in $binFiles) {
        $relPath = $f.FullName.Substring($binRoot.Length).TrimStart('\','/')
        $sha256  = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLower()
        $fileUrl = "$storagePublicBase/$storageFilesBase/$($relPath.Replace('\','/'))"
        $ext     = $f.Extension.ToLower()
        $needsRestart = ($ext -in @(".exe",".dll",".config"))

        $packageEntries.Add([ordered]@{
            path            = $relPath.Replace('\','/')
            url             = $fileUrl
            sha256          = $sha256
            size            = $f.Length
            restartRequired = $needsRestart
            updateMode      = if ($needsRestart) { "restart" } else { "hot" }
        })

        $packageUploadEntries.Add([ordered]@{
            objectName          = "$storageFilesBase/$($relPath.Replace('\','/'))"
            sourcePath          = $f.FullName
            preferredContentType = switch ($ext) {
                ".json" { "application/json" }
                ".xml"  { "application/xml" }
                ".html" { "text/html" }
                ".css"  { "text/css" }
                ".js"   { "application/javascript" }
                ".png"  { "image/png" }
                ".svg"  { "image/svg+xml" }
                default { "application/octet-stream" }
            }
        })
    }
    Write-Host "Packages index: $($packageEntries.Count) archivos desde $binRoot" -ForegroundColor Cyan
} else {
    Write-Host "AVISO: No se encontro directorio de binarios. packages/index.json se genera vacio." -ForegroundColor Yellow
}

$packagesIndexObj = [ordered]@{
    channel     = $channel
    version     = $appVersion
    revision    = $appRevision
    releaseDate = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    totalFiles  = $packageEntries.Count
    packages    = $packageEntries
    deleted     = @()
}
$packagesIndexJson = $packagesIndexObj | ConvertTo-Json -Depth 6

$packagesIndexLocalPath = Join-Path $webPublic "packages-index-$appVersion.json"
Set-Content -LiteralPath $packagesIndexLocalPath -Value $packagesIndexJson -Encoding UTF8

$packagesIndexUrl = "$storagePublicBase/packages/index.json"

# ── channel.json apunta al packages/index.json real ──────────────────────────
$channelObject = [ordered]@{
    channel          = $channel
    version          = $appVersion
    latestVersion    = $appVersion
    currentVersion   = $appVersion
    minSupportedVersion = "1.0.0"
    forceUpdateBelow = $appVersion
    revision         = $appRevision
    latestRevision   = $appRevision
    manifestUrl      = $packagesIndexUrl
    packagesIndexUrl = $packagesIndexUrl
    installerUrl     = $genericDownloadUrl
    downloadUrl      = $genericDownloadUrl
    url              = $genericDownloadUrl
    packageUrl       = $genericDownloadUrl
    directDownloadUrl = $genericDownloadUrl
    forceUpdate      = $false
    mandatory        = $true
    required         = $true
    notes            = $releaseNotes
    releaseNotes     = $releaseNotes
    releaseDate      = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
}

$channelJson = $channelObject | ConvertTo-Json -Depth 4

$escapedChangelog = [System.Security.SecurityElement]::Escape($releaseNotes)
$xmlContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$appVersion.0</version>
  <latestVersion>$appVersion</latestVersion>
  <minSupportedVersion>1.0.0</minSupportedVersion>
  <forceUpdateBelow>$appVersion</forceUpdateBelow>
  <url>$genericDownloadUrl</url>
  <downloadUrl>$genericDownloadUrl</downloadUrl>
  <installerUrl>$genericDownloadUrl</installerUrl>
  <directDownloadUrl>$genericDownloadUrl</directDownloadUrl>
  <revision>$appRevision</revision>
  <changelog>$escapedChangelog</changelog>
  <releaseNotes>$escapedChangelog</releaseNotes>
  <mandatory>true</mandatory>
  <required>true</required>
</item>
"@

Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $versionedManifestPath -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath $xmlPath -Value $xmlContent -Encoding UTF8
Set-Content -LiteralPath $versionedXmlPath -Value $xmlContent -Encoding UTF8
Set-Content -LiteralPath $channelPath -Value $channelJson -Encoding UTF8
Set-Content -LiteralPath $versionedChannelPath -Value $channelJson -Encoding UTF8

$coreUploadEntries = @(
    @{ objectName = $storageGenericInstallerName;    sourcePath = $genericInstallerPath;       preferredContentType = "application/octet-stream" }
    @{ objectName = $storageGenericManifestName;     sourcePath = $manifestPath;               preferredContentType = "application/json" }
    @{ objectName = $storageGenericXmlName;          sourcePath = $xmlPath;                    preferredContentType = "application/xml" }
    @{ objectName = $storageGenericChannelName;      sourcePath = $channelPath;                preferredContentType = "application/json" }
    @{ objectName = $storageInstallerName;           sourcePath = $versionedInstallerPath;     preferredContentType = "application/octet-stream" }
    @{ objectName = $storageManifestName;            sourcePath = $versionedManifestPath;      preferredContentType = "application/json" }
    @{ objectName = $storageXmlName;                 sourcePath = $versionedXmlPath;           preferredContentType = "application/xml" }
    @{ objectName = $storageChannelName;             sourcePath = $versionedChannelPath;       preferredContentType = "application/json" }
    @{ objectName = "packages/index.json";           sourcePath = $packagesIndexLocalPath;     preferredContentType = "application/json" }
    @{ objectName = "packages/index-$appVersion-$appRevision.json"; sourcePath = $packagesIndexLocalPath; preferredContentType = "application/json" }
)

# Combinar entradas core + archivos individuales del packages index
$uploadEntries = [System.Collections.Generic.List[object]]::new()
foreach ($e in $coreUploadEntries) { $uploadEntries.Add($e) }
foreach ($e in $packageUploadEntries) { $uploadEntries.Add($e) }

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

# ── Notificar a la Web API para que actualice la tabla acars_release_manifest ─
# Esto hace que ACARS detecte el cambio en tiempo real via /api/acars/manifest,
# sin esperar a que Vercel redeploy el proximo channel.json estatico.
$webApiBase = $settings["WebApiBase"]
if ([string]::IsNullOrWhiteSpace($webApiBase)) {
    $webApiBase = "https://patagonia-wings-site.vercel.app"
}

$manifestPostPayload = $channelObject | ConvertTo-Json -Depth 6 -Compress
try {
    $apiHeaders = @{
        "Content-Type"  = "application/json"
        "Authorization" = "Bearer $supabaseStorageWriteKey"
    }
    $resp = Invoke-RestMethod `
        -Uri "$webApiBase/api/acars/manifest" `
        -Method POST `
        -Headers $apiHeaders `
        -Body $manifestPostPayload `
        -TimeoutSec 15 `
        -ErrorAction Stop
    Write-Host "acars_release_manifest actualizado: version=$($resp.version) revision=$($resp.revision)" -ForegroundColor Green
} catch {
    Write-Host "AVISO: No se pudo notificar a /api/acars/manifest: $_" -ForegroundColor Yellow
    Write-Host "       El channel.json estatico en Supabase Storage sigue siendo la fuente de verdad." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Deploy ACARS completado." -ForegroundColor Green
Write-Host "Version:  $appVersion  |  Revision: $newRevision" -ForegroundColor Green
Write-Host "Packages index: $packagesIndexUrl" -ForegroundColor Cyan
Write-Host "Channel:        $storagePublicBase/$storageGenericChannelName" -ForegroundColor Cyan
Write-Host "Instalador:     $storagePublicBase/$storageGenericInstallerName" -ForegroundColor Cyan
Write-Host ""
Write-Host "ACARS detectara hotfixes automaticamente en la proxima consulta (max 30 min)." -ForegroundColor Green
