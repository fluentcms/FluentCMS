using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// The schema as the principal of the operation may see it (spec 4.2, 4.8): schema.read in the site of the operation, then
// what the evaluator lets it read there in the operation's locale, which is what TypeAccess asks of a content route. A
// field outside the allow-lists is left out, and so is a relation to a type it may not read, with the reverse sides of
// what went, so that no name of such a type shows. A component or block that only what went used goes too; one that
// nothing uses stays. Outside an operation the caller is the host's own code, which sees the whole schema.
internal sealed class SchemaIntrospection(
    SchemaModel schema,
    IAuthorizationEvaluator evaluator,
    ManagementAuthority authority,
    IOperationContextAccessor accessor) : ISchemaIntrospection
{
    public async Task<SchemaDescription> DescribeAsync(CancellationToken cancellationToken = default) =>
        SchemaDescriber.Describe(await VisibleAsync(cancellationToken));

    public async Task<SchemaTypeDescription?> DescribeTypeAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        return (await VisibleAsync(cancellationToken)).FindContentType(name) is { } type ? SchemaDescriber.Describe(type) : null;
    }

    // The canonical snapshot of what the principal sees: the instance's own, with its hash, when nothing is left out.
    public async Task<SchemaSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var visible = await VisibleAsync(cancellationToken);
        return ReferenceEquals(visible, schema) ? SchemaSnapshot.Of(schema) : SchemaSnapshot.Create(SchemaJsonWriter.Write(visible));
    }

    private async Task<SchemaModel> VisibleAsync(CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } operation)
        {
            return schema;
        }

        await authority.RequireAsync(CorePermissions.SchemaRead, operation.Site.Value, cancellationToken);
        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        return permissions.IsSystem ? schema : Narrow(schema, permissions, new AccessTarget(operation.Site, operation.Locale));
    }

    private static SchemaModel Narrow(SchemaModel schema, EffectivePermissions permissions, AccessTarget target)
    {
        var readable = schema.ContentTypes
            .Where(type => permissions.Allows(type, ContentOperation.Read, target))
            .ToDictionary(type => type.Name, StringComparer.Ordinal);

        Dictionary<string, HashSet<string>> fieldsOf = new(StringComparer.Ordinal);
        foreach (var type in readable.Values)
        {
            var allowed = permissions.ReadableFieldsOf(type, target);
            fieldsOf[type.Name] = type.Fields
                .Where(field => (allowed is null || allowed.Contains(field.Name)) && Reaches(field, readable))
                .Select(field => field.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        List<SchemaType> contentTypes = [.. readable.Values.Select(type => Narrowed(
            type,
            type.Fields.Where(field => fieldsOf[type.Name].Contains(field.Name)),
            type.ReverseRelations.Where(relation => fieldsOf.TryGetValue(relation.SourceType, out var source) && source.Contains(relation.SourceField))))];

        var used = Used(schema, schema.ContentTypes);
        var stillUsed = Used(schema, contentTypes);
        List<SchemaType> parts = [.. schema.Components.Concat(schema.Blocks)
            .Where(part => !used.Contains(part) || stillUsed.Contains(part))
            .Select(part => Narrowed(part, part.Fields.Where(field => Reaches(field, readable)), part.ReverseRelations))];

        HashSet<SchemaType> originals = [.. schema.ContentTypes, .. schema.Components, .. schema.Blocks];
        List<SchemaType> types = [.. contentTypes, .. parts];
        return types.Count == originals.Count && types.All(originals.Contains) ? schema : new SchemaModel(types, schema.FieldTypes);
    }

    private static bool Reaches(SchemaField field, Dictionary<string, SchemaType> readable) =>
        field.Relation is not { } relation || readable.ContainsKey(relation.Target);

    // The components and blocks that entries of these types can hold: through component and blocks fields at any depth, and
    // every block through a rich text field, whose inline blocks may be of any registered block.
    private static HashSet<SchemaType> Used(SchemaModel schema, IEnumerable<SchemaType> roots)
    {
        HashSet<SchemaType> used = [];
        Queue<SchemaType> pending = new(roots);
        while (pending.TryDequeue(out var type))
        {
            foreach (var field in type.Fields)
            {
                IEnumerable<SchemaType?> parts = field.FieldType switch
                {
                    FieldTypeNames.Component => [SettingValues.Text(field, FieldSettings.Component) is { } name ? schema.FindComponent(name) : null],
                    FieldTypeNames.Blocks => SettingValues.List(field, FieldSettings.Blocks).Select(schema.FindBlock),
                    FieldTypeNames.RichText => schema.Blocks,
                    _ => [],
                };

                foreach (var part in parts.OfType<SchemaType>().Where(used.Add))
                {
                    pending.Enqueue(part);
                }
            }
        }

        return used;
    }

    // The original when nothing of it was left out, so that a model nothing was taken from is the instance's own.
    private static SchemaType Narrowed(SchemaType type, IEnumerable<SchemaField> fields, IEnumerable<ReverseRelation> reverseRelations)
    {
        List<SchemaField> keptFields = [.. fields];
        List<ReverseRelation> keptReverse = [.. reverseRelations];
        if (keptFields.Count == type.Fields.Count && keptReverse.Count == type.ReverseRelations.Count)
        {
            return type;
        }

        // Every property of SchemaType, as SchemaModelFactory sets them.
        return new SchemaType
        {
            Name = type.Name,
            Kind = type.Kind,
            Localized = type.Localized,
            Scope = type.Scope,
            UnpublishedReferences = type.UnpublishedReferences,
            MaxVersions = type.MaxVersions,
            TrashRetentionDays = type.TrashRetentionDays,
            ClrType = type.ClrType,
            Fields = keptFields.AsReadOnly(),
            ReverseRelations = keptReverse.AsReadOnly(),
        };
    }
}
