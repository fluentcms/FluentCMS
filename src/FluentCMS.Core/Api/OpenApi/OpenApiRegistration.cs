using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace FluentCMS.Core.Api.OpenApi;

// The document v1 of Microsoft.AspNetCore.OpenApi, holding the Delivery and Management routes only, so a host's own
// endpoints stay out of it; a host that documents its own uses a document of another name.
internal static class OpenApiRegistration
{
    public const string DocumentName = "v1";
    public const string DocumentRoute = "/openapi/v1.json";
    public const string ScalarRoute = "/scalar";

    private static readonly string[] Surfaces = [ContentPaths.Delivery, "management"];

    public static IServiceCollection AddFluentCmsOpenApi(this IServiceCollection services, OpenApiDocumentOptions options)
    {
        services.AddFluentCmsOptions(OpenApiDocumentOptions.Section, HostErrorCodes.InvalidOption, options);
        return services.AddOpenApi(DocumentName, openApi =>
        {
            openApi.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            openApi.ShouldInclude = description => SurfaceOf(description) is { } surface && Surfaces.Contains(surface, StringComparer.Ordinal);
            openApi.AddSchemaTransformer(EnumsAsNames);
            openApi.AddSchemaTransformer(NumbersAsNumbers);
            openApi.AddOperationTransformer<FluentCmsOperationTransformer>();
            openApi.AddDocumentTransformer<FluentCmsDocumentTransformer>();
        });
    }

    public static string? SurfaceOf(ApiDescription description) =>
        description.ActionDescriptor.EndpointMetadata.OfType<FluentCmsSurfaceMetadata>().LastOrDefault()?.Name;

    // Where the Scalar page finds the document: beside it, in any group both are mapped in. The page adds the path base
    // the browser sees, so it is left out here.
    public static string DocumentUrl(HttpContext context)
    {
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? ScalarRoute;
        var at = pattern.LastIndexOf(ScalarRoute, StringComparison.Ordinal);
        var group = at > 0 ? "/" + pattern[..at].Trim('/') : "";
        return group + DocumentRoute;
    }

    // FluentCMS writes enum values as camel case names; a host's JsonOptions may not.
    private static Task EnumsAsNames(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum && underlying.Namespace?.StartsWith("FluentCMS", StringComparison.Ordinal) == true)
        {
            var nullable = underlying != type;
            schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
            schema.Format = null;
            schema.Enum = [.. Enum.GetNames(underlying).Select(name => (JsonNode)JsonNamingPolicy.CamelCase.ConvertName(name))];
            if (nullable)
            {
                schema.Enum.Add(JsonNullSentinel.JsonNull);
            }
        }

        return Task.CompletedTask;
    }

    // FluentCMS writes numbers as numbers and reads them in a body only as numbers; the web defaults of a host's JsonOptions
    // would describe them as a number or a numeric string.
    private static Task NumbersAsNumbers(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String) && (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0)
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    }
}
