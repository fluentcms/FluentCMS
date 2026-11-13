using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Cli;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.IntegrationTests.Blog;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Samples.Blog;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Commands;

// `fluentcms export` and `fluentcms import` between hosts of the Blog sample, inside the host as a project command runs and
// with --url against the transfer endpoints, through the tool's own command line.
public sealed class TransferCommandTests : IAsyncLifetime
{
    private const string Instance = "http://localhost";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _source = BlogFixture.NewHost();
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-transfer-commands-");

    private TestApiKey _sourceKey = null!;

    public async ValueTask InitializeAsync() =>
        _sourceKey = await _source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

    public async ValueTask DisposeAsync()
    {
        await _source.DisposeAsync();
        _directory.Delete(recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_directory.FullName, name);

    [Fact]
    public async Task ExportThenImportMoveTheBlogSampleInProjectModeAndWithTheUrlAndBothModesWriteTheSamePackage()
    {
        var inHost = PathOf("in-host.zip");
        var atUrl = PathOf("at-url.zip");

        var exported = await _source.RunCommandAsync("export", "--output", inHost, "--media-files");
        exported.ExitCode.ShouldBe(0, exported.Error);
        exported.Output.ShouldBe($"Wrote {inHost}{Environment.NewLine}");
        var (exitCode, output, error) = await ToolAsync(_source, _sourceKey, "export", "--output", atUrl, "--media-files");
        exitCode.ShouldBe(0, error);
        output.ShouldBe($"Wrote {atUrl}{Environment.NewLine}");
        await ShouldBeTheSamePackageAsync(inHost, atUrl);

        await using var projectTarget = await EmptyHostAsync();
        var imported = await projectTarget.Host.RunCommandAsync("import", inHost);
        imported.ExitCode.ShouldBe(0, imported.Error);
        imported.Output.ShouldContain("Total: ");
        imported.Error.ShouldContain("article: ");

        await using var urlTarget = await EmptyHostAsync();
        (exitCode, output, error) = await ToolAsync(urlTarget.Host, urlTarget.Key, "import", atUrl);
        exitCode.ShouldBe(0, error);
        output.ShouldBe(imported.Output);

        var entries = Entries(_source);
        entries.ShouldNotBeEmpty();
        using var source = _source.AsKey(_sourceKey);
        using var fromProject = projectTarget.Host.AsKey(projectTarget.Key);
        using var fromUrl = urlTarget.Host.AsKey(urlTarget.Key);
        foreach (var (type, id, locale) in entries)
        {
            var path = $"/api/management/v1/content/{type}/{id:D}?locale={locale}&status=draft";
            var expected = await ReadAsync(source, path);
            (await ReadAsync(fromProject, path)).ShouldBe(expected, path);
            (await ReadAsync(fromUrl, path)).ShouldBe(expected, path);
        }
    }

    [Fact]
    public async Task ADryRunChangesNothingAndExitsWithOneWhenTheReportHasAConflict()
    {
        var package = PathOf("package.zip");
        (await _source.RunCommandAsync("export", "--output", package, "--media-files")).ExitCode.ShouldBe(0);
        await using var target = await EmptyHostAsync();

        var dry = await target.Host.RunCommandAsync("import", package, "--dry-run");

        dry.ExitCode.ShouldBe(0, dry.Error);
        dry.Output.ShouldStartWith("Dry run: nothing was written.");
        Entries(target.Host).ShouldBeEmpty();

        (await target.Host.RunCommandAsync("import", package)).ExitCode.ShouldBe(0);
        var admin = target.Host.As(await target.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var author = (await admin.Content<Author>().QueryAsync(new ContentQuery { Status = QueryStatus.Draft }, Token)).Items[0];
        target.Host.Time.Advance(TimeSpan.FromHours(1));
        author.Name = "Changed in the target";
        await admin.Content<Author>().UpdateAsync(author.Id, author, author.Version, Token);
        var footprint = Footprint(target.Host);

        var inHost = await target.Host.RunCommandAsync("import", package, "--dry-run");
        var atUrl = await ToolAsync(target.Host, target.Key, "import", package, "--dry-run");

        foreach (var (exitCode, output, error) in new[] { (inHost.ExitCode, inHost.Output, inHost.Error), atUrl })
        {
            exitCode.ShouldBe(1);
            output.ShouldContain($"conflict author {author.Id:D}");
            error.ShouldContain("FCMS3250: the import reported 1 conflict and 0 errors; it was a dry run, so nothing was written.");
        }

        Footprint(target.Host).ShouldBe(footprint);
        (await admin.Content<Author>().GetAsync(author.Id, new GetOptions { Status = QueryStatus.Draft }, Token))!.Name.ShouldBe("Changed in the target");
    }

    [Fact]
    public async Task JsonPrintsTheReportOfTheEndpointInBothModes()
    {
        var package = PathOf("package.zip");
        (await _source.RunCommandAsync("export", "--output", package, "--media-files")).ExitCode.ShouldBe(0);
        await using var target = await EmptyHostAsync();
        using var client = target.Host.AsKey(target.Key);
        using var content = new ByteArrayContent(await File.ReadAllBytesAsync(package, Token));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await client.PostAsync(new Uri("/api/management/v1/transfer/import?dryRun=true", UriKind.Relative), content, Token);
        var endpoint = (await response.Content.ReadAsStringAsync(Token)).Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];

        var atUrl = await ToolAsync(target.Host, target.Key, "import", package, "--dry-run", "--json");
        var inHost = await target.Host.RunCommandAsync("import", package, "--dry-run", "--json");

        atUrl.ExitCode.ShouldBe(0, atUrl.Error);
        atUrl.Output.ShouldBe(endpoint + Environment.NewLine);
        inHost.Output.ShouldBe(endpoint + Environment.NewLine);
        JsonNode.Parse(endpoint)!["report"]!["dryRun"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task ARefusalOfTheEndpointKeepsItsCodeAndASiteThatDoesNotExistIsRefusedInBothModes()
    {
        var notAPackage = PathOf("not-a-package.zip");
        await File.WriteAllBytesAsync(notAPackage, [1, 2, 3], Token);

        var (exitCode, _, error) = await ToolAsync(_source, _sourceKey, "import", notAPackage);
        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS3200: ");
        (await _source.RunCommandAsync("import", notAPackage)).Error.ShouldStartWith("FCMS3200: ");

        (exitCode, _, error) = await ToolAsync(_source, _sourceKey, "export", "--output", PathOf("nowhere.zip"), "--site", "nowhere");
        exitCode.ShouldBe(1);
        error.ShouldStartWith("FCMS2301: ");
        (await _source.RunCommandAsync("export", "--output", PathOf("nowhere.zip"), "--site", "nowhere")).Error.ShouldStartWith("FCMS2301: ");
        File.Exists(PathOf("nowhere.zip")).ShouldBeFalse();

        var refused = await ToolAsync(_source, _sourceKey with { Secret = "not-a-key" }, "export", "--output", PathOf("refused.zip"));
        refused.ExitCode.ShouldBe(1);
        refused.Error.ShouldStartWith("FCMS3016: ");
        refused.Error.ShouldContain("check the key in --token or FLUENTCMS_TOKEN");
        refused.Error.ShouldNotContain("not-a-key");
    }

    // The tool run in this process, with the test server of the host as the instance of --url.
    private static async Task<(int ExitCode, string Output, string Error)> ToolAsync(FluentCmsTestHost host, TestApiKey key, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var handler = ((TestServer)host.Services.GetRequiredService<IServer>()).CreateHandler();
        var exitCode = await CommandLine.RunAsync([.. args, "--url", Instance, "--token", key.Secret], output, error, launcher: null, _ => new HttpClient(handler), Token);
        return (exitCode, output.ToString(), error.ToString());
    }

    // The types and settings of the Blog sample without its content, with the locales the sample's seeder adds.
    private async Task<Target> EmptyHostAsync()
    {
        var host = new FluentCmsTestHost()
            .WithContentTypes([.. BlogSample.ContentTypes])
            .WithMediaDirectory()
            .Configure(cms => cms.Settings.Add<BlogSettings>());
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var admin = host.AsKey(key);
        foreach (var code in Locales(_source).Except(Locales(host)))
        {
            using var response = await admin.PostAsync(new Uri($"/api/management/v1/sites/{DefaultSite.Id:D}/locales", UriKind.Relative), new StringContent($$"""{"code":"{{code}}"}""", Encoding.UTF8, "application/json"), Token);
            response.EnsureSuccessStatusCode();
        }

        return new Target(host, key);
    }

    private static async Task ShouldBeTheSamePackageAsync(string left, string right)
    {
        await using var a = await ZipFile.OpenReadAsync(left, Token);
        await using var b = await ZipFile.OpenReadAsync(right, Token);
        a.Entries.Select(entry => entry.FullName).ShouldBe(b.Entries.Select(entry => entry.FullName));
        foreach (var entry in a.Entries)
        {
            var mine = await BytesAsync(entry);
            var theirs = await BytesAsync(b.GetEntry(entry.FullName)!);
            if (entry.FullName == "manifest.json")
            {
                // Only the time it was made differs.
                var (x, y) = (JsonNode.Parse(mine)!.AsObject(), JsonNode.Parse(theirs)!.AsObject());
                x.Remove("createdAt");
                y.Remove("createdAt");
                x.ToJsonString().ShouldBe(y.ToJsonString());
            }
            else
            {
                mine.ShouldBe(theirs, entry.FullName);
            }
        }
    }

    private static async Task<byte[]> BytesAsync(ZipArchiveEntry entry)
    {
        await using var stream = await entry.OpenAsync(Token);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Token);
        return copy.ToArray();
    }

    private static async Task<string> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        body.AsObject().Remove("traceId");

        // A package carries no schedule (spec 4.14).
        body.AsObject().Remove("scheduledPublishAt");
        body.AsObject().Remove("scheduledUnpublishAt");
        return $"{(int)response.StatusCode} {body.ToJsonString()}";
    }

    private static List<string> Locales(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<FluentCMS.Core.Storage.Entities.SiteLocale>().AsNoTracking().Where(locale => locale.SiteId == DefaultSite.Id).OrderBy(locale => locale.Position).Select(locale => locale.Code)];
    }

    private static List<(string Type, Guid Id, string Locale)> Entries(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<CmsDbContext>().Documents.AsNoTracking()
            .Select(document => new { document.Type, document.DocumentId, document.Locale })
            .AsEnumerable()
            .Select(document => (document.Type, document.DocumentId, document.Locale.Length == 0 ? DefaultSite.Locale : document.Locale))];
    }

    // What a write would change: the rows of the content and their versions.
    private static string Footprint(FluentCmsTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var documents = context.Documents.IgnoreQueryFilters().AsNoTracking().AsEnumerable().OrderBy(document => document.DocumentId).ThenBy(document => document.Locale, StringComparer.Ordinal)
            .Select(document => $"{document.DocumentId} {document.Locale} {document.UpdatedAt.UtcTicks} {document.Status}").ToList();
        return $"{string.Join('\n', documents)} {context.Set<FluentCMS.Core.Storage.Entities.ContentVersion>().Count()}";
    }

    private sealed record Target(FluentCmsTestHost Host, TestApiKey Key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
