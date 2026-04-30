Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$manifestUrl = "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/acars-update.json"
$latestTarget = "7.0.9"

function Normalize-Version {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "0.0.0.0" }
    $raw = $Value.Trim()
    $raw = $raw -replace "(?i)^acars\s*", ""
    $raw = $raw -replace "(?i)^v", ""
    $parts = $raw.Split(".")
    $out = @()
    for ($i = 0; $i -lt 4; $i++) {
        if ($i -lt $parts.Length -and $parts[$i] -match "^\d+") { $out += $Matches[0] } else { $out += "0" }
    }
    return ($out -join ".")
}

function Should-Update {
    param([string]$Installed, [string]$Latest)
    try {
        $i = [Version](Normalize-Version $Installed)
        $l = [Version](Normalize-Version $Latest)
        if ($i -gt $l) { return $false }
        if ($i -eq $l) { return $false }
        return $true
    }
    catch {
        return $true
    }
}

function Parse-JsonSafe {
    param($Raw)
    $text = if ($Raw -is [byte[]]) { [Text.Encoding]::UTF8.GetString($Raw) } else { [string]$Raw }
    $text = $text.TrimStart([char]0xFEFF, [char]0x200B, [char]0x0000)
    return ($text | ConvertFrom-Json)
}

$manifestResponse = Invoke-WebRequest -UseBasicParsing $manifestUrl
Write-Host "ManifestStatus: $($manifestResponse.StatusCode)"
if ($manifestResponse.StatusCode -ne 200) { throw "Manifest no disponible" }

$manifest = Parse-JsonSafe -Raw $manifestResponse.Content
$latestVersion = [string]($manifest.latestVersion)
if ([string]::IsNullOrWhiteSpace($latestVersion)) { $latestVersion = [string]$manifest.version }
if ([string]::IsNullOrWhiteSpace($latestVersion)) { $latestVersion = $latestTarget }

$downloadUrl = [string]$manifest.downloadUrl
if ([string]::IsNullOrWhiteSpace($downloadUrl)) { $downloadUrl = [string]$manifest.url }
if ([string]::IsNullOrWhiteSpace($downloadUrl)) { $downloadUrl = [string]$manifest.installerUrl }
if ([string]::IsNullOrWhiteSpace($downloadUrl)) { throw "Manifest sin URL de descarga" }

Write-Host "LatestVersion: $latestVersion"
Write-Host "DownloadUrl: $downloadUrl"

$matrix = @(
    @{ Installed = "1.0.0"; Expected = $true },
    @{ Installed = "2.0.0"; Expected = $true },
    @{ Installed = "5.0.0"; Expected = $true },
    @{ Installed = "6.0.1"; Expected = $true },
    @{ Installed = "6.0.2"; Expected = $true },
    @{ Installed = "6.0.3"; Expected = $true },
    @{ Installed = "7.0.0"; Expected = $true },
    @{ Installed = "7.0.1"; Expected = $true },
    @{ Installed = "7.0.2"; Expected = $true },
    @{ Installed = "7.0.3"; Expected = $true },
    @{ Installed = "7.0.4"; Expected = $true },
    @{ Installed = "7.0.5"; Expected = $true },
    @{ Installed = "7.0.7"; Expected = $true },
    @{ Installed = "7.0.8"; Expected = $true },
    @{ Installed = "7.0.9"; Expected = $false },
    @{ Installed = "ACARS 6.0 listo"; Expected = $true }
)

$allOk = $true
foreach ($row in $matrix) {
    $actual = Should-Update -Installed $row.Installed -Latest $latestVersion
    $ok = ($actual -eq $row.Expected)
    if (-not $ok) { $allOk = $false }
    Write-Host ("MATRIX {0} -> {1} | expected={2} actual={3} | {4}" -f $row.Installed, $latestVersion, $row.Expected, $actual, ($(if($ok){"OK"}else{"FAIL"})))
}

$head = Invoke-WebRequest -UseBasicParsing -Method Head $downloadUrl
Write-Host "DownloadHeadStatus: $($head.StatusCode)"
if ($head.StatusCode -ne 200) { throw "Download URL no responde 200" }

$tempFile = Join-Path $env:TEMP "pw-acars-matrix-download.exe"
Invoke-WebRequest -UseBasicParsing $downloadUrl -OutFile $tempFile
$file = Get-Item $tempFile
Write-Host "DownloadedSizeBytes: $($file.Length)"
if ($file.Length -le 0) { throw "EXE descargado vacio" }

$firstBytes = [System.IO.File]::ReadAllBytes($tempFile)[0..1]
$isMz = ($firstBytes[0] -eq 0x4D -and $firstBytes[1] -eq 0x5A)
Write-Host "IsExeMZ: $isMz"
if (-not $isMz) { throw "El archivo descargado no parece EXE (posible HTML/error page)." }

if (-not $allOk) {
    throw "FAIL: la matriz de versiones no cumple la regla."
}

Write-Host "RESULT: OK"
