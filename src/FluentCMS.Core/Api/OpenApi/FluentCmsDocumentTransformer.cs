using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FluentCMS.Core.Api.OpenApi;

// Adds what the API explorer cannot see: the schemas of the live schema, the security schemes, the query grammar as
// parameters, and the content routes once per content type in place of their {type} form.
internal sealed class FluentCmsDocumentTransformer(SchemaModel schema, OpenApiDocumentOptions options, PublicOrigin origin) : IOpenApiDocumentTransformer
{
    public const string OAuth = "oauth2";
    public const string ApiKey = "apiKey";
    public const string PreviewToken = "previewToken";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var schemas = new ContentSchemas(schema, options);
        var paths = new ContentPaths(schema, schemas);

        // With a public origin the server, and so the authorization server's URLs, are at it as the issuer is, so a forged
        // Host cannot name itself there; without one they follow the request.
        if (origin.Base is { } publicBase)
        {
            document.Servers = [new OpenApiServer { Url = publicBase }];
        }

        document.Info ??= new OpenApiInfo();
        document.Info.Title = "FluentCMS";
        document.Info.Version = OpenApiRegistration.DocumentName;
        document.Info.Description = "The Delivery and Management APIs of this deployment, with a schema for each of its content types, components and blocks.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[OAuth] = OpenApiFragments.Read<OpenApiSecurityScheme>(OAuthScheme(document), document, "the oauth2 scheme");
        document.Components.SecuritySchemes[ApiKey] = OpenApiFragments.Read<OpenApiSecurityScheme>(ApiKeyScheme(), document, "the apiKey scheme");
        document.Components.SecuritySchemes[PreviewToken] = OpenApiFragments.Read<OpenApiSecurityScheme>(PreviewTokenScheme(), document, "the previewToken scheme");

        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (var (name, json) in SharedComponents.Schemas().Concat(schemas.All()))
        {
            document.Components.Schemas[name] = OpenApiFragments.Read<OpenApiSchema>(json, document, $"the schema {name}");
        }

        document.Components.Parameters ??= new Dictionary<string, IOpenApiParameter>(StringComparer.Ordinal);
        foreach (var (name, json) in SharedComponents.Parameters().Concat(paths.Parameters()))
        {
            document.Components.Parameters[name] = OpenApiFragments.Read<OpenApiParameter>(json, document, $"the parameter {name}");
        }

        document.Components.Responses ??= new Dictionary<string, IOpenApiResponse>(StringComparer.Ordinal);
        document.Components.Responses[SharedComponents.ProblemResponse] =
            OpenApiFragments.Read<OpenApiResponse>(SharedComponents.ProblemResponseBody(), document, "the problem response");

        List<ContentRoute> routes = [.. context.DescriptionGroups
            .SelectMany(group => group.Items)
            .Select(description => (Description: description, Operation: description.ActionDescriptor.EndpointMetadata.OfType<ContentOperation>().LastOrDefault()))
            .Where(item => item.Operation is not null && item.Description.HttpMethod is { } method && !HttpMethods.IsHead(method) && item.Description.RelativePath is not null)
            .Select(item => new ContentRoute(
                "/" + item.Description.RelativePath!.Trim('/'),
                item.Description.HttpMethod!,
                OpenApiRegistration.SurfaceOf(item.Description) ?? "",
                item.Operation!))];

        document.Paths ??= [];
        foreach (var route in routes)
        {
            document.Paths.Remove(route.Path);
        }

        foreach (var (path, item) in paths.Paths(routes))
        {
            document.Paths[path] = OpenApiFragments.Read<OpenApiPathItem>(item, document, $"the path {path}");
        }

        document.Tags = new HashSet<OpenApiTag>([.. document.Paths.Values
            .SelectMany(item => item.Operations?.Values.AsEnumerable() ?? [])
            .SelectMany(operation => operation.Tags ?? new HashSet<OpenApiTagReference>())
            .Select(tag => tag.Name)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => new OpenApiTag { Name = name })]);

        return Task.CompletedTask;
    }

    // PKCE is required, which OpenAPI has no field for; x-usePkce is what Scalar reads to use it.
    private static JsonObject OAuthScheme(OpenApiDocument document)
    {
        var server = document.Servers?.FirstOrDefault()?.Url?.TrimEnd('/') ?? "";
        JsonObject scopes = new()
        {
            ["openid"] = "Signs the user in.",
            ["profile"] = "The user's name.",
            ["email"] = "The user's email address.",
            ["offline_access"] = "A refresh token.",
            [TokenScopes.Draft] = "Reads drafts through the Delivery API.",
            [TokenScopes.Management] = "Uses the Management API.",
        };

        return new JsonObject
        {
            ["type"] = "oauth2",
            ["description"] = "The authorization server of this deployment. The authorization code flow needs PKCE with S256; a confidential client may use client credentials.",
            ["flows"] = new JsonObject
            {
                ["authorizationCode"] = new JsonObject
                {
                    ["authorizationUrl"] = $"{server}/connect/authorize",
                    ["tokenUrl"] = $"{server}/connect/token",
                    ["refreshUrl"] = $"{server}/connect/token",
                    ["scopes"] = scopes,
                    ["x-usePkce"] = "SHA-256",
                },
                ["clientCredentials"] = new JsonObject
                {
                    ["tokenUrl"] = $"{server}/connect/token",
                    ["scopes"] = scopes.DeepClone(),
                },
            },
        };
    }

    private static JsonObject PreviewTokenScheme() => new()
    {
        ["type"] = "http",
        ["scheme"] = PreviewTokenAuthentication.Scheme,
        ["description"] = "Authorization: Preview <token>, a token the Management API mints for one entry. It reads that entry's draft with status=draft, in its locale when it names one, until it expires.",
    };

    private static JsonObject ApiKeyScheme() => new()
    {
        ["type"] = "apiKey",
        ["in"] = "header",
        ["name"] = ApiKeyAuthentication.Header,
        ["description"] = "An API key, also accepted as Authorization: ApiKey <key>. A Delivery or Preview key reads, a Management key manages.",
    };
}
