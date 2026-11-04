namespace FluentCMS.Core.Storage.Entities;

// A row of cms_schedule: one publish or unpublish of one locale of one entry, to run at a time. At most one is pending per
// document, locale and action; the others are history. Write it through the content service, which keeps it in step with its job.
public sealed class ScheduleRow
{
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";

    public const string Pending = "pending";
    public const string Executed = "executed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
    public const string Failed = "failed";

    public const int ErrorMaxLength = 2000;

    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    // The locale of the document row; empty for a type that is not localized.
    public string Locale { get; set; } = "";

    public string Action { get; set; } = Publish;

    public DateTimeOffset RunAtUtc { get; set; }

    // The zone the time was given in, kept so that it can be shown as it was entered.
    public string? TimeZone { get; set; }

    public string Status { get; set; } = Pending;

    // The job that will run it. A job that is not the one named here does nothing.
    public Guid? JobId { get; set; }

    public int Version { get; set; }

    // A hash of the shared (not localizable) fields of the draft when a publish was scheduled. They go out with any locale, and
    // can be saved through another, which leaves this locale's draft version as it was. Null for an unpublish and for a type that is not localized.
    public string? SharedHash { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // JSON object of the claims the scheduling principal held, which is what its roles, scopes and row rules read. Null when
    // the system principal scheduled it, which has nothing to check again.
    public string? Claims { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }
}
