namespace FluentCMS.Providers.CacheProviders;

/// <summary>
/// Interface for a cache provider that supports adding, retrieving, and removing cached items.
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Removes a specific item from the cache by its key.
    /// </summary>
    /// <param name="key">The unique string identifier for the cache entry.</param>
    void Remove(string key);

    /// <summary>
    /// Adds or updates an item in the cache.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The unique string identifier for the cache entry.</param>
    /// <param name="value">The value to cache, of type <typeparamref name="T"/>.</param>
    void Set<T>(string key, T value);

    /// <summary>
    /// Attempts to retrieve a cached item by its key.
    /// </summary>
    /// <typeparam name="T">The type of the cached item.</typeparam>
    /// <param name="key">The unique string identifier for the cache entry.</param>
    /// <param name="value">
    /// When this method returns, contains the cached item of type <typeparamref name="T"/> if found; otherwise, the default value for the type of the <paramref name="value"/> parameter.
    /// </param>
    /// <returns>
    /// <c>true</c> if the cache contains an item with the specified key; otherwise, <c>false</c>.
    /// </returns>
    bool TryGetValue<T>(string key, out T? value);
}
