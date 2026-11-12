#Requires -Version 7.4
# Runs eng/fresh-machine.ps1 against two broken copies of a folder feed and expects each run to fail at the step that should
# catch it, the restore of the first host: a package that misses its assembly is refused by NuGet (NU1202), and a template
# that names another version than the packages is refused before NuGet, which would take a higher version than a lower one.
#
#   pwsh eng/fresh-machine-broken-feed.ps1 -Feed <the feed of a fresh-machine.ps1 run> -Version <its version> [-WorkDirectory <dir>]
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [string] $Version,
    [string] $WorkDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('fluentcms-broken-feed-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}

$WorkDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
$Feed = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Feed)
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null

function Edit-Package([string] $feed, [string] $id, [scriptblock] $edit) {
    $path = Join-Path $feed "$id.$Version.nupkg"
    if (-not (Test-Path $path)) {
        throw "The feed has no $id.$Version.nupkg."
    }

    $zip = [IO.Compression.ZipFile]::Open($path, 'Update')
    try { & $edit $zip } finally { $zip.Dispose() }
}

function Test-BrokenFeed([string] $case, [string] $expectedStep, [string] $expectedCode, [scriptblock] $break) {
    $broken = Join-Path $WorkDirectory "$case/feed"
    New-Item -ItemType Directory -Force $broken | Out-Null
    Copy-Item (Join-Path $Feed '*') $broken
    & $break $broken

    $log = Join-Path $WorkDirectory "$case.log"
    # In this process: the script exits with its code into $LASTEXITCODE and puts the environment back.
    & (Join-Path $PSScriptRoot 'fresh-machine.ps1') -Feed $broken -Version $Version -WorkDirectory (Join-Path $WorkDirectory "$case/run") *> $log
    $exitCode = $LASTEXITCODE
    $failure = Select-String -Path $log -Pattern "fresh-machine failed at '([^']*)'" | Select-Object -Last 1
    $step = if ($failure) { $failure.Matches[0].Groups[1].Value } else { $null }
    if ($exitCode -eq 0 -or $step -ne $expectedStep -or -not (Select-String -Path $log -Pattern $expectedCode -SimpleMatch -Quiet)) {
        Get-Content $log -Tail 80 | Write-Host
        throw "With $case, fresh-machine.ps1 exited with $exitCode at '$step' instead of failing at '$expectedStep' with $expectedCode. The log is $log."
    }

    Write-Host "With $case, fresh-machine.ps1 failed at '$step' with $expectedCode, as expected."
}

Test-BrokenFeed 'missing-file' 'Restore SourceSqlite and check that its FluentCMS packages come from the folder feed' 'NU1202' {
    param($feed)
    Edit-Package $feed 'FluentCMS.Data.Sqlite' {
        param($zip)
        $entry = $zip.Entries | Where-Object FullName -like 'lib/*/FluentCMS.Data.Sqlite.dll' | Select-Object -First 1
        if (-not $entry) { throw 'FluentCMS.Data.Sqlite has no lib/*/FluentCMS.Data.Sqlite.dll.' }
        $entry.Delete()
    }
}

Test-BrokenFeed 'wrong-version' 'Restore SourceSqlite and check that its FluentCMS packages come from the folder feed' 'names FluentCMS.Plugins.Webhooks 0.0.1-missing' {
    param($feed)
    Edit-Package $feed 'FluentCMS.Templates' {
        param($zip)
        $name = 'content/fluentcms/FluentCms-App.csproj'
        $entry = $zip.GetEntry($name)
        if (-not $entry) { throw "FluentCMS.Templates has no $name." }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $project = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $pinned = "Include=`"FluentCMS.Plugins.Webhooks`" Version=`"$Version`""
        if (-not $project.Contains($pinned)) { throw "$name does not pin FluentCMS.Plugins.Webhooks $Version." }
        $entry.Delete()
        $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open())
        try { $writer.Write($project.Replace($pinned, 'Include="FluentCMS.Plugins.Webhooks" Version="0.0.1-missing"')) } finally { $writer.Dispose() }
    }
}

Write-Host 'Both broken feeds failed the sequence.'

# $LASTEXITCODE still holds the last expected failure, which the runner would take as this script's exit code.
exit 0
