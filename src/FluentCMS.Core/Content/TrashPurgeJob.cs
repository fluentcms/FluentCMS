using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Content;

// Limit caps what one run purges of each type, so that a run is short; the rest waits for the next.
internal sealed record TrashPurgePayload(int Limit = 500);

// Purges what has been in the trash for longer than its type keeps it, through the content service so that references,
// versions and events go the way a purge by hand does. An entry that cannot be purged is logged and left for the next
// run, and the others are not held up; running again finds only what is still due.
internal sealed class TrashPurgeJob(CmsDbContext database, SchemaModel schema, ContentService content, IOperationContextAccessor accessor, TimeProvider time, ILogger<TrashPurgeJob> logger) : IJob<TrashPurgePayload>
{
    public const string Type = "content.trash.purge";

    public const string RecurringName = "content.trash.purge";

    public const string Cron = "17 3 * * *";

    public async Task ExecuteAsync(TrashPurgePayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.CancellationToken;
        var now = time.GetUtcNow();
        var purged = 0;
        var failed = 0;

        foreach (var type in schema.ContentTypes)
        {
            var name = type.Name;
            var before = now - TimeSpan.FromDays(type.TrashRetentionDays);
            var due = await database.Documents.IgnoreQueryFilters().AsNoTracking()
                .Where(document => document.Type == name && document.DeletedAt != null && document.DeletedAt < before)
                .Select(document => new { document.DocumentId, document.SiteId })
                .Distinct()
                .OrderBy(document => document.DocumentId)
                .Take(payload.Limit)
                .ToListAsync(cancellationToken);

            foreach (var entry in due)
            {
                var site = entry.SiteId is { } owner ? new SiteId(owner) : context.Operation.Site;
                using (SystemOperation.Begin(accessor, Origin.Job, site, correlationId: context.Operation.CorrelationId))
                {
                    try
                    {
                        await content.PurgeAsync(name, entry.DocumentId, cancellationToken);
                        purged++;
                    }
                    catch (EntryNotFoundException)
                    {
                        // Purged by hand or restored since the list was read.
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        failed++;
                        TrashLog.PurgeFailed(logger, exception, name, entry.DocumentId);
                    }
                }
            }

            if (due.Count > 0)
            {
                TrashLog.TypeReport(logger, name, type.TrashRetentionDays, due.Count);
            }
        }

        TrashLog.Report(logger, purged, failed);
    }
}

// Stores the schedule while the host starts, so that it is there when startup is over. If the database cannot be reached
// then, it keeps trying in the background, since a host without the schedule would otherwise keep its trash for good.
internal sealed class TrashPurgeScheduleService(IJobScheduler scheduler, TimeProvider time, ILogger<TrashPurgeScheduleService> logger) : BackgroundService
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
            await scheduler.AddRecurringAsync<TrashPurgeJob, TrashPurgePayload>(TrashPurgeJob.RecurringName, TrashPurgeJob.Cron, new TrashPurgePayload(), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            TrashLog.ScheduleFailed(logger, exception);
            return false;
        }
    }
}

internal static partial class TrashLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Trash purge: {Purged} entries purged, {Failed} failed.")]
    public static partial void Report(ILogger logger, int purged, int failed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trash purge: {Type} kept its trash for {Days} days and had {Due} entries past that.")]
    public static partial void TypeReport(ILogger logger, string type, int days, int due);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trash purge could not purge {Type} {Id}; it stays in the trash until the next run.")]
    public static partial void PurgeFailed(ILogger logger, Exception exception, string type, Guid id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not register the recurring purge of the trash; entries stay in the trash until it is.")]
    public static partial void ScheduleFailed(ILogger logger, Exception exception);
}
