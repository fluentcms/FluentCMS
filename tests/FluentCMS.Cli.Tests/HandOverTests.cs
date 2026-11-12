using System.Diagnostics;
using Shouldly;
using Xunit;

namespace FluentCMS.Cli.Tests;

// The tool checks the command line and starts the host with "fluentcms <command>"; the command runs in the host's process
// with its configuration and plugins, and its output and exit code come back through the tool.
public sealed class HandOverTests : IDisposable
{
    private readonly ToolRun _tool = new();

    public void Dispose() => _tool.Dispose();

    [Fact]
    public async Task MigrateAppliesEveryStreamOfTheHostWithItsPluginsAndASecondRunAppliesNothing()
    {
        (await _tool.HostCommandAsync("migrate")).ShouldBe(0, _tool.Error.ToString());

        var output = _tool.Output.ToString();
        output.ShouldContain($"system{Environment.NewLine}  ");
        output.ShouldContain($"sample{Environment.NewLine}  ");
        output.IndexOf("system", StringComparison.Ordinal).ShouldBeLessThan(output.IndexOf("sample", StringComparison.Ordinal));
        output.ShouldContain("Applied ");
        _tool.Exists("default.db").ShouldBeTrue();

        (await _tool.HostCommandAsync("migrate")).ShouldBe(0, _tool.Error.ToString());
        _tool.Output.ToString().ShouldContain("Nothing to apply; every stream is up to date.");
    }

    [Fact]
    public async Task MigrateCheckListsWhatIsPendingAndExitsWithOneUntilNothingIs()
    {
        (await _tool.HostCommandAsync("migrate", "--check")).ShouldBe(1);
        _tool.Output.ToString().ShouldContain($"sample{Environment.NewLine}  ");
        _tool.Error.ToString().ShouldContain("FCMS3303: ");
        _tool.Error.ToString().ShouldContain("run 'fluentcms migrate' to apply them");

        (await _tool.HostCommandAsync("migrate")).ShouldBe(0);

        (await _tool.HostCommandAsync("migrate", "--check")).ShouldBe(0);
        _tool.Output.ToString().ShouldContain("No migration is pending.");
    }

    [Fact]
    public async Task TheSchemaCommandsAndDoctorRunInTheBuiltHost()
    {
        (await _tool.HostCommandAsync("schema", "snapshot", "--output", Path.Combine(_tool.Root, "schema.json"))).ShouldBe(0, _tool.Error.ToString());
        _tool.Exists("schema.json").ShouldBeTrue();

        (await _tool.HostCommandAsync("schema", "validate")).ShouldBe(0, _tool.Error.ToString());
        _tool.Output.ToString().ShouldStartWith("The host's schema is valid: 0 types");

        (await _tool.HostCommandAsync("schema", "diff", "--schema", Path.Combine(_tool.Root, "schema.json"))).ShouldBe(0, _tool.Error.ToString());

        // Nothing is migrated yet, so doctor names what is pending and changes nothing.
        (await _tool.HostCommandAsync("doctor", "--json")).ShouldBe(1);
        _tool.Output.ToString().ShouldContain("\"pendingMigrations\":[{");
        _tool.Error.ToString().ShouldContain("FCMS3303: ");
        (await _tool.HostCommandAsync("migrate", "--check")).ShouldBe(1);
    }

    [Fact]
    public async Task TheEnvironmentChoosesTheHostsConfiguration()
    {
        _tool.WriteSettings("appsettings.Staging.json", "staging.db");

        (await _tool.HostCommandAsync("migrate", "--environment", "Staging")).ShouldBe(0, _tool.Error.ToString());

        _tool.Exists("staging.db").ShouldBeTrue();
        _tool.Exists("default.db").ShouldBeFalse();
    }

    [Fact]
    public async Task MigrationsListShowsAppliedAndPendingPerStream()
    {
        (await _tool.HostCommandAsync("migrations", "list")).ShouldBe(0, _tool.Error.ToString());
        _tool.Output.ToString().ShouldContain("  pending  ");
        _tool.Output.ToString().ShouldNotContain("  applied  ");

        await _tool.HostCommandAsync("migrate");
        (await _tool.HostCommandAsync("migrations", "list", "--json")).ShouldBe(0, _tool.Error.ToString());

        var json = _tool.Output.ToString().Trim();
        json.ShouldStartWith("{\"streams\":[{\"name\":\"system\",\"applied\":[\"");
        json.ShouldContain("{\"name\":\"sample\",\"applied\":[\"");
        json.ShouldNotContain("\"pending\":[\"");
    }

    [Fact]
    public async Task MigrationsScriptWritesTheFileTheCallerNamed()
    {
        var file = Path.Combine(_tool.Root, "sample.sql");

        (await _tool.HostCommandAsync("migrations", "script", "--stream", "sample", "--output", file)).ShouldBe(0, _tool.Error.ToString());

        File.ReadAllText(file).ShouldContain("CREATE TABLE \"cms_sample_note\"");
        _tool.Exists("default.db").ShouldBeFalse();
    }

    [Fact]
    public async Task AFailureInTheHostComesBackWithItsCodeAndExitCode()
    {
        (await _tool.HostCommandAsync("migrations", "script", "--stream", "nothing")).ShouldBe(2);

        _tool.Error.ToString().ShouldContain("FCMS3304: this host has no stream 'nothing'; its streams are system, sample, content");
    }

    // The relative database file of the configuration is the project directory's, in both hosts.
    [Fact]
    public async Task ABackupOfTheHostRestoresIntoTheHostOfAnotherProjectDirectoryOnce()
    {
        (await _tool.HostCommandAsync("migrate")).ShouldBe(0, _tool.Error.ToString());
        var archive = Path.Combine(_tool.Root, "backup.zip");

        (await _tool.HostCommandAsync("backup", "--output", archive)).ShouldBe(0, _tool.Error.ToString());
        _tool.Output.ToString().ShouldStartWith($"Wrote {archive} with the database");

        using var other = new ToolRun();
        (await other.HostCommandAsync("restore", archive)).ShouldBe(0, other.Error.ToString());
        other.Exists("default.db").ShouldBeTrue();
        other.Output.ToString().ShouldEndWith($"No migration is pending.{Environment.NewLine}");

        (await other.HostCommandAsync("restore", archive)).ShouldBe(1);
        other.Error.ToString().ShouldContain($"{Environment.NewLine}FCMS3244: ");
    }

    [Fact]
    public async Task TheToolHoldsNoReferenceToTheHostsTypes()
    {
        (await _tool.HostCommandAsync("migrations", "list")).ShouldBe(0, _tool.Error.ToString());

        AppDomain.CurrentDomain.GetAssemblies().ShouldNotContain(assembly => assembly.GetName().Name == Path.GetFileNameWithoutExtension(ToolRun.HostAssembly));
    }

    [Fact]
    public async Task AnAssemblyThatIsNotThereIsNamed()
    {
        (await _tool.RunAsync("migrate", "--assembly", Path.Combine(_tool.Root, "Missing.dll"))).ShouldBe(1);

        _tool.Error.ToString().ShouldContain("FCMS3311: ");
    }

    // The installed tool: a process of its own, on the console, that reads the project's TargetPath.
    [Fact]
    public async Task InProjectModeTheToolRunsTheBuiltProjectInItsDirectory()
    {
        var tool = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _tool.Root };
        foreach (var argument in new[] { typeof(CommandLine).Assembly.Location, "migrate", "--project", ToolRun.HostProject, "--no-build" })
        {
            tool.ArgumentList.Add(argument);
        }

        // MSBuild takes the configuration the tests were built in from the environment, and the host its database.
        tool.Environment["Configuration"] = ToolRun.Configuration;
        tool.Environment["ConnectionStrings__FluentCms"] = $"Data Source={Path.Combine(_tool.Root, "project.db")};Pooling=False";

        using var process = Process.Start(tool)!;
        var error = process.StandardError.ReadToEndAsync(ToolRun.Token);
        var output = await process.StandardOutput.ReadToEndAsync(ToolRun.Token);
        await process.WaitForExitAsync(ToolRun.Token);

        process.ExitCode.ShouldBe(0, await error);
        output.ShouldContain("Applied ");
        _tool.Exists("project.db").ShouldBeTrue();
    }
}
