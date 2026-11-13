using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Telemetry;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Delivery;

// The output cache of the Delivery API. A request without credentials reads the copy every such caller shares, and an API
// key reads a copy of its own, keyed by its claims, which name its role, sites and scope. Nothing else is cached: a bearer
// token's roles live in the database, credentials that fail are refused rather than served, and a draft changes with every
// save. The key carries the schema hash, so a store that outlives the process, or that instances on another schema share,
// never serves a copy made for another schema. Only a 200 is stored, with the tag of its site, and never one that a
// middleware inside the cache encoded, since the key does not vary on Accept-Encoding. A HEAD is stored apart from its GET,
// since the key carries the method. Beside the host name, which the framework puts in the key, the key carries the site
// that host name resolves to, so that a host name which moves to another site reads none of the copies it left. That site
// is resolved once for the request, and the endpoint reads the same one, so a copy is never stored under another site.
internal sealed class DeliveryOutputCachePolicy : IOutputCachePolicy
{
    public const string CacheName = "delivery";

    public static readonly DeliveryOutputCachePolicy Instance = new();

    private const string StatusParameter = "status";
    private const string Published = "published";

    private static readonly object HitItem = new();

    async ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetRequiredService<DeliveryCacheOptions>();
        var caller = options.OutputCache ? Caller(http) : null;

        context.EnableOutputCaching = caller is not null;
        if (caller is not null)
        {
            // Counted when the response is done, so that a lookup whose endpoint throws is a miss too.
            http.Response.OnCompleted(static state =>
            {
                var items = ((HttpContext)state).Items;
                FluentCmsTelemetry.RecordCache(CacheName, hit: items.ContainsKey(HitItem));
                return Task.CompletedTask;
            }, http);
        }

        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = options.OutputCacheDuration;

        // The routes have no modification date, so If-Modified-Since is ignored (RFC 9110, 13.1.3) rather than left for the
        // output cache, which would answer it by the date the copy was stored.
        var request = http.Request;
        request.Headers.IfModifiedSince = default;

        // What the request bridge reads, exactly as sent: the framework's key folds the case of the path and sorts repeated
        // values, where the bridge takes the first.
        var vary = context.CacheVaryByRules.VaryByValues;
        vary["path"] = request.Path.Value ?? "";
        vary["query"] = request.QueryString.Value ?? "";
        vary["site"] = string.Join('\n', request.Headers[RequestBridge.SiteHeader].ToArray());
        vary["locale"] = string.Join('\n', request.Headers[RequestBridge.LocaleHeader].ToArray());
        vary["caller"] = caller ?? "";
        vary["schema"] = SchemaSnapshot.Of(http.RequestServices.GetRequiredService<SchemaModel>()).Hash;

        // The header, the parameter and a key's binding, which win over the host name, are in the key already.
        vary["hostSite"] = caller is null ? "" : await HostSiteAsync(http, cancellationToken);
    }

    private static async ValueTask<string> HostSiteAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var site = await http.RequestServices.GetRequiredService<RequestBridge>().HostSiteAsync(http, cancellationToken);
        return site.Value.ToString("N");
    }

    ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Items[HitItem] = true;
        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        // The middleware asks ServeFromCacheAsync before it checks that the entry is fresh, so an entry it then turned down
        // ran the endpoint after all.
        var http = context.HttpContext;
        http.Items.Remove(HitItem);
        var response = http.Response;
        if (response.StatusCode == StatusCodes.Status200OK && response.Headers.SetCookie.Count == 0 && !CachedEncoding.EncodedInside(http)
            && http.Features.Get<ApiRequest>() is { } request)
        {
            context.Tags.Add(CacheTags.Site(request.Operation.Site.Value));
        }
        else
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }

    private static string? Caller(HttpContext context)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || ApiAuthentication.Failed(context)
            || (request.Query.TryGetValue(StatusParameter, out var status) && status.Any(value => value != Published)))
        {
            return null;
        }

        var caller = ApiCaller.Of(context);
        if (caller.IsAuthenticated)
        {
            return caller.IsApiKey ? caller.KeyCacheKey : null;
        }

        return request.Headers.Authorization.Count == 0 && !ApiKeyAuthentication.IsPresented(request) ? "anonymous" : null;
    }
}
