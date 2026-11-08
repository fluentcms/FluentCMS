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
        public const string Cache = "fluentcms.cache";
        public const string Result = "fluentcms.result";
        public const string Code = "fluentcms.code";
        public const string Handler = "fluentcms.handler";
    }

    public static class Instruments
    {
        public const string Requests = "fluentcms.requests";
        public const string QueryDuration = "fluentcms.query.duration";
        public const string JobLatency = "fluentcms.job.latency";
        public const string CacheRequests = "fluentcms.cache.requests";
        public const string CacheHitRatio = "fluentcms.cache.hit_ratio";
        public const string ApiKeysRefused = "fluentcms.api_keys.refused";
        public const string EventHandlerDuration = "fluentcms.event.handler.duration";
        public const string JobOldestPendingAge = "fluentcms.job.oldest_pending_age";
    }

    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        Instruments.Requests, "{request}", "Requests to FluentCMS endpoints, by surface (delivery, management, connect, account, health) and status class.");

    internal static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(
        Instruments.QueryDuration, "s", "How long a content query takes to run against the database.");

    internal static readonly Histogram<double> JobLatency = Meter.CreateHistogram<double>(
        Instruments.JobLatency, "s", "How long one attempt of a job takes from the claim to the end, by job type and outcome.");

    internal static readonly Histogram<double> EventHandlerDuration = Meter.CreateHistogram<double>(
        Instruments.EventHandlerDuration, "s", "How long an in-process event handler takes after the commit, by event type, handler and outcome (succeeded, failed or timed-out).");

    internal static readonly Counter<long> CacheRequests = Meter.CreateCounter<long>(
        Instruments.CacheRequests, "{request}", "Lookups in a cache, by cache and result (hit or miss).");

    internal static readonly Counter<long> ApiKeysRefused = Meter.CreateCounter<long>(
        Instruments.ApiKeysRefused, "{request}", "Requests whose API key was refused, by the FCMS code of the reason: wrong, expired, revoked, or sent with another credential.");

    // Reports hits / (hits + misses) per cache since the process started. A cache that nothing has looked up reports nothing.
    internal static readonly ObservableGauge<double> CacheHitRatio = Meter.CreateObservableGauge(
        Instruments.CacheHitRatio, CacheTotals.Ratios, "1", "The share of lookups in a cache that found their entry.");

    internal static void RecordJobQueueAges(IReadOnlyDictionary<string, TimeSpan> ages) => JobQueueAges.Record(ages);

    // A span for an operation, with no value of content. Null when nothing listens, which every caller tolerates.
    internal static System.Diagnostics.Activity? Start(string kind, string operation, string? type, OperationContext? context)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

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

    private static readonly string[] StatusClasses = ["0xx", "1xx", "2xx", "3xx", "4xx", "5xx"];

    internal static string StatusClass(int status) => StatusClasses[Math.Clamp(status / 100, 0, 5)];

    internal static void RecordCache(string cache, bool hit)
    {
        CacheTotals.Record(cache, hit);
        CacheRequests.Add(1, new KeyValuePair<string, object?>(Tags.Cache, cache), new KeyValuePair<string, object?>(Tags.Result, hit ? "hit" : "miss"));
    }

    // Reports what the job runner last measured: every queue of this instance's options, at zero when nothing waits, and
    // every other queue with a job pending. The gauge is made here, so that the first record publishes it whether or not
    // anything else of FluentCmsTelemetry has been touched.
    private static class JobQueueAges
    {
        public static ObservableGauge<double> Gauge { get; } = Meter.CreateObservableGauge(
            Instruments.JobOldestPendingAge, Measurements, "s", "How long the oldest pending job of a queue, due and held by no worker, had waited when the job runner last looked.");

        private static IReadOnlyDictionary<string, TimeSpan> _ages = new Dictionary<string, TimeSpan>();

        public static void Record(IReadOnlyDictionary<string, TimeSpan> ages) => Volatile.Write(ref _ages, ages);

        public static IEnumerable<Measurement<double>> Measurements()
        {
            foreach (var (queue, age) in Volatile.Read(ref _ages))
            {
                yield return new Measurement<double>(age.TotalSeconds, new KeyValuePair<string, object?>(Tags.Queue, queue));
            }
        }
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
                yield return new Measurement<double>((double)hits / (hits + misses), new KeyValuePair<string, object?>(Tags.Cache, cache));
            }
        }
    }
}
