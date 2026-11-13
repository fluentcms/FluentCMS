using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.IntegrationTests.Blog;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using FluentCMS.Samples.Blog;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// The transfer endpoints over HTTP: the Blog sample exported from one host and imported into an empty one.
public sealed class TransferEndpointsTests : IAsyncLifetime
{
    private const string Export = "/api/management/v1/transfer/export";
    private const string Import = "/api/management/v1/transfer/import";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _source = BlogFixture.NewHost();
    private readonly FluentCmsTestHost _target = EmptyHost();

    private HttpClient _sourceAdmin = null!;
    private HttpClient _targetAdmin = null!;

    public async ValueTask InitializeAsync()
    {
        _sourceAdmin = _source.AsKey(await _source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        _targetAdmin = _target.AsKey(await _target.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        await AllowLocalesAsync(_target, _targetAdmin);
    }

    // The types and settings of the Blog sample, without its content.
    private static FluentCmsTestHost EmptyHost() => new FluentCmsTestHost()
        .WithContentTypes([.. BlogSample.ContentTypes])
        .WithMediaDirectory()
        .Configure(cms => cms.Settings.Add<BlogSettings>());

    // The locales of the Blog sample, which its seeder adds to the default site.
    private async Task AllowLocalesAsync(FluentCmsTestHost host, HttpClient admin)
    {
        foreach (var code in Locales(_source).Except(Locales(host)))
        {
            using var response = await admin.PostAsync(new Uri($"/api/management/v1/sites/{DefaultSite.Id:D}/locales", UriKind.Relative), new StringContent($$"""{"code":"{{code}}"}""", Encoding.UTF8, "application/json"), Token);
            response.EnsureSuccessStatusCode();
        }
    }

    private static List<string> Locales(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<FluentCMS.Core.Storage.Entities.SiteLocale>().AsNoTracking().Where(locale => locale.SiteId == DefaultSite.Id).OrderBy(locale => locale.Position).Select(locale => locale.Code)];
    }

    public async ValueTask DisposeAsync()
    {
        _sourceAdmin.Dispose();
        _targetAdmin.Dispose();
        await _source.DisposeAsync();
        await _target.DisposeAsync();
    }

    [Fact]
    public async Task TheBlogSampleImportedIntoAnEmptyInstanceGivesTheSameResponsesForEveryEntryLocaleAndState()
    {
        var package = await ExportAsync(_sourceAdmin, """{"options":{"mediaFiles":"true"}}""");

        var lines = await ImportAsync(_targetAdmin, package);

        var report = lines[^1]["report"]!;
        report["total"]!["errors"]!.GetValue<int>().ShouldBe(0, lines[^1].ToJsonString());
        report["total"]!["created"]!.GetValue<int>().ShouldBeGreaterThan(0);
        lines.Take(lines.Count - 1).ShouldAllBe(line => line["report"] == null);

        var entries = Entries(_source);
        entries.ShouldNotBeEmpty();
        foreach (var (type, id, locale) in entries)
        {
            foreach (var path in new[]
            {
                $"/api/delivery/v1/{type}/{id:D}?locale={locale}",
                $"/api/management/v1/content/{type}/{id:D}?locale={locale}&status=draft",
            })
            {
                var expected = await ReadAsync(_sourceAdmin, path);
                var actual = await ReadAsync(_targetAdmin, path);
                actual.ShouldBe(expected, path);
            }
        }
    }

    [Fact]
    public async Task AnAssetOfThePackageIsReadableInTheTargetAndAMissingFileIsReported()
    {
        var package = await ExportAsync(_sourceAdmin, """{"options":{"mediaFiles":"true"}}""");
        var withFiles = await ImportAsync(_targetAdmin, package);
        var assets = AssetIds(package);
        assets.ShouldNotBeEmpty();
        withFiles[^1]["report"]!["sections"]!["media"]!["created"]!.GetValue<int>().ShouldBe(assets.Count);
        foreach (var asset in assets)
        {
            using var response = await _targetAdmin.GetAsync(new Uri($"/api/management/v1/media/{asset}", UriKind.Relative), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using var other = EmptyHost();
        using var otherAdmin = other.AsKey(await other.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        await AllowLocalesAsync(other, otherAdmin);
        var withoutFiles = await ImportAsync(otherAdmin, await ExportAsync(_sourceAdmin, "{}"));

        var missing = withoutFiles[^1]["report"]!["items"]!.AsArray().Where(item => item!["section"]!.GetValue<string>() == "media").ToList();
        missing.ShouldNotBeEmpty();
        missing.ShouldAllBe(item => item!["outcome"]!.GetValue<string>() == "error" && item["code"]!.GetValue<string>() == "FCMS3220");
    }

    [Fact]
    public async Task NoUserMemberOrApiKeyIsInAPackage()
    {
        var user = await _source.CreateUserAsync(BuiltInRoles.EditorName, "transfer-user-name", Token);
        var member = await _source.CreateMemberAsync("transfer-member@example.com", cancellationToken: Token);
        var key = await _source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, "transfer-key-name", Token);

        var package = await ExportAsync(_sourceAdmin, """{"roles":true,"options":{"mediaFiles":"true"}}""");

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName.Split('/')[0]).Distinct()
            .ShouldBe(["manifest.json", "schema.json", "content", "settings.json", "roles.json", "plugins"], ignoreOrder: true);
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.StartsWith("plugins/media/files/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            var text = await reader.ReadToEndAsync(Token);
            foreach (var secret in new[] { "transfer-user-name", user.Id.ToString("D"), "transfer-member@example.com", member.Id.ToString("D"), "transfer-key-name", key.Id.ToString("D"), key.Secret })
            {
                text.ShouldNotContain(secret, Case.Insensitive, entry.FullName);
            }
        }
    }

    [Fact]
    public async Task ACallerWithoutTransferManageInTheSiteIsRefused()
    {
        using var editor = _source.AsKey(await _source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token));

        using var export = await editor.PostAsync(new Uri(Export, UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        using var import = await editor.PostAsync(new Uri(Import, UriKind.Relative), Zip([1, 2, 3]), Token);

        export.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        import.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ASecondExportOfTheSiteWhileOneRunsIsRefusedWithItsCode()
    {
        await using var held = await _source.Services.GetRequiredService<IDistributedLock>().TryAcquireAsync($"transfer.export.{DefaultSite.Id:N}", TimeSpan.Zero, Token);

        using var response = await _sourceAdmin.PostAsync(new Uri(Export, UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("FCMS3205");
    }

    [Fact]
    public async Task ABodyThatIsNoPackageIsRefusedWithItsCode()
    {
        using var response = await _targetAdmin.PostAsync(new Uri(Import, UriKind.Relative), Zip([1, 2, 3]), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain("FCMS3200");
    }

    private static async Task<byte[]> ExportAsync(HttpClient client, string body)
    {
        using var response = await client.PostAsync(new Uri(Export, UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        return await response.Content.ReadAsByteArrayAsync(Token);
    }

    private static async Task<List<JsonNode>> ImportAsync(HttpClient client, byte[] package, string query = "")
    {
        using var response = await client.PostAsync(new Uri(Import + query, UriKind.Relative), Zip(package), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!)];
    }

    // The ids of the assets.jsonl lines of a package, as the report lists only the assets that were not imported.
    private static List<string> AssetIds(byte[] package)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("plugins/media/assets.jsonl")!.Open());
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!["id"]!.GetValue<string>())];
    }

    private static ByteArrayContent Zip(byte[] package)
    {
        var content = new ByteArrayContent(package);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    }

    private static async Task<string> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;

        // A problem names the request it answers.
        body.AsObject().Remove("traceId");
        return $"{(int)response.StatusCode} {body.ToJsonString()}";
    }

    private static List<(string Type, Guid Id, string Locale)> Entries(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<CmsDbContext>().Documents.AsNoTracking()
            .Select(document => new { document.Type, document.DocumentId, document.Locale })
            .AsEnumerable()
            .Select(document => (document.Type, document.DocumentId, document.Locale.Length == 0 ? DefaultSite.Locale : document.Locale))];
    }
}
