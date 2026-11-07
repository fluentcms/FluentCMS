using System.Collections.Frozen;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Plugins;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

// The 404 of a plugin's endpoint in a site where the plugin is disabled.
internal sealed class PluginDisabledException(string message) : Exception(message);

// Marks the endpoints of a plugin's groups, so that the plugin list can name them.
internal sealed record PluginEndpointMetadata(string Plugin);

// Which plugins are disabled in which site, and the gate that keeps a disabled plugin's endpoints, hooks, handlers and jobs
// from running there. The rows of cms_plugin_site are read whole and kept for every reader of this instance for CacheDuration;
// a write through PluginAdministration drops them at once, and another instance sees it when its copy expires. A host
// without plugins reads nothing.
internal sealed class PluginState(PluginRegistry registry, IServiceScopeFactory scopes, TimeProvider time) : IPluginState
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static readonly FrozenSet<string> None = FrozenSet<string>.Empty;

    private Snapshot? _snapshot;
    private long _generation;

    public async ValueTask<bool> IsEnabledAsync(string plugin, SiteId site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return !(await DisabledAsync(site.Value, cancellationToken)).Contains(plugin);
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    // The names of the plugins disabled in the site; none for the empty site of the system.
    public async ValueTask<FrozenSet<string>> DisabledAsync(Guid site, CancellationToken cancellationToken)
    {
        if (registry.Plugins.Count == 0 || site == Guid.Empty)
        {
            return None;
        }

        return (await CurrentAsync(cancellationToken)).GetValueOrDefault(site) ?? None;
    }

    public async ValueTask RequireEnabledAsync(string plugin, SiteId site, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(plugin, site, cancellationToken))
        {
            throw new PluginDisabledException($"The plugin '{plugin}' is not enabled for this site.");
        }
    }

    // The hooks or handlers whose plugin is enabled in the site, in their order; those of the host and core always are.
    public async ValueTask<List<T>> EnabledAsync<T>(List<T> items, Func<T, Type> typeOf, Guid site, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var disabled = await DisabledAsync(site, cancellationToken);
        return disabled.Count == 0
            ? items
            : [.. items.Where(item => registry.OwnerOf(typeOf(item)) is not { } plugin || !disabled.Contains(plugin.Name))];
    }

    public List<T> WithoutPlugins<T>(List<T> items, Func<T, Type> typeOf) => [.. items.Where(item => registry.OwnerOf(typeOf(item)) is null)];

    // The plugin of a job type or an outbox handler when it is disabled in the site, else null.
    public ValueTask<string?> DisabledJobAsync(string type, Guid? site, CancellationToken cancellationToken) =>
        DisabledOwnerAsync(registry.Contributions.JobOwner(type), site, cancellationToken);

    public ValueTask<string?> DisabledOutboxHandlerAsync(string name, Guid? site, CancellationToken cancellationToken) =>
        DisabledOwnerAsync(registry.Contributions.OutboxHandlerOwner(name), site, cancellationToken);

    public string? OutboxHandlerOwner(string name) => registry.Contributions.OutboxHandlerOwner(name);

    private async ValueTask<string?> DisabledOwnerAsync(string? plugin, Guid? site, CancellationToken cancellationToken) =>
        plugin is not null && site is { } one && (await DisabledAsync(one, cancellationToken)).Contains(plugin) ? plugin : null;

    private async ValueTask<FrozenDictionary<Guid, FrozenSet<string>>> CurrentAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Read(ref _generation);
        if (Volatile.Read(ref _snapshot) is { } snapshot && snapshot.Generation == generation && snapshot.Expires > time.GetUtcNow())
        {
            return snapshot.Disabled;
        }

        await using var scope = scopes.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<PluginSiteRow>().AsNoTracking()
            .Where(row => !row.Enabled)
            .Select(row => new { row.SiteId, row.Plugin })
            .ToListAsync(cancellationToken);

        var disabled = rows
            .GroupBy(row => row.SiteId)
            .ToFrozenDictionary(group => group.Key, group => group.Select(row => row.Plugin).ToFrozenSet(StringComparer.Ordinal));

        // A write during the read leaves this copy a generation behind, so the next reader reads again.
        Volatile.Write(ref _snapshot, new Snapshot(disabled, generation, time.GetUtcNow() + CacheDuration));
        return disabled;
    }

    private sealed record Snapshot(FrozenDictionary<Guid, FrozenSet<string>> Disabled, long Generation, DateTimeOffset Expires);
}
