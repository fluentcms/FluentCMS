using System.Collections.Concurrent;

namespace FluentCMS.Core.Settings;

internal enum SettingsSource
{
    Code = 0,
    Default = 1,
    Site = 2,
}

// What a read resolved to: the document, where it came from, and the version of the row it came from (0 for the code default).
internal sealed record ResolvedSetting(string Json, SettingsSource Source, int Version);

// What readers on this instance keep. A write drops everything of its area before it returns, and a read that was under way
// when the write happened is not kept, because it may have read the old row. Another instance is not told: its readers
// are stale for CacheDuration at the most.
internal sealed class SettingsCache(SettingsRegistry registry, TimeProvider time)
{
    // The scope of the instance defaults. No site has this id.
    public static readonly Guid InstanceScope = Guid.Empty;

    private readonly ConcurrentDictionary<(Guid Scope, string Area), (ResolvedSetting Value, DateTimeOffset Expires)> _entries = new();
    private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);

    public bool TryGet(Guid scope, string area, out ResolvedSetting value)
    {
        if (registry.CacheDuration > TimeSpan.Zero
            && _entries.TryGetValue((scope, area), out var entry)
            && entry.Expires > time.GetUtcNow())
        {
            value = entry.Value;
            return true;
        }

        value = null!;
        return false;
    }

    // Taken before the database is read, and given back to Set.
    public long Generation(string area) => _generations.GetValueOrDefault(area);

    public void Set(Guid scope, string area, ResolvedSetting value, long generation)
    {
        if (registry.CacheDuration <= TimeSpan.Zero)
        {
            return;
        }

        _entries[(scope, area)] = (value, time.GetUtcNow() + registry.CacheDuration);

        // A write came in between: what was just stored may be the old row, so take it out again.
        if (Generation(area) != generation)
        {
            _entries.TryRemove((scope, area), out _);
        }
    }

    public void Invalidate(string area)
    {
        _generations.AddOrUpdate(area, 1, (_, current) => current + 1);
        foreach (var key in _entries.Keys.Where(key => key.Area == area))
        {
            _entries.TryRemove(key, out _);
        }
    }

    public void InvalidateSite(Guid site)
    {
        foreach (var key in _entries.Keys.Where(key => key.Scope == site))
        {
            _entries.TryRemove(key, out _);
        }
    }
}
