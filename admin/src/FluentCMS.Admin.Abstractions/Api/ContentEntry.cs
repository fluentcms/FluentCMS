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

    /// <summary>When this locale was last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>When this locale was last published, if it was.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>When this locale is scheduled to be published, if it is.</summary>
    public DateTimeOffset? ScheduledPublishAt { get; init; }

    /// <summary>When this locale is scheduled to be unpublished, if it is.</summary>
    public DateTimeOffset? ScheduledUnpublishAt { get; init; }

    /// <summary>The fields of the entry's type, keyed by their names, without the system members; a save sends them back.</summary>
    public JsonObject Fields { get; init; } = [];
}
