namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

/// <summary>
/// Simple in-memory cache for configuration values
/// </summary>
internal class InMemoryCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(5);

    public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var entry = new CacheEntry
        {
            Value = value,
            ExpiresAt = DateTime.UtcNow.Add(expiration ?? _defaultExpiration)
        };

        _cache[key] = entry;
    }

    public bool TryGet<T>(string key, out T value) where T : class
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                value = (T)entry.Value;
                return true;
            }

            // Remove expired entry
            _cache.TryRemove(key, out _);
        }

        value = default!;
        return false;
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void Clear()
    {
        _cache.Clear();
    }

    private class CacheEntry
    {
        public required object Value { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
