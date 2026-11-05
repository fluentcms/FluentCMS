using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentCMS.Content;

namespace FluentCMS.Core.Telemetry;

// The one ActivitySource and the one Meter of FluentCMS. Nothing is exported from here: a host adds an exporter and
// subscribes with AddSource(FluentCmsTelemetry.Name) and AddMeter(FluentCmsTelemetry.Name). Tags name the operation, the
// content type and the site, and never a value of content.
public static class FluentCmsTelemetry
{
    public const string Name = "FluentCMS";

    public static ActivitySource ActivitySource { get; } = new(Name, Version);

    public static Meter Meter { get; } = new(Name, Version);

    private static string Version => typeof(FluentCmsTelemetry).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public static class Tags
    {
        public const string Operation = "fluentcms.operation";
        public const string Type = "fluentcms.type";
        public const string Site = "fluentcms.site";
        public const string Queue = "fluentcms.queue";
        public const string Hooks = "fluentcms.hooks";
        public const string Surface = "fluentcms.surface";
        public const string StatusClass = "fluentcms.status_class";
        public const string Outcome = "fluentcms.outcome";
    }

    public static class Instruments
    {
        public const string Requests = "fluentcms.requests";
        public const string QueryDuration = "fluentcms.query.duration";
        public const string JobLatency = "fluentcms.job.latency";
        public const string CacheRequests = "fluentcms.cache.requests";
        public const string CacheHitRatio = "fluentcms.cache.hit_ratio";
    }

    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        Instruments.Requests, "{request}", "Requests to FluentCMS endpoints, by surface (delivery, management, connect, account, health) and status class.");

    internal static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(
        Instruments.QueryDuration, "s", "How long a content query takes to run against the database.");

    internal static readonly Histogram<double> JobLatency = Meter.CreateHistogram<double>(
        Instruments.JobLatency, "s", "How long one attempt of a job takes from the claim to the end, by job type and outcome.");

    internal static readonly Counter<long> CacheRequests = Meter.CreateCounter<long>(
        Instruments.CacheRequests, "{request}", "Lookups in a cache, by cache and result (hit or miss).");

    // Reports hits / (hits + misses) per cache since the process started. A cache that nothing has looked up reports nothing.
    internal static readonly ObservableGauge<double> CacheHitRatio = Meter.CreateObservableGauge(
        Instruments.CacheHitRatio, CacheTotals.Ratios, "1", "The share of lookups in a cache that found their entry.");

    // A span for an operation, with no value of content. Null when nothing listens, which every caller tolerates.
    internal static System.Diagnostics.Activity? Start(string kind, string operation, string? type, OperationContext? context)
    {
        var activity = ActivitySource.StartActivity($"{kind} {operation}");
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(Tags.Operation, operation);
        if (type is not null)
        {
            activity.SetTag(Tags.Type, type);
        }

        if (context is not null)
        {
            activity.SetTag(Tags.Site, context.Site.Value.ToString("D"));
        }

        return activity;
    }

    internal static void RecordCache(string cache, bool hit)
    {
        CacheTotals.Record(cache, hit);
        CacheRequests.Add(1, new KeyValuePair<string, object?>("cache", cache), new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));
    }

    private static class CacheTotals
    {
        private static readonly ConcurrentDictionary<string, (long Hits, long Misses)> Counts = new(StringComparer.Ordinal);

        public static void Record(string cache, bool hit) =>
            Counts.AddOrUpdate(cache, hit ? (1, 0) : (0, 1), (_, current) => hit ? (current.Hits + 1, current.Misses) : (current.Hits, current.Misses + 1));

        public static IEnumerable<Measurement<double>> Ratios()
        {
            foreach (var (cache, (hits, misses)) in Counts)
            {
                yield return new Measurement<double>((double)hits / (hits + misses), new KeyValuePair<string, object?>("cache", cache));
            }
        }
    }
}
