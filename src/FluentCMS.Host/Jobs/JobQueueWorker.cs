using FluentCMS.Core.Jobs;
using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Jobs;

// The bounded worker pool from ADR-0010. Each worker claims a due job, runs its handler in a scope of its own as the job's
// initiator, and records how it ended; with nothing due it waits the polling interval.
public sealed class JobQueueWorker(
    IServiceScopeFactory scopes,
    IOptions<JobQueueOptions> options,
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
        runScope.ServiceProvider.GetRequiredService<JobRun>().Initiator = job.Initiator;

        var handler = runScope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(candidate => candidate.Type == job.Type);
        if (handler is null)
        {
            logger.LogWarning("Job {JobId} has the type {JobType}, which no handler runs.", job.Id, job.Type);
            await leases.AbandonAsync(job.Id, owner, $"No handler runs jobs of the type '{job.Type}'.", stoppingToken);
            return true;
        }

        var context = new JobContext(
            job,
            runScope.ServiceProvider,
            (progress, cancellationToken) => leases.ReportProgressAsync(job.Id, owner, progress, settings.LeaseDuration, cancellationToken));

        // The lease is kept while the handler works, so a long step without progress does not hand the job to another worker.
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var keeping = KeepLeaseAsync(job.Id, owner, settings.LeaseDuration, heartbeat.Token);

        // Once the handler is done, how it ended is recorded even if the host is stopping, so finished work is not run again.
        try
        {
            var result = await handler.RunAsync(context, stoppingToken);
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
            // The host is stopping mid-job: the lease runs out and another worker runs the job again.
            throw;
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

    // Renews the lease at a third of its duration, each time in a scope of its own, until the handler is done.
    private async Task KeepLeaseAsync(Guid jobId, string owner, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(duration / 3, cancellationToken);
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<IJobLeases>().RenewAsync(jobId, owner, duration, cancellationToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The handler is done.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The lease on job {JobId} could not be renewed.", jobId);
        }
    }

    // Doubled for each attempt after the first, so a failing dependency gets room to recover.
    public static TimeSpan RetryDelay(JobQueueOptions settings, int attempt)
    {
        var delay = settings.RetryDelay * Math.Pow(2, Math.Max(0, attempt - 1));
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
