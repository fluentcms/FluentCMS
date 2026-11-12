using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS.Client;

// The system fields of an entry, which the server sets; a write leaves them out.
public class Entry
{
    internal static readonly string[] SystemFields = ["id", "status", "locale", "version", "createdAt", "createdBy", "updatedAt", "updatedBy", "publishedAt", "fallbacks"];

    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; set; }
}

// The content type a generated class is an entry of, so that the client's calls need no type name.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContentTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

// A relation as a read gives it: a reference { "id", "type" }, or the populated entry. A write sends the id.
[JsonConverter(typeof(RelationConverterFactory))]
public sealed class Relation<T>(Guid id)
    where T : Entry
{
    public Guid Id { get; } = id;

    public string? Type { get; init; }

    // The entry, when the read populated the relation.
    public T? Entry { get; init; }
}

// A media field as a read gives it: a reference { "id", "type": "media" }, or the populated asset. A write sends the id.
[JsonConverter(typeof(MediaReferenceConverter))]
public sealed class MediaReference(Guid id)
{
    public Guid Id { get; } = id;

    public string? Type { get; init; }

    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    public long? Size { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? Title { get; init; }

    public string? Alt { get; init; }

    public string? Caption { get; init; }

    public string? Credit { get; init; }

    public MediaFocalPoint? FocalPoint { get; init; }

    public MediaUrls? Urls { get; init; }
}

public sealed record MediaFocalPoint(double X, double Y);

public sealed record MediaUrls(string? Original, IReadOnlyDictionary<string, string>? Presets, string? Template);

public sealed record EntryPage<T>(IReadOnlyList<T> Items, int? Total, int? Page, int? PageSize, string? NextCursor);

public sealed record PublishResult<T>(T Entry, IReadOnlyList<PublishWarning> Warnings);

public sealed record PublishWarning(string Path, string Code, string Message, string? TargetType = null, Guid? TargetId = null);

// The locale of a write; the default locale of the site when left out.
public sealed record WriteOptions(string? Locale = null);

internal sealed class RelationConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Relation<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(RelationConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class RelationConverter<T> : JsonConverter<Relation<T>>
    where T : Entry
{
    public override Relation<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind == JsonValueKind.String)
        {
            return new Relation<T>(element.GetGuid());
        }

        var populated = element.EnumerateObject().Any(property => property.Name is not ("id" or "type"));
        return new Relation<T>(element.GetProperty("id").GetGuid())
        {
            Type = element.TryGetProperty("type", out var type) ? type.GetString() : null,
            Entry = populated ? element.Deserialize<T>(options) : null,
        };
    }

    public override void Write(Utf8JsonWriter writer, Relation<T> value, JsonSerializerOptions options) => writer.WriteStringValue(value.Id);
}

internal sealed class MediaReferenceConverter : JsonConverter<MediaReference>
{
    public override MediaReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind == JsonValueKind.String)
        {
            return new MediaReference(element.GetGuid());
        }

        var asset = element.Deserialize<Asset>(options)!;
        return new MediaReference(asset.Id)
        {
            Type = asset.Type,
            FileName = asset.FileName,
            MimeType = asset.MimeType,
            Size = asset.Size,
            Width = asset.Width,
            Height = asset.Height,
            Title = asset.Title,
            Alt = asset.Alt,
            Caption = asset.Caption,
            Credit = asset.Credit,
            FocalPoint = asset.FocalPoint,
            Urls = asset.Urls,
        };
    }

    public override void Write(Utf8JsonWriter writer, MediaReference value, JsonSerializerOptions options) => writer.WriteStringValue(value.Id);

    private sealed record Asset(
        Guid Id,
        string? Type,
        string? FileName,
        string? MimeType,
        long? Size,
        int? Width,
        int? Height,
        string? Title,
        string? Alt,
        string? Caption,
        string? Credit,
        MediaFocalPoint? FocalPoint,
        MediaUrls? Urls);
}
