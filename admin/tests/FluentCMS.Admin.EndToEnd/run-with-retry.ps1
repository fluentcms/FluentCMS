# Runs the admin's end-to-end tests, and the cases that failed once more, so that one slow moment of a runner does not turn
# a run red while a flaky case stays visible (spec 7). A case that passes on the second run is named as flaky in the job's
# summary and as a warning, and the run passes; a case that fails twice fails it. Nothing is run again when the first run
# ended for another reason than failed cases (a timeout, a crash), when a case was skipped (--fail-skips reports it as a
# failure), or when more than MaxRetried cases failed, which is a broken suite, not a flake. The second run takes the host the
# first one published, or with FLUENTCMS_E2E_IMAGE the same image.
#
#   pwsh admin/tests/FluentCMS.Admin.EndToEnd/run-with-retry.ps1 -ResultsDirectory artifacts/test-results [-- <test options>]
param(
    [string] $ResultsDirectory = 'artifacts/test-results',
    [string] $Configuration = 'Release',
    [string] $Timeout = '15m',
    [string] $RetryTimeout = '6m',
    [int] $MaxRetried = 5,
    [string] $Project = (Join-Path $PSScriptRoot 'FluentCMS.Admin.EndToEnd.csproj'),
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $TestOptions = @()
)

$ErrorActionPreference = 'Stop'

# Exit code 2 of Microsoft.Testing.Platform is "at least one test failed"; every other failure is not a failed case.
$failedCases = 2

function Invoke-Tests([string] $name, [string] $timeout, [string[]] $options) {
    & dotnet test --project $Project -c $Configuration --no-build `
        --results-directory $ResultsDirectory --report-xunit-trx --report-xunit-trx-filename "$name.trx" `
        --report-xunit-xml --report-xunit-xml-filename "$name.xml" `
        --fail-skips on --timeout $timeout @options @TestOptions | Out-Host
    return $LASTEXITCODE
}

# The cases of a run's report with the result, each as Namespace.Class.Method once; a skipped case is written as a failure
# whose message starts with FAIL_SKIP.
function Get-Cases([string] $name, [string] $result, [switch] $Skipped) {
    $path = Join-Path $ResultsDirectory "$name.xml"
    if (-not (Test-Path $path)) {
        return @()
    }

    $tests = @(([xml](Get-Content -Raw $path)).SelectNodes("//test[@result='$result']"))
    if ($Skipped) {
        $tests = @($tests | Where-Object { "$($_.SelectSingleNode('failure/message').InnerText)".StartsWith('FAIL_SKIP') })
    }

    return @($tests | ForEach-Object { "$($_.type).$($_.method)" } | Sort-Object -Unique)
}

function Write-Summary([string] $text) {
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $text
    }

    Write-Host $text
}

function Write-Cases([string] $heading, [string[]] $cases) {
    Write-Summary "### $heading`n"
    foreach ($case in $cases) {
        Write-Summary "- ``$case``"
    }
}

# One publish of the host for both runs, outside the runs' own folders, which each run deletes.
$published = Join-Path ([IO.Path]::GetTempPath()) ("fluentcms-published-" + [Guid]::NewGuid().ToString('N'))
$env:FLUENTCMS_E2E_PUBLISHED = $published
try {
    $first = Invoke-Tests 'first' $Timeout @()
    if ($first -eq 0) {
        exit 0
    }

    $skipped = Get-Cases 'first' 'Fail' -Skipped
    $failed = @(Get-Cases 'first' 'Fail' | Where-Object { $skipped -notcontains $_ })
    if ($skipped.Count -gt 0) {
        Write-Cases 'End-to-end cases that were skipped' $skipped
        Write-Summary "`nA skipped case fails the run and nothing is run again: the browser or the host did not start as the job needs."
        exit $first
    }

    if ($first -ne $failedCases -or $failed.Count -eq 0) {
        Write-Summary "The end-to-end run ended with exit code $first, which is not failed cases alone; nothing is run again."
        exit $first
    }

    if ($failed.Count -gt $MaxRetried) {
        Write-Cases "End-to-end cases that failed ($($failed.Count), more than $MaxRetried, so nothing is run again)" $failed
        Write-Summary "`nSo many failures are a broken suite or host, not a flake."
        exit $first
    }

    Write-Host "Running the $($failed.Count) failed case(s) once more: $($failed -join ', ')"
    $filters = @($failed | ForEach-Object { '--filter-method', $_ })
    $second = Invoke-Tests 'retry' $RetryTimeout $filters
    $ran = @(@(Get-Cases 'retry' 'Pass') + @(Get-Cases 'retry' 'Fail') | Sort-Object -Unique)
    $passed = @(Get-Cases 'retry' 'Pass')
    $methodsPassed = @($failed | Where-Object { $passed -contains $_ })
    $stillFailing = @($failed | Where-Object { $passed -notcontains $_ })

    # The filter must have run exactly the cases that failed, no fewer and no others.
    $unexpected = if ($ran.Count -eq 0) { $failed } else { @(Compare-Object -ReferenceObject $failed -DifferenceObject $ran -PassThru) }
    if ($unexpected.Count -gt 0) {
        Write-Cases "The second run did not run exactly the $($failed.Count) failed case(s); it ran $($ran.Count)" $ran
        exit 1
    }

    if ($second -eq 0 -and $stillFailing.Count -eq 0) {
        Write-Cases 'Flaky end-to-end cases' $methodsPassed
        Write-Summary "`nThese failed, then passed when run again; the run passes. Each is a fault to find, in the test or in the product."
        foreach ($case in $methodsPassed) {
            Write-Host "::warning title=Flaky end-to-end case::$case failed, then passed when run again."
        }

        exit 0
    }

    Write-Cases 'End-to-end cases that failed twice' $stillFailing
    if ($methodsPassed.Count -gt 0) {
        $flaky = ($methodsPassed | ForEach-Object { '`' + $_ + '`' }) -join ', '
        Write-Summary "`nFailed, then passed when run again: $flaky"
    }

    exit $(if ($second -eq 0) { 1 } else { $second })
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $published
}
