using FluentCMS.Content;

namespace FluentCMS.Core.Storage.Entities;

// A row of cms_job. Write it through EF: the SQLite claim matches ids as the uppercase text EF stores.
public sealed class Job
{
    public const int LastErrorMaxLength = 2000;

    public Guid Id { get; set; }

    // The name a job is registered under.
    public string Type { get; set; } = "";

    // JSON text.
    public string Payload { get; set; } = "";

    public string Queue { get; set; } = "";

    public Guid? SiteId { get; set; }

    public DateTimeOffset RunAt { get; set; }

    public int Priority { get; set; }

    public string Status { get; set; } = JobClaimStatus.Pending;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public string? LeaseOwner { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    // Where the job was enqueued from, not how it runs: every job runs with Origin.Job.
    public Origin Origin { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
