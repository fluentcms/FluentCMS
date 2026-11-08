using System.Linq.Expressions;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

internal sealed record OutboxDrainPayload;

internal sealed class OutboxDrainJob(OutboxDrainer drainer) : IJob<OutboxDrainPayload>
{
    public const string Type = "events.outbox.drain";

    public const string RecurringName = "events.outbox.drain";

    // Its own queue, so that a handler that hangs holds up other deliveries and no other job.
    public const string Queue = "outbox";

    public Task ExecuteAsync(OutboxDrainPayload payload, JobContext context) => drainer.DrainAsync(context.CancellationToken);
}

internal sealed record OutboxPrunePayload;

// Removes the delivered rows older than Retention and, when FailedRetention is set, the failed rows that failed longer ago
// than it. It runs hourly on the default queue, so that a large prune holds up no delivery, and in batches like the
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
            var failedBefore = now - retention;
            failed = await DeleteAsync(context, row => row.Status == OutboxStatus.Failed && row.AvailableAt < failedBefore, options.PruneBatchSize, cancellationToken);
        }

        EventLog.Pruned(logger, delivered, failed);
    }

    public static async Task<int> DeleteAsync(SystemDbContext context, Expression<Func<OutboxMessage, bool>> matches, int batchSize, CancellationToken cancellationToken)
    {
        var removed = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await context.Set<OutboxMessage>().AsNoTracking().Where(matches).Select(row => row.Id).Take(batchSize).ToListAsync(cancellationToken);
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

// The drain is the safety net for a crash between a commit and its kick, which leaves rows nothing has asked to deliver, and
// the prune keeps the table to what is still owed or still kept. It does nothing when no outbox handler is registered, since
// then there are never rows, and it keeps trying until both schedules are stored, since a database that is down at startup
// would otherwise leave the host without them for good.
internal sealed class OutboxScheduleService(IJobScheduler scheduler, OutboxRegistry registry, TimeProvider time, ILogger<OutboxScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!registry.Any)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scheduler.AddRecurringAsync<OutboxDrainJob, OutboxDrainPayload>(OutboxDrainJob.RecurringName, "* * * * *", new OutboxDrainPayload(), OutboxDrainJob.Queue, stoppingToken);
                await scheduler.AddRecurringAsync<OutboxPruneJob, OutboxPrunePayload>(OutboxPruneJob.RecurringName, OutboxPruneJob.Schedule, new OutboxPrunePayload(), JobRunnerOptions.DefaultQueue, stoppingToken);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                EventLog.RecurringFailed(logger, exception);
            }

            try
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
