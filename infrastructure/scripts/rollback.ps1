[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [string] $ApplicationName,

    [string] $TargetRevision,

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI is required and must already be authenticated."
}

$revisionsJson = & az containerapp revision list --name $ApplicationName --resource-group $ResourceGroup --all --output json --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw "Unable to list revisions for '$ApplicationName'."
}

$revisions = @($revisionsJson | ConvertFrom-Json)
if ($revisions.Count -lt 2) {
    throw "Rollback requires at least two retained revisions. No traffic was changed."
}

$current = $revisions |
    Where-Object { $_.properties.trafficWeight -gt 0 } |
    Sort-Object { [int] $_.properties.trafficWeight } -Descending |
    Select-Object -First 1

if (-not $current) {
    throw "No currently routed revision was found. Inspect ingress traffic manually before changing it."
}

if ($TargetRevision) {
    $target = $revisions | Where-Object { $_.name -eq $TargetRevision } | Select-Object -First 1
}
else {
    $target = $revisions |
        Where-Object {
            $_.name -ne $current.name -and $_.properties.provisioningState -eq 'Provisioned'
        } |
        Sort-Object { [DateTimeOffset] $_.properties.createdTime } -Descending |
        Select-Object -First 1
}

if (-not $target) {
    throw "No prior provisioned revision is available. No traffic was changed."
}

if ($target.name -eq $current.name) {
    throw "Target revision '$($target.name)' already receives traffic. No rollback is needed."
}

if ($target.properties.provisioningState -ne 'Provisioned') {
    throw "Target revision '$($target.name)' is not provisioned. No traffic was changed."
}

Write-Host "Current revision: $($current.name)"
Write-Host "Rollback target:  $($target.name)"
Write-Warning 'Rollback changes application traffic only. It does not reverse a database migration, secret rotation, external message, or user-entered state.'

if (-not $Force) {
    $confirmation = Read-Host "Type the exact target revision name to continue"
    if ($confirmation -ne $target.name) {
        throw "Confirmation did not match. No traffic was changed."
    }
}

& az containerapp revision activate --revision $target.name --resource-group $ResourceGroup --only-show-errors --output none
if ($LASTEXITCODE -ne 0) {
    throw "Unable to activate rollback target '$($target.name)'. No traffic was changed."
}

$targetUrl = "https://$($target.properties.fqdn)"
& (Join-Path $PSScriptRoot 'health-check.ps1') -BaseUrl $targetUrl -Attempts 18 -DelaySeconds 5

& az containerapp ingress traffic set --name $ApplicationName --resource-group $ResourceGroup --revision-weight "$($target.name)=100" --only-show-errors --output none
if ($LASTEXITCODE -ne 0) {
    throw "Unable to route traffic to '$($target.name)'. Inspect Azure traffic weights immediately."
}

$fqdn = (& az containerapp show --name $ApplicationName --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn --output tsv --only-show-errors).Trim()
if ($LASTEXITCODE -ne 0 -or -not $fqdn) {
    throw "Traffic was changed, but the application URL could not be read. Verify the application manually."
}

$appUrl = "https://$fqdn"
try {
    & (Join-Path $PSScriptRoot 'health-check.ps1') -BaseUrl $appUrl -Attempts 12 -DelaySeconds 5
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -BaseUrl $appUrl
}
catch {
    Write-Error "Rollback target failed post-traffic checks. Attempting to restore traffic to '$($current.name)'."
    & az containerapp revision activate --revision $current.name --resource-group $ResourceGroup --only-show-errors --output none
    & az containerapp ingress traffic set --name $ApplicationName --resource-group $ResourceGroup --revision-weight "$($current.name)=100" --only-show-errors --output none
    throw
}

Write-Host "Rollback completed and checks passed at $appUrl using revision '$($target.name)'."
Write-Host 'Database compatibility and business/provider state still require operator review.'
