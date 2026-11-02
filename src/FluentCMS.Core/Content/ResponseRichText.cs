using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Applies ContentQuery.RichTextFormat to what a read returns: rich text becomes the rendered text, at every level and
// in populated entries. Stored JSON is the default and needs nothing.
internal sealed class ResponseRichText(SchemaModel schema, IRichTextRenderer renderer)
{
    private const string Json = "json";
    private const string Discriminator = "type";

    public static bool IsJson(string? format) => format is null || string.Equals(format, Json, StringComparison.OrdinalIgnoreCase);

    public void ThrowIfUnknown(string? format)
    {
        if (!IsJson(format) && !renderer.Formats.Contains(format, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationError("richText", QueryErrorCodes.InvalidParameter, $"'{format}' is not a rich text format.")]);
        }
    }

    public void Apply(SchemaType type, ContentDocument entry, string format)
    {
        foreach (var field in type.Fields)
        {
            if (entry.TryGetValue(field.Name, out var value) && value is not null)
            {
                entry[field.Name] = Render(field, value, format);
            }
        }
    }

    private object? Render(SchemaField field, object value, string format)
    {
        switch (value)
        {
            case RichText document:
                return renderer.Render(document, format);
            case ContentDocument inner when field.Relation is { } relation && schema.FindContentType(relation.Target) is { } target:
                Apply(target, inner, format);
                return inner;
            case IEnumerable<ContentDocument> many when field.Relation is { } relation && schema.FindContentType(relation.Target) is { } target:
                foreach (var item in many)
                {
                    Apply(target, item, format);
                }

                return value;
            case ContentDocument group when field.FieldType == FieldTypeNames.Component && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                Apply(component, group, format);
                return group;
            case JsonNode node:
                RenderInside(field, node, format);
                return node;
            default:
                return value;
        }
    }

    private void RenderInside(SchemaField field, JsonNode node, string format)
    {
        foreach (var item in node is JsonArray items ? items.OfType<JsonObject>() : node is JsonObject single ? [single] : [])
        {
            SchemaType? owner = field.FieldType switch
            {
                FieldTypeNames.Component => schema.FindComponent((string)field.Settings[FieldSettings.Component]),
                FieldTypeNames.Blocks when item[Discriminator] is JsonValue name && name.TryGetValue<string>(out var block) => schema.FindBlock(block),
                _ => null,
            };

            foreach (var inner in owner?.Fields ?? [])
            {
                switch (item[inner.Name])
                {
                    case JsonObject document when inner.FieldType == FieldTypeNames.RichText:
                        item[inner.Name] = renderer.Render(new RichText(EntryJson.Text(document)), format);
                        break;
                    case { } value:
                        RenderInside(inner, value, format);
                        break;
                }
            }
        }
    }
}
