using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Jobs;

// The bounded worker pool from ADR-0010. There is no jobs table yet, so the workers only keep the polling rhythm.
public sealed class JobQueueWorker(IOptions<JobQueueOptions> options, ILogger<JobQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var workerCount = Math.Max(1, settings.WorkerCount);
        var interval = settings.PollingInterval > TimeSpan.Zero ? settings.PollingInterval : TimeSpan.FromSeconds(1);

        logger.LogInformation("Job queue started with {WorkerCount} worker(s), polling every {Interval}.", workerCount, interval);

        var workers = Enumerable.Range(1, workerCount).Select(_ => IdleAsync(interval, stoppingToken));
        await Task.WhenAll(workers);

        logger.LogInformation("Job queue stopped.");
    }

    private static async Task IdleAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }
}
