using System.Text.Json;

namespace FluentCMS.Core.Content;

// One version of an entry: its fields as they were written, and the version of the content type they were checked against.
// Stored once and never changed (CNT-02).
public sealed class EntryVersion
{
    public EntryVersion(Guid entryId, int version, int contentTypeVersion, JsonElement fields, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(contentTypeVersion, 1);
        if (fields.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The fields of a version are a JSON object.", nameof(fields));
        }

        EntryId = entryId;
        Version = version;
        ContentTypeVersion = contentTypeVersion;
        // A clone outlives the document it was read from.
        Fields = fields.Clone();
        CreatedAt = createdAt;
    }

    public Guid EntryId { get; }

    public int Version { get; }

    public int ContentTypeVersion { get; }

    public JsonElement Fields { get; }

    public DateTimeOffset CreatedAt { get; }
}
