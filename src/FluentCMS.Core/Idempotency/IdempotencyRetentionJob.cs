using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Idempotency;

internal sealed record IdempotencyRetentionPayload;

// Removes the keys whose window has passed, held ones included, in batches. A request that finds an expired key removes
// it on its own, so this only keeps the keys that are never sent again from piling up.
internal sealed class IdempotencyRetentionJob(SystemDbContext context, TimeProvider time) : IJob<IdempotencyRetentionPayload>
{
    public const string Type = "idempotency.retention";

    public const string RecurringName = "idempotency.retention";

    public const string Schedule = "41 * * * *";

    public const int BatchSize = 1000;

    public async Task ExecuteAsync(IdempotencyRetentionPayload payload, JobContext jobContext)
    {
        ArgumentNullException.ThrowIfNull(jobContext);

        var cancellationToken = jobContext.CancellationToken;
        var now = time.GetUtcNow();
        var rows = context.Set<IdempotencyRow>();

        while (true)
        {
            var ids = await rows.AsNoTracking().Where(row => row.ExpiresAt <= now).OrderBy(row => row.ExpiresAt).Select(row => row.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return;
            }

            await rows.Where(row => ids.Contains(row.Id) && row.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
            if (ids.Count < BatchSize)
            {
                return;
            }
        }
    }
}

// Stores the schedule while the host starts and keeps trying in the background while the database cannot be reached.
internal sealed class IdempotencyRetentionScheduleService(IJobScheduler scheduler, TimeProvider time, ILogger<IdempotencyRetentionScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await TryScheduleAsync(cancellationToken))
        {
            await base.StartAsync(cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await TryScheduleAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task<bool> TryScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<IdempotencyRetentionJob, IdempotencyRetentionPayload>(IdempotencyRetentionJob.RecurringName, IdempotencyRetentionJob.Schedule, new IdempotencyRetentionPayload(), queue: null, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            IdempotencyLog.ScheduleFailed(logger, exception);
            return false;
        }
    }
}
