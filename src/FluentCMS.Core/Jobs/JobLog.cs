using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

// Event ids 95xx. What a job threw arrives as text already scrubbed of the payload, never as the exception itself.
internal static partial class JobLog
{
    [LoggerMessage(EventId = 9501, Level = LogLevel.Warning, Message = "Job {JobId} ({Type}) failed attempt {Attempt}: {Error}")]
    public static partial void AttemptFailed(ILogger logger, Guid jobId, string type, int attempt, string error);

    [LoggerMessage(EventId = 9502, Level = LogLevel.Error, Message = "Job {JobId} ({Type}) was dead-lettered after {Attempts} attempts: {Error}")]
    public static partial void DeadLettered(ILogger logger, Guid jobId, string type, int attempts, string error);

    [LoggerMessage(EventId = 9503, Level = LogLevel.Warning, Message = "Job {JobId} ({Type}) lost its lease during attempt {Attempt}, so this worker leaves it alone.")]
    public static partial void LeaseLost(ILogger logger, Guid jobId, string type, int attempt);

    [LoggerMessage(EventId = 9504, Level = LogLevel.Information, Message = "Job {JobId} ({Type}) was released by the shutdown during attempt {Attempt}.")]
    public static partial void Released(ILogger logger, Guid jobId, string type, int attempt);

    [LoggerMessage(EventId = 9505, Level = LogLevel.Warning, Message = "Could not renew the lease of job {JobId}.")]
    public static partial void HeartbeatFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 9506, Level = LogLevel.Warning, Message = "Could not write the outcome of job {JobId}; trying again.")]
    public static partial void SettleFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 9507, Level = LogLevel.Error, Message = "The worker of queue {Queue} hit an error and carries on.")]
    public static partial void WorkerFailed(ILogger logger, Exception exception, string queue);

    [LoggerMessage(EventId = 9508, Level = LogLevel.Error, Message = "The maintenance of the job runner hit an error and carries on.")]
    public static partial void MaintenanceFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9509, Level = LogLevel.Error, Message = "A job event sink threw for job {JobId}.")]
    public static partial void EventSinkFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 9510, Level = LogLevel.Error, Message = "{Code}: recurring job {Name} has a cron expression that cannot fire, so it is skipped: {Reason}")]
    public static partial void RecurringCronInvalid(ILogger logger, string code, string name, string reason);

    [LoggerMessage(EventId = 9511, Level = LogLevel.Error, Message = "Recurring job {Name} could not fire; it tries again next time.")]
    public static partial void RecurringFireFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = 9512, Level = LogLevel.Information, Message = "Job retention removed {Succeeded} succeeded and {DeadLettered} dead-lettered jobs.")]
    public static partial void Retention(ILogger logger, int succeeded, int deadLettered);

    [LoggerMessage(EventId = 9513, Level = LogLevel.Error, Message = "Could not register the recurring retention of finished jobs; they stay until it is registered.")]
    public static partial void RetentionScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9514, Level = LogLevel.Warning, Message = "The job runner stopped with jobs still running, whose leases will expire.")]
    public static partial void StoppedWithRunningJobs(ILogger logger);

    [LoggerMessage(EventId = 9515, Level = LogLevel.Error, Message = "Job {JobId} ({Type}) ignored its token for {Limit} during attempt {Attempt} and was abandoned; it may still be running.")]
    public static partial void Abandoned(ILogger logger, Guid jobId, string type, int attempt, TimeSpan limit);

    [LoggerMessage(EventId = 9516, Level = LogLevel.Warning, Message = "Job {JobId} ({Type}) failed after it was abandoned.")]
    public static partial void AbandonedJobFailed(ILogger logger, Exception exception, Guid jobId, string type);

    [LoggerMessage(EventId = 9517, Level = LogLevel.Warning, Message = "Job {JobId} has the type {Type}, which this instance does not know, so it is put back for an instance that does.")]
    public static partial void UnknownTypeReleased(ILogger logger, Guid jobId, string type);

    [LoggerMessage(EventId = 9518, Level = LogLevel.Warning, Message = "{Code}: recurring job {Name} has the type {Type}, which this instance does not know, so this instance does not fire it.")]
    public static partial void RecurringTypeUnknown(ILogger logger, string code, string name, string type);

    [LoggerMessage(EventId = 9519, Level = LogLevel.Warning, Message = "Job {JobId} ({Type}) ignored its token past its attempt timeout, but {Limit} abandoned jobs are already running, so it keeps its worker until it returns.")]
    public static partial void AbandonRefused(ILogger logger, Guid jobId, string type, int limit);

    [LoggerMessage(EventId = 9520, Level = LogLevel.Information, Message = "Job {JobId} ({Type}) stopped at its attempt timeout of {Limit}; what is left waits for its next run.")]
    public static partial void StoppedAtLimit(ILogger logger, Guid jobId, string type, TimeSpan limit);

    [LoggerMessage(EventId = 9521, Level = LogLevel.Warning, Message = "Could not read how long job {JobId} of the unknown type {Type} has waited; it is put back without using an attempt.")]
    public static partial void UnknownTypeDeferred(ILogger logger, Exception exception, Guid jobId, string type);

    [LoggerMessage(EventId = 9522, Level = LogLevel.Warning, Message = "A callback of the job runner's stop threw after the stop had stopped waiting for it.")]
    public static partial void StopCallbackFailed(ILogger logger, Exception exception);
}
