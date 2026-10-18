using System.Text.Json.Nodes;

namespace FluentCMS.Core.Schema;

// How a value of one field looks, for the schema document agents read (SCH-06): a JSON Schema for one value, an example that
// satisfies it and a sentence about it. The example comes from the field type, so it is always a valid value.
public sealed record FieldValueDescription(JsonObject Schema, JsonNode? Example, string Description)
{
    // Stands in for the id of an entry, entity or asset in examples.
    public const string ExampleId = "0199a000-0000-7000-8000-000000000001";
}
