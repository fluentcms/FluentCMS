namespace FluentCMS.Infrastructure.Content;

// An entry and its pointers to the latest draft and the published version; each version's fields are their own EntryVersionRow.
public sealed class EntryRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid ContentTypeId { get; set; }

    public int LatestVersion { get; set; }

    public int? PublishedVersion { get; set; }

    // Set while the entry is soft deleted (CNT-06).
    public DateTimeOffset? DeletedAt { get; set; }
}
