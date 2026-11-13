using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Applies ContentQuery.RichTextFormat to what a read returns: rich text becomes the rendered text, at every level and
// in populated entries. Stored JSON is the default and needs nothing. A media node gets its URL and, where it has none,
// its alternative text from the resolver of media, asked once for every node of the read.
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

    public async Task ApplyAsync(SchemaType type, IReadOnlyCollection<ContentDocument> entries, string format, ReferenceResolvers resolvers, string locale, CancellationToken cancellationToken)
    {
        RichTextRenderOptions? options = null;
        if (resolvers.Find(RichTextNodeTypes.Media) is { } media)
        {
            HashSet<Guid> ids = [];
            foreach (var entry in entries)
            {
                Walk(type, entry, document =>
                {
                    if (document.Json.Contains(RichTextNodeTypes.Media, StringComparison.Ordinal))
                    {
                        ids.UnionWith(RichTextReferenceExtractor.Extract(document).Media.Select(found => found.Id));
                    }
                });
            }

            if (ids.Count > 0)
            {
                var resolved = await resolvers.ResolveAsync(media, ids, locale, cancellationToken);
                options = new RichTextRenderOptions
                {
                    MediaUrlResolver = id => resolved.GetValueOrDefault(id)?.Url,
                    MediaAltResolver = id => resolved.GetValueOrDefault(id)?.Text,
                };
            }
        }

        foreach (var entry in entries)
        {
            Walk(type, entry, document => renderer.Render(document, format, options));
        }
    }

    // Replaces every rich text value with what render makes of it.
    private void Walk(SchemaType type, ContentDocument entry, Func<RichText, object?> render)
    {
        foreach (var field in type.Fields)
        {
            if (entry.TryGetValue(field.Name, out var value) && value is not null)
            {
                entry[field.Name] = Render(field, value, render);
            }
        }
    }

    private void Walk(SchemaType type, ContentDocument entry, Action<RichText> visit) =>
        Walk(type, entry, document =>
        {
            visit(document);
            return document;
        });

    private object? Render(SchemaField field, object value, Func<RichText, object?> render)
    {
        switch (value)
        {
            case RichText document:
                return render(document);
            case ContentDocument inner when field.Relation is { } relation && schema.FindContentType(relation.Target) is { } target:
                Walk(target, inner, render);
                return inner;
            case IEnumerable<ContentDocument> many when field.Relation is { } relation && schema.FindContentType(relation.Target) is { } target:
                foreach (var item in many)
                {
                    Walk(target, item, render);
                }

                return value;
            case ContentDocument group when field.FieldType == FieldTypeNames.Component && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                Walk(component, group, render);
                return group;
            case JsonNode node:
                RenderInside(field, node, render);
                return node;
            default:
                return value;
        }
    }

    private void RenderInside(SchemaField field, JsonNode node, Func<RichText, object?> render)
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
                        var rendered = render(new RichText(EntryJson.Text(document)));
                        if (rendered is string text)
                        {
                            item[inner.Name] = text;
                        }

                        break;
                    case { } value:
                        RenderInside(inner, value, render);
                        break;
                }
            }
        }
    }
}
