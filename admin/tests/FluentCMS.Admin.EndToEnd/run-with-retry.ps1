# Runs the admin's end-to-end tests, and the cases that failed once more, so that one slow moment of a runner does not turn
# a run red while a flaky case stays visible (spec 7). A case that passes on the second run is named as flaky in the job's
# summary and as a warning, and the run passes; a case that fails twice fails it. A run that ends for another reason than
# failed cases, such as a timeout, a crash or skipped cases, is not run again.
#
#   pwsh admin/tests/FluentCMS.Admin.EndToEnd/run-with-retry.ps1 -ResultsDirectory artifacts/test-results [-- <test options>]
param(
    [string] $ResultsDirectory = 'artifacts/test-results',
    [string] $Configuration = 'Release',
    [string] $Timeout = '15m',
    [string] $Project = (Join-Path $PSScriptRoot 'FluentCMS.Admin.EndToEnd.csproj'),
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $TestOptions = @()
)

$ErrorActionPreference = 'Stop'
$project = $Project

# Exit code 2 of Microsoft.Testing.Platform is "at least one test failed"; every other failure is not a failed case.
$failedCases = 2

function Invoke-Tests([string] $name, [string[]] $options) {
    & dotnet test --project $project -c $Configuration --no-build `
        --results-directory $ResultsDirectory --report-xunit-trx --report-xunit-trx-filename "$name.trx" `
        --report-xunit-xml --report-xunit-xml-filename "$name.xml" `
        --fail-skips on --timeout $Timeout @options @TestOptions | Out-Host
    return $LASTEXITCODE
}

function Get-Cases([string] $name, [string] $result) {
    $path = Join-Path $ResultsDirectory "$name.xml"
    if (-not (Test-Path $path)) {
        return @()
    }

    return @(([xml](Get-Content -Raw $path)).SelectNodes("//test[@result='$result']") |
        ForEach-Object { "$($_.type).$($_.method)" } | Sort-Object -Unique)
}

function Write-Summary([string] $text) {
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $text
    }

    Write-Host $text
}

$first = Invoke-Tests 'first' @()
if ($first -eq 0) {
    exit 0
}

$failed = Get-Cases 'first' 'Fail'
if ($first -ne $failedCases -or $failed.Count -eq 0) {
    Write-Host "The run ended with exit code $first, which is not failed cases alone; it is not run again."
    exit $first
}

Write-Host "Running the $($failed.Count) failed case(s) once more: $($failed -join ', ')"
$filters = @($failed | ForEach-Object { '--filter-method', $_ })
$second = Invoke-Tests 'retry' $filters
$passed = Get-Cases 'retry' 'Pass'
$methodsPassed = @($failed | Where-Object { $passed -contains $_ })
$stillFailing = @($failed | Where-Object { $passed -notcontains $_ })

if ($second -eq 0 -and $stillFailing.Count -eq 0) {
    Write-Summary "### Flaky end-to-end cases`n`nThese failed, then passed when run again; the run passes. Each is a fault to find, in the test or in the product:`n"
    foreach ($case in $methodsPassed) {
        Write-Summary "- ``$case``"
        Write-Host "::warning title=Flaky end-to-end case::$case failed, then passed when run again."
    }

    exit 0
}

Write-Summary "### End-to-end cases that failed twice`n"
foreach ($case in $stillFailing) {
    Write-Summary "- ``$case``"
}

if ($methodsPassed.Count -gt 0) {
    $flaky = ($methodsPassed | ForEach-Object { '`' + $_ + '`' }) -join ', '
    Write-Summary "`nFailed, then passed when run again: $flaky"
}

exit $(if ($second -eq 0) { 1 } else { $second })
