using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Mcp;
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.MultiSite;
using FluentCMS.Plugins.Search;
using FluentCMS.Plugins.Webhooks;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

// The first-party plugins' cleanup of a deleted site on the provider of the run: what each plugin keeps of a site goes
// with it, and what it keeps of another site stays. The plugins' own tests do the same through their endpoints on SQLite
// only; here the rows are written directly, since what differs between providers is the SQL of the removal.
public sealed class DeletedSitePluginTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-data-media-");
    private readonly IHost _host;

    public DeletedSitePluginTests()
    {
        _host = _database.CreateHost(
            ArticleSchemas.Titled,
            "Production",
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            new Dictionary<string, string?> { ["FluentCms:Media:Local:Root"] = _media.FullName },
            configure: cms =>
            {
                // An in-process handler is cut off at once here and never tried again, so a cleanup left to one fails.
                cms.Events.Handlers.Timeout = TimeSpan.FromTicks(1);
                cms.AddPlugin<WebhooksPlugin>();
                cms.AddPlugin<McpPlugin>();
                cms.AddPlugin<MediaPlugin>();
                cms.AddPlugin<SearchPlugin>();
                cms.AddPlugin<MultiSitePlugin>();
            });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _database.Dispose();
        _media.Delete(recursive: true);
    }

    [Fact]
    public async Task DeletingASiteRemovesWhatEachPluginKeptOfItAndNothingOfAnotherSite()
    {
        await _host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);
        var gone = await AsSystemAsync<ISiteAdministration, Guid>(async sites => (await sites.CreateAsync("Gone", "gone", "en", Token)).Id);
        var kept = await AsSystemAsync<ISiteAdministration, Guid>(async sites => (await sites.CreateAsync("Kept", "kept", "en", Token)).Id);
        var goneRows = await AddRowsAsync(gone);
        var keptRows = await AddRowsAsync(kept);

        await AsSystemAsync<ISiteAdministration, bool>(async sites =>
        {
            await sites.DeleteAsync(new SiteId(gone), Token);
            return true;
        });
        while (await _host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Guid[] both = [gone, kept];

        var webhooks = services.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        (await SitesOfAsync(webhooks.Set<Webhook>().Select(webhook => webhook.SiteId), both)).ShouldBe([kept]);
        // The plugins' tables have no foreign keys, so a child row is found by the id of its parent, which is gone.
        (await webhooks.Set<WebhookDelivery>().Where(delivery => delivery.WebhookId == goneRows.Webhook).CountAsync(Token)).ShouldBe(0);
        (await webhooks.Set<WebhookDelivery>().Where(delivery => delivery.WebhookId == keptRows.Webhook).CountAsync(Token)).ShouldBe(1);

        var mcp = services.GetRequiredService<PluginDbContext<McpPlugin>>();
        (await SitesOfAsync(mcp.Set<McpInstruction>().Select(instruction => instruction.SiteId), both)).ShouldBe([kept, kept]);
        (await SitesOfAsync(mcp.Set<McpPrompt>().Select(prompt => prompt.SiteId), both)).ShouldBe([kept]);

        var media = services.GetRequiredService<PluginDbContext<MediaPlugin>>();
        (await SitesOfAsync(media.Set<Folder>().Select(folder => folder.SiteId), both)).ShouldBe([kept]);
        (await SitesOfAsync(media.Set<Preset>().Select(preset => preset.SiteId), both)).ShouldBe([kept]);
        (await SitesOfAsync(media.Set<Asset>().Select(asset => asset.SiteId), both)).ShouldBe([kept]);
        (await media.Set<Variant>().Where(variant => variant.AssetId == goneRows.Asset).CountAsync(Token)).ShouldBe(0);
        (await media.Set<Variant>().Where(variant => variant.AssetId == keptRows.Asset).CountAsync(Token)).ShouldBe(1);
        var storage = services.GetRequiredKeyedService<IFileStorage>(LocalFileStorage.ProviderName);
        foreach (var key in goneRows.Files)
        {
            (await storage.ExistsAsync(key, Token)).ShouldBeFalse(key);
        }

        foreach (var key in keptRows.Files)
        {
            (await storage.ExistsAsync(key, Token)).ShouldBeTrue(key);
        }

        var search = services.GetRequiredService<PluginDbContext<SearchPlugin>>();
        (await SitesOfAsync(search.Set<SearchEntry>().Select(entry => entry.SiteId!.Value), both)).ShouldBe([kept]);

        var multiSite = services.GetRequiredService<PluginDbContext<MultiSitePlugin>>();
        (await SitesOfAsync(multiSite.Set<SiteHost>().Select(host => host.SiteId), both)).ShouldBe([kept]);
    }

    private static Task<List<Guid>> SitesOfAsync(IQueryable<Guid> sites, Guid[] of) =>
        sites.Where(site => of.Contains(site)).ToListAsync(Token);

    // A row of each table of each plugin in the site, and the files of its media.
    private async Task<SiteRows> AddRowsAsync(Guid site)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = Guid.CreateVersion7();
        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var webhooks = services.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        var webhook = new Webhook { Id = Guid.CreateVersion7(), SiteId = site, Name = "builds", Url = "https://example.com/hook", Secret = "protected", Enabled = true, CreatedAt = now, CreatedBy = actor, UpdatedAt = now, UpdatedBy = actor };
        webhooks.AddRange(
            webhook,
            new WebhookDelivery { Id = Guid.CreateVersion7(), DeliveryId = Guid.CreateVersion7(), WebhookId = webhook.Id, EventId = Guid.CreateVersion7(), Event = "content.published", Attempt = 1, State = DeliveryStates.Succeeded, StatusCode = 200, RequestBody = "{}", CreatedAt = now });
        await webhooks.SaveChangesAsync(Token);

        var mcp = services.GetRequiredService<PluginDbContext<McpPlugin>>();
        mcp.AddRange(
            new McpInstruction { Id = Guid.CreateVersion7(), SiteId = site, Text = "Titles are sentence case.", UpdatedAt = now, UpdatedBy = actor },
            new McpInstruction { Id = Guid.CreateVersion7(), SiteId = site, Type = "article", Text = "Articles are short.", UpdatedAt = now, UpdatedBy = actor },
            new McpPrompt { Id = Guid.CreateVersion7(), SiteId = site, Name = "sum-up", Template = "Sum it up.", UpdatedAt = now, UpdatedBy = actor });
        await mcp.SaveChangesAsync(Token);

        var media = services.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var folder = new Folder { Id = Guid.CreateVersion7(), SiteId = site, Name = "Photos" };
        folder.Path = $"/{folder.Id:D}/";
        var asset = new Asset { Id = Guid.CreateVersion7(), SiteId = site, FolderId = folder.Id, FileName = "photo.png", StorageProvider = LocalFileStorage.ProviderName, StorageKey = $"{site:N}/photo.png", OwnsFile = true, MimeType = "image/png", Size = 3, Checksum = new string('a', 64), CreatedAt = now, CreatedBy = actor, UpdatedAt = now, UpdatedBy = actor };
        var variant = new Variant { AssetId = asset.Id, ParamsHash = new string('b', 64), StorageKey = $"{site:N}/photo-small.png", Size = 3, CreatedAt = now };
        media.AddRange(folder, new Preset { SiteId = site, Name = "small", Params = """{"w":"100"}""" }, asset, variant);
        await media.SaveChangesAsync(Token);
        var storage = services.GetRequiredKeyedService<IFileStorage>(LocalFileStorage.ProviderName);
        foreach (var key in (string[])[asset.StorageKey, variant.StorageKey])
        {
            using var bytes = new MemoryStream([1, 2, 3]);
            await storage.WriteAsync(key, bytes, Token);
        }

        var search = services.GetRequiredService<PluginDbContext<SearchPlugin>>();
        search.Add(new SearchEntry { DocumentId = Guid.CreateVersion7(), Locale = "en", State = SearchStates.Published, Type = "article", SiteId = site, Text = "left behind", UpdatedAt = now });
        await search.SaveChangesAsync(Token);

        var multiSite = services.GetRequiredService<PluginDbContext<MultiSitePlugin>>();
        multiSite.Add(new SiteHost { SiteId = site, Hostname = $"{site:N}.example.com", IsPrimary = true, CreatedAt = now });
        await multiSite.SaveChangesAsync(Token);

        return new SiteRows(webhook.Id, asset.Id, [asset.StorageKey, variant.StorageKey]);
    }

    private async Task<TResult> AsSystemAsync<TService, TResult>(Func<TService, Task<TResult>> work)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Job, new SiteId(DefaultSite.Id));
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    // The parents of the rows that are found by their parent's id, and the keys of the media files.
    private sealed record SiteRows(Guid Webhook, Guid Asset, string[] Files);
}
