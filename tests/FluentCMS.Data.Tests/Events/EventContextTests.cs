using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Hooks;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Events;

// What a handler is told it runs as, how events are attributed and ordered, and what a broken handler may and may not break.
public sealed class EventContextTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static Task<int> DrainAsync(IServiceProvider instance) =>
        instance.GetRequiredService<OutboxDrainer>().DrainAsync(CancellationToken.None);

    private static Action<FluentCMS.Core.DependencyInjection.FluentCmsBuilder> Registrations(Seen seen, Action<FluentCMS.Core.DependencyInjection.FluentCmsBuilder>? more = null) =>
        EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.AddHandler<Ping, PingHandler>();
            cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
            more?.Invoke(cms);
        });

    private sealed class PublishesMany(IEventBus bus) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            for (var index = 0; index < 20; index++)
            {
                await bus.PublishAsync(new Ping($"{index:00}"), cancellationToken);
            }
        }
    }

    private sealed class PublishesTwice(IEventBus bus) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            var ping = new Ping("same");
            await bus.PublishAsync(ping, cancellationToken);
            await bus.PublishAsync(ping, cancellationToken);
        }
    }

    private sealed class PublishesBoth(IEventBus bus) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            await bus.PublishAsync(new Ping("first"), cancellationToken);
            await bus.PublishAsync(new Ping2("second"), cancellationToken);
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationTokenSource Stopping { get; } = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => Stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Stopping.Cancel();
    }

    [Fact]
    public async Task APublishWithoutASourceIsAttributedToTheOperationThatPublishesIt()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen));

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("x"), Token);
            return true;
        }, site: SiteB);

        var source = seen.Of<Ping>().Single().Source;
        source.ActorId.ShouldBe(Editor.Id);
        source.ActorKind.ShouldBe(Editor.Kind);
        source.SiteId.ShouldBe(SiteB.Value);
        source.CorrelationId.ShouldBe("correlation");
        source.Origin.ShouldBe(Origin.Api);

        var row = database.Rows().Single();
        row.SiteId.ShouldBe(SiteB.Value);
        row.Payload.ShouldContain(Editor.Id.ToString());
    }

    [Fact]
    public async Task APublishWithNoOperationRunningIsAttributedToTheSystem()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen));

        using var scope = database.Database.NewScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new Ping("x"), Token);

        var source = seen.Of<Ping>().Single().Source;
        source.ActorKind.ShouldBe(PrincipalKind.System);
        source.ActorId.ShouldBe(Guid.Empty);
        source.Origin.ShouldBe(Origin.Job);
        database.Rows().Single().SiteId.ShouldBeNull();
    }

    [Fact]
    public async Task ASourceThePublisherSetIsKeptAndRecordedInUtc()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen));
        var occurredAt = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.FromHours(2));
        var own = new EventSource(occurredAt, SiteB.Value, Guid.Parse("00000000-0000-0000-0000-0000000000f1"), "Importer", PrincipalKind.AdminUser, Origin.Cli, "import-1");

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("x") { Source = own }, Token);
            return true;
        });

        seen.Of<Ping>().Single().Source.ShouldBe(own);
        var row = database.Rows().Single();
        row.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        row.OccurredAt.ShouldBe(occurredAt);
        row.SiteId.ShouldBe(SiteB.Value);
    }

    [Fact]
    public async Task AnOutboxHandlerRunsAsTheSystemOnTheSiteOfTheEventInItsDefaultLocaleWithTheActorAndCorrelationOfTheEvent()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));
        database.Database.Context.Set<Site>().Where(site => site.Id == SiteA.Value).ExecuteUpdate(site => site.SetProperty(column => column.DefaultLocale, "fr"));
        OperationContext? seenOperation = null;
        seen.Behavior = (_, _) =>
        {
            seenOperation = database.Database.Services.GetRequiredService<IOperationContextAccessor>().Current;
            return Task.CompletedTask;
        };

        await database.CreateAsync(Editor, Titled("T"), locale: "fr");
        await DrainAsync(database.Database.Services);

        var operation = seenOperation.ShouldNotBeNull();
        operation.Principal.IsSystem.ShouldBeTrue();
        operation.Origin.ShouldBe(Origin.Job);
        operation.Site.ShouldBe(SiteA);
        operation.Locale.ShouldBe("fr");
        operation.Actor!.Id.ShouldBe(Editor.Id);
        operation.Actor.Kind.ShouldBe(Editor.Kind);
        operation.CorrelationId.ShouldBe("correlation");
        database.Database.Services.GetRequiredService<IOperationContextAccessor>().Current.ShouldBeNull();
    }

    [Fact]
    public async Task AnInProcessHandlerGetsTheShutdownOfTheHostAndNeverTheTokenOfTheRequest()
    {
        var seen = new Seen();
        var lifetime = new FakeLifetime();
        using var request = new CancellationTokenSource();
        bool? afterRequest = null;
        bool? afterShutdown = null;
        seen.Behavior = async (_, _) =>
        {
            var token = seen.Tokens.Last();
            await request.CancelAsync();
            afterRequest = token.IsCancellationRequested;
            await lifetime.Stopping.CancelAsync();
            afterShutdown = token.IsCancellationRequested;
        };
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<IHostApplicationLifetime>(lifetime);
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
        }));

        await database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CreateAsync("article", Titled("T"), request.Token));

        afterRequest.ShouldBe(false);
        afterShutdown.ShouldBe(true);
    }

    [Fact]
    public async Task TheEventsOfOneCommitAreDeliveredInTheOrderTheyWereRaised()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen, cms => cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesMany>()));

        await database.CreateAsync(Editor, Titled("T"));
        await DrainAsync(database.Database.Services);

        seen.Calls.Zip(seen.Events).Where(heard => heard.First == "ping-outbox").Select(heard => ((Ping)heard.Second).Note)
            .ShouldBe(Enumerable.Range(0, 20).Select(index => $"{index:00}"));
    }

    [Fact]
    public async Task TheEntryEventsOfSeparateCommitsAreDeliveredInTheOrderTheyCommitted()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 3));
        var entry = await database.CreateAsync(Editor, Titled("T"));
        var version = VersionOf(entry);
        for (var index = 0; index < 11; index++)
        {
            version = VersionOf(await database.UpdateAsync(Editor, IdOf(entry), Titled($"T{index}"), version));
        }

        await DrainAsync(database.Database.Services);

        seen.Of<EntryDraftSavedEvent>().Select(saved => saved.Version).ShouldBe(Enumerable.Range(1, 12));
    }

    [Fact]
    public async Task AnEventPublishedTwiceByAHookIsOneEventAndTheWriteSucceeds()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen, cms => cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesTwice>()));

        await database.CreateAsync(Editor, Titled("T"));
        await DrainAsync(database.Database.Services);

        seen.Calls.Count(call => call == "ping").ShouldBe(1);
        seen.Calls.Count(call => call == "ping-outbox").ShouldBe(1);
        database.Rows().Count(row => row.Handler == "ping-outbox").ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatCannotBeBuiltNeverReachesTheCallerOfAPublish()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(Registrations(seen, cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<Ping, BrokenPingHandler>();
        }));

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("x"), Token);
            return true;
        });

        logs.Count("HandlersUnavailable").ShouldBe(1);
        database.Rows().ShouldHaveSingleItem().Handler.ShouldBe("ping-outbox");
        DrainJobs(database).ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatCannotBeBuiltLosesNeitherALaterEventNorTheRequestForADrain()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen, cms =>
        {
            cms.Events.AddHandler<Ping, BrokenPingHandler>();
            cms.Events.AddHandler<Ping2, Ping2Handler>();
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesBoth>();
        }));

        var entry = await database.CreateAsync(Editor, Titled("T"));

        entry.ShouldContainKey("id");
        seen.Calls.ShouldBe(["ping2"]);
        DrainJobs(database).ShouldBe(1);
        database.Rows().Select(row => row.Handler).Order().ShouldBe(["ping-outbox", "saved-outbox"]);
    }

    [Fact]
    public async Task AnEntryEventWhoseHandlersCannotBeBuiltLeavesTheWriteAndTheOutboxAlone()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, BrokenSavedHandler>();
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
        }));

        var entry = await database.CreateAsync(Editor, Titled("T"));

        database.Versions(IdOf(entry)).Count.ShouldBe(1);
        database.Rows().ShouldHaveSingleItem();
        DrainJobs(database).ShouldBe(1);
    }

    [Fact]
    public async Task APublishInsideATransactionOnTheSystemContextIsRefusedAndNothingIsDelivered()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen));

        var message = await database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<SystemDbContext>().Database.BeginTransactionAsync(Token);
            var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("x"), Token));
            await transaction.RollbackAsync(Token);
            return exception.Message;
        });

        message.ShouldStartWith("FCMS2602");
        seen.Events.ShouldBeEmpty();
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task AJobEventIsRecordedInTheCommitThatSettlesTheJobAndTheJobStaysOpenWhileItCannotBe()
    {
        using var fixture = new JobFixture();
        var seen = new Seen();
        var faults = new JobFaults();
        var attempts = 0;
        using var instance = fixture.Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton(seen);
            cms.Events.AddOutboxHandler<JobDeadLetteredEvent, DeadOutbox>("dead");
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(faults));
        });
        var id = fixture.AddRow("no-such-job", "{}", maxAttempts: 1);

        faults.Fail(command =>
        {
            if (TestProvider.IsInsertInto(command.CommandText, "cms_outbox"))
            {
                Interlocked.Increment(ref attempts);
                return true;
            }

            return false;
        });
        var run = instance.GetRequiredService<Core.Jobs.JobExecutor>().RunNextAsync("default", "worker", TestContext.Current.CancellationToken);
        await Eventually.HoldsAsync(() => Volatile.Read(ref attempts) >= 1);

        // The outcome and its event stand or fall together: the job is not dead-lettered while its event cannot be recorded.
        fixture.Job(id).Status.ShouldBe(Core.Jobs.JobStatus.Running);
        OutboxRows(fixture.Services).ShouldBeEmpty();

        faults.Recover();
        await Eventually.CompletesAsync(run);

        fixture.Job(id).Status.ShouldBe(Core.Jobs.JobStatus.DeadLettered);
        var row = OutboxRows(fixture.Services).ShouldHaveSingleItem();
        row.Handler.ShouldBe("dead");
        row.EventId.ShouldBe(Core.Events.OutboxRows.StableId($"job:{id}:1:dead"));

        await instance.GetRequiredService<OutboxDrainer>().DrainAsync(CancellationToken.None);
        seen.Of<JobDeadLetteredEvent>().Single().JobId.ShouldBe(id);
    }

    [Fact]
    public async Task TheInProcessHandlersOfAJobEventRunUnderASystemOperation()
    {
        using var fixture = new JobFixture();
        var seen = new Seen();
        OperationContext? seenOperation = null;
        using var instance = fixture.Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton(seen);
            cms.Events.AddHandler<JobDeadLetteredEvent, DeadHandler>();
        });
        seen.Behavior = (_, _) =>
        {
            seenOperation = instance.GetRequiredService<IOperationContextAccessor>().Current;
            return Task.CompletedTask;
        };
        fixture.AddRow("no-such-job", "{}", maxAttempts: 1);

        await instance.GetRequiredService<Core.Jobs.JobExecutor>().RunNextAsync("default", "worker", TestContext.Current.CancellationToken);

        var operation = seenOperation.ShouldNotBeNull();
        operation.Principal.IsSystem.ShouldBeTrue();
        operation.Origin.ShouldBe(Origin.Job);
        instance.GetRequiredService<IOperationContextAccessor>().Current.ShouldBeNull();
    }

    [Fact]
    public void TheOutboxHasItsOwnQueueSoThatAHandlerThatHangsHoldsUpNoOtherJob()
    {
        using var database = new ServiceDatabase();

        var queues = database.Database.Services.GetRequiredService<Core.Jobs.JobRunnerOptions>().Queues;

        queues["outbox"].ShouldBe(1);
        queues["default"].ShouldBe(2);
    }

    [Fact]
    public async Task TheDrainIsAskedForOnTheOutboxQueue()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen));

        await database.CreateAsync(Editor, Titled("T"));

        database.Read(context => context.Set<Job>().AsNoTracking().Single(job => job.Type == OutboxDrainJob.Type).Queue).ShouldBe("outbox");
    }

    private static int DrainJobs(ServiceDatabase database) =>
        database.Read(context => context.Set<Job>().AsNoTracking().Count(job => job.Type == OutboxDrainJob.Type));

    private static List<OutboxMessage> OutboxRows(IServiceProvider instance)
    {
        using var scope = instance.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking()];
    }

    private sealed class DeadOutbox(Seen seen) : IOutboxEventHandler<JobDeadLetteredEvent>
    {
        public ValueTask HandleAsync(JobDeadLetteredEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("dead", message, delivery, cancellationToken);
    }

    private sealed class DeadHandler(Seen seen) : IEventHandler<JobDeadLetteredEvent>
    {
        public ValueTask HandleAsync(JobDeadLetteredEvent message, CancellationToken cancellationToken) => seen.HeardAsync("dead-in-process", message, null, cancellationToken);
    }
}
