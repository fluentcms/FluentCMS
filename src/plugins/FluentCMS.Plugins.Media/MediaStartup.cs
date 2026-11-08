using FluentCMS.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// Once the host has started, which is after the migrations: makes sure of the signing key, so that the instances that start
// together share the one they create first, and stores the daily schedule of the variant cleanup. Each step is tried again
// while the database is out of reach.
internal sealed class MediaStartup(
    MediaSigning signing,
    IJobScheduler scheduler,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<MediaStartup> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            try
            {
                await started.Task;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var keyReady = false;
        var scheduled = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!keyReady)
            {
                try
                {
                    await signing.KeyAsync(stoppingToken);
                    keyReady = true;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    MediaLog.SigningKeyNotReady(logger, exception);
                }
            }

            if (!scheduled)
            {
                try
                {
                    await scheduler.AddRecurringAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(MediaVariantCleanupJob.Type, MediaVariantCleanupJob.Schedule, new MediaVariantCleanupPayload(), cancellationToken: stoppingToken);
                    scheduled = true;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    MediaLog.CleanupNotScheduled(logger, exception);
                }
            }

            if (keyReady && scheduled)
            {
                return;
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
