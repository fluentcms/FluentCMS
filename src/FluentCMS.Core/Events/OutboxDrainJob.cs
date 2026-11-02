using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

internal sealed record OutboxDrainPayload;

internal sealed class OutboxDrainJob(OutboxDrainer drainer) : IJob<OutboxDrainPayload>
{
    public const string Type = "events.outbox.drain";

    public const string RecurringName = "events.outbox.drain";

    public Task ExecuteAsync(OutboxDrainPayload payload, JobContext context) => drainer.DrainAsync(context.CancellationToken);
}

// The safety net: a crash between a commit and its kick leaves rows nothing has asked to deliver.
internal sealed class OutboxScheduleService(IJobScheduler scheduler, ILogger<OutboxScheduleService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<OutboxDrainJob, OutboxDrainPayload>(OutboxDrainJob.RecurringName, "* * * * *", new OutboxDrainPayload(), cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            EventLog.RecurringFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
