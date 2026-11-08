using FluentCMS.Core.Plugins;
using FluentCMS.Plugins.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Tests;

// The webhooks stream holds a migration set per provider (#2711). Never opens a database.
public sealed class WebhookMigrationSetTests
{
    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSetMatchesTheModelOnItsProviderAndRepeatsTheSqliteSet(string set)
    {
        using var context = new WebhooksDbContextFactory().CreateDbContext([set]);
        using var sqlite = new WebhooksDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);

        context.Database.GetMigrations().Select(id => id[15..]).ShouldBe(["InitialWebhooks", "AddDeliveryClaim", "AddPreviousSecret"]);
        PluginMigrationSetChecks.IsTheSetOf(context, set);
        PluginMigrationSetChecks.MatchesTheSqliteSet(context, sqlite);
    }

    // The plugin maps no JSON column of its own, so the native json type of SQL Server 2025 leaves its model, and its set, alone.
    [Theory]
    [InlineData(PluginMigrationSets.Sqlite, false)]
    [InlineData(PluginMigrationSets.PostgreSql, false)]
    [InlineData(PluginMigrationSets.SqlServer, false)]
    [InlineData(PluginMigrationSets.SqlServer, true)]
    public void AHostTakesTheSetOfItsProvider(string set, bool nativeJson)
    {
        using var services = PluginMigrationSetChecks.Host(set, nativeJson, cms => cms.AddPlugin<WebhooksPlugin>());
        using var scope = services.CreateScope();

        PluginMigrationSetChecks.IsTheSetOf(scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>(), set);
    }
}
