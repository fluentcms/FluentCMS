using System.Security.Cryptography;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Mcp;
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.MultiSite;
using FluentCMS.Plugins.Search;
using FluentCMS.Plugins.Webhooks;
using FluentCMS.Sites;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

// #3586: PluginDbContext<T>.HoldSiteAsync holds a site's row as a content write that adds an entry does, so a plugin's
// write in its transaction either commits before the site's deletion and goes with the site's cleanup, or finds the site
// gone and is refused, and leaves no row of a deleted site. Then each first-party write that adds rows of a site, with the
// site deleted as its transaction begins, as on an instance whose cache still has the site.
public sealed class PluginSiteHoldTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFElEQVR42mPQztlAEmIY1TAoNAAALmO38Z6fgBcAAAAASUVORK5CYII=");

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-data-media-");
    private readonly CommandGate _deleting = new(command => TestProvider.IsDeleteFrom(command.CommandText, "cms_site"));
    private readonly TransactionGate _beginning = new();
    private readonly IHost _host;

    public PluginSiteHoldTests()
    {
        _host = _database.CreateHost(
            ArticleSchemas.Titled,
            "Production",
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            new Dictionary<string, string?> { ["FluentCms:Media:Local:Root"] = _media.FullName, ["FluentCms:Media:Library"] = nameof(MediaLibrary.PerSite) },
            configure: cms =>
            {
                cms.AddPlugin<MediaPlugin>();
                cms.AddPlugin<WebhooksPlugin>();
                cms.AddPlugin<McpPlugin>();
                cms.AddPlugin<MultiSitePlugin>();
                cms.AddPlugin<SearchPlugin>();
                cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(_deleting));
                cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(_beginning));
                cms.Services.ConfigureDbContext<PluginDbContext<WebhooksPlugin>>(options => options.AddInterceptors(_beginning));
                cms.Services.ConfigureDbContext<PluginDbContext<McpPlugin>>(options => options.AddInterceptors(_beginning));
                cms.Services.ConfigureDbContext<PluginDbContext<MultiSitePlugin>>(options => options.AddInterceptors(_beginning));
                cms.Services.ConfigureDbContext<PluginDbContext<SearchPlugin>>(options => options.AddInterceptors(_beginning));
            });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _database.Dispose();
        _media.Delete(recursive: true);
    }

    // The write holds the site from before its insert to its commit. On a server the deletion is seen waiting for the lock;
    // SQLite shows nothing of it, but its one writer is the write's until it commits.
    [Fact]
    public async Task AWriteHeldWhileItsSiteIsDeletedCommitsFirstAndGoesWithTheSite()
    {
        var site = await CreateSiteAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        await using var transaction = await media.Database.BeginTransactionAsync(Token);
        await media.HoldSiteAsync(new SiteId(site), Token);
        media.Add(Folder(site));
        await media.SaveChangesAsync(Token);

        var deleting = await RowLocks.BesideAsync(_database.ConnectionString, () => DeleteAsync(site), Token);
        deleting.IsCompleted.ShouldBeFalse();
        await transaction.CommitAsync(Token);
        await deleting.WaitAsync(RowLocks.Limit, Token);

        Folders(site).ShouldBe(1);
        await CleanUpAsync(site);
        Folders(site).ShouldBe(0);
    }

    // The deletion has removed the row and not committed yet: the hold waits for it, then finds no site.
    [Fact]
    public async Task AHoldThatWaitsForTheSitesDeletionIsRefusedAndLeavesNothing()
    {
        var site = await CreateSiteAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var deletion = _host.Services.CreateAsyncScope();
        _deleting.Context = deletion.ServiceProvider.GetRequiredService<CmsDbContext>();
        _deleting.At = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        var deleting = Task.Run(() => deletion.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(site, null, Token), Token);
        await entered.Task.WaitAsync(RowLocks.Limit, Token);

        var holding = await RowLocks.BesideAsync(_database.ConnectionString, () => WriteHeldAsync(site), Token);
        holding.IsCompleted.ShouldBeFalse();
        release.TrySetResult();
        await deleting.WaitAsync(RowLocks.Limit, Token);

        var error = (await Should.ThrowAsync<ValidationException>(() => holding.WaitAsync(RowLocks.Limit, Token))).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(SiteErrorCodes.SiteNotFound);
        Folders(site).ShouldBe(0);
    }

    // A hold that ended with its statement would guard nothing, so the member opens no transaction of its own.
    [Fact]
    public async Task AHoldOutsideATransactionIsRefused()
    {
        var site = await CreateSiteAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();

        (await Should.ThrowAsync<InvalidOperationException>(() => media.HoldSiteAsync(new SiteId(site), Token))).Message.ShouldContain("transaction");
        media.Database.CurrentTransaction.ShouldBeNull();
    }

    // Two sites held in ascending order, as two writes that share them take them; one that is gone refuses its hold and
    // leaves the transaction the plugin's.
    [Fact]
    public async Task ATransactionHoldsTwoSitesAndOneThatIsGoneIsRefused()
    {
        var first = await CreateSiteAsync();
        var second = await CreateSiteAsync();
        var gone = await CreateSiteAsync();
        await DeleteAsync(gone);

        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        await using (var transaction = await media.Database.BeginTransactionAsync(Token))
        {
            foreach (var site in ((Guid[])[first, second]).Order())
            {
                await media.HoldSiteAsync(new SiteId(site), Token);
            }

            media.AddRange(Folder(first), Folder(second));
            await media.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        media.ChangeTracker.Clear();
        await using (var transaction = await media.Database.BeginTransactionAsync(Token))
        {
            await media.HoldSiteAsync(new SiteId(first), Token);
            (await Should.ThrowAsync<ValidationException>(() => media.HoldSiteAsync(new SiteId(gone), Token))).Errors.ShouldHaveSingleItem().Code.ShouldBe(SiteErrorCodes.SiteNotFound);
            media.Database.CurrentTransaction.ShouldBeSameAs(transaction);
            await transaction.RollbackAsync(Token);
        }

        Folders(first).ShouldBe(1);
        Folders(second).ShouldBe(1);
        Folders(gone).ShouldBe(0);
    }

    // The upload writes its file before its row. The site is deleted after the upload checked it and wrote the file, as its
    // save begins; the row is refused, and the upload deletes the file it wrote.
    [Fact]
    public async Task AnUploadWhoseSiteIsDeletedAsItsSaveBeginsLeavesNoRowAndNoFile()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<MediaPlugin, MediaService>(site, media => media.UploadAsync(new MediaUpload(null, [new MediaUploadFile("dot.png", new MemoryStream(Png))]), Token));

        Read<MediaPlugin, int>(media => media.Set<Asset>().Count(asset => asset.SiteId == site)).ShouldBe(0);
        _media.EnumerateFiles("*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task AFolderWhoseSiteIsDeletedAsItsSaveBeginsIsRefused()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<MediaPlugin, MediaService>(site, media => media.CreateFolderAsync("held", null, Token));

        Folders(site).ShouldBe(0);
    }

    [Fact]
    public async Task APresetWhoseSiteIsDeletedAsItsSaveBeginsIsRefused()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<MediaPlugin, MediaService>(site, media => media.SetPresetAsync("small", new MediaImageOptions { Width = 100 }, Token));

        Read<MediaPlugin, int>(media => media.Set<Preset>().Count(preset => preset.SiteId == site)).ShouldBe(0);
    }

    // A line that adds a folder or an asset is reported refused, and the file the asset line wrote is deleted again.
    [Fact]
    public async Task AnImportWhoseSiteIsDeletedAsItsSavesBeginLeavesNoRowAndNoFile()
    {
        var site = await CreateSiteAsync();
        var (folder, asset) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var file = $"files/{asset:N}/original.png";
        var package = new Package(new SiteId(site), new Dictionary<string, byte[]>
        {
            ["folders.jsonl"] = Encoding.UTF8.GetBytes($$"""{"id":"{{folder}}","name":"held"}""" + "\n"),
            ["assets.jsonl"] = Encoding.UTF8.GetBytes($$"""{"id":"{{asset}}","fileName":"dot.png","mimeType":"image/png","size":{{Png.Length}},"checksum":"{{Convert.ToHexStringLower(SHA256.HashData(Png))}}","alt":{},"caption":{},"tags":[],"createdAt":"{{now:O}}","createdBy":"{{Guid.Empty}}","updatedAt":"{{now:O}}","updatedBy":"{{Guid.Empty}}","file":"{{file}}"}""" + "\n"),
            [file] = Png,
        });

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            _beginning.Context = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            _beginning.At = () => DeleteAsync(site);
            await ActivatorUtilities.CreateInstance<MediaTransfer>(scope.ServiceProvider).ImportAsync(package, Token);
        }

        _beginning.Passed.ShouldBeTrue();
        package.Reports.ShouldBe([(MediaTransfer.FoldersSection, folder.ToString("D"), TransferOutcome.Error, SiteErrorCodes.SiteNotFound), (MediaTransfer.AssetsSection, asset.ToString("D"), TransferOutcome.Error, SiteErrorCodes.SiteNotFound)]);
        Folders(site).ShouldBe(0);
        Read<MediaPlugin, int>(media => media.Set<Asset>().Count(row => row.SiteId == site)).ShouldBe(0);
        _media.EnumerateFiles("*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task AWebhookWhoseSiteIsDeletedAsItsSaveBeginsIsRefused()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<WebhooksPlugin, WebhookService>(site, webhooks => webhooks.CreateAsync(new WebhookRequest("builds", "https://93.184.215.14/hook", ["content.published"], null, null, null, null), Token));

        Read<WebhooksPlugin, int>(webhooks => webhooks.Set<Webhook>().Count(webhook => webhook.SiteId == site)).ShouldBe(0);
    }

    [Fact]
    public async Task GuidelinesWhoseSiteIsDeletedAsTheirSaveBeginsAreRefused()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<McpPlugin, McpInstructionService>(site, instructions => instructions.SaveAsync(null, new McpInstructionRequest("Titles are sentence case."), Token));

        Read<McpPlugin, int>(mcp => mcp.Set<McpInstruction>().Count(instruction => instruction.SiteId == site)).ShouldBe(0);
    }

    [Fact]
    public async Task APromptWhoseSiteIsDeletedAsItsSaveBeginsIsRefused()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<McpPlugin, McpPromptService>(site, prompts => prompts.CreateAsync(new McpPromptRequest("sum-up", null, null, null, "Sum it up."), Token));

        Read<McpPlugin, int>(mcp => mcp.Set<McpPrompt>().Count(prompt => prompt.SiteId == site)).ShouldBe(0);
    }

    // The service found the site, as an instance whose cache still has it would; the hold finds it gone.
    [Fact]
    public async Task AHostWhoseSiteIsDeletedAsItsSaveBeginsIsRefused()
    {
        var site = await CreateSiteAsync();

        await RefusedAsync<MultiSitePlugin, SiteHostService>(site, hosts => hosts.AddAsync(site, new SiteHostRequest($"{site:N}.example.com", null), Token));

        Read<MultiSitePlugin, int>(hosts => hosts.Set<SiteHost>().Count(host => host.SiteId == site)).ShouldBe(0);
    }

    // As an outbox delivery that read the entry before it went with its site; the delivery is tried again and reads it gone.
    [Fact]
    public async Task SearchEntriesWhoseSiteIsDeletedAsTheirSaveBeginsAreRefused()
    {
        var site = await CreateSiteAsync();
        var row = new SearchRow(Guid.CreateVersion7(), "en", SearchStates.Published, "article", site, new Dictionary<string, string> { ["title"] = "left behind" });

        await RefusedAsync<SearchPlugin, SearchStore>(site, store => store.WriteAsync([row], Token));

        Read<SearchPlugin, int>(search => search.Set<SearchEntry>().Count(entry => entry.SiteId == site)).ShouldBe(0);
    }

    // Runs a write of a plugin in a system operation of the site, with the site deleted as the write's transaction begins,
    // and expects the hold to refuse it.
    private async Task RefusedAsync<TPlugin, TService>(Guid site, Func<TService, Task> write)
        where TPlugin : class, IPlugin
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        _beginning.Context = scope.ServiceProvider.GetRequiredService<PluginDbContext<TPlugin>>();
        _beginning.At = () => DeleteAsync(site);
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Job, new SiteId(site));

        var error = (await Should.ThrowAsync<ValidationException>(() => write(scope.ServiceProvider.GetRequiredService<TService>()))).Errors.ShouldHaveSingleItem();
        _beginning.Passed.ShouldBeTrue();
        error.Code.ShouldBe(SiteErrorCodes.SiteNotFound);
    }

    private async Task WriteHeldAsync(Guid site)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        await using var transaction = await media.Database.BeginTransactionAsync(Token);
        await media.HoldSiteAsync(new SiteId(site), Token);
        media.Add(Folder(site));
        await media.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static Folder Folder(Guid site)
    {
        var id = Guid.CreateVersion7();
        return new Folder { Id = id, SiteId = site, Name = "held", Path = $"/{id:D}/" };
    }

    private async Task<Guid> CreateSiteAsync()
    {
        await _host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);
        await using var scope = _host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Held", $"held-{Guid.NewGuid():N}"[..20], "en", Token)).Id;
    }

    private async Task DeleteAsync(Guid site)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(site, null, Token);
    }

    // What the outbox delivery of SiteDeletedEvent runs after the deletion commits.
    private async Task CleanUpAsync(Guid site)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<DeletedSiteMedia>(scope.ServiceProvider).HandleAsync(new SiteDeletedEvent(site, "held"), new EventDelivery(Guid.NewGuid(), 1), Token);
    }

    private int Folders(Guid site) => Read<MediaPlugin, int>(media => media.Set<Folder>().Count(folder => folder.SiteId == site));

    private T Read<TPlugin, T>(Func<PluginDbContext<TPlugin>, T> read)
        where TPlugin : class, IPlugin
    {
        using var scope = _host.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<PluginDbContext<TPlugin>>());
    }

    // A package of the media plugin's files, and what the import reported of each line.
    private sealed class Package(SiteId site, Dictionary<string, byte[]> files) : ITransferImport
    {
        public List<(string Section, string Id, TransferOutcome Outcome, string? Code)> Reports { get; } = [];

        public SiteId Site => site;

        public SiteId PackageSite => site;

        public IReadOnlyDictionary<string, string> Options { get; } = new Dictionary<string, string>();

        public bool DryRun => false;

        public bool Overwrite => false;

        public IReadOnlyList<string> Paths => [.. files.Keys];

        public async Task<bool> ReadAsync(string path, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken)
        {
            if (!files.TryGetValue(path, out var bytes))
            {
                return false;
            }

            await using var stream = new MemoryStream(bytes, writable: false);
            await read(stream, cancellationToken);
            return true;
        }

        public void Report(string section, string id, TransferOutcome outcome, string? code, string? message) => Reports.Add((section, id, outcome, code));
    }
}
