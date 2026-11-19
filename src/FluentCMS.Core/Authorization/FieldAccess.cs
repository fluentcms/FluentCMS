using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// Field filtering for what does not go through the query engine, which applies the same permissions itself. The
// content service (#2693) calls this on what hooks return and on what clients write.
internal sealed class FieldAccess(EffectivePermissions permissions, SchemaModel schema)
{
    private const string Discriminator = "type";

    private FieldAccess? _targets;

    // A preview token reads the targets of its entry with permissions of their own (spec 4.7), at every level below it.
    private FieldAccess Targets => _targets ??= permissions.PublishedTargets is { } published ? new FieldAccess(published, schema) : this;

    // Omits, not blanks, what the principal cannot read, here and in populated entries, and any key that is not a field,
    // so a hook's extra key does not get past an allow-list. Whatever the service adds to a response, such as the
    // fallbacks of spec 4.4, comes after this. A target of a type the principal cannot read goes too. A row rule on a
    // target cannot be checked in memory: the query engine has already applied it. JSON that cannot be read is dropped.
    public void OmitUnreadable(ContentDocument document, SchemaType type, AccessTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(type);

        OmitInEntry(document, type, target, isTarget: false);
    }

    // Throws for every field the write may not touch, reported the same way whether it is hidden, outside what the
    // grants allow, or not a field at all, here or inside a component or block, so that a client cannot probe. Writable
    // comes from the evaluator, which knows the entry. The other system fields are the engine's to set, and #2693
    // ignores them; the two that identify a person are refused because own-entries trusts them. The system principal
    // is how a hook or job sets a hidden field, so it is not limited here. A value whose inside cannot be inspected,
    // such as a BlockList of block classes, is refused too: the typed mapper of #2693 must hand over JSON.
    public void RejectUnwritable(ContentDocument changes, SchemaType type, WritableFields writable)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(writable);

        if (permissions.IsSystem)
        {
            return;
        }

        List<ValidationError> errors = [];
        foreach (var (key, value) in changes)
        {
            if (SystemField.NamesAPerson(key))
            {
                Refuse(errors, key, type);
            }
            else if (SystemField.Find(key) is null)
            {
                if (type.FindField(key) is not { Hidden: false, ReadOnly: false } field || !writable.Includes(key))
                {
                    Refuse(errors, key, type);
                }
                else
                {
                    CheckInside(errors, key, value, field, type);
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    // Whether a value of a field names nothing inside it that no client may write, whoever is asking.
    public bool IsClean(SchemaField field, object? value, SchemaType type)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(type);

        List<ValidationError> errors = [];
        CheckInside(errors, field.Name, value, field, type);
        return errors.Count == 0;
    }

    // What a before hook changed is held to the same grants as the client it runs for, so that a hook cannot be the way
    // to a field the principal may not write. A hidden or read-only field is outside this: no client reaches it, and a
    // hook is what fills it. Fields that are not there at all never get this far: the canonicalizer refuses them.
    public void RejectHookChanges(IEnumerable<string> changed, SchemaType type, WritableFields writable)
    {
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(writable);

        if (permissions.IsSystem)
        {
            return;
        }

        List<ValidationError> errors = [];
        foreach (var name in changed)
        {
            if (type.FindField(name) is { Hidden: false, ReadOnly: false } && !writable.Includes(name))
            {
                Refuse(errors, name, type);
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private void OmitInEntry(ContentDocument document, SchemaType type, AccessTarget target, bool isTarget)
    {
        foreach (var key in document.Keys.ToList())
        {
            if (SystemField.Find(key) is not null)
            {
                if (!permissions.CanReadField(type, key, target))
                {
                    document.Remove(key);
                }
            }
            else if (!isTarget || key != Discriminator)
            {
                if (type.FindField(key) is { Hidden: false } field && permissions.CanReadField(type, key, target))
                {
                    OmitInValue(document, key, field, target);
                }
                else
                {
                    document.Remove(key);
                }
            }
        }
    }

    // A flattened component: its fields have no permissions of their own, since an allow-list names top-level fields.
    private void OmitInComponent(ContentDocument group, SchemaType component, AccessTarget target)
    {
        foreach (var key in group.Keys.ToList())
        {
            if (component.FindField(key) is { Hidden: false } field)
            {
                OmitInValue(group, key, field, target);
            }
            else
            {
                group.Remove(key);
            }
        }
    }

    private void OmitInValue(ContentDocument document, string key, SchemaField field, AccessTarget target)
    {
        if (field.Relation is { } relation && schema.FindContentType(relation.Target) is { } related)
        {
            OmitInRelation(document, key, related, target);
        }
        else if (field.FieldType == FieldTypeNames.RichText && document[key] is { } text)
        {
            if (text is RichText rich)
            {
                document[key] = HiddenFields.Remove(schema, rich, dropUnknown: true);
            }
            else if (ReadDocument(text) is { } node)
            {
                HiddenFields.Remove(schema, node, field, dropUnknown: true);
                document[key] = node;
            }
            else
            {
                document[key] = null;
            }
        }
        else if (field.FieldType is FieldTypeNames.Component or FieldTypeNames.Blocks && document[key] is not null)
        {
            var component = field.FieldType == FieldTypeNames.Component ? schema.FindComponent((string)field.Settings[FieldSettings.Component]) : null;
            switch (document[key])
            {
                case ContentDocument group when component is not null:
                    OmitInComponent(group, component, target);
                    break;
                case IEnumerable<ContentDocument> groups when component is not null:
                    foreach (var group in groups)
                    {
                        OmitInComponent(group, component, target);
                    }

                    break;
                default:
                    if (RawValue.TryReadJson(document[key], out var node, out _))
                    {
                        HiddenFields.Remove(schema, node, field, dropUnknown: true);
                        document[key] = node;
                    }
                    else
                    {
                        document[key] = null;
                    }

                    break;
            }
        }
    }

    private void OmitInRelation(ContentDocument document, string key, SchemaType related, AccessTarget target)
    {
        var targets = Targets;
        var readable = targets.CanRead(related, target);
        switch (document[key])
        {
            case ContentDocument single when readable:
                targets.OmitInEntry(single, related, target, isTarget: true);
                break;
            case ContentDocument:
                document[key] = null;
                break;
            case IEnumerable<ContentDocument> many when readable:
                foreach (var item in many)
                {
                    targets.OmitInEntry(item, related, target, isTarget: true);
                }

                break;
            case IEnumerable<ContentDocument>:
                document[key] = new List<ContentDocument>();
                break;
        }
    }

    private bool CanRead(SchemaType type, AccessTarget target) => permissions.Allows(type, ContentOperation.Read, target);

    // A component or block arrives whole, in any of the forms its field type coerces, so its fields are reached only
    // by reading it the way the coercion will.
    private void CheckInside(List<ValidationError> errors, string path, object? value, SchemaField field, SchemaType root)
    {
        if (field.FieldType == FieldTypeNames.RichText)
        {
            CheckInlineBlocks(errors, path, value, root);
            return;
        }

        if (field.FieldType is not (FieldTypeNames.Component or FieldTypeNames.Blocks))
        {
            return;
        }

        List<IReadOnlyList<KeyValuePair<string, object?>>> objects = [];
        if (!TryReadObjects(value, objects))
        {
            Refuse(errors, path, root);
            return;
        }

        foreach (var entries in objects)
        {
            if (field.FieldType == FieldTypeNames.Component)
            {
                if (schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
                {
                    CheckEntries(errors, path, entries, component, root, isBlock: false);
                }
                else
                {
                    Refuse(errors, path, root);
                }
            }
            else if (TextOf(entries.FirstOrDefault(entry => entry.Key == Discriminator).Value) is { } name && schema.FindBlock(name) is { } block)
            {
                CheckEntries(errors, path, entries, block, root, isBlock: true);
            }
            else
            {
                Refuse(errors, $"{path}.{Discriminator}", root);
            }
        }
    }

    private void CheckEntries(List<ValidationError> errors, string path, IEnumerable<KeyValuePair<string, object?>> entries, SchemaType owner, SchemaType root, bool isBlock)
    {
        foreach (var (key, value) in entries)
        {
            if (isBlock && key is Discriminator or BlockIds.Key)
            {
                continue;
            }

            if (owner.FindField(key) is { Hidden: false, ReadOnly: false } inner)
            {
                CheckInside(errors, $"{path}.{key}", value, inner, root);
            }
            else
            {
                Refuse(errors, $"{path}.{key}", root);
            }
        }
    }

    // The data of an inline block is held to the same rules as the block in a block list. A document that cannot be read is
    // refused as rich text by the canonicalizer, and an inline block of a type the schema does not know is left alone.
    private void CheckInlineBlocks(List<ValidationError> errors, string path, object? value, SchemaType root)
    {
        foreach (var block in InlineBlockJson.Find(ReadDocument(value)))
        {
            if (schema.FindBlock(block.BlockType) is { } known && block.Data is { } data)
            {
                CheckEntries(errors, path, [.. data.Select(pair => KeyValuePair.Create<string, object?>(pair.Key, pair.Value))], known, root, isBlock: false);
            }
        }
    }

    // Read the way the rich text field type coerces it, as deep as it accepts.
    internal static JsonNode? ReadDocument(object? value)
    {
        try
        {
            var json = RawValue.Unwrap(value) switch
            {
                RichText document => document.Json,
                string text => text,
                JsonElement { ValueKind: JsonValueKind.Object } element => element.GetRawText(),
                _ => null,
            };

            return json is null ? null : InlineBlockJson.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The objects a value holds. False for what cannot be inspected, including a scalar where an object belongs.
    private static bool TryReadObjects(object? value, List<IReadOnlyList<KeyValuePair<string, object?>>> objects)
    {
        switch (value)
        {
            case null:
                return true;
            case JsonElement or JsonNode or string:
                if (!RawValue.TryReadJson(value, out var node, out _))
                {
                    return false;
                }

                switch (node)
                {
                    case null:
                        return true;
                    case JsonObject json:
                        objects.Add([.. json.Select(pair => KeyValuePair.Create<string, object?>(pair.Key, pair.Value))]);
                        return true;
                    case JsonArray array:
                        return array.All(item => TryReadObjects(item, objects));
                    default:
                        return false;
                }

            case IEnumerable<KeyValuePair<string, object?>> document:
                objects.Add([.. document]);
                return true;
            case IDictionary dictionary:
                List<KeyValuePair<string, object?>> entries = [];
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key)
                    {
                        return false;
                    }

                    entries.Add(KeyValuePair.Create(key, entry.Value));
                }

                objects.Add(entries);
                return true;
            case IEnumerable items:
                return items.Cast<object?>().All(item => TryReadObjects(item, objects));
            default:
                return false;
        }
    }

    private static string? TextOf(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonValue json when json.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    private static void Refuse(List<ValidationError> errors, string path, SchemaType type) =>
        errors.Add(new ValidationError(path, AuthorizationErrorCodes.FieldNotWritable, $"'{path}' is not a field of {type.Name} that can be written."));
}
