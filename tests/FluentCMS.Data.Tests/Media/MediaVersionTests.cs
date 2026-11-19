using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Media;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Media;

// The version of a media asset on the provider of the run (#3333): it is the row's concurrency token, so an update that
// names a version another write moved on changes nothing, which is what makes If-Match one step with the write.
public sealed class MediaVersionTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-data-media-");
    private readonly IHost _host;

    public MediaVersionTests()
    {
        _host = _database.CreateHost(
            ArticleSchemas.Titled,
            "Production",
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            new Dictionary<string, string?> { ["FluentCms:Media:Local:Root"] = _media.FullName },
            configure: cms => cms.AddPlugin<MediaPlugin>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _database.Dispose();
        _media.Delete(recursive: true);
    }

    [Fact]
    public async Task AnUpdateBasedOnAVersionThatMovedOnChangesNothing()
    {
        await _host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);
        var id = await AddAssetAsync();
        await using var stale = _host.Services.CreateAsyncScope();
        var context = stale.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var read = await context.Set<Asset>().SingleAsync(asset => asset.Id == id, Token);
        read.Version.ShouldBe(1);

        var theirs = await AsSystemAsync(media => media.UpdateAsync(id, new MediaAssetChanges { Title = "Theirs" }, 1, Token));
        theirs.Version.ShouldBe(2);

        (read.Title, read.Version) = ("Mine", 2);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync(Token));

        var refused = await Should.ThrowAsync<VersionConflictException>(() => AsSystemAsync(media => media.UpdateAsync(id, new MediaAssetChanges { Title = "Mine" }, 1, Token)));
        refused.CurrentVersion.ShouldBe(2);
        var applied = await AsSystemAsync(media => media.UpdateAsync(id, new MediaAssetChanges { Title = "Mine" }, null, Token));
        (applied.Version, applied.Asset.Title).ShouldBe((3, "Mine"));
    }

    private async Task<Guid> AddAssetAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var asset = new Asset { Id = Guid.CreateVersion7(), SiteId = DefaultSite.Id, FileName = "photo.png", StorageProvider = LocalFileStorage.ProviderName, StorageKey = $"{DefaultSite.Id:N}/photo.png", MimeType = "image/png", Size = 3, Checksum = new string('a', 64), CreatedAt = now, UpdatedAt = now };
        media.Add(asset);
        await media.SaveChangesAsync(Token);
        return asset.Id;
    }

    private async Task<VersionedAsset> AsSystemAsync(Func<MediaService, Task<VersionedAsset>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Job, new SiteId(DefaultSite.Id));
        return await work(scope.ServiceProvider.GetRequiredService<MediaService>());
    }
}
