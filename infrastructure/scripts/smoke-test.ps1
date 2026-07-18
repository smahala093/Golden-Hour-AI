[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https?://')]
    [string] $BaseUrl,

    [ValidateRange(1, 60)]
    [int] $RequestTimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')
$uri = [Uri] $base

if ($uri.Scheme -ne 'https' -and $uri.Host -notin @('localhost', '127.0.0.1', '::1')) {
    throw "Shared environment smoke tests require HTTPS."
}

& "$PSScriptRoot/health-check.ps1" -BaseUrl $base -Attempts 3 -DelaySeconds 2 -RequestTimeoutSeconds $RequestTimeoutSeconds

$homeResponse = Invoke-WebRequest -Uri "$base/" -Method Get -TimeoutSec $RequestTimeoutSeconds -UseBasicParsing
if ($homeResponse.StatusCode -ne 200) {
    throw "The application root returned HTTP $($homeResponse.StatusCode), expected 200."
}

if ($homeResponse.Content -notmatch 'Golden Hour AI') {
    throw "The application root did not contain the expected product title."
}

$manifest = Invoke-RestMethod -Uri "$base/manifest.webmanifest" -Method Get -TimeoutSec $RequestTimeoutSeconds
if ($manifest.name -ne 'Golden Hour AI') {
    throw "The PWA manifest name was missing or unexpected."
}

if ($manifest.display -ne 'standalone') {
    throw "The PWA manifest is not configured for standalone display."
}

Write-Host "Same-origin shell, manifest, liveness, and readiness smoke checks passed for $base."
Write-Host "No external call, message, AI, provider delivery, or clinical behavior was asserted by this smoke test."
