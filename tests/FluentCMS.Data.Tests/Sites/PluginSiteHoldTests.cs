using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Media;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

// #3485: a plugin's save that adds a row of a site holds the site's row as a content write does, so it either commits before
// the site's deletion and goes with the site's cleanup, or finds the site gone and is refused, and no row of a deleted site
// is left. The media plugin stands for every plugin, since the hold is core's, on each plugin's context.
public sealed class PluginSiteHoldTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAJCAIAAAC0SDtlAAAAFElEQVR42mPQztlAEmIY1TAoNAAALmO38Z6fgBcAAAAASUVORK5CYII=");

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-data-media-");
    private readonly CommandGate _gate = new(command => command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("cms_media_folder", StringComparison.Ordinal), after: false);
    private readonly TransactionGate _saving = new();
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
                cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(_gate, _saving));
            });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _database.Dispose();
        _media.Delete(recursive: true);
    }

    // The save holds the site from before its insert to its commit. On a server the deletion is seen waiting for the lock;
    // SQLite shows nothing of it, but its one writer is the save's until it commits.
    [Fact]
    public async Task ARowAddedWhileItsSiteIsDeletedCommitsFirstAndGoesWithTheSite()
    {
        var site = await CreateSiteAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate.Context = media;
        _gate.At = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        media.Add(Folder(site));
        var saving = media.SaveChangesAsync(Token);
        await entered.Task.WaitAsync(RowLocks.Limit, Token);

        var deleting = await RowLocks.BesideAsync(_database.ConnectionString, () => DeleteAsync(site), Token);
        deleting.IsCompleted.ShouldBeFalse();
        release.TrySetResult();
        (await saving.WaitAsync(RowLocks.Limit, Token)).ShouldBe(1);
        await deleting.WaitAsync(RowLocks.Limit, Token);

        Folders(site).ShouldBe(1);
        await CleanUpAsync(site);
        Folders(site).ShouldBe(0);
    }

    // As on an instance whose SiteCache still has the site, or after the cleanup of the site ran.
    [Fact]
    public async Task ARowOfASiteDeletedBeforeItsSaveIsRefusedAndLeavesNothing()
    {
        var site = await CreateSiteAsync();
        await DeleteAsync(site);
        await CleanUpAsync(site);

        await using var scope = _host.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        media.Add(Folder(site));

        var error = (await Should.ThrowAsync<ValidationException>(() => media.SaveChangesAsync(Token))).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(SiteErrorCodes.SiteNotFound);
        media.Database.CurrentTransaction.ShouldBeNull();
        Folders(site).ShouldBe(0);
    }

    // The upload writes its file before its row. The site is deleted after the upload checked it and wrote the file, as
    // the save begins; the row is refused, and the upload deletes the file it wrote.
    [Fact]
    public async Task AnUploadWhoseSiteIsDeletedBeforeItsRowIsSavedLeavesNoRowAndNoFile()
    {
        var site = await CreateSiteAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        _saving.Context = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        _saving.At = () => DeleteAsync(site);
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Job, new SiteId(site));
        var upload = new MediaUpload(null, [new MediaUploadFile("dot.png", new MemoryStream(Png))]);

        var error = (await Should.ThrowAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<MediaService>().UploadAsync(upload, Token))).Errors.ShouldHaveSingleItem();
        _saving.Passed.ShouldBeTrue();
        error.Code.ShouldBe(SiteErrorCodes.SiteNotFound);
        Read(media => media.Set<Asset>().Count(asset => asset.SiteId == site)).ShouldBe(0);
        _media.EnumerateFiles("*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private static Folder Folder(Guid site)
    {
        var id = Guid.CreateVersion7();
        return new Folder { Id = id, SiteId = site, Name = "held", Path = $"/{id:N}/" };
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
        await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(site, Token);
    }

    // What the outbox delivery of SiteDeletedEvent runs after the deletion commits.
    private async Task CleanUpAsync(Guid site)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<DeletedSiteMedia>(scope.ServiceProvider).HandleAsync(new SiteDeletedEvent(site, "held"), new EventDelivery(Guid.NewGuid(), 1), Token);
    }

    private int Folders(Guid site) => Read(media => media.Set<Folder>().Count(folder => folder.SiteId == site));

    private T Read<T>(Func<PluginDbContext<MediaPlugin>, T> read)
    {
        using var scope = _host.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>());
    }
}
