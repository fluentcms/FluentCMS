using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.PostgreSql;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.SqlServer;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Schema;
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

    private void InsertArticle(string column, string value) =>
        _database.Execute($"insert into cms_article (id, document_id, site_id, state, {column}) values ('{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', '{Guid.Empty}', 0, '{value}')");

    [Fact]
    public async Task AFreshDatabaseGetsTheContentTablesWithoutTheContentStream()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);

        _database.Tables().ShouldContain("cms_article");
        _database.Tables().ShouldContain("cms__migrations");
        _database.Tables().ShouldNotContain("cms_content__migrations");
        _database.ReadStrings("select name from cms_role").Count.ShouldBe(6);
    }

    [Fact]
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

    [Fact]
    public async Task AddingAFieldAppliesAtTheNextStartupToo()
    {
        await StartAndStopAsync(ArticleSchemas.Titled);
        InsertArticle("title", "Kept");

        await StartAndStopAsync(ArticleSchemas.WithSubtitle);

        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.ReadStrings("select title from cms_article").ShouldBe(["Kept"]);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task ADestructiveChangeAtStartupIsLogged()
    {
        await StartAndStopAsync(ArticleSchemas.Article("title", "legacy"));

        var logs = new ListLoggerProvider();
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, services: services => services.AddSingleton<ILoggerProvider>(logs));

        await host.StartAsync(Token);

        logs.Messages.ShouldContain(message => message.Contains("drop column legacy from table cms_article", StringComparison.Ordinal));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task SyncingTwiceAppliesNothingTheSecondTime()
    {
        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        (await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token)).Applied.ShouldNotBeEmpty();
        (await Runner(host).SyncContentSchemaAsync(ArticleSchemas.WithSubtitle, Token)).Applied.ShouldBeEmpty();
    }

    [Fact]
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

    [Fact]
    public async Task SchemaSyncAutoOutsideDevelopmentIsRefused()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, "Production", settings: new Dictionary<string, string?> { ["FluentCms:Database:SchemaSync"] = "Auto" });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1301");
        _database.Tables().ShouldNotContain("cms_article");
        await Should.ThrowAsync<InvalidOperationException>(() => Runner(host).SyncContentSchemaAsync(cancellationToken: Token));
    }

    [Fact]
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

    [Fact]
    public async Task OutsideDevelopmentADestructiveChangeIsStillOnlyReported()
    {
        await StartAndStopAsync(ArticleSchemas.WithSubtitle);

        using var host = _database.CreateHost(ArticleSchemas.Titled, "Production", configure: cms => cms.AllowSchemaSyncOutsideDevelopment());
        await host.StartAsync(Token);

        _database.Columns("cms_article").ShouldContain("subtitle");
        await host.StopAsync(Token);
    }

    [Fact]
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

    [Fact]
    public async Task InDevelopmentTheReportDoesNotListTheSync()
    {
        using var host = DevelopmentHost(ArticleSchemas.Titled);
        await host.StartAsync(Token);

        host.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("schema-sync-outside-development");
        await host.StopAsync(Token);
    }

    [Fact]
    public void AProviderWithoutSchemaSyncRefusesTheOptIn()
    {
        var postgres = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms => cms.UsePostgreSql("Host=localhost").AllowSchemaSyncOutsideDevelopment()));
        var sqlServer = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms => cms.UseSqlServer("Server=localhost").AllowSchemaSyncOutsideDevelopment()));

        postgres.Code.ShouldBe("FCMS3154");
        postgres.Message.ShouldContain("PostgreSQL");
        sqlServer.Code.ShouldBe("FCMS3154");
    }

    [Fact]
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
}
