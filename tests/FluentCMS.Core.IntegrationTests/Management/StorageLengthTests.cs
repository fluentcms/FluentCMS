using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// SQLite stores a value longer than its column, so this is the only check that keeps such a value from PostgreSQL and SQL
// Server, which would fail the write with a 500.
public sealed class StorageLengthTests : IAsyncLifetime
{
    private const string Link = "https://example.com/";

    private readonly FluentCmsTestHost _host = new FluentCmsTestHost().WithContentTypes<Bookmark>();
    private HttpClient _editor = null!;

    public async ValueTask InitializeAsync() => _editor = await _host.AsUserAsync(await _host.CreateUserAsync("Editor"));

    public async ValueTask DisposeAsync()
    {
        _editor.Dispose();
        await _host.DisposeAsync();
    }

    private Task<ApiResponse> CreateAsync(string slug, string link) =>
        ManagementFixture.SendAsync(_editor, HttpMethod.Post, "bookmark", new JsonObject { ["slug"] = slug, ["link"] = link }.ToJsonString());

    [Fact]
    public async Task ValuesUpToTheStorageDefaultsAreSaved()
    {
        var created = await CreateAsync(new string('a', 200), Link + new string('a', 2048 - Link.Length));

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
    }

    [Fact]
    public async Task ALongerValueFailsValidationBeforeTheDatabaseSeesIt()
    {
        var refused = await CreateAsync(new string('a', 201), Link + new string('a', 2049 - Link.Length));

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS2103");
        refused.Json["errors"]!.AsArray().Select(error => $"{error!["path"]}|{error["code"]}").ShouldBe(["slug|FCMS2103", "link|FCMS2103"], ignoreOrder: true);
    }
}
