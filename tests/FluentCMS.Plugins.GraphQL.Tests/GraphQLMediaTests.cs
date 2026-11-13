using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.GraphQL.Tests.BlogHost;

namespace FluentCMS.Plugins.GraphQL.Tests;

// The Blog host whose Public role reads articles, so that an anonymous caller reads them.
public sealed class PublicBlogHost : BlogHost
{
    public PublicBlogHost()
    {
        Host.Seed(async services =>
        {
            var database = services.GetRequiredService<CmsDbContext>();
            database.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = DefaultSite.PublicRole, Permission = "content.article.read" });
            await database.SaveChangesAsync();
        });
    }
}

// A caller without the management scope reads media on /graphql as the Delivery API shows it, by the media plugin's rule:
// a public asset of the site's library, and never a private or deleted one. A managing caller resolves by its folder grants.
// The rule itself, with an asset of another library and a site where the plugin is disabled, is the media plugin's tests'.
public sealed class GraphQLMediaTests(PublicBlogHost blog) : IClassFixture<PublicBlogHost>
{
    private const string Media = "/api/management/v1/media";
    private const string Articles = "/api/delivery/v1/article";
    private const string Read = "query($id: ID!) { article(id: $id, richText: HTML) { hero body sections { ... on HeroBlock { image } } } }";

    // A 16 by 9 PNG of one colour.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFElEQVR42mPQztlAEmIY1TAoNAAALmO38Z6fgBcAAAAASUVORK5CYII=");

    [Fact]
    public async Task AnAnonymousCallerAndADeliveryKeyGetThePublicMediaOfAnEntryAsRestGivesIt()
    {
        using var admin = await blog.AdminAsync();
        var hero = await UploadAsync(admin, "hero.png");
        var id = await ArticleAsync(admin, hero, [hero], hero);
        using var anonymous = blog.Anonymous();
        using var deliveryKey = await blog.KeyAsync(ApiKeyClass.Delivery, "content.article.read");
        using var withMediaRead = await blog.KeyAsync(ApiKeyClass.Delivery, "content.article.read", "media.read");
        var original = (await GraphQLQueryTests.RestAsync(anonymous, $"/api/delivery/v1/media/{hero:D}"))["urls"]!["original"]!.GetValue<string>();

        foreach (var client in new[] { anonymous, deliveryKey, withMediaRead })
        {
            var entry = (await client.QueryAsync(Read, new { id }))["article"]!;
            var rest = await GraphQLQueryTests.RestAsync(client, $"{Articles}/{id:D}?populate=hero,sections&richText=html");

            entry["hero"]!["urls"]!["original"]!.GetValue<string>().ShouldBe(original);
            JsonNode.DeepEquals(entry["hero"], rest["hero"]).ShouldBeTrue();
            JsonNode.DeepEquals(entry["sections"]![0]!["image"], rest["sections"]![0]!["image"]).ShouldBeTrue();
            entry["body"]!.GetValue<string>().ShouldBe(rest["body"]!.GetValue<string>());
            entry["body"]!.GetValue<string>().ShouldContain($"<img src=\"{original}\" alt=\"\" data-media-id=\"{hero:D}\">");
        }
    }

    // A preview token reads /graphql as it reads the Delivery API, so its draft shows the same media.
    [Fact]
    public async Task APreviewTokenGetsTheMediaOfItsDraftAsTheDeliveryApiGivesIt()
    {
        using var admin = await blog.AdminAsync();
        var hero = await UploadAsync(admin, "hero.png");
        var secret = await PrivateAsync(admin, "secret.png");
        var id = await ArticleAsync(admin, hero, [hero, secret], secret, publish: false);
        using var minted = await admin.PostAsync(new Uri($"/api/management/v1/content/article/{id:D}/preview-token", UriKind.Relative), null, Token);
        var answer = await minted.Content.ReadAsStringAsync(Token);
        minted.StatusCode.ShouldBe(HttpStatusCode.OK, answer);
        var token = JsonNode.Parse(answer)!["token"]!.GetValue<string>();
        using var preview = blog.Anonymous();
        preview.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Preview", token);

        var entry = (await preview.QueryAsync("query($id: ID!) { article(id: $id, status: DRAFT, richText: HTML) { hero body sections { ... on HeroBlock { image } } } }", new { id }))["article"]!;
        var rest = await GraphQLQueryTests.RestAsync(preview, $"{Articles}/{id:D}?status=draft&populate=hero,sections&richText=html");

        entry["hero"]!["urls"]!["original"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
        JsonNode.DeepEquals(entry["hero"], rest["hero"]).ShouldBeTrue();
        entry["sections"]![0]!["image"].ShouldBeNull();
        entry["body"]!.GetValue<string>().ShouldBe(rest["body"]!.GetValue<string>());
        entry["body"]!.GetValue<string>().ShouldContain($"<img src=\"{entry["hero"]!["urls"]!["original"]!.GetValue<string>()}\"");
        entry["body"]!.GetValue<string>().ShouldContain($"<img alt=\"\" data-media-id=\"{secret:D}\">");
    }

    [Fact]
    public async Task APrivateOrADeletedAssetNeverResolvesForThem()
    {
        using var admin = await blog.AdminAsync();
        var secret = await PrivateAsync(admin, "secret.png");
        var gone = await UploadAsync(admin, "gone.png");
        var id = await ArticleAsync(admin, secret, [secret, gone], gone);
        using (var deleted = await admin.DeleteAsync(new Uri($"{Media}/{gone:D}?force=true", UriKind.Relative), Token))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var anonymous = blog.Anonymous();
        using var withMediaRead = await blog.KeyAsync(ApiKeyClass.Delivery, "content.article.read", "media.read");

        foreach (var client in new[] { anonymous, withMediaRead })
        {
            var entry = (await client.QueryAsync(Read, new { id }))["article"]!;

            entry["hero"].ShouldBeNull();
            entry["sections"]![0]!["image"].ShouldBeNull();
            entry["body"]!.GetValue<string>().ShouldNotContain("src=");
        }
    }

    // On the same endpoint a managing caller sees what its media.read reaches: a private asset of its folder, without URLs,
    // and not a public one of another folder, which the anonymous caller sees.
    [Fact]
    public async Task AManagingCallerResolvesByItsFolderGrantsAsBefore()
    {
        using var admin = await blog.AdminAsync();
        var open = await FolderAsync(admin, "open");
        var closed = await FolderAsync(admin, "closed");
        var secret = await PrivateAsync(admin, "secret.png", open);
        var inOpen = await UploadAsync(admin, "open.png", open);
        var inClosed = await UploadAsync(admin, "closed.png", closed);
        var id = await ArticleAsync(admin, secret, [inOpen], inClosed);
        using var reader = await blog.KeyWithGrantsAsync(
            ApiKeyClass.Management,
            new { permission = "content.article.read" },
            new { permission = "media.read", constraints = new { resources = new[] { open.ToString("D") } } });
        using var withoutMediaRead = await blog.KeyAsync(ApiKeyClass.Management, "content.article.read");
        using var anonymous = blog.Anonymous();

        var managed = (await reader.QueryAsync(Read, new { id }))["article"]!;
        var withoutGrant = (await withoutMediaRead.QueryAsync(Read, new { id }))["article"]!;
        var anyone = (await anonymous.QueryAsync(Read, new { id }))["article"]!;

        managed["hero"]!["id"]!.GetValue<string>().ShouldBe(secret.ToString("D"));
        managed["hero"]!["urls"].ShouldBeNull();
        managed["sections"]![0]!["image"].ShouldBeNull();
        managed["body"]!.GetValue<string>().ShouldContain($"data-media-id=\"{inOpen:D}\"");
        managed["body"]!.GetValue<string>().ShouldContain("src=");

        withoutGrant["hero"].ShouldBeNull();
        withoutGrant["sections"]![0]!["image"].ShouldBeNull();
        withoutGrant["body"]!.GetValue<string>().ShouldNotContain("src=");

        anyone["hero"].ShouldBeNull();
        anyone["sections"]![0]!["image"]!["id"]!.GetValue<string>().ShouldBe(inClosed.ToString("D"));
        anyone["body"]!.GetValue<string>().ShouldContain("src=");
    }

    // An article by Ada with the asset as its hero, the assets as images of its body and a hero block of the banner, published
    // unless asked otherwise.
    private static async Task<Guid> ArticleAsync(HttpClient admin, Guid hero, Guid[] body, Guid banner, bool publish = true)
    {
        var author = (await admin.QueryAsync($$"""{ author(slug: "{{BlogSeeder.AdaSlug}}") { id } }"""))["author"]!["id"]!.GetValue<string>();
        var input = new
        {
            title = "Pictures",
            slug = "pictures-" + Guid.NewGuid().ToString("N")[..8],
            author,
            hero,
            body = new { type = "doc", content = body.Select(asset => new { type = "media", attrs = new { mediaId = asset } }).ToArray() },
            sections = new object[] { new { type = "hero", heading = "Banner", image = banner } },
        };
        var id = (await admin.QueryAsync("mutation($input: ArticleInput!) { createArticle(input: $input) { id } }", new { input }))["createArticle"]!["id"]!.GetValue<string>();
        if (publish)
        {
            await admin.QueryAsync("mutation($id: ID!) { publishArticle(id: $id) { entry { id } } }", new { id });
        }

        return Guid.Parse(id);
    }

    private static async Task<Guid> UploadAsync(HttpClient admin, string name, Guid? folder = null)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Png);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "files", name);
        if (folder is { } id)
        {
            form.Add(new StringContent(id.ToString("D")), "folderId");
        }

        using var response = await admin.PostAsync(new Uri(Media, UriKind.Relative), form, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return Guid.Parse(JsonNode.Parse(text)!["items"]![0]!["id"]!.GetValue<string>());
    }

    private static async Task<Guid> PrivateAsync(HttpClient admin, string name, Guid? folder = null)
    {
        var asset = await UploadAsync(admin, name, folder);
        using var changed = await admin.PatchAsJsonAsync(new Uri($"{Media}/{asset:D}", UriKind.Relative), new { isPrivate = true }, Token);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Token));
        return asset;
    }

    private static async Task<Guid> FolderAsync(HttpClient admin, string name)
    {
        using var response = await admin.PostAsJsonAsync(new Uri($"{Media}/folders", UriKind.Relative), new { name = $"{name}-{Guid.NewGuid():N}" }, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return Guid.Parse(JsonNode.Parse(text)!["id"]!.GetValue<string>());
    }
}
