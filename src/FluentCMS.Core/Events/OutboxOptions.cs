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

    // How long one delivery may take, from the renewal of its lease, before it is abandoned. Shorter than the lease, so that a
    // delivery never outlives the claim that lets it run. A handler that started counts a failed attempt; one that never
    // started counts nothing and waits for a later drain.
    public TimeSpan DeliveryTimeout { get; set; } = TimeSpan.FromMinutes(1);

    // How long the job runner waits for the in-process handlers of a job event before it goes on. They are not stopped.
    public TimeSpan JobEventTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // How long a row waits when no handler is registered under its name, as in a rolling deploy or after a plugin was removed.
    public TimeSpan MissingHandlerDelay { get; set; } = TimeSpan.FromMinutes(5);

    // How long a delivered row stays for diagnosis.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    internal void Validate()
    {
        Require(BatchSize >= 1, $"{nameof(BatchSize)} is {BatchSize}; it must be at least 1.");
        Require(MaxAttempts >= 1, $"{nameof(MaxAttempts)} is {MaxAttempts}; it must be at least 1.");
        Require(LeaseDuration > TimeSpan.Zero, $"{nameof(LeaseDuration)} must be positive.");
        Require(DeliveryTimeout > TimeSpan.Zero && DeliveryTimeout < LeaseDuration, $"{nameof(DeliveryTimeout)} must be positive and shorter than {nameof(LeaseDuration)}.");
        Require(JobEventTimeout > TimeSpan.Zero, $"{nameof(JobEventTimeout)} must be positive.");
        Require(MissingHandlerDelay > TimeSpan.Zero, $"{nameof(MissingHandlerDelay)} must be positive.");
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
