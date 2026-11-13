using System.Diagnostics;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Search;

// Type names one content type, or null for every type with a searchable field.
internal sealed record SearchRebuildPayload(string? Type);

// Indexes what the site and the global types hold, for content that existed before the plugin and for a site where the
// plugin was disabled for a while. An attempt that fails or times out starts again, and writes only what still differs.
internal sealed class SearchRebuildJob(SearchIndexer indexer, ILogger<SearchRebuildJob> logger) : IJob<SearchRebuildPayload>
{
    public const string Type = "search.rebuild";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(30);

    public static JobOptions Options(Guid site) => new() { Site = new SiteId(site) };

    public async Task ExecuteAsync(SearchRebuildPayload payload, JobContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        var started = Stopwatch.GetTimestamp();
        var site = context.Operation.Site;
        var (written, removed) = await indexer.RebuildAsync(payload.Type, site, context.CancellationToken);
        var duration = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        SearchLog.Rebuilt(logger, payload.Type ?? "every type", site.Value, written, removed, duration);
    }
}
