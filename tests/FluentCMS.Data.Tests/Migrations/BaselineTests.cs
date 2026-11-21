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

    // What a preview added to the baseline after the squash, which the chain never made: a preview does not upgrade a
    // database (10.4, H5), so a change of the model goes into the baseline until 1.0.0.
    private static readonly string[] TablesAddedToTheBaseline = ["cms_default_grant", "cms_user_preference"];
    private static readonly (string Table, string Column)[] ColumnsAddedToTheBaseline = [("cms_version", "author_kind"), ("cms_version", "author_name"), ("cms_media_asset", "owns_file"), ("cms_media_asset", "version"), ("cms_search_entry", "field"), ("cms_ai_usage", "principal_name")];

    // Indexes added the same way, by the prefix of their catalog lines.
    private static readonly string[] IndexesAddedToTheBaseline = ["index:cms_media_asset.ix_cms_media_asset_site_owns_file:", "index:cms_webhooks_delivery.ix_cms_webhooks_delivery_queued:", "index:cms_job.ix_cms_job_status_completed:", "index:cms_outbox.ix_cms_outbox_failed:", "index:cms_outbox.ix_cms_outbox_handler_pending:", "index:cms_outbox.ix_cms_outbox_failed_available_at:", "index:cms_outbox.ix_cms_outbox_failed_handler:"];

    // The keys, in order with their directions, and the filter of the core indexes added to the baseline, as every provider's
    // catalog line reads once quotes, brackets, parentheses, casts and spaces are taken out.
    private static readonly (string Index, string Keys, string? Filter)[] AddedIndexShapes =
    [
        ("index:cms_job.ix_cms_job_status_completed:", "status,completed_atdesc,id", null),
        ("index:cms_outbox.ix_cms_outbox_failed:", "sequencedesc,id", "status='failed'"),
        ("index:cms_outbox.ix_cms_outbox_handler_pending:", "handler,sequence,id", "status='pending'"),
        ("index:cms_outbox.ix_cms_outbox_failed_available_at:", "available_at,attempts", "status='failed'"),
        ("index:cms_outbox.ix_cms_outbox_failed_handler:", "handler", "status='failed'"),
    ];

    private static string Normalized(string line) =>
        new([.. line.Replace("::text", "", StringComparison.Ordinal).ToLowerInvariant().Where(character => character is not ('"' or '[' or ']' or '(' or ')' or ' ' or '`'))]);

    // Indexes whose columns the baseline changed, each with a column the baseline's has and the chain's does not.
    private static readonly (string Index, string Column)[] IndexesChangedInTheBaseline = [("index:cms_search_entry.ux_cms_search_entry_document:", "field")];

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
        var built = WithoutWhatWasAddedToTheBaseline(DatabaseCatalog.Of(baseline, historyRows: false));

        chain.Except(built).ShouldBeEmpty("made by the chain and not by the baseline");
        built.Except(chain).ShouldBeEmpty("made by the baseline and not by the chain");
        chain.ShouldBe(built);

        // The indexes the baseline added are left out of that comparison, so their shape is checked here.
        var all = DatabaseCatalog.Of(baseline, historyRows: false);
        foreach (var (index, keys, filter) in AddedIndexShapes)
        {
            var line = Normalized(all.Single(item => item.StartsWith(index, StringComparison.Ordinal)));
            line.ShouldContain(keys, Case.Sensitive, index);
            if (filter is null)
            {
                line.ShouldNotContain("status='", Case.Sensitive, index);
            }
            else
            {
                line.ShouldContain(filter, Case.Sensitive, index);
            }
        }

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
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, settings: NoMigrationOnStartup);

        var (exitCode, output, error) = await RunAsync(host, "migrate");

        exitCode.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldBe($"FCMS1307: stream system has applied {FirstOfChain(MigrationRunner.SystemStream)} but not its baseline {BaselineOf<SystemDbContext>(host.Services)}; {WhatToDo}{Environment.NewLine}");
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    [Fact]
    public async Task DoctorNamesADatabaseOfTheChainAsAReasonTheHostWouldNotStart()
    {
        ApplyChain(_database, MigrationRunner.SystemStream);
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, settings: NoMigrationOnStartup);

        var (exitCode, _, error) = await RunAsync(host, "doctor");

        exitCode.ShouldBe(1);
        error.ShouldContain($"FCMS3325: the host would not start: FCMS1307: stream system has applied {FirstOfChain(MigrationRunner.SystemStream)} but not its baseline {BaselineOf<SystemDbContext>(host.Services)}; {WhatToDo}{Environment.NewLine}");
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

    // Until 1.0.0 a change of the model goes into the baseline, so a database made by an earlier build of it holds the
    // baseline in its history, has nothing pending and lacks what came since, as one without the dropped tables does.
    [Fact]
    public async Task ADatabaseOfAnEarlierBuildOfTheBaselineIsRefusedAndNothingIsApplied()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<MediaPlugin>()))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        _database.Execute($"drop table {TestProvider.Quote(TablesAddedToTheBaseline[0])}");
        _database.Execute($"drop table {TestProvider.Quote("cms_media_preset")}");
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<MediaPlugin>());
        var refusal = $"FCMS1308: stream system has applied its baseline {BaselineOf<SystemDbContext>(host.Services)} but lacks cms_default_grant; " +
            $"stream media has applied its baseline {BaselineOf<PluginDbContext<MediaPlugin>>(host.Services)} but lacks cms_media_preset; " +
            "nothing was applied: the database was made by an earlier build of the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again";

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));
        var (exitCode, _, error) = await RunAsync(host, "migrate");

        exception.Code.ShouldBe("FCMS1308");
        exception.Message.ShouldBe(refusal);
        exitCode.ShouldBe(1);
        error.ShouldBe(refusal + Environment.NewLine);
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    // A column folded into a table the earlier build already made, as cms_version.author_kind (#3097), is missed the same way.
    [Fact]
    public async Task ADatabaseOfAnEarlierBuildOfTheBaselineThatLacksAColumnIsRefused()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        var (table, column) = ColumnsAddedToTheBaseline[0];
        _database.Execute($"alter table {TestProvider.Quote(table)} drop column {TestProvider.Quote(column)}");
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production);
        var refusal = $"FCMS1308: stream system has applied its baseline {BaselineOf<SystemDbContext>(host.Services)} but lacks {table}.{column}; " +
            "nothing was applied: the database was made by an earlier build of the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again";

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));
        var (exitCode, _, error) = await RunAsync(host, "migrate");

        exception.Code.ShouldBe("FCMS1308");
        exception.Message.ShouldBe(refusal);
        exitCode.ShouldBe(1);
        error.ShouldBe(refusal + Environment.NewLine);
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    // The same for a plugin stream: a media database of a build before owns_file (#3133).
    [Fact]
    public async Task AMediaDatabaseOfAnEarlierBuildOfTheBaselineThatLacksAColumnIsRefused()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<MediaPlugin>()))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        var (table, column) = ColumnsAddedToTheBaseline.Last(added => added.Table == "cms_media_asset");
        var index = TestProvider.Quote("ix_cms_media_asset_site_owns_file");
        _database.Execute(TestProvider.IsSqlServer ? $"drop index {index} on {TestProvider.Quote(table)}" : $"drop index {index}");
        _database.Execute($"alter table {TestProvider.Quote(table)} drop column {TestProvider.Quote(column)}");
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<MediaPlugin>());
        var refusal = $"FCMS1308: stream media has applied its baseline {BaselineOf<PluginDbContext<MediaPlugin>>(host.Services)} but lacks {table}.{column}; " +
            "nothing was applied: the database was made by an earlier build of the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again";

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1308");
        exception.Message.ShouldBe(refusal);
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    // And for the AI usage log of a build before principal_name (#3418), which 1.0.0-preview.1 made.
    [Fact]
    public async Task AnAiDatabaseOfAnEarlierBuildOfTheBaselineThatLacksAColumnIsRefused()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<AiAssistPlugin>()))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        var (table, column) = ColumnsAddedToTheBaseline.Last(added => added.Table == "cms_ai_usage");
        _database.Execute($"alter table {TestProvider.Quote(table)} drop column {TestProvider.Quote(column)}");
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<AiAssistPlugin>());
        var refusal = $"FCMS1308: stream ai has applied its baseline {BaselineOf<PluginDbContext<AiAssistPlugin>>(host.Services)} but lacks {table}.{column}; " +
            "nothing was applied: the database was made by an earlier build of the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again";

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1308");
        exception.Message.ShouldBe(refusal);
        DatabaseCatalog.Of(_database, historyRows: true).ShouldBe(before);
    }

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["FluentCms:Database:MigrateOnStartup"] = "false" };

    private const string WhatToDo = "nothing was applied: the database was made by the migrations from before the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again";

    private static string ProviderFolder => TestProvider.Name switch
    {
        TestProvider.Sqlite => "Sqlite",
        TestProvider.PostgreSql => "PostgreSql",
        _ => "SqlServer",
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IHost host, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(host.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }

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

        foreach (var (index, column) in IndexesChangedInTheBaseline)
        {
            result.ShouldContain(line => line.StartsWith(index, StringComparison.Ordinal) && !line.Contains(column, StringComparison.Ordinal), index);
            result.RemoveAll(line => line.StartsWith(index, StringComparison.Ordinal));
        }

        var author = result.FindIndex(line => line.StartsWith("row:cms_role:", StringComparison.Ordinal) && line.Contains($"|id={AuthorRole}|", StringComparison.OrdinalIgnoreCase));
        author.ShouldBeGreaterThanOrEqualTo(0, "the Author role");
        result[author].ShouldContain("|permissions_version=2|");
        result[author] = result[author].Replace("|permissions_version=2|", "|permissions_version=1|", StringComparison.Ordinal);

        return [.. result.Order(StringComparer.Ordinal)];
    }

    // A line names its table after its kind, as in column:cms_default_grant.role_id:..., and what was added must be there.
    private static List<string> WithoutWhatWasAddedToTheBaseline(List<string> catalog)
    {
        foreach (var table in TablesAddedToTheBaseline)
        {
            catalog.Any(line => line.StartsWith("table:", StringComparison.Ordinal) && OfTable(line, table)).ShouldBeTrue(table);
        }

        foreach (var (table, column) in ColumnsAddedToTheBaseline)
        {
            catalog.Any(line => OfColumn(line, table, column)).ShouldBeTrue($"{table}.{column}");
        }

        foreach (var added in IndexesAddedToTheBaseline)
        {
            catalog.Any(line => line.StartsWith(added, StringComparison.Ordinal)).ShouldBeTrue(added);
        }

        foreach (var (index, column) in IndexesChangedInTheBaseline)
        {
            catalog.ShouldContain(line => line.StartsWith(index, StringComparison.Ordinal) && line.Contains(column, StringComparison.Ordinal), index);
        }

        return [.. catalog.Where(line => !TablesAddedToTheBaseline.Any(table => OfTable(line, table)) && !ColumnsAddedToTheBaseline.Any(added => OfColumn(line, added.Table, added.Column)) && !IndexesAddedToTheBaseline.Any(added => line.StartsWith(added, StringComparison.Ordinal)) && !IndexesChangedInTheBaseline.Any(changed => line.StartsWith(changed.Index, StringComparison.Ordinal)))];

        static bool OfTable(string line, string table)
        {
            var name = line.AsSpan(line.IndexOf(':', StringComparison.Ordinal) + 1);
            return name.StartsWith(table, StringComparison.Ordinal) && (name.Length == table.Length || name[table.Length] is '.' or ':');
        }

        static bool OfColumn(string line, string table, string column) => line.StartsWith($"column:{table}.{column}:", StringComparison.Ordinal);
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
