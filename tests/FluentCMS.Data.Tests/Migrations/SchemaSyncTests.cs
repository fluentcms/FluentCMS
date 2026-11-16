using System.Data.Common;
using System.Threading.Channels;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Providers;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

public sealed class SchemaSyncTests : IDisposable
{
    private static readonly string[] HeroBlock = ["hero"];

    private readonly SqliteFileDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    private static MigrationRunner Runner(IHost host) => host.Services.GetRequiredService<MigrationRunner>();

    private IHost DevelopmentHost(SchemaModel schema) => _database.CreateHost(schema, Environments.Development);

    private async Task StartAndStopAsync(SchemaModel schema)
    {
        using var host = DevelopmentHost(schema);
        await host.StartAsync(Token);
        await host.StopAsync(Token);
    }

    private static SchemaModel WithHero(string field, string? former = null) =>
        SampleSchema.NewBuilder()
            .AddBlock("hero", block => block.Field(field, "text", heading => heading.FormerNames(former is null ? [] : [former])))
            .AddCollection("article", type => type.Field("title", "text").Field("sections", "blocks", sections => sections.Setting(FieldSettings.Blocks, HeroBlock)))
            .Build();

    private static SchemaModel Hero(params string[] fields) =>
        SampleSchema.NewBuilder()
            .AddBlock("hero", block =>
            {
                foreach (var field in fields)
                {
                    block.Field(field, "text");
                }
            })
            .AddCollection("article", type => type.Field("title", "text").Field("sections", "blocks", sections => sections.Setting(FieldSettings.Blocks, HeroBlock)))
            .Build();

    private void InsertArticle(string column, string value) =>
        _database.Execute($"insert into cms_article (id, document_id, site_id, state, {column}) values ('{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', '{Guid.Empty}', 0, '{value}')");

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AFreshDatabaseGetsTheContentTablesWithoutTheContentStream()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);

        _database.Tables().ShouldContain("cms_article");
        _database.Tables().ShouldContain("cms__migrations");
        _database.Tables().ShouldNotContain("cms_content__migrations");
        _database.ReadStrings("select name from cms_role").Count.ShouldBe(6);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AddingAFieldAppliesWithoutARestartInDevelopment()
    {
        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);
        InsertArticle("title", "Kept");

        var report = await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token);

        report.Applied.ShouldBe(["add column subtitle to table cms_article"]);
        report.NotApplied.ShouldBeEmpty();
        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);

        using var context = ContentMigrations.CreateContext(new SqliteDataProvider(_database.ConnectionString), ArticleSchemas.WithSubtitle, typeof(CmsDbContext).Assembly);
        context.Rows("cms_article").Single()["subtitle"].ShouldBeNull();
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AddingAFieldAppliesAtTheNextStartupToo()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);
        InsertArticle("title", "Kept");

        await StartAndStopAsync(ArticleSchemas.WithSubtitle);

        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ANewTypeAndANewIndexApply()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);

        var changed = SampleSchema.NewBuilder()
            .AddCollection("article", type => type.Field("title", "text", field => field.Indexed()))
            .AddCollection("tag", type => type.Field("name", "text", field => field.Unique()))
            .Build();

        using var host = DevelopmentHost(changed);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(cancellationToken: Token);
        report.Applied.ShouldBeEmpty();
        report.NotApplied.ShouldBeEmpty();

        _database.Tables().ShouldContain("cms_tag");
        _database.ReadStrings("select name from sqlite_master where type = 'index' and name = 'ix_cms_article_title'").ShouldHaveSingleItem();
        _database.ReadStrings("select sql from sqlite_master where name = 'ux_cms_tag_name'").ShouldHaveSingleItem().ShouldEndWith("WHERE state = 1 AND \"name\" IS NOT NULL");
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ADestructiveChangeIsReportedAndNotApplied()
    {
        await StartAndStopAsync(ArticleSchemas.Article("title", "legacy"));
        InsertArticle("legacy", "Kept");

        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(cancellationToken: Token);

        report.Applied.ShouldBeEmpty();
        report.NotApplied.ShouldBe(["drop column legacy from table cms_article"]);
        _database.ReadStrings("select legacy from cms_article").ShouldBe(["Kept"]);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ADestructiveChangeAtStartupIsLogged()
    {
        await StartAndStopAsync(ArticleSchemas.Article("title", "legacy"));

        var logs = new ListLoggerProvider();
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: services => services.AddSingleton<ILoggerProvider>(logs));

        await host.StartAsync(Token);

        logs.Messages.ShouldContain(message => message.Contains("drop column legacy from table cms_article", StringComparison.Ordinal));
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ARemovedTypeATypeChangeAndARemovedIndexAreReported()
    {
        var before = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("count", "text")
                .Field("title", "text", field => field.Indexed()))
            .AddCollection("old", type => type.Field("name", "text"))
            .Build();
        await StartAndStopAsync(before);

        var after = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("count", "integer")
                .Field("title", "text"))
            .Build();

        using var host = DevelopmentHost(after);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(cancellationToken: Token);

        report.Applied.ShouldBeEmpty();
        report.NotApplied.ShouldBe(
            [
                "change column count on table cms_article from TEXT NULL to INTEGER NULL",
                "drop index ix_cms_article_title from table cms_article",
                "drop table cms_old",
            ],
            ignoreOrder: true);
        _database.Tables().ShouldContain("cms_old");
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AKeyMadeBeforeItHeldTheSiteIsReportedAsChanged()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);
        _database.Execute("drop index uq_cms_article_document_state; create unique index uq_cms_article_document_state on cms_article (document_id, state)");

        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(cancellationToken: Token);

        report.Applied.ShouldBeEmpty();
        report.NotApplied.ShouldBe(["change index uq_cms_article_document_state on table cms_article"]);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ARequiredColumnWithNoDefaultIsReportedAndNotApplied()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);

        var changed = SampleSchema.NewBuilder()
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field("code", "text", field => field.Required()))
            .Build();

        using var host = DevelopmentHost(changed);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(cancellationToken: Token);

        report.NotApplied.ShouldBe(["add required column code with no default to table cms_article"]);
        _database.Columns("cms_article").ShouldNotContain("code");
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AListColumnHasADefaultSoItAppliesButItsCheckConstraintIsReported()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);
        InsertArticle("title", "Kept");

        var changed = SampleSchema.NewBuilder()
            .AddBlock("hero", block => block.Field("heading", "text"))
            .AddCollection("article", type => type
                .Field("title", "text")
                .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, HeroBlock)))
            .Build();

        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(changed, Token);

        report.Applied.ShouldBe(["add column sections to table cms_article"]);
        report.NotApplied.ShouldBe(["add check constraint ck_cms_article_sections_json to table cms_article"]);
        _database.ReadStrings("select sections from cms_article").ShouldBe(["[]"]);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task APluginsTableIsNotAStaleContentTableAndTheSyncIsRecorded()
    {
        var logs = new ListLoggerProvider();
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: services =>
        {
            DemoPlugin.Add(services, _database);
            services.AddSingleton<ILoggerProvider>(logs);
        });

        await host.StartAsync(Token);

        _database.Tables().ShouldContain(DemoPlugin.Table);
        _database.ReadStrings($"select MigrationId from {DemoPlugin.HistoryTable}").ShouldBe([DemoPlugin.MigrationId]);
        _database.ReadStrings("select hash from cms_schema_history").ShouldHaveSingleItem();
        logs.Messages.ShouldNotContain(message => message.Contains($"drop table {DemoPlugin.Table}", StringComparison.Ordinal));

        var report = await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token);

        report.Applied.ShouldBe(["add column subtitle to table cms_article"]);
        report.NotApplied.ShouldBeEmpty();
        _database.ReadStrings("select hash from cms_schema_history").Count.ShouldBe(2);
        await host.StopAsync(Token);
    }

    // Sync moves no value, and the history keeps telling schema apply that the values under heading are lost.
    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ABlockFieldRenamedIsNotRecordedSinceItsValuesStayUnderTheFormerName()
    {
        await StartAndStopAsync(WithHero("heading"));
        var logs = new ListLoggerProvider();

        using var host = _database.CreateHost(WithHero("title", former: "heading"), Environments.Development, services: services => services.AddSingleton<ILoggerProvider>(logs));
        await host.StartAsync(Token);

        _database.ReadStrings("select hash from cms_schema_history").ShouldHaveSingleItem();
        logs.Messages.ShouldContain(message => message.Contains("field 'block hero.heading' renamed to 'title'", StringComparison.Ordinal) && message.Contains("Run 'fluentcms schema apply' to apply and record it", StringComparison.Ordinal));
        await host.StopAsync(Token);
    }

    // Nothing but sync would record past the loss on a host with content migrations, which schema apply refuses.
    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AHostWithContentMigrationsRecordsASyncThatRemovesABlockField()
    {
        var migrations = GeneratedMigrations.CompileFirst(Hero("heading", "caption"));
        using (var first = _database.CreateHost(Hero("heading", "caption"), Environments.Development, migrations))
        {
            await first.StartAsync(Token);
            await first.StopAsync(Token);
        }

        using var host = _database.CreateHost(Hero("heading"), Environments.Development, migrations);
        await host.StartAsync(Token);

        _database.ReadStrings("select hash from cms_schema_history").Count.ShouldBe(2);
        await host.StopAsync(Token);
    }

    // A history that cannot be read is logged, as the other records of the runner log it, and the sync goes on.
    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ASyncWhoseHistoryCannotBeReadIsNotRecordedAndDoesNotFail()
    {
        var logs = new ListLoggerProvider();
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: services => services.AddSingleton<ILoggerProvider>(logs));
        await host.StartAsync(Token);
        _database.Execute("drop table cms_schema_history");

        var report = await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token);

        report.Applied.ShouldBe(["add column subtitle to table cms_article"]);
        logs.Messages.ShouldContain(message => message.StartsWith("The schema was not recorded in the schema history (", StringComparison.Ordinal));
        await host.StopAsync(Token);
    }

    // The database has the plugin's history table, which tells that the table is the plugin's.
    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ATableOfAPluginTheHostNoLongerRegistersIsNotAStaleContentTable()
    {
        using (var withPlugin = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: services => DemoPlugin.Add(services, _database)))
        {
            await withPlugin.StartAsync(Token);
            await withPlugin.StopAsync(Token);
        }

        using var host = DevelopmentHost(ArticleSchemas.WithSubtitle);
        await host.StartAsync(Token);

        _database.ReadStrings("select hash from cms_schema_history").Count.ShouldBe(2);
        var report = await Runner(host).SyncContentSchemaAsync(cancellationToken: Token);

        report.NotApplied.ShouldBeEmpty();
        _database.Tables().ShouldContain(DemoPlugin.Table);
        await host.StopAsync(Token);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task SyncingTwiceAppliesNothingTheSecondTime()
    {
        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        (await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token)).Applied.ShouldNotBeEmpty();
        (await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token)).Applied.ShouldBeEmpty();
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ASyncWaitsForAnotherNodeAndAppliesOnlyWhatIsStillMissing()
    {
        var reads = new LockReads();
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            Environments.Development,
            configure: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(reads)));
        await host.StartAsync(Token);

        Task<SchemaSyncReport> sync;
        await using (var other = await host.Services.GetRequiredService<IDistributedLock>().TryAcquireAsync(MigrationRunner.SchemaSyncLock, TimeSpan.Zero, Token))
        {
            other.ShouldNotBeNull();
            reads.Watching = true;
            sync = Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token);

            // Two reads of the lock's row: the sync found the lock held, waited, and looked again.
            await reads.NextAsync(2);
            sync.IsCompleted.ShouldBeFalse();

            // The node that holds the lock adds the column, which the waiting one would otherwise add a second time.
            _database.Execute("alter table cms_article add column subtitle TEXT NULL");
        }

        var report = await sync;

        report.Applied.ShouldBeEmpty();
        report.NotApplied.ShouldBeEmpty();
        _database.Columns("cms_article").ShouldContain("subtitle");
    }

    // A lock that is never released, as when its release failed and a wall clock that stands still never ends its lease. The
    // lock's monotonic clock runs a thousand times fast, so the wait reaches its timeout within a second or two.
    [SqliteTheory("schema sync, which only SQLite supports")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASyncGivesUpOnALockThatIsNeverReleased(bool frozenWallClock)
    {
        var clock = new FastMonotonicClock(frozenWallClock ? DateTimeOffset.UtcNow : null);
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: services => FastMonotonicClock.Lock(services, clock));
        await host.StartAsync(Token);

        await using var other = await host.Services.GetRequiredService<IDistributedLock>().TryAcquireAsync(MigrationRunner.SchemaSyncLock, TimeSpan.Zero, Token);
        other.ShouldNotBeNull();

        var exception = await Should.ThrowAsync<TimeoutException>(Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token).WaitAsync(TimeSpan.FromSeconds(30), Token));

        exception.Message.ShouldStartWith($"The lock {MigrationRunner.SchemaSyncLock} was not free within 15 minutes, so the content tables were not changed; check the owner and lease_until of its row in cms_lock");
        exception.Message.ShouldContain("when the log has event 9166 for the lock, it can also mean that the database was busy");
        _database.Columns("cms_article").ShouldNotContain("subtitle");
    }

    // SQLite has one writer, so the lock cannot renew its lease while the transaction of the sync lasts.
    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ASyncWhoseTransactionOutlastsTheLeaseOfItsLockFinishes()
    {
        var work = new LongTransaction();
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: LongTransaction.Lock, configure: work.Configure);
        await host.StartAsync(Token);

        var report = await Runner(host).SyncContentSchemaAsync(ArticleSchemas.Article("title", "subtitle", "summary"), Token);

        report.Applied.Count.ShouldBe(2);
        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.Columns("cms_article").ShouldContain("summary");
        await host.StopAsync(Token);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task SchemaSyncOffInDevelopmentUsesTheContentStream()
    {
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            Environments.Development,
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            new Dictionary<string, string?> { ["FluentCms:Database:SchemaSync"] = "Off" });

        await host.StartAsync(Token);

        _database.Tables().ShouldContain("cms_content__migrations");
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task SchemaSyncAutoOutsideDevelopmentIsRefused()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, "Production", settings: new Dictionary<string, string?> { ["FluentCms:Database:SchemaSync"] = "Auto" });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1301");
        _database.Tables().ShouldNotContain("cms_article");
        await Should.ThrowAsync<InvalidOperationException>(() => Runner(host).SyncContentSchemaAsync(cancellationToken: Token));
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task AHostThatAllowsItSyncsOutsideDevelopmentAndTheReportSaysSo()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);
        InsertArticle("title", "Kept");

        using var host = _database.CreateHost(ArticleSchemas.WithSubtitle, "Production", configure: cms => cms.AllowSchemaSyncOutsideDevelopment());
        await host.StartAsync(Token);

        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.Tables().ShouldNotContain("cms_content__migrations");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
        (await Runner(host).SyncContentSchemaAsync(cancellationToken: Token)).Applied.ShouldBeEmpty();
        host.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldContain("schema-sync-outside-development");
        await host.StopAsync(Token);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task OutsideDevelopmentADestructiveChangeIsStillOnlyReported()
    {
        await StartAndStopAsync(ArticleSchemas.WithSubtitle);

        using var host = _database.CreateHost(ArticleSchemas.Titled, "Production", configure: cms => cms.AllowSchemaSyncOutsideDevelopment());
        await host.StartAsync(Token);

        _database.Columns("cms_article").ShouldContain("subtitle");
        await host.StopAsync(Token);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task ConfigurationCanStillTurnTheAllowedSyncOff()
    {
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            "Production",
            settings: new Dictionary<string, string?> { ["FluentCms:Database:SchemaSync"] = "Off" },
            configure: cms => cms.AllowSchemaSyncOutsideDevelopment());

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1301");
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public async Task InDevelopmentTheReportDoesNotListTheSync()
    {
        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        host.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("schema-sync-outside-development");
        await host.StopAsync(Token);
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public void AProviderWithoutSchemaSyncRefusesTheOptIn()
    {
        var postgres = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms => cms.UsePostgreSql("Host=localhost").AllowSchemaSyncOutsideDevelopment()));
        var sqlServer = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms => cms.UseSqlServer("Server=localhost").AllowSchemaSyncOutsideDevelopment()));

        postgres.Code.ShouldBe("FCMS3154");
        postgres.Message.ShouldContain("PostgreSQL");
        sqlServer.Code.ShouldBe("FCMS3154");
    }

    [SqliteFact("schema sync, which only SQLite supports")]
    public void OnlySqliteSupportsSchemaSync()
    {
        new SqliteDataProvider("Data Source=:memory:").SupportsSchemaSync.ShouldBeTrue();
        new PostgreSqlDataProvider("Host=localhost").SupportsSchemaSync.ShouldBeFalse();
        new SqlServerDataProvider("Server=localhost").SupportsSchemaSync.ShouldBeFalse();
    }

    private sealed class ListLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }

    // Signals each read of the row of the schema sync lock, which a node waiting for the lock repeats while another holds it.
    private sealed class LockReads : DbCommandInterceptor
    {
        private readonly Channel<bool> _reads = Channel.CreateUnbounded<bool>();

        public volatile bool Watching;

        public async Task NextAsync(int count)
        {
            for (var read = 0; read < count; read++)
            {
                await _reads.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);
            }
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Watching
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"cms_lock\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, MigrationRunner.SchemaSyncLock)))
            {
                _reads.Writer.TryWrite(true);
            }

            return ValueTask.FromResult(result);
        }
    }
}
