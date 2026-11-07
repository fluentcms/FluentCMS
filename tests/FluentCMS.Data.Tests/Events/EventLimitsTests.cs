using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Telemetry;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Events;
using FluentCMS.Hooks;
using FluentCMS.Sites;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Events;

// Attempts that are only counted when a handler really starts, handlers that block or write while they are told, the
// scrubbing of what is stored, and the site of a job's events.
public sealed class EventLimitsTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static Task<int> DrainAsync(IServiceProvider instance) =>
        instance.GetRequiredService<OutboxDrainer>().DrainAsync(Token);

    private static Action<FluentCmsBuilder> Registrations(Seen seen, Action<FluentCmsBuilder>? more = null) => cms =>
    {
        cms.Services.AddSingleton(seen);
        cms.Events.AddOutboxHandler<EntryDraftSavedEvent, SavedOutbox>("saved-outbox");
        cms.Events.AddHandler<Ping, PingHandler>();
        cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
        cms.Events.Outbox.RetryBaseDelay = TimeSpan.Zero;
        cms.Events.Outbox.RetryMaxDelay = TimeSpan.Zero;
        cms.Events.Outbox.RetryJitter = 0;
        more?.Invoke(cms);
    };

    private sealed class PublishesPing(IEventBus bus) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (context.Entry["title"] as string == "Outer")
            {
                await bus.PublishAsync(new Ping("outer"), cancellationToken);
            }
        }
    }

    // Writes another entry, valid or not, while it is told of the first.
    private sealed class NestedWriter(IContentService content, NestedPlan plan) : IEventHandler<EntryDraftSavedEvent>
    {
        public async ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken)
        {
            if (plan.Done++ > 0)
            {
                return;
            }

            try
            {
                await content.CreateAsync("article", Titled(plan.Valid ? "Nested" : new string('x', 41)), cancellationToken);
            }
            catch (ValidationException)
            {
            }
        }
    }

    private sealed class NestedPlan
    {
        public bool Valid { get; set; }

        public int Done { get; set; }
    }

    private sealed class DeadOutbox(Seen seen) : IOutboxEventHandler<JobDeadLetteredEvent>
    {
        public ValueTask HandleAsync(JobDeadLetteredEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("dead", message, delivery, cancellationToken);
    }

    private sealed class FailedOutbox(Seen seen) : IOutboxEventHandler<JobFailedEvent>
    {
        public ValueTask HandleAsync(JobFailedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("failed", message, delivery, cancellationToken);
    }

    private sealed class DeadHandler(Seen seen) : IEventHandler<JobDeadLetteredEvent>
    {
        public ValueTask HandleAsync(JobDeadLetteredEvent message, CancellationToken cancellationToken) => seen.HeardAsync("dead-in-process", message, null, cancellationToken);
    }

    private sealed class SlowPing(Seen seen) : IEventHandler<Ping>
    {
        public ValueTask HandleAsync(Ping message, CancellationToken cancellationToken) => seen.HeardAsync("slow-ping", message, null, cancellationToken);
    }

    // Ignores its token and, once the test lets it go, touches the scope of the request that raised the event, as one that
    // kept HttpContext.RequestServices would.
    private sealed class LateHandler(LateGate gate) : IEventHandler<EntryDraftSavedEvent>
    {
        public async ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken)
        {
            await gate.Release.Task;
            try
            {
                gate.Request!.GetRequiredService<IContentService>();
            }
            finally
            {
                gate.Touched.TrySetResult();
            }
        }
    }

    private sealed class LateGate
    {
        public IServiceProvider? Request { get; set; }

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Touched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Ignores its token and holds a query of its context open, past the timeout, until the test lets it go.
    [HookOrder(-1)]
    private sealed class BusyContext(CmsDbContext context, Overlap overlap) : IEventHandler<EntryDraftSavedEvent>
    {
        public async ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken)
        {
            try
            {
                await context.Database.OpenConnectionAsync(CancellationToken.None);
                ((SqliteConnection)context.Database.GetDbConnection()).CreateFunction("held", () =>
                {
                    overlap.Release!.Wait(Token);
                    return 1L;
                });
                overlap.Held = (await context.Database.SqlQueryRaw<long>("SELECT held() AS Value").ToListAsync(CancellationToken.None)).Single();
            }
            finally
            {
                // Its scope is disposed only after this returns, and Windows cannot delete the test's file while the connection is open.
                await context.Database.CloseConnectionAsync();
                overlap.Done.TrySetResult();
            }
        }
    }

    [HookOrder(1)]
    private sealed class ReadsContext(CmsDbContext context, Overlap overlap) : IEventHandler<EntryDraftSavedEvent>
    {
        public async ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken) =>
            overlap.Read = (await context.Database.SqlQueryRaw<long>("SELECT 1 AS Value").ToListAsync(cancellationToken)).Single();
    }

    private sealed class Overlap
    {
        public ManualResetEventSlim? Release { get; set; }

        public long? Held { get; set; }

        public long? Read { get; set; }

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Fact]
    public async Task HealthyEventsAreNeverFailedBecauseWorkersClaimedThemAndRanOutOfLeaseBeforeTheirTurn()
    {
        const int entries = 8;
        var seen = new Seen();
        seen.Behavior = async (_, _) => await Task.Delay(120);
        void Tight(FluentCmsBuilder cms)
        {
            cms.Events.Outbox.BatchSize = entries;
            cms.Events.Outbox.MaxAttempts = 3;
            cms.Events.Outbox.LeaseDuration = TimeSpan.FromMilliseconds(400);
            cms.Events.Outbox.DeliveryTimeout = TimeSpan.FromMilliseconds(300);
        }

        using var database = new ServiceDatabase(Registrations(seen, Tight));
        for (var index = 0; index < entries; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        using var second = database.Database.NewInstance(Registrations(seen, Tight));
        using var third = database.Database.NewInstance(Registrations(seen, Tight));

        await Task.WhenAll(
            Task.Run(() => DrainAsync(database.Database.Services), Token),
            Task.Run(async () =>
            {
                await Task.Delay(250, Token);
                return await DrainAsync(second);
            }, Token),
            Task.Run(async () =>
            {
                await Task.Delay(500, Token);
                return await DrainAsync(third);
            }, Token));

        for (var round = 0; round < 20 && database.Rows().Any(row => row.Status == OutboxStatus.Pending); round++)
        {
            await Task.Delay(100, Token);
            await DrainAsync(database.Database.Services);
        }

        var rows = database.Rows();
        rows.ShouldAllBe(row => row.Status == OutboxStatus.Delivered);
        seen.Of<EntryDraftSavedEvent>().Select(heard => heard.EventId).Distinct().Count().ShouldBe(entries);
    }

    [Fact]
    public async Task AnAttemptIsOnlyCountedWhenAHandlerStarts()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(Registrations(seen));
        await database.CreateAsync(Editor, Titled("T"));
        var drained = database.Database.Services.GetRequiredService<OutboxDrainer>();

        // Claimed by a worker that never got to it: the lease runs out with no attempt used.
        database.Read(context => context.Set<OutboxMessage>().ExecuteUpdate(row => row
            .SetProperty(column => column.LeaseOwner, "slow/worker")
            .SetProperty(column => column.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));
        database.Rows().Single().Attempts.ShouldBe(0);

        (await drained.DrainAsync(Token)).ShouldBe(1);

        seen.Deliveries.ShouldHaveSingleItem().Attempt.ShouldBe(1);
        database.Rows().Single().Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatWritesContentWhileItIsToldNeitherRepeatsNorLosesTheEventsOfTheWriteBeingDelivered()
    {
        var seen = new Seen();
        var plan = new NestedPlan { Valid = true };
        using var database = new ServiceDatabase(Registrations(seen, cms =>
        {
            cms.Services.AddSingleton(plan);
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesPing>();
            cms.Events.AddHandler<EntryDraftSavedEvent, NestedWriter>();
        }));

        await database.CreateAsync(Editor, Titled("Outer"));

        seen.Calls.Count(call => call == "ping").ShouldBe(1);
        database.Footprint().ShouldStartWith("2,");
        database.Rows().Select(row => row.Handler).Order().ShouldBe(["ping-outbox", "saved-outbox", "saved-outbox"]);
        (await DrainAsync(database.Database.Services)).ShouldBe(3);
    }

    [Fact]
    public async Task AHandlerWhoseOwnWriteFailsDoesNotTakeTheEventsOfTheWriteBeingDeliveredWithIt()
    {
        var seen = new Seen();
        var plan = new NestedPlan { Valid = false };
        using var database = new ServiceDatabase(Registrations(seen, cms =>
        {
            cms.Services.AddSingleton(plan);
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, PublishesPing>();
            cms.Events.AddHandler<EntryDraftSavedEvent, NestedWriter>();
        }));

        await database.CreateAsync(Editor, Titled("Outer"));

        seen.Calls.Count(call => call == "ping").ShouldBe(1);
        database.Footprint().ShouldStartWith("1,");
        database.Rows().Select(row => row.Handler).Order().ShouldBe(["ping-outbox", "saved-outbox"]);
        database.Read(context => context.Set<Job>().AsNoTracking().Count(job => job.Type == OutboxDrainJob.Type)).ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatBlocksBeforeItsFirstAwaitIsStillCutOffAndCountedAsAbandoned()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        using var gate = new ManualResetEventSlim();
        seen.Behavior = (_, _) =>
        {
            gate.Wait(Token);
            return Task.CompletedTask;
        };
        using var database = new ServiceDatabase(Registrations(seen, cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.Outbox.DeliveryTimeout = TimeSpan.FromMilliseconds(100);
            cms.Events.Outbox.LeaseDuration = TimeSpan.FromSeconds(30);
            cms.Events.Outbox.MaxAttempts = 1;
        }));
        await database.CreateAsync(Editor, Titled("T"));

        try
        {
            await DrainAsync(database.Database.Services).WaitAsync(TimeSpan.FromSeconds(10), Token);

            var row = database.Rows().Single();
            row.Status.ShouldBe(OutboxStatus.Failed);
            row.LastError!.ShouldContain("did not finish");
            logs.Count("DeliveryAbandoned").ShouldBe(1);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task AnInProcessHandlerOfAJobEventThatNeverReturnsDoesNotHoldTheJobWorkerForLong()
    {
        using var fixture = new JobFixture();
        var seen = new Seen();
        var logs = new RecordingLogs();
        using var gate = new ManualResetEventSlim();
        seen.Behavior = (_, _) =>
        {
            gate.Wait(Token);
            return Task.CompletedTask;
        };
        using var instance = fixture.Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton(seen);
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<JobDeadLetteredEvent, DeadHandler>();
            cms.Events.Outbox.JobEventTimeout = TimeSpan.FromMilliseconds(200);
        });
        var id = fixture.AddRow("no-such-job", "{}", maxAttempts: 1);

        try
        {
            await instance.GetRequiredService<JobExecutor>().RunNextAsync("default", "worker", Token).WaitAsync(TimeSpan.FromSeconds(10), Token);

            fixture.Job(id).Status.ShouldBe(JobStatus.DeadLettered);
            logs.Count("DispatchAbandoned").ShouldBe(1);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task AnInProcessHandlerThatBlocksDoesNotHoldTheWriteBeyondItsTimeout()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        using var gate = new ManualResetEventSlim();
        seen.Behavior = (_, _) =>
        {
            gate.Wait(Token);
            return Task.CompletedTask;
        };
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Events.Handlers.Timeout = TimeSpan.FromMilliseconds(200);
        }));

        try
        {
            var entry = await database.CreateAsync(Editor, Titled("T")).WaitAsync(TimeSpan.FromSeconds(10), Token);

            database.Versions(IdOf(entry)).Count.ShouldBe(1);
            logs.Count("HandlerAbandoned").ShouldBe(1);
            logs.Count("HandlerFailed").ShouldBe(0);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task AnInProcessHandlerThatOutlivesItsTimeoutHasItsTokenCancelledAndDoesNotHoldThePublish()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        seen.Behavior = async (_, _) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, seen.Tokens.Last());
            }
            finally
            {
                stopped.TrySetResult();
            }
        };
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<Ping, PingHandler>();
            cms.Events.Handlers.Timeout = TimeSpan.FromMilliseconds(200);
        }));

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("hangs"), Token);
            return true;
        }).WaitAsync(TimeSpan.FromSeconds(10), Token);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        seen.Tokens.Single().IsCancellationRequested.ShouldBeTrue();
        logs.Count("HandlerAbandoned").ShouldBe(1);
        logs.Count("AbandonedHandlerFailed").ShouldBe(0);
    }

    [Fact]
    public async Task AnAbandonedHandlerThatTouchesTheEndedScopeOfItsRequestFailsQuietly()
    {
        var logs = new RecordingLogs();
        var gate = new LateGate();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton(gate);
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<EntryDraftSavedEvent, LateHandler>();
            cms.Events.Handlers.Timeout = TimeSpan.FromMilliseconds(200);
        });

        await database.AsAsync(Editor, services =>
        {
            gate.Request = services;
            return services.GetRequiredService<IContentService>().CreateAsync("article", Titled("T"), Token);
        }).WaitAsync(TimeSpan.FromSeconds(10), Token);
        gate.Release.SetResult();
        await gate.Touched.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        for (var attempt = 0; attempt < 100 && logs.Count("AbandonedHandlerFailed") == 0; attempt++)
        {
            await Task.Delay(50, Token);
        }

        logs.Count("HandlerAbandoned").ShouldBe(1);
        logs.Count("AbandonedHandlerFailed").ShouldBe(1);
    }

    [Fact]
    public async Task AnAbandonedHandlerThatKeepsUsingItsContextBreaksNeitherTheNextHandlerNorTheResponse()
    {
        var logs = new RecordingLogs();
        using var release = new ManualResetEventSlim();
        var overlap = new Overlap { Release = release };
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton(overlap);
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<EntryDraftSavedEvent, BusyContext>();
            cms.Events.AddHandler<EntryDraftSavedEvent, ReadsContext>();
            cms.Events.Handlers.Timeout = TimeSpan.FromSeconds(1);
        });

        try
        {
            var entry = await database.CreateAsync(Editor, Titled("T")).WaitAsync(TimeSpan.FromSeconds(30), Token);

            entry["title"].ShouldBe("T");
            overlap.Read.ShouldBe(1);
            overlap.Held.ShouldBeNull();
        }
        finally
        {
            release.Set();
        }

        await overlap.Done.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        overlap.Held.ShouldBe(1);
        logs.Count("HandlerAbandoned").ShouldBe(1);
        logs.Count("HandlerFailed").ShouldBe(0);
        logs.Count("AbandonedHandlerFailed").ShouldBe(0);
    }

    [Fact]
    public async Task ASlowInProcessHandlerIsLoggedAndItsDurationRecorded()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        var durations = new ConcurrentQueue<(double Seconds, string? Event, string? Outcome)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.EventHandlerDuration)
                {
                    meter.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            if (Tag(tags, FluentCmsTelemetry.Tags.Handler) == typeof(SlowPing).FullName)
            {
                durations.Enqueue((value, Tag(tags, FluentCmsTelemetry.Tags.Operation), Tag(tags, FluentCmsTelemetry.Tags.Outcome)));
            }
        });
        listener.Start();
        seen.Behavior = (_, _) => Task.Delay(300, Token);
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Events.AddHandler<Ping, SlowPing>();
            cms.Events.Handlers.SlowThreshold = TimeSpan.FromMilliseconds(100);
        }));

        await database.AsAsync(Editor, async services =>
        {
            await services.GetRequiredService<IEventBus>().PublishAsync(new Ping("slow"), Token);
            return true;
        });

        logs.Count("HandlerSlow").ShouldBe(1);
        logs.Count("HandlerAbandoned").ShouldBe(0);
        var duration = durations.ShouldHaveSingleItem();
        duration.Event.ShouldBe(nameof(Ping));
        duration.Outcome.ShouldBe("succeeded");
        duration.Seconds.ShouldBeGreaterThan(0.2);
    }

    [Fact]
    public async Task TheEventsOfAJobCarryTheSiteOfTheJobAndRunThereInTheDefaultLocaleOfThatSite()
    {
        using var fixture = new JobFixture();
        var seen = new Seen();
        var site = Guid.CreateVersion7();
        OperationContext? seenOperation = null;
        using var instance = fixture.Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton(seen);
            cms.Events.AddHandler<JobDeadLetteredEvent, DeadHandler>();
            cms.Events.AddOutboxHandler<JobDeadLetteredEvent, DeadOutbox>("dead");
        });
        seen.Behavior = (@event, _) =>
        {
            if (@event is JobDeadLetteredEvent && seenOperation is null)
            {
                seenOperation = instance.GetRequiredService<IOperationContextAccessor>().Current;
            }

            return Task.CompletedTask;
        };
        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            context.Add(new Site { Id = site, Name = "Other", Slug = "other", DefaultLocale = "fr", CreatedAt = DateTimeOffset.UtcNow });
            context.Add(new SiteLocale { SiteId = site, Code = "fr", Position = 0, Enabled = true });
            context.SaveChanges();
        }

        var id = fixture.AddRow("no-such-job", "{}", maxAttempts: 1);
        using (var scope = fixture.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().Where(job => job.Id == id).ExecuteUpdate(job => job.SetProperty(column => column.SiteId, site));
        }

        await instance.GetRequiredService<JobExecutor>().RunNextAsync("default", "worker", Token);
        await DrainAsync(instance);

        var operation = seenOperation.ShouldNotBeNull();
        operation.Site.ShouldBe(new SiteId(site));
        operation.Locale.ShouldBe("fr");
        var dead = seen.Of<JobDeadLetteredEvent>().ToList();
        dead.Count.ShouldBe(2);
        dead.ShouldAllBe(heard => heard.Source.SiteId == site);
        using var check = fixture.Services.CreateScope();
        check.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().Single().SiteId.ShouldBe(site);
    }

    [Fact]
    public async Task AJobErrorThatQuotesASecretIsScrubbedBeforeItReachesAnEventOrTheOutbox()
    {
        using var fixture = new JobFixture();
        var seen = new Seen();
        fixture.Probe.Behavior = (_, _) => throw new InvalidOperationException("POST https://hooks.slack.com/services/T00/B00/XXSECRETXX failed, token=s3cr3t");
        using var instance = fixture.NewInstance(cms: cms =>
        {
            cms.Services.AddSingleton(seen);
            cms.Events.AddOutboxHandler<JobFailedEvent, FailedOutbox>("failed");
        });
        await fixture.EnqueueAsync(options: new FluentCMS.Jobs.JobOptions { MaxAttempts = 1 });

        await instance.GetRequiredService<JobExecutor>().RunNextAsync("default", "worker", Token);

        using var check = fixture.Services.CreateScope();
        var payload = check.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().Single().Payload;
        payload.ShouldContain("https://hooks.slack.com");
        payload.ShouldNotContain("XXSECRETXX");
        payload.ShouldNotContain("s3cr3t");
        fixture.Events.Failed.Single().Error.ShouldNotContain("XXSECRETXX");
    }

    [Fact]
    public async Task AHandlerErrorThatQuotesASecretIsScrubbedBeforeItIsStored()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => throw new InvalidOperationException("POST https://hooks.slack.com/services/T00/B00/XXSECRETXX failed, token=s3cr3t, password: \"hunter2\", Authorization: Bearer abcdef123456");
        using var database = new ServiceDatabase(Registrations(seen, cms => cms.Events.Outbox.MaxAttempts = 1));
        await database.CreateAsync(Editor, Titled("T"));

        await DrainAsync(database.Database.Services);

        var error = database.Rows().Single().LastError!;
        error.ShouldContain("https://hooks.slack.com failed");
        foreach (var secret in new[] { "XXSECRETXX", "s3cr3t", "hunter2", "abcdef123456" })
        {
            error.ShouldNotContain(secret);
        }
    }

    [Theory]
    [InlineData("see https://a.example/p/q?x=1#f now", "see https://a.example now")]
    [InlineData("https://user:pw@a.example:8443/p", "https://a.example:8443")]
    [InlineData("api_key=abc&other=1", "api_key=***&other=1")]
    [InlineData("{\"clientSecret\":\"abc\",\"name\":\"n\"}", "{\"clientSecret\":***,\"name\":\"n\"}")]
    [InlineData("Authorization: Bearer abcdefghijkl", "Authorization: *** ***")]
    [InlineData("Authorization: ApiKey fcms_mk_AbCdEfGh-IjKl_MnOp", "Authorization: *** ***")]
    [InlineData("request failed with ApiKey fcms_dk_AbCdEfGh-IjKl_MnOp", "request failed with ApiKey ***")]
    [InlineData("key fcms_pk_AbCdEfGh-IjKl_MnOp was refused", "key fcms_pk_*** was refused")]
    [InlineData("X-Api-Key: fcms_mk_AbCdEfGh-IjKl_MnOp", "X-Api-Key: ***")]
    [InlineData("sent key_fcms_mk_AbCdEfGh-IjKl_MnOp", "sent key_fcms_mk_***")]
    [InlineData("nothing to hide here", "nothing to hide here")]
    public void AUrlIsReducedToItsHostAndTheValueOfASecretKeyIsMasked(string text, string expected) =>
        SecretScrubber.Of(text).ShouldBe(expected);

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }
}
