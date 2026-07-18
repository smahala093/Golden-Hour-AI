[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SubscriptionId,

    [Parameter(Mandatory)]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [string] $Location,

    [ValidateSet('dev', 'test', 'staging', 'prod')]
    [string] $EnvironmentName = 'dev',

    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9_-]{2,15}$')]
    [string] $NamePrefix = 'goldenhour',

    [string] $PostgresAdministratorLogin = 'goldenhouradmin',

    [Parameter(Mandatory)]
    [Security.SecureString] $PostgresAdminPassword,

    [Parameter(Mandatory)]
    [Security.SecureString] $JwtSigningKey,

    [Parameter(Mandatory)]
    [Security.SecureString] $WebhookSigningSecret,

    [Security.SecureString] $OpenAiApiKey,

    [Security.SecureString] $SmsGatewaySigningSecret,

    [Security.SecureString] $DemoPassword,

    [bool] $UseMockProviders = $true,

    [string] $OpenAiModel = 'gpt-4.1-mini',

    [string] $SmsGatewayEndpoint = '',

    [string] $EmergencyNumber = '112',

    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$')]
    [string] $ImageTag,

    [switch] $WhatIfOnly,

    [switch] $SkipProviderRegistration,

    [switch] $SkipMigration
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$templatePath = Join-Path $repoRoot 'infrastructure/bicep/main.bicep'

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI with the Container Apps commands is required. Authenticate using OIDC or 'az login' first."
}

if (-not (Test-Path -LiteralPath $templatePath)) {
    throw "Bicep template not found at '$templatePath'."
}

function ConvertFrom-SecureValue {
    param([Security.SecureString] $Value)
    if ($null -eq $Value) {
        return ''
    }

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string] $Operation)
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

& az account set --subscription $SubscriptionId --only-show-errors
Assert-LastExitCode -Operation 'Selecting the Azure subscription'

if (-not $SkipProviderRegistration) {
    $providers = @(
        'Microsoft.App',
        'Microsoft.ContainerRegistry',
        'Microsoft.DBforPostgreSQL',
        'Microsoft.Insights',
        'Microsoft.KeyVault',
        'Microsoft.ManagedIdentity',
        'Microsoft.Network',
        'Microsoft.OperationalInsights',
        'Microsoft.ServiceBus',
        'Microsoft.SignalRService',
        'Microsoft.Storage'
    )

    foreach ($provider in $providers) {
        Write-Host "Registering or confirming Azure provider $provider."
        & az provider register --namespace $provider --wait --only-show-errors
        Assert-LastExitCode -Operation "Registering $provider"
    }
}

& az group create --name $ResourceGroup --location $Location --only-show-errors --output none
Assert-LastExitCode -Operation 'Creating or updating the resource group'

if (-not $ImageTag) {
    $gitTag = ''
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $gitTag = (& git -C $repoRoot rev-parse --short=12 HEAD 2>$null)
        if ($LASTEXITCODE -ne 0) {
            $gitTag = ''
        }
    }
    if ($gitTag) {
        $ImageTag = $gitTag.Trim()
    }
    else {
        $ImageTag = [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss')
    }
}

$bootstrapImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
$existingAppsJson = & az containerapp list --resource-group $ResourceGroup --output json --only-show-errors
Assert-LastExitCode -Operation 'Listing existing Container Apps'
$existingApps = @($existingAppsJson | ConvertFrom-Json)
$existingApp = $existingApps | Where-Object {
    $_.tags.application -eq 'Golden Hour AI' -and $_.tags.environment -eq $EnvironmentName
} | Select-Object -First 1
if ($existingApp -and $existingApp.properties.template.containers.Count -gt 0) {
    $bootstrapImage = [string] $existingApp.properties.template.containers[0].image
}

$postgresPasswordPlain = ConvertFrom-SecureValue $PostgresAdminPassword
$jwtSigningKeyPlain = ConvertFrom-SecureValue $JwtSigningKey
$webhookSigningSecretPlain = ConvertFrom-SecureValue $WebhookSigningSecret
$openAiApiKeyPlain = ConvertFrom-SecureValue $OpenAiApiKey
$smsGatewaySigningSecretPlain = ConvertFrom-SecureValue $SmsGatewaySigningSecret
$demoPasswordPlain = ConvertFrom-SecureValue $DemoPassword

if (-not $UseMockProviders) {
    if ([string]::IsNullOrWhiteSpace($openAiApiKeyPlain)) {
        throw 'OpenAiApiKey is required when UseMockProviders is false.'
    }
    if ([string]::IsNullOrWhiteSpace($SmsGatewayEndpoint) -or -not [Uri]::IsWellFormedUriString($SmsGatewayEndpoint, [UriKind]::Absolute) -or -not $SmsGatewayEndpoint.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SmsGatewayEndpoint must be an absolute HTTPS URL when UseMockProviders is false.'
    }
    if ([Text.Encoding]::UTF8.GetByteCount($smsGatewaySigningSecretPlain) -lt 32) {
        throw 'SmsGatewaySigningSecret must contain at least 32 UTF-8 bytes when UseMockProviders is false.'
    }
}

$parameterDocument = [ordered]@{
    '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
    contentVersion = '1.0.0.0'
    parameters = [ordered]@{
        environmentName = @{ value = $EnvironmentName }
        location = @{ value = $Location }
        namePrefix = @{ value = $NamePrefix }
        containerImage = @{ value = $bootstrapImage }
        postgresAdministratorLogin = @{ value = $PostgresAdministratorLogin }
        postgresAdministratorPassword = @{ value = $postgresPasswordPlain }
        jwtSigningKey = @{ value = $jwtSigningKeyPlain }
        webhookSigningSecret = @{ value = $webhookSigningSecretPlain }
        openAiApiKey = @{ value = $openAiApiKeyPlain }
        smsGatewaySigningSecret = @{ value = $smsGatewaySigningSecretPlain }
        demoPassword = @{ value = $demoPasswordPlain }
        useMockProviders = @{ value = $UseMockProviders }
        openAiModel = @{ value = $OpenAiModel }
        smsGatewayEndpoint = @{ value = $SmsGatewayEndpoint }
        emergencyNumber = @{ value = $EmergencyNumber }
    }
}

$parameterPath = [IO.Path]::GetTempFileName()
try {
    $parameterJson = $parameterDocument | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($parameterPath, $parameterJson, (New-Object Text.UTF8Encoding($false)))
    if ($env:OS -eq 'Windows_NT') {
        & icacls $parameterPath /inheritance:r /grant:r "$env:USERNAME`:F" | Out-Null
    }
    else {
        & chmod 600 $parameterPath
    }

    Write-Host 'Validating the Bicep deployment.'
    & az deployment group validate --resource-group $ResourceGroup --template-file $templatePath --parameters "@$parameterPath" --only-show-errors --output none
    Assert-LastExitCode -Operation 'Bicep validation'

    if ($WhatIfOnly) {
        & az deployment group what-if --resource-group $ResourceGroup --template-file $templatePath --parameters "@$parameterPath" --only-show-errors
        Assert-LastExitCode -Operation 'Bicep what-if'
        Write-Host 'What-if completed. No deployment or image build was performed.'
        return
    }

    $deploymentName = "goldenhour-$EnvironmentName-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
    Write-Host "Deploying Azure resources as '$deploymentName'."
    $outputsJson = & az deployment group create --name $deploymentName --resource-group $ResourceGroup --template-file $templatePath --parameters "@$parameterPath" --query properties.outputs --output json --only-show-errors
    Assert-LastExitCode -Operation 'Bicep deployment'
    $outputs = $outputsJson | ConvertFrom-Json
}
finally {
    $postgresPasswordPlain = $null
    $jwtSigningKeyPlain = $null
    $webhookSigningSecretPlain = $null
    $openAiApiKeyPlain = $null
    $smsGatewaySigningSecretPlain = $null
    $demoPasswordPlain = $null
    if (Test-Path -LiteralPath $parameterPath) {
        Remove-Item -LiteralPath $parameterPath -Force
    }
}

$registryName = [string] $outputs.containerRegistryName.value
$registryServer = [string] $outputs.containerRegistryLoginServer.value
$appName = [string] $outputs.applicationName.value
$appUrl = [string] $outputs.applicationUrl.value
$migrationJobName = [string] $outputs.migrationJobName.value
$image = "$registryServer/golden-hour:$ImageTag"

Push-Location $repoRoot
try {
    Write-Host "Building immutable release image '$image' in Azure Container Registry."
    & az acr build --registry $registryName --image "golden-hour:$ImageTag" --file Dockerfile . --only-show-errors
    Assert-LastExitCode -Operation 'Azure Container Registry build'
}
finally {
    Pop-Location
}

if (-not $SkipMigration) {
    & (Join-Path $PSScriptRoot 'migrate.ps1') -ResourceGroup $ResourceGroup -JobName $migrationJobName -Image $image
}
else {
    Write-Warning 'Database migration was explicitly skipped. Do not promote an incompatible image or describe the deployment as verified.'
}

$revisionSuffix = ($ImageTag.ToLowerInvariant() -replace '[^a-z0-9-]', '-').Trim('-')
if ($revisionSuffix.Length -gt 40) {
    $revisionSuffix = $revisionSuffix.Substring(0, 40).TrimEnd('-')
}
if (-not $revisionSuffix) {
    $revisionSuffix = [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss')
}

Write-Host "Creating a new Container App revision from '$image'."
& az containerapp update --name $appName --resource-group $ResourceGroup --image $image --revision-suffix $revisionSuffix --set-env-vars "PublicAppUrl=$appUrl" --only-show-errors --output none
Assert-LastExitCode -Operation 'Container App revision update'

& (Join-Path $PSScriptRoot 'health-check.ps1') -BaseUrl $appUrl
& (Join-Path $PSScriptRoot 'smoke-test.ps1') -BaseUrl $appUrl

Write-Host "Deployment health and smoke checks passed at $appUrl."
Write-Host 'This does not assert that OpenAI, messaging, notifications, emergency services, APK/AAB packaging, or any clinical integration succeeded.'
