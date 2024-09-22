using AutoMapper;
using Microsoft.Extensions.Caching.Memory;

namespace FluentCMS.Providers.CacheProviders;

public class InMemoryCacheProvider(IMemoryCache memoryCache, IMapper mapper) : ICacheProvider
{
    public void Remove(string key)
    {
        memoryCache.Remove(key);
    }

    public void Set<T>(string key, T value)
    {
        var cachedValue = mapper.Map<T>(value);
        memoryCache.Set(key, cachedValue);
    }

    public bool TryGetValue<T>(object key, out T? value)
    {
        if (memoryCache.TryGetValue(key, out object? result))
        {
            if (result == null)
            {
                value = default;
                return true;
            }

            if (result is T item)
            {
                value = mapper.Map<T>(item);
                return true;
            }
        }

        value = default;
        return false;
    }
}
