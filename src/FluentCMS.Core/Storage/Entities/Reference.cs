namespace FluentCMS.Core.Storage.Entities;

// A row of cms_reference: one relation or media value of a document, in one state and locale.
public sealed class Reference
{
    public Guid Id { get; set; }

    public string SourceType { get; set; } = "";

    public Guid SourceDocumentId { get; set; }

    public short State { get; set; }

    // Empty for a non-localized value, never null, because a null locale defeats the unique key.
    public string Locale { get; set; } = "";

    public string Field { get; set; } = "";

    public int Position { get; set; }

    public string TargetType { get; set; } = "";

    public Guid TargetId { get; set; }
}
