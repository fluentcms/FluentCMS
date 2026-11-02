using FluentCMS.Content;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Storage.Entities;

// A row of cms_version: the immutable snapshot of one save of a document in one locale.
public sealed class ContentVersion
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string Locale { get; set; } = "";

    public int Number { get; set; }

    // JSON text.
    public string Snapshot { get; set; } = "";

    public EntryStatus StatusAfter { get; set; }

    public Guid AuthorId { get; set; }

    public Origin Origin { get; set; }

    public string? ClientId { get; set; }

    public string? Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
