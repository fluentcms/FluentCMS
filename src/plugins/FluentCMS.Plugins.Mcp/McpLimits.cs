using System.Collections.Concurrent;
using FluentCMS.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Mcp;

// Bound from FluentCms:Mcp:Limits.
public sealed class McpCallLimitOptions
{
    // The tool calls a client may make in a minute.
    public int CallsPerMinute { get; set; } = 120;

    // The calls of tools that change data a client may make in a minute, which count as calls too.
    public int WritesPerMinute { get; set; } = 30;
}

// What a client that is over a limit is told: the limit and how long until its next call is let through.
internal readonly record struct McpCallRefusal(bool Write, int Limit, TimeSpan RetryAfter, bool Report)
{
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));
}

// The tool calls of each client, counted on this instance, as core counts its rate limits (spec 4.10): a bucket holds the
// limit and fills again over a minute, kept as the time it is next full, so a quiet client may send the limit at once and
// then one call per minute divided by the limit. A client is the client id of a token, or the key of an API key, which
// the caller does not choose. A call that is refused takes nothing from a bucket.
internal sealed class McpCallLimits(IOptions<McpOptions> options, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Above this many buckets, the full ones, which hold nothing a new one would not, are dropped once a window.
    private const int SweepAbove = 1024;

    private readonly ConcurrentDictionary<string, Bucket> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Bucket> _writes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(Guid Site, Principal Principal, string Tool), long> _reads = new();
    private long _swept;

    public static string ClientOf(OperationContext operation) =>
        operation.ClientId is { Length: > 0 } client
            ? $"{(operation.Principal.Kind == PrincipalKind.ApiKey ? "key" : "client")}:{client}"
            : $"principal:{operation.Principal}";

    public McpCallRefusal? Call(string client) => Take(_calls, client, options.Value.Limits.CallsPerMinute, write: false);

    public McpCallRefusal? Write(string client) => Take(_writes, client, options.Value.Limits.WritesPerMinute, write: true);

    // A tool that a list showed a principal as read-only in a site is taken as one for a window, so that a read does not
    // list the tools each time. It is remembered for that principal alone, since a list is made for its caller, and only
    // a read is remembered: a tool that changes data is looked up on every call.
    public bool IsKnownRead(OperationContext operation, string tool) =>
        _reads.TryGetValue((operation.Site.Value, operation.Principal, tool), out var until) && time.GetUtcNow().UtcTicks < until;

    public void RememberRead(OperationContext operation, string tool) =>
        _reads[(operation.Site.Value, operation.Principal, tool)] = time.GetUtcNow().UtcTicks + Window.Ticks;

    private McpCallRefusal? Take(ConcurrentDictionary<string, Bucket> buckets, string client, int limit, bool write)
    {
        var now = time.GetUtcNow().UtcTicks;
        Sweep(now);
        var wait = buckets.GetOrAdd(client, static (_, start) => new Bucket(start), now).Take(now, Math.Max(1, Window.Ticks / limit), out var report);
        return wait > 0 ? new McpCallRefusal(write, limit, TimeSpan.FromTicks(wait), report) : null;
    }

    private void Sweep(long now)
    {
        var swept = Interlocked.Read(ref _swept);
        if (_calls.Count + _writes.Count + _reads.Count <= SweepAbove
            || Math.Abs(now - swept) < Window.Ticks
            || Interlocked.CompareExchange(ref _swept, now, swept) != swept)
        {
            return;
        }

        foreach (var buckets in (ReadOnlySpan<ConcurrentDictionary<string, Bucket>>)[_calls, _writes])
        {
            foreach (var (client, bucket) in buckets)
            {
                if (bucket.IsFull(now))
                {
                    buckets.TryRemove(KeyValuePair.Create(client, bucket));
                }
            }
        }

        foreach (var read in _reads)
        {
            if (read.Value <= now)
            {
                _reads.TryRemove(read);
            }
        }
    }

    private sealed class Bucket(long start)
    {
        private readonly Lock _gate = new();
        private long _full = start;
        private long _last = start;
        private long _reported = long.MinValue;

        // Zero when the call is let through, and otherwise the ticks until the next one is. Report is true for the first
        // refusal of a window, so that a client in a loop is logged once a minute and not on every call.
        public long Take(long now, long interval, out bool report)
        {
            lock (_gate)
            {
                Move(now);
                report = false;
                var start = Math.Max(_full, now);
                var wait = start + interval - now - Window.Ticks;
                if (wait <= 0)
                {
                    _full = start + interval;
                    return 0;
                }

                if (_reported == long.MinValue || Math.Abs(now - _reported) >= Window.Ticks)
                {
                    _reported = now;
                    report = true;
                }

                return wait;
            }
        }

        public bool IsFull(long now)
        {
            lock (_gate)
            {
                Move(now);
                return _full <= now;
            }
        }

        // A clock that moves back moves the bucket with it, so that it neither fills nor empties.
        private void Move(long now)
        {
            if (now < _last)
            {
                _full -= _last - now;
            }

            _last = now;
        }
    }
}

internal static class McpRequestLimits
{
    // A body over the limit is refused before it is read, as the REST APIs refuse one over theirs (FCMS3111). The
    // server's own limit is lowered where it has one, so that a body sent in chunks ends there too. Core's cap has run
    // already, so the lower of the two holds.
    public static RouteGroupBuilder LimitBody(this RouteGroupBuilder group, long limit)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            LimitBody(context.HttpContext, limit);
            return await next(context);
        });
        return group;
    }

    public static void LimitBody(HttpContext context, long limit)
    {
        if (context.Request.ContentLength > limit)
        {
            throw new BadHttpRequestException($"The request body is larger than the {limit} bytes allowed.", StatusCodes.Status413PayloadTooLarge);
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = Math.Min(feature.MaxRequestBodySize ?? long.MaxValue, limit);
        }
    }
}
