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
    // is no write: it goes, as if the client had left it out, and the composer keeps the stored one. It is compared where
    // the composer takes the stored value from: a single component is its own place, and an item of a list or an inline
    // block is paired with a stored one as CarryProtected pairs them, so the two cannot disagree. A read-only value on an
    // item that pairs with none, or that is not the one the read showed on its pair, is a change. Only a field the
    // principal may write is looked into: one it may not is the whole field's to answer for. A value that cannot be read
    // is left as sent, for the field check to refuse.
    private ContentDocument WithoutUnchangedInside(WriteScope write, ContentDocument changes, ContentDocument stored, ContentDocument? shown, IReadOnlySet<Guid>? unseen, WritableFields writable, bool addsLocale)
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
                && TryTakeOutUnchanged(write, field, value, stored.GetValueOrDefault(key), visible, unseen ?? new HashSet<Guid>(), out var kept)
                    ? kept
                    : value;
        }

        return result;
    }

    private bool TryTakeOutUnchanged(WriteScope write, SchemaField field, object? value, object? stored, object? shown, IReadOnlySet<Guid> unseen, out JsonNode? kept)
    {
        kept = null;
        try
        {
            if (!TryReadSent(field, value, out var sent) || !canonicalizer.TryRead(field, value, out var canonical))
            {
                return false;
            }

            var took = false;
            kept = TakeOutUnchanged(write, field, sent, new Views(EntryJson.ToNode(canonical), EntryJson.ToNode(stored), EntryJson.ToNode(shown)), unseen, ref took);
            return took;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // The same value as the canonicalizer reads what was sent, which is what the composer pairs, as stored, and as the
    // read showed it.
    private readonly record struct Views(JsonNode? Canonical, JsonNode? Stored, JsonNode? Shown)
    {
        public Views this[string key] => new(Canonical?[key], Stored?[key], Shown?[key]);
    }

    private JsonNode? TakeOutUnchanged(WriteScope write, SchemaField field, JsonNode? sent, Views views, IReadOnlySet<Guid> unseen, ref bool took)
    {
        if (sent is JsonValue text && text.TryGetValue<string>(out var json))
        {
            sent = field.FieldType == FieldTypeNames.RichText ? InlineBlockJson.Parse(json) : JsonDepth.ParseValue(json);
        }

        if (views.Canonical is not { } canonical || views.Stored is not { } stored || views.Shown is not { } shown)
        {
            return sent;
        }

        switch (field.FieldType)
        {
            case FieldTypeNames.RichText:
                TakeOutOfInline(write, field, sent, canonical, stored, shown, unseen, ref took);
                break;
            case FieldTypeNames.Component when !field.Multiple:
                if (sent is JsonObject group && canonical is JsonObject && stored is JsonObject && shown is JsonObject
                    && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
                {
                    TakeOutOf(write, component, group, views, isBlock: false, unseen, ref took);
                }

                break;
            case FieldTypeNames.Component or FieldTypeNames.Blocks:
                TakeOutOfItems(write, field, sent, canonical, stored, shown, unseen, ref took);
                break;
        }

        return sent;
    }

    private void TakeOutOfItems(WriteScope write, SchemaField field, JsonNode? sent, JsonNode canonical, JsonNode stored, JsonNode shown, IReadOnlySet<Guid> unseen, ref bool took)
    {
        if (sent is not JsonArray items || canonical is not JsonArray canonicalItems || items.Count != canonicalItems.Count
            || stored is not JsonArray storedItems || shown is not JsonArray shownItems || ShownIndexes(field, storedItems, shownItems) is not { } shownAt)
        {
            return;
        }

        var isBlock = field.FieldType == FieldTypeNames.Blocks;
        foreach (var (old, current) in composer.Pairs(field, stored, canonical, unseen))
        {
            var at = IndexOf(canonicalItems, current);
            var was = IndexOf(storedItems, old);
            if (at < 0 || was < 0 || shownAt[was] < 0 || items[at] is not JsonObject item || shownItems[shownAt[was]] is not JsonObject seen
                || ItemOwner(field, item) is not { } owner || ItemOwner(field, current) != owner || ItemOwner(field, old) != owner || ItemOwner(field, seen) != owner)
            {
                continue;
            }

            TakeOutOf(write, owner, item, new Views(current, old, seen), isBlock, unseen, ref took);
        }
    }

    private void TakeOutOfInline(WriteScope write, SchemaField field, JsonNode? sent, JsonNode canonical, JsonNode stored, JsonNode shown, IReadOnlySet<Guid> unseen, ref bool took)
    {
        var sentBlocks = InlineBlockJson.Find(sent);
        var canonicalBlocks = InlineBlockJson.Find(canonical);
        var storedBlocks = InlineBlockJson.Find(stored);
        var shownBlocks = InlineBlockJson.Find(shown);
        if (sentBlocks.Count != canonicalBlocks.Count || storedBlocks.Count != shownBlocks.Count)
        {
            return;
        }

        foreach (var (old, current) in composer.Pairs(field, stored, canonical, unseen))
        {
            var at = canonicalBlocks.FindIndex(block => ReferenceEquals(block.Data, current));
            var was = storedBlocks.FindIndex(block => ReferenceEquals(block.Data, old));
            if (at < 0 || was < 0 || sentBlocks[at].Data is not { } data || shownBlocks[was].Data is not { } seen
                || sentBlocks[at].BlockType != canonicalBlocks[at].BlockType || shownBlocks[was].BlockType != storedBlocks[was].BlockType
                || storedBlocks[was].BlockType != canonicalBlocks[at].BlockType || schema.FindBlock(storedBlocks[was].BlockType) is not { } block)
            {
                continue;
            }

            TakeOutOf(write, block, data, new Views(current, old, seen), isBlock: false, unseen, ref took);
        }
    }

    private void TakeOutOf(WriteScope write, SchemaType owner, JsonObject sent, Views views, bool isBlock, IReadOnlySet<Guid> unseen, ref bool took)
    {
        foreach (var (key, value) in sent.ToList())
        {
            if ((isBlock && key == BlockDiscriminator) || owner.FindField(key) is not { Hidden: false } inner)
            {
                continue;
            }

            if (inner.ReadOnly)
            {
                if (SameAsSeen(write, inner, value, views.Shown?[key]))
                {
                    sent.Remove(key);
                    took = true;
                }
            }
            else if (inner.FieldType is FieldTypeNames.Component or FieldTypeNames.Blocks or FieldTypeNames.RichText && value is not null)
            {
                var kept = TakeOutUnchanged(write, inner, value, views[key], unseen, ref took);
                if (!ReferenceEquals(kept, value))
                {
                    sent[key] = kept;
                }
            }
        }
    }

    // Where each stored item is in what the read showed, which leaves out a block of a type the field does not take.
    private int[]? ShownIndexes(SchemaField field, JsonArray stored, JsonArray shown)
    {
        var allowed = field.FieldType == FieldTypeNames.Blocks ? SettingValues.List(field, FieldSettings.Blocks) : null;
        var indexes = new int[stored.Count];
        var next = 0;
        for (var index = 0; index < stored.Count; index++)
        {
            var dropped = allowed is not null && stored[index] is JsonObject block
                && (BlockName(block) is not { } name || schema.FindBlock(name) is null || !allowed.Contains(name));
            indexes[index] = dropped ? -1 : next++;
        }

        return next == shown.Count ? indexes : null;
    }

    private SchemaType? ItemOwner(SchemaField field, JsonObject item) => field.FieldType == FieldTypeNames.Blocks
        ? BlockName(item) is { } name ? schema.FindBlock(name) : null
        : schema.FindComponent((string)field.Settings[FieldSettings.Component]);

    private static int IndexOf(JsonArray items, JsonObject item)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
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
