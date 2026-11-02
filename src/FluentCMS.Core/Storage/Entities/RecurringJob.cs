namespace FluentCMS.Core.Storage.Entities;

public sealed class RecurringJob
{
    public string Name { get; set; } = "";

    public string Cron { get; set; } = "";

    // The name the job is registered under.
    public string Type { get; set; } = "";

    public string Payload { get; set; } = "";

    public string Queue { get; set; } = "";

    public DateTimeOffset NextRunAt { get; set; }

    public bool Enabled { get; set; }
}
