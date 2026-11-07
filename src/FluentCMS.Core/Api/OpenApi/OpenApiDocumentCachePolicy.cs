using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Api.OpenApi;

// The document walks every type, route, component and block of the schema, so it is built once and kept. It is the same
// for every caller; what changes it is the schema, which is built once per process, and the server it names, which follows
// the request's scheme, host and path base unless a public origin is set. The key carries the schema hash, so a store
// that outlives the process never serves a copy made for another schema. The output cache runs before UseAuthorization, so
// a copy would be served past an authorization policy the host put on the route; such a route is built per request.
internal sealed class OpenApiDocumentCachePolicy : IOutputCachePolicy
{
    public static readonly OpenApiDocumentCachePolicy Instance = new();

    private static readonly TimeSpan Duration = TimeSpan.FromDays(1);

    ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var cached = (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) && !Authorizes(http);

        context.EnableOutputCaching = cached;
        context.AllowCacheLookup = cached;
        context.AllowCacheStorage = cached;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = Duration;

        var services = http.RequestServices;
        context.CacheVaryByRules.VaryByHost = services.GetRequiredService<PublicOrigin>().Base is null;
        context.CacheVaryByRules.VaryByValues["schema"] = SchemaSnapshot.Of(services.GetRequiredService<SchemaModel>()).Hash;
        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }

    // What the authorization middleware would check: the route's own requirement, or the fallback policy of a route that has none.
    private static bool Authorizes(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata is null || metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return false;
        }

        return metadata.GetMetadata<IAuthorizeData>() is not null
            || metadata.GetMetadata<AuthorizationPolicy>() is not null
            || metadata.GetMetadata<IAuthorizationRequirementData>() is not null
            || context.RequestServices.GetService<IOptions<AuthorizationOptions>>()?.Value.FallbackPolicy is not null;
    }
}
