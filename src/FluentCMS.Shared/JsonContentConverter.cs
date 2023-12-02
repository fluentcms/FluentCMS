using FluentCMS.Entities;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace FluentCMS;

public class JsonContentConverter() : JsonConverter<Content>
{
    private readonly static JsonConverter<string> stringConv =
        (JsonConverter<string>)JsonSerializerOptions.Default.GetConverter(typeof(string));

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(Content);
    }

    public override Content? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        var content = new Content();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return content;

            // Get the key.
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string? key = reader.GetString() ?? throw new JsonException();

            // Check for Id property
            if (key == "id")
            {
                reader.Read();
                content.Id = reader.GetGuid();
                continue;
            }

            // check for _t property
            if (key == "_t")
            {
                reader.Read();
                content.TypeId = reader.GetGuid();
                continue;
            }

            // check for CreatedBy property
            if (key.ToLower() == "createdby")
            {
                reader.Read();
                content.CreatedBy = reader.GetString() ?? string.Empty;
                continue;
            }

            // check for LastUpdateBy property
            if (key.ToLower() == "lastupdateby")
            {
                reader.Read();
                content.LastUpdatedBy = reader.GetString() ?? string.Empty;
                continue;
            }

            // check for CreatedBy property
            if (key.ToLower() == "createdat")
            {
                reader.Read();
                content.CreatedAt = reader.GetDateTime();
                continue;
            }

            // check for LastUpdateBy property
            if (key.ToLower() == "lastupdatedat")
            {
                reader.Read();
                content.LastUpdatedAt = reader.GetDateTime();
                continue;
            }

            // Get the value.
            reader.Read();

            // Add to dictionary.
            content.Add(key, ExtractValue(ref reader, options));
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Content content, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("id");
        writer.WriteStringValue(content.Id);

        writer.WritePropertyName("_t");
        writer.WriteStringValue(content.TypeId);

        writer.WritePropertyName("createdAt");
        writer.WriteStringValue(content.CreatedAt);

        writer.WritePropertyName("createdBy");
        writer.WriteStringValue(content.CreatedBy);

        writer.WritePropertyName("lastUpdatedAt");
        writer.WriteStringValue(content.LastUpdatedAt);

        writer.WritePropertyName("lastUpdatedBy");
        writer.WriteStringValue(content.LastUpdatedBy);


        foreach ((string key, object value) in content)
        {
            var propertyName = key.ToString();
            writer.WritePropertyName
                (options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName);

            stringConv.Write(writer, value.ToString(), options);
        }

        writer.WriteEndObject();
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
