Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$manifestUrl = "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/acars-update.json"

function Parse-JsonSafe {
    param([Parameter(Mandatory = $true)]$Raw)
    $text = if ($Raw -is [byte[]]) { [Text.Encoding]::UTF8.GetString($Raw) } else { [string]$Raw }
    $text = $text.TrimStart([char]0xFEFF, [char]0x200B, [char]0x0000)
    return ($text | ConvertFrom-Json)
}

Write-Host "ManifestUrl: $manifestUrl"
$manifestResponse = Invoke-WebRequest -UseBasicParsing $manifestUrl
Write-Host "ManifestStatus: $($manifestResponse.StatusCode)"
$manifest = Parse-JsonSafe -Raw $manifestResponse.Content

$latestVersion = [string]($manifest.version)
$downloadUrl = [string]($manifest.downloadUrl)
if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
    throw "Manifest sin downloadUrl"
}

Write-Host "LatestVersion: $latestVersion"
Write-Host "DownloadUrl: $downloadUrl"

$tempDir = Join-Path $env:TEMP "pw-acars-update-test"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$targetPath = Join-Path $tempDir ("PatagoniaWingsACARSSetup-test-" + $latestVersion + ".exe")

Invoke-WebRequest -UseBasicParsing $downloadUrl -OutFile $targetPath
Write-Host "DownloadStatus: 200"

$file = Get-Item $targetPath
Write-Host "DownloadedPath: $($file.FullName)"
Write-Host "DownloadedSizeBytes: $($file.Length)"

if ($file.Length -le 0) {
    throw "FAIL: archivo descargado vacio"
}

Write-Host "RESULT: OK"
