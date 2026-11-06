using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Delivery;

// The output cache of the Delivery API. A request without credentials reads the copy every such caller shares, and an API
// key reads a copy of its own, keyed by its claims, which name its role, sites and scope. Nothing else is cached: a bearer
// token's roles live in the database, credentials that fail are refused rather than served, and a draft changes with every
// save. A request with If-Modified-Since and no If-None-Match goes to the endpoint, which decides a 304 by the ETag alone.
// Only a 200 is stored, with the tag of its site.
internal sealed class DeliveryOutputCachePolicy : IOutputCachePolicy
{
    public const string CacheName = "delivery";

    public static readonly DeliveryOutputCachePolicy Instance = new();

    private const string StatusParameter = "status";
    private const string Published = "published";

    private static readonly object HitItem = new();

    ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
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

        // What the request bridge reads, exactly as sent: the framework's key folds the case of the path and sorts repeated
        // values, where the bridge takes the first.
        var request = http.Request;
        var vary = context.CacheVaryByRules.VaryByValues;
        vary["path"] = request.Path.Value ?? "";
        vary["query"] = request.QueryString.Value ?? "";
        vary["site"] = string.Join('\n', request.Headers[RequestBridge.SiteHeader].ToArray());
        vary["locale"] = string.Join('\n', request.Headers[RequestBridge.LocaleHeader].ToArray());
        vary["caller"] = caller ?? "";
        return ValueTask.CompletedTask;
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
        if (http.Response.StatusCode == StatusCodes.Status200OK && http.Response.Headers.SetCookie.Count == 0 && http.Features.Get<ApiRequest>() is { } request)
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
        if (!HttpMethods.IsGet(request.Method)
            || ApiAuthentication.Failed(context)
            || (request.Query.TryGetValue(StatusParameter, out var status) && status.Any(value => value != Published))
            || (request.Headers.IfNoneMatch.Count == 0 && request.Headers.IfModifiedSince.Count > 0))
        {
            return null;
        }

        var user = ApiAuthentication.Of(context);
        if (user.Identity?.IsAuthenticated == true)
        {
            return user.FindFirst(CmsClaimTypes.Kind)?.Value == nameof(PrincipalKind.ApiKey) ? "key:" + Hash(user.Claims) : null;
        }

        return request.Headers.Authorization.Count == 0 && !ApiKeyAuthentication.IsPresented(request) ? "anonymous" : null;
    }

    private static string Hash(IEnumerable<Claim> claims)
    {
        var text = string.Join('\n', claims.Select(claim => $"{claim.Type}={claim.Value}").Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
