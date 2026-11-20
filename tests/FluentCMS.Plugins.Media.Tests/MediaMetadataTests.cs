using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Authorization;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Role = FluentCMS.Core.Storage.Entities.Role;
using RoleGrant = FluentCMS.Core.Storage.Entities.RoleGrant;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaMetadataTests(MediaHost media) : IClassFixture<MediaHost>
{
    private static readonly string[] SeaTags = ["sea", "boats", "sea"];
    private static readonly string[] QuotedTags = ["a\"b"];
    private static readonly string[] BeachTags = ["beach"];

    // IMediaService.RequireUpdateAsync makes the check of UpdateAsync and writes nothing (#3418): for each caller and asset
    // the two refuse alike, and an asset the caller may update is left at its version.
    [Fact]
    public async Task RequireUpdateRefusesAsAnUpdateRefusesAndWritesNothing()
    {
        using var client = await media.AdminAsync();
        var open = await FolderAsync(client, Unique("open"));
        var closed = await FolderAsync(client, Unique("closed"));
        var reachable = Id((await UploadedAsync(client, open, ("a.jpg", Files.Jpeg(10, 10)))).Single()!);
        var hidden = Id((await UploadedAsync(client, closed, ("b.jpg", Files.Jpeg(10, 10)))).Single()!);
        var binned = Id((await UploadedAsync(client, open, ("c.jpg", Files.Jpeg(10, 10)))).Single()!);
        using (var deleted = await client.DeleteAsync(new Uri($"{Root}/{binned:D}", UriKind.Relative), Token))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var reader = await ActorAsync(("media.read", null));
        var limited = await ActorAsync(("media.read", null), ("media.update", new PermissionConstraints { Resources = [open.ToString("D")] }));
        var before = await VersionAsync(client, reachable);

        foreach (var (caller, asset, refusal) in new (TestActor, Guid, string)[]
        {
            (reader, reachable, "media.update"),
            (limited, hidden, MediaErrorCodes.AssetNotFound),
            (limited, binned, MediaErrorCodes.AssetNotFound),
            (limited, Guid.CreateVersion7(), MediaErrorCodes.AssetNotFound),
        })
        {
            var checking = await RefusalAsync(() => caller.RunAsync<IMediaService, bool>(async service =>
            {
                await service.RequireUpdateAsync(asset, Token);
                return true;
            }));
            var updating = await RefusalAsync(() => caller.RunAsync<IMediaService, MediaAsset>(service => service.UpdateAsync(asset, new MediaAssetChanges { Title = "Changed" }, Token)));
            checking.ShouldBe(updating);
            checking.ShouldStartWith(refusal);
        }

        await limited.RunAsync<IMediaService, bool>(async service =>
        {
            await service.RequireUpdateAsync(reachable, Token);
            return true;
        });
        (await VersionAsync(client, reachable)).ShouldBe(before);
    }

    [Fact]
    public async Task MetadataIsChangedAndReadBackWithAltAndCaptionInTheRequestedLocale()
    {
        await media.AddLocaleAsync("fr");
        using var client = await media.AdminAsync();
        var id = Id((await UploadedAsync(client, null, ("photo.jpg", Files.Jpeg(10, 10)))).Single()!);

        var english = await PatchAsync(client, $"{Root}/{id:D}?locale=en", new { title = "Harbour", alt = "Boats in a harbour", caption = "At dawn", credit = "A. Photographer", tags = SeaTags, focalPoint = new { x = 0.25, y = 0.75 }, isPrivate = true });
        var french = await PatchAsync(client, $"{Root}/{id:D}?locale=fr", new { alt = "Bateaux dans un port" });

        english["alt"]!.GetValue<string>().ShouldBe("Boats in a harbour");
        french["alt"]!.GetValue<string>().ShouldBe("Bateaux dans un port");
        french["caption"].ShouldBeNull();
        french["title"]!.GetValue<string>().ShouldBe("Harbour");

        var readEnglish = await GetAsync(client, $"{Root}/{id:D}?locale=en");
        var readFrench = await GetAsync(client, $"{Root}/{id:D}?locale=fr");
        (readEnglish["locale"]!.GetValue<string>(), readEnglish["alt"]!.GetValue<string>(), readEnglish["caption"]!.GetValue<string>()).ShouldBe(("en", "Boats in a harbour", "At dawn"));
        (readFrench["locale"]!.GetValue<string>(), readFrench["alt"]!.GetValue<string>()).ShouldBe(("fr", "Bateaux dans un port"));
        readEnglish["credit"]!.GetValue<string>().ShouldBe("A. Photographer");
        readEnglish["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()).ShouldBe(["sea", "boats"]);
        (readEnglish["focalPoint"]!["x"]!.GetValue<double>(), readEnglish["focalPoint"]!["y"]!.GetValue<double>()).ShouldBe((0.25, 0.75));
        readEnglish["isPrivate"]!.GetValue<bool>().ShouldBeTrue();

        var cleared = await PatchAsync(client, $"{Root}/{id:D}?locale=en", new { alt = "", isPrivate = false });
        cleared["alt"].ShouldBeNull();
        cleared["isPrivate"]!.GetValue<bool>().ShouldBeFalse();
        (await GetAsync(client, $"{Root}/{id:D}?locale=fr"))["alt"]!.GetValue<string>().ShouldBe("Bateaux dans un port");
    }

    [Fact]
    public async Task InvalidMetadataIsRefusedField()
    {
        using var client = await media.AdminAsync();
        var id = Id((await UploadedAsync(client, null, ("photo.jpg", Files.Jpeg(10, 10)))).Single()!);

        using var response = await client.PatchAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { title = new string('t', 300), tags = QuotedTags, focalPoint = new { x = 2, y = 0 } }, Token);

        var errors = (await JsonAsync(response, HttpStatusCode.UnprocessableEntity))["errors"]!.AsArray().Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>())).ToList();
        errors.ShouldBe([("title", MediaErrorCodes.InvalidField), ("tags[0]", MediaErrorCodes.InvalidField), ("focalPoint", MediaErrorCodes.InvalidField)], ignoreOrder: true);
    }

    [Fact]
    public async Task TheListFiltersByFolderTypeAndTagAndADeletedAssetIsGone()
    {
        using var client = await media.AdminAsync();
        var photos = await FolderAsync(client, "photos");
        var summer = await FolderAsync(client, "summer", photos);
        var root = Id((await UploadedAsync(client, null, ("doc.pdf", Files.Pdf()))).Single()!);
        var inPhotos = Id((await UploadedAsync(client, photos, ("a.png", Files.Png(1, 1)))).Single()!);
        var inSummer = Id((await UploadedAsync(client, summer, ("b.jpg", Files.Jpeg(1, 1)))).Single()!);
        await PatchAsync(client, $"{Root}/{inSummer:D}", new { tags = BeachTags });

        // The host is shared: a list is read for this test's assets alone, and the page relies on them being the newest.
        List<Guid> own = [inSummer, inPhotos, root];
        async Task<List<Guid>> OwnAsync(string query) => [.. (await IdsAsync(client, query)).Where(own.Contains)];

        (await OwnAsync("")).ShouldBe([inSummer, inPhotos, root]);
        (await OwnAsync("?folder=root")).ShouldBe([root]);
        (await IdsAsync(client, $"?folder={photos:D}")).ShouldBe([inPhotos]);
        (await IdsAsync(client, $"?folder={photos:D}&subfolders=true")).ShouldBe([inSummer, inPhotos]);
        (await OwnAsync("?type=image/*")).ShouldBe([inSummer, inPhotos]);
        (await OwnAsync("?type=application/pdf")).ShouldBe([root]);
        (await OwnAsync("?type=IMAGE/*")).ShouldBe([inSummer, inPhotos]);
        (await OwnAsync("?type=Application/PDF")).ShouldBe([root]);
        (await OwnAsync("?tag=beach")).ShouldBe([inSummer]);
        (await OwnAsync("?tag=bea")).ShouldBeEmpty();
        (await IdsAsync(client, "?pageSize=1&page=2")).ShouldBe([inPhotos]);

        using var deleted = await client.DeleteAsync(new Uri($"{Root}/{inPhotos:D}", UriKind.Relative), Token);
        using var gone = await client.GetAsync(new Uri($"{Root}/{inPhotos:D}", UriKind.Relative), Token);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).ShouldBe(MediaErrorCodes.AssetNotFound);
        (await OwnAsync("")).ShouldBe([inSummer, root]);
    }

    [Fact]
    public async Task TheListSearchesTheTitleTheFileNameAndTheTagsWhateverTheCase()
    {
        using var client = await media.AdminAsync();
        var mark = Guid.NewGuid().ToString("N")[..12];
        var byName = Id((await UploadedAsync(client, null, ($"Quay-{mark}.png", Files.Png(1, 1)))).Single()!);
        var byTitle = Id((await UploadedAsync(client, null, ("a.png", Files.Png(1, 1)))).Single()!);
        var byTag = Id((await UploadedAsync(client, null, ("b.pdf", Files.Pdf()))).Single()!);
        await PatchAsync(client, $"{Root}/{byTitle:D}", new { title = $"Harbour {mark} at dusk" });
        await PatchAsync(client, $"{Root}/{byTag:D}", new { tags = new[] { "boats", $"sea-{mark}" } });

        (await IdsAsync(client, $"?search={mark}")).ShouldBe([byTag, byTitle, byName]);
        (await IdsAsync(client, $"?search={mark.ToUpperInvariant()}&type=image/*")).ShouldBe([byTitle, byName]);
        (await IdsAsync(client, $"?search=QUAY-{mark}")).ShouldBe([byName]);
        (await IdsAsync(client, $"?search=harbour%20{mark}%20at")).ShouldBe([byTitle]);
        (await IdsAsync(client, $"?search=sea-{mark}")).ShouldBe([byTag]);
        (await IdsAsync(client, $"?search=%22sea-{mark}")).ShouldBeEmpty();
        (await IdsAsync(client, $"?search=boats%22,%22sea-{mark}")).ShouldBeEmpty();

        using var blank = await client.GetAsync(new Uri($"{Root}?search=%20", UriKind.Relative), Token);
        using var tooLong = await client.GetAsync(new Uri($"{Root}?search={new string('x', 257)}", UriKind.Relative), Token);
        blank.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string query) =>
        [.. (await GetAsync(client, Root + query))["items"]!.AsArray().Select(item => Id(item!))];

    private static async Task<JsonNode> PatchAsync(HttpClient client, string path, object changes)
    {
        using var response = await client.PatchAsJsonAsync(new Uri(path, UriKind.Relative), changes, Token);
        return await JsonAsync(response);
    }

    // A caller in this process whose role holds these grants alone.
    private async Task<TestActor> ActorAsync(params (string Permission, PermissionConstraints? Constraints)[] grants)
    {
        var role = new Role { Id = Guid.CreateVersion7(), Name = Unique("role") };
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<Role>().Add(role);
            context.Set<RoleGrant>().AddRange(grants.Select(grant => new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = grant.Permission, Constraints = grant.Constraints?.ToJson() }));
            await context.SaveChangesAsync(Token);
        }

        return media.Host.As(await media.Host.CreateApiKeyAsync(ApiKeyClass.Management, role.Name, cancellationToken: Token));
    }

    private static async Task<string> VersionAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.Headers.ETag!.Tag;
    }

    // The refusal of a call, by the permission it needs or the code and message of the plugin.
    private static async Task<string> RefusalAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (PermissionDeniedException denied)
        {
            return $"{denied.Permission} {denied.Message}";
        }
        catch (PluginException refused)
        {
            return $"{refused.Code} {refused.Message}";
        }

        throw new ShouldAssertException("The call was not refused.");
    }
}
