using FluentCMS.Content;

namespace FluentCMS.Core.Storage.Entities;

// A row of cms_activity: one thing that happened, who did it and from where. Rows are only added, and removed by the retention job.
public sealed class ActivityRow
{
    public Guid Id { get; set; }

    // Null for what belongs to the instance, such as the failure of a job.
    public Guid? SiteId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public PrincipalKind ActorType { get; set; }

    public Guid ActorId { get; set; }

    public string ActorName { get; set; } = "";

    public Origin Origin { get; set; }

    public string? ClientId { get; set; }

    public string Action { get; set; } = "";

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    public string? TargetLabel { get; set; }

    public string? Locale { get; set; }

    // JSON text of an object of strings, null when there are no details.
    public string? Details { get; set; }

    public string CorrelationId { get; set; } = "";

    public string? Ip { get; set; }
}
