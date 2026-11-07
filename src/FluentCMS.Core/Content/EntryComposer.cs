using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Makes the entry an update asks for out of what is stored and what the client sent, before it is canonicalized.
internal sealed class EntryComposer(SchemaModel schema)
{
    private readonly NestedTargets _nested = new(schema);
    private readonly BlockIds _blocks = new(schema);

    private const string Discriminator = "type";

    private static readonly HashSet<Guid> NoTargets = [];

    // A full update describes the whole entry, but only as far as the client can see and change it: keep says which
    // fields it has no say over. A key that is not a field stays, so that the canonicalizer refuses it.
    public static ContentDocument Replace(SchemaType type, ContentDocument stored, ContentDocument incoming, Func<SchemaField, bool> keep)
    {
        ContentDocument composed = [];
        foreach (var field in type.Fields)
        {
            if (incoming.TryGetValue(field.Name, out var value))
            {
                composed[field.Name] = value;
            }
            else if (keep(field))
            {
                stored.TryGetValue(field.Name, out var kept);
                composed[field.Name] = kept;
            }
        }

        foreach (var (key, value) in incoming)
        {
            if (type.FindField(key) is null && SystemField.Find(key) is null)
            {
                composed[key] = value;
            }
        }

        return composed;
    }

    // RFC 7396: a null removes, an object merges into a single component or a JSON field, and anything else replaces.
    // A block list and a repeated component are arrays, so they replace, and rich text is one document.
    public ContentDocument Patch(SchemaType type, ContentDocument stored, ContentDocument patch)
    {
        ContentDocument composed = [];
        foreach (var field in type.Fields)
        {
            stored.TryGetValue(field.Name, out var value);
            composed[field.Name] = value;
        }

        foreach (var (key, value) in patch)
        {
            if (SystemField.Find(key) is null)
            {
                composed[key] = type.FindField(key) is { } field ? Merge(field, stored.GetValueOrDefault(key), value) : value;
            }
        }

        return composed;
    }

    private object? Merge(SchemaField field, object? stored, object? patch)
    {
        if (RawValue.Unwrap(patch) is null)
        {
            return null;
        }

        if (field.FieldType == FieldTypeNames.Component && !field.Multiple && EntryCanonicalizer.TryReadMap(patch, out var map) && map is not null
            && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
        {
            ContentDocument group = stored as ContentDocument is { } existing ? new(existing) : [];
            foreach (var (key, value) in map)
            {
                group[key] = component.FindField(key) is { } inner ? Merge(inner, group.GetValueOrDefault(key), value) : value;
            }

            return group;
        }

        if (field.FieldType == FieldTypeNames.Json && RawValue.TryReadJson(patch, out var node, out _) && node is JsonObject changes)
        {
            return MergeJson(stored as JsonNode, changes);
        }

        return patch;
    }

    private static JsonObject MergeJson(JsonNode? stored, JsonObject patch)
    {
        var result = stored is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
        foreach (var (key, value) in patch)
        {
            switch (value)
            {
                case null:
                    result.Remove(key);
                    break;
                case JsonObject inner:
                    result[key] = MergeJson(result[key], inner);
                    break;
                default:
                    result[key] = value.DeepClone();
                    break;
            }
        }

        return result;
    }

    // What a client cannot see or change inside a component or a block stays when it sends the rest back, even when it
    // sends no component at all. A block sent with its id keeps the values of the stored block of the same id and type,
    // wherever either is in the top-level field, so it can move into or out of a nested list or document; one whose id is
    // not stored is new. A block sent without an id keeps the values of the stored block of the same type in its list
    // whose visible content is identical, each used once, and what is left pairs up in order within its type; it then
    // takes the id of the block it paired with. A block both moved and edited without its id can still pair with the
    // wrong one. A single component is left alone on a patch only for the system principal, which may be clearing a
    // value, since a patch merged into a component already keeps the rest. unseen holds the nested targets a read left
    // out: they stay too, and a rich text document keeps its links only when it comes back as it was read, since its
    // other nodes have no ids to pair an edit by. The inline blocks of a document that changed pair up as the blocks of a
    // list do, across the whole document.
    public void CarryProtected(SchemaType type, ContentDocument stored, ContentDocument composed, bool includingGroups, IReadOnlySet<Guid>? unseen = null)
    {
        unseen ??= NoTargets;
        foreach (var field in type.Fields)
        {
            stored.TryGetValue(field.Name, out var before);
            composed.TryGetValue(field.Name, out var after);
            var blocks = _blocks.Scope(field, before, after);
            if (field.FieldType == FieldTypeNames.Component && !field.Multiple)
            {
                if (includingGroups && before is ContentDocument oldGroup && ComponentOf(field) is { } component)
                {
                    CarryGroup(component, oldGroup, after as ContentDocument, group => composed[field.Name] = group, blocks, unseen);
                }
            }
            else if (before is RichText oldText && after is RichText newText)
            {
                composed[field.Name] = CarryRichText(field, oldText, newText, blocks, unseen);
            }
            else if (before is JsonNode oldNode && after is JsonNode newNode)
            {
                CarryInside(field, oldNode, newNode, blocks, unseen);
            }
        }
    }

    // The blocks of a top-level field by id. A stored block Pairs answers with is one of before's own nodes.
    public BlockScope Scope(SchemaField field, JsonNode? before, JsonNode? after) => _blocks.Scope(field, before, after);

    // The top-level fields in which a block got an id.
    public List<string> AssignBlockIds(SchemaType type, ContentDocument entry) => _blocks.Assign(type, entry);

    private RichText CarryRichText(SchemaField field, RichText before, RichText after, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        if (unseen.Count == 0 && blocks.IsEmpty && !InlineBlockJson.MayHold(before.Json))
        {
            return after;
        }

        var old = InlineBlockJson.Parse(before.Json)!;
        var current = InlineBlockJson.Parse(after.Json)!;
        if (CameBack(field, old, current, unseen))
        {
            return before;
        }

        return CarryInlineBlocks(old, current, blocks, unseen) ? InlineBlockJson.ToRichText(current) : after;
    }

    // As a read showed it, which is all a client could send back. What the client did send is compared as it is, so that
    // the system principal changing a hidden value is a change.
    private bool CameBack(SchemaField field, JsonNode before, JsonNode after, IReadOnlySet<Guid> unseen) =>
        JsonNode.DeepEquals(ComparableValue(field, before, unseen), after);

    // Which stored item each sent one keeps what it cannot see from, as CarryProtected pairs them: the objects of a block
    // list or a repeated component, or the data of the inline blocks of a rich text document. blocks is the scope of the
    // top-level field the value is in, so an item may pair with a stored one that is not in before.
    public List<(JsonObject Old, JsonObject New)> Pairs(SchemaField field, JsonNode before, JsonNode after, IReadOnlySet<Guid> unseen, BlockScope blocks) => field.FieldType switch
    {
        FieldTypeNames.RichText => PairInline(before, after, blocks, unseen).Pairs,
        _ when before is JsonArray oldItems && after is JsonArray newItems => PairItems(field, oldItems, newItems, blocks, unseen),
        _ => [],
    };

    private bool CarryInlineBlocks(JsonNode before, JsonNode after, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        var was = after.DeepClone();
        var (pairs, owners, ids, newData) = PairInline(before, after, blocks, unseen);
        foreach (var (oldItem, newItem) in pairs)
        {
            if (ids[newItem] is null && ids.GetValueOrDefault(oldItem) is { } id)
            {
                BlockIds.Set(newData.Find(item => ReferenceEquals(item.Data, newItem)).Block.Attributes, id);
            }

            CarryIntoObject(owners[newItem], oldItem, newItem, blocks, unseen);
        }

        if (!blocks.IsEmpty)
        {
            var paired = pairs.Select(pair => pair.New).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var (data, _) in newData.Where(item => !paired.Contains(item.Data)))
            {
                CarryIntoObject(owners[data], [], data, blocks, unseen);
            }
        }

        foreach (var (data, block) in newData.Where(item => item.Data.Count > 0 && item.Block.Data is null))
        {
            block.Attributes["data"] = data;
        }

        return !JsonNode.DeepEquals(was, after);
    }

    // Ids holds the id of each data object of both documents; one that paired without an id paired within before.
    private (List<(JsonObject Old, JsonObject New)> Pairs, Dictionary<JsonObject, SchemaType> Owners, Dictionary<JsonObject, string?> Ids, List<(JsonObject Data, InlineBlockJsonNode Block)> NewData) PairInline(JsonNode before, JsonNode after, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        Dictionary<JsonObject, SchemaType> owners = new(ReferenceEqualityComparer.Instance);
        Dictionary<JsonObject, string?> ids = new(ReferenceEqualityComparer.Instance);
        List<JsonObject> oldData = [];
        foreach (var block in InlineBlockJson.Find(before))
        {
            if (schema.FindBlock(block.BlockType) is { } owner && block.Data is { } data)
            {
                owners[data] = owner;
                ids[data] = BlockIds.Of(block.Attributes);
                oldData.Add(data);
            }
        }

        List<(JsonObject Data, InlineBlockJsonNode Block)> newData = [];
        foreach (var block in InlineBlockJson.Find(after))
        {
            if (schema.FindBlock(block.BlockType) is { } owner)
            {
                var data = block.Data ?? [];
                owners[data] = owner;
                ids[data] = BlockIds.Of(block.Attributes);
                newData.Add((data, block));
            }
        }

        var pairs = PairUp([.. oldData], [.. newData.Select(item => item.Data)], item => owners.GetValueOrDefault(item), item => ids.GetValueOrDefault(item), blocks, unseen);
        return (pairs, owners, ids, newData);
    }

    private List<(JsonObject Old, JsonObject New)> PairItems(SchemaField field, JsonArray before, JsonArray after, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        Func<JsonObject, string?> idOf = field.FieldType == FieldTypeNames.Blocks ? BlockIds.Of : static _ => null;
        return PairUp([.. before.OfType<JsonObject>()], [.. after.OfType<JsonObject>()], item => OwnerOf(field, item), idOf, blocks, unseen);
    }

    private SchemaType? ComponentOf(SchemaField field) =>
        field.Settings.TryGetValue(FieldSettings.Component, out var name) && name is string component ? schema.FindComponent(component) : null;

    // An absent component comes back only if it has something to carry.
    private void CarryGroup(SchemaType component, ContentDocument before, ContentDocument? after, Action<ContentDocument> set, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        var group = after ?? [];
        foreach (var field in component.Fields)
        {
            var old = before.GetValueOrDefault(field.Name);
            var current = group.GetValueOrDefault(field.Name);
            if ((field.Hidden || field.ReadOnly) && old is not null && current is null)
            {
                group[field.Name] = old;
            }
            else if (current is RichText newText && (old is RichText || !blocks.IsEmpty))
            {
                group[field.Name] = CarryRichText(field, old as RichText ?? RichText.Empty, newText, blocks, unseen);
            }
            else if (current is JsonNode newNode && (old is JsonNode || !blocks.IsEmpty))
            {
                CarryInside(field, old as JsonNode, newNode, blocks, unseen);
            }
            else if (field.FieldType == FieldTypeNames.Component && !field.Multiple && ComponentOf(field) is { } inner
                && (old is ContentDocument || (!blocks.IsEmpty && current is ContentDocument)))
            {
                CarryGroup(inner, old as ContentDocument ?? [], current as ContentDocument, value => group[field.Name] = value, blocks, unseen);
            }
        }

        if (after is null && group.Values.Any(value => value is not null))
        {
            set(group);
        }
    }

    // Where nothing is stored at the place there is nothing to fall back on, but a block with an id may come from elsewhere.
    private void CarryInside(SchemaField field, JsonNode? before, JsonNode after, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        if (after is JsonArray newItems && (before is JsonArray || (before is null && !blocks.IsEmpty)))
        {
            var pairs = PairItems(field, before as JsonArray ?? [], newItems, blocks, unseen);
            foreach (var (oldItem, newItem) in pairs)
            {
                if (OwnerOf(field, newItem) is not { } owner)
                {
                    continue;
                }

                if (field.FieldType == FieldTypeNames.Blocks && BlockIds.Of(newItem) is null && BlockIds.Of(oldItem) is { } id)
                {
                    BlockIds.Set(newItem, id);
                }

                CarryIntoObject(owner, oldItem, newItem, blocks, unseen);
            }

            if (!blocks.IsEmpty)
            {
                var paired = pairs.Select(pair => pair.New).ToHashSet(ReferenceEqualityComparer.Instance);
                foreach (var item in newItems.OfType<JsonObject>().Where(item => !paired.Contains(item)))
                {
                    if (OwnerOf(field, item) is { } owner)
                    {
                        CarryIntoObject(owner, [], item, blocks, unseen);
                    }
                }
            }
        }
        else if (after is JsonObject newObject && OwnerOf(field, newObject) is { } single
            && ((before is JsonObject oldObject && OwnerOf(field, oldObject) == single) || (before is null && !blocks.IsEmpty)))
        {
            CarryIntoObject(single, before as JsonObject ?? [], newObject, blocks, unseen);
        }
    }

    // A block with an id pairs only by it, and a stored block whose id the write sends is not left for another to take.
    private List<(JsonObject Old, JsonObject New)> PairUp(List<JsonObject> before, List<JsonObject> after, Func<JsonObject, SchemaType?> ownerOf, Func<JsonObject, string?> idOf, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        List<(JsonObject Old, JsonObject New)> pairs = [];
        List<JsonObject> oldLeft = [.. before.Where(old => !blocks.IsSent(idOf(old)))];
        List<JsonObject> withoutId = [];
        foreach (var item in after)
        {
            if (idOf(item) is not { } id)
            {
                withoutId.Add(item);
            }
            else if (ownerOf(item) is { } owner && blocks.Find(id, owner) is { } stored)
            {
                pairs.Add((stored, item));
            }
        }

        List<JsonObject> newLeft = [];
        foreach (var item in withoutId)
        {
            var same = ownerOf(item) is { } owner
                ? oldLeft.FindIndex(old => ownerOf(old) == owner && JsonNode.DeepEquals(Comparable(owner, old, unseen), Comparable(owner, item, unseen)))
                : -1;

            if (same < 0)
            {
                newLeft.Add(item);
            }
            else
            {
                pairs.Add((oldLeft[same], item));
                oldLeft.RemoveAt(same);
            }
        }

        foreach (var item in newLeft)
        {
            var owner = ownerOf(item);
            var next = oldLeft.FindIndex(old => ownerOf(old) == owner);
            if (owner is not null && next >= 0)
            {
                pairs.Add((oldLeft[next], item));
                oldLeft.RemoveAt(next);
            }
        }

        return pairs;
    }

    // What a client saw of a block apart from its id: no protected values, and no key that holds nothing, which is the
    // same as no key.
    private JsonObject Comparable(SchemaType owner, JsonObject item, IReadOnlySet<Guid> unseen)
    {
        JsonObject result = [];
        foreach (var (key, value) in item)
        {
            var field = owner.FindField(key);
            if (value is null || field is { Hidden: true } or { ReadOnly: true } || (field is null && key == BlockIds.Key))
            {
                continue;
            }

            var inner = field is not null ? ComparableValue(field, value, unseen) : value.DeepClone();
            if (inner is not (null or JsonObject { Count: 0 }))
            {
                result[key] = inner;
            }
        }

        return result;
    }

    private JsonNode? ComparableValue(SchemaField field, JsonNode value, IReadOnlySet<Guid> unseen)
    {
        if (field.Relation is not null && IsUnseen(value, unseen))
        {
            return null;
        }

        if (field.FieldType == FieldTypeNames.RichText)
        {
            var text = value.DeepClone();
            _nested.DropLinks(text, (_, id) => !unseen.Contains(id));
            HiddenFields.RemoveWhere(schema, text, field, static inner => inner.Hidden || inner.ReadOnly);
            return text;
        }

        return value switch
        {
            JsonObject single when OwnerOf(field, single) is { } owner => Comparable(owner, single, unseen),
            JsonArray items => new JsonArray([.. items.Where(item => item is not null && (field.Relation is null || !IsUnseen(item, unseen))).Select(item => ComparableValue(field, item!, unseen))]),
            _ => value.DeepClone(),
        };
    }

    private static bool IsUnseen(JsonNode? value, IReadOnlySet<Guid> unseen) =>
        value is not JsonArray && TargetId(value) is { } id && unseen.Contains(id);

    private static Guid? TargetId(JsonNode? value) =>
        EntryCanonicalizer.TryReadId(RawValue.Unwrap(value), out var id) ? id : null;

    private SchemaType? OwnerOf(SchemaField field, JsonObject item) => field.FieldType switch
    {
        FieldTypeNames.Component => ComponentOf(field),
        FieldTypeNames.Blocks when item[Discriminator] is JsonValue name && name.TryGetValue<string>(out var block) => schema.FindBlock(block),
        _ => null,
    };

    private void CarryIntoObject(SchemaType owner, JsonObject before, JsonObject after, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        foreach (var field in owner.Fields)
        {
            if (before[field.Name] is not { } value)
            {
                if (!blocks.IsEmpty && after[field.Name] is { } added)
                {
                    CarryAdded(field, added, blocks, unseen);
                }

                continue;
            }

            if (field.Hidden || field.ReadOnly)
            {
                if (!after.ContainsKey(field.Name))
                {
                    after[field.Name] = value.DeepClone();
                }
            }
            else if (field.Relation is not null)
            {
                CarryTargets(field, value, after, unseen);
            }
            else if (field.FieldType == FieldTypeNames.RichText)
            {
                if (after[field.Name] is not { } text)
                {
                    continue;
                }

                if (CameBack(field, value, text, unseen))
                {
                    after[field.Name] = value.DeepClone();
                }
                else
                {
                    CarryInlineBlocks(value, text, blocks, unseen);
                }
            }
            else if (after[field.Name] is { } inner)
            {
                CarryInside(field, value, inner, blocks, unseen);
            }
            else if (field.FieldType == FieldTypeNames.Component && !field.Multiple && value is JsonObject nested && ComponentOf(field) is { } component)
            {
                JsonObject kept = [];
                CarryIntoObject(component, nested, kept, blocks, unseen);
                if (kept.Count > 0)
                {
                    after[field.Name] = kept;
                }
            }
        }
    }

    // A value with nothing stored at its place may still hold a block that moved there.
    private void CarryAdded(SchemaField field, JsonNode added, BlockScope blocks, IReadOnlySet<Guid> unseen)
    {
        if (field.FieldType == FieldTypeNames.RichText)
        {
            CarryInlineBlocks(new JsonObject(), added, blocks, unseen);
        }
        else if (field.FieldType is FieldTypeNames.Blocks or FieldTypeNames.Component)
        {
            CarryInside(field, null, added, blocks, unseen);
        }
    }

    // Each goes back where it was in a list, which leaves a list sent as it was read unchanged.
    private static void CarryTargets(SchemaField field, JsonNode before, JsonObject after, IReadOnlySet<Guid> unseen)
    {
        if (before is not JsonArray oldTargets)
        {
            if (after[field.Name] is null && IsUnseen(before, unseen))
            {
                after[field.Name] = before.DeepClone();
            }

            return;
        }

        var targets = after[field.Name] as JsonArray ?? [];
        HashSet<Guid> present = [.. targets.Select(TargetId).OfType<Guid>()];
        for (var index = 0; index < oldTargets.Count; index++)
        {
            if (IsUnseen(oldTargets[index], unseen) && TargetId(oldTargets[index]) is { } id && present.Add(id))
            {
                targets.Insert(Math.Min(index, targets.Count), oldTargets[index]?.DeepClone());
            }
        }

        if (targets.Count > 0)
        {
            after[field.Name] = targets;
        }
    }
}
