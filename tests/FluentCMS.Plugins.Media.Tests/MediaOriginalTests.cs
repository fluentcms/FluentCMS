using System.Net;
using System.Net.Http.Json;
using FluentCMS.Media;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaOriginalTests(MediaHost media) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task ADownloadUrlServesAPrivateFileUntilItExpiresAndIsRefusedAfter()
    {
        await using var lifetime = await media.ConfigureAsync(settings => settings.DownloadUrlSeconds = 60);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var team = await FolderAsync(admin, Unique("team"));
        var pdf = Files.Pdf();
        var asset = Id((await UploadedAsync(admin, team, ("plan.pdf", pdf))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        using var editor = await media.ClientWithAsync(admin, new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } });
        var now = media.Host.Time.GetUtcNow();

        using var issued = await editor.PostAsync(new Uri($"{Root}/{asset:D}/download-url", UriKind.Relative), null, Token);
        var download = await JsonAsync(issued);
        var url = download["url"]!.GetValue<string>();
        using var served = await FetchAsync(anonymous, url);
        using var tampered = await FetchAsync(anonymous, WithParameter(url, MediaOriginals.Expires, (now.ToUnixTimeSeconds() + 3600).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        media.Host.Time.Advance(TimeSpan.FromSeconds(60));
        using var expired = await FetchAsync(anonymous, url);

        url.ShouldStartWith($"/media/{asset:D}/original/plan.pdf?exp=");
        download["expiresAt"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() + 60));
        (await served.Content.ReadAsByteArrayAsync(Token)).ShouldBe(pdf);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        served.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        served.Headers.CacheControl!.NoStore.ShouldBeTrue();
        served.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        tampered.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(tampered)).ShouldBe(MediaErrorCodes.SignatureInvalid);
        expired.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(expired)).ShouldBe(MediaErrorCodes.DownloadExpired);
    }

    [Fact]
    public async Task ACallerWhoCannotReadTheFolderGetsNoDownloadUrl()
    {
        using var admin = await media.AdminAsync();
        var team = await FolderAsync(admin, Unique("team"));
        var other = await FolderAsync(admin, Unique("other"));
        var asset = Id((await UploadedAsync(admin, other, ("plan.pdf", Files.Pdf()))).Single()!);
        using var editor = await media.ClientWithAsync(admin, new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } });
        using var nobody = await media.ClientWithAsync(admin, new { permission = "schema.read" });

        using var outside = await editor.PostAsync(new Uri($"{Root}/{asset:D}/download-url", UriKind.Relative), null, Token);
        using var refused = await nobody.PostAsync(new Uri($"{Root}/{asset:D}/download-url", UriKind.Relative), null, Token);

        outside.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(outside)).ShouldBe(MediaErrorCodes.AssetNotFound);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheOriginalOfAPublicAssetIsServedSignedAndNeverThatOfAPrivateOrDeletedOne()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var bytes = Bands(8, 8);
        var asset = await UploadAsync(admin, bytes, "bands.png");
        var original = (await GetAsync(anonymous, $"/api/delivery/v1/media/{asset.Id:D}"))["urls"]!["original"]!.GetValue<string>();
        var transform = await UrlAsync(media, asset);

        using var served = await FetchAsync(anonymous, original);
        using var unsigned = await FetchAsync(anonymous, $"/media/{asset.Id:D}/original/bands.png");
        using var borrowed = await FetchAsync(anonymous, $"/media/{asset.Id:D}/original/bands.png?{transform.Split('?')[1]}");
        using var unknown = await FetchAsync(anonymous, $"{original}&w=10");
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        using var madePrivate = await FetchAsync(anonymous, original);
        using var issued = await admin.PostAsync(new Uri($"{Root}/{asset.Id:D}/download-url", UriKind.Relative), null, Token);
        var download = (await JsonAsync(issued))["url"]!.GetValue<string>();
        (await admin.DeleteAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), Token)).Dispose();
        using var deleted = await FetchAsync(anonymous, download);

        (await served.Content.ReadAsByteArrayAsync(Token)).ShouldBe(bytes);
        served.Headers.CacheControl!.ToString().ShouldBe("public, max-age=86400");
        served.Headers.ETag!.Tag.ShouldBe($"\"{asset.Checksum}\"");
        served.Content.Headers.ContentDisposition.ShouldBeNull();
        unsigned.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(unsigned)).ShouldBe(MediaErrorCodes.SignatureInvalid);
        borrowed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        madePrivate.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnSvgIsServedWithAPolicyThatRunsNothingAndNosniff()
    {
        await using var sanitised = await media.ConfigureAsync(settings => settings.Svg = SvgHandling.Sanitize);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = Id((await UploadedAsync(admin, null, ("logo.svg", Files.Svg("""<rect width="10" height="10"/>""")))).Single()!);
        var original = (await GetAsync(anonymous, $"/api/delivery/v1/media/{asset:D}"))["urls"]!["original"]!.GetValue<string>();

        using var served = await FetchAsync(anonymous, original);

        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        served.Headers.GetValues("Content-Security-Policy").ShouldBe([MediaEndpoints.SvgPolicy]);
        served.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task AStoredTypeThePluginDoesNotFindIsSentAsAnAttachmentAndAnSvgTypeInAnyCaseGetsThePolicy()
    {
        await using var sanitised = await media.ConfigureAsync(settings => settings.Svg = SvgHandling.Sanitize);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var png = Files.Png(4, 4);
        var page = Id((await UploadedAsync(admin, null, ("page.png", png))).Single()!);
        var logo = Id((await UploadedAsync(admin, null, ("logo.svg", Files.Svg("""<rect width="10" height="10"/>""")))).Single()!);
        var upper = Id((await UploadedAsync(admin, null, ("upper.png", png))).Single()!);
        await media.SetMimeTypeAsync(page, "text/html");
        await media.SetMimeTypeAsync(logo, "IMAGE/SVG+XML");
        await media.SetMimeTypeAsync(upper, "Image/PNG");

        using var html = await FetchAsync(anonymous, await OriginalAsync(anonymous, page));
        using var svg = await FetchAsync(anonymous, await OriginalAsync(anonymous, logo));
        using var image = await FetchAsync(anonymous, await OriginalAsync(anonymous, upper));

        html.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        html.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        html.Content.Headers.ContentDisposition.FileName.ShouldBe("page.png");
        html.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        html.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        (await html.Content.ReadAsByteArrayAsync(Token)).ShouldBe(png);
        svg.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        svg.Content.Headers.ContentDisposition.ShouldBeNull();
        svg.Headers.GetValues("Content-Security-Policy").ShouldBe([MediaEndpoints.SvgPolicy]);
        image.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        image.Content.Headers.ContentDisposition.ShouldBeNull();
    }

    private static async Task<string> OriginalAsync(HttpClient client, Guid asset) =>
        (await GetAsync(client, $"/api/delivery/v1/media/{asset:D}"))["urls"]!["original"]!.GetValue<string>();
}
