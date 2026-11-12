using System.Net;
using System.Net.Http.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class PerSiteMediaHost() : MediaHost(options => options.Library = MediaLibrary.PerSite);

// FluentCms:Media:Library. Shared, the default: every site reads and writes the library of the default site, and the
// permission is still each site's own.
public sealed class SharedMediaLibraryTests(MediaHost fixture) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task AnAssetAFolderAndAPresetOfOneSiteAreTheSameInAnother()
    {
        using var first = await fixture.AdminAsync();
        using var second = await MediaLibraryTesting.InSiteAsync(fixture, await MediaLibraryTesting.CreateSiteAsync(fixture));
        var folder = await FolderAsync(second, Unique("shared"));
        var asset = (await UploadedAsync(second, folder, ("a.png", Files.Png(2, 2)))).Single()!;
        var preset = Unique("thumb");
        using var put = await second.PutAsJsonAsync(new Uri($"{Root}/presets/{preset}", UriKind.Relative), new { width = 10, height = 10 }, Token);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(Token));

        var read = await GetAsync(first, $"{Root}/{Id(asset)}");

        read["siteId"]!.GetValue<string>().ShouldBe(DefaultSite.Id.ToString());
        asset["siteId"]!.GetValue<string>().ShouldBe(DefaultSite.Id.ToString());
        Id(read, "folderId").ShouldBe(folder);
        (await GetAsync(first, $"{Root}/folders"))["items"]!.AsArray().Select(item => Id(item!)).ShouldContain(folder);
        (await GetAsync(first, $"{Root}/presets"))["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ShouldContain(preset);
        (await GetAsync(fixture.Host.CreateClient(), $"/api/delivery/v1/media/{Id(asset)}"))["id"]!.GetValue<string>().ShouldBe(Id(asset).ToString());
    }

    // A role of the second site reads the folder its grant names, in the second site and nowhere else.
    [Fact]
    public async Task ThePermissionIsCheckedInTheSiteOfTheRequestWithItsFolderLimits()
    {
        using var admin = await fixture.AdminAsync();
        var site = await MediaLibraryTesting.CreateSiteAsync(fixture);
        var open = await FolderAsync(admin, Unique("open"));
        var closed = await FolderAsync(admin, Unique("closed"));
        var reachable = Id((await UploadedAsync(admin, open, ("a.png", Files.Png(1, 1)))).Single()!);
        var hidden = Id((await UploadedAsync(admin, closed, ("b.png", Files.Png(1, 1)))).Single()!);
        var role = Unique("reader");
        using var created = await admin.PostAsJsonAsync(
            new Uri("/api/management/v1/roles", UriKind.Relative),
            new { name = role, siteId = site, grants = new[] { new { permission = "media.read", constraints = new { resources = new[] { open.ToString() } } } } },
            Token);
        var roleId = Id(await JsonAsync(created, HttpStatusCode.Created));
        using var key = await admin.PostAsJsonAsync(new Uri("/api/management/v1/api-keys", UriKind.Relative), new { name = role, @class = "management", roleId }, Token);
        using var reader = fixture.Host.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new("ApiKey", (await JsonAsync(key, HttpStatusCode.Created))["secret"]!.GetValue<string>());

        using var inItsSite = await reader.GetAsync(new Uri($"{Root}/{reachable}?site={site}", UriKind.Relative), Token);
        using var outOfReach = await reader.GetAsync(new Uri($"{Root}/{hidden}?site={site}", UriKind.Relative), Token);
        using var inAnotherSite = await reader.GetAsync(new Uri($"{Root}/{reachable}", UriKind.Relative), Token);
        using var upload = await UploadAsync(reader, open, ("c.png", Files.Png(1, 1)));

        inItsSite.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CodeAsync(outOfReach)).ShouldBe("FCMS1585");
        inAnotherSite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        upload.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // What a site uploads is in the default site's library, so deleting the site removes none of it.
    [Fact]
    public async Task DeletingASiteKeepsTheSharedLibrary()
    {
        var site = await MediaLibraryTesting.CreateSiteAsync(fixture);
        using var second = await MediaLibraryTesting.InSiteAsync(fixture, site);
        var asset = Id((await UploadedAsync(second, null, ("a.png", Files.Png(1, 1)))).Single()!);

        await MediaLibraryTesting.DeleteSiteAsync(fixture, site);

        using var first = await fixture.AdminAsync();
        Id(await GetAsync(first, $"{Root}/{asset}")).ShouldBe(asset);
    }
}

// PerSite: each site keeps its own library, as built in M3.
public sealed class PerSiteMediaLibraryTests(PerSiteMediaHost fixture) : IClassFixture<PerSiteMediaHost>
{
    [Fact]
    public async Task AnAssetAFolderAndAPresetOfOneSiteAreUnknownInAnother()
    {
        using var first = await fixture.AdminAsync();
        var site = await MediaLibraryTesting.CreateSiteAsync(fixture);
        using var second = await MediaLibraryTesting.InSiteAsync(fixture, site);
        var folder = await FolderAsync(first, Unique("own"));
        var asset = Id((await UploadedAsync(first, folder, ("a.png", Files.Png(2, 2)))).Single()!);
        var preset = Unique("thumb");
        using var put = await first.PutAsJsonAsync(new Uri($"{Root}/presets/{preset}", UriKind.Relative), new { width = 10, height = 10 }, Token);
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(Token));

        using var read = await second.GetAsync(new Uri($"{Root}/{asset}", UriKind.Relative), Token);
        using var delivered = await fixture.Host.CreateClient().GetAsync(new Uri($"/api/delivery/v1/media/{asset}?site={site}", UriKind.Relative), Token);
        using var intoFolder = await UploadAsync(second, folder, ("b.png", Files.Png(1, 1)));

        (await CodeAsync(read)).ShouldBe("FCMS1585");
        (await CodeAsync(delivered)).ShouldBe("FCMS1585");
        (await CodeAsync(intoFolder)).ShouldBe("FCMS1586");
        (await GetAsync(second, Root))["items"]!.AsArray().ShouldBeEmpty();
        (await GetAsync(second, $"{Root}/folders"))["items"]!.AsArray().ShouldBeEmpty();
        (await GetAsync(second, $"{Root}/presets"))["items"]!.AsArray().ShouldBeEmpty();
        (await GetAsync(first, $"{Root}/{asset}"))["siteId"]!.GetValue<string>().ShouldBe(DefaultSite.Id.ToString());

        var own = (await UploadedAsync(second, null, ("c.png", Files.Png(1, 1)))).Single()!;
        own["siteId"]!.GetValue<string>().ShouldBe(site.ToString());
        using var fromFirst = await first.GetAsync(new Uri($"{Root}/{Id(own)}", UriKind.Relative), Token);
        (await CodeAsync(fromFirst)).ShouldBe("FCMS1585");
    }

    // A deleted site takes its library with it: its assets, variants, folders and presets, rows and files. An asset whose file
    // cannot be deleted stays as a deleted row, which the cleanup purges, and another site keeps its own. The handler may run
    // again for the same event and goes on with what is left.
    [Fact]
    public async Task DeletingASiteRemovesItsLibraryAndItsFiles()
    {
        using var first = await fixture.AdminAsync();
        var site = await MediaLibraryTesting.CreateSiteAsync(fixture);
        using var second = await MediaLibraryTesting.InSiteAsync(fixture, site);
        var folder = await FolderAsync(second, Unique("gone"));
        var resized = await Transforms.UploadAsync(second, Transforms.Bands(8, 8));
        await UploadedAsync(second, folder, ("a.png", Files.Png(2, 2)));
        var stuck = Id((await UploadedAsync(second, null, ("b.png", Files.Png(2, 2)))).Single()!);
        var kept = Id((await UploadedAsync(first, null, ("c.png", Files.Png(2, 2)))).Single()!);
        using (var put = await second.PutAsJsonAsync(new Uri($"{Root}/presets/{Unique("thumb")}", UriKind.Relative), new { width = 10, height = 10 }, Token))
        {
            put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync(Token));
        }

        using (var anonymous = fixture.Host.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());
            using var variant = await Transforms.FetchAsync(anonymous, await Transforms.UrlAsync(fixture, resized, new MediaImageOptions { Width = 4 }));
            variant.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            (await database.Set<Variant>().AsNoTracking().CountAsync(variant => variant.AssetId == resized.Id, Token)).ShouldBe(1);
            await database.Set<Asset>().Where(asset => asset.Id == stuck).ExecuteUpdateAsync(set => set.SetProperty(asset => asset.StorageProvider, "elsewhere"), Token);
        }

        await MediaLibraryTesting.DeleteSiteAsync(fixture, site);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            var left = await database.Set<Asset>().AsNoTracking().Where(asset => asset.SiteId == site).ToListAsync(Token);
            left.Select(asset => asset.Id).ShouldBe([stuck]);
            left.Single().DeletedAt.ShouldNotBeNull();
            (await database.Set<Variant>().AsNoTracking().AnyAsync(variant => variant.AssetId == resized.Id, Token)).ShouldBeFalse();
            (await database.Set<Folder>().AsNoTracking().AnyAsync(item => item.SiteId == site, Token)).ShouldBeFalse();
            (await database.Set<Preset>().AsNoTracking().AnyAsync(preset => preset.SiteId == site, Token)).ShouldBeFalse();
            (await database.Set<Asset>().AsNoTracking().SingleAsync(asset => asset.Id == kept, Token)).DeletedAt.ShouldBeNull();

            await ActivatorUtilities.CreateInstance<DeletedSiteMedia>(scope.ServiceProvider).HandleAsync(new SiteDeletedEvent(site, "gone"), new EventDelivery(Guid.CreateVersion7(), 2), Token);
            (await database.Set<Asset>().AsNoTracking().Where(asset => asset.SiteId == site).Select(asset => asset.Id).ToListAsync(Token)).ShouldBe([stuck]);
        }

        Directory.EnumerateFiles(Path.Combine(fixture.StorageRoot, site.ToString("N")), "*", SearchOption.AllDirectories).ShouldHaveSingleItem().ShouldContain(stuck.ToString("N"));
        Directory.EnumerateFiles(Path.Combine(fixture.StorageRoot, DefaultSite.Id.ToString("N"), kept.ToString("N"))).ShouldNotBeEmpty();
    }

    // A storage that fails, with an error or with a timeout of its own, fails that file only: its asset stays deleted, the
    // run goes past it to the next batch, and the deletion of the site stands.
    [Fact]
    public async Task AFileTheStorageFailsToDeleteLeavesItsAssetAndTheRestAreRemoved()
    {
        var site = await MediaLibraryTesting.CreateSiteAsync(fixture);
        using var second = await MediaLibraryTesting.InSiteAsync(fixture, site);
        List<Guid> assets = [];
        for (var index = 0; index < 4; index++)
        {
            assets.Add(Id((await UploadedAsync(second, null, ($"{index}.png", Files.Png(1, 1)))).Single()!));
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var keys = await database.Set<Asset>().AsNoTracking().Where(asset => asset.Id == assets[0] || asset.Id == assets[2]).Select(asset => asset.StorageKey).ToListAsync(Token);
        var storage = new FailingStorage(fixture.Services.GetRequiredService<IFileStorage>(), keys[0], keys[1]);
        var handler = new DeletedSiteMedia(database, new MediaStorage(storage, scope.ServiceProvider), TimeProvider.System, NullLogger<DeletedSiteMedia>.Instance) { BatchSize = 1 };

        await handler.HandleAsync(new SiteDeletedEvent(site, "gone"), new EventDelivery(Guid.CreateVersion7(), 1), Token);

        var left = await database.Set<Asset>().AsNoTracking().Where(asset => asset.SiteId == site).ToListAsync(Token);
        left.Select(asset => asset.Id).ShouldBe([assets[0], assets[2]], ignoreOrder: true);
        left.ShouldAllBe(asset => asset.DeletedAt != null);
    }

    // The first key fails with an error, the second with a timeout of the storage's own.
    private sealed class FailingStorage(IFileStorage inner, string broken, string slow) : IFileStorage
    {
        public string Name => inner.Name;

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);

        public Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default) => inner.WriteAsync(key, content, cancellationToken);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            key == broken ? throw new IOException("The storage refused the delete.")
            : key == slow ? throw new TaskCanceledException("The storage timed out.")
            : inner.DeleteAsync(key, cancellationToken);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);

        public Task<FileStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default) => inner.GetMetadataAsync(key, cancellationToken);
    }
}

internal static class MediaLibraryTesting
{
    public static async Task<Guid> CreateSiteAsync(MediaHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Second", $"second-{Guid.NewGuid():N}", "en", Token)).Id;
    }

    // As an administrator of the instance, and then the outbox, so that the handlers of the event have run.
    public static async Task DeleteSiteAsync(MediaHost host, Guid site)
    {
        await host.Host.As(await host.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).RunAsync<ISiteAdministration, bool>(async sites =>
        {
            await sites.DeleteAsync(new SiteId(site), Token);
            return true;
        });
        await host.DrainOutboxAsync();
    }

    // An administrator of the instance whose every request names the site.
    public static async Task<HttpClient> InSiteAsync(MediaHost host, Guid site)
    {
        var client = await host.AdminAsync();
        client.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());
        return client;
    }
}
