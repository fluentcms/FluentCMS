using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Events;
using FluentCMS.Hooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Events;

// Failures injected at the stage of the outbox row, at the commit, at the settle of a delivery, in the lease of a worker
// that is still running, and in the process that delivers.
public sealed class OutboxFailureTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static Task<int> DrainAsync(IServiceProvider instance) =>
        instance.GetRequiredService<OutboxDrainer>().DrainAsync(Token);

    private static Action<FluentCMS.Core.DependencyInjection.FluentCmsBuilder> Fast(Action<FluentCMS.Core.DependencyInjection.FluentCmsBuilder>? more = null) => cms =>
    {
        cms.Events.AddOutboxHandler<EntryDraftSavedEvent, SavedOutbox>("saved-outbox");
        cms.Events.Outbox.RetryBaseDelay = TimeSpan.Zero;
        cms.Events.Outbox.RetryMaxDelay = TimeSpan.Zero;
        cms.Events.Outbox.RetryJitter = 0;
        more?.Invoke(cms);
    };

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public volatile bool Armed;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            Armed ? throw new InvalidOperationException("The database went away at the commit.") : ValueTask.FromResult(result);
    }

    private sealed class PoisoningHandler(CmsDbContext context) : IEventHandler<EntryDraftSavedEvent>
    {
        public ValueTask HandleAsync(EntryDraftSavedEvent message, CancellationToken cancellationToken)
        {
            context.Add(new Site { Name = "Duplicate", Slug = "a", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
            throw new InvalidOperationException("The handler left a mess in the context.");
        }
    }

    // Each attempt the drain starts is the update that adds one to the attempts of the row.
    private sealed class AttemptStarts : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (result == 1 && command.CommandText.Contains("cms_outbox", StringComparison.Ordinal) && command.CommandText.Contains("\"attempts\" + 1", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return ValueTask.FromResult(result);
        }
    }

    // The cut-off of a delivery is armed before the thread pool runs the handler, so on a busy machine it can use up an
    // attempt whose handler never ran. This holds each cut-off until the handler of every attempt started has been called.
    private sealed class CutOffAfterTheCall(TimeSpan cutOff, Func<bool> called) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != cutOff)
            {
                return base.CreateTimer(callback, state, dueTime, period);
            }

            ITimer? timer = null;
            timer = base.CreateTimer(_ =>
            {
                if (called())
                {
                    callback(state);
                    return;
                }

                try
                {
                    timer!.Change(TimeSpan.FromMilliseconds(5), Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(dueTime, period);
            return timer;
        }
    }

    [Fact]
    public async Task AWriteThatFailsToRecordItsRowLeavesNothingForTheNextWriteOfTheScopeToCommit()
    {
        var seen = new Seen();
        var faults = new JobFaults();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(faults)))));

        var second = await database.AsAsync(Editor, async services =>
        {
            var content = services.GetRequiredService<IContentService>();
            faults.Fail(command => command.CommandText.Contains("cms_outbox", StringComparison.Ordinal));
            await Should.ThrowAsync<Exception>(() => content.CreateAsync("article", Titled("First"), Token));
            faults.Recover();
            return IdOf(await content.CreateAsync("article", Titled("Second"), Token));
        });

        database.Rows().ShouldHaveSingleItem().Payload.ShouldContain(second.ToString());
        database.Footprint().ShouldStartWith("1,");
        database.Versions(second).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AWriteWhoseCommitFailsIsNeverDeliveredNorRecorded()
    {
        var seen = new Seen();
        var fault = new CommitFault();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
            cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(fault));
        })));

        fault.Armed = true;
        await Should.ThrowAsync<InvalidOperationException>(() => database.CreateAsync(Editor, Titled("T")));
        fault.Armed = false;

        seen.Events.ShouldBeEmpty();
        database.Rows().ShouldBeEmpty();
        database.Footprint().ShouldBe("0,0,0,0,0");
        (await DrainAsync(database.Database.Services)).ShouldBe(0);
    }

    [Fact]
    public async Task AWriteDoesNotSaveWhatTheCallerTrackedAndNeverSaved()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast()));

        var (state, kept) = await database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            var site = new Site { Name = "Pending", Slug = "pending", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow };
            context.Add(site);

            await services.GetRequiredService<IContentService>().CreateAsync("article", Titled("T"), Token);

            return (context.Entry(site).State, site.Slug);
        });

        state.ShouldBe(EntityState.Added);
        kept.ShouldBe("pending");
        database.Read(context => context.Set<Site>().AsNoTracking().Count(site => site.Slug == "pending")).ShouldBe(0);
        database.Rows().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnInProcessHandlerThatLeavesAMessInTheContextDoesNotSpoilTheNextWrite()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms => cms.Events.AddHandler<EntryDraftSavedEvent, PoisoningHandler>())));

        await database.AsAsync(Editor, async services =>
        {
            var content = services.GetRequiredService<IContentService>();
            await content.CreateAsync("article", Titled("First"), Token);
            await content.CreateAsync("article", Titled("Second"), Token);
            return true;
        });

        database.Footprint().ShouldStartWith("2,");
        database.Rows().Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnInProcessHandlerMayWriteContentWhileItIsBeingTold()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms =>
        {
            cms.Events.AddHandler<EntryDraftSavedEvent, WritingHandler>();
            cms.Events.AddHandler<EntryDraftSavedEvent, SavedHandler>();
        })));

        await database.CreateAsync(Editor, Titled("Outer"));

        database.Footprint().ShouldStartWith("2,");
        seen.Calls.Count(call => call == "saved").ShouldBe(2);
        database.Rows().Count.ShouldBe(2);
    }

    [Fact]
    public async Task ADeliveryWhoseSettleFailsIsDeliveredAgainWithTheSameKeyWhenItsLeaseEnds()
    {
        var seen = new Seen();
        var faults = new JobFaults();
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms =>
        {
            cms.Services.AddSingleton<ILoggerProvider>(logs);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(faults));
        })));
        await database.CreateAsync(Editor, Titled("T"));

        faults.Fail(command => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("delivered_at", StringComparison.Ordinal));
        (await DrainAsync(database.Database.Services)).ShouldBe(1);
        faults.Recover();

        logs.Count("SettleFailed").ShouldBe(1);
        var held = database.Rows().Single();
        held.Status.ShouldBe(OutboxStatus.Pending);
        held.LeaseOwner.ShouldNotBeNull();

        (await DrainAsync(database.Database.Services)).ShouldBe(0);
        ExpireLeases(database);
        (await DrainAsync(database.Database.Services)).ShouldBe(1);

        seen.Deliveries.Select(delivery => delivery.Key).Distinct().ShouldHaveSingleItem();
        seen.Deliveries.Select(delivery => delivery.Attempt).ShouldBe([1, 2]);
        database.Rows().Single().Status.ShouldBe(OutboxStatus.Delivered);
    }

    [Fact]
    public async Task AWorkerWhoseLeaseWasTakenOverMidBatchDeliversNothingMoreAndLeavesTheNewOwnersResultAlone()
    {
        var seen = new Seen();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heard = 0;
        seen.Behavior = async (_, _) =>
        {
            if (Interlocked.Increment(ref heard) != 1)
            {
                return;
            }

            // The first worker is in its first delivery when its lease on the whole batch runs out.
            entered.SetResult();
            await gate.Task;
            throw new InvalidOperationException("The slow worker fails when it finally ends.");
        };

        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast()));
        for (var index = 0; index < 3; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        using var other = database.Database.NewInstance(EventRegistrations.With(seen, Fast()));
        var slow = Task.Run(() => DrainAsync(database.Database.Services), Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        ExpireLeases(database);
        (await DrainAsync(other)).ShouldBe(3);

        gate.SetResult();
        (await slow).ShouldBe(0);

        var perEvent = seen.Of<EntryDraftSavedEvent>().GroupBy(heard => heard.EventId).Select(group => group.Count()).Order().ToList();
        perEvent.ShouldBe([1, 1, 2]);
        var rows = database.Rows();
        rows.ShouldAllBe(row => row.Status == OutboxStatus.Delivered);
        rows.ShouldAllBe(row => row.LastError == null);
    }

    [Fact]
    public async Task ADeliveryThatKillsItsProcessEveryTimeIsLeftAsFailedAtTheLimitAndNeverDeliveredAgain()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms =>
        {
            cms.Events.Outbox.MaxAttempts = 3;
            cms.Services.AddSingleton<ILoggerProvider>(logs);
        })));
        await database.CreateAsync(Editor, Titled("T"));

        // Three workers claimed the row and died before settling it.
        database.Read(context => context.Set<OutboxMessage>().ExecuteUpdate(row => row
            .SetProperty(column => column.LeaseOwner, "dead/worker")
            .SetProperty(column => column.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))
            .SetProperty(column => column.Attempts, 3)));

        (await DrainAsync(database.Database.Services)).ShouldBe(0);

        seen.Events.ShouldBeEmpty();
        var row = database.Rows().Single();
        row.Status.ShouldBe(OutboxStatus.Failed);
        row.Attempts.ShouldBe(3);
        row.LastError.ShouldNotBeNull();
        logs.Count("Exhausted").ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatNeverReturnsIsCutOffAndCountedAsFailed()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => new TaskCompletionSource().Task;
        var starts = new AttemptStarts();
        var cutOff = TimeSpan.FromMilliseconds(150);
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms =>
        {
            cms.Events.Outbox.DeliveryTimeout = cutOff;
            cms.Events.Outbox.LeaseDuration = TimeSpan.FromSeconds(30);
            cms.Events.Outbox.MaxAttempts = 2;
            cms.Services.AddSingleton<TimeProvider>(new CutOffAfterTheCall(cutOff, () => seen.Deliveries.Count >= starts.Count));
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(starts));
        })));
        await database.CreateAsync(Editor, Titled("T"));

        // The bound fails the test, instead of hanging it, if the handler is never called.
        (await DrainAsync(database.Database.Services).WaitAsync(TimeSpan.FromSeconds(30), Token)).ShouldBe(0);

        starts.Count.ShouldBe(2);
        seen.Deliveries.Select(delivery => delivery.Attempt).ShouldBe([1, 2]);
        var row = database.Rows().Single();
        row.Status.ShouldBe(OutboxStatus.Failed);
        row.Attempts.ShouldBe(2);
        row.LastError!.ShouldContain("did not finish");
    }

    [Fact]
    public async Task ADrainTheHostIsStoppingDoesNotCallTheRestOfItsBatchAndGivesTheAttemptBack()
    {
        var seen = new Seen();
        using var stopping = new CancellationTokenSource();
        seen.Behavior = (_, _) =>
        {
            stopping.Cancel();
            throw new OperationCanceledException(stopping.Token);
        };
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast()));
        for (var index = 0; index < 4; index++)
        {
            await database.CreateAsync(Editor, Titled($"T{index}"));
        }

        await DrainWithAsync(database.Database.Services, stopping.Token);

        seen.Calls.Count.ShouldBe(1);
        var rows = database.Rows();
        rows.ShouldAllBe(row => row.Status == OutboxStatus.Pending && row.Attempts == 0 && row.LeaseOwner == null && row.LastError == null);
    }

    [Fact]
    public async Task ARowWhoseHandlerThisInstanceDoesNotHaveWaitsWithoutUsingAnAttempt()
    {
        var seen = new Seen();
        var logs = new RecordingLogs();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms =>
        {
            cms.Events.Outbox.MaxAttempts = 2;
            cms.Services.AddSingleton<ILoggerProvider>(logs);
        })));
        database.Read(context =>
        {
            context.Add(new OutboxMessage
            {
                EventId = Guid.CreateVersion7(),
                EventType = "Acme.Thing",
                Handler = "acme-plugin",
                Payload = "{}",
                OccurredAt = DateTimeOffset.UtcNow,
                Sequence = 1,
                AvailableAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
            return context.SaveChanges();
        });

        for (var drain = 0; drain < 3; drain++)
        {
            await DrainAsync(database.Database.Services);
        }

        var row = database.Rows().Single();
        row.Status.ShouldBe(OutboxStatus.Pending);
        row.Attempts.ShouldBe(0);
        row.LeaseOwner.ShouldBeNull();
        row.AvailableAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        logs.Count("HandlerMissing").ShouldBe(1);
    }

    [Fact]
    public async Task AFailedDeliveryCanBeListedRetriedAndRemoved()
    {
        var seen = new Seen();
        var behave = 0;
        seen.Behavior = (_, _) => Volatile.Read(ref behave) == 0 ? throw new InvalidOperationException("Down.") : Task.CompletedTask;
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms => cms.Events.Outbox.MaxAttempts = 2)));
        await database.CreateAsync(Editor, Titled("First"));
        await database.CreateAsync(Editor, Titled("Second"));
        await DrainAsync(database.Database.Services);
        var dead = database.Database.Services.GetRequiredService<IEventDeadLetters>();

        var failed = await dead.ListAsync(cancellationToken: Token);

        failed.Count.ShouldBe(2);
        failed.ShouldAllBe(item => item.Handler == "saved-outbox" && item.Attempts == 2 && item.LastError != null);

        Volatile.Write(ref behave, 1);
        (await dead.RetryAsync(failed[0].Id, Token)).ShouldBeTrue();
        (await dead.RetryAsync(failed[0].Id, Token)).ShouldBeFalse();
        (await dead.RemoveAsync(failed[1].Id, Token)).ShouldBeTrue();
        (await dead.RemoveAsync(failed[1].Id, Token)).ShouldBeFalse();

        (await DrainAsync(database.Database.Services)).ShouldBe(1);
        (await dead.ListAsync(cancellationToken: Token)).ShouldBeEmpty();
        database.Rows().ShouldHaveSingleItem().Status.ShouldBe(OutboxStatus.Delivered);
    }

    [Fact]
    public async Task AFailureStoresTheTypeOfTheExceptionAndNoCredentialsOrQueryStrings()
    {
        var seen = new Seen();
        seen.Behavior = (_, _) => throw new HttpRequestException("POST https://user:pw@hooks.example/x?token=s3cr3t#frag failed with 500");
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms => cms.Events.Outbox.MaxAttempts = 1)));
        await database.CreateAsync(Editor, Titled("T"));

        await DrainAsync(database.Database.Services);

        var error = database.Rows().Single().LastError!;
        error.ShouldStartWith("System.Net.Http.HttpRequestException: ");
        error.ShouldContain("https://hooks.example failed with 500");
        error.ShouldNotContain("s3cr3t");
        error.ShouldNotContain("pw@");
        error.ShouldNotContain("frag");
    }

    [Fact]
    public void ErrorTextKeepsTheHostAndPathOfEveryUrlAndNothingElseOfIt()
    {
        var text = OutboxErrorText.Of(new InvalidOperationException("a http://h/p?q=1 and ftp://u:p@h2:21/f.txt?x=y then wss://h3/ws#z"), "{}");

        text.ShouldBe("System.InvalidOperationException: a http://h and ftp://h2:21 then wss://h3");
        OutboxErrorText.Of(new InvalidOperationException(new string('x', 5000)), "{}").Length.ShouldBe(Job.LastErrorMaxLength);
    }

    [Fact]
    public async Task OnlyARunWithPruneRemovesOldDeliveredRows()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(EventRegistrations.With(seen, Fast(cms => cms.Events.Outbox.Retention = TimeSpan.Zero)));
        await database.CreateAsync(Editor, Titled("T"));
        await DrainAsync(database.Database.Services);
        await Task.Delay(5, Token);

        await DrainAsync(database.Database.Services);
        database.Rows().Count.ShouldBe(1);

        await database.Database.Services.GetRequiredService<OutboxDrainer>().DrainAsync(prune: true, Token);
        database.Rows().ShouldBeEmpty();
    }

    private static Task<int> DrainWithAsync(IServiceProvider instance, CancellationToken token) =>
        instance.GetRequiredService<OutboxDrainer>().DrainAsync(prune: false, token);

    // As if the workers that hold these rows had stopped renewing.
    private static void ExpireLeases(ServiceDatabase database) =>
        database.Read(context => context.Set<OutboxMessage>().Where(row => row.LeaseOwner != null).ExecuteUpdate(row => row.SetProperty(column => column.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));
}
