using FluentCMS.Publishing;

namespace FluentCMS.Core.Storage.Entities;

// A row of cms_document: the type-independent state of one document in one locale.
public sealed class Document
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    // Empty for a non-localized document, never null, because a null locale defeats the unique key.
    public string Locale { get; set; } = "";

    public string Type { get; set; } = "";

    // Null for a type with ContentScope.Global.
    public Guid? SiteId { get; set; }

    public EntryStatus Status { get; set; }

    public int DraftVersion { get; set; }

    public int? PublishedVersion { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? FirstPublishedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset? SchedulePublishAt { get; set; }

    public DateTimeOffset? ScheduleUnpublishAt { get; set; }
}
