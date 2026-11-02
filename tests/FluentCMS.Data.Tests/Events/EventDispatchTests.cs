using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Events;
using FluentCMS.Hooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Events;

public sealed class EventDispatchTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private sealed class PublishesThenFails(IEventBus bus) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            await bus.PublishAsync(new Ping("from a hook"), cancellationToken);
            throw new InvalidOperationException("A plugin table is gone.");
        }
    }

    private sealed class PublishesThenSucceeds(IEventBus bus) : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) =>
            bus.PublishAsync(new Ping("from a hook"), cancellationToken);
    }

    // The first create makes a second entry from its after hook; the second create publishes and fails, which rolls the
    // second entry back to its savepoint, and the first hook carries on.
    private sealed class NestedWrite(IContentService content, IEventBus bus, NestedState state) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (state.Depth++ == 0)
            {
                try
                {
                    await content.CreateAsync("article", Titled("Nested"), cancellationToken);
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            await bus.PublishAsync(new Ping("from the nested write"), cancellationToken);
            throw new InvalidOperationException("The nested write fails.");
        }
    }

    private sealed class NestedState
    {
        public int Depth { get; set; }
    }

    [Fact]
    public async Task AHandlerHearsTheChangeOnlyAfterTheCommit()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms => cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>()));
        var committed = new List<int>();
        seen.Behavior = (@event, _) =>
        {
            committed.Add(database.Versions(((EntryEvent)@event).EntryId).Count);
            return Task.CompletedTask;
        };

        var entry = await database.CreateAsync(Editor, Titled("T"));

        var heard = seen.Of<EntryDraftSavedEvent>().ShouldHaveSingleItem();
        heard.EntryId.ShouldBe(IdOf(entry));
        heard.Type.ShouldBe("article");
        heard.Locale.ShouldBe("en");
        heard.Version.ShouldBe(1);
        heard.Source.ActorId.ShouldBe(Editor.Id);
        heard.Source.Origin.ShouldBe(Origin.Api);
        heard.Source.SiteId.ShouldBe(SiteA.Value);
        committed.ShouldBe([1]);
    }

    [Fact]
    public async Task EveryKindOfChangeBecomesItsEvent()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Events.AddHandler<EntryTrashedEvent, TrashedHandler>();
        }));

        var entry = await database.CreateAsync(Editor, Titled("T"));
        await database.DeleteAsync(Editor, IdOf(entry));

        seen.Calls.ShouldBe(["saved", "trashed"]);
        seen.Of<EntryTrashedEvent>().ShouldHaveSingleItem().EntryId.ShouldBe(IdOf(entry));
    }

    [Fact]
    public async Task AWriteThatFailsIsNeverPublished()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>()));

        await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, Titled(new string('x', 41))));

        seen.Events.ShouldBeEmpty();
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWriteRolledBackByAnAfterHookIsNeverPublishedNorRecorded()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Events.AddHandler<Ping, PingHandler>();
            cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesThenFails>();
        }));

        await Should.ThrowAsync<InvalidOperationException>(() => database.CreateAsync(Editor, Titled("T")));

        seen.Events.ShouldBeEmpty();
        database.Rows().ShouldBeEmpty();
        database.Footprint().ShouldBe("0,0,0,0,0");
    }

    [Fact]
    public async Task AnEventPublishedByAHookIsDeliveredAfterTheCommitWithTheChange()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Events.AddHandler<Ping, PingHandler>();
            cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesThenSucceeds>();
        }));
        var versionsWhenPingHeard = -1;
        seen.Behavior = (@event, _) =>
        {
            if (@event is Ping)
            {
                versionsWhenPingHeard = database.Read(context => context.Versions.Count());
            }

            return Task.CompletedTask;
        };

        await database.CreateAsync(Editor, Titled("T"));

        seen.Calls.ShouldBe(["saved", "ping"]);
        versionsWhenPingHeard.ShouldBe(1);
        database.Rows().Select(row => row.Handler).Order().ShouldBe(["ping-outbox", "saved-outbox"]);
    }

    [Fact]
    public async Task EventsOfAWriteRolledBackToASavepointAreDroppedWhileTheOuterWriteIsKept()
    {
        var seen = new Seen();
        var state = new NestedState();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Events.AddHandler<Ping, PingHandler>();
            cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
            cms.Services.AddSingleton(state);
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, NestedWrite>();
        }));

        var entry = await database.CreateAsync(Editor, Titled("Outer"));

        state.Depth.ShouldBe(2);
        seen.Calls.ShouldBe(["saved"]);
        seen.Of<EntryDraftSavedEvent>().ShouldHaveSingleItem().EntryId.ShouldBe(IdOf(entry));
        database.Rows().ShouldHaveSingleItem().Handler.ShouldBe("saved-outbox");
        database.Footprint().ShouldStartWith("1,");
    }

    [Fact]
    public async Task AHandlerThatThrowsDoesNotAffectTheWriteOrTheOtherHandlers()
    {
        var seen = new Seen();
        var logs = new EventLogs();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<EntryDraftSavedEvent, EarlySaved>();
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Events.AddHandler<EntryDraftSavedEvent, LateSaved>();
        }));
        seen.Behavior = (_, _) => seen.Calls.Count == 1 ? throw new InvalidOperationException("The handler is broken.") : Task.CompletedTask;

        var entry = await database.CreateAsync(Editor, Titled("T"));

        seen.Calls.ShouldBe(["early", "saved", "late"]);
        logs.Count("HandlerFailed").ShouldBe(1);
        (await database.GetAsync(Editor, IdOf(entry)))!["title"].ShouldBe("T");
        database.Versions(IdOf(entry)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatCancelsDoesNotAffectTheWrite()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms => cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>()));
        seen.Behavior = (_, _) => throw new OperationCanceledException();

        var entry = await database.CreateAsync(Editor, Titled("T"));

        database.Versions(IdOf(entry)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task HandlersRunInHookOrderThenByThePluginDependencyRankThenByRegistration()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<IHookTiebreaker, RankTiebreaker>();
            cms.Events.AddHandler<EntryDraftSavedEvent, LateSaved>();
            cms.Events.AddHandler<EntryDraftSavedEvent, TiedFirstRegistered>();
            cms.Events.AddHandler<EntryDraftSavedEvent, TiedLastRegistered>();
            cms.Events.AddHandler<EntryDraftSavedEvent, EarlySaved>();
        }));

        await database.CreateAsync(Editor, Titled("T"));

        seen.Calls.ShouldBe(["early", "tied-last-registered", "tied-first-registered", "late"]);
    }

    [Fact]
    public async Task APublishOutsideATransactionIsDeliveredAtOnce()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.AddHandler<Ping, PingHandler>();
            cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
        }));

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("now"), Token);
            return true;
        });

        seen.Calls.ShouldBe(["ping"]);
        database.Rows().ShouldHaveSingleItem().Handler.ShouldBe("ping-outbox");
    }

    [Fact]
    public async Task APublishInATransactionTheServiceDoesNotOwnIsRefused()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms => cms.Events.AddHandler<Ping, PingHandler>()));

        var exception = await database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            return await Should.ThrowAsync<InvalidOperationException>(async () => await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("early"), Token));
        });

        exception.Message.ShouldStartWith("FCMS2602");
        seen.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task AWriteInATransactionTheServiceDoesNotOwnRecordsNothingForTheOutbox()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>()));

        await database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            await services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Rolled back"), Token);
            await transaction.RollbackAsync(Token);
            return true;
        });

        seen.Events.ShouldBeEmpty();
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARecordedRowNamesTheEventAndItsHandler()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));

        await database.CreateAsync(Editor, Titled("T"));

        var row = database.Rows().ShouldHaveSingleItem();
        row.Status.ShouldBe("pending");
        row.EventType.ShouldBe(typeof(EntryDraftSavedEvent).FullName);
        database.Read(context => context.Set<OutboxMessage>().AsNoTracking().Count()).ShouldBe(1);
    }

    [Fact]
    public async Task AnEventOfOneTypeIsNotHeardByTheHandlersOfAnother()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms => cms.Events.AddHandler<EntryTrashedEvent, TrashedHandler>()));
        var dispatcher = database.Database.Services.GetRequiredService<EventDispatcher>();

        await dispatcher.DispatchAsync(new EntryDraftSavedEvent("article", Guid.NewGuid(), "en", 1));

        seen.Events.ShouldBeEmpty();
    }
}
