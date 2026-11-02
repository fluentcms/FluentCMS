using System.Collections;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// Field filtering for what does not go through the query engine, which applies the same permissions itself. The
// content service (#2693) calls this on what hooks return and on what clients write.
internal sealed class FieldAccess(EffectivePermissions permissions, SchemaModel schema)
{
    // Omits, not blanks, what the principal cannot read, here and in populated entries. A target of a type it cannot
    // read goes too. A row rule on a target cannot be checked in memory: the query engine has already applied it.
    public void OmitUnreadable(ContentDocument document, SchemaType type, AccessTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(type);

        foreach (var key in document.Keys.ToList())
        {
            if (SystemField.Find(key) is not null)
            {
                if (!permissions.CanReadField(type, key, target))
                {
                    document.Remove(key);
                }
            }
            else if (type.FindField(key) is { } field)
            {
                if (field.Hidden || !permissions.CanReadField(type, key, target))
                {
                    document.Remove(key);
                }
                else
                {
                    OmitInValue(document, key, field, target);
                }
            }
        }
    }

    // Throws for every field the operation may not write, which is reported the same way whether the field is hidden,
    // outside the grant's list, or not a field at all, so that a client cannot probe. The other system fields are the
    // engine's to set, and #2693 ignores them; the two that identify a person are refused because own-entries trusts them.
    public void RejectUnwritable(ContentDocument changes, SchemaType type, ContentOperation operation, AccessTarget target)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(type);

        var allowed = permissions.Allows(type, operation, target);
        var fields = permissions.FieldsOf(type, operation, target);

        List<ValidationError> errors = [];
        foreach (var (key, value) in changes)
        {
            if (SystemField.NamesAPerson(key))
            {
                Refuse(errors, key, type);
            }
            else if (SystemField.Find(key) is null)
            {
                var field = type.FindField(key);
                if (!allowed || field is null || field.Hidden || fields?.Contains(key) == false)
                {
                    Refuse(errors, key, type);
                }
                else
                {
                    CheckHiddenInside(errors, key, value, field, type);
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private void OmitInValue(ContentDocument document, string key, SchemaField field, AccessTarget target)
    {
        if (field.Relation is { } relation && schema.FindContentType(relation.Target) is { } related)
        {
            var readable = permissions.Allows(related, ContentOperation.Read, target);
            switch (document[key])
            {
                case ContentDocument single when readable:
                    OmitUnreadable(single, related, target);
                    break;
                case ContentDocument:
                    document[key] = null;
                    break;
                case IEnumerable<ContentDocument> many when readable:
                    foreach (var item in many)
                    {
                        OmitUnreadable(item, related, target);
                    }

                    break;
                case IEnumerable<ContentDocument>:
                    document[key] = new List<ContentDocument>();
                    break;
            }
        }
        else if (field.FieldType == FieldTypeNames.Component && document[key] is ContentDocument group
            && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
        {
            foreach (var hidden in component.Fields.Where(inner => inner.Hidden))
            {
                group.Remove(hidden.Name);
            }
        }
    }

    // A component or block arrives whole, so a hidden field inside it is reached only by looking.
    private void CheckHiddenInside(List<ValidationError> errors, string path, object? value, SchemaField field, SchemaType root)
    {
        if (field.FieldType == FieldTypeNames.Component && schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component)
        {
            foreach (var item in Items(value))
            {
                CheckEntries(errors, path, item, component, root);
            }
        }
        else if (field.FieldType == FieldTypeNames.Blocks)
        {
            foreach (var item in Items(value))
            {
                var name = Entries(item).Where(entry => entry.Key == "type").Select(entry => TextOf(entry.Value)).FirstOrDefault();
                if (name is not null && schema.FindBlock(name) is { } block)
                {
                    CheckEntries(errors, path, item, block, root);
                }
            }
        }
    }

    private void CheckEntries(List<ValidationError> errors, string path, object item, SchemaType owner, SchemaType root)
    {
        foreach (var (key, value) in Entries(item))
        {
            var inner = owner.FindField(key);
            if (inner?.Hidden == true)
            {
                Refuse(errors, $"{path}.{key}", root);
            }
            else if (inner is not null)
            {
                CheckHiddenInside(errors, $"{path}.{key}", value, inner, root);
            }
        }
    }

    private static IEnumerable<object> Items(object? value) => value switch
    {
        null or string => [],
        ContentDocument or JsonObject => [value],
        IEnumerable items => items.Cast<object?>().OfType<object>(),
        _ => [],
    };

    private static IEnumerable<KeyValuePair<string, object?>> Entries(object item) => item switch
    {
        ContentDocument document => document,
        JsonObject json => json.Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value)),
        _ => [],
    };

    private static string? TextOf(object? value) => value switch
    {
        string text => text,
        JsonValue json when json.TryGetValue<string>(out var text) => text,
        _ => null,
    };

    private static void Refuse(List<ValidationError> errors, string path, SchemaType type) =>
        errors.Add(new ValidationError(path, AuthorizationErrorCodes.FieldNotWritable, $"'{path}' is not a field of {type.Name} that can be written."));
}
