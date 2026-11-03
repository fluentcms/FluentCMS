namespace FluentCMS.Core.Storage.Entities;

// A row of cms_outbox: one event for one outbox handler, written in the transaction that raised the event.
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = "";

    // The name the handler was registered under.
    public string Handler { get; set; } = "";

    public string Payload { get; set; } = "";

    public Guid? SiteId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    // Rises with the order rows were recorded in, and within an event with the order its handlers run in.
    public long Sequence { get; set; }

    public string Status { get; set; } = OutboxStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public string? LeaseOwner { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }
}

public static class OutboxStatus
{
    public const string Pending = "pending";

    public const string Delivered = "delivered";

    // Out of attempts: it stays for someone to look at.
    public const string Failed = "failed";
}
