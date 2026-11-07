using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Every block and inline block has an id that is unique within its top-level field at every depth (#2751): under "id"
// beside "type" on a block, and under "attrs.id" on an inline block. A write may send any id of 1 to 64 URL-safe
// characters, and one without gets a random id before it is stored.
internal sealed class BlockIds(SchemaModel schema)
{
    public const string Key = "id";

    private const string Discriminator = "type";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    private const int Length = 12;

    public static string? Of(JsonObject? holder) =>
        holder?[Key] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;

    // A block keeps "type" first and its id next to it; the attributes of an inline block are ordered when it is written.
    public static void Set(JsonObject holder, string id)
    {
        if (holder.ContainsKey(Discriminator) && !holder.ContainsKey(Key))
        {
            holder.Insert(Math.Min(1, holder.Count), Key, id);
        }
        else
        {
            holder[Key] = id;
        }
    }

    // The blocks a value of the field holds, at every depth, with the object that pairs (a block, or the data of an inline
    // block) and the one that holds the id. An inline block of a type the schema does not know is left as written.
    public List<BlockNode> Find(SchemaField field, JsonNode? value, string path)
    {
        List<BlockNode> found = [];
        Visit(field, value, path, found);
        return found;
    }

    // A stored block the scope finds is one of before's own nodes when before is JSON.
    public BlockScope Scope(SchemaField field, object? before, object? after)
    {
        if (!MayHoldBlocks(field))
        {
            return BlockScope.Empty;
        }

        Dictionary<string, (JsonObject, SchemaType)> stored = new(StringComparer.Ordinal);
        foreach (var block in Find(field, NodeOf(before), ""))
        {
            if (Of(block.Holder) is { } id && block.Item is { } item)
            {
                stored.TryAdd(id, (item, block.Owner));
            }
        }

        return stored.Count == 0
            ? BlockScope.Empty
            : new BlockScope(stored, [.. Find(field, NodeOf(after), "").Select(block => Of(block.Holder)).OfType<string>()]);
    }

    // An id is used once in a top-level field, wherever the block is.
    public void CheckUnique(SchemaType type, ContentDocument entry, List<ValidationError> errors)
    {
        foreach (var field in type.Fields.Where(MayHoldBlocks))
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var block in Find(field, NodeOf(entry.GetValueOrDefault(field.Name)), field.Name))
            {
                if (Of(block.Holder) is { } id && !seen.Add(id))
                {
                    errors.Add(new ValidationError($"{block.Path}.{Key}", ContentErrorCodes.DuplicateBlockId, $"The block id '{id}' is used more than once in {field.Name}."));
                }
            }
        }
    }

    // Gives each block that has no id a new one, in place, before the entry is written.
    public List<string> Assign(SchemaType type, ContentDocument entry)
    {
        List<string> assigned = [];
        foreach (var field in type.Fields.Where(MayHoldBlocks))
        {
            var parts = Parts(field, entry).ToList();
            var blocks = parts.Select(part => Find(part.Field, part.Node, "")).ToList();
            HashSet<string> used = new(blocks.SelectMany(found => found.Select(block => Of(block.Holder)).OfType<string>()), StringComparer.Ordinal);
            for (var index = 0; index < parts.Count; index++)
            {
                var missing = blocks[index].Where(block => Of(block.Holder) is null).ToList();
                foreach (var block in missing)
                {
                    Set(block.Holder, New(used));
                }

                if (missing.Count > 0)
                {
                    parts[index].Commit?.Invoke();
                    if (!assigned.Contains(field.Name))
                    {
                        assigned.Add(field.Name);
                    }
                }
            }
        }

        return assigned;
    }

    private static string New(HashSet<string> used)
    {
        string id;
        do
        {
            id = RandomNumberGenerator.GetString(Alphabet, Length);
        }
        while (!used.Add(id));

        return id;
    }

    private static JsonNode? NodeOf(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        RichText text => InlineBlockJson.MayHold(text.Json) ? InlineBlockJson.Parse(text.Json) : null,
        _ => EntryJson.ToNode(value),
    };

    private static bool MayHoldBlocks(SchemaField field) =>
        field.FieldType is FieldTypeNames.Blocks or FieldTypeNames.RichText or FieldTypeNames.Component;

    // The JSON of a stored field: rich text and a single component are not JSON in the entry, so a change to them is
    // written back.
    private IEnumerable<(SchemaField Field, JsonNode Node, Action? Commit)> Parts(SchemaField field, ContentDocument holder)
    {
        switch (holder.GetValueOrDefault(field.Name))
        {
            case JsonNode node:
                yield return (field, node, null);
                break;
            case RichText text when InlineBlockJson.MayHold(text.Json) && InlineBlockJson.Parse(text.Json) is { } document:
                yield return (field, document, () => holder[field.Name] = InlineBlockJson.ToRichText(document));
                break;
            case ContentDocument group when ComponentOf(field) is { } component:
                foreach (var inner in component.Fields.Where(MayHoldBlocks))
                {
                    foreach (var part in Parts(inner, group))
                    {
                        yield return part;
                    }
                }

                break;
        }
    }

    private void Visit(SchemaField field, JsonNode? value, string path, List<BlockNode> found)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.Blocks when value is JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    if (items[index] is JsonObject block && block[Discriminator] is JsonValue name && name.TryGetValue<string>(out var blockName)
                        && schema.FindBlock(blockName) is { } owner)
                    {
                        var at = $"{path}[{index}]";
                        found.Add(new BlockNode(block, block, owner, at));
                        VisitFields(owner, block, at, found);
                    }
                }

                break;
            case FieldTypeNames.RichText when value is JsonObject document:
                foreach (var inline in InlineBlockJson.Find(document))
                {
                    if (schema.FindBlock(inline.BlockType) is { } owner)
                    {
                        found.Add(new BlockNode(inline.Attributes, inline.Data, owner, $"{path}{inline.Path}.attrs"));
                        if (inline.Data is { } data)
                        {
                            VisitFields(owner, data, path + inline.DataPath, found);
                        }
                    }
                }

                break;
            case FieldTypeNames.Component when ComponentOf(field) is { } component:
                if (value is JsonArray groups)
                {
                    for (var index = 0; index < groups.Count; index++)
                    {
                        if (groups[index] is JsonObject group)
                        {
                            VisitFields(component, group, $"{path}[{index}]", found);
                        }
                    }
                }
                else if (value is JsonObject single)
                {
                    VisitFields(component, single, path, found);
                }

                break;
        }
    }

    private void VisitFields(SchemaType owner, JsonObject item, string path, List<BlockNode> found)
    {
        foreach (var field in owner.Fields.Where(MayHoldBlocks))
        {
            if (item[field.Name] is { } value)
            {
                Visit(field, value, $"{path}.{field.Name}", found);
            }
        }
    }

    private SchemaType? ComponentOf(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Component, out var name) && name is string component ? schema.FindComponent(component) : null;
}

// Holder carries the id; Item is what pairs, which for an inline block is its data and may be missing.
internal readonly record struct BlockNode(JsonObject Holder, JsonObject? Item, SchemaType Owner, string Path);

// The stored blocks of one top-level field by id, and the ids a write sends in it, so that a block pairs with its stored
// self wherever either of them is.
internal sealed class BlockScope(Dictionary<string, (JsonObject Item, SchemaType Owner)> stored, HashSet<string> sent)
{
    public static readonly BlockScope Empty = new([], []);

    public bool IsEmpty => stored.Count == 0;

    public JsonObject? Find(string id, SchemaType owner) =>
        stored.TryGetValue(id, out var found) && found.Owner == owner ? found.Item : null;

    // A stored block whose id the write sends is that block's, wherever it went, so it is no one else's to pair with.
    public bool IsSent(string? id) => id is not null && sent.Contains(id);
}
