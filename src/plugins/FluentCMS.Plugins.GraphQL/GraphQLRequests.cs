using System.Collections.Frozen;
using FluentCMS.Authorization;
using FluentCMS.Content;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.PersistedOperations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RequestDelegate = HotChocolate.Execution.RequestDelegate;

namespace FluentCMS.Plugins.GraphQL;

// Runs for every request before it executes, inside the operation the endpoint set: it gives the request the depth limit
// of the options, and lets it introspect the schema where the caller may.
internal sealed class GraphQLRequestInterceptor : DefaultHttpRequestInterceptor
{
    // Core's permission for the schema routes of the Management API.
    private const string SchemaRead = "schema.read";

    public override async ValueTask OnCreateAsync(HttpContext context, IRequestExecutor requestExecutor, OperationRequestBuilder requestBuilder, CancellationToken cancellationToken)
    {
        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<GraphQLOptions>>().Value;
        requestBuilder.SetMaximumAllowedExecutionDepth(options.MaxDepth);
        if (await MayIntrospectAsync(services, options, cancellationToken))
        {
            requestBuilder.AllowIntrospection();
        }

        await base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }

    // Introspection shows every type and field of the one schema, whatever the caller may read of them, so outside
    // Development it is for a signed-in caller that holds schema.read in the site of the request. The checker decides.
    internal static async ValueTask<bool> MayIntrospectAsync(IServiceProvider services, GraphQLOptions options, CancellationToken cancellationToken)
    {
        if (options.OpenIntrospection ?? services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return true;
        }

        return services.GetRequiredService<IOperationContextAccessor>().Current is { Principal.IsAnonymous: false } operation
            && await services.GetRequiredService<IPermissionChecker>().AllowsAsync(SchemaRead, operation.Site, cancellationToken);
    }
}

internal static class GraphQLRequests
{
    // HotChocolate's codes for what it refuses before a field runs, as the plugin's.
    private static readonly FrozenDictionary<string, string> Codes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ErrorCodes.Validation.IntrospectionNotAllowed] = GraphQLErrorCodes.IntrospectionNotAllowed,

        // MaxDepth does not count introspection fields, so this rule is what holds a caller that may introspect.
        [ErrorCodes.Validation.MaxIntrospectionDepthOverflow] = GraphQLErrorCodes.DepthExceeded,
        [ErrorCodes.Execution.CostExceeded] = GraphQLErrorCodes.CostExceeded,
        [ErrorCodes.Execution.OnlyPersistedOperationsAllowed] = GraphQLErrorCodes.OnlyPersistedQueries,
        [ErrorCodes.Execution.PersistedOperationNotFound] = GraphQLErrorCodes.PersistedQueryNotFound,
        [ErrorCodes.Execution.Timeout] = GraphQLErrorCodes.Timeout,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // The outermost step of the request pipeline: a refusal of a limit, of introspection or of a query that is not persisted
    // leaves with its FCMS code in extensions.code.
    public static RequestDelegate WithCodes(RequestDelegate next) => async context =>
    {
        await next(context);
        if (context.Result is not OperationResult { Errors: { Count: > 0 } errors } result)
        {
            return;
        }

        var coded = errors.Select(error => CodeOf(error) is { } code ? error.WithCode(code) : error).ToList();
        if (coded.Where((error, index) => !ReferenceEquals(error, errors[index])).Select(error => error.Code).Distinct().ToList() is { Count: > 0 } codes)
        {
            result.Errors = [.. coded];
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<GraphQLPlugin>();
            codes.ForEach(code => GraphQLLog.Refused(logger, code!));
        }
    };

    // The depth rule gives its error no code; it names the depth it allows.
    private static string? CodeOf(IError error) =>
        error.Code is { } code && Codes.TryGetValue(code, out var known) ? known
        : error.Extensions?.ContainsKey("allowedExecutionDepth") == true ? GraphQLErrorCodes.DepthExceeded
        : null;

    // A body over the limit is refused as the REST APIs refuse one over theirs. One sent in chunks, which names no length,
    // is read here up to the limit, before HotChocolate parses it: a body cut while its parser reads it would be answered
    // as a request that is not well-formed, and some servers do not cut it at all.
    public static async ValueTask LimitBodyAsync(HttpContext context, long limit)
    {
        var request = context.Request;
        if (request.ContentLength > limit)
        {
            throw TooLarge(limit);
        }

        if (request.ContentLength is not null || HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return;
        }

        var buffered = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffered.Length + read > limit)
            {
                await buffered.DisposeAsync();
                throw TooLarge(limit);
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        context.Response.RegisterForDisposeAsync(buffered);
        request.Body = buffered;
    }

    private static BadHttpRequestException TooLarge(long limit) =>
        new($"The request body is larger than the {limit} bytes allowed.", StatusCodes.Status413PayloadTooLarge);
}

// A failure that no resolver answered, such as a stored value below a root field that does not read as its scalar, is
// what REST answers it with, FCMS3199 for an exception that says nothing of its cause, and is logged. The root fields
// report what the content service throws themselves, with its code, so an error with a code is left as it is, and so is
// a cancellation, which the timeout of the request answers.
internal sealed class UnexpectedErrors(IProblemDescriber problems, ILoggerFactory loggers) : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is not { } exception || exception is OperationCanceledException || error.Code is not null)
        {
            return error;
        }

        var problem = problems.Describe(exception);
        if (problem.Status >= 500)
        {
            GraphQLLog.FieldFailed(loggers.CreateLogger<GraphQLPlugin>(), exception, FieldOf(error.Path), problem.Code);
        }

        return ErrorBuilder.FromError(error).SetMessage(problem.Detail).SetCode(problem.Code).SetExtension("status", problem.Status).Build();
    }

    // The field that failed, by its name, with no index of a list: a path holds no value of content, but a name is all the log needs.
    private static string FieldOf(HotChocolate.Path? path)
    {
        for (var segment = path; segment is not null; segment = segment.Parent)
        {
            if (segment is NamePathSegment named)
            {
                return named.Name;
            }
        }

        return "(request)";
    }
}

// The persisted queries: the files of a folder, read once at start, each named {id}.graphql. A request names one by its id.
internal sealed class PersistedQueries : IOperationDocumentStorage
{
    private const string Extension = ".graphql";

    private readonly FrozenDictionary<string, OperationDocumentSourceText> _documents;

    public PersistedQueries(string? folder, string contentRoot)
    {
        Dictionary<string, OperationDocumentSourceText> documents = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var path = System.IO.Path.GetFullPath(folder, contentRoot);
            if (!Directory.Exists(path))
            {
                throw Invalid($"{nameof(GraphQLOptions.PersistedQueries)} names '{folder}', which is not a folder.");
            }

            foreach (var file in Directory.EnumerateFiles(path, "*" + Extension))
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(file);
                documents[id] = OperationDocumentId.IsValidId(id)
                    ? new OperationDocumentSourceText(File.ReadAllText(file))
                    : throw Invalid($"The persisted query '{System.IO.Path.GetFileName(file)}' has a name that cannot be the id of a query: use letters, digits, '-' and '_'.");
            }
        }

        _documents = documents.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public int Count => _documents.Count;

    public ValueTask<IOperationDocument?> TryReadAsync(OperationDocumentId documentId, CancellationToken cancellationToken = default) =>
        new(_documents.GetValueOrDefault(documentId.Value));

    public ValueTask SaveAsync(OperationDocumentId documentId, IOperationDocument document, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The persisted queries are the files of the folder; a request does not add one.");

    private static PluginException Invalid(string message) =>
        new(GraphQLErrorCodes.InvalidOptions, $"{GraphQLErrorCodes.InvalidOptions}: {GraphQLOptions.Section}: {message}");
}

internal sealed class GraphQLOptionsValidation : IValidateOptions<GraphQLOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphQLOptions options) => options.Problem() is { } problem
        ? ValidateOptionsResult.Fail($"{GraphQLErrorCodes.InvalidOptions}: {GraphQLOptions.Section}: {problem}")
        : ValidateOptionsResult.Success;
}

// Builds the schema when the host starts, so that options that are not valid, a folder that is missing or a name the
// content schema gives twice stop the host and not the first request.
internal sealed class GraphQLStartup(
    IRequestExecutorProvider executors,
    ContentModel model,
    PersistedQueries persisted,
    IEnumerable<IGraphQLSchemaContributor> contributors,
    IOptions<GraphQLOptions> options,
    ILogger<GraphQLPlugin> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = options.Value;
        await executors.GetExecutorAsync(GraphQLPlugin.SchemaName, cancellationToken);
        var contentTypes = model.ContentTypes.Count();
        var contributed = contributors.Count();
        GraphQLLog.SchemaBuilt(logger, contentTypes, contributed, persisted.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
