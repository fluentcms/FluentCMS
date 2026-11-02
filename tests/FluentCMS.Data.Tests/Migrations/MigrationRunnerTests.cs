using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

public sealed class MigrationRunnerTests : IDisposable
{
    private const string Production = "Production";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["Database:MigrateOnStartup"] = "false" };

    private readonly SqliteFileDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AFreshDatabaseIsFullyMigratedAtStartup()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

        await host.StartAsync(Token);

        _database.Tables().ShouldBe(
            ["__EFMigrationsLock", "cms__migrations", "cms_article", "cms_content__migrations", "cms_document", "cms_reference", "cms_version"],
            ignoreOrder: true);
        _database.ReadStrings("select MigrationId from cms__migrations").ShouldHaveSingleItem().ShouldEndWith("_InitialSystem");
        _database.ReadStrings("select MigrationId from cms_content__migrations").ShouldHaveSingleItem().ShouldEndWith("_Initial");
        (await host.Services.GetRequiredService<MigrationRunner>().GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheStreamsApplyInOrderEachWithItsOwnHistoryTable()
    {
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            Production,
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            services: AddDemoPlugin);

        var runner = host.Services.GetRequiredService<MigrationRunner>();

        var pending = await runner.GetPendingMigrationsAsync(Token);
        pending.Select(migration => migration.Stream).ShouldBe(["system", DemoPlugin.PluginName, "content"]);

        var applied = await runner.MigrateAsync(Token);

        applied.ShouldBe(pending);
        _database.Tables().ShouldContain(DemoPlugin.Table);
        _database.ReadStrings($"select MigrationId from {DemoPlugin.HistoryTable}").ShouldBe([DemoPlugin.MigrationId]);
        (await runner.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARestartAppliesOnlyWhatIsNew()
    {
        var first = GeneratedMigrations.Scaffold(ArticleSchemas.Titled, "Initial");
        var firstAssembly = GeneratedMigrations.Compile(first.MigrationCode, first.MetadataCode, first.SnapshotCode);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, firstAssembly));

        var second = GeneratedMigrations.Scaffold(ArticleSchemas.WithSubtitle, "AddSubtitle", firstAssembly);
        var secondAssembly = GeneratedMigrations.Compile(first.MigrationCode, first.MetadataCode, second.MigrationCode, second.MetadataCode, second.SnapshotCode);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.WithSubtitle, Production, secondAssembly));

        _database.Columns("cms_article").ShouldContain("subtitle");
        _database.ReadStrings("select MigrationId from cms_content__migrations").Count.ShouldBe(2);
    }

    [Fact]
    public async Task AMissingColumnInProductionFailsWithTheColumnAndTheCommandToRun()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));

        using var changed = _database.CreateHost(ArticleSchemas.WithSubtitle, Production, migrations);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => changed.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1301");
        exception.Message.ShouldBe("FCMS1301: table cms_article is missing column subtitle; run 'fluentcms migrations add AddSubtitle'");
        _database.Columns("cms_article").ShouldNotContain("subtitle");
    }

    [Fact]
    public async Task SeveralMissingColumnsAreAllNamed()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));

        using var changed = _database.CreateHost(ArticleSchemas.Article("title", "subtitle", "summary"), Production, migrations);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => changed.StartAsync(Token));

        exception.Message.ShouldBe("FCMS1301: table cms_article is missing column subtitle; table cms_article is missing column summary; run 'fluentcms migrations add AddSubtitle'");
    }

    [Fact]
    public async Task AMissingTableWithNoMigrationAsksForOne()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Message.ShouldBe("FCMS1301: table cms_article is missing; run 'fluentcms migrations add AddArticle'");
    }

    [Fact]
    public async Task AMigrationThatIsOnlyPendingAsksToMigrate()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), NoMigrationOnStartup);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Message.ShouldBe("FCMS1301: table cms_article is missing; run 'fluentcms migrate'");
    }

    [Fact]
    public async Task MigrateOnStartupFalseLeavesTheDatabaseAlone()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), NoMigrationOnStartup);

        await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        _database.Tables().ShouldBeEmpty();
    }

    [Fact]
    public async Task MigrateOnStartupFalseWithNoContentTypesStartsAndCreatesNothing()
    {
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, settings: NoMigrationOnStartup);

        await host.StartAsync(Token);

        _database.Tables().ShouldBeEmpty();
    }

    [Fact]
    public async Task MigrateOnStartupFalseAlsoStopsSchemaSyncInDevelopment()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Environments.Development, settings: NoMigrationOnStartup);

        await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        _database.Tables().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheRunnerApiMigratesWhenStartupDoesNot()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), NoMigrationOnStartup);

        await host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);
        await host.StartAsync(Token);

        _database.Tables().ShouldContain("cms_article");
    }

    [Theory]
    [InlineData("Database:MigrateOnStartup", "maybe")]
    [InlineData("Database:SchemaSync", "Sometimes")]
    public async Task AnInvalidSettingFailsStartup(string key, string value)
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, settings: new Dictionary<string, string?> { [key] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1304");
        exception.Message.ShouldContain(key);
    }

    [Fact]
    public void TheContentContextHasItsHistoryTableAndExcludesEverythingElse()
    {
        using var services = _database.CreateServices(ArticleSchemas.Titled);
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        context.GetService<IHistoryRepository>().GetCreateScript().ShouldContain("cms_content__migrations");

        var tables = context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables;
        tables.Where(table => !table.IsExcludedFromMigrations).Select(table => table.Name).ShouldBe(["cms_article"]);
        tables.Where(table => table.IsExcludedFromMigrations).Select(table => table.Name).ShouldBe(["cms_document", "cms_reference", "cms_version"], ignoreOrder: true);
    }

    [Fact]
    public void TheUnionModelHasNoPendingChangesOnceItsMigrationExists()
    {
        using var services = _database.CreateServices(ArticleSchemas.Titled);
        using var scope = services.CreateScope();
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);

        using var context = ContentMigrations.CreateContext(services.GetRequiredService<IDataProvider>(), ArticleSchemas.Titled, migrations);

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    private static async Task StartAndStopAsync(IHost host)
    {
        using (host)
        {
            await host.StartAsync(Token);
            await host.StopAsync(Token);
        }
    }

    private void AddDemoPlugin(IServiceCollection services)
    {
        services.AddDbContext<DemoPluginDbContext>(options =>
            options.UseSqlite(_database.ConnectionString, sqlite => sqlite
                .MigrationsAssembly(typeof(DemoPluginDbContext).Assembly)
                .MigrationsHistoryTable(DemoPlugin.HistoryTable)));
        services.AddSingleton<IPluginMigrations, DemoPlugin>();
    }
}
