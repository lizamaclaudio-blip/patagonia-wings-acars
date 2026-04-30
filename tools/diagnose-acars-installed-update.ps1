Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-AppSettingValue {
    param([Parameter(Mandatory = $true)][xml]$Config,[Parameter(Mandatory = $true)][string]$Key)
    $node = $Config.configuration.appSettings.add | Where-Object { $_.key -eq $Key } | Select-Object -First 1
    if ($null -eq $node) { return "" }
    return [string]$node.value
}

function Parse-JsonSafe {
    param([Parameter(Mandatory = $true)]$Raw)
    $text = if ($Raw -is [byte[]]) { [Text.Encoding]::UTF8.GetString($Raw) } else { [string]$Raw }
    $text = $text.TrimStart([char]0xFEFF, [char]0x200B, [char]0x0000)
    return ($text | ConvertFrom-Json)
}

function Pick-FirstValue {
    param([Parameter(Mandatory = $true)]$Obj,[Parameter(Mandatory = $true)][string[]]$Names)
    foreach ($name in $Names) {
        $prop = $Obj.PSObject.Properties[$name]
        if ($null -ne $prop) {
            $value = [string]$prop.Value
            if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
        }
    }
    return ""
}

$process = Get-Process | Where-Object { $_.ProcessName -eq "PatagoniaWings.Acars.Master" } | Select-Object -First 1
$exePath = if ($process) { $process.Path } else { "" }
if ([string]::IsNullOrWhiteSpace($exePath)) {
    $fallback = Join-Path $env:LOCALAPPDATA "Programs\\PatagoniaWings\\ACARS\\PatagoniaWings.Acars.Master.exe"
    if (Test-Path $fallback) { $exePath = $fallback }
}

$configPath = if ($exePath) { Join-Path (Split-Path $exePath -Parent) "PatagoniaWings.Acars.Master.exe.config" } else { "" }
$installedVersion = ""
$manifestUrl = ""
$channelUrl = ""
$installerUrl = ""
$latestVersion = ""
$downloadUrl = ""
$updateAvailable = $false
$errorText = ""

if ($exePath -and (Test-Path $exePath)) {
    $installedVersion = [string](Get-Item $exePath).VersionInfo.ProductVersion
}

if ($configPath -and (Test-Path $configPath)) {
    [xml]$cfg = Get-Content -LiteralPath $configPath
    $appVersion = Get-AppSettingValue -Config $cfg -Key "AppVersion"
    if (-not [string]::IsNullOrWhiteSpace($appVersion)) { $installedVersion = $appVersion }
    $manifestUrl = Get-AppSettingValue -Config $cfg -Key "UpdateManifestUrl"
    $channelUrl = Get-AppSettingValue -Config $cfg -Key "UpdateChannelUrl"
    $installerUrl = Get-AppSettingValue -Config $cfg -Key "InstallerDownloadUrl"
}

try {
    if (-not [string]::IsNullOrWhiteSpace($channelUrl)) {
        $ch = Parse-JsonSafe -Raw (Invoke-WebRequest -UseBasicParsing $channelUrl).Content
        $latestVersion = Pick-FirstValue -Obj $ch -Names @("latestVersion","version")
        $downloadUrl = Pick-FirstValue -Obj $ch -Names @("downloadUrl","installerUrl","url")
    }
    if ([string]::IsNullOrWhiteSpace($latestVersion) -and -not [string]::IsNullOrWhiteSpace($manifestUrl)) {
        $mf = Parse-JsonSafe -Raw (Invoke-WebRequest -UseBasicParsing $manifestUrl).Content
        $latestVersion = Pick-FirstValue -Obj $mf -Names @("latestVersion","version","currentVersion")
        $downloadUrl = Pick-FirstValue -Obj $mf -Names @("downloadUrl","installerUrl","url")
    }
    if (-not [string]::IsNullOrWhiteSpace($latestVersion) -and -not [string]::IsNullOrWhiteSpace($installedVersion)) {
        try {
            $updateAvailable = ([Version]($latestVersion.Split(' ')[0]) -gt [Version]($installedVersion.Split(' ')[0]))
        } catch {
            $updateAvailable = $true
        }
    }
}
catch {
    $errorText = $_.Exception.Message
}

$logRoot = Join-Path $env:APPDATA "PatagoniaWings\\Acars\\logs"
$lastLog = if (Test-Path $logRoot) { Get-ChildItem $logRoot -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1 } else { $null }

$result = [ordered]@{
    ExePath = $exePath
    ConfigPath = $configPath
    InstalledVersion = $installedVersion
    ManifestUrl = $manifestUrl
    LatestVersion = $latestVersion
    UpdateAvailable = $updateAvailable
    DownloadUrl = $downloadUrl
    InstallerDownloadUrlConfig = $installerUrl
    LastUpdateLog = if ($lastLog) { $lastLog.FullName } else { "" }
    LastUpdateLogTime = if ($lastLog) { $lastLog.LastWriteTime.ToString("s") } else { "" }
    Error = $errorText
}

$result.GetEnumerator() | ForEach-Object { "{0}: {1}" -f $_.Key, $_.Value }
