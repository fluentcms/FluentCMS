using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Api.Common;

// A group a plugin mapped with MapProtectedResource: an OAuth protected resource of its own (RFC 9728), as /mcp is.
internal sealed record ProtectedResource(string Plugin, string Prefix, string Scope, string? Name);

// Marks the endpoints of a protected resource, so that a refusal carries its challenge.
internal sealed record ProtectedResourceMetadata(ProtectedResource Resource);

// The protected resources of the host. They are added while the endpoints are mapped and read by requests after that:
// by the metadata documents under /.well-known, by the challenge of a refusal, and by the authorization server, which
// takes the public URL of one as the resource parameter of a request (RFC 8707). The URLs are built from the public
// origin, or from the request when none is set, as the issuer is.
internal sealed class ProtectedResources(PublicOrigin origin)
{
    public const string MetadataPath = "oauth-protected-resource";

    private volatile ProtectedResource[] _resources = [];

    public IReadOnlyList<ProtectedResource> All => _resources;

    public void Add(ProtectedResource resource) => _resources = [.. _resources, resource];

    public Uri UrlOf(HttpRequest request, ProtectedResource resource) => origin.Link(request, resource.Prefix);

    public Uri MetadataUrlOf(HttpRequest request, ProtectedResource resource) =>
        origin.Link(request, $"{FluentCmsEndpointGroups.PrefixOf(FluentCmsEndpointGroup.WellKnown)}/{MetadataPath}{resource.Prefix}");

    // Whether a resource parameter is the public URL of a protected resource. The scheme and host compare as URIs do and
    // the path as written, so nothing but the URL the metadata names is taken.
    public bool Names(HttpRequest request, string resource) =>
        Uri.TryCreate(resource, UriKind.Absolute, out var named)
        && _resources.Any(item => string.Equals(UrlOf(request, item).AbsoluteUri, named.AbsoluteUri, StringComparison.Ordinal));

    // The document of RFC 9728. The authorization server is this instance, named as its discovery document names it.
    public JsonObject Document(HttpRequest request, ProtectedResource resource)
    {
        var issuer = origin.Issuer ?? new Uri($"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}", UriKind.Absolute);
        var document = new JsonObject
        {
            ["resource"] = UrlOf(request, resource).AbsoluteUri,
            ["authorization_servers"] = new JsonArray(issuer.AbsoluteUri),
            ["scopes_supported"] = new JsonArray(resource.Scope),
            ["bearer_methods_supported"] = new JsonArray("header"),
        };
        if (resource.Name is { Length: > 0 } name)
        {
            document["resource_name"] = name;
        }

        return document;
    }

    // RFC 6750 and RFC 9728: a 401 names the metadata of the resource, so that a client finds the authorization server, and
    // a token without the scope of the resource is told which one it lacks.
    public void Challenge(HttpContext context, ProtectedResource resource, ApiProblem problem, Exception exception)
    {
        var metadata = $"resource_metadata=\"{MetadataUrlOf(context.Request, resource).AbsoluteUri}\"";
        var scope = $"scope=\"{resource.Scope}\"";
        var challenge = problem.Status switch
        {
            StatusCodes.Status401Unauthorized when exception is RequestAuthenticationException => $"Bearer error=\"invalid_token\", {metadata}, {scope}",
            StatusCodes.Status401Unauthorized => $"Bearer {metadata}, {scope}",
            StatusCodes.Status403Forbidden when exception is ScopeDeniedException => $"Bearer error=\"insufficient_scope\", {scope}, {metadata}",
            _ => null,
        };
        if (challenge is not null)
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = challenge;
        }
    }
}

internal static class ProtectedResourceAccess
{
    // After WithFluentCmsOperation, whose filter resolves the principal this one reads. No endpoint of a protected resource
    // is anonymous. A bearer token needs the scope of the resource. An API key holds no OAuth scope, so it is taken by its
    // class, which the engine reads; a preview token carries the kind of the principal that minted it and is not a key.
    public static TBuilder RequireResourceScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var principal = ApiRequest.Of(context.HttpContext).Operation.Principal;
            if (principal.IsAnonymous)
            {
                throw new SignInRequiredException("This endpoint needs a bearer token or an API key.");
            }

            var isKey = principal.Kind == PrincipalKind.ApiKey && principal.Claim(CmsClaimTypes.Entry) is null;
            var scopes = (principal.Claim(TokenScopes.Claim) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!isKey && !scopes.Contains(scope, StringComparer.Ordinal))
            {
                throw new ScopeDeniedException($"This endpoint needs a bearer token with the {scope} scope, or an API key.");
            }

            return await next(context);
        });
}
