using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Health;

// FluentCms:Health.
public sealed class HealthOptions
{
    public const string Section = "Health";

    // How old the last beat of the job runner may be before /health/ready reports it unhealthy. The runner beats on every
    // turn of its maintenance loop, every JobRunnerOptions.MaintenanceInterval (15 seconds by default), so keep this well
    // above that. Timeout.InfiniteTimeSpan leaves the runner out of the check, for an instance that runs no jobs.
    public TimeSpan JobRunnerHeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        if (JobRunnerHeartbeatTimeout != Timeout.InfiniteTimeSpan && JobRunnerHeartbeatTimeout <= TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(JobRunnerHeartbeatTimeout)} is {JobRunnerHeartbeatTimeout}; it must be positive, or Timeout.InfiniteTimeSpan for no check.");
        }
    }
}
