using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Content;

// The schedule a job is for, and who scheduled it. The actor is for the audit trail alone: the job runs as the system
// principal, and what the actor may do is read again from its roles when the job runs.
internal sealed record ScheduledPayload(Guid ScheduleId, JobActor? Actor = null) : IJobActorPayload;

internal sealed class ScheduledPublishJob(ContentService content, ILogger<ScheduledPublishJob> logger) : IJob<ScheduledPayload>
{
    public const string Type = "content.schedule.publish";

    public async Task ExecuteAsync(ScheduledPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        var outcome = await content.RunScheduledAsync(payload.ScheduleId, context.JobId, ScheduleAction.Publish, context.Attempt >= context.MaxAttempts, context.CancellationToken);
        ScheduleLog.Ran(logger, payload.ScheduleId, ScheduleAction.Publish, outcome);
    }
}

internal sealed class ScheduledUnpublishJob(ContentService content, ILogger<ScheduledUnpublishJob> logger) : IJob<ScheduledPayload>
{
    public const string Type = "content.schedule.unpublish";

    public async Task ExecuteAsync(ScheduledPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        var outcome = await content.RunScheduledAsync(payload.ScheduleId, context.JobId, ScheduleAction.Unpublish, context.Attempt >= context.MaxAttempts, context.CancellationToken);
        ScheduleLog.Ran(logger, payload.ScheduleId, ScheduleAction.Unpublish, outcome);
    }
}

// The runner dead-letters a job its own way as well (an attempt that ran out of time on the last try, a worker that died),
// without the job getting to say so. It tells this process once the job is settled. It is not an outbox handler, which would
// give every host an outbox; a crash between the settling and this leaves the schedule pending, and its job in the dead-letter list.
internal sealed class ScheduleDeadLetterHandler(ScheduleStore schedules, CmsDbContext context, TimeProvider time) : IEventHandler<JobDeadLetteredEvent>
{
    public async ValueTask HandleAsync(JobDeadLetteredEvent message, CancellationToken cancellationToken)
    {
        if (message.Type is not (ScheduledPublishJob.Type or ScheduledUnpublishJob.Type))
        {
            return;
        }

        // A job brought back from the dead-letter list is not dead, whatever this says.
        if (!await context.Set<Job>().AnyAsync(job => job.Id == message.JobId && job.Status == JobStatus.DeadLettered, cancellationToken))
        {
            return;
        }

        await schedules.FailByJobAsync(message.JobId, time.GetUtcNow(), $"The job was dead-lettered after {message.Attempts} attempts.", cancellationToken);
    }
}

internal static partial class ScheduleLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {Schedule} ({Action}) ran: {Outcome}.")]
    public static partial void Ran(ILogger logger, Guid schedule, ScheduleAction action, ScheduleOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Schedule {Schedule} ({Action}) of entry {Document} was refused when it came due; it is marked failed and nothing was changed.")]
    public static partial void Refused(ILogger logger, Exception exception, Guid schedule, string action, Guid document);
}
