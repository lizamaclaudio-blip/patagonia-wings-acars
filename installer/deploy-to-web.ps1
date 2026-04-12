# ============================================================================
# Patagonia Wings ACARS - Deploy installer to web public folder
# Copies PatagoniaWingsACARSSetup.exe into the Next.js public/downloads folder
# ============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installerSrc = Join-Path $PSScriptRoot "..\\release\\PatagoniaWingsACARSSetup.exe"
$webPublic = "C:\\Users\\lizam\\Desktop\\PatagoniaWingsACARS\\PATAGONIA WINGS WEB 2.0\\patagonia-wings-site\\public\\downloads"
$dest = Join-Path $webPublic "PatagoniaWingsACARSSetup.exe"
$manifestDest = Join-Path $webPublic "acars-update.json"
$appConfigPath = Join-Path $PSScriptRoot "..\\PatagoniaWings.Acars.Master\\App.config"
$appVersion = "2.0.1"

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
    downloadUrl = "https://www.patagoniaw.com/downloads/PatagoniaWingsACARSSetup.exe"
    notes = "Nueva version disponible de Patagonia Wings ACARS."
    mandatory = $false
    publishedAtUtc = [DateTime]::UtcNow.ToString("o")
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestDest -Encoding UTF8
Write-Host "Manifest updated: $manifestDest" -ForegroundColor Green
Write-Host ""
Write-Host "Installer available at:" -ForegroundColor White
Write-Host "  /downloads/PatagoniaWingsACARSSetup.exe" -ForegroundColor Cyan
Write-Host "  /downloads/acars-update.json" -ForegroundColor Cyan
