using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using FluentCMS.Events;

namespace FluentCMS.Core.Sites;

// What site resolution and the locale registry read for every request of this instance: the default site, the site of a
// slug, that a site exists, and its locales. A write through SiteManager, and a SiteUpdatedEvent raised on this instance,
// drop all of it at once, and a read that was under way then is not kept, because it may have read the old rows. No signal
// reaches another instance, so it sees a change when its copy expires, CacheDuration at the most. What is not found is not
// kept, so a site that another instance created is found at once; only that the site of a host name is gone is, since that
// site's id is never given again.
internal sealed class SiteCache(TimeProvider time)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public const string DefaultKey = "default";

    private readonly ConcurrentDictionary<string, (object Value, DateTimeOffset Expires)> _entries = new(StringComparer.Ordinal);
    private long _generation;

    // Taken before the database is read, and given back to Set.
    public long Generation => Interlocked.Read(ref _generation);

    public static string SlugKey(string slug) => "slug:" + slug;

    public static string SiteKey(Guid site) => "site:" + site.ToString("N");

    public static string LocalesKey(Guid site) => "locales:" + site.ToString("N");

    public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > time.GetUtcNow() && entry.Value is T found)
        {
            value = found;
            return true;
        }

        value = default;
        return false;
    }

    public void Set(string key, object value, long generation)
    {
        _entries[key] = (value, time.GetUtcNow() + CacheDuration);

        // A write came in between: what was just stored may be the old row, so take it out again.
        if (Generation != generation)
        {
            _entries.TryRemove(key, out _);
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _entries.Clear();
    }
}

// A plugin that changes a site raises SiteUpdatedEvent, as SiteManager does.
internal sealed class SiteCacheInvalidation(SiteCache cache) : IEventHandler<SiteUpdatedEvent>
{
    public ValueTask HandleAsync(SiteUpdatedEvent message, CancellationToken cancellationToken)
    {
        cache.Invalidate();
        return ValueTask.CompletedTask;
    }
}
