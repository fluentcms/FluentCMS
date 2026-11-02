using System.Text;
using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Validation;

// A condition in the JSON shape schema.json holds, by way of the schema reader and writer, so there is one format.
public static class ConditionJson
{
    public static void Write(Condition condition, Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(writer);
        SchemaJsonWriter.WriteCondition(writer, condition);
    }

    public static string ToJson(Condition condition)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(condition, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Condition Read(JsonElement element)
    {
        // The reader reports problems on a builder; one without types fails its build with exactly those problems.
        var builder = new SchemaBuilder();
        var condition = new SchemaJsonReader(builder).ReadCondition(element, "condition");

        try
        {
            builder.Build();
        }
        catch (SchemaValidationException exception)
        {
            throw new JsonException($"The condition is not valid: {string.Join(" ", exception.Errors.Select(error => error.Message.TrimStart(':', ' ')))}");
        }

        return condition ?? throw new JsonException("The condition is not valid.");
    }

    public static Condition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }
}
