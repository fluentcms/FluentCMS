namespace FluentCMS.Core.Storage.Entities;

// A row of cms_recurring_job. Whoever moves NextRunAt on enqueues the job, so that one instance fires each occurrence.
public sealed class RecurringJob
{
    public string Name { get; set; } = "";

    public string Cron { get; set; } = "";

    // The name the job is registered under.
    public string Type { get; set; } = "";

    // JSON text.
    public string Payload { get; set; } = "";

    public string Queue { get; set; } = "";

    public DateTimeOffset NextRunAt { get; set; }

    public bool Enabled { get; set; }
}
