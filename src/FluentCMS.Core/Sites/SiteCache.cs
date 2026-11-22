using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using FluentCMS.Events;

namespace FluentCMS.Core.Sites;

// What site resolution and the locale registry read for every request of this instance: the default site, the site of a
// slug, that a site exists, and its locales; and the default locale of a site, which every delivery and job run reads. A write through SiteManager, and a SiteUpdatedEvent raised on this instance,
// drop all of it at once, and a read that was under way then is not kept, because it may have read the old rows. No signal
// reaches another instance, so it sees a change when its copy expires, CacheDuration at the most. What is not found is not
// kept, so a site that another instance created is found at once; only that the site of a host name is gone is, since that
// site's id is never given again, and for MaxGoneSites ids at the most, since a host resolver may give any number of them.
// Requests that miss the same key at once share one read of it (GetOrLoadAsync), so a copy that expires under load is read
// again once, not once per request.
internal sealed class SiteCache(TimeProvider time)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public const string DefaultKey = "default";

    public const int MaxGoneSites = 1000;

    private readonly ConcurrentDictionary<string, (object Value, DateTimeOffset Expires)> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _gone = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (Task Load, long Generation)> _loads = new(StringComparer.Ordinal);
    private long _generation;

    // Taken before the database is read, and given back to Set.
    public long Generation => Interlocked.Read(ref _generation);

    public static string SlugKey(string slug) => "slug:" + slug;

    public static string SiteKey(Guid site) => "site:" + site.ToString("N");

    public static string LocalesKey(Guid site) => "locales:" + site.ToString("N");

    public static string DefaultLocaleKey(Guid site) => "default-locale:" + site.ToString("N");

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

    // The value of the key, or what load reads when it is not kept, which is then kept when keep says so. The first request
    // that misses runs load on its own context to the end, with no token, so that the requests that wait for it with theirs
    // are not failed by its going away; a read begun before a write is not shared with a request that comes after it.
    public async Task<T> GetOrLoadAsync<T>(string key, Func<CancellationToken, Task<T>> load, Func<T, bool> keep, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(keep);

        if (TryGet<T>(key, out var cached))
        {
            return cached;
        }

        TaskCompletionSource<T>? owned = null;
        Task<T> shared;
        long generation;
        lock (_gate)
        {
            generation = Generation;
            if (_loads.TryGetValue(key, out var running) && running.Generation == generation && running.Load is Task<T> same)
            {
                shared = same;
            }
            else
            {
                owned = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                _loads[key] = (owned.Task, generation);
                shared = owned.Task;
            }
        }

        if (owned is null)
        {
            return await shared.WaitAsync(cancellationToken);
        }

        try
        {
            var value = await load(CancellationToken.None);

            // A read that a write overtook would, through Set, take out what a read after the write has kept.
            if (keep(value) && Generation == generation)
            {
                Set(key, value!, generation);
            }

            owned.SetResult(value);
            return value;
        }
        catch (Exception exception)
        {
            owned.SetException(exception);
            _ = owned.Task.Exception;
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (_loads.TryGetValue(key, out var running) && running.Load == owned.Task)
                {
                    _loads.Remove(key);
                }
            }
        }
    }

    public bool IsGone(Guid site) => _gone.TryGetValue(site, out var expires) && expires > time.GetUtcNow();

    // With MaxGoneSites ids kept and none expired, one more is not kept, and is read again on its next request. The bound is
    // soft: calls at the same moment can each pass the check and go over it by their number.
    public void SetGone(Guid site, long generation)
    {
        var now = time.GetUtcNow();
        if (!_gone.ContainsKey(site) && _gone.Count >= MaxGoneSites)
        {
            foreach (var entry in _gone)
            {
                if (entry.Value <= now)
                {
                    _gone.TryRemove(entry);
                }
            }

            if (_gone.Count >= MaxGoneSites)
            {
                return;
            }
        }

        _gone[site] = now + CacheDuration;
        if (Generation != generation)
        {
            _gone.TryRemove(site, out _);
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _entries.Clear();
        _gone.Clear();
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
