using System.Text.RegularExpressions;
using FluentCMS.Core.Commands;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// The commands of the command entry point on the provider of the run, on hosts that are built and never started, as in a
// process that runs "fluentcms <command>". MigrateOnStartup is false, which is the default on a server.
public sealed partial class MigrateCommandTests : IDisposable
{
    private const string Production = "Production";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["FluentCms:Database:MigrateOnStartup"] = "false" };

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task MigrateAppliesTheSystemPluginAndContentStreamsInOrderAndASecondRunAppliesNothing()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        using var host = Host(migrations);

        var (exitCode, output, error) = await RunAsync(host, "migrate");

        exitCode.ShouldBe(0, error);
        Streams(output).ShouldBe(["system", DemoPlugin.PluginName, "content"]);
        output.ShouldContain($"{DemoPlugin.PluginName}{Environment.NewLine}  {DemoPlugin.MigrationId}");
        output.ShouldContain($"_Initial{Environment.NewLine}Applied ");
        _database.Tables().ShouldContain("cms_article");
        _database.Tables().ShouldContain(DemoPlugin.Table);
        _database.ReadStrings("select hash from cms_schema_history").ShouldHaveSingleItem();

        using var again = Host(migrations);
        (exitCode, output, _) = await RunAsync(again, "migrate");

        exitCode.ShouldBe(0);
        output.ShouldBe($"Nothing to apply; every stream is up to date.{Environment.NewLine}");
    }

    [Fact]
    public async Task MigrateCheckListsThePendingMigrationsOfEveryStreamAndExitsWithOneUntilNoneIsPending()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);

        using (var host = Host(migrations))
        {
            var (exitCode, output, error) = await RunAsync(host, "migrate", "--check");

            exitCode.ShouldBe(1);
            Streams(output).ShouldBe(["system", DemoPlugin.PluginName, "content"]);
            error.ShouldStartWith("FCMS3303: ");
            error.ShouldContain("pending; run 'fluentcms migrate' to apply them");
            (await RunAsync(host, "migrations", "list")).Output.ShouldNotContain("  applied  ");

            var migrated = await RunAsync(host, "migrate");
            migrated.ExitCode.ShouldBe(0, migrated.Error);

            (exitCode, output, _) = await RunAsync(host, "migrate", "--check");
            exitCode.ShouldBe(0);
            output.ShouldBe($"No migration is pending.{Environment.NewLine}");
        }
    }

    [Fact]
    public async Task TwoMigrateRunsStartedTogetherApplyEachMigrationOnce()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        using var first = Host(migrations);
        using var second = Host(migrations);

        var runs = await Task.WhenAll(RunAsync(first, "migrate"), RunAsync(second, "migrate"));

        runs.ShouldAllBe(run => run.ExitCode == 0, string.Join(Environment.NewLine, runs.Select(run => run.Error)));
        var applied = runs.SelectMany(run => AppliedIds(run.Output)).ToList();
        applied.ShouldBeUnique();
        applied.Count.ShouldBe(_database.ReadStrings("select \"MigrationId\" from cms__migrations").Count + 2);
        _database.ReadStrings("select \"MigrationId\" from cms_content__migrations").ShouldHaveSingleItem();
        _database.ReadStrings($"select \"MigrationId\" from {DemoPlugin.HistoryTable}").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MigrationsListShowsWhatIsAppliedAndPendingPerStream()
    {
        using var host = Host(GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

        var (exitCode, output, _) = await RunAsync(host, "migrations", "list");

        exitCode.ShouldBe(0);
        output.ShouldContain($"{DemoPlugin.PluginName}{Environment.NewLine}  pending  {DemoPlugin.MigrationId}");
        output.ShouldNotContain("  applied  ");

        await RunAsync(host, "migrate");
        (_, output, _) = await RunAsync(host, "migrations", "list");

        output.ShouldContain($"{DemoPlugin.PluginName}{Environment.NewLine}  applied  {DemoPlugin.MigrationId}");
        output.ShouldNotContain("  pending  ");
    }

    [Fact]
    public async Task MigrationsScriptIdempotentWritesSqlThatAppliesOnAnEmptyDatabaseTwice()
    {
        using var host = Host(GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));
        var file = Path.Combine(Path.GetTempPath(), $"fluentcms-script-{Guid.NewGuid():N}.sql");

        try
        {
            var (exitCode, output, error) = await RunAsync(host, "migrations", "script", "--idempotent", "--output", file);

            if (TestProvider.IsSqlite)
            {
                // EF writes no idempotent script for SQLite.
                exitCode.ShouldBe(1);
                error.ShouldStartWith("FCMS3306: the provider of this host writes no idempotent script");
                return;
            }

            exitCode.ShouldBe(0, error);
            output.ShouldContain(file);
            var script = await File.ReadAllTextAsync(file, Token);
            Regex.Matches(script, "^-- stream (.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim()).ShouldBe(["system", DemoPlugin.PluginName, "content"]);

            ApplyScript(script);
            ApplyScript(script);

            _database.Tables().ShouldContain("cms_article");
            _database.Tables().ShouldContain(DemoPlugin.Table);
            (await RunAsync(host, "migrate", "--check")).ExitCode.ShouldBe(0);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task MigrationsScriptOfOneStreamGoesFromOneMigrationToAnother()
    {
        using var host = Host(GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

        var (exitCode, output, error) = await RunAsync(host, "migrations", "script", "--stream", DemoPlugin.PluginName, "--from", "0", "--to", DemoPlugin.MigrationId);

        exitCode.ShouldBe(0, error);
        output.ShouldStartWith($"-- stream {DemoPlugin.PluginName}");
        output.ShouldContain(DemoPlugin.Table);
        output.ShouldNotContain("cms_article");
        _database.Tables().ShouldNotContain(DemoPlugin.Table);
    }

    [Theory]
    [InlineData(new[] { "migrations", "script", "--stream", "nothing" }, "FCMS3304: this host has no stream 'nothing'; its streams are system, demo, content")]
    [InlineData(new[] { "migrations", "script", "--stream", "demo", "--from", "20200101000000_Nothing" }, "FCMS3305: stream demo has no migration '20200101000000_Nothing'")]
    [InlineData(new[] { "migrations", "script", "--to", "20200101000000_Nothing" }, "FCMS3300: --from and --to name a migration of one stream, so they need --stream")]
    [InlineData(new[] { "migrate", "--force" }, "FCMS3300: --force is not an option of 'fluentcms migrate'.")]
    [InlineData(new[] { "migrations", "add", "Initial" }, "FCMS3300: 'migrations add Initial' is not a command that this host runs.")]
    public async Task BadUsageInTheHostPrintsTheUsageAndExitsWithTwo(string[] args, string message)
    {
        using var host = Host(GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

        var (exitCode, _, error) = await RunAsync(host, args);

        exitCode.ShouldBe(2);
        error.ShouldStartWith(message);
        error.ShouldContain("Usage: fluentcms ");
    }

    private IHost Host(System.Reflection.Assembly migrations) =>
        _database.CreateHost(ArticleSchemas.Titled, Production, migrations, NoMigrationOnStartup, services => DemoPlugin.Add(services, _database));

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IHost host, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }

    // The streams named in the output of migrate, in their order: a line that is not indented and is no summary.
    private static List<string> Streams(string output) =>
        [.. output.Split(Environment.NewLine).Where(line => line.Length > 0 && !line.StartsWith(' ') && !line.StartsWith("Applied ", StringComparison.Ordinal))];

    private static IEnumerable<string> AppliedIds(string output) =>
        output.Split(Environment.NewLine).Where(line => line.StartsWith("  ", StringComparison.Ordinal)).Select(line => line.Trim());

    // A SQL Server script separates its batches with GO, which only tools understand.
    private void ApplyScript(string script)
    {
        var batches = TestProvider.IsSqlServer ? GoSeparator().Split(script) : [script];
        foreach (var batch in batches.Where(batch => batch.Trim().Length > 0))
        {
            _database.Execute(batch);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline)]
    private static partial Regex GoSeparator();
}
