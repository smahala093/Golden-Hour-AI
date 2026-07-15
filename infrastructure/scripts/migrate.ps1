[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [string] $JobName,

    [string] $Image,

    [ValidateRange(60, 3600)]
    [int] $TimeoutSeconds = 1200,

    [ValidateRange(2, 60)]
    [int] $PollSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI is required. Install it and authenticate with 'az login' or workload identity first."
}

if ($Image) {
    Write-Host "Updating migration job image to immutable release '$Image'."
    & az containerapp job update --name $JobName --resource-group $ResourceGroup --image $Image --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to update Container Apps migration job '$JobName'."
    }
}

$executionName = (& az containerapp job start --name $JobName --resource-group $ResourceGroup --query name --output tsv --only-show-errors).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to start Container Apps migration job '$JobName'."
}

if (-not $executionName) {
    $executionName = (& az containerapp job execution list --name $JobName --resource-group $ResourceGroup --query "sort_by(@, &properties.startTime)[-1].name" --output tsv --only-show-errors).Trim()
}

if (-not $executionName) {
    throw "Azure did not return a migration execution name. Inspect the job before changing application traffic."
}

Write-Host "Started migration execution '$executionName'."
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $status = (& az containerapp job execution show --name $JobName --resource-group $ResourceGroup --job-execution-name $executionName --query properties.status --output tsv --only-show-errors).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read migration execution '$executionName'."
    }

    switch ($status) {
        'Succeeded' {
            Write-Host "Migration execution '$executionName' succeeded."
            return
        }
        'Failed' {
            throw "Migration execution '$executionName' failed. The application image was not promoted; inspect Container Apps job logs."
        }
        'Stopped' {
            throw "Migration execution '$executionName' was stopped. The application image was not promoted."
        }
        default {
            Write-Host "Migration execution '$executionName' status: $status"
            Start-Sleep -Seconds $PollSeconds
        }
    }
}

throw "Migration execution '$executionName' did not complete within $TimeoutSeconds seconds. Inspect it before retrying; do not assume it failed or succeeded."
