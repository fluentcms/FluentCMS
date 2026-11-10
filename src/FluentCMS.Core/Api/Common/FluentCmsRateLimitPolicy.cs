using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FluentCMS.Core.Api.Common;

internal static class RateLimitPolicyNames
{
    public const string Delivery = "delivery";
    public const string Management = "management";
    public const string Auth = "auth";
}

// One policy of the ASP.NET Core rate limiter, a token bucket per caller. Responses carry the IETF draft fields
// RateLimit-Limit, RateLimit-Remaining and RateLimit-Reset (seconds until the bucket is full), as the bucket was right after
// it counted the request, and a refused request also Retry-After (seconds until the next request is let through). They are
// not read from the bucket when the response starts, since the partitioned limiter may have disposed it by then.
internal abstract class FluentCmsRateLimitPolicy : IRateLimiterPolicy<string>
{
    // A caller's key starts with "ip:", "key:" or "sub:", so it is never this one.
    private const string CountedPartition = "counted";

    private readonly object _stateKey = new();
    private readonly object _countedKey = new();
    private readonly TimeProvider _time;
    private readonly bool _reportsHeaders;
    private readonly Func<string, RateLimiter> _createDefault;
    private readonly Func<object, Task> _writeHeaders;

    protected FluentCmsRateLimitPolicy(int defaultLimit, TimeProvider time, bool reportsHeaders = true)
    {
        DefaultLimit = defaultLimit;
        _time = time;
        _reportsHeaders = reportsHeaders;
        _createDefault = _ => new TokenBucketLimiter(defaultLimit, time);
        _writeHeaders = WriteHeaders;
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    protected int DefaultLimit { get; }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // The request was counted by the pre-authentication check of its path, so the endpoint's own limiter lets it through.
        // The endpoint's limiter keeps one limiter per key, whatever the factory, so the pass-through has a key of its own that
        // no caller's bucket shares.
        if (httpContext.Items.ContainsKey(_countedKey))
        {
            return RateLimitPartition.GetNoLimiter(CountedPartition);
        }

        var (key, limit) = Partition(httpContext);
        TokenBucketLimiter.RecordNextAcquireIn(_reportsHeaders ? Record(httpContext) : null);
        return RateLimitPartition.Get(key, limit == DefaultLimit ? _createDefault : Factory(limit));
    }

    public void MarkCounted(HttpContext context) => context.Items[_countedKey] = true;

    // The caller, which names its limit too only when that is not the policy's, so that a request builds no key of its own.
    protected abstract (string Key, int Limit) Partition(HttpContext context);

    protected static string Address(HttpContext context) => ApiCaller.Address(context);

    // Apart from GetPartition, whose every call would otherwise allocate the closure.
    private Func<string, RateLimiter> Factory(int limit) => _ => new TokenBucketLimiter(limit, _time);

    // The rate limiter asks again for the partition of a request it refused, to wait for a permit, and the request keeps
    // the one record.
    private RateLimitRecord Record(HttpContext context)
    {
        if (context.Items.TryGetValue(_stateKey, out var known))
        {
            return (RateLimitRecord)known!;
        }

        var record = new RateLimitRecord();
        context.Items[_stateKey] = record;
        context.Response.OnStarting(_writeHeaders, context);
        return record;
    }

    private Task WriteHeaders(object state)
    {
        var context = (HttpContext)state;
        if (context.Items.TryGetValue(_stateKey, out var record) && record is RateLimitRecord { State: { } bucket })
        {
            var headers = context.Response.Headers;
            headers["RateLimit-Limit"] = bucket.Limit.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Remaining"] = bucket.Remaining.ToString(CultureInfo.InvariantCulture);
            headers["RateLimit-Reset"] = bucket.SecondsToReset.ToString(CultureInfo.InvariantCulture);
        }

        return Task.CompletedTask;
    }

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        // The bucket that refused the request says when the next one is let through.
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : (int)TokenBucketLimiter.Window.TotalSeconds;

        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await ProblemWriter.WriteAsync(
            http,
            ProblemMapper.Known(LimitErrorCodes.RateLimited, $"Too many requests; try again in {seconds} seconds."),
            cancellationToken: cancellationToken);
    }
}

internal sealed class DeliveryRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Delivery.PermitLimit, time)
{
    protected override (string Key, int Limit) Partition(HttpContext context)
    {
        var caller = ApiCaller.Of(context);
        if (caller is { IsApiKey: true, Subject: { } subject })
        {
            var limit = caller.RateLimit is { } own ? Math.Min(own, options.Delivery.MaxKeyLimit) : DefaultLimit;
            return (limit == DefaultLimit ? "key:" + subject : string.Concat("key:", subject, "|", limit.ToString(CultureInfo.InvariantCulture)), limit);
        }

        return (Address(context), DefaultLimit);
    }
}

internal sealed class ManagementRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Management.PermitLimit, time)
{
    protected override (string Key, int Limit) Partition(HttpContext context) =>
        ApiCaller.Of(context).Subject is { } subject ? ("sub:" + subject, DefaultLimit) : (Address(context), DefaultLimit);
}

internal sealed class AuthRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Auth.PermitLimit, time)
{
    protected override (string Key, int Limit) Partition(HttpContext context) => (Address(context), DefaultLimit);
}

// The coarse limit that runs before authentication; it adds no headers, since the policy of the endpoint adds its own.
internal sealed class PreAuthRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.PreAuth.PermitLimit, time, reportsHeaders: false)
{
    protected override (string Key, int Limit) Partition(HttpContext context) => (Address(context), DefaultLimit);
}

// The probes' own partition of the check before authentication, with no headers either.
internal sealed class HealthRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Health.PermitLimit, time, reportsHeaders: false)
{
    protected override (string Key, int Limit) Partition(HttpContext context) => (Address(context), DefaultLimit);
}

internal static class ClientAddress
{
    // An IPv4-mapped address is its IPv4 address, and an IPv6 address is its /64, which is what one subscriber gets.
    public static string Partition(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
