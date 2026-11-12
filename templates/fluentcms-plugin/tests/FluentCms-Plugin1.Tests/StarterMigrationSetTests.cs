using FluentCMS.Core.Plugins;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluentCms_Plugin1.Tests;

// A host takes the migration set of its provider, so each set must match the model on its provider. Never opens a database.
public sealed class StarterMigrationSetTests
{
    public static TheoryData<string> Sets => [PluginMigrationSets.Sqlite, PluginMigrationSets.PostgreSql, PluginMigrationSets.SqlServer];

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSetMatchesTheModelOnItsProvider(string set)
    {
        using var context = new StarterDbContextFactory().CreateDbContext([set]);

        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges(), $"The {set} set is behind the model; add a migration to it.");
    }
}
