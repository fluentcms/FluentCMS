using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Events;

public sealed class OutboxTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static Task<int> DrainAsync(IServiceProvider instance) =>
        instance.GetRequiredService<OutboxDrainer>().DrainAsync(Token);

    // Each drain claims a batch while the drains before it hold theirs, then all of them carry on at once and share out the
    // rows that are left.
    private static async Task<int[]> DrainTogetherAsync(Seen seen, params IServiceProvider[] instances)
    {
        using var holding = new SemaphoreSlim(0);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        seen.Behavior = async (_, _) =>
        {
            holding.Release();
            await go.Task;
        };

        List<Task<int>> drains = [];
        foreach (var instance in instances)
        {
            drains.Add(Task.Run(() => DrainAsync(instance), Token));
            (await holding.WaitAsync(TimeSpan.FromSeconds(30), Token)).ShouldBeTrue();
        }

        go.SetResult();
        return await Task.WhenAll(drains);
    }

    private static async Task PruneAsync(IServiceProvider instance)
    {
        using var scope = instance.CreateScope();
        var operation = new OperationContext(Principal.System, Origin.Job, new SiteId(Guid.Empty), "en", null, "prune", DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<OutboxPruneJob>().ExecuteAsync(
            new OutboxPrunePayload(),
            new JobContext(Guid.CreateVersion7(), OutboxPruneJob.Type, JobRunnerOptions.DefaultQueue, 1, 3, operation, Token));
    }

    // The update that claims a batch is the only one on cms_outbox that adds to the attempts.
    private sealed class ClaimCounter : DbCommandInterceptor
    {
        private int _claims;
        private int _writes;

        public int Claims => Volatile.Read(ref _claims);

        public int Writes => Volatile.Read(ref _writes);

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var text = command.CommandText;
            if (text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && text.Contains("cms_outbox", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _writes);
                if (text.Contains($"{TestProvider.Quote("attempts")} + 1", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref _claims);
                }
            }

            return ValueTask.FromResult(result);
        }
    }

    // Counts the reads of the default locale of a site.
    private sealed class LocaleReads : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            var text = command.CommandText;
            if (text.Contains(TestProvider.Quote("default_locale"), StringComparison.Ordinal) && !text.Contains("cms_site_locale", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task TheRowIsWrittenWithTheWriteAndNamesTheSameEventAsTheHandlersOfThisProcess()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>()));

        var entry = await database.CreateAsync(Editor, Titled("T"));

        var row = database.Rows().ShouldHaveSingleItem();
        row.Handler.ShouldBe("saved-outbox");
        row.Status.ShouldBe(OutboxStatus.Pending);
        row.Attempts.ShouldBe(0);
        row.SiteId.ShouldBe(SiteA.Value);
        row.Payload.ShouldContain(IdOf(entry).ToString());
        row.EventId.ShouldBe(seen.Of<EntryDraftSavedEvent>().Single().EventId);
        row.EventType.ShouldBe(typeof(EntryDraftSavedEvent).FullName);
    }

    [Fact]
    public async Task TheDrainDeliversTheEventOnceAndMarksTheRow()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));
        var entry = await database.CreateAsync(Editor, Titled("T"));

        (await DrainAsync(database.Database.Services)).ShouldBe(1);
        (await DrainAsync(database.Database.Services)).ShouldBe(0);

        var heard = seen.Of<EntryDraftSavedEvent>().ShouldHaveSingleItem();
        heard.EntryId.ShouldBe(IdOf(entry));
        heard.Source.ActorId.ShouldBe(Editor.Id);
        var delivery = seen.Deliveries.ShouldHaveSingleItem();
        delivery.Attempt.ShouldBe(1);

        var row = database.Rows().Single();
        row.Status.ShouldBe(OutboxStatus.Delivered);
        row.DeliveredAt.ShouldNotBeNull();
        row.LeaseOwner.ShouldBeNull();
        delivery.Key.ShouldBe(row.Id);
    }

    [Fact]
    public async Task AnOutboxHandlerGetsTheEventAfterACrashBetweenTheCommitAndTheDispatch()
    {
        var seen = new Seen();
        var faults = new JobFaults();
        var logs = new RecordingLogs();
        using var crashing = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(faults));
        }));

        // The process loses the database as soon as the write has committed: nothing asks for the delivery, and none runs.
        faults.Fail(command => command.CommandText.Contains("cms_job", StringComparison.Ordinal));
        var entry = await crashing.CreateAsync(Editor, Titled("T"));

        logs.Count("KickFailed").ShouldBe(1);
        seen.Events.ShouldBeEmpty();
        crashing.Rows().ShouldHaveSingleItem().Status.ShouldBe(OutboxStatus.Pending);

        // A process that starts later finds the row and delivers it.
        using var restarted = crashing.Database.NewInstance(EventRegistrations.Outbox(seen));
        (await DrainAsync(restarted)).ShouldBe(1);

        seen.Of<EntryDraftSavedEvent>().ShouldHaveSingleItem().EntryId.ShouldBe(IdOf(entry));
        crashing.Rows().Single().Status.ShouldBe(OutboxStatus.Delivered);
    }

    [Fact]
    public async Task AWriteThatRollsBackLeavesNoRowForTheOutbox()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));

        await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, Titled(new string('x', 41))));

        database.Rows().ShouldBeEmpty();
        (await DrainAsync(database.Database.Services)).ShouldBe(0);
        seen.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task InstancesDrainingTogetherDeliverEachEventOnce()
    {
        const int entries = 12;
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 2));
        for (var index = 0; index < entries; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        using var second = database.Database.NewInstance(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 2));
        using var third = database.Database.NewInstance(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 2));

        var delivered = await DrainTogetherAsync(seen, database.Database.Services, second, third);

        delivered.Sum().ShouldBe(entries);
        seen.Deliveries.Select(delivery => delivery.Key).Distinct().Count().ShouldBe(entries);
        seen.Of<EntryDraftSavedEvent>().Select(heard => heard.EventId).Distinct().Count().ShouldBe(entries);
        seen.Deliveries.ShouldAllBe(delivery => delivery.Attempt == 1);
        database.Rows().ShouldAllBe(row => row.Status == OutboxStatus.Delivered);
    }

    // Each claim has an owner of its own, so a drain reads back only the rows its own claim won.
    [Fact]
    public async Task DrainsOfOneInstanceRunningTogetherDeliverEachEventOnce()
    {
        const int entries = 12;
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 2));
        for (var index = 0; index < entries; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        var instance = database.Database.Services;
        var delivered = await DrainTogetherAsync(seen, instance, instance, instance);

        delivered.Sum().ShouldBe(entries);
        seen.Deliveries.Count.ShouldBe(entries);
        seen.Deliveries.Select(delivery => delivery.Key).Distinct().Count().ShouldBe(entries);
        database.Rows().ShouldAllBe(row => row.Status == OutboxStatus.Delivered);
    }

    [Fact]
    public async Task ABatchIsClaimedWithOneStatement()
    {
        var seen = new Seen();
        var claims = new ClaimCounter();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.Outbox.BatchSize = 3;
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(claims));
        }));
        for (var index = 0; index < 6; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        (await DrainAsync(database.Database.Services)).ShouldBe(6);

        claims.Claims.ShouldBe(2);
    }

    [Fact]
    public async Task ABatchOfEventsIsClaimedAndSettledWithOneWriteEach()
    {
        var seen = new Seen();
        var claims = new ClaimCounter();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(claims))));
        for (var index = 0; index < 8; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        (await DrainAsync(database.Database.Services)).ShouldBe(8);

        claims.Claims.ShouldBe(1);
        claims.Writes.ShouldBe(2);
        database.Rows().ShouldAllBe(row => row.Status == OutboxStatus.Delivered && row.Attempts == 1 && row.LeaseOwner == null);
    }

    [Fact]
    public async Task AHandlerThatBlocksDoesNotHoldBackTheDeliveriesOfAnother()
    {
        var seen = new Seen();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = 0;
        seen.Behavior = (_, _) => seen.Calls.Contains("outbox-1") && Interlocked.CompareExchange(ref blocked, 1, 0) == 0 ? gate.Task : Task.CompletedTask;
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxFirst>("first");
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxSecond>("second");
        }));
        for (var index = 0; index < 3; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        var drain = DrainAsync(database.Database.Services);
        try
        {
            await Eventually.HoldsAsync(() => database.Rows().Count(row => row.Handler == "second" && row.Status == OutboxStatus.Delivered) == 3);
            seen.Calls.Count(call => call == "outbox-1").ShouldBe(1);
        }
        finally
        {
            gate.TrySetResult();
        }

        (await drain).ShouldBe(6);
        database.Rows().ShouldAllBe(row => row.Status == OutboxStatus.Delivered);
    }

    [Fact]
    public async Task AKickStartsALaneForANewHandlerWhileAnotherHandlerHangs()
    {
        var seen = new Seen();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        seen.Behavior = (_, _) => seen.Calls.Last() == "outbox-1" ? gate.Task : Task.CompletedTask;
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxFirst>("first");
            cms.Events.AddOutboxHandler<EntryTrashedEvent, TrashedOutbox>("trashed");
        }));
        var entry = await database.CreateAsync(Editor, Titled("T"));

        var drain = DrainAsync(database.Database.Services);
        try
        {
            await Eventually.HoldsAsync(() => seen.Calls.Contains("outbox-1"));
            await database.DeleteAsync(Editor, IdOf(entry));

            await Eventually.HoldsAsync(() => database.Rows().Any(row => row.Handler == "trashed" && row.Status == OutboxStatus.Delivered));
            database.Rows().Single(row => row.Handler == "first").Status.ShouldBe(OutboxStatus.Pending);
        }
        finally
        {
            gate.TrySetResult();
        }

        (await drain).ShouldBe(2);
    }

    [Fact]
    public async Task TheEventsOfOneHandlerAreDeliveredInTheOrderTheyHappened()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxFirst>("first");
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxSecond>("second");
            cms.Events.Outbox.BatchSize = 2;
        }));
        for (var index = 0; index < 5; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        (await DrainAsync(database.Database.Services)).ShouldBe(10);

        var rows = database.Rows();
        var handlerOf = rows.ToDictionary(row => row.Id, row => row.Handler);
        foreach (var handler in new[] { "first", "second" })
        {
            seen.Deliveries.Select(delivery => delivery.Key).Where(key => handlerOf[key] == handler)
                .ShouldBe(rows.Where(row => row.Handler == handler).Select(row => row.Id));
        }
    }

    [Fact]
    public async Task ABatchReadsTheDefaultLocaleOfItsSiteOnce()
    {
        var seen = new Seen();
        var reads = new LocaleReads();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(reads))));
        for (var index = 0; index < 4; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        var before = reads.Count;
        (await DrainAsync(database.Database.Services)).ShouldBe(4);

        (reads.Count - before).ShouldBe(1);
    }

    [Fact]
    public async Task AFailingHandlerIsCalledAgainWithTheSameKeyUntilItSucceeds()
    {
        var seen = new Seen();
        seen.Behavior = (_, delivery) => delivery!.Attempt < 3 ? throw new InvalidOperationException("The endpoint is down.") : Task.CompletedTask;
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));
        await database.CreateAsync(Editor, Titled("T"));

        (await DrainAsync(database.Database.Services)).ShouldBe(1);

        seen.Deliveries.Select(delivery => delivery.Attempt).ShouldBe([1, 2, 3]);
        seen.Deliveries.Select(delivery => delivery.Key).Distinct().ShouldHaveSingleItem();
        var row = database.Rows().Single();
        row.Status.ShouldBe(OutboxStatus.Delivered);
        row.Attempts.ShouldBe(3);
        row.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task AHandlerThatKeepsFailingIsLeftAsFailedAfterItsAttempts()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => throw new InvalidOperationException("The endpoint is down.");
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.MaxAttempts = 3));
        await database.CreateAsync(Editor, Titled("T"));

        (await DrainAsync(database.Database.Services)).ShouldBe(0);
        (await DrainAsync(database.Database.Services)).ShouldBe(0);

        seen.Deliveries.Count.ShouldBe(3);
        var row = database.Rows().Single();
        row.Status.ShouldBe(OutboxStatus.Failed);
        row.Attempts.ShouldBe(3);
        row.LastError!.ShouldContain("The endpoint is down.");
    }

    [Fact]
    public async Task AFailingHandlerDoesNotHoldBackTheOthers()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => seen.Calls.Last() == "outbox-1" ? throw new InvalidOperationException("The endpoint is down.") : Task.CompletedTask;
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxFirst>("first");
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxSecond>("second");
            cms.Events.Outbox.MaxAttempts = 2;
            cms.Events.Outbox.RetryBaseDelay = TimeSpan.Zero;
            cms.Events.Outbox.RetryMaxDelay = TimeSpan.Zero;
            cms.Events.Outbox.RetryJitter = 0;
        }));
        await database.CreateAsync(Editor, Titled("T"));

        (await DrainAsync(database.Database.Services)).ShouldBe(1);

        database.Rows().ToDictionary(row => row.Handler, row => row.Status).ShouldBe(new Dictionary<string, string>
        {
            ["first"] = OutboxStatus.Failed,
            ["second"] = OutboxStatus.Delivered,
        });
    }

    // Each handler has a lane of its own, so their order holds for the rows of an event, and not for the calls.
    [Fact]
    public async Task OutboxHandlersAreRecordedInHookOrder()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxThird>("third");
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxFirst>("first");
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, OutboxSecond>("second");
        }));
        await database.CreateAsync(Editor, Titled("T"));

        database.Rows().Select(row => row.Handler).ShouldBe(["first", "second", "third"]);
        await DrainAsync(database.Database.Services);

        seen.Calls.ShouldBe(["outbox-1", "outbox-2", "outbox-3"], ignoreOrder: true);
    }

    [Fact]
    public async Task OutboxHandlersFollowThePluginDependencyRankWhenTheirOrderIsEqual()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Services.AddSingleton<IHookTiebreaker, OutboxRank>();
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, SavedOutbox>("registered-first");
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, PluginOutbox>("registered-last");
        }));
        await database.CreateAsync(Editor, Titled("T"));

        database.Rows().Select(row => row.Handler).ShouldBe(["registered-last", "registered-first"]);
        await DrainAsync(database.Database.Services);

        seen.Calls.ShouldBe(["plugin-outbox", "outbox"], ignoreOrder: true);
    }

    [Fact]
    public async Task ADeliveryHeldByADeadWorkerIsTakenOverWhenItsLeaseEnds()
    {
        var seen = new Seen();
        var clock = new ManualTimeProvider(JobFixture.Start);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Services.AddSingleton<TimeProvider>(clock)));
        await database.CreateAsync(Editor, Titled("T"));

        // The first worker claimed the row and died before settling it.
        database.Read(context => context.Set<OutboxMessage>().ExecuteUpdate(row => row
            .SetProperty(column => column.LeaseOwner, "dead/worker")
            .SetProperty(column => column.LeaseUntil, clock.GetUtcNow() + TimeSpan.FromMinutes(5))
            .SetProperty(column => column.Attempts, 1)));

        (await DrainAsync(database.Database.Services)).ShouldBe(0);
        seen.Events.ShouldBeEmpty();

        clock.Advance(TimeSpan.FromMinutes(6));
        (await DrainAsync(database.Database.Services)).ShouldBe(1);

        seen.Deliveries.ShouldHaveSingleItem().Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task DeliveredRowsAreKeptForTheRetentionAndThenRemoved()
    {
        var seen = new Seen();
        var clock = new ManualTimeProvider(JobFixture.Start);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(clock);
            cms.Events.Outbox.Retention = TimeSpan.FromDays(1);
        }));
        await database.CreateAsync(Editor, Titled("T"));
        await DrainAsync(database.Database.Services);

        clock.Advance(TimeSpan.FromHours(23));
        await PruneAsync(database.Database.Services);
        database.Rows().Count.ShouldBe(1);

        clock.Advance(TimeSpan.FromHours(2));
        await DrainAsync(database.Database.Services);
        database.Rows().Count.ShouldBe(1);

        await PruneAsync(database.Database.Services);
        database.Rows().ShouldBeEmpty();
    }

    // Counted from when the row failed, not from when its event happened, so that a delivery that failed late stays as long.
    [Fact]
    public async Task FailedRowsStayUnlessAFailedRetentionIsSetAndThenGoThatLongAfterTheyFailed()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => throw new InvalidOperationException("The endpoint is down.");
        var clock = new ManualTimeProvider(JobFixture.Start);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(clock);
            cms.Events.Outbox.MaxAttempts = 1;
        }));
        await database.CreateAsync(Editor, Titled("T"));
        clock.Advance(TimeSpan.FromDays(10));
        await DrainAsync(database.Database.Services);
        var failed = database.Rows().Single();
        failed.Status.ShouldBe(OutboxStatus.Failed);
        failed.AvailableAt.ShouldBe(clock.GetUtcNow());

        using var keeping = database.Database.NewInstance(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(clock);
            cms.Events.Outbox.FailedRetention = TimeSpan.FromDays(1);
        }));
        clock.Advance(TimeSpan.FromHours(23));
        await PruneAsync(keeping);
        database.Rows().ShouldHaveSingleItem();

        clock.Advance(TimeSpan.FromDays(400));
        await PruneAsync(database.Database.Services);
        database.Rows().ShouldHaveSingleItem();

        await PruneAsync(keeping);
        database.Rows().ShouldBeEmpty();
    }

    [Fact]
    public async Task ThePruneRemovesInBatchesAndLeavesPendingRows()
    {
        var seen = new Seen();
        var clock = new ManualTimeProvider(JobFixture.Start);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(clock);
            cms.Events.Outbox.Retention = TimeSpan.Zero;
            cms.Events.Outbox.PruneBatchSize = 2;
        }));
        for (var index = 0; index < 5; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        await DrainAsync(database.Database.Services);
        await database.CreateAsync(Editor, Titled("Pending"));
        clock.Advance(TimeSpan.FromSeconds(1));

        await PruneAsync(database.Database.Services);

        database.Rows().ShouldHaveSingleItem().Status.ShouldBe(OutboxStatus.Pending);
    }

    [Fact]
    public async Task ARunningJobRunnerDeliversAfterTheCommit()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Jobs.Options.PollInterval = TimeSpan.FromMilliseconds(10)));
        var runner = database.Database.Services.GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        try
        {
            var entry = await database.CreateAsync(Editor, Titled("T"));

            await Eventually.HoldsAsync(() => !seen.Deliveries.IsEmpty);
            seen.Of<EntryDraftSavedEvent>().ShouldHaveSingleItem().EntryId.ShouldBe(IdOf(entry));
        }
        finally
        {
            await runner.StopAsync(Token);
        }
    }

    [Fact]
    public async Task WritesAskForOneDrainWhileOneIsWaiting()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));

        for (var index = 0; index < 3; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        database.Read(context => context.Set<Job>().AsNoTracking().Count(job => job.Type == OutboxDrainJob.Type)).ShouldBe(1);
    }

    [Fact]
    public async Task AWriteWithNoOutboxHandlerRecordsNothingAndAsksForNoDrain()
    {
        using var database = new ServiceDatabase();

        await database.CreateAsync(Editor, Titled("T"));

        database.Rows().ShouldBeEmpty();
        database.Read(context => context.Set<Job>().AsNoTracking().Count()).ShouldBe(0);
    }

    [Fact]
    public async Task AKickAfterAFailedDrainAsksForADrainThatIsDueNow()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen));

        // The retry of a drain that failed waits for its backoff.
        database.Read(context =>
        {
            context.Add(new Job
            {
                Type = OutboxDrainJob.Type,
                Payload = "{}",
                Queue = OutboxDrainJob.Queue,
                RunAt = DateTimeOffset.UtcNow.AddMinutes(7),
                MaxAttempts = 5,
                Attempts = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                Origin = Origin.Job,
            });
            return context.SaveChanges();
        });

        await database.CreateAsync(Editor, Titled("T"));

        var now = DateTimeOffset.UtcNow;
        database.Read(context => context.Set<Job>().AsNoTracking().Count(job => job.Type == OutboxDrainJob.Type && job.RunAt <= now)).ShouldBe(1);
    }

    [Fact]
    public async Task StartupStoresThePruneAndRemovesTheRecurringDrainOfEarlierVersions()
    {
        using var database = new ServiceDatabase(EventRegistrations.Outbox(new Seen()));
        await database.Database.Services.GetRequiredService<IJobScheduler>().AddRecurringAsync<OutboxDrainJob, OutboxDrainPayload>(
            OutboxDrainJob.RecurringName, "* * * * *", new OutboxDrainPayload(), OutboxDrainJob.Queue, Token);

        var schedule = database.Database.Services.GetServices<IHostedService>().OfType<OutboxScheduleService>().Single();
        await schedule.StartAsync(Token);
        try
        {
            await Eventually.HoldsAsync(() => database.Read(context => context.Set<RecurringJob>().AsNoTracking().Select(job => job.Name).ToList()) is [OutboxPruneJob.RecurringName]);
        }
        finally
        {
            await schedule.StopAsync(Token);
        }

        var prune = database.Read(context => context.Set<RecurringJob>().AsNoTracking().Single());
        prune.Type.ShouldBe(OutboxPruneJob.Type);
        prune.Cron.ShouldBe("0 * * * *");
        prune.Queue.ShouldBe(JobRunnerOptions.DefaultQueue);
        prune.Enabled.ShouldBeTrue();
    }

    // The sweep reads and asks for a drain only when rows are due, where a recurring drain added a job every minute.
    [Fact]
    public async Task AnIdleInstanceAddsNoJobForTheOutboxAndTheSweepCatchesWhatNothingAskedFor()
    {
        var clock = new ManualTimeProvider(JobFixture.Start);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(new Seen(), cms => cms.Services.AddSingleton<TimeProvider>(clock)));
        var schedule = database.Database.Services.GetServices<IHostedService>().OfType<OutboxScheduleService>().Single();
        await schedule.StartAsync(Token);
        try
        {
            await Eventually.HoldsAsync(() => database.Read(context => context.Set<RecurringJob>().AsNoTracking().Count()) == 1);
            for (var minute = 0; minute < 5; minute++)
            {
                clock.Advance(OutboxScheduleService.SweepInterval);
            }

            database.Read(context => context.Set<Job>().AsNoTracking().Count()).ShouldBe(0);

            // A row that nothing asked to deliver, as after a crash between the commit and the kick.
            database.Read(context =>
            {
                context.Add(new OutboxMessage
                {
                    EventId = Guid.CreateVersion7(),
                    EventType = typeof(EntryDraftSavedEvent).FullName!,
                    Handler = "saved-outbox",
                    Payload = "{}",
                    OccurredAt = clock.GetUtcNow(),
                    Sequence = 1,
                    AvailableAt = clock.GetUtcNow(),
                });
                return context.SaveChanges();
            });

            await Eventually.HoldsAsync(
                () => database.Read(context => context.Set<Job>().AsNoTracking().Count(job => job.Type == OutboxDrainJob.Type)) == 1,
                () => clock.Advance(OutboxScheduleService.SweepInterval));
        }
        finally
        {
            await schedule.StopAsync(Token);
        }
    }

    [Fact]
    public async Task TheSweepLeavesADeliveryWhoseWorkerDiedOnItsLastAttemptAsFailed()
    {
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(new Seen(), cms =>
        {
            cms.Events.Outbox.MaxAttempts = 2;
            cms.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs);
        }));
        await database.CreateAsync(Editor, Titled("T"));
        database.Read(context => context.Set<OutboxMessage>().ExecuteUpdate(row => row
            .SetProperty(column => column.LeaseOwner, "dead/worker")
            .SetProperty(column => column.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))
            .SetProperty(column => column.Attempts, 2)));
        var drainer = database.Database.Services.GetRequiredService<OutboxDrainer>();

        (await drainer.SweepAsync(Token)).ShouldBeFalse();
        (await drainer.SweepAsync(Token)).ShouldBeFalse();

        database.Rows().Single().Status.ShouldBe(OutboxStatus.Failed);
        logs.Count("Exhausted").ShouldBe(1);
    }

    [Fact]
    public async Task ADeliveryTakenOverFromAWorkerWhoseLeaseLapsedIsClaimedAlone()
    {
        var seen = new Seen();
        var claims = new ClaimCounter();
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(claims))));
        for (var index = 0; index < 3; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        // A worker took the first two and died, perhaps of the first one's delivery.
        var first = database.Rows().Take(2).Select(row => row.Id).ToList();
        database.Read(context => context.Set<OutboxMessage>().Where(row => first.Contains(row.Id)).ExecuteUpdate(row => row
            .SetProperty(column => column.LeaseOwner, "dead/worker")
            .SetProperty(column => column.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))
            .SetProperty(column => column.Attempts, 1)));

        (await DrainAsync(database.Database.Services)).ShouldBe(3);

        claims.Claims.ShouldBe(3);
        database.Rows().Select(row => row.Attempts).ShouldBe([2, 2, 1]);
    }

    [Fact]
    public async Task AHostWithNoOutboxHandlerSchedulesNoDrain()
    {
        using var database = new ServiceDatabase();

        var schedule = database.Database.Services.GetServices<IHostedService>().OfType<OutboxScheduleService>().Single();
        await schedule.StartAsync(Token);
        await schedule.ExecuteTask!;

        database.Read(context => context.Set<RecurringJob>().AsNoTracking().Count()).ShouldBe(0);
    }

    [Fact]
    public async Task TheScheduleIsRetriedUntilItIsStoredWhenTheDatabaseIsDownAtStartup()
    {
        var faults = new JobFaults();
        var logs = new RecordingLogs();
        var clock = new ManualTimeProvider(JobFixture.Start);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(new Seen(), cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(clock);
            cms.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(faults));
        }));
        faults.Fail(command => command.CommandText.Contains("cms_recurring_job", StringComparison.Ordinal));

        var schedule = database.Database.Services.GetServices<IHostedService>().OfType<OutboxScheduleService>().Single();
        await schedule.StartAsync(Token);
        await Eventually.HoldsAsync(() => logs.Count("RecurringFailed") >= 1);
        database.Read(context => context.Set<RecurringJob>().AsNoTracking().Count()).ShouldBe(0);

        faults.Recover();
        try
        {
            await Eventually.HoldsAsync(
                () => database.Read(context => context.Set<RecurringJob>().AsNoTracking().Count()) == 1,
                () => clock.Advance(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            await schedule.StopAsync(Token);
        }

        database.Read(context => context.Set<RecurringJob>().AsNoTracking().Single()).Name.ShouldBe(OutboxPruneJob.RecurringName);
    }

    [Fact]
    public void TwoHandlersUnderOneNameAreRefused()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, cms =>
        {
            cms.Events.AddOutboxHandler<EntryDraftSavedEvent, SavedOutbox>("same");
            cms.Events.AddOutboxHandler<EntryTrashedEvent, TrashedOutbox>("same");
        }));

        Should.Throw<FluentCmsConfigurationException>(() => database.Database.Services.GetRequiredService<OutboxRegistry>())
            .Message.ShouldContain("FCMS2601");
    }

    [Fact]
    public void AnInvalidOutboxOptionIsRefusedAtStartup()
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceDatabase(cms => cms.Events.Outbox.BatchSize = 0));

        exception.Message.ShouldContain("FCMS2603");
    }

    [Fact]
    public void AHandlerNameTooLongForTheColumnIsRefused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceDatabase(cms => cms.Events.AddOutboxHandler<EntryDraftSavedEvent, SavedOutbox>(new string('n', 129))));

    private sealed class PluginOutbox(Seen seen) : IOutboxEventHandler<EntryDraftSavedEvent>
    {
        public ValueTask HandleAsync(EntryDraftSavedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => seen.HeardAsync("plugin-outbox", message, delivery, cancellationToken);
    }

    private sealed class OutboxRank : IHookTiebreaker
    {
        public int Rank(Type hookType) => hookType == typeof(PluginOutbox) ? -1 : 0;
    }
}
