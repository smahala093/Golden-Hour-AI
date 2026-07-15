[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $ManifestPath).Path
[xml] $document = Get-Content -LiteralPath $resolved -Raw
$androidNamespace = 'http://schemas.android.com/apk/res/android'
$manifest = $document.manifest

if (-not $manifest) {
    throw "The generated Android manifest is missing its root element."
}

function Get-AndroidName {
    param([Xml.XmlElement] $Element)
    return $Element.GetAttribute('name', $androidNamespace)
}

foreach ($permissionNode in @($manifest.'uses-permission')) {
    if ((Get-AndroidName $permissionNode) -in @(
        'android.permission.ACCESS_BACKGROUND_LOCATION',
        'android.permission.READ_EXTERNAL_STORAGE',
        'android.permission.WRITE_EXTERNAL_STORAGE'
    )) {
        [void] $manifest.RemoveChild($permissionNode)
    }
}

$requiredPermissions = @(
    'android.permission.INTERNET',
    'android.permission.RECORD_AUDIO',
    'android.permission.ACCESS_COARSE_LOCATION',
    'android.permission.ACCESS_FINE_LOCATION'
)

foreach ($permission in $requiredPermissions) {
    $exists = @($manifest.'uses-permission') | Where-Object { (Get-AndroidName $_) -eq $permission }
    if (-not $exists) {
        $node = $document.CreateElement('uses-permission')
        $node.SetAttribute('name', $androidNamespace, $permission)
        [void] $manifest.InsertBefore($node, $manifest.application)
    }
}

$optionalFeatures = @(
    'android.hardware.microphone',
    'android.hardware.location',
    'android.hardware.location.gps'
)
foreach ($feature in $optionalFeatures) {
    $exists = @($manifest.'uses-feature') | Where-Object { (Get-AndroidName $_) -eq $feature }
    if (-not $exists) {
        $node = $document.CreateElement('uses-feature')
        $node.SetAttribute('name', $androidNamespace, $feature)
        $node.SetAttribute('required', $androidNamespace, 'false')
        [void] $manifest.InsertBefore($node, $manifest.application)
    }
}

$manifest.application.SetAttribute('usesCleartextTraffic', $androidNamespace, 'false')
$manifest.application.SetAttribute('allowBackup', $androidNamespace, 'false')

$settings = New-Object Xml.XmlWriterSettings
$settings.Encoding = New-Object Text.UTF8Encoding($false)
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create($resolved, $settings)
try {
    $document.Save($writer)
}
finally {
    $writer.Dispose()
}

Write-Host "Patched least-privilege foreground Android permissions in '$resolved'."
