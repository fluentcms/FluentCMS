using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

// The media field host whose Public role reads stories, so that an anonymous caller reads them on the Delivery API.
public sealed class PublicStoryHost : MediaHost
{
    public PublicStoryHost()
        : base(typeof(Story), typeof(BannerBlock))
    {
        Host.Seed(async services =>
        {
            var database = services.GetRequiredService<CmsDbContext>();
            database.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.story.read" });
            await database.SaveChangesAsync();
        });
    }
}

// On the Delivery API a media reference resolves to an asset that is public, not deleted and in the library of the site,
// whoever reads, with the URLs the media route gives it; a private one, a deleted one and one of another library never
// resolve there. The Management API resolves by the caller's folder grants, as before.
public sealed class MediaDeliveryReferenceTests(PublicStoryHost media) : IClassFixture<PublicStoryHost>
{
    private const string Stories = "/api/management/v1/content/story";
    private const string Delivery = "/api/delivery/v1/story";
    private const string Populated = "populate=hero,gallery,sections&richText=html";

    [Fact]
    public async Task APublishedEntryGivesItsPublicMediaWithTheUrlsOfTheMediaRouteToThePublicRoleAndDeliveryKeys()
    {
        using var admin = await media.AdminAsync();
        await PresetAsync(admin, "card");
        var hero = await AssetAsync(admin, "hero.png", Transforms.Bands(8, 4));
        var id = await PublishedAsync(admin, Story(hero, [hero], [hero], banner: hero));
        using var anonymous = media.Host.CreateClient();
        using var deliveryKey = await DeliveryKeyAsync(admin, new { permission = "content.story.read" });
        using var withMediaRead = await DeliveryKeyAsync(admin, new { permission = "content.story.read" }, new { permission = "media.read" });
        var urls = (await GetAsync(anonymous, $"/api/delivery/v1/media/{hero:D}"))["urls"]!;
        var original = urls["original"]!.GetValue<string>();
        var card = urls["presets"]!["card"]!.GetValue<string>();

        foreach (var client in new[] { anonymous, deliveryKey, withMediaRead })
        {
            var entry = await GetAsync(client, $"{Delivery}/{id:D}?{Populated}");

            Urls(entry["hero"]).ShouldBe((original, card));
            entry["gallery"]!.AsArray().Select(item => Urls(item)).ShouldBe([(original, card)]);
            Urls(entry["sections"]![0]!["image"]).ShouldBe((original, card));
            entry["body"]!.GetValue<string>().ShouldContain($"<img src=\"{original}\" alt=\"\" data-media-id=\"{hero:D}\">");
        }

        (await GetAsync(anonymous, $"{Delivery}/{id:D}"))["hero"]!.AsObject().Select(pair => pair.Key).ShouldBe(["id", "type"]);
    }

    [Fact]
    public async Task APreviewTokenResolvesTheMediaOfItsDraftByTheSameRule()
    {
        using var admin = await media.AdminAsync();
        var hero = await AssetAsync(admin, "hero.png");
        var secret = await PrivateAsync(admin, "secret.png");
        var id = Id(await CreateAsync(admin, Story(hero, [secret], [hero, secret])));
        using var minted = await admin.PostAsync(new Uri($"{Stories}/{id:D}/preview-token", UriKind.Relative), null, Token);
        using var preview = media.Host.CreateClient();
        preview.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Preview", (await JsonAsync(minted))["token"]!.GetValue<string>());
        using var anonymous = media.Host.CreateClient();
        var original = (await GetAsync(anonymous, $"/api/delivery/v1/media/{hero:D}"))["urls"]!["original"]!.GetValue<string>();

        var draft = await GetAsync(preview, $"{Delivery}/{id:D}?status=draft&{Populated}");

        Urls(draft["hero"]).Original.ShouldBe(original);
        draft["gallery"]!.AsArray().ShouldBeEmpty();
        var body = draft["body"]!.GetValue<string>();
        body.ShouldContain($"<img src=\"{original}\" alt=\"\" data-media-id=\"{hero:D}\">");
        body.ShouldContain($"<img alt=\"\" data-media-id=\"{secret:D}\">");
    }

    // A Delivery key whose role holds media.read and a managing key read the Delivery API as anyone does; the Management API
    // still gives the private asset, without URLs, to a caller whose media.read reaches it.
    [Fact]
    public async Task APrivateADeletedAndAnotherLibrarysAssetNeverResolveOnDelivery()
    {
        using var admin = await media.AdminAsync();
        var secret = await PrivateAsync(admin, "secret.png");
        var gone = await AssetAsync(admin, "gone.png");
        var elsewhere = await AssetAsync(admin, "elsewhere.png");
        var id = await PublishedAsync(admin, Story(secret, [secret, elsewhere], [secret, gone, elsewhere], banner: gone));
        using (var deleted = await admin.DeleteAsync(new Uri($"{Root}/{gone:D}?force=true", UriKind.Relative), Token))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await MoveToAnotherLibraryAsync(elsewhere, await MediaLibraryTesting.CreateSiteAsync(media));
        using var anonymous = media.Host.CreateClient();
        using var withMediaRead = await DeliveryKeyAsync(admin, new { permission = "content.story.read" }, new { permission = "media.read" });

        foreach (var client in new[] { anonymous, withMediaRead, admin })
        {
            var entry = await GetAsync(client, $"{Delivery}/{id:D}?{Populated}");

            entry["hero"].ShouldBeNull();
            entry["gallery"]!.AsArray().ShouldBeEmpty();
            entry["sections"]![0]!["image"].ShouldBeNull();
            entry["body"]!.GetValue<string>().ShouldNotContain("src=");
        }

        var managed = await GetAsync(admin, $"{Stories}/{id:D}?{Populated}");
        Id(managed["hero"]!).ShouldBe(secret);
        managed["hero"]!["urls"].ShouldBeNull();
        managed["gallery"]!.AsArray().Select(item => Id(item!)).ShouldBe([secret]);
        managed["sections"]![0]!["image"].ShouldBeNull();
        managed["body"]!.GetValue<string>().ShouldNotContain("src=");
    }

    // The Delivery copy of an entry carries the tags of the media route's copy, so that an asset made private, or deleted,
    // leaves it when the outbox runs, as it leaves the media route.
    [Fact]
    public async Task ACachedReadThatShowsAnAssetIsEvictedWhenTheAssetTurnsPrivateOrIsDeleted()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var hidden = await AssetAsync(admin, "hidden.png");
        var gone = await AssetAsync(admin, "gone.png");
        var first = await PublishedAsync(admin, Story(hidden, body: [hidden]));
        var second = await PublishedAsync(admin, Story(null, body: [gone]));

        // What is pending would evict by the type and the site, and so would hide whether the media tags do.
        await media.DrainOutboxAsync();
        var shown = await GetAsync(anonymous, $"{Delivery}/{first:D}?{Populated}");
        (await GetAsync(anonymous, $"{Delivery}/{second:D}?{Populated}"))["body"]!.GetValue<string>().ShouldContain("src=");
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{hidden:D}", UriKind.Relative), new { isPrivate = true }, Token)).Dispose();
        (await admin.DeleteAsync(new Uri($"{Root}/{gone:D}?force=true", UriKind.Relative), Token)).Dispose();
        var cached = await GetAsync(anonymous, $"{Delivery}/{first:D}?{Populated}");
        await media.DrainOutboxAsync();
        var evicted = await GetAsync(anonymous, $"{Delivery}/{first:D}?{Populated}");
        var withoutGone = await GetAsync(anonymous, $"{Delivery}/{second:D}?{Populated}");

        shown["hero"]!["urls"].ShouldNotBeNull();
        JsonNode.DeepEquals(cached, shown).ShouldBeTrue();
        evicted["hero"].ShouldBeNull();
        evicted["body"]!.GetValue<string>().ShouldNotContain("src=");
        withoutGone["body"]!.GetValue<string>().ShouldNotContain("src=");
    }

    [Fact]
    public async Task TheTagHeaderOfAReadThatShowsAnAssetNamesTheTagsOfTheMediaRoute()
    {
        await using var tagged = new PublicStoryHost();
        tagged.Host.Configure(cms => cms.DeliveryCache.CacheTagHeader = "Cache-Tag");
        using var admin = await tagged.AdminAsync();
        using var anonymous = tagged.Host.CreateClient();
        var hero = await AssetAsync(admin, "hero.png");
        var inBody = await AssetAsync(admin, "body.png");
        var id = await PublishedAsync(admin, Story(hero, body: [inBody]));

        using var response = await anonymous.GetAsync(new Uri($"{Delivery}/{id:D}?{Populated}", UriKind.Relative), Token);
        var sent = response.Headers.GetValues("Cache-Tag").ShouldHaveSingleItem().Split(',');
        using var plain = await anonymous.GetAsync(new Uri($"{Delivery}/{id:D}", UriKind.Relative), Token);

        sent.ShouldContain($"media:{hero:D}");
        sent.ShouldContain($"media:{inBody:D}");
        sent.ShouldContain($"media-presets:{DefaultSite.Id:D}");
        sent.ShouldContain($"entry:{id:D}");
        plain.Headers.GetValues("Cache-Tag").ShouldHaveSingleItem().ShouldNotContain("media");
    }

    private static (string Original, string? Card) Urls(JsonNode? asset) =>
        (asset!["urls"]!["original"]!.GetValue<string>(), asset["urls"]!["presets"]!["card"]?.GetValue<string>());

    private static async Task<Guid> AssetAsync(HttpClient admin, string name, byte[]? bytes = null) =>
        Id((await UploadedAsync(admin, null, (name, bytes ?? Files.Png(4, 2)))).Single()!);

    private static async Task<Guid> PrivateAsync(HttpClient admin, string name)
    {
        var asset = await AssetAsync(admin, name);
        using var changed = await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Token));
        return asset;
    }

    private static async Task PresetAsync(HttpClient admin, string name)
    {
        using var preset = await admin.PutAsJsonAsync(new Uri($"{Root}/presets/{name}", UriKind.Relative), new { width = 4 }, Token);
        preset.StatusCode.ShouldBe(HttpStatusCode.OK, await preset.Content.ReadAsStringAsync(Token));
    }

    private async Task<HttpClient> DeliveryKeyAsync(HttpClient admin, params object[] grants)
    {
        var name = "role-" + Guid.NewGuid().ToString("N");
        using var role = await admin.PostAsJsonAsync(new Uri("/api/management/v1/roles", UriKind.Relative), new { name, grants }, Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        return media.Host.AsKey(await media.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, name, cancellationToken: Token));
    }

    // The row of an asset of another site's library, as a site keeps while the library is per site (5.1).
    private async Task MoveToAnotherLibraryAsync(Guid asset, Guid site)
    {
        await using var scope = media.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>()
            .Where(item => item.Id == asset)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.SiteId, site), Token);
    }

    private static async Task<Guid> PublishedAsync(HttpClient admin, JsonObject story)
    {
        var id = Id(await CreateAsync(admin, story));
        using var published = await admin.PostAsync(new Uri($"{Stories}/{id:D}/publish", UriKind.Relative), null, Token);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
        return id;
    }

    private static async Task<JsonNode> CreateAsync(HttpClient admin, JsonObject story)
    {
        using var response = await admin.PostAsync(new Uri(Stories, UriKind.Relative), new StringContent(story.ToJsonString(), Encoding.UTF8, "application/json"), Token);
        return await JsonAsync(response, HttpStatusCode.Created);
    }

    private static JsonObject Story(Guid? hero, Guid[]? gallery = null, Guid[]? body = null, Guid? banner = null)
    {
        JsonObject story = new() { ["title"] = "Story", ["hero"] = hero?.ToString("D") };
        if (gallery is not null)
        {
            story["gallery"] = new JsonArray([.. gallery.Select(id => (JsonNode)id.ToString("D"))]);
        }

        if (body is not null)
        {
            story["body"] = new JsonObject
            {
                ["type"] = "doc",
                ["content"] = new JsonArray([.. body.Select(id => (JsonNode)new JsonObject { ["type"] = "media", ["attrs"] = new JsonObject { ["mediaId"] = id.ToString("D") } })]),
            };
        }

        if (banner is { } image)
        {
            story["sections"] = new JsonArray(new JsonObject { ["type"] = "banner", ["heading"] = "Banner", ["image"] = image.ToString("D") });
        }

        return story;
    }
}
