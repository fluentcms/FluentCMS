namespace FluentCMS.Core.Storage.Entities;

// A row of cms_job_attempt. Outcome stays null while the attempt runs, and after a crash until the job is next
// claimed or swept.
public sealed class JobAttempt
{
    public Guid JobId { get; set; }

    public int Attempt { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Outcome { get; set; }

    public string? Error { get; set; }
}
