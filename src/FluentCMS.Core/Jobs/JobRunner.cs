using FluentCMS.Jobs;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

// The database-backed runner: a few workers per queue, plus one loop that fires recurring jobs and sweeps. Instances
// share nothing but the database, so any number can run against one.
internal sealed class JobRunner(
    JobExecutor executor,
    JobStore store,
    RecurringJobs recurring,
    JobRunnerOptions options,
    TimeProvider time,
    ILogger<JobRunner> logger) : IJobRunner, IDisposable
{
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    // Tells the workers to stop taking jobs.
    private readonly CancellationTokenSource _stopping = new();

    // Tells the running jobs to stop.
    private readonly CancellationTokenSource _shutdown = new();

    // Stops the heartbeats of jobs that ignored the shutdown, so that their leases can expire.
    private readonly CancellationTokenSource _abandon = new();

    private readonly string _instance = Guid.NewGuid().ToString("N");

    private Task? _loops;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();

        if (_loops is not null)
        {
            throw new InvalidOperationException("The job runner has already started.");
        }

        var loops = new List<Task> { Task.Run(MaintainAsync, CancellationToken.None) };
        foreach (var (queue, workers) in options.Queues)
        {
            for (var worker = 0; worker < workers; worker++)
            {
                var owner = $"{_instance}/{queue}/{worker}";
                loops.Add(Task.Run(() => WorkAsync(queue, owner), CancellationToken.None));
            }
        }

        _loops = Task.WhenAll(loops);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_loops is null)
        {
            return;
        }

        await _stopping.CancelAsync();

        if (await FinishedWithinAsync(options.ShutdownGracePeriod, cancellationToken))
        {
            return;
        }

        await _shutdown.CancelAsync();

        // The host may have stopped waiting, but a job that stops now still gets its release written.
        if (!await FinishedWithinAsync(options.ReleaseTimeout, CancellationToken.None))
        {
            JobLog.StoppedWithRunningJobs(logger);
            await _abandon.CancelAsync();
        }
    }

    // A host disposed without being stopped must not leave a loop that still opens the database.
    public void Dispose()
    {
        _stopping.Cancel();
        _shutdown.Cancel();
        _abandon.Cancel();

        try
        {
            if (_loops?.Wait(DisposeWait) == false)
            {
                return;
            }
        }
        catch (AggregateException)
        {
            // The loops catch what they can; there is no one left to tell about the rest.
        }

        _stopping.Dispose();
        _shutdown.Dispose();
        _abandon.Dispose();
    }

    private async Task<bool> FinishedWithinAsync(TimeSpan period, CancellationToken cancellationToken)
    {
        try
        {
            await _loops!.WaitAsync(period, time, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task WorkAsync(string queue, string owner)
    {
        while (!_stopping.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                worked = await executor.RunNextAsync(queue, owner, _shutdown.Token, _abandon.Token);
            }
            catch (Exception exception)
            {
                JobLog.WorkerFailed(logger, exception, queue);
            }

            if (!worked && !await WaitAsync(options.PollInterval))
            {
                return;
            }
        }
    }

    private async Task MaintainAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                await recurring.FireDueAsync(_stopping.Token);
                await store.SweepAsync();
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                JobLog.MaintenanceFailed(logger, exception);
            }

            if (!await WaitAsync(options.MaintenanceInterval))
            {
                return;
            }
        }
    }

    private async Task<bool> WaitAsync(TimeSpan period)
    {
        try
        {
            await Task.Delay(period, time, _stopping.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
