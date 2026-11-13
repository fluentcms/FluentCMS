using System.Text.RegularExpressions;
using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins.AiAssist;
using FluentCMS.Plugins.Mcp;
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.MultiSite;
using FluentCMS.Plugins.Search;
using FluentCMS.Plugins.Webhooks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// Each system and plugin stream starts with one baseline migration, Initial, for each provider (#3052). The chains it replaced
// are kept here only as test data, in BeforeBaseline/{provider}/{stream}.sql: the SQL that 'dotnet ef migrations script' wrote
// for them before they were deleted. This class builds a database with that SQL and one with the baseline, on the provider of
// the run, and compares them, and it checks that a database of the chain is refused and left as it was.
public sealed partial class BaselineTests : IDisposable
{
    private const string Production = "Production";

    // In the order a host applies them.
    private static readonly (string Stream, Action<FluentCmsBuilder> Add)[] Streams =
    [
        (MigrationRunner.SystemStream, _ => { }),
        ("media", cms => cms.AddPlugin<MediaPlugin>()),
        ("webhooks", cms => cms.AddPlugin<WebhooksPlugin>()),
        ("mcp", cms => cms.AddPlugin<McpPlugin>()),
        ("multi-site", cms => cms.AddPlugin<MultiSitePlugin>()),
        ("ai", cms => cms.AddPlugin<AiAssistPlugin>()),
        ("search", cms => cms.AddPlugin<SearchPlugin>()),
    ];

    // What the chain left that the model does not say, so the baseline does not make it. A NOT NULL column that a migration
    // added to a table with rows got a default for those rows; EF writes the column's value on every insert.
    private static readonly (string Table, string Column)[] DefaultsOfAddedColumns = [("cms_role", "require_two_factor"), ("cms_webhooks_delivery", "manual")];

    // The chain raised the stamp of the Author role when it changed that role's seeded grants, so that a cached grant was
    // read again; the baseline seeds every role at its first stamp.
    private const string AuthorRole = "00000000-0000-7000-8000-000000000003";

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            if (TestProvider.IsSqlServer)
            {
                SetSnapshot(_database, true);
            }
        }
        finally
        {
            _database.Dispose();
        }
    }

    [Fact]
    public async Task TheBaselineBuildsWhatTheChainBeforeItBuilt()
    {
        using var baseline = TestDatabase.Create();
        if (TestProvider.IsSqlServer)
        {
            // From model, both databases have it on already, so the chain's database has it off to show that the chain turns
            // it on. SqlServerSnapshotTests show that the baseline does: turning it on ends the database's other sessions,
            // which would include the pooled ones of the runner here.
            SetSnapshot(_database, false);
        }

        foreach (var (stream, _) in Streams)
        {
            ApplyChain(_database, stream);
        }

        using (var host = baseline.CreateHost(ArticleSchemas.Titled, Production, configure: cms => Array.ForEach(Streams, stream => stream.Add(cms))))
        {
            var applied = await host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);
            applied.Select(migration => migration.Stream).ShouldBe(Streams.Select(stream => stream.Stream));
            applied.ShouldAllBe(migration => migration.Id.EndsWith("_Initial", StringComparison.Ordinal));
        }

        var chain = WithoutWhatTheModelDoesNotSay(DatabaseCatalog.Of(_database, historyRows: false));
        var built = DatabaseCatalog.Of(baseline, historyRows: false);

        chain.Except(built).ShouldBeEmpty("made by the chain and not by the baseline");
        built.Except(chain).ShouldBeEmpty("made by the baseline and not by the chain");
        chain.ShouldBe(built);

        // What no scaffold writes, which the baseline carries by hand.
        if (TestProvider.IsSqlite)
        {
            built.ShouldContain(line => line.StartsWith("table:cms_search_entry_fts:CREATE VIRTUAL TABLE", StringComparison.Ordinal));
            built.Count(line => line.StartsWith("trigger:cms_search_entry.cms_search_entry_fts_", StringComparison.Ordinal)).ShouldBe(3);
        }
        else if (TestProvider.IsPostgreSql)
        {
            built.ShouldContain(line => line.StartsWith("index:cms_search_entry.ix_cms_search_entry_text:", StringComparison.Ordinal) && line.Contains("USING gin", StringComparison.Ordinal));
        }
        else
        {
            built.ShouldContain("option:read_committed_snapshot=1");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AHostRefusesADatabaseOfTheChainAndAppliesNothing(bool migrateOnStartup)
    {
        ApplyChain(_database, MigrationRunner.SystemStream);
        var before = DatabaseCatalog.Of(_database, historyRows: true);

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, settings: new() { ["FluentCms:Database:MigrateOnStartup"] = migrateOnStartup ? "true" : "false" });
        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1307");
        exception.Message.ShouldBe($"FCMS1307: stream system has applied {FirstOfChain(MigrationRunner.SystemStream)} but not its baseline {BaselineOf<SystemDbContext>(host.Services)}; {WhatToDo}");
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    [Fact]
    public async Task MigrateRefusesADatabaseOfTheChainAndAppliesNothing()
    {
        ApplyChain(_database, MigrationRunner.SystemStream);
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, settings: new() { ["FluentCms:Database:MigrateOnStartup"] = "false" });

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, "migrate"], output, error, Token);

        exitCode.ShouldBe(1);
        output.ToString().ShouldBeEmpty();
        error.ToString().ShouldBe($"FCMS1307: stream system has applied {FirstOfChain(MigrationRunner.SystemStream)} but not its baseline {BaselineOf<SystemDbContext>(host.Services)}; {WhatToDo}{Environment.NewLine}");
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    [Fact]
    public async Task APluginStreamOfTheChainIsRefusedOnADatabaseWhoseSystemStreamIsOnTheBaseline()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, configure: cms => cms.AddPlugin<MediaPlugin>());
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.MigrateAsync(Token);
        }

        ApplyChain(_database, "media");
        var before = DatabaseCatalog.Of(_database, historyRows: true);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token));

        exception.Message.ShouldBe($"FCMS1307: stream media has applied {FirstOfChain("media")} but not its baseline {BaselineOf<PluginDbContext<MediaPlugin>>(host.Services)}; {WhatToDo}");
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    private const string WhatToDo = "nothing was applied: the database was made by the migrations from before the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again";

    private static string ProviderFolder => TestProvider.Name switch
    {
        TestProvider.Sqlite => "Sqlite",
        TestProvider.PostgreSql => "PostgreSql",
        _ => "SqlServer",
    };

    private static string HistoryTable(string stream) =>
        stream == MigrationRunner.SystemStream ? SystemDbContext.HistoryTable : $"cms_{stream.Replace('-', '_')}__migrations";

    // A SQL Server script separates its batches with GO, which only tools understand. A SQLite file that the provider made is
    // in WAL mode.
    private static void ApplyChain(TestDatabase database, string stream)
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Migrations", "BeforeBaseline", ProviderFolder, $"{stream}.sql"));
        database.UseJournalMode("wal");
        if (TestProvider.IsSqlServer)
        {
            database.ExecuteBatches(GoSeparator().Split(script));
        }
        else
        {
            database.Execute(script);
        }
    }

    // EF lists the history of a stream in the order of the ids.
    private string FirstOfChain(string stream) =>
        _database.ReadStrings($"select \"MigrationId\" from {HistoryTable(stream)} order by \"MigrationId\"")[0];

    private static string BaselineOf<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContext>().Database.GetMigrations().ShouldHaveSingleItem();
    }

    private static List<string> WithoutWhatTheModelDoesNotSay(List<string> catalog)
    {
        var result = new List<string>(catalog);
        foreach (var (table, column) in DefaultsOfAddedColumns)
        {
            var at = result.FindIndex(line => line.StartsWith($"column:{table}.{column}:", StringComparison.Ordinal));
            at.ShouldBeGreaterThanOrEqualTo(0, $"{table}.{column}");
            DefaultValue().Match(result[at]).Value.ShouldNotBeEmpty($"{table}.{column} has no default to leave out");
            result[at] = DefaultValue().Replace(result[at], "");
        }

        var author = result.FindIndex(line => line.StartsWith("row:cms_role:", StringComparison.Ordinal) && line.Contains($"|id={AuthorRole}|", StringComparison.OrdinalIgnoreCase));
        author.ShouldBeGreaterThanOrEqualTo(0, "the Author role");
        result[author].ShouldContain("|permissions_version=2|");
        result[author] = result[author].Replace("|permissions_version=2|", "|permissions_version=1|", StringComparison.Ordinal);

        return [.. result.Order(StringComparer.Ordinal)];
    }

    private static void SetSnapshot(TestDatabase database, bool on)
    {
        using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) <> {(on ? 1 : 0)} ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT {(on ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE";
        command.ExecuteNonQuery();
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline)]
    private static partial Regex GoSeparator();

    // The default of a column line of DatabaseCatalog, up to the next field.
    [GeneratedRegex("(?<=:default=)[^:]*")]
    private static partial Regex DefaultValue();
}
