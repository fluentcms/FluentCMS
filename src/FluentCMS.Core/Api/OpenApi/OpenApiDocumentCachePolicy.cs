using FluentCMS.Core.Caching;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.OpenApi;

// The document walks every type, route, component and block of the schema, so it is built once and kept. It is the same
// for every caller; what changes it is the schema, which is built once per process, and the server it names, which follows
// the request's scheme, host and path base unless a public origin is set. The key carries the schema hash, so a store
// that outlives the process never serves a copy made for another schema. UseFluentCms runs authorization before the output
// cache, so a policy the host puts on the route holds for a kept copy as for a fresh one. A copy that a middleware inside
// the cache encoded is not kept, since the key does not vary on Accept-Encoding.
internal sealed class OpenApiDocumentCachePolicy : IOutputCachePolicy
{
    public static readonly OpenApiDocumentCachePolicy Instance = new();

    private static readonly TimeSpan Duration = TimeSpan.FromDays(1);

    ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var cached = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);

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
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0 || CachedEncoding.EncodedInside(context.HttpContext))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
