namespace FluentCMS.Infrastructure.Content;

// One version of an entry; inserted once and never changed (CNT-02), which a trigger enforces on both providers.
public sealed class EntryVersionRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public int Version { get; set; }

    public int ContentTypeVersion { get; set; }

    public string Fields { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
