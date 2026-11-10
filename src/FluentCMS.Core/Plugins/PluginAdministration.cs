using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Plugins;

internal sealed record PluginSiteInfo(Guid SiteId, string Slug, bool Enabled);

internal sealed record PluginEndpointInfo(string Method, string Route);

internal sealed record PluginContributionsInfo(
    IReadOnlyList<string> FieldTypes,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<PluginEndpointInfo> Endpoints,
    IReadOnlyList<string> Jobs,
    IReadOnlyList<string> Tools);

internal sealed record PluginInfo(
    string Name,
    string Version,
    string SdkRange,
    IReadOnlyList<string> DependsOn,
    bool Enabled,
    PluginContributionsInfo Contributions,
    IReadOnlyList<PluginSiteInfo> Sites);

// The plugin list and per-site enablement of the Management API. The list has every plugin's manifest and whether it is enabled
// in the site of the operation, which every admin needs to hide a disabled plugin's UI, and its enablement in each site where the
// caller holds plugins.manage. Enabling and disabling need plugins.manage for the site.
internal sealed class PluginAdministration(
    PluginRegistry registry,
    PluginState state,
    SystemDbContext context,
    ManagementAuthority authority,
    IEventBus bus,
    IOperationContextAccessor accessor,
    IServiceProvider services,
    TimeProvider time,
    ILogger<PluginAdministration> logger)
{
    public async Task<IReadOnlyList<PluginInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var current = accessor.Current?.Site.Value ?? DefaultSite.Id;
        var sites = await context.Set<Site>().AsNoTracking().ToListAsync(cancellationToken);
        List<Site> managed = [];
        foreach (var site in sites.OrderByDescending(item => item.IsDefault).ThenBy(item => item.Slug, StringComparer.Ordinal))
        {
            if (await authority.AllowsAsync(CorePermissions.PluginsManage, site.Id, cancellationToken))
            {
                managed.Add(site);
            }
        }

        var disabled = (await context.Set<PluginSiteRow>().AsNoTracking()
                .Where(row => !row.Enabled)
                .Select(row => new { row.Plugin, row.SiteId })
                .ToListAsync(cancellationToken))
            .Select(row => (row.Plugin, row.SiteId))
            .ToHashSet();

        var providers = services.GetServices<IPermissionProvider>().ToList();
        var endpoints = Endpoints();
        var tools = await ToolsAsync(plugin => !disabled.Contains((plugin, current)), cancellationToken);
        return [.. registry.Plugins.Select(plugin => new PluginInfo(
            plugin.Name,
            plugin.Version,
            plugin.SdkRange,
            plugin.DependsOn,
            !disabled.Contains((plugin.Name, current)),
            new PluginContributionsInfo(
                registry.Contributions.FieldTypesOf(plugin.Name),
                PermissionsOf(plugin, providers),
                [.. endpoints.Where(endpoint => endpoint.Plugin == plugin.Name).Select(endpoint => endpoint.Info)],
                registry.Contributions.JobsOf(plugin.Name),
                tools.GetValueOrDefault(plugin.Name) ?? []),
            [.. managed.Select(site => new PluginSiteInfo(site.Id, site.Slug, !disabled.Contains((plugin.Name, site.Id))))]))];
    }

    // Enabling a plugin that has no row for the site, or setting what the row holds, changes nothing and raises no event.
    public async Task<PluginSiteInfo> SetEnabledAsync(string name, Guid siteId, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        await authority.RequireAsync(CorePermissions.PluginsManage, siteId, cancellationToken);
        var plugin = registry.Plugins.FirstOrDefault(item => item.Name == name)
            ?? throw new ValidationException([new ValidationError("plugin", PluginErrorCodes.PluginNotFound, $"There is no plugin '{name}'.")]);
        var site = await context.Set<Site>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == siteId, cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {siteId}.")]);

        var changed = await WriteAsync(plugin.Name, site.Id, enabled, cancellationToken);
        state.Invalidate();
        if (changed)
        {
            // What happens to a site is that site's.
            var source = (accessor.Current is { } operation ? EventSource.From(operation) : EventSource.FromSystem(time.GetUtcNow())) with { SiteId = site.Id };
            if (enabled)
            {
                PluginLog.Enabled(logger, plugin.Name, site.Id);
                await bus.PublishAsync(new PluginEnabledEvent(plugin.Name, site.Id) { Source = source }, cancellationToken);
            }
            else
            {
                PluginLog.Disabled(logger, plugin.Name, site.Id);
                await bus.PublishAsync(new PluginDisabledEvent(plugin.Name, site.Id) { Source = source }, cancellationToken);
            }
        }

        return new PluginSiteInfo(site.Id, site.Slug, enabled);
    }

    // True when this write changed the state. Each statement is conditional, so of two writers of the same state one changes it.
    private async Task<bool> WriteAsync(string plugin, Guid site, bool enabled, CancellationToken cancellationToken)
    {
        var rows = context.Set<PluginSiteRow>();
        for (var attempt = 0; ; attempt++)
        {
            var updated = await rows
                .Where(row => row.Plugin == plugin && row.SiteId == site && row.Enabled != enabled)
                .ExecuteUpdateAsync(row => row.SetProperty(column => column.Enabled, enabled), cancellationToken);
            if (updated > 0)
            {
                return true;
            }

            if (enabled || await rows.AnyAsync(row => row.Plugin == plugin && row.SiteId == site, cancellationToken))
            {
                return false;
            }

            var row = new PluginSiteRow { Plugin = plugin, SiteId = site, Enabled = false };
            context.Add(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Another writer added the row first, and what it holds now decides, unless the site went in between.
                context.Entry(row).State = EntityState.Detached;
                if (!await context.Set<Site>().AnyAsync(item => item.Id == site, cancellationToken))
                {
                    throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {site}.")]);
                }
            }
        }
    }

    private List<string> PermissionsOf(RegisteredPlugin plugin, List<IPermissionProvider> providers)
    {
        var types = registry.Contributions.PermissionProvidersOf(plugin.Name);
        return [.. providers
            .Where(provider => types.Contains(provider.GetType()))
            .SelectMany(provider => provider.GetPermissions())
            .Select(permission => permission.Name)
            .Distinct(StringComparer.Ordinal)];
    }

    // The MCP tools each plugin offers the caller in the site of the operation, by name, as the tool list of the MCP server
    // gives them: the providers of a plugin that is disabled there are not asked, a name an earlier plugin lists is left
    // out, and so is a provider that fails. The providers decide what the caller may use, as they do for a tool list.
    private async Task<Dictionary<string, List<string>>> ToolsAsync(Func<string, bool> enabled, CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>> tools = new(StringComparer.Ordinal);
        if (accessor.Current is not { } operation)
        {
            return tools;
        }

        var request = new McpListRequest(operation);
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (var registration in registry.Contributions.Tools.Where(registration => enabled(registration.Plugin)))
        {
            IReadOnlyList<McpToolDescriptor> listed;
            try
            {
                listed = await ((IMcpToolProvider)services.GetRequiredService(registration.Provider)).ListToolsAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                PluginLog.ToolsNotListed(logger, exception, registration.Provider.Name, registration.Plugin);
                continue;
            }

            if (!tools.TryGetValue(registration.Plugin, out var ofPlugin))
            {
                tools[registration.Plugin] = ofPlugin = [];
            }

            ofPlugin.AddRange(listed.Select(tool => tool.Name).Where(names.Add));
        }

        return tools;
    }

    // The endpoints the plugins' groups hold once the app is built; none outside a web host.
    private List<(string Plugin, PluginEndpointInfo Info)> Endpoints()
    {
        if (services.GetService<EndpointDataSource>() is not { } source)
        {
            return [];
        }

        return [.. source.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<PluginEndpointMetadata>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => (Plugin: endpoint.Metadata.GetMetadata<PluginEndpointMetadata>()!.Plugin, Info: new PluginEndpointInfo(method, endpoint.RoutePattern.RawText ?? ""))))
            .OrderBy(endpoint => endpoint.Info.Route, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Info.Method, StringComparer.Ordinal)];
    }
}
