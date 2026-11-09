using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaFieldTests(StoryHost media) : IClassFixture<StoryHost>
{
    private const string Stories = "/api/management/v1/content/story";

    [Fact]
    public void WithoutThePluginAMediaFieldStopsTheHost()
    {
        var error = Should.Throw<SchemaValidationException>(() => new SchemaBuilder().AddContentType<Story>().AddContentType<BannerBlock>().Build());

        error.Errors.Select(item => item.Code).ShouldContain("FCMS1203");
    }

    [Fact]
    public void WithThePluginATypeWithASingleAndAListOfMediaStarts()
    {
        var story = media.Services.GetRequiredService<SchemaModel>().FindContentType("story").ShouldNotBeNull();

        (story.FindField("hero")!.FieldType, story.FindField("hero")!.Multiple).ShouldBe(("media", false));
        (story.FindField("gallery")!.FieldType, story.FindField("gallery")!.Multiple).ShouldBe(("media", true));
    }

    [Fact]
    public async Task AWriteStoresItsMediaAsReferencesAtEveryDepth()
    {
        using var admin = await media.AdminAsync();
        var hero = await AssetAsync(admin, "hero.png");
        var other = await AssetAsync(admin, "other.png");

        var id = Id(await CreateAsync(admin, Story(hero, [hero, other], body: Body(other), banner: other)));

        await using var scope = media.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<CmsDbContext>().References.AsNoTracking()
            .Where(reference => reference.SourceType == "story" && reference.SourceDocumentId == id)
            .OrderBy(reference => reference.Field).ThenBy(reference => reference.Position)
            .Select(reference => new { reference.Field, reference.Locale, reference.Position, reference.TargetType, reference.TargetId })
            .ToListAsync(Token);
        rows.Select(row => (row.Field, row.Locale, row.Position, row.TargetType, row.TargetId)).ShouldBe(
        [
            ("body", "", 0, "media", other),
            ("gallery", "", 0, "media", hero),
            ("gallery", "", 1, "media", other),
            ("hero", "", 0, "media", hero),
            ("sections", "", 0, "media", other),
        ]);
    }

    [Fact]
    public async Task AWriteRefusesAnAssetThatDoesNotExistOrIsDeleted()
    {
        using var admin = await media.AdminAsync();
        var deleted = await AssetAsync(admin, "gone.png");
        using (var removed = await admin.DeleteAsync(new Uri($"{Root}/{deleted:D}", UriKind.Relative), Token))
        {
            removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var unknown = Guid.CreateVersion7();

        using var response = await PostAsync(admin, Story(unknown, [deleted], body: Body(unknown), banner: deleted));

        var errors = (await JsonAsync(response, HttpStatusCode.UnprocessableEntity))["errors"]!.AsArray()
            .Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>())).ToList();
        errors.ShouldBe(
            [("hero", ValidationErrorCodes.UnknownReference), ("gallery", ValidationErrorCodes.UnknownReference), ("body", ValidationErrorCodes.UnknownReference), ("sections", ValidationErrorCodes.UnknownReference)],
            ignoreOrder: true);
    }

    [Fact]
    public async Task AnEntryWhoseAssetWasDeletedSinceCanStillBeSaved()
    {
        using var admin = await media.AdminAsync();
        var hero = await AssetAsync(admin, "hero.png");
        var created = await CreateAsync(admin, Story(hero, body: Body(hero), banner: hero));

        // A forced delete removes the rows; the block and the rich text keep the id in their JSON.
        using (var removed = await admin.DeleteAsync(new Uri($"{Root}/{hero:D}?force=true", UriKind.Relative), Token))
        {
            removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"{Stories}/{Id(created):D}", UriKind.Relative))
        {
            Content = new StringContent("""{ "title": "Renamed" }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var response = await admin.SendAsync(request, Token);

        (await JsonAsync(response))["title"]!.GetValue<string>().ShouldBe("Renamed");
    }

    [Fact]
    public async Task EqualAndInFiltersOnAMediaFieldFindTheEntries()
    {
        using var admin = await media.AdminAsync();
        var first = await AssetAsync(admin, "first.png");
        var second = await AssetAsync(admin, "second.png");
        var alpha = Id(await CreateAsync(admin, Story(first, title: "Alpha")));
        var beta = Id(await CreateAsync(admin, Story(second, [first], title: "Beta")));

        (await IdsAsync(admin, $"filter[hero][eq]={first:D}")).ShouldBe([alpha]);
        (await IdsAsync(admin, $"filter[hero][in]={first:D},{second:D}")).ShouldBe([alpha, beta], ignoreOrder: true);
        (await IdsAsync(admin, $"filter[gallery][eq]={first:D}")).ShouldBe([beta]);
        (await IdsAsync(admin, $"filter[hero][eq]={Guid.CreateVersion7():D}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task PopulateReturnsTheAssetInTheRequestLocaleWithUrlsThatServeIt()
    {
        await media.AddLocaleAsync("fr");
        using var admin = await media.AdminAsync();
        var hero = await AssetAsync(admin, "hero.png", Transforms.Bands(40, 20));
        await PatchAsync(admin, $"{Root}/{hero:D}?locale=en", new { title = "Harbour", alt = "Boats in a harbour", caption = "At dawn", focalPoint = new { x = 0.25, y = 0.5 } });
        await PatchAsync(admin, $"{Root}/{hero:D}?locale=fr", new { alt = "Bateaux dans un port" });
        using (var preset = await admin.PutAsJsonAsync(new Uri($"{Root}/presets/thumb", UriKind.Relative), new { width = 10 }, Token))
        {
            preset.StatusCode.ShouldBe(HttpStatusCode.OK, await preset.Content.ReadAsStringAsync(Token));
        }

        var id = Id(await CreateAsync(admin, Story(hero)));

        var plain = await GetAsync(admin, $"{Stories}/{id:D}");
        var english = (await GetAsync(admin, $"{Stories}/{id:D}?populate=hero&locale=en"))["hero"]!;
        var french = (await GetAsync(admin, $"{Stories}/{id:D}?populate=*&locale=fr"))["hero"]!;

        JsonNode.DeepEquals(plain["hero"], new JsonObject { ["id"] = hero.ToString("D"), ["type"] = "media" }).ShouldBeTrue(plain["hero"]?.ToJsonString());
        (english["id"]!.GetValue<string>(), english["type"]!.GetValue<string>(), english["fileName"]!.GetValue<string>(), english["mimeType"]!.GetValue<string>()).ShouldBe((hero.ToString("D"), "media", "hero.png", "image/png"));
        (english["width"]!.GetValue<int>(), english["height"]!.GetValue<int>(), english["title"]!.GetValue<string>()).ShouldBe((40, 20, "Harbour"));
        (english["alt"]!.GetValue<string>(), english["caption"]!.GetValue<string>()).ShouldBe(("Boats in a harbour", "At dawn"));
        (english["focalPoint"]!["x"]!.GetValue<double>(), english["focalPoint"]!["y"]!.GetValue<double>()).ShouldBe((0.25, 0.5));
        french["alt"]!.GetValue<string>().ShouldBe("Bateaux dans un port");
        english.AsObject().ContainsKey("createdBy").ShouldBeFalse();

        var url = english["urls"]!["original"]!.GetValue<string>();
        url.ShouldStartWith($"/media/{hero:D}/original/hero.png?sig=");
        var thumb = english["urls"]!["presets"]!["thumb"]!.GetValue<string>();
        thumb.ShouldContain("preset=thumb");
        using var image = await admin.GetAsync(new Uri(url, UriKind.Relative), Token);
        image.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var small = await admin.GetAsync(new Uri(thumb, UriKind.Relative), Token);
        small.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnAssetTheCallerCannotReadPopulatesAsNullAndAPrivateOneWithoutAUrl()
    {
        using var admin = await media.AdminAsync();
        var team = await FolderAsync(admin, "team");
        var inside = await AssetAsync(admin, "inside.png", folderId: team);
        var secret = await AssetAsync(admin, "secret.png", folderId: team);
        var outside = await AssetAsync(admin, "outside.png");
        await PatchAsync(admin, $"{Root}/{secret:D}", new { isPrivate = true });
        var id = Id(await CreateAsync(admin, Story(outside, [inside, outside, secret])));
        using var reader = await media.ClientWithAsync(
            admin,
            new { permission = "content.story.read" },
            new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } });
        using var noMedia = await media.ClientWithAsync(admin, new { permission = "content.story.read" });

        var entry = await GetAsync(reader, $"{Stories}/{id:D}?populate=hero,gallery");
        var unpopulated = await GetAsync(reader, $"{Stories}/{id:D}");
        var withoutGrant = await GetAsync(noMedia, $"{Stories}/{id:D}?populate=hero,gallery");

        entry["hero"].ShouldBeNull();
        var gallery = entry["gallery"]!.AsArray();
        gallery.Select(item => item!["id"]!.GetValue<string>()).ShouldBe([inside.ToString("D"), secret.ToString("D")]);
        gallery[0]!["urls"]!["original"].ShouldNotBeNull();
        gallery[1]!["urls"].ShouldBeNull();
        unpopulated["hero"]!["id"]!.GetValue<string>().ShouldBe(outside.ToString("D"));
        withoutGrant["hero"].ShouldBeNull();
        withoutGrant["gallery"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task RichTextMediaAndBlockImagesResolveThroughTheResolver()
    {
        using var admin = await media.AdminAsync();
        var image = await AssetAsync(admin, "photo.png");
        await PatchAsync(admin, $"{Root}/{image:D}", new { alt = "A <b> & \"quote\"" });
        var id = Id(await CreateAsync(admin, Story(null, body: Body(image, ownAlt: "Own alt"), banner: image)));

        var html = (await GetAsync(admin, $"{Stories}/{id:D}?richText=html"))["body"]!.GetValue<string>();
        var markdown = (await GetAsync(admin, $"{Stories}/{id:D}?richText=markdown"))["body"]!.GetValue<string>();
        var plain = await GetAsync(admin, $"{Stories}/{id:D}");
        var populated = await GetAsync(admin, $"{Stories}/{id:D}?populate=sections");
        var everything = await GetAsync(admin, $"{Stories}/{id:D}?populate=*");

        html.ShouldContain($"<img src=\"/media/{image:D}/original/photo.png?sig=");
        html.ShouldContain("alt=\"A &lt;b&gt; &amp; &quot;quote&quot;\"");
        html.ShouldContain("alt=\"Own alt\"");
        markdown.ShouldContain($"![A <b> & \"quote\"](/media/{image:D}/original/photo.png?sig=");
        JsonNode.DeepEquals(plain["sections"]![0]!["image"], new JsonObject { ["id"] = image.ToString("D"), ["type"] = "media" }).ShouldBeTrue();
        populated["sections"]![0]!["image"]!["urls"]!["original"]!.GetValue<string>().ShouldStartWith($"/media/{image:D}/original/photo.png?sig=");
        populated["sections"]![0]!["heading"]!.GetValue<string>().ShouldBe("Banner");
        everything["sections"]![0]!["image"]!["alt"]!.GetValue<string>().ShouldBe("A <b> & \"quote\"");
    }

    // The rows a media field writes are the ones usage lists and a delete counts.
    [Fact]
    public async Task AMediaFieldShowsInTheUsageOfItsAssetAndHoldsBackItsDelete()
    {
        using var admin = await media.AdminAsync();
        var hero = await AssetAsync(admin, "hero.png");
        var id = Id(await CreateAsync(admin, Story(hero, [hero], body: Body(hero), banner: hero)));
        using (var published = await admin.PostAsync(new Uri($"{Stories}/{id:D}/publish", UriKind.Relative), null, Token))
        {
            published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync(Token));
        }

        var usage = (await GetAsync(admin, $"{Root}/{hero:D}/usage"))["items"]!.AsArray()
            .Select(item => (item!["type"]!.GetValue<string>(), Id(item, "entryId"), item["status"]!.GetValue<string>(), item["locale"]?.GetValue<string>(), item["field"]!.GetValue<string>()))
            .ToList();
        using var refused = await admin.DeleteAsync(new Uri($"{Root}/{hero:D}", UriKind.Relative), Token);
        var problem = await JsonAsync(refused, HttpStatusCode.Conflict);

        usage.ShouldBe(
        [
            ("story", id, "draft", null, "body"),
            ("story", id, "draft", null, "gallery"),
            ("story", id, "draft", null, "hero"),
            ("story", id, "draft", null, "sections"),
            ("story", id, "published", null, "body"),
            ("story", id, "published", null, "gallery"),
            ("story", id, "published", null, "hero"),
            ("story", id, "published", null, "sections"),
        ],
        ignoreOrder: true);
        problem["code"]!.GetValue<string>().ShouldBe(MediaErrorCodes.AssetInUse);
    }

    [Fact]
    public async Task APopulatePathIntoABlockListGoesNoFurther()
    {
        using var admin = await media.AdminAsync();

        using var response = await admin.GetAsync(new Uri($"{Stories}?populate=sections.image", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Token));
    }

    private static async Task<Guid> AssetAsync(HttpClient admin, string name, byte[]? bytes = null, Guid? folderId = null) =>
        Id((await UploadedAsync(admin, folderId, (name, bytes ?? Files.Png(4, 2)))).Single()!);

    private static JsonObject Story(Guid? hero, Guid[]? gallery = null, JsonObject? body = null, Guid? banner = null, string title = "Story")
    {
        JsonObject story = new() { ["title"] = title, ["hero"] = hero?.ToString("D") };
        if (gallery is not null)
        {
            story["gallery"] = new JsonArray([.. gallery.Select(id => (JsonNode)id.ToString("D"))]);
        }

        if (body is not null)
        {
            story["body"] = body;
        }

        if (banner is { } image)
        {
            story["sections"] = new JsonArray(new JsonObject { ["type"] = "banner", ["heading"] = "Banner", ["image"] = image.ToString("D") });
        }

        return story;
    }

    private static JsonObject Body(Guid media, string? ownAlt = null)
    {
        JsonObject Node(string? alt)
        {
            JsonObject attributes = new() { ["mediaId"] = media.ToString("D") };
            if (alt is not null)
            {
                attributes["alt"] = alt;
            }

            return new JsonObject { ["type"] = "media", ["attrs"] = attributes };
        }

        JsonArray content = [new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Hello" }) }, Node(null)];
        if (ownAlt is not null)
        {
            content.Add(Node(ownAlt));
        }

        return new JsonObject { ["type"] = "doc", ["content"] = content };
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, JsonObject entry) =>
        client.PostAsync(new Uri(Stories, UriKind.Relative), new StringContent(entry.ToJsonString(), Encoding.UTF8, "application/json"), Token);

    private static async Task<JsonNode> CreateAsync(HttpClient client, JsonObject entry)
    {
        using var response = await PostAsync(client, entry);
        return await JsonAsync(response, HttpStatusCode.Created);
    }

    private static async Task<JsonNode> PatchAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PatchAsJsonAsync(new Uri(path, UriKind.Relative), body, Token);
        return await JsonAsync(response);
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string query) =>
        [.. (await GetAsync(client, $"{Stories}?{query}"))["items"]!.AsArray().Select(item => Id(item!))];
}
