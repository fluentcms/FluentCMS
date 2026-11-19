using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// A javascript: link over HTTP: the Management API refuses it with 422 at the href, and one stored before that rule
// reaches neither a Management nor a Delivery read.
public sealed class RichTextLinkTests : IAsyncLifetime
{
    private const string Safe = "https://example.com/safe";

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost().WithContentTypes<Bulletin>();
    private HttpClient _editor = null!;

    public async ValueTask InitializeAsync() => _editor = await _host.AsUserAsync(await _host.CreateUserAsync("Editor"));

    public async ValueTask DisposeAsync()
    {
        _editor.Dispose();
        await _host.DisposeAsync();
    }

    private static string Body(string href) =>
        new JsonObject
        {
            ["title"] = "T",
            ["body"] = JsonNode.Parse($$$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Click","marks":[{"type":"link","attrs":{"href":"{{{href}}}"}}]}]}]}"""),
        }.ToJsonString();

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData(" JavaScript:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    public async Task AnUnsafeHrefIsRefusedWith422AtItsPath(string href)
    {
        var refused = await ManagementFixture.SendAsync(_editor, HttpMethod.Post, "bulletin", Body(href));

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = refused.Json["errors"]!.AsArray().ShouldHaveSingleItem()!;
        error["path"]!.GetValue<string>().ShouldBe("body.content[0].content[0].marks[0].attrs.href");
        error["code"]!.GetValue<string>().ShouldBe("FCMS2114");
    }

    [Theory]
    [InlineData(Safe)]
    [InlineData("/about")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+15551234567")]
    public async Task ASafeHrefIsAccepted(string href)
    {
        var created = await ManagementFixture.SendAsync(_editor, HttpMethod.Post, "bulletin", Body(href));

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        created.Json["body"]!["content"]![0]!["content"]![0]!["marks"]![0]!["attrs"]!["href"]!.GetValue<string>().ShouldBe(href);
    }

    [Fact]
    public async Task AStoredUnsafeHrefReachesNoRead()
    {
        var created = await ManagementFixture.SendAsync(_editor, HttpMethod.Post, "bulletin", Body(Safe));
        var id = created.Json["id"]!.GetValue<string>();
        (await ManagementFixture.SendAsync(_editor, HttpMethod.Post, $"bulletin/{id}/publish")).Status.ShouldBe(HttpStatusCode.OK);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            foreach (var row in context.Rows("cms_bulletin").ToList())
            {
                row["body"] = ((string)row["body"]).Replace(Safe, "javascript:alert(1)", StringComparison.Ordinal);
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var delivery = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Delivery, "Editor", cancellationToken: TestContext.Current.CancellationToken));
        var management = await ManagementFixture.SendAsync(_editor, HttpMethod.Get, $"bulletin/{id}");
        using var response = await delivery.GetAsync(new Uri($"/api/delivery/v1/bulletin/{id}", UriKind.Relative), TestContext.Current.CancellationToken);
        var delivered = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        management.Status.ShouldBe(HttpStatusCode.OK);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        management.Json.ToJsonString().ShouldNotContain("javascript");
        delivered.ShouldNotContain("javascript");
        delivered.ShouldContain("Click");
    }
}
