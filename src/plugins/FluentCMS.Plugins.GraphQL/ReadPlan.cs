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
// batches each level and refuses a path deeper than the REST depth cap.
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
            await plan.AddAsync(context, surface, type, selection, "", locale);
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

    private async ValueTask<bool> AddAsync(IResolverContext context, IContentSurface surface, TypeModel type, Selection parent, string prefix, string? locale)
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

                    if (Selections(context, selection, field.Target!.Name).All(inner => inner.Field.Name == Id || inner.Field.Name.StartsWith("__", StringComparison.Ordinal)))
                    {
                        Select.Add(path);
                        break;
                    }

                    Populate.Add(path);
                    if (!await AddAsync(context, surface, field.Target, selection, path + ".", locale))
                    {
                        Select.Add(path);
                    }

                    break;
                case FieldKind.Component when !field.Multiple:
                    if (!await AddAsync(context, surface, field.Target!, selection, path + ".", locale))
                    {
                        Select.Add(path);
                    }

                    break;
                default:
                    Select.Add(path);
                    if (field.Kind == FieldKind.Reference || field.HoldsReferences)
                    {
                        Populate.Add(path);
                    }

                    break;
            }

            any = true;
        }

        return any;
    }
}
