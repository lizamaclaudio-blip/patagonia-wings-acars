Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-AppSettingValue {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$Config,
        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $node = $Config.configuration.appSettings.add | Where-Object { $_.key -eq $Key } | Select-Object -First 1
    if ($null -eq $node) { return "" }
    return [string]$node.value
}

function Normalize-Version {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "0.0.0.0" }
    $parts = $Value.Trim().Split(".")
    $out = @()
    for ($i = 0; $i -lt 4; $i++) {
        if ($i -lt $parts.Length -and $parts[$i] -match "^\d+") { $out += $Matches[0] } else { $out += "0" }
    }
    return ($out -join ".")
}

function Is-NewerVersion {
    param(
        [string]$Available,
        [string]$Installed
    )
    try {
        $a = [Version](Normalize-Version $Available)
        $b = [Version](Normalize-Version $Installed)
        return ($a.CompareTo($b) -gt 0)
    } catch {
        return $true
    }
}

function Pick-FirstValue {
    param(
        [Parameter(Mandatory = $true)]$Obj,
        [Parameter(Mandatory = $true)][string[]]$Names
    )
    foreach ($name in $Names) {
        $prop = $Obj.PSObject.Properties[$name]
        if ($null -ne $prop) {
            $value = [string]$prop.Value
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }
    return ""
}

function Parse-JsonSafe {
    param([Parameter(Mandatory = $true)]$Raw)
    $clean = ""
    if ($Raw -is [byte[]]) {
        $clean = [Text.Encoding]::UTF8.GetString($Raw)
    }
    else {
        $clean = [string]$Raw
    }
    if (-not [string]::IsNullOrEmpty($clean)) {
        $clean = $clean.TrimStart([char]0xFEFF, [char]0x200B, [char]0x0000)
    }
    return ($clean | ConvertFrom-Json)
}

$process = Get-Process | Where-Object { $_.ProcessName -eq "PatagoniaWings.Acars.Master" } | Select-Object -First 1
$installedExePath = if ($process) { $process.Path } else { "" }

if ([string]::IsNullOrWhiteSpace($installedExePath)) {
    $defaultPath = Join-Path $env:LOCALAPPDATA "Programs\\PatagoniaWings\\ACARS\\PatagoniaWings.Acars.Master.exe"
    if (Test-Path $defaultPath) {
        $installedExePath = $defaultPath
    }
}

$installedFolder = if ($installedExePath) { Split-Path $installedExePath -Parent } else { "" }
$installedConfigPath = if ($installedFolder) { Join-Path $installedFolder "PatagoniaWings.Acars.Master.exe.config" } else { "" }

$installedVersion = ""
$manifestUrl = ""
$xmlUrl = ""
$channelUrl = ""
$installerUrl = ""
$errorText = ""
$feedVersion = ""
$feedRevision = ""
$feedMandatory = $false
$feedDownloadUrl = ""
$updateAvailable = $false
$lastUpdateCheckTime = ""

if ($installedExePath -and (Test-Path $installedExePath)) {
    $exeItem = Get-Item $installedExePath
    $installedVersion = [string]$exeItem.VersionInfo.ProductVersion
}

if ($installedConfigPath -and (Test-Path $installedConfigPath)) {
    [xml]$cfg = Get-Content -LiteralPath $installedConfigPath
    $appVersion = Get-AppSettingValue -Config $cfg -Key "AppVersion"
    if (-not [string]::IsNullOrWhiteSpace($appVersion)) { $installedVersion = $appVersion }
    $manifestUrl = Get-AppSettingValue -Config $cfg -Key "UpdateManifestUrl"
    $xmlUrl = Get-AppSettingValue -Config $cfg -Key "AutoUpdaterXmlUrl"
    $channelUrl = Get-AppSettingValue -Config $cfg -Key "UpdateChannelUrl"
    $installerUrl = Get-AppSettingValue -Config $cfg -Key "InstallerDownloadUrl"
}

$logCandidates = @(
    (Join-Path $env:LOCALAPPDATA "PatagoniaWings\\ACARS"),
    (Join-Path $env:APPDATA "PatagoniaWings\\ACARS"),
    (Join-Path $installedFolder "logs")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

$logFiles = @()
foreach ($dir in $logCandidates) {
    $logFiles += Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "update|updater|acars|log" }
}
$lastLog = $logFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($lastLog) { $lastUpdateCheckTime = $lastLog.LastWriteTime.ToString("s") }

try {
    if (-not [string]::IsNullOrWhiteSpace($channelUrl)) {
        $r = Invoke-WebRequest -UseBasicParsing $channelUrl
        $j = Parse-JsonSafe -Raw $r.Content
        $feedVersion = Pick-FirstValue -Obj $j -Names @("version","latestVersion")
        $feedRevision = Pick-FirstValue -Obj $j -Names @("revision","latestRevision")
        $mandatoryRaw = Pick-FirstValue -Obj $j -Names @("mandatory","forceUpdate")
        $feedMandatory = [string]::Equals($mandatoryRaw, "true", [System.StringComparison]::OrdinalIgnoreCase)
        $feedDownloadUrl = Pick-FirstValue -Obj $j -Names @("installerUrl","downloadUrl","url")
    }
    elseif (-not [string]::IsNullOrWhiteSpace($manifestUrl)) {
        $r = Invoke-WebRequest -UseBasicParsing $manifestUrl
        $j = Parse-JsonSafe -Raw $r.Content
        $feedVersion = Pick-FirstValue -Obj $j -Names @("version","latestVersion","currentVersion")
        $feedRevision = Pick-FirstValue -Obj $j -Names @("revision")
        $mandatoryRaw = Pick-FirstValue -Obj $j -Names @("mandatory","required")
        $feedMandatory = [string]::Equals($mandatoryRaw, "true", [System.StringComparison]::OrdinalIgnoreCase)
        $feedDownloadUrl = Pick-FirstValue -Obj $j -Names @("downloadUrl","url","installerUrl")
    }
    $updateAvailable = Is-NewerVersion -Available $feedVersion -Installed $installedVersion
}
catch {
    $errorText = $_.Exception.Message
}

$downloadStatus = ""
if (-not [string]::IsNullOrWhiteSpace($feedDownloadUrl)) {
    try {
        $dr = Invoke-WebRequest -UseBasicParsing -Method Head $feedDownloadUrl
        $downloadStatus = "HTTP $($dr.StatusCode)"
    }
    catch {
        $downloadStatus = "ERROR: $($_.Exception.Message)"
    }
}

$result = [ordered]@{
    InstalledExePath     = $installedExePath
    InstalledConfigPath  = $installedConfigPath
    CurrentVersion       = $installedVersion
    ManifestUrl          = $manifestUrl
    AutoUpdaterXmlUrl    = $xmlUrl
    ChannelUrl           = $channelUrl
    LastUpdateCheckTime  = $lastUpdateCheckTime
    LatestVersionFromFeed = $feedVersion
    LatestRevisionFromFeed = $feedRevision
    MandatoryFromFeed    = $feedMandatory
    UpdateAvailable      = $updateAvailable
    DownloadUrl          = $feedDownloadUrl
    DownloadUrlStatus    = $downloadStatus
    Error                = $errorText
}

$result.GetEnumerator() | ForEach-Object { "{0}: {1}" -f $_.Key, $_.Value }
