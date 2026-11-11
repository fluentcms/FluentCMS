using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.MultiSite.Tests.MultiSiteTesting;

namespace FluentCMS.Plugins.MultiSite.Tests;

// What two sites share and what each keeps: the types and the media library are the instance's, a global type has one set
// of entries, and a site-scoped type and the roles are each site's.
public sealed class SharedContentTests(MultiSiteHost fixture) : IClassFixture<MultiSiteHost>
{
    // A 1 by 1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task TwoSitesReadTheSameGlobalTypeAndASiteScopedTypeHasSeparateEntriesInEach()
    {
        using var admin = await AdminAsync(fixture.Host);
        var (first, firstHost) = await SiteWithHostAsync(fixture.Host, admin);
        var (second, secondHost) = await SiteWithHostAsync(fixture.Host, admin);
        var author = fixture.Host.As(await fixture.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var name = "tag-" + Guid.NewGuid().ToString("N");
        var tags = author.In(new SiteId(first)).Content<Tag>();
        var tag = await tags.CreateAsync(new Tag { Name = name }, Token);
        await tags.PublishAsync(tag.Id, cancellationToken: Token);
        await PublishArticleAsync(fixture.Host, first, "Only in the first");
        await PublishArticleAsync(fixture.Host, second, "Only in the second");
        using var client = fixture.Host.CreateClient();

        (await TagAsync(client, firstHost, tag.Id)).ShouldBe(name);
        (await TagAsync(client, secondHost, tag.Id)).ShouldBe(name);
        (await ArticlesAsync(client, firstHost)).Titles.ShouldBe(["Only in the first"]);
        (await ArticlesAsync(client, secondHost)).Titles.ShouldBe(["Only in the second"]);
    }

    // The library is shared by default: an asset uploaded in one site is the same asset in the other.
    [Fact]
    public async Task TwoSitesReadTheSameAsset()
    {
        using var admin = await AdminAsync(fixture.Host);
        var (first, firstHost) = await SiteWithHostAsync(fixture.Host, admin);
        var (second, secondHost) = await SiteWithHostAsync(fixture.Host, admin);

        var asset = await UploadAsync(admin, firstHost);
        using var client = fixture.Host.CreateClient();
        var inFirst = await SendAsync(client, HttpMethod.Get, $"http://{firstHost}/api/delivery/v1/media/{asset}");
        var inSecond = await SendAsync(client, HttpMethod.Get, $"http://{secondHost}/api/delivery/v1/media/{asset}");
        var listed = await SendAsync(admin, HttpMethod.Get, $"http://{secondHost}/api/management/v1/media");

        inFirst!["id"]!.GetValue<string>().ShouldBe(asset);
        inSecond!.ToJsonString().ShouldBe(inFirst.ToJsonString());
        listed!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ShouldContain(asset);
        first.ShouldNotBe(second);
    }

    // The library is shared, the permission is not: each site's roles decide who uploads and who reads there.
    [Fact]
    public async Task ARoleOfOneSiteGrantsNothingInTheOther()
    {
        using var admin = await AdminAsync(fixture.Host);
        var (first, firstHost) = await SiteWithHostAsync(fixture.Host, admin);
        var (_, secondHost) = await SiteWithHostAsync(fixture.Host, admin);
        await PublishArticleAsync(fixture.Host, first, "Of the first");
        using var editor = await RoleKeyAsync(fixture.Host, admin, "management", first, null, "content.article.read", "media.read", "media.upload");

        var own = await SendAsync(editor, HttpMethod.Get, $"http://{firstHost}/api/management/v1/content/article");
        own!["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>()).ShouldBe(["Of the first"]);
        await SendAsync(editor, HttpMethod.Get, $"http://{secondHost}/api/management/v1/content/article", expected: HttpStatusCode.NotFound);

        var asset = await UploadAsync(editor, firstHost);
        (await SendAsync(editor, HttpMethod.Get, $"http://{firstHost}/api/management/v1/media/{asset}"))!["id"]!.GetValue<string>().ShouldBe(asset);
        await SendAsync(editor, HttpMethod.Get, $"http://{secondHost}/api/management/v1/media/{asset}", expected: HttpStatusCode.Forbidden);
        using var refused = await UploadResponseAsync(editor, secondHost);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<string> TagAsync(HttpClient client, string hostname, Guid id) =>
        (await SendAsync(client, HttpMethod.Get, $"http://{hostname}/api/delivery/v1/tag/{id}"))!["name"]!.GetValue<string>();

    private static async Task<string> UploadAsync(HttpClient client, string hostname)
    {
        using var response = await UploadResponseAsync(client, hostname);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!["items"]!.AsArray().Single()!["id"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> UploadResponseAsync(HttpClient client, string hostname)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "files", "pixel.png");
        return await client.PostAsync(new Uri($"http://{hostname}/api/management/v1/media"), form, Token);
    }
}
