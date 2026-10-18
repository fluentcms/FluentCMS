namespace FluentCMS.Infrastructure.Schema;

// A content type, the number of its current version and the number of its published one; each version's definition is its own
// ContentTypeVersionRow.
public sealed class ContentTypeRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public int Version { get; set; }

    // Moved only by publishing a change set (CHG-05); null until one publishes the type. Writes still move Version at once
    // until schema changes wait for publish (SCH-05).
    public int? PublishedVersion { get; set; }
}
