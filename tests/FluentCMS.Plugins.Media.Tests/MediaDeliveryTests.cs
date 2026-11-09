using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaDeliveryTests(MediaHost media) : IClassFixture<MediaHost>
{
    private const string Delivery = "/api/delivery/v1/media";

    [Fact]
    public async Task TheDeliveryRouteReturnsTheMetadataInTheRequestLocaleWithSignedUrlsAndTheCacheHeadersOfContent()
    {
        await media.AddLocaleAsync("fr");
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var bytes = Bands(40, 20);
        var asset = await UploadAsync(admin, bytes, "bands.png");
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}?locale=en", UriKind.Relative), new { title = "Bands", alt = "Four bands" }, Token)).Dispose();
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}?locale=fr", UriKind.Relative), new { alt = "Quatre bandes" }, Token)).Dispose();
        (await admin.PutAsJsonAsync(new Uri($"{Root}/presets/thumb", UriKind.Relative), new { width = 10 }, Token)).Dispose();

        using var response = await anonymous.GetAsync(new Uri($"{Delivery}/{asset.Id:D}?locale=fr", UriKind.Relative), Token);
        var body = await JsonAsync(response);
        var urls = body["urls"]!;
        using var original = await FetchAsync(anonymous, urls["original"]!.GetValue<string>());
        using var thumb = await FetchAsync(anonymous, urls["presets"]!["thumb"]!.GetValue<string>());
        using var again = new HttpRequestMessage(HttpMethod.Get, new Uri($"{Delivery}/{asset.Id:D}?locale=fr", UriKind.Relative));
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var notModified = await anonymous.SendAsync(again, Token);

        (body["locale"]!.GetValue<string>(), body["alt"]!.GetValue<string>(), body["title"]!.GetValue<string>()).ShouldBe(("fr", "Quatre bandes", "Bands"));
        (body["width"]!.GetValue<int>(), body["mimeType"]!.GetValue<string>()).ShouldBe((40, "image/png"));
        body.AsObject().Select(pair => pair.Key).ShouldNotContain("createdBy");
        body.AsObject().Select(pair => pair.Key).ShouldNotContain("siteId");
        urls["original"]!.GetValue<string>().ShouldStartWith($"/media/{asset.Id:D}/original/bands.png?sig=");
        urls["template"].ShouldBeNull();
        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60, stale-while-revalidate=300");
        response.Headers.Vary.ShouldContain("X-Cms-Locale");
        (await original.Content.ReadAsByteArrayAsync(Token)).ShouldBe(bytes);
        Decode((await ImageAsync(thumb)).Bytes).Width.ShouldBe(10);
        notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task AnUpdateOfTheAssetEvictsTheCachedResponseAndAPresetChangeEvictsItAtOnce()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(8, 8), "bands.png");

        var first = await GetAsync(anonymous, $"{Delivery}/{asset.Id:D}");
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), new { title = "Changed" }, Token)).Dispose();
        var cached = await GetAsync(anonymous, $"{Delivery}/{asset.Id:D}");
        await media.DrainOutboxAsync();
        var evicted = await GetAsync(anonymous, $"{Delivery}/{asset.Id:D}");
        (await admin.PutAsJsonAsync(new Uri($"{Root}/presets/hero", UriKind.Relative), new { width = 4 }, Token)).Dispose();
        var withPreset = await GetAsync(anonymous, $"{Delivery}/{asset.Id:D}");

        first["title"].ShouldBeNull();
        cached["title"].ShouldBeNull();
        evicted["title"]!.GetValue<string>().ShouldBe("Changed");
        evicted["urls"]!["presets"]!.AsObject().ContainsKey("hero").ShouldBeFalse();
        withPreset["urls"]!["presets"]!["hero"]!.GetValue<string>().ShouldContain("preset=hero");
    }

    // Only a managing principal holds a permission other than reading content (4.8), so an anonymous caller or a Delivery
    // key never reads a private asset, whatever its role grants.
    [Fact]
    public async Task WithTheOutputCacheOffNothingIsEvicted()
    {
        await using var media = new MediaHost();
        media.Host.WithServices(services => services.PostConfigure<DeliveryCacheControl>(options => options.OutputCache = false));
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, Bands(8, 8), "bands.png");

        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), new { title = "Changed" }, Token)).Dispose();
        (await admin.PutAsJsonAsync(new Uri($"{Root}/presets/hero", UriKind.Relative), new { width = 4 }, Token)).Dispose();
        await media.DrainOutboxAsync();

        (await GetAsync(admin, "/api/management/v1/activity?action=cache.invalidated"))["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task APrivateAssetIsHiddenFromACallerWhoCannotReadItsFolderAndHasNoUrls()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var team = await FolderAsync(admin, "team");
        var other = await FolderAsync(admin, "other");
        var asset = Id((await UploadedAsync(admin, team, ("plan.pdf", Files.Pdf()))).Single()!);
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        object teamGrant = new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } };
        using var member = await media.ClientWithAsync(admin, teamGrant);
        using var outsider = await media.ClientWithAsync(admin, new { permission = "media.read", constraints = new { resources = new[] { other.ToString("D") } } });
        using var deliveryKey = await DeliveryClientWithAsync(media, admin, teamGrant);

        using var hidden = await anonymous.GetAsync(new Uri($"{Delivery}/{asset:D}", UriKind.Relative), Token);
        using var refused = await outsider.GetAsync(new Uri($"{Delivery}/{asset:D}", UriKind.Relative), Token);
        using var byKey = await deliveryKey.GetAsync(new Uri($"{Delivery}/{asset:D}", UriKind.Relative), Token);
        using var shown = await member.GetAsync(new Uri($"{Delivery}/{asset:D}", UriKind.Relative), Token);
        var body = await JsonAsync(shown);

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(hidden)).ShouldBe(MediaErrorCodes.AssetNotFound);
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        byKey.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body["isPrivate"]!.GetValue<bool>().ShouldBeTrue();
        body["urls"].ShouldBeNull();
        shown.Headers.CacheControl!.Private.ShouldBeTrue();
    }

    [Fact]
    public async Task WhereUnsignedUrlsAreServedAnImageHasATemplate()
    {
        await using var media = new MediaHost();
        media.Host.WithServices(services => services.PostConfigure<MediaOptions>(options => options.RequireSignedUrls = false));
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var image = await UploadAsync(admin, Bands(8, 8), "bands.png");
        var pdf = Id((await UploadedAsync(admin, null, ("plan.pdf", Files.Pdf()))).Single()!);

        var template = (await GetAsync(anonymous, $"{Delivery}/{image.Id:D}"))["urls"]!["template"]!.GetValue<string>();
        using var filled = await FetchAsync(anonymous, template.Replace("{w}", "4", StringComparison.Ordinal).Replace("&h={h}&fit={fit}&q={q}&f={f}", "", StringComparison.Ordinal));

        template.ShouldBe($"/media/{image.Id:D}/bands.png?w={{w}}&h={{h}}&fit={{fit}}&q={{q}}&f={{f}}");
        Decode((await ImageAsync(filled)).Bytes).Width.ShouldBe(4);
        (await GetAsync(anonymous, $"{Delivery}/{pdf:D}"))["urls"]!["template"].ShouldBeNull();
    }

    private static async Task<HttpClient> DeliveryClientWithAsync(MediaHost media, HttpClient admin, params object[] grants)
    {
        var name = "role-" + Guid.NewGuid().ToString("N");
        using var role = await admin.PostAsJsonAsync(new Uri("/api/management/v1/roles", UriKind.Relative), new { name, grants }, Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        return media.Host.AsKey(await media.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, name, cancellationToken: Token));
    }
}
