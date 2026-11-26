using System.Text.RegularExpressions;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

// IPluginStore<TPlugin> on cms_plugin_store, keyed by the plugin's registered name, which no member takes. Each call runs on
// a context of its own, so it saves nothing else the caller's scope tracks, and the version in the WHERE of one UPDATE or
// DELETE is what makes a write conditional on every provider.
internal sealed class PluginStore<TPlugin> : IPluginStore<TPlugin>
    where TPlugin : class, IPlugin
{
    private const long FirstVersionBound = 1L << 62;

    private readonly string _plugin;
    private readonly IServiceScopeFactory _scopes;
    private readonly ITransactionProbe _transaction;
    private readonly IDataProvider _provider;
    private readonly TimeProvider _time;

    public PluginStore(PluginRegistry registry, IServiceScopeFactory scopes, ITransactionProbe transaction, IDataProvider provider, TimeProvider time)
    {
        _plugin = registry.Plugins.FirstOrDefault(plugin => plugin.Type == typeof(TPlugin))?.Name
            ?? throw new InvalidOperationException($"{typeof(TPlugin).FullName} is not a plugin of this host, so it has no store; add it with cms.AddPlugin<{typeof(TPlugin).Name}>().");
        _scopes = scopes;
        _transaction = transaction;
        _provider = provider;
        _time = time;
    }

    public async Task<PluginStoreValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        Check(key);
        await using var scope = _scopes.CreateAsyncScope();
        var row = await Rows(scope, key).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new PluginStoreValue(row.Key, row.Value, row.Version, row.UpdatedAt);
    }

    public async Task<PluginStoreValue?> TryAddAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        Check(key, value);
        await using var scope = _scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        // A row added again after a delete must not repeat a version that a writer of the earlier row may hold, and one more
        // per write from below 2^62 never reaches long.MaxValue.
        var row = new PluginStoreRow { Plugin = _plugin, Key = key, Value = value, Version = Random.Shared.NextInt64(1, FirstVersionBound), UpdatedAt = _time.GetUtcNow() };
        context.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (_provider.IsUniqueViolation(exception))
        {
            return null;
        }

        return new PluginStoreValue(key, value, row.Version, row.UpdatedAt);
    }

    public async Task<PluginStoreValue?> TrySetAsync(string key, string value, long version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        Check(key, value);
        var now = _time.GetUtcNow();
        await using var scope = _scopes.CreateAsyncScope();
        var written = await Rows(scope, key)
            .Where(row => row.Version == version)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Value, value).SetProperty(row => row.Version, row => row.Version + 1).SetProperty(row => row.UpdatedAt, now), cancellationToken);
        return written > 0 ? new PluginStoreValue(key, value, version + 1, now) : null;
    }

    public async Task<bool> DeleteAsync(string key, long? version = null, CancellationToken cancellationToken = default)
    {
        Check(key);
        await using var scope = _scopes.CreateAsyncScope();
        var rows = Rows(scope, key);
        if (version is { } expected)
        {
            rows = rows.Where(row => row.Version == expected);
        }

        return await rows.ExecuteDeleteAsync(cancellationToken) > 0;
    }

    private IQueryable<PluginStoreRow> Rows(AsyncServiceScope scope, string key) =>
        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<PluginStoreRow>().Where(row => row.Plugin == _plugin && row.Key == key);

    private void Check(string key, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length > PluginStoreRow.KeyMaxLength || !PluginStoreKeys.Pattern().IsMatch(key))
        {
            throw new ArgumentException($"A key of a plugin's store has 1 to {PluginStoreRow.KeyMaxLength} characters: lower case letters and digits, in groups joined by a single '.', '-', '_' or ':'.", nameof(key));
        }

        if (value is not null && value.Length > PluginStoreRow.ValueMaxLength)
        {
            throw new ArgumentException($"A value of a plugin's store has at most {PluginStoreRow.ValueMaxLength} characters; this one has {value.Length}.", nameof(value));
        }

        // PostgreSQL stores no U+0000 in text, and the other providers would, so no provider takes it.
        if (value is not null && value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A value of a plugin's store holds no U+0000 character.", nameof(value));
        }

        // The statement commits at once, so a rollback of the caller's transaction would not take it back.
        if (_transaction.InTransaction)
        {
            throw new InvalidOperationException("A plugin's store is read and written outside a transaction, because each call commits on its own and a rollback of the transaction would not take it back; call it before the transaction or after it ends.");
        }
    }
}

internal static partial class PluginStoreKeys
{
    [GeneratedRegex(@"^[a-z0-9]+(?:[._:-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
}
