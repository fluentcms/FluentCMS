using FluentCMS.Content;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.GraphQL;

// What the principal may read of the content types one root field reaches, as the content service's seam answered it. The
// resolvers below that field ask it, so that a field or a type the principal may not read is null with an error, once.
internal sealed class ReadAccess
{
    public const string Key = "FluentCMS.GraphQL.ReadAccess";

    private readonly Dictionary<string, IReadOnlySet<string>?> _readable = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public async ValueTask<IReadOnlySet<string>?> ReadableAsync(IContentSurface surface, TypeModel type, string? locale, CancellationToken cancellationToken)
    {
        if (!_readable.TryGetValue(type.SchemaName, out var readable))
        {
            readable = await surface.ReadableFieldsAsync(type.SchemaName, locale, cancellationToken);
            _readable[type.SchemaName] = readable;
        }

        return readable;
    }

    public bool DeniesType(TypeModel type) => _readable.TryGetValue(type.SchemaName, out var readable) && readable is null;

    public bool DeniesField(TypeModel type, string field) =>
        type.Role == TypeRole.Content && _readable.TryGetValue(type.SchemaName, out var readable) && readable is not null && !readable.Contains(field);

    // Why the read after a write failed, when it did: the entry the write returned is answered without its population.
    public Exception? ReadFailure { get; set; }

    // Whether this is the first report of the field, so that a list of a hundred entries carries one error and not a hundred.
    public bool FirstReport(string key)
    {
        lock (_reported)
        {
            return _reported.Add(key);
        }
    }
}

// The selection of a GraphQL field as the field selection and population of a content read: every selected field the
// principal may read is selected, a relation whose fields are selected is populated with those fields, level by level,
// and a media field, a block list or a repeated component is populated for the references it holds. The content service
// batches each level. A path deeper than the REST depth cap is refused here, before anything runs: the content service
// would refuse it too, but only when it reads, which for a write is after the write.
internal sealed class ReadPlan
{
    private const string Id = "id";

    public List<string> Select { get; } = [];

    public List<string> Populate { get; } = [];

    public ReadAccess Access { get; } = new();

    // The principal may not read the type itself.
    public bool TypeDenied { get; private set; }

    // selections are the fields of the request whose type is the content type; an alias gives a field more than once.
    public static async ValueTask<ReadPlan> BuildAsync(IResolverContext context, TypeModel type, IEnumerable<Selection> selections, string? locale)
    {
        var plan = new ReadPlan();
        var surface = context.RequestServices.GetRequiredService<IContentSurface>();
        if (await plan.Access.ReadableAsync(surface, type, locale, context.RequestAborted) is null)
        {
            plan.TypeDenied = true;
            return plan;
        }

        foreach (var selection in selections.ToList())
        {
            await plan.AddAsync(context, surface, type, selection, "", 1, locale);
        }

        Distinct(plan.Select);
        Distinct(plan.Populate);
        return plan;
    }

    private static void Distinct(List<string> paths)
    {
        var distinct = paths.Distinct(StringComparer.Ordinal).ToList();
        paths.Clear();
        paths.AddRange(distinct);
    }

    // Whether the read populates the field for its selection: a relation selected for more than its id, a media field, a
    // block list and a repeated component.
    public static bool Populates(IResolverContext context, Selection selection, FieldModel field) => field is { Kind: FieldKind.Relation, ReferenceOnly: false }
        ? !IdOnly(context, selection, field.Target!)
        : field.Kind == FieldKind.Reference || field.HoldsReferences;

    private static bool IdOnly(IResolverContext context, Selection selection, TypeModel target) =>
        Selections(context, selection, target.Name).All(inner => inner.Field.Name == Id || inner.Field.Name.StartsWith("__", StringComparison.Ordinal));

    public static Selection[] Selections(IResolverContext context, Selection parent, string typeName)
    {
        var selections = context.Operation.GetSelectionSet(parent, context.Schema.Types.GetType<ObjectType>(typeName)).Selections;
        List<Selection> included = new(selections.Length);
        foreach (var selection in selections)
        {
            if (selection.IsIncluded(context.IncludeConditionFlags))
            {
                included.Add(selection);
            }
        }

        return [.. included];
    }

    // level is the depth at which the fields of the entry are populated, as the content service counts a populate path: 1
    // for the entry of the root field, and one more past each relation.
    private async ValueTask<bool> AddAsync(IResolverContext context, IContentSurface surface, TypeModel type, Selection parent, string prefix, int level, string? locale)
    {
        var readable = type.Role == TypeRole.Content ? await Access.ReadableAsync(surface, type, locale, context.RequestAborted) : null;
        var any = false;
        foreach (var selection in Selections(context, parent, type.Name))
        {
            var name = selection.Field.Name;
            var path = prefix + name;
            if (type.Role == TypeRole.Content && SystemFieldModel.Find(name) is not null)
            {
                if (name != Id && readable!.Contains(name))
                {
                    Select.Add(path);
                    any = true;
                }

                continue;
            }

            // A field the principal may not read stays out of the read; its resolver reports it.
            if (type.Find(name) is not { } field || readable?.Contains(name) == false)
            {
                continue;
            }

            switch (field.Kind)
            {
                case FieldKind.Relation when !field.ReferenceOnly:
                    if (await Access.ReadableAsync(surface, field.Target!, locale, context.RequestAborted) is null)
                    {
                        continue;
                    }

                    if (IdOnly(context, selection, field.Target!))
                    {
                        Select.Add(path);
                        break;
                    }

                    AddPopulate(surface, path, level);
                    if (!await AddAsync(context, surface, field.Target!, selection, path + ".", level + 1, locale))
                    {
                        Select.Add(path);
                    }

                    break;
                case FieldKind.Component when !field.Multiple:
                    if (!await AddAsync(context, surface, field.Target!, selection, path + ".", level, locale))
                    {
                        Select.Add(path);
                    }

                    break;
                default:
                    Select.Add(path);
                    if (field.Kind == FieldKind.Reference || field.HoldsReferences)
                    {
                        AddPopulate(surface, path, level);
                    }

                    break;
            }

            any = true;
        }

        return any;
    }

    private void AddPopulate(IContentSurface surface, string path, int level)
    {
        if (level > surface.MaxPopulateDepth)
        {
            throw new ValidationException([new ValidationError("populate", RestErrorCodes.LimitExceeded, $"'{path}' goes deeper than the {surface.MaxPopulateDepth} levels allowed.")]);
        }

        Populate.Add(path);
    }
}
