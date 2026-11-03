using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Asks the job runner to drain the outbox now, after the commit that recorded rows. Whether it works or not nothing is
// lost: the rows are committed, and the recurring drain finds them.
internal sealed class OutboxKick(IServiceScopeFactory scopes, IJobScheduler scheduler, ILogger<OutboxKick> logger)
{
    public async ValueTask KickAsync()
    {
        try
        {
            await using (var scope = scopes.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
                if (await context.Set<Job>().AnyAsync(job => job.Type == OutboxDrainJob.Type && job.Status == JobClaimStatus.Pending))
                {
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
