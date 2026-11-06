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
    // block is paired with a stored one as CarryProtected pairs them, by its id anywhere in the top-level field or else
    // within its list, so the two cannot disagree. A read-only value on an item that pairs with none, or that is not the
    // one the read showed on its pair, is a change. Only a field the principal may write is looked into: one it may not is
    // the whole field's to answer for. A value that cannot be read is left as sent, for the field check to refuse.
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
            var views = new Views(EntryJson.ToNode(canonical), EntryJson.ToNode(stored), EntryJson.ToNode(shown));
            var check = new Check(composer.Scope(field, views.Stored, views.Canonical), ShownItems(field, views.Stored, views.Shown), unseen);
            kept = TakeOutUnchanged(write, field, sent, views, check, ref took);
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

    // What holds for the whole top-level field: its stored blocks by id, since a block may come from anywhere in it,
    // where each stored item is in what the read showed, and the targets the read left out.
    private sealed record Check(BlockScope Blocks, Dictionary<JsonObject, JsonObject> Shown, IReadOnlySet<Guid> Unseen);

    private JsonNode? TakeOutUnchanged(WriteScope write, SchemaField field, JsonNode? sent, Views views, Check check, ref bool took)
    {
        if (sent is JsonValue text && text.TryGetValue<string>(out var json))
        {
            sent = field.FieldType == FieldTypeNames.RichText ? InlineBlockJson.Parse(json) : JsonDepth.ParseValue(json);
        }

        if (views.Canonical is not { } canonical)
        {
            return sent;
        }

        switch (field.FieldType)
        {
            case FieldTypeNames.RichText:
                TakeOutOfInline(write, field, sent, canonical, views.Stored, check, ref took);
                break;
            case FieldTypeNames.Component when !field.Multiple:
                if (sent is JsonObject group && canonical is JsonObject
                    && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
                {
                    TakeOutOf(write, component, group, views, isBlock: false, check, ref took);
                }

                break;
            case FieldTypeNames.Component or FieldTypeNames.Blocks:
                TakeOutOfItems(write, field, sent, canonical, views.Stored, check, ref took);
                break;
        }

        return sent;
    }

    // An item that pairs with none is looked into all the same, since what it holds may pair with a stored block by id.
    private void TakeOutOfItems(WriteScope write, SchemaField field, JsonNode? sent, JsonNode canonical, JsonNode? stored, Check check, ref bool took)
    {
        if (sent is not JsonArray items || canonical is not JsonArray canonicalItems || items.Count != canonicalItems.Count)
        {
            return;
        }

        var isBlock = field.FieldType == FieldTypeNames.Blocks;
        var pairs = PairedWith(composer.Pairs(field, stored as JsonArray ?? [], canonicalItems, check.Unseen, check.Blocks));
        for (var at = 0; at < items.Count; at++)
        {
            if (items[at] is not JsonObject item || canonicalItems[at] is not JsonObject current
                || ItemOwner(field, item) is not { } owner || ItemOwner(field, current) != owner)
            {
                continue;
            }

            TakeOutOf(write, owner, item, PairViews(current, pairs, check), isBlock, check, ref took);
        }
    }

    private void TakeOutOfInline(WriteScope write, SchemaField field, JsonNode? sent, JsonNode canonical, JsonNode? stored, Check check, ref bool took)
    {
        var sentBlocks = InlineBlockJson.Find(sent);
        var canonicalBlocks = InlineBlockJson.Find(canonical);
        if (sentBlocks.Count != canonicalBlocks.Count)
        {
            return;
        }

        var pairs = PairedWith(composer.Pairs(field, stored ?? new JsonObject(), canonical, check.Unseen, check.Blocks));
        for (var at = 0; at < canonicalBlocks.Count; at++)
        {
            if (sentBlocks[at].Data is not { } data || canonicalBlocks[at].Data is not { } current
                || sentBlocks[at].BlockType != canonicalBlocks[at].BlockType || schema.FindBlock(canonicalBlocks[at].BlockType) is not { } block)
            {
                continue;
            }

            TakeOutOf(write, block, data, PairViews(current, pairs, check), isBlock: false, check, ref took);
        }
    }

    private static Dictionary<JsonObject, JsonObject> PairedWith(List<(JsonObject Old, JsonObject New)> pairs) =>
        pairs.ToDictionary<(JsonObject Old, JsonObject New), JsonObject, JsonObject>(pair => pair.New, pair => pair.Old, ReferenceEqualityComparer.Instance);

    private static Views PairViews(JsonObject current, Dictionary<JsonObject, JsonObject> pairs, Check check) =>
        pairs.TryGetValue(current, out var old) ? new Views(current, old, check.Shown.GetValueOrDefault(old)) : new Views(current, null, null);

    // A read-only value is compared only where the read showed the item it pairs with.
    private void TakeOutOf(WriteScope write, SchemaType owner, JsonObject sent, Views views, bool isBlock, Check check, ref bool took)
    {
        foreach (var (key, value) in sent.ToList())
        {
            if ((isBlock && key is BlockDiscriminator or BlockIds.Key) || owner.FindField(key) is not { Hidden: false } inner)
            {
                continue;
            }

            if (inner.ReadOnly)
            {
                if (views.Shown is JsonObject seen && SameAsSeen(write, inner, value, seen[key]))
                {
                    sent.Remove(key);
                    took = true;
                }
            }
            else if (inner.FieldType is FieldTypeNames.Component or FieldTypeNames.Blocks or FieldTypeNames.RichText && value is not null)
            {
                var kept = TakeOutUnchanged(write, inner, value, views[key], check, ref took);
                if (!ReferenceEquals(kept, value))
                {
                    sent[key] = kept;
                }
            }
        }
    }

    // Where each stored item is in what the read showed, at every depth: a read leaves out what is hidden and a block of a
    // type the field does not take, and keeps everything else in its place.
    private Dictionary<JsonObject, JsonObject> ShownItems(SchemaField field, JsonNode? stored, JsonNode? shown)
    {
        Dictionary<JsonObject, JsonObject> map = new(ReferenceEqualityComparer.Instance);
        MapShown(field, stored, shown, map);
        return map;
    }

    private void MapShown(SchemaField field, JsonNode? stored, JsonNode? shown, Dictionary<JsonObject, JsonObject> map)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.RichText when stored is JsonObject && shown is JsonObject:
                var storedBlocks = InlineBlockJson.Find(stored);
                var shownBlocks = InlineBlockJson.Find(shown);
                for (var index = 0; storedBlocks.Count == shownBlocks.Count && index < storedBlocks.Count; index++)
                {
                    if (storedBlocks[index].Data is { } data && shownBlocks[index].Data is { } seen
                        && storedBlocks[index].BlockType == shownBlocks[index].BlockType && schema.FindBlock(storedBlocks[index].BlockType) is { } block)
                    {
                        map[data] = seen;
                        MapInside(block, data, seen, map);
                    }
                }

                break;
            case FieldTypeNames.Component when !field.Multiple:
                if (stored is JsonObject group && shown is JsonObject seenGroup
                    && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
                {
                    MapInside(component, group, seenGroup, map);
                }

                break;
            case FieldTypeNames.Component or FieldTypeNames.Blocks when stored is JsonArray storedItems && shown is JsonArray shownItems:
                if (ShownIndexes(field, storedItems, shownItems) is not { } shownAt)
                {
                    break;
                }

                for (var index = 0; index < storedItems.Count; index++)
                {
                    if (shownAt[index] >= 0 && storedItems[index] is JsonObject item && shownItems[shownAt[index]] is JsonObject seen
                        && ItemOwner(field, item) is { } owner && ItemOwner(field, seen) == owner)
                    {
                        map[item] = seen;
                        MapInside(owner, item, seen, map);
                    }
                }

                break;
        }
    }

    private void MapInside(SchemaType owner, JsonObject stored, JsonObject shown, Dictionary<JsonObject, JsonObject> map)
    {
        foreach (var field in owner.Fields.Where(field => !field.Hidden && field.FieldType is FieldTypeNames.Component or FieldTypeNames.Blocks or FieldTypeNames.RichText))
        {
            MapShown(field, stored[field.Name], shown[field.Name], map);
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
