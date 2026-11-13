using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Transfer;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// A plugin's own ITransferContributor, the sample plugin's, writes its folder on export and reads it on import.
public sealed class PluginTransferTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePluginsContributorWritesItsFolderOnExportAndReadsItOnImport()
    {
        await using var source = new FluentCmsTestHost();
        await using var target = new FluentCmsTestHost();
        var note = new SampleNote { Id = Guid.CreateVersion7(), Text = "carried", CreatedAt = DateTimeOffset.UnixEpoch };
        await using (var scope = source.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>();
            database.Add(note);
            await database.SaveChangesAsync(Token);
        }

        using var sourceAdmin = source.AsKey(await source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        using var exported = await sourceAdmin.PostAsync(new Uri("/api/management/v1/transfer/export", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        exported.StatusCode.ShouldBe(HttpStatusCode.OK);
        var package = await exported.Content.ReadAsByteArrayAsync(Token);

        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            archive.GetEntry("plugins/sample/notes.jsonl").ShouldNotBeNull();
            var manifest = JsonNode.Parse(new StreamReader(archive.GetEntry("manifest.json")!.Open()).ReadToEnd())!;
            manifest["sections"]!.AsArray().Select(section => section!.GetValue<string>()).ShouldContain("plugins/sample/");
        }

        using var targetAdmin = target.AsKey(await target.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var dry = await ImportAsync(targetAdmin, package, "?dryRun=true");
        var real = await ImportAsync(targetAdmin, package, "");

        dry["sections"]!.ToJsonString().ShouldBe(real["sections"]!.ToJsonString());
        real["items"]!.AsArray().ShouldBeEmpty();
        real["sections"]![SampleTransfer.Section]!["created"]!.GetValue<int>().ShouldBe(1);
        await using (var scope = target.Services.CreateAsyncScope())
        {
            var notes = await scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().AsNoTracking().ToListAsync(Token);
            notes.ShouldHaveSingleItem().Text.ShouldBe("carried");
        }
    }

    // Core holds every line of a .jsonl file to the limit, a contributor's too, and the import stops at the first that is over.
    [Fact]
    public async Task ALineOfAContributorsFileOverTheLimitStopsTheImportWithTheError()
    {
        await using var source = new FluentCmsTestHost();
        await using var target = new FluentCmsTestHost().WithServices(services => services.Configure<TransferOptions>(options => options.MaxLineLength = 20));
        await using (var scope = source.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>();
            database.Add(new SampleNote { Id = Guid.CreateVersion7(), Text = "a note longer than the limit", CreatedAt = DateTimeOffset.UnixEpoch });
            await database.SaveChangesAsync(Token);
        }

        using var sourceAdmin = source.AsKey(await source.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        using var exported = await sourceAdmin.PostAsync(new Uri("/api/management/v1/transfer/export", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        var package = await exported.Content.ReadAsByteArrayAsync(Token);
        using var targetAdmin = target.AsKey(await target.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var dry = await ImportAsync(targetAdmin, package, "?dryRun=true");
        var real = await ImportAsync(targetAdmin, package, "");

        dry["items"]!.ToJsonString().ShouldBe(real["items"]!.ToJsonString());
        var stop = real["items"]!.AsArray()[^1]!;
        stop["section"]!.GetValue<string>().ShouldBe("@package");
        stop["id"]!.GetValue<string>().ShouldBe("plugins/sample/notes.jsonl");
        stop["code"]!.GetValue<string>().ShouldBe("FCMS3200");
        await using (var scope = target.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().CountAsync(Token)).ShouldBe(0);
        }
    }

    private static async Task<JsonNode> ImportAsync(HttpClient client, byte[] package, string query)
    {
        using var body = new ByteArrayContent(package);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await client.PostAsync(new Uri("/api/management/v1/transfer/import" + query, UriKind.Relative), body, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonNode.Parse(text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1])!["report"]!;
    }
}
