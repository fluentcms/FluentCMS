using FluentCMS.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS;

public class JsonContentConverter<TContent>() : JsonConverter<TContent> where TContent : Content, new()
{
    protected readonly static JsonConverter<Dictionary<string, object?>> _dictConvertor =
    (JsonConverter<Dictionary<string, object?>>)JsonSerializerOptions.Default.GetConverter(typeof(Dictionary<string, object?>)) ??
        throw new InvalidOperationException("No converter found for Dictionary<string, object?>.");

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(TContent);
    }

    public override TContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        var content = new TContent();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return content;

            // Get the key.
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string? key = reader.GetString() ?? throw new JsonException();

            // Get the value.
            reader.Read();

            switch (key.ToLower())
            {
                case "id":
                    content.Id = reader.GetGuid();
                    continue;

                case "siteid":
                    content.SiteId = reader.GetGuid();
                    continue;

                case "type":
                    content.Type = reader.GetString() ?? string.Empty;
                    continue;

                case "createdby":
                    content.CreatedBy = reader.GetString() ?? string.Empty;
                    continue;

                case "lastupdatedby":
                    content.LastUpdatedBy = reader.GetString() ?? string.Empty;
                    continue;

                case "createdat":
                    content.CreatedAt = reader.GetDateTime();
                    continue;

                case "lastupdatedat":
                    content.LastUpdatedAt = reader.GetDateTime();
                    continue;

                default:
                    break;
            }

            // Add to dictionary.
            content.Add(key, ExtractValue(ref reader, options));
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, TContent content, JsonSerializerOptions options)
    {
        var dict = content.ToDictionary();
        _dictConvertor.Write(writer, dict, options);
    }

    private object? ExtractValue(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                if (reader.TryGetDateTime(out var date))
                {
                    return date;
                }
                return reader.GetString();
            case JsonTokenType.False:
                return false;
            case JsonTokenType.True:
                return true;
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var result))
                {
                    return result;
                }
                return reader.GetDecimal();
            case JsonTokenType.StartObject:
                return Read(ref reader, null!, options);
            case JsonTokenType.StartArray:
                var list = new List<object?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    list.Add(ExtractValue(ref reader, options));
                }
                return list;
            default:
                throw new JsonException($"'{reader.TokenType}' is not supported");
        }
    }
}
