using System.Text.RegularExpressions;
using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Identity;
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
using Microsoft.Extensions.Logging;
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
    private static readonly string[] TablesAddedToTheBaseline = ["cms_default_grant", "cms_user_preference", "cms_plugin_store"];
    private static readonly (string Table, string Column)[] ColumnsAddedToTheBaseline = [("cms_version", "author_kind"), ("cms_version", "author_name"), ("cms_media_asset", "owns_file"), ("cms_media_asset", "version"), ("cms_search_entry", "field"), ("cms_ai_usage", "principal_name"), ("cms_signing_key", "sequence"), ("cms_signing_key", "active_from"), ("cms_signing_key", "retired_at")];

    // Indexes added the same way, by the prefix of their catalog lines.
    private static readonly string[] IndexesAddedToTheBaseline = ["index:cms_media_asset.ix_cms_media_asset_site_owns_file:", "index:cms_webhooks_delivery.ix_cms_webhooks_delivery_queued:", "index:cms_job.ix_cms_job_status_completed:", "index:cms_outbox.ix_cms_outbox_failed:", "index:cms_outbox.ix_cms_outbox_handler_pending:", "index:cms_outbox.ix_cms_outbox_failed_available_at:", "index:cms_outbox.ix_cms_outbox_failed_handler:", "index:cms_signing_key.uq_cms_signing_key_use_sequence:"];

    // The keys, in order with their directions, and the filter of the core indexes added to the baseline, as every provider's
    // catalog line reads once quotes, brackets, parentheses, casts and spaces are taken out.
    private static readonly (string Index, string Keys, string? Filter)[] AddedIndexShapes =
    [
        ("index:cms_job.ix_cms_job_status_completed:", "status,completed_atdesc,id", null),
        ("index:cms_outbox.ix_cms_outbox_failed:", "sequencedesc,id", "status='failed'"),
        ("index:cms_outbox.ix_cms_outbox_handler_pending:", "handler,sequence,id", "status='pending'"),
        ("index:cms_outbox.ix_cms_outbox_failed_available_at:", "available_at,attempts", "status='failed'"),
        ("index:cms_outbox.ix_cms_outbox_failed_handler:", "handler", "status='failed'"),
        ("index:cms_signing_key.uq_cms_signing_key_use_sequence:", "use,sequence", null),
    ];

    // Without the index's name, which can hold a key's, as ix_cms_outbox_failed_handler does.
    private static string DefinitionOf(List<string> catalog, string index)
    {
        var line = catalog.Single(item => item.StartsWith(index, StringComparison.Ordinal))[index.Length..];
        return Normalized(line.Replace(index[(index.IndexOf('.', StringComparison.Ordinal) + 1)..^1], "", StringComparison.Ordinal));
    }

    // The whole key list of an index, and what it includes after it, so that a key too many or an included column fails the
    // comparison as a wrong key does. SQLite and PostgreSQL list the keys in the parentheses after the table, and SQL Server
    // in fields of its own.
    private static string KeysOf(List<string> catalog, string index)
    {
        var line = catalog.Single(item => item.StartsWith(index, StringComparison.Ordinal))[index.Length..];
        string keys;
        string included;
        if (TestProvider.IsSqlServer)
        {
            var fields = SqlServerKeys().Match(line);
            keys = fields.Groups["keys"].Value;
            included = fields.Groups["include"].Value;
        }
        else
        {
            var start = line.IndexOf('(', line.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase));
            var end = start + 1;
            for (var depth = 1; depth > 0; end++)
            {
                depth += line[end] switch { '(' => 1, ')' => -1, _ => 0 };
            }

            keys = line[(start + 1)..(end - 1)];
            included = Include().Match(line[end..]).Groups["columns"].Value;
        }

        return Normalized(keys) + (included.Length == 0 ? "" : $"include:{Normalized(included)}");
    }

    [GeneratedRegex(":keys=(?<keys>[^:]*):include=(?<include>.*)$")]
    private static partial Regex SqlServerKeys();

    [GeneratedRegex(@"^\s*INCLUDE\s*\((?<columns>[^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex Include();

    private static string Normalized(string line) =>
        new([.. line.Replace("::text", "", StringComparison.Ordinal).ToLowerInvariant().Where(character => character is not ('"' or '[' or ']' or '(' or ')' or ' ' or '`'))]);

    // Indexes the baseline changed, each with a column the baseline's has and the chain's does not, and the whole line of the
    // baseline's on the provider of the run, so that its uniqueness and its other columns are held too.
    private static readonly (string Index, string Column, string Line)[] IndexesChangedInTheBaseline =
    [
        ("index:cms_search_entry.ux_cms_search_entry_document:", "field", TestProvider.Name switch
        {
            TestProvider.Sqlite => "createuniqueindexux_cms_search_entry_documentoncms_search_entrydocument_id,locale,state,field",
            TestProvider.PostgreSql => "createuniqueindexux_cms_search_entry_documentonpublic.cms_search_entryusingbtreedocument_id,locale,state,field",
            _ => "nonclustered:unique=1:pk=0:constraint=0:filter=:keys=document_id,locale,state,field:include=",
        }),
    ];

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
            KeysOf(all, index).ShouldBe(keys, index);
            var line = DefinitionOf(all, index);
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

    // An index of that name on another key, on the right key with one more after it, and on another key with the right one
    // included, which SQLite cannot make.
    [Theory]
    [InlineData("sequence", null)]
    [InlineData("handler,sequence", null)]
    [InlineData("sequence", "handler")]
    public async Task TheShapeCheckFailsForAnIndexOfTheRightNameOnOtherKeys(string columns, string? include)
    {
        Assert.SkipWhen(include is not null && TestProvider.IsSqlite, "SQLite has no included columns.");
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production);
        await host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);
        var (index, keys, _) = AddedIndexShapes.Single(shape => shape.Index == "index:cms_outbox.ix_cms_outbox_failed_handler:");
        KeysOf(DatabaseCatalog.Of(_database, historyRows: false), index).ShouldBe(keys);

        var name = TestProvider.Quote("ix_cms_outbox_failed_handler");
        var table = TestProvider.Quote("cms_outbox");
        string Quoted(string list) => string.Join(", ", list.Split(',').Select(TestProvider.Quote));
        _database.Execute(TestProvider.IsSqlServer ? $"drop index {name} on {table}" : $"drop index {name}");
        _database.Execute($"create index {name} on {table} ({Quoted(columns)}){(include is null ? "" : $" include ({Quoted(include)})")} where {TestProvider.Quote("status")} = 'failed'");

        KeysOf(DatabaseCatalog.Of(_database, historyRows: false), index).ShouldNotBe(keys);
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

    // A host that does not migrate takes no lock, so the order of its reads alone keeps it from refusing the database: the
    // other process applies the media stream just after the host first read the catalog.
    [Fact]
    public async Task ADatabaseThatAnotherProcessIsCreatingIsNotRefusedAsAnEarlierBaseline()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        using var creating = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.AddPlugin<MediaPlugin>());
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, settings: NoMigrationOnStartup, configure: cms => cms.AddPlugin<MediaPlugin>());
        var catalog = new CatalogReader(
            host.Services.GetRequiredService<IDataProvider>().SchemaReader,
            () => creating.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token));
        var runner = InterceptedProvider.Runner(host.Services, (member, value) => member == $"get_{nameof(IDataProvider.SchemaReader)}" ? catalog : value);

        await runner.RunAtStartupAsync(Token);

        _database.Tables().ShouldContain("cms_media_preset");
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

    // An index folded into the baseline later, as ix_cms_job_status_completed (#3285), only makes what it serves read more
    // rows, so a database without it starts and the index is named (#3407). One made by the baseline names none.
    [Fact]
    public async Task ADatabaseOfAnEarlierBuildOfTheBaselineThatLacksAnIndexStartsAndNamesIt()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => Array.ForEach(Streams, stream => stream.Add(cms))))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        // The start that makes the database compares no index, since its migrations are still pending when it checks.
        var healthy = new CapturedLogs();
        using (var untouched = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms =>
        {
            Array.ForEach(Streams, stream => stream.Add(cms));
            cms.Services.AddSingleton<ILoggerProvider>(healthy);
        }))
        {
            await untouched.StartAsync(Token);
            await untouched.StopAsync(Token);
        }

        healthy.Messages.ShouldNotContain(message => message.Contains("but lacks the indexes", StringComparison.Ordinal));

        const string index = "ix_cms_job_status_completed";
        _database.Execute(TestProvider.IsSqlServer ? $"drop index {TestProvider.Quote(index)} on {TestProvider.Quote("cms_job")}" : $"drop index {TestProvider.Quote(index)}");
        var logs = new CapturedLogs();
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, configure: cms => cms.Services.AddSingleton<ILoggerProvider>(logs));

        await host.StartAsync(Token);
        await host.StopAsync(Token);

        logs.Messages.ShouldContain(message => message.StartsWith($"The system stream has applied its baseline {BaselineOf<SystemDbContext>(host.Services)} but lacks the indexes {index}, which a later build", StringComparison.Ordinal));
    }

    // A unique index that is missing would let in rows the model refuses, so it is refused with the columns.
    [Fact]
    public async Task ADatabaseOfAnEarlierBuildOfTheBaselineThatLacksAUniqueIndexIsRefused()
    {
        using (var built = _database.CreateHost(ArticleSchemas.NoTypes, Production))
        {
            await built.StartAsync(Token);
            await built.StopAsync(Token);
        }

        const string index = "uq_cms_schedule_pending";
        _database.Execute(TestProvider.IsSqlServer ? $"drop index {TestProvider.Quote(index)} on {TestProvider.Quote("cms_schedule")}" : $"drop index {TestProvider.Quote(index)}");
        var before = DatabaseCatalog.Of(_database, historyRows: true);
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production);
        var refusal = $"FCMS1308: stream system has applied its baseline {BaselineOf<SystemDbContext>(host.Services)} but lacks index {index}; " +
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

        foreach (var (index, column, _) in IndexesChangedInTheBaseline)
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

        foreach (var (index, _, expected) in IndexesChangedInTheBaseline)
        {
            Normalized(catalog.Single(line => line.StartsWith(index, StringComparison.Ordinal))).ShouldBe(index + expected, index);
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

    // Runs what another process does once, after the first read of the catalog.
    private sealed class CatalogReader(IDatabaseSchemaReader inner, Func<Task> afterFirstRead) : IDatabaseSchemaReader
    {
        private bool _read;

        public async Task<DatabaseSchema> ReadAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken = default)
        {
            var schema = await inner.ReadAsync(connection, cancellationToken);
            if (!_read)
            {
                _read = true;
                await afterFirstRead();
            }

            return schema;
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline)]
    private static partial Regex GoSeparator();

    // The default of a column line of DatabaseCatalog, up to the next field.
    [GeneratedRegex("(?<=:default=)[^:]*")]
    private static partial Regex DefaultValue();
}
