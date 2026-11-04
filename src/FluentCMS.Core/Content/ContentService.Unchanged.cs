using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

internal sealed partial class ContentService
{
    private const string BlockDiscriminator = "type";

    // A read-only value inside a component, a block or the data of an inline block that comes back as the read showed it
    // at the same place is no write: it goes, as if the client had left it out, and the composer keeps the stored one. The
    // place is the index in a list, or among the inline blocks of a document, of an item of the same type, so a read-only
    // value in a block that moved, was added or lost its place to a removed one is a change. Only a field the principal
    // may write is looked into: one it may not is the whole field's to answer for. A value that cannot be read as JSON is
    // left as sent, for the field check to refuse.
    private ContentDocument WithoutUnchangedInside(WriteScope write, ContentDocument changes, ContentDocument? shown, WritableFields writable, bool addsLocale)
    {
        if (shown is null)
        {
            return changes;
        }

        ContentDocument result = [];
        foreach (var (key, value) in changes)
        {
            result[key] = write.Type.FindField(key) is { Hidden: false, ReadOnly: false } field
                && field.FieldType is FieldTypeNames.Component or FieldTypeNames.Blocks or FieldTypeNames.RichText
                && writable.Includes(key)
                && (!addsLocale || !field.Localizable)
                && shown.TryGetValue(key, out var visible)
                && TryTakeOutUnchanged(write, field, value, EntryJson.ToNode(visible), out var kept)
                    ? kept
                    : value;
        }

        return result;
    }

    private bool TryTakeOutUnchanged(WriteScope write, SchemaField field, object? value, JsonNode? seen, out JsonNode? kept)
    {
        kept = null;
        try
        {
            if (!TryReadSent(field, value, out var sent))
            {
                return false;
            }

            var took = false;
            kept = TakeOutUnchanged(write, field, sent, seen, ref took);
            return took;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private JsonNode? TakeOutUnchanged(WriteScope write, SchemaField field, JsonNode? sent, JsonNode? seen, ref bool took)
    {
        if (sent is JsonValue text && text.TryGetValue<string>(out var json))
        {
            sent = field.FieldType == FieldTypeNames.RichText ? InlineBlockJson.Parse(json) : JsonNode.Parse(json);
        }

        switch (field.FieldType)
        {
            case FieldTypeNames.RichText:
                var before = InlineBlockJson.Find(seen);
                var after = InlineBlockJson.Find(sent);
                for (var index = 0; index < after.Count && index < before.Count; index++)
                {
                    if (after[index].BlockType == before[index].BlockType && schema.FindBlock(after[index].BlockType) is { } block && after[index].Data is { } data)
                    {
                        TakeOutOf(write, block, data, before[index].Data, isBlock: false, ref took);
                    }
                }

                break;
            case FieldTypeNames.Component when schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                if (sent is JsonObject group)
                {
                    TakeOutOf(write, component, group, seen as JsonObject, isBlock: false, ref took);
                }
                else if (sent is JsonArray groups && seen is JsonArray seenGroups)
                {
                    for (var index = 0; index < groups.Count && index < seenGroups.Count; index++)
                    {
                        if (groups[index] is JsonObject item)
                        {
                            TakeOutOf(write, component, item, seenGroups[index] as JsonObject, isBlock: false, ref took);
                        }
                    }
                }

                break;
            case FieldTypeNames.Blocks when sent is JsonArray blocks && seen is JsonArray seenBlocks:
                for (var index = 0; index < blocks.Count && index < seenBlocks.Count; index++)
                {
                    if (blocks[index] is JsonObject item && seenBlocks[index] is JsonObject was
                        && BlockName(item) is { } name && name == BlockName(was) && schema.FindBlock(name) is { } block)
                    {
                        TakeOutOf(write, block, item, was, isBlock: true, ref took);
                    }
                }

                break;
        }

        return sent;
    }

    private void TakeOutOf(WriteScope write, SchemaType owner, JsonObject sent, JsonObject? seen, bool isBlock, ref bool took)
    {
        foreach (var (key, value) in sent.ToList())
        {
            if ((isBlock && key == BlockDiscriminator) || owner.FindField(key) is not { Hidden: false } inner)
            {
                continue;
            }

            if (inner.ReadOnly)
            {
                if (seen is not null && SameAsSeen(write, inner, value, seen[key]))
                {
                    sent.Remove(key);
                    took = true;
                }
            }
            else if (inner.FieldType is FieldTypeNames.Component or FieldTypeNames.Blocks or FieldTypeNames.RichText && value is not null)
            {
                var kept = TakeOutUnchanged(write, inner, value, seen?[key], ref took);
                if (!ReferenceEquals(kept, value))
                {
                    sent[key] = kept;
                }
            }
        }
    }

    // The same test as for a top-level field: what the read showed, with no hidden field named.
    private bool SameAsSeen(WriteScope write, SchemaField field, JsonNode? value, JsonNode? seen) =>
        write.Access.IsClean(field, value, write.Type)
        && canonicalizer.TryRead(field, value, out var read)
        && canonicalizer.TryRead(field, seen, out var was)
        && JsonNode.DeepEquals(Visible(field, read), Visible(field, was));

    private static string? BlockName(JsonObject block) =>
        block[BlockDiscriminator] is JsonValue name && name.TryGetValue<string>(out var text) ? text : null;

    // The forms the field types coerce, read as JSON without losing a key, so that the field check still sees every one.
    private static bool TryReadSent(SchemaField field, object? value, out JsonNode? node)
    {
        if (field.FieldType == FieldTypeNames.RichText)
        {
            node = FieldAccess.ReadDocument(value);
            return node is not null;
        }

        return value is JsonElement or JsonNode or string ? RawValue.TryReadJson(value, out node, out _) : TryToJson(value, out node);
    }

    private static bool TryToJson(object? value, out JsonNode? node)
    {
        node = null;
        switch (value)
        {
            case null:
                return true;
            case JsonElement or JsonNode or string or bool or int or long or decimal or double or DateOnly or DateTimeOffset or Guid or MediaRef or RichText:
                node = EntryJson.ToNode(value);
                return true;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                JsonObject result = [];
                foreach (var (key, inner) in pairs)
                {
                    if (!TryToJson(inner, out var item))
                    {
                        return false;
                    }

                    result.Add(key, item);
                }

                node = result;
                return true;
            case IDictionary:
                return false;
            case IEnumerable items:
                JsonArray list = [];
                foreach (var item in items)
                {
                    if (!TryToJson(item, out var inner))
                    {
                        return false;
                    }

                    list.Add(inner);
                }

                node = list;
                return true;
            default:
                return false;
        }
    }
}
