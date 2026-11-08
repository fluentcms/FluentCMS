using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Events;
using FluentCMS.Hooks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Events;

// Each transition reaches a handler of this process and stages its outbox row only when it commits, and goes the way a save
// goes inside a transaction the service did not begin: the row commits or rolls back with it, and no handler here is told.
public sealed class PublishingEventTests
{
    private sealed class Heard<TEvent>(Seen seen) : IEventHandler<TEvent>
        where TEvent : CmsEvent
    {
        public ValueTask HandleAsync(TEvent message, CancellationToken cancellationToken) => seen.HeardAsync("heard", message, null, cancellationToken);
    }

    private sealed class HeardOutbox<TEvent>(Seen seen) : IOutboxEventHandler<TEvent>
        where TEvent : CmsEvent
    {
        public ValueTask HandleAsync(TEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("outbox", message, delivery, cancellationToken);
    }

    private sealed class Failing : IAfterPublishHook<ContentDocument>, IAfterUnpublishHook<ContentDocument>, IAfterUpdateHook<ContentDocument>
    {
        public bool Armed { get; set; }

        private ValueTask Fail() => Armed ? throw new InvalidOperationException("hook failed") : ValueTask.CompletedTask;

        public ValueTask AfterPublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Fail();

        public ValueTask AfterUnpublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Fail();

        public ValueTask AfterUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => Fail();
    }

    private static Action<FluentCmsBuilder> Register<TEvent>(Seen seen, Failing hook)
        where TEvent : CmsEvent => cms =>
    {
        cms.Services.AddSingleton(seen);
        cms.Services.AddSingleton<IAfterPublishHook<ContentDocument>>(hook);
        cms.Services.AddSingleton<IAfterUnpublishHook<ContentDocument>>(hook);
        cms.Services.AddSingleton<IAfterUpdateHook<ContentDocument>>(hook);
        cms.Events.AddHandler<TEvent, Heard<TEvent>>();
        cms.Events.AddOutboxHandler<TEvent, HeardOutbox<TEvent>>("heard-outbox");
    };

    private static async Task<Guid> ModifiedAsync(ServiceDatabase database)
    {
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One" }));
        await database.PublishAsync(Editor, id);
        await database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1);
        return id;
    }

    private static async Task CommittedAsync<TEvent>(Func<IContentService, Guid, Task> transition, int version)
        where TEvent : EntryEvent
    {
        var seen = new Seen();
        var hook = new Failing();
        using var database = new ServiceDatabase(Register<TEvent>(seen, hook));
        var id = await ModifiedAsync(database);
        seen.Events.Clear();
        var before = database.Rows().Count;

        await database.AsAsync(Editor, async services =>
        {
            await transition(services.GetRequiredService<IContentService>(), id);
            return true;
        });

        var heard = seen.Of<TEvent>().ShouldHaveSingleItem();
        heard.EntryId.ShouldBe(id);
        heard.Type.ShouldBe("article");
        heard.Locale.ShouldBe("en");
        heard.Version.ShouldBe(version);

        var rows = database.Rows();
        rows.Count.ShouldBe(before + 1);
        rows[^1].EventId.ShouldBe(heard.EventId);
        rows[^1].EventType.ShouldBe(typeof(TEvent).FullName);
    }

    private static async Task RolledBackAsync<TEvent>(Func<IContentService, Guid, Task> transition)
        where TEvent : EntryEvent
    {
        var seen = new Seen();
        var hook = new Failing();
        using var database = new ServiceDatabase(Register<TEvent>(seen, hook));
        var id = await ModifiedAsync(database);
        seen.Events.Clear();
        var before = database.Rows().Count;
        hook.Armed = true;

        await Should.ThrowAsync<InvalidOperationException>(() => database.AsAsync(Editor, async services =>
        {
            await transition(services.GetRequiredService<IContentService>(), id);
            return true;
        }));

        seen.Events.ShouldBeEmpty();
        database.Rows().Count.ShouldBe(before);
    }

    private static async Task InAForeignTransactionAsync<TEvent>(Func<IContentService, Guid, Task> transition)
        where TEvent : EntryEvent
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Register<TEvent>(seen, new Failing()));
        var id = await ModifiedAsync(database);
        seen.Events.Clear();
        var before = database.Rows().Count;

        await database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            await transition(services.GetRequiredService<IContentService>(), id);
            await transaction.RollbackAsync(Token);
            return true;
        });

        seen.Events.ShouldBeEmpty();
        database.Rows().Count.ShouldBe(before);

        await database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            await transition(services.GetRequiredService<IContentService>(), id);
            await transaction.CommitAsync(Token);
            return true;
        });

        seen.Events.ShouldBeEmpty();
        database.Rows().Count.ShouldBe(before + 1);
    }

    private static Task Publish(IContentService service, Guid id) => service.PublishAsync("article", id, null, Token);

    private static Task Unpublish(IContentService service, Guid id) => service.UnpublishAsync("article", id, null, Token);

    private static Task Discard(IContentService service, Guid id) => service.DiscardDraftAsync("article", id, null, Token);

    private static Task Restore(IContentService service, Guid id) => service.RestoreVersionAsync("article", id, 1, null, Token);

    [Fact]
    public Task APublishThatCommitsIsToldAndStaged() => CommittedAsync<EntryPublishedEvent>(Publish, 2);

    [Fact]
    public Task AnUnpublishThatCommitsIsToldAndStaged() => CommittedAsync<EntryUnpublishedEvent>(Unpublish, 2);

    [Fact]
    public Task ADiscardThatCommitsIsToldAndStaged() => CommittedAsync<EntryDraftDiscardedEvent>(Discard, 3);

    [Fact]
    public Task ARestoreThatCommitsIsToldAndStaged() => CommittedAsync<EntryVersionRestoredEvent>(Restore, 3);

    [Fact]
    public Task APublishRolledBackTellsNoOne() => RolledBackAsync<EntryPublishedEvent>(Publish);

    [Fact]
    public Task AnUnpublishRolledBackTellsNoOne() => RolledBackAsync<EntryUnpublishedEvent>(Unpublish);

    [Fact]
    public Task ADiscardRolledBackTellsNoOne() => RolledBackAsync<EntryDraftDiscardedEvent>(Discard);

    [Fact]
    public Task ARestoreRolledBackTellsNoOne() => RolledBackAsync<EntryVersionRestoredEvent>(Restore);

    [Fact]
    public Task APublishInAForeignTransactionGoesWithIt() => InAForeignTransactionAsync<EntryPublishedEvent>(Publish);

    [Fact]
    public Task AnUnpublishInAForeignTransactionGoesWithIt() => InAForeignTransactionAsync<EntryUnpublishedEvent>(Unpublish);

    [Fact]
    public Task ADiscardInAForeignTransactionGoesWithIt() => InAForeignTransactionAsync<EntryDraftDiscardedEvent>(Discard);

    [Fact]
    public Task ARestoreInAForeignTransactionGoesWithIt() => InAForeignTransactionAsync<EntryVersionRestoredEvent>(Restore);

    [Fact]
    public async Task APublishThatMovesASharedFieldTellsOnlyTheLocalePublished()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Register<EntryPublishedEvent>(seen, new Failing()));
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.PatchAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        await database.PublishAsync(Editor, id);
        await database.PublishAsync(Editor, id, "fr", operationLocale: "fr");
        await database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);
        seen.Events.Clear();

        await database.PublishAsync(Editor, id);

        seen.Of<EntryPublishedEvent>().ShouldHaveSingleItem().Locale.ShouldBe("en");
    }

    [Fact]
    public async Task ADiscardThatWritesNothingTellsNoOne()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Register<EntryDraftDiscardedEvent>(seen, new Failing()));
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One" }));
        await database.PublishAsync(Editor, id);
        var before = database.Rows().Count;

        await database.DiscardAsync(Editor, id);

        seen.Events.ShouldBeEmpty();
        database.Rows().Count.ShouldBe(before);
    }
}
