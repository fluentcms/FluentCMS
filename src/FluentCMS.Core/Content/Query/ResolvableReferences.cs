using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// The populated references of a read whose target is no content type, such as media, gathered from every level and from
// the blocks and repeated components a populate path names, then resolved once per target. A target the resolver leaves
// out is null, or leaves its list. A target with no resolver stays { "id", "type" }.
internal sealed class ResolvableReferences(SchemaModel schema, ReferenceResolvers resolvers)
{
    private const string Discriminator = "type";

    private readonly Dictionary<string, HashSet<Guid>> _ids = new(StringComparer.Ordinal);
    private readonly List<Action<Func<string, Guid, ContentDocument?>>> _replacements = [];

    public void AddField(ContentDocument holder, string name, SchemaField field)
    {
        var target = EntryPaths.ReferenceTarget(field);
        if (resolvers.Find(target) is null)
        {
            return;
        }

        switch (holder[name])
        {
            case List<ContentDocument> stubs:
                Want(target, stubs.Select(IdOf));
                _replacements.Add(resolved => holder[name] = stubs.Select(stub => resolved(target, IdOf(stub))).OfType<ContentDocument>().ToList());
                break;
            case ContentDocument stub:
                Want(target, [IdOf(stub)]);
                _replacements.Add(resolved => holder[name] = resolved(target, IdOf(stub)));
                break;
        }
    }

    // The blocks or repeated components of a field, at every depth inside it.
    public void AddInside(SchemaField field, JsonNode? node)
    {
        foreach (var item in node is JsonArray items ? items.OfType<JsonObject>() : node is JsonObject single ? [single] : [])
        {
            var owner = field.FieldType switch
            {
                FieldTypeNames.Component when field.Settings.TryGetValue(FieldSettings.Component, out var name) && name is string component => schema.FindComponent(component),
                FieldTypeNames.Blocks when item[Discriminator] is JsonValue name && name.TryGetValue<string>(out var block) => schema.FindBlock(block),
                _ => null,
            };

            foreach (var inner in owner?.Fields ?? [])
            {
                if (item[inner.Name] is not { } value)
                {
                    continue;
                }

                if (schema.FieldTypes.Find(inner.FieldType)!.StorageKind(inner) == FieldStorageKind.Reference)
                {
                    AddJson(item, inner, value);
                }
                else if (inner.FieldType is FieldTypeNames.Blocks or FieldTypeNames.Component)
                {
                    AddInside(inner, value);
                }
            }
        }
    }

    public async Task ResolveAsync(string locale, CancellationToken cancellationToken)
    {
        if (_replacements.Count == 0)
        {
            return;
        }

        Dictionary<string, IReadOnlyDictionary<Guid, ResolvedReference>> resolved = new(StringComparer.Ordinal);
        foreach (var (target, ids) in _ids)
        {
            resolved[target] = await resolvers.Find(target)!.ResolveAsync(ids, locale, cancellationToken);
        }

        // Each place gets a copy of its own, so that what a later step changes in one does not show in another.
        ContentDocument? Lookup(string target, Guid id) =>
            resolved[target].TryGetValue(id, out var found) ? ContentCopy.Of(found.Value) : null;

        foreach (var replace in _replacements)
        {
            replace(Lookup);
        }
    }

    private void AddJson(JsonObject holder, SchemaField field, JsonNode value)
    {
        var target = EntryPaths.ReferenceTarget(field);
        if (resolvers.Find(target) is null)
        {
            return;
        }

        static JsonNode? Node(ContentDocument? document) => document is null ? null : EntryJson.ToNode(document);

        if (value is JsonArray stubs)
        {
            var ids = stubs.Select(IdOf).OfType<Guid>().ToList();
            Want(target, ids);
            _replacements.Add(resolved => holder[field.Name] = new JsonArray([.. ids.Select(id => Node(resolved(target, id))).OfType<JsonNode>()]));
        }
        else if (IdOf(value) is { } id)
        {
            Want(target, [id]);
            _replacements.Add(resolved => holder[field.Name] = Node(resolved(target, id)));
        }
    }

    private void Want(string target, IEnumerable<Guid> ids)
    {
        if (!_ids.TryGetValue(target, out var wanted))
        {
            wanted = [];
            _ids[target] = wanted;
        }

        wanted.UnionWith(ids);
    }

    private static Guid IdOf(ContentDocument stub) => (Guid)stub["id"]!;

    private static Guid? IdOf(JsonNode? stub) =>
        EntryCanonicalizer.TryReadId(RawValue.Unwrap(stub), out var id) ? id : null;
}
