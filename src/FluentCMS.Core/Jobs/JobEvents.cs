using FluentCMS.Events;

namespace FluentCMS.Core.Jobs;

// A sink that throws is logged and changes nothing about the job.
internal interface IJobEventSink
{
    Task PublishAsync(JobFailedEvent failed);

    Task PublishAsync(JobDeadLetteredEvent deadLettered);
}
