using FluentCMS.Core.Plugins;
using FluentCMS.Plugins.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Search.Tests;

// The search stream holds a migration set per provider (#2711), each with the full-text objects of its provider written by
// hand. The sets are checked without a database, and the SQLite set is applied by a host, as every host of these tests
// applies it. FluentCMS.Data.Tests applies the set of the provider of its run.
public sealed class SearchMigrationSetTests
{
    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSetMatchesTheModelOnItsProviderAndRepeatsTheSqliteSet(string set)
    {
        using var context = new SearchDbContextFactory().CreateDbContext([set]);
        using var sqlite = new SearchDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);

        context.Database.GetMigrations().Select(id => id[15..]).ShouldBe(["InitialSearch"]);
        PluginMigrationSetChecks.IsTheSetOf(context, set);
        PluginMigrationSetChecks.MatchesTheSqliteSet(context, sqlite, exceptSql: true);
    }

    [Fact]
    public void EachSetAddsTheFullTextObjectsOfItsProvider()
    {
        using var sqlite = new SearchDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);
        using var postgreSql = new SearchDbContextFactory().CreateDbContext([PluginMigrationSets.PostgreSql]);
        using var sqlServer = new SearchDbContextFactory().CreateDbContext([PluginMigrationSets.SqlServer]);

        var fts = PluginMigrationSetChecks.Sql(sqlite);
        fts.Count.ShouldBe(4);
        fts[0].ShouldContain($"CREATE VIRTUAL TABLE {SearchModel.FtsTable} USING fts5(\"text\", content='{SearchModel.EntryTable}', content_rowid='id')");
        fts.Skip(1).ShouldAllBe(sql => sql.Contains("CREATE TRIGGER") && sql.Contains(SearchModel.FtsTable));

        PluginMigrationSetChecks.Sql(postgreSql).ShouldHaveSingleItem()
            .ShouldContain($"CREATE INDEX {SearchModel.TextIndex} ON {SearchModel.EntryTable} USING gin (to_tsvector('simple'::regconfig, \"text\"))");

        PluginMigrationSetChecks.Sql(sqlServer).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void AHostTakesTheSetOfItsProvider(string set)
    {
        using var services = PluginMigrationSetChecks.Host(set, cms => cms.AddPlugin<SearchPlugin>());
        using var scope = services.CreateScope();

        PluginMigrationSetChecks.IsTheSetOf(scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>(), set);
    }

    [Fact]
    public async Task TheSqliteSetAppliesWithItsFullTextTable()
    {
        await using var search = new SearchHost();
        await using var scope = search.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SearchPlugin>>();

        (await database.Database.GetAppliedMigrationsAsync(Token)).ShouldHaveSingleItem().ShouldEndWith("_InitialSearch");
        database.Model.GetEntityTypes().Select(entity => entity.GetTableName()).ShouldBe([SearchModel.EntryTable]);
        (await database.Set<SearchEntry>().CountAsync(Token)).ShouldBe(0);
        (await database.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM cms_search_entry_fts").SingleAsync(Token)).ShouldBe(0);
    }
}
