[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BuildGradlePath,

    [Parameter(Mandatory)]
    [ValidateRange(1, 2100000000)]
    [int] $VersionCode,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $VersionName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $BuildGradlePath).Path
$content = Get-Content -LiteralPath $resolved -Raw

$codeMatches = [Regex]::Matches($content, '(?m)^\s*versionCode\s+\d+\s*$')
$nameMatches = [Regex]::Matches($content, '(?m)^\s*versionName\s+"[^"]+"\s*$')
if ($codeMatches.Count -ne 1 -or $nameMatches.Count -ne 1) {
    throw "Expected exactly one versionCode and versionName in generated build.gradle; Capacitor template may have changed."
}

$content = [Regex]::Replace($content, '(?m)^(\s*)versionCode\s+\d+\s*$', "`${1}versionCode $VersionCode")
$content = [Regex]::Replace($content, '(?m)^(\s*)versionName\s+"[^"]+"\s*$', "`${1}versionName `"$VersionName`"")
[IO.File]::WriteAllText($resolved, $content, (New-Object Text.UTF8Encoding($false)))
Write-Host "Set Android versionName=$VersionName and versionCode=$VersionCode."
