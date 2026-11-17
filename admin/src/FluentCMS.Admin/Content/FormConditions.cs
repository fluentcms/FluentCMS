using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Content;

// visibleWhen and requiredWhen against the form's values, as the server's ConditionEvaluator reads them (spec 4.5): a list
// equals a value it contains and an empty list equals null, an unknown field is null. An operator this build does not know
// decides nothing, so its field is shown and not required here, and the server decides at publish.
internal static class FormConditions
{
    public static bool Shows(SchemaFieldDescription field, JsonObject values) =>
        field.VisibleWhen is not { } condition || Evaluate(condition, values) != false;

    public static bool Requires(SchemaFieldDescription field, JsonObject values) =>
        field.Validation.Required || (field.RequiredWhen is { } condition && Evaluate(condition, values) == true);

    public static bool? Evaluate(ConditionDescription condition, JsonObject values)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(values);
        var operands = condition.Operands ?? [];
        switch (condition.Operator)
        {
            case "equal":
                return Matches(Get(values, condition), condition.Values is [var expected, ..] ? expected : null);
            case "in":
                var actual = Get(values, condition);
                return (condition.Values ?? []).Any(expected => Matches(actual, expected));
            case "isEmpty":
                return IsEmpty(Get(values, condition));
            case "and":
                var all = operands.Select(operand => Evaluate(operand, values)).ToList();
                return all.Contains(false) ? false : all.Contains(null) ? null : true;
            case "or":
                var any = operands.Select(operand => Evaluate(operand, values)).ToList();
                return any.Contains(true) ? true : any.Contains(null) ? null : false;
            case "not":
                return operands is [var single] ? !Evaluate(single, values) : null;
            default:
                return null;
        }
    }

    // As the server's FieldValue.IsEmpty: no value, blank text, an empty list, a reference without an id and a rich text
    // document without text.
    public static bool IsEmpty(JsonNode? value) => value switch
    {
        null => true,
        JsonValue text when text.GetValueKind() == JsonValueKind.String => string.IsNullOrWhiteSpace(text.GetValue<string>()),
        JsonValue other => other.GetValueKind() == JsonValueKind.Null,
        JsonArray items => items.Count == 0,
        JsonObject document when Text(document["type"]) == "doc" => HasNoContent(document),
        JsonObject reference when reference.ContainsKey("id") => IsEmpty(reference["id"]) || Text(reference["id"]) == Guid.Empty.ToString("D"),
        _ => false,
    };

    private static JsonNode? Get(JsonObject values, ConditionDescription condition) =>
        condition.Field is { } name && values.TryGetPropertyValue(name, out var value) ? value : null;

    private static bool Matches(JsonNode? actual, JsonNode? expected)
    {
        if (actual is JsonArray items)
        {
            return IsNull(expected) ? items.Count == 0 : items.Any(item => Same(item, expected));
        }

        return Same(actual, expected);
    }

    // Strings by ordinal, numbers by value, a reference by its id.
    private static bool Same(JsonNode? actual, JsonNode? expected)
    {
        if (actual is JsonObject reference && reference.TryGetPropertyValue("id", out var id))
        {
            actual = id;
        }

        if (IsNull(actual) || IsNull(expected))
        {
            return IsNull(actual) && IsNull(expected);
        }

        if (actual is not JsonValue left || expected is not JsonValue right)
        {
            return false;
        }

        return (left.GetValueKind(), right.GetValueKind()) switch
        {
            (JsonValueKind.String, JsonValueKind.String) => string.Equals(left.GetValue<string>(), right.GetValue<string>(), StringComparison.Ordinal),
            (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => left.GetValueKind() == right.GetValueKind(),
            (JsonValueKind.Number, JsonValueKind.Number) => Number(left) is { } a && Number(right) is { } b && a == b,
            _ => false,
        };
    }

    private static bool IsNull(JsonNode? node) => node is null || node.GetValueKind() == JsonValueKind.Null;

    private static decimal? Number(JsonValue value) => value.TryGetValue<decimal>(out var number)
        ? number
        : decimal.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "doc", "paragraph", "heading", "bulletList", "orderedList", "listItem", "blockquote", "codeBlock", "hardBreak",
    };

    private static bool HasNoContent(JsonNode? node)
    {
        if (node is not JsonObject item || Text(item["type"]) is not { } type)
        {
            return false;
        }

        if (type == "text")
        {
            return string.IsNullOrWhiteSpace(Text(item["text"]));
        }

        return Containers.Contains(type) && (item["content"] is not JsonArray content || content.All(HasNoContent));
    }
}
