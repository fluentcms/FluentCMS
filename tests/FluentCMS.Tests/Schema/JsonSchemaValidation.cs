using System.Text.Json.Nodes;
using NJsonSchema;

namespace FluentCMS.Tests.Schema;

// Checks documents with an independent validator, so the tests do not grade the schema with the logic that wrote it.
internal static class JsonSchemaValidation
{
    public static async Task<IReadOnlyList<string>> ErrorsAsync(JsonNode schema, JsonNode? instance)
    {
        var parsed = await JsonSchema.FromJsonAsync(schema.ToJsonString());
        return parsed.Validate(instance?.ToJsonString() ?? "null").Select(error => error.ToString()).ToList();
    }
}
