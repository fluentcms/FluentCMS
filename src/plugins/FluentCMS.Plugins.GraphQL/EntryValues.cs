using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Publishing;
using HotChocolate.Language;

namespace FluentCMS.Plugins.GraphQL;

// An entry reaches the resolvers as the JSON the REST APIs answer, so that both surfaces show one value. These read a
// value of it as the runtime type of its GraphQL scalar, and a GraphQL literal as the JSON a write sends.
internal static class EntryValues
{
    private const string Discriminator = "type";

    public static object? Scalar(JsonNode? node, string scalar)
    {
        if (node is null)
        {
            return null;
        }

        return scalar switch
        {
            ScalarNames.Json => Json(node),
            ScalarNames.Boolean => node.GetValueKind() == JsonValueKind.True,
            ScalarNames.Int => (int)Number(node),
            ScalarNames.Long => (long)Number(node),
            ScalarNames.Decimal => Number(node),
            ScalarNames.DateTime => DateTimeOffset.Parse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ScalarNames.Date => DateOnly.ParseExact(Text(node), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Text(node),
        };
    }

    public static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);

    public static string? Id(JsonObject entry) => entry["id"] is { } id ? Text(id) : null;

    public static EntryStatus? Status(JsonNode? node) =>
        node is not null && Enum.TryParse<EntryStatus>(Text(node), ignoreCase: true, out var status) ? status : null;

    public static string? BlockType(JsonObject block) =>
        block[Discriminator] is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;

    // A populated entry has fields; a relation that was not populated is its id and type alone.
    public static bool IsReference(JsonObject entry) => entry.Count == 2 && entry.ContainsKey("id") && entry.ContainsKey(Discriminator);

    // A literal as JSON: an enum value is its name, which a select turns into its option before this is called.
    public static JsonNode? ToJson(IValueNode literal) => literal switch
    {
        NullValueNode => null,
        StringValueNode text => JsonValue.Create(text.Value),
        BooleanValueNode flag => JsonValue.Create(flag.Value),
        IntValueNode number => JsonValue.Create(number.ToInt64()),
        FloatValueNode number => JsonValue.Create(number.ToDecimal()),
        EnumValueNode name => JsonValue.Create(name.Value),
        ListValueNode list => new JsonArray([.. list.Items.Select(ToJson)]),
        ObjectValueNode fields => new JsonObject(fields.Fields.Select(field => KeyValuePair.Create(field.Name.Value, ToJson(field.Value)))),
        _ => throw new InvalidOperationException($"A {literal.Kind} is not a value."),
    };

    // The text REST would send for the value, which is what the filter of the content service reads.
    public static string? ToText(IValueNode literal) => literal switch
    {
        NullValueNode => null,
        StringValueNode text => text.Value,
        BooleanValueNode flag => flag.Value ? "true" : "false",
        IntValueNode number => number.Value,
        FloatValueNode number => number.Value,
        EnumValueNode name => name.Value,
        _ => ToJson(literal)?.ToJsonString(),
    };

    private static string Text(JsonNode node) => node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();

    private static decimal Number(JsonNode node) => decimal.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
