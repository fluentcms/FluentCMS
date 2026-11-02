namespace FluentCMS.Core.Jobs;

internal static class RetryBackoff
{
    public static TimeSpan After(int attempt, JobRunnerOptions options)
    {
        var seconds = Math.Min(
            options.RetryBaseDelay.TotalSeconds * Math.Pow(2, Math.Max(attempt - 1, 0)),
            options.RetryMaxDelay.TotalSeconds);

        return TimeSpan.FromSeconds(seconds * (1 + (Random.Shared.NextDouble() * options.RetryJitter)));
    }
}
