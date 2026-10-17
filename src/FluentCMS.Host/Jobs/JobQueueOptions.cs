namespace FluentCMS.Host.Jobs;

public sealed class JobQueueOptions
{
    public const string SectionName = "JobQueue";

    public int WorkerCount { get; set; } = 2;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
}
