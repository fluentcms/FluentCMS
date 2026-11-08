using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Plugins.Sample;
using FluentCMS.Plugins.Tests.MigrationSets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// A plugin assembly holds a migration set per provider, and a host takes its provider's set (#2711). Only the SQLite cases
// open a database.
public sealed class PluginMigrationSetTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSampleSetMatchesTheModelOnItsProviderAndRepeatsTheSqliteSet(string set)
    {
        using var context = new SampleDbContextFactory().CreateDbContext([set]);
        using var sqlite = new SampleDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);

        context.Database.GetMigrations().Select(id => id[15..]).ShouldBe(["InitialSample"]);
        PluginMigrationSetChecks.IsTheSetOf(context, set);
        PluginMigrationSetChecks.MatchesTheSqliteSet(context, sqlite);
    }

    // The late plugin's hand-written migration is outside every provider's namespace and has no target model to tell.
    [Theory]
    [MemberData(nameof(Sets))]
    public async Task AHostTakesTheSampleSetOfItsProviderAndAHandWrittenSetOnAnyProvider(string set)
    {
        using var services = PluginMigrationSetChecks.Host(set, nativeJson: false, cms => cms.AddPlugin<LatePlugin>());
        using var scope = services.CreateScope();

        PluginMigrationSetChecks.IsTheSetOf(scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>(), set);
        scope.ServiceProvider.GetRequiredService<PluginDbContext<LatePlugin>>().Database.GetMigrations().ShouldBe([LatePlugin.MigrationId]);
        await services.GetRequiredService<MigrationRunner>().RefuseForeignMigrationsAsync(Token);
    }

    [Theory]
    [InlineData(PluginMigrationSets.PostgreSql, "PostgreSQL", "stream legacy was scaffolded for SQLite and has no set in a namespace ending in .PostgreSql; stream partial was scaffolded for SQLite")]
    [InlineData(PluginMigrationSets.SqlServer, "SQL Server", "stream legacy was scaffolded for SQLite and has no set in a namespace ending in .SqlServer; stream partial has no set in a namespace ending in .SqlServer")]
    public async Task AServerHostRefusesAnotherProvidersSetOrNoneBeforeTouchingTheDatabase(string set, string provider, string streams)
    {
        using var services = PluginMigrationSetChecks.Host(set, nativeJson: false, cms => cms.AddPlugin<LegacySetPlugin>().AddPlugin<PartialSetPlugin>());
        var runner = services.GetRequiredService<MigrationRunner>();

        var atStartup = await Should.ThrowAsync<FluentCmsConfigurationException>(() => runner.RunAtStartupAsync(Token));
        var migrate = await Should.ThrowAsync<FluentCmsConfigurationException>(() => runner.MigrateAsync(Token));

        atStartup.Code.ShouldBe("FCMS1306");
        atStartup.Message.ShouldBe($"FCMS1306: {streams}; this host runs {provider}, and a migration set applies only to the provider it was scaffolded for");
        migrate.Message.ShouldBe(atStartup.Message);
    }

    [Fact]
    public async Task ASqliteHostAppliesASetOutsideTheProviderNamespacesAndItsOwnSetBesideOthers()
    {
        var file = Path.Combine(Path.GetTempPath(), $"fluentcms-sets-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddFluentCms(cms => cms.UseSqlite($"Data Source={file};Pooling=False").AddPlugin<LegacySetPlugin>().AddPlugin<PartialSetPlugin>());
            await using var provider = services.BuildServiceProvider();

            var applied = await provider.GetRequiredService<MigrationRunner>().MigrateAsync(Token);

            applied.Where(migration => migration.Stream == "legacy").Select(migration => migration.Id).ShouldBe([LegacySetPlugin.MigrationId]);
            applied.Where(migration => migration.Stream == "partial").Select(migration => migration.Id).ShouldBe([PartialSetPlugin.SqliteMigrationId]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }

    [Fact]
    public void TheDesignTimeFactoryTakesTheNameOfASet()
    {
        using var postgreSql = new SampleDbContextFactory().CreateDbContext(["postgresql"]);

        postgreSql.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
        Should.Throw<ArgumentException>(() => new SampleDbContextFactory().CreateDbContext([]))
            .Message.ShouldStartWith("Name the migration set after --, one of Sqlite, PostgreSql, SqlServer:");
        Should.Throw<ArgumentException>(() => new SampleDbContextFactory().CreateDbContext(["Oracle"]));
    }
}
