using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
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

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new() { ["FluentCms:Database:MigrateOnStartup"] = "false" };

    private readonly SqliteFileDatabase _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AFreshDatabaseIsFullyMigratedAtStartup()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

        await host.StartAsync(Token);

        _database.Tables().ShouldBe(
            ["__EFMigrationsLock", "cms__migrations", "cms_activity", "cms_api_key", "cms_data_protection_key", "cms_oidc_application", "cms_oidc_authorization", "cms_oidc_scope", "cms_oidc_token", "cms_signing_key", "cms_article", "cms_content__migrations", "cms_document", "cms_invitation", "cms_job", "cms_job_attempt", "cms_outbox", "cms_recurring_job", "cms_reference", "cms_role", "cms_role_permission", "cms_schedule", "cms_schema_history", "cms_setting", "cms_site", "cms_site_locale", "cms_user", "cms_user_claim", "cms_user_login", "cms_user_role", "cms_user_token", "cms_version"],
            ignoreOrder: true);
        _database.ReadStrings("select MigrationId from cms__migrations").ShouldContain(id => id.EndsWith("_InitialSystem", StringComparison.Ordinal));
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
        pending.Select(migration => migration.Stream).Distinct().ShouldBe(["system", DemoPlugin.PluginName, "content"]);

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
    public async Task AContentMigrationThatIsOnlyPendingAsksToMigrate()
    {
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.NoTypes, Production));

        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), NoMigrationOnStartup);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Message.ShouldBe("FCMS1301: table cms_article is missing; run 'fluentcms migrate'");
    }

    [Fact]
    public async Task APendingSystemMigrationFailsStartupWhenStartupDoesNotMigrate()
    {
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, settings: NoMigrationOnStartup);
        var pending = await host.Services.GetRequiredService<MigrationRunner>().GetPendingMigrationsAsync(Token);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1305");
        exception.Message.ShouldBe($"FCMS1305: stream system has not applied {string.Join(", ", pending.Select(migration => migration.Id))}; run 'fluentcms migrate'");
    }

    [Fact]
    public async Task APendingPluginMigrationFailsStartupWhenStartupDoesNotMigrate()
    {
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.NoTypes, Production));

        using var host = _database.CreateHost(ArticleSchemas.NoTypes, Production, settings: NoMigrationOnStartup, services: AddDemoPlugin);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        exception.Message.ShouldBe($"FCMS1305: stream {DemoPlugin.PluginName} has not applied {DemoPlugin.MigrationId}; run 'fluentcms migrate'");
    }

    [Fact]
    public async Task MigrateOnStartupFalseStartsWhenTheDatabaseIsUpToDate()
    {
        var migrations = GeneratedMigrations.CompileFirst(ArticleSchemas.Titled);
        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations));

        await StartAndStopAsync(_database.CreateHost(ArticleSchemas.Titled, Production, migrations, NoMigrationOnStartup));

        _database.ReadStrings("select MigrationId from cms_content__migrations").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MigrateOnStartupFalseLeavesTheDatabaseAlone()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled), NoMigrationOnStartup);

        await Should.ThrowAsync<FluentCmsConfigurationException>(() => host.StartAsync(Token));

        _database.Tables().ShouldBeEmpty();
    }

    [Fact]
    public async Task SystemThenContentOnAFreshDatabaseLeavesExactlyTheSeededRoles()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));

        await host.StartAsync(Token);

        _database.ReadStrings("select name from cms_role").ShouldBe(["Super Admin", "Editor", "Author", "Public", "Member", "Public"], ignoreOrder: true);
    }

    [Fact]
    public async Task AHostContextRaisesTheRoleStamp()
    {
        using var host = _database.CreateHost(ArticleSchemas.Titled, Production, GeneratedMigrations.CompileFirst(ArticleSchemas.Titled));
        await host.StartAsync(Token);

        using (var scope = host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var role = context.Set<Role>().Single(role => role.Id == BuiltInRoles.Editor);
            role.PermissionsVersion.ShouldBe(1);

            role.Description = "Changed";
            await context.SaveChangesAsync(Token);
        }

        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<Role>().AsNoTracking().Single(role => role.Id == BuiltInRoles.Editor).PermissionsVersion.ShouldBe(2);
        }
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
    [InlineData("FluentCms:Database:MigrateOnStartup", "maybe")]
    [InlineData("FluentCms:Database:SchemaSync", "Sometimes")]
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
        tables.Where(table => table.IsExcludedFromMigrations).Select(table => table.Name).ShouldBe(["cms_activity", "cms_api_key", "cms_data_protection_key", "cms_oidc_application", "cms_oidc_authorization", "cms_oidc_scope", "cms_oidc_token", "cms_signing_key", "cms_document", "cms_invitation", "cms_job", "cms_job_attempt", "cms_outbox", "cms_recurring_job", "cms_reference", "cms_role", "cms_role_permission", "cms_schedule", "cms_schema_history", "cms_setting", "cms_site", "cms_site_locale", "cms_user", "cms_user_claim", "cms_user_login", "cms_user_role", "cms_user_token", "cms_version"], ignoreOrder: true);
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
