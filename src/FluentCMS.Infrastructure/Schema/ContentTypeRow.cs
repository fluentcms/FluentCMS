namespace FluentCMS.Infrastructure.Schema;

// A content type and the number of its current version; each version's definition is its own ContentTypeVersionRow.
public sealed class ContentTypeRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public int Version { get; set; }
}
