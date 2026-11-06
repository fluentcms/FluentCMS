using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FluentCMS.Core.Api.OpenApi;

// Every route but the content ones, which the document transformer writes per type: a tag from its group, an operation
// id from its method and path, who may call it, and the problem body of its errors.
internal sealed class FluentCmsOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var description = context.Description;
        if (description.ActionDescriptor.EndpointMetadata.OfType<ContentOperation>().Any() || context.Document is not { } document)
        {
            return Task.CompletedTask;
        }

        var surface = OpenApiRegistration.SurfaceOf(description);
        var path = (description.RelativePath ?? "").Trim('/');
        var segments = Local(path, surface);

        operation.Tags = new HashSet<OpenApiTagReference> { new(Tag(segments), document) };
        operation.OperationId ??= (description.HttpMethod ?? "get").ToLowerInvariant() + string.Concat(segments.Select(Name));
        operation.Security = Security(surface, document);
        operation.Responses ??= [];
        operation.Responses["default"] = new OpenApiResponseReference(SharedComponents.ProblemResponse, document);
        return Task.CompletedTask;
    }

    // Delivery answers anyone as the Public role, and a key or a token as itself; the Management API needs a token or a
    // key with the management scope.
    public static List<OpenApiSecurityRequirement> Security(string? surface, OpenApiDocument document)
    {
        var oauth = new OpenApiSecuritySchemeReference(FluentCmsDocumentTransformer.OAuth, document);
        var key = new OpenApiSecuritySchemeReference(FluentCmsDocumentTransformer.ApiKey, document);
        return surface == ContentPaths.Delivery
            ? [[], new() { [oauth] = [] }, new() { [key] = [] }]
            : [new() { [oauth] = [TokenScopes.Management] }, new() { [key] = [] }];
    }

    // The path below its group's prefix, such as users/{id}/sessions under /api/management/v1.
    private static string[] Local(string path, string? surface)
    {
        var prefix = FluentCmsEndpointGroups.Prefixes
            .Where(entry => entry.Group.ToString().Equals(surface, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Prefix.Trim('/'))
            .FirstOrDefault();
        var start = prefix is null ? -1 : path.IndexOf(prefix, StringComparison.Ordinal);
        var local = start < 0 ? path : path[(start + prefix!.Length)..];
        return local.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Tag(string[] segments)
    {
        var first = segments.FirstOrDefault(segment => !segment.StartsWith('{')) ?? "api";
        var words = first.Replace('-', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Name(string segment) =>
        segment.StartsWith('{') ? "By" + ContentPaths.Pascal(segment.Trim('{', '}', '?')) : ContentPaths.Pascal(segment);
}
