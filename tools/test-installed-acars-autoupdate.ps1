Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$manifestUrl = "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/acars-update.json"
$versionsToTest = @("1.0.0","6.0.1","6.0.2","6.0.3","7.0.0","7.0.1","7.0.2","7.0.3","6.01","ACARS 6.0 listo","legacy-invalid")

function Parse-JsonSafe {
    param([Parameter(Mandatory = $true)]$Raw)
    $text = if ($Raw -is [byte[]]) { [Text.Encoding]::UTF8.GetString($Raw) } else { [string]$Raw }
    $text = $text.TrimStart([char]0xFEFF, [char]0x200B, [char]0x0000)
    return ($text | ConvertFrom-Json)
}

function Normalize-Version {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "0.0.0.0" }
    $raw = $Value.Trim()
    $idx = -1
    for ($i=0; $i -lt $raw.Length; $i++) {
        if ([char]::IsDigit($raw[$i])) { $idx = $i; break }
    }
    if ($idx -gt 0) { $raw = $raw.Substring($idx) }
    $suffix = $raw.IndexOfAny(@('-','+',' '))
    if ($suffix -ge 0) { $raw = $raw.Substring(0, $suffix) }
    $parts = $raw.Split('.')
    if ($parts.Length -eq 2 -and $parts[1].Length -gt 1 -and $parts[1].StartsWith('0')) {
        $patch = $parts[1].TrimStart('0')
        if ([string]::IsNullOrWhiteSpace($patch)) { $patch = "0" }
        $parts = @($parts[0], "0", $patch)
    }
    $out = @()
    for ($i=0; $i -lt 4; $i++) {
        if ($i -lt $parts.Length -and $parts[$i] -match '^\d+') { $out += $Matches[0] } else { $out += "0" }
    }
    return ($out -join '.')
}

function Is-NewerVersion {
    param([string]$Available,[string]$Installed)
    try {
        $a = [Version](Normalize-Version $Available)
        $b = [Version](Normalize-Version $Installed)
        return ($a.CompareTo($b) -gt 0)
    } catch {
        return $true
    }
}

Write-Host "ManifestUrl: $manifestUrl"
$manifestResponse = Invoke-WebRequest -UseBasicParsing $manifestUrl
Write-Host "ManifestStatus: $($manifestResponse.StatusCode)"
$manifest = Parse-JsonSafe -Raw $manifestResponse.Content

$latestVersion = [string]($manifest.latestVersion)
if ([string]::IsNullOrWhiteSpace($latestVersion)) { $latestVersion = [string]($manifest.version) }
$downloadUrl = [string]($manifest.downloadUrl)
if ([string]::IsNullOrWhiteSpace($downloadUrl)) { $downloadUrl = [string]($manifest.url) }
if ([string]::IsNullOrWhiteSpace($downloadUrl)) { throw "Manifest sin downloadUrl/url" }

Write-Host "LatestVersion: $latestVersion"
Write-Host "DownloadUrl: $downloadUrl"

foreach ($v in $versionsToTest) {
    $available = Is-NewerVersion -Available $latestVersion -Installed $v
    Write-Host ("COMPARE {0} -> {1} | updateAvailable={2}" -f $v, $latestVersion, $available)
}

$tempDir = Join-Path $env:TEMP "pw-acars-installed-update-test"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$targetPath = Join-Path $tempDir ("PatagoniaWingsACARSSetup-test-" + $latestVersion + ".exe")
Invoke-WebRequest -UseBasicParsing $downloadUrl -OutFile $targetPath

$file = Get-Item $targetPath
$bytes = [System.IO.File]::ReadAllBytes($targetPath)
$isMZ = ($bytes.Length -ge 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
Write-Host "DownloadedPath: $($file.FullName)"
Write-Host "DownloadedSizeBytes: $($file.Length)"
Write-Host "IsExeMZ: $isMZ"

if ($file.Length -le 0 -or -not $isMZ) {
    throw "FAIL: instalador invalido (size/MZ)."
}

Write-Host "RESULT: OK"
