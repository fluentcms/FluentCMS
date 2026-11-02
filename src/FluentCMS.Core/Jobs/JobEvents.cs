namespace FluentCMS.Core.Jobs;

// The events of spec 4.11, until the event bus (#2697) publishes them.
internal sealed record JobFailedEvent(Guid JobId, string Type, string Queue, int Attempt, int MaxAttempts, string Error);

internal sealed record JobDeadLetteredEvent(Guid JobId, string Type, string Queue, int Attempts, string Error);

// The seam for #2697. A sink that throws is logged and changes nothing about the job.
internal interface IJobEventSink
{
    Task PublishAsync(JobFailedEvent failed);

    Task PublishAsync(JobDeadLetteredEvent deadLettered);
}

internal sealed class NullJobEventSink : IJobEventSink
{
    public Task PublishAsync(JobFailedEvent failed) => Task.CompletedTask;

    public Task PublishAsync(JobDeadLetteredEvent deadLettered) => Task.CompletedTask;
}
