using Microsoft.Extensions.Caching.Memory;

namespace FluentCMS.Providers.CacheProviders;

public class InMemoryCacheProvider(IMemoryCache memoryCache) : ICacheProvider
{
    // Remove an item from the cache by key
    public void Remove(string key)
    {
        memoryCache.Remove(key);
    }

    public void Set<T>(string key, T value)
    {
        memoryCache.Set(key, value);
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        // Try to get the cached item by key
        if (memoryCache.TryGetValue(key, out T? result))
        {
            // If the cached value is explicitly null
            if (result is null)
            {
                value = default; // Return default value for type T
                return true; // Indicates that a null value was found in cache
            }

            value = result; // Return the cached value
            return true;
        }

        // If the item was not found in the cache
        value = default;
        return false;
    }
}
