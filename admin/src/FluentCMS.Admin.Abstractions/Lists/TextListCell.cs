using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components.Rendering;

namespace FluentCMS.Admin.Lists;

/// <summary>
/// The cell of a field type that has none registered: its value as plain text. A list of values is shown comma separated,
/// and a populated relation by the label it was read with.
/// </summary>
public sealed class TextListCell : ListCellBase
{
    // Field types whose value reads as text, and a relation, whose label the list reads with populate and fields. Rich text,
    // JSON, components, blocks and a plugin's types wait for a cell of their own.
    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal)
    {
        "text", "longText", "email", "url", "slug", "integer", "decimal", "boolean", "date", "dateTime", "select", "relation",
    };

    internal static bool Shows(string fieldType) => TextTypes.Contains(fieldType);

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "cms-list-cell-text");
        builder.AddContent(2, Text(Value));
        builder.CloseElement();
    }

    internal static string? Text(JsonNode? value) => value switch
    {
        null => null,
        JsonArray items => string.Join(", ", items.Select(Text).Where(text => !string.IsNullOrEmpty(text))),
        JsonObject entry => Label(entry),
        JsonValue scalar when scalar.GetValueKind() == JsonValueKind.String => scalar.GetValue<string>(),
        JsonValue scalar when scalar.TryGetValue<decimal>(out var number) => number.ToString(CultureInfo.CurrentCulture),
        _ => value.ToJsonString(),
    };

    // The first text member of a populated entry, since the list reads it with fields that name its label only; a reference
    // that was not populated has its id.
    private static string? Label(JsonObject entry) =>
        entry.Where(member => member.Key is not ("type" or "id") && member.Value is JsonValue { } text && text.GetValueKind() == JsonValueKind.String)
            .Select(member => member.Value!.GetValue<string>())
            .FirstOrDefault()
        ?? (entry["id"] is JsonValue id && id.GetValueKind() == JsonValueKind.String ? id.GetValue<string>() : null);
}
