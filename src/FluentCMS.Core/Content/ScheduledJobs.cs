using FluentCMS.Jobs;
using FluentCMS.Publishing;
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

internal static partial class ScheduleLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {Schedule} ({Action}) ran: {Outcome}.")]
    public static partial void Ran(ILogger logger, Guid schedule, ScheduleAction action, ScheduleOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Schedule {Schedule} ({Action}) of entry {Document} was refused when it came due; it is marked failed and nothing was changed.")]
    public static partial void Refused(ILogger logger, Exception exception, Guid schedule, string action, Guid document);
}
