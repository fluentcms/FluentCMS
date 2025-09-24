using FluentCMS.Providers.Caching.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace FluentCMS.Providers.Caching.InMemory;

/// <summary>
/// In-memory implementation of ICacheProvider using Microsoft.Extensions.Caching.Memory
/// </summary>
public class InMemoryCacheProvider(IMemoryCache memoryCache) : ICacheProvider
{
    private static void CheckKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
    }

    public Task<T> Get<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CheckKey(key);

        var value = memoryCache.Get<T>(key) ??
            throw new KeyNotFoundException($"Key '{key}' not found in cache.");

        return Task.FromResult(value);
    }

    public Task<bool> TryGetValue<T>(string key, out T? value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CheckKey(key);

        var found = memoryCache.TryGetValue(key, out value);
        return Task.FromResult(found);
    }


    public Task Set<T>(string key, T value, DateTimeOffset? absoluteExpiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CheckKey(key);

        var options = new MemoryCacheEntryOptions();
        if (absoluteExpiration.HasValue)
        {
            options.AbsoluteExpiration = absoluteExpiration.Value;
        }

        memoryCache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task Set<T>(string key, T value, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CheckKey(key);

        var options = new MemoryCacheEntryOptions();
        if (slidingExpiration.HasValue)
        {
            options.SlidingExpiration = slidingExpiration.Value;
        }

        memoryCache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task Remove(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CheckKey(key);

        memoryCache.Remove(key);
        return Task.CompletedTask;
    }
}