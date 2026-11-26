namespace FluentCMS.Plugins;

/// <summary>A value of a plugin's store, with the version a write compares.</summary>
/// <param name="Key">The key of the value.</param>
/// <param name="Value">The value.</param>
/// <param name="Version">The version, 1 when the value is added and one more at each write.</param>
/// <param name="UpdatedAt">When the value was last written, by the host's clock.</param>
public sealed record PluginStoreValue(string Key, string Value, long Version, DateTimeOffset UpdatedAt);

/// <summary>
/// The values one plugin keeps for the instance, such as where a job stands, in the database that every instance of the host
/// shares. A write is conditional on the version read, so that of two instances that write at once one wins and the other
/// is told it lost. Nothing is cached: a value another instance wrote reads at once. Values are the plugin's alone: no site
/// has them, a transfer package does not carry them, and they stay when the plugin is removed from the host.
/// <para>
/// A key has 1 to 128 characters: lower case letters and digits, in groups joined by a single <c>.</c>, <c>-</c>, <c>_</c>
/// or <c>:</c>, such as <c>media.dimensions.backfill</c>. A value is text of at most 4,000 characters; a plugin that keeps
/// a structure serializes it, as JSON for example. Each call is one statement that commits on its own, so a call inside a
/// transaction that the caller has open is refused: its outcome would not follow a later rollback of the transaction.
/// </para>
/// </summary>
/// <typeparam name="TPlugin">The plugin whose values these are, which the host must register.</typeparam>
public interface IPluginStore<TPlugin>
    where TPlugin : class, IPlugin
{
    /// <summary>Reads a value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value with its version, or <see langword="null"/> when there is none.</returns>
    /// <exception cref="ArgumentException">The key breaks the rule of keys.</exception>
    /// <exception cref="InvalidOperationException">The caller has a transaction open.</exception>
    Task<PluginStoreValue?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Adds a value when the key has none.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The value added, at version 1, or <see langword="null"/> when the key has a value already.</returns>
    /// <exception cref="ArgumentException">The key breaks the rule of keys, or the value is longer than 4,000 characters.</exception>
    /// <exception cref="InvalidOperationException">The caller has a transaction open.</exception>
    Task<PluginStoreValue?> TryAddAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Replaces a value when its version is still the one given.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The new value.</param>
    /// <param name="version">The version the caller read.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The value written, at the next version, or <see langword="null"/> when another write moved the version on or removed
    /// the value.
    /// </returns>
    /// <exception cref="ArgumentException">The key breaks the rule of keys, or the value is longer than 4,000 characters.</exception>
    /// <exception cref="InvalidOperationException">The caller has a transaction open.</exception>
    Task<PluginStoreValue?> TrySetAsync(string key, string value, long version, CancellationToken cancellationToken = default);

    /// <summary>Removes a value, whatever its version or only at the version given.</summary>
    /// <param name="key">The key.</param>
    /// <param name="version">The version the caller read, or <see langword="null"/> to remove the value at any version.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether a value was removed.</returns>
    /// <exception cref="ArgumentException">The key breaks the rule of keys.</exception>
    /// <exception cref="InvalidOperationException">The caller has a transaction open.</exception>
    Task<bool> DeleteAsync(string key, long? version = null, CancellationToken cancellationToken = default);
}
