using FluentCMS.Core.Plugins;
using FluentCMS.Plugins.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Tests;

// The media stream holds a migration set per provider (#2711). Never opens a database.
public sealed class MediaMigrationSetTests
{
    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSetMatchesTheModelOnItsProviderAndRepeatsTheSqliteSet(string set)
    {
        using var context = new MediaDbContextFactory().CreateDbContext([set]);
        using var sqlite = new MediaDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);

        context.Database.GetMigrations().Select(id => id[15..]).ShouldBe(["Initial"]);
        PluginMigrationSetChecks.IsTheSetOf(context, set);
        PluginMigrationSetChecks.MatchesTheSqliteSet(context, sqlite);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void AHostTakesTheSetOfItsProvider(string set)
    {
        using var services = PluginMigrationSetChecks.Host(set, cms => cms.AddPlugin<MediaPlugin>());
        using var scope = services.CreateScope();

        PluginMigrationSetChecks.IsTheSetOf(scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>(), set);
    }

    // The check of a replace for other assets that name the old keys is SQL on every provider.
    [Theory]
    [MemberData(nameof(Sets))]
    public void TheCheckForOtherAssetsThatNameAKeyTranslatesOnEachProvider(string set)
    {
        using var context = new MediaDbContextFactory().CreateDbContext([set]);
        var asset = new Asset { Id = Guid.CreateVersion7(), SiteId = Guid.CreateVersion7(), StorageKey = "a/b/original.png" };

        ReplacedFiles.OthersNaming(context, asset, [asset.StorageKey, "a/b/variant.webp"]).ToQueryString().ShouldContain("storage_key", Case.Insensitive);
    }
}
