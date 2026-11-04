using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

// The rows of cms_schedule and their jobs. It knows nothing of permissions: ContentService decides, and calls this inside
// its transaction, so a schedule and the job that runs it are written, moved and removed together.
internal sealed class ScheduleStore(CmsDbContext context)
{
    public Task<ScheduleRow?> PendingAsync(Guid documentId, string locale, string action, CancellationToken cancellationToken) =>
        context.Set<ScheduleRow>().AsNoTracking()
            .FirstOrDefaultAsync(row => row.DocumentId == documentId && row.Locale == locale && row.Action == action && row.Status == ScheduleRow.Pending, cancellationToken);

    // For a job that has been dead-lettered: its schedule will never run, so it is closed rather than left pending.
    public async Task<bool> FailByJobAsync(Guid jobId, DateTimeOffset at, string error, CancellationToken cancellationToken)
    {
        if (await context.Set<ScheduleRow>().AsNoTracking().FirstOrDefaultAsync(row => row.JobId == jobId && row.Status == ScheduleRow.Pending, cancellationToken) is not { } pending
            || !await CloseAsync(pending.Id, jobId, ScheduleRow.Failed, at, error, cancellationToken))
        {
            return false;
        }

        await SetMirrorAsync(pending.DocumentId, pending.Locale, pending.Action, null, cancellationToken);
        return true;
    }

    public Task<ScheduleRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<ScheduleRow>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

    // A new schedule is added with its job. A pending one is moved: its job is removed if it has not started, and the new one
    // takes its place, so the row names the one job that may run it.
    public async Task SaveAsync(ScheduleRow? pending, ScheduleRow row, Job job, CancellationToken cancellationToken)
    {
        job.Id = Guid.CreateVersion7();
        row.JobId = job.Id;
        if (pending is not null)
        {
            await RemoveJobAsync(pending.JobId, cancellationToken);
        }

        var held = HeldChanges.Take(context);
        try
        {
            context.Add(job);
            if (pending is null)
            {
                context.Add(row);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            held.Release();
        }

        if (pending is not null)
        {
            await context.Set<ScheduleRow>()
                .Where(item => item.Id == pending.Id && item.Status == ScheduleRow.Pending)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(item => item.RunAtUtc, row.RunAtUtc)
                        .SetProperty(item => item.TimeZone, row.TimeZone)
                        .SetProperty(item => item.JobId, row.JobId)
                        .SetProperty(item => item.Version, row.Version)
                        .SetProperty(item => item.SharedHash, row.SharedHash)
                        .SetProperty(item => item.CreatedBy, row.CreatedBy)
                        .SetProperty(item => item.CreatedAt, row.CreatedAt)
                        .SetProperty(item => item.Claims, row.Claims),
                    cancellationToken);
        }
    }

    // Null when nothing was pending, or another writer cancelled or ran it first.
    public async Task<ScheduleRow?> CancelAsync(Guid documentId, string locale, string action, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (await PendingAsync(documentId, locale, action, cancellationToken) is not { } pending
            || !await CloseAsync(pending.Id, null, ScheduleRow.Cancelled, at, null, cancellationToken))
        {
            return null;
        }

        await RemoveJobAsync(pending.JobId, cancellationToken);
        await SetMirrorAsync(documentId, locale, action, null, cancellationToken);
        return pending;
    }

    // Ends a pending schedule, and only that one: false when it is not pending any more or, given a job, is no longer its
    // job's. This is what lets one of two racing jobs run, and lets nothing run a cancelled schedule.
    public async Task<bool> CloseAsync(Guid id, Guid? jobId, string status, DateTimeOffset at, string? error, CancellationToken cancellationToken)
    {
        var text = error is { Length: > ScheduleRow.ErrorMaxLength } ? error[..ScheduleRow.ErrorMaxLength] : error;
        var closed = await context.Set<ScheduleRow>()
            .Where(row => row.Id == id && row.Status == ScheduleRow.Pending && (jobId == null || row.JobId == jobId))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.Status, status)
                    .SetProperty(row => row.CompletedAt, at)
                    .SetProperty(row => row.Error, text),
                cancellationToken);

        return closed == 1;
    }

    public Task SetMirrorAsync(Guid documentId, string locale, string action, DateTimeOffset? at, CancellationToken cancellationToken)
    {
        var documents = context.Documents.IgnoreQueryFilters().Where(document => document.DocumentId == documentId && document.Locale == locale);
        return action == ScheduleRow.Publish
            ? documents.ExecuteUpdateAsync(set => set.SetProperty(document => document.SchedulePublishAt, at), cancellationToken)
            : documents.ExecuteUpdateAsync(set => set.SetProperty(document => document.ScheduleUnpublishAt, at), cancellationToken);
    }

    // Only a job that has not been claimed is removed. One that has will find its schedule closed and do nothing.
    private Task RemoveJobAsync(Guid? jobId, CancellationToken cancellationToken) =>
        jobId is { } id
            ? context.Set<Job>().Where(job => job.Id == id && job.Status == JobClaimStatus.Pending).ExecuteDeleteAsync(cancellationToken)
            : Task.CompletedTask;
}
