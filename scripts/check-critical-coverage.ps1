[CmdletBinding()]
param(
    [string] $CoverageRoot = 'TestResults',

    [ValidateRange(0, 100)]
    [double] $MinimumPercent = 80
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoverageRoot)) {
    throw "Coverage root '$CoverageRoot' does not exist."
}

$reports = @(Get-ChildItem -Path $CoverageRoot -Recurse -Filter 'coverage.cobertura.xml' -File)
if ($reports.Count -eq 0) {
    throw "No coverage.cobertura.xml reports were found under '$CoverageRoot'."
}

$lines = @{}
foreach ($report in $reports) {
    [xml] $document = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($class in @($document.coverage.packages.package.classes.class)) {
        $filename = ([string] $class.filename).Replace('\', '/')
        if ($filename -notmatch '(^|/)(apps/api/)?(Domain|Application)/') {
            continue
        }

        foreach ($line in @($class.lines.line)) {
            $key = "$filename`:$($line.number)"
            $hits = [int] $line.hits
            if (-not $lines.ContainsKey($key) -or $hits -gt $lines[$key]) {
                $lines[$key] = $hits
            }
        }
    }
}

if ($lines.Count -eq 0) {
    throw "Coverage reports did not contain instrumented Domain or Application source lines. Check collector path normalization and test configuration."
}

$covered = @($lines.Values | Where-Object { $_ -gt 0 }).Count
$total = $lines.Count
$percent = [Math]::Round(($covered * 100.0) / $total, 2)
Write-Host "Critical Domain/Application line coverage: $covered/$total ($percent%). Required: $MinimumPercent%."

if ($percent -lt $MinimumPercent) {
    throw "Critical Domain/Application line coverage $percent% is below the required $MinimumPercent%. Add meaningful tests; do not weaken the gate."
}
