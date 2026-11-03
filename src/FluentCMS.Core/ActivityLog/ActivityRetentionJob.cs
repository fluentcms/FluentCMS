using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.ActivityLog;

internal sealed record ActivityRetentionPayload;

// Removes the rows that are older than the retention of their site. A site named in SiteRetention has its own age; every
// other row, the rows of no site included, has the default. The delete goes in batches, since a log that was never pruned
// can hold more rows than one statement should take.
internal sealed class ActivityRetentionJob(SystemDbContext context, ActivityOptions options, TimeProvider time) : IJob<ActivityRetentionPayload>
{
    public const string Type = "activity.retention";

    public const string RecurringName = "activity.retention";

    public async Task ExecuteAsync(ActivityRetentionPayload payload, JobContext jobContext)
    {
        var cancellationToken = jobContext.CancellationToken;
        var now = time.GetUtcNow();

        var overrides = options.SiteRetention.Count == 0
            ? []
            : await context.Set<Site>().AsNoTracking().Select(site => new { site.Id, site.Slug }).ToListAsync(cancellationToken);

        List<Guid> own = [];
        foreach (var site in overrides)
        {
            if (!options.SiteRetention.TryGetValue(site.Slug, out var retention))
            {
                continue;
            }

            own.Add(site.Id);
            var before = now - retention;
            var siteId = site.Id;
            await DeleteAsync(rows => rows.Where(row => row.SiteId == siteId && row.OccurredAt < before), cancellationToken);
        }

        var cutoff = now - options.Retention;
        await DeleteAsync(rows => rows.Where(row => row.OccurredAt < cutoff && (row.SiteId == null || !own.Contains(row.SiteId.Value))), cancellationToken);
    }

    private async Task DeleteAsync(Func<IQueryable<ActivityRow>, IQueryable<ActivityRow>> expired, CancellationToken cancellationToken)
    {
        while (true)
        {
            var ids = await expired(context.Set<ActivityRow>().AsNoTracking())
                .OrderBy(row => row.OccurredAt)
                .Select(row => row.Id)
                .Take(options.PruneBatchSize)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                return;
            }

            await context.Set<ActivityRow>().Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }
}

// Stores the schedule of the retention job before the host reports started, so that a failure is seen at once. A database
// that is down at startup is retried in the background, since the host would otherwise be left without the job for good.
internal sealed class ActivityScheduleService(IJobScheduler scheduler, ActivityOptions options, TimeProvider time, ILogger<ActivityScheduleService> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    private CancellationTokenSource _stopping = new();

    private Task _retrying = Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping.Dispose();
        _stopping = new CancellationTokenSource();
        if (!await TryScheduleAsync(cancellationToken))
        {
            _retrying = Task.Run(() => RetryAsync(_stopping.Token), CancellationToken.None);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await _retrying.WaitAsync(cancellationToken);
    }

    public void Dispose() => _stopping.Dispose();

    private async Task RetryAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            while (!await TryScheduleAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> TryScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<ActivityRetentionJob, ActivityRetentionPayload>(ActivityRetentionJob.RecurringName, options.PruneSchedule, new ActivityRetentionPayload(), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ActivityLogMessages.ScheduleFailed(logger, exception);
            return false;
        }
    }
}

internal static partial class ActivityLogMessages
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not register the recurring retention job of the activity log; old rows stay until it is registered.")]
    public static partial void ScheduleFailed(ILogger logger, Exception exception);
}
