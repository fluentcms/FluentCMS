using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Content;

// Limit caps what one run purges of each type, so that a run is short; the rest waits for the next. PageSize is how many
// documents are read at a time while looking for what is due.
internal sealed record TrashPurgePayload(int Limit = 500, int PageSize = 100);

// Purges what has been in the trash for longer than its type keeps it, through the content service so that references,
// versions and events go the way a purge by hand does. An entry that cannot be purged is logged and left for the next
// run, and the others are not held up: the due entries are read oldest first in pages, so those that fail do not use up
// the limit or hide the ones behind them. One type that fails is logged and the others still run. Running again finds
// only what is still due.
internal sealed class TrashPurgeJob(CmsDbContext database, SchemaModel schema, ContentService content, IOperationContextAccessor accessor, TimeProvider time, ILogger<TrashPurgeJob> logger) : IJob<TrashPurgePayload>
{
    public const string Type = "content.trash.purge";

    public const string RecurringName = "content.trash.purge";

    public const string Cron = "17 3 * * *";

    public async Task ExecuteAsync(TrashPurgePayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(payload.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(payload.PageSize, 1);

        var cancellationToken = context.CancellationToken;
        var now = time.GetUtcNow();
        var purged = 0;
        var failed = 0;

        foreach (var type in schema.ContentTypes)
        {
            try
            {
                var (typePurged, typeFailed) = await PurgeTypeAsync(type, now - TimeSpan.FromDays(type.TrashRetentionDays), payload, context);
                purged += typePurged;
                failed += typeFailed;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                TrashLog.TypeFailed(logger, exception, type.Name);
            }
        }

        TrashLog.Report(logger, purged, failed);
    }

    private async Task<(int Purged, int Failed)> PurgeTypeAsync(SchemaType type, DateTimeOffset before, TrashPurgePayload payload, JobContext context)
    {
        var cancellationToken = context.CancellationToken;
        var name = type.Name;
        var purged = 0;
        var failed = 0;
        HashSet<Guid> tried = [];
        (DateTimeOffset At, Guid Id)? after = null;

        while (purged < payload.Limit)
        {
            var due = database.Documents.IgnoreQueryFilters().AsNoTracking()
                .Where(document => document.Type == name && document.DeletedAt != null && document.DeletedAt < before);
            if (after is { } last)
            {
                due = due.Where(document => document.DeletedAt > last.At || (document.DeletedAt == last.At && document.DocumentId.CompareTo(last.Id) > 0));
            }

            var page = await due
                .OrderBy(document => document.DeletedAt).ThenBy(document => document.DocumentId)
                .Select(document => new { document.DocumentId, document.SiteId, At = document.DeletedAt!.Value })
                .Take(payload.PageSize)
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            after = (page[^1].At, page[^1].DocumentId);
            foreach (var entry in page.Where(entry => tried.Add(entry.DocumentId)))
            {
                var site = entry.SiteId is { } owner ? new SiteId(owner) : context.Operation.Site;
                using (SystemOperation.Begin(accessor, Origin.Job, site, correlationId: context.Operation.CorrelationId))
                {
                    try
                    {
                        await content.PurgeAsync(name, entry.DocumentId, before, cancellationToken);
                        purged++;
                    }
                    catch (EntryNotFoundException)
                    {
                        // Purged by hand, restored or put in the trash again since it was read.
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        failed++;
                        TrashLog.PurgeFailed(logger, exception, name, entry.DocumentId);
                    }
                }

                if (purged >= payload.Limit)
                {
                    break;
                }
            }
        }

        if (purged + failed > 0)
        {
            TrashLog.TypeReport(logger, name, type.TrashRetentionDays, purged, failed);
        }

        return (purged, failed);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Trash purge: {Type} keeps its trash for {Days} days; {Purged} entries purged, {Failed} failed.")]
    public static partial void TypeReport(ILogger logger, string type, int days, int purged, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Trash purge could not go through the entries of {Type}; the other types still run.")]
    public static partial void TypeFailed(ILogger logger, Exception exception, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trash purge could not purge {Type} {Id}; it stays in the trash until the next run.")]
    public static partial void PurgeFailed(ILogger logger, Exception exception, string type, Guid id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not register the recurring purge of the trash; entries stay in the trash until it is.")]
    public static partial void ScheduleFailed(ILogger logger, Exception exception);
}
