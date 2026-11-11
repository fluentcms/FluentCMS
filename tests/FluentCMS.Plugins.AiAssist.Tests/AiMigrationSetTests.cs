using FluentCMS.Core.Plugins;
using FluentCMS.Plugins.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The AI assist stream holds a migration set per provider (#2711). The sets are checked without a database, and the
// SQLite set is applied by a host, as every host of these tests applies it.
public sealed class AiMigrationSetTests
{
    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSetMatchesTheModelOnItsProviderAndRepeatsTheSqliteSet(string set)
    {
        using var context = new AiAssistDbContextFactory().CreateDbContext([set]);
        using var sqlite = new AiAssistDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);

        context.Database.GetMigrations().Select(id => id[15..]).ShouldBe(["InitialAi"]);
        PluginMigrationSetChecks.IsTheSetOf(context, set);
        PluginMigrationSetChecks.MatchesTheSqliteSet(context, sqlite);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void AHostTakesTheSetOfItsProvider(string set)
    {
        using var services = PluginMigrationSetChecks.Host(set, cms => cms.AddPlugin<AiAssistPlugin>());
        using var scope = services.CreateScope();

        PluginMigrationSetChecks.IsTheSetOf(scope.ServiceProvider.GetRequiredService<PluginDbContext<AiAssistPlugin>>(), set);
    }

    [Fact]
    public async Task TheSqliteSetAppliesAndItsTableIsTheUsageLog()
    {
        await using var ai = new AiHost();
        await using var scope = ai.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<AiAssistPlugin>>();

        (await database.Database.GetAppliedMigrationsAsync(Token)).ShouldHaveSingleItem().ShouldEndWith("_InitialAi");
        database.Model.GetEntityTypes().Select(entity => entity.GetTableName()).ShouldBe([AiUsageModel.UsageTable]);
        (await database.Set<AiUsage>().CountAsync(Token)).ShouldBe(0);
    }
}
