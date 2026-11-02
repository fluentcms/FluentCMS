using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// The relations inside blocks and repeated components and the entry links inside rich text. They have no output of their
// own in a query, so the rules on a top-level relation reach them through here. A rich text link keeps its text.
internal sealed class NestedTargets(SchemaModel schema)
{
    private const string Discriminator = "type";

    public void Filter(SchemaType owner, ContentDocument entry, Func<string, Guid, bool> keep)
    {
        foreach (var field in owner.Fields.Where(field => !field.Hidden))
        {
            if (entry.TryGetValue(field.Name, out var value) && value is not null)
            {
                entry[field.Name] = FilterValue(field, value, keep);
            }
        }
    }

    public void Filter(SchemaType owner, JsonObject item, Func<string, Guid, bool> keep)
    {
        foreach (var field in owner.Fields.Where(field => !field.Hidden))
        {
            if (item[field.Name] is { } value)
            {
                FilterProperty(item, field, value, keep);
            }
        }
    }

    public Dictionary<string, HashSet<Guid>> Collect(SchemaType owner, ContentDocument entry)
    {
        Dictionary<string, HashSet<Guid>> found = new(StringComparer.Ordinal);
        Filter(owner, entry, Recording(found));
        return found;
    }

    public Dictionary<string, HashSet<Guid>> Collect(SchemaType owner, JsonObject item)
    {
        Dictionary<string, HashSet<Guid>> found = new(StringComparer.Ordinal);
        Filter(owner, item, Recording(found));
        return found;
    }

    public static bool DropLinks(JsonNode? node, Func<string, Guid, bool> keep)
    {
        if (node is not JsonObject item)
        {
            return false;
        }

        var changed = false;
        if (item["marks"] is JsonArray marks)
        {
            foreach (var mark in marks.OfType<JsonObject>().Where(mark => IsUnwantedLink(mark, keep)).ToList())
            {
                marks.Remove(mark);
                changed = true;
            }

            if (changed && marks.Count == 0)
            {
                item.Remove("marks");
            }
        }

        if (item["content"] is JsonArray content)
        {
            foreach (var child in content)
            {
                changed |= DropLinks(child, keep);
            }
        }

        return changed;
    }

    public static RichText DropLinks(RichText document, Func<string, Guid, bool> keep)
    {
        if (!document.Json.Contains(RichTextMarkTypes.EntryLink, StringComparison.Ordinal))
        {
            return document;
        }

        var node = JsonNode.Parse(document.Json);
        return DropLinks(node, keep) ? new RichText(EntryJson.Text(node!)) : document;
    }

    private static Func<string, Guid, bool> Recording(Dictionary<string, HashSet<Guid>> found) => (type, id) =>
    {
        if (!found.TryGetValue(type, out var ids))
        {
            ids = [];
            found[type] = ids;
        }

        ids.Add(id);
        return true;
    };

    private static bool IsUnwantedLink(JsonObject mark, Func<string, Guid, bool> keep) =>
        mark[Discriminator] is JsonValue kind && kind.TryGetValue<string>(out var name) && name == RichTextMarkTypes.EntryLink
        && mark["attrs"] is JsonObject attributes
        && attributes["entryType"] is JsonValue type && type.TryGetValue<string>(out var typeName)
        && attributes["entryId"] is JsonValue target && target.TryGetValue<string>(out var text)
        && Guid.TryParse(text, out var id)
        && !keep(typeName, id);

    private object? FilterValue(SchemaField field, object value, Func<string, Guid, bool> keep)
    {
        switch (value)
        {
            case RichText document when field.FieldType == FieldTypeNames.RichText:
                return DropLinks(document, keep);
            case JsonNode node:
                FilterItems(field, node, keep);
                break;
            case ContentDocument group when field.FieldType == FieldTypeNames.Component && Component(field) is { } component:
                Filter(component, group, keep);
                break;
            case ContentDocument populated when RelationTarget(field) is { } target:
                Filter(target, populated, keep);
                break;
            case IEnumerable<ContentDocument> populated when RelationTarget(field) is { } target:
                foreach (var item in populated)
                {
                    Filter(target, item, keep);
                }

                break;
        }

        return value;
    }

    private void FilterProperty(JsonObject holder, SchemaField field, JsonNode value, Func<string, Guid, bool> keep)
    {
        if (field.Relation is { } relation)
        {
            if (value is JsonArray ids)
            {
                foreach (var id in ids.Where(id => !IsKept(relation.Target, id, keep)).ToList())
                {
                    ids.Remove(id);
                }
            }
            else if (!IsKept(relation.Target, value, keep))
            {
                holder[field.Name] = null;
            }
        }
        else if (field.FieldType == FieldTypeNames.RichText)
        {
            DropLinks(value, keep);
        }
        else
        {
            FilterItems(field, value, keep);
        }
    }

    private void FilterItems(SchemaField field, JsonNode node, Func<string, Guid, bool> keep)
    {
        foreach (var item in node is JsonArray items ? items.OfType<JsonObject>() : node is JsonObject single ? [single] : [])
        {
            var owner = field.FieldType switch
            {
                FieldTypeNames.Component => Component(field),
                FieldTypeNames.Blocks when item[Discriminator] is JsonValue name && name.TryGetValue<string>(out var block) => schema.FindBlock(block),
                _ => null,
            };

            if (owner is not null)
            {
                Filter(owner, item, keep);
            }
        }
    }

    private static bool IsKept(string target, JsonNode? value, Func<string, Guid, bool> keep) =>
        !EntryCanonicalizer.TryReadId(RawValue.Unwrap(value), out var id) || keep(target, id);

    private SchemaType? Component(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Component, out var name) && name is string component ? schema.FindComponent(component) : null;

    private SchemaType? RelationTarget(SchemaField field) =>
        field.Relation is { } relation ? schema.FindContentType(relation.Target) : null;
}
