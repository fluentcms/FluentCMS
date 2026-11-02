using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Events;

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 50;

    // Deliveries to one handler before the event is left as failed.
    public int MaxAttempts { get; set; } = 8;

    // How long a claim holds a delivery. A handler that outlives it may run a second time.
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    public double RetryJitter { get; set; } = 0.2;

    // How long a delivered row stays for diagnosis.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    internal void Validate()
    {
        Require(BatchSize >= 1, $"{nameof(BatchSize)} is {BatchSize}; it must be at least 1.");
        Require(MaxAttempts >= 1, $"{nameof(MaxAttempts)} is {MaxAttempts}; it must be at least 1.");
        Require(LeaseDuration > TimeSpan.Zero, $"{nameof(LeaseDuration)} must be positive.");
        Require(RetryBaseDelay >= TimeSpan.Zero && RetryMaxDelay >= RetryBaseDelay, $"{nameof(RetryMaxDelay)} must be at least {nameof(RetryBaseDelay)}, which must not be negative.");
        Require(RetryJitter >= 0, $"{nameof(RetryJitter)} must not be negative.");
        Require(Retention >= TimeSpan.Zero, $"{nameof(Retention)} must not be negative.");
    }

    internal TimeSpan RetryAfter(int attempt)
    {
        var seconds = Math.Min(
            RetryBaseDelay.TotalSeconds * Math.Pow(2, Math.Max(attempt - 1, 0)),
            RetryMaxDelay.TotalSeconds);

        return TimeSpan.FromSeconds(seconds * (1 + (Random.Shared.NextDouble() * RetryJitter)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(EventErrorCodes.InvalidOutboxOptions, message);
        }
    }
}
