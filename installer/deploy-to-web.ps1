# ============================================================================
# Patagonia Wings ACARS - Deploy installer to web public folder
# Copies PatagoniaWingsACARSSetup.exe into the Next.js public/downloads folder
# ============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$appVersion = "3.2.4"
$releaseVersioned = Join-Path $PSScriptRoot "..\\release\\PatagoniaWingsACARSSetup-$appVersion.exe"
$releaseGeneric = Join-Path $PSScriptRoot "..\\release\\PatagoniaWingsACARSSetup.exe"
$installerSrc = if (Test-Path -LiteralPath $releaseVersioned) { $releaseVersioned } else { $releaseGeneric }
$webPublic = "C:\\Users\\lizam\\Desktop\\PatagoniaWingsACARS\\PATAGONIA WINGS WEB 2.0\\patagonia-wings-site\\public\\downloads"
$dest = Join-Path $webPublic "PatagoniaWingsACARSSetup.exe"
$manifestDest = Join-Path $webPublic "acars-update.json"
$xmlDest = Join-Path $webPublic "autoupdater.xml"
$appConfigPath = Join-Path $PSScriptRoot "..\\PatagoniaWings.Acars.Master\\App.config"
$supabaseBase = "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases"

Write-Host ""
Write-Host "Patagonia Wings - Deploy ACARS installer to web" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path -LiteralPath $installerSrc)) {
    Write-Host "Installer not found: $installerSrc" -ForegroundColor Red
    Write-Host "Run build-release.ps1 first." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path -LiteralPath $webPublic)) {
    New-Item -ItemType Directory -Path $webPublic -Force | Out-Null
    Write-Host "Created folder: $webPublic" -ForegroundColor Green
}

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

Copy-Item -LiteralPath $installerSrc -Destination $dest -Force

$sizeMB = [math]::Round((Get-Item -LiteralPath $dest).Length / 1MB, 1)
Write-Host "Copied: $dest ($sizeMB MB)" -ForegroundColor Green

$manifest = [ordered]@{
    version = $appVersion
    downloadUrl = "$supabaseBase/PatagoniaWingsACARSSetup.exe"
    notes = "Nueva version disponible de Patagonia Wings ACARS."
    mandatory = $false
    publishedAtUtc = [DateTime]::UtcNow.ToString("o")
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestDest -Encoding UTF8
Write-Host "Manifest updated: $manifestDest" -ForegroundColor Green

$xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$appVersion.0</version>
  <url>$supabaseBase/PatagoniaWingsACARSSetup.exe</url>
  <changelog>Patagonia Wings ACARS v$appVersion</changelog>
  <mandatory>false</mandatory>
</item>
"@
$xml | Set-Content -LiteralPath $xmlDest -Encoding UTF8
Write-Host "Manifest updated: $xmlDest" -ForegroundColor Green
Write-Host ""
Write-Host "Installer available at:" -ForegroundColor White
Write-Host "  /downloads/PatagoniaWingsACARSSetup.exe" -ForegroundColor Cyan
Write-Host "  $supabaseBase/PatagoniaWingsACARSSetup.exe" -ForegroundColor Cyan
Write-Host "  /downloads/acars-update.json" -ForegroundColor Cyan
Write-Host "  /downloads/autoupdater.xml" -ForegroundColor Cyan
