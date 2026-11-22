using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Hooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Sites;

// #3306: a site is never deleted under an entry. A deletion waits for a write that is adding an entry to the site, and a
// write to a site that is gone is refused, also on an instance whose cache still has the site.
public sealed class SiteDeletionRaceTests : IDisposable
{
    private readonly Held _held = new();
    private readonly ServiceDatabase _database;

    public SiteDeletionRaceTests() =>
        _database = new ServiceDatabase(cms => cms.Services
            .AddSingleton(_held)
            .AddScoped<IAfterCreateHook<ContentDocument>, HoldingHook>());

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ASiteThatGainsAnEntryWhileItIsDeletedIsNotDeleted()
    {
        var creating = _database.CreateAsync(Editor, new() { ["title"] = "Hold" }, site: SiteB);
        await _held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // The write holds its transaction open after its rows went in; the deletion waits for it.
        var deleting = Task.Run(() => DeleteAsync(_database.Database.Services.GetRequiredService<IServiceScopeFactory>(), SiteB.Value), Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
        deleting.IsCompleted.ShouldBeFalse();

        _held.Release.TrySetResult();
        var created = await creating;

        var error = (await Should.ThrowAsync<ValidationException>(() => deleting)).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(SiteErrorCodes.SiteHasContent);
        _database.Read(context => context.Set<Site>().Count(site => site.Id == SiteB.Value)).ShouldBe(1);
        _database.Read(context => context.Documents.Single(document => document.DocumentId == IdOf(created)).SiteId).ShouldBe(SiteB.Value);
    }

    [Fact]
    public async Task AWriteToASiteDeletedOnAnotherInstanceIsRefusedAndLeavesNothing()
    {
        // This instance has read the site, and keeps it for SiteCache.CacheDuration.
        await _database.AsAsync(Editor, services => services.GetRequiredService<LocaleRegistry>().GetAsync(SiteB, Token), site: SiteB);

        using (var other = _database.Database.NewInstance())
        {
            await DeleteAsync(other.GetRequiredService<IServiceScopeFactory>(), SiteB.Value);
        }

        var before = _database.Footprint();
        var error = (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Late" }, site: SiteB))).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(SiteErrorCodes.SiteNotFound);
        _database.Footprint().ShouldBe(before);
        _database.Read(context => context.Documents.IgnoreQueryFilters().Count(document => document.SiteId == SiteB.Value)).ShouldBe(0);
    }

    private static async Task DeleteAsync(IServiceScopeFactory scopes, Guid site)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(site, Token);
    }

    private sealed class Held
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class HoldingHook(Held held) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (Equals(context.Entry["title"], "Hold"))
            {
                held.Entered.TrySetResult();
                await held.Release.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
