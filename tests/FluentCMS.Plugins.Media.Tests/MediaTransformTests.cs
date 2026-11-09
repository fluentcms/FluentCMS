using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SkiaSharp;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaTransformTests(TransformHosts hosts) : IClassFixture<TransformHosts>
{
    [Fact]
    public async Task ASignedUrlReturnsTheImageResizedFittedAndEncodedAtTheQualityAndFormatAsked()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));

        async Task<(string? Type, byte[] Bytes)> Get(MediaImageOptions options)
        {
            using var response = await FetchAsync(anonymous, await UrlAsync(media, asset, options));
            return await ImageAsync(response);
        }

        var contain = await Get(new MediaImageOptions { Width = 100, Height = 100, Format = MediaImageFormat.Jpeg, Quality = 90 });
        var fill = await Get(new MediaImageOptions { Width = 100, Height = 100, Fit = ImageFit.Fill, Format = MediaImageFormat.Png });
        var cover = await Get(new MediaImageOptions { Width = 100, Height = 100, Fit = ImageFit.Cover, Format = MediaImageFormat.Webp });
        var wide = await Get(new MediaImageOptions { Width = 200 });
        var low = await Get(new MediaImageOptions { Width = 100, Height = 100, Format = MediaImageFormat.Jpeg, Quality = 5 });

        contain.Type.ShouldBe("image/jpeg");
        using (var image = Decode(contain.Bytes))
        {
            (image.Width, image.Height).ShouldBe((100, 50));
        }

        fill.Type.ShouldBe("image/png");
        using (var image = Decode(fill.Bytes))
        {
            (image.Width, image.Height).ShouldBe((100, 100));
        }

        cover.Type.ShouldBe("image/webp");
        using (var image = Decode(cover.Bytes))
        {
            (image.Width, image.Height).ShouldBe((100, 100));
        }

        wide.Type.ShouldBe("image/png");
        using (var image = Decode(wide.Bytes))
        {
            (image.Width, image.Height).ShouldBe((200, 100));
        }

        low.Bytes.Length.ShouldBeLessThan(contain.Bytes.Length);
    }

    [Fact]
    public async Task AUrlWithoutASignatureOrWithAWrongOneIsRefused()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var signed = await UrlAsync(media, asset, new MediaImageOptions { Width = 100 });

        using var unsigned = await FetchAsync(anonymous, $"/media/{asset.Id:D}/photo.png?w=100");
        using var tampered = await FetchAsync(anonymous, WithParameter(signed, "w", "300"));
        using var forged = await FetchAsync(anonymous, WithParameter(signed, "sig", "AAAA"));
        using var good = await FetchAsync(anonymous, signed);

        unsigned.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(unsigned)).ShouldBe(MediaErrorCodes.SignatureInvalid);
        tampered.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        forged.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        good.StatusCode.ShouldBe(HttpStatusCode.OK);
        good.Headers.CacheControl!.Public.ShouldBeTrue();
    }

    [Fact]
    public async Task UnsignedUrlsAreServedWhenSignaturesAreNotRequired()
    {
        var media = hosts.UnsignedUrls;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));

        using var response = await FetchAsync(anonymous, $"/media/{asset.Id:D}/any-name.png?w=40&h=20&fit=fill");

        using var image = Decode((await ImageAsync(response)).Bytes);
        (image.Width, image.Height).ShouldBe((40, 20));
    }

    [Fact]
    public async Task AutoPicksWebPFromAcceptAndOtherwiseTheOriginalTypeAndVariesByAccept()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 100, Format = MediaImageFormat.Auto });

        using var browser = await FetchAsync(anonymous, url, "image/avif,image/webp,image/apng,*/*;q=0.8");
        using var refused = await FetchAsync(anonymous, url, "image/webp;q=0, */*");
        using var plain = await FetchAsync(anonymous, url, "*/*");
        using var fixedType = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = 100, Format = MediaImageFormat.Webp }), "*/*");

        (await ImageAsync(browser)).Type.ShouldBe("image/webp");
        (await ImageAsync(refused)).Type.ShouldBe("image/png");
        (await ImageAsync(plain)).Type.ShouldBe("image/png");
        browser.Headers.Vary.ShouldContain("Accept");
        plain.Headers.Vary.ShouldContain("Accept");
        fixedType.Headers.Vary.ShouldNotContain("Accept");
    }

    [Fact]
    public async Task AutoPicksAvifWhereTheProcessorCanEncodeIt()
    {
        await using var media = RecordingHost(calls => calls.EncodesAvif = true);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 100, Format = MediaImageFormat.Auto });

        using var avif = await FetchAsync(anonymous, url, "image/avif,image/webp,*/*");
        using var webp = await FetchAsync(anonymous, url, "image/webp,*/*");

        var (type, bytes) = await ImageAsync(avif);
        (type, Encoding.ASCII.GetString(bytes)).ShouldBe(("image/avif", "AVIF"));
        (await ImageAsync(webp)).Type.ShouldBe("image/webp");
    }

    [Fact]
    public async Task TheDefaultProcessorRefusesAvifAndAutoNeverPicksIt()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));

        using var avif = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Format = MediaImageFormat.Avif }));
        using var auto = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Format = MediaImageFormat.Auto }), "image/avif");

        avif.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(avif)).ShouldBe(MediaErrorCodes.InvalidField);
        (await ImageAsync(auto)).Type.ShouldBe("image/png");
    }

    [Fact]
    public async Task ARepeatedRequestUnderAnyFileNameIsServedFromTheStoredVariant()
    {
        var media = hosts.Recording;
        var calls = CallsFrom(media);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 120, Format = MediaImageFormat.Webp });
        var renamed = url.Replace("/photo.png?", "/another-name.jpg?", StringComparison.Ordinal);
        renamed.ShouldNotBe(url);

        using var first = await FetchAsync(anonymous, url);
        var made = await ImageAsync(first);
        using var second = await FetchAsync(anonymous, renamed);
        var served = await ImageAsync(second);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var notModified = await anonymous.SendAsync(conditional, Token);

        (served.Type, Convert.ToHexString(served.Bytes)).ShouldBe((made.Type, Convert.ToHexString(made.Bytes)));
        second.Headers.ETag.ShouldBe(first.Headers.ETag);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        calls().Processed.ShouldBe(1);
        await using var scope = media.Services.CreateAsyncScope();
        var variant = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Variant>().AsNoTracking().SingleAsync(item => item.AssetId == asset.Id, Token);
        variant.AssetId.ShouldBe(asset.Id);
        variant.Size.ShouldBe(made.Bytes.LongLength);
        variant.StorageKey.ShouldStartWith($"{asset.SiteId:N}/{asset.Id:N}/");
        variant.StorageKey.ShouldEndWith(".webp");
        (await scope.ServiceProvider.GetRequiredService<IFileStorage>().ExistsAsync(variant.StorageKey, Token)).ShouldBeTrue();
    }

    [Fact]
    public async Task CoverKeepsTheFocalPointInTheCropAndFxAndFyOverrideIt()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 100));
        var square = new MediaImageOptions { Width = 100, Height = 100, Fit = ImageFit.Cover, Format = MediaImageFormat.Png };

        async Task<SKColor> CentreAsync(MediaImageOptions options)
        {
            using var response = await FetchAsync(anonymous, await UrlAsync(media, asset, options));
            using var image = Decode((await ImageAsync(response)).Bytes);
            (image.Width, image.Height).ShouldBe((100, 100));
            return image.GetPixel(50, 50);
        }

        using (var patch = await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), new { focalPoint = new { x = 0.875, y = 0.5 } }, Token))
        {
            patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await CentreAsync(square)).ShouldBe(Yellow);
        (await CentreAsync(square with { FocalPoint = new MediaFocalPoint(0.125, 0.5) })).ShouldBe(Red);
        (await CentreAsync(square with { FocalPoint = new MediaFocalPoint(0.625, 0.5) })).ShouldBe(Blue);
    }

    [Fact]
    public async Task APhotoIsTurnedAsItsExifSaysAndAGifBecomesAPng()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var turned = await UploadAsync(admin, TurnedJpeg(400, 100), "turned.jpg");
        var gif = await UploadAsync(admin, Gif(), "pixel.gif");

        using var photo = await FetchAsync(anonymous, await UrlAsync(media, turned, new MediaImageOptions { Width = 50, Format = MediaImageFormat.Png }));
        using var still = await FetchAsync(anonymous, await UrlAsync(media, gif, new MediaImageOptions { Width = 10 }));

        using (var image = Decode((await ImageAsync(photo)).Bytes))
        {
            (image.Width, image.Height).ShouldBe((50, 200));
            var top = image.GetPixel(25, 10);
            var bottom = image.GetPixel(25, 190);
            (top.Red > 200 && top.Green < 60 && top.Blue < 60).ShouldBeTrue(top.ToString());
            (bottom.Red > 200 && bottom.Green > 200 && bottom.Blue < 60).ShouldBeTrue(bottom.ToString());
        }

        var (type, bytes) = await ImageAsync(still);
        type.ShouldBe("image/png");
        using (var image = Decode(bytes))
        {
            (image.Width, image.Height).ShouldBe((10, 10));
        }
    }

    [Fact]
    public async Task ARequestOverTheLimitsIsRefusedBeforeTheImageIsRead()
    {
        var media = hosts.Recording;
        var calls = CallsFrom(media);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        await using var limits = await media.ConfigureAsync(settings =>
        {
            settings.MaxTransformWidth = 100;
            settings.MaxTransformHeight = 50;
            settings.MaxTransformOperations = 2;
        });

        using var wide = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = 101 }));
        using var tall = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Height = 51 }));
        using var busy = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = 50, Height = 50, Quality = 50 }));
        using var within = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = 100, Height = 50 }));

        foreach (var refused in (HttpResponseMessage[])[wide, tall, busy])
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await CodeAsync(refused)).ShouldBe(MediaErrorCodes.TransformLimit);
        }

        within.StatusCode.ShouldBe(HttpStatusCode.OK);
        calls().ShouldBe((1, 1));
    }

    [Fact]
    public async Task ASideThatFollowsTheAspectRatioIsHeldToTheLimitsBeforeTheImageIsDecoded()
    {
        var media = hosts.Recording;
        var calls = CallsFrom(media);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var tall = await UploadAsync(admin, Bands(20, 400));
        await using var limits = await media.ConfigureAsync(settings => settings.MaxTransformHeight = 1000);

        using var over = await FetchAsync(anonymous, await UrlAsync(media, tall, new MediaImageOptions { Width = 60 }));
        using var within = await FetchAsync(anonymous, await UrlAsync(media, tall, new MediaImageOptions { Width = 40 }));

        over.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(over)).ShouldBe(MediaErrorCodes.TransformLimit);
        using (var image = Decode((await ImageAsync(within)).Bytes))
        {
            (image.Width, image.Height).ShouldBe((40, 800));
        }

        calls().Processed.ShouldBe(1);
    }

    [Fact]
    public async Task AnImageOverTheSourcePixelsIsRefusedFromItsHeaderBeforeItIsDecoded()
    {
        var media = hosts.Recording;
        var calls = CallsFrom(media);
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        await using var limits = await media.ConfigureAsync(settings => settings.MaxSourcePixels = 400 * 200 - 1);
        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 10 });

        using var recorded = await FetchAsync(anonymous, url);
        await using (var scope = media.Services.CreateAsyncScope())
        {
            // An asset whose size was not read at upload is held to the header that the processor reads.
            await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>()
                .Where(item => item.Id == asset.Id)
                .ExecuteUpdateAsync(item => item.SetProperty(row => row.Width, (int?)null).SetProperty(row => row.Height, (int?)null), Token);
        }

        using var declared = await FetchAsync(anonymous, url);

        recorded.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        declared.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(declared)).ShouldBe(MediaErrorCodes.TransformLimit);
        calls().ShouldBe((1, 0));
    }

    [Fact]
    public async Task APrivateOrDeletedAssetIsNeverServedEvenWithAValidSignature()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var hidden = await UploadAsync(admin, Bands(40, 20));
        var deleted = await UploadAsync(admin, Bands(40, 20));
        var hiddenUrl = await UrlAsync(media, hidden);
        var deletedUrl = await UrlAsync(media, deleted);
        using (var before = await FetchAsync(anonymous, hiddenUrl))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var patch = await admin.PatchAsJsonAsync(new Uri($"{Root}/{hidden.Id:D}", UriKind.Relative), new { isPrivate = true }, Token))
        {
            patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var delete = await admin.DeleteAsync(new Uri($"{Root}/{deleted.Id:D}", UriKind.Relative), Token))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var privateResponse = await FetchAsync(anonymous, hiddenUrl);
        using var deletedResponse = await FetchAsync(anonymous, deletedUrl);

        privateResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(privateResponse)).ShouldBe(MediaErrorCodes.AssetNotFound);
        deletedResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AFileTheProcessorCannotReadIsRefused()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var pdf = await UploadAsync(admin, Files.Pdf(), "report.pdf");
        var broken = await UploadAsync(admin, Files.Png(32, 16), "broken.png");

        using var document = await FetchAsync(anonymous, await UrlAsync(media, pdf, new MediaImageOptions { Width = 10 }));
        using var headerOnly = await FetchAsync(anonymous, await UrlAsync(media, broken, new MediaImageOptions { Width = 10 }));

        document.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(document)).ShouldBe(MediaErrorCodes.NotTransformable);
        headerOnly.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(headerOnly)).ShouldBe(MediaErrorCodes.NotTransformable);
    }

    [Fact]
    public async Task AnUnknownRepeatedOrInvalidParameterIsRefused()
    {
        var media = hosts.UnsignedUrls;
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(40, 20));
        var path = $"/media/{asset.Id:D}/photo.png";

        foreach (var (query, field) in (IEnumerable<(string, string)>)[("width=10", "width"), ("w=10&W=20", "w"), ("w=0", "w"), ("w=ten", "w"), ("q=101", "q"), ("fit=crop", "fit"), ("f=tiff", "f"), ("fx=0.5", "fy"), ("fx=2&fy=0", "fx")])
        {
            using var response = await FetchAsync(anonymous, $"{path}?{query}");
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, query);
            var errors = (await JsonAsync(response, HttpStatusCode.UnprocessableEntity))["errors"]!.AsArray();
            errors.Select(error => (error!["path"]?.GetValue<string>(), error["code"]!.GetValue<string>())).ShouldContain((field, MediaErrorCodes.InvalidField), query);
        }

        using var site = await FetchAsync(anonymous, $"{path}?w=10&site={DefaultSiteId()}");
        site.StatusCode.ShouldNotBe(HttpStatusCode.UnprocessableEntity);
    }

    private static string DefaultSiteId() => Core.Sites.DefaultSite.Id.ToString("D");
}
