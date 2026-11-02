using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

internal sealed class JobExecutor(
    JobStore store,
    JobRegistry registry,
    IServiceScopeFactory scopes,
    IOperationContextAccessor operation,
    JobRunnerOptions options,
    TimeProvider time,
    ILogger<JobExecutor> logger)
{
    private static readonly TimeSpan FirstSettleRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LastSettleRetry = TimeSpan.FromSeconds(30);

    // False when the queue has nothing due.
    public async Task<bool> RunNextAsync(
        string queue,
        string owner,
        CancellationToken stopClaiming = default,
        CancellationToken shutdown = default,
        CancellationToken abandon = default)
    {
        if (await store.ClaimAsync(queue, owner, stopClaiming) is not { } job)
        {
            return false;
        }

        await RunAsync(job, shutdown, abandon);
        return true;
    }

    public async Task RunAsync(ClaimedJob job, CancellationToken shutdown = default, CancellationToken abandon = default)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(abandon);

        var heartbeat = HeartbeatAsync(job, attempt, stopHeartbeat.Token);
        try
        {
            await ExecuteAndSettleAsync(job, shutdown, attempt.Token, abandon);
        }
        finally
        {
            await stopHeartbeat.CancelAsync();
            await heartbeat;
        }
    }

    private async Task ExecuteAndSettleAsync(ClaimedJob job, CancellationToken shutdown, CancellationToken attempt, CancellationToken abandon)
    {
        Func<Task<bool>> settle;
        try
        {
            await InvokeAsync(job, attempt);
            settle = () => store.SucceedAsync(job);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            JobLog.Released(logger, job.Id, job.Type, job.Attempt);
            settle = () => store.ReleaseAsync(job);
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
            return;
        }
        catch (Exception exception)
        {
            JobLog.AttemptFailed(logger, job.Id, job.Type, job.Attempt, JobErrorText.ForLog(exception, job.Payload));
            var error = JobErrorText.ForStorage(exception, job.Payload);
            settle = () => store.FailAsync(job, error);
        }

        await SettleWithRetriesAsync(job, settle, abandon);
    }

    // A result not written means the job runs again, or is dead-lettered as if it never finished, so a brief outage is
    // waited out. Retrying past the real end of the lease is safe, because a write matches nothing once another worker
    // has the job. It gives up after one lease, in case the failure is not an outage.
    private async Task SettleWithRetriesAsync(ClaimedJob job, Func<Task<bool>> settle, CancellationToken abandon)
    {
        var giveUpAt = time.GetUtcNow() + options.LeaseDuration;
        var delay = FirstSettleRetry;

        while (true)
        {
            try
            {
                if (!await settle())
                {
                    JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
                }

                return;
            }
            catch (Exception exception) when (time.GetUtcNow() + delay < giveUpAt)
            {
                JobLog.SettleFailed(logger, exception, job.Id);
            }

            await Task.Delay(delay, time, abandon);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, LastSettleRetry.Ticks));
        }
    }

    private async Task InvokeAsync(ClaimedJob job, CancellationToken attempt)
    {
        var registration = registry.Find(job.Type)
            ?? throw new JobInvocationException($"{JobErrorCodes.UnknownJobType}: no job is registered for the type '{job.Type}'.");

        var payload = ReadPayload(registration, job);

        await using var scope = scopes.CreateAsyncScope();
        var context = new JobContext(job.Id, job.Type, job.Queue, job.Attempt, job.MaxAttempts, await OperationOfAsync(job, payload, scope.ServiceProvider), attempt);

        var previous = operation.Current;
        operation.Current = context.Operation;
        try
        {
            await registration.Invoke(scope.ServiceProvider, payload, context);
        }
        finally
        {
            operation.Current = previous;
        }
    }

    // Keeps nothing of the reader's exception: a converter can quote any value in it, and a path can name a dictionary key.
    private static object ReadPayload(JobRegistration registration, ClaimedJob job)
    {
        try
        {
            return registration.Read(job.Payload)
                ?? throw new JobInvocationException($"{JobErrorCodes.BadPayload}: the payload of '{job.Type}' is null.");
        }
        catch (Exception exception) when (exception is not JobInvocationException)
        {
            throw new JobInvocationException(
                $"{JobErrorCodes.BadPayload}: the payload of '{job.Type}' could not be read as {registration.Payload.Name} ({exception.GetType().Name}).");
        }
    }

    private async Task<OperationContext> OperationOfAsync(ClaimedJob job, object payload, IServiceProvider services)
    {
        var site = job.SiteId ?? DefaultSite.Id;
        var locale = await services.GetRequiredService<SystemDbContext>().Set<Site>()
            .Where(row => row.Id == site)
            .Select(row => row.DefaultLocale)
            .FirstOrDefaultAsync();

        return new OperationContext(Principal.System, Origin.Job, new SiteId(site), locale ?? DefaultSite.Locale, ClientId: null, job.Id.ToString(), time.GetUtcNow())
        {
            Actor = ActorOf(job, payload),
        };
    }

    // Built from the id alone, so that nothing in a payload can bring claims. It never replaces Principal.System.
    private static Principal? ActorOf(ClaimedJob job, object payload)
    {
        if (payload is not IJobActorPayload { Actor: { } actor })
        {
            return null;
        }

        if (actor.Id == Guid.Empty)
        {
            throw new JobInvocationException($"{JobErrorCodes.BadActor}: the payload of '{job.Type}' names an actor with an empty id.");
        }

        try
        {
            return new Principal(actor.Kind, actor.Id, actor.Name);
        }
        catch (ArgumentException)
        {
            throw new JobInvocationException($"{JobErrorCodes.BadActor}: the payload of '{job.Type}' names an actor of kind {actor.Kind}, which is not an identity a job can act for.");
        }
    }

    private async Task HeartbeatAsync(ClaimedJob job, CancellationTokenSource attempt, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(options.HeartbeatInterval, time);
        var leaseUntil = job.LeaseUntil;
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    if (await store.RenewLeaseAsync(job) is not { } renewed)
                    {
                        await attempt.CancelAsync();
                        return;
                    }

                    leaseUntil = renewed;
                }
                catch (Exception exception) when (!stop.IsCancellationRequested)
                {
                    JobLog.HeartbeatFailed(logger, exception, job.Id);

                    // With no renewal before the lease ends, another worker can take the job, so stop first.
                    if (time.GetUtcNow() + options.HeartbeatInterval >= leaseUntil)
                    {
                        await attempt.CancelAsync();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }
}

// The job could not start. The message is what last_error stores, and never holds a value of the payload.
internal sealed class JobInvocationException(string message) : Exception(message);
