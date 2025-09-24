using FluentCMS.Providers.Abstractions;

namespace FluentCMS.Providers.Caching.Abstractions;

/// <summary>
/// Provides a generic interface for cache operations
/// </summary>
public interface ICacheProvider : IProvider
{
    public const string Area = "Caching";

    /// <summary>
    /// Gets a value from cache by key
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The cached value or default if not found</returns>
    Task<T> Get<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to get a value from cache by key
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The cached value if found, otherwise default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the value was found, otherwise false</returns>
    Task<bool> TryGetValue<T>(string key, out T? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value in cache with the specified key
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="absoluteExpiration">Absolute expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task Set<T>(string key, T value, DateTimeOffset? absoluteExpiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value in cache with the specified key and sliding expiration
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="slidingExpiration">Sliding expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task Set<T>(string key, T value, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a value from cache by key
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task Remove(string key, CancellationToken cancellationToken = default);
}