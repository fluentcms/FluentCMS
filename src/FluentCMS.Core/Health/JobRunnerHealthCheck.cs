using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Health;

internal sealed class JobRunnerHealthCheck(JobRunnerPulse pulse, HealthOptions options, TimeProvider time, ILogger<JobRunnerHealthCheck> logger) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var limit = options.JobRunnerHeartbeatTimeout;
        if (limit == Timeout.InfiniteTimeSpan)
        {
            return Task.FromResult(HealthCheckResult.Healthy("The job runner is not checked."));
        }

        if (pulse.LastBeat is not { } last)
        {
            HealthLog.JobRunnerNotStarted(logger);
            return Task.FromResult(HealthCheckResult.Unhealthy("The job runner has not started."));
        }

        var age = time.GetUtcNow() - last;
        if (age > limit)
        {
            HealthLog.JobRunnerSilent(logger, age, limit);
            return Task.FromResult(HealthCheckResult.Unhealthy($"The job runner has not beaten for {(int)age.TotalSeconds} seconds."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("The job runner is beating."));
    }
}
