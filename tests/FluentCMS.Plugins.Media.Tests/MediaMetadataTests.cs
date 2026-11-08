using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaMetadataTests
{
    private static readonly string[] SeaTags = ["sea", "boats", "sea"];
    private static readonly string[] QuotedTags = ["a\"b"];
    private static readonly string[] BeachTags = ["beach"];

    [Fact]
    public async Task MetadataIsChangedAndReadBackWithAltAndCaptionInTheRequestedLocale()
    {
        await using var media = new MediaHost();
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
        await using var media = new MediaHost();
        using var client = await media.AdminAsync();
        var id = Id((await UploadedAsync(client, null, ("photo.jpg", Files.Jpeg(10, 10)))).Single()!);

        using var response = await client.PatchAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { title = new string('t', 300), tags = QuotedTags, focalPoint = new { x = 2, y = 0 } }, Token);

        var errors = (await JsonAsync(response, HttpStatusCode.UnprocessableEntity))["errors"]!.AsArray().Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>())).ToList();
        errors.ShouldBe([("title", MediaErrorCodes.InvalidField), ("tags[0]", MediaErrorCodes.InvalidField), ("focalPoint", MediaErrorCodes.InvalidField)], ignoreOrder: true);
    }

    [Fact]
    public async Task TheListFiltersByFolderTypeAndTagAndADeletedAssetIsGone()
    {
        await using var media = new MediaHost();
        using var client = await media.AdminAsync();
        var photos = await FolderAsync(client, "photos");
        var summer = await FolderAsync(client, "summer", photos);
        var root = Id((await UploadedAsync(client, null, ("doc.pdf", Files.Pdf()))).Single()!);
        var inPhotos = Id((await UploadedAsync(client, photos, ("a.png", Files.Png(1, 1)))).Single()!);
        var inSummer = Id((await UploadedAsync(client, summer, ("b.jpg", Files.Jpeg(1, 1)))).Single()!);
        await PatchAsync(client, $"{Root}/{inSummer:D}", new { tags = BeachTags });

        (await IdsAsync(client, "")).ShouldBe([inSummer, inPhotos, root]);
        (await IdsAsync(client, "?folder=root")).ShouldBe([root]);
        (await IdsAsync(client, $"?folder={photos:D}")).ShouldBe([inPhotos]);
        (await IdsAsync(client, $"?folder={photos:D}&subfolders=true")).ShouldBe([inSummer, inPhotos]);
        (await IdsAsync(client, "?type=image/*")).ShouldBe([inSummer, inPhotos]);
        (await IdsAsync(client, "?type=application/pdf")).ShouldBe([root]);
        (await IdsAsync(client, "?tag=beach")).ShouldBe([inSummer]);
        (await IdsAsync(client, "?tag=bea")).ShouldBeEmpty();
        (await IdsAsync(client, "?pageSize=1&page=2")).ShouldBe([inPhotos]);

        using var deleted = await client.DeleteAsync(new Uri($"{Root}/{inPhotos:D}", UriKind.Relative), Token);
        using var gone = await client.GetAsync(new Uri($"{Root}/{inPhotos:D}", UriKind.Relative), Token);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).ShouldBe(MediaErrorCodes.AssetNotFound);
        (await IdsAsync(client, "")).ShouldBe([inSummer, root]);
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string query) =>
        [.. (await GetAsync(client, Root + query))["items"]!.AsArray().Select(item => Id(item!))];

    private static async Task<JsonNode> PatchAsync(HttpClient client, string path, object changes)
    {
        using var response = await client.PatchAsJsonAsync(new Uri(path, UriKind.Relative), changes, Token);
        return await JsonAsync(response);
    }
}
