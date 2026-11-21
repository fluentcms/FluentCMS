using System.Linq.Expressions;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

internal sealed record OutboxDrainPayload;

internal sealed class OutboxDrainJob(OutboxDrainer drainer) : IJob<OutboxDrainPayload>
{
    public const string Type = "events.outbox.drain";

    // The name of the recurring drain that earlier versions stored, which OutboxScheduleService removes.
    public const string RecurringName = "events.outbox.drain";

    // Its own queue, so that a handler that hangs holds up only the deliveries of its owner, in their lane, and no other job.
    // A second worker of the queue on one instance finds the drain running and only signals it.
    public const string Queue = "outbox";

    public Task ExecuteAsync(OutboxDrainPayload payload, JobContext context) => drainer.DrainOnceAsync(context.CancellationToken);
}

internal sealed record OutboxPrunePayload;

// Removes the delivered rows older than Retention and, when FailedRetention is set, the failed rows that ran out of attempts
// longer ago than it. A row moved to the dead-letter list for a missing handler or a disabled plugin never used up its
// attempts, so it stays until it is retried or removed; a lower MaxAttempts can count such a row in. It runs hourly on the default queue, so that a large prune holds up no delivery, and in batches like the
// retention of jobs: each batch is taken without an order and its delete checks the conditions again, so that a row
// retried in between stays and two instances running at once remove a row once.
internal sealed class OutboxPruneJob(SystemDbContext context, OutboxOptions options, TimeProvider time, ILogger<OutboxPruneJob> logger) : IJob<OutboxPrunePayload>
{
    public const string Type = "events.outbox.prune";

    public const string RecurringName = "events.outbox.prune";

    public const string Schedule = "0 * * * *";

    // Stops a large prune from holding a worker for good; what is left waits for the next run.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(10);

    public async Task ExecuteAsync(OutboxPrunePayload payload, JobContext jobContext)
    {
        var cancellationToken = jobContext.CancellationToken;
        var now = time.GetUtcNow();

        var deliveredBefore = now - options.Retention;
        var delivered = await DeleteAsync(context, row => row.Status == OutboxStatus.Delivered && row.DeliveredAt < deliveredBefore, options.PruneBatchSize, cancellationToken);

        var failed = 0;
        if (options.FailedRetention is { } retention)
        {
            failed = await DeleteAsync(context, FailedBefore(now - retention, options.MaxAttempts), options.PruneBatchSize, cancellationToken);
        }

        EventLog.Pruned(logger, delivered, failed);
    }

    // The failed rows that ran out of attempts before a time; a row left for a missing handler or a disabled plugin did not.
    // ix_cms_outbox_failed_available_at serves it.
    internal static Expression<Func<OutboxMessage, bool>> FailedBefore(DateTimeOffset before, int maxAttempts) =>
        row => row.Status == OutboxStatus.Failed && row.AvailableAt < before && row.Attempts >= maxAttempts;

    // The failed rows of one handler, which its purge removes; ix_cms_outbox_failed_handler serves it.
    internal static Expression<Func<OutboxMessage, bool>> FailedOf(string handler) =>
        row => row.Status == OutboxStatus.Failed && row.Handler == handler;

    // The ids of one batch of the prune, taken without an order.
    internal static IQueryable<Guid> Batch(SystemDbContext context, Expression<Func<OutboxMessage, bool>> matches, int batchSize) =>
        context.Set<OutboxMessage>().AsNoTracking().Where(matches).Select(row => row.Id).Take(batchSize);

    public static async Task<int> DeleteAsync(SystemDbContext context, Expression<Func<OutboxMessage, bool>> matches, int batchSize, CancellationToken cancellationToken)
    {
        var removed = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await Batch(context, matches, batchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return removed;
            }

            removed += await context.Set<OutboxMessage>().Where(matches).Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);

            if (ids.Count < batchSize)
            {
                return removed;
            }
        }
    }
}

// The sweep is the safety net for a crash between a commit and its kick, which leaves rows nothing has asked to deliver, and
// for a worker that stopped on the last attempt of a delivery: every SweepInterval it leaves such deliveries as failed and,
// when rows are due, kicks the drain. It reads before it writes, so an idle instance adds no row and takes no write lock,
// where a recurring drain added a job and an attempt every minute. The prune keeps the table to what is still owed or still
// kept. It does nothing when no outbox handler is registered, since then there are never rows, and it keeps trying until
// the prune's schedule is stored, since a database that is down at startup would otherwise leave the host without it for
// good. The recurring drain of earlier versions is removed when the schedule is stored.
internal sealed class OutboxScheduleService(
    IJobScheduler scheduler,
    OutboxRegistry registry,
    OutboxDrainer drainer,
    OutboxKick kick,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<OutboxScheduleService> logger) : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!registry.Any)
        {
            return;
        }

        if (!await ScheduleAsync(stoppingToken))
        {
            return;
        }

        while (await WaitAsync(SweepInterval, stoppingToken))
        {
            try
            {
                if (await drainer.SweepAsync(stoppingToken))
                {
                    await kick.KickAsync();
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                EventLog.SweepFailed(logger, exception);
            }
        }
    }

    private async Task<bool> ScheduleAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scheduler.AddRecurringAsync<OutboxPruneJob, OutboxPrunePayload>(OutboxPruneJob.RecurringName, OutboxPruneJob.Schedule, new OutboxPrunePayload(), JobRunnerOptions.DefaultQueue, stoppingToken);
                await RemoveRecurringDrainAsync(stoppingToken);
                return true;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                EventLog.PruneScheduleFailed(logger, exception);
            }

            if (!await WaitAsync(Retry, stoppingToken))
            {
                return false;
            }
        }

        return false;
    }

    private async Task RemoveRecurringDrainAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var recurring = context.Set<RecurringJob>().Where(job => job.Name == OutboxDrainJob.RecurringName);
        if (await recurring.AnyAsync(cancellationToken))
        {
            await recurring.ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, time, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
