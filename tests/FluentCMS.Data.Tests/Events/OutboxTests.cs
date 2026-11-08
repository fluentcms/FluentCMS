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

    private static async Task PruneAsync(IServiceProvider instance)
    {
        using var scope = instance.CreateScope();
        var operation = new OperationContext(Principal.System, Origin.Job, new SiteId(Guid.Empty), "en", null, "prune", DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<OutboxPruneJob>().ExecuteAsync(
            new OutboxPrunePayload(),
            new JobContext(Guid.CreateVersion7(), OutboxPruneJob.Type, JobRunnerOptions.DefaultQueue, 1, 3, operation, Token));
    }

    // The update that claims a batch is the only one on cms_outbox that does not name the attempts.
    private sealed class ClaimCounter : DbCommandInterceptor
    {
        private int _claims;

        public int Claims => Volatile.Read(ref _claims);

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var text = command.CommandText;
            if (text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && text.Contains("cms_outbox", StringComparison.Ordinal) && !text.Contains("attempts", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _claims);
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
        seen.Behavior = async (_, _) => await Task.Delay(3);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 4));
        for (var index = 0; index < entries; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        using var second = database.Database.NewInstance(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 4));
        using var third = database.Database.NewInstance(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 4));

        var delivered = await Task.WhenAll(
            Task.Run(() => DrainAsync(database.Database.Services), Token),
            Task.Run(() => DrainAsync(second), Token),
            Task.Run(() => DrainAsync(third), Token));

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
        seen.Behavior = async (_, _) => await Task.Delay(3);
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms => cms.Events.Outbox.BatchSize = 4));
        for (var index = 0; index < entries; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        var delivered = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => DrainAsync(database.Database.Services), Token)));

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

    [Fact]
    public async Task OutboxHandlersAreDeliveredInHookOrder()
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

        seen.Calls.ShouldBe(["outbox-1", "outbox-2", "outbox-3"]);
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

        await DrainAsync(database.Database.Services);

        seen.Calls.ShouldBe(["plugin-outbox", "outbox"]);
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
        using var database = new ServiceDatabase(EventRegistrations.Outbox(seen, cms =>
        {
            cms.Events.Outbox.Retention = TimeSpan.Zero;
            cms.Events.Outbox.PruneBatchSize = 2;
        }));
        for (var index = 0; index < 5; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        await DrainAsync(database.Database.Services);
        await database.CreateAsync(Editor, Titled("Pending"));
        await Task.Delay(5, Token);

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
    public async Task StartupSchedulesTheRecurringDrainThatCatchesWhatNothingAskedFor()
    {
        using var database = new ServiceDatabase(EventRegistrations.Outbox(new Seen()));

        var schedule = database.Database.Services.GetServices<IHostedService>().OfType<OutboxScheduleService>().Single();
        await schedule.StartAsync(Token);
        await schedule.ExecuteTask!;

        var recurring = database.Read(context => context.Set<RecurringJob>().AsNoTracking().Single(job => job.Name == OutboxDrainJob.RecurringName));
        recurring.Type.ShouldBe(OutboxDrainJob.Type);
        recurring.Cron.ShouldBe("* * * * *");
        recurring.Queue.ShouldBe(OutboxDrainJob.Queue);
        recurring.Payload.ShouldBe("{}");
        recurring.Enabled.ShouldBeTrue();

        var prune = database.Read(context => context.Set<RecurringJob>().AsNoTracking().Single(job => job.Name == OutboxPruneJob.RecurringName));
        prune.Type.ShouldBe(OutboxPruneJob.Type);
        prune.Cron.ShouldBe("0 * * * *");
        prune.Queue.ShouldBe(JobRunnerOptions.DefaultQueue);
        prune.Enabled.ShouldBeTrue();
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
        clock.Advance(TimeSpan.FromSeconds(30));
        await Eventually.CompletesAsync(schedule.ExecuteTask!, () => clock.Advance(TimeSpan.FromSeconds(30)));

        database.Read(context => context.Set<RecurringJob>().AsNoTracking().Count()).ShouldBe(2);
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
