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

        // The seconds the oldest pending job of each queue had waited, as data for a health publisher. A queue behind is
        // Degraded rather than Unhealthy, since taking this instance out of rotation drains no queue.
        var oldest = pulse.OldestPending;
        var data = oldest.ToDictionary(queue => queue.Key, queue => (object)Math.Round(queue.Value.TotalSeconds, 3), StringComparer.Ordinal);
        var ageLimit = options.PendingJobAgeLimit;
        if (ageLimit != Timeout.InfiniteTimeSpan && oldest.Where(queue => queue.Value > ageLimit).OrderByDescending(queue => queue.Value).FirstOrDefault() is { Key: { } behind, Value: var waited })
        {
            HealthLog.JobQueueBehind(logger, behind, waited, ageLimit);
            return Task.FromResult(HealthCheckResult.Degraded($"The oldest pending job of the queue '{behind}' has waited {(int)waited.TotalSeconds} seconds; the limit is {(int)ageLimit.TotalSeconds}.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("The job runner is beating.", data));
    }
}
