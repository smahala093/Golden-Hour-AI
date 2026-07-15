[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https?://')]
    [string] $BaseUrl,

    [ValidateRange(1, 120)]
    [int] $Attempts = 18,

    [ValidateRange(1, 60)]
    [int] $DelaySeconds = 5,

    [ValidateRange(1, 60)]
    [int] $RequestTimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')

if (-not [Uri]::IsWellFormedUriString($base, [UriKind]::Absolute)) {
    throw "BaseUrl must be an absolute HTTP(S) URL."
}

$uri = [Uri] $base
if ($uri.Scheme -ne 'https' -and $uri.Host -notin @('localhost', '127.0.0.1', '::1')) {
    throw "Shared environment health checks require HTTPS."
}

function Invoke-HealthEndpoint {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $target = "$base$Path"
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $target -Method Get -TimeoutSec $RequestTimeoutSeconds -UseBasicParsing
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host "$Path returned HTTP $($response.StatusCode)."
                return
            }

            Write-Warning "$Path returned HTTP $($response.StatusCode) on attempt $attempt of $Attempts."
        }
        catch {
            $safeMessage = $_.Exception.Message -replace '(?i)(password|token|secret|key)=[^;&\s]+', '$1=[REDACTED]'
            Write-Warning "${Path} was not ready on attempt $attempt of ${Attempts}: $safeMessage"
        }

        if ($attempt -lt $Attempts) {
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    throw "$Path did not become healthy after $Attempts attempts."
}

Invoke-HealthEndpoint -Path '/health/live'
Invoke-HealthEndpoint -Path '/health/ready'
Write-Host "Liveness and readiness checks passed for $base."
