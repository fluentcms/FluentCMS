using Cronos;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Jobs;

public sealed class JobRunnerOptions
{
    public const string DefaultQueue = "default";

    internal static readonly TimeSpan LongestWait = TimeSpan.FromDays(49);

    // Workers per queue on this instance, which is also the concurrency limit of the queue here. A queue left out has
    // no worker on this instance.
    public IDictionary<string, int> Queues { get; } = new Dictionary<string, int>(StringComparer.Ordinal) { [DefaultQueue] = 2 };

    public int DefaultMaxAttempts { get; set; } = 5;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    // How long one attempt may run before its token is cancelled and its lease is no longer renewed. Null puts no limit
    // on an attempt; a job registration can set its own, or Timeout.InfiniteTimeSpan for none.
    public TimeSpan? AttemptTimeout { get; set; }

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    // A retry waits up to this fraction longer, so that jobs failing together do not return together.
    public double RetryJitter { get; set; } = 0.2;

    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan ReleaseTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public const int MaxRetentionBatchSize = 1000;

    internal static readonly TimeSpan LongestRetention = TimeSpan.FromDays(36500);

    // How long a succeeded job and its attempts stay. Pending, running and dead-lettered jobs are never removed.
    public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(7);

    // The most jobs one delete of the retention job takes. The ids of a batch go into one statement as parameters, which
    // is why it is capped.
    public int RetentionBatchSize { get; set; } = 500;

    // When the retention job runs, as a cron expression in UTC.
    public string RetentionSchedule { get; set; } = "45 3 * * *";

    internal void Validate()
    {
        foreach (var (queue, workers) in Queues)
        {
            Require(!string.IsNullOrEmpty(queue) && queue.Length <= JobScheduler.QueueMaxLength, $"{nameof(Queues)} has the queue name '{queue}', which is not valid.");
            Require(workers >= 0, $"{nameof(Queues)}:{queue} is {workers}; it must not be negative.");
        }

        Require(DefaultMaxAttempts >= 1, $"{nameof(DefaultMaxAttempts)} is {DefaultMaxAttempts}; it must be at least 1.");
        Require(PollInterval > TimeSpan.Zero, $"{nameof(PollInterval)} must be positive.");
        Require(MaintenanceInterval > TimeSpan.Zero, $"{nameof(MaintenanceInterval)} must be positive.");
        Require(HeartbeatInterval > TimeSpan.Zero, $"{nameof(HeartbeatInterval)} must be positive.");
        Require(LeaseDuration > HeartbeatInterval, $"{nameof(LeaseDuration)} must be longer than {nameof(HeartbeatInterval)}, or a live job loses its lease.");
        Require(AttemptTimeout is null || IsValidAttemptTimeout(AttemptTimeout.Value), $"{nameof(AttemptTimeout)} must be positive and no longer than {LongestWait.TotalDays} days, or Timeout.InfiniteTimeSpan.");
        Require(RetryBaseDelay >= TimeSpan.Zero && RetryMaxDelay >= RetryBaseDelay, $"{nameof(RetryMaxDelay)} must be at least {nameof(RetryBaseDelay)}, which must not be negative.");
        Require(RetryJitter >= 0, $"{nameof(RetryJitter)} must not be negative.");
        Require(ShutdownGracePeriod >= TimeSpan.Zero && ReleaseTimeout >= TimeSpan.Zero, $"{nameof(ShutdownGracePeriod)} and {nameof(ReleaseTimeout)} must not be negative.");

        Require(SucceededRetention > TimeSpan.Zero && SucceededRetention <= LongestRetention, $"{nameof(SucceededRetention)} must be positive and no longer than {LongestRetention.TotalDays} days.");
        Require(RetentionBatchSize is >= 1 and <= MaxRetentionBatchSize, $"{nameof(RetentionBatchSize)} is {RetentionBatchSize}; it must be from 1 to {MaxRetentionBatchSize}.");
        Require(IsCron(RetentionSchedule), $"{nameof(RetentionSchedule)} '{RetentionSchedule}' is not a cron expression that ever occurs.");

        // Longer than a timer can wait, these would throw only when the loop first waits, or at shutdown.
        TimeSpan[] waits = [PollInterval, MaintenanceInterval, HeartbeatInterval, ShutdownGracePeriod, ReleaseTimeout];
        Require(waits.All(wait => wait <= LongestWait), $"{nameof(PollInterval)}, {nameof(MaintenanceInterval)}, {nameof(HeartbeatInterval)}, {nameof(ShutdownGracePeriod)} and {nameof(ReleaseTimeout)} must not be longer than {LongestWait.TotalDays} days.");
    }

    internal static bool IsValidAttemptTimeout(TimeSpan timeout) =>
        timeout == Timeout.InfiniteTimeSpan || (timeout > TimeSpan.Zero && timeout <= LongestWait);

    private static bool IsCron(string? cron)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(cron) && CronExpression.Parse(cron).GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc) is not null;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(JobErrorCodes.InvalidRunnerOptions, message);
        }
    }
}
