#Requires -Version 7.4
# The M6 sequence as a new user runs it, from packages alone, in a work directory outside the repository: every package of
# the solution packed into a folder feed, FluentCMS.Templates and the fluentcms tool installed from it, a Blog host made with
# `dotnet new fluentcms`, its migrations added and applied, an administrator and a management key made with the tool, the
# TypeScript of the instance generated and compiled, the site exported and imported into a second new host and compared there
# through the Delivery API, and a plugin made with `dotnet new fluentcms-plugin`, built and tested.
#
#   pwsh eng/fresh-machine.ps1 [-WorkDirectory <dir>] [-Version <version>] [-Feed <dir>]
#                              [-PostgreSql <connection string> -PostgreSqlImport <connection string>]
#
# SQLite always runs. PostgreSQL runs when both connection strings are given (or FLUENTCMS_FRESH_POSTGRES and
# FLUENTCMS_FRESH_POSTGRES_IMPORT hold them): two empty databases, one for each host. -Feed takes a feed that holds the packages
# of -Version and skips packing. It needs the .NET SDK and Node.js with npm.
#
# Past the pack, which builds the repository as `dotnet pack` always does (its bin/ and obj/, and its packages in the NuGet
# cache), everything it writes is under the work directory, which it keeps for its logs: the NuGet packages, the installed
# templates, the npm cache and the hosts. It stops the hosts it starts and puts the environment back however it ends.
[CmdletBinding()]
param(
    [string] $WorkDirectory,
    [string] $Version,
    [string] $Feed,
    [string] $PostgreSql = $env:FLUENTCMS_FRESH_POSTGRES,
    [string] $PostgreSqlImport = $env:FLUENTCMS_FRESH_POSTGRES_IMPORT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$ProgressPreference = 'SilentlyContinue'

$repository = Split-Path $PSScriptRoot -Parent
$onGitHub = $env:GITHUB_ACTIONS -eq 'true'
$adminEmail = 'admin@example.com'
$welcomeSlug = 'welcome-to-fluentcms'

if (-not $Version) {
    $Version = '1.0.0-fresh.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
}

if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('fluentcms-fresh-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}

# Relative to PowerShell's location, which .NET's current directory need not be.
$WorkDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
if ($Feed) {
    $Feed = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Feed)
}
$withPostgreSql = [bool]$PostgreSql -and [bool]$PostgreSqlImport
if ([bool]$PostgreSql -ne [bool]$PostgreSqlImport) {
    [Console]::Error.WriteLine('PostgreSQL needs two databases: give both -PostgreSql and -PostgreSqlImport, or neither.')
    exit 2
}

# The FluentCMS packages would come from the repository's own Directory.Packages.props inside it.
$inside = [IO.Path]::GetRelativePath($repository, $WorkDirectory)
if (-not $inside.StartsWith('..') -and -not [IO.Path]::IsPathRooted($inside)) {
    [Console]::Error.WriteLine("The work directory $WorkDirectory is inside the repository; give one outside it.")
    exit 2
}

if ((Test-Path $WorkDirectory) -and (Get-ChildItem -Force $WorkDirectory | Select-Object -First 1)) {
    [Console]::Error.WriteLine("The work directory $WorkDirectory is not empty.")
    exit 2
}

New-Item -ItemType Directory -Force $WorkDirectory | Out-Null

$script:step = $null
$script:stepStarted = $null
$script:timings = [Collections.Generic.List[object]]::new()
$script:hosts = [Collections.Generic.List[object]]::new()
$script:savedEnvironment = @{}

function Start-Step([string] $name) {
    Stop-Step
    $script:step = $name
    $script:stepStarted = [Diagnostics.Stopwatch]::StartNew()
    if ($onGitHub) { Write-Host "::group::$name" } else { Write-Host "`n==> $name" -ForegroundColor Cyan }
}

function Stop-Step {
    if ($null -ne $script:step) {
        $script:timings.Add([pscustomobject]@{ Step = $script:step; Seconds = [Math]::Round($script:stepStarted.Elapsed.TotalSeconds) })
        if ($onGitHub) { Write-Host '::endgroup::' }
        $script:step = $null
    }
}

function Fail([string] $message) {
    throw [InvalidOperationException]::new($message)
}

# Runs a command and fails the step when it exits with anything but 0. No argument holds a secret: keys go in the
# environment and passwords on standard input.
function Invoke-Checked([string] $file, [string[]] $arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) {
        Fail "'$file $($arguments -join ' ')' exited with $LASTEXITCODE"
    }
}

function Hide([string] $secret) {
    if ($onGitHub) { Write-Host "::add-mask::$secret" }
}

# A null value removes the variable; [Environment]::SetEnvironmentVariable would get an empty string from PowerShell instead.
function Set-EnvironmentValue([string] $variableName, $variableValue) {
    if ($null -eq $variableValue) {
        Remove-Item "Env:$variableName" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item "Env:$variableName" $variableValue
    }
}

function Set-Variables([hashtable] $values) {
    foreach ($name in $values.Keys) {
        if (-not $script:savedEnvironment.ContainsKey($name)) {
            $script:savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        }

        Set-EnvironmentValue $name $values[$name]
    }
}

# Runs the block with the variables set, and puts each back as it was. The block sees its caller's variables, so the names
# here are ones no caller uses.
function Use-Variables([hashtable] $withVariables, [scriptblock] $inBlock) {
    $previousValues = @{}
    foreach ($variableName in $withVariables.Keys) {
        $previousValues[$variableName] = [Environment]::GetEnvironmentVariable($variableName)
        Set-EnvironmentValue $variableName $withVariables[$variableName]
    }

    try {
        & $inBlock
    }
    finally {
        foreach ($variableName in $previousValues.Keys) {
            Set-EnvironmentValue $variableName $previousValues[$variableName]
        }
    }
}

function Use-Location([string] $atPath, [scriptblock] $inBlock) {
    Push-Location $atPath
    try {
        & $inBlock
    }
    finally {
        Pop-Location
    }
}

function New-Password {
    'Aa1!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}

function Format-Path([string] $path) {
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($path))
}

# Every FluentCMS package the project restored is a package of -Version that NuGet took from the folder feed, which the
# package source mapping of nuget.config allows and this proves.
function Assert-FromFeed([string] $projectDirectory) {
    $assetsFile = Join-Path $projectDirectory 'obj/project.assets.json'
    if (-not (Test-Path $assetsFile)) {
        Fail "$assetsFile is missing; the project was not restored"
    }

    $assets = Get-Content -Raw $assetsFile | ConvertFrom-Json -AsHashtable
    $packages = @($assets.libraries.GetEnumerator() | Where-Object { $_.Key -like 'FluentCMS.*/*' })
    if ($packages.Count -eq 0) {
        Fail "$projectDirectory restored no FluentCMS package"
    }

    foreach ($package in $packages) {
        $name, $resolved = $package.Key -split '/', 2
        if ($package.Value.type -ne 'package') {
            Fail "$name in $projectDirectory is a $($package.Value.type), not a package"
        }

        if ($resolved -ne $Version) {
            Fail "$projectDirectory restored $name $resolved, not $Version"
        }

        Assert-PackageSource (Join-Path $env:NUGET_PACKAGES $package.Value.path) $name
    }

    Write-Host "$($packages.Count) FluentCMS packages of $Version, all from the folder feed: $(($packages | ForEach-Object { ($_.Key -split '/')[0] }) -join ', ')"
}

# The restored package is the feed's file byte for byte, and NuGet names the feed as its source where it records one (a
# local tool's package has no source in its metadata).
function Assert-PackageSource([string] $packageDirectory, [string] $name) {
    $restored = Join-Path $packageDirectory "$($name.ToLowerInvariant()).$($Version.ToLowerInvariant()).nupkg"
    $fed = Join-Path $Feed "$name.$Version.nupkg"
    if (-not (Test-Path $restored)) {
        Fail "$name $Version is not restored in $packageDirectory"
    }

    if (-not (Test-Path $fed)) {
        Fail "the folder feed has no $name.$Version.nupkg"
    }

    if ((Get-FileHash -Algorithm SHA512 $restored).Hash -ne (Get-FileHash -Algorithm SHA512 $fed).Hash) {
        Fail "the restored $name $Version is not the package of the folder feed"
    }

    $source = (Get-Content -Raw (Join-Path $packageDirectory '.nupkg.metadata') | ConvertFrom-Json -AsHashtable)['source']
    if ($source -and (Format-Path $source) -ne $Feed) {
        Fail "$name came from '$source', not from the folder feed $Feed"
    }
}

# Each FluentCMS package a created project references is named at -Version: NuGet would take a higher version than a lower
# one the template names, with a warning only.
function Assert-TemplateVersions([string] $directory) {
    foreach ($project in Get-ChildItem $directory -Recurse -Filter '*.csproj') {
        foreach ($reference in ([xml](Get-Content -Raw $project.FullName)).SelectNodes('//PackageReference[starts-with(@Include, "FluentCMS.")]')) {
            if ($reference.Version -ne $Version) {
                Fail "$($project.Name) names $($reference.Include) $($reference.Version), not $Version"
            }
        }
    }
}

function New-Host([string] $name, [string] $provider, [string] $directory) {
    Invoke-Checked dotnet @('new', 'fluentcms', '-n', $name, '-o', $directory, '--provider', $provider, '--sample', 'blog', '--debug:custom-hive', $hive)
}

# Restored from the feed, built with warnings as errors, and the tool of .config/dotnet-tools.json restored, from the feed too.
function Initialize-Host([string] $name, [string] $directory) {
    Start-Step "Restore $name and check that its FluentCMS packages come from the folder feed"
    Assert-TemplateVersions $directory
    Invoke-Checked dotnet @('restore', $directory)
    Assert-FromFeed $directory
    $tool = (Get-Content -Raw (Join-Path $directory '.config/dotnet-tools.json') | ConvertFrom-Json -AsHashtable).tools['fluentcms.cli']
    if ($null -eq $tool -or $tool['version'] -ne $Version) {
        Fail ".config/dotnet-tools.json of $name does not pin fluentcms.cli $Version"
    }

    Start-Step "Build $name"
    Invoke-Checked dotnet @('build', $directory, '--no-restore', '-warnaserror')

    Start-Step "Restore the fluentcms tool of $name from the folder feed"
    Use-Location $directory { Invoke-Checked dotnet @('tool', 'restore') }
    Assert-PackageSource (Join-Path $env:NUGET_PACKAGES "fluentcms.cli/$($Version.ToLowerInvariant())") 'FluentCMS.Cli'
}

# fluentcms migrations add Initial and fluentcms migrate, with migrate --check after it, and the build of the migration.
function Initialize-Database([string] $name, [string] $directory, [hashtable] $variables) {
    Start-Step "fluentcms migrations add Initial and fluentcms migrate in $name"
    Use-Location $directory {
        Invoke-Checked dotnet @('fluentcms', 'migrations', 'add', 'Initial')
        if (-not (Get-ChildItem (Join-Path $directory 'Migrations/Content') -Filter '*_Initial.cs')) {
            Fail 'migrations add wrote no Initial migration to Migrations/Content'
        }

        Invoke-Checked dotnet @('build', $directory, '--no-restore', '-warnaserror')
        Use-Variables $variables {
            Invoke-Checked dotnet @('fluentcms', 'migrate', '--no-build', '--environment', 'Development')
            Invoke-Checked dotnet @('fluentcms', 'migrate', '--check', '--no-build', '--environment', 'Development')
        }
    }
}

# A management key with the Super Admin role, which apikey create prints alone on standard output.
function New-ManagementKey([string] $directory, [hashtable] $variables) {
    $key = Use-Location $directory {
        Use-Variables ($variables + @{ Logging__Console__LogToStandardErrorThreshold = 'Trace' }) {
            & dotnet fluentcms apikey create --name fresh-machine --class management --role 'Super Admin' --no-build --environment Development
        }
    }

    if ($LASTEXITCODE -ne 0) {
        Fail "apikey create exited with $LASTEXITCODE"
    }

    $lines = @($key | Where-Object { $_ })
    if ($lines.Count -ne 1 -or $lines[0] -match '\s') {
        Fail "apikey create printed $($lines.Count) lines rather than the key alone"
    }

    Hide $lines[0]
    $lines[0]
}

# Starts the built host in Development on a free port, as `dotnet run` does but on HTTP only, since the sequence signs in with
# keys and the authorization server is not used, and waits for /health/ready. Its tables come from the migrations: schema sync
# is off.
function Start-Host([string] $name, [string] $directory, [hashtable] $variables) {
    $port = Get-FreePort
    $origin = "http://127.0.0.1:$port"
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $directory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add('exec')
    $start.ArgumentList.Add((Join-Path $directory "bin/Debug/net10.0/$name.dll"))
    $start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $start.Environment['ASPNETCORE_URLS'] = $origin
    $start.Environment['FluentCms__Database__SchemaSync'] = 'Off'
    foreach ($variable in $variables.GetEnumerator()) {
        $start.Environment[$variable.Key] = $variable.Value
    }

    $log = Join-Path $WorkDirectory "$name.log"
    $errorLog = Join-Path $WorkDirectory "$name.err.log"
    # Unbuffered and readable while the host writes, so that a failure can show the end of the log.
    $output = [IO.FileStream]::new($log, 'Create', 'Write', 'ReadWrite', 1)
    $errors = [IO.FileStream]::new($errorLog, 'Create', 'Write', 'ReadWrite', 1)
    $process = [Diagnostics.Process]::Start($start)
    $cms = [pscustomobject]@{
        Name = $name
        Origin = $origin
        Process = $process
        Log = $log
        ErrorLog = $errorLog
        Files = @($output, $errors)
        Pumps = @($process.StandardOutput.BaseStream.CopyToAsync($output), $process.StandardError.BaseStream.CopyToAsync($errors))
        Key = $null
    }
    $script:hosts.Add($cms)

    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while ($true) {
        if ($process.HasExited) {
            Stop-Host $cms
            Show-Log $cms
            Fail "$name stopped with exit code $($process.ExitCode) before it was ready"
        }

        try {
            $ready = Invoke-WebRequest "$origin/health/ready" -TimeoutSec 5 -SkipHttpErrorCheck
            if ($ready.StatusCode -eq 200) {
                Write-Host "$name is ready at $origin"
                return $cms
            }
        }
        catch {
            # Not listening yet.
        }

        if ([DateTime]::UtcNow -gt $deadline) {
            Stop-Host $cms
            Show-Log $cms
            Fail "$name was not ready at $origin/health/ready after three minutes"
        }

        Start-Sleep -Seconds 2
    }
}

function Stop-Host($cms) {
    if (-not $cms.Process.HasExited) {
        $cms.Process.Kill($true)
    }

    $cms.Process.WaitForExit()
    try { [Threading.Tasks.Task]::WaitAll($cms.Pumps, 10000) | Out-Null } catch { }
    foreach ($file in $cms.Files) { $file.Dispose() }
    $cms.Process.Dispose()
    $script:hosts.Remove($cms) | Out-Null
}

function Show-Log($cms) {
    foreach ($file in @($cms.Log, $cms.ErrorLog)) {
        if (Test-Path $file) {
            Write-Host "--- the last lines of $file"
            Get-Content $file -Tail 60 | Write-Host
        }
    }
}

# A request with the host's management key; the key is never part of a message.
function Invoke-Cms($cms, [string] $method, [string] $path, [object] $body, [hashtable] $headers = @{}) {
    $parameters = @{
        Uri = $cms.Origin + $path
        Method = $method
        Headers = @{ Authorization = "ApiKey $($cms.Key)" } + $headers
        SkipHttpErrorCheck = $true
        TimeoutSec = 60
    }
    if ($null -ne $body) {
        $parameters.Body = $body | ConvertTo-Json -Depth 10 -Compress
        $parameters.ContentType = 'application/json'
    }

    Invoke-WebRequest @parameters
}

function Get-Entry($cms, [string] $path, [string] $locale, [int] $expected = 200) {
    $response = Invoke-Cms $cms GET $path $null @{ 'X-Cms-Locale' = $locale }
    if ($response.StatusCode -ne $expected) {
        Fail "GET $path ($locale) on $($cms.Name) answered $($response.StatusCode), not $expected`: $($response.Content)"
    }

    $response.Content
}

# The seeder runs after the host is ready, so the host's log says when the Blog seed is complete (event 9901), before anything
# reads or exports it; a seed that failed or was skipped (9903, 9902) fails the step.
function Wait-Seeded($cms) {
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    while ($true) {
        if (Select-String -Path $cms.Log -Pattern 'SeedRunner\[(9902|9903)\]' -Quiet) {
            Show-Log $cms
            Fail "the Blog seed of $($cms.Name) failed or was skipped"
        }

        if (Select-String -Path $cms.Log -Pattern 'Seeder .*BlogSeeder version \d+ was applied' -Quiet) {
            break
        }

        if ([DateTime]::UtcNow -gt $deadline -or $cms.Process.HasExited) {
            Show-Log $cms
            Fail "the Blog seed of $($cms.Name) was not applied within two minutes"
        }

        Start-Sleep -Seconds 2
    }

    $response = Invoke-Cms $cms GET "/api/delivery/v1/article/by/slug/$welcomeSlug" $null
    if ($response.StatusCode -ne 200) {
        Fail "the Delivery API of $($cms.Name) did not serve the welcome article: $($response.StatusCode) $($response.Content)"
    }

    $response.Content | ConvertFrom-Json
}

# The same JSON values; System.Text.Json keeps dates as the strings they were sent as.
function Compare-Json([string] $left, [string] $right) {
    [Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse($left), [Text.Json.Nodes.JsonNode]::Parse($right))
}

# fluentcms generate typescript against the running instance, and the module compiled in strict mode with @fluentcms/client
# from its package and the TypeScript version the client is built with.
function Test-TypeScript($cms, [string] $directory) {
    Start-Step "fluentcms generate typescript --url and tsc in strict mode ($($cms.Name))"
    $web = Join-Path $WorkDirectory "web-$($cms.Name)"
    New-Item -ItemType Directory -Force (Join-Path $web 'src') | Out-Null
    Use-Location $directory {
        Use-Variables @{ FLUENTCMS_TOKEN = $cms.Key } {
            Invoke-Checked dotnet @('fluentcms', 'generate', 'typescript', '--url', $cms.Origin, '--output', (Join-Path $web 'src/cms'))
        }
    }

    if (-not (Test-Path (Join-Path $web 'src/cms/fluentcms.ts'))) {
        Fail 'generate typescript wrote no src/cms/fluentcms.ts'
    }

    $client = Get-ChildItem $Feed -Filter 'fluentcms-client-*.tgz' | Select-Object -First 1
    if (-not $client) {
        Fail "the feed $Feed has no fluentcms-client-*.tgz"
    }

    $typescript = (Get-Content -Raw (Join-Path $repository 'clients/typescript/package.json') | ConvertFrom-Json).devDependencies.typescript
    Set-Content (Join-Path $web 'package.json') '{ "name": "fresh-machine-web", "private": true, "type": "module" }'
    Set-Content (Join-Path $web 'tsconfig.json') @'
{
  "compilerOptions": {
    "target": "es2022",
    "module": "nodenext",
    "moduleResolution": "nodenext",
    "lib": ["es2022", "dom"],
    "types": [],
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "exactOptionalPropertyTypes": true,
    "noImplicitOverride": true,
    "noEmit": true
  },
  "include": ["src"]
}
'@
    Set-Content (Join-Path $web 'src/main.ts') @'
import { ArticleFields, createClient, schemaHash, type Article } from "./cms/fluentcms.js";

const cms = createClient({ baseUrl: "http://127.0.0.1/" });

export const articles = cms.article;
export const fields = ArticleFields;
export const hash: string = schemaHash;
export const titleOf = (article: Article): string => article.title ?? "";
'@
    Use-Location $web {
        Invoke-Checked npm @('install', '--no-audit', '--no-fund', '--save-exact', $client.FullName, "typescript@$typescript")
        Invoke-Checked node @((Join-Path $web 'node_modules/typescript/bin/tsc'), '-p', 'tsconfig.json')
    }

    Write-Host "The generated module compiles with @fluentcms/client from $($client.Name) and TypeScript $typescript."
}

# One provider: a source host with the Blog seed, its TypeScript, an export with the media files, a target host with the Blog
# types and no seed content, an import with a dry run first, and the welcome article compared through both Delivery APIs.
function Test-Provider([string] $provider, [hashtable] $sourceVariables, [hashtable] $targetVariables) {
    $title = (Get-Culture).TextInfo.ToTitleCase($provider)
    $sourceName = "Source$title"
    $targetName = "Target$title"
    $source = Join-Path $WorkDirectory $sourceName
    $target = Join-Path $WorkDirectory $targetName
    $package = Join-Path $WorkDirectory "$provider-blog.zip"

    Start-Step "dotnet new fluentcms --provider $provider --sample blog ($sourceName)"
    Use-Location $WorkDirectory { New-Host $sourceName $provider $source }
    Initialize-Host $sourceName $source
    Initialize-Database $sourceName $source $sourceVariables

    Start-Step "fluentcms user create and apikey create in $sourceName"
    $password = New-Password
    Hide $password
    Use-Location $source {
        Use-Variables $sourceVariables {
            $password | & dotnet fluentcms user create --email $adminEmail --role 'Super Admin' --password-stdin --no-build --environment Development
            if ($LASTEXITCODE -ne 0) { Fail "user create exited with $LASTEXITCODE" }
        }
    }
    $sourceKey = New-ManagementKey $source $sourceVariables

    Start-Step "Start $sourceName, wait for the Blog seed and read its welcome article"
    $sourceHost = Start-Host $sourceName $source $sourceVariables
    $sourceHost.Key = $sourceKey
    $welcome = Wait-Seeded $sourceHost
    if ($welcome.title -ne 'Welcome to FluentCMS') {
        Fail "the welcome article is titled '$($welcome.title)'"
    }

    Test-TypeScript $sourceHost $source

    Start-Step "fluentcms export --media-files from $sourceName"
    Use-Location $source {
        Use-Variables $sourceVariables {
            Invoke-Checked dotnet @('fluentcms', 'export', '--output', $package, '--media-files', '--no-build', '--environment', 'Development')
        }
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        $manifestEntry = $archive.GetEntry('manifest.json')
        if (-not $manifestEntry -or 'content/article.jsonl' -notin $entries -or -not ($entries | Where-Object { $_ -like 'plugins/media/*' })) {
            Fail "the package lacks manifest.json, content/article.jsonl or plugins/media/: $($entries -join ', ')"
        }

        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "Wrote $package with $($entries.Count) files, locales $($manifest.locales -join ', ')."

    # The Blog types without the seed content, which would hold the same slugs under other ids.
    Start-Step "dotnet new fluentcms --provider $provider --sample blog ($targetName), with no seeding at start"
    Use-Location $WorkDirectory { New-Host $targetName $provider $target }
    $setup = Join-Path $target 'CmsSetup.cs'
    $code = Get-Content -Raw $setup
    if (-not $code.Contains('cms.AddBlogSample();')) {
        Fail "$setup does not call cms.AddBlogSample()"
    }
    [IO.File]::WriteAllText($setup, $code.Replace('cms.AddBlogSample();', "cms.AddBlogSample();`n        cms.Seeding.RunOnStart = false;"))
    Initialize-Host $targetName $target
    Initialize-Database $targetName $target $targetVariables
    $targetKey = New-ManagementKey $target $targetVariables

    Start-Step "Start $targetName and allow the locales of the package"
    $targetHost = Start-Host $targetName $target $targetVariables
    $targetHost.Key = $targetKey
    $sites = Invoke-Cms $targetHost GET '/api/management/v1/sites' $null
    if ($sites.StatusCode -ne 200) { Fail "GET /sites answered $($sites.StatusCode): $($sites.Content)" }
    $site = ($sites.Content | ConvertFrom-Json).items | Where-Object isDefault | Select-Object -First 1
    if ($null -eq $site) { Fail "GET /sites of $targetName lists no default site: $($sites.Content)" }
    $locales = Invoke-Cms $targetHost GET "/api/management/v1/sites/$($site.id)/locales" $null
    if ($locales.StatusCode -ne 200) { Fail "GET /sites/$($site.id)/locales answered $($locales.StatusCode): $($locales.Content)" }
    $allowed = @(($locales.Content | ConvertFrom-Json).items | ForEach-Object code)
    foreach ($locale in $manifest.locales | Where-Object { $_ -notin $allowed }) {
        $added = Invoke-Cms $targetHost POST "/api/management/v1/sites/$($site.id)/locales" @{ code = $locale }
        if ($added.StatusCode -ne 201) { Fail "adding the locale $locale answered $($added.StatusCode): $($added.Content)" }
        Write-Host "Allowed $locale in $targetName."
    }

    Start-Step "fluentcms import --dry-run, then fluentcms import, into $targetName"
    Use-Location $target {
        Use-Variables @{ FLUENTCMS_TOKEN = $targetKey } {
            Invoke-Checked dotnet @('fluentcms', 'import', $package, '--dry-run', '--url', $targetHost.Origin)
            Get-Entry $targetHost "/api/delivery/v1/article/by/slug/$welcomeSlug" 'en' 404 | Out-Null
            Invoke-Checked dotnet @('fluentcms', 'import', $package, '--url', $targetHost.Origin)
        }
    }

    Start-Step "The imported welcome article equals the exported one through the Delivery API"
    foreach ($locale in $manifest.locales) {
        $path = "/api/delivery/v1/article/$($welcome.id)"
        $exported = Get-Entry $sourceHost $path $locale
        $imported = Get-Entry $targetHost $path $locale
        if (-not (Compare-Json $exported $imported)) {
            Fail "the welcome article in $locale differs.`n$sourceName`: $exported`n$targetName`: $imported"
        }

        Write-Host "The welcome article in $locale is the same in $sourceName and $targetName."
    }

    Stop-Host $targetHost
    Stop-Host $sourceHost
}

$failed = $false
$total = [Diagnostics.Stopwatch]::StartNew()
try {
    Write-Host "FluentCMS $Version from packages in $WorkDirectory"

    if (-not $Feed) {
        $Feed = Join-Path $WorkDirectory 'feed'
        Start-Step "Pack every package of the solution as $Version into a folder feed"
        # A build of the repository like any other, which leaves no build server running.
        Use-Variables @{ MSBUILDDISABLENODEREUSE = '1'; DOTNET_CLI_USE_MSBUILD_SERVER = '0'; UseSharedCompilation = 'false' } {
            Invoke-Checked dotnet @('pack', (Join-Path $repository 'FluentCMS.slnx'), '-c', 'Release', '-o', $Feed, "-p:Version=$Version")
        }

        Start-Step 'Pack @fluentcms/client'
        $clientSource = Join-Path $WorkDirectory 'client-src'
        New-Item -ItemType Directory -Force $clientSource | Out-Null
        foreach ($item in 'src', 'package.json', 'package-lock.json', 'tsconfig.json') {
            Copy-Item -Recurse (Join-Path $repository "clients/typescript/$item") $clientSource
        }

        Use-Variables @{ npm_config_cache = (Join-Path $WorkDirectory 'npm-cache') } {
            Use-Location $clientSource {
                Invoke-Checked npm @('ci', '--no-audit', '--no-fund')
                Invoke-Checked npm @('run', 'build')
                Invoke-Checked npm @('pack', '--pack-destination', $Feed)
            }
        }
    }

    $Feed = Format-Path $Feed
    if (-not (Get-ChildItem $Feed -Filter "FluentCMS.Templates.$Version.nupkg")) {
        Fail "the feed $Feed has no FluentCMS.Templates.$Version.nupkg"
    }

    # A new user's machine from here on: nothing of the repository, and NuGet, npm, dotnet new and MSBuild keep their state in
    # the work directory or nowhere, so that the user's own caches neither help nor keep anything.
    Start-Step 'Isolate the work directory'
    $hive = Join-Path $WorkDirectory 'hive'
    Set-Variables @{
        NUGET_PACKAGES = Join-Path $WorkDirectory 'packages'
        NUGET_HTTP_CACHE_PATH = Join-Path $WorkDirectory 'nuget-http-cache'
        NUGET_PLUGINS_CACHE_PATH = Join-Path $WorkDirectory 'nuget-plugins-cache'
        npm_config_cache = Join-Path $WorkDirectory 'npm-cache'
        npm_config_update_notifier = 'false'
        # The SDK's first run and the resolver cache of local tools, which would otherwise find a tool restored by an earlier
        # run, live under DOTNET_CLI_HOME; the first run neither makes a certificate nor changes PATH.
        DOTNET_CLI_HOME = Join-Path $WorkDirectory 'dotnet-home'
        DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'
        MSBUILDDISABLENODEREUSE = '1'
        UseSharedCompilation = 'false'
        FLUENTCMS_TOKEN = $null
        ConnectionStrings__FluentCms = $null
    }
    Set-Content (Join-Path $WorkDirectory 'nuget.config') @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="feed" value="$Feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="feed">
      <package pattern="FluentCMS.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    Write-Host "nuget.config maps FluentCMS.* to $Feed only; NuGet packages in $env:NUGET_PACKAGES."

    Start-Step "dotnet new install FluentCMS.Templates@$Version from the folder feed"
    Use-Location $WorkDirectory {
        Invoke-Checked dotnet @('new', 'install', "FluentCMS.Templates@$Version", '--debug:custom-hive', $hive)
    }

    Test-Provider 'sqlite' @{} @{}
    if ($withPostgreSql) {
        Test-Provider 'postgresql' @{ ConnectionStrings__FluentCms = $PostgreSql } @{ ConnectionStrings__FluentCms = $PostgreSqlImport }
    }

    $plugin = 'Acme.FreshMachine'
    $pluginDirectory = Join-Path $WorkDirectory $plugin
    Start-Step "dotnet new fluentcms-plugin -n $plugin, built and tested"
    Use-Location $WorkDirectory {
        Invoke-Checked dotnet @('new', 'fluentcms-plugin', '-n', $plugin, '-o', $pluginDirectory, '--debug:custom-hive', $hive)
    }
    Assert-TemplateVersions $pluginDirectory
    Invoke-Checked dotnet @('restore', (Join-Path $pluginDirectory "$plugin.slnx"))
    Assert-FromFeed (Join-Path $pluginDirectory "src/$plugin")
    Assert-FromFeed (Join-Path $pluginDirectory "tests/$plugin.Tests")
    Invoke-Checked dotnet @('build', (Join-Path $pluginDirectory "$plugin.slnx"), '--no-restore', '-warnaserror')
    Invoke-Checked dotnet @('test', (Join-Path $pluginDirectory "$plugin.slnx"), '--no-build')
    Stop-Step
}
catch {
    $failed = $true
    $message = "fresh-machine failed at '$script:step': $($_.Exception.Message)"
    Stop-Step
    if ($onGitHub) { Write-Host "::error::$message" } else { Write-Host $message -ForegroundColor Red }
}
finally {
    foreach ($cms in @($script:hosts)) {
        try { Stop-Host $cms } catch { Write-Warning "Could not stop $($cms.Name): $($_.Exception.Message)" }
    }

    foreach ($name in $script:savedEnvironment.Keys) {
        Set-EnvironmentValue $name $script:savedEnvironment[$name]
    }
}

Write-Host ''
$script:timings | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Total: $([Math]::Round($total.Elapsed.TotalMinutes, 1)) minutes. Work directory: $WorkDirectory"
if ($failed) {
    exit 1
}

Write-Host "The M6 sequence passed from the packages of FluentCMS $Version."
