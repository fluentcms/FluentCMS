namespace FluentCMS.Host.Jobs;

public sealed class JobQueueOptions
{
    public const string SectionName = "JobQueue";

    public int WorkerCount { get; set; } = 2;

    // How long a worker waits before looking again when no job is due.
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    // How long a claimed job stays a worker's without word from it; reporting progress renews it. A job whose worker stopped is
    // claimed again once this runs out.
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    // The delay before the first retry of a job that threw, doubled for each later attempt up to MaxRetryDelay.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);
}
