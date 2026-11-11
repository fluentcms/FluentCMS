using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.AiAssist;

internal sealed record AiUsageRetentionPayload;

// Removes the usage rows older than AiOptions.UsageRetention, in batches.
internal sealed class AiUsageRetentionJob(PluginDbContext<AiAssistPlugin> database, IOptions<AiOptions> options, TimeProvider time, ILogger<AiUsageRetentionJob> logger)
    : IJob<AiUsageRetentionPayload>
{
    public const string Type = "ai.usage-retention";
    public const string Schedule = "30 4 * * *";
    public const int BatchSize = 500;

    // Stops a large prune from holding a worker for good. The batches before it are committed, so the attempt that the
    // runner retries goes on where it stopped.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(10);

    public async Task ExecuteAsync(AiUsageRetentionPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cutoff = time.GetUtcNow() - options.Value.UsageRetention;
        var removed = 0;
        while (true)
        {
            var ids = await database.Set<AiUsage>()
                .Where(usage => usage.CreatedAt < cutoff)
                .OrderBy(usage => usage.CreatedAt)
                .Select(usage => usage.Id)
                .Take(BatchSize)
                .ToListAsync(context.CancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            removed += await database.Set<AiUsage>().Where(usage => ids.Contains(usage.Id)).ExecuteDeleteAsync(context.CancellationToken);
        }

        AiLog.Pruned(logger, removed);
    }
}

// Stores the daily schedule of the retention once the host has started, which is after the migrations, and keeps trying
// while the database is out of reach.
internal sealed class AiUsageRetentionSchedule(IJobScheduler scheduler, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<AiUsageRetentionSchedule> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            try
            {
                await started.Task;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scheduler.AddRecurringAsync<AiUsageRetentionJob, AiUsageRetentionPayload>(AiUsageRetentionJob.Type, AiUsageRetentionJob.Schedule, new AiUsageRetentionPayload(), queue: null, cancellationToken: stoppingToken);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                AiLog.RetentionNotScheduled(logger, exception);
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
