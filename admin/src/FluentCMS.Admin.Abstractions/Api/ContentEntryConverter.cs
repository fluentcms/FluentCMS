using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Api;

/// <summary>Reads and writes a <see cref="ContentEntry"/> as the API does: one object, the system members beside the fields.</summary>
/// <remarks>The members are split by name, since source-generated extension data cannot be a <see cref="JsonObject"/>.</remarks>
public sealed class ContentEntryConverter : JsonConverter<ContentEntry>
{
    private const string Id = "id";
    private const string Status = "status";
    private const string Locale = "locale";
    private const string Version = "version";
    private const string CreatedAt = "createdAt";
    private const string UpdatedAt = "updatedAt";
    private const string PublishedAt = "publishedAt";
    private const string ScheduledPublishAt = "scheduledPublishAt";
    private const string ScheduledUnpublishAt = "scheduledUnpublishAt";

    /// <inheritdoc/>
    public override ContentEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject document)
        {
            throw new JsonException("An entry is a JSON object.");
        }

        return new ContentEntry
        {
            Id = Take(document, Id)?.GetValue<Guid>() ?? Guid.Empty,
            Status = Take(document, Status)?.GetValue<string>(),
            Locale = Take(document, Locale)?.GetValue<string>(),
            Version = Take(document, Version)?.GetValue<int>() ?? 0,
            CreatedAt = Take(document, CreatedAt)?.GetValue<DateTimeOffset>(),
            UpdatedAt = Take(document, UpdatedAt)?.GetValue<DateTimeOffset>(),
            PublishedAt = Take(document, PublishedAt)?.GetValue<DateTimeOffset>(),
            ScheduledPublishAt = Take(document, ScheduledPublishAt)?.GetValue<DateTimeOffset>(),
            ScheduledUnpublishAt = Take(document, ScheduledUnpublishAt)?.GetValue<DateTimeOffset>(),
            Fields = document,
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ContentEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(Id, value.Id);
        Write(writer, Status, value.Status);
        Write(writer, Locale, value.Locale);
        writer.WriteNumber(Version, value.Version);
        Write(writer, CreatedAt, value.CreatedAt);
        Write(writer, UpdatedAt, value.UpdatedAt);
        Write(writer, PublishedAt, value.PublishedAt);
        Write(writer, ScheduledPublishAt, value.ScheduledPublishAt);
        Write(writer, ScheduledUnpublishAt, value.ScheduledUnpublishAt);
        foreach (var (name, field) in value.Fields)
        {
            writer.WritePropertyName(name);
            if (field is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                field.WriteTo(writer, options);
            }
        }

        writer.WriteEndObject();
    }

    private static JsonNode? Take(JsonObject document, string name) =>
        document.Remove(name, out var value) ? value : null;

    private static void Write(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void Write(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is { } moment)
        {
            writer.WriteString(name, moment);
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}
