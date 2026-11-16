using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Api;

/// <summary>Reads and writes a <see cref="ContentEntry"/> as the API does: one object, the system members beside the fields.</summary>
/// <remarks>The members are split by name, since source-generated extension data cannot be a <see cref="JsonObject"/>.</remarks>
public sealed class ContentEntryConverter : JsonConverter<ContentEntry>
{
    /// <inheritdoc/>
    public override ContentEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject document)
        {
            throw new JsonException("An entry is a JSON object.");
        }

        var system = new JsonObject();
        foreach (var name in document.Select(member => member.Key).Where(ContentEntry.SystemMembers.Contains).ToList())
        {
            document.Remove(name, out var value);
            system[name] = value;
        }

        return new ContentEntry
        {
            Id = Value<Guid>(system, "id") ?? Guid.Empty,
            Status = Text(system, "status"),
            Locale = Text(system, "locale"),
            Version = Value<int>(system, "version") ?? 0,
            CreatedAt = Value<DateTimeOffset>(system, "createdAt"),
            CreatedBy = Value<Guid>(system, "createdBy"),
            UpdatedAt = Value<DateTimeOffset>(system, "updatedAt"),
            UpdatedBy = Value<Guid>(system, "updatedBy"),
            PublishedAt = Value<DateTimeOffset>(system, "publishedAt"),
            ScheduledPublishAt = Value<DateTimeOffset>(system, "scheduledPublishAt"),
            ScheduledUnpublishAt = Value<DateTimeOffset>(system, "scheduledUnpublishAt"),
            DeletedAt = Value<DateTimeOffset>(system, "deletedAt"),
            DeletedBy = Value<Guid>(system, "deletedBy"),
            Fallbacks = Fallbacks(system),
            Fields = document,
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ContentEntry value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        Write(writer, "status", value.Status);
        Write(writer, "locale", value.Locale);
        writer.WriteNumber("version", value.Version);
        Write(writer, "createdAt", value.CreatedAt);
        WriteIfSet(writer, "createdBy", value.CreatedBy);
        Write(writer, "updatedAt", value.UpdatedAt);
        WriteIfSet(writer, "updatedBy", value.UpdatedBy);
        Write(writer, "publishedAt", value.PublishedAt);
        Write(writer, "scheduledPublishAt", value.ScheduledPublishAt);
        Write(writer, "scheduledUnpublishAt", value.ScheduledUnpublishAt);
        if (value.DeletedAt is { } deletedAt)
        {
            writer.WriteString("deletedAt", deletedAt);
        }

        WriteIfSet(writer, "deletedBy", value.DeletedBy);
        if (value.Fallbacks is { } fallbacks)
        {
            writer.WriteStartObject("fallbacks");
            foreach (var (field, locale) in fallbacks)
            {
                writer.WriteString(field, locale);
            }

            writer.WriteEndObject();
        }

        // A field under a system member's name would write that member twice, so it is left out.
        foreach (var (name, field) in value.Fields.Where(member => !ContentEntry.SystemMembers.Contains(member.Key)))
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

    private static T? Value<T>(JsonObject system, string name)
        where T : struct
    {
        try
        {
            return system[name] is { } node ? node.GetValue<T>() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new JsonException($"The entry member '{name}' is not a {typeof(T).Name}.", exception);
        }
    }

    private static string? Text(JsonObject system, string name)
    {
        try
        {
            return system[name]?.GetValue<string>();
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException($"The entry member '{name}' is not a string.", exception);
        }
    }

    private static Dictionary<string, string>? Fallbacks(JsonObject system) => system["fallbacks"] switch
    {
        null => null,
        JsonObject fallbacks => fallbacks.ToDictionary(member => member.Key, member => Text(fallbacks, member.Key) ?? "", StringComparer.Ordinal),
        _ => throw new JsonException("The entry member 'fallbacks' is not an object."),
    };

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

    private static void WriteIfSet(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is { } id)
        {
            writer.WriteString(name, id);
        }
    }
}
