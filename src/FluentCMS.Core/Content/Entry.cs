using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Content;

// An entry of a content type, in the type's site (CNT-02, SCH-07). Every write makes the next version, which becomes the latest
// draft; the published version moves only when a change set is published. The id, site and content type never change.
public sealed class Entry
{
    private Entry(Guid id, Guid siteId, Guid contentTypeId, string contentType, int? publishedVersion, EntryVersion latest)
    {
        Id = id;
        SiteId = siteId;
        ContentTypeId = contentTypeId;
        ContentType = contentType;
        PublishedVersion = publishedVersion;
        Latest = latest;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public Guid ContentTypeId { get; }

    // The slug of the content type.
    public string ContentType { get; }

    // The latest draft.
    public EntryVersion Latest { get; }

    public int LatestVersion => Latest.Version;

    // Null until the entry is first published.
    public int? PublishedVersion { get; }

    public static Result<Entry> Create(ContentType type, JsonElement fields, FieldTypeRegistry fieldTypes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        var validated = EntryFields.Validate(fields, type.Definition, fieldTypes);
        if (!validated.IsSuccess)
        {
            return Result<Entry>.Failure(validated.Errors);
        }

        var id = Guid.CreateVersion7(now);
        return Result<Entry>.Success(
            new Entry(id, type.SiteId, type.Id, type.Slug, publishedVersion: null, new EntryVersion(id, 1, type.Version, validated.Value, now)));
    }

    // Rebuilds a stored entry as it was written, without validating again: an entry stays readable after its type changes.
    public static Entry Restore(Guid id, Guid siteId, Guid contentTypeId, string contentType, int? publishedVersion, EntryVersion latest)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(latest);

        if (latest.EntryId != id)
        {
            throw new ArgumentException($"Version {latest.Version} belongs to entry {latest.EntryId}, not {id}.", nameof(latest));
        }

        return new Entry(id, siteId, contentTypeId, contentType, publishedVersion, latest);
    }

    // Checked against the type's version given, normally its current one. The current instance is left as it is, so a failed
    // revision changes nothing.
    public Result<Entry> Revise(ContentType type, JsonElement fields, FieldTypeRegistry fieldTypes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        if (type.Id != ContentTypeId)
        {
            throw new ArgumentException($"Entry {Id} is of the content type {ContentTypeId}, not {type.Id}.", nameof(type));
        }

        var validated = EntryFields.Validate(fields, type.Definition, fieldTypes);
        if (!validated.IsSuccess)
        {
            return Result<Entry>.Failure(validated.Errors);
        }

        var next = new EntryVersion(Id, LatestVersion + 1, type.Version, validated.Value, now);
        return Result<Entry>.Success(new Entry(Id, SiteId, ContentTypeId, ContentType, PublishedVersion, next));
    }
}
