using FluentCMS.Sites;

namespace FluentCMS.Plugins;

/// <summary>
/// Whether a plugin is enabled for a site. A plugin is enabled for every site until it is disabled for one, and core
/// already keeps a disabled plugin's endpoints, hooks, event handlers, outbox handlers and jobs from running there.
/// </summary>
public interface IPluginState
{
    /// <summary>Whether the plugin is enabled for the site.</summary>
    /// <param name="plugin">The name of the plugin.</param>
    /// <param name="site">The site.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True unless the plugin is disabled for the site.</returns>
    ValueTask<bool> IsEnabledAsync(string plugin, SiteId site, CancellationToken cancellationToken = default);
}
