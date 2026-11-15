using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Media;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// The URLs that GET /media and GET /media/{id} give the media library and the picker (#3100).
public sealed class MediaManagementUrlTests(TransformHosts hosts) : IClassFixture<TransformHosts>
{
    [Fact]
    public async Task AReadGivesAPublicImageASignedOriginalAndThumbnailAndAnyOtherFileItsOriginalOnly()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var folder = await FolderAsync(admin, Unique("photos"));
        var bytes = Bands(400, 200);
        var image = Id((await UploadedAsync(admin, folder, ("bands.png", bytes))).Single()!);
        var pdf = Id((await UploadedAsync(admin, folder, ("plan.pdf", Files.Pdf()))).Single()!);
        var avif = Id((await UploadedAsync(admin, folder, ("photo.avif", Avif()))).Single()!);

        using var read = await admin.GetAsync(new Uri($"{Root}/{image:D}", UriKind.Relative), Token);
        var body = await JsonAsync(read);
        var urls = body["urls"]!;
        var list = (await GetAsync(admin, $"{Root}?folder={folder:D}"))["items"]!.AsArray();
        using var thumbnail = await FetchAsync(anonymous, urls["thumbnail"]!.GetValue<string>());
        using var original = await FetchAsync(anonymous, urls["original"]!.GetValue<string>());
        var presets = (await GetAsync(admin, $"{Root}/presets"))["items"]!.AsArray();

        presets.ShouldBeEmpty();
        body.AsObject().Select(pair => pair.Key).ShouldBe(["id", "siteId", "folderId", "fileName", "mimeType", "size", "checksum", "width", "height", "title", "locale", "alt", "caption", "credit", "tags", "focalPoint", "isPrivate", "createdAt", "createdBy", "updatedAt", "updatedBy", "urls"]);
        (body["fileName"]!.GetValue<string>(), body["width"]!.GetValue<int>(), body["checksum"]!.GetValue<string>().Length).ShouldBe(("bands.png", 400, 64));
        var version = body["checksum"]!.GetValue<string>()[..16];
        read.Headers.CacheControl!.NoStore.ShouldBeTrue();
        urls["original"]!.GetValue<string>().ShouldStartWith($"/media/{image:D}/original/bands.png?v={version}&sig=");
        urls["thumbnail"]!.GetValue<string>().ShouldStartWith($"/media/{image:D}/thumbnail/bands.png?v={version}&sig=");
        urls.ToJsonString().ShouldNotContain("site=");
        urls["expiresAt"].ShouldBeNull();
        var (type, thumb) = await ImageAsync(thumbnail);
        type.ShouldBe("image/webp");
        using (var decoded = Decode(thumb))
        {
            (decoded.Width, decoded.Height).ShouldBe((320, 160));
        }

        thumbnail.Headers.CacheControl!.ToString().ShouldBe("public, max-age=86400");
        thumbnail.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        (await original.Content.ReadAsByteArrayAsync(Token)).ShouldBe(bytes);

        list.Select(item => Id(item!)).ShouldBe([avif, pdf, image], ignoreOrder: true);
        list.Single(item => Id(item!) == image)!["urls"]!.ToJsonString().ShouldBe(urls.ToJsonString());
        foreach (var other in list.Where(item => Id(item!) != image))
        {
            other!["urls"]!["original"]!.GetValue<string>().ShouldContain("/original/");
            other["urls"]!["thumbnail"].ShouldBeNull();
        }
    }

    [Fact]
    public async Task APrivateAssetGetsUrlsThatServeItInlineToACallerWhoMayReadItUntilTheyExpire()
    {
        var media = hosts.Media;
        await using var lifetime = await media.ConfigureAsync(settings => settings.DownloadUrlSeconds = 60);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var team = await FolderAsync(admin, Unique("team"));
        var bytes = Bands(400, 200);
        var asset = Id((await UploadedAsync(admin, team, ("secret.png", bytes))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        using var editor = await media.ClientWithAsync(admin, new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } });
        var now = media.Host.Time.GetUtcNow().ToUnixTimeSeconds();
        var expires = MediaService.PrivateExpiry(now, 60);

        using var read = await editor.GetAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), Token);
        var body = await JsonAsync(read);
        var urls = body["urls"]!;
        var version = body["checksum"]!.GetValue<string>()[..16];
        var listed = (await GetAsync(editor, $"{Root}?folder={team:D}"))["items"]!.AsArray().Single()!["urls"]!;
        var delivered = await GetAsync(editor, $"/api/delivery/v1/media/{asset:D}");
        var original = urls["original"]!.GetValue<string>();
        var thumbnail = urls["thumbnail"]!.GetValue<string>();
        using var servedOriginal = await FetchAsync(anonymous, original);
        using var servedThumbnail = await FetchAsync(anonymous, thumbnail);
        media.Host.Time.Advance(TimeSpan.FromSeconds(expires - now - 1));
        using var lastSecond = await FetchAsync(anonymous, thumbnail);
        media.Host.Time.Advance(TimeSpan.FromSeconds(1));
        using var expiredOriginal = await FetchAsync(anonymous, original);
        using var expiredThumbnail = await FetchAsync(anonymous, thumbnail);

        read.Headers.CacheControl!.NoStore.ShouldBeTrue();
        original.ShouldStartWith($"/media/{asset:D}/original/secret.png?disposition=inline&exp={expires}&v={version}&sig=");
        thumbnail.ShouldStartWith($"/media/{asset:D}/thumbnail/secret.png?exp={expires}&v={version}&sig=");
        urls["expiresAt"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.FromUnixTimeSeconds(expires));
        listed.ToJsonString().ShouldBe(urls.ToJsonString());
        delivered["urls"].ShouldBeNull();

        (await servedOriginal.Content.ReadAsByteArrayAsync(Token)).ShouldBe(bytes);
        servedOriginal.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        servedOriginal.Content.Headers.ContentDisposition.ShouldBeNull();
        foreach (var served in (HttpResponseMessage[])[servedOriginal, servedThumbnail])
        {
            served.StatusCode.ShouldBe(HttpStatusCode.OK);
            served.Headers.CacheControl!.Private.ShouldBeTrue();
            served.Headers.CacheControl.Public.ShouldBeFalse();
            served.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromSeconds(expires - now));
        }

        (await ImageAsync(servedThumbnail)).Type.ShouldBe("image/webp");
        lastSecond.StatusCode.ShouldBe(HttpStatusCode.OK);
        lastSecond.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromSeconds(1));
        foreach (var expired in (HttpResponseMessage[])[expiredOriginal, expiredThumbnail])
        {
            expired.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(expired)).ShouldBe(MediaErrorCodes.DownloadExpired);
        }
    }

    [Fact]
    public async Task ACallerWhoseReadDoesNotReachTheFolderGetsNoUrls()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        var team = await FolderAsync(admin, Unique("team"));
        var other = await FolderAsync(admin, Unique("other"));
        var inside = Id((await UploadedAsync(admin, team, ("inside.png", Bands(8, 8)))).Single()!);
        var outside = Id((await UploadedAsync(admin, other, ("outside.png", Bands(8, 8)))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{outside:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        using var editor = await media.ClientWithAsync(admin, new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } });
        using var nobody = await media.ClientWithAsync(admin, new { permission = "schema.read" });

        using var get = await editor.GetAsync(new Uri($"{Root}/{outside:D}", UriKind.Relative), Token);
        var listed = (await GetAsync(editor, Root))["items"]!.AsArray().Select(item => Id(item!)).ToList();
        using var outOfFolder = await editor.GetAsync(new Uri($"{Root}?folder={other:D}", UriKind.Relative), Token);
        using var refused = await nobody.GetAsync(new Uri($"{Root}/{inside:D}", UriKind.Relative), Token);

        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(get)).ShouldBe(MediaErrorCodes.AssetNotFound);
        (await get.Content.ReadAsStringAsync(Token)).ShouldNotContain("/media/");
        listed.ShouldBe([inside]);
        outOfFolder.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AThumbnailSignatureFitsItsOwnAssetSizeAndExpiryAndNoOtherUrl()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var first = Id((await UploadedAsync(admin, null, ("first.png", Bands(400, 200)))).Single()!);
        var second = Id((await UploadedAsync(admin, null, ("second.png", Bands(400, 200)))).Single()!);
        var hidden = Id((await UploadedAsync(admin, null, ("hidden.png", Bands(400, 200)))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{hidden:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        var thumbnail = (await GetAsync(admin, $"{Root}/{first:D}"))["urls"]!["thumbnail"]!.GetValue<string>();
        var signature = ParameterOf(thumbnail, "sig");
        var hiddenUrls = (await GetAsync(admin, $"{Root}/{hidden:D}"))["urls"]!;
        var hiddenThumbnail = hiddenUrls["thumbnail"]!.GetValue<string>();
        var hiddenOriginal = hiddenUrls["original"]!.GetValue<string>();
        using var issued = await admin.PostAsync(new Uri($"{Root}/{hidden:D}/download-url", UriKind.Relative), null, Token);
        var download = (await JsonAsync(issued))["url"]!.GetValue<string>();
        var later = ParameterOf(hiddenThumbnail, "exp") + "0";

        using var good = await FetchAsync(anonymous, thumbnail);
        using var otherAsset = await FetchAsync(anonymous, $"/media/{second:D}/thumbnail/first.png?sig={signature}");
        using var unsigned = await FetchAsync(anonymous, $"/media/{first:D}/thumbnail/first.png");
        using var sized = await FetchAsync(anonymous, $"{thumbnail}&w=4096");
        using var asTransform = await FetchAsync(anonymous, $"/media/{first:D}/first.png?w=4096&sig={signature}");
        using var asWholeImage = await FetchAsync(anonymous, $"/media/{first:D}/first.png?sig={signature}");
        using var asOriginal = await FetchAsync(anonymous, $"/media/{first:D}/original/first.png?sig={signature}");
        using var publicForm = await FetchAsync(anonymous, $"/media/{hidden:D}/thumbnail/hidden.png?sig={ParameterOf(hiddenThumbnail, "sig")}");
        using var extended = await FetchAsync(anonymous, WithParameter(hiddenThumbnail, "exp", later));
        using var asDownload = await FetchAsync(anonymous, WithoutParameter(hiddenOriginal, "disposition"));
        using var downloadInline = await FetchAsync(anonymous, WithParameter(download, "disposition", "inline"));
        using var otherVersion = await FetchAsync(anonymous, WithParameter(thumbnail, "v", "0000000000000000"));
        using var inlineWithoutExpiry = await FetchAsync(anonymous, $"/media/{first:D}/original/first.png?disposition=inline&sig={signature}");

        good.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var forbidden in (HttpResponseMessage[])[otherAsset, unsigned, asTransform, asWholeImage, asOriginal, publicForm, extended, asDownload, downloadInline, otherVersion])
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(forbidden)).ShouldBe(MediaErrorCodes.SignatureInvalid);
        }

        sized.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(sized)).ShouldBe(MediaErrorCodes.InvalidField);
        inlineWithoutExpiry.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(inlineWithoutExpiry)).ShouldBe(MediaErrorCodes.InvalidField);
    }

    [Fact]
    public async Task AThumbnailIsAlwaysSignedAndAPublicOneStopsWhenTheAssetTurnsPrivate()
    {
        var media = hosts.UnsignedUrls;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = Id((await UploadedAsync(admin, null, ("bands.png", Bands(400, 200)))).Single()!);
        var thumbnail = (await GetAsync(admin, $"{Root}/{asset:D}"))["urls"]!["thumbnail"]!.GetValue<string>();

        using var unsignedTransform = await FetchAsync(anonymous, $"/media/{asset:D}/bands.png?w=40");
        using var unsigned = await FetchAsync(anonymous, $"/media/{asset:D}/thumbnail/bands.png");
        using var signed = await FetchAsync(anonymous, thumbnail);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        using var madePrivate = await FetchAsync(anonymous, thumbnail);

        unsignedTransform.StatusCode.ShouldBe(HttpStatusCode.OK);
        unsigned.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(unsigned)).ShouldBe(MediaErrorCodes.SignatureInvalid);
        signed.StatusCode.ShouldBe(HttpStatusCode.OK);
        madePrivate.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(madePrivate)).ShouldBe(MediaErrorCodes.AssetNotFound);
    }

    [Fact]
    public async Task AThumbnailIsNotHeldToTheTransformLimitsOfTheSiteButToItsSourcePixels()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200), "bands.png");
        var pdf = Id((await UploadedAsync(admin, null, ("plan.pdf", Files.Pdf()))).Single()!);
        var thumbnail = (await GetAsync(admin, $"{Root}/{asset.Id:D}"))["urls"]!["thumbnail"]!.GetValue<string>();
        string pdfThumbnail;
        await using (var scope = media.Services.CreateAsyncScope())
        {
            pdfThumbnail = await MediaTransforms.ThumbnailUrlAsync(scope.ServiceProvider.GetRequiredService<MediaSigning>(), pdf, "plan.pdf", null, null, Token);
        }

        HttpResponseMessage limited, transform;
        await using (await media.ConfigureAsync(settings =>
        {
            settings.MaxTransformWidth = 100;
            settings.MaxTransformHeight = 100;
            settings.MaxTransformOperations = 1;
        }))
        {
            limited = await FetchAsync(anonymous, thumbnail);
            transform = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = 320 }));
        }

        HttpResponseMessage tooLarge;
        await using (await media.ConfigureAsync(settings => settings.MaxSourcePixels = 400 * 200 - 1))
        {
            tooLarge = await FetchAsync(anonymous, thumbnail);
        }

        using var notAnImage = await FetchAsync(anonymous, pdfThumbnail);

        using (limited)
        using (transform)
        using (tooLarge)
        {
            using (var image = Decode((await ImageAsync(limited)).Bytes))
            {
                (image.Width, image.Height).ShouldBe((320, 160));
            }

            transform.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            tooLarge.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await CodeAsync(tooLarge)).ShouldBe(MediaErrorCodes.TransformLimit);
        }

        notAnImage.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(notAnImage)).ShouldBe(MediaErrorCodes.NotTransformable);
    }

    [Fact]
    public async Task AListSignsTheUrlsOfEveryAssetWithoutTransformingAnyAndAThumbnailIsMadeOnce()
    {
        var media = hosts.Recording;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var folder = await FolderAsync(admin, Unique("grid"));
        for (var index = 0; index < 12; index++)
        {
            await UploadedAsync(admin, folder, ($"photo-{index}.png", Bands(40 + index, 20)));
        }

        var calls = CallsFrom(media);
        var items = (await GetAsync(admin, $"{Root}?folder={folder:D}&pageSize=100"))["items"]!.AsArray();
        var listed = calls();
        var thumbnail = items[0]!["urls"]!["thumbnail"]!.GetValue<string>();
        using var first = await FetchAsync(anonymous, thumbnail);
        using var again = await FetchAsync(anonymous, thumbnail);

        items.Count.ShouldBe(12);
        items.ShouldAllBe(item => item!["urls"]!["thumbnail"] != null);
        listed.ShouldBe((0, 0));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.Headers.ETag.ShouldBe(first.Headers.ETag);
        calls().ShouldBe((1, 1));
    }

    [Fact]
    public async Task APrivateAssetKeepsItsUrlsForHalfTheirLifetimeAndEachLivesLongerThanThat()
    {
        var media = hosts.Media;
        await using var lifetime = await media.ConfigureAsync(settings => settings.DownloadUrlSeconds = 60);
        using var admin = await media.AdminAsync();
        var asset = Id((await UploadedAsync(admin, null, ("held.png", Bands(8, 8)))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        var time = media.Host.Time;

        async Task<(JsonNode Urls, long Left)> ReadAsync()
        {
            var urls = (await GetAsync(admin, $"{Root}/{asset:D}"))["urls"]!;
            return (urls, urls["expiresAt"]!.GetValue<DateTimeOffset>().ToUnixTimeSeconds() - time.GetUtcNow().ToUnixTimeSeconds());
        }

        var first = await ReadAsync();
        var windowEnd = time.GetUtcNow().ToUnixTimeSeconds() + first.Left - 30;
        time.Advance(TimeSpan.FromSeconds(windowEnd - time.GetUtcNow().ToUnixTimeSeconds() - 1));
        var lastOfWindow = await ReadAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        var next = await ReadAsync();

        first.Left.ShouldBeInRange(31, 60);
        lastOfWindow.Urls.ToJsonString().ShouldBe(first.Urls.ToJsonString());
        lastOfWindow.Left.ShouldBe(31);
        next.Urls["thumbnail"]!.GetValue<string>().ShouldNotBe(first.Urls["thumbnail"]!.GetValue<string>());
        next.Left.ShouldBe(60);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(61)]
    [InlineData(300)]
    [InlineData(86400)]
    public void APrivateExpiryLivesMoreThanHalfTheLifetimeAndNeverLonger(int lifetime)
    {
        var start = 1_763_000_000L;
        for (var now = start; now < start + lifetime + 3; now++)
        {
            var left = MediaService.PrivateExpiry(now, lifetime) - now;
            left.ShouldBeGreaterThan(lifetime / 2);
            left.ShouldBeLessThanOrEqualTo(lifetime);
        }
    }

    [Fact]
    public async Task AReplaceGivesTheAssetNewUrlsSoNoCacheServesTheOldFile()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = Id((await UploadedAsync(admin, null, ("wide.png", Bands(400, 200)))).Single()!);
        var before = (await GetAsync(admin, $"{Root}/{asset:D}"))["urls"]!;
        using var oldThumbnail = await FetchAsync(anonymous, before["thumbnail"]!.GetValue<string>());

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Bands(200, 400));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", "tall.png");
        using var replace = await admin.PostAsync(new Uri($"{Root}/{asset:D}/replace", UriKind.Relative), form, Token);
        var replaced = await JsonAsync(replace);
        var after = (await GetAsync(admin, $"{Root}/{asset:D}"))["urls"]!;
        using var newThumbnail = await FetchAsync(anonymous, after["thumbnail"]!.GetValue<string>());

        after["original"]!.GetValue<string>().ShouldNotBe(before["original"]!.GetValue<string>());
        after["original"]!.GetValue<string>().ShouldContain($"v={replaced["checksum"]!.GetValue<string>()[..16]}&");
        after["thumbnail"]!.GetValue<string>().ShouldNotBe(before["thumbnail"]!.GetValue<string>());
        newThumbnail.Headers.ETag.ShouldNotBe(oldThumbnail.Headers.ETag);
        using var image = Decode((await ImageAsync(newThumbnail)).Bytes);
        (image.Width, image.Height).ShouldBe((160, 320));
    }

    [Fact]
    public async Task ASmallImageIsEnlargedToTheBoxAndADeletedAssetHasNoThumbnail()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var wide = Id((await UploadedAsync(admin, null, ("small-wide.png", Bands(40, 20)))).Single()!);
        var square = Id((await UploadedAsync(admin, null, ("small-square.png", Bands(8, 8)))).Single()!);
        var hidden = Id((await UploadedAsync(admin, null, ("small-hidden.png", Bands(8, 8)))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{hidden:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        var wideThumbnail = (await GetAsync(admin, $"{Root}/{wide:D}"))["urls"]!["thumbnail"]!.GetValue<string>();
        var squareThumbnail = (await GetAsync(admin, $"{Root}/{square:D}"))["urls"]!["thumbnail"]!.GetValue<string>();
        var hiddenUrls = (await GetAsync(admin, $"{Root}/{hidden:D}"))["urls"]!;

        using var wideServed = await FetchAsync(anonymous, wideThumbnail);
        using var squareServed = await FetchAsync(anonymous, squareThumbnail);
        foreach (var id in (Guid[])[square, hidden])
        {
            (await admin.DeleteAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token)).Dispose();
        }

        using var deleted = await FetchAsync(anonymous, squareThumbnail);
        using var deletedPrivate = await FetchAsync(anonymous, hiddenUrls["thumbnail"]!.GetValue<string>());
        using var deletedOriginal = await FetchAsync(anonymous, hiddenUrls["original"]!.GetValue<string>());

        using (var image = Decode((await ImageAsync(wideServed)).Bytes))
        {
            (image.Width, image.Height).ShouldBe((320, 160));
        }

        using (var image = Decode((await ImageAsync(squareServed)).Bytes))
        {
            (image.Width, image.Height).ShouldBe((320, 320));
        }

        foreach (var gone in (HttpResponseMessage[])[deleted, deletedPrivate, deletedOriginal])
        {
            gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CodeAsync(gone)).ShouldBe(MediaErrorCodes.AssetNotFound);
        }
    }

    // A browser asks for an image without the X-Cms-Site of the read, so with a library per site the URLs name the site.
    [Fact]
    public async Task WithALibraryPerSiteTheUrlsOfAnotherSiteServeWithoutItsHeader()
    {
        await using var media = new PerSiteMediaHost();
        var site = await MediaLibraryTesting.CreateSiteAsync(media);
        using var second = await MediaLibraryTesting.InSiteAsync(media, site);
        using var anonymous = media.Host.CreateClient();
        var bytes = Bands(400, 200);
        var open = Id((await UploadedAsync(second, null, ("open.png", bytes))).Single()!);
        var hidden = Id((await UploadedAsync(second, null, ("hidden.png", bytes))).Single()!);
        (await second.PatchAsJsonAsync(new Uri($"{Root}/{hidden:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        var urls = (await GetAsync(second, Root))["items"]!.AsArray().SelectMany(item => (string[])[item!["urls"]!["original"]!.GetValue<string>(), item["urls"]!["thumbnail"]!.GetValue<string>()]).ToList();

        List<HttpResponseMessage> served = [];
        List<HttpResponseMessage> withoutSite = [];
        foreach (var url in urls)
        {
            served.Add(await FetchAsync(anonymous, url));
            withoutSite.Add(await FetchAsync(anonymous, WithoutParameter(url, "site")));
        }

        urls.Count.ShouldBe(4);
        urls.ShouldAllBe(url => url.EndsWith($"&site={site:D}", StringComparison.Ordinal));
        foreach (var response in served)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Dispose();
        }

        foreach (var response in withoutSite)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            response.Dispose();
        }
    }

    // An AVIF header, a type the plugin finds from bytes and the default image processor does not read.
    private static byte[] Avif() => [0x00, 0x00, 0x00, 0x1C, .. "ftypavif"u8, 0x00, 0x00, 0x00, 0x00, .. "avifmif1miaf"u8, 0x00, 0x00, 0x00, 0x00];

    private static string ParameterOf(string url, string name) =>
        url.Split('?', 2)[1].Split('&').Select(pair => pair.Split('=', 2)).Single(pair => pair[0] == name)[1];

    private static string WithoutParameter(string url, string name)
    {
        var parts = url.Split('?', 2);
        return $"{parts[0]}?{string.Join('&', parts[1].Split('&').Where(pair => pair.Split('=', 2)[0] != name))}";
    }
}
