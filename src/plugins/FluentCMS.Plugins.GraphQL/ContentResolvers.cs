using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Localization;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using HotChocolate;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.GraphQL;

internal sealed record EntryList(IReadOnlyList<JsonObject> Items, long? Total, int? Page, int PageSize, string? NextCursor);

internal sealed record PublishPayload(JsonObject Entry, IReadOnlyList<PublishWarning> Warnings);

internal static class Arguments
{
    public const string Id = "id";
    public const string Input = "input";
    public const string ExpectedVersion = "expectedVersion";
    public const string Filter = "filter";
    public const string Sort = "sort";
    public const string Search = "search";
    public const string Page = "page";
    public const string Offset = "offset";
    public const string PageSize = "pageSize";
    public const string Cursor = "cursor";
    public const string Locale = "locale";
    public const string Status = "status";
    public const string Fallback = "fallback";
    public const string RichText = "richText";

    public static IReadOnlyList<string> Read { get; } = [Id, Locale, Status, Fallback, RichText];
}

// The root fields. Each carries the identity of the request, which the endpoint's operation holds, and the intent of its
// arguments and selection to IContentService, which decides everything: permissions, row rules, field limits, validation,
// versions and events. An exception of the service becomes the error REST answers it with.
internal static class ContentResolvers
{
    public const string Items = "items";
    public const string Total = "total";
    public const string Entry = "entry";

    public static FieldResolverDelegate Guard(Func<IResolverContext, ValueTask<object?>> resolver) => async context =>
    {
        try
        {
            return await resolver(context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Report(context, exception);
            return null;
        }
    };

    public static async ValueTask<object?> ListAsync(IResolverContext context, TypeModel type, string listType, IReadOnlyDictionary<string, string> sortPaths)
    {
        var read = await ReadArgumentsAsync(context);
        var parts = ReadPlan.Selections(context, context.Selection, listType);
        var plan = await ReadPlan.BuildAsync(context, type, parts.Where(part => part.Field.Name == Items), read.Locale);
        if (plan.TypeDenied)
        {
            return TypeDenied(context, type);
        }

        var query = new ContentQuery
        {
            Filter = FilterReader.Read(context.ArgumentLiteral<IValueNode>(Arguments.Filter), type),
            Search = context.ArgumentValue<string?>(Arguments.Search),
            Sort = SortReader.Read(context.ArgumentLiteral<IValueNode>(Arguments.Sort), sortPaths),
            Page = new PageRequest
            {
                Number = context.ArgumentValue<int?>(Arguments.Page),
                Offset = context.ArgumentValue<int?>(Arguments.Offset),
                Size = context.ArgumentValue<int?>(Arguments.PageSize),

                // A total is a second query, so it runs only for a selection that asks for it.
                WithTotal = parts.Any(part => part.Field.Name == Total),
            },
            Cursor = context.ArgumentValue<string?>(Arguments.Cursor),
            Select = plan.Select,
            Populate = plan.Populate,
            Status = read.Status,
            Locale = read.Locale,
            Fallback = read.Fallback,
            RichTextFormat = read.RichText,
        };

        var result = await Content(context).QueryAsync(type.SchemaName, query, context.RequestAborted);
        var surface = Surface(context);
        Share(context, plan.Access);
        return new EntryList([.. result.Items.Select(surface.ToJson)], result.Total, result.Page, result.PageSize, result.NextCursor);
    }

    // By id, or by the value of one unique field, as the by/ route of the Delivery API.
    public static async ValueTask<object?> SingleAsync(IResolverContext context, TypeModel type, IReadOnlyList<string> uniqueFields)
    {
        var read = await ReadArgumentsAsync(context);
        var id = context.ArgumentValue<string?>(Arguments.Id);
        var by = uniqueFields
            .Select(name => (Name: name, Value: context.ArgumentValue<string?>(name)))
            .Where(item => item.Value is not null)
            .ToList();
        if ((id is null ? 0 : 1) + by.Count != 1)
        {
            throw new PluginException(
                GraphQLErrorCodes.InvalidArguments,
                $"Name the {type.SchemaName} by one of {string.Join(", ", [Arguments.Id, .. uniqueFields])}, and by one alone.");
        }

        var plan = await ReadPlan.BuildAsync(context, type, [context.Selection], read.Locale);
        if (plan.TypeDenied)
        {
            return TypeDenied(context, type);
        }

        var entry = id is not null
            ? await Content(context).GetAsync(
                type.SchemaName,
                EntryId(id),
                new GetOptions { Status = read.Status, Locale = read.Locale, Fallback = read.Fallback, Select = plan.Select, Populate = plan.Populate, RichTextFormat = read.RichText },
                context.RequestAborted)
            : await FirstAsync(context, type, read, plan, new FieldFilter(by[0].Name, FilterOperator.Equal, by[0].Value));

        Share(context, plan.Access);
        return entry is null ? null : Surface(context).ToJson(entry);
    }

    public static async ValueTask<object?> SingletonAsync(IResolverContext context, TypeModel type)
    {
        var read = await ReadArgumentsAsync(context);
        var plan = await ReadPlan.BuildAsync(context, type, [context.Selection], read.Locale);
        if (plan.TypeDenied)
        {
            return TypeDenied(context, type);
        }

        var entry = await FirstAsync(context, type, read, plan, filter: null);
        Share(context, plan.Access);
        return entry is null ? null : Surface(context).ToJson(entry);
    }

    public static async ValueTask<object?> CreateAsync(IResolverContext context, TypeModel type)
    {
        var locale = await LocaleAsync(context);
        var plan = await ReadPlan.BuildAsync(context, type, [context.Selection], locale);
        var input = InputReader.Read(context.ArgumentLiteral<IValueNode>(Arguments.Input), type);
        ContentDocument created;
        using (InLocale(context, locale))
        {
            created = await Content(context).CreateAsync(type.SchemaName, input, context.RequestAborted);
        }

        return await WrittenAsync(context, type, created, plan, locale);
    }

    // A merge patch, as PATCH on the Management API: a field the input leaves out keeps its value, and null clears it.
    public static async ValueTask<object?> UpdateAsync(IResolverContext context, TypeModel type)
    {
        var locale = await LocaleAsync(context);
        var plan = await ReadPlan.BuildAsync(context, type, [context.Selection], locale);
        var id = EntryId(context.ArgumentValue<string>(Arguments.Id));
        var patch = InputReader.Read(context.ArgumentLiteral<IValueNode>(Arguments.Input), type);
        ContentDocument updated;
        using (InLocale(context, locale))
        {
            updated = await Content(context).PatchAsync(type.SchemaName, id, patch, context.ArgumentValue<int>(Arguments.ExpectedVersion), context.RequestAborted);
        }

        return await WrittenAsync(context, type, updated, plan, locale);
    }

    public static async ValueTask<object?> DeleteAsync(IResolverContext context, TypeModel type)
    {
        var id = EntryId(context.ArgumentValue<string>(Arguments.Id));
        await Content(context).DeleteAsync(type.SchemaName, id, context.RequestAborted);
        return id.ToString("D");
    }

    public static async ValueTask<object?> PublishAsync(IResolverContext context, TypeModel type, string payloadType)
    {
        var locale = await LocaleAsync(context);
        var plan = await ReadPlan.BuildAsync(context, type, ReadPlan.Selections(context, context.Selection, payloadType).Where(part => part.Field.Name == Entry), locale);
        var id = EntryId(context.ArgumentValue<string>(Arguments.Id));
        var result = await Content(context).PublishAsync(
            type.SchemaName,
            id,
            new LocaleWriteOptions { Locale = locale, ExpectedVersion = context.ArgumentValue<int?>(Arguments.ExpectedVersion) },
            context.RequestAborted);

        return new PublishPayload(await WrittenAsync(context, type, result.Entry, plan, locale), result.Warnings);
    }

    public static async ValueTask<object?> UnpublishAsync(IResolverContext context, TypeModel type)
    {
        var locale = await LocaleAsync(context);
        var plan = await ReadPlan.BuildAsync(context, type, [context.Selection], locale);
        var id = EntryId(context.ArgumentValue<string>(Arguments.Id));
        var entry = await Content(context).UnpublishAsync(type.SchemaName, id, new LocaleOptions { Locale = locale }, context.RequestAborted);
        return await WrittenAsync(context, type, entry, plan, locale);
    }

    public static object? SystemField(IResolverContext context, TypeModel owner, SystemFieldModel field)
    {
        var entry = context.Parent<JsonObject>();
        if (Denied(context, owner, field.Name))
        {
            return null;
        }

        return field.Name == SystemFieldModel.Status ? EntryValues.Status(entry[field.Name]) : EntryValues.Scalar(entry[field.Name], field.Scalar);
    }

    public static object? Field(IResolverContext context, TypeModel owner, FieldModel field)
    {
        var parent = context.Parent<JsonObject>();
        if (Denied(context, owner, field.Name))
        {
            return null;
        }

        if (field is { Kind: FieldKind.Relation, ReferenceOnly: false } && Access(context)?.DeniesType(field.Target!) == true)
        {
            ReportOnce(context, $"type:{field.Target!.SchemaName}", RestErrorCodes.NotFound, $"The content type '{field.Target.SchemaName}' cannot be read.");
            return null;
        }

        if (Access(context)?.ReadFailure is { } failure && ReadPlan.Populates(context, context.Selection, field))
        {
            Report(context, failure);
            return null;
        }

        if (parent[field.Name] is not { } value)
        {
            return null;
        }

        return field.Kind switch
        {
            FieldKind.Scalar => field.Multiple ? Many(value, item => EntryValues.Scalar(item, field.Scalar)) : EntryValues.Scalar(value, field.Scalar),
            FieldKind.Enum => field.Multiple ? Many(value, item => Option(item, field)) : Option(value, field),
            FieldKind.Relation or FieldKind.Component => field.Multiple ? Many(value, item => Reference(item, field)) : Reference(value, field),
            FieldKind.Blocks => Many(value, item => item is JsonObject block && EntryValues.BlockType(block) is { } name && field.BlockNames.Contains(name) ? block : null),
            _ => EntryValues.Json(value),
        };
    }

    public static ReadAccess? Access(IResolverContext context) =>
        context.ScopedContextData.TryGetValue(ReadAccess.Key, out var access) ? access as ReadAccess : null;

    private static bool Denied(IResolverContext context, TypeModel owner, string field)
    {
        if (Access(context)?.DeniesField(owner, field) != true)
        {
            return false;
        }

        // The words the content service has for a field a query names and the principal may not read.
        ReportOnce(context, $"field:{owner.SchemaName}.{field}", RestErrorCodes.UnknownField, $"'{field}' is not a field of {owner.SchemaName} that can be read.");
        return true;
    }

    private static List<object> Many(JsonNode value, Func<JsonNode?, object?> read) =>
        value is JsonArray items ? [.. items.Select(read).OfType<object>()] : [];

    // A value that is no option of the select, which an older schema may have stored, is not a value of its enum.
    private static string? Option(JsonNode? value, FieldModel field) =>
        value is JsonValue text && text.TryGetValue<string>(out var option) && field.OptionsByName.Values.Contains(option) ? option : null;

    // Inside a block or a repeated component a relation is stored as the id of its target.
    private static JsonObject? Reference(JsonNode? value, FieldModel field) => value switch
    {
        JsonObject entry => entry,
        JsonValue id when field.Kind == FieldKind.Relation && id.TryGetValue<string>(out var text) => new JsonObject { ["id"] = text, ["type"] = field.Target!.SchemaName },
        _ => null,
    };

    private static async ValueTask<ContentDocument?> FirstAsync(IResolverContext context, TypeModel type, ReadArguments read, ReadPlan plan, ContentFilter? filter)
    {
        var query = new ContentQuery
        {
            Filter = filter,
            Page = new PageRequest { Size = 1, WithTotal = false },
            Select = plan.Select,
            Populate = plan.Populate,
            Status = read.Status,
            Locale = read.Locale,
            Fallback = read.Fallback,
            RichTextFormat = read.RichText,
        };
        return (await Content(context).QueryAsync(type.SchemaName, query, context.RequestAborted)).Items is [var first, ..] ? first : null;
    }

    // What a write returns is what the principal's read shows. A selection that goes into a relation or a media field is
    // read again, as a draft, with that population; any other is answered from the entry the content service returned.
    // The plan was built before the write, so a selection the read would refuse was refused before anything was written.
    // A read that still fails cannot undo the write: the entry the write returned is answered, and each field the read was
    // to populate is null with the read's error.
    private static async ValueTask<JsonObject> WrittenAsync(IResolverContext context, TypeModel type, ContentDocument written, ReadPlan plan, string? locale)
    {
        if (plan is { TypeDenied: false, Populate.Count: > 0 })
        {
            try
            {
                written = await Content(context).GetAsync(
                    type.SchemaName,
                    written.Get<Guid>(Arguments.Id),
                    new GetOptions { Status = QueryStatus.Draft, Locale = locale, Select = plan.Select, Populate = plan.Populate },
                    context.RequestAborted) ?? written;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                plan.Access.ReadFailure = exception;
            }
        }

        // A principal that may write the type and not read it is shown what the write returned, and nothing is reported.
        Share(context, plan.TypeDenied ? new ReadAccess() : plan.Access);
        return Surface(context).ToJson(written);
    }

    private static object? TypeDenied(IResolverContext context, TypeModel type)
    {
        // The code of the 404 that REST answers for a type the principal may not read.
        context.ReportError(Error(context, RestErrorCodes.NotFound, $"The content type '{type.SchemaName}' cannot be read.").Build());
        return null;
    }

    private static void ReportOnce(IResolverContext context, string key, string code, string message)
    {
        if (Access(context)?.FirstReport(key) == true)
        {
            context.ReportError(Error(context, code, message).Build());
        }
    }

    private static void Report(IResolverContext context, Exception exception)
    {
        var problem = context.RequestServices.GetRequiredService<IProblemDescriber>().Describe(exception);
        if (problem.Status >= 500)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<GraphQLPlugin>();
            GraphQLLog.FieldFailed(logger, exception, context.Selection.Field.Name, problem.Code);
        }

        var error = Error(context, problem.Code, problem.Detail).SetExtension("status", problem.Status);
        if (problem.Errors.Count > 0)
        {
            error.SetExtension("errors", problem.Errors
                .Select(item => new Dictionary<string, object?> { ["path"] = item.Path, ["code"] = item.Code, ["message"] = item.Message })
                .ToList());
        }

        // The entry a stale write would have changed, as the 412 of REST carries it.
        if (exception is VersionConflictException { Current: { } current })
        {
            error.SetExtension("current", Plain(Surface(context).ToJson(current)));
        }

        context.ReportError(error.Build());
    }

    private static ErrorBuilder Error(IResolverContext context, string code, string message) =>
        ErrorBuilder.New().SetMessage(message).SetCode(code).SetPath(context.Path);

    private static object? Plain(JsonNode? node) => node switch
    {
        null => null,
        JsonObject fields => fields.ToDictionary(pair => pair.Key, pair => Plain(pair.Value)),
        JsonArray items => items.Select(Plain).ToList(),
        _ => node.GetValueKind() switch
        {
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => EntryValues.Scalar(node, ScalarNames.Decimal),
        },
    };

    private sealed record ReadArguments(string? Locale, QueryStatus Status, LocaleFallback Fallback, string? RichText);

    private static async ValueTask<ReadArguments> ReadArgumentsAsync(IResolverContext context) => new(
        await LocaleAsync(context),
        context.ArgumentValue<QueryStatus?>(Arguments.Status) ?? QueryStatus.Published,
        context.ArgumentValue<LocaleFallback?>(Arguments.Fallback) ?? LocaleFallback.Chain,
        context.ArgumentValue<RichTextFormat?>(Arguments.RichText) switch
        {
            RichTextFormat.Html => RichTextFormats.Html,
            RichTextFormat.Markdown => RichTextFormats.Markdown,
            _ => null,
        });

    // The locale argument, checked against the locales of the site as the locale parameter of REST is, or null for the
    // locale of the request.
    private static async ValueTask<string?> LocaleAsync(IResolverContext context)
    {
        var asked = context.ArgumentValue<string?>(Arguments.Locale);
        if (string.IsNullOrWhiteSpace(asked))
        {
            return null;
        }

        var resolved = await context.RequestServices.GetRequiredService<ILocaleResolver>()
            .ResolveAsync(new LocaleRequest(Operation(context).Site) { Parameter = asked }, context.RequestAborted);
        return resolved == ILocaleResolver.All
            ? throw new ValidationException([new ValidationError(Arguments.Locale, RestErrorCodes.InvalidParameter, "A GraphQL field reads one locale. Name it, and read another under an alias.")])
            : resolved;
    }

    // A create or a patch is in the locale of its operation, so the locale argument becomes that for the call.
    private static OperationLocale? InLocale(IResolverContext context, string? locale)
    {
        if (locale is null)
        {
            return null;
        }

        var accessor = context.RequestServices.GetRequiredService<IOperationContextAccessor>();
        var previous = Operation(context);
        accessor.Current = previous with { Locale = locale };
        return new OperationLocale(accessor, previous);
    }

    private sealed class OperationLocale(IOperationContextAccessor accessor, OperationContext previous) : IDisposable
    {
        public void Dispose() => accessor.Current = previous;
    }

    private static Guid EntryId(string id) => Guid.TryParseExact(id, "D", out var entryId)
        ? entryId
        : throw new ValidationException([new ValidationError(Arguments.Id, RestErrorCodes.InvalidValue, $"'{id}' is not an entry id.")]);

    private static OperationContext Operation(IResolverContext context) =>
        context.RequestServices.GetRequiredService<IOperationContextAccessor>().Current
        ?? throw new InvalidOperationException("The GraphQL endpoint does not run inside the operation of its request.");

    private static void Share(IResolverContext context, ReadAccess access) =>
        context.ScopedContextData = context.ScopedContextData.SetItem(ReadAccess.Key, access);

    private static IContentService Content(IResolverContext context) => context.RequestServices.GetRequiredService<IContentService>();

    private static IContentSurface Surface(IResolverContext context) => context.RequestServices.GetRequiredService<IContentSurface>();
}
