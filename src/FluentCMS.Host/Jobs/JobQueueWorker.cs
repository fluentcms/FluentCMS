using FluentCMS.Core.Jobs;
using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Jobs;

// The bounded worker pool from ADR-0010. Each worker claims a due job, runs its handler in a scope of its own as the job's
// initiator, and records how it ended; with nothing due it waits the polling interval.
public sealed class JobQueueWorker(
    IServiceScopeFactory scopes,
    IOptions<JobQueueOptions> options,
    TimeProvider clock,
    ILogger<JobQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var workerCount = Math.Max(1, settings.WorkerCount);
        var interval = settings.PollingInterval > TimeSpan.Zero ? settings.PollingInterval : TimeSpan.FromSeconds(1);

        logger.LogInformation("Job queue started with {WorkerCount} worker(s), polling every {Interval}.", workerCount, interval);

        var workers = Enumerable.Range(1, workerCount)
            .Select(index => WorkAsync($"{Environment.MachineName}:{Environment.ProcessId}:{index}:{Guid.NewGuid():N}", settings, interval, stoppingToken));
        await Task.WhenAll(workers);

        logger.LogInformation("Job queue stopped.");
    }

    private async Task WorkAsync(string owner, JobQueueOptions settings, TimeSpan interval, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await RunNextAsync(owner, settings, stoppingToken))
                {
                    await Task.Delay(interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The queue itself failed, such as the database being unreachable; the next poll tries again.
                logger.LogError(exception, "The job queue could not claim or finish a job.");
                await DelayAsync(interval, stoppingToken);
            }
        }
    }

    private async Task<bool> RunNextAsync(string owner, JobQueueOptions settings, CancellationToken stoppingToken)
    {
        // The leases have a scope of their own, so a handler's unit of work never holds their updates back.
        await using var leaseScope = scopes.CreateAsyncScope();
        var leases = leaseScope.ServiceProvider.GetRequiredService<IJobLeases>();

        var job = await leases.ClaimAsync(owner, settings.LeaseDuration, stoppingToken);
        if (job is null)
        {
            return false;
        }

        await using var runScope = scopes.CreateAsyncScope();

        var handler = runScope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(candidate => candidate.Type == job.Type);
        if (handler is null)
        {
            logger.LogWarning("Job {JobId} has the type {JobType}, which no handler runs.", job.Id, job.Type);
            await leases.AbandonAsync(job.Id, owner, $"No handler runs jobs of the type '{job.Type}'.", stoppingToken);
            return true;
        }

        runScope.ServiceProvider.GetRequiredService<JobRun>().Initiator = handler.RunAs ?? job.Initiator;

        var context = new JobContext(
            job,
            runScope.ServiceProvider,
            (progress, cancellationToken) => leases.ReportProgressAsync(job.Id, owner, progress, settings.LeaseDuration, cancellationToken));

        // The lease is kept while the handler works, so a long step without progress does not hand the job to another worker. Once it
        // is lost, the handler is stopped through its token, since another worker may already be running the job.
        using var running = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var keeping = KeepAsync(job, owner, settings.LeaseDuration, running, heartbeat.Token);

        // Once the handler is done, how it ended is recorded even if the host is stopping, so finished work is not run again.
        try
        {
            var result = await handler.RunAsync(context, running.Token);
            await heartbeat.CancelAsync();
            await keeping;

            if (result.IsSuccess)
            {
                await leases.SucceedAsync(job.Id, owner, CancellationToken.None);
            }
            else
            {
                await leases.FailAsync(job.Id, owner, string.Join(" ", result.Errors.Select(error => $"{error.Code}: {error.Message}")), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await heartbeat.CancelAsync();
            await keeping;

            // The host is stopping mid-job: the job goes back to the queue with its attempt given back, so stopping never uses one up.
            if (!await ReleaseAsync(leases, job, owner))
            {
                logger.LogWarning("Job {JobId} could not be put back in the queue as the host stopped; it runs again once its lease runs out.", job.Id);
            }

            throw;
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            // The lease was lost and the handler stopped: the job is the next claimant's, so nothing is recorded for this attempt.
            await keeping;
        }
        catch (Exception exception)
        {
            await heartbeat.CancelAsync();
            await keeping;

            logger.LogWarning(exception, "Job {JobId} of the type {JobType} failed on attempt {Attempt} of {MaxAttempts}.", job.Id, job.Type, job.Attempts, job.MaxAttempts);
            await leases.RetryAsync(job.Id, owner, exception.Message, RetryDelay(settings, job.Attempts), CancellationToken.None);
        }

        return true;
    }

    // Each renewal has a scope of its own; a lost lease stops the handler.
    private async Task KeepAsync(Job job, string owner, TimeSpan duration, CancellationTokenSource running, CancellationToken cancellationToken)
    {
        var kept = await KeepLeaseAsync(
            async renewing =>
            {
                await using var scope = scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IJobLeases>().RenewAsync(job.Id, owner, duration, renewing);
            },
            duration,
            clock,
            logger,
            cancellationToken);

        if (!kept)
        {
            logger.LogWarning("Job {JobId} of the type {JobType} lost its lease on attempt {Attempt}; its handler is stopped.", job.Id, job.Type, job.Attempts);
            await running.CancelAsync();
        }
    }

    // Renews the lease at a third of its duration until the token says the handler is done, and is then true. A renewal that throws is
    // tried again at the next beat while the lease still holds, so one transient error does not give the job away. False once a
    // renewal is refused, because another worker holds the lease, or once the lease has run out with no renewal.
    public static async Task<bool> KeepLeaseAsync(
        Func<CancellationToken, Task<bool>> renew,
        TimeSpan duration,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var heldUntil = clock.GetUtcNow() + duration;
        try
        {
            while (true)
            {
                await Task.Delay(duration / 3, cancellationToken);
                var renewedAt = clock.GetUtcNow();
                try
                {
                    if (!await renew(cancellationToken))
                    {
                        return false;
                    }

                    heldUntil = renewedAt + duration;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    if (clock.GetUtcNow() >= heldUntil)
                    {
                        logger.LogError(exception, "A job's lease ran out at {HeldUntil} while it could not be renewed.", heldUntil);
                        return false;
                    }

                    logger.LogWarning(exception, "A job's lease could not be renewed; it holds until {HeldUntil} and is tried again.", heldUntil);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
    }

    // The update is guarded by the owner, so a worker that lost the lease meanwhile changes nothing.
    private async Task<bool> ReleaseAsync(IJobLeases leases, Job job, string owner)
    {
        try
        {
            return await leases.ReleaseAsync(job.Id, owner, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Job {JobId} could not be released.", job.Id);
            return false;
        }
    }

    // Doubled for each attempt after the first, so a failing dependency gets room to recover. Doubling stops at the ceiling, so no
    // attempt count overflows.
    public static TimeSpan RetryDelay(JobQueueOptions settings, int attempt)
    {
        var delay = settings.RetryDelay;
        for (var doubled = 1; doubled < attempt && delay > TimeSpan.Zero && delay < settings.MaxRetryDelay; doubled++)
        {
            delay = delay >= settings.MaxRetryDelay / 2 ? settings.MaxRetryDelay : delay + delay;
        }

        return delay < settings.MaxRetryDelay ? delay : settings.MaxRetryDelay;
    }

    private static async Task DelayAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(interval, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }
}
