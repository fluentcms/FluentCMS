using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// A javascript: link over HTTP: the Management API refuses it with 422 at the href, and one stored before that rule
// reaches no read, through the fallback of another locale or a populated relation as well, nor makes a write fail.
public sealed class RichTextLinkTests : IAsyncLifetime
{
    private const string Safe = "https://example.com/safe";
    private const string Script = "javascript:alert(1)";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost().WithContentTypes<Bulletin>();
    private HttpClient _editor = null!;
    private HttpClient _delivery = null!;

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SiteManager>().EnsureLocaleAsync(DefaultSite.Id, "fr", Token);
        }

        _editor = await _host.AsUserAsync(await _host.CreateUserAsync("Editor"));
        _delivery = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, "Editor", cancellationToken: Token));
    }

    public async ValueTask DisposeAsync()
    {
        _editor.Dispose();
        _delivery.Dispose();
        await _host.DisposeAsync();
    }

    private static string Document(string href) =>
        $$$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Click","marks":[{"type":"link","attrs":{"href":"{{{href}}}"}}]}]}]}""";

    private static string Body(string href, string? related = null) =>
        new JsonObject { ["title"] = "T", ["body"] = JsonNode.Parse(Document(href)), ["related"] = related }.ToJsonString();

    private Task<ApiResponse> SendAsync(HttpMethod method, string path, string? json = null, string? ifMatch = null) =>
        ManagementFixture.SendAsync(_editor, method, path, json, ifMatch);

    private async Task<string> DeliverAsync(string path)
    {
        using var response = await _delivery.GetAsync(new Uri("/api/delivery/v1/" + path, UriKind.Relative), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return text;
    }

    private async Task<string> CreatePublishedAsync(string? related = null)
    {
        var created = await SendAsync(HttpMethod.Post, "bulletin", Body(Safe, related));
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        (await SendAsync(HttpMethod.Post, $"bulletin/{id}/publish")).Status.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    // Puts javascript: in place of the safe href in every row, as data stored before the rule would have it.
    private async Task StoreUnsafeAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        foreach (var row in context.Rows("cms_bulletin_i18n").ToList())
        {
            if (row["body"] is string body)
            {
                row["body"] = body.Replace(Safe, Script, StringComparison.Ordinal);
            }
        }

        await context.SaveChangesAsync(Token);
    }

    [Theory]
    [InlineData(Script)]
    [InlineData(" JavaScript:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    public async Task AnUnsafeHrefIsRefusedWith422AtItsPath(string href)
    {
        var refused = await SendAsync(HttpMethod.Post, "bulletin", Body(href));

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = refused.Json["errors"]!.AsArray().ShouldHaveSingleItem()!;
        error["path"]!.GetValue<string>().ShouldBe("body.content[0].content[0].marks[0].attrs.href");
        error["code"]!.GetValue<string>().ShouldBe("FCMS2114");
    }

    [Fact]
    public async Task APatchWithAnUnsafeHrefIsRefusedWith422()
    {
        var id = (await SendAsync(HttpMethod.Post, "bulletin", Body(Safe))).Json["id"]!.GetValue<string>();

        var refused = await SendAsync(HttpMethod.Patch, $"bulletin/{id}", $$"""{ "body": {{Document(Script)}} }""", "\"1\"");

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("body.content[0].content[0].marks[0].attrs.href");
    }

    [Theory]
    [InlineData(Safe)]
    [InlineData("/about")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+15551234567")]
    public async Task ASafeHrefIsAccepted(string href)
    {
        var created = await SendAsync(HttpMethod.Post, "bulletin", Body(href));

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        created.Json["body"]!["content"]![0]!["content"]![0]!["marks"]![0]!["attrs"]!["href"]!.GetValue<string>().ShouldBe(href);
    }

    [Fact]
    public async Task AStoredUnsafeHrefReachesNoRead()
    {
        var id = await CreatePublishedAsync();
        var linking = await CreatePublishedAsync(related: id);

        // A French row with no body of its own, which falls back to the English one.
        (await SendAsync(HttpMethod.Patch, $"bulletin/{id}?locale=fr", """{ "title": "Un" }""", "\"0\"")).Status.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"bulletin/{id}/publish?locale=fr")).Status.ShouldBe(HttpStatusCode.OK);
        await StoreUnsafeAsync();

        var reads = new List<string>
        {
            (await SendAsync(HttpMethod.Get, $"bulletin/{id}")).Json.ToJsonString(),
            (await SendAsync(HttpMethod.Get, $"bulletin/{id}?locale=fr")).Json.ToJsonString(),
            (await SendAsync(HttpMethod.Get, $"bulletin/{linking}?populate=related")).Json.ToJsonString(),
            await DeliverAsync($"bulletin/{id}"),
            await DeliverAsync($"bulletin/{id}?locale=fr"),
            await DeliverAsync($"bulletin/{linking}?populate=related"),
            await DeliverAsync("bulletin?populate=related"),
        };

        reads.ShouldAllBe(read => !read.Contains("javascript") && read.Contains("Click"));
    }

    [Fact]
    public async Task APatchOfAnotherFieldIsNotRefusedByAStoredUnsafeHref()
    {
        var id = await CreatePublishedAsync();
        await StoreUnsafeAsync();

        var patched = await SendAsync(HttpMethod.Patch, $"bulletin/{id}", """{ "title": "Again" }""", "\"1\"");

        patched.Status.ShouldBe(HttpStatusCode.OK, patched.Body?.ToJsonString());
        patched.Json.ToJsonString().ShouldNotContain("javascript");
        (await SendAsync(HttpMethod.Post, $"bulletin/{id}/publish")).Status.ShouldBe(HttpStatusCode.OK);
    }
}
