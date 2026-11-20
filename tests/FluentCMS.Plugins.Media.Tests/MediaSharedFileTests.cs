using System.Net;
using System.Text.Json;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// Two assets on one key: the owner, which wrote the file, and an asset whose key a line of an import named, which owns
// nothing. Each has a variant at the same key, and the owner one of its own. A purge, a delete and the deletion of a site
// delete a file only for its owner and only when no other asset names it, so the last of the two to go takes the file.
public sealed class MediaSharedFileTests(PerSiteMediaHost media) : IClassFixture<PerSiteMediaHost>
{
    private static readonly TimeSpan PastRetention = new MediaOptions().DeletedRetention + TimeSpan.FromDays(1);

    [Fact]
    public async Task TheOwnerDeletedFirstKeepsTheFilesTheOtherNamesUntilTheOtherGoesToo()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());

        await DeleteAsync(admin, shared.Owner);
        await CleanupAsync();

        Exists(shared.Key, shared.SharedVariant).ShouldBe([true, true]);
        Exists(shared.OwnVariant).ShouldBe([false]);
        (await VariantKeysAsync(shared.Owner)).ShouldBe([shared.SharedVariant]);
        await ServesAsync(admin, null, shared.Named);

        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        Exists(shared.Key, shared.SharedVariant).ShouldBe([true, true]);
        (await RowAsync(shared.Owner)).ShouldNotBeNull().DeletedAt.ShouldNotBeNull();
        await ServesAsync(admin, null, shared.Named);

        await DeleteAsync(admin, shared.Named);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();
        await CleanupAsync();

        (await RowAsync(shared.Owner)).ShouldBeNull();
        (await RowAsync(shared.Named)).ShouldBeNull();
        FilesOf(null, shared.Owner).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheOriginalOfTheOwnerPurgedFirstStaysUntilTheOtherIsPurged()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7(), withVariants: false);

        await DeleteAsync(admin, shared.Owner);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Owner)).ShouldNotBeNull().DeletedAt.ShouldNotBeNull();
        Exists(shared.Key).ShouldBe([true]);

        await DeleteAsync(admin, shared.Named);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Owner)).ShouldBeNull();
        (await RowAsync(shared.Named)).ShouldBeNull();
        Exists(shared.Key).ShouldBe([false]);
    }

    [Fact]
    public async Task TheAssetThatNamesTheKeyDeletedFirstLeavesTheOwnersFiles()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());

        await DeleteAsync(admin, shared.Named);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Named)).ShouldBeNull();
        Exists(shared.Key, shared.SharedVariant, shared.OwnVariant).ShouldBe([true, true, true]);
        (await VariantKeysAsync(shared.Owner)).ShouldBe([shared.SharedVariant, shared.OwnVariant], ignoreOrder: true);
        await ServesAsync(admin, null, shared.Owner);

        await DeleteAsync(admin, shared.Owner);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Owner)).ShouldBeNull();
        FilesOf(null, shared.Owner).ShouldBeEmpty();
    }

    // The ids put the asset that names the key before the owner, or after it; either way it goes first.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletingTheSiteRemovesBothAssetsAndEveryFileOfTheOwner(bool namedFirst)
    {
        var site = await MediaLibraryTesting.CreateSiteAsync(media);
        using var admin = await MediaLibraryTesting.InSiteAsync(media, site);
        var shared = await SharedAsync(admin, site, namedFirst ? Guid.CreateVersion7(DateTimeOffset.UnixEpoch) : Guid.CreateVersion7(DateTimeOffset.MaxValue.AddYears(-1)));

        await using (var scope = media.Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<DeletedSiteMedia>(scope.ServiceProvider).HandleAsync(new SiteDeletedEvent(site, "gone"), new EventDelivery(Guid.CreateVersion7(), 1), Token);
        }

        (await RowAsync(shared.Owner)).ShouldBeNull();
        (await RowAsync(shared.Named)).ShouldBeNull();
        FilesOf(site, shared.Owner).ShouldBeEmpty();
    }

    // The row of a variant whose asset is gone names no owner, so its file goes only when no other asset names it.
    [Fact]
    public async Task AVariantOfAnAssetThatIsGoneKeepsAFileAnotherAssetNames()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());
        var gone = Guid.CreateVersion7();
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            database.Add(new Variant { AssetId = gone, ParamsHash = new string('0', 64), StorageKey = shared.OwnVariant, Size = 1, CreatedAt = media.Host.Time.GetUtcNow() });
            await database.SaveChangesAsync(Token);
        }

        await CleanupAsync();

        (await VariantKeysAsync(gone)).ShouldBeEmpty();
        Exists(shared.OwnVariant).ShouldBe([true]);
        (await VariantKeysAsync(shared.Owner)).ShouldBe([shared.SharedVariant, shared.OwnVariant], ignoreOrder: true);
    }

    // An uploaded image and an asset with the given id on its key, as an import leaves one; with variants, the owner's two
    // and four pixels wide and the other's two pixels wide, which takes the key of the owner's.
    private async Task<(Guid Owner, Guid Named, string Key, string SharedVariant, string OwnVariant)> SharedAsync(HttpClient admin, Guid? site, Guid named, bool withVariants = true)
    {
        var owner = await UploadAsync(admin, Bands(8, 8));
        string key;
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            var row = await database.Set<Asset>().AsNoTracking().SingleAsync(asset => asset.Id == owner.Id, Token);
            key = row.StorageKey;
            row.Id = named;
            row.OwnsFile = false;
            database.Add(row);
            await database.SaveChangesAsync(Token);
        }

        if (!withVariants)
        {
            return (owner.Id, named, key, "", "");
        }

        await VariantAsync(admin, site, owner.Id, 2);
        await VariantAsync(admin, site, owner.Id, 4);
        await VariantAsync(admin, site, named, 2);
        var shared = (await VariantKeysAsync(named)).ShouldHaveSingleItem();
        var owned = await VariantKeysAsync(owner.Id);
        owned.ShouldContain(shared);
        owned.Count.ShouldBe(2);
        return (owner.Id, named, key, shared, owned.Single(variant => variant != shared));
    }

    private async Task VariantAsync(HttpClient admin, Guid? site, Guid id, int width)
    {
        var asset = (await GetAsync(admin, $"{Root}/{id:D}")).Deserialize<MediaAsset>(JsonSerializerOptions.Web)!;
        using var anonymous = media.Host.CreateClient();
        if (site is { } other)
        {
            anonymous.DefaultRequestHeaders.Add("X-Cms-Site", other.ToString());
        }

        using var response = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = width }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The original and the variant two pixels wide, read from the storage.
    private async Task ServesAsync(HttpClient admin, Guid? site, Guid id)
    {
        await VariantAsync(admin, site, id, 2);
        (await VariantKeysAsync(id)).ShouldContain(key => File.Exists(Path.Combine(media.StorageRoot, key)));
        var row = (await RowAsync(id)).ShouldNotBeNull();
        File.Exists(Path.Combine(media.StorageRoot, row.StorageKey)).ShouldBeTrue();
    }

    private static async Task DeleteAsync(HttpClient admin, Guid id)
    {
        using var response = await admin.DeleteAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task CleanupAsync()
    {
        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using var scope = media.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
        {
        }
    }

    private async Task<Asset?> RowAsync(Guid id)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == id, Token);
    }

    private async Task<List<string>> VariantKeysAsync(Guid id)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Variant>().AsNoTracking().Where(variant => variant.AssetId == id).Select(variant => variant.StorageKey).ToListAsync(Token);
    }

    private bool[] Exists(params string[] keys) => [.. keys.Select(key => File.Exists(Path.Combine(media.StorageRoot, key)))];

    private List<string> FilesOf(Guid? site, Guid id)
    {
        var directory = Path.Combine(media.StorageRoot, (site ?? DefaultSite.Id).ToString("N"), id.ToString("N"));
        return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory)] : [];
    }
}
