namespace FluentCMS.Jobs;

/// <summary>
/// Runs the queued jobs while the host is up. Register another implementation before <c>AddFluentCms</c> to hand the
/// work to something else, such as Hangfire, together with an <see cref="IJobScheduler"/> that feeds it.
/// </summary>
public interface IJobRunner
{
    /// <summary>Starts running jobs and returns without waiting for them.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the runner has started.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops taking jobs, lets the running ones finish or releases them, and returns.</summary>
    /// <param name="cancellationToken">Cancelled when the host no longer waits for the running jobs.</param>
    /// <returns>A task that completes when the runner has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
