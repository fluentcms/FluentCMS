using System.Collections.Frozen;
using FluentCMS.Publishing;

namespace FluentCMS.Schema;

/// <summary>
/// Base class of every content type. It carries the system fields; the engine sets them.
/// The names of the system fields are reserved, see <see cref="ReservedFieldNames"/>.
/// </summary>
public abstract class ContentItem
{
    /// <summary>
    /// The reserved names, as they appear in the API: the system fields, <c>fallbacks</c>, which a response adds, the
    /// pending schedules a Management read adds, and <c>deletedAt</c> and <c>deletedBy</c>, which a read of the trash adds.
    /// Compared without regard to case, so a C# property named <c>CreatedAt</c> matches <c>createdAt</c>. A content
    /// type field with one of these names fails schema validation.
    /// </summary>
    public static IReadOnlySet<string> ReservedFieldNames { get; } = new[]
    {
        "id",
        "status",
        "locale",
        "version",
        "createdAt",
        "createdBy",
        "updatedAt",
        "updatedBy",
        "publishedAt",
        "fallbacks",
        "scheduledPublishAt",
        "scheduledUnpublishAt",
        "deletedAt",
        "deletedBy",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The id of the entry.</summary>
    public Guid Id { get; set; }

    /// <summary>The publishing state of this locale of the entry.</summary>
    public EntryStatus Status { get; set; }

    /// <summary>The locale of this entry, as a BCP 47 tag.</summary>
    public string Locale { get; set; } = "";

    /// <summary>The version number of this locale of the entry, used for optimistic concurrency.</summary>
    public int Version { get; set; }

    /// <summary>When the entry was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The id of the principal that created the entry.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>When the entry was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The id of the principal that last updated the entry.</summary>
    public Guid UpdatedBy { get; set; }

    /// <summary>When the entry was last published, or <see langword="null"/> if it never was.</summary>
    public DateTimeOffset? PublishedAt { get; set; }
}
