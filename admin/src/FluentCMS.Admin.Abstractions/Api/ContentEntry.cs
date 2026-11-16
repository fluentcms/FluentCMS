using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Api;

/// <summary>
/// An entry of the content group in one locale: the system members typed, and the fields of its type as the JSON document
/// a form edits, since the admin is generic over content types.
/// </summary>
[JsonConverter(typeof(ContentEntryConverter))]
public sealed record ContentEntry
{
    /// <summary>The names of the system members, which no field may have; they are never part of <see cref="Fields"/> and a save leaves them out.</summary>
    public static IReadOnlySet<string> SystemMembers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id", "status", "locale", "version", "createdAt", "createdBy", "updatedAt", "updatedBy", "publishedAt", "fallbacks",
        "scheduledPublishAt", "scheduledUnpublishAt", "deletedAt", "deletedBy",
    };

    /// <summary>The id of the entry.</summary>
    public Guid Id { get; init; }

    /// <summary>The status in this locale: <c>draft</c>, <c>published</c>, <c>modified</c> or <c>unpublished</c>.</summary>
    public string? Status { get; init; }

    /// <summary>The locale, empty for a type that is not localized.</summary>
    public string? Locale { get; init; }

    /// <summary>The version in this locale, which the entity tag of a read carries.</summary>
    public int Version { get; init; }

    /// <summary>When the entry was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Who created the entry, when the read asked for it with <c>fields</c>.</summary>
    public Guid? CreatedBy { get; init; }

    /// <summary>When this locale was last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Who last changed this locale, when the read asked for it with <c>fields</c>.</summary>
    public Guid? UpdatedBy { get; init; }

    /// <summary>When this locale was last published, if it was.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>When this locale is scheduled to be published, if it is.</summary>
    public DateTimeOffset? ScheduledPublishAt { get; init; }

    /// <summary>When this locale is scheduled to be unpublished, if it is.</summary>
    public DateTimeOffset? ScheduledUnpublishAt { get; init; }

    /// <summary>When the entry was moved to the trash, on a read of the trash.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>Who moved the entry to the trash, on a read of the trash.</summary>
    public Guid? DeletedBy { get; init; }

    /// <summary>
    /// The locale each field was read from when the site's fallback chain filled it, or <see langword="null"/> when nothing
    /// fell back. A read with fallbacks carries no entity tag, since it is no precondition for this locale.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Fallbacks { get; init; }

    /// <summary>The fields of the entry's type, keyed by their names, without the system members; a save sends them back.</summary>
    public JsonObject Fields { get; init; } = [];
}
