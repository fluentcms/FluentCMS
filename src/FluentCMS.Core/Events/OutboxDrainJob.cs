using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Prune is set by the recurring run only, so that the table is not walked for old rows on every drain.
internal sealed record OutboxDrainPayload(bool Prune = false);

internal sealed class OutboxDrainJob(OutboxDrainer drainer) : IJob<OutboxDrainPayload>
{
    public const string Type = "events.outbox.drain";

    public const string RecurringName = "events.outbox.drain";

    // Its own queue, so that a handler that hangs holds up other deliveries and no other job.
    public const string Queue = "outbox";

    public Task ExecuteAsync(OutboxDrainPayload payload, JobContext context) => drainer.DrainAsync(payload.Prune, context.CancellationToken);
}

// The safety net for a crash between a commit and its kick, which leaves rows nothing has asked to deliver. It does
// nothing when no outbox handler is registered, since then there are never rows, and it keeps trying until the
// schedule is stored, since a database that is down at startup would otherwise leave the host without it for good.
internal sealed class OutboxScheduleService(IJobScheduler scheduler, OutboxRegistry registry, TimeProvider time, ILogger<OutboxScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!registry.Any)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scheduler.AddRecurringAsync<OutboxDrainJob, OutboxDrainPayload>(OutboxDrainJob.RecurringName, "* * * * *", new OutboxDrainPayload(Prune: true), OutboxDrainJob.Queue, stoppingToken);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                EventLog.RecurringFailed(logger, exception);
            }

            try
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
