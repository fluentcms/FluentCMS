using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Asks the job runner to drain the outbox now, after the commit that recorded rows, and a drain already running on this
// instance to start a lane for a handler that has none. Whether it works or not nothing is lost: the rows are committed, and
// the sweep of OutboxScheduleService finds them. A drain job already pending and due is not added again, but an idle outbox
// worker of this instance is still woken for it; one that waits for a later retry does not count. The check reads cms_job,
// so it dedupes only for the built-in runner; an IJobRunner that keeps its jobs elsewhere gets a drain job per kick, and the
// drains find little.
internal sealed class OutboxKick(IServiceScopeFactory scopes, IJobScheduler scheduler, JobWake wake, OutboxDrainer drainer, TimeProvider time, ILogger<OutboxKick> logger)
{
    public async ValueTask KickAsync()
    {
        drainer.Signal();
        try
        {
            await using (var scope = scopes.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
                var now = time.GetUtcNow();
                if (await context.Set<Job>().AnyAsync(job =>
                    job.Queue == OutboxDrainJob.Queue && job.Status == JobClaimStatus.Pending && job.RunAt <= now && job.Type == OutboxDrainJob.Type))
                {
                    wake.Signal(OutboxDrainJob.Queue);
                    return;
                }
            }

            await scheduler.EnqueueAsync<OutboxDrainJob, OutboxDrainPayload>(new OutboxDrainPayload(), new JobOptions { Queue = OutboxDrainJob.Queue });
        }
        catch (Exception exception)
        {
            EventLog.KickFailed(logger, exception);
        }
    }
}
