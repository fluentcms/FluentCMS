using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS;

public class DictionaryJsonConverter : JsonConverter<Dictionary<string, object?>>
{
    public override Dictionary<string, object?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        var dictionary = new Dictionary<string, object?>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return dictionary;

            // Get the key.
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            var propertyName = reader.GetString() ??
                throw new JsonException();

            // Get the value.
            reader.Read();
            var value = MapValue(ref reader, options);

            // Add to dictionary.
            dictionary.Add(propertyName, value);
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value, options);
        }

        writer.WriteEndObject();
    }

    private object? MapValue(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.StartObject:
                return Read(ref reader, typeof(Dictionary<string, object?>), options);
            case JsonTokenType.StartArray:
                return ReadArray(ref reader, options);
            case JsonTokenType.None:
                break;
            case JsonTokenType.EndObject:
                break;
            case JsonTokenType.EndArray:
                break;
            case JsonTokenType.PropertyName:
                break;
            case JsonTokenType.Comment:
                break;
            default:
                throw new JsonException();
        }
        return null;
    }

    private object?[] ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<object?>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list.ToArray();

            var value = MapValue(ref reader, options);
            list.Add(value);
        }

        throw new JsonException();
    }

    private void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case DateTime dateTimeValue:
                writer.WriteStringValue(dateTimeValue.ToString());
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case Dictionary<string, object?> dictionaryValue:
                Write(writer, dictionaryValue, options);
                break;
            case object?[] arrayValue:
                WriteArray(writer, arrayValue, options);
                break;
            default:
                throw new JsonException();
        }
    }

    private void WriteArray(Utf8JsonWriter writer, object?[] arrayValue, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var value in arrayValue)
        {
            WriteValue(writer, value, options);
        }

        writer.WriteEndArray();
    }
}
