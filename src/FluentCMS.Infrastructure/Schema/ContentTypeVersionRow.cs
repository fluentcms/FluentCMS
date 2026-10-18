namespace FluentCMS.Infrastructure.Schema;

// One version of a content type definition; inserted once and never changed, so earlier versions stay readable.
public sealed class ContentTypeVersionRow
{
    public Guid Id { get; set; }

    public Guid ContentTypeId { get; set; }

    public int Version { get; set; }

    public string Definition { get; set; } = string.Empty;
}
