using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    // A replace of the owner leaves its old file and variants, which others name, to a holder: a deleted row that owns
    // them. The variant no other names goes at the next cleanup, and the rest only once the last asset that names each is
    // gone, whatever the retention says.
    [Fact]
    public async Task AReplacedFileThatOthersNameGoesWithItsVariantsOnceTheLastOfThemGoes()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());
        var other = await NameAsync(shared.Owner, Guid.CreateVersion7());

        await ReplaceAsync(admin, shared.Owner, Bands(12, 4));

        var holder = (await HolderAsync(shared.Key)).ShouldNotBeNull();
        holder.Id.ShouldNotBeOneOf(shared.Owner, shared.Named, other);
        (await GetAsync(admin, $"{Root}?pageSize=100")).ToJsonString().ShouldNotContain(holder.Id.ToString("D"));
        (await ExportedAssetsAsync(admin)).ShouldNotContain(holder.Id.ToString("D"));
        (await VariantKeysAsync(holder.Id)).ShouldBe([shared.SharedVariant, shared.OwnVariant], ignoreOrder: true);
        (await VariantKeysAsync(shared.Owner)).ShouldBeEmpty();
        Exists(shared.Key, shared.SharedVariant, shared.OwnVariant).ShouldBe([true, true, true]);

        await CleanupAsync();

        Exists(shared.Key, shared.SharedVariant, shared.OwnVariant).ShouldBe([true, true, false]);
        await ServesAsync(admin, null, shared.Named);

        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        Exists(shared.Key, shared.SharedVariant).ShouldBe([true, true]);
        (await RowAsync(holder.Id)).ShouldNotBeNull();

        await DeleteAsync(admin, shared.Named);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Named)).ShouldBeNull();
        Exists(shared.Key, shared.SharedVariant).ShouldBe([true, false]);
        (await RowAsync(holder.Id)).ShouldNotBeNull();

        await DeleteAsync(admin, other);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();
        await CleanupAsync();

        (await RowAsync(holder.Id)).ShouldBeNull();
        Exists(shared.Key).ShouldBe([false]);
        var owner = (await RowAsync(shared.Owner)).ShouldNotBeNull();
        FilesOf(null, shared.Owner).Select(Path.GetFileName).ShouldBe([Path.GetFileName(owner.StorageKey)]);
    }

    // Each replaced file that another asset names has a holder of its own, and goes with the last asset that names it.
    [Fact]
    public async Task EachReplacedFileThatAnotherNamesGoesWithTheLastAssetThatNamesIt()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7(), withVariants: false);
        await ReplaceAsync(admin, shared.Owner, Bands(12, 4));
        var second = (await RowAsync(shared.Owner)).ShouldNotBeNull().StorageKey;
        var other = await NameAsync(shared.Owner, Guid.CreateVersion7());
        await ReplaceAsync(admin, shared.Owner, Bands(16, 4));

        (await HolderAsync(shared.Key)).ShouldNotBeNull();
        (await HolderAsync(second)).ShouldNotBeNull();
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();
        Exists(shared.Key, second).ShouldBe([true, true]);

        await DeleteAsync(admin, shared.Named);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        Exists(shared.Key, second).ShouldBe([false, true]);
        (await HolderAsync(shared.Key)).ShouldBeNull();

        await DeleteAsync(admin, other);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        Exists(second).ShouldBe([false]);
        (await HolderAsync(second)).ShouldBeNull();
        var owner = (await RowAsync(shared.Owner)).ShouldNotBeNull();
        FilesOf(null, shared.Owner).Select(Path.GetFileName).ShouldBe([Path.GetFileName(owner.StorageKey)]);
    }

    // A replace of an asset on another's key keeps the files, and hands its variant rows to the owner of the key, as its
    // delete does, so they go with the owner.
    [Fact]
    public async Task AReplaceOfAnAssetOnTheKeyHandsItsVariantsToTheOwner()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());
        await VariantAsync(admin, null, shared.Named, 6);
        var made = (await VariantKeysAsync(shared.Named)).Where(key => key != shared.SharedVariant).ShouldHaveSingleItem();

        await ReplaceAsync(admin, shared.Named, Bands(12, 4));

        (await VariantKeysAsync(shared.Named)).ShouldBeEmpty();
        (await VariantKeysAsync(shared.Owner)).ShouldBe([shared.SharedVariant, shared.OwnVariant, made], ignoreOrder: true);
        Exists(shared.Key, made).ShouldBe([true, true]);

        await DeleteAsync(admin, shared.Owner);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Owner)).ShouldBeNull();
        FilesOf(null, shared.Owner).ShouldBeEmpty();
    }

    // One variant that the owner made at the same moment beats its row, and one whose save fails otherwise stays with the
    // asset for the next run; the others are handed over all the same.
    [Fact]
    public async Task AHandOverGoesRowByRowAndKeepsARowItCouldNotHandOver()
    {
        var save = new HandOverSave();
        await using var host = new PerSiteMediaHost();
        host.Host.Configure(cms => cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(save)));
        using var admin = await host.AdminAsync();
        var owner = await UploadAsync(admin, Bands(8, 8));
        var named = Guid.CreateVersion7();
        string[] hashes = [new('1', 64), new('2', 64), new('3', 64)];
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            var row = await database.Set<Asset>().AsNoTracking().SingleAsync(asset => asset.Id == owner.Id, Token);
            var folder = row.StorageKey[..row.StorageKey.LastIndexOf('/')];
            row.Id = named;
            row.OwnsFile = false;
            row.DeletedAt = host.Host.Time.GetUtcNow();
            database.Add(row);
            database.AddRange(hashes.Select(hash => new Variant { AssetId = named, ParamsHash = hash, StorageKey = $"{folder}/{hash}.png", Size = 1, CreatedAt = host.Host.Time.GetUtcNow() }));
            await database.SaveChangesAsync(Token);
        }

        (save.Services, save.Owner, save.Duplicate, save.Refused) = (host.Services, owner.Id, hashes[0], hashes[1]);
        await CleanupAsync(host);

        (await HashesAsync(host, owner.Id)).ShouldBe([hashes[0], hashes[2]], ignoreOrder: true);
        (await HashesAsync(host, named)).ShouldBe([hashes[1]]);
    }

    // The owner that holds the bytes of its old file again makes its variants at the keys its holder names, which the
    // holder then leaves to it.
    [Fact]
    public async Task AVariantTheOwnerMakesAgainAtTheKeyOfItsHolderStays()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());
        await ReplaceAsync(admin, shared.Owner, Bands(12, 4));
        await ReplaceAsync(admin, shared.Owner, Bands(8, 8));
        await VariantAsync(admin, null, shared.Owner, 4);
        (await VariantKeysAsync(shared.Owner)).ShouldBe([shared.OwnVariant]);

        await CleanupAsync();

        Exists(shared.Key, shared.SharedVariant, shared.OwnVariant).ShouldBe([true, true, true]);
        await ServesAsync(admin, null, shared.Owner);
    }

    // An asset on the owner's key makes variants in the owner's folder; when it goes, they go to the owner, and with it.
    [Fact]
    public async Task TheVariantsThatAnAssetOnTheKeyMadeGoToTheOwnerAndGoWithIt()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7());
        await VariantAsync(admin, null, shared.Named, 6);
        var made = (await VariantKeysAsync(shared.Named)).Where(key => key != shared.SharedVariant).ShouldHaveSingleItem();

        await DeleteAsync(admin, shared.Named);
        await CleanupAsync();

        (await VariantKeysAsync(shared.Named)).ShouldBeEmpty();
        (await VariantKeysAsync(shared.Owner)).ShouldBe([shared.SharedVariant, shared.OwnVariant, made], ignoreOrder: true);
        Exists(made).ShouldBe([true]);

        await DeleteAsync(admin, shared.Owner);
        media.Host.Time.Advance(PastRetention);
        await CleanupAsync();

        (await RowAsync(shared.Owner)).ShouldBeNull();
        (await RowAsync(shared.Named)).ShouldBeNull();
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
        await VariantAsync(admin, site, shared.Named, 6);

        await using (var scope = media.Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<DeletedSiteMedia>(scope.ServiceProvider).HandleAsync(new SiteDeletedEvent(site, "gone"), new EventDelivery(Guid.CreateVersion7(), 1), Token);
        }

        (await RowAsync(shared.Owner)).ShouldBeNull();
        (await RowAsync(shared.Named)).ShouldBeNull();
        FilesOf(site, shared.Owner).ShouldBeEmpty();
    }

    // An asset imported with the key of another instance that shares the storage: no row of this instance owns its file, so
    // the deletion of its site takes its rows and leaves the file and the variant file beside it.
    [Fact]
    public async Task DeletingTheSiteLeavesTheFilesOfAnAssetThatOwnsNone()
    {
        var site = await MediaLibraryTesting.CreateSiteAsync(media);
        using var admin = await MediaLibraryTesting.InSiteAsync(media, site);
        var uploaded = await UploadAsync(admin, Bands(8, 8));
        var foreign = Guid.CreateVersion7();
        var key = $"{site:N}/{foreign:N}/original.png";
        var variant = $"{site:N}/{foreign:N}/{new string('c', 64)}.png";
        Directory.CreateDirectory(Path.Combine(media.StorageRoot, site.ToString("N"), foreign.ToString("N")));
        await File.WriteAllBytesAsync(Path.Combine(media.StorageRoot, key), Bands(8, 8), Token);
        await File.WriteAllBytesAsync(Path.Combine(media.StorageRoot, variant), Bands(2, 2), Token);
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            var row = await database.Set<Asset>().AsNoTracking().SingleAsync(asset => asset.Id == uploaded.Id, Token);
            row.Id = foreign;
            row.StorageKey = key;
            row.OwnsFile = false;
            database.Add(row);
            database.Add(new Variant { AssetId = foreign, ParamsHash = new string('c', 64), StorageKey = variant, Size = 1, CreatedAt = media.Host.Time.GetUtcNow() });
            await database.SaveChangesAsync(Token);
        }

        await using (var scope = media.Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<DeletedSiteMedia>(scope.ServiceProvider).HandleAsync(new SiteDeletedEvent(site, "gone"), new EventDelivery(Guid.CreateVersion7(), 1), Token);
        }

        (await RowAsync(foreign)).ShouldBeNull();
        (await VariantKeysAsync(foreign)).ShouldBeEmpty();
        (await RowAsync(uploaded.Id)).ShouldBeNull();
        Exists(key, variant).ShouldBe([true, true]);
        FilesOf(site, uploaded.Id).ShouldBeEmpty();
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

    // A gone asset made the variants in its own folder; one beside another asset's key it made for a key a line named, which
    // can be another instance's, so only its row goes.
    [Fact]
    public async Task AVariantOfAGoneAssetBesideAnotherAssetsKeyKeepsItsFile()
    {
        using var admin = await media.AdminAsync();
        var shared = await SharedAsync(admin, null, Guid.CreateVersion7(), withVariants: false);
        var gone = Guid.CreateVersion7();
        var beside = $"{DefaultSite.Id:N}/{shared.Owner:N}/{new string('b', 64)}.png";
        var own = $"{DefaultSite.Id:N}/{gone:N}/{new string('c', 64)}.png";
        Directory.CreateDirectory(Path.Combine(media.StorageRoot, DefaultSite.Id.ToString("N"), gone.ToString("N")));
        foreach (var key in (string[])[beside, own])
        {
            await File.WriteAllBytesAsync(Path.Combine(media.StorageRoot, key), Bands(2, 2), Token);
        }

        await using (var scope = media.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            database.Add(new Variant { AssetId = gone, ParamsHash = new string('b', 64), StorageKey = beside, Size = 1, CreatedAt = media.Host.Time.GetUtcNow() });
            database.Add(new Variant { AssetId = gone, ParamsHash = new string('c', 64), StorageKey = own, Size = 1, CreatedAt = media.Host.Time.GetUtcNow() });
            await database.SaveChangesAsync(Token);
        }

        await CleanupAsync();

        (await VariantKeysAsync(gone)).ShouldBeEmpty();
        Exists(beside, own).ShouldBe([true, false]);
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

    // Another asset on the owner's key, as an import leaves one.
    private async Task<Guid> NameAsync(Guid owner, Guid named)
    {
        await using var scope = media.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var row = await database.Set<Asset>().AsNoTracking().SingleAsync(asset => asset.Id == owner, Token);
        row.Id = named;
        row.OwnsFile = false;
        database.Add(row);
        await database.SaveChangesAsync(Token);
        return named;
    }

    // The deleted row that owns a file a replace left.
    private async Task<Asset?> HolderAsync(string key)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.StorageKey == key && asset.OwnsFile && asset.DeletedAt != null, Token);
    }

    private static async Task ReplaceAsync(HttpClient admin, Guid id, byte[] png)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "new.png");
        using var replaced = await admin.PostAsync(new Uri($"{Root}/{id:D}/replace", UriKind.Relative), form, Token);
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync(Token));
    }

    private static async Task DeleteAsync(HttpClient admin, Guid id)
    {
        using var response = await admin.DeleteAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task CleanupAsync() => CleanupAsync(media);

    private static async Task CleanupAsync(MediaHost host)
    {
        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using var scope = host.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
        {
        }
    }

    private static async Task<List<string>> HashesAsync(MediaHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Variant>().AsNoTracking().Where(variant => variant.AssetId == id).Select(variant => variant.ParamsHash).ToListAsync(Token);
    }

    // The asset lines of an export of the default site.
    private static async Task<string> ExportedAssetsAsync(HttpClient admin)
    {
        using var exported = await admin.PostAsync(new Uri("/api/management/v1/transfer/export", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        exported.StatusCode.ShouldBe(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync(Token));
        using var package = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync(Token)), ZipArchiveMode.Read);
        await using var lines = package.GetEntry("plugins/media/assets.jsonl").ShouldNotBeNull().Open();
        using var reader = new StreamReader(lines);
        var text = await reader.ReadToEndAsync(Token);
        text.ShouldNotBeEmpty();
        return text;
    }

    // A save that hands a variant over to the owner: for one hash the owner makes the same variant first, through another
    // context, and for another the database refuses the save.
    private sealed class HandOverSave : SaveChangesInterceptor
    {
        private int _armed = 1;

        public IServiceProvider? Services { get; set; }

        public Guid Owner { get; set; }

        public string? Duplicate { get; set; }

        public string? Refused { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var added = eventData.Context!.ChangeTracker.Entries<Variant>().Where(entry => entry.State == EntityState.Added && entry.Entity.AssetId == Owner).Select(entry => entry.Entity).ToList();
            if (added.Any(variant => variant.ParamsHash == Refused))
            {
                throw new DbUpdateException("The database refused the row.");
            }

            if (added.FirstOrDefault(variant => variant.ParamsHash == Duplicate) is { } duplicate && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                await using var scope = Services!.CreateAsyncScope();
                var other = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
                other.Add(new Variant { AssetId = Owner, ParamsHash = duplicate.ParamsHash, StorageKey = duplicate.StorageKey, Size = 2, CreatedAt = duplicate.CreatedAt });
                await other.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
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
